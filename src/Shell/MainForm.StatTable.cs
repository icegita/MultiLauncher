using System.Globalization;
using System.Text.Json.Nodes;

namespace MultiLauncher;

// DEC-125② 显示项这张表（唯一家：id/标签/算法/有没有平均档 + 分段与次序）。
// 2026-10-15 从 MainForm.StatusBar.cs 整段搬出，一行未改。
public partial class MainForm
{
    // ---------------- DEC-125②：显示项这张表 = 唯一家 ----------------
    // 一行 = 一个可复选的档。加一项 = 这张表加一行 + `StatPack` 多一个字段 + 各模块那一行愿意给就填，
    // 别处（文案、设置页那一排复选、自检穷举）全从这张表长出来。
    // **HasAvg = false 的行在设置页不会长出「平均」那枚复选** —— 他的原话："平均不是每个参数都有,
    // 如勾选 decode 时,才出现 decode 这个的平均值复选"。队列 / 显存 / 温度 = 只有当下这一瞬的值、
    // token 总量 = 它本身就是累计、MTP 接受率 = DEC-039 定过"MTP 就只需要累计值" —— 这四类都没有"平均 vs 当前"两说。
    internal enum StatKind { Rate, Pct, Count, Tokens, Vram, Temp }

    internal readonly record struct StatItemDef(string Id, string Label, StatKind Kind, bool HasAvg);

    internal static readonly StatItemDef[] StatItemDefs =
    {
        new("prefill", "预填充", StatKind.Rate, true),
        new("decode", "解码", StatKind.Rate, true),
        new("mtp", "MTP 接受率", StatKind.Pct, false),
        new("queue", "队列", StatKind.Count, false),
        new("cache", "缓存命中", StatKind.Pct, false),
        new("tokin", "输入", StatKind.Tokens, false),      // DEC-128③：文案去掉 "token"（他点名：直接写"输入/输出"）
        new("tokout", "输出", StatKind.Tokens, false),
        new("vram", "显存", StatKind.Vram, false),        // 整卡值：见下面那个采样器的注释
        new("temp", "温度", StatKind.Temp, false),
    };

    internal static bool IsStatAvgable(string id) =>
        StatItemDefs.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase)) is { HasAvg: true };

    /// <summary>勾了哪些档（csv → 集合）。表外 id 一律忽略 = 手改坏的 ini / 以后退役的项都不会把状态栏弄炸。</summary>
    internal static HashSet<string> StatusBarIdSet(string? csv)
        => new(StatusBarOrderOf(csv), StringComparer.OrdinalIgnoreCase);

    /// <summary>csv 里那串**按写进去的次序**筛一遍（表外的跳过、重复的只留第一次）。
    /// DEC-127②：次序的家从「`StatItemDefs` 那张表的行序」改到「csv 的先后」——
    /// 设置页那一排现在可以拖动，拖出来的先后就是状态栏那串的先后；表序退为**兜底**（见 `StatRowOrder`）。</summary>
    internal static List<string> StatusBarOrderOf(string? csv)
    {
        var order = new List<string>();
        foreach (var raw in (csv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            // 收的是**表里那一个 id**（不是文件里怎么写就怎么收）= 大小写不敏、拼法只有一种，
            // 后面 StatDefOf 查得到、去重也只用一次比较
            if (StatDefOf(raw) is { } d && !order.Contains(d.Id, StringComparer.OrdinalIgnoreCase)) order.Add(d.Id);
        return order;
    }

    /// <summary>设置页那一排的行序 = csv 里拖出来的次序打头，表里还没进 csv 的（新加的档 / 用户没勾的）按表序补在后面。
    /// 表里九行**一行都不许少**：少了那一行，用户就没有地方把它勾回来。</summary>
    internal static List<string> StatRowOrder(string? csv)
    {
        var order = StatusBarOrderOf(csv);
        order.AddRange(StatItemDefs.Where(d => !order.Any(id => string.Equals(id, d.Id, StringComparison.OrdinalIgnoreCase)))
                                   .Select(d => d.Id));
        return order;
    }

    /// <summary>DEC-128②：九行的全量视觉次序 —— 新键 `StatusBarOrder` 优先；老 ini（还没存过新键）按
    /// `StatRowOrder(Items)` 兜底（正是升级前那一套，一字不差）。渲染、量宽、设置页建行共用这一条。</summary>
    internal static List<string> StatusBarRowOrder(string? orderCsv, string? itemsCsv)
        => StatRowOrder(string.IsNullOrWhiteSpace(orderCsv) ? itemsCsv : orderCsv);

    /// <summary>按 id 找表里那一行（表外 id = null，调用处跳过 = 判不了就当没这一档）。
    /// 为什么不用 `FirstOrDefault(谓词)`：`StatItemDef` 是 record **struct**，找不到时返回的是 default 结构体、
    /// 会被隐式包成"有值的 nullable"（里面 Id 是 null）—— 于是 `is { } d` 永远成立、表外 id 反而带着一个空标签进来
    /// （这一条就是自检抓出来的：「BOGUS」混进次序串里变成一个空档）。</summary>
    internal static StatItemDef? StatDefOf(string id)
    {
        foreach (var d in StatItemDefs)
            if (string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase)) return d;
        return null;
    }

    /// <summary>「平均」档：先按表筛一遍，再把不该有平均的收掉 —— ini 里塞一个 "mtp" 也不会多出半档。</summary>
    internal static HashSet<string> StatusBarAvgSet(string? csv)
    {
        var set = StatusBarIdSet(csv);
        set.RemoveWhere(id => !IsStatAvgable(id));
        return set;
    }

    /// <summary>三张模块那一行交出来的**全部可显示值**（取不到的给 null = 短横，不许自己造 0）。
    /// 预填充/解码各两档（平均 / 当前），其余单项。</summary>
    internal readonly record struct StatPack(
        double? PrefillAvg, double? PrefillNow, double? DecodeAvg, double? DecodeNow, double? Accept,
        double? Queue, double? CacheHitPct, double? TokIn, double? TokOut,
        double? VramUsedMib, double? VramTotalMib, double? GpuTempC);

    /// <summary>按**九行全量次序**（`StatusBarRowOrder`，设置页拖出来的那个）把值排成「标签 + 值」段
    /// （**文案的唯一算法**：界面绘制、自检断言、宽度量尺都走它）。
    /// DEC-128②（他挑的"同排两枚独立"）：带平均的行 = 「实时」「平均」两枚**各自独立** ——
    /// 只勾实时 = 只写当前，只勾平均 = 只写平均值（`466.2 t/s`，"平均在前"的老约定不变），
    /// 两枚都勾 = 照旧 `平均|当前`（DEC-039 那版样式）；两枚都没勾的行根本不进这张单子（写侧保证，
    /// 手改 ini 撞上就当"出当前"，不把它藏了）。其余行 = 勾了就出。</summary>
    internal static List<(string Label, string Value)> StatusBarSegments(StatPack p,
        IReadOnlyList<string> order, IReadOnlyCollection<string> nowOn, IReadOnlyCollection<string> avgOn)
    {
        var list = new List<(string, string)>();
        foreach (var id in order)
        {
            if (StatDefOf(id) is not { } d) continue;              // 表外 id：跳过，不炸也不编一句出来
            if (d.HasAvg)
            {
                bool showNow = nowOn.Contains(d.Id);
                bool showAvg = avgOn.Contains(d.Id);
                if (!showNow && !showAvg) continue;                // 两枚都没勾 = 这一档不出（次序里有它 ≠ 要出它）
                list.Add((d.Label, StatValue(d, p, showNow, showAvg)));
            }
            else if (nowOn.Contains(d.Id))
                list.Add((d.Label, StatValue(d, p, true, false)));
        }
        return list;
    }

    // DEC-127①（他点名）：**没有数值的时候不显示单位** —— 短横就是短横，不许写成 `- t/s` / `-%` / `-°C`。
    // 一条判据：这一档**一个数都没取到** → 只出短横；只要有一个数取到了，单位照旧跟着（缺的那一格仍写短横占位，
    // 否则「平均」那档空着时会把竖线后面的数顶成"看起来像平均"）。
    const string Dash = "-";

    static string StatValue(StatItemDef d, StatPack p, bool showNow, bool showAvg) => d.Kind switch
    {
        StatKind.Rate => RateText(d.Id == "prefill" ? p.PrefillAvg : p.DecodeAvg,
                                  d.Id == "prefill" ? p.PrefillNow : p.DecodeNow, showNow, showAvg, " t/s"),
        StatKind.Pct => WithUnit(Fmt(d.Id == "mtp" ? p.Accept : p.CacheHitPct, "0.0"),
                                 (d.Id == "mtp" ? p.Accept : p.CacheHitPct) is not null, "%"),
        // token 总量没有尾单位（DEC-125② 定的样子）；标签从 DEC-128③ 起就叫「输入 / 输出」，值照旧只给数
        StatKind.Count => WithUnit(Fmt(p.Queue, "0"), p.Queue is not null, ""),
        StatKind.Tokens => WithUnit(Fmt(d.Id == "tokin" ? p.TokIn : p.TokOut, "0"),
                                    (d.Id == "tokin" ? p.TokIn : p.TokOut) is > 0, ""),   // DEC-148：0 = 没收到数据 = 短横
        // 显存是「用了/整卡」两个数，取不到任何一个都不写这个 G（原来那个 null 分支就是这个形状）
        StatKind.Vram => p.VramUsedMib is null || p.VramTotalMib is null or <= 0
            ? Dash : $"{p.VramUsedMib / 1024.0:0.0}/{p.VramTotalMib / 1024.0:0.0}G",
        StatKind.Temp => WithUnit(Fmt(p.GpuTempC, "0"), p.GpuTempC is not null, "°C"),
        _ => Dash,
    };

    // Rate 那一族两档：平均 / 当前（DEC-128② 起两枚各自独立）。
    //   两枚都勾：`平均|当前`（两档都没数 = 只出一个短横；只缺一档 = 那一格短横、单位照写，DEC-127①）
    //   只勾一枚：就出那一枚（没数 = 短横 + 不写单位）
    static string RateText(double? avgVal, double? nowVal, bool showNow, bool showAvg, string unit)
    {
        if (showAvg && showNow)
        {
            if (avgVal is null && nowVal is null) return Dash;
            return Fmt(avgVal, "0.0") + "|" + Fmt(nowVal, "0.0") + unit;
        }
        var v = showAvg ? avgVal : nowVal;
        return v is null ? Dash : Fmt(v.Value, "0.0") + unit;
    }

    static string WithUnit(string number, bool has, string unit) => has ? number + unit : Dash;

    /// <summary>默认全勾时那条串有多宽、条区给得起多少（"塞这么多会不会放不下"要量出来，不靠肉眼）。</summary>
    internal (int Need, int Room) StatusBarFitForTest()
    {
        var all = StatusBarSegments(ShowAllSample,
                                    StatusBarRowOrder(_config.Current.StatusBarOrder, _config.Current.StatusBarItems),
                                    StatusBarIdSet(_config.Current.StatusBarItems),
                                    StatusBarAvgSet(_config.Current.StatusBarAvg));
        int gap = UiTheme.S(this, 4);
        var f = new Font("Microsoft YaHei UI", 8.5f);
        int need = all.Sum(part => TextRenderer.MeasureText(part.Label + " " + part.Value, f, Size.Empty,
            TextFormatFlags.NoPadding).Width) + gap * Math.Max(0, all.Count * 2 - 1);
        f.Dispose();
        return (need, _statusBar is null ? 0 : Math.Max(0, _statusBar.Width - UiTheme.S(this, 16 + 4)));
    }

    // 宽度量尺喂的样本 = 每一位都取"放得下最坏情况"的数（五位千分位、九位显存串）
    internal static readonly StatPack ShowAllSample =
        new(1234.5, 12345.6, 123.4, 123.4, 81.99, 1234, 37.2, 1234567, 45678, 18944, 24576, 73);

    /// <summary>自检/出图台用的旧形状入口（五档 → StatPack，其余全短横），一条文案算法只有一个家。
    /// 旧形状 = 三档都出当前、前两档带平均（与升级前那条串逐字相同）。</summary>
    internal static List<(string Label, string Value)> StatusBarTextSegments(double? prefillAvg, double? prefillNow,
        double? decodeAvg, double? decodeNow, double? accept) =>
        StatusBarSegments(new StatPack(prefillAvg, prefillNow, decodeAvg, decodeNow, accept,
            null, null, null, null, null, null, null),
            new[] { "prefill", "decode", "mtp" }, new[] { "prefill", "decode", "mtp" }, new[] { "prefill", "decode" });

    internal static string StatusBarText(double? prefillAvg, double? prefillNow, double? decodeAvg, double? decodeNow,
                                         double? accept) => StatusBarJoinText(
        StatusBarTextSegments(prefillAvg, prefillNow, decodeAvg, decodeNow, accept));

    internal static string StatusBarJoinText(IEnumerable<(string Label, string Value)> parts)
        => string.Join("·", parts.Select(p => $"{p.Label} {p.Value}"));


}
