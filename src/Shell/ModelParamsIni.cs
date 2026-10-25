using System.Text;

namespace MultiLauncher;

/// <summary>`strata_models.ini` 的行级读写（DEC-210，2026-10-22 他点名「换成 ini，一行一个键」）。
///
/// 形状 —— 一个模型一段，键名与界面那 16 格同名（与老 `[Model:&lt;tag&gt;]` 段同一套字，所以他不用学第二套）：
/// <code>
/// [iq3_s]
/// ApiModel = qwen3.8-flash-next-iq3_s
/// Context = 262144
/// Kv = int8
/// ...
/// Args1 = --fake-flag
/// Args2 = 1
/// {resident-budget-gib = 48}      ← 2026-10-24 另一种写法：一行一块（DEC-219，见 ReadBraceArgs）
/// </code>
///
/// 等号两边各一个空格（2026-10-24 他点：「VramReserveMib = 1600 弄成这种，方便查看，也与其他 2 个模块统一」）——
/// 与 `llama_models.ini` / `ninfer_models.ini` 同一套手感。**只改写侧**：`KeyOf` / `ValueOf` 两侧都 `Trim`，
/// 所以老的无空格文件照读不误；保存一次那 16 格就自然转成新格式（值一个都不动）。
///
/// 为什么换（他给的由头是"容易改错"，这个判断是对的）：
///   · JSON 少一个逗号 / 引号 = **整份读不动** → 按"坏了不写"的纪律，保存会一直失败；
///     ini 一行一个键，改错一行只影响那一行（缺键 = 用默认），其余照常；
///   · 跟 `llama_models.ini` / `ninfer_models.ini` / `MultiLauncher.ini` 同一套手感，不用记语法；
///   · `ui` 那一层包装（"壳写的"和"你写的"分块）在 ini 里自然消失 —— 16 格是键，`ArgsN` 也是键。
///
/// 三条纪律：
///   ① **只改壳自己那 16 格**：段里的注释、`ArgsN`、你不认识的键，写回时一行都不动；
///   ② 键不分大小写（`Context` / `context` 都认），值按第一个 `=` 切（`Args3=--foo=a` 也读得对）；
///   ③ 段的先后 = 文件里的先后；新段按 tag 排序插进去（翻起来有章法，不随保存次序跳）。
///
/// 空值行不写（DEC-221，2026-10-25 他点「像这种空白值的参数，是不是不应该显示在配置文件里，更整洁一点」）——
/// 出厂默认就是空串的那几格（`ApiModel` / `Gpu` / `LayerSplit` / `ArenaPinGib` / `ExtraArgs` / `ReasoningEffort`）
/// 值为空时**整行不写**：读侧「缺键 = 用默认」本来就等价（见 `MainForm.LoadModelSnapStore`），
/// 而 `llama_models.ini` / `ninfer_models.ini` 里压根没有空值行 —— 这一步做完才算真统一。**默认非空的那 10 格照写**
/// （留着行 = 留着校验拦截，删了反而会静默回落默认）。这一层只管拼行，取舍在写盘那一层（`SaveModelSnapStore`）。
internal static class ModelParamsIni
{
    /// <summary>手加参数的键前缀：`Args1` / `Args2` …（一行一个 token，他选的形态）。</summary>
    internal const string ArgsKeyPrefix = "Args";

    /// <summary>写回时键与值之间的分隔：<c> = </c>（等号两边各一个空格，2026-10-24 他点的「与其他 2 个模块统一」）。
    /// 读侧 <see cref="KeyOf"/> / <see cref="ValueOf"/> 两侧都 Trim，所以老的无空格行照读；写一次就转成新格式。</summary>
    internal const string KeyValueSeparator = " = ";

    /// <summary>拼一行键值。**值为空时不留尾随空格**（写成 <c>LayerSplit =</c> 而不是 <c>LayerSplit = 后面挂一个看不见的空格</c>）——
    /// 空值是常态（<c>LayerSplit</c> / <c>ArenaPinGib</c> / <c>ExtraArgs</c> / <c>ReasoningEffort</c> 常空），
    /// 满文件尾随空白翻起来刺眼，diff 里也容易被看成改动。读侧照旧 <c>Trim</c>，两种写法等价。</summary>
    internal static string KeyLine(string key, string value) =>
        value.Length == 0 ? key + KeyValueSeparator.TrimEnd() : key + KeyValueSeparator + value;

    internal sealed class Section
    {
        public string Name = "";
        public List<string> Body = new();       // 段内原始行（不含段头）
    }

    internal sealed class Doc
    {
        public List<string> Preamble = new();   // 第一个段之前的行（文件头注释 / 空行）
        public List<Section> Sections = new();

        internal Section? Find(string name) =>
            Sections.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>把文件读成行结构。段头 = `[名字]`；其余行按出现顺序留着（值不做任何归一，写回时原样）。
    /// **段尾的连续空行不算段内容**（2026-10-22 他报"中间的空行太多"）：它们本来只是段与段之间的分隔，
    /// 以前被当成上一个段的行收进来，写回时再补一个 —— 于是每保存一次就多一行空行，攒到第八个他才看出来。
    /// 在这里裁掉 = 读回来就是干净形状，写回也天然幂等。</summary>
    internal static Doc Parse(string text)
    {
        var doc = new Doc();
        Section? cur = null;
        foreach (var raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.Length >= 3 && line[0] == '[' && line[^1] == ']')
            {
                cur = new Section { Name = line[1..^1].Trim() };
                doc.Sections.Add(cur);
                continue;
            }
            if (cur is null) doc.Preamble.Add(line);
            else cur.Body.Add(line);
        }
        TrimEmptyTail(doc.Preamble);
        foreach (var s in doc.Sections) TrimEmptyTail(s.Body);
        return doc;
    }

    /// <summary>把尾部的连续空行裁掉（只动尾部，段中间的空行保留 —— 那是用户排版用的）。</summary>
    static void TrimEmptyTail(List<string> lines)
    {
        while (lines.Count > 0 && lines[^1].Trim().Length == 0) lines.RemoveAt(lines.Count - 1);
    }

    /// <summary>行结构 → 文本行（**段与段之间只留一个空行**；文件末尾不留空行）。</summary>
    internal static List<string> Render(Doc doc)
    {
        var lines = new List<string>(doc.Preamble);
        TrimEmptyTail(lines);
        foreach (var s in doc.Sections)
        {
            if (lines.Count > 0) lines.Add("");
            lines.Add("[" + s.Name + "]");
            TrimEmptyTail(s.Body);          // 防外部构造的 Doc 带尾部空行（Parse 那一头已经裁过一次）
            lines.AddRange(s.Body);
        }
        TrimEmptyTail(lines);
        return lines;
    }

    /// <summary>一行是键值就交出键名（注释行 / 空行 / 段头交 null）。按第一个 `=` 切。</summary>
    internal static string? KeyOf(string line)
    {
        var t = line.Trim();
        if (t.Length == 0 || t[0] == ';' || t[0] == '#') return null;
        int i = t.IndexOf('=');
        if (i <= 0) return null;
        var k = t[..i].Trim();
        return k.Length == 0 ? null : k;
    }

    internal static string? ValueOf(string line)
    {
        int i = line.IndexOf('=');
        return i < 0 ? null : line[(i + 1)..].Trim();
    }

    /// <summary>段里这个键的值（不分大小写；没有 = false）。</summary>
    internal static bool TryGet(Section sec, string key, out string value)
    {
        foreach (var line in sec.Body)
            if (KeyOf(line) is { } k && k.Equals(key, StringComparison.OrdinalIgnoreCase))
            { value = ValueOf(line) ?? ""; return true; }
        value = "";
        return false;
    }

    /// <summary>这个键在这一段里出现了几次（`Args1` 这类重复键要点名，别静默取最后一个）。</summary>
    internal static int CountKey(Section sec, string key) =>
        sec.Body.Count(l => KeyOf(l) is { } k && k.Equals(key, StringComparison.OrdinalIgnoreCase));

    /// <summary>把若干键写进某个模型的段（缺段就建，建完按 tag 排序归位）。
    /// **只动这几个键所在的行** —— 注释、`ArgsN`、不认识的键原样留着。</summary>
    internal static void SetKeys(Doc doc, string tag, IEnumerable<KeyValuePair<string, string>> kv)
    {
        var sec = doc.Find(tag);
        if (sec is null)
        {
            sec = new Section { Name = tag };
            doc.Sections.Add(sec);
            doc.Sections.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        }
        foreach (var pair in kv)
        {
            var line = KeyLine(pair.Key, pair.Value);
            int at = sec.Body.FindIndex(l => KeyOf(l) is { } k && k.Equals(pair.Key, StringComparison.OrdinalIgnoreCase));
            if (at >= 0) sec.Body[at] = line;
            else sec.Body.Insert(LastKeyLineIndex(sec.Body), line);
        }
    }

    /// <summary>把若干键的行从这个模型的段里删掉（DEC-221：值为空且出厂默认也空的那几格不写行，
    /// 写回时要顺手把以前留下的空值行清掉，否则「越保存越脏」）。
    /// **只删这几个键所在的行** —— 注释、`ArgsN`、不认识的键原样留着。</summary>
    internal static void RemoveKeys(Doc doc, string tag, IEnumerable<string> keys)
    {
        var sec = doc.Find(tag);
        if (sec is null) return;
        var names = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
        if (names.Count == 0) return;
        sec.Body.RemoveAll(l => KeyOf(l) is { } k && names.Contains(k));
    }

    /// <summary>新键的落点 = 最后一个键值行的下一行（尾部若有注释，键排在注释前面）。</summary>
    static int LastKeyLineIndex(List<string> body)
    {
        for (int i = body.Count - 1; i >= 0; i--)
            if (KeyOf(body[i]) is not null) return i + 1;
        return 0;
    }

    /// <summary>删掉某个模型的整段（自检与"删掉这个模型"那一支用）。</summary>
    internal static bool Remove(Doc doc, string tag)
    {
        var sec = doc.Find(tag);
        return sec is not null && doc.Sections.Remove(sec);
    }

    /// <summary>这个段里的手加参数（`Args1`..`ArgsN` 按序号拼起来；序号缺号不补，一个键一个 token）。</summary>
    internal static List<string> ReadArgs(Section sec)
    {
        var numbered = new List<(int N, string V)>();
        foreach (var line in sec.Body)
        {
            var k = KeyOf(line);
            if (k is null || !k.StartsWith(ArgsKeyPrefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (!int.TryParse(k[ArgsKeyPrefix.Length..], out var n)) continue;   // `Args` 本身不算
            numbered.Add((n, ValueOf(line) ?? ""));
        }
        return numbered.OrderBy(x => x.N).Select(x => x.V).ToList();
    }

    /// <summary>段里的花括号手加参数（DEC-219，2026-10-24 他点的「统一写法」）：`{参数名 = 值}` 一行一块。
    /// 摊成 `--参数名` + `值` 两个 token（无值开关 `{vision}` 摊成单 token），交给 `MergeExtraArgs` —— 合并语义零改动。
    /// **只认一行一块**：`{` 单起一行、`}` 另起一行都不认，往 <paramref name="warnings"/> 里点名
    /// （为什么不做跨行块：见 `docs/design/方案-strata参数花括号写法-2026-10-24.md` §3 —— 块内后续行没有花括号前缀，
    /// 会被 `KeyOf` 交成裸键名，壳保存时就可能命中原位替换 / 把块切开）。
    /// </summary>
    internal static List<string> ReadBraceArgs(Section sec, List<string> warnings)
    {
        var tokens = new List<string>();
        for (int i = 0; i < sec.Body.Count; i++)
        {
            var t = sec.Body[i].Trim();
            if (t.Length == 0 || t[0] == ';' || t[0] == '#') continue;
            if (t[0] != '{' && t[^1] != '}') continue;         // 普通 16 格 / ArgsN 行，不归这一支管
            if (t[0] != '{')                                   // 只有 } 没有 {：跨行块的后半截，或手抖多打一个
            {
                warnings.Add($"[{sec.Name}] 第 {i + 1} 行有一个没有配对「{{」的「}}」：花括号只认一行一块，这一行按没有处理（{t}）");
                continue;
            }
            if (t[^1] != '}')                                  // { 开头却没在行尾收口
            {
                int close = t.IndexOf('}');
                warnings.Add(close > 0
                    ? $"[{sec.Name}] 第 {i + 1} 行的花括号块后面还有别的内容：一块写一行，这一行按没有处理（{t}）"
                    : $"[{sec.Name}] 第 {i + 1} 行是「{{」单起一行：花括号只认一行一块（{{参数名 = 值}}），跨行块不认，这一行按没有处理");
                continue;
            }
            var inner = t[1..^1].Trim();
            if (inner.Length == 0)
            {
                warnings.Add($"[{sec.Name}] 第 {i + 1} 行是个空的花括号块：这一行按没有处理");
                continue;
            }
            int eq = inner.IndexOf('=');
            var name = (eq < 0 ? inner : inner[..eq]).Trim();
            if (name.Length == 0)
            {
                warnings.Add($"[{sec.Name}] 第 {i + 1} 行的花括号块没有参数名：这一行按没有处理（{t}）");
                continue;
            }
            if (!name.StartsWith("--", StringComparison.Ordinal)) name = "--" + name;   // 带不带 -- 都容忍
            tokens.Add(name);
            if (eq < 0) continue;                              // {vision} = 无值开关，只产出一个 token
            var val = inner[(eq + 1)..].Trim();
            if (val.Length == 0)
            {
                warnings.Add($"[{sec.Name}] 第 {i + 1} 行的「{name}」值是空的：按无值开关处理（只出 {name} 一个 token）");
                continue;
            }
            tokens.Add(val);                                   // 值取第一个 = 之后整段 → 值里带空格也不会串
        }
        return tokens;
    }

    /// <summary>把一串 token 写成 `Args1..ArgsN`（先删旧的 `ArgsN` 行，再按顺序追加；注释与别的键不动）。</summary>
    internal static void WriteArgs(Doc doc, string tag, IReadOnlyList<string> args)
    {
        var sec = doc.Find(tag);
        if (sec is null)
        {
            sec = new Section { Name = tag };
            doc.Sections.Add(sec);
            doc.Sections.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        }
        sec.Body.RemoveAll(l => KeyOf(l) is { } k && k.Length > ArgsKeyPrefix.Length
                                   && k.StartsWith(ArgsKeyPrefix, StringComparison.OrdinalIgnoreCase)
                                   && int.TryParse(k[ArgsKeyPrefix.Length..], out _));
        if (args.Count == 0) return;
        int at = LastKeyLineIndex(sec.Body);
        for (int i = 0; i < args.Count; i++)
            sec.Body.Insert(at + i, KeyLine(ArgsKeyPrefix + (i + 1), args[i]));
    }

    /// <summary>整份文本（CRLF，无 BOM 的那一份由调用方定编码）。</summary>
    internal static string Text(Doc doc) => string.Join("\r\n", Render(doc)) + "\r\n";
}
