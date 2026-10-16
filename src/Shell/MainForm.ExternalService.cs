using System.Diagnostics;
using System.Text.Json.Nodes;

namespace MultiLauncher;

// 外部服务的识别与结束（用户反馈 2026-10-01：按钮状态要跟随实际）。
// 2026-10-15 从 MainForm.Service.cs 整段搬出，一行未改。
public partial class MainForm
{
    // ---------------- 外部服务的识别与结束（用户反馈 2026-10-01：按钮状态要跟随实际） ----------------

    // 按监听端口找进程：解析 `netstat -ano` 的 "TCP 0.0.0.0:8080 … LISTENING 12345"。
    // 只在用户点「停止/卸载」且探到外部服务时调用（确认框中显示进程名与 PID 由用户拍板），不参与轮询。
    internal static bool TryFindListenerOnPort(int port, out int pid, out string procName)
    {
        pid = 0; procName = "";
        try
        {
            using var p = Process.Start(new ProcessStartInfo("netstat", "-ano")
            { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true });
            if (p is null) return false;
            // DEC-061：先挂异步读、再**限时**等退出。以前是 ReadToEnd() 之后才 WaitForExit(3000)——
            // ReadToEnd 一路阻塞到子进程自己关掉 stdout，那道超时等于没写；netstat 在连接数多的机器上要跑
            // 几百毫秒到数秒，而这条链是在 **UI 线程**上（StartService → PortTakenByOtherModule），点「启动」就假死。
            var outT = p.StandardOutput.ReadToEndAsync();
            if (!p.WaitForExit(3000)) { try { p.Kill(true); } catch { /* 已经退了 */ } return false; }
            var text = outT.GetAwaiter().GetResult();
            foreach (var raw in text.Split('\n'))
            {
                var t = raw.Trim();
                if (!t.StartsWith("TCP", StringComparison.Ordinal)) continue;
                var parts = t.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 5 || parts[3] != "LISTENING") continue;
                if (!parts[1].EndsWith(":" + port, StringComparison.Ordinal)) continue;
                if (!int.TryParse(parts[^1], out var found) || found <= 0) continue;
                pid = found;
                try { procName = Process.GetProcessById(found).ProcessName + ".exe"; }
                catch { procName = "未知进程"; }
                return true;
            }
        }
        catch { /* 查不到就当没有 */ }
        return false;
    }

    // DEC-024 A 段：启动前的实际占用检查——目标端口被"自家 / 已知的另一个模块"占着就拒起并说清让谁让路。
    // 最后一档查到"任何别人在听"也一并挡下（指名道姓报进程），免得 doom start 完再对着 bind 报错猜是谁。
    internal string? PortTakenByOtherModule(int port, bool startingLlama)
    {
        if (StrataGateway is not null && StrataGateway.Port == port) return $"端口 {port} 是网关的公开端口";
        if (startingLlama)
        {
            if (_process is { HasExited: false } && _serviceBindPort == port) return $"端口 {port} 上是本启动器拉起的 Strata 服务";
            if (_externalRunning && ServicePortValue() == port) return $"端口 {port} 上是外部拉起的 Strata 服务";
        }
        else
        {
            if (_llamaProcess is { HasExited: false } && _llamaActivePort == port) return $"端口 {port} 上是本启动器拉起的 llama-server";
            if (_llamaExternal && LlamaPortValue() == port) return $"端口 {port} 上是外部拉起的 llama-server";
        }
        if (TryFindListenerOnPort(port, out var pid, out var name))
        {
            if (pid == Environment.ProcessId) return $"端口 {port} 上是本启动器自己的监听";
            return startingLlama
                ? $"端口 {port} 上已经有个监听者：{name}（PID {pid}），像是另一个引擎的口"
                : $"端口 {port} 上已经有个监听者：{name}（PID {pid}）";
        }
        return null;
    }

    // 「把端口腾出来」：找到占用者 -> 弹确认（显示进程名与 PID）-> 用户确认才杀。
    // 返回 true = 可以继续（已经结束，或本来就没人在跑）；false = 用户取消或结束失败。
    bool EnsurePortFree(int port, string title)
    {
        if (!TryFindListenerOnPort(port, out var pid, out var name)) return true;   // 端口上已经没人了
        // 台账防护（DEC-024）：端口上是**本启动器拉起的**进程时，绝不在这里结束——
        // 曾有过"llama 被误报成外部 Strata"的路径，若顺着它确认就会误杀自己的 llama（BUG-023 的附加险情）
        if (pid == _process?.Id || pid == _llamaProcess?.Id)
        {
            Log($"端口 {port} 上是本启动器拉起的 {name}（PID {pid}）：不通过「结束外部进程」处理，请用对应模块的「停止」或改端口");
            return false;
        }
        // 深色确认框（③）：这是"要结束一个壳没拉起来的进程"，主按钮用红、文字写清动作
        if (Dialogs.Confirm(this, title,
                $"端口 {port} 上是 {name}（PID {pid}），不是本启动器拉起的。", "要结束它吗？",
                yes: "结束它", no: "取消", danger: true) != DialogResult.Yes) return false;
        try
        {
            Process.GetProcessById(pid).Kill(true);
            Log($"$ 已结束外部进程 {name}（PID {pid}）");
            return true;
        }
        catch (Exception ex)
        {
            Log("结束外部进程失败: " + ex.Message);
            return false;
        }
    }
}
