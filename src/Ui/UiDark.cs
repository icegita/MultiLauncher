using System.Runtime.InteropServices;

namespace MultiLauncher;

// 让系统标题栏也跟着深色主题（WinForms 自己做不到）。只影响外观，失败就保持系统默认：
//   DWMWA_USE_IMMERSIVE_DARK_MODE = 20（Win10 20H1 之前是 19，失败即忽略）
//   DWMWA_WINDOW_CORNER_PREFERENCE = 33（Win11 圆角；2 = DWMWCP_ROUND）
//   DWMWA_BORDER_COLOR / CAPTION_COLOR / TEXT_COLOR = 34 / 35 / 36（Win11 才有）
//     —— 把标题栏底色设成窗体底色后，激活与非激活是同一个颜色（DWM 不再调暗），
//        标题栏与界面就接成一片了。设不了（Win10）自然退回上一行的暗色模式。
//
// 变体开关（开发期截图对比用，读环境变量 STRATA_UI_TITLEBAR）：
//   off   = 只开暗色模式（标题栏仍是系统深灰，激活/非激活会变色）
//   flat  = 标题栏 = 窗体底色，边框也同色（最无缝）        ← 当前默认
//   edged = 标题栏 = 窗体底色，边框 = UiTheme.Border（窗口边缘仍可辨）
internal static class UiDark
{
    [DllImport("dwmapi.dll", PreserveSig = true)]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    const int DWMWA_BORDER_COLOR = 34;
    const int DWMWA_CAPTION_COLOR = 35;
    const int DWMWA_TEXT_COLOR = 36;

    public enum TitleBarMode { Off, Flat, Edged }

    public static TitleBarMode TitleMode = Environment.GetEnvironmentVariable("STRATA_UI_TITLEBAR") switch
    {
        "off" => TitleBarMode.Off,
        "edged" => TitleBarMode.Edged,
        _ => TitleBarMode.Flat,
    };

    // COLORREF：0x00BBGGRR
    static int ColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);
    static void Set(IntPtr hwnd, int attr, int value)
    {
        try { DwmSetWindowAttribute(hwnd, attr, ref value, sizeof(int)); } catch { /* 老系统没有这个属性 */ }
    }

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    static extern int SetWindowTheme(IntPtr hwnd, string? appName, string? idList);

    /// 让原生滚动条（TextBox / 面板 AutoScroll）走深色主题。
    /// 这是 uxtheme 的既有开关，不支持的 Windows 版本上静默无效——所以只是"尽力"，不影响功能。
    public static void DarkScrollbar(Control c)
    {
        void Apply()
        {
            try { SetWindowTheme(c.Handle, "DarkMode_Explorer", null); }
            catch { /* 老系统没有这个主题 */ }
        }
        if (c.IsHandleCreated) Apply();
        else c.HandleCreated += (_, _) => Apply();
    }

    public static void Apply(Form form, bool titleColors = true, bool borderColor = true)
    {
        void ApplyNow()
        {
            var h = form.Handle;
            int on = 1;
            Set(h, DWMWA_USE_IMMERSIVE_DARK_MODE, on);      // 标题栏按暗色画（含最大/最小/关闭按钮的符号）
            int round = 2;
            Set(h, DWMWA_WINDOW_CORNER_PREFERENCE, round);  // Win11 圆角
            // titleColors=false：只开暗色模式、不上自定义色。设置窗走这条 ——
            // 它的标题栏与下面那排 Tab 同色会糊成一块，留系统那档深灰才有分界（用户第三轮反馈第 2 条）。
            if (!titleColors || TitleMode == TitleBarMode.Off) return;
            Set(h, DWMWA_CAPTION_COLOR, ColorRef(UiTheme.Window));
            Set(h, DWMWA_TEXT_COLOR, ColorRef(UiTheme.Text));
            // DEC-092（用户口径：恢复与页面同色那一版，但保留现在的边框）：弹窗族停在这里 ——
            // 不设 BORDER_COLOR，窗口那一圈仍由系统画，所以边界照旧看得见。
            if (!borderColor) return;
            Set(h, DWMWA_BORDER_COLOR, ColorRef(TitleMode == TitleBarMode.Flat ? UiTheme.Window : UiTheme.Border));
        }
        if (form.IsHandleCreated) ApplyNow();
        else form.HandleCreated += (_, _) => ApplyNow();
    }

    // DEC-092（2026-10-06 用户实跑后拍板）：弹窗族（更新窗 + Dialogs.* 整套）标题栏**与内容区同底色**（#1E1E1E），
    // 但**不设 BORDER_COLOR** —— 窗口那一圈边框仍由系统画，边界照旧看得见。
    // 这一档把 DEC-059 的「标题栏与窗体同底色」恢复回来、把 DEC-063 的退档理由（flat 变体连边框一起设成窗体色，
    // 边缘看不出边界）单独摘掉：DEC-063 当时只能整档退回系统深灰 #202020，现在两个诉求各走各的开关。
    // 抽成常量位是为了自检能断言：DWM 的色要窗口真显示才读得回，自检不开窗，所以断言打在这两个判据上。
    public const bool DialogsUseCustomCaptionColors = true;
    public const bool DialogsKeepSystemBorder = true;

    // DEC-093（用户：上一轮改标题栏颜色时漏了设置页）：这一档覆盖的窗 = Dialogs.* 整套 + 更新窗 + **设置窗**
    // + 出图台里的弹窗样式 mock。设置窗原先自写 `Apply(this, titleColors: false)`（DEC-028 那轮的"与页签带要有分界"），
    // 结果它一直是系统那档 #202020、与同屏其他窗的 #1E1E1E 对不齐。现在它也只许走 ApplyDialog。
    // 登记位只为自检用：ApplyDialog 盖过哪些窗，断言就能读回来（弱表，窗关了自动掉，不拖住表单）。
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Form, object> DialogStyled = new();

    /// 弹窗族（Dialogs.* + 更新窗 + 设置窗）统一走这里，别各自再写一遍 titleColors。
    public static void ApplyDialog(Form f)
    {
        Apply(f, titleColors: DialogsUseCustomCaptionColors, borderColor: !DialogsKeepSystemBorder);
        if (!DialogStyled.TryGetValue(f, out _)) DialogStyled.Add(f, true);
    }

    /// 自检用：这个窗是不是走弹窗族那一档标题栏（ApplyDialog 盖过 = true）。
    public static bool IsDialogStyled(Form f) => DialogStyled.TryGetValue(f, out _);

    // 自检用：读回标题栏底色（没设过自定义色 / 系统不支持 -> null）
    public static int? CaptionColorOf(IntPtr hwnd)
    {
        try
        {
            int v = 0;
            int hr = DwmGetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref v, sizeof(int));
            return hr == 0 ? v & 0xFFFFFF : null;
        }
        catch { return null; }
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}