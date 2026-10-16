using System.Drawing.Drawing2D;

namespace MultiLauncher;

// ============================================================================
// 叠放 + 下方露边 + 点一下轮换（DEC-087 形④ / DEC-088 动画 / DEC-089 露边 / DEC-090 两处共用）
// ============================================================================
// 一处公共控件，界面上两个地方用它，**不许写两遍**：
//   · 卡1 左侧那枚「后端方块」= Band=false：整枚 78×62 逻辑（150% 下 117×93），
//     面 = 46×30 逻辑居中，四边等宽（左右边距照搬上下边距 = 16 逻辑）。
//   · 左栏那枚「模式」= Band=true：形状仍通栏（侧栏只有 S(210)，塞方块右边会空一大片死区），
//     但借同一个轮换形式：当前枚 = 现有 S(32) 高、底下一枚正下方露一条边，**侧栏总高净增 0**。
//
// 三条硬口径（用户原话，别自行改判）：
//   1) 「完全覆盖不对」—— 底下一枚必须在**正下方**露出一条边（不斜 45°、也不完全藏住），那是"还有另一个"的提示；
//   2) 只剩一个可选项时**只画那一枚**（不可点、无动画、不露边）；
//   3) 动画 = 淡出 + 上移 / 淡入落位、180ms —— 帧计算是**纯函数**，定时器只负责把曲线应用到控件上
//      （同 Toast 那一族的做法：无头自检才证伪得了，靠肉眼拍动画永远拍不准）。
internal sealed class StackedToggle : Control
{
    // 尺寸口径（逻辑像素；物理 = ×DPI/96）。150% 下：方块 117×93、面 69×45、露边约 8。
    public const int CardLogicalW = 78, CardLogicalH = 62;
    public const int FaceLogicalW = 46, FaceLogicalH = 30;
    public const int BandFaceLogicalH = 32;
    public const int PeekLogical = 5;              // S(5) 在 150% 下 = 7.5 ≈ 8 物理（DEC-089 那条边）
    public const int RiseLogical = 8;              // 淡出上移的量（150% 下 ≈ 12 物理，DEC-088 那版动画）
    public const int DurationMs = 180;             // DEC-088：与 toast 出现同档

    string[] _faces = { "", "" };
    int _selected;
    System.Windows.Forms.Timer? _anim;
    int _animStartTick;                            // 这一轮起播时刻（Environment.TickCount）
    double _t;                                     // 0..1 缓过值的进度（画与断言共用这一个数，2026-10-16 改按时间取帧）

    /// <summary>圆角外露那一圈透出的是父层颜色（自绘控件的老规矩：Backdrop 必须设对，否则圆角外是"黑角"）。</summary>
    public Color Backdrop = UiTheme.Card;

    public StackedToggle()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
               | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Font = UiTheme.Ui(8.5f);
        Cursor = Cursors.Hand;
        _hwLive++;
    }

    /// <summary>通栏（左栏模式条）还是方块（卡1 后端）。只影响摆位算法，画法与动画同一套。</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Band { get; set; }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string[] Faces
    {
        get => _faces;
        set { _faces = value.Length == 1 ? new[] { value[0], "" } : new[] { value[0], value[1] }; Invalidate(); }
    }

    /// <summary>当前露在上面的那一枚（0 / 1）。只有一个可选项时恒 0 且点不动。</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Selected
    {
        get => _selected;
        set { _selected = Math.Clamp(value, 0, Solo ? 0 : 1); Invalidate(); }
    }

    public bool Solo => string.IsNullOrEmpty(_faces[1]);
    public string ActiveText => _faces[Math.Clamp(_selected, 0, _faces.Length - 1)];

    /// <summary>点当前枚 = 请求轮换（调用方可以在这里先弹确认，再决定要不要真换）。
    /// 取消的话别调 Cycle() —— 控件自己不改状态，界面也就不会被点一下就走。</summary>
    public event Action? SwapRequested;

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (Enabled && !Solo) SwapRequested?.Invoke();
        base.OnMouseDown(e);
    }

    // 自动化护栏认 Enabled（灰的按钮 BM_CLICK 也点不动，DEC-074 的口径同 FlatButton）
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x00F5) { if (Enabled && !Solo) SwapRequested?.Invoke(); return; }
        base.WndProc(ref m);
    }

    /// <summary>悬停擦入（2026-10-16 他点名"win/wsl 与 路由模式/单体模型 这两枚没适配"）：与 FlatButton 同一条
    /// 曲线、同一档时长（UiMotion.FollowMs）。原来这一枚的 hover 是 `Brand → BrandHover` 瞬切，
    /// 与按钮那一族两种手感；现在三族（实底按钮 / 叠放按钮 / 分段胶囊）共用同一处算式。
    /// 不播的两种情况：Solo（只剩一枚可选项，本来就不换色）、灰置（恒吃 Input，无差可擦）。</summary>
    internal const int HoverWipeMs = UiMotion.FollowMs;
    System.Windows.Forms.Timer? _hoverWipe;
    int _hwStartTick;
    double _hwK, _hwFrom, _hwTo;
    static int _hwLive;
    internal static int WipeLiveForTest => _hwLive;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal double WipeKForTest { get => _hwK; set { _hwK = _hwTo = Math.Clamp(value, 0, 1); Invalidate(); } }
    internal static double WipeAt(double from, double to, int elapsedMs) => UiMotion.RampAt(from, to, elapsedMs, HoverWipeMs);

    void StartHoverWipe(double to)
    {
        if (Solo || !Enabled) return;
        if (_hwTo == to && _hoverWipe is { Enabled: true }) return;
        _hwTo = to;
        _hwFrom = _hwK;
        _hwStartTick = Environment.TickCount;
        _hoverWipe ??= new System.Windows.Forms.Timer { Interval = UiMotion.FrameMs };
        _hoverWipe.Tick -= OnHoverWipeTick;
        _hoverWipe.Tick += OnHoverWipeTick;
        _hoverWipe.Start();
    }

    void OnHoverWipeTick(object? sender, EventArgs e)
    {
        int el = Environment.TickCount - _hwStartTick;
        _hwK = WipeAt(_hwFrom, _hwTo, el);
        if (el >= HoverWipeMs) { _hwK = _hwTo; _hoverWipe?.Stop(); }
        Invalidate();
    }

    // 悬停状态不另存布尔：_hwK 就是唯一的画法开关（与 FlatButton 同一课 —— 掺一层 _hover 门会把反向擦回吃掉）
    protected override void OnMouseEnter(EventArgs e) { StartHoverWipe(1); Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { StartHoverWipe(0); Invalidate(); base.OnMouseLeave(e); }
    protected override void OnEnabledChanged(EventArgs e)
    {
        if (!Enabled) { _hoverWipe?.Stop(); _hwK = _hwTo = 0; }   // 灰置那一档无差可擦，顺手停表
        Invalidate();
        base.OnEnabledChanged(e);
    }

    /// <summary>调用方确认要换之后调它：换状态 + 播一次动画。</summary>
    public void Cycle()
    {
        if (Solo) { Selected = 0; return; }
        _selected = _selected == 0 ? 1 : 0;
        StartAnim();
    }

    /// <summary>把当前枚设成指定值，值真变了才播动画（程序化推值不该每刷一次界面就动一下）。</summary>
    public void Select(int index, bool animate = true)
    {
        index = Math.Clamp(index, 0, Solo ? 0 : 1);
        if (index == _selected) { Invalidate(); return; }
        _selected = index;
        if (animate) StartAnim(); else Invalidate();
    }

    void StartAnim()
    {
        // 2026-10-16 收口（§5.1）：进度改按**实际经过的时间**取，曲线改吃 UiMotion 那一处（原来是 12 帧除尽 + 线性）。
        // 心跳仍是 15ms 那一档；改的只是"这一拍对应的 t 是多少" —— WM_TIMER 被消息泵合并几拍，总时长不再跟着漂。
        _t = 0;
        _animStartTick = Environment.TickCount;
        _anim ??= new System.Windows.Forms.Timer { Interval = UiMotion.FrameMs };
        _anim.Tick -= OnAnimTick;
        _anim.Tick += OnAnimTick;
        _anim.Start();
        Invalidate();
    }

    void OnAnimTick(object? sender, EventArgs e)
    {
        double raw = (Environment.TickCount - _animStartTick) / (double)DurationMs;
        _t = UiMotion.EaseOutCubic(raw);
        if (raw >= 1) { _anim?.Stop(); _t = 1; }
        Invalidate();
    }

    // ---------------- 帧计算（纯函数，自检直接喂 t 断起止点与单调性） ----------------
    // 这两条式子对**参数**是线性的，但喂进来的 t 已经是 UiMotion.EaseOutCubic 缓过的值（见 OnAnimTick）
    // —— 所以复合出来的曲线与 toast 出现那一族同一条，而端点与单调性一个字没变，原有语义照旧可断。

    /// <summary>t = 0..1（缓过值的进度）。出场那枚：不透明度 1→0、同时向上飘 Rise。</summary>
    internal static (double Alpha, double Dy) OutFrame(double t) => (1 - t, -RiseLogical * t);

    /// <summary>入场那枚：从下方 Rise 处淡入并落回 0（t=0 时不透明度 0、t=1 时 1 且位置归零）。</summary>
    internal static (double Alpha, double Dy) InFrame(double t) => (t, RiseLogical * (1 - t));

    internal static (double OutA, double OutDy, double InA, double InDy) FramesAt(double t)
    {
        var o = OutFrame(t); var i = InFrame(t);
        return (o.Alpha, o.Dy, i.Alpha, i.Dy);
    }

    // ---------------- 画 ----------------

    /// <summary>方块那档的外框 = 整枚控件；通栏那档不画外框（父面板已经是卡片色）。</summary>
    Rectangle FaceBounds()
    {
        int peek = UiTheme.S(this, PeekLogical);
        if (Band)
        {
            int fh = UiTheme.S(this, BandFaceLogicalH);
            return new Rectangle(0, 0, Width, fh);          // 底下一枚就从这条边往下露
        }
        int w = UiTheme.S(this, FaceLogicalW), h = UiTheme.S(this, FaceLogicalH);
        return new Rectangle((Width - w) / 2, (Height - h - peek) / 2 + peek / 2, w, h);   // 居中，下面留出 peek
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // 方块：自己就是那枚小卡（卡片色 + 描边）；通栏：只把父层颜色铺平，不再描一圈
        var g = e.Graphics;
        g.Clear(Backdrop);
        if (Band) return;
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using var p = UiTheme.RoundRect(r, UiTheme.S(this, 12));
        using var b = new SolidBrush(UiTheme.Card);
        g.FillPath(b, p);
        using var pen = new Pen(UiTheme.Border, 1f);
        g.DrawPath(pen, p);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var face = FaceBounds();
        int peek = UiTheme.S(this, PeekLogical);
        int rise = UiTheme.S(this, RiseLogical);
        bool animating = _anim is { Enabled: true };
        double t = animating ? Math.Clamp(_t, 0, 1) : 1.0;
        var (outA, outDy, inA, inDy) = FramesAt(t);
        int dyOut = (int)Math.Round(outDy / RiseLogical * rise);
        int dyIn = (int)Math.Round(inDy / RiseLogical * rise);
        string other = _faces[_selected == 0 ? 1 : 0];
        // 灰置那一档（模块/后端不可切时）：与 FlatButton 同口径 —— 压平底色 + 灰字，别只挡点击不动画没差别
        bool off = !Enabled;
        // 悬停擦入进度只由 _hwK 决定（不掺 _hover）：离场那一段 _hwK 还在往回爬，掺门就会把反向擦回吃掉
        double wk = off || Solo ? 0 : Math.Clamp(_hwK, 0, 1);
        var curFill = off ? UiTheme.Input : UiTheme.Brand;
        var curLine = off ? UiTheme.Border : (wk >= 0.5 ? UiTheme.BrandHover : curFill);
        var curInk = off ? Color.FromArgb(0x5C, 0x62, 0x6C) : UiTheme.BrandOn;

        // ① 底下一枚：正下方露一条边（"还有另一个"的提示；不斜 45°、也不是完全覆盖）
        // DEC-125③：这一枚的底色/描边从 Input(#2A2A2A) 换成 StackedPeek(#454545) —— 坐在 Card(#232323) 上
        // 只差 7 级、用户实跑判"几乎看不出来"，而那一条边就是"还有另一个"的唯一提示。描边与底色同值：
        // 只露 5 逻辑像素，一圈更暗的描边把它压回背景里（原来是同一个问题的另一半）。
        if (!Solo)
            DrawFace(g, new Rectangle(face.X, face.Y + peek, face.Width, face.Height), other,
                     UiTheme.StackedPeek, UiTheme.StackedPeek, UiTheme.Text, 1.0);
        // ② 动画中的出场那枚：淡出 + 上移
        if (animating && outA > 0.01)
            DrawFace(g, new Rectangle(face.X, face.Y + dyOut, face.Width, face.Height), other,
                     UiTheme.StackedPeek, UiTheme.StackedPeek, UiTheme.Text, outA);
        // ③ 当前枚：淡入落位（没有动画时就是稳稳贴在 face 位置）；悬停时从左擦进 BrandHover 那一档
        DrawFace(g, new Rectangle(face.X, face.Y + (animating ? dyIn : 0), face.Width, face.Height),
                 _faces[_selected], curFill, curLine, curInk, animating ? inA : 1.0, wk);
    }

    void DrawFace(Graphics g, Rectangle r, string text, Color fill, Color line, Color ink, double alpha, double wipe = 0)
    {
        if (r.Height <= 0) return;
        int a = (int)Math.Clamp(alpha * 255, 0, 255);
        using (var path = UiTheme.RoundRect(r, UiTheme.S(this, 8)))
        {
            using (var b = new SolidBrush(Color.FromArgb(a, fill)))
                g.FillPath(b, path);
            // 悬停擦入：裁一条宽按 wipe 的竖带，带内改画 BrandHover（硬边、与 FlatButton 同一画法）
            if (wipe > 0)
            {
                int band = (int)Math.Round(r.Width * Math.Clamp(wipe, 0, 1));
                if (band > 0)
                {
                    var st = g.Save();
                    g.SetClip(new Rectangle(r.X, r.Y, band, r.Height), CombineMode.Replace);
                    using var over = new SolidBrush(Color.FromArgb(a, UiTheme.BrandHover));
                    g.FillPath(over, path);
                    g.Restore(st);
                }
            }
            using var pen = new Pen(Color.FromArgb(a, line), 1f);
            g.DrawPath(pen, path);
        }
        TextRenderer.DrawText(g, text, Font, r, Color.FromArgb(a, ink),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
    }

    // ---------------- 自检 / 出图出口 ----------------

    /// <summary>当前枚的矩形与底下一枚露出的高度（物理像素）。DEC-090 要断"露边 8 物理"与"侧栏总高净增 0"，
    /// 这两个数必须量得出来，不能靠肉眼看图。</summary>
    internal (int FaceTop, int FaceBottom, int FaceWidth, int FaceHeight, int Peek, int ControlHeight) GeometryForTest()
    {
        var f = FaceBounds();
        return (f.Top, f.Bottom, f.Width, f.Height, UiTheme.S(this, PeekLogical), Height);
    }

    internal bool AnimatingForTest => _anim is { Enabled: true };
    internal double ProgressForTest => _t;
    internal void FinishAnimForTest() { _anim?.Stop(); _t = 1; Invalidate(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _anim?.Stop(); _anim?.Dispose(); _anim = null;                 // DEC-061：带句柄的协作对象点名释放
            _hoverWipe?.Stop(); _hoverWipe?.Dispose(); _hoverWipe = null; _hwLive--;
        }
        base.Dispose(disposing);
    }
}
