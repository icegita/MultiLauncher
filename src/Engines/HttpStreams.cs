using System.Net.Sockets;
using System.Text;

namespace MultiLauncher;

// 网关用的最小 HTTP 字节读写。
// 关键点：TCP 一个包里可能同时带着头与体，所以「读头」必须留缓冲——NetReader 把多读到的字节存着，
// 后面的读体/读响应从缓冲里接着取，不会丢字节（这是流式转发不掉数据的前提）。
internal sealed class NetReader
{
    readonly NetworkStream _s;
    readonly byte[] _buf = new byte[16 * 1024];
    int _off, _len;

    public NetReader(NetworkStream s) => _s = s;

    public async Task<int> ReadAsync(Memory<byte> dst, CancellationToken ct)
    {
        if (_len == 0)
        {
            int n = await _s.ReadAsync(_buf, ct);
            if (n <= 0) return 0;
            _off = 0;
            _len = n;
        }
        int take = Math.Min(_len, dst.Length);
        _buf.AsSpan(_off, take).CopyTo(dst.Span);
        _off += take;
        _len -= take;
        return take;
    }

    public Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct) => _s.WriteAsync(data, ct).AsTask();
    public Task FlushAsync(CancellationToken ct) => _s.FlushAsync(ct);

    // 读到空行（CRLFCRLF）为止，返回「头 + 空行」的原始字节；连接先断了返回 null。
    // 多读到的字节留在缓冲里，交给后面的读体。
    public async Task<byte[]?> ReadHeadAsync(CancellationToken ct)
    {
        var head = new List<byte>(512);
        var one = new byte[1];
        while (head.Count < HttpStreams.MaxHeadBytes)
        {
            int n = await ReadAsync(one, ct);
            if (n <= 0) return null;
            head.Add(one[0]);
            if (head.Count >= 4 && head[^4] == (byte)'\r' && head[^3] == (byte)'\n' && head[^2] == (byte)'\r' && head[^1] == (byte)'\n')
                return head.ToArray();
        }
        return null;
    }

    // 读一行（含结尾 CRLF，原样返回），最长 8KB
    public async Task<byte[]?> ReadLineAsync(CancellationToken ct)
    {
        var line = new List<byte>(64);
        var one = new byte[1];
        while (line.Count < 8192)
        {
            int n = await ReadAsync(one, ct);
            if (n <= 0) return null;
            line.Add(one[0]);
            if (line.Count >= 2 && line[^2] == (byte)'\r' && line[^1] == (byte)'\n') return line.ToArray();
        }
        return null;
    }

    public async Task<byte[]> ReadExactlyAsync(int count, CancellationToken ct)
    {
        var buf = new byte[count];
        int off = 0;
        while (off < count)
        {
            int n = await ReadAsync(buf.AsMemory(off, count - off), ct);
            if (n <= 0) throw new IOException("连接提前结束，数据不完整");
            off += n;
        }
        return buf;
    }

    public async Task CopyToEndAsync(NetReader dst, CancellationToken ct)
    {
        var buf = new byte[64 * 1024];
        while (true)
        {
            int n = await ReadAsync(buf, ct);
            if (n <= 0) break;
            await dst.WriteAsync(buf.AsMemory(0, n), ct);
        }
        await dst.FlushAsync(ct);
    }
}

// 最小 HTTP 头解析与三种收尾方式（Content-Length / 分块 / 读到连接关闭）的搬运。
internal static class HttpStreams
{
    public const int MaxHeadBytes = 64 * 1024;
    public const long MaxBodyBytes = 256L * 1024 * 1024;

    public static string[] HeadLines(byte[] headBytes) => Encoding.Latin1.GetString(headBytes).Split("\r\n");

    public static List<KeyValuePair<string, string>> ParseHeaders(string[] lines)
    {
        var headers = new List<KeyValuePair<string, string>>();
        for (int i = 1; i < lines.Length; i++)
        {
            if (lines[i].Length == 0) break;
            var c = lines[i].IndexOf(':');
            if (c <= 0) continue;
            headers.Add(new(lines[i][..c].Trim(), lines[i][(c + 1)..].Trim()));
        }
        return headers;
    }

    public static string? Header(List<KeyValuePair<string, string>> headers, string name)
        => headers.FirstOrDefault(h => h.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;

    public static bool HasChunked(List<KeyValuePair<string, string>> headers)
        => (Header(headers, "Transfer-Encoding") ?? "").Contains("chunked", StringComparison.OrdinalIgnoreCase);

    // 请求体：Content-Length 或分块（分块带着分块头原样读回来，转发时也不用重写）
    public static async Task<byte[]> ReadBodyAsync(NetReader s, List<KeyValuePair<string, string>> headers, CancellationToken ct)
    {
        if (HasChunked(headers)) return await ReadChunkedAsync(s, ct);
        if (long.TryParse(Header(headers, "Content-Length"), out var len) && len > 0 && len <= MaxBodyBytes)
            return await s.ReadExactlyAsync((int)len, ct);
        return Array.Empty<byte>();
    }

    public static async Task CopyExactlyAsync(NetReader from, NetReader to, long count, CancellationToken ct)
    {
        var buf = new byte[64 * 1024];
        long left = count;
        while (left > 0)
        {
            int n = await from.ReadAsync(buf.AsMemory(0, (int)Math.Min(buf.Length, left)), ct);
            // DEC-061：上游在声明的长度之前断开时不再静默截断——客户端会收到半截 JSON，而壳这边一行日志都没有。
            // 抛出去由网关的转发 catch 记一行「网关转发失败」。
            if (n <= 0) throw new IOException($"响应在声明的长度之前断开（还差 {left} 字节）");
            await to.WriteAsync(buf.AsMemory(0, n), ct);
            left -= n;
        }
        await to.FlushAsync(ct);
    }

    // 分块体原样搬运：块大小行 + 数据 + CRLF，直到 0 块（之后是 trailer，读到空行结束）
    public static async Task CopyChunkedAsync(NetReader from, NetReader to, CancellationToken ct)
    {
        while (true)
        {
            var sizeLine = await from.ReadLineAsync(ct);
            if (sizeLine is null) return;
            await to.WriteAsync(sizeLine, ct);
            if (ChunkSize(sizeLine) is not { } size) return;
            if (size == 0) { await CopyTrailersAsync(from, to, ct); return; }
            await CopyExactlyAsync(from, to, size + 2L, ct);   // 数据 + 结尾 CRLF
        }
    }

    static async Task CopyTrailersAsync(NetReader from, NetReader to, CancellationToken ct)
    {
        while (true)
        {
            var line = await from.ReadLineAsync(ct);
            if (line is null) return;
            await to.WriteAsync(line, ct);
            if (line.Length <= 2) { await to.FlushAsync(ct); return; }
        }
    }

    static async Task<byte[]> ReadChunkedAsync(NetReader s, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        while (true)
        {
            var sizeLine = await s.ReadLineAsync(ct);
            if (sizeLine is null) break;
            ms.Write(sizeLine);
            if (ChunkSize(sizeLine) is not { } size) break;
            if (size == 0)
            {
                while (true)
                {
                    var trailer = await s.ReadLineAsync(ct);
                    if (trailer is null) break;
                    ms.Write(trailer);
                    if (trailer.Length <= 2) break;
                }
                break;
            }
            ms.Write(await s.ReadExactlyAsync(size + 2, ct));
        }
        return ms.ToArray();
    }

    static int? ChunkSize(byte[] sizeLine)
    {
        var hex = Encoding.Latin1.GetString(sizeLine).Split(';')[0].Trim();
        return int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var size) && size >= 0 ? size : null;
    }
}
