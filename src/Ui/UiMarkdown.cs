using System.Runtime.InteropServices;
using System.Text;

namespace MultiLauncher;

// 发布说明的轻量渲染（DEC-050/051，用户反馈：更新内容框"还是 md 没渲染出来、滚动条异常也不工作"）。
// 只认 GitHub release body 真用得上的那几样：**粗体**、`代码`、- 列表、# 标题、--- 分隔线、[文字](链接)，
// 外加把 <a>/<img> 这类 HTML 标签丢掉（只留文字）。**不引第三方 Markdown 库**——publish 的 exe 要保持在
// 几百 KB 量级（见 UiTheme.cs 头注的纪律），装个解析库就是 MB 级。
//
// 两个类分工（DEC-051）：
//   · MdDoc   —— 只负责"按给定宽度排版 + 画"，滚动是**画时的偏移**（ScrollY），不当滚动容器；
//   · MdScroll —— 容器（圆角、无边框感）+ 细滑条 + 滚轮派发。
// 为什么不用 Panel.AutoScroll + 原生滚动条：原生条换深色主题后箭头与轨道仍残留（拖起来还掉圆角残渣），
// 而且滚轮按焦点派发、只读区没拿到焦点时根本不动——用户实跑两条都撞上了。
internal sealed partial class MdDoc : Control
{
    string _src = "";
    int _layoutWidth = -1;
    readonly List<Line> _lines = new();

    readonly Font _f, _fb, _fh, _fc;
    static readonly Color CLink = Color.FromArgb(0x7F, 0xD9, 0xA6); // 链接：绿系里再浅一档，和正文明显分开

    /// 行内垂直对齐（DEC-051 让用户看图挑）：true=按行框居中，false=顶对齐。
    public bool CenterLines = true;

    sealed class Line
    {
        internal readonly List<(string Text, Font Font, Color Color)> Runs = new();
        internal int Height;
        internal int Kind;        // 0 文字 · 1 分隔线 · 2 空行
        internal int Indent;
        internal int TextH;
        internal int Ascender;    // 行内最高的那段字的上伸部：同段其它字按基线往下挪（DEC-052）
        // 表格行才有：与 Runs 平行，每段**相对 PadX** 的起始 x（null = 老行为，逐段紧挨着画）。
        // 有它之后画/命中/选中三条链都改按它取段起点，其余算式一字不动 —— 非表格内容走的还是原来那条路。
        internal List<int>? Xs;
        // 表格"带"：同一行的多条折行共享同一个 Band 号（-1 = 不是表格行）。BandL/R = 这一带的左右边界
        //（相对客户区左边，画表头底色与行分隔线用）；BandHead = 表头带（涂 Input 底）；
        // BandEnd = 这一带的最后一条线（它下面画 Border 分隔线）。
        internal int Band = -1;
        internal int BandL, BandR;
        internal bool BandHead, BandEnd;
    }

    public MdDoc()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
               | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
               | ControlStyles.Selectable, true);      // Selectable：要能拿到焦点才收得到 Ctrl+C（DEC-092）
        Cursor = Cursors.IBeam;                         // 这块文字现在可选了，光标也该给文本形
        BackColor = UiTheme.Window;
        // 字号与界面一致（9pt）；标题只大一档，别做成"另一套设计"
        _f = new Font("Microsoft YaHei UI", 9f);
        _fb = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
        _fh = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
        _fc = new Font("Consolas", 8.5f);
    }

    // 设计器不参与（本程序没有设计器）：不标 Hidden 会被 WFO1000 当错误报
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Markdown
    {
        get => _src;
        set { _src = value ?? ""; _dirty = true; _layoutWidth = -1; MeasureLayout(); }
    }

    int PadX => UiTheme.S(this, 10);
    int PadY => UiTheme.S(this, 8);
    int Avail => Math.Max(UiTheme.S(this, 60), Width - PadX * 2);

    /// 内容总高（画布高，不是控件高）：容器拿它算滑条范围
    public int Total { get; private set; }

    int _scrollY;
    /// 滚动偏移（画时上移这么多）；由容器按滑条值驱动
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int ScrollY
    {
        get => _scrollY;
        set
        {
            // **上界也要钳**。早先只钳 0：`OnMouseMove` 里那根"拖选到上下边缘替用户滚一点"（±S(14)）
            // 直接把偏移推出「内容总高 − 可视高」之外 —— 内容被推出视野，而 `OnPaint` 开头先
            // `g.Clear(BackColor)` 再什么也画不出来 → 留下一大片空白（他实跑抓到的）。
            // 滚轮与滑条那两条路各自有钳（UiScrollbar.Set 第 53 行），唯独这条没有；补上之后三条路行为一致。
            int v = Math.Min(Math.Max(0, value), Math.Max(0, Total - Height));
            if (v == _scrollY) return;
            _scrollY = v;
            Invalidate();
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        MeasureLayout();
    }

    void MeasureLayout()
    {
        if (IsDisposed || Width <= 0) return;
        if (!_dirty && _layoutWidth == Width && _lines.Count > 0) return;
        _dirty = false;
        _layoutWidth = Width;
        _lines.Clear();
        int w = Avail;
        var para = new List<string>();

        void FlushPara()
        {
            if (para.Count == 0) return;
            // GitHub 的正文是硬换行的整段：段内拼回一句再按宽度重排（不然会出现"每行都短一截"）
            Emit(Inline(string.Join(" ", para)), 0, w);
            para.Clear();
            Gap();
        }

        var srcLines = _src.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (int si = 0; si < srcLines.Length; si++)
        {
            var line = srcLines[si].TrimEnd();
            if (line.Length == 0) { FlushPara(); continue; }
            // 表格块（表头行 + `|---|` 分隔行 + 若干数据行）。**放不下就一行都不吃**、退回普通段落
            //（= 旧行为：整块当段落文字排），所以这条分支对"排不下"的内容零影响。
            if (IsTableRow(line) && si + 1 < srcLines.Length && IsTableSep(srcLines[si + 1].TrimEnd()))
            {
                var rows = new List<string[]> { SplitCells(line) };
                int sj = si + 2;
                while (sj < srcLines.Length && IsTableRow(srcLines[sj].TrimEnd()))
                {
                    rows.Add(SplitCells(srcLines[sj].TrimEnd()));
                    sj++;
                }
                if (EmitTable(rows, w, FlushPara)) { si = sj - 1; continue; }
            }
            if (System.Text.RegularExpressions.Regex.IsMatch(line, @"^\s*(-{3,}|\*{3,}|_{3,})\s*$"))
            {
                FlushPara();
                _lines.Add(new Line { Kind = 1, Height = UiTheme.S(this, 13) });
                continue;
            }
            var h = System.Text.RegularExpressions.Regex.Match(line, @"^(#{1,6})\s+(.*)$");
            if (h.Success)
            {
                FlushPara();
                var ln = NewLine(0);
                foreach (var (txt, _, color) in Inline(h.Groups[2].Value)) ln.Runs.Add((txt, _fh, color));
                Finish(ln);
                Gap();
                continue;
            }
            var b = System.Text.RegularExpressions.Regex.Match(line, @"^\s*[-*+]\s+(.*)$");
            if (b.Success)
            {
                FlushPara();
                Emit(Inline(b.Groups[1].Value), UiTheme.S(this, 14), w, "· ");
                continue;
            }
            para.Add(line.TrimStart());
        }
        FlushPara();

        int total = PadY * 2;
        foreach (var l in _lines) total += l.Height;
        Total = total;
        BuildPlain();                          // 复制与命中测试都靠这份纯文本（DEC-092）
        ScrollY = _scrollY;                    // 上界钳走**同一个家**（setter），不在这儿再抄一遍算式
        Invalidate();
    }

    bool _dirty = true;

}
