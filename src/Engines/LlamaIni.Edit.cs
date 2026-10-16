using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace MultiLauncher;

// llama ini 的就地读-改-写（DEC-079：EditSection 与它的 onBeforeWrite 钩子）。
// 2026-10-15 从 LlamaIni.cs 整段搬出，一行未改。
internal static partial class LlamaIni
{
    // ---------------- 就地读-改-写（DEC-079） ----------------
    // 为什么不能沿用 File.WriteAllLines：本机那份 llama_models.ini 是 **LF 换行、无 BOM、12 行带行尾空格**，
    //   而 WriteAllLines 在 Windows 上写 CRLF —— 那样每一行都变 = 整文件 diff，正好违背「没动的行逐字节原样」。
    //   所以这里按字节读进来，切成「一行 + 这一行自带的行尾」，改完再原样拼回去（BOM、文件末尾有没有换行也照抄）。
    // 三条拒绝：段名空或 `[*]`、键不在白名单、文件或段不存在 —— 一律不写并报错，不猜、不新建段、不新建文件。

    /// <summary>一行文本 + 它自己带的行尾（"\n" / "\r\n" / 末行可以没有）。保真就靠这个拆法。</summary>
    internal readonly struct IniLine
    {
        public readonly string Text;
        public readonly string Ending;
        public IniLine(string text, string ending) { Text = text; Ending = ending; }
    }

    /// <summary>一条**已经落到文件里**的改动（用户 2026-10-04 第 6 条：日志与提示要写明改了什麼）。
    /// Old = null = 这一段原来没有这个键（新增）；New = null = 这个键从段里删掉了（= 回到继承）。
    /// 两个值都是文件里的字面（布尔是 true/false），界面上的「开 / 关」口径由 MainForm 那边转。</summary>
    internal readonly struct IniChange
    {
        public readonly string Key;
        public readonly string? Old;
        public readonly string? New;
        public IniChange(string key, string? old, string? @new) { Key = key; Old = old; New = @new; }
    }

    /// <summary>一条就地编辑：Value = null 或空白 = **从该段删掉这个键**（= 回到继承，用户批复 §10.1 第③条），否则写这个值。</summary>
    internal readonly struct IniEdit
    {
        public readonly string Key;
        public readonly string? Value;
        public IniEdit(string key, string? value) { Key = key; Value = value; }
    }

    // 键行 = 缩进 + 键 + 可选空格 + = + 可选空格 + 其余（值与它的尾随空格都落在 rest 里，原样留着）
    static readonly Regex KeyRx = new(
        @"^(?<ind>[ \t]*)(?<key>[^ \t=][^=]*?)(?<b>[ \t]*)=(?<a>[ \t]*)(?<rest>.*)$", RegexOptions.Compiled);

    static bool IsComment(string t) => t.Length > 0 && (t[0] == '#' || t[0] == ';');
    static bool IsHeader(string t) => t.Length >= 2 && t[0] == '[' && t[^1] == ']';

    /// <summary>这一行是不是「键 = 值」行（空行、`#`/`;` 注释、段头都不算）。注释行里带 = 号（本机尾部那串 `# ====`）
    /// 会被正则当成键，所以判注释必须走在正则前面。</summary>
    static bool KeyOf(string text, out string key)
    {
        key = "";
        var t = text.TrimStart();
        if (t.Length == 0 || IsComment(t)) return false;
        var m = KeyRx.Match(text);
        if (!m.Success) return false;
        key = m.Groups["key"].Value.Trim();
        return key.Length > 0;
    }

    static string ValOf(string text)
    {
        var m = KeyRx.Match(text);
        return m.Success ? m.Groups["rest"].Value.Trim() : "";
    }

    static List<IniLine> ReadLines(string path, out bool bom)
    {
        var bytes = File.ReadAllBytes(path);
        bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        var text = Encoding.UTF8.GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
        var list = new List<IniLine>();
        int i = 0, n = text.Length;
        while (i < n)
        {
            int s = i;
            while (i < n && text[i] != '\n' && text[i] != '\r') i++;
            int bodyEnd = i;
            string ending = "";
            if (i < n)
            {
                if (text[i] == '\r' && i + 1 < n && text[i + 1] == '\n') { ending = "\r\n"; i += 2; }
                else { ending = text[i].ToString(); i++; }
            }
            list.Add(new IniLine(text[s..bodyEnd], ending));
        }
        if (list.Count == 0) list.Add(new IniLine("", ""));   // 空文件也要能表示（免得就地写凭空造出内容）
        return list;
    }

    static byte[] RenderBytes(List<IniLine> lines, bool bom)
    {
        var sb = new StringBuilder();
        foreach (var l in lines) sb.Append(l.Text).Append(l.Ending);
        var body = Encoding.UTF8.GetBytes(sb.ToString());
        if (!bom) return body;
        var pre = Encoding.UTF8.GetPreamble();
        var all = new byte[pre.Length + body.Length];
        pre.CopyTo(all, 0); body.CopyTo(all, pre.Length);
        return all;
    }

    /// <summary>文件里第一个用到的行尾（本机那份全 LF 就得到 "\n"）；整文件没有行尾就退回 "\n"。</summary>
    static string DominantEnding(List<IniLine> lines)
    {
        foreach (var l in lines) if (l.Ending.Length > 0) return l.Ending;
        return "\n";
    }

    /// <summary>段的位置 = [段头行号, 下一个段头行号)；找不到返回 null（就地写据此拒绝新建段）。</summary>
    static (int Start, int End)? FindSection(List<IniLine> lines, string section)
    {
        int start = -1;
        for (int i = 0; i < lines.Count; i++)
        {
            var t = lines[i].Text.Trim();
            if (t.Length == 0 || IsComment(t) || !IsHeader(t)) continue;
            var name = t[1..^1].Trim();
            if (start < 0) { if (string.Equals(name, section, StringComparison.OrdinalIgnoreCase)) start = i; }
            else return (start, i);
        }
        return start < 0 ? null : (start, lines.Count);
    }

    public static bool SectionExists(string iniPath, string section)
    {
        if (string.IsNullOrWhiteSpace(iniPath) || !File.Exists(iniPath)) return false;
        try { return FindSection(ReadLines(iniPath, out _), section.Trim()) is not null; }
        catch { return false; }
    }

    /// <summary>「全局」= 文件头（第一个段之前）+ `[*]` 段；两处都算，同名键以 `[*]` 为准。
    /// 别的模型段里的键**不算**全局（zone 一改成别的段就跳过，注释与段头行也跳过）。</summary>
    public static Dictionary<string, string> ReadGlobalValues(string iniPath)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(iniPath) || !File.Exists(iniPath)) return map;
        try
        {
            int zone = 0;   // 0 = 文件头，1 = [*]，2 = 别的模型段
            foreach (var l in ReadLines(iniPath, out _))
            {
                var t = l.Text.Trim();
                if (t.Length == 0 || IsComment(t)) continue;
                if (IsHeader(t)) { zone = t[1..^1].Trim() == "*" ? 1 : 2; continue; }
                if (zone == 2) continue;
                if (!KeyOf(t, out var k)) continue;
                map[k] = ValOf(t);
            }
        }
        catch { }
        return map;
    }

    /// <summary>「段 → 全局 → 空」这条读序的唯一判据（空 = 两处都没写 = 引擎默认）。
    /// Inherited=true 表示这个值是继承来的，界面上要标出来（交接文档 §2 第 5 条：不摊平 + 继承要看得见）。</summary>
    public static (string Value, bool Inherited) EffectiveValue(
        Dictionary<string, string> sectionVals, Dictionary<string, string> globalVals, string key)
    {
        if (sectionVals.TryGetValue(key, out var s) && s.Trim().Length > 0) return (s.Trim(), false);
        if (globalVals.TryGetValue(key, out var g) && g.Trim().Length > 0) return (g.Trim(), true);
        return ("", false);
    }

    /// <summary>布尔读取按 llama.cpp 自己的字面表（DEC-145 对齐它 `is_truthy` 那一套，大小写不敏感）：
    /// 真 = on / enabled / true / 1。以前壳这边多认一个 `yes`（引擎其实拒它 —— 段里写 = yes 服务会起不来，
    /// 界面上却显"开"）= 旧口径已去掉；写入固定 true / false。</summary>
    public static bool IsTrue(string? v) => (v ?? "").Trim().ToLowerInvariant() is "on" or "enabled" or "true" or "1";

    /// <summary>假字面表那一半（引擎 `is_falsey` 的同款）。表外字两边都不认 —— 调用方自己兜"解析不了"。</summary>
    public static bool IsFalsey(string? v) => (v ?? "").Trim().ToLowerInvariant() is "off" or "disabled" or "false" or "0";

    public static string BoolText(bool v) => v ? "true" : "false";

    /// <summary>
    /// 就地改「选中模型那一段」里的若干键。只动这些键所在的行：段里已有的键**原位改值**（保留这一行原来的
    /// 缩进与 `=` 两侧风格、键也照文件里的写法），段里没有的键插到段尾空行与注释块之前，Value 给空 = 删掉整行；
    /// 其余行逐字节原样（换行风格、BOM、行尾空格、注释、段顺序一概不动）。
    /// changed = 实际改动的行数；为 0 时一个字节都不写（「继承不摊平」，也就没必要备份）。
    /// </summary>
    /// <param name="onBeforeWrite">真要动笔前的钩子（写备份就在这里）。返回非 null = 取消这次写入、
    /// 那个字符串是给用户看的原因，**原文件一个字节都不碰**；没变化时压根不会调它。</param>
    /// <param name="details">可选：逐条改动明细（键、旧值、新值；见 <see cref="IniChange"/>）。
    /// 只在写成功时才填，条数与 changed 一致；调用方用它把"改了什麼"说清楚（DEC-079 的日志只报数量，
    /// 用户 2026-10-04 第 6 条要求写明「top-k 20 → 15」这样的事实）。</param>
    public static bool EditSection(string iniPath, string section, IReadOnlyList<IniEdit> edits,
        IReadOnlyCollection<string> allowedKeys, out int changed, out string error,
        Func<string, string?>? onBeforeWrite = null, List<IniChange>? details = null)
    {
        changed = 0; error = "";
        section = (section ?? "").Trim();
        if (string.IsNullOrWhiteSpace(iniPath)) { error = "没给 ini 路径。"; return false; }
        if (!File.Exists(iniPath)) { error = "找不到文件：" + iniPath; return false; }
        if (section.Length == 0 || section == "*") { error = "只写选中模型那一段：不写 [*]、不新建段。"; return false; }
        var allow = new HashSet<string>(allowedKeys, StringComparer.OrdinalIgnoreCase);
        foreach (var e in edits)
        {
            var k = e.Key.Trim();
            if (k.Length == 0 || !allow.Contains(k)) { error = "这个键壳不写：" + e.Key; return false; }
        }
        var tmp = iniPath + ".tmp";
        try
        {
            var lines = ReadLines(iniPath, out bool bom);
            var sec = FindSection(lines, section);
            if (sec is null) { error = "文件里没有 [" + section + "] 这一段（壳不新建段）：" + iniPath; return false; }
            int start = sec.Value.Start, end = sec.Value.End;

            var pending = new List<IniEdit>();
            var rec = new List<IniChange>();          // 明细先记在本地，只有真写成功了才交给调用方（失败/取消 = 空）
            foreach (var e in edits)
            {
                var key = e.Key.Trim();
                var val = (e.Value ?? "").Trim();
                bool hit = false;
                for (int i = start + 1; i < end; i++)
                {
                    if (!KeyOf(lines[i].Text, out var have)) continue;
                    if (!string.Equals(have, key, StringComparison.OrdinalIgnoreCase)) continue;
                    hit = true;
                    if (val.Length == 0)
                    {
                        rec.Add(new IniChange(have, ValOf(lines[i].Text), null));   // 留空 = 删键
                        lines.RemoveAt(i); end--; i--; changed++; continue;
                    }
                    var m = KeyRx.Match(lines[i].Text);
                    if (m.Groups["rest"].Value.Trim() == val) continue;    // 值本来就是这个 = 一个字不动（含尾随空格）
                    rec.Add(new IniChange(have, m.Groups["rest"].Value.Trim(), val));
                    lines[i] = new IniLine(
                        m.Groups["ind"].Value + have + m.Groups["b"].Value + "=" + m.Groups["a"].Value + val,
                        lines[i].Ending);
                    changed++;
                }
                if (hit || val.Length == 0) continue;
                pending.Add(e);                                           // 段里没有这个键、又给了值 → 待插
            }
            if (pending.Count > 0)
            {
                // 插入点 = 段里最后一个键行之后（= 段尾空行与注释块之前），照抄当年 CloseSection 的摘尾思路
                int at = start + 1;
                for (int i = start + 1; i < end; i++) if (KeyOf(lines[i].Text, out _)) at = i + 1;
                var nl = DominantEnding(lines);
                var add = pending.Select(p => new IniLine(p.Key.Trim() + " = " + (p.Value ?? "").Trim(), nl)).ToList();
                foreach (var p in pending)
                    rec.Add(new IniChange(p.Key.Trim(), null, (p.Value ?? "").Trim()));    // 段里原来没这个键 = 新增
                if (at > 0 && lines[at - 1].Ending.Length == 0)
                {
                    lines[at - 1] = new IniLine(lines[at - 1].Text, nl);   // 原末行没换行：补给它，末尾留到最后一条新键
                    add[^1] = new IniLine(add[^1].Text, "");
                }
                lines.InsertRange(at, add);
                changed += add.Count;
            }
            if (changed == 0) return true;                                 // 什么都没变：不写文件、也不必备备份

            // 真要动笔了才给调用方一次机会（写前备份挂在这里）。它返回错误 = "备份没做成"，
            // 那就一个字都不写：宁可这次没落盘，也不能留下一份退不回去的家底。
            if (onBeforeWrite is not null)
            {
                var block = onBeforeWrite(iniPath);
                if (block is not null) { error = block; changed = 0; return false; }
            }

            var bytes = RenderBytes(lines, bom);
            File.WriteAllBytes(tmp, bytes);
            File.Move(tmp, iniPath, overwrite: true);                      // 同目录改名 = 不会留下半截家底
            details?.AddRange(rec);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* 半截临时文件删不掉也只是垃圾 */ }
            return false;
        }
    }
}
