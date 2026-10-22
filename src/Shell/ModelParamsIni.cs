using System.Text;

namespace MultiLauncher;

/// <summary>`strata_models.ini` 的行级读写（DEC-210，2026-10-22 他点名「换成 ini，一行一个键」）。
///
/// 形状 —— 一个模型一段，键名与界面那 16 格同名（与老 `[Model:&lt;tag&gt;]` 段同一套字，所以他不用学第二套）：
/// <code>
/// [iq3_s]
/// ApiModel=qwen3.8-flash-next-iq3_s
/// Context=262144
/// Kv=int8
/// ...
/// Args1=--fake-flag
/// Args2=1
/// </code>
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
internal static class ModelParamsIni
{
    /// <summary>手加参数的键前缀：`Args1` / `Args2` …（一行一个 token，他选的形态）。</summary>
    internal const string ArgsKeyPrefix = "Args";

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
            var line = pair.Key + "=" + pair.Value;
            int at = sec.Body.FindIndex(l => KeyOf(l) is { } k && k.Equals(pair.Key, StringComparison.OrdinalIgnoreCase));
            if (at >= 0) sec.Body[at] = line;
            else sec.Body.Insert(LastKeyLineIndex(sec.Body), line);
        }
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
            sec.Body.Insert(at + i, ArgsKeyPrefix + (i + 1) + "=" + args[i]);
    }

    /// <summary>整份文本（CRLF，无 BOM 的那一份由调用方定编码）。</summary>
    internal static string Text(Doc doc) => string.Join("\r\n", Render(doc)) + "\r\n";
}
