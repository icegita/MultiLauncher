using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace MultiLauncher;

// 深色下拉（整套自绘）。不直接用 ComboBox 的原因：
//   1) 原生弹出列表在深色主题下是白底；
//   2) ComboBox 不支持「分组标题 + 分割线」；
//   3) OwnerDraw 只能改列表项绘制，收起时的方框、边框、箭头仍走系统主题，风格不统一。
// 因此：收起态 = 自绘圆角框；展开态 = 无边框弹出窗（宽度可超出控件，保证长名字完整显示）。
//
// 弹出窗**不抢激活**（WS_EX_NOACTIVATE + ShowWithoutActivation）：主窗在整个展开期间一直是激活窗口，
// 所以收起时不存在「把层级还回去」这回事。以前让它自己激活、收起时再 SetWindowPos 提顶，实测压不住 ——
// 弹出窗是在自己的 WM_ACTIVATE(失活) 处理里被销毁的，Windows 这时会把前台交给 z 序上的下一个窗口
// （往往是别的应用），主窗就被露在后面了。见 BUG-009。
// 代价：焦点始终留在收起框上，所以键盘与滚轮要由 DarkSelect 转发给弹出窗；关闭时机改成「点到列表外」
// 与「主窗失活」（消息过滤器 + Form.Deactivate），不再有 DropForm.Deactivate。
internal enum DarkItemKind { Normal, Header, Separator }

internal sealed class DarkItem
{
    public DarkItemKind Kind = DarkItemKind.Normal;
    public string Text = "";
    public string Right = "";      // 右侧灰字（估算大小、版本号等）
    public string Badge = "";      // 右侧品牌绿状态词（DEC-065：「已加载」），画在 Right 前面
    public object? Tag { get; set; }
    public bool Enabled = true;

    public static DarkItem Header(string text) => new() { Kind = DarkItemKind.Header, Text = text };
    public static DarkItem Separator() => new() { Kind = DarkItemKind.Separator };
}

internal sealed partial class DarkSelect : Control
{
    public Color FillColor = UiTheme.Input;
    public Color BorderColor = UiTheme.Border;
    public Color Backdrop = UiTheme.Card;
    public int Radius = 8;
    // 弹列表宽度 = **控件自身宽度**（用户 2026-10-04：菜单宽度和输入框对不上，看着就是错位）。
    // 需要更宽的（长模型名、备份时间戳那种）在实例上自己设地板，别改这里的默认值。
    public int MinDropWidth = 0;

    readonly List<DarkItem> _items = new();
    int _sel = -1;
    int _closedTicks;                   // 刚因失焦关闭的时间：防止「点一下先关再开」
    DropForm? _popup;
    OutsideClickGuard? _guard;          // 弹出窗不抢激活，靠它发现「点到列表外了」
    Form? _guardedOwner;                // 挂过滤器时宿主窗，收起时要解绑
    EventHandler? _onOwnerLostFocus;
    EventHandler? _onOwnerMoved;

    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    public event EventHandler? SelectedIndexChanged;

    public DarkSelect()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
               | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
               | ControlStyles.Selectable, true);
        TabStop = true;
        Cursor = Cursors.Hand;
        Font = new Font("Microsoft YaHei UI", 9f);
        Height = UiTheme.S(this, 30);
    }

    public IReadOnlyList<DarkItem> Items => _items;

    // 出图用：展开中的那个列表窗（没有展开就是 null）。PrintWindow 要按窗拍，所以给个只读出口。
    internal Form? DropForTest => _popup;
    internal void OpenDropDownForTest() => OpenDropDown();
    internal void CloseDropDownForTest() => CloseDropDown();

    [DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => _sel;
        set
        {
            var v = value >= 0 && value < _items.Count && _items[value].Kind == DarkItemKind.Normal ? value : -1;
            if (v == _sel) return;
            _sel = v;
            // 同步窗口文本：语义上这就是"当前值"，自动化/无障碍也靠它定位
            Text = _sel >= 0 && _sel < _items.Count ? _items[_sel].Text : "";
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public DarkItem? Selected => _sel >= 0 && _sel < _items.Count ? _items[_sel] : null;

    // 换列表：不触发 SelectedIndexChanged（调用方随后自己设选中项）
    public void SetItems(IEnumerable<DarkItem> items)
    {
        _items.Clear();
        _items.AddRange(items);
        _sel = -1;
        Invalidate();
    }

    public void SelectByTag(object tag)
    {
        for (int i = 0; i < _items.Count; i++)
        {
            if (!Equals(_items[i].Tag, tag)) continue;
            _sel = i;
            Text = _items[i].Text;      // 与 SelectedIndex 一样同步窗口文本
            Invalidate();
            return;
        }
    }

    // 只改右侧状态词并重绘（DEC-065：加载状态是实时变的，而 SetItems 会把选中项与展开态一起吞掉）。
    // 列表开着也要跟着变 —— DropForm 与收起框用的是同一批 DarkItem 对象，改完两边各自重绘。
    public void RefreshBadges(Func<DarkItem, string> badgeOf)
    {
        bool changed = false;
        foreach (var it in _items)
        {
            var b = badgeOf(it);
            if (string.Equals(it.Badge, b, StringComparison.Ordinal)) continue;
            it.Badge = b ?? "";
            changed = true;
        }
        if (_popup is { IsDisposed: false } p) p.Invalidate();
        if (changed) Invalidate();
    }

    // ---------------- 绘制 ----------------

    // 右侧那两列（品牌绿状态词 + 灰色估算大小）只有一处摆位算法：靠右排，最右是大小，状态词在它左边。
    // 收起框与展开列表都走这里，免得两处各画一套、改一处忘一处。返回吃掉的宽度（调用方据此收窄文字区）。
    static int DrawRightRuns(Graphics g, DarkItem it, int rightEdge, int top, int height,
        Font sizeFont, Font badgeFont, int gap)
    {
        int used = 0, x = rightEdge;
        if (it.Right.Length > 0)
        {
            int w = TextRenderer.MeasureText(it.Right, sizeFont).Width;
            TextRenderer.DrawText(g, it.Right, sizeFont, new Rectangle(x - w, top, w, height),
                UiTheme.TextMuted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            x -= w; used += w;
        }
        if (it.Badge.Length > 0)
        {
            if (used > 0) { x -= gap; used += gap; }
            int w = TextRenderer.MeasureText(it.Badge, badgeFont).Width;
            TextRenderer.DrawText(g, it.Badge, badgeFont, new Rectangle(x - w, top, w, height),
                UiTheme.Brand, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            used += w;
        }
        return used;
    }

    protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Backdrop);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = ClientRectangle;
        r.Width -= 1; r.Height -= 1;
        using (var path = UiTheme.RoundRect(r, UiTheme.S(this, Radius)))
        {
            using var brush = new SolidBrush(FillColor);
            g.FillPath(brush, path);
            // 描边只在**展开期间**高亮：按 Focused 判会留残影（展开过一次后焦点一直在这，绿框赖着不走）
            using var pen = new Pen(_popup is { IsDisposed: false } ? UiTheme.Brand : BorderColor, 1f);
            g.DrawPath(pen, path);
        }

        int arrow = UiTheme.S(this, 16);
        int padX = UiTheme.S(this, 8);
        var item = Selected;

        // 箭头（自绘三角，避免依赖字体里的 ▾ 字形）
        int ax = r.Right - padX - arrow / 2;
        int ay = r.Height / 2;
        int a = UiTheme.S(this, 4);
        using (var pen = new Pen(UiTheme.TextMuted, 1.4f))
        {
            g.DrawLine(pen, ax - a, ay - a / 2, ax, ay + a / 2);
            g.DrawLine(pen, ax, ay + a / 2, ax + a, ay - a / 2);
        }

        var textRect = new Rectangle(padX, 0, TextRoomPx(r.Width), r.Height);
        if (item is null)
        {
            TextRenderer.DrawText(g, "未选择", Font, textRect, UiTheme.TextMuted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            return;
        }
        int rightW = DrawRightRuns(g, item, textRect.Right, 0, r.Height, Font, Font, UiTheme.S(this, 8));
        if (rightW > 0) textRect.Width = Math.Max(1, textRect.Width - rightW - UiTheme.S(this, 8));
        TextRenderer.DrawText(g, item.Text, Font, textRect, item.Enabled ? UiTheme.Text : UiTheme.TextMuted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    // ---------------- 展开 / 收起 ----------------

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        if (_popup is not null) { CloseDropDown(); return; }
        // 刚因失焦关掉（就是这一次点击造成的）就别马上再开
        if (Environment.TickCount - _closedTicks < 200) return;
        OpenDropDown();
        base.OnMouseDown(e);
    }

    // 展开期间焦点仍在收起框上（弹出窗不抢激活），所以列表的按键在这里接、转给列表
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (_popup is { IsDisposed: false } p && p.HandleKey(keyData)) return true;
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Down or Keys.Enter or Keys.Space) { OpenDropDown(); e.Handled = true; return; }
        base.OnKeyDown(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        // 滚轮按「焦点窗口」投递时落在本控件上：光标在列表里就转给列表，否则照旧冒泡
        if (_popup is { IsDisposed: false } p && p.RectangleToScreen(p.ClientRectangle).Contains(Cursor.Position))
        {
            p.ScrollBy(e.Delta);
            return;
        }
        base.OnMouseWheel(e);
    }

    // 收起框里文字实际可用的宽度（与 OnPaint 里 textRect 的同一条算式；抽出来给自检量"这格放得下取值吗"）
    int TextRoomPx(int boxWidth)
        => Math.Max(1, boxWidth - UiTheme.S(this, 8) * 2 - UiTheme.S(this, 16) - UiTheme.S(this, 4));
    internal int TextRoomForTest() => TextRoomPx(Width - 1);        // OnPaint 先减了 1（描边留位）

    // 弹列表宽度：默认 = 控件自身宽（菜单与输入框对得上），MinDropWidth 只当地板，最后再被屏幕可用宽夹住。
    int PopupWidth(int maxAvailable) => Math.Min(Math.Max(Width, UiTheme.S(this, MinDropWidth)), Math.Max(1, maxAvailable));
    internal int PopupWidthForTest(int maxAvailable = 100000) => PopupWidth(maxAvailable);

    public void OpenDropDown()
    {
        if (_popup is not null || !Enabled || !Visible) return;
        if (_items.Count == 0) return;

        var screen = Screen.FromControl(this).WorkingArea;
        int w = PopupWidth(screen.Width - UiTheme.S(this, 16));
        int maxH = Math.Min(UiTheme.S(this, 380), screen.Height / 2);
        var f = new DropForm(_items, _sel, w, maxH, DeviceDpi);
        var top = PointToScreen(new Point(0, Height + UiTheme.S(this, 2)));
        int y = top.Y;
        if (y + f.Height > screen.Bottom)
        {
            int above = PointToScreen(Point.Empty).Y - f.Height - UiTheme.S(this, 2);
            y = above >= screen.Top ? above : Math.Max(screen.Top, screen.Bottom - f.Height);
        }
        f.Location = new Point(top.X, y);
        // Picked/Cancelled 都**延迟**关窗（CloseDropDownLater）：这两个回调是在弹窗自己的 WndProc 栈上触发的，
        // 当场 Dispose 自己会让后续消息处理踩在已释放对象上（BUG-009 的那类坑）；选中值立即更新，关窗投到空闲时。
        f.Picked += i => { CloseDropDownLater(); SelectedIndex = i; };
        f.Cancelled += () => CloseDropDownLater();
        _popup = f;
        try
        {
            f.Show(FindForm());         // 不 Activate：列表全程不抢主窗激活（见文件头 BUG-009 说明）
            f.ReassertSize();           // 窗口被系统改过尺寸时钉回请求值（见 ReassertSize 注释）
            AttachGuards(f);
            Invalidate();               // 描边要跟着"展开中"这个状态变
        }
        catch { _popup = null; f.Dispose(); }
    }

    // 关闭时机改由这两处判断（弹出窗不激活，所以它自己收不到 Deactivate）：
    //   · 点到列表外（本进程内的鼠标按下）→ 消息过滤器；
    //   · 主窗失活 / 挪位 / 改大小 → 宿主窗事件。
    void AttachGuards(DropForm f)
    {
        var owner = FindForm();
        DetachGuards();
        if (owner is null) return;
        _guard = new OutsideClickGuard(f, CloseDropDownLater);
        Application.AddMessageFilter(_guard);
        _onOwnerLostFocus = (_, _) =>
        {
            // 失活若只是"弹出窗自己变成了前台窗"就别收列表——用户正要点的就是它。
            // 正常情况下弹出窗根本不该激活（见 DropForm.WndProc 的 WM_MOUSEACTIVATE 说明），
            // 这条是兜底：万一某代系统/外设还是把它激活了，也不至于让这一击连人和列表一起消失。
            if (_popup is { IsDisposed: false } p && GetForegroundWindow() == p.Handle) return;
            CloseDropDown();
        };
        _onOwnerMoved = (_, _) => CloseDropDown();
        owner.Deactivate += _onOwnerLostFocus;
        owner.LocationChanged += _onOwnerMoved;
        owner.SizeChanged += _onOwnerMoved;
        _guardedOwner = owner;
    }

    void CloseDropDownLater()
    {
        // 消息过滤器正在派发鼠标消息，不当场销毁窗口：投回消息循环空闲时再收
        try { BeginInvoke(new Action(CloseDropDown)); }
        catch { CloseDropDown(); }      // 句柄还没建（自检只搭控件树）：直接收
    }

    void DetachGuards()
    {
        if (_guard is not null)
        {
            try { Application.RemoveMessageFilter(_guard); } catch { /* 消息循环正在遍历快照，本来就可重入 */ }
            _guard = null;
        }
        if (_guardedOwner is not null)
        {
            if (_onOwnerLostFocus is not null) _guardedOwner.Deactivate -= _onOwnerLostFocus;
            if (_onOwnerMoved is not null)
            {
                _guardedOwner.LocationChanged -= _onOwnerMoved;
                _guardedOwner.SizeChanged -= _onOwnerMoved;
            }
            _guardedOwner = null;
        }
    }

    void CloseDropDown()
    {
        var f = _popup;
        _popup = null;
        _closedTicks = Environment.TickCount;
        DetachGuards();
        if (f is not null)
        {
            try { f.Hide(); f.Dispose(); } catch { /* 已销毁 */ }
        }
        Invalidate();
        var form = FindForm();
        if (form is null) return;
        if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal;
        // 不再 SetWindowPos / Activate：主窗从没失过激活，层级本就不会变（老写法见文件头）
        if (Visible && Enabled && !Focused) Focus();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) CloseDropDown();
        base.Dispose(disposing);
    }

    // 点到列表外就收列表（弹出窗不抢激活，收不到它自己的 Deactivate，只能自己盯）。
    // 客户端区域的这一击**吞掉**：与原生 ComboBox 一致，关列表的那一下不透到后面的按钮上，
    // 免得顺手点空白处却把「启动 / 删除」按了。标题栏与边框（NC）照旧放行，窗口还能正常拖动缩放
    // —— 列表会随主窗挪位/改大小由 AttachGuards 里的 LocationChanged / SizeChanged 关掉。
    sealed class OutsideClickGuard : IMessageFilter
    {
        const int WM_LBUTTONDOWN = 0x0201, WM_RBUTTONDOWN = 0x0204;

        readonly DropForm _pop;
        readonly Action _close;

        public OutsideClickGuard(DropForm pop, Action close) { _pop = pop; _close = close; }

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg is not (WM_LBUTTONDOWN or WM_RBUTTONDOWN)) return false;
            if (_pop.IsDisposed || !_pop.Visible) return false;
            // 消息目标就是列表窗口 → 一定点在列表里，放行。
            // （用户反馈"第一次点击不生效、要重新展开点第二次才选中"：老写法用 Cursor.Position 与窗口矩形做
            //  坐标换算，首击里换算和真实光标可能错一拍；改用消息目标判定后没有时序/坐标系可错。）
            if (m.HWnd == _pop.Handle) return false;
            // 兜底：消息发给了别处，但光标其实贴着列表边缘（窗口被系统改过尺寸留下的缝）——按行选中
            var pt = Cursor.Position;
            var rc = _pop.RectangleToScreen(_pop.ClientRectangle);
            var near = rc;
            near.Inflate(Tol(10), Tol(4));
            if (m.Msg == WM_LBUTTONDOWN && near.Contains(pt))
            {
                int cy = Math.Max(0, Math.Min(rc.Height - 1, pt.Y - rc.Top));
                if (_pop.PickAtClientY(cy)) return true;    // 吞掉：别透到背后的窗口上
            }
            _close();     // 正在派发消息，不当场销毁窗口：投回消息循环空闲时再收
            return true;
        }

        int Tol(int logical) => (int)Math.Round(logical * _pop.DeviceDpi / 96.0);
    }

}