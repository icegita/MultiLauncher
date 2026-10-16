using System.Runtime.InteropServices;
using System.Text;

namespace MultiLauncher;

// 内置 Markdown 渲染器的表格分支（`| a | b |` + `|---|---|`，表头底色/表格线/胶囊态用既有色表档）。
// 2026-10-15 从 UiMarkdown.cs 整段搬出，一行未改。
internal sealed partial class MdDoc : Control
{
    // ---------------- 表格（`| a | b |` + `|---|---|`） ----------------
    // 为什么要加：内置渲染器以前只认粗体 / 行内代码 / 列表 / 标题 / 分隔线 / 链接，**表格会当普通段落排**，
    // 显示成"一堆带竖线的文字"（上游 release body 与说明书里都有表）。
    // 做法 = 走"定位画"：表行的每一段自带起始 x（`Line.Xs`），画 / 命中 / 选中三条链按它取段起点，
    // **其余算式一字不动** —— 所以非表格内容走的还是原来那条路，行为零变化。
    // 两条纪律：① 只认"表头行 + 分隔行"开头的块（不猜）；② **排不下就一行都不吃、整体退回段落**
    //（宁可排成难看的文字，也不许画糊或压字）。
    static bool IsTableRow(string s)
    {
        s = s.Trim();
        return s.Length >= 2 && s[0] == '|' && s[^1] == '|';
    }

    static bool IsTableSep(string s)
    {
        s = s.Trim();
        if (!IsTableRow(s)) return false;
        foreach (var ch in s)
            if (ch != '|' && ch != '-' && ch != ':' && ch != ' ') return false;
        return s.Contains('-');
    }

    static string[] SplitCells(string s)
    {
        s = s.Trim();
        if (s.StartsWith("|")) s = s[1..];
        if (s.EndsWith("|")) s = s[..^1];
        var parts = s.Split('|');
        for (int i = 0; i < parts.Length; i++) parts[i] = parts[i].Trim();
        return parts;
    }

    /// <summary>把若干行按列宽排成表格。**排不下返回 false 且一行都不加**（调用方退回普通段落）。
    /// 列宽 = 该列最宽单元格的自然宽，超出可用宽就按比例缩；缩到单列下限还放不下就放弃。</summary>
    bool EmitTable(List<string[]> rows, int w, Action flush)
    {
        int n = 0;
        foreach (var r in rows) n = Math.Max(n, r.Length);
        if (n < 2 || rows.Count < 2) return false;

        int gap = UiTheme.S(this, 16);          // 列间距（列与列之间）
        int pad = UiTheme.S(this, 4);           // 单元格左右内衬（不然文字贴着列边界）
        // 列槽 = 内衬 | 文字 | 内衬。**pad 必须算进槽宽**：早先只从折行宽度里扣、没算进列宽预算，
        // 于是"刚好放得下一行"的列被扣窄 2×pad，平白多折一行（他实跑截图：`llama.cpp` 折成 `llama.c` + `p`、
        // 表头「界面显示名」折成两行 —— 两处都是这种"刚好"的列）。
        int minSlot = Math.Max(UiTheme.S(this, 36), Measure("关", _f)) + pad * 2;
        if (w - (long)gap * (n - 1) < (long)minSlot * n) return false;
        var cells = new List<List<(string, Font, Color)>[]>();
        var cw = new int[n];
        for (int c = 0; c < n; c++) cw[c] = minSlot;
        foreach (var r in rows)
        {
            var rr = new List<(string, Font, Color)>[n];
            for (int c = 0; c < n; c++)
            {
                var runs = Inline(c < r.Length ? r[c] : "");
                rr[c] = runs;
                // 自然宽按**逐字量**算，与 WrapRuns 同一种算法：比例字体的整串量与逐字量不是一回事（字距），
                // 两种算法各算一套，就会出现"布局说放得下、折行说放不下"= 平白多折一行。
                int nat = 0;
                foreach (var x in runs) nat += MeasureByChar(x.Item1, x.Item2);
                cw[c] = Math.Max(cw[c], nat + pad * 2);
            }
            cells.Add(rr);
        }
        long room = w - (long)gap * (n - 1);
        long need = 0;
        foreach (var c in cw) need += c;
        // 列宽**容器驱动**：无论要缩还是要涨，都按各列自然宽等比分到 room 上。
        // 早先是"放得下就原样用自然宽"（内容驱动）→ 表格比正文窄一截、右侧一大块空白（他实跑抓到的）。
        // 等比分而不是等宽分：描述列自然拿大头，"模块"这种短列几乎不涨，看着不空。
        if (need > 0)
        {
            double k = (double)room / need;
            for (int c = 0; c < n; c++) cw[c] = Math.Max(minSlot, (int)(cw[c] * k));
        }
        long total = 0;
        foreach (var c in cw) total += c;
        if (total > room) return false;         // 缩到列槽下限还放不下 → 整体退回段落

        flush();                                 // 段落先收口，表行才接得上
        var xs = new int[n];
        int acc = 0;                             // 第 0 列的槽左缘
        for (int c = 0; c < n; c++) { xs[c] = acc + pad; acc += cw[c] + gap; }
        int bandL = 0, bandR = acc - gap;        // 带 = 从第一列槽左缘到最后一列槽右缘

        for (int ri = 0; ri < rows.Count; ri++)
        {
            var wrapped = new List<List<(string, Font, Color)>>[n];
            int maxLines = 1;
            for (int c = 0; c < n; c++)
            {
                wrapped[c] = WrapRuns(cells[ri][c], cw[c] - pad * 2);   // 折行按"去掉左右内衬"的净宽算
                maxLines = Math.Max(maxLines, wrapped[c].Count);
            }
            for (int k = 0; k < maxLines; k++)
            {
                var ln = new Line { Xs = new List<int>(), Band = ri, BandL = bandL, BandR = bandR, BandHead = ri == 0 };
                for (int c = 0; c < n; c++)
                {
                    if (k >= wrapped[c].Count) continue;
                    int x = xs[c];
                    foreach (var (text, font, color) in wrapped[c][k])
                    {
                        var f = ri == 0 ? _fb : font;      // 表头整行加粗
                        ln.Xs!.Add(x);
                        ln.Runs.Add((text, f, color));
                        x += Measure(text, f);
                    }
                    // 列与列之间补两格空白：**只为了复制出来的纯文本分得开**（画面上位置由 Xs 定，不看它）
                    if (c < n - 1) { ln.Xs!.Add(x); ln.Runs.Add(("  ", _f, UiTheme.TextMuted)); }
                }
                if (ln.Runs.Count == 0) ln.Runs.Add((" ", _f, UiTheme.Text));
                Finish(ln);
                _lines.Add(ln);
            }
            _lines[^1].BandEnd = true;           // 这一带最后一条线的下面画一条行分隔线
        }
        Gap();
        return true;
    }

    /// <summary>把一格的若干段按宽度折成多行（**逐字符量**：表格里 CJK 与半角混排，按词切不可靠）。</summary>
    List<List<(string, Font, Color)>> WrapRuns(List<(string, Font, Color)> runs, int width)
    {
        var outp = new List<List<(string, Font, Color)>>();
        var cur = new List<(string, Font, Color)>();
        var sb = new StringBuilder();
        Font curFont = _f;
        Color curColor = UiTheme.Text;
        int x = 0;
        void FlushRun()
        {
            if (sb.Length == 0) return;
            cur.Add((sb.ToString(), curFont, curColor));
            sb.Clear();
        }
        foreach (var (text, font, color) in runs)
        {
            curFont = font; curColor = color;
            foreach (var ch in text)
            {
                int cw = Measure(ch.ToString(), font);
                if (x > 0 && x + cw > width)
                {
                    FlushRun();
                    outp.Add(cur);
                    cur = new List<(string, Font, Color)>();
                    x = 0;
                }
                sb.Append(ch);
                x += cw;
            }
            FlushRun();
        }
        FlushRun();
        if (cur.Count > 0 || outp.Count == 0) outp.Add(cur);
        return outp;
    }

    Line NewLine(int indent)
    {
        var l = new Line { Indent = indent };
        _lines.Add(l);
        return l;
    }

    void Gap() => _lines.Add(new Line { Kind = 2, Height = UiTheme.S(this, 5) });

    static int S(int px) => (int)Math.Round(px * UiTheme.Dpi / 96.0);   // 全局 DPI（静态处拿不到控件）

    readonly Dictionary<Font, int> _asc = new();

    /// 一个字体的上伸部（像素）：混排时**按基线对齐**要用它。
    /// 各家字体的 ascent/height 比例不一样（Consolas 的 ascent 占比明显高于微软雅黑），
    /// 按同一顶边摆就会看到"代码段比正文高半格"（用户 2026-10-03 看图抓到的）。
    int Ascent(Font f)
    {
        if (_asc.TryGetValue(f, out int cached)) return cached;
        int v;
        try
        {
            using var ff = new FontFamily(f.Name);
            v = (int)Math.Ceiling(ff.GetCellAscent(f.Style) / (float)ff.GetEmHeight(f.Style)
                                  * f.SizeInPoints * UiTheme.Dpi / 72.0);
        }
        catch { v = f.Height; }     // 字体族拿不到（极端情况）：退回整高，至少不会崩
        _asc[f] = v;
        return v;
    }

    // 一行的实际高度 = 行内最高字号 + 行距（**每行都要算**：早先只给了最后一行，其余行高 0，整段叠在一起）
    void Finish(Line l)
    {
        int th = 0, asc = 0;
        foreach (var r in l.Runs)
        {
            th = Math.Max(th, r.Font.Height);
            asc = Math.Max(asc, Ascent(r.Font));
        }
        l.TextH = th;
        l.Ascender = asc;
        l.Height = Math.Max(th + S(5), S(18));
    }

    // 一段（或一条列表项）按宽度折行；indent 同时用作续行的悬挂缩进
    void Emit(List<(string Text, Font Font, Color Color)> runs, int indent, int w, string bullet = "")
    {
        int from = _lines.Count;
        int x = indent;
        var cur = NewLine(bullet.Length > 0 ? 0 : indent);
        if (bullet.Length > 0)
        {
            cur.Runs.Add((bullet, _f, UiTheme.TextMuted));
            x += Measure(bullet, _f);
        }
        foreach (var (text, font, color) in runs)
        {
            foreach (var tok0 in Tokens(text))
            {
                string tok = tok0;
                int tw = Measure(tok, font);
                if (x + tw > indent + w && x > indent) { cur = NewLine(indent); x = indent; }
                while (tw > w)                       // 超长整块（URL）按字符硬断
                {
                    int take = tok.Length;
                    while (take > 1 && Measure(tok[..take], font) > w) take--;
                    string head = tok[..take];
                    cur.Runs.Add((head, font, color));
                    x += Measure(head, font);
                    tok = tok[take..];
                    tw = Measure(tok, font);
                    if (tok.Length == 0) break;
                    cur = NewLine(indent); x = indent;
                }
                if (tok.Length == 0) continue;
                cur.Runs.Add((tok, font, color));
                x += tw;
            }
        }
        if (_lines.Count > from && _lines[^1].Runs.Count == 0) _lines.RemoveAt(_lines.Count - 1);
        for (int i = from; i < _lines.Count; i++) Finish(_lines[i]);
    }

    static int Measure(string s, Font f) =>
        TextRenderer.MeasureText(s, f, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width;

    /// <summary>逐字量一段字的宽度 —— **与 WrapRuns 同一种算法**（它是逐字累加的）。
    /// 列宽的"自然宽"必须用它算：比例字体的整串量与逐字量不是一回事（字距），两种算法各算一套，
    /// 就会出现"布局算的列宽说放得下、折行逐字一说放不下"= 一行被平白折成两行。</summary>
    static int MeasureByChar(string s, Font f)
    {
        int x = 0;
        foreach (var ch in s) x += Measure(ch.ToString(), f);
        return x;
    }

    // 按空格切词并保留尾随空格（这样折行不会把空格吞掉，也不会把 CJK 连续串切不开）
    static IEnumerable<string> Tokens(string t)
    {
        if (t.Length == 0) yield break;
        int start = 0;
        for (int i = 0; i < t.Length; i++)
        {
            if (t[i] != ' ') continue;
            yield return t[start..(i + 1)];
            start = i + 1;
        }
        if (start < t.Length) yield return t[start..];
    }

    // 行内：**粗体** / `代码` / [文字](链接)；HTML 标签丢掉只留文字
    static List<(string, Font, Color)> Inline(MdDoc d, string s)
    {
        var outp = new List<(string, Font, Color)>();
        s = StripHtml(s);
        var buf = new System.Text.StringBuilder();
        void Flush(Font f, Color c) { if (buf.Length > 0) { outp.Add((buf.ToString(), f, c)); buf.Clear(); } }
        for (int i = 0; i < s.Length;)
        {
            if (i + 1 < s.Length && s[i] == '*' && s[i + 1] == '*')
            {
                int end = s.IndexOf("**", i + 2, StringComparison.Ordinal);
                if (end > i + 2)
                {
                    Flush(d._f, UiTheme.Text);
                    outp.Add((s.Substring(i + 2, end - i - 2), d._fb, UiTheme.Text));
                    i = end + 2;
                    continue;
                }
            }
            if (s[i] == '`')
            {
                int end = s.IndexOf('`', i + 1);
                if (end > i)
                {
                    Flush(d._f, UiTheme.Text);
                    outp.Add((s.Substring(i + 1, end - i - 1), d._fc, UiTheme.Text));
                    i = end + 1;
                    continue;
                }
            }
            if (s[i] == '[')
            {
                int close = s.IndexOf(']', i + 1);
                if (close > i && close + 1 < s.Length && s[close + 1] == '(')
                {
                    int pe = s.IndexOf(')', close + 2);
                    if (pe > close)
                    {
                        Flush(d._f, UiTheme.Text);
                        outp.Add((s.Substring(i + 1, close - i - 1), d._f, CLink));
                        i = pe + 1;
                        continue;
                    }
                }
            }
            buf.Append(s[i]);
            i++;
        }
        Flush(d._f, UiTheme.Text);
        return outp;
    }

    List<(string, Font, Color)> Inline(string s) => Inline(this, s);

    static string StripHtml(string s)
    {
        s = System.Text.RegularExpressions.Regex.Replace(s, "<[^>]{0,400}>", "");
        return s.Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">")
                .Replace("&quot;", "\"").Replace("&#39;", "'");
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        int y = PadY - _scrollY, w = Avail, li = 0;
        foreach (var l in _lines)
        {
            if (y >= ClientSize.Height) break;
            if (y + l.Height > 0)
            {
                if (l.Kind == 1)
                {
                    using var pen = new Pen(UiTheme.Border, 1f);
                    int ly = y + l.Height / 2;
                    g.DrawLine(pen, PadX, ly, PadX + w, ly);
                }
                else if (l.Kind == 0)
                {
                    // 表格的"带"先铺底 / 画线，**再**走选中与文字 —— 顺序不能反（反了选中底色就被带底盖住）
                    if (l.Band >= 0)
                    {
                        if (l.BandHead)
                        {
                            using var hb = new SolidBrush(UiTheme.Input);
                            g.FillRectangle(hb, PadX + l.BandL, y, l.BandR - l.BandL, l.Height);
                        }
                        if (l.BandEnd)
                        {
                            using var pen = new Pen(UiTheme.Border, 1f);
                            g.DrawLine(pen, PadX + l.BandL, y + l.Height - 1, PadX + l.BandR, y + l.Height - 1);
                        }
                    }
                    DrawSelection(g, l, y, li);       // 选中底色走在文字前面（DEC-092 第 3 条）
                    int x = PadX + l.Indent;
                    // 行内文字的垂直摆位：整行居中（默认）或顶对齐——两种都给用户看过再定（DEC-051）；
                    // 行内各段**按基线对齐**（DEC-052）：矮一档的行内代码不再往上飘
                    int extra = Math.Max(0, l.Height - l.TextH);
                    int ty = y + (CenterLines ? extra / 2 : 0);
                    for (int ri = 0; ri < l.Runs.Count; ri++)
                    {
                        var r = l.Runs[ri];
                        int rx = l.Xs is not null && ri < l.Xs.Count ? PadX + l.Xs[ri] : x;   // 表行：段起点由 Xs 给
                        // 行内代码 = 胶囊态（参考 GitHub 暗色）：等宽字体 + 圆角底 + 左右各探出 3px。
                        // **文字用默认色**（有底了，品牌绿会抢）；底色用 SegOn —— 它在 Window 与表头的 Input
                        // 上都看得见（用 Input 的话在表头那一带会隐身）。认出它靠**字体身份**（_fc 全篇只此一家）。
                        if (ReferenceEquals(r.Font, _fc))
                        {
                            int cw = Measure(r.Text, r.Font);
                            using var path = UiTheme.RoundRect(
                                new Rectangle(rx - UiTheme.S(this, 3), ty, cw + UiTheme.S(this, 6), l.TextH),
                                UiTheme.S(this, 4));
                            using var cb = new SolidBrush(UiTheme.SegOn);
                            g.FillPath(cb, path);
                        }
                        TextRenderer.DrawText(g, r.Text, r.Font, new Point(rx, ty + l.Ascender - Ascent(r.Font)), r.Color,
                            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                        x = rx + Measure(r.Text, r.Font);
                    }
                }
            }
            y += l.Height;
            li++;
        }
    }

}
