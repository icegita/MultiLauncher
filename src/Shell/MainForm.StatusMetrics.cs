using System.Globalization;
using System.Text.Json.Nodes;

namespace MultiLauncher;

// DEC-148 llama 页实时取数（router 的 /metrics，autoload=false）+ DEC-128① 设置页实时预览。
// 2026-10-15 从 MainForm.StatusBar.cs 整段搬出，一行未改。
public partial class MainForm
{
    // ---------------- DEC-148：llama 页的实时取数 = router 的 /metrics（autoload=false，绝不吵醒模型） ----------------
    // 为什么以前只有日志行（DEC-038/039/040）：路由模式下 `/metrics` 必须带 `?model=`，而带 model 的 GET 会把那件真加载起来。
    // b11530 源码里这一条有解 = `is_autoload()` 认查询参数 `autoload`（`server-models.cpp:1873`），
    // `router_validate_model()` 在"autoload=false 且那件没在跑"时直接回 400「model is not loaded」（:1866），一个进程都不起。
    // 本机实测（2026-10-11，他的 router 8078）：问一件 unloaded 的 → 400，之后 `/v1/models` 里它照旧 unloaded = 真没被吵醒。
    // 那件在跑时 `proxy_request` 把请求转给它自己的子实例，拿到 `llamacpp:*` 全套计数器 —— 与 Ninfer 页同一族取数：
    // 平均 = 累计读数，当前 = 两次读数相减（`DeltaRate`）。
    // 顺带补上 llama 页以前**永远短横**的两档：队列 = `requests_deferred`、缓存命中 = `prompt_tokens_cached_total` 占比
    //（DEC-038/039/040 那句"拿不到"从今天起只对"没在跑的件"成立）。
    /// <summary>这一拍该问哪一件：选中那件在跑就选它，否则取在跑的任一件，都没在跑 = 空（不打、全短横）。</summary>
    internal static string LlamaStatsModel(IReadOnlyCollection<string> loaded, string selected)
    {
        foreach (var t in loaded)
            if (string.Equals(t, selected, StringComparison.OrdinalIgnoreCase)) return t;
        return loaded.Count > 0 ? loaded.First() : "";
    }

    /// <summary>那一发的 URL 只有一个家（绘制与自检共用）。`autoload=false` 是这条链的**硬护栏**：
    /// 少了它，状态栏每 5 秒就把"下拉里选中但那件没在跑"的模型真加载起来（几十 GB 显存 + 一次唤醒）。</summary>
    internal static string LlamaMetricsUrl(string host, int port, string tag)
        => $"http://{host}:{port}/metrics?model={Uri.EscapeDataString(tag)}&autoload=false";

    /// <summary>`/metrics` 那一路要的水表读数（缺哪项哪项 null = 那一档短横，口径同 DEC-067）。</summary>
    internal readonly record struct LlamaCounters(
        double? PromptTok, double? PromptCached, double? PromptSec, double? DecTok, double? DecSec,
        double? Accepted, double? Draft, double? Deferred)
    {
        internal static LlamaCounters Parse(string text) => new(
            MetricValue(text, "llamacpp:prompt_tokens_total"),
            MetricValue(text, "llamacpp:prompt_tokens_cached_total"),
            MetricValue(text, "llamacpp:prompt_seconds_total"),
            MetricValue(text, "llamacpp:tokens_predicted_total"),
            MetricValue(text, "llamacpp:tokens_predicted_seconds_total"),
            MetricValue(text, "llamacpp:spec_decode_num_accepted_tokens_total"),
            MetricValue(text, "llamacpp:spec_decode_num_draft_tokens_total"),
            MetricValue(text, "llamacpp:requests_deferred"));
    }

    /// <summary>两拍读数 → 状态栏那一份包。`prev` = null（第一拍 / 换件 / 刚清零）→ 「当前」档给短横，
    /// 不写 0 装成卡死（DEC-127① 那一族 + `DeltaRate` 的口径）。
    /// 输入总量 = 真读进去的 + 缓存复用的（`prompt_tokens_total` 明写"excluding cached"，只报前者会把"输入"报小）。</summary>
    internal static StatPack LlamaMetricsPack(LlamaCounters c, LlamaCounters? prev)
    {
        double? tokIn = c.PromptTok is null && c.PromptCached is null
            ? null : (c.PromptTok ?? 0) + (c.PromptCached ?? 0);
        return new StatPack(
            Rate(c.PromptTok, (c.PromptSec ?? 0) * 1000),
            prev is { } p ? DeltaRate(p.PromptTok, p.PromptSec, c.PromptTok, c.PromptSec) : null,
            Rate(c.DecTok, (c.DecSec ?? 0) * 1000),
            prev is { } q ? DeltaRate(q.DecTok, q.DecSec, c.DecTok, c.DecSec) : null,
            Pct(c.Accepted, c.Draft),
            c.Deferred,
            tokIn is > 0 && c.PromptCached is >= 0 ? c.PromptCached!.Value * 100 / tokIn!.Value : null,
            tokIn, c.DecTok, null, null, null);
    }

    async Task PollLlamaStats()
    {
        _llamaMetricsAttempts++;                      // 证人位：这条真路径被走到过（下面两条挡下的路也算）
        int port = LlamaRunning ? _llamaActivePort : (_llamaExternal ? LlamaPortValue() : 0);
        var tag = LlamaStatsModel(_llamaLoaded, _config.Current.LlamaModel);
        if (port == 0 || tag.Length == 0) { ClearLlamaMetrics(); return; }   // 没服务 / 没件在跑 = 全短横
        _llamaMetricsAsked++;                         // 证人位：真发了一发（护栏挡下"没在跑就不问"这一枪要分得开）
        try
        {
            using var resp = await ProbeGetAsync(LlamaMetricsUrl(ProbeHost(), port, tag));
            if (!resp.IsSuccessStatusCode) { ClearLlamaMetrics(); return; }  // 400 = 那件没在跑（护栏在生效的形状）
            var text = await resp.Content.ReadAsStringAsync();
            if (IsDisposed) return;
            var c = LlamaCounters.Parse(text);
            var prev = _llamaMetricTag == tag ? _llamaPrev : null;           // 换件 = 换子实例 = 基准作废
            _llamaMetricPack = LlamaMetricsPack(c, prev);
            _llamaPrev = c;
            _llamaMetricTag = tag;
            _llamaMetricAt = DateTime.Now;
        }
        catch { ClearLlamaMetrics(); }                                       // 连不上 = 不留假数
    }

    /// <summary>llama 侧那份包：**有新鲜 `/metrics` 读数就用它**（DEC-148，5 秒一拍、看得见动），
    /// 日志行那一路留作后路（老版引擎没这项 / 子实例没起来 / 连不上）。
    /// 后路那两个数（`_llamaPromptTok` / `_llamaDecodeTok`）是 `double` 累加器、初值 0 ——
    /// **0 要当"没收到数据"交 null**（DEC-127①：整档一个数都没取到 = 只出短横）；
    /// 以前直接交出去，llama 页那两格就永远写着「输入 0·输出 0」，而另两页写短横（他实跑点名）。</summary>
    internal StatPack LlamaPack()
    {
        if (_llamaMetricPack is { } m && (DateTime.Now - _llamaMetricAt).TotalSeconds <= LlamaStatsTtlSeconds)
            return m;
        return LlamaLogPack();
    }

    /// <summary>后路那一份：它没有 /metrics 可问时的老形状（DEC-038/039/040）。
    /// DEC-125②：输入/输出 token 总量 = 壳从每次请求收尾行累加的那两个数（"本次服务启动至今"，进程一退就归零）。
    /// "换模型了也不算"那条靠 `RefreshLlamaStatesAsync` 里加载集合变了就清零（这一族引擎进程不换，只有集合会换）。</summary>
    internal StatPack LlamaLogPack() => new(
        Rate(_llamaPromptTok, _llamaPromptMs), _llamaPrefillNow,
        Rate(_llamaDecodeTok, _llamaDecodeMs), _llamaDecodeNow,
        _llamaOffered > 0 ? _llamaAccepted / _llamaOffered * 100 : null,
        null, null,
        _llamaPromptTok > 0 ? _llamaPromptTok : null,
        _llamaDecodeTok > 0 ? _llamaDecodeTok : null,
        null, null, null);


    static double? Rate(double? tokens, double? ms) =>
        tokens is > 0 && ms is > 0 ? tokens!.Value / ms!.Value * 1000 : null;

    // ---------------- DEC-128①：设置页的"实时预览" ----------------
    // 他点名："勾选取消状态栏显示参数或调整顺序时，应实时渲染到状态栏上，方便查看效果；调整完毕不保存则回到
    // 之前的状态，保存则生效。" 预览只有一个家 = 这里：设置页把"拟生效"的四样塞进来（含主开关），关窗时清掉；
    // 显隐（含条带占位）、文字、显存采样三处统一吃下面这个 EffectiveStatusBar，别各拼各的。
    internal readonly record struct StatusBarPreviewState(bool Visible, string Items, string Avg, string Order);
    StatusBarPreviewState? _statusBarPreview;
    internal void SetStatusBarPreview(bool visible, string items, string avg, string order)
    {
        _statusBarPreview = new StatusBarPreviewState(visible, items, avg, order);
        RefreshStatusBar();
    }
    internal void ClearStatusBarPreview()
    {
        if (_statusBarPreview is null) return;
        _statusBarPreview = null;
        RefreshStatusBar();
    }

    /// <summary>生效值 = 预览中的那一份（设置页开着）?? 磁盘上那一份 —— 渲染与显存采样共用这一条。</summary>
    (bool Vis, string Items, string Avg, string Order) EffectiveStatusBar()
        => _statusBarPreview is { } p ? (p.Visible, p.Items, p.Avg, p.Order)
                                      : (StatusBarVisible(_config.Current.ShowStatusBar),
                                         _config.Current.StatusBarItems, _config.Current.StatusBarAvg,
                                         _config.Current.StatusBarOrder);

    void RefreshStatusBar()
    {
        if (_statusBar is null || IsDisposed) return;
        // 显隐的唯一判据是设置里的开关（用户 2026-10-03：开了就该**立刻**有区域和文字——没数据也显示占位短横，
        // 不能"没文字就折叠"）。服务在不在跑只影响取数：不在跑时各档取不到 → 全是 `-`。
        // DEC-125②：再叠一条「有没有可显示的档」= 一项都不勾时没有字可写，整条连区域一起收（与关掉开关同档，
        // 不是"留一条空带子"）。DEC-128①：判据全走 EffectiveStatusBar（预览期 = 拟生效的那份）。
        // DEC-128②：次序 = 九行全量视觉次序（`StatusBarOrder`；老 ini 没这一键就按 Items 兜底 = 升级前那一套）
        // —— "只勾平均"的行也有自己的位置；勾没勾都吃这一条。
        var (vis, itemsCsv, avgCsv, orderCsv) = EffectiveStatusBar();
        if (!vis) { _statusBar.SetParts(EmptyParts); return; }
        var order = StatusBarRowOrder(orderCsv, itemsCsv);
        var parts = StatusBarSegments(ActiveView.StatsValues(), order,
                                      StatusBarIdSet(itemsCsv), StatusBarAvgSet(avgCsv));
        _statusBar.SetParts(parts.Count == 0 ? EmptyParts : parts);   // 一项都不勾 = 没字可写 = 整条连区域一起收
    }

    static readonly List<(string Label, string Value)> EmptyParts = new();

    // 显隐判据（纯函数，自检可直接断言）：只认开关——开 = 条区 + 文字（没数据也是占位短横），关 = 整条收起。
    // 曾经还叠加"服务在不在跑"，导致开了开关却要等服务起来才出现区域（用户 2026-10-03 纠正）。
    internal static bool StatusBarVisible(bool switchOn) => switchOn;

}
