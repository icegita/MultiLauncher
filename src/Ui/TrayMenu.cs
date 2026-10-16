namespace MultiLauncher;

// 托盘右键菜单（2026-10-03，DEC-041；用户在原型里挑定：**变体 A 组头带状态 + 深色自绘**）。
// 结构：`显示主界面 / 设置` → 分隔线 → 每个**启用**模块一组（组头带状态、**不带端口**；
//       `启动服务 / 停止服务 / 检查更新`，启停按实际状态置灰）→ 分隔线 → `退出`。
// 语义对齐：停用模块整组不出现（与主界面"全隐身"一致）；状态词直接复用左栏「模块状态」的判据
//          （`ModuleLight`），只是把"端口 xxxx"那段挑掉，避免两处各算一套。
// 菜单每次**打开时重建**，所以置灰与状态永远跟实际一致（不用维护"菜单项同步"这种额外状态）。
// 去掉的项：「打开网页」（用户 2026-10-03：不需要）；「打开配置」不放（同上）；「检查更新」按模块各留一个。
public partial class MainForm
{
    ContextMenuStrip? _trayMenu;

    // 置灰项的墨色。2026-10-15（用户：托盘里"不要留蓝灰色"）：原来是 #5C626C —— 蓝通道比红高 16 的冷灰，
    // 在这套纯灰阶自绘界面里偏蓝。换成 `Color.Gray`（#808080，R=G=B 纯中性）：它本来就在项目的颜色语汇里
    //（`LightColor(LightState.Gray)` 那档、状态灯点的初始色），所以**不是新颜色**。
    // 注意：组头用的 `UiTheme.TextMuted`（#9CA3AF）也是冷灰，但它是全项目"弱化文字"的槽位（日志/提示/设置页
    // 都用它），而且 `TrayMenuRowsForTest` 正是拿它来把"组头"与"可点/置灰"分开的判据 —— 不动。
    static readonly Color MenuDisabledText = Color.Gray;

    void BuildTrayMenu()
    {
        _trayMenu = new ContextMenuStrip
        {
            Renderer = new DarkMenuRenderer(),
            // 图标列（image margin）：用户看到别的软件菜单左侧那条色块就是这个——菜单给每项图标/勾选留的竖条。
            // 我们给这条列配了比菜单底更深一档的色，条与内容自然分成两栏，也免去用空格手动缩进（2026-10-03 DEC-045）。
            ShowImageMargin = true,
            Font = _fUi,
            BackColor = UiTheme.Card,
            ForeColor = UiTheme.Text,
        };
        // 不用 NotifyIcon 内置的右键弹出：内置路径在**第一次**右键时才建菜单句柄，而 Opening 里重建（Items.Clear）
        // 会把这次弹出吞掉——用户实测"第一次点不出来、第二次正常"。改成自己在 MouseUp 里填好内容再 Show。
        _tray.ContextMenuStrip = null;
        _tray.MouseUp += (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            if (!Enabled) return;        // 模态（设置窗等）开着时不给弹：与内置行为一致，也免得在未保存状态上叠操作
            FillTrayMenu(_trayMenu!);
            ShowTrayMenu();
        };
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hWnd);

    // 纯函数：光标在下半屏就往上弹（托盘在屏幕底部的常规情形），否则往下（托盘被拖到上半屏时）
    internal static ToolStripDropDownDirection TrayDropDirection(int cursorY, Rectangle workArea) =>
        cursorY > workArea.Top + workArea.Height / 2
            ? ToolStripDropDownDirection.AboveLeft
            : ToolStripDropDownDirection.BelowLeft;

    // 弹出位置（2026-10-03 实跑反馈：`Show(光标点)` 是"菜单左上角贴光标"，托盘在屏幕右下角时会被屏幕边缘
    // 顶回去、看着离点击处很远）。按方向弹：光标在下半屏 → 菜单**自点击点向上**展开（AboveLeft，和系统托盘
    // 菜单一致）；上半屏 → 向下展开。点的坐标仍是光标处，菜单从那里长出来。
    void ShowTrayMenu()
    {
        var m = _trayMenu!;
        var pt = Cursor.Position;
        try { SetForegroundWindow(m.Handle); } catch { /* 拿不到前台也不致命 */ }   // 点别处能收起、键盘可用
        m.Show(pt, TrayDropDirection(pt.Y, Screen.FromPoint(pt).WorkingArea));
    }

    void FillTrayMenu(ContextMenuStrip menu)
    {
        menu.Items.Clear();
        menu.Items.Add(MenuItem("显示主界面", (_, _) => ShowFromTray()));
        menu.Items.Add(MenuItem("设置", (_, _) => OpenSettingsFromTray()));
        menu.Items.Add(new ToolStripSeparator());
        foreach (var m in ModuleRegistry.All.Where(m => m.Enabled(_config.Current)))
        {
            var (state, desc) = ModuleLight(m.Key);
            menu.Items.Add(MenuGroup(m.DisplayName + " · " + LightWords(desc)));
            bool running = state != LightState.Gray;      // 灰 = 没在跑；绿/橙/红都算"有东西可以停"
            string key = m.Key;
            menu.Items.Add(MenuItem("启动服务", (_, _) => StartModule(key), !running));
            menu.Items.Add(MenuItem("停止服务", (_, _) => StopModule(key), running));
            // DEC-107：Ninfer 是"一后端一窗" → 这一组多一枚（「检查更新 · WIN」「检查更新 · WSL」）；
            // 另两模块 UpdateSidesFor 只给一枚 = 菜单文字与上一版一字不变。
            foreach (var side in UpdateSidesFor(m.Key))
                menu.Items.Add(MenuItem(UpdateMenuText(key, side), (_, _) => UpdateModule(key, side), UpdateMenuEnabled(key)));
        }
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(MenuItem("退出", (_, _) => ExitApp()));
    }

    // 第 0 轮 DEC-094：起停/更新都转给模块表那一行（以前是 if (key == "strata") 两支）
    void StartModule(string key) => View(key).StartService();
    void StopModule(string key) => View(key).StopService();

    // DEC-058：托盘的「检查更新」不再后台悄悄查（那样主窗藏着时完全没有落点），改成**打开该模块的更新窗并立刻查一次**。
    // 升级进行中这条读「升级中…」并置灰——窗里那枚小 X 才是取消的地方，别在菜单上再开一个口。
    // DEC-107：Ninfer 一后端一窗 → 它这一组列两枚（WIN / WSL），各开各的窗。
    internal static string[] UpdateSidesFor(string key) => key == MainForm.NinferKey ? new[] { "win", "wsl" } : new[] { "win" };

    void UpdateModule(string key, string side) => OpenUpdateWindow(key, null, side);
    string UpdateMenuText(string key, string side) => UpdateMenuLabel(key, side);
    internal bool UpdateMenuEnabled(string key) => !View(key).UpdateBusy();

    // 托盘「设置」：只弹设置页、**不显示主窗**（用户 2026-10-03 要的行为）。
    // 主窗可能藏着甚至从没显示过，而设置窗默认 CenterParent —— 会跟着看不见的父窗跑位，所以这一路显式改屏幕居中。
    void OpenSettingsFromTray()
    {
        using var dlg = new SettingsForm(this, _config.Current) { StartPosition = FormStartPosition.CenterScreen };
        dlg.ShowDialog(this);
    }

    static ToolStripMenuItem MenuItem(string text, EventHandler onClick, bool enabled = true)
    {
        var it = new ToolStripMenuItem(text)
        {
            Enabled = enabled,
            BackColor = Color.Transparent,
            ForeColor = enabled ? UiTheme.Text : MenuDisabledText,
        };
        it.Click += onClick;
        return it;
    }

    // 组头：不可点的说明行（变体 A 的"带状态"就体现在这里的文字上）
    static ToolStripMenuItem MenuGroup(string text) => new(text)
    {
        Enabled = false,
        BackColor = Color.Transparent,
        ForeColor = UiTheme.TextMuted,
    };

    // 自检取态用：返回每一行 "文字/角色"（组头、可点、置灰），断言结构与置灰规则而不是断言像素
    internal bool TrayShowImageMarginForTest => _trayMenu?.ShowImageMargin ?? false;
    // DEC-061 自检用：托盘**不该**再挂 NotifyIcon 内置的 ContextMenuStrip（挂了就会和自己在 MouseUp 里
    // 按方向弹出那条路重复弹，并且把 DEC-045 治的「首次右键被 Items.Clear 吞掉」重新引进来）
    internal bool TrayUsesBuiltinMenuForTest => _tray.ContextMenuStrip is not null;

    internal List<string> TrayMenuRowsForTest()
    {
        using var menu = new ContextMenuStrip();
        FillTrayMenu(menu);
        return menu.Items.Cast<ToolStripItem>().Select(i => i is ToolStripSeparator
                ? "──"
                : i.Text!.Trim() + (i.ForeColor == UiTheme.TextMuted ? "/组头" : i.Enabled ? "/可点" : "/置灰"))
            .ToList();
    }
}

// 深色菜单渲染：系统原生 ContextMenuStrip 是浅色专业主题，贴在这套深色自绘界面里很突兀
// （用户挑了"深色"）。做法 = 换色表把渐变全压平成卡片色 + 悬停色，文字颜色由各项自己的 ForeColor 决定。
sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    public DarkMenuRenderer() : base(new DarkMenuColors()) { RoundedEdges = true; }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        // 禁用箭头的墨色同 MenuDisabledText（2026-10-15：原来 #454A52 也是冷灰）。启用那一档照旧用
        // `UiTheme.TextMuted`（同组头那一档，见上面那段的说明）。
        e.ArrowColor = e.Item?.Enabled ?? false ? UiTheme.TextMuted : Color.Gray;
        base.OnRenderArrow(e);
    }
}

sealed class DarkMenuColors : ProfessionalColorTable
{
    static readonly Color Bg = UiTheme.Card;                              // #232323
    static readonly Color Rail = Color.FromArgb(0x1B, 0x1B, 0x1B);        // 图标列底色（比菜单底深一档）
    // 悬停/选中底色。2026-10-15（用户实跑点名）：原来是 #2F4A7A（深蓝），贴在这套灰阶深色界面里那一条蓝最扎眼，
    // 他要求换回项目现有配色 —— 取 `UiTheme.Border`（#333333），也就是项目里"悬停一行"的既有口径
    //（设置页行悬停 `SettingsForm.Rows.cs`、次要按钮悬停 `MainForm.Ui.cs` 用的都是它），**不新增颜色**。
    static readonly Color Hi = UiTheme.Border;
    static readonly Color Line = Color.FromArgb(0x3A, 0x3A, 0x3A);        // 边框与分隔线

    public override Color MenuBorder => Line;
    public override Color MenuItemBorder => Hi;
    public override Color MenuItemSelected => Hi;
    public override Color MenuItemSelectedGradientBegin => Hi;
    public override Color MenuItemSelectedGradientEnd => Hi;
    public override Color MenuItemPressedGradientBegin => Bg;
    public override Color MenuItemPressedGradientMiddle => Bg;
    public override Color MenuItemPressedGradientEnd => Bg;
    public override Color MenuStripGradientBegin => Bg;
    public override Color MenuStripGradientEnd => Bg;
    public override Color ToolStripDropDownBackground => Bg;
    public override Color ImageMarginGradientBegin => Rail;
    public override Color ImageMarginGradientMiddle => Rail;
    public override Color ImageMarginGradientEnd => Rail;
    public override Color ImageMarginRevealedGradientBegin => Rail;
    public override Color ImageMarginRevealedGradientMiddle => Rail;
    public override Color ImageMarginRevealedGradientEnd => Rail;
    public override Color SeparatorDark => Line;
    public override Color SeparatorLight => Line;
    public override Color StatusStripGradientBegin => Bg;
    public override Color StatusStripGradientEnd => Bg;
}
