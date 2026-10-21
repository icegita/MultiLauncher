using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Drawing.Drawing2D;

namespace MultiLauncher;

// ThinProgress、TinyX、TinyUpdate 三枚小件（含各自的 Dispose 释放）。
// 2026-10-15 从 UiControls.cs 整段搬出，一行未改。
internal sealed class ThinProgress : Control
{
    readonly System.Windows.Forms.Timer _tick = new() { Interval = 40 };
    double _frac;
    int _marquee, _dir = 1;

    public Color FillColor = UiTheme.Brand;
    public Color TrackColor = UiTheme.Input;
    public Color TrackBorderColor = UiTheme.Border;
    public int Radius = 3;

    public ThinProgress()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
               | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = UiTheme.Window;
        _tick.Tick += (_, _) => { StepMarquee(); };
        LiveForTest++;        // DEC-061：自检用它确认" Dispose 覆写真的跑到了"（定时器随控件释放）
    }

    /// 自检用：当前存活的 ThinProgress 数量（建 +1、Dispose −1）
    internal static int LiveForTest;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public double Fraction
    {
        get => _frac;
        set
        {
            double v = value;
            if (!double.IsNaN(v) && v >= 0) v = Math.Clamp(v, 0, 1); else v = -1;
            if (Math.Abs(v - _frac) < 0.0005) { UpdateTimer(); return; }
            _frac = v;
            UpdateTimer();
            Invalidate();
        }
    }

    bool Indeterminate => _frac < 0;

    void UpdateTimer()
    {
        bool want = Indeterminate && Visible && IsHandleCreated;
        if (want && !_tick.Enabled) _tick.Start();
        else if (!want && _tick.Enabled) _tick.Stop();
    }

    void StepMarquee()
    {
        int span = Math.Max(1, ClientSize.Width - ThumbW());
        _marquee += _dir * Math.Max(2, UiTheme.S(this, 3));
        if (_marquee >= span) { _marquee = span; _dir = -1; }
        if (_marquee <= 0) { _marquee = 0; _dir = 1; }
        Invalidate();
    }

    int ThumbW() => Math.Max(UiTheme.S(this, 18), ClientSize.Width / 3);

    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); UpdateTimer(); }
    protected override void OnResize(EventArgs e) { base.OnResize(e); UpdateTimer(); Invalidate(); }

    // DEC-061：条子被销毁时定时器必须停。以前没有任何停止路径——UpdateTimer 只在 Fraction/Visible/Size
    // 变化时被叫，而控件 Dispose 时这三样都不再变化。「关窗不停流程」（DEC-058）恰恰让最常见的一态是
    // **不确定态**时关窗：那枚 40ms 游标会一路跑到进程结束，每拍还在已销毁的控件上读 ClientSize、调 Invalidate。
    protected override void OnHandleDestroyed(EventArgs e) { _tick.Stop(); base.OnHandleDestroyed(e); }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { _tick.Stop(); _tick.Dispose(); LiveForTest--; }
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var full = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        using (var path = UiTheme.RoundRect(full, UiTheme.S(this, Radius)))
        {
            using var track = new SolidBrush(TrackColor);
            g.FillPath(track, path);
            using var pen = new Pen(TrackBorderColor, 1f);
            g.DrawPath(pen, path);
        }
        Rectangle seg;
        if (Indeterminate)
            seg = new Rectangle(_marquee, 0, ThumbW(), full.Height);
        else
        {
            int w = (int)Math.Round(full.Width * Math.Clamp(_frac, 0, 1));
            if (w <= 0) return;
            seg = new Rectangle(0, 0, Math.Max(w, UiTheme.S(this, Radius * 2)), full.Height);
        }
        // 填充裁在圆角轨道里，右端才不会露出方角
        using (var track = UiTheme.RoundRect(full, UiTheme.S(this, Radius)))
        {
            g.SetClip(track, System.Drawing.Drawing2D.CombineMode.Intersect);
            using var b = new SolidBrush(FillColor);
            g.FillRectangle(b, seg);
            g.ResetClip();
        }
    }
}

// 进度条后面那枚红色小 X（DEC-056 出图对照后定稿 = 变体②「圆底」）：
// 常态只有一枚红叉（不打底，免得升级那一栏看起来更挤），鼠标移上去才浮出淡红圆底、字形同时变亮；
// 点过一次自己按住（Enabled=false → 字形转灰）。命中区是整块控件、字形只占中间一半——
// 20px 见方再小就点不着了。取消只在「下载 / 解压」阶段出现，判据在 MainForm.PhaseCancellable。
internal sealed class TinyX : Control
{
    public Color Ink = UiTheme.Danger;
    public Color InkHover = Color.FromArgb(0xF8, 0x71, 0x71);   // 悬停要更亮：深底上压暗等于消失
    public Color Backdrop = UiTheme.Window;
    bool _hover;
    internal bool HoverForTest;      // 出图用：把悬停态画出来给人看（静态截图看不出 hover 反馈）
    readonly ToolTip _tip = new() { AutoPopDelay = 8000, InitialDelay = 250, ReshowDelay = 120 };

    public TinyX()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
               | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = UiTheme.Window;
        Cursor = Cursors.Hand;
        TabStop = false;
    }

    public string Tip = "取消更新";       // 悬停说明（字形太小，光看猜不出是干什么的）
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (Tip.Length > 0) _tip.SetToolTip(this, Tip);
    }

    // DEC-061：自带的 ToolTip 组件要随控件释放（它背后有一个隐藏的 tooltip 窗口），否则按开窗次数累积。
    protected override void Dispose(bool disposing)
    {
        if (disposing) _tip.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    // 同 FlatButton：自动化（ui-probe -Click）发的 BM_CLICK 得能点动
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x00F5) { OnClick(EventArgs.Empty); return; }
        base.WndProc(ref m);
    }

    protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Backdrop);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        int d = Math.Min(Width, Height);
        if (d <= 0) return;
        var box = new Rectangle((Width - d) / 2, (Height - d) / 2, d, d);
        // 点过一次就按住（Enabled=false）：字形跟着变灰，别留一个还能点亮的红叉
        bool hov = _hover || HoverForTest;
        var ink = !Enabled ? Color.FromArgb(0x5C, 0x62, 0x6C) : (hov ? InkHover : Ink);

        if (Enabled && hov)
        {
            using var halo = new SolidBrush(Color.FromArgb(0x3C, Ink.R, Ink.G, Ink.B));
            g.FillEllipse(halo, box);
        }

        // 字形占盒子的中间一半多一点，线宽随 DPI 走
        float inset = d * 0.30f;
        float th = Math.Max(1.4f, UiTheme.S(this, 2) * 0.85f);
        var a = new RectangleF(box.Left + inset, box.Top + inset, box.Width - inset * 2, box.Height - inset * 2);
        using (var pen = new Pen(ink, th))
        {
            pen.StartCap = pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
            g.DrawLine(pen, a.Left, a.Top, a.Right, a.Bottom);
            g.DrawLine(pen, a.Right, a.Top, a.Left, a.Bottom);
        }
    }
}

/// <summary>左栏版本行末尾那枚小更新按钮（DEC-107，用户给的图形 = lucide `circle-arrow-up`：
/// 圆 r10 + 折线 `m16 12-4-4-4 4` + 箭杆 `M12 16V8`，全部按 24 格等比缩到方窗里）。
/// 与 <see cref="TinyX"/> 同族：只画、不占布局（尺寸由放它的行给，DEC-108 起 S(12)：他批"太大、缩 40%"，
/// 行里垂直居中摆、右缘不动 —— 值列让出宽度而不是整列变宽）；TabStop=false、按钮语义走 Click。
/// **颜色两态**（DEC-108，他批"绿太醒目"）：默认灰 `<see cref="UiTheme.TextMuted"/>`（没探到新版本）——
/// 探到新版本（或被跳过那一版在）由宿主置 <see cref="NewVersion"/> 转品牌绿；禁用时压暗同 TinyX。</summary>
internal sealed class TinyUpdate : Control
{
    public Color Ink = UiTheme.TextMuted;      // 默认灰（他批：绿色只留给"有新版本"）
    public Color InkHover = UiTheme.Text;
    public bool NewVersion;                    // 探到新版本 → 品牌绿（宿主按当前模块的 offer 置位）
    public Color Backdrop = UiTheme.Card;
    bool _hover;
    internal bool HoverForTest;      // 出图用：把悬停态画出来给人看
    readonly ToolTip _tip = new() { AutoPopDelay = 8000, InitialDelay = 250, ReshowDelay = 120 };

    public TinyUpdate()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
               | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = UiTheme.Window;
        Cursor = Cursors.Hand;
        TabStop = false;
    }

    public string Tip = "打开更新窗（开窗即查一次）";

    /// <summary>DEC-202：宿主可以改提示文字。原来只在 <see cref="OnHandleCreated"/> 设一次 —— 页头那枚
    /// 在窄态（只留图标）要把版本号说在提示里，而那一刻还不知道探到的是哪一版。</summary>
    internal void SetTip(string text)
    {
        Tip = text;
        try { _tip.SetToolTip(this, text); } catch { /* 句柄还没建：OnHandleCreated 会用 Tip 再设一次 */ }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _tip.SetToolTip(this, Tip);
    }

    // 同 TinyX / FlatButton：自带的 ToolTip 要随控件释放
    protected override void Dispose(bool disposing)
    {
        if (disposing) _tip.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    // 同 FlatButton：自动化（ui-probe -Click）发的 BM_CLICK 得能点动
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x00F5) { OnClick(EventArgs.Empty); return; }
        base.WndProc(ref m);
    }

    protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Backdrop);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        int d = Math.Min(Width, Height);
        if (d <= 0) return;
        var box = new Rectangle((Width - d) / 2, (Height - d) / 2, d, d);
        bool hov = _hover || HoverForTest;
        // 两态取色（DEC-108）：新版本 = 品牌绿那对；默认 = 灰（悬停提亮到正文色）
        var baseInk = NewVersion ? UiTheme.Brand : Ink;
        var ink = !Enabled ? Color.FromArgb(0x5C, 0x62, 0x6C)
                          : NewVersion ? (hov ? UiTheme.BrandHover : UiTheme.Brand)
                          : (hov ? InkHover : Ink);
        if (Enabled && hov)
        {
            using var halo = new SolidBrush(Color.FromArgb(0x30, baseInk.R, baseInk.G, baseInk.B));
            g.FillEllipse(halo, box);
        }
        // 24 格坐标 → 本方窗：圆心 = 盒心，圆 r10（外圈吃 stroke 的半个线宽），折线 16,12 → 12,8 → 8,12，箭杆 12,16 → 12,8
        float th = Math.Max(1.3f, d * 0.085f);
        float u = d / 24f;                                    // 一个 24 格单位
        float cx = box.Left + d / 2f, cy = box.Top + d / 2f;
        using var pen = new Pen(ink, th);
        pen.StartCap = pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
        pen.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;
        float r = 10f * u - th / 2f;                          // 圆半径（外缘贴 24 格）
        g.DrawEllipse(pen, cx - r, cy - r, r * 2, r * 2);
        float L( float gx) => box.Left + gx * u;              // 24 格 x → 窗内
        float T( float gy) => box.Top + gy * u;               // 24 格 y → 窗内
        g.DrawLine(pen, L(12), T(16), L(12), T(8.4f));        // 箭杆（16 → 8，稍收一点给圆环留缝）
        g.DrawLine(pen, L(16), T(12), L(12), T(8.2f));        // 折线右半
        g.DrawLine(pen, L(8), T(12), L(12), T(8.2f));         // 折线左半
    }
}
