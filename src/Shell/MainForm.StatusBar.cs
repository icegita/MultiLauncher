using System.Globalization;
using System.Text.Json.Nodes;

namespace MultiLauncher;

// 底部状态栏（2026-10-03，DEC-038；同日第二轮改版见 DEC-039）。用户口径：**三行数值、每档两值**——
// `预填充 平均|当前 t/s · 解码 平均|当前 t/s · MTP 接受率 平均|当前%`，平均 = 启动以来，当前 = 引擎/日志的实时窗口；
// 取不到的那档显示短横 `-`（长横杠太占位）。文字**右对齐**、无分隔线、矮条；两个模块都要，别的信息不放
// （左栏底部「模块状态」继续管"在不在跑 + 工作端口"）。服务停下时整条隐藏。
//
// 数据源两侧不一样，这是实测定的，不是随手挑的：
//   Strata = `GET /metrics`，打**实际服务端口**（`_serviceBindPort`），不打公开端口——DEC-002 的纪律：
//            打公开端口要经网关，会把空闲停掉的模型又吵醒。实测 0.1.35 返回约 10KB JSON：
//            `live.prefill_tok_s_mean`（本轮预填充均值）、`live.tok_s`（引擎自己算的近 2 秒窗口，RATE_WINDOW_S=2.0）、
//            `totals.drafts_accepted / drafts_offered`（MTP 投机草稿的整体接受率）。
//   llama  = 解析 llama-server 自己的 stdout。它其实也有 Prometheus 的 /metrics
//            （`llamacpp:prompt_tokens_seconds` / `predicted_tokens_seconds` / `spec_decode_num_*_total`），
//            但**路由模式下必须带 ?model=**，而带 model 的请求会把那个模型真加载起来（实测不带 model 直接
//            400 "model name is missing from the request"）——为了三个数字去动几十 GB 显存不划算。
//            而这些数它本来就每几秒打在日志里，壳一直在收：
//              `prompt processing, n_tokens = 4096, progress = 0.42, t = 3.20 s / 1280.00 tokens per second`
//              `n_gen = 123, tg = 45.20 t/s, tg_3s = 47.10 t/s`
//              `draft acceptance = 0.65432 ( 1234 accepted / 1888 generated), mean len = 1.65`
//
// 节拍 = 5 秒，与 ③ 的日志节流窗口同一个常量（`ProgressGapSeconds`），界面与状态栏因此同一拍。
public partial class MainForm
{
    // Timer 必须写全名：隐式 using 里同时有 System.Threading.Timer 与 System.Windows.Forms.Timer（不写会歧义）
    readonly System.Windows.Forms.Timer _statsTimer = new() { Interval = (int)(ProgressGapSeconds * 1000) };
    UiStatusBar? _statusBar;

    // 每个数两档：`启动以来平均 | 当前`（用户 2026-10-03 反馈的口径；取不到显示短横 "-"，不用长横杠）
    // Strata：平均 = /metrics 的 totals（引擎进程启动以来的累计）；当前 = live.*（引擎自己的窗口值）
    // DEC-125②：这几档收成一个 StatPack 交出去（新增的队列 / 缓存命中 / token 总量一起走这一份）
    StatPack _strataPack = EmptyPack;
    internal static readonly StatPack EmptyPack =
        new(null, null, null, null, null, null, null, null, null, null, null, null);
    // llama：当前 = 每几秒打的进度行（带 TTL，超时不装作在跑）；平均 = 每次请求收尾行的 token/ms 累加
    double? _llamaPrefillNow, _llamaDecodeNow;
    DateTime _llamaStatsAt = DateTime.MinValue;
    double _llamaPromptTok, _llamaPromptMs, _llamaDecodeTok, _llamaDecodeMs, _llamaAccepted, _llamaOffered;
    const double LlamaStatsTtlSeconds = 15.0;     // 超过这么久没再出现就当没有——旧数字不能装作还在跑
    // DEC-148：`/metrics` 那一路的读数（5 秒一拍）。`_llamaPrev` = 上一拍的水表读数（`DeltaRate` 用它相减出「当前」档），
    // `_llamaMetricTag` = 这一路是按哪一件打出来的（换件 = 换子实例 = 计数器归零，基准必须一起清）。
    LlamaCounters? _llamaPrev;
    StatPack? _llamaMetricPack;
    DateTime _llamaMetricAt = DateTime.MinValue;
    string _llamaMetricTag = "";
    // 证人位（DEC-148）：两个数分得开"这条真路径走到了"与"真发了一发" —— 只断后者分不开
    // "没件在跑所以没问"与"这条线压根没接上"（BUG-043 那一族空判据，DEC-136 E-3 同形）。
    int _llamaMetricsAttempts, _llamaMetricsAsked;
    internal int LlamaMetricsAttemptsForTest => _llamaMetricsAttempts;
    internal int LlamaMetricsAskedForTest => _llamaMetricsAsked;
    /// <summary>自检出口：直接踩一次那一发取数（喂样本走真路径，不能只断纯函数）。</summary>
    internal System.Threading.Tasks.Task PollLlamaStatsForTest() => PollLlamaStats();
    /// <summary>自检出口：读/清这一路当前的包（"没件在跑 = 全短横"要看得见落点）。</summary>
    internal StatPack? LlamaMetricPackForTest => _llamaMetricPack;
    /// <summary>自检出口：喂一份"刚问出来的包"进去（`LlamaPack` 到底吃哪一份，要能被真路径钉住）。</summary>
    internal void SetLlamaMetricPackForTest(StatPack? p)
    { _llamaMetricPack = p; _llamaMetricAt = DateTime.Now; _llamaMetricTag = "zz-selftest"; }
    /// <summary>自检出口：踩一次清零（换件 / 进程没了那两条落点）。</summary>
    internal void ResetLlamaStatsForTest() => ResetLlamaStats();
    internal void SetLlamaExternalForTest(bool on) => _llamaExternal = on;

    // 显存 / 温度（DEC-125②）：**整卡值、不分模块** —— nvidia-smi 只有全卡口径（本机逐进程查询实测回 [N/A]，
    // 凑不出"这一模块占多少"），所以三页显示的是同一张卡上的数。只有勾了这两档才去问，
    // 且一律走后台（一次 spawn 最长 3 秒，不能坐在 UI 线程上）；测试态一次都不问。
    double? _vramUsedMib, _vramTotalMib, _gpuTempC;
    internal static bool GpuProbeDisabledForTest = false;    // 自检与出图台一律不起 nvidia-smi（按进程收口，DEC-111 同族）

    // 状态条区域 = 常规外层留白（16）+ 额外高度（8）= 24 逻辑像素（用户 2026-10-03：显示时"加一点高度作为
    // 状态条区域"，具体数值我按行盒 17 + 上下各 ~3.5 留白量的；嫌厚嫌薄就改这一个常量）。
    internal const int StatusBarLogicalHeight = 24;
    const int FormMarginLogical = 16;      // 窗体四周的常规外层留白（与左/右/上三边一致）

    // BuildUi 末尾调用（DPI 已在 ctor 里捕获过）
    void SetupStatusBar()
    {
        _statusBar = new UiStatusBar();
        Controls.Add(_statusBar);
        _statusBar.BringToFront();
        Resize += (_, _) => LayoutStatusBar();
        LayoutStatusBar();
        RefreshStatusBar();
    }

    // 状态条显隐 → 窗体**底留白**跟着变：显示时留 24（= 条区），隐藏时回到 16（与另外三边一样的常规留白）。
    // 为什么这么绕：窗体有外层留白，Bottom 停靠的控件会停在留白之上（条子下面反而空出一条——用户前几轮
    // 反馈的"下方大量空白"就是这个）；而且光把文字画进留白里，视觉上"没有一条状态条区域"（用户 2026-10-03 第二轮）。
    // 所以：条子的高度 = Padding.Bottom，显隐即改 Padding.Bottom，再由 LayoutStatusBar 把它铺满这条带子。
    internal void StatusBarBandChanged(bool visible)
    {
        int bottom = UiTheme.S(this, visible ? StatusBarLogicalHeight : FormMarginLogical);
        if (Padding.Bottom != bottom)
            Padding = new Padding(Padding.Left, Padding.Top, Padding.Right, bottom);
        LayoutStatusBar();
    }

    // 几何：铺满窗口底部那条带子（卡片底边 = 条子上边、条子下边 = 窗口底边），文字在带子里 VerticalCenter
    void LayoutStatusBar()
    {
        if (_statusBar is null) return;
        int band = Padding.Bottom;
        _statusBar.Bounds = new Rectangle(0, ClientSize.Height - band, ClientSize.Width, band);
        _statusBar.BringToFront();
    }

    // 第 0 轮 DEC-094：状态栏问哪个模块取数 = ActiveView（Strata 走 /metrics、llama 读它自己的日志行）
    // DEC-125②：显存 / 温度是整卡值、不属任何模块，所以在这条总拍子上问一次（勾了才问、走后台、测试态不问）。
    void StatsTick() { ActiveView.PollStats(); _ = SampleGpuForStatusBar(); }

    /// <summary>勾了「显存」或「温度」才起一次 nvidia-smi；没勾 / 状态栏整条关着 / 测试态 = 一个字都不问，
    /// 并把上一轮的数收掉（免得关掉了还在显示旧值）。DEC-128①：判据走 EffectiveStatusBar ——
    /// 设置页预览里刚勾上这两档时也问（"方便查看效果"）。</summary>
    /// <summary>自检出口：直接踩一次状态栏那发显存查询的入口 —— 用来证"这条路走到了但被测试闸挡下"
    /// （DEC-136 E-3；只断计数为 0 分不开"没走到"与"走到了但门开着"）。</summary>
    internal System.Threading.Tasks.Task SampleGpuForStatusBarForTest() => SampleGpuForStatusBar();

    async Task SampleGpuForStatusBar()
    {
        var (vis, itemsCsv, _, _) = EffectiveStatusBar();
        var shown = StatusBarIdSet(itemsCsv);
        // DEC-136（E-3）：证人位要记"这条路走到了"，不能只记"真起了进程"——
        // 只断 `GpuQueryCallsForTest == 0` 的话，"门开着"、"没勾那两档"、"这条路根本没跑"三种情况全都满足 = 空判据。
        bool wants = vis && (shown.Contains("vram") || shown.Contains("temp"));
        if (wants) GpuQueryAttemptsForTest++;
        bool want = wants && !GpuProbeDisabledForTest;
        if (want)
        {
            var g = await Task.Run(QueryGpuCardStatus);          // 最长 3 秒：绝不坐在 UI 线程上等它
            if (IsDisposed) return;
            _vramUsedMib = g.UsedMib; _vramTotalMib = g.TotalMib; _gpuTempC = g.TempC;
        }
        else { _vramUsedMib = _vramTotalMib = _gpuTempC = null; }
        RefreshStatusBar();
    }

    /// <summary>整卡显存与温度（多卡：显存相加、温度取最高 = 最坏那一颗）。
    /// 失败返回全 null = **判不了就当没有这一档**（显示短横，口径同 DEC-067 / 那条显存起前算数）。</summary>
    internal static (double? UsedMib, double? TotalMib, double? TempC) QueryGpuCardStatus()
    {
        var rows = RunGpuQuery("memory.used,memory.total,temperature.gpu");
        double used = 0, total = 0; double? temp = null; bool any = false;
        foreach (var parts in rows)
        {
            if (parts.Length < 3) continue;
            if (!double.TryParse(parts[0], out var u) || !double.TryParse(parts[1], out var t) || t <= 0) continue;
            used += u; total += t; any = true;
            if (double.TryParse(parts[2], out var tc)) temp = temp is null ? tc : Math.Max(temp.Value, tc);
        }
        return any ? (used, total, temp) : (null, null, null);
    }

    // 把整卡那两个值并到某一页交出来的包上（模块那一行不认识显存/温度 —— 它们不是模块的数）
    StatPack WithGpu(StatPack p) => p with { VramUsedMib = _vramUsedMib, VramTotalMib = _vramTotalMib, GpuTempC = _gpuTempC };

    // 状态栏该问哪个端口：壳自己拉起的进程用实际绑定端口；外部在跑的（START-HERE 起的）用配置的服务端口
    int StrataStatsPort()
    {
        if (!ModuleRegistry.StrataEnabled(_config.Current)) return 0;
        if (_process is { HasExited: false }) return _serviceBindPort;
        return _externalRunning ? ServicePortValue() : 0;
    }

    async Task PollStrataStats()
    {
        var port = StrataStatsPort();
        if (port == 0) { ClearStrataStats(); return; }
        try
        {
            using var resp = await ProbeGetAsync($"http://127.0.0.1:{port}/metrics");
            if (!resp.IsSuccessStatusCode) { ClearStrataStats(); return; }   // 网关在但模型没加载 = 503
            _strataPack = ParseStrataMetrics(await resp.Content.ReadAsStringAsync()).ToPack();
            // MTP 只显示累计档（用户 2026-10-03 第三轮；DEC-125② 他再点名"这种就不用平均"）——本轮档解析得到但不显示
        }
        catch { ClearStrataStats(); }                                         // 超时/连不上/格式意外：不留假数字
        RefreshStatusBar();
    }

    void ClearStrataStats()
    {
        _strataPack = EmptyPack;
        RefreshStatusBar();
    }

    // 每几秒一条的进度行 → 「当前值」（预填充速率 / 解码 3 秒窗口）。返回 null = 这行不含当前值。
    internal static (string Field, double Value)? ReadLlamaStatLine(string line)
    {
        var t = StripPidPrefix(line.Trim());
        if (t.Contains("prompt processing", StringComparison.Ordinal)
            && LastNumberBefore(t, "tokens per second") is { } pp) return ("prefill", pp);
        if (t.Contains("n_gen =", StringComparison.Ordinal) && ValueAfter(t, "tg_3s = ", " ") is { } tg) return ("decode", tg);
        return null;
    }

    // 每次请求收尾打的行 → 累计「启动以来平均」用的 token 与毫秒；draft 行给接受率的分子分母。
    //   prompt eval time =   1234.56 ms /   1000 tokens (… tokens per second)
    //          eval time =   5678.90 ms /    200 tokens (… tokens per second)
    //   draft acceptance = 0.65432 (  1234 accepted /   1888 generated), mean len =  1.65
    internal static (double? PromptTokens, double? PromptMs, double? DecodeTokens, double? DecodeMs,
                     double? Accepted, double? Offered, double? LastRoundRate) ReadLlamaRequestLine(string line)
    {
        var t = StripPidPrefix(line.Trim());
        if (t.Contains("prompt eval time =", StringComparison.Ordinal))
            return (ValueAfter(t, "ms / ", " tokens"), ValueAfter(t, "prompt eval time = ", " ms"), null, null, null, null, null);
        if (t.Contains("eval time =", StringComparison.Ordinal))
            return (null, null, ValueAfter(t, "ms / ", " tokens"), ValueAfter(t, "eval time = ", " ms"), null, null, null);
        if (t.Contains("draft acceptance =", StringComparison.Ordinal))
            return (null, null, null, null, ValueAfter(t, "(", " accepted"), ValueAfter(t, "/ ", " generated"),
                    ValueAfter(t, "draft acceptance = ", " "));
        return (null, null, null, null, null, null, null);
    }

    // 收到 llama-server 的 stdout/stderr 行时顺手记一下（不动日志本身：那行照旧进界面与文件）
    void NoteLlamaStats(string line)
    {
        if (ReadLlamaStatLine(line) is { } cur)
        {
            _llamaStatsAt = DateTime.Now;
            if (cur.Field == "prefill") _llamaPrefillNow = cur.Value; else _llamaDecodeNow = cur.Value;
        }
        var req = ReadLlamaRequestLine(line);
        _llamaPromptTok += req.PromptTokens ?? 0;
        _llamaPromptMs += req.PromptMs ?? 0;
        _llamaDecodeTok += req.DecodeTokens ?? 0;
        _llamaDecodeMs += req.DecodeMs ?? 0;
        _llamaAccepted += req.Accepted ?? 0;
        _llamaOffered += req.Offered ?? 0;
    }

    // llama-server 重启 = "启动以来"重新起算（否则上一轮的累计会混进这一轮的平均里）
    void ResetLlamaStats()
    {
        _llamaPromptTok = _llamaPromptMs = _llamaDecodeTok = _llamaDecodeMs = _llamaAccepted = _llamaOffered = 0;
        _llamaPrefillNow = _llamaDecodeNow = null;
        _llamaStatsAt = DateTime.MinValue;
        ClearLlamaMetrics();
    }

    // DEC-148：`/metrics` 那一路清零（连基准一起清 —— 基准是"上一件的水表读数"，留着会给下一件算出一个假速度）
    void ClearLlamaMetrics()
    {
        _llamaPrev = null;
        _llamaMetricPack = null;
        _llamaMetricAt = DateTime.MinValue;
        _llamaMetricTag = "";
    }

    void RefreshLlamaStats()
    {
        if (!LlamaRunning && !_llamaExternal) { ResetLlamaStats(); RefreshStatusBar(); return; }
        // DEC-148：先问 `/metrics`（5 秒一拍，看得见动），日志行那一路在下面当后路
        _ = PollLlamaStats();
        if ((DateTime.Now - _llamaStatsAt).TotalSeconds > LlamaStatsTtlSeconds)
        { _llamaPrefillNow = _llamaDecodeNow = null; }               // 当前值过期只清当前，累计仍是启动以来的
        RefreshStatusBar();
    }

}
