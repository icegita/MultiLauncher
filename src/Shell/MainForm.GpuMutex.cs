namespace MultiLauncher;

// ============================================================================
// DEC-157：跨模块显存互斥 —— 三道"真要把模型读进显存"的路上共用的那一半
// ============================================================================
// 为什么要有这一档（他 2026-10-12 实跑抓的）：ninfer/WIN 那件已经加载时，请求打到 Strata 的公开口，
// 壳照样把件读进显存 → 24 GB 的卡上两件同时驻留。设计稿支点 I 从第一天就写的是**三模块一视同仁**，
// 但 DEC-096~098 施工时只把判据装在 Ninfer 自己那一族里（`GpuShortMessage` 那一发 nvidia-smi 账），
// Strata 与 llama 两头一个字节都没问过这张卡 = 那条路从来没有这道闸。
//
// 两头判据不同、各有依据，不许互相抄：
//   · Ninfer 有精确估算（件字节 + 段自己那枚 `kv-capacity` × 档表，DEC-156）→ 它照旧问 `memory.free`；
//   · Strata / llama 没有可靠的"这一件要吃多少"（要新造两族算法，与他定的"能复用就复用"相冲）
//     → 这一档走**粗判据** = 本壳起的另一枚**真把模型读进显存**的那一件在跑 = 这张卡已被占。
//
// "在跑"的口径（他当场确认的那一条）= **有模型已加载**，不是"进程存在"：
//   · Strata = 服务进程活着（这一族只在真加载模型时才有进程；空闲停服后进程退出、只剩网关 = 不占）；
//   · llama  = 进程活着**且** router 报了已加载件；单体模式（没路由）连模型一起起 = 也算；
//   · Ninfer = 引擎进程活着（`NinferRunning`；只把网关立起来那一档不占显存，DEC-100①）。
//
// 分入口口径一字未改（DEC-083 第 6 条 / DEC-097②）：**人点按钮 = 当场拒绝并点名**（走 `error` / 弹窗那一条，
// 绝不静默等待）；**请求从公开口进来 = 排队等**（`GpuMutex` = queue，上限 `GpuWakeWaitSec`，等满回 504 并说清
// 前面是谁；`reject` = 立刻 503）。**本壳不替用户停别人的进程**，也不代停另一模块那一件 —— 让位归「卸载」/
// 「停止」或那一族的空闲停服。
public partial class MainForm
{
    /// <summary>卡上一枚"真把模型读进显存"的件（Key = 模块键，What = 说给人听的那一句身份）。</summary>
    internal readonly record struct GpuHolder(string Key, string What);

    /// <summary>自检与探针用的假占用表：非 null 时 <see cref="SnapshotGpuHolders"/> 交这份 ——
    /// 真起三枚进程才能拍出这张表（要占他的显存），所以这一族按既有纪律"喂样本走真路径"（DEC-126 那条硬规矩）。</summary>
    internal static List<GpuHolder>? GpuHoldersForTest;

    /// <summary>读本壳台账：此刻哪几枚真在显存里（只在界面线程上调 —— llama 那份集合由轮询在 UI 线程维护）。</summary>
    internal List<GpuHolder> SnapshotGpuHolders()
    {
        if (GpuHoldersForTest is { } fed) return fed;
        var list = new List<GpuHolder>();
        if (_process is { HasExited: false })
            list.Add(new GpuHolder(StrataKey, "Strata 的服务（" + (_runningTag.Length > 0 ? _runningTag : "模型加载中") + "）"));
        if (_llamaProcess is { HasExited: false })
        {
            // 路由模式下 router 空跑（一件都没加载）不占大头 = 不该拦另一族；单体模式那一下连模型一起起 = 占
            var loaded = string.Join("、", _llamaLoaded.OrderBy(x => x));
            if (loaded.Length > 0) list.Add(new GpuHolder(LlamaKey, "llama.cpp 的件（" + loaded + "）"));
            else if (!_routeModeLlama) list.Add(new GpuHolder(LlamaKey, "llama.cpp 的单体服务"));
        }
        if (NinferRunning)
            list.Add(new GpuHolder(NinferKey, "Ninfer 的引擎（" + NinferFaceLabel(_ninferRunningBackend) + " · " + _ninferRunningTag + "）"));
        return list;
    }

    /// <summary>纯判据 + 句子唯一家：除自己之外还有谁占着。null = 可以起。
    /// 自己那一枚要摘掉 —— Ninfer 起另一件时它自己在跑是"停旧起新"（同一模块内切件），不是抢卡。</summary>
    internal static string? GpuHeldByOther(string wantingKey, IReadOnlyList<GpuHolder> holders)
    {
        var others = holders
            .Where(h => !string.Equals(h.Key, wantingKey, StringComparison.Ordinal))
            .Select(h => h.What).ToList();
        if (others.Count == 0) return null;
        return "这张卡上此刻正跑着别的一枚：" + string.Join("、", others) +
               "。\n任一时刻只能有一件把模型读进显存。要让它让位：到那一页点「卸载」/「停止」，或者等它的空闲停服把显存还回来。";
    }

    /// <summary>排队等满那一句（Ninfer 的 504 与另两族共用这一个家）。</summary>
    internal static string GpuWakeTimeoutLine(string occupantLine) =>
        "在排队等显存，排到超时还没轮到：" + occupantLine;

    /// <summary>开始排队那一句（界面与日志要说清"在排队，前面是谁"，别让客户端以为是卡死 —— 支点 I 那条局限）。
    /// 只取判据那句的**第一行**：粗判据那句第二行是"怎么让它让位"的交代，等满 504 时才需要给人看全。</summary>
    internal static string GpuWaitingLine(string occupantLine) =>
        "在排队等显存：" + occupantLine.Split('\n')[0].TrimEnd('。');

    /// <summary>点按钮那一路：当场拒绝并点名（怎么把这句话交出去归调用方 —— Strata 走 <c>error</c>、llama 走 FailLlama、
    /// Ninfer 走 preflight 那条已有的）。台账空 = 一张卡都没占 = 返回 null 放行（口径同 DEC-067"判不了就当没这条校验"）。</summary>
    internal string? GpuMutexRefusal(string selfKey) => GpuHeldByOther(selfKey, SnapshotGpuHolders());

    /// <summary>网关等**这一次唤醒**的总时限（DEC-159 收成一家）。为什么要有这一条：
    /// 原来 `StartGateway` 里写死 `TimeSpan.FromSeconds(300)`，而排队上限可以配到 600 ——
    /// 于是设 301~600 时**先吃的是那句笼统的「等待模型就绪超过 300 秒」**，我们那句点名"前面是谁"的 504 永远到不了客户端；
    /// 设成正好 300 就是掷硬币（同一份配置两次跑出两种文案）。
    /// 口径 = **总时限必须比排队上限长一截**（余量 60 秒：排到之后还要真去起进程并等 ready），
    /// 同时不下于 300 秒（那是历史值，冷启动本来就可能要那么久，别把它缩成新的失败）。
    /// 纯函数，自检直接喂三档；调用点只有一个（<see cref="StartGateway"/>），别再各处写死 300。</summary>
    internal static int GatewayWakeTimeoutSeconds(int queueCapSeconds) => Math.Max(300, queueCapSeconds + 60);

    /// <summary>「排队等显存上限(秒)」那一格的形状判据（DEC-159）：只认 30~600 的整数，别的在设置页保存时就拦下 ——
    /// 口径同 <see cref="GpuMutexProblem"/>：填错会让那一档静默回默认（"设了不生效"最难查）。</summary>
    internal static string? GpuWakeWaitProblem(Settings s)
    {
        var t = (s.GpuWakeWaitSec ?? "").Trim();
        if (!int.TryParse(t, out var v)) return "「排队等显存上限(秒)」要填一个整数（秒）。";
        if (v < 30 || v > 600) return "「排队等显存上限(秒)」只认 30~600：更短还没排到就先别排，更长客户端早就自己断了。";
        return null;
    }

    /// <summary>DEC-160 的喂样本口：那一圈等待读的仍是 `<see cref="_config"/>.Current` 这两个键（同一个家），
    /// 自检把样本摆进去再断"取消=收手"，**不另开一条按参数走的入口** ——
    /// 那两枚是他能在设置页改的值，"他 ini 里恰好写着什么"不该决定用例红绿（AGENTS 那条硬规矩②）。</summary>
    internal void SetGpuMutexForTest(string mode, int waitSeconds)
    {
        _config.Current.GpuMutex = mode;
        _config.Current.GpuWakeWaitSec = waitSeconds.ToString();
    }

    /// <summary>网关唤醒那一路的排队圈（在网关线程上调：要么用别的线程问出来的判据，要么用 <see cref="UiAsync"/> 取台账）。
    /// 档 / 等待上限 / 那两句文案全与 Ninfer 那一圈同源：三族**判据**不同，但"怎么等、等多久、说什么"只有一个家。
    /// <paramref name="offThreadProbe"/> = 调用方在**别的线程**问出来的判据（Ninfer 那一发 nvidia-smi 最长 3 秒，
    /// 摆到 UI 线程上就是界面冻一下 —— DEC-138 C-9 后半治的就是这个）；不给就用粗判据（只读本壳台账，很便宜）。
    /// <paramref name="ct"/> = **这一发请求的**取消令牌（DEC-160）：客户端走了 / 这一发的总时限到点 = 立刻收手，
    /// 排队不再"替一个已经拿到 504 的客户端把件读进显存"（第 36/37 轮登记的那条幽灵起件）。</summary>
    internal async Task GpuGateAsync(string selfKey, Action<string> log, Func<string?>? offThreadProbe = null,
                                     CancellationToken ct = default)
    {
        var mode = GpuMutexMode(_config.Current);
        var until = DateTime.UtcNow.AddSeconds(WakeWaitSeconds());
        var said = false;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var msg = offThreadProbe is not null ? offThreadProbe() : await UiAsync(() => GpuMutexRefusal(selfKey));
            if (msg is null) return;
            if (!said) { said = true; if (mode == "queue") log(GpuWaitingLine(msg)); }
            if (mode != "queue") throw new GatewayRequestException(503, msg);
            if (DateTime.UtcNow >= until) throw new GatewayRequestException(504, GpuWakeTimeoutLine(msg));
            try { await Task.Delay(1000, ct); }
            // DEC-160：这一发没人等了 = 往上抛（调用方因此不会去起进程），别学旧写法"吞掉当放行"
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { return; }
        }
    }
}
