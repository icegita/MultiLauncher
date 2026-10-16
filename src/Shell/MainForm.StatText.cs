using System.Globalization;
using System.Text.Json.Nodes;

namespace MultiLauncher;

// 状态栏的解析与文案（纯函数，自检可直接喂样本）。
// 2026-10-15 从 MainForm.StatusBar.cs 整段搬出，一行未改。
public partial class MainForm
{
    // ---------------- 解析与文案（纯函数，自检可直接喂样本） ----------------

    // /metrics 的 JSON → 状态栏能拿到的全部值。字段缺失、为 null、形状变了都只回 null（显示短横），不抛。
    //   平均：totals（进程启动以来累计）——预填充只算真读进去的部分（prompt_tokens - reused）
    //   当前：live.prefill_tok_s_mean（本轮预填充）、live.tok_s（引擎的近 2 秒窗口，缺失退 tok_s_mean）、
    //         requests[0] 的草稿数（最近一次已完成请求的接受率）
    //   DEC-125② 新增：live.queued（队列）、totals.reused / prompt_tokens（缓存命中率）、
    //         totals.prompt_tokens / output_tokens（本次服务启动至今的输入/输出 token 总量 ——
    //         引擎进程一起来就归零，所以"超时卸载后不算 / 换模型（停旧起新）也不算"这条天然成立）
    internal readonly record struct StrataMetrics(
        double? PrefillAvg, double? PrefillNow, double? DecodeAvg, double? DecodeNow,
        double? AcceptAvg, double? AcceptNow, double? Queue, double? CacheHitPct, double? TokIn, double? TokOut)
    {
        /// <summary>交给状态栏的那一份（MTP 走累计档；本轮档留在这儿但不显示）。</summary>
        internal StatPack ToPack() => new(PrefillAvg, PrefillNow, DecodeAvg, DecodeNow, AcceptAvg,
                                          Queue, CacheHitPct, TokIn, TokOut, null, null, null);
    }

    internal static StrataMetrics ParseStrataMetrics(string json)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root) return NoMetrics;
            var live = root["live"] as JsonObject;
            var totals = root["totals"] as JsonObject;
            var prefillAvg = Rate(ReadOn(totals, "prompt_tokens") - ReadOn(totals, "reused"), ReadOn(totals, "prompt_ms"));
            var decodeAvg = Rate(ReadOn(totals, "output_tokens"), ReadOn(totals, "decode_ms"));
            var last = (root["requests"] as JsonArray)?[0] as JsonObject;
            var promptTok = Num(totals?["prompt_tokens"]);
            var reused = Num(totals?["reused"]);
            return new StrataMetrics(
                prefillAvg, Num(live?["prefill_tok_s_mean"]),
                decodeAvg, Num(live?["tok_s"]) ?? Num(live?["tok_s_mean"]),
                Pct(ReadOn(totals, "drafts_accepted"), ReadOn(totals, "drafts_offered")),
                Pct(ReadOn(last, "drafts_accepted"), ReadOn(last, "drafts_offered")),
                Num(live?["queued"]),
                promptTok is > 0 && reused is >= 0 ? reused!.Value * 100 / promptTok!.Value : null,
                promptTok, Num(totals?["output_tokens"]));
        }
        catch { return NoMetrics; }
    }

    static readonly StrataMetrics NoMetrics = new(null, null, null, null, null, null, null, null, null, null);

    static double ReadOn(JsonObject? o, string key) => Num(o?[key]) ?? 0;

    static double? Num(JsonNode? n)
    {
        try { return n?.GetValue<double>(); } catch { return null; }   // null 节点 / 字符串值都算取不到
    }

    // 速率 = token / 毫秒 × 1000；接受率 = 分子/分母 × 100。分母为 0 或分子取不到都算没有
    static double? Pct(double? num, double? den) => num is >= 0 && den is > 0 ? num!.Value / den!.Value * 100 : null;

    // 三档里前两档是「平均|当前」两值；**MTP 只要累计一个值**（用户 2026-10-03 第三轮：`MTP就只需要累计值`，
    // DEC-125② 他再点名"这种就不用平均"）。取不到的那档用短横 "-"（长横杠太占位），
    // 且**整档都没有数时不写单位**（DEC-127①：他点名"没有数值的时候,不显示单位,如 t/s,%等"）。
    // 文案算法只有一个家 = 上面那张 StatItemDefs 表 + StatusBarSegments（旧的五参数版已并进去，见 StatusBarText 重载）。
    static string Fmt(double? v, string fmt) => v is null ? Dash
        : v.Value >= 1000 ? v.Value.ToString("N0", CultureInfo.CurrentCulture)
        : v.Value.ToString(fmt, CultureInfo.CurrentCulture);

    // "… t = 3.20 s / 1280.00 tokens per second"：marker 前最后那串数字
    static double? LastNumberBefore(string s, string marker)
    {
        int i = s.IndexOf(marker, StringComparison.Ordinal);
        if (i < 0) return null;
        int e = i;
        while (e > 0 && s[e - 1] == ' ') e--;
        int b = e;
        while (b > 0 && (char.IsDigit(s[b - 1]) || s[b - 1] == '.' || s[b - 1] == ',')) b--;
        if (b == e) return null;
        var token = s[b..e].Replace(",", "");
        return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    // "tg_3s = 47.10 t/s"：marker 后、until 前的那串数字
    static double? ValueAfter(string s, string marker, string until)
    {
        int i = s.IndexOf(marker, StringComparison.Ordinal);
        if (i < 0) return null;
        var rest = s[(i + marker.Length)..].TrimStart();
        int j = rest.IndexOf(until, StringComparison.Ordinal);
        var token = (j < 0 ? rest : rest[..j]).Replace(",", "").Trim();
        return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    // 自检取态用
    internal string StatusBarTextForTest => _statusBar?.StatusForTest ?? "";
    internal bool StatusBarShownForTest => _statusBar?.Visible ?? false;
    internal int StatusBarHeightForTest => _statusBar?.Height ?? 0;
}

// 自绘的一行：**右对齐**、无分隔线、矮条（用户 2026-10-03 反馈：不要分割线、高度太高、空白太多、文字全部靠最右）。
// 标签用弱化色、数值用亮色。不用原生 StatusStrip——它是浅色系统控件，贴在这套深色自绘界面里会花
// （本项目纪律：纯 WinForms 自绘、零第三方依赖）。
sealed class UiStatusBar : Control
{
    // 高度不再写死：MainForm.LayoutStatusBar 把这条控件铺满"窗口底部的那条留白带"（窗体 Padding.Bottom），
    // 文字在这条带子里 VerticalCenter —— 上面的卡片底边=条子上边、条子下边=窗口底边，都不留多余的空白带。
    readonly Font _f = new("Microsoft YaHei UI", 8.5f);
    List<(string Label, string Value)> _parts = new();

    // 宽度只量"贴字的盒子"（NoPadding）；默认 MeasureText 会把行距空白也算进去（8.5pt 量出 17px，实际字形 ~12px），
    // 用那个数居中就会把文字顶到条子上沿 —— 用户截图量的就是这个偏差（偏上）。
    const TextFormatFlags Flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.Left
        | TextFormatFlags.VerticalCenter;

    public UiStatusBar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = UiTheme.Window;
        Dock = DockStyle.None;      // 位置由 MainForm.LayoutStatusBar 显式给：占满窗口底部那条留白带
        TabStop = false;
    }

    // DEC-138（F-5）：`_f` 是这一族**自己 new 的**（不是 UiTheme 那把共享的），所以必须在这里点名释放 ——
    // WinForms 只拆控件树，不会替你还 Font。形状同 ThinProgress 的 Timer / TinyX 的 ToolTip（AGENTS 那一行）。
    protected override void Dispose(bool disposing)
    {
        if (disposing) _f.Dispose();
        base.Dispose(disposing);
    }

    // DEC-125②：控件不再从整串文案里"抠"各段的值（那是文案算法之外的第二个家 —— 项一多、标签重名就会抠错）。
    // 现在由 MainForm 按那张表算好「标签 + 值」段交进来，控件只管排版与配色。
    internal string StatusForTest => MainForm.StatusBarJoinText(_parts);
    internal int PartCountForTest => _parts.Count;
    internal int InkHeightForTest => TextRenderer.MeasureText("国预Ag", _f, new Size(9999, 999),
        TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Height;

    public void SetParts(List<(string Label, string Value)> parts)
    {
        var joined = MainForm.StatusBarJoinText(parts);
        if (string.Equals(StatusForTest, joined, StringComparison.Ordinal) && parts.Count == _parts.Count) return;
        _parts = parts;
        bool vis = parts.Count > 0;           // 开关关掉 / 一项都没勾 -> 整条不显示（不是只藏文字）
        Visible = vis;
        // 通知宿主：显隐要连"底部留白"一起变（显示 = 多让出 StatusBarLogicalHeight 作为条区）。
        // 传 vis 而不是 Visible —— 窗体自身没显示时 Visible 恒为 false（父链生效），不能用它当意图。
        (Parent as MainForm)?.StatusBarBandChanged(vis);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        if (_parts.Count == 0) return;

        // 每档两段：标签（弱）+ 值（亮），档之间一个点（弱）。先量总宽，再从右边往左画 = 整条右对齐
        var runs = new List<(string Text, Color Color)>();
        for (int i = 0; i < _parts.Count; i++)
        {
            if (i > 0) runs.Add(("·", UiTheme.TextMuted));
            runs.Add((_parts[i].Label, UiTheme.TextMuted));
            runs.Add((_parts[i].Value, UiTheme.Text));
        }
        int total = 0, gap = UiTheme.S(this, 4);
        foreach (var (text, _) in runs) total += Ink(text).Width;
        total += gap * (runs.Count - 1);
        int x = Math.Max(UiTheme.S(this, 4), Width - UiTheme.S(this, 16) - total);   // 右缘与卡片右缘对齐（外层留白 16）
        foreach (var (text, color) in runs)
        {
            int w = Ink(text).Width;
            // 每段都在"整条高度"这个矩形里垂直居中：不靠手算 y，DPI 换算是唯一变量
            TextRenderer.DrawText(g, text, _f, new Rectangle(x, 0, w + UiTheme.S(this, 2), Height), color, Flags);
            x += w + gap;
        }
    }

    Size Ink(string text) => TextRenderer.MeasureText(text, _f, new Size(9999, 999), Flags);
}
