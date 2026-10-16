using System.Runtime.InteropServices;

namespace MultiLauncher;

// 日志框（原生 TextBox）的自绘滚动条。
// TextBox 不是 ScrollableControl，值的读写只能走 EDIT 控件自己的消息：
//   总行数   EM_GETLINECOUNT
//   首可见行 EM_GETFIRSTVISIBLELINE
//   滚动     EM_LINESCROLL（wParam = 水平字符数，lParam = 垂直行数，可负）
// 用户用滚轮 / 键盘 / 拖动滚动时 TextBox 不发任何 WinForms 事件，所以额外挂一个 NativeWindow
// 截 WM_VSCROLL / WM_MOUSEWHEEL / WM_KEYDOWN，等它自己处理完再回读位置（必须在 base.WndProc 之后）。
public partial class MainForm
{
    readonly UiScrollbar _logBar = new();
    LogScrollHook? _logHook;

    const int EM_GETLINECOUNT = 0x00BA;
    const int EM_GETFIRSTVISIBLELINE = 0x00CE;
    const int EM_LINESCROLL = 0x00B6;

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    void SetupLogScrollbar(Control host)
    {
        _logBar.Backdrop = UiTheme.Card;
        _logBar.Scrolled += (_, _) => ScrollLogTo(_logBar.Value);
        host.Controls.Add(_logBar);
        _logBar.BringToFront();

        _txtLog.TextChanged += (_, _) => SyncLogScrollbarLater();   // AppendText / Clear
        // 2026-10-02 反馈（展开/折叠高级后滚动条黑残）：卡片 Layout 事件在日志框完成 Dock 重排**之前**跑，
        // 同步拿到的还是旧尺寸——展开后条高还是 256（框已缩）、折叠后条高残留 0 → 自绘条整根消失、
        // 露出原生黑滚动条（#171717），直到下一条日志才自愈。SizeChanged 在尺寸落定后才发，用它补一发。
        _txtLog.SizeChanged += (_, _) => SyncLogScrollbar();
        host.Layout += (_, _) => SyncLogScrollbar();
        _logHook = new LogScrollHook(_txtLog, SyncLogScrollbarLater);
        _txtLog.Disposed += (_, _) =>
        {
            try { _logHook?.ReleaseHandle(); } catch { /* 句柄已随窗口销毁 */ }
            _logHook = null;
        };
    }

    // AppendText 之后紧跟 ScrollToCaret，位置要等这一轮消息走完才稳定 —— 延迟到消息队列空闲再读
    void SyncLogScrollbarLater()
    {
        if (IsDisposed) return;
        try { BeginInvoke(new Action(SyncLogScrollbar)); } catch { /* 窗口正在销毁 */ }
    }

    void SyncLogScrollbar()
    {
        if (IsDisposed) return;
        // 几何（位置/尺寸）不依赖句柄：先跟住日志框——自检不开窗时没有句柄，也要能断言条不滞后（BUG-026）
        int w = Math.Max(UiTheme.S(this, 10), UiScrollbar.SystemWidth(UiTheme.Dpi));
        _logBar.SetBounds(_txtLog.Right - w, _txtLog.Top, w, _txtLog.Height);
        if (!_txtLog.IsHandleCreated || !_logBar.IsHandleCreated) return;
        var h = _txtLog.Handle;
        int total = (int)SendMessage(h, EM_GETLINECOUNT, IntPtr.Zero, IntPtr.Zero);
        int first = (int)SendMessage(h, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero);
        int lineH = Math.Max(1, _txtLog.Font.Height);
        int page = Math.Max(1, _txtLog.ClientSize.Height / lineH);
        _logBar.Set(total, page, first);
        // 日志框的 ScrollBars=Vertical 是常驻的（内容不够一屏也画一条禁用态滚动条），
        // 所以自绘条要一直盖着它；不可滚动时只是不画滑条（UiScrollbar.OnPaint 自己判断）。
    }

    void ScrollLogTo(int first)
    {
        if (IsDisposed || !_txtLog.IsHandleCreated) return;
        int cur = (int)SendMessage(_txtLog.Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero);
        if (first == cur) return;
        SendMessage(_txtLog.Handle, EM_LINESCROLL, IntPtr.Zero, (IntPtr)(first - cur));
    }

    // 自检用：日志框与自绘滚动条的几何（2026-10-02 BUG-026：展开/折叠后条的 Top/高要跟住日志框）
    internal (Rectangle bar, Rectangle box) LogScrollGeomForTest => (_logBar.Bounds, _txtLog.Bounds);

    // 日志框滚动消息的旁路监听（TextBox 自身不发事件）
    sealed class LogScrollHook : NativeWindow
    {
        const int WM_HSCROLL = 0x0114, WM_VSCROLL = 0x0115, WM_MOUSEWHEEL = 0x020A, WM_KEYDOWN = 0x0100;

        readonly Action _changed;

        public LogScrollHook(TextBox box, Action changed)
        {
            _changed = changed;
            if (box.IsHandleCreated) AssignHandle(box.Handle);
            else box.HandleCreated += (_, _) => AssignHandle(box.Handle);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg is WM_HSCROLL or WM_VSCROLL or WM_MOUSEWHEEL or WM_KEYDOWN) _changed();
        }
    }
}
