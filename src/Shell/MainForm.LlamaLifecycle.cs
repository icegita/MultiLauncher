using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MultiLauncher;

// llama 的生命周期（拉起/停止/就绪/退出处理）与命令行组装。
// 2026-10-15 从 MainForm.Llama.cs 整段搬出，一行未改。
public partial class MainForm
{
    // ---------------- 生命周期 ----------------

    void StartLlamaServer()
    {
        if (LlamaRunning) return;
        // DEC-212（2026-10-22，他报"改完 ini 重启服务界面不更新"）：拉起之前先看家底文件有没有被人改过 ——
        // 改过就按当前选中的段重读一遍并推界面（`CollectSettings()` 取的是界面卡，卡不更新 = 起的还是老配置）。
        ReloadLlamaRecordIfIniChanged();
        // DEC-222：家底被增删过（删掉某一段 / 新加一段）→ 只重扫 llama 这一份列表并重摆下拉。
        // 与上面那句分工：那句管**参数**（按选中的段重读界面卡），这句管**列表本身**；戳没变就一个字节都不读。
        RescanModelsIfHomeChanged(LlamaKey);
        // 模块开关（B 段地基，DEC-024）：停用的模块不起进程（全隐身）
        if (!ModuleRegistry.LlamaEnabled(_config.Current))
        {
            LogL("llama.cpp 模块未启用：不拉起 llama-server（到设置页「通用 → 模块」里开启）");
            return;
        }
        // 端口上已有外部 llama-server：起新的会撞端口。先问一句，确认后结束它再起（用户反馈：按钮要反映真实状态）
        if (_llamaExternal)
        {
            if (!EnsurePortFree(LlamaPortValue(), "已有 llama-server 在跑")) { LogL("已取消：外部 llama-server 仍在跑"); return; }
            _llamaExternal = false;
            Thread.Sleep(500);      // 给端口一点释放时间（罕见路径，短暂让出主线程没关系）
        }
        _llamaRunFailed = false;    // 每次启动尝试先清失败标记（状态灯红灯）
        var s = CollectSettings();
        var dir = s.LlamaDir.Trim();
        var exe = dir.Length > 0 ? Path.Combine(dir, "llama-server.exe") : "";
        if (exe.Length == 0 || !File.Exists(exe))
        {
            FailLlama("找不到 llama-server.exe",
                "请先在设置页 llama.cpp Tab 配置「llama.cpp 目录」。\n当前值：" + (dir.Length > 0 ? dir : "（空）"));
            return;
        }
        var preset = ResolveLlamaPreset(s, out var presetErr);
        if (preset is null) { FailLlama("无法准备模型配置", presetErr); return; }

        var port = LlamaPortValue();
        // 启动前实际占用检查（DEC-024 A 段）：目标端口被另一个模块占着就拒起并说清让谁让路
        if (PortTakenByOtherModule(port, startingLlama: true) is { } takenBy)
        {
            LogL($"llama-server 不能启动：{takenBy}，端口没法共用。请到设置页把两个模块的端口错开（保存时会拦下相同的值），或先停掉它");
            return;
        }
        // DEC-157（他批的第 1 条）：**单体模式**那一下是连模型一起起 = 真要吃显存 → 人点按钮那一路当场拒绝并点名。
        // 路由模式那一下只把 server 立起来（一件都没加载，占的是零点几 GiB），不该拦 —— 否则又回到他当年那句
        // "为什么起一个另一个就退出"（BUG-046）。
        if (!_routeModeLlama && GpuMutexRefusal(LlamaKey) is { } heldGpu)
        {
            FailLlama("没有启动", heldGpu);
            return;
        }
        var args = BuildLlamaArgs(s, s.Host, port, preset);
        var psi = new ProcessStartInfo(exe)
        {
            WorkingDirectory = dir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        LogL(RedactApiKey("$ " + exe + " " + string.Join(" ", args), ApiKeyValue()));
        // DEC-142：起服务这一行之后点名一次"这一族没定过 parallel"（判据与句子都在 LlamaParallelHintLine，
        // 壳不改行为、也不替他改家底 —— 慢的原因与两条补法都在那句话里）
        if (LlamaParallelHintLine(args.Contains("--parallel") || args.Contains("-np"),
                                  LlamaIniDefinesParallel(preset)) is { } speedHint)
            LogL(speedHint);

        try
        {
            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            // B 段阶段③：llama-server 的行按类型记（llama）——文件进 logs\llama.log；界面只在 llama.cpp 页显示
            // ④（DEC-038）：顺手从这些行里抠出状态栏的三个值（预填充 / 解码 / MTP 接受率），日志本身照旧
            p.OutputDataReceived += (_, e) => { if (e.Data is null) return; NoteLlamaStats(e.Data); Log(e.Data, "llama"); };
            p.ErrorDataReceived += (_, e) => { if (e.Data is null) return; NoteLlamaStats(e.Data); Log(e.Data, "llama"); };
            // DEC-061：同 Strata 侧——退出的必须是当前这一个 llama-server，否则旧进程迟到的 Exited
            // 会把刚拉起的新 server 的状态（端口、运行标记、灯）一起清掉。
            var me = p;
            p.Exited += (_, _) =>
            {
                try { BeginInvoke(new Action(() => { if (ReferenceEquals(_llamaProcess, me)) OnLlamaExited(); })); } catch { /* 窗口已销毁 */ }
            };
            if (!p.Start())
            {
                LogL("llama-server 启动失败：进程没有起来");
                return;
            }
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            ResetLlamaStats();        // ④（DEC-039）：新进程 = "启动以来"重新起算，别把上一轮的累计混进来
            _llamaProcess = p;
            _llamaActivePort = port;
            _llamaStartedAt = DateTime.UtcNow;
            RefreshStatusUi();
            // DEC-151：空闲卸载按分钟报（与另两模块的日志同一单位），留空/0 = 不自动卸
            var idleMin = LlamaIdleMinutes(s);
            LogL($"llama-server 已拉起（内部端口 {port}，{LlamaPortSourceText(PublicPortValue(), InternalPortValue(), _config.Current.LlamaInternalPort, AnyGateway?.Port ?? 0, _serviceBindPort, port)}）：模型按需加载，第一个请求会等它加载完；"
                + (idleMin > 0 ? $"空闲 {idleMin} 分钟自动卸模型" : "空闲不自动卸模型"));
            ProbeLlamaReady(p, port);
            // 单体模式：启动连模型一起起（用户规格 2026-10-01）；路由模式只起服务（模型按需，或按 ini 里的 load-on-startup）
            if (!_routeModeLlama)
            {
                var tag = SelectedTag();
                if (tag.Length > 0) LoadLlamaModel(tag);    // 内部会等 server 就绪再加载
            }
        }
        catch (Exception ex)
        {
            _llamaRunFailed = true;
            LogL("llama-server 启动失败: " + ex.Message);
        }
    }

    void StopLlamaServer()
    {
        var p = _llamaProcess;
        if (p is null || p.HasExited)
        {
            RefreshStatusUi();
            return;
        }
        LogL("$ 停止 llama-server…");
        _llamaStopping = true;
        try { p.Kill(true); }
        catch (Exception ex) { _llamaStopping = false; LogL("停止 llama-server 失败: " + ex.Message); }
    }

    // 退出路径上的停 llama-server：不弹窗、不等，尽力而为（与 StopServerQuiet 同口径）
    void StopLlamaQuiet()
    {
        var p = _llamaProcess;
        if (p is null || p.HasExited) return;
        _llamaStopping = true;
        try { p.Kill(true); } catch { /* 已经退了 */ }
    }

    void OnLlamaExited()
    {
        int code = -1;
        try { if (_llamaProcess is not null) code = _llamaProcess.ExitCode; }
        catch { /* 还没真正启动过 */ }
        // "刚拉起就退"多半是端口被占（bind 失败，llama-server 自己打的是 couldn't bind...）——
        // 退出行带上这句提示，别让用户对着 exit code 猜（用户实跑 2026-10-01 的困惑点之一）
        bool quick = _llamaStartedAt != default && (DateTime.UtcNow - _llamaStartedAt).TotalSeconds < 15;
        // 状态灯（B 段）：非手动停且非零退出 = 异常
        if (code != 0 && !_llamaStopping) _llamaRunFailed = true;
        _llamaStopping = false;
        _llamaProcess = null;
        _llamaExternal = false;      // 壳拉起的刚退出；端口上暂时没人（下个探测周期再确认）
        _llamaActivePort = 0;
        _llamaStartedAt = default;
        _llamaLoaded.Clear();        // 进程没了，加载状态一并清空
        _llamaFailed.Clear();
        _llamaOp = "";
        RefreshStatusUi();
        LogL($"llama-server 已退出 (exit code {code})"
            + (quick && code != 0 ? "——刚拉起就退，多半是端口被占（看上面几行有没有 couldn't bind/socket）" : ""));
    }

    // 就绪信号只是日志一行（状态灯看进程在不在）：llama-server 起来后把 /health 探到有响应为止。
    // 模型没加载时 /health 可能回 503（loading），那也算「server 活了」——所以不做状态码判断。
    async void ProbeLlamaReady(Process p, int port)
    {
        var url = $"http://{ProbeHost()}:{port}/health";
        for (int i = 0; i < 60; i++)
        {
            if (IsDisposed || !LlamaRunning || !ReferenceEquals(_llamaProcess, p)) return;
            try
            {
                using var _ = await ProbeGetAsync(url);
                _llamaRunFailed = false;    // 服务活了即清失败标记（状态灯红灯）
                LogL("llama-server 已就绪：http://" + ProbeHost() + ":" + port + "/");
                return;
            }
            catch { /* 还没监听上，接着等 */ }
            try { await Task.Delay(500); } catch { return; }
        }
    }

    void FailLlama(string title, string detail)
    {
        _llamaRunFailed = true;      // 红灯：拉起前的配置/环境问题也算"启动失败"
        LogL(title + "：" + detail);
        Dialogs.Message(this, title, detail, danger: true);
    }

}
