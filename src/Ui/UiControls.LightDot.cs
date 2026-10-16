using System.Drawing.Drawing2D;

namespace MultiLauncher;

/// <summary>状态灯那枚圆点（DEC-177，2026-10-15 他实跑点名）。
///
/// **为什么不用「●」字形**：那个字符的墨迹中心落在它自己行盒的中线上，而拉丁文字的墨迹中心在
/// 基线上方约半个大写字高处 —— 两个行盒高度还不一样（灯 10pt、文字 8.5/9pt）。150% 下按他那张图
/// 逐像素量：灯墨迹 68..76（心 72.0）、文字墨迹 59..72（心 65.5）= **灯比文字低 6.5 物理像素（≈S(4)）**，
/// 而且灯还压着基线。这条差是字形设计决定的，改 `Location.Y` 只能在某一个 DPI 上碰对，换 DPI 就漂。
/// 自绘 = 圆心由调用方按**文字墨迹中心**摆（见 `MainForm.LightTextCenterFromTop`），与字号、DPI 都无关。
///
/// 直径取原「●」的实测墨迹：150% 下那张图里灯墨迹 16..25 × 68..76 = 10×9 物理像素 = S(7)（他没挑过灯的大小，
/// 只挑了位置，所以尺寸照旧 —— 先按 S(6) 出过一版，探针实测 8×8，比原来小一档，改回 S(7)）。
/// 与 <see cref="TinyX"/> / <see cref="TinyUpdate"/> 同族：只画、不占布局，尺寸由放它的那一行给。</summary>
internal sealed class LightDot : Control
{
    /// <summary>圆点直径（逻辑像素）—— 调用方算行内几何时要用同一档。</summary>
    public const int LogicalDiameter = 7;

    public Color Ink = Color.Gray;           // 灯色（由 `MainForm.LightColor(LightState)` 给，四态）
    public Color Backdrop = UiTheme.Card;    // 底色跟着所在卡片，不留方框

    public LightDot()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
               | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = UiTheme.Card;
        TabStop = false;
    }

    protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Backdrop);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int d = Math.Min(Width, Height);
        if (d <= 0) return;
        using var b = new SolidBrush(Ink);
        g.FillEllipse(b, (Width - d) / 2f, (Height - d) / 2f, d, d);
    }
}
