using System.Drawing.Drawing2D;

namespace MultiLauncher;

// 自绘深色小提示（DEC-066）。为什么要有它：用户口径「点了必须有看得见的落点」，而设置页保存、路径选择、
// 预设存取、更新完成这些以前**只写一行日志** —— 日志可能长在另一个窗上、还会被滚走，等于没反馈。
//
// 形状：无边框、圆角、左侧一条状态色竖条，出现在宿主窗水平居中、距窗底约 1/4 窗高处（2026-10-04 改），
// 动画（2026-10-04 DEC-075，原型定稿 = 变体 B）：出现 = 由下 16px 滑上 + 淡入 180ms；
// 消失 = 淡出 + 再上飘 10px 200ms；停留 2.5 秒；同屏多条时往上堆，不互相盖。
// 三条硬口径：
//   · **不抢焦点**（WS_EX_NOACTIVATE）也不进任务栏（WS_EX_TOOLWINDOW）——提示不该把光标从用户正在填的格子里抢走。
//   · 尺寸换算只用 UiTheme.S（与弹窗族同一口径，别再引 owner.DeviceDpi）。
//   · 它是"告诉你一声"，不是要你决策：需要回答的一律走 Dialogs.*，别拿 toast 当确认框用。
internal static class Toast
{
    public enum Kind { Info, Ok, Warn, Error }

    internal const int VisibleMs = 2500;      // 停留时长（不含滑入/退场动画）
    internal const int MaxLive = 4;           // 同屏最多几条，超了把最老的立刻收掉（不许叠成一堵墙）

    // 动画（2026-10-04 DEC-075，原型定稿=变体 B）：出现 = 由下 16px 滑上 + 淡入 180ms（ease-out）；
    // 消失 = 淡出 + 再上飘 10px 200ms（ease-in，"飘走"感，与出现同向）。
    // 帧计算抽成纯函数：真定时器按 15ms 一拍调用它们，自检也能直接断言曲线（起止点 / 单调 / 中点）。
    // 2026-10-16（§5.1）：两条曲线搬到 UiMotion 那一处（叠放按钮共用同一条，不再各写一遍 pow）—— 数值一字未变。
    internal const int ShowMs = 180, HideMs = 200, RiseLogical = 16, DriftLogical = 10;

    internal static (double Opacity, double Dy) ShowFrame(int elapsedMs)
    {
        double k = UiMotion.EaseOutCubic(elapsedMs / (double)ShowMs);   // ease-out cubic
        return (k, RiseLogical * (1 - k));
    }

    internal static (double Opacity, double Dy) HideFrame(int elapsedMs)
    {
        double k = UiMotion.EaseInQuad(elapsedMs / (double)HideMs);     // ease-in quad
        return (1 - k, -DriftLogical * k);
    }

    static readonly List<ToastShell> _live = new();

    // DEC-127⑥（他点名："自检时经常弹『界面参数已保存』…能不能不弹这个"）：自检期间一律不弹提示。
    // 根因不是那句 toast 该不该有，而是**自检真走了「保存」那一条路**（SelfTest 里 `SaveParameterEditsForTest`
    // 就是要点一次真保存才能断"落盘"），而 `Toast.Show` 在宿主窗不可见时会退回屏幕工作区照样 `Show()` ——
    // 于是他在旁边看着一条一条闪。DEC-111② 当年把 `logs\` 按进程收口了，这一族漏了，这次补上同一条闸。
    // 只在 **SelfTest.Run 入口**点名（出图台不开：`--uiprobe toast` 那六张拍的就是真 toast，开了闸等于拍空气）。
    // 判据仍是两面：`AttemptsForTest`（这条路真被走过）+ `ShownForTest`（真弹出去几条）——
    // 只断"一条都没弹"会连"根本没走到这里"一起放过（DEC-111/BUG-043 那一族空判据）。
    internal static bool DisabledForTest = false;
    static int _attempts, _shown;
    internal static int AttemptsForTest => _attempts;
    internal static int ShownForTest => _shown;

    /// <summary>要不要弹（纯函数 = 画与断言共用这一个家）。红/绿双向验按回去的就是这道判据。</summary>
    internal static bool WouldShow(bool disabledForTest, string? text)
        => !disabledForTest && !string.IsNullOrWhiteSpace(text);

    public static Color Accent(Kind kind) => kind switch
    {
        Kind.Ok => UiTheme.Brand,
        Kind.Warn => Color.FromArgb(0xF5, 0x9E, 0x0B),
        Kind.Error => UiTheme.Danger,
        _ => UiTheme.TextMuted,
    };

    /// 弹一条提示。owner 可空（托盘那条路主窗藏着）；owner 不可见 / 最小化时退回屏幕工作区（同一条摆位规矩）。
    /// 从后台线程调（更新链就是这么来的）会自己 marshal 到 owner 的 UI 线程。
    /// 弹不出来也不报错给调用方：同一句话日志已经写了，提示只是补个落点。
    public static void Show(Form? owner, string text, Kind kind = Kind.Info)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        _attempts++;                                 // 证人位：这条路真被走到过（闸挡下几条就记几条，自检断的不是空判据）
        if (!WouldShow(DisabledForTest, text)) return;   // DEC-127⑥：自检期间一条都不许弹到屏幕上
        // DEC-137（F-1）：线程转发**必须排在可见性过滤之前**。原先 `UsableOwner` 先把藏托盘/最小化的宿主判成 null，
        // 于是下面那道 `anchor.InvokeRequired` 整个被跳过 = 更新链在后台线程上直接 `shell.Show()`：
        // 句柄建在没有消息泵的线程池线程上 → 不重绘、OnShown 那枚 15ms 动画定时器一拍都不走 →
        // 一条 Opacity=0 的窗悬在屏幕中下方吃掉那块点击、并永久挂在 _live 里；攒到第 5 条时
        // DismissNow() 跨线程 Close() 的异常被吞 → 此后任何提示都不再出现（日志照写，看着像"只是没弹"）。
        // 他机器上 HideToTrayOnStart=True，这条路天天走。文件头第 68 行的注释承诺的就是"自己 marshal"，现在才名副实。
        if (owner is { IsDisposed: false } o && o.InvokeRequired)
        {
            try { o.BeginInvoke(new Action(() => Show(o, text, kind))); } catch { /* 窗正在关 */ }
            return;
        }
        var anchor = UsableOwner(owner);
        if (anchor is null && owner is null)
        {
            // 没有宿主（托盘那一路）：挑一枚还活着的窗做转发，一个都没有就只留日志、不硬弹
            var live = Application.OpenForms.OfType<Form>().FirstOrDefault(f => !f.IsDisposed);
            if (live is not null)
            {
                if (live.InvokeRequired) { try { live.BeginInvoke(new Action(() => Show(live, text, kind))); } catch { } return; }
                anchor = live;
            }
        }
        var screen = Screen.FromPoint(anchor?.Location ?? Cursor.Position).WorkingArea;
        ToastShell? overflow = null;
        var shell = new ToastShell(text.Trim(), Accent(kind));
        try
        {
            shell.Location = NextPlace(anchor, shell, screen, UiTheme.S(shell, 14));
            shell.Opacity = 0;                // 先全透明再 Show（滑入由 OnShown 的动画接管），免得闪一帧全亮
            shell.Show();                     // 不用 ShowDialog：提示不该挡任何人
            _shown++;                         // 证人位：真弹出去了（DEC-127⑥ 的反面 = 闸放开时确实弹得出去）
            shell.FormClosed += (_, _) => { lock (_live) _live.Remove(shell); };
            lock (_live)
            {
                _live.Add(shell);
                if (_live.Count > MaxLive) overflow = _live[0];
            }
        }
        catch { shell.Dispose(); }
        overflow?.DismissNow();               // 拆旧条放在锁外：Close 会回调 FormClosed，别在锁里绕回来
    }

    static Form? UsableOwner(Form? f)
        => f is { IsDisposed: false, Visible: true } x && x.WindowState != FormWindowState.Minimized ? x : null;

    // 出图用：当前活着的几条（从新到旧无所谓，探针自己排）
    internal static IReadOnlyList<ToastShell> LiveForTest() { lock (_live) return _live.ToArray(); }

    // 摆位：先按锚点算基准位，再往上躲开已经活着的那几条，最后夹回工作区
    internal static Point NextPlace(Form? anchor, ToastShell shell, Rectangle screen, int gap)
    {
        var size = shell.Size;
        var p = PlaceFor(anchor?.Bounds, size, screen);
        lock (_live)
        {
            foreach (var live in _live)
            {
                if (live.IsDisposed || live == shell) continue;
                if (live.Right < p.X - size.Width || live.Left > p.X + size.Width) continue;   // 不在同一列，不用躲
                p = new Point(p.X, Math.Min(p.Y, live.Top - size.Height - UiTheme.S(live, 6)));
            }
        }
        return Clamp(p, size, screen, gap);
    }

    // 纯函数（自检断言）：位置 = 水平居中于宿主窗、纵向"距窗底约 1/4 窗高"（单条的中线压在这条线上，多条再往上堆）。
    // 2026-10-04 用户指定（原来是贴右下角）：右下角离眼睛太远、还容易跟右下角别的浮层打架；放到窗口中下方居中。
    // 宿主窗不可用（主窗藏着 / 最小化）时按屏幕工作区套同一条规矩。
    internal static Point PlaceFor(Rectangle? hostBounds, Size size, Rectangle screen)
        => hostBounds is { } h && h.Width > 0 && h.Height > 0
            ? new Point(h.Left + (h.Width - size.Width) / 2, h.Bottom - h.Height / 4 - size.Height / 2)
            : new Point(screen.Left + (screen.Width - size.Width) / 2, screen.Bottom - screen.Height / 4 - size.Height / 2);

    internal static Point Clamp(Point p, Size size, Rectangle screen, int gap)
        => new(Math.Max(screen.Left + gap, Math.Min(p.X, screen.Right - size.Width - gap)),
               Math.Max(screen.Top + gap, Math.Min(p.Y, screen.Bottom - size.Height - gap)));

    // ---------------- 那一条提示本身 ----------------

    internal sealed class ToastShell : Form
    {
        const int CS_DROPSHADOW = 0x20000;
        const int WS_EX_NOACTIVATE = 0x08000000;
        const int WS_EX_TOOLWINDOW = 0x00000080;

        readonly string _text;
        readonly Color _accent;
        readonly Font _font;
        internal string MessageForTest => _text;     // 出图用：文件名带上文案，别拍成一堆 toast-1/2/3
        System.Windows.Forms.Timer? _timer;
        int _phase;                             // 0 = 滑入中，1 = 停留中，2 = 退场中
        int _phaseStart;
        Point _final;                           // 摆位算出来的静止位置（滑入从它下方 16px 出发，退场向它上方飘 10px）
        double _outFromOpacity = 1;             // 退场起点（滑入半路被打断时从"当前"出发，不跳变）
        int _outFromY;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ClassStyle |= CS_DROPSHADOW;
                cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;   // 置顶走 Form.TopMost，不重复设 EX_TOPMOST
                return cp;
            }
        }

        public ToastShell(string text, Color accent)
        {
            _text = text;
            _accent = accent;
            _font = new Font("Microsoft YaHei UI", 9f);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = UiTheme.Card;
            DoubleBuffered = true;

            // 量字必须带 NoPadding：默认 MeasureText 把行距空白算进去，文字会顶到上沿（DEC-043 的教训）
            var box = TextRenderer.MeasureText(text, _font, new Size(UiTheme.S(this, 360), 0),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            ClientSize = new Size(UiTheme.S(this, 8) + UiTheme.S(this, 3) + UiTheme.S(this, 9) + box.Width + UiTheme.S(this, 14),
                Math.Max(UiTheme.S(this, 38), UiTheme.S(this, 10) * 2 + box.Height));
            using var path = UiTheme.RoundRect(new Rectangle(0, 0, ClientSize.Width, ClientSize.Height), Radius());
            Region = new Region(path);
        }

        int Radius() => UiTheme.S(this, Math.Max(6, ClientSize.Height / 3));

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            UiTheme.CaptureDpi(Handle);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _final = Location;                              // 摆位结果先记下
            Location = new Point(_final.X, _final.Y + UiTheme.S(this, RiseLogical));   // 从下 16px 出发
            _phase = 0;
            _phaseStart = Environment.TickCount;
            var t = new System.Windows.Forms.Timer { Interval = UiMotion.FrameMs };   // 心跳档位与曲线同一个家（UiMotion）
            t.Tick += (_, _) => Tick();
            t.Start();
            _timer = t;
        }

        // 15ms 一拍：滑入（ShowFrame）→ 停留 VisibleMs → 退场（HideFrame）→ 关闭。
        // 帧计算全在 Toast.ShowFrame / HideFrame（纯函数，自检直接断言），这里只做应用与相位切换。
        void Tick()
        {
            int now = Environment.TickCount;
            int e = now - _phaseStart;
            if (_phase == 0)
            {
                var (op, dy) = ShowFrame(e);
                Opacity = op;
                Location = new Point(_final.X, _final.Y + UiTheme.S(this, (int)Math.Round(dy)));
                if (e >= ShowMs) { Opacity = 1; Location = _final; _phase = 1; _phaseStart = now; }
                return;
            }
            if (_phase == 1)
            {
                if (e < VisibleMs) return;
                BeginDismiss();
                return;
            }
            var (op2, dy2) = HideFrame(e);
            Opacity = _outFromOpacity * op2;
            Location = new Point(_final.X, _outFromY + UiTheme.S(this, (int)Math.Round(dy2)));
            if (e < HideMs) return;
            StopTimer();
            Close();
        }

        void StopTimer()
        {
            try { _timer?.Stop(); _timer?.Dispose(); } catch { /* 正在拆 */ }
            _timer = null;
        }

        // 点一下就收 / 停留到点自动收：播放退场动画（变体 B：淡出 + 上飘 10px）。
        // 从"当前不透明度与位置"出发，所以滑入半路被点也不跳变。
        public void BeginDismiss()
        {
            if (IsDisposed || _phase == 2) return;
            _outFromOpacity = Opacity;
            _outFromY = Location.Y;
            _phase = 2;
            _phaseStart = Environment.TickCount;
        }

        // 不播动画立刻收：同屏超额要腾位时用（等 200ms 退场反而会叠到 5 条）
        public void DismissNow()
        {
            if (IsDisposed) return;
            StopTimer();
            try { Close(); } catch { /* 已经在拆了 */ }
        }

        // 点一下就收：用户不想等的提示不该硬撑两秒半
        protected override void OnMouseDown(MouseEventArgs e) { BeginDismiss(); base.OnMouseDown(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
            using (var path = UiTheme.RoundRect(r, Radius()))
            {
                using var fill = new SolidBrush(UiTheme.Card);
                g.FillPath(fill, path);
                using var pen = new Pen(UiTheme.Border, 1f);
                g.DrawPath(pen, path);
            }
            int barX = UiTheme.S(this, 8), barW = UiTheme.S(this, 3);
            using (var bar = new SolidBrush(_accent))
                g.FillRectangle(bar, barX, UiTheme.S(this, 8), barW, ClientSize.Height - UiTheme.S(this, 16));
            int x = barX + barW + UiTheme.S(this, 9);
            var textRect = new Rectangle(x, 0, ClientSize.Width - x - UiTheme.S(this, 14), ClientSize.Height);
            TextRenderer.DrawText(g, _text, _font, textRect, UiTheme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                StopTimer();            // WinForms 只拆控件树，不替你还自己 new 的定时器（DEC-061 那条口径）
                _font.Dispose();
                Region?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
