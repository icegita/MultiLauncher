using System.Diagnostics;
using System.Text;

namespace MultiLauncher;

// ============================================================================
// Ninfer 模块（DEC-096，第三个模块）—— 家底与身份这一段
// ============================================================================
// 位置与形态：
//   · 家底 = exe 同目录一份 `ninfer_models.ini`，**一个段 = 一个模型件**（段名 = 对外 model-id）。
//     整套口径照 llama 侧（DEC-079）：界面就地读写它、写之前必备份（留 3 份，DEC-110⑤）、拒绝即零改动、
//     自检与探针只在 `data\tmp\` 的副本上写。
//   · 与 llama 侧唯一不同的地方：**格子的家不在独立 ini 键里，在那一条 `args =` 模板里**
//     （支点 B1）。所以「保存」= 就地改写 args 里对应旗标的位置，「起服务」= 摘掉全部壳管理旗标
//     再按界面值追加。文件层写回的仍是整条 args（白名单只有 `args` 这一个键 —— backend / file
//     由用户手编，壳新建段、改段名、删段一概不做，同 llama 的"段不存在就报错不猜"）。
//   · 两个后端（win / wsl）共用这一份文件，段里 `backend =` 说归属；下拉按当前后端过滤（DEC-088）。
//
// 边界：`D:\Strata` 照旧一个字都不写；不接管模型下载；起停服务归用户实跑，壳只发只读请求。
public partial class MainForm
{
    // ---------------- 进程与运行状态（对位 _process / _llamaProcess：视图不存状态，字段留在各模块自己这儿） ----------------

    Process? _ninferProcess;                       // 壳拉起来的引擎进程（WIN = ninfer-serve-89.exe；WSL = wsl.exe 承载的那个）
    volatile bool _ninferExternal;                 // 端口上探到"不是本壳拉起"的实例
    volatile bool _ninferStopping;                 // 手动停（Kill 的退出码不算异常）
    volatile bool _ninferRunFailed;                // 状态灯红灯
    volatile string _ninferOp = "";                // 忙态文案（"加载中…"…；空 = 闲）
    int _ninferActivePort;                         // 引擎实际听着的内部端口（0 = 没跑）
    DateTime _ninferStartedAt;                     // 「刚起就退」多半是端口/显存/件没找到
    string _ninferRunningTag = "";                 // 这个进程跑的哪一件（= 下拉里"已加载"那一枚的判据）
    string _ninferRunningBackend = "";             // 这个进程是哪一侧起来的（win / wsl）——关后端那一侧的校验只认这个，不认界面当前值
    internal bool NinferRunning => _ninferProcess is { HasExited: false };

    // ---------------- 后端启用位（DEC-087 第④条：关掉 = 那一侧全隐身） ----------------

    /// <summary>模块开关（EnableNinfer）之外，Ninfer 内部还有两枚后端开关：
    /// 关掉那一侧 = 不扫那侧目录、不起进程、下拉不列那侧件、高级区不出那侧的格。</summary>
    internal static bool NinferBackendOn(Settings s, string backend) =>
        backend == NinferIni.BackendWsl ? s.NinferWslOn : backend == NinferIni.BackendWin ? s.NinferWinOn : false;

    /// <summary>当前界面上在用哪个后端（卡1 左侧那枚方块切的就是它）。段的家底另有 backend，
    /// 两边不一致时下拉那侧已经滤掉了，这里按界面值走。</summary>
    internal static string ActiveNinferBackend(Settings s) =>
        s.NinferBackend == NinferIni.BackendWsl && s.NinferWslOn ? NinferIni.BackendWsl
        : s.NinferWinOn ? NinferIni.BackendWin
        : s.NinferWslOn ? NinferIni.BackendWsl          // WIN 被关掉时兜到还开着那一侧（"至少启用一个"由保存校验拦）
        : NinferIni.BackendWin;

    // ---------------- 拉起 / 停止 ----------------

    /// <summary>④ 那条护栏的纯判据：**只有 WIN 那一代要求「上下文 &gt; 设备池」**（环靠主机池补足）。
    /// WSL 那代是全驻留形状（池 = 上下文才是它调好的样子），照 WIN 的判据去判就会把唯一那件 WSL 模型永远拦死。
    /// 返回 null = 放行。</summary>
    internal static string? CtxVsPoolProblem(string backend, string? ctx, string? pool)
    {
        if (backend != NinferIni.BackendWin) return null;
        if (!long.TryParse((ctx ?? "").Trim(), out var c) || !long.TryParse((pool ?? "").Trim(), out var p)) return null;
        return c > p ? null : $"上下文 {c} 必须大于设备池 {p}（WIN 这条环靠主机池补足，池子比上下文还大 = 摆位没有意义）。";
    }

    /// <summary>WIN 那侧交给引擎的参数表：**件路径必须是第一个位置参数**（引擎 usage =
    /// `ninfer-serve-89.exe &lt;model.ninfer&gt; [options]`），旗标跟在后面。
    /// 上一轮 WIN 起不来就是因为这条只给了旗标 —— 引擎把第一个不认识的东西当件名，
    /// 于是把 `--max-concurrency` 的值 `1` 当成了它（`ninfer-serve: unknown argument: 1` + 整张 help）。</summary>
    internal static List<string> NinferWinArgv(string artifact, string argv)
    {
        var list = new List<string>();
        if (artifact.Length > 0) list.Add(artifact);
        list.AddRange(argv.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
        return list;
    }

    // DEC-102（BUG-048）：自检干跑位 —— 真入口算到「这次起哪一件」就把结果记下来收手，绝不真起进程。
    internal static bool NinferStartDryRunForTest;
    internal string NinferLastStartTagForTest = "";
    /// <summary>DEC-135（拍板①）：干跑那一次**实际用上的那份参数**（唤醒 = 段、手动 = 界面卡）。
    /// 判据要打在真入口上，不能只测 `BuildNinferWakeSettings` 那个纯算式 —— 接线漏了照样是白写（BUG-044 那一族）。</summary>
    internal Settings NinferLastStartSettingsForTest = new();
    /// <summary>自检用：界面上当前选中的 Ninfer 那件（用例跑完要把状态按回去，别污染后面的用例）。</summary>
    internal string NinferSelectedTagForTest => _config.Current.NinferModel;
    /// <summary>自检用：把界面那枚方块按到指定那一侧（DEC-157「走段」那两条用例要造"方块在 WSL、请求要 WIN 那件"
    /// 这个形状 —— 不然测不出"归属到底跟谁"）。只动这一格，不写盘、不重扫、不碰网关。</summary>
    internal void SetNinferFaceForTest(string backend) => _config.Current.NinferBackend = backend;
    internal string NinferFaceForTest => _config.Current.NinferBackend;

    /// <summary>「这一次起哪一件」的判据（DEC-102）：**显式 tag 优先，没给才回落到界面选中**。
    /// 现场（BUG-048，用户 10-08 的日志）：唤醒明明拿到了请求要的 tag，却只把它写进
    /// `_config.Current.NinferModel` 就调 `StartNinferServer()` —— 后者开头 `CollectSettings()` 会把
    /// **下拉当前选中**收回 `s.NinferModel`、把刚写的顶掉 →「请求 swift 却起了 gsq」，引擎以 gsq 身份就绪，
    /// 客户端拿 swift 名被引擎 404。起件三入口的表态：唤醒 / 网关接管喂 tag；手动「加载」喂 null = 用选中。</summary>
    internal static string NinferStartTagFor(string? tagOverride, string uiSelection) =>
        string.IsNullOrWhiteSpace(tagOverride) ? (uiSelection ?? "").Trim() : tagOverride.Trim();

    /// <summary>自检出口：走真入口干跑一次（<see cref="NinferStartDryRunForTest"/> 打开时不起进程）。</summary>
    internal void StartNinferServerForTest(string? tagOverride, bool wakeFromSection = false)
        => StartNinferServer(tagOverride, wakeFromSection);

    /// <param name="gpuVerdict">DEC-138（C-9 后半）：护栏 ⑧ 那一发 nvidia-smi 由调用方**在别的线程先问出来**再递进来
    /// （`gpuProbed = true` 时护栏不再自己问）。手动「加载」与唤醒两条路都这么走，界面不再因为这一发最长冻 3 秒；
    /// 没问过（`gpuProbed = false`）护栏照旧当场问，结论一字未改。</param>
    void StartNinferServer(string? tagOverride, bool wakeFromSection, string? gpuVerdict = null, bool gpuProbed = false)
    {
        if (NinferRunning) return;
        // DEC-222：家底被增删过（删掉某一段）→ 只重扫 Ninfer 这一份列表并重摆下拉。
        // 排在干跑开关之前：自检走的就是这条真入口，戳门自己会把「没变」那档挡掉。
        RescanModelsIfHomeChanged(NinferKey);
        // DEC-159（他批的第 1 条）：每一次"起不来"都先把那句话记下来 —— 手动那一击拿去弹框，
        // 唤醒那一击拿去回客户端（同一个家，不许两条路各编一句）。
        NinferStartFailure = null;
        // DEC-135（拍板①）：这一击**必须表态**（参数没有默认值，形状同 DEC-122① 的 StartService/wakeFromRecord、
        // DEC-092 的 SaveConfig/commitParams）：
        //   true  = 远端唤醒 / 自动接管 → 按**要起那一段**取参（`BuildNinferWakeSettings`），界面上没点保存的草稿不参与；
        //   false = 卡3 那枚「加载」→ 吃界面卡现值（他的试验流照旧）。
        // 护栏判的仍然是实际那一把命令行（DEC-099 一字未退）；`NinferPreflight` 里那几条读 `s` 的格子，
        // 在 true 那一路也已经是段的值 —— 判据与起进程用的是同一份事实。
        var modal = !wakeFromSection;      // DEC-159：**弹窗只归人点的那一击**；远端那一击改"记日志 + 同一句话回客户端"
        var s = wakeFromSection ? BuildNinferWakeSettings(tagOverride ?? "") : CollectSettings();
        var tag = NinferStartTagFor(tagOverride, s.NinferModel);
        if (tagOverride is not null) s.NinferModel = tag;   // 起前护栏（swift × 行粒度那条）读的是 s.NinferModel，一起对齐
        if (NinferStartDryRunForTest)
        {
            NinferLastStartTagForTest = tag;
            NinferLastStartSettingsForTest = s;      // DEC-135：把"这一次是按哪一份参数起的"交出去（唤醒 vs 手动那条路的判据位）
            return;                                  // 自检干跑：不起进程、不进护栏（护栏那一发 nvidia-smi 也不许碰）
        }
        if (!ModuleRegistry.NinferEnabled(s))
        {
            NinferStartFailure = "Ninfer 模块没启用：不起引擎（设置页「通用 → 模块」里开启）。";
            LogN("Ninfer 模块未启用：不拉起引擎（到设置页「通用 → 模块」里开启）");
            return;
        }
        var backend = ActiveNinferBackend(s);
        if (!NinferBackendOn(s, backend))
        {
            NinferStartFailure = $"Ninfer 的 {NinferFaceLabel(backend)} 后端没启用：不起引擎（设置页 Ninfer 节里那一侧的开关）。";
            LogN($"Ninfer 的 {backend} 后端未启用：不起进程（设置页 Ninfer 节里那一侧的开关）");
            return;
        }
        if (_ninferExternal)
        {
            if (!EnsurePortFree(NinferServicePortValue(), "引擎端口上已有 Ninfer 在跑"))
            {
                NinferStartFailure = "引擎端口上已有别的 Ninfer 在跑，壳没有结束它。";
                LogN("已取消：外部 Ninfer 仍在跑");
                return;
            }
            _ninferExternal = false;
            // DEC-137（C-7）：原来是固定 Thread.Sleep(500)，而且坐在 UI 线程上（手动「加载」直接调、唤醒经 UiAsync 调）
            // —— 半秒不是判据（本文件注释自己写着"停成功只能看进程 + 端口"），端口没真让出照样 bind 失败。
            // 换成同一条轮询算式（120ms 一轮、上限 15 秒），并且不阻塞界面：等不到就交给下面的端口占用判据报错。
            if (!WaitPortReleased(NinferServicePortValue(), 15_000))
                LogN("端口还没让出来（等了 15 秒）：引擎可能起不来，届时日志会点名是谁占着");
        }
        _ninferRunFailed = false;
        var sec = ReadNinferSections().FirstOrDefault(x => string.Equals(x.Name, tag, StringComparison.OrdinalIgnoreCase));
        if (sec is null)
        {
            FailNinfer("没有这一件", tag.Length == 0
                ? "还没有选中 Ninfer 模型：先在「模型」下拉里选一件（段不存在时壳不新建，也不猜）。"
                : $"ninfer_models.ini 里没有 [{tag}] 这一段：{NinferIniPath()}\n\n要加新件请在文件里补一段（backend / file / args 三个键）。", modal);
            return;
        }
        if (SecBackend(sec).Length > 0)
        {
            // DEC-157（他批的"走段"）：**起哪一条链以段里那一侧为准**，界面上那枚方块只表示"现在看着哪一侧"。
            // 原来这里是「段与 face 不一致 = 当场拒绝 + 模态弹窗」，而网关 `/v1/models` 两侧全列、
            // 占用估算与显存账又早按段取数（DEC-156）—— 他实跑那句"界面在 WSL、唤醒的是 WIN 那件，被提示中断"就是这个形状。
            // 现在只剩一种真不能起的情况：**段那一侧被人在设置页关掉了**（关掉 = 那一侧全隐身，DEC-087 第④条）。
            backend = SecBackend(sec);
            if (!NinferBackendOn(s, backend))
            {
                FailNinfer("这一段所属的后端没启用", $"段 [{sec.Name}] 的 backend 是 {NinferFaceLabel(backend)}，" +
                    $"而设置页 Ninfer 节里那一侧被关掉了（关掉 = 不扫那侧目录、不起进程）。\n到设置页把那一侧打开，或换一件 {NinferFaceLabel(ActiveNinferBackend(s))} 那侧的。", modal);
                return;
            }
        }
        // 先算这一把命令行（摘模板旗标 + 按界面值追加 + 壳定那三枚），再照**它**过一遍护栏：
        // 界面上留空 = 把模板那枚摘掉，只看段里的模板判不出"少了 --max-shared-prefixes 0"这一档。
        var enginePort = NinferInternalPortValue();
        var argv = BuildNinferArgvLine(sec, s, backend, enginePort);
        var err = NinferPreflight(sec, s, backend, NinferIni.Split(argv), gpuVerdict, gpuProbed);   // DEC-138（C-9 后半）
        if (err is not null)
        {
            _ninferRunFailed = true;
            NinferStartFailure = err;
            LogN("启动被拒：" + err.Replace("\n", " "));
            // DEC-159：起前护栏拒 = 最容易被远端撞上的那一种（卡被**壳外**的东西占着、或段里的值手改坏了）。
            // 以前这里无条件弹框，而弹框坐在网关排进 UI 线程的那段 lambda 里、`UiAsync` 没有超时 →
            // 请求一路挂到有人来点「知道了」，客户端重试还会因为模态循环照样泵消息而**把框叠起来**。
            if (modal) Dialogs.Message(this, "没有启动：" + sec.Name, err, danger: true);
            return;
        }
        // 从公开口进来的请求走排队那一档：人自己点按钮 = 上面已经当场拒绝（DEC-083 第 6 条"分入口"）
        ProcessStartInfo psi;
        if (backend == NinferIni.BackendWsl)
        {
            var distro = s.NinferDistro.Trim();
            var bin = s.NinferWslBin.Trim();
            if (distro.Length == 0 || bin.Length == 0) { FailNinfer("WSL 侧没配全", "发行版与引擎可执行路径都不能空（设置页 Ninfer 节 WSL 那一组）。", modal); return; }
            // 旧壳实测：服务这条**不经过 bash -lc**（那是辅助命令用的），直接 wsl.exe -d <distro> <bin> <件> <旗标…>
            psi = new ProcessStartInfo("wsl.exe",
                $"-d {WslQuote(distro)} {bin} {WslQuote(sec.File)} {argv}")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            };
            LogN(RedactApiKey($"$ wsl -d {distro} {bin} {sec.File} {argv}", ApiKeyValue()));
        }
        else
        {
            var dir = s.NinferDir.Trim();
            var exe = Path.Combine(dir, NinferExeName(dir));
            if (!File.Exists(exe)) { FailNinfer("找不到引擎可执行", "ninfer-serve-89.exe 不在：" + exe +
                "\n\n引擎目录对不对？（设置页 Ninfer 节「WIN 后端 → 引擎目录」）", modal); return; }
            psi = new ProcessStartInfo(exe)
            {
                WorkingDirectory = dir, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
            // **件是位置参数、要排在所有旗标前面**（引擎 usage = `<model.ninfer> [options]`）→ 走 NinferWinArgv 那一个家
            foreach (var a in NinferWinArgv(sec.File, argv)) psi.ArgumentList.Add(a);
            foreach (var (k, v) in NinferRingEnv) psi.Environment[k] = v;      // 环需要的那五枚：缺一枚就是静默答错
            LogN(RedactApiKey("$ " + exe + " " + string.Join(" ", NinferWinArgv(sec.File, argv)), ApiKeyValue()));
            LogN("$ 注入环境变量：" + string.Join(" ", NinferRingEnv.Select(e => e.Key + "=" + e.Value)));
        }
        try
        {
            _ninferReadyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            void OnLine(string? line)
            {
                if (line is null) return;
                if (line.IndexOf('\0') >= 0) return;                  // wsl.exe 的 UTF-16 横幅乱码：整行丢（旧壳同一条判据）
                if (NinferReadyMarker(line, enginePort) && !IsDisposed)
                {
                    _ninferReadyTcs?.TrySetResult(true);
                    _ninferRunFailed = false;
                    // 同 Strata：就绪 = 空闲计时的真正起点（加载那十几秒不算"没人用"）
                    GatewayFor(ModelEntry.BackendNinfer)?.NoteUpstreamActivity();
                    UiPost(() => { LogN("Ninfer 引擎已就绪：" + line.Trim()); RefreshStatusUi(); });
                }
                NoteNinferStats(line);
                Log(line, "ninfer");
            }
            p.OutputDataReceived += (_, e) => OnLine(e.Data);
            p.ErrorDataReceived += (_, e) => OnLine(e.Data);
            var me = p;
            p.Exited += (_, _) =>
            {
                try { BeginInvoke(new Action(() => { if (ReferenceEquals(_ninferProcess, me)) OnNinferExited(); })); }
                catch { /* 窗口已销毁 */ }                              // DEC-061：迟到的 Exited 不许把新进程注销
            };
            if (!p.Start()) { _ninferReadyTcs = null; LogN("启动失败：进程没有起来"); _ninferRunFailed = true; return; }
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            _ninferProcess = p;
            _ninferActivePort = enginePort;
            _ninferRunningTag = sec.Name;
            _ninferRunningBackend = backend;        // 设置页要按"跑的是哪一侧"拦关开关（界面上切过去了不算）
            // 同 Strata：手动起件不走网关唤醒路径，锚点在这里补一次（就绪那一刻再补一次）
            GatewayFor(ModelEntry.BackendNinfer)?.NoteUpstreamActivity();
            _ninferStartedAt = DateTime.UtcNow;
            ResetNinferStats();
            SetButtons(true);
            RefreshStatusUi();
            LogN($"引擎已拉起（{backend} · 内部端口 {enginePort} · 这一件 = {sec.Name}）：壳守公开端口 {NinferPublicPortValue()}，" +
                 $"空闲 {NinferIdleMinutes(s, backend)} 分钟没有请求就停服还显存，下次请求自动起（本机约 9 秒 / WSL 约 15 秒）");
            // DEC-157（走段）：起来的那一侧与界面上那枚方块不一致时，**方块跟着过去**（落点 = [Current]，
            // 与人点方块是同一条路 = DEC-087 四联动第 3 条）。网关不动（它守公开口，与哪条链在跑无关）；
            // 模型下拉按新那侧的件表重列一次（DEC-097⑤：只重扫 Ninfer 这一家，别家日志一个字都不多）。
            // 参数格不在这里换 —— 那归 DEC-155 的跟模型那一条（FollowModelInPage → ApplyNinferRecord），别长第二个落点。
            if (!string.Equals(_config.Current.NinferBackend, backend, StringComparison.Ordinal))
            {
                _config.Current.NinferBackend = backend;
                SaveConfig(commitParams: false);
                RefreshNinferBackendUi();
                RefreshModels(NinferKey);
                RefreshStatusUi();
            }
        }
        catch (Exception ex)
        {
            _ninferRunFailed = true;
            _ninferReadyTcs = null;
            LogN("启动失败: " + ex.Message);
        }
    }

    /// <summary>引擎可执行的名字：目录里有 ninfer-serve-89.exe 用它；否则按本机 compute capability 选
    /// （-89 / -86 / -120a）。无 PTX 回退 = 装错架构直接起不来，所以这里只挑名字对得上的那一只。</summary>
    internal static string NinferExeName(string dir)
    {
        try
        {
            if (File.Exists(Path.Combine(dir, "ninfer-serve-89.exe"))) return "ninfer-serve-89.exe";
            foreach (var f in Directory.GetFiles(dir, "ninfer-serve-*.exe")) return Path.GetFileName(f);
        }
        catch { /* 目录读不动 */ }
        return "ninfer-serve-89.exe";
    }

    TaskCompletionSource<bool>? _ninferReadyTcs;
    internal bool NinferLoaded => NinferRunning && _ninferRunningTag.Length > 0;

    void StopNinferServer()
    {
        var p = _ninferProcess;
        if (p is null || p.HasExited) { SetButtons(false); RefreshStatusUi(); return; }
        LogN("$ 停止 Ninfer 引擎…");            // 本机那代被停时不打 "server stopped"：所以停成功只能看进程 + 端口
        _ninferStopping = true;
        try { p.Kill(true); }
        catch (Exception ex) { _ninferStopping = false; LogN("停止失败: " + ex.Message); }
    }

    void StopNinferQuiet()
    {
        var p = _ninferProcess;
        if (p is null || p.HasExited) return;
        _ninferStopping = true;
        try { p.Kill(true); } catch { /* 已经退了 */ }
    }

    /// <summary>等端口不再 LISTENING（实测 ≤ 0.2 秒、与进程退出同一拍；别睡固定秒数）。上限 15 秒兜异常。</summary>
    /// <summary>DEC-137（C-7）：同步版"等端口让出"—— 只在端口真还被占着时才等（120ms 一轮），
    /// 判据与下面那条异步版同一个家（`TryFindListenerOnPort`）。取代原先那记固定 `Thread.Sleep(500)`：
    /// 半秒不是判据（本文件注释自己写着"停成功只能看进程 + 端口"），而且它坐在 UI 线程上白冻界面。</summary>
    internal static bool WaitPortReleased(int port, int timeoutMs = 15000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (!TryFindListenerOnPort(port, out var pid, out _) || pid == Environment.ProcessId) return true;
            Thread.Sleep(120);
        }
        return false;
    }

    internal static async Task<bool> WaitPortReleasedAsync(int port, int timeoutMs = 15000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (!TryFindListenerOnPort(port, out var pid, out _) || pid == Environment.ProcessId) return true;
            try { await Task.Delay(120); } catch { return false; }
        }
        return false;
    }

    void OnNinferExited()
    {
        int code = -1;
        try { if (_ninferProcess is not null) code = _ninferProcess.ExitCode; } catch { }
        bool quick = _ninferStartedAt != default && (DateTime.UtcNow - _ninferStartedAt).TotalSeconds < 20;
        if (code != 0 && !_ninferStopping) _ninferRunFailed = true;
        _ninferStopping = false;
        _ninferProcess = null;
        _ninferExternal = false;
        _ninferActivePort = 0;
        _ninferRunningTag = "";
        _ninferRunningBackend = "";
        _ninferStartedAt = default;
        _ninferOp = "";
        _ninferReadyTcs?.TrySetException(new InvalidOperationException($"引擎进程已退出 (exit code {code})"));
        _ninferReadyTcs = null;
        SetButtons(false);
        RefreshStatusUi();
        LogN($"Ninfer 引擎已退出 (exit code {code})" +
             (quick && code != 0 ? "——刚拉起就退：多半是端口被占、显存不够，或件/引擎路径不对（看上面几行）" : ""));
    }

    /// <summary>起不来的那一句话（DEC-159）。**两条路共用同一个家**：手动那一击拿去弹框 + 日志，
    /// 唤醒那一击拿去 `throw` → 网关把同一句话回给客户端（原来那一条只能说"引擎没能起来（上面几行日志写了原因）"，
    /// 而远端那边没有人会去翻他屏幕上的日志）。</summary>
    internal string? NinferStartFailure;

    /// <summary>自检接缝：直接喂一次"起不来"，只看呈现方式（模态证人位），不碰进程。</summary>
    internal void FailNinferForTest(string title, string detail, bool modal) => FailNinfer(title, detail, modal);

    /// <summary>回给客户端那一句的兜底（记录为空时不许说"成功"，也不许把 null 端出去）。</summary>
    internal static string NinferWakeFailureLine(string? recorded) =>
        recorded is { Length: > 0 } r ? r : "引擎没能起来（上面几行日志写了原因）。";

    /// <summary>起不来时的落点。<paramref name="modal">**必须表态**（DEC-159，形状同 `wakeFromSection` / DEC-083 第 6 条「分入口」）：
    /// 人点按钮那一路 = 弹框（点了必须有看得见的落点）；请求从公开口进来那一路 = 只记日志，
    /// 因为 `Dialogs.Message` 是 `ShowDialog` 而网关那段 `UiAsync` 没有超时 —— 弹框会把请求挂到有人来点，
    /// 客户端重试时模态循环照样泵消息，框会一层层叠上去。</summary>
    void FailNinfer(string title, string detail, bool modal)
    {
        _ninferRunFailed = true;
        NinferStartFailure = title + "：" + detail;
        LogN(title + "：" + detail.Replace("\n", " "));
        if (modal) Dialogs.Message(this, title, detail, danger: true);
    }

    /// <summary>左栏那一击到底做什么（纯函数，自检穷举四态）。次序是硬口径：
    /// 引擎在跑 → 停引擎 + 关网关（成对，DEC-011）；端口上是外部实例 → 先结束它；只剩网关 → 关网关；
    /// 都没跑 → **只立网关**（DEC-100①：不碰引擎、不占显存，模型归第一个 /v1 请求或卡3「加载」）。</summary>
    internal enum NinferServiceMove { StopBoth, StopExternal, StopGatewayOnly, GatewayOnly }

    internal static NinferServiceMove PickNinferServiceMove(bool running, bool external, bool gatewayUp)
        => running ? NinferServiceMove.StopBoth
         : external ? NinferServiceMove.StopExternal
         : gatewayUp ? NinferServiceMove.StopGatewayOnly
         : NinferServiceMove.GatewayOnly;

    // 左栏「启动」= **只把网关立起来**（壳守公开口），不碰引擎、不占显存 —— 与 Strata 的路由模式同一条形状（DEC-100①）。
    // 模型的两个入口：第一个 /v1 请求（网关唤醒 → StartNinferServer）与卡3 那枚「加载」。
    // 这里原来是直接 StartNinferServer()：一点启动就把件读进显存，而且 `StartGateway("ninfer")` 在全代码里
    // 从来没有调用点（只有 Strata 的模式条会走那条路）→ 公开口上没人听，"下次请求自动起"那条链根本是断的。
    void ToggleNinferService()
    {
        switch (PickNinferServiceMove(NinferRunning, _ninferExternal, NinferGatewayUp))
        {
            case NinferServiceMove.StopBoth: StopNinferServiceAndGateway(); return;
            case NinferServiceMove.StopExternal:
                if (!EnsurePortFree(NinferServicePortValue(), "结束外部 Ninfer")) return;
                _ninferExternal = false; return;
            case NinferServiceMove.StopGatewayOnly: StopGateway(ModelEntry.BackendNinfer); return;
            default: StartNinferGateway(); return;
        }
    }

    /// <summary>只立网关。起不来时 `StartGateway` 自己已经弹过窗、写过日志，这里不再补第二句（同义反复）。</summary>
    void StartNinferGateway()
    {
        // DEC-222：Ninfer 左栏「启动」只立网关、不起引擎 —— 但列表照样要跟上家底（他就在这一档发现列表不更新）。
        RescanModelsIfHomeChanged(NinferKey);
        StartGateway(ModelEntry.BackendNinfer);
        if (!NinferGatewayUp) return;
        LogN($"$ 网关已就绪（公开端口 {NinferPublicPortValue()}，Ninfer 用内部端口 {NinferInternalPortValue()}）：当前不加载模型，/v1 请求到达时自动加载");
        RefreshRunInfo();
        RefreshStatusUi();
    }

    /// <summary>「停止」= 停引擎 + 关网关（DEC-011 那条成对口径，与 Strata 的 `StopServiceAndGateway` 同形）。</summary>
    void StopNinferServiceAndGateway()
    {
        StopNinferServer();
        if (NinferGatewayUp) StopGateway(ModelEntry.BackendNinfer);
        RefreshStatusUi();
    }

    // DEC-138（C-9 后半）：这一枚按钮改异步 —— 护栏 ⑧ 那一发 nvidia-smi（最长 3 秒）先在别的线程问完，
    // 再把结果递给护栏。以前它是同步一路跑在 UI 线程上：点「加载」→ 界面冻住最长 3 秒（形状同 BUG-039 那 13 秒，
    // 只是量小）。忙态位 `_ninferOp` 在问之前置上，问完才放行，所以 await 期间用户点到的是同一道闸门。
    async void NinferModelAction()
    {
        var m = SelectedModel();
        if (m is null || _ninferOp.Length > 0) return;
        if (NinferRunning)
        {
            // DEC-141：引擎在跑就卸**它实际在跑的那一件**，不再要求下拉指过去。
            // 于是 DEC-102③ 那一整块（跑别的件时「加载」弹「无法启动：引擎已经在跑 X」）成套退档 =
            // 那条死胡同的成因是"按钮在跑着东西的时候写「加载」"，现在那一枚写的就是「卸载」，
            // 一步到位；"不替用户停旧起新、手动切件 = 先卸载再加载"这条口径本身一字未改。
            var t = UnloadTagFor(_ninferRunningTag, m.Tag);
            LogN("$ 卸载模型 " + t + "（网关保留，下次 /v1 请求会自动加载）");
            _ninferOp = "卸载中…"; RefreshStatusUi(); StopNinferServer(); _ninferOp = ""; return;
        }
        // 「加载」= 起引擎，而这一族的引擎**恒绑内部口**（公开口归壳网关）→ 网关没立就先把网关立起来，
        // 否则模型起来了也没有入口（DEC-100①：启动只立网关、加载才起引擎，两条路都得保证客户端连得进来）。
        if (!NinferGatewayUp) StartNinferGateway();
        if (!NinferGatewayUp) return;
        _ninferOp = "加载中…";
        RefreshStatusUi();
        string? gpuVerdict = null; bool gpuProbed = false;
        try
        {
            // "这一件要吃多少显存"在 UI 线程上算（要读段、要看界面值），后台那一趟只做 spawn 与读数；
            // 问不出来（异常）= 当没这条校验，口径同 DEC-067。
            var need = NinferNeedGiBFor(m.Tag);
            if (need is { } n)
            {
                try { gpuVerdict = await Task.Run(() => GpuShortMessage(n)); }
                catch (Exception ex) { LogN("$ 显存那一发没问成（当没这条校验）：" + ex.Message); gpuVerdict = null; }
                gpuProbed = true;
            }
            // 手动那一击 = 起"界面上选中的这一件"，含没点保存的草稿（DEC-135）。
            // tag 显式喂 m.Tag：await 之后界面选中可能已经变了，不喂就会"按点击时看见的那件问的显存、按另一件起进程"。
            StartNinferServer(m.Tag, wakeFromSection: false, gpuVerdict, gpuProbed);
        }
        finally { _ninferOp = ""; if (!IsDisposed) RefreshStatusUi(); }
    }

    // 2026-10-15（他点名「停止网关/停止按钮,统一修改为停止」）：三档统一「停止」，与 Strata 那条一字不差
    //（原来"只剩网关"那一档报「停止网关」）。点下去做的事没变。
    internal static string StartStopTextForNinfer(bool running, bool external, bool gatewayUp)
        => running || external || gatewayUp ? "停止" : "启动";

    internal static string ModelActionTextForNinfer(string? busy, bool anyRunning)
    {
        if (!string.IsNullOrEmpty(busy)) return busy;
        return anyRunning ? "卸载" : "加载";   // DEC-141：不看"跑的是不是选中的那件"
    }

    // DEC-101②：与另两页同形状 —— 判据拆纯函数（自检穷举用），实例壳只喂本页的实时值。
    // DEC-125④：颜色一律从状态灯那张表取（`LightColor`），"待机"那一档跟着灯走橙 —— 与另两页一字不差、同色。
    internal static (string Text, Color Color) StatusTextForNinfer(bool failed, string op, bool running, bool external, bool gatewayUp)
    {
        if (failed) return ("● 启动失败", LightColor(LightState.Red));
        if (op.Length > 0) return ("● " + op, LightColor(LightState.Orange));
        if (running) return ("● 运行中", LightColor(LightState.Green));
        if (external) return ("● 运行中（已有服务）", LightColor(LightState.Green));
        if (gatewayUp) return ("● 待机 · 模型未加载", LightColor(LightState.Orange));
        return ("已停止", LightColor(LightState.Gray));
    }

    internal (string Text, Color Color) NinferStatusText() =>
        StatusTextForNinfer(_ninferRunFailed, _ninferOp, NinferRunning, _ninferExternal, NinferGatewayUp);

    internal static (LightState State, string Desc) NinferLight(bool failed, bool running, bool loaded,
                                                                bool external, bool gatewayUp, int port)
    {
        if (failed) return (LightState.Red, "异常 · 启动失败");
        if (running) return loaded ? (LightState.Green, $"运行中 · 模型已加载 · 端口 {port}")
                                   : (LightState.Orange, $"待机 · 模型未加载 · 端口 {port}");
        if (external) return (LightState.Green, $"运行中 · 外部服务 · 端口 {port}");   // DEC-100②：括号换点号，与 Strata 那句一字不差
        if (gatewayUp) return (LightState.Orange, $"待机 · 模型未加载 · 端口 {port}");
        return (LightState.Gray, $"已停止 · 端口 {port}");
    }

}


