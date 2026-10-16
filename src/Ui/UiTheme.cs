using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace MultiLauncher;

// UI 主题与自绘基础设施（深色主题）。
//
// 纪律：纯 WinForms 自绘、零第三方依赖 —— publish 后的 exe 必须保持几百 KB 量级
// （现状 318 KB；参照同类工具 506 KB）。装主题库会涨到 MB 级，不允许。
//
// DPI：尺寸一律走 S() 换算，且窗体用 AutoScaleMode.None。
// 两套缩放叠加会让控件在 125%/150% 下尺寸错位（实测过）。
internal static class UiTheme
{
    // DEC-138（F-5）：字体只有一个家 —— 按（字族, 字号, 字形）缓存，同一档全进程只有一支 Font 对象。
    // 为什么必须收：WinForms **不会**替控件释放赋给 `Font` 的那支，而自绘控件与设置页那些工厂以前每建一枚控件
    // 就 `new Font(...)` 一次（`GroupTitle` 更是刚 new 完就被下一行替换掉 = 当场漏一支）。外壳是常驻托盘的长命进程，
    // 一次开合设置窗 ≈ 40~70 支、卡片重摆一轮又是几支，GDI 对象配额 10000/进程 —— 攒够了就是画不出字、界面花掉。
    // Font 是不可变的、控件也不拥有它，所以共享是安全的；只有"这一族自己 new、自己负责"的（UiStatusBar._f、
    // Toast._font、UiMarkdown 那四支）例外 —— 它们在各自的 Dispose 里点名释放，那种不许改成吃这里的缓存。
    static readonly Dictionary<(string, float, FontStyle), Font> _fonts = new();
    static readonly object _fontsLock = new();
    public static Font FontOf(string family, float size, FontStyle style = FontStyle.Regular)
    {
        var key = (family, size, style);
        lock (_fontsLock)
        {
            if (_fonts.TryGetValue(key, out var f)) return f;
            f = new Font(family, size, style);
            _fonts[key] = f;
            return f;
        }
    }
    public static Font Ui(float size, FontStyle style = FontStyle.Regular) => FontOf("Microsoft YaHei UI", size, style);
    public static Font Mono(float size, FontStyle style = FontStyle.Regular) => FontOf("Consolas", size, style);
    // 自检用：这座家里现在有几支（收口前后的差 = 这一批到底省下了多少支，别拿"应该少了"当判据）
    public static int FontHomeCountForTest { get { lock (_fontsLock) return _fonts.Count; } }

    // 窗体 DPI。为什么不用 Control.DeviceDpi：句柄刚建好时 WM_DPICHANGED 还没进消息循环，
    // DeviceDpi 可能仍是 96 —— 实测同一份 exe 两次启动，窗口一次 1455px、一次 970px，界面会整体错位。
    // 所以句柄建好后用 GetDpiForWindow 同步取一次，之后全局统一按它换算（本程序只有主窗与设置窗，同屏）。
    static int _dpi = 96;
    public static int Dpi => _dpi;

    [DllImport("user32.dll")]
    static extern uint GetDpiForWindow(IntPtr hwnd);

    public static void CaptureDpi(IntPtr hwnd)
    {
        try
        {
            var d = GetDpiForWindow(hwnd);
            if (d is >= 72 and <= 480) _dpi = (int)d;
        }
        catch { /* 老系统没有这个 API：保持 96 */ }
    }

    public static readonly Color Window = Color.FromArgb(0x1E, 0x1E, 0x1E);
    public static readonly Color Card = Color.FromArgb(0x23, 0x23, 0x23);
    public static readonly Color Input = Color.FromArgb(0x2A, 0x2A, 0x2A);
    public static readonly Color Text = Color.FromArgb(0xE5, 0xE5, 0xE5);
    public static readonly Color TextMuted = Color.FromArgb(0x9C, 0xA3, 0xAF);
    public static readonly Color Border = Color.FromArgb(0x33, 0x33, 0x33);
    public static readonly Color Brand = Color.FromArgb(0x22, 0xC5, 0x5E);
    public static readonly Color BrandHover = Color.FromArgb(0x16, 0xA3, 0x4A);
    public static readonly Color BrandOn = Color.FromArgb(0x0B, 0x1F, 0x12);
    public static readonly Color Danger = Color.FromArgb(0xEF, 0x44, 0x44);
    public static readonly Color DangerHover = Color.FromArgb(0xDC, 0x26, 0x26);
    public static readonly Color LogText = Color.FromArgb(0x9C, 0xA3, 0xAF);     // 日志文字（就绪行另用更亮的值）
    public static readonly Color SegOn = Color.FromArgb(0x3A, 0x3A, 0x3A);       // 页头分段控件的激活格底色（原型 .segw-i.on）
    // 叠放按钮（StackedToggle）底下一枚"露出来的那条边"的底色。DEC-125③：以前用 Input(#2A2A2A)，坐在
    // Card(#232323) 上只差 7 级 = 用户实跑判"几乎看不出来"（那一条边正是"还有另一个"的唯一提示）。
    // 取 #454545 = 与自绘细滑条的常态同一档（UiScrollbar 常态色），**不新增一族颜色**，只差按"稍微调淡"提亮度。
    public static readonly Color StackedPeek = Color.FromArgb(0x45, 0x45, 0x45);

    // 逻辑像素 -> 物理像素（统一按启动时捕获的窗体 DPI，见 CaptureDpi）
    public static int S(Control c, int px) => (int)Math.Round(px * _dpi / 96.0);   // 参数 c 保留：调用点语义清晰（谁在用）

    // 物理像素 -> 逻辑像素（反向换算，自检报数用：量出来的宽度要说"逻辑多少"才和 DEC 里的数字对得上）
    public static int DpiToLogical(int px) => (int)Math.Round(px * 96.0 / _dpi);

    public static GraphicsPath RoundRect(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        int d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}