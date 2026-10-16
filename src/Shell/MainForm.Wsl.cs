using System.Diagnostics;
using System.Drawing;
using System.Text;

namespace MultiLauncher;

// WSL 辅助命令（旧壳 MainForm.Wsl.cs 那一套：只取 stdout、stderr 排空防死锁；2026-10-15 从 MainForm.Ninfer.cs 整段搬出，一行未改）
public partial class MainForm
{
    // ---------------- WSL 辅助命令（旧壳 MainForm.Wsl.cs 那一套：只取 stdout、stderr 排空防死锁） ----------------

    /// <summary>引号只在含空格时加（旧壳口径；`-d Ubuntu-24.04` 这种就让它裸着）。</summary>
    internal static string WslQuote(string s) =>
        s.Contains(' ') ? "\"" + s.Replace("\"", "\\\"") + "\"" : s;

    /// <summary>跑一条要给 VM 里执行的**脚本**（不是单条命令），从 stdin 喂给 <c>sh -s</c>。
    /// 为什么要 stdin：脚本里有 <c>$( )</c>、<c>|</c>、引号与空格，走 Windows 那侧的命令行会被 wsl.exe
    /// 再解析一遍，引号怎么转都可能有坑；从标准输入进去就一个字都不动。<c>bin</c> 用单引号嵌进脚本正文，
    /// 不当位置参数（<c>sh -s</c> 的 <c>$0</c>/<c>$1</c> 归法两边实现不一致，别赌）。</summary>
    internal static (int code, string output) RunWslScript(string distro, string script, int timeoutMs = 90_000)
        => RunWslScriptCore(distro, script, timeoutMs, login: false);

    /// <summary>更新链（DEC-106）走这一口：**登录壳** <c>bash -l -s</c>。旧壳那条链就是 `bash -lc` —— 
    /// cmake / CUDA 那些工具靠 profile 里的 PATH，非登录壳会差环境；脚本照旧从 stdin 喂（免引号）。</summary>
    internal static (int code, string output) RunWslChain(string distro, string script, int timeoutMs)
        => RunWslScriptCore(distro, script, timeoutMs, login: true);

    /// <summary>自检/出图的证人位：真把 wsl.exe 拉起来过几次（DEC-106 的干跑用例断言它**零增长** —— 
    /// "测试期间一个字都不进 VM"这条判据以前只挡在探针那一个调用点上，这条链是第二个可能摸 VM 的口）。</summary>
    internal static int RunWslScriptCountForTest;

    static (int code, string output) RunWslScriptCore(string distro, string script, int timeoutMs, bool login)
    {
        try
        {
            var psi = new ProcessStartInfo("wsl.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            psi.ArgumentList.Add("-d");
            psi.ArgumentList.Add(distro);
            if (login)
            {
                psi.ArgumentList.Add("bash");
                psi.ArgumentList.Add("-l");
                psi.ArgumentList.Add("-s");
            }
            else
            {
                psi.ArgumentList.Add("sh");
                psi.ArgumentList.Add("-s");
            }
            RunWslScriptCountForTest++;
            using var p = Process.Start(psi);
            if (p is null) return (-1, "wsl.exe 没能起来");
            try { p.StandardInput.Write(script); p.StandardInput.Close(); }
            catch (Exception ex) { try { p.Kill(true); } catch { /* 已经退了 */ } return (-1, "送不进脚本：" + ex.Message); }
            var outT = p.StandardOutput.ReadToEndAsync();
            var errT = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs))
            {
                try { p.Kill(true); } catch { /* 已经退了 */ }
                p.WaitForExit(3000);
                return (-1, "VM 没在 " + (timeoutMs / 1000) + " 秒内应答（发行版没装？还是 VM 起不来？）");
            }
            var text = outT.Wait(3000) ? outT.Result : "";
            try { errT.GetAwaiter().GetResult(); } catch { /* 只要排空 */ }
            return (p.ExitCode, CleanWslOutput(text));
        }
        catch (Exception ex) { return (-1, ex.Message); }
    }

    /// <summary>单引号包裹并转义，给 `bash -lc` 用。</summary>
    internal static string BashQuote(string s) => "'" + s.Replace("'", "'\\''") + "'";

    /// <summary>跑一条 wsl.exe 辅助命令（find / pgrep / pkill 这一类），拿退出码 + stdout。
    /// **不**并进 stderr：WSL 的 UTF-16 横幅与 motd 都在那一条流里（旧壳审计 3.6 的结论）；
    /// 两条流都要持续排空，否则管道写满会把子进程卡死。需要看错误的命令在脚本里自己写 `2&gt;&amp;1`。</summary>
    internal static (int code, string output) RunWslCapture(string args, int timeoutMs = 30_000)   // DEC-136（C-11）：默认不再无限等（VM 挂住时线程与 wsl.exe 会一直攒着）
    {
        try
        {
            var psi = new ProcessStartInfo("wsl.exe", args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            using var p = Process.Start(psi)!;
            var outT = p.StandardOutput.ReadToEndAsync();
            var errT = p.StandardError.ReadToEndAsync();
            if (timeoutMs < 0) p.WaitForExit();
            else if (!p.WaitForExit(timeoutMs))
            {
                try { p.Kill(true); } catch { /* 已经退了 */ }
                p.WaitForExit(3000);
            }
            var text = outT.Wait(3000) ? outT.Result : "";
            try { errT.GetAwaiter().GetResult(); } catch { /* 只要排空 */ }
            return (p.ExitCode, CleanWslOutput(text));
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }

    /// <summary>WSL 输出的两处噪声：**含 NUL 的行整行丢**（wsl.exe 的 UTF-16 横幅乱码，旧壳同一条判据）
    /// 与空行。没别的规则了 —— 别加"看着像噪声就丢"的猜测，那会把引擎的真实输出也吃掉。</summary>
    internal static string CleanWslOutput(string? text) =>
        string.Join("\n", (text ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries)
                          .Where(l => l.IndexOf('\0') < 0)
                          .Select(l => l.TrimEnd('\r'))
                          .Where(l => l.Trim().Length > 0));

    // ============================================================================
    // S3/S4：起停链路、护栏、显存互斥、按需唤醒与取数
    // ============================================================================
    // 与另两模块对位：拉起 = 一件一进程（没有 llama 的 router 多模型，也没有 Strata 的 python 服务层）；
    // 「加载」= 把这一件起起来，「卸载」= 把进程停掉还显存 —— 本族只有这两态，**没有"重载"**（DEC-087 第③条）。
    // 就绪判据 = 引擎自己打的 marker（实测形 `listening on http://0.0.0.0:8068 | model <对外名> | auth disabled`）：
    // **IP 与模型名都不能进匹配串**（§4 实测表），只认 `listening on http://` + 那个端口。
    // 停止成功的唯一判据 = 进程退出 + 端口不再 LISTENING（本机那代被停时**不打** `server stopped`）。

    /// <summary>marker 判据（纯函数，自检喂真行与假行）。port 给 0 = 只认前半句（用于"这条是 marker"的探测）。</summary>
    internal static bool NinferReadyMarker(string? line, int port)
    {
        if (string.IsNullOrEmpty(line)) return false;
        if (!line.Contains("listening on http://", StringComparison.OrdinalIgnoreCase)) return false;
        if (port <= 0) return true;
        var i = line.IndexOf("http://", StringComparison.OrdinalIgnoreCase);
        var rest = line[i..];
        var seg = rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)[0];   // 只看那一段 URL，别把模型名里的数字算进来
        return seg.EndsWith(":" + port, StringComparison.Ordinal);
    }

}
