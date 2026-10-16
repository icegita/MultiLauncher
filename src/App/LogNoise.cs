using System.Text.RegularExpressions;

namespace MultiLauncher;

// ============================================================================
// DEC-158：三模块共用的那张降噪表（DEC-111 §5 第 2、3 条 = 当时"已批未施工"的那两条）
// ============================================================================
// DEC-162（2026-10-13 第 40 轮，他实跑点名）：**「合并」那一档整档作废**。
//   为什么：DEC-158 把四类高频台账收成每 5 秒一条 `[降噪] <类名> ×N` 的**壳自己造的合成行** ——
//   他原话「我让你做降噪不是要从一堆杂乱信息改成另一堆杂物」：生成期间每分钟几十条合成行 = 噪声还在，
//   只是换成了壳的字，还多出一层无内容的假行。**壳从此不产出任何自己的合成行。**
//
// DEC-163（2026-10-13 第 41 轮，他核过体积后拍"B 路径"，原话"1MB 还能接受"）：再把「整类丢弃」也退了 ——
//   八类引擎台账（六类纯台账 + `ring retrieve`/`host-backed` 两类真证据）**文件与内存历史照记**，
//   只做一条**简单去重**：同一类里与上一条逐字节相同就不落（19 分钟那份实测 6.29 MB → ≈1 MB）。
//
// 现行语义（两件事、一个名单）：
//   · **界面过滤挡**（`Program.IsNoise` → `ShownAsNoise`）：「调试信息过滤」开着时这些行界面上不显示；
//   · **连发去重**（`IsRepeat`，挂在 `Program.Log` 入口）：同一类里内容没变 = 整条不落（不上屏 / 不进历史 / 不写文件）。
// 表里没这条 = 原样（信号行、真错误行都走这一档，一条不许少，也**不去重** —— 两条一字不差的警告必须都记）。
// 「引擎调试日志（写全量）」开关与 `EngineFullLedger` 键随本批整套退役（读不认、随下次保存消失，口径同 LowRam DEC-075）。
//
// 自检口径：喂样本行断"这一类在不在名单里" + 去重三面（首条放行 / 同内容挡下 / 变了再放）+ 不误伤 + 红/绿双向。
internal sealed class NoiseRule
{
    public required string Id { get; init; }
    /// <summary>全部命中才算这一类（AND）。形状只认实测日志里确认过的字面。</summary>
    public required string[] Has { get; init; }
    /// <summary>表达不到的形状（#6 那类"空转才算"）；null = 只看字面。</summary>
    public Func<string, bool>? Extra { get; init; }

    public bool Matches(string t)
    {
        foreach (var h in Has)
            if (!t.Contains(h, StringComparison.Ordinal)) return false;
        return Extra is null || Extra(t);
    }
}

internal static class LogNoise
{
    /// <summary>名单 = 八类 Ninfer 引擎台账。行序 = 判序：先具体后宽泛（`KEPT` 走在 `SELECT` 前，两条都含 `kvmem_score:`）。
    /// llama 那族高频形状仍留在 `Program.NoiseContains`（只挡界面、不去重），两边在 `Program.IsNoise` 这一处并起来。</summary>
    internal static readonly NoiseRule[] Rules =
    {
        // ---- 六类纯台账（分类账 §3 的 #1~#6；占 99% 行与字节 —— 去重后 ≈1 MB/19 分钟）----
        new() { Id = "ring budgets（池分配台账）", Has = new[] { "[ring] budgets:" } },            // #1：逐字节相同的常数
        new() { Id = "kvmem KEPT（主机缓存块索引）", Has = new[] { "kvmem_score: KEPT" } },        // #4：几百个数字的裸清单
        new() { Id = "mask hidden（mapped）", Has = new[] { "mask hidden:", "site=mapped" } },     // #2：每 prefill chunk 一次
        new() { Id = "mask hidden（commit）", Has = new[] { "mask hidden:", "site=commit" } },     // #3
        new() { Id = "kvmem SELECT（打分的入参）", Has = new[] { "kvmem_score: SELECT" } },        // #5
        new() { Id = "demote-other 空转", Has = new[] { "demote-other short:", "freed=0" }, Extra = DemoteIdle },   // #6：非零那几种原样留、也不去重
        // ---- 两类真证据（#7/#8）：同一条简单去重 ----
        new() { Id = "ring retrieve（命中率）", Has = new[] { "ring retrieve:" } },                 // #7
        new() { Id = "host-backed（KV 落主机/回读）", Has = new[] { "host-backed: frontier=" } },   // #8

        // ---- Strata 进度行**不在名单里**：界面走它自己的 5 秒节流（`IsProgressSpam` + `ProgressAllowed`），
        //      文件照旧全量（DEC-038 口径）—— DEC-163 起这张表不再管它，别再加回来。
    };

    /// <summary>这一行在不在"引擎台账"名单里（null = 不在 = 原样，也不去重）。</summary>
    internal static NoiseRule? Classify(string text)
    {
        if (text.Length == 0) return null;
        foreach (var r in Rules) if (r.Matches(text)) return r;
        return null;
    }

    /// <summary>界面「调试信息过滤」打开时该挡的那些 = 名单里的每一行
    /// （llama 那一族自己的高频形状在 `Program.NoiseContains` 里，两边在 `Program.IsNoise` 这一处并起来）。</summary>
    internal static bool ShownAsNoise(string text) => Classify(text) is not null;

    /// <summary>简单去重（DEC-163）：同一类里与上一条逐字节相同 → true（整条不落，`Program.Log` 入口那一道用）。
    /// 每次调用把这一条记成"上一条"；键 = 类名 —— 八条 key 的小字典，不需要定时器。</summary>
    internal static bool IsRepeat(IDictionary<string, string> last, NoiseRule r, string text)
    {
        if (last.TryGetValue(r.Id, out var prev) && prev == text) return true;
        last[r.Id] = text;
        return false;
    }

    // ---------------- #6 那条"条件放行"：空转才合并，真结果原样 ----------------

    /// <summary>只看解放数与失败位；`want` / `slots` / `pins` / `pending` 是上下文不是结果，不参与。</summary>
    static readonly string[] DemoteFailBits = { "freed", "invalid", "noresident", "stalehost",
                                                "partial", "dup", "flushfail", "declined" };

    internal static bool DemoteIdle(string t)
    {
        foreach (var k in DemoteFailBits) if (NumberAfter(t, k) > 0) return false;
        return true;
    }

    static int NumberAfter(string t, string key)
    {
        var m = Regex.Match(t, Regex.Escape(key) + @"\s*=\s*(\d+)");
        return m.Success && int.TryParse(m.Groups[1].Value, out var v) ? v : 0;
    }
}
