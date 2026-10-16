using System.Diagnostics;
using System.Text;

namespace MultiLauncher;

// 取远端内容：优先 .NET 的 HttpClient（走 schannel）。
// 本机实测：在受限环境下 schannel 会拿不到凭证（SEC_E_NO_CREDENTIALS，curl 同样报 0x8009030e），
// 而项目 .venv 里的 Python 自带 OpenSSL + 根证书、同一台机器上能正常访问 GitHub，
// 因此 HttpClient 失败时退回 Python 下载，保证「检查更新」在网络正常时不会因 TLS 后端而失效。
internal static class Net
{
    static readonly HttpClient Http = NewClient();

    static HttpClient NewClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(180) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("MultiLauncher");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    // 下载 url 到 dest；成功返回 true。失败时 error 里带两条后端各自的失败原因。
    // onProgress(已下字节, 总字节)：总长拿不到时传 0（调用方按"不确定"画）。DEC-054 给更新进度条用。
    // cancel：DEC-055 的「取消」——每块搬运前问一次；取消后**不再**退到 Python 重试（那是用户主动停的，不是网络失败）。
    public static bool Download(string url, string dest, string venvPython, out string error,
                                Action<long, long>? onProgress = null, Func<bool>? cancel = null)
    {
        error = "";
        bool cancelled = false;
        try
        {
            using var resp = Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? 0;
            using var src = resp.Content.ReadAsStream();
            using var file = File.Create(dest);
            Pump(src, file, total, onProgress, cancel, ref cancelled);
            return true;
        }
        catch (OperationCanceledException) { cancelled = true; }
        catch (Exception ex)
        {
            error = "HttpClient: " + ex.Message;
        }
        if (cancelled) { error = "已取消"; TryDelete(dest); return false; }

        if (File.Exists(venvPython))
        {
            try
            {
                var script = Path.Combine(Path.GetDirectoryName(dest) ?? Path.GetTempPath(), "strata-launcher-net.py");
                File.WriteAllText(script, PythonScript, new UTF8Encoding(false));
                var psi = new ProcessStartInfo(venvPython)
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                };
                psi.ArgumentList.Add(script);
                psi.ArgumentList.Add(url);
                psi.ArgumentList.Add(dest);
                using var p = Process.Start(psi)!;
                // stderr 要**并发**抽：串行等它会和 stdout 互相把管道卡住（子进程写满 stderr 缓冲区就停在
                // 下一行 print 上，父进程又停在 ReadLine 上 → 更新流程永久挂住，_updating 再没人清）。
                var errTask = p.StandardError.ReadToEndAsync();
                var rest = new StringBuilder();
                string? line;
                while ((line = p.StandardOutput.ReadLine()) is not null)
                {
                    // Python 侧每 0.5 秒报一行 "PROGRESS <done> <total>"（total 未知时 0）
                    if (line.StartsWith("PROGRESS ", StringComparison.Ordinal))
                    {
                        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length == 3 && long.TryParse(parts[1], out var done) && long.TryParse(parts[2], out var tot))
                            Report(onProgress, done, tot);
                        if (cancel is not null && cancel())
                        {
                            // 用户取消：杀掉 Python 子进程、删掉半截文件，别把它当「网络失败」再报一遍
                            try { p.Kill(true); } catch { /* 已经退了 */ }
                            cancelled = true;
                            break;
                        }
                        continue;
                    }
                    rest.Append(line).AppendLine();
                }
                var stdout = rest.ToString();
                string stderr;
                try { stderr = errTask.Wait(3000) ? errTask.Result : "（stderr 读取超时）"; }
                catch { stderr = ""; }
                p.WaitForExit();
                // DEC-055 的口径「取消不当成网络失败、也不再退到另一条后端重试」以前只在 HttpClient 分支落了，
                // Python 兜底这条漏了：取消既不报「已取消」、也不删半截包，成败只由退出码决定（最坏会被判成下载成功）。
                if (cancelled) { error = "已取消"; TryDelete(dest); return false; }
                if (p.ExitCode == 0 && File.Exists(dest) && new FileInfo(dest).Length > 0)
                    return true;
                error += " | python: " + (stderr.Trim().Length > 0 ? stderr.Trim() : stdout.Trim());
            }
            catch (Exception ex)
            {
                error += " | python: " + ex.Message;
            }
        }
        return false;
    }


    static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* 半截文件删不掉也不影响结论 */ }
    }

    // ---------------- 下载后的校验（DEC-067）----------------
    // 以前只验到"下完了、非空、解压时 CRC 对得上"，也就是**下坏了过不了解压**，但**下完整了却不是那个包**
    // （URL 指错、门户页伪装 200、CDN 串包）没人管。GitHub 的 release 资产现在带 digest 与 size，
    // 所以二进制包这条路可以做真校验；源码 zipball 没有官方摘要，改由调用方验包内版本号（见 MainForm.Update）。
    // 不符就删掉半截包并说清"什么都没改"，别留一个下次会被当成"已经下好了"的文件。

    internal static string Sha256File(string path)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
    }

    // digest 字面有两种写法：GitHub 给的是 "sha256:<hex>"，也接受裸 hex；大小写不敏感。
    // 空 digest = 上游没给摘要（不是校验失败），返回 true 让调用方按"没这条校验"继续。
    // 前缀不是 sha256 的一律判不符：我们只算了 sha256，拿别的算法的字面来比是假通过（自检抓到过 fail-open）。
    internal static bool DigestMatches(string? digest, string actualHex)
    {
        var want = (digest ?? "").Trim();
        if (want.Length == 0) return true;
        int colon = want.IndexOf(':');
        if (colon >= 0)
        {
            if (!string.Equals(want[..colon].Trim(), "sha256", StringComparison.OrdinalIgnoreCase)) return false;
            want = want[(colon + 1)..];
        }
        return string.Equals(want.Trim(), actualHex.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// 校验下载物：expectSize>0 比字节数，expectSha256 非空比摘要。任一条不符即失败并删文件。
    /// error 里带上"探到的 / 实际算出的"两个值，日志里能直接判是包错了还是路径错了。
    internal static bool VerifyDownload(string path, long expectSize, string? expectSha256, out string error)
    {
        error = "";
        try
        {
            var actual = new FileInfo(path).Length;
            if (expectSize > 0 && actual != expectSize)
            {
                error = $"体积不符：探到 {expectSize} 字节，实际 {actual} 字节";
                TryDelete(path);
                return false;
            }
            if (!string.IsNullOrWhiteSpace(expectSha256))
            {
                var hex = Sha256File(path);
                if (!DigestMatches(expectSha256, hex))
                {
                    error = $"sha256 不符：探到 {expectSha256}，实际 {hex}";
                    TryDelete(path);
                    return false;
                }
            }
            return true;
        }
        catch (Exception ex) { error = "校验读不到文件：" + ex.Message; return false; }
    }

    // 分块搬运并按 ~2% 或 200ms 一次报进度（回调可能来自后台线程，调用方自己 marshal）
    static void Pump(Stream src, Stream file, long total, Action<long, long>? onProgress, Func<bool>? cancel, ref bool cancelled)
    {
        var buf = new byte[1 << 16];
        long done = 0;
        DateTime last = DateTime.MinValue;
        int n;
        while ((n = src.Read(buf, 0, buf.Length)) > 0)
        {
            if (cancel is not null && cancel()) { cancelled = true; throw new OperationCanceledException(); }
            file.Write(buf, 0, n);
            done += n;
            var now = DateTime.UtcNow;
            bool byPct = total > 0 && done * 100 / total != (done - n) * 100 / total;
            if (byPct || (now - last).TotalMilliseconds >= 200)
            {
                last = now;
                Report(onProgress, done, total);
            }
        }
        Report(onProgress, done, total);
    }

    static void Report(Action<long, long>? onProgress, long done, long total)
    {
        if (onProgress is null) return;
        try { onProgress(done, total); } catch { /* 进度只是显示用的，不能把下载带崩 */ }
    }

    const string PythonScript = @"import sys, time, urllib.request
url, dest = sys.argv[1], sys.argv[2]
req = urllib.request.Request(url, headers={'User-Agent': 'MultiLauncher'})
with urllib.request.urlopen(req, timeout=180) as r, open(dest, 'wb') as f:
    total = int(r.headers.get('Content-Length') or 0)
    done, last = 0, 0.0
    while True:
        chunk = r.read(1 << 16)
        if not chunk:
            break
        f.write(chunk)
        done += len(chunk)
        now = time.time()
        if now - last >= 0.5:
            last = now
            print('PROGRESS', done, total, flush=True)
print('PROGRESS', done, total, flush=True)
";
}
