using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Drawing.Drawing2D;

namespace MultiLauncher;

// 自绘控件库（深色主题）。均在 DpiProbe（--uiprobe）里可预览。
//
// 每个控件都要设 Backdrop（自己外面那层的底色）：WinForms 的圆角是画在矩形控件上的，
// 圆角外露出的部分必须由控件自己填父容器的底色，否则会露出黑色方块。

// 非 sealed：MdScroll（更新说明的滚动容器）在它上面加滑条与滚轮派发
class RoundedPanel : Panel
{
    public Color FillColor = UiTheme.Card;
    public Color BorderColor = UiTheme.Border;
    public Color Backdrop = UiTheme.Window;
    public int Radius = 12;

    public RoundedPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
               | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
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
