using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Drawing.Drawing2D;

namespace MultiLauncher;

// ToggleSwitch 与 CheckItem（按 square-check 重画的方框）。
// 2026-10-15 从 UiControls.cs 整段搬出，一行未改。
internal sealed class ToggleSwitch : Control
{
    bool _on;
    public Color Backdrop = UiTheme.Window;

    // ---------------- 滑块行程（2026-10-16 用户给参考动效点名要：Uiverse「zanina-yassine」那枚开关）----------------
    // 参考里动的只有两样：圆点从左到右、轨道换色。**轨道换色我们不跟着插值** —— 走「位置动、透明度动、颜色瞬切」
    // 那条口径（Input→Brand 一旦插值就造出色表外的中间绿）。所以这一枚只动圆点。
    // 时长照他给的 0.2s；曲线走 UiMotion.EaseInOut（位移类，理由见那里）。
    // 只有用户那一击才播：程序化推值（切页 / 恢复备份 / 换后端那一族 PushCard2）一律落位，同 StackedToggle 口径。
    internal const int KnobMs = 200;
    System.Windows.Forms.Timer? _knob;
    int _knobStartTick;
    double _knobT, _knobFrom, _knobTo;        // _knobT = 圆点行程进度（0 = 关那一头，1 = 开那一头）
    static int _knobLive;                     // 存活计数（ThinProgress / FlatButton / SegmentedControl 同形状）

    /// <summary>自检用：还活着的 ToggleSwitch 有几枚（定时器漏 Dispose 会被这条抓到）。</summary>
    internal static int KnobLiveForTest => _knobLive;
    /// <summary>出图用：把圆点钉在某一个行程进度上拍"走到一半"那一帧。</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal double KnobTForTest { get => _knobT; set { _knobT = _knobTo = Math.Clamp(value, 0, 1); Invalidate(); } }

    /// <summary>圆点该画在哪一格（纯函数 = 画与自检共用这一个家）。两端 = 原来那两条写死的表达式。</summary>
    internal static int KnobX(int width, int dot, int pad, double t)
    {
        int lo = pad + 1, hi = System.Math.Max(lo, width - dot - pad - 1);
        return (int)System.Math.Round(lo + (hi - lo) * System.Math.Clamp(t, 0, 1));
    }

    // 2026-10-04（DEC-074）：「保存」按钮的脏状态要跟着开关走——原先是裸字段，没有变更通知。
    // 语义与 WinForms CheckBox.CheckedChanged 一致：值真的变了才发（程序化推视图也发，调用方自己消化）。
    public event EventHandler? CheckedChanged;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Checked
    {
        get => _on;
        set
        {
            bool changed = _on != value;
            _on = value;
            if (changed) SettleKnob();          // 程序化推值 = 落位（切页 / 恢复备份 / 换后端那一族都走这条路）
            Invalidate();
            if (changed) CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public ToggleSwitch()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
               | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Size = new Size(UiTheme.S(this, 36), UiTheme.S(this, 20));
        Cursor = Cursors.Hand;
        _knobLive++;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _knob?.Stop(); _knob?.Dispose(); _knob = null; _knobLive--; }   // 带句柄的协作对象点名释放
        base.Dispose(disposing);
    }

    protected override void OnClick(EventArgs e)
    {
        double from = _knobT;                   // 先记当前进度：下面那句 setter 会把它落位到终点
        Checked = !Checked;
        if (_knobT != from) StartKnob(from);    // 值真变了（且不是被外部事件改回去）才播这一程
        base.OnClick(e);
    }

    void StartKnob(double from)
    {
        _knobFrom = from;
        _knobTo = _on ? 1 : 0;
        _knobT = from;
        _knobStartTick = Environment.TickCount;
        _knob ??= new System.Windows.Forms.Timer { Interval = UiMotion.FrameMs };
        _knob.Tick -= OnKnobTick;
        _knob.Tick += OnKnobTick;
        _knob.Start();
    }

    /// <summary>圆点行程进度（纯函数 = 画与自检共用这一个家）。</summary>
    internal static double KnobAt(double from, double to, int elapsedMs) =>
        UiMotion.Ramp(from, to, UiMotion.EaseInOut(elapsedMs / (double)KnobMs));

    void OnKnobTick(object? sender, EventArgs e)
    {
        int el = Environment.TickCount - _knobStartTick;
        _knobT = KnobAt(_knobFrom, _knobTo, el);
        if (el >= KnobMs) { _knob?.Stop(); _knobT = _knobTo; }
        Invalidate();
    }

    void SettleKnob() { _knob?.Stop(); _knobT = _knobFrom = _knobTo = _on ? 1 : 0; }

    // 与 FlatButton 同一条：自绘控件不认 BM_CLICK，ui-probe.ps1 -Click 点不动（BUG-016）；
    // 禁用态挡住（同 FlatButton 的护栏，DEC-074）
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x00F5) { if (Enabled) OnClick(EventArgs.Empty); return; }
        base.WndProc(ref m);
    }

    protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Backdrop);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int pad = UiTheme.S(this, 2);
        var track = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var path = UiTheme.RoundRect(track, track.Height / 2))
        {
            using var brush = new SolidBrush(_on ? UiTheme.Brand : UiTheme.Input);
            g.FillPath(brush, path);
            using var pen = new Pen(_on ? UiTheme.Brand : UiTheme.Border, 1f);
            g.DrawPath(pen, path);
        }
        int d = Height - pad * 2;
        // 圆点位置按行程进度插值（两端就是原来那两条写死的表达式）；轨道与圆点的**颜色仍瞬切**
        int x = KnobX(Width, d, pad, _knobT);
        using var knob = new SolidBrush(_on ? Color.White : UiTheme.TextMuted);
        g.FillEllipse(knob, x, pad, d, d);
    }
}

// 复选项（DEC-125②：状态栏那一排「显示 / 平均」用它 —— 两档的都是复选语义，不是开关的"开/关"）。
// 形状 = 圆角方框 + 右侧文字，沿用本项目一套：自绘、零依赖、尺寸全走 S()、Backdrop 必须设对（否则圆角外是黑角）。
// 与 ToggleSwitch 同一条纪律：BM_CLICK 护栏认 Enabled（灰的按钮自动化也点不动，DEC-074）、值真变了才发 CheckedChanged。
//
// DEC-127③（他点名"复选框样式太丑,样式参考这个,颜色用我们自己的品牌色"，给的形状 = tabler square-check 那枚）：
//   · 选中 = **品牌绿圆角实底 + 勾做成镂空**（镂空那两条透出的是这一枚的 Backdrop，不是白色 —— 与 SVG 里
//     勾是"挖掉的那一块"同一个道理，白勾在这套深色里比字还亮，原注释那条顾虑照旧成立）；
//   · 未选中 = **同尺寸同圆角**的框，底色 Input、描边 Border —— 他原话"未选中状态也要一起修改,别光改一个"，
//     所以两态共用同一个几何（方角→圆角一起换），不许只换亮的那一态；
//   · hover：实底转 BrandHover、描边转 TextMuted（都在既有色表里，**不新增颜色**）。
// 三个判据抽成纯函数（CheckFill/CheckLine/CheckOutline），自检按"两态都在、勾是镂空"穷举，改回方角或白勾就红。
internal sealed class CheckItem : Control
{
    bool _on;
    bool _hover;
    string _caption = "";
    public Color Backdrop = UiTheme.Window;
    // DEC-129④：caption 的颜色可指定 —— 次级用法（"实时/平均"这种子选项）降到 TextMuted 一档；
    // 字号本来就跟着实例的 Font 走（构对象时先给 Font 再给 Caption，SizeToFit 才量得准）。
    public Color CaptionColor = UiTheme.Text;

    // 方框那枚的尺寸与圆角（逻辑像素）：14 → 16（DEC-127 圆角化）→ **13 / 2**（DEC-129②：他实跑"太大了"，
    // 整体缩 20%：16×0.8 = 12.8 ≈ 13、圆角 3×0.8 = 2.4 ≈ 2）
    internal const int BoxLogical = 13;
    internal const int BoxRadiusLogical = 2;

    public event EventHandler? CheckedChanged;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Checked
    {
        get => _on;
        set
        {
            bool changed = _on != value;
            _on = value;
            Invalidate();
            if (changed) CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>右侧那行字。**不叫 Text** —— Control.Text 是继承来的（同名会藏掉它并吃一条 CS0114，
    /// 而自绘控件本来就不走基类的文字绘制），这一族的标签类控件一律用 Caption（口径同 FlatButton）。</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Caption
    {
        get => _caption;
        set { _caption = value ?? ""; SizeToFit(); Invalidate(); }
    }

    public CheckItem()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
               | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Font = UiTheme.Ui(9f);
        Cursor = Cursors.Hand;
        TabStop = false;
    }

    /// <summary>宽 = 方框 + 间距 + 文字 + 一点余量（设置页那一排按右缘摆，宽度要自己报得出）。</summary>
    void SizeToFit()
    {
        int box = UiTheme.S(this, BoxLogical), gap = UiTheme.S(this, 6);
        int tw = TextRenderer.MeasureText(_caption, Font, Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
        Size = new Size(box + gap + tw + UiTheme.S(this, 2), UiTheme.S(this, 20));
    }

    /// <summary>方框那一段的矩形（自检量"命中区比字形大一圈"用）。</summary>
    internal Rectangle BoxRectForTest()
        => new(0, (Height - UiTheme.S(this, BoxLogical)) / 2, UiTheme.S(this, BoxLogical), UiTheme.S(this, BoxLogical));

    /// <summary>出图用：把 hover 那一档推上去拍一张（真机是鼠标自己触发的；判据仍是同一个 CheckFill/CheckOutline）。</summary>
    internal void HoverForTest(bool on) { _hover = on; Invalidate(); }

    /// <summary>框子里那层实底的颜色（纯函数 = 判据唯一家；画与自检共用）。
    /// 选中 → 品牌绿（hover 转深一档）；未选中 → Input。**灰掉时两态都不给品牌色**。</summary>
    internal static Color CheckFill(bool on, bool enabled, bool hover) => !enabled
        ? UiTheme.Input
        : on ? (hover ? UiTheme.BrandHover : UiTheme.Brand) : UiTheme.Input;

    /// <summary>描边颜色（纯函数）。未选中：Border，hover 提到 TextMuted（看得见"这是一枚能点的框"）；
    /// 选中：与实底同色 = 没有第二圈线（SVG 那一枚只有一层形状）。</summary>
    internal static Color CheckOutline(bool on, bool enabled, bool hover) => !enabled
        ? UiTheme.Border
        : on ? (hover ? UiTheme.BrandHover : UiTheme.Brand)
             : (hover ? UiTheme.TextMuted : UiTheme.Border);

    /// <summary>勾那一笔的颜色 = **这一枚坐落的底色**（镂空，透出 Backdrop），不是白色、也不是第二个颜色。
    /// 灰掉时实底转 Input，勾就得比 Input 暗一点才读得出"有勾但点不动"，那一档退回 Border。</summary>
    internal static Color CheckLine(bool enabled, Color backdrop) => enabled ? backdrop : UiTheme.Border;

    // DEC-131②（他点名"勾选只检测 box、其余地方全是拖动锚点"）：**勾选只认方框** —— 点在框上才勾/取消；
    // 点在字上、空白处不勾（那一下交给外层的拖动线）。判据抽成一个纯函数（画与断言同一个家）。
    // 鼠标路与合成路分开：BM_CLICK（自动化）依旧直接勾、不吃这个判定。
    internal bool DragForwardWired;      // 证人位：外层把这枚的"非框区"接给拖动时置 true（自检读它）

    protected override void OnMouseDown(MouseEventArgs e)
    {
        _suppressMouseClick = !ClickTogglesAt(e.Location);
        base.OnMouseDown(e);
    }

    protected override void OnClick(EventArgs e)
    {
        bool suppress = _suppressMouseClick;
        _suppressMouseClick = false;      // 无论怎么进来的都先归零（别把上一次那笔账记到下一次）
        if (suppress) return;             // 点在字上/空白：不勾（那一下是拖动的）
        if (Enabled) Checked = !Checked;
        base.OnClick(e);
    }

    /// <summary>这一点算不算"点着方框"（DEC-131② 的唯一判据：勾选只认它；其余地方归拖动）。</summary>
    internal bool ClickTogglesAt(Point p) => BoxRectForTest().Contains(p);

    bool _suppressMouseClick;

    // DEC-132（他："悬浮时,复选框亮起,会给人一个点击就能选中的错觉,不应该亮起"）：悬停高亮**只认方框** ——
    // 光标在字上/空白（= 拖动锚点、点了本来就不勾）时不亮；与 `ClickTogglesAt` 同一判据。
    // DEC-133（他："拖拽图标不用变,保持默认即可"）：光标也收口到同一判据 —— **框上=手形（能勾）、别处=默认箭头**；
    // 给拖动区配"十字箭头"那一套退役（拖动照旧、只是不给特殊光标）。
    void UpdatePointerAt(Point p)
    {
        Cursor = ClickTogglesAt(p) ? Cursors.Hand : Cursors.Default;
        bool lit = ClickTogglesAt(p);
        if (_hover != lit) { _hover = lit; Invalidate(); }
    }
    internal void UpdatePointerForTest(Point p) => UpdatePointerAt(p);   // 自检驱动同一个家
    internal bool HoverLitForTest => _hover;

    protected override void OnMouseEnter(EventArgs e) { UpdatePointerAt(PointToClient(Control.MousePosition)); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        UpdatePointerAt(e.Location);      // 光标 + 高亮都只认方框（DEC-132 / DEC-133）
        base.OnMouseMove(e);
    }

    protected override void WndProc(ref Message m)
    {
        // BM_CLICK（0x00F5）= 合成点击（自动化用）：不经过"点在框里"的判定，直接勾
        if (m.Msg == 0x00F5) { if (Enabled) Checked = !Checked; return; }
        base.WndProc(ref m);
    }

    protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Backdrop);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var box = BoxRectForTest();
        int radius = Math.Max(1, UiTheme.S(this, BoxRadiusLogical));
        // 描边往内 0.5px 收，1px 线才落在像素上（同 RoundedPanel 那一族的做法）
        var outline = new Rectangle(box.X, box.Y, box.Width - 1, box.Height - 1);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using (var path = UiTheme.RoundRect(outline, radius))
        {
            using (var fill = new SolidBrush(CheckFill(_on, Enabled, _hover))) g.FillPath(fill, path);
            using var pen = new Pen(CheckOutline(_on, Enabled, _hover), 1f);
            g.DrawPath(pen, path);
        }
        if (_on)
        {
            // 勾 = 挖空（透出 Backdrop），线宽按方框等比走、端点与拐角都圆 —— tabler 那一枚的形状
            using var ink = new Pen(CheckLine(Enabled, Backdrop), Math.Max(2, box.Width * 14 / 100))
            { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round,
              LineJoin = System.Drawing.Drawing2D.LineJoin.Round };
            int x = box.X, y = box.Y, w = box.Width, h = box.Height;
            g.DrawLines(ink, new[]
            {
                new Point(x + w * 26 / 100, y + h * 52 / 100),
                new Point(x + w * 43 / 100, y + h * 69 / 100),
                new Point(x + w * 76 / 100, y + h * 34 / 100),
            });
        }
        TextRenderer.DrawText(g, _caption, Font,
            new Rectangle(box.Right + UiTheme.S(this, 6), 0, Width - box.Right - UiTheme.S(this, 8), Height),
            Enabled ? CaptionColor : UiTheme.TextMuted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
    }
}

// 1px 分隔线（键值列表、设置窗分组用）
