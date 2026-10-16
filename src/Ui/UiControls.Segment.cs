using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Drawing.Drawing2D;

namespace MultiLauncher;

// HairLine（发丝线）与 SegmentedControl（页签胶囊）。
// 2026-10-15 从 UiControls.cs 整段搬出，一行未改。
internal sealed class HairLine : Control
{
    public Color LineColor = UiTheme.Border;
    public Color Backdrop = UiTheme.Card;

    public HairLine()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
               | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Height = 1;
    }

    protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Backdrop);

    protected override void OnPaint(PaintEventArgs e)
    {
        using var pen = new Pen(LineColor, 1f);
        e.Graphics.DrawLine(pen, 0, 0, Width, 0);
    }
}

// 页头模块分段控件（B 段，原型 .segw）：一排格子，激活格浅底 + 白字；点击切格并触发 ValueChanged。
// 自绘控件不认 BM_CLICK（BUG-016）：这里没有补 0x00F5 —— 自动化请按格心坐标发 WM_LBUTTONDOWN/UP（驱动脚本用法）。
internal sealed class SegmentedControl : Control
{
    public Color Backdrop = UiTheme.Window;
    public event EventHandler? ValueChanged;

    // ---------------- 激活胶囊滑动（2026-10-16 用户批：方案文档 §5.2）----------------
    // 跑道 A：只在 OnPaint 里把胶囊矩形按进度插值。**不参与布局、不改尺寸、不换色**
    //（胶囊底色恒取 SegOn，文字色瞬切 —— 见 §2 那条「位置动、透明度动、颜色瞬切」）。
    // 只有用户点出来的那一次才播：程序化推值（切页 / 自检 / 出图 / SetItems）一律直接落位，同 StackedToggle 那条口径。
    // 起点矩形必须在改 _active **之前**捕获 —— OnMouseDown 里 ValueChanged 是同步进 SwitchModule 整块重建的。
    internal const int PillSlideMs = UiMotion.FollowMs;   // 跟随类那一档（150ms），与按钮悬停擦入同一处
    System.Windows.Forms.Timer? _anim;
    int _animStartTick;
    Rectangle _pillFrom;          // 起滑那一刻旧胶囊的矩形
    double _pillT = 1;            // 1 = 不在滑（画的时候直接用当前格的矩形）
    static int _live;             // 存活计数（ThinProgress.LiveForTest 同形状）：建 +1、Dispose −1

    /// <summary>自检用：当前有几枚 SegmentedControl 还活着（定时器漏释放会被这条抓到）。</summary>
    internal static int LiveForTest => _live;

    string[] _items = Array.Empty<string>();
    int _active;
    int _hot = -1;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int ActiveIndex
    {
        get => _active;
        set { if (value == _active) return; _active = value; SettlePill(); Invalidate(); }   // 程序化推值 = 不播动画
    }

    public string ActiveText => _active >= 0 && _active < _items.Length ? _items[_active] : "";

    // 自检用：格子文本（含未激活格）
    internal string[] ItemsForTest => _items;

    public SegmentedControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
               | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Font = UiTheme.Ui(9.5f);
        Cursor = Cursors.Hand;
        ApplyDpiHeight();
        _live++;                                   // 与 ThinProgress 同一条：带定时器的自绘控件要能被数出来有没有漏释放
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _anim?.Stop(); _anim?.Dispose(); _anim = null; _live--; }   // 带句柄的协作对象点名释放
        base.Dispose(disposing);
    }

    // 控件高 = 按钮高（S(30)）+ 胶囊两侧内缩（2×S(2)）——即**激活胶囊本身**与旁边按钮同高
    //（2026-10-02 用户反馈"胶囊tab高度太矮，需要跟其他按钮差不多高"）。
    // 注意构造时机：主窗的 _segModules 是字段初始化，构造时全局 DPI 还没 CaptureDpi（还是 96），
    // 150% 下会算成 32 物理像素、比按钮（45）矮一截——owner 必须在 DPI 捕获后重调一次本方法
    //（MainForm.BuildUi 有调用；SettingsForm 在 CaptureDpi 之后才 new，天然正确）。
    internal void ApplyDpiHeight() => Height = UiTheme.S(this, 30) + 2 * UiTheme.S(this, 2);

    // 自检用：激活胶囊的净高（= 控件高 − 上下内缩；与 OnPaint 的 cell 计算同款）
    internal int PillHeightForTest => Height - 2 * UiTheme.S(this, 2);

    /// <summary>第 i 格那枚**胶囊**的外框（内缩 S(2)；净高 = 控件高 − 2×S(2) = 按钮高）。
    /// 画胶囊、捕获滑动起点、自检量落位共用这一个家。内缩 S(2) 那条是 2026-10-02 反馈定的，别动。</summary>
    Rectangle PillRect(int i)
    {
        if (i < 0 || i >= _items.Length) return Rectangle.Empty;
        int m = UiTheme.S(this, 2), cx = 0;
        for (int k = 0; k < i; k++) cx += ItemWidth(k);
        return new Rectangle(cx + m, m, ItemWidth(i) - 2 * m, Height - 2 * m);
    }

    /// <summary>落位（不播动画）：程序化推值、换表、改宽都走这里。</summary>
    void SettlePill() { _anim?.Stop(); _pillFrom = PillRect(_active); _pillT = 1; }

    void StartPillAnim()
    {
        _animStartTick = Environment.TickCount;
        _pillT = 0;
        _anim ??= new System.Windows.Forms.Timer { Interval = UiMotion.FrameMs };
        _anim.Tick -= OnPillTick;
        _anim.Tick += OnPillTick;
        _anim.Start();
    }

    /// <summary>滑动进度（纯函数 = 画与自检共用）：位移曲线吃 EaseInOut，两端速度为 0（"跳一下再蹭"就是它治的）。</summary>
    internal static double PillProgressAt(int elapsedMs) => UiMotion.EaseInOut(elapsedMs / (double)PillSlideMs);

    void OnPillTick(object? sender, EventArgs e)
    {
        int el = Environment.TickCount - _animStartTick;
        _pillT = PillProgressAt(el);
        if (el >= PillSlideMs) { _anim?.Stop(); _pillT = 1; }
        Invalidate();
    }

    // 出图 / 自检出口：把胶囊钉在某一个滑动进度上拍"擦到一半"那一帧（静态截图本来证明不了滑动）
    internal bool PillSlidingForTest => _pillT < 1;
    internal void FinishPillForTest() { SettlePill(); Invalidate(); }
    internal Rectangle PillRectAtForTest(int i) => PillRect(i);
    internal void SetPillPreviewForTest(int fromIndex, double t)
    {
        _anim?.Stop();
        _pillFrom = PillRect(fromIndex);
        _pillT = Math.Clamp(t, 0, 1);
        Invalidate();
    }

    public void SetItems(params string[] items)
    {
        // **表没变就不许落位**（2026-10-16 他实跑"胶囊没动画"的根因）：切页那条路每次都会走
        // RefreshModuleScopedUi → SetItems(同一串名字)，而 SetItems 原本无条件 SettlePill() ——
        // 用户那一击刚把动画起起来，同步的切页回来就把它掐在第 0 帧，动画一帧都没播过。
        bool same = _items.Length == items.Length;
        if (same) for (int i = 0; i < items.Length && same; i++) same = _items[i] == items[i];
        _items = items;
        if (_active >= items.Length) { _active = 0; same = false; }
        ApplyWidth();
        if (!same) SettlePill();          // 真的换了表（启用模块增减）才落位：旧起点矩形那时没有意义
        Invalidate();
    }

    public void ApplyWidth()
    {
        int w = 0;
        for (int i = 0; i < _items.Length; i++) w += ItemWidth(i);
        if (Width != w) { Width = w; SettlePill(); }   // 只有宽真变了才落位（改宽会让旧起点矩形错位）
    }

    int ItemWidth(int i) => TextRenderer.MeasureText(_items[i], Font).Width + UiTheme.S(this, 32);

    int Hit(int x)
    {
        int at = 0;
        for (int i = 0; i < _items.Length; i++) { at += ItemWidth(i); if (x < at) return i; }
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int h = Hit(e.X);
        if (h != _hot) { _hot = h; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e) { _hot = -1; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        int i = Hit(e.X);
        if (i >= 0 && e.Button == MouseButtons.Left && i != _active)
        {
            // 起点必须在改 _active 之前捕获：下面那句 ValueChanged 是**同步**进 SwitchModule 整块重建的，
            // 回来再量就只剩新格了（滑动会变成"从终点起步"= 一格都不动）。
            _pillFrom = PillRect(_active);
            _active = i;
            StartPillAnim();
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
        base.OnMouseDown(e);
    }

    protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Backdrop);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = ClientRectangle;
        r.Width -= 1;
        r.Height -= 1;
        using var path = UiTheme.RoundRect(r, UiTheme.S(this, 8));
        using (var bg = new SolidBrush(UiTheme.Input)) g.FillPath(bg, path);
        // ① 胶囊先画（滑动中它会压过中间那格的底），② 再把所有文字画在它上面 ——
        // 原来胶囊与文字在同一个循环里交替画，滑动时经过的那一格文字会被胶囊盖住（落位态两者等价）。
        var target = PillRect(_active);
        var cell = _pillT < 1 ? UiMotion.LerpRect(_pillFrom, target, _pillT) : target;
        if (cell.Width > 0 && cell.Height > 0)
        {
            using var on = new SolidBrush(UiTheme.SegOn);
            using var pill = UiTheme.RoundRect(cell, Math.Min(UiTheme.S(this, 8), cell.Height / 2));
            g.FillPath(on, pill);
        }
        int cx = 0;
        for (int i = 0; i < _items.Length; i++)
        {
            int w = ItemWidth(i);
            // 文字色瞬切（不吃滑动进度）：插值会造出 UiTheme 色表外的中间色，违反「取色一律取既有档」那条。
            TextRenderer.DrawText(g, _items[i], Font, new Rectangle(cx, 0, w, r.Height),
                i == _active || _hot == i ? UiTheme.Text : UiTheme.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            cx += w;
        }
        using var border = new Pen(UiTheme.Border, 1f);
        g.DrawPath(border, path);
    }
}

// 细长进度条（DEC-054，用户：升级时要在「升级到」那一栏里看到进度）：
// 高 6 逻辑像素、圆角、品牌绿填充；Fraction >= 0 按百分比画，< 0 走"来回扫"的不确定态
// （下载有总长能算百分比，"同步官方预编译引擎"那种只能报个阶段名，就让它扫）。
// 只在不确定态开一个 40ms 的定时器，且控件不可见时立刻停——别给空闲界面留后台开销。
