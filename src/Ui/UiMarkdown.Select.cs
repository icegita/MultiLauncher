using System.Runtime.InteropServices;
using System.Text;

namespace MultiLauncher;

// 选中与复制（DEC-092 第 3 条：拖选高亮 + Ctrl+C/A、位置↔字符下标复用排版量）。
// 2026-10-15 从 UiMarkdown.cs 整段搬出，一行未改。
internal sealed partial class MdDoc : Control
{
    // ---------------- 选中与复制（DEC-092 第 3 条，用户："里面的内容现在无法选中复制了"） ----------------
    // 为什么以前能、现在不能：DEC-050/051 把这段说明从只读 TextBox 换成了自绘的 MdDoc，
    // 而自绘只做"排版 + 画"，没有任何命中测试，所以鼠标拖过去什么都不会发生。
    // 这里不重做布局：排版本来就把**每行由哪几段(runs)、每段多宽**都算好了，
    // 于是位置 <=> 字符下标的换算按行按段量一次就有（与 OnPaint 用的是同一个 Measure）。
    // 下标空间 = 整篇的纯文本（Plain，行间用 \n 连），拖选/复制/Ctrl+A 全用这一套整数区间。

    /// 选中底色：品牌绿压暗一档（与 Window #1E1E1E 同系）。蓝色系那档在本项目被否过（DEC-052 行内代码）。
    static readonly Color SelBack = Color.FromArgb(0x1F, 0x47, 0x2E);
    readonly List<int> _lineStart = new();       // 每条文字行在 Plain 里的起始下标
    string _plain = "";
    int _selA = -1, _selB = -1;                  // 锚点 / 活动端（-1 = 没有选中）
    bool _selecting;

    /// 整篇的纯文本（渲染后的文字，不含 ** 与反引号）。行间以 \n 相连。
    public string Plain => _plain;
    int SelFrom => Math.Min(_selA, _selB);
    int SelTo => Math.Max(_selA, _selB);
    /// 当前选中的文字（没选 = 空串）。
    public string SelectedText => _selA < 0 || _selB < 0 || _selA == _selB
        ? "" : _plain.Substring(SelFrom, SelTo - SelFrom);

    // 排版完顺带把纯文本与各行的起点算出来（复制与命中测试都靠它，所以跟 _lines 同生同灭）
    void BuildPlain()
    {
        _lineStart.Clear();
        var sb = new StringBuilder();
        foreach (var l in _lines)
        {
            _lineStart.Add(sb.Length);
            if (l.Kind == 0)
                foreach (var r in l.Runs) sb.Append(r.Text);
            sb.Append('\n');
        }
        if (sb.Length > 0) sb.Length--;           // 末尾那个换行不是内容
        _plain = sb.ToString();
        _selA = _selB = Math.Max(-1, Math.Min(_selA, _plain.Length));
        _selB = Math.Max(-1, Math.Min(_selB, _plain.Length));
    }

    /// 客户区坐标 -> 纯文本下标（点在行上方=本篇开头，点在行尾之后=该行末尾）。
    public int PosAt(Point pt)
    {
        int y = PadY - _scrollY;
        for (int i = 0; i < _lines.Count; i++)
        {
            var l = _lines[i];
            if (pt.Y < y) return _lineStart[i];                       // 落在这一行之上
            if (pt.Y <= y + l.Height) return _lineStart[i] + CharsInLine(l, pt.X);
            y += l.Height;
        }
        return _plain.Length;
    }

    // 行内：按段的累计宽度定位字符（与 OnPaint 的摆位同一条算式：先减掉缩进，再逐段量）
    int CharsInLine(Line l, int x)
    {
        if (l.Kind != 0) return 0;
        int px = x - PadX - l.Indent;
        if (px <= 0) return 0;
        int used = 0, chars = 0;
        for (int i = 0; i < l.Runs.Count; i++)
        {
            var r = l.Runs[i];
            if (l.Xs is not null && i < l.Xs.Count) used = l.Xs[i] - l.Indent;   // 表行：段起点由 Xs 给
            int rw = Measure(r.Text, r.Font);
            if (px > used + rw) { used += rw; chars += r.Text.Length; continue; }
            int lo = 0;
            while (lo < r.Text.Length && Measure(r.Text[..(lo + 1)], r.Font) <= px - used) lo++;
            return chars + lo;
        }
        return chars + l.Runs.Sum(r => r.Text.Length);
    }

    // 把 [SelFrom,SelTo) 这段落到某一行上并画底色（没有交集就什么都不画）
    void DrawSelection(Graphics g, Line l, int lineTop, int li)
    {
        if (_selA < 0 || _selB < 0 || SelFrom == SelTo || li >= _lineStart.Count) return;
        int ls = _lineStart[li], le = ls + l.Runs.Sum(r => r.Text.Length);
        // **换成行内坐标再比**：SelFrom/SelTo 是整篇纯文本的下标，而下面的 rs/re 是从 0 数起的行内下标。
        // 原来直接拿整篇下标与行内下标比，只有第一行（行首=0）能对上交集 → 用户实跑的"只能选中第一行"：
        // 底色只画在第一行，选中的数据其实是对的（DEC-092 那张出图只选了开头 46 个字，正好落在第一行里，所以没暴露）。
        int a = Math.Max(SelFrom, ls) - ls, b = Math.Min(SelTo, le) - ls;
        if (a >= b) return;
        using var br = new SolidBrush(SelBack);
        int pad = UiTheme.S(this, 1);
        int top = lineTop + pad, h = Math.Max(UiTheme.S(this, 12), l.Height - pad * 2);
        int x = PadX + l.Indent, chars = 0;
        for (int i = 0; i < l.Runs.Count; i++)
        {
            var r = l.Runs[i];
            if (l.Xs is not null && i < l.Xs.Count) x = PadX + l.Xs[i];      // 表行：段起点由 Xs 给
            int rs = chars, re = chars + r.Text.Length;
            if (re > a && rs < b)
            {
                int from = a <= rs ? 0 : Measure(r.Text[..(a - rs)], r.Font);
                int to = b >= re ? Measure(r.Text, r.Font) : Measure(r.Text[..(b - rs)], r.Font);
                g.FillRectangle(br, x + from, top, to - from, h);
            }
            x += Measure(r.Text, r.Font);
            chars = re;
        }
    }

    /// 全选（双击界面也走这条）。
    public void SelectAll() { if (_plain.Length == 0) return; _selA = 0; _selB = _plain.Length; Invalidate(); }

    /// 把选中复制到剪贴板（Ctrl+C 走这里）。返回有没有真的复制走。
    public bool CopyToClipboard()
    {
        var t = SelectedText;
        if (t.Length == 0) return false;
        try { Clipboard.SetText(t); return true; }
        catch { return false; }       // 剪贴板被别家占着（CLIPBRD_E_CANT_OPEN）：不崩、下一次再试
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        Focus();                                     // 拿不到焦点就收不到 Ctrl+C
        if (e.Clicks >= 2) { SelectAll(); return; }   // 双击 = 全选（说明框里"选一个词"没意义）
        _selA = _selB = PosAt(e.Location);
        _selecting = true;
        Capture = true;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_selecting) return;
        _selB = PosAt(e.Location);
        int edge = UiTheme.S(this, 10);               // 拖到上下边缘时替用户滚一点，长说明不用来回松手
        if (e.Y < edge) ScrollY = _scrollY - UiTheme.S(this, 14);
        else if (e.Y > Height - edge) ScrollY = _scrollY + UiTheme.S(this, 14);
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _selecting = false;
        Capture = false;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.C)) { CopyToClipboard(); return true; }
        if (keyData == (Keys.Control | Keys.A)) { SelectAll(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // 自检用：直接按下标区间设选中（不经过鼠标，150% DPI 下也稳定）
    internal void SetSelectionForTest(int from, int to)
    {
        _selA = Math.Max(0, Math.Min(from, _plain.Length));
        _selB = Math.Max(0, Math.Min(to, _plain.Length));
        Invalidate();
    }
    internal int PosAtForTest(Point pt) => PosAt(pt);
    internal string SelectedTextForTest => SelectedText;
    /// <summary>自检用：把"按下 → 拖动 → 松手"这一串真走一遍（走的就是 OnMouseDown/Move/Up 本体，
    /// 所以 Capture、边缘自动滚动、BuildPlain 里的钳位这些副作用都在）。
    /// DEC-097：上一轮只验过 SetSelectionForTest（绕过鼠标），结果用户实跑报"只能选中第一行"没被抓到。</summary>
    internal void DragForTest(Point from, Point to)
    {
        OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, from.X, from.Y, 0));
        OnMouseMove(new MouseEventArgs(MouseButtons.Left, 1, to.X, to.Y, 0));
        OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, to.X, to.Y, 0));
    }
    internal bool SelectingForTest => _selecting;
    internal string PlainForTest => _plain;

    /// <summary>自检用：给 `ScrollY` 赋一个离谱的值，回报上界与最终落在哪。
    /// 上界不钳的后果 = 拖选到边缘的自动滚动把内容推出视野、`g.Clear` 之后画不出东西 → 一大片空白。</summary>
    internal (int Max, int Got) ScrollYClampForTest(string md, int width, int value)
    {
        Markdown = md;
        Width = width;
        Relayout();
        ScrollY = value;
        return (Math.Max(0, Total - Height), _scrollY);
    }

    /// <summary>自检用：按给定宽度把一段 markdown 排一遍，回报"表格有没有被认出来"以及"带"（表头底色 / 分隔线 / 内衬）。
    /// 走的就是 `MeasureLayout` 本体（设好 Width 即可），**不另写一条排版路径** —— 判据与真机同一家。</summary>
    internal (int TextLines, int TableLines, int MaxXs, bool HeaderBold, string Plain,
              int HeadBands, int BandEnds, int BandR, int MinBandR, int MinCellPad,
              int CodeRuns, int CodeRunsBranded) TableProbeForTest(string md, int width)
    {
        Markdown = md;
        Width = width;
        Relayout();
        int text = 0, tbl = 0, maxXs = 0, head = 0, ends = 0, bandR = 0, minBand = int.MaxValue, minPad = int.MaxValue, code = 0, codeBranded = 0;
        bool bold = false;
        foreach (var l in _lines)
        {
            if (l.Kind != 0) continue;
            text++;
            foreach (var r in l.Runs)
                if (ReferenceEquals(r.Font, _fc) && r.Text.Trim().Length > 0)
                {
                    code++;
                    if (r.Color == UiTheme.Brand) codeBranded++;
                }
            if (l.Xs is null) continue;
            tbl++;
            maxXs = Math.Max(maxXs, l.Xs.Count);
            minPad = Math.Min(minPad, l.Xs.Count > 0 ? l.Xs[0] : minPad);
            bandR = Math.Max(bandR, l.BandR);
            minBand = Math.Min(minBand, l.BandR);
            if (l.BandHead)
            {
                head++;
                foreach (var r in l.Runs)
                    if (ReferenceEquals(r.Font, _fb) && r.Text.Trim().Length > 0) bold = true;
            }
            if (l.BandEnd) ends++;
        }
        return (text, tbl, maxXs, bold, _plain, head, ends, bandR,
                minBand == int.MaxValue ? 0 : minBand, minPad == int.MaxValue ? 0 : minPad,
                code, codeBranded);
    }

    /// <summary>自检用：表格里每一段文字**实际画出去**的右边界，相对"下一段起点 / 带右边界"的最大越界量（px）。
    /// **&lt;= 0 = 没有重叠。** 这就是"表格里的文字不得重叠"的判据本体 —— 不靠看截图，直接量坐标。
    /// 表格行之外的行（Paragraph / 列表）不走这里：它们不是定位画，本来就没有"越过下一列"这回事。</summary>
    internal int TableOverflowForTest(string md, int width)
    {
        Markdown = md;
        Width = width;
        Relayout();
        int worst = 0;
        foreach (var l in _lines)
        {
            if (l.Kind != 0 || l.Xs is null || l.Xs.Count == 0) continue;
            for (int i = 0; i < l.Runs.Count; i++)
            {
                int x0 = l.Xs[Math.Min(i, l.Xs.Count - 1)];
                int x1 = x0 + Measure(l.Runs[i].Text, l.Runs[i].Font);
                int next = i + 1 < l.Runs.Count ? l.Xs[Math.Min(i + 1, l.Xs.Count - 1)] : l.BandR;
                worst = Math.Max(worst, x1 - next);
            }
        }
        return worst;
    }

    /// 排版宽度变了要重排（容器改大小、滑条出现/消失都会走到这里）
    public void Relayout() { _dirty = true; MeasureLayout(); }
}

// 更新说明的滚动容器：圆角面板（与窗体同底色，不再是一块深色"卡片"）+ 细滑条 + 滚轮派发。
// 完全不用原生滚动条，所以没有"拖动时掉圆角残渣"的问题；滚轮按**光标位置**派给本区域，
// 且只在**本区域所属的窗是前台窗**时消化消息——否则模态弹窗后面的设置页会把滚轮抢走（用户实跑抓到）。
internal sealed class MdScroll : RoundedPanel
{
    public MdDoc Doc { get; } = new() { Dock = DockStyle.Fill };
    readonly UiScrollbar _bar = new() { Backdrop = UiTheme.Window };
    WheelFilter? _wheel;

    public MdScroll()
    {
        Backdrop = UiTheme.Window;
        FillColor = UiTheme.Window;
        BorderColor = UiTheme.Border;
        Radius = 8;
        Controls.Add(Doc);
        Controls.Add(_bar);
        _bar.BringToFront();
        _bar.Scrolled += (_, _) => Doc.ScrollY = _bar.Value;
        Layout += (_, _) => Place();
        HandleCreated += (_, _) =>
        {
            // DEC-061：幂等注册。这条过滤器是**应用级**的、只在 Disposed 里摘掉最后一个，
            // 而 HandleCreated 在句柄销毁重建时会再发一次（改 RTL、父窗 RecreateHandle 那种）——
            // 不挡就会叠上第二个，同一击滚轮被消化两次（看起来像"滚一格走两格"）。
            if (_wheel is not null) return;
            _wheel = new WheelFilter(Doc, _bar);
            Application.AddMessageFilter(_wheel);
        };
        Disposed += (_, _) => { try { if (_wheel is not null) Application.RemoveMessageFilter(_wheel); } catch { } };
    }

    void Place()
    {
        int w = UiTheme.S(this, 12), pad = UiTheme.S(this, 5);
        _bar.SetBounds(Width - w - pad, pad, w, Math.Max(UiTheme.S(this, 20), Height - pad * 2));
        Sync();
    }

    void Sync()
    {
        if (!IsHandleCreated) return;
        int page = Math.Max(1, Doc.ClientSize.Height);
        _bar.Set(Math.Max(1, Doc.Total), page, Doc.ScrollY);
        _bar.Visible = _bar.Scrollable;
    }

    // 内容变了（换一段说明）也要重算范围
    public void SetMarkdown(string md) { Doc.Markdown = md; Doc.Relayout(); Sync(); }

    internal UiScrollbar BarForTest => _bar;

    sealed class WheelFilter : IMessageFilter
    {
        const int WM_MOUSEWHEEL = 0x020A;
        readonly MdDoc _doc;
        readonly UiScrollbar _bar;

        public WheelFilter(MdDoc doc, UiScrollbar bar) { _doc = doc; _bar = bar; }

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != WM_MOUSEWHEEL) return false;
            if (_doc.IsDisposed || !_doc.Visible || !_bar.Scrollable) return false;
            if (!_doc.RectangleToScreen(_doc.ClientRectangle).Contains(Cursor.Position)) return false;
            // 只有"这块内容所属的窗是前台窗"时才消化：否则弹窗还开着，后面的设置页会把它抢走
            var top = Control.FromHandle(GetForegroundWindow());
            if (top is not null && !ReferenceEquals(top.FindForm(), _doc.FindForm())) return false;
            _bar.ScrollByWheel((short)(m.WParam.ToInt64() >> 16));
            return true;
        }
    }

    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();
}
