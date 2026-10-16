namespace MultiLauncher;

// 主界面装配（新布局：左栏 + 右侧三卡片 + 底部大日志）。控件字段定义在 Program.cs。
// 布局纪律：
//  1) 全手工定位：窗体 AutoScaleMode.None，尺寸一律走 UiTheme.S()；两套缩放叠加会让控件在 125%/150% 下错位（实测过）；
//  2) 卡片与左栏都是圆角面板，圆角外露的部分靠 Backdrop 填父容器底色，所以每个自绘控件的 Backdrop 必须设对；
//  3) Dock 顺序：Fill 的先加、Top 的后加（WinForms 按 z 序逆序停靠），否则日志区会被顶掉；
//  4) 卡1 = 监听地址｜监听端口｜思考深度｜保存、卡2 = 模型名｜上下文 ／ KV 精度｜附加参数（DEC-076 定稿；
//     宽度按运行时实测给，别靠肉眼——见 GridFitForTest）；页头 = 分段 + 打开配置 / 设置。
public partial class MainForm
{
    // 字号（点）：与原型一致，UI 12px≈9pt、8.5pt 为次要文字
    readonly Font _fUi = new("Microsoft YaHei UI", 9f);
    readonly Font _fSmall = new("Microsoft YaHei UI", 8.5f);
    readonly Font _fTitle = new("Microsoft YaHei UI", 12f);
    readonly Font _fMono = new("Consolas", 9f);

    /// <summary>左栏内宽（扣掉 CardPad 之后）：DEC-107 他拍板 178 → 200（"+10% 左右、就近取整"——
    /// 按他给的取整法：178×1.1≈195.8 → 就近取 200；196 实测差 1px：「运行中 · 模型已加载 · 端口 18082」
    /// 在自检基准下要 181、可用只有 180，取 200 后可用 184、九句全放得下）。
    /// 单一常量 —— 侧栏总宽、灯行宽、「API 地址」框宽、灯行放不下的量尺全从它算，别再各处写 S(178)。</summary>
    internal const int SideInnerLogical = 200;

    int RowH => UiTheme.S(this, 30);
    int RowGap => UiTheme.S(this, 12);
    int CardPad => UiTheme.S(this, 16);

    Label Lbl(string text, Font f, Color color, Color backdrop)
        => new() { Text = text, AutoSize = true, ForeColor = color, BackColor = backdrop, Font = f };

    RoundedTextBox Box(string text, int width, Color backdrop, bool readOnly = false, Font? font = null)
    {
        var b = new RoundedTextBox { Backdrop = backdrop, Width = width, Height = RowH };
        b.Inner.Font = font ?? _fMono;
        b.Inner.ReadOnly = readOnly;
        b.Text = text;
        return b;
    }

    FlatButton SecondaryBtn(string text) => new()
    {
        Text = text, Backdrop = UiTheme.Card, FillColor = UiTheme.Input, HoverColor = UiTheme.Border,
        TextColor = UiTheme.Text, Font = _fUi, Height = RowH,
    };

    FlatButton PrimaryBtn(string text) => new() { Text = text, Backdrop = UiTheme.Card, Font = _fUi, Height = RowH };

    FlatButton DangerBtn(string text) => new()
    {
        Text = text, Backdrop = UiTheme.Card, FillColor = UiTheme.Danger, HoverColor = UiTheme.DangerHover,
        TextColor = Color.White, Font = _fUi, Height = RowH,
    };

    // 按钮宽度按文字量出来 + 左右内边距；输入框宽度按 DEC-011 的"刚好放下"给的固定值
    int BtnW(string text) => TextRenderer.MeasureText(text, _fUi).Width + UiTheme.S(this, 20);

    // 顶到底排列的一列（子控件宽度跟着列宽走）。
    // 间距靠子控件的 Margin.Bottom 实现（FlowLayoutPanel 没有 gap 属性）—— 加进来时统一给 gap，
    // 需要特殊间距的地方在 Add 之后自己改 Margin。
    static FlowLayoutPanel Column(int gap)
    {
        var p = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = false,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = Color.Transparent,
        };
        p.ControlAdded += (_, e) => { if (e.Control is not null) e.Control.Margin = new Padding(0, 0, 0, gap); };
        return p;
    }

    static void FillWidth(FlowLayoutPanel column)
    {
        void Sync()
        {
            foreach (Control c in column.Controls) c.Width = Math.Max(1, column.ClientSize.Width - c.Margin.Horizontal);
        }
        column.ClientSizeChanged += (_, _) => Sync();
        Sync();
    }

    // 左栏「运行状态」的一行：左键右值，值写进 _kv；行面板登记进 _kvPanels（B 段：行集随激活模块显隐）。
    // 「状态」行是特例（2026-10-04）：它接的是原「运行状态」标题行右侧那个状态词——字体 8.5pt、颜色随状态
    // （绿=运行中 / 蓝=网关在听、模型未加载 / 灰=已停止 …；DEC-082 起状态词统一「待机 · 模型未加载」，
    //  蓝那一档只是颜色，不再是"网关待唤醒"那句话），比别的值行宽一点（"● 运行中（已有服务）"这种要放得下）。
    // DEC-107：「引擎」「版本」两行末尾挂一枚小更新按钮（圆环上箭头）—— 只从值列里让出宽度，
    // 行高/行宽/列宽都不变（他第 6 条：不得撑高/宽、原本的行/列）。
    readonly Dictionary<string, Panel> _kvPanels = new();
    internal readonly Dictionary<string, TinyUpdate> _kvUpdateBtns = new();
    Panel KvRow(string key, Color backdrop)
    {
        bool isState = key == "状态";
        var row = new Panel { Height = UiTheme.S(this, 22), Margin = new Padding(0), BackColor = backdrop };
        var k = Lbl(key, _fSmall, UiTheme.TextMuted, backdrop);
        k.Location = new Point(0, UiTheme.S(this, 3));
        var v = Lbl("-", isState ? _fSmall : _fMono, UiTheme.Text, backdrop);
        v.AutoSize = false;
        v.TextAlign = ContentAlignment.MiddleRight;
        v.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        int vw = UiTheme.S(this, isState ? 132 : 120);
        int vRightPad = 0;                 // 有按钮的行：值列再让出按钮那一截，右缘不对齐按钮（否则"读"会被压在按钮下面）
        if (key == "版本")      // 2026-10-15：原来「引擎」「版本」两行各一枚，合并后只剩这一行
        {
            // DEC-108（他批"太大、缩小 40%、记得居中"）：S(20) → S(12)，行里垂直居中、右缘照旧贴边
            var upd = new TinyUpdate { Backdrop = backdrop, Size = new Size(UiTheme.S(this, 12), UiTheme.S(this, 12)) };
            upd.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            upd.SetBounds(row.Width - upd.Width, (row.Height - upd.Height) / 2, upd.Width, upd.Height);
            upd.Click += (_, _) => OpenUpdateWindow(_activeModule, this, UpdateSideFor(_activeModule));
            vw -= UiTheme.S(this, 18);        // 值列让宽给按钮（整行不变宽）；版本串 ~14 字符仍放得下
            vRightPad = UiTheme.S(this, 18);  // 值的右缘再往左收一个按钮位 + 间距
            row.Controls.Add(upd);
            _kvUpdateBtns[key] = upd;
        }
        v.SetBounds(row.Width - vw - vRightPad, UiTheme.S(this, 1), vw, UiTheme.S(this, 20));
        row.Controls.Add(k);
        row.Controls.Add(v);
        _kv[key] = v;
        _kvPanels[key] = row;
        return row;
    }

    // 出图用（--uiprobe sidebar）：把版本行那枚小更新按钮的悬停态画出来（静态截图看不出 hover 反馈）
    internal void SetUpdateBtnHoverForTest(bool on)
    {
        foreach (var b in _kvUpdateBtns.Values) { b.HoverForTest = on; b.Invalidate(); }
    }

    /// <summary>两枚小按钮的颜色随"当前模块探没探到新版本"走（DEC-108）：灰 = 没探到；品牌绿 = 探到过
    /// （或被跳过那一版在）。两个挂点：切页/状态刷新（RefreshRunStateView）与更新流程各广播点（PokeUpdateUi）——
    /// 于是"开过更新窗、查到了新版"这之后左栏那枚会自己转绿，不用重开界面。</summary>
    void RefreshSidebarUpdateButtons()
    {
        if (IsDisposed || _kvUpdateBtns.Count == 0) return;
        if (InvokeRequired) { try { BeginInvoke(new Action(RefreshSidebarUpdateButtons)); } catch { } return; }
        bool green = View(_activeModule).Offer().Show;
        foreach (var b in _kvUpdateBtns.Values)
            if (b.NewVersion != green) { b.NewVersion = green; b.Invalidate(); }
    }
    internal void RefreshSidebarButtonsForTest() => RefreshSidebarUpdateButtons();

    void BuildUi()
    {
        BackColor = UiTheme.Window;
        // 与外层留白（原型 .body 的 padding: 16px）：窗体内缩 16px，左栏与主区之间再由 main 的 Padding 给 16px 间距
        Padding = new Padding(UiTheme.S(this, 16));

        // ---- 左栏（B 段：纯上下文——启停 / 状态键值行 / 模式条 / API 地址，随激活模块；底部「模块状态」恒在）----
        // DEC-107：内宽 SideInnerLogical（178→196，他拍板 +10%）——总宽 = 内宽 + 两侧 CardPad
        var side = new RoundedPanel
        {
            Dock = DockStyle.Left,
            Width = UiTheme.S(this, SideInnerLogical + 16 * 2),
            FillColor = UiTheme.Card,
            BorderColor = UiTheme.Border,
            Backdrop = UiTheme.Window,
            Padding = new Padding(CardPad),
        };
        var sideCol = Column(UiTheme.S(this, 12));
        side.Controls.Add(sideCol);

        var titleBlock = new Panel { Height = UiTheme.S(this, 26), BackColor = Color.Transparent, Margin = new Padding(0) };
        // DEC-146（甲档，他 2026-10-11 拍）：这一格改口「运行信息」。原句「控制台」是本程序自己的名字、不是这一栏内容的名字
        // （模块名长在页头那排分段控件上 = DEC-076 那句"不再单列模块标题——分段即标题"），坐在这里既无指向又是整窗唯一一枚
        // 12pt，于是"突兀"。几何一个数不动：字号（_fTitle 12pt）、那一档的高度 S(26) 与下间距 S(12) 全照旧 ——
        // 左栏那一列是 TopDown + Dock=Fill（`Column()`），摘掉标题会让整摞往上窜 S(38)，多出来的空掉进中段，
        // 他点名的"重心太靠上"就是这么来的。降字号（甲′）与挪到按钮下面（乙）两档他都不要，_fTitle 因此留着。
        var t1 = Lbl("运行信息", _fTitle, UiTheme.Text, UiTheme.Card);
        t1.Location = new Point(0, 0);
        titleBlock.Controls.Add(t1);
        sideCol.Controls.Add(titleBlock);

        // 「打开网页」挪到参数卡右侧按钮列（「高级」上方）；「更新」挪进各模块设置页的维护组（B 段，DEC-025）——
        // 这两个入口现在是设置页里动态建的行按钮，点击接线在各自的调用处。
        _btnOpenWeb.Click += (_, _) => OpenWeb();
        _btnStartStop.Click += (_, _) => ToggleService();
        _btnStartStop.Backdrop = UiTheme.Card;
        _btnStartStop.BorderColor = UiTheme.Border;
        _btnStartStop.Height = UiTheme.S(this, 36);
        sideCol.Controls.Add(_btnStartStop);
        _btnStartStop.Margin = new Padding(0, UiTheme.S(this, 6), 0, UiTheme.S(this, 12));
        Tip.Set(_btnStartStop, "只管服务本身：Strata 页 = 路由模式立/关网关、单体模式起/停服务；llama.cpp 页 = 启/停 llama-server");

        // 「运行状态」标题行已合并（2026-10-04 用户口径）：它右侧的状态词与下方「状态」键值行本来就是同一串字
        // （RefreshLamp 一处刷新、两处显示），去掉标题行；字体与状态色搬到「状态」行的值上（见 KvRow 特例）。
        var sep = new HairLine { LineColor = UiTheme.Border, Backdrop = UiTheme.Card };
        sideCol.Controls.Add(sep);
        sep.Margin = new Padding(0, 0, 0, UiTheme.S(this, 4));
        // 三页同一套行（2026-10-15 合并）：strata 原来单列「源码」「引擎」两行 → 与另两页统一成一行「版本」。
        // 行集全等之后切页零重排（左栏模式条不再跳一行），显隐那一问一答仍走 RefreshRunStateView。
        foreach (var key in new[] { "状态", "PID", "地址", "端口", "版本" })
        {
            var row = KvRow(key, UiTheme.Card);
            sideCol.Controls.Add(row);
            row.Margin = new Padding(0);          // 键值行紧挨着，不要列间距
        }

        // 模式条 = 可点的切换按钮（文字就是当前模式；B 段：按模块各自记忆，显示激活模块的模式）。
        // DEC-090 落地：形状仍通栏，但用与卡1 后端方块**同一个** StackedToggle（当前枚 + 正下方露一条边 + 同一族动画）。
        // 侧栏总高净增 0：露边那几像素从原来的上边距 S(8) 里挤（→ S(3)），往下长的部分由露边占用。
        _btnMode.Backdrop = UiTheme.Card;
        _btnMode.Faces = new[] { "路由模式", "单体模型" };
        _btnMode.Height = UiTheme.S(this, StackedToggle.BandFaceLogicalH + StackedToggle.PeekLogical);
        _btnMode.SwapRequested += ToggleStartMode;
        sideCol.Controls.Add(_btnMode);
        _btnMode.Margin = new Padding(0, UiTheme.S(this, 3), 0, 0);
        Tip.Set(_btnMode, "点一下切换激活模块的启动模式（两个模块各自记忆）。路由模式：Strata 侧点「启动」只立网关，/v1 请求到了才按 model 名加载；llama 侧只起服务、模型按需加载。单体模型：启动直接连模型一起起");

        var apiBlock = new Panel { Height = UiTheme.S(this, 62), BackColor = Color.Transparent };
        var apiTitle = Lbl("API 地址", _fSmall, UiTheme.TextMuted, UiTheme.Card);
        apiTitle.Location = new Point(0, 0);
        _txtApi.Backdrop = UiTheme.Card;
        _txtApi.Height = UiTheme.S(this, 28);
        _txtApi.Top = UiTheme.S(this, 22);
        _txtApi.Left = 0;
        _txtApi.Width = UiTheme.S(this, SideInnerLogical);
        // DEC-107：框随左栏加宽，字体 7.5 → 8pt（他：加宽后"如果可以增大顺便也增大一点,保持能完整显示即可"）。
        // 实测账（自检基准，内层宽 184）：四位口 `http://192.168.1.9:8068/v1` 8pt=156 ✓ 富余 28；
        // 五位口最坏 `…:18082/v1` 8pt=162 ✓ 富余 22；再上一档 8.5pt 是对 189 —— 五位口放不下，所以取 8pt。
        // 判据在 GridFitForTest 的 api 段（截图口径 = 量出来的）。
        _txtApi.Inner.Font = new Font("Consolas", 8f);
        apiBlock.Controls.Add(apiTitle);
        apiBlock.Controls.Add(_txtApi);
        sideCol.Controls.Add(apiBlock);
        apiBlock.Margin = new Padding(0, UiTheme.S(this, 12), 0, 0);

        // 底部「模块状态」（B 段）：点 + 名 + 说明文字（·工作端口）；只展示、不可点；不随切页变。
        // 行集由 EnsureLightRows 按启用模块维护（全隐身：停用模块整行消失）。
        // 2026-10-15（他点名加折叠）：标题右侧一枚箭头按钮 —— 折叠态只留一行横排「灯 + 简称」，悬浮看完整状态；
        // 状态记在 `Settings.LightsCollapsed`（他明确"记住上次"）。箭头方向口径同卡2「高级」（展开 ▾ / 折叠 ◂）。
        var lightTitle = Lbl("模块状态", _fUi, UiTheme.Text, UiTheme.Card);
        lightTitle.Location = new Point(0, UiTheme.S(this, 2));
        _btnLightToggle.Backdrop = UiTheme.Card;
        // DEC-177（他实跑点名）：这枚箭头**不打底、不描边**，只留图形 —— 命中区仍是整块控件（他说"点击范围可以保留"）。
        // 高 S(20) → S(16)、顶 S(1) → S(2)：出图台实测（150%）标题「模块状态」墨迹中心 1141、箭头墨迹中心 1139.5，
        // 收高再下移一格后两者齐平（原来 S(20) 那一版，箭头比标题低 3 物理像素 = 看着压着标题下沿）。
        _btnLightToggle.Bare = true;
        // DEC-179：这枚箭头与卡2「高级」那枚**同源** —— 同一个字形（▾ / ◂）、同一支字体（`_fSmall` 8.5pt，
        // 就是 `_btnAdv` 那支）、同一个字色（UiTheme.Text），差别只在这一枚不打底不描边（`Bare`，他点名要的）。
        // 上一轮为"增大 100%"改过两版都被否，账留在 DEC-178 / DEC-179：字号翻到 17pt → 箭头心比标题低 9 物理像素
        //（▾ 坐在基线上，越大掉得越低）；改画矢量三角 S(9)×S(8) → 判"太大"，收一半 S(5)×S(4) → 又与「高级」不同形不同号。
        _btnLightToggle.Font = _fSmall;
        _btnLightToggle.TextColor = UiTheme.Text;         // 与「高级」那枚同字色；悬停不换色（他点名"不需要提绿，保持原版色"）
        _btnLightToggle.SetBounds(UiTheme.S(this, SideInnerLogical - 26), UiTheme.S(this, 2),
                                  UiTheme.S(this, 26), UiTheme.S(this, 16));
        _btnLightToggle.Click += (_, _) => ToggleLights();
        var lightSep = new HairLine { LineColor = UiTheme.Border, Backdrop = UiTheme.Card };
        lightSep.SetBounds(0, UiTheme.S(this, 26), UiTheme.S(this, SideInnerLogical), 1);
        _lightBlock.Dock = DockStyle.Bottom;
        _lightBlock.Controls.Add(lightTitle);
        _lightBlock.Controls.Add(_btnLightToggle);
        _lightBlock.Controls.Add(lightSep);
        _lightStrip.Visible = false;      // 展开态是默认；真正的摆位由 ApplyLightsCollapsed 按 ini 里的值做
        _lightBlock.Controls.Add(_lightStrip);
        side.Controls.Add(_lightBlock);
        EnsureLightRows();
        FillWidth(sideCol);

        // ---- 主区 ----
        var main = new Panel { Dock = DockStyle.Fill, Padding = new Padding(UiTheme.S(this, 16), 0, 0, 0), BackColor = UiTheme.Window };

        // 页头（B 段）：模块切换 = 分段控件（左）+ 打开配置 / 设置（右）；不再单列模块标题——分段即标题。
        // 2026-10-04 DEC-076 候选（原型）：卡1 回归后「保存」搬回卡1（用户口径"保存按钮还是放在那里"），
        // 页头回到两枚按钮。置灰逻辑不动（RefreshSaveButton）。
        var pageHead = new Panel { Dock = DockStyle.Top, Height = UiTheme.S(this, 52), BackColor = UiTheme.Window };
        _pageHead = pageHead;
        _segModules.Backdrop = UiTheme.Window;
        // 2026-10-02 反馈：_segModules 是字段初始化，构造时 DPI 还没捕获（按 96 算只有 32 物理像素、
        // 比旁边按钮矮一截）——这里按真实 DPI 重算高度（胶囊 = 按钮高，见 SegmentedControl.ApplyDpiHeight）
        _segModules.ApplyDpiHeight();
        // 第 0 轮那张表的口径：**页签项也从模块表来**（原来这里写死两枚，加第三个模块就会少一页）
        _segModules.SetItems(ModuleRegistry.All.Select(m => m.DisplayName).ToArray());
        pageHead.Controls.Add(_segModules);
        foreach (var b in new[] { _btnOpenConfig, _btnSettings })
        {
            b.Backdrop = UiTheme.Window;
            b.FillColor = UiTheme.Input;
            b.HoverColor = UiTheme.Border;
            b.BorderColor = UiTheme.Border;
            b.TextColor = UiTheme.Text;
            b.Font = _fSmall;
            b.Height = RowH;
        }
        _btnOpenConfig.Click += (_, _) => OpenActiveConfig();
        _btnSettings.Click += (_, _) => OpenSettings();
        Tip.Set(_btnOpenConfig, "打开引擎实际读的那一份（点「启动」用的同一条生成逻辑，按当前界面参数重新生成后再打开）：" +
                                 "Strata = " + DisplayDataPath(RealRunConfigDir()) + "\\<tag>.json（跟随选中的模型）；" +
                                 "llama = " + DisplayDataPath(DefaultRouterIniPath()) + "（界面改的就是它、引擎读的也是它，不重新生成）。" +
                                 "Strata 那份是产物、下次启动会重写，要长期改就动源文件（日志里会给路径）");
        Tip.Set(_btnSettings, "打开设置（通用 / 每个启用模块各一节 / 关于）");
        pageHead.Controls.Add(_btnOpenConfig);
        pageHead.Controls.Add(_btnSettings);
        pageHead.Layout += (_, _) =>
        {
            // 「设置」= 矢量齿轮 + 文字，宽度另算（齿轮那块字体量不出来）
            _btnSettings.Width = BtnW85("设置") + _btnSettings.GearBlockWidth;
            _btnOpenConfig.Width = BtnW85("打开配置");
            int top = UiTheme.S(this, 12);
            _btnSettings.Location = new Point(pageHead.Width - _btnSettings.Width - UiTheme.S(this, 2), top);
            _btnOpenConfig.Location = new Point(_btnSettings.Left - _btnOpenConfig.Width - UiTheme.S(this, 8), top);
            // 分段控件比按钮高（含胶囊内缩）：与右侧按钮**按中线对齐**（原来固定 +S(2)，重心偏下）
            _segModules.Location = new Point(UiTheme.S(this, 2), top + (RowH - _segModules.Height) / 2);
        };

        // Dock 顺序：Fill 先加，Top 后加；后加的 Top 更靠上，所以「间隔」要插在它后面的卡片之前
        // —— 原型 .main { gap: 12px }，卡片之间留 12px（之前是 0，三张卡贴成一块）
        Control Gap() => new Panel { Dock = DockStyle.Top, Height = UiTheme.S(this, 12), BackColor = UiTheme.Window };
        _cardModel = CardModel();
        _cardParams = CardParams();
        _cardStart = CardStart();       // DEC-076 候选：卡1 回归（监听地址｜监听端口｜思考深度｜保存）
        _card1Row = Card1Row(_cardStart);   // DEC-182 甲：卡1 与后端方块左右相邻，两圈描边不重叠
        _cardLog = _logCard();
        // 从上到下：pageHead / card1 / card2 / card3 / 提示行 / 日志区。
        // 提示行没有内容时整体隐藏不占位，「模型卡 → 日志卡」因此始终只隔一个 12px。
        main.Controls.Add(_cardLog);
        main.Controls.Add(Gap());
        main.Controls.Add(_hintRow());
        main.Controls.Add(_cardModel);
        main.Controls.Add(Gap());
        main.Controls.Add(_cardParams);
        main.Controls.Add(Gap());
        main.Controls.Add(_card1Row);   // DEC-182：主区里挂的是**那一行**（方块卡 + 卡1），不是卡1 本身
        main.Controls.Add(Gap());
        main.Controls.Add(pageHead);
        _sideCol = sideCol;

        Controls.Add(main);
        Controls.Add(side);
        // ④ 底部状态栏（DEC-038）：最后加 = 最先停靠 = 占住整条底边，main(Fill) 自动让出高度
        SetupStatusBar();

        WireEvents();
    }

    // 卡片3 下方的提示行（已下载但未配置的模型等）。没有内容时**整行隐藏**（Dock 布局会跳过不可见控件，
    // 不占高度），保证「模型卡 → 日志卡」的间距任何时候都与其他卡片一致（用户反馈：中间空隙太大）
    Panel? _cardHint;
    Control _hintRow()
    {
        var p = new Panel { Dock = DockStyle.Top, Height = UiTheme.S(this, 24), BackColor = UiTheme.Window, Visible = false };
        _lblHint.BackColor = UiTheme.Window;
        _lblHint.Location = new Point(UiTheme.S(this, 2), UiTheme.S(this, 4));
        p.Controls.Add(_lblHint);
        _cardHint = p;
        return p;
    }

    // 底部大日志：占剩余全部高度
    Control _logCard()
    {
        var card = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            FillColor = UiTheme.Card,
            BorderColor = UiTheme.Border,
            Backdrop = UiTheme.Window,
            Padding = new Padding(CardPad),
            Margin = new Padding(0),
        };
        _txtLog.BackColor = UiTheme.Card;
        _txtLog.ForeColor = UiTheme.Text;                        // 原来用 #9CA3AF，太灰、读日志费劲
        // 字体与其它区域统一用雅黑：Consolas 没有 CJK 字形，中文会回退成另一套字形，混排看起来不一致
        _txtLog.Font = new Font("Microsoft YaHei UI", 9f);
        _txtLog.BorderStyle = BorderStyle.None;
        _txtLog.Dock = DockStyle.Fill;
        UiDark.DarkScrollbar(_txtLog);      // 原生滚动条仍变暗：自绘条盖不满的那一两像素也不能发白
        card.Controls.Add(_txtLog);
        SetupLogScrollbar(card);            // 盖住原生滚动条的自绘细滑条（见 MainForm.LogScroll.cs）
        return card;
    }
}