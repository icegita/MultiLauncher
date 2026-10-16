namespace MultiLauncher;

// 设置窗（独立窗口，4 个自绘 Tab：通用 / Strata / llama.cpp / 关于）。
// 生效时机（用户反馈 2026-10-01 修订）：**全部项都点「保存」才生效并落盘**；「取消」丢弃所有改动
//（原来开关类是"拨动即生效"，与数值/路径类两套语义，用户要求统一）。
// 落盘与副作用都交回 MainForm（它才是配置的主人）。
//
// 布局纪律：每一页是 FlowLayoutPanel（TopDown），每行是一个 Panel；
// 行内**必须在行的 Layout 事件里按行宽摆**，不能在构建时按页面宽度算坐标 ——
// 页面未显示时 ClientSize 还是 0，算出来的位置全是错的（第一版就这么错的）。
internal sealed partial class SettingsForm : Form
{
    readonly MainForm _main;
    readonly Settings _s;
    readonly Panel _content = new() { Dock = DockStyle.Fill, BackColor = UiTheme.Window };
    readonly SegmentedControl _segTabs;         // 页签（2026-10-02 反馈修订：分段样式，参考用户给的样式图）
    readonly FlowLayoutPanel[] _pages;
    readonly UiScrollbar[] _bars;               // 每个 Tab 页一根自绘细滑条（盖住原生滚动条），挂在 _content 上
    // 自检用：记下「标题 ↔ 右侧控件」的配对，断言它们的垂直中心对得上（DEC-014 第 3 条）
    readonly List<(Control a, Control b, string what)> _alignPairs = new();

    // DEC-058：更新那三行搬进了「更新」窗，设置页只留两行的**本机版本值**（升级完/切目录后要跟着刷）
    Label? _lblVerLocal;
    Label? _lblLlamaVer;
    Label? _lblNinferVer;               // DEC-096：Ninfer 那条更新链把版本换掉了，这一行要跟着重读
    Label? _lblNinferWslVer;            // DEC-097③：同一个窗里 WSL 那一侧的版本（读的是 VM 里的 git 提交）
    RoundedTextBox? _boxLlamaDir;
    FlatButton? _btnRollback;
    Action? _refreshHandler;      // 刷新是广播（设置窗 + 更新窗都挂），关窗时摘自己那一份
    Panel? _footer;               // 底部保存栏（DEC-059：底色必须与内容区一致，别再单独一块 Card）

    const int PadX = 20;
    internal const int PadXForTest = PadX;    // 自检量「说明文字有没有铺满整行」要用同一个内边距

    public SettingsForm(MainForm main, Settings current)
    {
        _main = main;
        _s = current.Clone();

        Text = "设置";
        BackColor = UiTheme.Window;
        ForeColor = UiTheme.Text;
        Font = UiTheme.Ui(9f);
        AutoScaleMode = AutoScaleMode.None;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        CreateControl();
        UiTheme.CaptureDpi(Handle);
        // 基准客户高 = ClientLogicalH。DEC-128⑤（他补话定案）：这是**恒定**窗高 ——
        // 「显示状态栏」那排显隐只让页面内容变长/变短、滚动条跟着走，窗子一个像素都不动
        //（作废 DEC-127④ 的"随开关长缩 + 自动滚到眼前"）。
        ClientSize = new Size(UiTheme.S(this, 680), UiTheme.S(this, ClientLogicalH));
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { /* 默认图标 */ }

        // 设置窗（2026-10-02 复验反馈修订，DEC-028）：通用 / **每个启用模块各一节** / 关于——
        // 不再只跟主区激活页（两个模块都启用时两节都在）；停用模块其节整节消失（全隐身）。
        // 第 0 轮 DEC-094：节名与"这一节由哪个函数搭"都按模块表来 —— 加模块时忘了搭一节会**当场抛**，
        // 不会像原来那样悄悄把新模块渲染成 Strata 那一节。
        var names = new List<string> { "通用" };
        var builders = new List<Func<FlowLayoutPanel>> { PageGeneral };
        foreach (var m in ModuleRegistry.All.Where(x => x.Enabled(_s)))
        {
            names.Add(m.DisplayName);
            builders.Add(() => PageForModule(m.Key));
        }
        names.Add("关于");
        builders.Add(PageAbout);

        var tabRow = new Panel { Dock = DockStyle.Top, Height = UiTheme.S(this, 42), BackColor = UiTheme.Window };
        _segTabs = new SegmentedControl { Backdrop = UiTheme.Window };
        _segTabs.SetItems(names.ToArray());
        // 分段控件含胶囊内缩、比按钮高：在页签带里按中线摆（2026-10-02 反馈：胶囊高度对齐按钮）
        _segTabs.Location = new Point(UiTheme.S(this, 16), Math.Max(0, (tabRow.Height - _segTabs.Height) / 2));
        _segTabs.ValueChanged += (_, _) => ShowPage(_segTabs.ActiveIndex);
        tabRow.Controls.Add(_segTabs);

        _pages = new FlowLayoutPanel[names.Count];
        for (int i = 0; i < names.Count; i++) _pages[i] = builders[i]();
        // 更新流程的刷新是广播（DEC-058：设置窗与「更新」窗可能同时挂着），关窗时要摘自己那一份
        _refreshHandler = RefreshUpdateRows;
        _main.AddUpdateUiRefresh(_refreshHandler);
        // ④：llama.cpp 更新把目录切过去之后，这行的显示值要跟上——不然用户随后点「保存」会把旧路径写回去
        _main.LlamaDirAppliedExternally = dir => { if (_boxLlamaDir is not null) _boxLlamaDir.Text = dir; };
        FormClosed += (_, _) =>
        {
            if (_refreshHandler is not null) _main.RemoveUpdateUiRefresh(_refreshHandler);
            _main.LlamaDirAppliedExternally = null;
            // DEC-128①：关窗一律收掉预览 —— 点「保存」的场合此刻磁盘上那份已经是新值（渲染不变），
            // 取消 / ✕ 的场合就回到原来那条串。两条路都走这一个收口。
            _main.ClearStatusBarPreview();
        };
        // 先挂页面，再挂滚动条：滚动条要按页面在内容区里的实际位置算坐标（页面未进容器时 Bounds 还是空的）
        foreach (var p in _pages)
        {
            p.Visible = false;
            _content.Controls.Add(p);
            UiDark.DarkScrollbar(p);          // 原生滚动条被自绘条盖住，本地化主题只为盖不满的边缘
        }
        _bars = new UiScrollbar[_pages.Length];
        for (int i = 0; i < _pages.Length; i++)
            _bars[i] = UiScrollbar.AttachTo(_pages[i], _content, UiTheme.Window);

        // 底部保存栏**不再单独一块底色**（用户：跟其他元素一致就行），只靠上面那条发丝线分界
        var footer = new Panel { Dock = DockStyle.Bottom, Height = UiTheme.S(this, 56), BackColor = UiTheme.Window };
        _footer = footer;
        var btnCancel = Secondary("取消", UiTheme.Window);
        btnCancel.Click += (_, _) => Close();
        var btnSave = new FlatButton { Text = "保存", Backdrop = UiTheme.Window, Width = UiTheme.S(this, 84), Height = UiTheme.S(this, 30) };
        btnSave.Click += (_, _) =>
        {
            // 模块开关（B 段地基，DEC-025）：① 至少启用一个；② 被关闭的模块服务还在跑 → 拦下先停
            if (_main.ModuleSwitchAtSave(_s) is { } moduleErr)
            {
                Dialogs.Message(this, "模块开关", moduleErr, danger: true);
                return;
            }
            // DEC-096：Ninfer 模块内部还有两枚后端开关（至少启用一个 / 关掉那一侧前先停服务），
            // 判据与上面那条同形状，只是这一层在模块里面；「显存互斥处理」那一格只认那两个词（DEC-097②）。
            if (_main.NinferBackendAtSave(_s) is { } backendErr)
            {
                Dialogs.Message(this, "Ninfer 后端", backendErr, danger: true);
                return;
            }
            if (MainForm.GpuMutexProblem(_s) is { } mutexErr)
            {
                Dialogs.Message(this, "显存互斥处理", mutexErr, "填错的值会让「排队等显存」那一档静默失效，所以这里拦下。", danger: true);
                return;
            }
            // DEC-159：那一格上了界面就要当场拦表外值 —— 同一族的账：静默回默认 = 人以为设过了
            if (MainForm.GpuWakeWaitProblem(_s) is { } capErr)
            {
                Dialogs.Message(this, "排队等显存上限(秒)", capErr, "填错的值会让那一档静默回默认 300 秒，所以这里拦下。", danger: true);
                return;
            }
            // DEC-024 A 段：保存前先做全局端口冲突校验；不通过就把窗口留着、说清撞谁。
            // 以前是"存得进去、用的时候静默替换"——冲突被藏起来，最后以请求全被拒的方式爆出来（BUG-023）
            if (_main.PortConflictAtSettingsSave(_s) is { } conflict)
            {
                Dialogs.Message(this, "端口冲突", conflict, "改开后点「保存」。", danger: true);
                return;
            }
            _main.ApplySettingsWindowValues(_s, this);
            Close();
        };
        footer.Controls.Add(btnCancel);
        footer.Controls.Add(btnSave);
        footer.Layout += (_, _) =>
        {
            int top = (footer.Height - btnSave.Height) / 2;
            btnSave.Location = new Point(footer.Width - btnSave.Width - UiTheme.S(this, 16), top);
            btnCancel.Location = new Point(btnSave.Left - btnCancel.Width - UiTheme.S(this, 8), top);
        };

        var topLine = new HairLine { Dock = DockStyle.Top, Height = 1, Backdrop = UiTheme.Window, LineColor = UiTheme.Border };
        Controls.Add(_content);
        Controls.Add(topLine);
        Controls.Add(footer);
        Controls.Add(tabRow);
        ShowPage(0);
        // DEC-093（用户：上一轮改标题栏颜色时漏了设置页）：设置窗跟弹窗族走同一档 ——
        // 标题栏与内容区同底色（#1E1E1E）、**不设 BORDER_COLOR**（那一圈仍由系统画，边界看得见）。
        // DEC-028 当年留系统那档深灰的理由是"标题栏与下面那排页签同色会糊成一块"，这一轮按用户口径不再补分界线。
        // 别再自写 `Apply(this, titleColors: ...)`：口径位只有 DialogsUseCustomCaptionColors / DialogsKeepSystemBorder 两个。
        UiDark.ApplyDialog(this);
    }

    void ShowPage(int index)
    {
        if (index < 0 || index >= _pages.Length) index = 0;
        _segTabs.ActiveIndex = index;         // 与点击同步（程序性赋值不触发 ValueChanged）
        for (int i = 0; i < _pages.Length; i++) _pages[i].Visible = i == index;
        _bars[index].BringToFront();          // 页面是在滚动条之后加进内容区的，切换时要把它重新提到最上层
        _pages[index].PerformLayout();
    }

    // 自检 / 出图台用：切到某一节（出图要拍 Strata 节就得让它真显示出来）
    internal void ShowTabForTest(int index) => ShowPage(index);

    // DEC-058：设置页不再显示更新进度与结果（那些全进了「更新」窗），这里只跟着刷新两行的**本机版本值**
    //（升级完、切目录后值会变；窗没开时 _lbl* 是 null，直接跳过）。
    void RefreshUpdateRows()
    {
        if (IsDisposed) return;
        if (_lblVerLocal is not null)
        {
            var (vSrc, vEng) = _main.VersionsForSettings();
            _lblVerLocal.Text = $"源码 {vSrc} · 引擎 {vEng}";
            _lblVerLocal.Parent?.PerformLayout();
        }
        if (_lblLlamaVer is not null)
        {
            _lblLlamaVer.Text = _main.LlamaVersionForSettings();
            _lblLlamaVer.Parent?.PerformLayout();
        }
        if (_lblNinferVer is not null)
        {
            _lblNinferVer.Text = _main.NinferVersionForSettings();
            _lblNinferVer.Parent?.PerformLayout();
        }
        if (_lblNinferWslVer is not null)
        {
            _lblNinferWslVer.Text = _main.NinferWslVersionForSettings();
            _lblNinferWslVer.Parent?.PerformLayout();
        }
        if (_btnRollback is not null) _btnRollback.Enabled = !_main.UpdateUiBusy;
    }

    // ---------------- 四个 Tab ----------------

    /// 「这个模块的那一节由谁搭」——第 0 轮 DEC-094：这张派发表是它唯一的家。
    /// 加模块时在 ModuleRegistry 与模块表各加一行之后，这里必须跟着补一节：**漏了就当场抛**，
    /// 不许像两模块时代那样 `Key=="llama" ? llama : strata` 把没见过的键悄悄渲染成 Strata 那一节。
    FlowLayoutPanel PageForModule(string key) => key switch
    {
        MainForm.StrataKey => PageStrata(),
        MainForm.LlamaKey => PageLlama(),
        MainForm.NinferKey => PageNinfer(),
        _ => throw new InvalidOperationException("设置页还没有「" + key + "」这一节（加模块要在这里补一节）"),
    };

}
// DEC-129⑤：状态栏那块横排区的承载格 —— 双缓冲面板（拖动期间要在它上面画源格描边与落点定位条，不闪）。
// DEC-130②（他点名"做胶囊/小卡片试试看"）：每格 = 一枚**小卡片** —— 自己画圆角底 + 描边；
// 格子里的名字与复选一律把 Backdrop 换成卡片底色（它们各自会清自己的矩形，不清就会用方角盖掉圆角）。
// `DrawCard=false` 时行为和原来的普通面板一字不差（外层的承载格就拿它当普通面板用）。
internal sealed class StatDropWrap : Panel
{
    public Color CardFill = UiTheme.Card;
    public Color CardLine = UiTheme.Border;
    public int CardRadiusLogical = 6;
    public bool DrawCard;

    public StatDropWrap()
        => SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                  | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

    protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(BackColor);

    protected override void OnPaint(PaintEventArgs e)
    {
        if (!DrawCard) { base.OnPaint(e); return; }
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        int r = Math.Max(1, UiTheme.S(this, CardRadiusLogical));
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);   // 描边往内收 0.5px，1px 线才落在像素上
        using var path = UiTheme.RoundRect(rect, r);
        using (var fill = new SolidBrush(CardFill)) g.FillPath(fill, path);
        using (var pen = new Pen(CardLine, 1f)) g.DrawPath(pen, path);
    }
}
