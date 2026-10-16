using System.Diagnostics;
using System.Drawing;
using System.Text;

namespace MultiLauncher;

// Ninfer 的状态栏取数与外部实例/网关两头（原 MainForm.Ninfer.cs 的「状态栏取数：/metrics」+「外部实例与网关那两头」两段，2026-10-15 整段搬出，一行未改）
public partial class MainForm
{
    // ---------------- 状态栏取数：/metrics（实测就在引擎自己那个口上，指标名与 llama.cpp 同套） ----------------

    // DEC-125②：交出去的那一份收成一个 StatPack；"当前"档由**两次累计采样相减**得到（引擎只给水表读数，
    // 瞬时速度是壳算的）—— 以前 Ninfer 页的「当前」档一直是短横，关掉「平均」那一页就没字了。
    StatPack _ninferPack = EmptyPack;
    double? _ninferPrevPromptTok, _ninferPrevPromptSec, _ninferPrevDecTok, _ninferPrevDecSec;
    double _ninferAccepted, _ninferOffered;      // 累计计数器（日志行的 accepted/offered 也累到这里）

    /// <summary>差分 = "这一拍跑了多少 ÷ 这一拍花了多少秒"。任一档取不到、或这一拍根本没推进（≤0）都回 null = 短横，
    /// **不写 0.0**（那看着像卡死，而事实是"这段时间没在生成"）。</summary>
    internal static double? DeltaRate(double? prevCount, double? prevSecs, double? nowCount, double? nowSecs)
        => prevCount is null || prevSecs is null || nowCount is null || nowSecs is null
            ? null
            : nowCount - prevCount is > 0 and var dc && nowSecs - prevSecs is > 0 and var ds ? dc / ds : null;

    /// <summary>从 prometheus 文本里抠一枚计数器（只要第一个匹配；两侧独有项不重合，抠不到就是 null = 短横）。</summary>
    internal static double? MetricValue(string text, string name)
    {
        foreach (var raw in (text ?? "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var sp = line.LastIndexOf(' ');
            if (sp <= 0) continue;
            if (!line[..sp].StartsWith(name, StringComparison.Ordinal)) continue;
            var head = line[..sp];
            if (head.Length != name.Length && !(head[name.Length] is ' ' or '{')) continue;
            return double.TryParse(line[(sp + 1)..], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
        }
        return null;
    }

    /// <summary>三档的取数（累计平均 + 当前档由差分；MTP/投机只要累计）。**绝不自动发验活请求** ——
    /// 8 token 的探活会把 prefill 14.9 tok/s 这种统计垃圾写进日志，并污染累计计数（§4 实测表）。</summary>
    async Task PollNinferStats()
    {
        int port = NinferRunning ? _ninferActivePort : (_ninferExternal ? NinferServicePortValue() : 0);
        if (port == 0) { ResetNinferStats(); return; }
        try
        {
            using var resp = await ProbeGetAsync($"http://{ProbeHost()}:{port}/metrics");
            if (!resp.IsSuccessStatusCode) return;
            var text = await resp.Content.ReadAsStringAsync();
            if (IsDisposed) return;
            var pt = MetricValue(text, "llamacpp:prompt_tokens_total");
            var ps = MetricValue(text, "llamacpp:prompt_seconds_total");
            var dt = MetricValue(text, "llamacpp:tokens_predicted_total");
            var ds = MetricValue(text, "llamacpp:tokens_predicted_seconds_total");
            var acc = MetricValue(text, "ninfer:draft_accepted_tokens_total");
            var off = MetricValue(text, "ninfer:draft_tokens_total");
            // 先差分（拿上一拍的读数），再把这一拍存成下一拍的基准
            var prefillNow = DeltaRate(_ninferPrevPromptTok, _ninferPrevPromptSec, pt, ps);
            var decodeNow = DeltaRate(_ninferPrevDecTok, _ninferPrevDecSec, dt, ds);
            if (pt is not null && ps is not null) { _ninferPrevPromptTok = pt; _ninferPrevPromptSec = ps; }
            if (dt is not null && ds is not null) { _ninferPrevDecTok = dt; _ninferPrevDecSec = ds; }
            _ninferAccepted = acc ?? 0; _ninferOffered = off ?? 0;
            var reused = MetricValue(text, "ninfer:reused_prompt_tokens_total");   // WSL 那一代没这项 = 短横
            _ninferPack = new StatPack(
                Rate(pt, (ps ?? 0) * 1000), prefillNow,
                Rate(dt, (ds ?? 0) * 1000), decodeNow,
                _ninferOffered > 0 ? _ninferAccepted / _ninferOffered * 100 : null,
                MetricValue(text, "ninfer:requests_deferred"),                      // 本机侧没这项 = 短横
                pt is > 0 && reused is >= 0 ? reused!.Value * 100 / pt!.Value : null,
                pt, dt, null, null, null);
        }
        catch { /* 连不上 = 不动旧数，由下一拍或停止流程清 */ }
        RefreshStatusBar();
    }

    /// <summary>日志行里的第二数据源（`req#N done` / dflash2 accepted x/y）—— /metrics 拿不到时的后路。</summary>
    void NoteNinferStats(string line)
    {
        if (line.Contains("dflash2 accepted", StringComparison.OrdinalIgnoreCase)
            || line.Contains("mtp accepted", StringComparison.OrdinalIgnoreCase))
        {
            var m = System.Text.RegularExpressions.Regex.Match(line, @"accepted\s+(\d+)\s*/\s*(\d+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (m.Success && double.TryParse(m.Groups[1].Value, out var a) && double.TryParse(m.Groups[2].Value, out var o) && o > 0)
            { _ninferAccepted += a; _ninferOffered += o; }
        }
    }

    void ResetNinferStats()
    {
        _ninferAccepted = _ninferOffered = 0;
        _ninferPrevPromptTok = _ninferPrevPromptSec = _ninferPrevDecTok = _ninferPrevDecSec = null;
        _ninferPack = EmptyPack;
    }

    void RefreshNinferStats()
    {
        // 进程没了就彻底清零；还活着就不用管 TTL —— 差分本身就是"这一拍有没有在跑"，
        // 不推进的下一拍当前档自然取不到（旧数字不会装作还在跑）。
        if (!NinferRunning && !_ninferExternal) ResetNinferStats();
        RefreshStatusBar();
    }

    // ---------------- 外部实例与网关那两头 ----------------

    /// <summary>端口上有一个不是本壳拉起的 Ninfer：按形状认（它的 /v1/models 条目 owned_by = "ninfer"，
    /// 与 llama 的 "llamacpp"、Strata 的 "strata" 区分开 —— 同族指纹那套路子，别只看 200）。</summary>
    internal static bool LooksLikeNinferModelsList(string? body)
    {
        if (string.IsNullOrWhiteSpace(body) || body.Length > 1_000_000) return false;
        try
        {
            if (System.Text.Json.Nodes.JsonNode.Parse(body) is not System.Text.Json.Nodes.JsonObject o
                || o["data"] is not System.Text.Json.Nodes.JsonArray arr) return false;
            foreach (var it in arr)
                if (string.Equals(it?["owned_by"]?.GetValue<string>(), "ninfer", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
        catch { return false; }
    }

    async Task ProbeNinferExternal()
    {
        if (!ModuleRegistry.NinferEnabled(_config.Current))
        {
            if (_ninferExternal) { _ninferExternal = false; RefreshStatusUi(); }
            return;
        }
        if (_ninferProcess is not null) { if (_ninferExternal) { _ninferExternal = false; RefreshStatusUi(); } return; }
        var port = NinferServicePortValue();
        bool up;
        try
        {
            using var resp = await ProbeGetAsync($"http://{ProbeHost()}:{port}/v1/models");
            up = resp.IsSuccessStatusCode && LooksLikeNinferModelsList(await resp.Content.ReadAsStringAsync());
        }
        catch { up = false; }
        if (IsDisposed || _ninferProcess is not null) return;
        if (up != _ninferExternal)
        {
            _ninferExternal = up;
            if (up) LogN($"检测到 {ProbeHost()}:{port} 上已有 Ninfer 引擎在跑（不是本启动器拉起的）");
            RefreshStatusUi();
        }
    }

    /// <summary>网关的唤醒这一头。显存这一关按**入口**分档（DEC-083 第 6 条 / DEC-097②）：
    /// queue = 排队等（等到 GpuWakeWaitSec 上限，超时回 504 并写"在排队，前面是谁"）；
    /// reject = 直接 503。没有「不拦」那一档了。人自己点按钮那一路在 NinferPreflight 里已经当场拒绝过了。
    /// DEC-157：那一圈等待抽成三模块共用的 <see cref="GpuGateAsync"/> —— 判据仍是这一族那发 nvidia-smi 的精确账
    /// （另两族没有可靠估算，走粗判据），但"怎么等 / 等多久 / 说什么"从此只有一个家。
    /// 排队那一发仍在别的线程上问（DEC-138 C-9 后半：摆到 UI 线程就是界面冻最长 3 秒）。</summary>
    async Task NinferWakeUpstream(string tag, CancellationToken ct)
    {
        var need = await UiAsync(() => NinferNeedGiBFor(tag));
        if (need is { } n)
            await GpuGateAsync(NinferKey, LogN, () => GpuShortMessage(n), ct);
        ct.ThrowIfCancellationRequested();     // DEC-160：排到了但对面已经走了 = 这一件不起
        var started = await UiAsync(() =>
        {
            // DEC-102：显式喂"请求要的那一件" —— 别动界面选中，也别光改 _config.Current（会被 CollectSettings 顶掉）
            // DEC-138（C-9 后半）：上面那一圈已经在后台问过显存了（问过 = gpuProbed，结论必然是"够"才走到这里）
            StartNinferServer(tag, wakeFromSection: true, gpuVerdict: null, gpuProbed: need is not null);
            return (Running: NinferRunning, Why: NinferStartFailure);
        });
        // DEC-159（他批的第 1 条）：这一族起不来的那一句**原样交给客户端**（不再只说"上面几行日志写了原因"——
        // 远端那边没有人会去翻他屏幕上的日志），也不再弹模态框（弹窗只归人点的那一击，见 FailNinfer 的 modal）。
        if (!started.Running) throw new InvalidOperationException(NinferWakeFailureLine(started.Why));
        if (_ninferReadyTcs is null) throw new InvalidOperationException("启动没有建立就绪信号");
    }

    /// <summary>这一件起来大约要吃多少显存（GiB）；段读不到、或**件字节量不到**就返回 null = 不显示也不判
    ///（DEC-067「判不了就当没这条校验」）。下拉那格的占用估算与起前那道账共用它。
    /// 2026-10-12 两处口径改动：① 池与精度按**这一段自己**的 `kv-capacity` / `kv-dtype` 取 —— 下拉同时列着
    /// 两侧的件，而界面上那格只有一个值（跟着激活后端走），按界面值算必然错一边；② WSL 那侧的权重改用
    /// 那趟 find 带回来的真实字节数（以前恒 0 → 落到 14.0 那个占位，实测少算约 3 GiB）。</summary>
    internal double? NinferNeedGiBFor(string tag)
    {
        var sec = ReadNinferSections().FirstOrDefault(x => string.Equals(x.Name, tag, StringComparison.OrdinalIgnoreCase));
        if (sec is null) return null;
        var s = _config.Current;
        var backend = SecBackend(sec) ?? ActiveNinferBackend(s);
        long bytes = 0;
        try
        {
            if (backend == NinferIni.BackendWin)
            {
                var path = NinferWinArtifactPath(s, sec.File);
                if (path.Length > 0 && File.Exists(path)) bytes = new FileInfo(path).Length;
            }
            else bytes = _ninferWslFiles.FirstOrDefault(f => string.Equals(f.File, sec.File, StringComparison.Ordinal)).Bytes;
        }
        catch { }
        if (bytes <= 0) return null;      // 件不在 / WSL 那侧还没扫到 = 这一格空着（与升级前同形），这条也不判
        var toks = NinferIni.Split(sec.Args);
        long pool = long.TryParse(NinferIni.FlagValue(toks, "kv-capacity"), out var p) ? p : 55296;
        var dtype = NinferIni.FlagValue(toks, "kv-dtype") is { Length: > 0 } d ? d : backend == NinferIni.BackendWsl ? "rk4v4-e8" : "k8v4";
        return NinferGpuNeedGiB(bytes, pool, dtype);
    }

    /// <summary>显存够不够（够 = null）。判不了（nvidia-smi 不在/超时）也返回 null = 按"没这条校验"处理。</summary>
    /// <summary>DEC-138（C-9 后半）：某一件的显存那一发**先问出来**（调用方在后台线程上调它，
    /// 再把结果喂给 <see cref="NinferPreflight"/> 的 gpuVerdict）。null = 够 / 判不了 / 这一件没量。</summary>
    internal string? NinferGpuVerdictFor(string tag)
    {
        var need = NinferNeedGiBFor(tag);
        return need is null ? null : GpuShortMessage(need.Value);
    }

    internal string? GpuShortMessage(double needGiB)
    {
        var gpu = QueryGpuMemory();
        if (gpu is null) return null;
        double free = (gpu.Value.TotalMib - gpu.Value.UsedMib) / 1024.0;
        if (free >= needGiB) return null;
        var who = NinferOccupants();
        return $"需要约 {needGiB:0.0} GiB，此刻只剩 {free:0.0} GiB" +
               (who.Length > 0 ? $"（占着的是：{who}）" : "（占着的是别的东西）");
    }

    /// <summary>排队上限的纯判据：默认 300、只认 30~600，表外一律回默认（自检直接喂串）。
    /// DEC-157 起三模块共用这一档等待上限（旧名 NinferWakeWaitSeconds）。</summary>
    internal static int GpuWakeWaitSeconds(string? text) =>
        int.TryParse((text ?? "").Trim(), out var v) && v is >= 30 and <= 600 ? v : 300;

    /// <summary>排队上限（秒）：设置里那一格，默认 300、可配到 600（DEC-083 第 6 条；DEC-157 起三模块共用）。</summary>
    internal int WakeWaitSeconds() => GpuWakeWaitSeconds(_config.Current.GpuWakeWaitSec);

    /// <summary>Ninfer 的 /v1/models 要**列全三件**（与 Strata 那条"只列已就绪"相反，DEC-083：
    /// 客户端切会话时得看得见全部 model-id，否则未知名会被引擎秒拒 404）。</summary>
    internal static string BuildNinferModelsJson(IEnumerable<ModelEntry> models)
    {
        var arr = new System.Text.Json.Nodes.JsonArray();
        long created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (var m in models)
            arr.Add(new System.Text.Json.Nodes.JsonObject
            {
                ["id"] = m.Tag, ["object"] = "model", ["created"] = created, ["owned_by"] = "ninfer",
            });
        return new System.Text.Json.Nodes.JsonObject { ["object"] = "list", ["data"] = arr }
            .ToJsonString(new System.Text.Json.JsonSerializerOptions
            { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

}
