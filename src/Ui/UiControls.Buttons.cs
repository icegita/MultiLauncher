using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Drawing.Drawing2D;

namespace MultiLauncher;

// RoundedTextBox（内层输入转发成外层 TextChanged，BUG-017）与 FlatButton。
// 2026-10-15 从 UiControls.cs 整段搬出，一行未改。
// 圆角输入框：自绘圆角底 + 内部无边框 TextBox（写文字仍走原生控件，输入法/选区都正常）。
// WinForms 的 TextBox 不支持圆角，这是必须自绘的一块。
internal sealed class RoundedTextBox : Control
{
    readonly TextBox _box = new() { BorderStyle = BorderStyle.None };

    public Color FillColor = UiTheme.Input;
    public Color BorderColor = UiTheme.Border;
    public Color Backdrop = UiTheme.Window;
    public int Radius = 8;

    public RoundedTextBox()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
               | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        _box.BackColor = FillColor;
        _box.ForeColor = UiTheme.Text;
        _box.Font = UiTheme.Mono(9.5f);
        Controls.Add(_box);
        // 把内层 TextBox 的变化转发成自己的 TextChanged：外层所有联动都挂在这个事件上
        // （设置窗「改值→写入 _s」、主界面端口→API 地址/内部端口自动跟）。不转发就一次都不触发（BUG-017）。
        _box.TextChanged += (_, _) => OnTextChanged(EventArgs.Empty);
        Height = UiTheme.S(this, 30);
    }

    [AllowNull]
    public override string Text { get => _box.Text; set => _box.Text = value ?? ""; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ReadOnly { get => _box.ReadOnly; set => _box.ReadOnly = value; }

    public TextBox Inner => _box;

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        int padX = UiTheme.S(this, 8);
        int h = _box.PreferredHeight;
        _box.SetBounds(padX, Math.Max(0, (Height - h) / 2), Math.Max(1, Width - padX * 2), h);
    }

    protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Backdrop);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = ClientRectangle;
        r.Width -= 1; r.Height -= 1;
        using var path = UiTheme.RoundRect(r, UiTheme.S(this, Radius));
        using var brush = new SolidBrush(FillColor);
        g.FillPath(brush, path);
        using var pen = new Pen(BorderColor, 1f);
        g.DrawPath(pen, path);
    }
}

// 实底按钮：圆角 + 居中文字 + hover 变色
internal sealed class FlatButton : Control
{
    public Color FillColor = UiTheme.Brand;
    public Color HoverColor = UiTheme.BrandHover;
    public Color TextColor = UiTheme.BrandOn;
    public Color BorderColor = Color.Empty;    // 空 = 用填充色描边（看不出边框）；次要按钮设成 UiTheme.Border
    public Color Backdrop = UiTheme.Window;   // 次要按钮：Backdrop=卡片色、FillColor=UiTheme.Input、TextColor=UiTheme.Text
    public int Radius = 8;
    public bool TextLeft;                     // 文字靠左（原型 .navitem 没有 text-align，只有 .primary 居中）
    // DEC-177：只画字形、不打底不描边（左栏「模块状态」那枚折叠箭头 —— 他点名"不需要任何按钮背景，
    // 只保留图形，但点击范围可以保留"）。命中区仍是整块控件，所以点击范围不变。
    // **悬停不换色**（DEC-179 收尾：他点名"不需要提绿，保持原版色即可"）—— 所以这里没有 `HoverInk` 这一档，
    // Bare 与非 Bare 的墨迹都只吃 `TextColor`；"可按"的信号留给手型光标 + 点下去整块变形。
    public bool Bare;
    public bool ShowGear;                     // 文字前画一个矢量齿轮（见 DrawGear）
    public bool ShowSwap;                     // 文字后画一个矢量「⇄」（模式切换按钮用）

    // ---------------- 悬停擦入（2026-10-16 用户批：方案文档 §7，源动效 = Uiverse「alexroumi」那枚 ::before 宽度 0→100%）----------------
    // 只取「一层底从左擦进来」那一条，其余三处按我们自己的口径改掉：
    //   · **文字全程不换色** —— DEC-179 已删 HoverInk，非 Bare 档的 hover 本来就只换底不换墨（BrandOn 在两档绿上、
    //     White 在两档红上都读得清），CSS 那句 `color: #e8e8e8` 在这套里没有对应物；
    //   · **描边不跟着擦** —— 最后整条画终态色（`BorderColor` 为空时它本来就等于底色那一档）；
    //   · **退场反向擦回**（2026-10-16 他实跑点名"离开好像没有动画"）—— 上一轮勾的是"瞬回"，实机读下来
    //     像是坏了；改成与入场同一条曲线同一个时长反向走，从**当前 k** 起步（划过一排按钮时它会中途翻面，
    //     不跳变 = Toast.BeginDismiss 同一条纪律）。要回瞬回只改一个数：把 WipeMs 那档当 0 用。
    //   · 曲线走 UiMotion.EaseInOut（位移类），时长 150ms。
    // 可见度按 DEC-125 那条门槛量过：Brand→BrandHover Δ28、Danger→DangerHover Δ27.6 擦得出来；
    // 副按钮 Input(#2A2A2A)→Border(#333333) 只差 9 级 = 基本看不见，用户知情后仍要三类全上（§7.4 第 1 条记的是知情选择）。
    internal const int WipeMs = UiMotion.FollowMs;      // 跟随类那一档（150ms），与胶囊滑动 / 叠放悬停同一处
    System.Windows.Forms.Timer? _wipe;
    int _wipeStartTick;
    double _k, _kFrom, _kTo;                  // _k = 当前擦入进度（0 = 完全不吃 HoverColor，1 = 全吃）
    static int _wipeLive;                     // 存活计数（ThinProgress / SegmentedControl 同形状）：建 +1、Dispose −1

    /// <summary>出图用：把擦入进度钉死，拍"常态 / 擦到一半 / 终态"三帧（静态截图本来拍不到 hover 反馈，
    /// experience §12 那一条）。终态那一帧 = 1，与旧的 HoverForTest 同一张图 —— 进度本来就是唯一的画法开关，
    /// 不再另立一枚布尔（上一版那枚在改成"离场也反向擦回"之后会变成死档）。</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal double WipeKForTest { get => _k; set { _k = _kTo = Math.Clamp(value, 0, 1); Invalidate(); } }
    internal static int WipeLiveForTest => _wipeLive;

    /// <summary>这一枚到底播不播（纯函数 = 画与自检共用这一个家）：
    /// Bare 档连底都不画（DEC-177 那枚折叠箭头）、禁用态两档同吃 Input 无差可擦、两档同色也没有可擦的东西。</summary>
    internal static bool WipeApplies(bool bare, bool enabled, Color fill, Color hover) =>
        !bare && enabled && !fill.Equals(hover);

    public FlatButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
               | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Font = UiTheme.Ui(9f);
        Cursor = Cursors.Hand;
        Height = UiTheme.S(this, 32);
        _wipeLive++;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _wipe?.Stop(); _wipe?.Dispose(); _wipe = null; _wipeLive--; }   // 带句柄的协作对象点名释放
        base.Dispose(disposing);
    }

    // 悬停状态不再单独存一份布尔：擦入进度 _k 就是唯一的画法开关（进场往 1 爬、离场往 0 爬、落定即 0），
    // 多一个 _hover 反而会在"离场还在爬"那一拍与 _k 打架（上一版就是这么把反向擦回吃掉的）。
    protected override void OnMouseEnter(EventArgs e) { StartWipe(1); Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { StartWipe(0); Invalidate(); base.OnMouseLeave(e); }

    /// <summary>把擦入进度往 to（0 / 1）推一段。判据不过 = 一枚表都不动；起点取当前值 = 中途翻面不跳变。</summary>
    void StartWipe(double to)
    {
        if (!WipeApplies(Bare, Enabled, FillColor, HoverColor)) return;
        if (_kTo == to && _wipe is { Enabled: true }) return;   // 同一目标重复触发（进出抖动）不再重起一轮
        _kTo = to;
        _kFrom = _k;
        _wipeStartTick = Environment.TickCount;
        _wipe ??= new System.Windows.Forms.Timer { Interval = UiMotion.FrameMs };
        _wipe.Tick -= OnWipeTick;
        _wipe.Tick += OnWipeTick;
        _wipe.Start();
    }

    /// <summary>擦入进度（纯函数 = 画与自检共用这一个家）：从 from 走到 to、已走 elapsedMs。位移曲线吃 EaseInOut。</summary>
    internal static double WipeAt(double from, double to, int elapsedMs) => UiMotion.RampAt(from, to, elapsedMs, WipeMs);

    void OnWipeTick(object? sender, EventArgs e)
    {
        int el = Environment.TickCount - _wipeStartTick;
        _k = WipeAt(_kFrom, _kTo, el);
        if (el >= WipeMs) { _k = _kTo; _wipe?.Stop(); }
        Invalidate();
    }

    /// <summary>直接落位（不播）：灰置那一档两色同吃 Input，没有可擦的东西。</summary>
    void SettleWipe() { _wipe?.Stop(); _k = _kTo = 0; _kFrom = 0; }

    // 齿轮直径与图标-文字间距（逻辑像素）：调用方算按钮宽度时要用
    public const int GearLogicalSize = 15;
    public const int GearGapLogical = 6;
    public const int SwapLogicalSize = 11;
    public int GearBlockWidth => UiTheme.S(this, GearLogicalSize + GearGapLogical);
    public int SwapBlockWidth => UiTheme.S(this, SwapLogicalSize + GearGapLogical);

    // UserPaint 控件改 Text 不会自动重绘：切换模式/启停时按钮文案与颜色都变了，
    // 屏幕却可能停在旧样子（"按钮未及时更新"，用户实跑反馈 2026-10-01）。
    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

    // 同族：Enabled 变化也要立刻重绘（启用/禁用两套画法差很远；DIP 批次里"忙态灰置"就靠它）
    protected override void OnEnabledChanged(EventArgs e)
    {
        if (!Enabled) SettleWipe();   // 灰下去那一档两色同吃 Input，没有可擦的东西 —— 顺手把表停掉，别留空跑的定时器
        Invalidate();
        base.OnEnabledChanged(e);
    }

    // 原生 Button 收到 BM_CLICK(0x00F5) 会点一下，自绘按钮没有这个过程 —— 于是 ui-probe.ps1 -Click
    // 这类自动化点不动它（5a 验证时实测：消息到了、Click 不触发）。补上：语义 = 用户点击。
    // 2026-10-04（DEC-074）：禁用态必须挡住——「保存」按钮的"置灰不可点击"得在自动化嘴上也算数。
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
        var r = ClientRectangle;
        r.Width -= 1; r.Height -= 1;
        // 禁用态（升级/回退进行中要"按实际状态灰置"）：底色压平、文字转灰、描边给一档边界。
        // 不渲染的话 Enabled=false 只是点不动、看着还是亮的——用户会以为没生效（2026-10-03 反馈）。
        bool off = !Enabled;
        // 擦入进度只由 _k 决定：进场往 1 爬、离场往 0 爬、落定即 0 —— 不再拿"当前是不是悬停"当门，
        // 那样离场那一段会被直接画回常态（= 他这次报的"离开没有动画"）。三处不该动的地方一律按 0 画：
        // Bare 不打底 / 禁用无差可擦 / 两档同色没东西可擦。
        double k = off || !WipeApplies(Bare, Enabled, FillColor, HoverColor) ? 0 : Math.Clamp(_k, 0, 1);
        var baseFill = off ? UiTheme.Input : FillColor;      // 擦之前那一层
        var fill = off ? UiTheme.Input : HoverColor;         // 擦进来那一层 = 原来的 hover 终态
        // 描边不跟着擦（整条画一个色），取"过半就算到位"那一档 —— 免得离场那一拍外圈还停在 hover 色上
        var edge = k >= 0.5 ? fill : baseFill;
        var line = off ? UiTheme.Border : (BorderColor.IsEmpty ? edge : BorderColor);
        var ink = off ? Color.FromArgb(0x5C, 0x62, 0x6C) : TextColor;   // 墨迹全程不换色（DEC-179 删的就是这一档）
        using var path = UiTheme.RoundRect(r, UiTheme.S(this, Radius));
        if (!Bare)
        {
            using var brush = new SolidBrush(baseFill);
            g.FillPath(brush, path);
            if (k > 0)
            {
                // 从左擦进来的那一层：裁一条宽按 k 的竖带，带内改画 HoverColor（硬边、不羽化 = 原动效那条边）。
                // 裁剪区用 g.Save/Restore 成对还原 —— Region 是 GDI 对象，本项目对这类配额有账（AGENTS 那一行）。
                int band = (int)Math.Round(Width * k);
                if (band > 0)
                {
                    var st = g.Save();
                    g.SetClip(new Rectangle(0, 0, band, Height), CombineMode.Replace);
                    using var over = new SolidBrush(fill);
                    g.FillPath(over, path);
                    g.Restore(st);
                }
            }
            using var pen = new Pen(line, 1f);
            g.DrawPath(pen, path);      // 描边不跟着擦：整条画终态色
        }

        int padX = UiTheme.S(this, 12);
        int lead = ShowGear ? GearBlockWidth : 0;
        int trail = ShowSwap ? SwapBlockWidth : 0;
        var tw = TextRenderer.MeasureText(g, Text, Font, new Size(int.MaxValue, Height), TextFormatFlags.NoPadding);
        int total = lead + tw.Width + trail;
        int x = TextLeft ? padX : Math.Max(padX, (Width - total) / 2);
        if (ShowGear)
            DrawGear(g, x + UiTheme.S(this, GearLogicalSize) / 2, Height / 2, UiTheme.S(this, GearLogicalSize), ink);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(x + lead, 0, Math.Max(1, tw.Width + 2), Height), ink,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        if (ShowSwap)
            DrawSwap(g, x + lead + tw.Width + UiTheme.S(this, GearGapLogical), Height / 2, ink,
                UiTheme.S(this, SwapLogicalSize));
    }

    // 矢量「⇄」：上下两条反向箭头，用来标"点一下切换"。字体里这个字符不一定有字形，画出来稳。
    internal static void DrawSwap(Graphics g, int x, int cy, Color color, int w)
    {
        w = Math.Max(7, w);
        int a = Math.Max(2, w / 4);              // 上下两条的偏移
        int head = Math.Max(3, w / 3);           // 箭头斜边的水平投影
        using var pen = new Pen(color, Math.Max(1.4f, w / 8f));
        g.DrawLine(pen, x, cy - a, x + w, cy - a);                                  // 上：→
        g.DrawLine(pen, x + w - head, cy - a - head / 2f, x + w, cy - a);
        g.DrawLine(pen, x + w, cy - a, x + w - head, cy - a + head / 2f);
        g.DrawLine(pen, x + w, cy + a, x, cy + a);                                  // 下：←
        g.DrawLine(pen, x + head, cy + a - head / 2f, x, cy + a);
        g.DrawLine(pen, x, cy + a, x + head, cy + a + head / 2f);
    }

    // 矢量齿轮：字体里的 "⚙" 在 Win11 上会被当 emoji 字形画（彩色、粗细与界面不成套），
    // 而原型用的是 SVG 图标 —— 所以这里自己画：8 个齿 + 中心孔（Alternate 填充挖空，透出按钮底色）。
    internal static void DrawGear(Graphics g, int cx, int cy, int size, Color color)
    {
        if (size < 6) return;
        float outer = size / 2f, inner = outer * 0.72f, hole = outer * 0.36f;
        using var p = new GraphicsPath { FillMode = FillMode.Alternate };
        var pts = new List<PointF>();
        const int teeth = 8;
        double step = Math.PI * 2 / teeth;
        for (int i = 0; i < teeth; i++)
        {
            double a = i * step - Math.PI / 2;
            pts.Add(Polar(cx, cy, inner, a));
            pts.Add(Polar(cx, cy, outer, a + step * 0.14));
            pts.Add(Polar(cx, cy, outer, a + step * 0.36));
            pts.Add(Polar(cx, cy, inner, a + step * 0.50));
        }
        p.AddPolygon(pts.ToArray());
        p.AddEllipse(cx - hole, cy - hole, hole * 2, hole * 2);
        using var b = new SolidBrush(color);
        g.FillPath(b, p);
    }

    static PointF Polar(int cx, int cy, float rad, double ang)
        => new(cx + (float)(Math.Cos(ang) * rad), cy + (float)(Math.Sin(ang) * rad));
}

// 胶囊开关：绿=开 / 深灰=关；圆点用白色（用主色会糊在绿底里看不见）
