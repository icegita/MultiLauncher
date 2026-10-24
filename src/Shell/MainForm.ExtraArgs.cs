using System.Text.Json.Nodes;

namespace MultiLauncher;

// C（2026-10-22，方案「Strata 参数拆分与手加键覆盖层」§4；DEC-205）：
// `<tag>.extra.json` 手加键覆盖层的**读取 + 合并**。纯函数族，不碰界面、不碰进程、不写文件。
//
// 为什么要有这一层：物化（BuildEffectiveConfig）每次从模板全量重建，而模板是 SETUP 的地盘 ——
// 用户自己往模板里加的上游新参数会被 SETUP 重跑冲掉。覆盖层住 data\config\strata\，与模板分开，
// SETUP 不认识它，也就冲不掉它。
//
// 三条硬规则（方案 §4.2，一字不改地落在这里）：
//   ① 覆盖层里写了**壳接管**的 flag（ValueFlags / BoolFlags）→ 丢弃 + 点名。界面是唯一真相，
//      不开"覆盖层赢"这个开关（方案 C1：两套真相必出事）；
//   ② 覆盖层坏了（不是合法 JSON / 顶层不是对象 / args 不是字符串数组）→ 整份忽略 + 点名，
//      **不阻断启动**（启动期不弹模态，只进日志）；
//   ③ 文件不存在 → 完全静默（大多数用户不会有这个文件）。
//
// 合并语义（方案 §4.1）：按 flag 名合并 —— 同名**原位替换**（保住模板顺序），新 flag 追加尾部，
// 裸值追加尾部并告警。用"原位替换"而不是"追加"：同一 flag 出现两次时 argparse 的行为不保证，
// 一般后者覆盖，但不该赌（方案 C6）。
public partial class MainForm
{
    /// <summary>args 的一个条目：<c>Flag</c> 为空 = 裸值（位置参数），值放在 <c>Value</c> 里。
    /// 无值 flag（<c>--mmap-experts</c> / <c>--vision</c> 这类）的 <c>Value</c> 为 null。
    /// 判定"下一个 token 是不是它的值"只用一条规则：**以 `--` 开头就当成另一个 flag**（引擎参数里没有以两个减号开头的值）。</summary>
    readonly record struct ArgEntry(string Flag, string? Value);

    /// <summary>把线性 args 解析成条目表。只认"两个减号开头"当 flag，其余一律裸值。</summary>
    static List<ArgEntry> ParseArgEntries(IReadOnlyList<string> args)
    {
        var list = new List<ArgEntry>(args.Count);
        for (int i = 0; i < args.Count; i++)
        {
            var a = args[i] ?? "";
            if (a.StartsWith("--", StringComparison.Ordinal))
            {
                string? v = null;
                if (i + 1 < args.Count && !(args[i + 1] ?? "").StartsWith("--", StringComparison.Ordinal)) v = args[++i];
                list.Add(new ArgEntry(a, v));
            }
            else list.Add(new ArgEntry("", a));
        }
        return list;
    }

    static List<string> FlattenArgEntries(List<ArgEntry> entries)
    {
        var list = new List<string>(entries.Count + 4);
        foreach (var e in entries)
        {
            if (e.Flag.Length == 0) { if (e.Value is not null) list.Add(e.Value); continue; }
            list.Add(e.Flag);
            if (e.Value is not null) list.Add(e.Value);
        }
        return list;
    }

    /// <summary>这个 flag 是不是壳接管的（界面是唯一真相）。物化的"删了再按界面重加"与覆盖层的"丢弃并点名"共用这一处判据。</summary>
    internal static bool ManagedByShell(string flag) => ValueFlags.Contains(flag) || BoolFlags.Contains(flag);

    /// <summary>一份线性 args 里有多少个条目（flag 与裸值各算一条，flag 的值不算）—— 日志里"合并 N 条手加键"报的这个数。</summary>
    internal static int CountArgEntries(IReadOnlyList<string> args) => ParseArgEntries(args).Count;

    /// <summary>方案 §4.1 的合并：按 flag 名合并，同名原位替换、新 flag 追加、裸值追加并告警。
    /// <paramref name="warnings"/> 是**追加**用的（调用方给一份，读/合并两段往里写同一份，日志只需照单全打）；
    /// <paramref name="ignored"/> = 因"壳接管"被丢弃的条数（来源摘要那句要报的数）。
    /// <paramref name="overlayName"/> 只用于告警文案（给文件名，不给全路径 —— 那句是给人看的）。
    ///
    /// 2026-10-24 他点的两件（同一处改动，两条消息）：
    ///   · **甲**：手加键的 flag 名在模板 args 里一个都没有 → 提示一句。**这不是白名单**（壳不知道引擎有哪些参数，
    ///     实测 --help 都不全），只是「拼错 / 上游改名 / 拿错了模板」的信号 —— 多一个能让人早看见的字。
    ///   · **覆盖出声**：手加键把模板里同名的值改掉了 → 点名「模板原来是 X、现在按 Y 走」，值相同则不出声（不制造噪声）。
    ///   · 两条都**按 flag 去重**（2026-10-24 他点「同一段同一条只报一次」）：同一段里同一个 flag 写了两遍，
    ///     只提示一条 —— 那是同一件事，重复打两遍只会把日志淹掉。</summary>
    internal static List<string> MergeExtraArgs(IReadOnlyList<string> templateArgs, IReadOnlyList<string> extraArgs,
                                               string overlayName, List<string> warnings, out int ignored)
    {
        ignored = 0;
        var merged = ParseArgEntries(templateArgs);
        var reported = new HashSet<string>(StringComparer.Ordinal);   // 已经报过的那几条 flag（同一段只报一次）
        var handAdded = new HashSet<string>(StringComparer.Ordinal);  // 由手加键追加进来的 flag（它们覆盖自己不算「覆盖模板」）
        foreach (var e in ParseArgEntries(extraArgs))
        {
            // 规则：裸值（位置参数）照样追加，但要告警 —— args 里裸值的含义由引擎自己定，壳不担保
            if (e.Flag.Length == 0)
            {
                merged.Add(e);
                warnings.Add($"位置参数「{e.Value}」已追加到参数末尾 —— args 里的裸值含义由引擎自己定，不保证生效");
                continue;
            }
            // 硬规则 ①：壳接管的一律丢弃，并在启动日志点名（界面唯一真相，方案 C1）
            if (ManagedByShell(e.Flag))
            {
                ignored++;
                warnings.Add($"{e.Flag} 由界面接管，{overlayName} 里这条已忽略");
                continue;
            }
            int at = merged.FindIndex(m => m.Flag == e.Flag);
            if (at < 0)
            {
                // 模板里没有这个 flag → 追加尾部，并且出声（甲）：拼错时这是唯一的信号
                merged.Add(e);
                handAdded.Add(e.Flag);
                if (reported.Add(e.Flag))
                    warnings.Add($"{e.Flag} 模板里没有这一项，按手加键追加到参数末尾（拼错名字的话引擎会拒绝启动，日志里看得到）");
                continue;
            }
            // 模板有 → 原位替换（保住模板顺序）；值真改了才出声（覆盖出声），相同则静默
            var was = merged[at];
            merged[at] = e;
            // 手加键自己写了第二遍（覆盖的是自己刚追加的那条）不算「覆盖模板里的值」，也别再报一次
            if (!handAdded.Contains(e.Flag) && !ArgValueSame(was, e) && reported.Add(e.Flag))
                warnings.Add($"{e.Flag} 覆盖了模板里的值：{ArgValueText(was)} → {ArgValueText(e)}（来源：{overlayName}）");
            // 模板自己重复写了同一个 flag 时也收成一条：合并结果里同一 flag 只许出现一次（方案 C6）
            for (int i = merged.Count - 1; i > at; i--) if (merged[i].Flag == e.Flag) merged.RemoveAt(i);
        }
        return FlattenArgEntries(merged);
    }

    /// <summary>两个条目算不算"值相同"（无值 flag 的 Value 是 null，两个 null 也算相同）。</summary>
    static bool ArgValueSame(ArgEntry a, ArgEntry b) =>
        string.Equals(a.Value ?? "", b.Value ?? "", StringComparison.Ordinal);

    /// <summary>告警文案里的值：无值 flag 写成「（无值开关）」，免得日志里出现「→ 」这种半截话。</summary>
    static string ArgValueText(ArgEntry e) => e.Value is null ? "（无值开关）" : $"「{e.Value}」";

    /// <summary>读一份覆盖层（方案 §4.2 / 只认 args：2026-10-22 他拍）。
    /// 任何结构问题 = **整份按空处理** + 一条告警点名路径与原因，绝不半份生效、绝不抛、绝不阻断启动。
    /// 文件不存在 = 静默返回空（规则 ③）。</summary>
    internal static List<string> ReadExtraOverlay(string path, List<string> warnings)
    {
        var empty = new List<string>();
        if (!File.Exists(path)) return empty;

        string text;
        try { text = File.ReadAllText(path); }
        catch (Exception ex) { warnings.Add($"{path} 读不动（{ex.Message}），这份覆盖层已忽略"); return empty; }

        JsonObject? node;
        try { node = JsonNode.Parse(text) as JsonObject; }
        catch (Exception ex) { warnings.Add($"{path} 不是合法 JSON（{ex.Message}），这份覆盖层已忽略"); return empty; }
        if (node is null) { warnings.Add($"{path} 的顶层不是 JSON 对象，这份覆盖层已忽略"); return empty; }

        if (node["args"] is not JsonArray arr)
        {
            // 只认 args（顶层键壳也在改，两层抢同一个键必打架，方案 §4.3）——顺带把不认的键点名，
            // 别让人以为写进去了（这正是 C3「静默失效」要防的那一类）
            var others = node.Select(kv => kv.Key).Where(k => k != "args").ToList();
            warnings.Add(others.Count == 0
                ? $"{path} 里没有 args —— 这一层只认 args，等于没写"
                : $"{path} 里只有 args 算数，这些键已忽略：{string.Join("、", others)}");
            return empty;
        }
        // args 在、旁边还塞了别的顶层键：args 照读，别的键点名忽略（不静默吞掉用户写的东西）
        {
            var others = node.Select(kv => kv.Key).Where(k => k != "args").ToList();
            if (others.Count > 0)
                warnings.Add($"{path} 只认 args，这些顶层键已忽略：{string.Join("、", others)}");
        }

        var list = new List<string>(arr.Count);
        foreach (var el in arr)
        {
            if (el is not JsonValue v || !v.TryGetValue<string>(out var s))
            {
                warnings.Add($"{path} 的 args 里有非字符串项，这份覆盖层已忽略");
                return empty;
            }
            list.Add(s);
        }
        return list;
    }

    /// <summary>段里那些"写了但没有任何消费者"的键点名（DEC-219 §5 第 5 条）。
    /// 目前认四种：16 格（界面键）、`ArgsN`、花括号块、注释。别的一律告警 —— 因为写回纪律①保证
    /// "不认识的键一行不动"，所以它**不会报错、也不会生效**，只会静默留存在文件里（正是要防的那一类）。</summary>
    static void WarnUnknownModelKeys(ModelParamsIni.Section sec, List<string> warnings)
    {
        foreach (var line in sec.Body)
        {
            var k = ModelParamsIni.KeyOf(line);
            if (k is null) continue;                                  // 空行 / 注释 / 段头
            if (k.StartsWith("{", StringComparison.Ordinal)) continue; // 花括号块（ReadBraceArgs 那一支管）
            if (k.Length > ModelParamsIni.ArgsKeyPrefix.Length
                && k.StartsWith(ModelParamsIni.ArgsKeyPrefix, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(k[ModelParamsIni.ArgsKeyPrefix.Length..], out _)) continue;   // ArgsN
            if (ModelOwnedKeys.Contains(k, StringComparer.OrdinalIgnoreCase)) continue;       // 16 格
            warnings.Add($"[{sec.Name}] 这一行写着「{k} = ...」，但它既不是界面上那 16 格、也不是 "
                       + $"{ModelParamsIni.ArgsKeyPrefix}N、也不是花括号块 —— 壳不认这个键，它不会生效也不会报错（写回时会原样留着）");
        }
    }

    /// <summary>某个模型的手加参数从哪儿读（2026-10-22 第三版，DEC-210；2026-10-24 DEC-219 加第二种写法）：
    /// `strata_models.ini` 里该段的 `Args1`/`Args2`… 行（一行一个 token，按序号），以及
    /// `{参数名 = 值}` 行（一行一块，摊成 `--参数名` + `值`）。两种拼成一条线性序列：`ArgsN` 在前、花括号在后。
    /// 段不在 / 没写 = 空（静默）。
    /// 老形态（DEC-207 的 `strata_models.json`、C 那版的 `<tag>.extra.json`）还在时也照读 ——
    /// 迁移之前那一趟要能用，迁移完就被并进 `ArgsN` 并挪走。</summary>
    internal static List<string> ReadExtraArgsForTag(string tag, List<string> warnings)
    {
        var empty = new List<string>();
        var path = ModelParamsPath();
        if (File.Exists(path))
        {
            string text;
            try { text = File.ReadAllText(path); }
            catch (Exception ex) { warnings.Add($"{ModelParamsFileName} 读不动（{ex.Message}），这一层按没有处理"); return empty; }
            var sec = ModelParamsIni.Parse(text).Find(tag);
            if (sec is null) return empty;                                    // 这一段没有 = 没有手加参数
            WarnUnknownModelKeys(sec, warnings);                              // 写了但不生效的键要点名，别静默
            var args = ModelParamsIni.ReadArgs(sec);
            args.AddRange(ModelParamsIni.ReadBraceArgs(sec, warnings));       // 第二种写法：一行一块（DEC-219）
            return args;
        }
        // 迁移还没跑（老的 json 还在）：照读，别让手加参数在升级那一趟丢一次
        var legacyJson = LegacyModelParamsPath();
        if (File.Exists(legacyJson))
        {
            try
            {
                if (JsonNode.Parse(File.ReadAllText(legacyJson)) is JsonObject root
                    && root[tag]?[GroupArgsKey] is JsonArray arr)
                    return arr.Select(x => x?.GetValue<string>() ?? "").ToList();
            }
            catch (Exception ex) { warnings.Add($"{LegacyModelParamsFileName} 读不动（{ex.Message}），这一层按没有处理"); }
            return empty;
        }
        var legacy = ExtraOverlayPath(tag);
        return File.Exists(legacy) ? ReadExtraOverlay(legacy, warnings) : empty;
    }
}
