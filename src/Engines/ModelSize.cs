using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MultiLauncher;

// 模型占用估算（公式来自 DEC-007，llama.cpp 实测四例误差 0）。
//   KV = ctx × Σ(kv_heads) × (key_length × 每元素字节(k) + value_length × 每元素字节(v))
// Σ(kv_heads) = 所有「有 KV 的层」的 KV head 数之和：
//   元数据给的是标量 -> 只有 1/full_attention_interval 的层有 KV（混合注意力模型，这是 DEC-007 记的坑）；
//   元数据给的是数组 -> 直接取元素之和。
// 合计 ≈ 模型文件 + 附加文件(mmproj/草稿/分词包) + KV + compute buffer（粗估，显示时统一带 ~）。
// 「模型文件」= 配置点名的文件 **加上它的兄弟分片**（见 WithSiblings，2026-10-03 反馈⑤）。
// 只读：仅解析 GGUF 头部元数据，从不加载模型。
internal static class ModelSize
{
    /// 每元素字节：f16=2 / q8_0=1.0625 / q4_0=0.5625（DEC-007 实测标定）
    public static double BytesPerElement(string cacheType) => cacheType.Trim().ToLowerInvariant() switch
    {
        "q8_0" => 1.0625,
        "q4_0" => 0.5625,
        _ => 2.0,      // f16 及未知值
    };

    public static long KvBytes(GgufMeta m, long ctx, string cacheK, string cacheV)
    {
        long heads = m.KvHeadsTotal();
        if (heads <= 0 || m.KeyLength <= 0 || m.ValueLength <= 0 || ctx <= 0) return 0;
        double perElem = m.KeyLength * BytesPerElement(cacheK) + m.ValueLength * BytesPerElement(cacheV);
        return (long)(ctx * heads * perElem);
    }

    // compute buffer：llama.cpp 构图时按需分配，实测区间 104 MiB ~ 2179 MiB（随模型规模/MoE 变化），
    // 只能粗估。这个比例系数**尚未用样本标定**（DEC-007 只要求"大致对"）；改它只影响界面显示的数字，
    // 不影响任何启动参数。
    const double ComputeRatio = 0.08;
    const long ComputeMin = 128L << 20;
    const long ComputeMax = 2179L << 20;

    static long ComputeBuffer(long modelBytes) => Math.Clamp((long)(modelBytes * ComputeRatio), ComputeMin, ComputeMax);

    public static long TotalBytes(long modelBytes, long extraBytes, long kvBytes)
        => modelBytes + extraBytes + kvBytes + ComputeBuffer(modelBytes);

    /// 显示用：「22.6G」/「768M」（1G = 1024^3）
    public static string Format(long bytes)
        => bytes >= 1L << 30 ? (bytes / (double)(1L << 30)).ToString("0.0") + "G"
                             : Math.Max(1, bytes >> 20) + "M";

    /// 目录（递归）或文件的字节数；不存在返回 0
    public static long PathBytes(string path)
    {
        try
        {
            if (File.Exists(path)) return new FileInfo(path).Length;
            if (!Directory.Exists(path)) return 0;
            long sum = 0;
            foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try { sum += new FileInfo(f).Length; } catch { /* 单个文件读不到就跳过 */ }
            }
            return sum;
        }
        catch { return 0; }
    }

    /// 分片补齐（2026-10-03 用户反馈⑤）：形如 `xxx-00001-of-00002.gguf` 的文件只是「这一套模型的一块」。
    /// 配置常常只点名第一块——本机 swift 那份 `--native` 与 `--ple-gguf` 都指 00001，而 00002 里装着 907 个张量
    /// （第一块只有 317 个），于是估算漏了 33.7 GiB，两个模型显示成 44.6 / 83.8（实际只差 10%）。
    /// 这里按**同目录 + 同前缀 + 同扩展名**把兄弟块一起交出去；调用方按完整路径去重，
    /// 所以「第一块 + 单独的 PLE 文件」那种（本机 iq3_s 的 00002 只有 1 个张量，本来就被 --ple-gguf 点名）不会重复计数。
    public static IEnumerable<string> WithSiblings(string file)
    {
        yield return file;
        var name = Path.GetFileName(file);
        var head = Regex.Match(name, @"^(?<p>.*-)\d{4,5}-of-\d{4,5}(?<e>\.[A-Za-z0-9]+)$", RegexOptions.IgnoreCase);
        if (!head.Success) yield break;
        var dir = Path.GetDirectoryName(file);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) yield break;
        var pattern = @"^" + Regex.Escape(head.Groups["p"].Value) + @"\d{4,5}-of-\d{4,5}" +
                      Regex.Escape(head.Groups["e"].Value) + "$";
        string[] peers;
        try { peers = Directory.GetFiles(dir); } catch { yield break; }
        foreach (var f in peers)
            if (Regex.IsMatch(Path.GetFileName(f), pattern, RegexOptions.IgnoreCase)) yield return f;
    }

    /// 一套模型（可能分片）的文件字节数；按完整路径去重
    public static long ModelBytes(IEnumerable<string> files)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long sum = 0;
        foreach (var f in files)
            foreach (var s in WithSiblings(Path.GetFullPath(f)))
                if (seen.Add(s)) sum += PathBytes(s);
        return sum;
    }

    // ---------------- 两个后端的「从配置算大小」 ----------------

    /// Strata 侧：模型文件（`--native` 与 `--ple-gguf`，**按完整路径去重 + 分片补齐**——本机实测 swift 那份两者是同一个文件，
    /// 而它还有第二块装着 907 个张量；iq3_s 那份的第二块是单独的 PLE 文件，去重后不重复计）
    /// + 附加（`--pack` 目录、`--mtp` 目录）+ KV。
    /// KV 的每元素字节按 config 的 `--kv` 映射。注意：**Strata 侧没有实测量样本**（DEC-007 的四个样本都来自
    /// llama.cpp），这一段是推断，所以显示值统一带 `~`。
    public static string EstimateStrata(string configPath)
    {
        try
        {
            var node = JsonNode.Parse(File.ReadAllText(configPath)) as JsonObject;
            var args = (node?["args"] as JsonArray)?.Select(x => x?.GetValue<string>() ?? "").ToList() ?? new List<string>();
            string? Arg(string flag)
            {
                var i = args.IndexOf(flag);
                return i >= 0 && i + 1 < args.Count ? args[i + 1] : null;
            }

            long extra = 0;
            var named = new List<string>();
            foreach (var flag in new[] { "--native", "--ple-gguf" })
                if (Arg(flag) is { } p) named.Add(p);
            long model = ModelBytes(named);   // 分片补齐见 WithSiblings
            foreach (var flag in new[] { "--pack", "--mtp" })
                if (Arg(flag) is { } p) extra += PathBytes(Path.GetFullPath(p));
            if (model == 0) return "";

            var native = Arg("--native");
            var meta = native is not null ? GgufMeta.Read(native) : null;
            long ctx = long.TryParse(Arg("--max-context"), out var c) ? c : 0;
            long kv = 0;
            if (meta is not null)
            {
                KvTypesForStrata(Arg("--kv") ?? "int8", out var kt, out var vt);
                kv = KvBytes(meta, ctx, kt, vt);
            }
            return "~" + Format(TotalBytes(model, extra, kv));
        }
        catch { return ""; }
    }

    // Strata 的 --kv 取值（int8 / q4_0 / k8v4）-> 复用同一套每元素字节
    static void KvTypesForStrata(string kv, out string k, out string v)
    {
        switch (kv.Trim().ToLowerInvariant())
        {
            case "q4_0": k = "q4_0"; v = "q4_0"; break;
            case "k8v4": k = "q8_0"; v = "q4_0"; break;
            default: k = "q8_0"; v = "q8_0"; break;   // int8：8 位 + 每块缩放，按 q8_0 的实测值算
        }
    }

    /// llama.cpp 侧：model 的 gguf + mmproj + KV（ctx-size 与 cache-type-k/v 直接来自 ini 段）
    public static string EstimateLlama(LlamaModelEntry m)
    {
        try
        {
            if (m.GgufPath.Length == 0) return "";
            long model = ModelBytes(new[] { m.GgufPath });   // 同样补齐兄弟分片（ini 里通常只写第一块）
            if (model == 0) return "";
            long extra = m.MmprojPath.Length > 0 ? PathBytes(m.MmprojPath) : 0;
            var meta = GgufMeta.Read(m.GgufPath);
            long ctx = long.TryParse(m.CtxSize, out var c) ? c : 0;
            long kv = meta is not null ? KvBytes(meta, ctx, m.CacheTypeK, m.CacheTypeV) : 0;
            return "~" + Format(TotalBytes(model, extra, kv));
        }
        catch { return ""; }
    }
}

// GGUF 头部元数据：只取算 KV 需要的几项。只顺序扫头部（元数据在张量数据之前），不碰张量。
internal sealed class GgufMeta
{
    public string Architecture = "";
    public int BlockCount;
    public int[] KvHeads = Array.Empty<int>();   // 每层 KV head 数（标量时会按层数展开）
    public int KeyLength;
    public int ValueLength;
    public int FullAttentionInterval;

    /// 上一次 Read 失败的原因（自检用；成功时为空）
    public static string LastError = "";
    static string LastKey = "";

    /// 所有有 KV 的层的 KV head 数之和
    public long KvHeadsTotal()
    {
        long sum = 0;
        foreach (var h in ExpandHeads()) sum += h;
        return sum;
    }

    public int[] ExpandHeads()
    {
        if (KvHeads.Length > 1) return KvHeads;                 // 已按层给全（混合注意力模型常见）
        int scalar = KvHeads.Length == 1 ? KvHeads[0] : 0;
        if (scalar <= 0 || BlockCount <= 0) return Array.Empty<int>();
        int layers = FullAttentionInterval > 0 ? BlockCount / FullAttentionInterval : BlockCount;
        if (layers < 1) layers = 1;
        var a = new int[layers];
        Array.Fill(a, scalar);
        return a;
    }

    public static GgufMeta? Read(string path)
    {
        LastError = "";
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var b = new Reader(fs);
            var magic = b.Bytes(4);
            if (magic is null || magic[0] != 'G' || magic[1] != 'G' || magic[2] != 'U' || magic[3] != 'F') return null;
            uint ver = b.U32();
            if (ver is < 2 or > 3) return null;
            _ = b.U64();                       // tensor_count（用不到）
            ulong kvCount = b.U64();
            if (kvCount > 100_000) return null;

            var meta = new GgufMeta();
            var heads = new List<int>();
            for (ulong i = 0; i < kvCount; i++)
            {
                var key = b.Str();
                if (key is null) return null;
                uint type = b.U32();
                LastKey = i + " type=" + type + " key=" + key;
                if (key == "general.architecture") meta.Architecture = b.Str() ?? "";
                else if (key.EndsWith(".block_count", StringComparison.Ordinal)) meta.BlockCount = (int)b.Num(type);
                else if (key.EndsWith(".attention.head_count_kv", StringComparison.Ordinal)) b.ReadInts(type, heads);
                else if (key.EndsWith(".attention.key_length", StringComparison.Ordinal)) meta.KeyLength = (int)b.Num(type);
                else if (key.EndsWith(".attention.value_length", StringComparison.Ordinal)) meta.ValueLength = (int)b.Num(type);
                else if (key.EndsWith(".full_attention_interval", StringComparison.Ordinal)) meta.FullAttentionInterval = (int)b.Num(type);
                else b.Skip(type);
            }
            meta.KvHeads = heads.ToArray();
            return meta.BlockCount > 0 ? meta : null;
        }
        catch (Exception ex) { LastError = ex.GetType().Name + ": " + ex.Message + "  @KV[" + LastKey + "]"; return null; }
    }

    // GGUF 的二进制读法：小端、小段读取。Skip 走 Seek（FileStream 上是常数时间）。
    sealed class Reader(Stream s)
    {
        readonly Stream _s = s;

        public byte[]? Bytes(int n)
        {
            var a = new byte[n];
            int got = 0;
            while (got < n)
            {
                int r = _s.Read(a, got, n - got);
                if (r <= 0) return null;
                got += r;
            }
            return a;
        }

        byte[] Need(int n) => Bytes(n) ?? throw new EndOfStreamException();

        public uint U32() => BitConverter.ToUInt32(Need(4));
        public ulong U64() => BitConverter.ToUInt64(Need(8));

        public string? Str()
        {
            ulong len = U64();
            if (len > 1 << 24) return null;          // 单条字符串不可能这么大，视为损坏
            var bytes = Bytes((int)len);
            return bytes is null ? null : Encoding.UTF8.GetString(bytes);
        }

        void Seek(long n) => _s.Seek(n, SeekOrigin.Current);

        public long Num(uint t) => t switch
        {
            0 => Need(1)[0],
            1 => (sbyte)Need(1)[0],
            2 => BitConverter.ToUInt16(Need(2)),
            3 => BitConverter.ToInt16(Need(2)),
            4 => BitConverter.ToUInt32(Need(4)),
            5 => BitConverter.ToInt32(Need(4)),
            6 => (long)BitConverter.ToSingle(Need(4)),
            7 => Need(1)[0],
            10 => (long)BitConverter.ToUInt64(Need(8)),
            11 => BitConverter.ToInt64(Need(8)),
            12 => (long)BitConverter.ToDouble(Need(8)),
            _ => SkipZero(t),
        };

        long SkipZero(uint t) { Skip(t); return 0; }

        public void ReadInts(uint t, List<int> into)
        {
            if (t == 9)                              // ARRAY
            {
                uint et = U32();
                ulong n = U64();
                for (ulong i = 0; i < n; i++) into.Add((int)Num(et));
            }
            else into.Add((int)Num(t));
        }

        public void Skip(uint t)
        {
            switch (t)
            {
                case 0 or 1 or 7: Seek(1); break;
                case 2 or 3: Seek(2); break;
                case 4 or 5 or 6: Seek(4); break;
                case 10 or 11 or 12: Seek(8); break;
                case 8:                                  // STRING
                    {
                        ulong len = U64();
                        // 注意：必须写 1L << 32。写成 1 << 32 时 1 是 int、移位数被掩成 0，
                        // 结果等于 1，任何正常字符串都会被误判成"太长"。
                        if (len > 1L << 32) throw new InvalidDataException("GGUF string too long");
                        Seek((long)len);
                        break;
                    }
                case 9:                                  // ARRAY
                    {
                        uint et = U32();
                        ulong n = U64();
                        for (ulong i = 0; i < n; i++) Skip(et);
                        break;
                    }
                default: throw new InvalidDataException("GGUF value type " + t);
            }
        }
    }
}