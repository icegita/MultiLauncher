using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace MultiLauncher;

// 自绘细滚动条：只有一根圆角滑条，没有上下箭头、也没有轨道底色。
// 系统原生滚动条这两样都去不掉（换 DarkMode 主题后箭头和轨道仍在），所以做法是
// 自绘一根**叠在原生滚动条正上方**把它盖住：
//   · 容器自身的滚动能力完全保留（原生滚动条还在原位，只是看不见了）；
//   · 值同步由使用方负责 —— 设置页读 AutoScrollPosition（AttachTo 已封装），
//     日志框读 EM_GETFIRSTVISIBLELINE（见 MainForm.LogScroll.cs）。
// 单位是"行/像素位移"这种抽象量：total = 内容总量，page = 可视量，value = 起点。
internal sealed class UiScrollbar : Control
{
    int _total = 1;
    int _page = 1;
    int _value;
    bool _hover, _dragging;
    int _grab;                       // 按下时鼠标相对滑条顶端的偏移

    public Color Backdrop = UiTheme.Card;
    public Color ThumbColor = Color.FromArgb(0x45, 0x45, 0x45);          // 常态：比卡片底亮一档，看得见但不抢眼
    public Color ThumbActiveColor = Color.FromArgb(0x6E, 0x6E, 0x6E);   // hover / 拖动
    public int Thickness = 6;        // 滑条厚度（逻辑像素；控件本身还要再宽一点以盖住原生滚动条）
    public int MinThumb = 24;        // 滑条最短长度（逻辑像素）

    // 用户操作改了值：使用方据此滚动真正的容器
    public event EventHandler? Scrolled;

    internal WheelRouter? Router;      // AttachTo 装的滚轮路由（自检要看它在不在）
    internal Action? SyncNow;          // 直接同步一次（自检用；真路径走 AttachTo 里的同步/重同步）
    bool _syncQueued;                  // 重同步的排队去重（布局风暴只排一次）
    int _layoutSyncHits;               // 证人位：宿主 Layout 这条重同步线被走到几次（无头下 BeginInvoke 不会真跑）
    internal int LayoutSyncHitsForTest => _layoutSyncHits;
    internal (int Total, int Page, int Value) RangeForTest => (_total, _page, _value);

    public UiScrollbar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
               | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
    }

    public int Value => _value;
    public bool Scrollable => _total > _page;

    /// <summary>滑条要不要出面（2026-10-23，BUG-086）：**按真几何** —— 内容比可视高才出面。
    /// 旧实现靠原生 `VScrollBar` 的 `Maximum/LargeChange` 判，那两个值在"页面还在构造期被隐藏 / 尺寸未定"
    /// 时是陈旧的：关于页实测停在 `max=478 large=100`，于是内容明明比可视短一屏（480 vs 812）也被判成可滚，
    /// 右边缘挂出一条拇指只有两成高的空条（他 2026-10-23 截图报的就是这条）。
    /// `DisplayRectangle` 与 `ClientSize` 才是这一刻的事实。</summary>
    internal static bool BarNeeded(int contentHeight, int viewHeight) => contentHeight > Math.Max(1, viewHeight);

    // 更新范围与当前位置（total = 内容总量，page = 可视量，value = 起点）
    public void Set(int total, int page, int value)
    {
        page = Math.Max(1, page);
        total = Math.Max(1, total);
        // 不能再把 total 抬到 page+1：那样"刚好一屏"也算可滚动，会画出一根满高的滑条（用户反馈：只在需要时展示）
        int v = Math.Clamp(value, 0, Math.Max(0, total - page));
        if (total == _total && page == _page && v == _value) return;
        _total = total;
        _page = page;
        _value = v;
        Cursor = Scrollable ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    int TrackH => ClientSize.Height;
    int ThumbH => Math.Max(UiTheme.S(this, MinThumb), (int)((long)TrackH * _page / _total));
    int ThumbTop => _total <= _page ? 0 : (int)((long)(TrackH - ThumbH) * _value / (_total - _page));

    Rectangle ThumbRect()
    {
        int w = Math.Max(UiTheme.S(this, Thickness), 1);
        int x = Math.Max(0, (ClientSize.Width - w) / 2);
        int h = Math.Max(UiTheme.S(this, 8), ThumbH - UiTheme.S(this, 2));
        int y = Math.Clamp(ThumbTop + UiTheme.S(this, 1), 0, Math.Max(0, TrackH - h));
        return new Rectangle(x, y, w, h);
    }

    protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Backdrop);

    protected override void OnPaint(PaintEventArgs e)
    {
        if (!Scrollable) return;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = ThumbRect();
        using var path = UiTheme.RoundRect(r, r.Width / 2);
        using var brush = new SolidBrush(_dragging || _hover ? ThumbActiveColor : ThumbColor);
        g.FillPath(brush, path);
    }

    // ---------------- 交互 ----------------

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (!Scrollable) return;
        var r = ThumbRect();
        if (r.Contains(e.Location))
        {
            _dragging = true;
            _grab = e.Y - r.Top;
            Capture = true;
        }
        else SetValue(_value + (e.Y < r.Top ? -_page : _page));   // 点空白处：翻一页
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging && _total > _page)
        {
            int span = TrackH - ThumbH;
            if (span > 0) SetValue((int)Math.Round((double)(e.Y - _grab) * (_total - _page) / span));
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _dragging = false;
        Capture = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        ScrollByWheel(e.Delta);
        base.OnMouseWheel(e);
    }

    // 滚轮一格滚多少：按可视页的 1/6（不设死行数，页高不同时速感一致）
    public void ScrollByWheel(int delta)
    {
        if (Scrollable) SetValue(_value - Math.Sign(delta) * Math.Max(1, _page / 6));
    }

    void SetValue(int v)
    {
        int nv = Math.Clamp(v, 0, Math.Max(0, _total - _page));
        if (nv == _value) return;
        _value = nv;
        Invalidate();
        Scrolled?.Invoke(this, EventArgs.Empty);
    }

    // ---------------- 静态辅助 ----------------

    const int SM_CXVSCROLL = 2;

    [DllImport("user32.dll")]
    static extern int GetSystemMetricsForDpi(int index, uint dpi);

    // 系统原生垂直滚动条在本进程 DPI 下的宽度：自绘条至少要这么宽才能把它盖住
    public static int SystemWidth(int dpi)
    {
        try
        {
            int w = GetSystemMetricsForDpi(SM_CXVSCROLL, (uint)dpi);
            if (w > 0) return w;
        }
        catch { /* Win10 1607 之前没有这个 API */ }
        return SystemInformation.VerticalScrollBarWidth;
    }

    // 把自绘滚动条挂到可滚动容器上，盖住它的原生滚动条并双向同步。
    // overlay 必须是 host 之外的一层（FlowLayoutPanel 会重排自己的子控件，滚动条不能挂进去），
    // 且 overlay 与 host 位置一致（设置页里 host 是 Dock=Fill 的 Tab 页，overlay 是它外面的内容区）。
    //
    // 滚轮这一路单独处理（BUG-010）：WinForms 把 WM_MOUSEWHEEL 投给**有焦点**的窗口，
    // 悬停滚动要不要转给光标底下的窗口由系统设置决定，落在输入框上时还会被输入框吃掉 ——
    // 结果就是「页面自己滚了、滑条不动」或者「滚了但滚的不是这一页」。所以：
    //   · WheelRouter（IMessageFilter）按光标位置把滚轮派给光标底下那一页，并驱动滑条；
    //   · HostScrollHook 兜住别进来的滚动（原生条被拖、键盘、系统仍自己滚的情况），事后回读位置。
    public static UiScrollbar AttachTo(ScrollableControl host, Control overlay, Color backdrop)
    {
        var bar = new UiScrollbar { Backdrop = backdrop, Visible = false };
        overlay.Controls.Add(bar);
        bar.BringToFront();
        bool busy = false;

        void Sync()
        {
            if (busy || host.IsDisposed || overlay.IsDisposed) return;
            if (!host.IsHandleCreated || !overlay.IsHandleCreated) return;
            busy = true;
            try
            {
                int w = Math.Max(UiTheme.S(host, 10), SystemWidth(UiTheme.Dpi));
                bar.SetBounds(host.Right - w, host.Top, w, host.Height);
                // 2026-10-23（BUG-086）：范围按**真几何**算，不读原生 VScrollBar 的那两个陈旧值（见 BarNeeded 的注释）。
                var contentH = host.DisplayRectangle.Height;
                var viewH = Math.Max(1, host.ClientSize.Height);
                bar.Set(contentH, viewH, -host.AutoScrollPosition.Y);
                bar.Visible = UiScrollbar.BarNeeded(contentH, viewH) && host.Visible;
            }
            finally { busy = false; }
        }

        // 滚动之后位置才稳定（AutoScroll 会把自己夹到合法范围），延迟到消息空闲再回读。
        // DEC-129①：再加一根 **host.Layout 重同步线** —— 内容"长高/变矮"（设置页那块显隐）之后滑条的范围
        // 必须跟着刷新，否则缓存停在旧值：用户实测"拨开那块之后往下滚不动、先往上滚再往下才行"
        //（往上滚触发 host.Scroll → 才回头重同步）。布局这件事发生在"使用者改内容"之后，只有宿主自己知道，
        // 所以由 Layout 事件兜住；按消息空闲延迟执行 + 排队去重（布局风暴只排一次）。证人位 = 自检的判据。
        void SyncLater()
        {
            bar._layoutSyncHits++;
            if (bar._syncQueued) return;
            bar._syncQueued = true;
            try { host.BeginInvoke(new Action(() => { bar._syncQueued = false; Sync(); })); }
            catch { bar._syncQueued = false; /* 窗口正在销毁 / 无头 */ }
        }

        bar.Scrolled += (_, _) =>
        {
            busy = true;
            try { host.AutoScrollPosition = new Point(0, bar.Value); }
            finally { busy = false; }
            SyncLater();
        };
        host.Scroll += (_, _) => Sync();
        host.ClientSizeChanged += (_, _) => Sync();
        host.VisibleChanged += (_, _) => Sync();
        host.ControlAdded += (_, _) => Sync();
        host.ControlRemoved += (_, _) => Sync();
        overlay.ClientSizeChanged += (_, _) => Sync();
        host.Layout += (_, _) => SyncLater();     // DEC-129①：内容长缩之后范围要跟上（见 SyncLater 上的注释）
        bar.SyncNow = Sync;

        var router = new WheelRouter(host, bar);
        bar.Router = router;            // 自检用：设置页要断言每页都装了滚轮路由
        Application.AddMessageFilter(router);
        var hook = new HostScrollHook(host, SyncLater);
        host.Disposed += (_, _) =>
        {
            try { Application.RemoveMessageFilter(router); } catch { /* 消息循环正在遍历快照 */ }
            try { hook.ReleaseHandle(); } catch { /* 句柄已随窗口销毁 */ }
        };

        Sync();
        return bar;
    }

    // 滚轮按光标位置派给底下那一页（见 AttachTo 的说明）
    internal sealed class WheelRouter : IMessageFilter
    {
        const int WM_MOUSEWHEEL = 0x020A;

        readonly ScrollableControl _host;
        readonly UiScrollbar _bar;

        public WheelRouter(ScrollableControl host, UiScrollbar bar) { _host = host; _bar = bar; }

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != WM_MOUSEWHEEL) return false;
            if (_host.IsDisposed || !_host.Visible || !_bar.Scrollable) return false;
            if (!_host.RectangleToScreen(_host.ClientRectangle).Contains(Cursor.Position)) return false;
            // 消息过滤器是全局的、且按注册顺序问：设置页先注册，模态弹窗（更新内容/回退…）盖在它上面时
            // 光标还在设置页矩形内，就会被设置页先把滚轮吃掉（用户实跑：在弹窗里滚，动的是后面的设置页）。
            // 判据 = 前台窗必须是本宿主所属的那个窗（下拉弹窗不抢激活，所以不受影响）。
            if (!OwnsForeground(_host)) return false;
            _bar.ScrollByWheel((short)(m.WParam.ToInt64() >> 16));
            return true;      // 这一击由我们消化：不要让焦点控件再滚一遍别的东西
        }

        // 单独抽出来：自检要直接断言这条判据（DEC-051）
        internal static bool OwnsForeground(Control host)
        {
            var top = Control.FromHandle(GetForegroundWindow());
            return top is null || ReferenceEquals(top.FindForm(), host.FindForm());
        }

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();
    }

    // 别的来源（拖原生条、键盘、系统自己响应滚轮）改了滚动位置后回读，保证滑条跟得上
    sealed class HostScrollHook : NativeWindow
    {
        const int WM_HSCROLL = 0x0114, WM_VSCROLL = 0x0115, WM_MOUSEWHEEL = 0x020A, WM_KEYDOWN = 0x0100;

        readonly Action _changed;

        public HostScrollHook(Control host, Action changed)
        {
            _changed = changed;
            if (host.IsHandleCreated) AssignHandle(host.Handle);
            else host.HandleCreated += (_, _) => AssignHandle(host.Handle);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg is WM_HSCROLL or WM_VSCROLL or WM_MOUSEWHEEL or WM_KEYDOWN) _changed();
        }
    }
}
