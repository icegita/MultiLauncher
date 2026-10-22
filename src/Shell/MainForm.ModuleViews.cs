namespace MultiLauncher;

// ============================================================================
// 第 0 轮 · N 模块表驱动（DEC-094）
// ============================================================================
// **本文件不按 300/500 行门槛再拆（2026-10-15 拆分轮的决定，理由是约定本身）**：
// conventions 的「模块呈现唯一家」这一条要的就是"加一个模块 = 在这里读一处表、补一节实现"，
// 表本体（三行模块能力）与它的成对实现（动作/视图）**必须同处一文件**才守得住"一处说全"这条性质；
// 拆成两个文件之后，加模块要跑两个文件才能补完，而自检里那 8 条"按表穷举"的用例正是钉这条性质的。
// 其余超长文件（MainForm.Ninfer / Program / SettingsForm 等）已按职责拆开，本文件是唯一被辩护保留的。
// 两模块时代界面上到处是 `ActiveIsStrata ? 甲 : 乙`（实测 37 处）——加第三个模块时每一处都得再摊一遍，
// 而且"哪个模块有哪些格子/哪些状态"没有一处能说全。这一张表把这些二元判断收成一个家：
//
//   · 一个模块 = ModuleRegistry.All 里的一行身份（Key / 显示名 / 启用位）+ 本表里的一行能力（ModuleView）
//   · 界面只问 ActiveView 要东西：要格子给格子、要状态文案给状态文案、要动作就转发动作
//   · 视图**不存状态**：它只是 MainForm 上该模块那些字段的取数口（进程、模型表、失败位仍在原处，
//     搬家会把已验证的起停链路整个掀一遍，第 0 轮的验收口径是"两页实跑无差异"，不动它们）
//   · 视图**不算判据**：灯态 / 状态文案 / 按钮文字这些早有纯函数（StrataLight、StartStopTextFor、
//     ModelActionTextFor…），视图只负责把本模块的实时值喂给它们，不许另起一套算法
//
// 加一个模块（Ninfer 就是照这个形状接）：ModuleRegistry 加一行 + 这里加一个 BuildNinferView()，
// 再把它的动作实现写成 Strata*/Llama* 那样的成对方法。**别再新增 `ActiveIsStrata`**（那个属性已删）。
//
// 表里每个成员都属于哪一格，见成员声明处的注释；自检的界面排布用例（PortSlot/ReasoningSlot/adv-backend/
// GridFit/DropWidths）都从这张表取值，所以"界面没变"是可断言的，不是肉眼看的。

/// <summary>更新窗里 WSL 那扇的所有事（DEC-097③ 起是"第二段"，DEC-107 起是**独立的一扇窗**）。
/// 摆位仍守弹窗族那条硬口径：一行、值落在值列、按钮恒最右一枚。
/// <c>OnWindowOpened</c> = 开窗时顺手做一次该做的读取（Ninfer 的 WSL 版本 = 只在启用那一侧且没缓存时才进 VM）。
/// <c>ButtonText</c>（DEC-106）：那枚按钮的字态（WSL 链忙时读"检查中…/重编中…"）；null = 恒读 <c>ActionLabel</c>。
/// <c>Busy</c>（DEC-107）：这一扇窗自己那条链在不在忙（决定它的阶段行出不出面；与模块级 UpdateBusy 不是一回事）。</summary>
internal sealed record UpdateSection(
    string Title, Func<string> Version, string ActionLabel, Action OnAction, Action OnWindowOpened,
    Func<string>? ButtonText = null, Func<bool>? Busy = null,
    Func<(string Memo, Color Color)>? StatusMemo = null);

public partial class MainForm
{
    /// 模块键（与 ModelEntry.Backend / ModuleRegistry 的 Key 同值）：想拿"某一模块"的东西就用它，别再写字面量
    internal const string StrataKey = ModelEntry.BackendStrata;
    internal const string LlamaKey = ModelEntry.BackendLlama;
    internal const string NinferKey = ModelEntry.BackendNinfer;      // DEC-096：第三个模块键

    /// 一个模块对界面的完整呈现面。required init 成员 = 加模块时必须表态的项，漏了编译不过。
    /// DEC-202：同时实现 <see cref="IUpdatePane"/>（显式实现，见类末尾）—— 更新窗从此只认那个接口。
    internal sealed class ModuleView : IUpdatePane
    {
        // —— 身份 ——
        public required string Key { get; init; }
        /// 日志通道的键（LogS/LogL 那两路；"launcher" 只留给真·跨模块的话）
        public required string LogKey { get; init; }
        /// 「保存」写家底时那一份文件的名字（toast 与日志要说得出落在哪儿）。Strata 侧没有段级家底 = 空串。
        public required string HomeIniName { get; init; }
        /// <summary>左栏「模块状态」折叠态那一行用的简称（2026-10-15 他拍「Strata/llama/Ninfer」）：
        /// 全称 `llama.cpp` 与另两个横排放不下（卡片内宽 200 逻辑 px）。展开态照旧用 DisplayName。</summary>
        public required string ShortName { get; init; }

        // —— 卡1 / 卡2 / 高级区的控件（谁在明面上由摆位循环决定）——
        public required Func<Control> PortBox { get; init; }        // 卡1「监听端口」那一格
        public required Func<DarkSelect> ReasonBox { get; init; }   // 卡1「思考深度」那一格
        public required Func<DarkSelect> KvBox { get; init; }       // 卡2「KV 精度」那一格
        public required Func<Control> AdvPanel { get; init; }       // 高级区那一套
        public required Action LayoutAdv { get; init; }             // 摆这一套（AdvLayout + 高度）
        /// 自检用：这只格子当前在画外时归谁 —— 卡1 端口格 Strata 报 "public"（它是公开口）、llama 报 "llama"
        public required string PortSlotLabel { get; init; }
        public required string ReasonSlotLabel { get; init; }
        /// GridFit 用例喂给思考深度格的最长取值（截不截断按这个量，别按肉眼）
        public required string ReasonFitSample { get; init; }

        // —— 模型表与"选中了哪个" ——
        public required Func<List<ModelEntry>> Models { get; init; }
        public required Func<Settings, string> TagOf { get; init; }
        public required Action<Settings, string> SetTag { get; init; }

        // —— 卡2 视图推拉与提示 ——
        public required Action<Settings> PushCard2 { get; init; }
        public required Action<Settings> CollectCard2 { get; init; }
        public required Action RefreshTips { get; init; }
        /// 「模型名称」那格可不可编辑：Strata 它是 API 调用名（可填），llama 它是 ini 的段名 = 身份键（只读）
        public required bool ModelNameReadOnly { get; init; }
        /// 切页/重扫后按"家底"校正一遍（llama = 按段读 ini；Strata 走 ApplySettings 那条路，这里空）
        public required Action EnsureRecordLoaded { get; init; }

        // —— 状态与文案（全部转调既有纯函数 / 既有 StatusText）——
        public required Func<(string Text, Color Color)> StatusText { get; init; }
        public required Func<(LightState State, string Desc)> Light { get; init; }
        public required Func<string> StartStopText { get; init; }
        public required Func<string> ModelActionText { get; init; }
        public required Action<string> Log { get; init; }
        /// 左栏「运行状态」卡里属于本模块的那几行（源码/引擎 vs 版本），行序按这里给的
        public required string[] RunInfoRows { get; init; }
        public required Func<string> PidText { get; init; }
        public required Func<string> PortRowText { get; init; }
        /// 「API 地址」那一行用的端口文字（地址格两页同值，归 MainForm）
        public required Func<string> ApiPortText { get; init; }

        // —— 模式条（路由/单体，各模块各记各的）——
        public required Func<bool> RouteMode { get; init; }
        /// 把本模块的模式写进 Settings 的那一格（Strata = WakeOnRequest、llama = LlamaRouteMode）
        public required Action<Settings, bool> WriteRouteMode { get; init; }
        public required Action ToggleRouteMode { get; init; }

        // —— 动作（只转发，不在视图里实现流程）——
        public required Action ToggleService { get; init; }
        /// 卡3 那枚「加载 / 卸载」（DEC-100①b：原来 `Program.ModelAction` 是两模块时代的形状
        /// —— `if (backend == llama) …` 之后整段是 Strata 的流程，Ninfer 的模型进来就会去拉 Strata 引擎，
        /// 与 BUG-039 同族）。动作本身仍留在各模块那一侧，这里只挂转发口。
        public required Action ModelAction { get; init; }
        public required Action PollStats { get; init; }
        /// 状态栏那三档此刻的数（取不到给 null = 短横）
        // DEC-125②：交出来的不再是五档，而是整张显示项表能要的那一份（StatPack）；取不到的字段给 null = 短横
        public required Func<StatPack> StatsValues { get; init; }
        /// 「打开网页」用的完整 URL（含本模块自己的"没在跑"提示）
        public required Func<string> WebUrl { get; init; }
        /// 「打开配置」开的是哪一份 + 一句说明（DEC-077 定口径，DEC-079 改 llama 那一支，**DEC-122② 改 Strata 那一支**）
        public required Func<(string Path, string Note)> ConfigTarget { get; init; }
        // DEC-122②：这一支以前还配了一条「打开前先重物化产物」（ModuleView.Materialize + 三个实现 +
        // Program.MaterializeActiveConfig）。Strata 那一支改开 MultiLauncher.ini 之后整条退役 ——
        // 一个"看一眼"的动作不该顺手写文件；产物照旧由每次启动（手动 / 唤醒）重建。
        // llama / Ninfer 那两支本来就只剩存在性检查、与 ConfigTarget 重复，一并收掉
        //（无调用点不留死代码，先例 = DEC-110② 的 NoteRow）。要看产物就在设置页 Strata 节「模型配置目录」那枚「打开」直达。
        /// 「保存」里本模块那一步：把与"当前生效值"真不一样的键写进家底。Strata 侧没有这一步 = Nothing
        public required Func<(SegWrite State, string Detail)> SaveSectionEdits { get; init; }

        /// <summary>DEC-155：本模块"卡面与家底真不一样的格子数"（0 = 没有未保存改动）。
        /// 跟模型前用它拦"别顶掉本页草稿"（他挑的粒度 = **按目标页**，另两页的草稿不相干）。</summary>
        public required Func<int> PendingEdits { get; init; }

        // —— 模块开关（全隐身）、自动启动、托盘动作、更新入口：第 0 轮 DEC-094 起的集合化判据 ——
        public bool EnabledIn(Settings s) => ModuleRegistry.All.First(x => x.Key == Key).Enabled(s);
        /// <summary>壳自己拉起来的东西还在不在跑（模块开关要拦"要关了但还在跑"；外部实例不算 —— 那不是壳在管的）</summary>
        public required Func<bool> ShellBusy { get; init; }
        /// <summary>模块被停用时清掉「端口上有个外部实例」那一档显示态（DEC-096：三个模块了，别再在 Program 里点名两个）</summary>
        public required Action ClearExternalProbe { get; init; }
        /// <summary>DEC-160（BUG-068①）：外部探活这一族在自检/出图台里**一次都不真问端口**（那道按进程的闸在
        /// <see cref="MainForm.ExternalProbeDisabledForTest"/>），于是"端口上有别人在跑"这一档只能喂出来 ——
        /// 喂进各模块那一个判据位、走同一条 <c>RefreshStatusUi</c>，界面/按钮/托盘跟着变（真路径，不是第二套算法）。</summary>
        public required Action<bool> SetExternalProbe { get; init; }
        /// 「它还在跑」那句话怎么说（每个模块的进程名不一样：Strata = 服务/网关，llama = llama-server）
        public required string SwitchOffMessage { get; init; }
        /// 「自动启动服务 · X」这一格在 Settings 里的落点 + 说明文字（各模块一个字段、一句话）
        public required Func<Settings, bool> AutoStart { get; init; }
        public required Action<Settings, bool> SetAutoStart { get; init; }
        public required string AutoStartDesc { get; init; }
        /// 托盘「启动服务」/「停止服务」与自动启动走的路（与左栏那枚按钮不同：托盘只管进程本身）
        public required Action StartService { get; init; }
        public required Action StopService { get; init; }
        /// <summary>退出时静默停掉本模块（不写「停止」那行日志、不动界面）。
        /// DEC-096：三个模块了，关窗那条路不再点名"停甲 + 停乙"，改成遍历这张表。</summary>
        public required Action StopQuiet { get; init; }
        /// 更新入口：这条流程忙不忙 / 立刻查一次 / 已经探过一次（开窗即查的"不必再查"判据）
        public required Func<bool> UpdateBusy { get; init; }
        public required Action CheckUpdateNow { get; init; }
        public required Func<bool> UpdateOfferSeen { get; init; }
        // —— 更新窗那一套（DEC-096：第三个模块进来后 UpdateForm 里不能再有 `_strata ? 甲 : 乙`，
        //     三条链的"这一窗要读的六件事"全部落在下面这几行里；动作仍转回 MainForm 现成出口）——
        /// 这一版有没有新版本 / 是不是被跳过的那一版（更新窗第二、三行的数据源）
        public required Func<UpdateOffer> Offer { get; init; }
        /// 更新窗第二行那句状态话与它的颜色（"检查中 / 已是最新 / 检查失败"…）
        public required Func<(string Memo, Color Color)> StatusMemo { get; init; }
        /// 「版本」那一行的当前值（Strata = 源码 + 引擎两档；llama = 目录名里的 b 号；Ninfer = 本地哈希反查资产表）
        public required Func<string> CurrentVersion { get; init; }
        public required Action ShowUpdateDetail { get; init; }
        public required Action StartUpgrade { get; init; }
        public required Action SkipOffer { get; init; }
        public required Action UnskipOffer { get; init; }
        /// 更新中途点小 X（只有下载/解压那两档给取消，DEC-055；标志位按模块各一份，DEC-061）
        public required Action RequestCancelUpdate { get; init; }
        /// <summary>DEC-097③：一个模块在一个更新窗里**不止一段**时，第一段要给自己起个标题
        /// （Ninfer = WIN 后端那一支链），否则它与下面那段分不清。单段模块给 null = 仍旧只写模块名。</summary>
        public string? PrimarySectionTitle { get; init; }
        /// <summary>DEC-097③：更新窗下面那一段 = 标题 + 一行版本 + 最右一枚按钮。
        /// null = 这一模块只有一段（Strata / llama 走的就是这条，窗的形状一字不变）。
        /// 为什么给 Ninfer 两段：它一个模块两个后端、两条更新链（WIN = 换 exe，WSL = VM 里重编），
        /// 用户口径「托盘右键那个更新弹窗应该有 WSL 与 WIN 两个版本的信息」—— 仍是一个模块一个窗，不拆窗。</summary>
        public UpdateSection? Secondary { get; init; }

        // —— 壳网关（路由模式就是它实现出来的）——
        /// 挂上 = 本模块走壳网关（今天只有 Strata）；null = 路由由引擎自己实现（llama.cpp 的 --models-preset）
        public GatewayHooks? Gateway { get; init; }
        /// 网关起不来时把本模块标成异常（状态灯红灯）——失败位各模块一份，所以由模块自己说怎么标
        public required Action MarkGatewayFailed { get; init; }

        // —— 这一页的**形状**（DEC-096：第三个模块之后，连"卡长什么样"都不完全一样，也摊进这张表）——
        /// 左栏模式条出不出面：Ninfer 只留按需唤醒那一档（DEC-083 第 4 条"服务常驻被否"）= false
        public required bool ModeBarShown { get; init; }
        /// 「打开网页」那一格在本页出不出面：Ninfer 这一族不放（引擎没有网页界面，/docs 与 /stats 实测 404）。
        /// DEC-107：不出面也**把那一格留空占位**（「高级」仍在下面那一格，位置与另两页对齐）——
        /// 卡2 从此三页同形（两行四格 + 右侧两枚按钮位）；「附加参数」Ninfer 也给了（DEC-107 他批复，
        /// 取代 DEC-083 第 6 条"不给"—— 起前护栏照旧判**实际命令行**，附加参数进不了护栏的盲区）。
        public required bool WebButtonShown { get; init; }
        /// 卡1 左侧那枚叠放方块（WSL / WIN 轮换）。null = 这一页没有后端切换
        public Control? BackendBox { get; init; }
        /// 卡1 三格的宽度口径（测量函数由摆位那侧喂进来；Ninfer 那档 = DEC-088/089 定稿的 59 / 89 / 108 逻辑）
        public required Func<Card1Metrics, (int PortW, int ReasonW, int HostFloor)> Card1Widths { get; init; }
        /// <summary>卡1「思考深度」那一档**三页一起**扣掉的宽度（逻辑像素）。DEC-182 甲：后端方块从"贴在卡1 里侧左沿"
        /// 改成"卡1 左边的兄弟卡"之后，卡1 的内容起点右移了一档 CardPad（16 逻辑），整窗不加宽（DEC-088 第 1 条）
        /// 靠这一笔从余量里出。**必须三页一起扣**：只扣 ninfer 会破 DEC-107④/108 那条「卡1 端口/思考深度三页同宽」
        /// （自检当场红）。底线由「Ninfer 卡1思考深度放得下最长一档（富余 >= 12 逻辑）」盯着 —— 改前实测富余 36 逻辑。
        /// 自检那条同宽判据也引用这一个数，改这里不会漏改判据。</summary>
        public const int ReasonCutLogical = 16;

        /// <summary>卡1 摆位用的三个测量口（同一套算式交给各模块那一行，别在摆位里再写 if）。</summary>
        internal readonly record struct Card1Metrics(Func<string, int> BoxW, Func<string, Font, int> Need, Func<int, int> S);

        public string DisplayName => ModuleRegistry.All.First(x => x.Key == Key).DisplayName;

        // —— DEC-202：更新窗要的那一面（显式实现，签名与成员名一个不改）——
        // 为什么显式：本表里 `Offer` / `StatusMemo` / `UpdateBusy` 是 `Func<…>` **属性**，接口要的是方法；
        // 显式实现让两个家并存、互不遮蔽，也让"窗只要这几项"这件事在接口那一份写清楚（见 IUpdatePane）。
        string IUpdatePane.Key => Key;
        string IUpdatePane.DisplayName => DisplayName;
        string? IUpdatePane.PrimarySectionTitle => PrimarySectionTitle;
        UpdateSection? IUpdatePane.Secondary => Secondary;
        UpdateOffer IUpdatePane.Offer() => Offer();
        (string Memo, Color Color) IUpdatePane.StatusMemo() => StatusMemo();
        bool IUpdatePane.UpdateBusy() => UpdateBusy();
        bool IUpdatePane.UpdateOfferSeen() => UpdateOfferSeen();
        string IUpdatePane.CurrentVersion() => CurrentVersion();
        void IUpdatePane.CheckUpdateNow() => CheckUpdateNow();
        void IUpdatePane.ShowUpdateDetail() => ShowUpdateDetail();
        void IUpdatePane.StartUpgrade() => StartUpgrade();
        void IUpdatePane.SkipOffer() => SkipOffer();
        void IUpdatePane.UnskipOffer() => UnskipOffer();
        void IUpdatePane.RequestCancelUpdate() => RequestCancelUpdate();
    }

    // ---------------- 表本体 ----------------

    ModuleView[] _views = Array.Empty<ModuleView>();

    void BuildModuleViews()
    {
        // 加一个模块 = 三处点名（ModuleRegistry.All / 这里一行 / 设置页 PageForModule），漏任何一处当场抛（DEC-094 第 2 条）
        _views = new ModuleView[] { BuildStrataView(), BuildLlamaView(), BuildNinferView() };
    }

    internal ModuleView View(string key) =>
        _views.FirstOrDefault(v => v.Key == key)
        ?? throw new InvalidOperationException($"模块表里没有这一行：{key}（ModuleRegistry 有它、视图没建 = 第 0 轮那张表漏了行）");

    /// 当前激活模块的呈现面。界面代码要模块相关的东西**只从它要**，别再问"是不是 Strata"。
    internal ModuleView ActiveView => View(_activeModule);

    /// 整张表（自检与设置页按它穷举 —— 加模块后这些用例自动多一条，不用回来改）
    internal IReadOnlyList<ModuleView> ModuleViews => _views;

    // ---- 自检出口 ----
    internal IReadOnlyList<ModuleView> ViewsForTest => _views;
    /// 「要关掉它但它的服务还在跑」那句话（校验用；话长在各模块那一行，加模块要说清"怎么停"）
    internal string SwitchOffMessageForTest(string key) => View(key).SwitchOffMessage;
    /// 壳自己拉起来的东西在不在跑（外部实例不算）
    internal bool ShellBusyForTest(string key) => View(key).ShellBusy();

    // ---------------- Strata 那一行 ----------------

    ModuleView BuildStrataView() => new()
    {
        Key = "strata",
        LogKey = "strata",
        HomeIniName = "",                     // Strata 侧的家底是物化出来的 data\config\strata\<tag>.json，不叫 ini
        ShortName = "Strata",
        Log = LogS,

        PortBox = () => _txtPort,
        ReasonBox = () => _cboReasoning,
        KvBox = () => _cboKv,
        AdvPanel = () => _advStrata,
        LayoutAdv = () => { _advStrata.Height = AdvLayout(_advStrata); },
        PortSlotLabel = "public",       // Strata 卡1 那个口就是公开端口（网关/服务都听它）
        ReasonSlotLabel = "strata",
        ReasonFitSample = "medium",

        Models = () => _models,
        TagOf = s => s.Model,
        SetTag = (s, tag) => s.Model = tag,

        PushCard2 = StrataPushCard2,
        CollectCard2 = StrataCollectCard2,
        RefreshTips = StrataTips,
        ModelNameReadOnly = false,        // Strata 那格是 API 调用名（model_name），可以填
        EnsureRecordLoaded = () => { /* Strata 的家底随 ApplySettings/换模型推，切页不需要补读 */ },

        StatusText = StrataStatusText,
        // 2026-10-15（用户实跑点名）：灯里那个端口与另两页统一成「监听端口」= 卡1 那一格 / 下面「API 地址」行用的
        // 那个口（客户端连的就是它）。以前这里传 ServicePortValue()，而它"网关一立就退到内部端口"→ 路由模式这个
        // 常态下灯里报的是 8081 那类内部口，看着与另两页两套口径。检测那一头照旧打**实际服务端口**
        //（MainForm.StatusBar.cs 的 /metrics，不打公开端口），只是显示这一格换成监听口。
        Light = () => StrataLight(_strataFailed, _process is { HasExited: false }, _readyUrl.Length > 0,
                                  _externalRunning, StrataGateway is not null, _routeModeStrata, PublicPortValue()),
        StartStopText = () => StartStopTextForStrata(_process is { HasExited: false }, _externalRunning, StrataGateway is not null),
        ModelActionText = StrataModelActionText,
        // 2026-10-15（他拍）：原来这里单列「源码」「引擎」两行 → 与另两页统一成一行「版本」（值 = 引擎版本）。
        // 三页行集全等之后，切页时 RefreshRunStateView 那行恒为 true、setter 提前返回 = **零重排**，
        // 左栏模式条不再跟着跳一行（这就是"切页时模式条破碎"的根因）。
        RunInfoRows = new[] { "版本" },
        PidText = () => _process is { HasExited: false } ? _process!.Id.ToString() : "-",
        // 端口行：跑着（或有网关）报**监听端口**（公开口）—— 与左栏状态灯、卡1「监听端口」格、上面「API 地址」行
        // 同一档（2026-10-15 用户点名"改公开"）。以前传 ServicePortValue()，网关一立就退到内部口，于是灯报 8080、
        // 这一行报 8081，自己跟自己不同值。服务实际听哪个口归"检测"那一档（/metrics 打 _serviceBindPort），不在这显示。
        // 没跑就把界面上填的那串原样显示（含没填/填错的情况）—— 这一档与 Ninfer 那行同形，不动。
        PortRowText = () => _process is { HasExited: false } || StrataGateway is not null
            ? PublicPortValue().ToString() : _txtPort.Text.Trim(),
        ApiPortText = () => _txtPort.Text.Trim(),

        RouteMode = () => _routeModeStrata,
        WriteRouteMode = (s, v) => s.WakeOnRequest = v,
        ToggleRouteMode = StrataToggleRouteMode,

        // 壳网关（路由模式就是它实现出来的）：Strata 是今天唯一挂 hooks 的模块
        Gateway = new GatewayHooks(
            Key: "strata",
            DisplayName: "Strata",
            OwnedBy: ModelEntry.BackendStrata,          // /v1/models 里那个 owned_by（第 0 轮起参数化）
            PublicPort: PublicPortValue,
            BindHost: HostValue,                 // DEC-138（A-5）：绑址走注入，StartGateway 不再摸 _txtHost
            IdleTimeout: () => TimeSpan.FromMinutes(IdleStopMinutes()),
            UpstreamPort: InternalPortValue,
            Prepare: FixInternalPort,
            Models: () => _models,
            CurrentTag: SelectedTag,
            UpstreamRunning: () => _process is { HasExited: false },
            RunningTag: () => _runningTag,
            Wake: StrataWakeUpstream,
            StopUpstream: StopServer,                   // 调用方（空闲停服）负责把它排到界面线程上
            StopUpstreamAndWait: StrataStopUpstreamAndWait,
            // DEC-165：只收"此刻跑的正是这一件"那一档 —— 别的一件正跑着就什么都不做（调用方已排到界面线程）
            UnloadAbandonedStarted: tag => { if (_process is { HasExited: false } && _runningTag == tag) StopServer(); },
            WaitReady: WaitReadyAsync,
            Log: LogS,
            OnStarted: StrataGatewayStarted,
            OnStopped: StrataGatewayStopped,
            // DEC-122①：这一族的唤醒取参 = 「配置模板 ⊕ [Model:<tag>] 记录」，所以要说清按谁的存档起
            WakeFromHomeNote: static tag => "按 " + tag + " 的存档启动",
            // 2026-10-15（他实跑报"不到 10 分钟就被按超时卸载"）：加载中这一档 = 进程在跑但还没 ready。
            // 空闲判定在它上面停一拍，别把正在读权重的进程停掉。
            UpstreamBusy: () => _process is { HasExited: false } && _readyUrl.Length == 0),
        MarkGatewayFailed = () => { _strataFailed = true; },   // 网关起不来 = 本模块异常（状态灯红灯）

        // 模块开关 / 自动启动 / 托盘 / 更新入口（第 0 轮 DEC-094：这些以前散在 bool 四元组与 if (key=="strata") 里）
        ShellBusy = () => _process is { HasExited: false } || StrataGateway is not null,
        ClearExternalProbe = () => { _externalRunning = false; },
        SetExternalProbe = (up) => { _externalRunning = up; },
        SwitchOffMessage = "Strata 模块就要被关闭，但它的服务/网关还在跑。先点左栏「停止」把服务停掉，再保存。",
        AutoStart = s => s.AutoStartStrata,
        SetAutoStart = (s, v) => s.AutoStartStrata = v,
        AutoStartDesc = "打开启动器后自动启动 Strata 服务。",
        StartService = StartServer,
        StopService = StopServer,
        StopQuiet = StopServerQuiet,
        UpdateBusy = () => UpdateUiBusy,
        CheckUpdateNow = UpdateClick,
        UpdateOfferSeen = () => _remoteTag.Length > 0,
        Offer = UpdateOfferForSettings,
        StatusMemo = UpdateStatusForSettings,
        CurrentVersion = () => { var (src, eng) = VersionsForSettings(); return $"源码 {src} · 引擎 {eng}"; },
        ShowUpdateDetail = ShowUpdateDetail,
        StartUpgrade = DoUpgrade,
        SkipOffer = SkipCurrentVersion,
        UnskipOffer = UnskipCurrentVersion,
        RequestCancelUpdate = () => RequestCancelUpdate(llama: false),

        ToggleService = StrataToggleService,
        ModelAction = StrataModelAction,
        PollStats = () => { _ = PollStrataStats(); },
        StatsValues = () => WithGpu(_strataPack),
        WebUrl = StrataWebUrl,
        ConfigTarget = StrataConfigTarget,
        SaveSectionEdits = () => (SegWrite.Nothing, ""),    // Strata 侧没有"段"这个东西
        PendingEdits = () => StrataPendingEditCount(CollectSettings()),

        // 这一页的形状（DEC-096 起的四个新成员）：另两页全部保持 DEC-078/079 定稿的样子，一个字没变
        ModeBarShown = true, WebButtonShown = true, BackendBox = null,
        // 宽度的算式与 CardStart() 原来写死的那三行一字不差（吃余量的是监听地址、端口 +10%、思考深度拿地址那档）
        Card1Widths = static m => ((int)Math.Round(m.BoxW("00000") * 1.1), m.BoxW("255.255.255.255") - m.S(ModuleView.ReasonCutLogical), m.BoxW("255.255.255.255")),
    };

    // ---------------- llama.cpp 那一行 ----------------

    ModuleView BuildLlamaView() => new()
    {
        Key = "llama",
        LogKey = "llama",
        HomeIniName = LlamaIniFileName,
        ShortName = "llama",                  // 简称：DisplayName 的 "llama.cpp" 与另两个横排放不下（他 2026-10-15 点名用 llama）
        Log = LogL,

        PortBox = () => _txtPortLlama,
        ReasonBox = () => _cboReasoningEffort,
        KvBox = () => _cboKvLlama,
        AdvPanel = () => _advLlama,
        LayoutAdv = () => { _advLlama.Height = AdvLayout(_advLlama); },
        PortSlotLabel = "llama",
        ReasonSlotLabel = "llama",
        ReasonFitSample = "default",

        Models = () => _llamaModels,
        TagOf = s => s.LlamaModel,
        SetTag = (s, tag) => s.LlamaModel = tag,

        PushCard2 = LlamaPushCard2,
        CollectCard2 = LlamaCollectCard2,
        RefreshTips = LlamaTips,
        ModelNameReadOnly = true,         // llama 那格是 llama_models.ini 的段名 = 身份键，只读
        EnsureRecordLoaded = EnsureLlamaRecordLoaded,

        StatusText = LlamaStatusText,
        Light = () => LlamaLight(_llamaRunFailed, _llamaOp, LlamaRunning || _llamaExternal,
                                 _llamaLoaded.Count > 0, LlamaRunning ? _llamaActivePort : LlamaPortValue()),
        StartStopText = () => StartStopTextForLlama(LlamaRunning || _llamaExternal),
        ModelActionText = LlamaModelActionText,
        RunInfoRows = new[] { "版本" },
        PidText = () => LlamaRunning ? _llamaProcess!.Id.ToString() : "-",
        PortRowText = () => (LlamaRunning ? _llamaActivePort : LlamaPortValue()).ToString(),
        ApiPortText = () => LlamaPortValue().ToString(),

        RouteMode = () => _routeModeLlama,
        WriteRouteMode = (s, v) => s.LlamaRouteMode = v,
        ToggleRouteMode = LlamaToggleRouteMode,
        // 本模块**不挂 Gateway hooks**（= Gateway 成员留空）：llama.cpp 的"路由"是引擎自己的
        // --models-preset / models-max 实现的，壳不替它站一个公开端口的网关
        //（第 0 轮把这条写成"表里没有 = 不立网关"，别再靠 if (key=="strata") 判断）。
        MarkGatewayFailed = () => { _llamaRunFailed = true; },   // 真接到壳网关时才算它 failed

        // 模块开关 / 自动启动 / 托盘 / 更新入口
        ShellBusy = () => _llamaProcess is { HasExited: false },
        ClearExternalProbe = () => { _llamaExternal = false; },
        SetExternalProbe = (up) => { _llamaExternal = up; },
        SwitchOffMessage = "llama.cpp 模块就要被关闭，但 llama-server 还在跑。先选中 llama 模型、点左栏「停止」把它停掉，再保存。",
        AutoStart = s => s.AutoStartLlama,
        SetAutoStart = (s, v) => s.AutoStartLlama = v,
        AutoStartDesc = "打开启动器后自动拉起 llama-server。",
        StartService = StartLlamaServer,
        StopService = StopLlamaServer,
        StopQuiet = StopLlamaQuiet,
        UpdateBusy = () => LlamaUiBusy,
        CheckUpdateNow = LlamaCheckUpdate,
        UpdateOfferSeen = () => _llamaRemoteTag.Length > 0,
        Offer = LlamaUpdateOfferForSettings,
        StatusMemo = LlamaUpdateStatusForSettings,
        CurrentVersion = LlamaVersionForSettings,
        ShowUpdateDetail = ShowLlamaUpdateDetail,
        StartUpgrade = LlamaUpgrade,
        SkipOffer = LlamaSkip,
        UnskipOffer = LlamaUnskip,
        RequestCancelUpdate = () => RequestCancelUpdate(llama: true),

        ToggleService = LlamaToggleService,
        ModelAction = LlamaModelAction,
        PollStats = RefreshLlamaStats,
        StatsValues = () => WithGpu(LlamaPack()),
        WebUrl = LlamaWebUrl,
        ConfigTarget = LlamaConfigTarget,
        SaveSectionEdits = () => { var st = SaveLlamaSectionEdits(out var d); return (st, d); },
        // 没选段 = 没有可保护的草稿（家底基线随选中段走，空段上比出来的是假差异）。
        // 比的是**卡面现值**（CollectSettings：当前页读控件、另两页取各自的草稿），不是 _config.Current 那份旧的
        PendingEdits = () => { var s = CollectSettings(); return s.LlamaModel.Length == 0 ? 0
            : LlamaSectionEdits(s, _llamaEffective).Count; },

        ModeBarShown = true, WebButtonShown = true, BackendBox = null,
        Card1Widths = static m => ((int)Math.Round(m.BoxW("00000") * 1.1), m.BoxW("255.255.255.255") - m.S(ModuleView.ReasonCutLogical), m.BoxW("255.255.255.255")),
    };

    // ---------------- Ninfer 那一行（DEC-096，第三个模块）----------------

    ModuleView BuildNinferView() => new()
    {
        Key = "ninfer",
        LogKey = "ninfer",
        HomeIniName = NinferIniFileName,
        ShortName = "Ninfer",
        Log = LogN,

        PortBox = () => _txtPortNinfer,               // 卡1「监听端口」= 公开口（可编辑，DEC-089 撤掉只读）
        ReasonBox = () => _cboNinferReasoning,        // 卡1「思考深度」= --default-reasoning-effort / --no-thinking
        KvBox = () => _cboKvNinfer,                   // 卡2「KV 精度」= --kv-dtype（与另两页那只下拉同格互斥）
        AdvPanel = () => _advNinfer,                  // 高级区内容跟后端换（两套格子互斥可见）
        LayoutAdv = () => { _advNinfer.Height = AdvLayout(_advNinfer); },
        PortSlotLabel = "ninfer",
        ReasonSlotLabel = "ninfer",
        ReasonFitSample = "minimal",                  // 两张取值表里最长的那一档（WIN 有 minimal/default）

        Models = () => _ninferModels.Where(m => m.NinferBackend == ActiveNinferBackend(_config.Current)).ToList(),
        TagOf = s => s.NinferModel,
        SetTag = (s, tag) => s.NinferModel = tag,

        PushCard2 = NinferPushCard2,
        CollectCard2 = NinferCollectCard2,
        RefreshTips = NinferTips,
        ModelNameReadOnly = true,                     // 段名 = 对外 model-id = 身份键，不可编辑（同 llama 那一页）
        EnsureRecordLoaded = EnsureNinferRecordLoaded,

        StatusText = NinferStatusText,
        // 2026-10-15（与 Strata 同一条口径）：灯里的端口恒取**公开端口** = 卡1 那一格 / 「API 地址」行的那个口。
        // 以前跑着时传 _ninferActivePort（引擎内部口），与它自己的「端口」行（PortRowText 恒报公开口）就对不上；
        // 引擎实际听哪个内部口属于"检测"那一档（MainForm.NinferStatus.cs 的探活照旧用它），不进状态显示。
        Light = () => NinferLight(_ninferRunFailed, NinferRunning, NinferLoaded, _ninferExternal,
                                  NinferGatewayUp, NinferPublicPortValue()),
        StartStopText = () => StartStopTextForNinfer(NinferRunning, _ninferExternal, NinferGatewayUp),
        ModelActionText = NinferModelActionText,
        // 左栏那几行：这一族也给「版本」一行（DEC-107 他问"为什么不像其他 2 页一样显示版本"）。
        // 历史小账：「内部端口 / 件 / 空闲阈值」三行当年只写进了这张表、从没真正建出来过（KvRow 那组
        // 固定键里没有它们，RefreshRunStateView 也只认源码/引擎/版本）—— 这轮按"表里写的必须是真的"收掉。
        RunInfoRows = new[] { "版本" },
        PidText = () => NinferRunning ? _ninferProcess!.Id.ToString() : "-",
        PortRowText = () => NinferRunning || NinferGatewayUp ? NinferPublicPortValue().ToString() : _txtPortNinfer.Text.Trim(),
        ApiPortText = () => NinferPublicPortValue().ToString(),

        // 模式：本族只留按需唤醒一档 → 模式条整条不出现，RouteMode 恒真（网关照常按这一档立）
        RouteMode = () => true,
        WriteRouteMode = static (s, v) => { /* Ninfer 没有模式可记：常驻那一档被用户否了（DEC-083 第 4 条） */ },
        ToggleRouteMode = NinferToggleRouteMode,

        // 壳网关：这一族要按需唤醒，所以自己填一份 hooks（DEC-094 第 4 条：不挂 hooks = 不立网关）
        Gateway = new GatewayHooks(
            Key: "ninfer",
            DisplayName: "Ninfer",
            OwnedBy: ModelEntry.BackendNinfer,            // DEC-137（A-4）
            PublicPort: NinferPublicPortValue,
            BindHost: HostValue,                 // DEC-138（A-5）：绑址走注入，StartGateway 不再摸 _txtHost
            // DEC-157（他批的第 6 条）：阈值跟**真起来那一侧**走 —— 原来读的是界面那枚方块，
            // 而「走段」（DEC-157 第 4 条）之后跑的那一侧与方块可以不是同一枚（方块也会跟着切，但网关这一档
            // 在跟着切之前与停服判定上都该认实际那一件），拿另一侧的数判这一侧的空闲 = 他调好的阈值不生效。
            IdleTimeout: () => TimeSpan.FromMinutes(NinferIdleMinutes(_config.Current,
                _ninferProcess is { HasExited: false } && _ninferRunningBackend.Length > 0
                    ? _ninferRunningBackend : ActiveNinferBackend(_config.Current))),
            UpstreamPort: NinferInternalPortValue,
            Prepare: FixNinferInternalPort,
            Models: AllNinferModels,                  // /v1/models 列全三件（不按当前后端过滤，DEC-083）
            CurrentTag: () => _config.Current.NinferModel,
            UpstreamRunning: () => NinferRunning,
            RunningTag: () => _ninferRunningTag,
            Wake: NinferWakeUpstream,
            StopUpstream: StopNinferServer,
            StopUpstreamAndWait: NinferStopUpstreamAndWait,
            // DEC-165：同上 —— 只收"此刻跑的正是这一件"那一档（NinferRunning 是引擎在跑，网关立着不算）
            UnloadAbandonedStarted: tag => { if (NinferRunning && string.Equals(_ninferRunningTag, tag, StringComparison.Ordinal)) StopNinferServer(); },
            WaitReady: WaitNinferReadyAsync,
            Log: LogN,
            OnStarted: NinferGatewayStarted,
            OnStopped: NinferGatewayStopped,
            // DEC-135（拍板①）：这一族现在也有"另一份参数"要交代了 —— 远端唤醒按**那一段自己**取参、不吃界面卡，
            // 于是「保存」同样是"让远端用上新参数的唯一动作"（与 Strata 那一行、llama 侧的段同一句话）。
            WakeFromHomeNote: static tag => "按 " + tag + " 段里的那一把启动",
            // 同 Strata 那一行：引擎在跑但还没打出 ready 标记 = 加载中，空闲判定让一拍
            UpstreamBusy: () => _ninferProcess is { HasExited: false } && _ninferReadyTcs is not { Task.IsCompleted: true }),
        MarkGatewayFailed = () => { _ninferRunFailed = true; },

        ShellBusy = () => _ninferProcess is { HasExited: false } || NinferGatewayUp,
        ClearExternalProbe = () => { _ninferExternal = false; },
        SetExternalProbe = (up) => { _ninferExternal = up; },
        SwitchOffMessage = "Ninfer 模块就要被关闭，但它的引擎/网关还在跑。先点左栏「停止」把 ninfer-serve 停掉，再保存。",
        AutoStart = s => s.AutoStartNinfer,
        SetAutoStart = (s, v) => s.AutoStartNinfer = v,
        AutoStartDesc = "打开启动器后自动启动 Ninfer 服务。",
        // DEC-100①：自动启动与「启动」同一条路 = 只立网关（原来这里直接起引擎 = 开机就把件读进显存）
        StartService = StartNinferGateway,
        StopService = StopNinferServiceAndGateway,
        StopQuiet = StopNinferQuiet,
        ModelAction = NinferModelAction,
        UpdateBusy = () => NinferUiBusy,
        CheckUpdateNow = NinferCheckUpdate,
        UpdateOfferSeen = () => _ninferRemoteTag.Length > 0,
        Offer = NinferUpdateOfferForSettings,
        StatusMemo = NinferUpdateStatusForSettings,
        CurrentVersion = NinferVersionForSettings,
        ShowUpdateDetail = ShowNinferUpdateDetail,
        StartUpgrade = NinferUpgrade,
        SkipOffer = NinferSkip,
        UnskipOffer = NinferUnskip,
        RequestCancelUpdate = RequestCancelNinferUpdate,
        // DEC-097③：一个模块两条链 → 曾做成"一个窗两段"；DEC-107 按用户批复（"之前说好是 2 个模块分开做，
        // 各自的弹窗"）改回**一后端一窗**（设计稿支点 F 原本的写法）：WIN 走上面那套三行窗（主段标题
        // 「WIN 后端」），WSL 走自己的小窗（一节 = 一行版本 + 一枚按钮）。段名沿用设置页 Ninfer 节那两个组标题。
        // DEC-106：WSL 那条链按钮字态跟着忙态走（更新… / 检查中… / 重编中…）；回退入口住设置页 WSL 组。
        // DEC-110③：再加一条 StatusMemo —— 另三扇窗（Strata / llama / Ninfer-WIN）都有结果回显那一行，
        // WSL 这扇以前只有日志 + toast（用户抓的"已是最新只出现在日志区"）。
        PrimarySectionTitle = "WIN 后端",
        Secondary = new UpdateSection("WSL 后端", NinferWslVersionForSettings, "更新…",
            NinferWslUpdateFlow, () => EnsureNinferWslVersionProbed(), NinferWslButtonText,
            () => _ninferWslPhase.Length > 0, NinferWslStatusMemo),

        ToggleService = ToggleNinferService,
        PollStats = () => { _ = PollNinferStats(); },
        StatsValues = () => WithGpu(_ninferPack),
        WebUrl = NinferWebUrl,                        // 这一族不放「打开网页」（URL 只给托盘/日志那条后路用）
        ConfigTarget = NinferConfigTarget,
        SaveSectionEdits = () => { var st = SaveNinferSectionEdits(out var d); return (st, d); },
        PendingEdits = () => { var s = CollectSettings(); return s.NinferModel.Length == 0 ? 0
            : NinferSectionEdits(s, NinferBackendOf(s, s.NinferModel.Trim()), _ninferEffective).Count; },

        // DEC-107：卡1 右侧「监听端口｜思考深度」两格改用**另两页一字不差的那两条算式**（他批复：
        // "右侧端口/思考深度部分与其他2页也要完全一致,可以压缩监听地址输入框的宽度"）——
        // 方块吃掉的那截从监听地址（吃余量项）身上出，地址的**下限**相应压到 S(96)：
        // 窗口收到最小时地址格只保证 "192.168.1.9" 这类 11 字符 IP 放得下；窗口正常大小时它拿余量（≈136+ 逻辑）。
        // 自检钉两条：端口/思考深度宽与 Strata 页相等（跨页等宽），地址格在最小窗下不裁卡片。
        ModeBarShown = false, WebButtonShown = false,
        BackendBox = _backendToggle,
        // 端口 59 / 思考深度 89 / 地址下限 96 那一档（DEC-088/089 拍、DEC-096 收进这一行；地址下限是 DEC-089
        // 给方块让路时压下来的）。DEC-182 甲：思考深度这一档同样吃 `ReasonCutLogical`，三页一起扣才守得住同宽。
        Card1Widths = static m => ((int)Math.Round(m.BoxW("00000") * 1.1), m.BoxW("255.255.255.255") - m.S(ModuleView.ReasonCutLogical), m.S(96)),


    };

    // ---------------- 成对的动作与视图实现（一个模块一节）----------------
    //（卡2 的互斥可见性与可编辑性收口在 MainForm.Modules.cs 的 PushCard2ForActiveModule：
    //  每模块一只 KV 下拉、只有激活那只在明面上，「模型名称」按本模块的 ModelNameReadOnly 决定能不能改）

    // —— 卡2 推 / 收 ——
    // 卡2 那四只格子（模型名称｜上下文 ／ KV 精度｜附加参数）是**两页共用的控件**，
    // 值随激活模块换：可见性与互斥摆位由摆位循环负责，这里只管"把值推进去 / 把值收出来"。

    void StrataPushCard2(Settings s)
    {
        _txtModelName.Text = s.ApiModel;
        _txtContext.Text = s.Context;
        _cboKv.SelectByTag(_cboKv.Items.Any(i => (i.Tag as string) == s.Kv) ? s.Kv : "int8");
        _txtExtra.Text = s.ExtraArgs;
        AfterCard2Push(s);
    }

    void LlamaPushCard2(Settings s)
    {
        // DEC-079：值就取 s（按模型取家底那条路已经把「段 → `[*]` → 空」读进去了）。
        // **不再"每次刷视图现读一遍文件"**——那样会把还没点「保存」的手改顶掉（界面上改的就是那一段，
        // 视图得先认用户的手；要回到文件里的值切一次模型或点「保存」就是了）。
        var t = SelectedModel()?.Tag ?? s.LlamaModel;
        _txtModelName.Text = t;
        _txtContext.Text = s.LlamaCtx;
        SelectWithFallback(_cboKvLlama, s.LlamaKv);   // 表外组合（段里手写过的）补成一项再选中，不静默丢
        _txtExtra.Text = s.LlamaExtraArgs;            // DEC-075：llama 侧附加参数（追加到 llama-server 命令行）
        AfterCard2Push(s);
    }

    // 两页共用的尾部：提示重算 + 让卡2 重新 Layout（KV 格换控件 / 挪位）
    void AfterCard2Push(Settings s)
    {
        ActiveView.RefreshTips();
        if (HeightChanged is not null) HeightChanged();
    }

    void StrataCollectCard2(Settings s)
    {
        s.Model = SelectedTag();
        s.ApiModel = _txtModelName.Text.Trim();
        s.Context = _txtContext.Text.Trim();
        s.Kv = _cboKv.Selected?.Tag as string ?? "int8";
        s.ExtraArgs = _txtExtra.Text.Trim();
    }

    void LlamaCollectCard2(Settings s)
    {
        s.LlamaModel = SelectedTag();
        // DEC-079：这三格从"只读展示"变成段级编辑（上下文 / KV 精度），收进 Settings 只当界面载体
        // —— 它们已退役出 [Current]，真正的家底在 llama_models.ini 的那一段里（「保存」负责写回去）。
        // 附加参数（DEC-075 起）照旧可编辑：原样追加到 llama-server 命令行末尾
        s.LlamaCtx = _txtContext.Text.Trim();
        s.LlamaKv = _cboKvLlama.Selected?.Tag as string ?? "";
        s.LlamaExtraArgs = _txtExtra.Text.Trim();
    }

    // —— 卡2 那几格的 tooltip ——
    // DEC-079 起 llama 侧的模型名仍是只读展示（它是 ini 的段名 = 身份键），
    // 但上下文 / KV / 高级那批都**可编辑并写回该模型那一段**；这些格子的文案由 RefreshLlamaInheritanceTips
    // 统一负责（继承状态换模型就会变，tooltip 得跟着重算），所以 llama 这一侧只调它。
    void StrataTips()
    {
        Tip.Set(_txtModelName, "API 调用时客户端填的 model 名（写进运行配置的 model_name）；留空 = 保留模型配置原本的");
        Tip.Set(_txtContext, "上下文长度（token）；越大越吃内存/显存");
        Tip.Set(_cboKv, "KV 缓存精度：int8 默认；q4_0 省一半 KV 内存、精度略低；k8v4 混合（不支持 KV 流式）");
        Tip.Set(_txtExtra, "原样追加到 Strata 引擎参数末尾，用于壳未提供的 flag；留空即不追加");
    }

    void LlamaTips() => RefreshLlamaInheritanceTips();

    // 视图切到 llama 页 / 重扫之后，如果选中的段变了（自动选了第一个、或 ini 里从没记过 llama 模型），
    // 要补一次「按段取家底」。DEC-079 之前这步是免费的——卡2 每次刷视图都现读文件；
    // 现在视图认的是"用户的手"（没点保存的改动不能被现读顶掉），所以没载入过就必须显式载一次，
    // 否则界面会空着（自检抓到过：新 ini 里没记 llama 模型时，切到 llama 页格子全空）。
    // Strata 那一行不需要这一步（家底随 ApplySettings / 换模型那条路推）。
    void EnsureLlamaRecordLoaded()
    {
        var tag = SelectedTag().Length > 0 ? SelectedTag() : _config.Current.LlamaModel;
        if (tag.Length == 0 || string.Equals(_llamaLoadedTag, tag, StringComparison.OrdinalIgnoreCase)) return;
        ApplyLlamaRecord(tag);
    }

    // 卡2 那几格的提示：由各模块那一行给（llama 侧走继承标注那套，文案家只在 RefreshLlamaInheritanceTips）
    void RefreshCommonTips() => ActiveView.RefreshTips();

    // —— 模式条（路由模式 / 单体模型）——
    void StrataToggleRouteMode()
    {
        _routeModeStrata = !_routeModeStrata;
        RefreshModeLabel();
        LogS($"Strata 启动模式切换为「{(_routeModeStrata ? "路由模式" : "单体模型")}」");
        ApplyGatewaySetting(_routeModeStrata);
        // 起/停网关内部有好几条分支（服务在跑=>重启 / 没跑=>置停止态 / 失败=>弹窗），
        // ToggleStartMode 尾部统一 RefreshStatusUi 收口（原口径不变）
    }

    void LlamaToggleRouteMode()
    {
        _routeModeLlama = !_routeModeLlama;
        RefreshModeLabel();
        LogL($"llama.cpp 启动模式切换为「{(_routeModeLlama ? "路由模式" : "单体模型")}」");
    }

    // —— 壳网关的 Strata 那一头（第 0 轮 DEC-094：网关只认 hooks，不再自己摸 _process / _models / _runningTag）——
    // 拉起：仍不等 ready（等由调用方在单飞门外做，并发请求共享同一个等待，DEC-061）
    // DEC-122①：这一条是**远端请求**要起某一件，取参 = 「配置模板 ⊕ 该模型自己的 [Model:<tag>] 记录」，
    // 不读界面卡（换下拉只是"看一眼"、会把所看那件的存档载上卡），也不读物化产物（那是上一次的结果，
    // 拿它打底就是自洽循环）。于是「保存」= 让远端用上新参数的唯一动作。
    async Task StrataWakeUpstream(string tag, CancellationToken ct)
    {
        // DEC-157（他批的第 1 条）：显存这一关从此**三模块一视同仁**（支点 I 从设计第一天就是这么写的，
        // DEC-096~098 施工时只装在 Ninfer 那一族里）。请求从公开口进来 = 按「显存互斥处理」排队等 / 直接报错，
        // 判据 = 本壳起的另一枚**真把模型读进显存**的在跑；台账空 = 判不了 = 放行（口径同 DEC-067）。
        // 人自己点「启动」/「加载」那一档在 StartService 里当场拒绝并点名，不在这里排队。
        // DEC-160：排队那一圈吃**这一发请求的**令牌；轮到自己要起件前再看一眼（对面走了就不起）。
        await GpuGateAsync(StrataKey, LogS, null, ct);
        ct.ThrowIfCancellationRequested();
        var started = await UiAsync(() =>
        {
            var ok = StartService(tag, openBrowser: false, wakeFromRecord: true, out var err);
            return (ok, err);
        });
        if (!started.Item1) throw new InvalidOperationException(started.Item2);
        if (_readyTcs is null) throw new InvalidOperationException("启动没有建立就绪信号");
    }

    // 切模型前先停旧的：等**那一个**进程真退出（原口径 WaitForExit(20000)），否则新服务撞上还占着的端口
    async Task StrataStopUpstreamAndWait()
    {
        var old = _process;
        UiPost(StopServer);
        if (old is null) return;
        try { await Task.Run(() => old.WaitForExit(20000)); } catch { /* 已经退了 */ }
    }

    // 立好网关之后：服务原本占着公开端口 => 按网关模式重启到内部端口，否则端口会撞
    void StrataGatewayStarted()
    {
        if (_process is { HasExited: false })
        {
            LogS("服务原本占着公开端口：按网关模式重启到内部端口 " + InternalPortValue());
            RestartServer();
        }
        else SetStoppedState();
    }

    // 收了网关之后：公开端口交还引擎（同样要重启，否则服务还跑在内部端口上、外面连不到）
    void StrataGatewayStopped()
    {
        if (_process is { HasExited: false })
        {
            LogS("服务原本跑在内部端口：按非网关模式重启到公开端口 " + PublicPortValue());
            RestartServer();
        }
        else SetStoppedState();
    }
    //（同 StartGateway：网关启停不扫模型 —— 要扫由服务那一头的 StartService / OnServerExited 负责）

    // —— 左栏启停：只管服务本身（DEC-011）——
    // 「停止」覆盖三种真实情况（用户反馈 2026-10-01）：壳拉起的服务、外部拉起的服务（确认后结束）、仅网关。
    void StrataToggleService()
    {
        if (_process is { HasExited: false }) { StopServiceAndGateway(); return; }
        if (_externalRunning) { StopExternalStrata(); return; }
        if (StrataGateway is not null) { StopGateway(); return; }
        StartServer();
    }

    void LlamaToggleService()
    {
        if (LlamaRunning) { StopLlamaServer(); return; }
        if (_llamaExternal) { StopExternalLlama(); return; }
        StartLlamaServer();
    }

    /// <summary>Strata 那一支的模型按钮 = 原 `Program.ModelAction` 的正文，**一字未改**，只是不再在那里按 backend 分岔
    ///（DEC-100①b：那个 `if (backend == llama) …` 之后整段是 Strata 流程，Ninfer 的模型进来会去拉 Strata 引擎）。</summary>
    void StrataModelAction()
    {
        var m = SelectedModel();
        if (m is null) return;
        bool loaded = _process is { HasExited: false };
        if (loaded)
        {
            // DEC-141：跑着哪件就卸哪件，不再要求下拉正指在那件上。以前跑 A 选 B 时这枚按钮写「加载」，
            // 点下去只会吃到 StartService 第一行那句「服务已经在运行。」= 死胡同（现在按钮直接给「卸载」）。
            var t = UnloadTagFor(_runningTag, m.Tag);
            LogS("$ 卸载模型 " + t + "（网关保留，下次 /v1 请求会自动加载）");
            StopServer();
            return;
        }
        if (!loaded && _externalRunning)
        {
            // DEC-141：外部实例也算"本模块有东西在跑"，这枚按钮就写着「卸载」—— 点它卸的**就是它**
            //（确认后结束外部服务）。以前这里"结束它顺手加载选中件"，与按钮文字不符；要加载再点一次
            //（那时按钮已变「加载」）= 手动切件两步，与 DEC-141「不替用户停旧起新」的口径一致。
            StopExternalStrata();
            return;
        }
        if (StartService(m.Tag, _config.Current.OpenBrowser, wakeFromRecord: false, out var err)) return;
        Dialogs.Message(this, "无法启动", err, danger: true);
        LogS("启动失败: " + err);
    }

    // —— 模型行那枚按钮的文字（纯函数 StartStopTextFor* / ModelActionTextFor* 的喂参数口）——
    // DEC-141：三页都只喂"本模块有没有东西在跑/加载"，不再问"跑的是不是下拉选中的那一件"
    string StrataModelActionText()
        => ModelActionTextForStrata(_process is { HasExited: false }, _externalRunning);

    string LlamaModelActionText() => ModelActionTextForLlama(_llamaOp, _llamaLoaded.Count > 0, _routeModeLlama);

    // —— 「打开网页」用的 URL ——
    // 两处都取「实际在跑的那个」端口：端口设置改过但没重启时，监听还留在旧端口上——用设置值会开到空地方
    // 或开到网关头上（用户实跑 2026-10-01：llama-server 因端口被占没起来，「打开网页」跳到了网关，看着像
    // "llama.cpp 打不开 WebUI"）。
    string StrataWebUrl()
    {
        var port = StrataGateway?.Port ?? PublicPortValue();
        if (StrataGateway is not null && StrataGateway.Port != PublicPortValue())
            LogS($"（网关仍监听着旧端口 {StrataGateway.Port}：改过的公开端口 {PublicPortValue()} 要「停止」再「启动」网关才生效）");
        return $"http://{ProbeHost()}:{port}/";
    }

    string LlamaWebUrl()
    {
        if (!LlamaRunning && !_llamaExternal)
            LogL("llama-server 没在跑：先点左栏「启动」（或模型行「加载」），起了再打开就是 WebUI");
        var port = LlamaRunning ? _llamaActivePort : LlamaPortValue();
        return $"http://{ProbeHost()}:{port}/";
    }

    // —— 「打开配置」= 打开**这一击真正落盘的那一份**（DEC-077 定的口径，DEC-079 把 llama 那一支改成"就是家底本身"，
    //    DEC-122② 把 Strata 这一支改成开壳 ini；**2026-10-22 再改两次（DEC-206 → DEC-211）**）——
    //   Strata = **`data\config\strata\strata_models.ini` 这一份文件**：一个模型一段，段里就是界面那 16 格 +
    //            他手加的 `Args1..ArgsN`（引擎实读的产物在旁边的 `run\` 子目录，不给入口 —— 那两份每次启动重建）。
    //            壳自己的 `MultiLauncher.ini` 从这里退出：它搬到设置页「通用 → 路径」那一行（他点名）。
    //   llama  = data\config\llama\llama_models.ini（取消融合层之后界面改的就是它、引擎读的也是它）
    // 返回空路径时 note 就是给用户看的说明。
    (string Path, string Note) StrataConfigTarget()
    {
        var s = _config.Current;
        var tag = SelectedTag();
        if (tag.Length == 0) tag = s.Model;         // 下拉还没值时退回已保存的选中项
        var dir = RunConfigDir();
        if (!Directory.Exists(dir))
            return ("", "还没有配置目录：" + dir + "（点一次「启动」就会生成；界面上改模型参数点「保存」也会写进去）");
        // 2026-10-22（DEC-211，他点名）：**直接开 `strata_models.ini` 这一份文件**，不再开目录 ——
        // 目录里剩下的产物与伴生都在 `run\` 里、本来就不该他碰，开目录反而要多走一步。
        // 形状与 llama / Ninfer 那两支对齐（开各自的家底 ini，说明只留一句）。
        var store = ModelParamsPath();
        if (!File.Exists(store))
            return (dir, "还没有 " + ModelParamsFileName + "（点一次「启动」或界面上「保存」就会生成）：" + dir);
        return (store, "改完点「停止」再「启动」就生效（后台会重读这份文件）。");
    }

    (string Path, string Note) LlamaConfigTarget()
    {
        var ini = RouterIniPath();
        if (!File.Exists(ini)) return ("", $"找不到模型配置：{ini}（llama 侧的模型与参数都住在这份文件里）");
        // DEC-092（用户 2026-10-06 第 1 条）：llama 侧不补一句说明 —— 打开的就是引擎读的那一份本身，
        // 「保存」写哪一段、备份留几份、去哪儿恢复都写在设置页与 docs/.ai/参考-界面与参数对照.md 里了，日志里再抄一遍只是噪音。
        return (ini, "");
    }

    // —— 「打开配置」用的落点（DEC-122② 之后每条链只剩这一个入口判据）——
    // 这一支以前还有一条「打开前先重物化产物」（`ModuleView.Materialize` + 三个实现 + Program.MaterializeActiveConfig），
    // 打开目标改成 `MultiLauncher.ini` 之后那一步没有读者了（开的不是产物，就不该为一个"看一眼"的动作去写文件），
    // 整套退役 —— 无调用点不留死代码（先例 = DEC-110② 的 NoteRow）。
    // 产物照旧由每次启动（手动 / 唤醒）重建；要看就在设置页 Strata 节「模型配置目录」那枚「打开」里直达。

    // ---------------- Ninfer 那一节的成对实现（DEC-096） ----------------
    // 与上面 Strata* / Llama* 同一形状：视图只喂本模块的实时值、只转发动作，不存状态、不写文件、不起进程。

    void NinferPushCard2(Settings s)
    {
        _txtModelName.Text = s.NinferModel;               // 段名 = 对外 model-id（只展示，不可编辑）
        _txtContext.Text = s.NinferCtx;                   // --max-context（卡2 那一格本页可编辑）
        SelectWithFallback(_cboKvNinfer, s.NinferKvDtype); // --kv-dtype（表外写法补成一项再选中，不静默丢）
        // DEC-107：Ninfer 也给了「附加参数」格（他批复；取代 DEC-083 第 6 条"不给"）——住 [Current]、
        // 起服务时追加到命令行末尾（不写进段），护栏判的是同一把最终 argv，没有"绕过护栏"的缝。
        _txtExtra.Text = s.NinferExtraArgs;
        AfterCard2Push(s);
    }

    void NinferCollectCard2(Settings s)
    {
        s.NinferModel = SelectedTag();
        s.NinferCtx = _txtContext.Text.Trim();
        s.NinferKvDtype = _cboKvNinfer.Selected?.Tag as string ?? "";
        s.NinferExtraArgs = _txtExtra.Text.Trim();        // DEC-107：命令行追加（不进段里的 args）
    }

    void NinferTips()
    {
        Tip.Set(_txtModelName, "Ninfer 侧：段名就是对外 model-id（客户端请求里填的那个名）；只展示，不可编辑。" +
                                (_ninferSectionFile.Length > 0 ? "\n件：" + _ninferSectionFile : ""));
        Tip.Set(_txtContext, "Ninfer 侧 = 这一段 args 里的 --max-context；改了写进那一段（留空 = 摘掉这枚旗标）");
        Tip.Set(_cboKvNinfer, "Ninfer 侧 = 这一段 args 里的 --kv-dtype；swift 件不能配行粒度那几档（起前会拦）");
        Tip.Set(_txtPortNinfer, "公开端口（本族默认 8068）：客户端连的就是它。改这里 = 所有客户端配置都要跟着改，所以改值会先弹一次确认");
        Tip.Set(_cboNinferReasoning, "思考深度（--default-reasoning-effort）：取值表按后端给；WSL 那代只有 low / medium / xhigh，" +
                                     "空白 = 写字面上不写（这一代空白档另有 --no-thinking 那条路，见说明）");
        Tip.Set(_txtExtra, "原样追加到 ninfer 起服务命令行末尾（按空白切分），用于壳未提供的旗标；留空即不追加、不写进段里的 args");
        RefreshNinferTips();
    }

    string _ninferSectionFile = "";      // 「模型名称」那格 tooltip 用的件路径（argv 身份块删掉之后，路径就住在这里）

    /// <summary>切页/重扫后按选中的那一段补载一次家底（口径同 llama 的 EnsureLlamaRecordLoaded：
    /// 视图认的是"用户的手"，没点保存的改动不能被现读顶掉）。</summary>
    void EnsureNinferRecordLoaded()
    {
        var tag = SelectedTag().Length > 0 ? SelectedTag() : _config.Current.NinferModel;
        if (tag.Length == 0 || string.Equals(_ninferLoadedTag, tag, StringComparison.OrdinalIgnoreCase)) return;
        ApplyNinferRecord(tag);
    }

    // DEC-141：引擎在跑（哪一件都行）就给「卸载」，点下去停的是实际在跑的那一件
    string NinferModelActionText() => ModelActionTextForNinfer(_ninferOp, NinferRunning);

    /// <summary>网关那一条路的等 ready：marker 已经把它点亮（_ninferReadyTcs），这里只等。</summary>
    async Task WaitNinferReadyAsync()
    {
        var ready = _ninferReadyTcs;
        if (ready is not null) await ready.Task;
    }

    /// <summary>切件前先停旧的：等**那一个**进程真退出 + 端口不再 LISTENING（实测 ≤ 0.2 秒，别睡固定秒数）。
    /// 不等就会撞上还占着的端口（这条链的口径同 Strata 的 StopUpstreamAndWait）。</summary>
    async Task NinferStopUpstreamAndWait()
    {
        var old = _ninferProcess;
        var port = _ninferActivePort;
        UiPost(StopNinferServer);
        if (old is not null)
        {
            try { await Task.Run(() => old.WaitForExit(20000)); } catch { /* 已经退了 */ }
        }
        if (port > 0) await WaitPortReleasedAsync(port);
    }

    /// <summary>立好网关之后：引擎原本占着公开端口 → 重启到内部端口，否则端口会撞（同 Strata 那一支）。</summary>
    void NinferGatewayStarted()
    {
        if (_ninferProcess is { HasExited: false })
        {
            LogN($"引擎原本占着公开端口：按网关模式重启到内部端口 {NinferInternalPortValue()}");
            var tag = _ninferRunningTag;
            StopNinferServer();
            _ = Task.Run(async () =>
            {
                await WaitPortReleasedAsync(NinferPublicPortValue());
                UiPost(() => { if (!IsDisposed) StartNinferServer(tag, wakeFromSection: true); });   // DEC-102 起接管的是"原来跑的那一件"；DEC-135：自动那条路不读界面卡
            });
        }
        else UiPost(() => RefreshStatusUi());
    }

    /// <summary>收了网关：这一族**没有"引擎直接占公开口"那一档**（只留按需唤醒，DEC-083 第 4 条），
    /// 引擎那把 `--port` 恒由 `NinferInternalPortValue()` 定 → 照 Strata 那样"搬回公开口重启"既搬不动也是白重启一次
    ///（DEC-100①：网关从没被立过，这条分支从来没跑过；接上派发之后它会在每次「停止」时误杀引擎）。</summary>
    void NinferGatewayStopped() => UiPost(() => RefreshStatusUi());

    /// <summary>起网关前把内部端口纠正成能用的值（等于公开口 = 网关和引擎抢同一个口，必须挡住）。
    /// 落盘走非提交式那条路（DEC-092：这不是"用户点了保存"）。</summary>
    void FixNinferInternalPort()
    {
        var pub = NinferPublicPortValue();
        var raw = _config.Current.NinferInternalPort.Trim();
        var want = NinferInternalPortValue();
        if (raw == want.ToString()) return;
        LogN($"Ninfer 内部端口「{raw}」不能直接用（要避开公开端口 {pub}，且在 1-65535）：已改为 {want}");
        _config.Current.NinferInternalPort = want.ToString();
        SaveConfig(commitParams: false);
    }

    /// <summary>网关的 /v1/models 要看得见**全部三件**（两个后端的段都列；与 Strata 那条"只列已就绪"相反）。
    /// 模块整体停用时不列（全隐身）。</summary>
    List<ModelEntry> AllNinferModels() => ModuleRegistry.NinferEnabled(_config.Current) ? _ninferModels : new List<ModelEntry>();

    string NinferWebUrl()
    {
        LogN("这一族没有引擎自带的网页界面（/docs 与 /stats 实测都是 404）：要看的都在日志与底部状态栏里");
        return $"http://{ProbeHost()}:{(NinferRunning ? _ninferActivePort : NinferPublicPortValue())}/";
    }

    (string Path, string Note) NinferConfigTarget()
    {
        var ini = NinferIniPath();
        if (!File.Exists(ini)) return ("", $"找不到模型配置：{ini}（Ninfer 侧的模型与参数都住在这份文件里）");
        return (ini, "");        // 同 llama：开的就是引擎读的那一份本身，不补说明（DEC-092 第 1 条的口径）
    }

    void NinferToggleRouteMode()
        => LogN("Ninfer 只有「按需唤醒」这一档：壳守公开端口、没请求就停服还显存、下次请求自动起（服务常驻那一档已被否）");

    // ============ DEC-100 自检接缝：动作到底挂在哪个方法上（不跑流程、不占端口、不碰显存） ============
    // 为什么断"委托指向哪个方法"而不是真点一次：真点一次要绑公开口、要把 14 GB 的件读进显存，
    // 那是他的实跑判据（AGENTS：修复结果须由用户验证）；而这一条要防的是"派发又挂回旧方法"这种回归
    //（BUG-039 / BUG-041 同族：机制都在，就是没接上派发）。

    /// <summary>各模块「启动」与「加载」挂在哪个方法上（方法名）。自检按名字断，别按"跑一次看效果"断。</summary>
    internal (string StartTarget, string ModelTarget) ServiceActionTargetsForTest(string key) =>
        (View(key).StartService.Method.Name, View(key).ModelAction.Method.Name);
}
