using System.Text.RegularExpressions;

namespace MultiLauncher;

// Ninfer 的起前算数与护栏（纯函数，拒绝启动的每一条都从这里出）。
// 2026-10-15 从 NinferIni.cs 整段搬出，一行未改。
internal static partial class NinferIni
{
    // ---------------- 起前算数与护栏（纯函数，拒绝启动的每一条都从这里出） ----------------

    /// <summary>包文档给的两个换算（教程-KVMem与短测 §172 / §178）：
    /// 64 token = 1 页；主机池在 k8v4 这一档 ≈ 25 KiB / token。判据原文 = 主机池页数 + 设备池页数 ≥ 逻辑上下文页数。</summary>
    public const int TokensPerPage = 64;
    public const double KibPerTokenK8V4 = 25.0;

    /// <summary>「起前算数」这一条只认得 k8v4 那一档的换算表（包文档只给了它）。
    /// 别的 KV 精度档**按"没这条校验"处理**（口径同 DEC-067：上游没给判据就不许误杀），
    /// 返回值里 checked=false 就是这个意思，日志会交代一句。</summary>
    internal readonly struct PageMath
    {
        public readonly bool Checked;          // false = 这一档没有换算表，这条不判
        public readonly long NeedHostKib;      // 主机池至少要多少 KiB
        public readonly long HaveHostKib;      // --host-kv-mib 给了多少
        public readonly string Text;           // 说给用户听的那一句
        public PageMath(bool chk, long need, long have, string text)
        { Checked = chk; NeedHostKib = need; HaveHostKib = have; Text = text; }
    }

    /// <summary>算一页 = 64 token 的那条不等式。任一量读不进去就返回「不判」（别拿猜出来的数拒绝启动）。</summary>
    public static PageMath HostPages(string? maxContext, string? kvCapacity, string? hostKvMib, string? kvDtype)
    {
        if (!string.Equals((kvDtype ?? "").Trim(), "k8v4", StringComparison.OrdinalIgnoreCase)
            || !long.TryParse((maxContext ?? "").Trim(), out var ctx) || ctx <= 0
            || !long.TryParse((kvCapacity ?? "").Trim(), out var pool) || pool <= 0
            || !long.TryParse((hostKvMib ?? "").Trim(), out var mib) || mib <= 0)
            return new PageMath(false, 0, 0, "");
        long logical = (ctx + TokensPerPage - 1) / TokensPerPage;
        long poolPages = pool / TokensPerPage;
        long needPages = Math.Max(0, logical - poolPages);
        long needKib = needPages * TokensPerPage * (long)Math.Round(KibPerTokenK8V4);
        long haveKib = mib * 1024;
        // 单位是 **MiB**（haveKib/1024 = MiB）：原来写成 GiB，那行日志会报"主机池 16384 GiB vs 需要 ≥ 5050 GiB"
        // —— 16 TB 的池子，看着就像算错了（DEC-098 实跑核对读数时抓到的）。界面上那一格填的也是 MiB。
        var text = $"主机池 {haveKib / 1024} MiB vs 需要 ≥ {needKib / 1024} MiB" +
                   $"（逻辑上下文 {logical} 页 − 设备池 {poolPages} 页 = {needPages} 页，k8v4 每 token ≈ {KibPerTokenK8V4:0} KiB）";
        return new PageMath(true, needKib, haveKib, text);
    }

    /// <summary>思考档按后端各一张（DEC-086/088：WSL 那代只有 low/medium/xhigh，空白 = --no-thinking；
    /// WIN 那代是 llama 同款七档）。空白档一律不写字。</summary>
    public static readonly string[] WslEfforts = { "low", "medium", "xhigh" };
    public static readonly string[] WinEfforts = { "default", "minimal", "low", "medium", "high", "xhigh", "max" };

    public static string[] EffortsFor(string backend) => backend == BackendWsl ? WslEfforts : WinEfforts;

    // ---------------- 高级区那几只下拉的项表（**按后端各一张**，原型对照表 = 施工唯一判据源） ----------------
    // 表外的值（段里手写过别的）由 MainForm.SelectWithFallback 补成一项再选中，不静默丢（DEC-076 口径）。
    // WSL 那侧的 设备池 / 投机深度 / 分块预填充 是**文本框**（那代是全驻留形状，262144 / 3 / 1024 不在 WIN 那三档里），
    // 所以这里只给 WIN 的档。

    /// <summary>KV 精度下拉给的档 = <see cref="KvChoices"/>（**用户 2026-10-08 批复：bf16 / int8 / rk4v4 / rk2v4-e8 不再列**）。
    /// 历史：两代引擎各读过自己的 `--help`、取值同一批九个（WIN = 用户 2026-10-07 贴的 usage，
    /// WSL = 2026-10-08 进 VM 跑 `ninfer-serve --help` 读回的那行），DEC-100④ 曾照拍板给全九档。
    /// **去掉的那四档引擎仍认** → 校验照旧收 <see cref="AllowedKvDtypes"/> 九档：段里手写过它们的老文件
    /// 读得出来、保存不拦（只有表外的写法才拒）；swift 件 × 行粒度 KV 那条起前护栏一字未动（RowGranularKv）。</summary>
    public static string[] KvDtypesFor(string backend) => KvChoices;

    /// <summary>下拉保留的五档（次序 = 原九档里剩下的那一段顺序）。</summary>
    internal static readonly string[] KvChoices = { "fp8", "nvfp4", "k8v4", "rk8v4", "rk4v4-e8" };

    /// <summary>设备池（--kv-capacity）WIN 三档；&lt;17920 与 auto 一律拒绝启动（ValueProblem 那一条）。</summary>
    public static readonly string[] WinPools = { "55296", "34816", "17920" };
    /// <summary>分块预填充（--prefill-chunk）WIN 三档；&gt;4,096 拒绝启动（ArgvProblemWin 那一条）。</summary>
    public static readonly string[] WinPrefills = { "4096", "2048", "1024" };
    /// <summary>主机缓存（--host-kv-mib）WIN 三档；&gt;16384 拒绝（256K 内实测无收益、纯吃内存）。</summary>
    public static readonly string[] WinHostKib = { "16384", "8192", "4096" };
    // （原「投机深度 --draft-tokens WIN 两档 { 12, 4 }」与「共享前缀上限 --max-shared-prefixes { 0 }」两张项表
    //   随 DEC-107 退役：前者改输入框（校验只要求 ≥1 整数）、后者整格去掉（旗标从段里透传，护栏仍在）。
    //   投机方式的取值表仍在下面（WslSpecs / WinSpecs —— 起前校验的白名单）。）

    /// <summary>WIN 那条环的起前上限（原型对照表点名的两档数值护栏）。
    /// 返回值 = null 表示没问题，否则是给用户看的那一句（拒绝启动，不放行）。</summary>
    internal static string? ArgvProblemWin(IReadOnlyList<string> tokens)
    {
        var pre = FlagValue(tokens, "prefill-chunk");
        if (pre is not null && double.TryParse(pre, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var pc) && pc > 4096)
            return $"分块预填充 {pre} 大于 4,096：这条环上实测会在 57K 题面丢埋针（两个独立进程各复现一次）。";
        var host = FlagValue(tokens, "host-kv-mib");
        if (host is not null && double.TryParse(host, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var hk) && hk > 16384)
            return $"主机缓存 {host} MiB 大于 16,384：256K 以内实测无收益、纯吃内存。";
        var shared = FlagValue(tokens, "max-shared-prefixes");
        if (shared is not null && shared != "0")
            return $"共享前缀上限写了 {shared}：这条环只认 0 —— 非 0 会让超池题面逐字节重发，把实例打砖（此后全部 503）。";
        return null;
    }

    public const string BackendWin = "win";
    public const string BackendWsl = "wsl";

    /// <summary>界面值 → argv 旗标（WSL 与 WIN 各自那一圈格子；表外的组合在这里就拦掉，
    /// 不等引擎用一份静默答错的配置起来）。返回 null = 没问题，否则是给用户看的第一条原因。</summary>
    internal static string? ValueProblem(string flag, string value, string backend)
    {
        var v = (value ?? "").Trim();
        if (v.Length == 0) return null;                     // 留空 = 不写这枚旗标 = 引擎默认
        switch (flag)
        {
            case "max-context":
            case "kv-capacity":
                if (flag == "kv-capacity" && string.Equals(v, "auto", StringComparison.OrdinalIgnoreCase))
                    return "设备池（--kv-capacity）不接受 auto：auto 会让引擎按可用显存当场决定池子，" +
                           "而这条环的「主机池 + 设备池 ≥ 逻辑上下文」就没人算了（实测超池会逐字节重发把实例打砖）。";
                if (!long.TryParse(v, out var n) || n < 1) return flag + " 必须是正整数（或留空 = 不写）";
                if (flag == "kv-capacity" && n < 17920)
                    return $"设备池 {n} 太小：这条环已测下限是 4,032 token（63 页），17,920 以下会把每轮可搬回的页数压薄到未验证区。";
                return null;
            case "pending-timeout-ms":
            case "draft-tokens":
            case "device-state-slots":
                if (!long.TryParse(v, out var m)) return flag + " 必须是整数（或留空 = 不写）";
                if (flag != "device-state-slots" && m < 1) return flag + " 必须 >= 1（或留空 = 不写）";
                return null;
            case "temperature":
            case "top-p":
            case "top-k":
            case "presence-penalty":
            case "max-shared-prefixes":
            case "max-concurrency":
            case "max-pending-requests":
            case "prefill-chunk":
            case "default-max-tokens":
            case "host-kv-mib":
                if (!double.TryParse(v, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var num))
                    return flag + " 得是个数（或留空 = 不写）";
                // DEC-088 批复：原「只读」那批改判成格子，**护栏只换时机不换结论** ——
                // 这三档上限从"界面上打不开"改成"起前拒绝并说明"（对照表 WIN 那一张的最后一行）。
                if (flag == "max-shared-prefixes" && num != 0)
                    return "共享前缀上限只认 0：非 0 会让超池题面逐字节重发，把实例打砖（此后全部 503）。";
                if (backend == BackendWin && flag == "prefill-chunk" && num > 4096)
                    return "分块预填充不能大于 4,096：这条环实测会在 57K 题面丢埋针（两个独立进程各复现一次）。";
                if (backend == BackendWin && flag == "host-kv-mib" && num > 16384)
                    return "主机缓存不能大于 16,384 MiB：256K 以内实测无收益、纯吃内存。";
                return null;
            case "kv-dtype":
                return AllowedKvDtypes.Any(t => string.Equals(t, v, StringComparison.OrdinalIgnoreCase))
                    ? null : "KV 精度只能是 " + string.Join(" / ", AllowedKvDtypes);
            case "spec":
                var allowed = backend == BackendWsl ? WslSpecs : WinSpecs;
                return allowed.Any(t => string.Equals(t, v, StringComparison.OrdinalIgnoreCase))
                    ? null : $"{(backend == BackendWsl ? "WSL" : "WIN")} 这一代投机方式只能是 " + string.Join(" / ", allowed) +
                             "（件里没那个头 = 引擎 FATAL missing component，几秒就退）";
            default: return null;
        }
    }

    // 引擎 help 里 KV 存储的全部写法（次序照 help 那行）：WIN = 用户贴的 usage，WSL = 2026-10-08 进 VM 读回的
    // `--kv-dtype bf16|int8|fp8|nvfp4|k8v4|rk8v4|rk4v4|rk4v4-e8|rk2v4-e8` —— 两代同一批。
    // **DEC-101① 起这张表只当校验白名单**：界面下拉只列 `KvChoices` 那五档（用户批掉了 bf16/int8/rk4v4/rk2v4-e8），
    // 但去掉的那四档引擎仍认 —— 老文件里手写过的照旧读得出、保存不拦（不静默丢用户的写法）。
    internal static readonly string[] AllowedKvDtypes =
        { "bf16", "int8", "fp8", "nvfp4", "k8v4", "rk8v4", "rk4v4", "rk4v4-e8", "rk2v4-e8" };
    internal static readonly string[] WslSpecs = { "mtp", "dflash" };
    internal static readonly string[] WinSpecs = { "dflash2" };

    /// <summary>「swift 件 × 行粒度 KV = 静默答错」这条护栏（上游禁忌文档点名的两条硬判据之一）。
    /// 行粒度 = rk* 那一族与 fp8/int8 那几档量化；k8v4/bf16 不算。</summary>
    internal static readonly string[] RowGranularKv = { "fp8", "rk8v4", "rk4v4", "rk4v4-e8", "rk2v4-e8", "int8" };

    public static bool IsRowGranularKv(string? dtype) =>
        RowGranularKv.Contains((dtype ?? "").Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>model-id 两两不同名这条判据（同名 = 引擎静默命中第一个 = 起错件）。
    /// 返回重复的那两个段名，null = 没问题。</summary>
    public static string? DuplicateModelIds(IEnumerable<Section> sections)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in sections)
            if (!seen.Add(s.Name)) return s.Name;
        return null;
    }

    /// <summary>段名必须是合法 model-id（不能空、不能带空格与方括号 —— 它同时是 ini 段头与 argv 里的值）。</summary>
    public static bool UsableSectionName(string name) =>
        !string.IsNullOrWhiteSpace(name) && !name.Contains(' ') && !name.Contains('[') && !name.Contains(']');
}
