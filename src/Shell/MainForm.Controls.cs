using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace MultiLauncher;

// MainForm 的界面控件字段（左栏 + 三卡片 + Ninfer 那一整套）。
// 2026-10-15 从 Program.cs 整段搬出，一行未改；装配见 MainForm.Ui.cs / MainForm.UiCards.cs。
public partial class MainForm
{
    // ---------------- 界面控件（新布局：左栏 + 三卡片 + 底部大日志）----------------
    // 装配见 MainForm.Ui.cs / MainForm.UiCards.cs；尺寸一律走 UiTheme.S()。
    // 左栏
    readonly FlatButton _btnOpenWeb = new() { Text = "打开网页" };   // B 段起住参数卡右侧按钮列
    readonly FlatButton _btnStartStop = new() { Text = "启动" };
    // 原「运行状态」标题行右侧的 _lblRunState 已删（2026-10-04）：它和下方「状态」键值行是同一串字，
    // 合并后由 _kv["状态"] 承担（字体 8.5pt + 状态色，见 RefreshLamp）。
    readonly Dictionary<string, Label> _kv = new();
    // 启动模式：点左栏那根模式条切换（原来在卡片1 用下拉，第四轮之后挪到这里，卡片1 少一项 → 窗口能收窄）。
    // B 段（DEC-025）：模式条按模块各自记忆；DEC-096（DEC-090 的落地）：它换成与卡1 后端方块**同一个公共控件**
    // StackedToggle（当前枚 + 正下方露一条边 + 180ms 轮换动画），**形状仍通栏**（侧栏只有 S(210)，塞方块右边会空 148 逻辑）。
    readonly StackedToggle _btnMode = new() { Band = true };
    bool _routeModeStrata = true;               // Strata 的路由模式（壳网关）
    bool _routeModeLlama = true;                // llama.cpp 的路由模式（引擎原生）
    readonly RoundedTextBox _txtApi = new() { ReadOnly = true, TabStop = false };

    // 主区标题行（B 段：模块切换 = 页头分段控件；打开配置 / 设置 在右）
    readonly SegmentedControl _segModules = new();
    readonly FlatButton _btnOpenConfig = new() { Text = "打开配置" };
    readonly FlatButton _btnSettings = new() { Text = "设置", ShowGear = true };   // 齿轮是矢量画的（字体里的 ⚙ 会走 emoji 字形）

    // 卡片1：监听地址 / 监听端口 / 保存（2026-10-04 DEC-073：预设已拆，「保存」改义为"保存界面参数修改"）
    readonly RoundedTextBox _txtHost = new();
    readonly RoundedTextBox _txtPort = new();          // Strata 页「监听端口」= 公开端口（网关/服务的对外口）
    readonly RoundedTextBox _txtPortLlama = new();     // llama 页「监听端口」= llama-server 实际端口（2026-10-02 从设置页挪出）
    readonly FlatButton _btnSave = new() { Text = "保存" };

    // 卡片2：通用项（4 个）+ 「高级」（B 段：第三格 = KV 精度；指定显卡挪进了 Strata 高级区；
    // 「打开网页」挪到本卡右侧按钮列、在「高级」上方。
    // DEC-079：llama 侧这一格从"只读展示"换成组合下拉 _cboKvLlama（6 档 + 空白在首），与 Strata 的 _cboKv 同格互斥可见）
    readonly RoundedTextBox _txtModelName = new();
    readonly RoundedTextBox _txtContext = new();
    readonly DarkSelect _cboKvLlama = new();                   // llama 侧 KV 精度 = cache-type-k / cache-type-v（DEC-079 起可编辑、写回该段）
    readonly RoundedTextBox _txtExtra = new();
    readonly RoundedTextBox _txtGpu = new();                           // 指定显卡（B 段起挪进 Strata 高级区）
    readonly FlatButton _btnAdv = new() { Text = "◂ 高级" };
    readonly Panel _advHost = new() { Height = 0, Margin = new Padding(0) };   // 不用 Dock：位置/尺寸由卡片 Layout 算（Dock=Top 会和上面的参数行重叠）
    readonly Panel _advStrata = new() { Dock = DockStyle.Top };
    readonly Panel _advLlama = new() { Dock = DockStyle.Top };
    // （原 `bool _selBackendLlama`：第 0 轮 DEC-094 删掉 —— "高级区摆哪一套"改由模块表决定，见 ShowAdvancedForActiveModule）

    // 高级项（Strata）
    readonly DarkSelect _cboKv = new();
    readonly DarkSelect _cboVision = new();
    readonly DarkSelect _cboReasoning = new();               // 思考深度（2026-10-04）：none/low/medium/high，写共享设置文件
    readonly RoundedTextBox _txtSpec = new();
    readonly RoundedTextBox _txtSpecMinP = new();
    readonly RoundedTextBox _txtExpertCache = new();
    readonly RoundedTextBox _txtPrefill = new();
    readonly RoundedTextBox _txtVramReserve = new();
    readonly RoundedTextBox _txtLayerSplit = new();
    readonly RoundedTextBox _txtArenaPin = new();
    // （原 _swLowRam「低内存模式」开关：2026-10-04 DEC-075 整链删除）
    readonly ToggleSwitch _swKvRes = new();
    readonly ToggleSwitch _swHeadLate = new();

    // llama.cpp 高级项（DEC-079 起：这些值**住在 llama_models.ini 选中模型那一段里**，
    // 读 = 段 → `[*]`/文件头 → 引擎默认，写 = 只写变过的键；原「合并 ini 覆盖每个段」那一条已退役）
    readonly RoundedTextBox _txtTemperature = new();
    readonly RoundedTextBox _txtTopP = new();
    readonly RoundedTextBox _txtTopK = new();
    readonly RoundedTextBox _txtMinP = new();
    readonly RoundedTextBox _txtPresencePenalty = new();
    readonly RoundedTextBox _txtRepeatPenalty = new();
    readonly RoundedTextBox _txtSpecDraftNMax = new();
    readonly DarkSelect _cboSpecType = new();                 // llama 高级区「投机方式」= spec-type（DEC-145）
    readonly DarkSelect _cboMmproj = new();                   // llama 高级区「视觉」= mmproj-offload 的 GPU / CPU / 空白（DEC-145）
    // flash-attn（DEC-079，用户口径「fa 只做开关，不做下拉，只有开关 2 态」）：两态、没有空白档，
    // 所以动过它就一直写在这一段里（要回到"跟随全局"得自己在 ini 里删那一行，或从设置页恢复备份）。
    readonly ToggleSwitch _swFlashAttn = new();
    // 思考深度（2026-10-04；DEC-076 起下拉）：合并 ini 的 reasoning-effort——取值按官方帮助
    // default / minimal / low / medium / high / xhigh / max，另有**空白项 = 留空**（不覆盖你 ini 里的值）。
    // 原「并行数」（--parallel）已撤下（DEC-069）：parallel 不再由壳覆盖。
    readonly DarkSelect _cboReasoningEffort = new();

    // 卡片3：模型 / 加载·卸载 / 清屏
    readonly DarkSelect _cboModel = new();
    readonly FlatButton _btnModelAction = new() { Text = "加载" };
    readonly FlatButton _btnClearLog = new() { Text = "清屏" };
    readonly Label _lblHint = new() { AutoSize = true, Visible = false, ForeColor = Color.DarkOrange };

    // ================= Ninfer（DEC-096）=================
    // 卡1 那两格 + 卡1 左侧的后端方块 + 卡2 的 KV 下拉 + 高级区那一整套（内容跟后端换）。
    // 上下文与模型名称复用两页共用的 _txtContext / _txtModelName（值由 NinferPushCard2 推、NinferCollectCard2 收）。
    readonly RoundedTextBox _txtPortNinfer = new();          // 「监听端口」= NinferPort（公开口；可编辑 + 失焦确认，DEC-089/091）
    readonly DarkSelect _cboNinferReasoning = new();         // 「思考深度」= --default-reasoning-effort / --no-thinking（取值按后端各一张）
    readonly DarkSelect _cboKvNinfer = new();                // 「KV 精度」= --kv-dtype（与 _cboKv / _cboKvLlama 同格互斥）
    readonly StackedToggle _backendToggle = new() { Band = false };   // 卡1 左侧那枚方块（WSL / WIN 轮换）
    readonly Panel _advNinfer = new() { Dock = DockStyle.Top };
    // 这几档"选项按后端各一张"（DEC-086：WSL 没有 dflash2、WIN 的池只有三档），用同一只下拉换项表就好：
    // 段里写过的表外值由 SelectWithFallback 补成一项再选中，不静默丢（DEC-076 口径）。
    readonly DarkSelect _cboNinferPool = new();             // 设备池 --kv-capacity
    readonly DarkSelect _cboNinferPrefill = new();          // 分块预填充 --prefill-chunk
    readonly DarkSelect _cboNinferHostKib = new();          // 主机缓存 --host-kv-mib（只 WIN）
    // DEC-107（他第 2 条）：投机方式 / 投机深度的下拉改输入框（除空白外只一档的下拉不值得占一个展开动作；
    // 深度两档也一并纳入）。取值仍由起前校验拦：WSL 只认 mtp/dflash、WIN 只认 dflash2、深度须 ≥1 整数。
    readonly RoundedTextBox _txtNinferDraft = new();         // 投机深度 --draft-tokens（WIN 那侧）
    readonly RoundedTextBox _txtNinferSpec = new();          // 投机方式 --spec（WinSpecs / WslSpecs 当校验白名单）
    readonly RoundedTextBox _txtNinferMaxTokens = new();    // 输出上限 --default-max-tokens
    // （共享前缀上限 --max-shared-prefixes 整格去掉 —— DEC-107 他第 1 条。旗标照旧从段里透传，
    //   起前"必须 0"的护栏仍在，判的是实际命令行）
    readonly RoundedTextBox _txtNinferTemperature = new();   // temperature
    readonly RoundedTextBox _txtNinferTopP = new();          // top-p
    readonly RoundedTextBox _txtNinferTopK = new();          // top-k
    readonly RoundedTextBox _txtNinferPresence = new();      // presence-penalty
    readonly RoundedTextBox _txtNinferConcurrency = new();   // 并发数 --max-concurrency（只 WSL 给格；WIN 架构级硬拒 mc>1）
    readonly RoundedTextBox _txtNinferSlots = new();         // 状态槽数 --device-state-slots（只 WSL）
    readonly RoundedTextBox _txtNinferPending = new();       // 排队上限 --max-pending-requests（只 WSL）
    readonly RoundedTextBox _txtNinferPendingSec = new();    // 排队超时（秒）--pending-timeout-ms（只 WSL；界面按秒填，传之前 ×1000）
    // 同一格位两代各一只控件的三档（原型对照表：WSL 是文本框、WIN 是下拉档 —— WSL 那代是全驻留形状，
    // 262144 / 3 / 1024 都不在 WIN 那几张档表里；互斥摆位与高级区的 shown 判据同一套路，值读口 = NinferPoolValue() 那三个）
    readonly RoundedTextBox _txtNinferPoolWsl = new();       // 设备池 --kv-capacity（WSL 文本框）
    readonly RoundedTextBox _txtNinferDraftWsl = new();      // 投机深度 --draft-tokens（WSL 文本框）
    readonly RoundedTextBox _txtNinferPrefillWsl = new();    // 分块预填充 --prefill-chunk（WSL 文本框）
    readonly ToggleSwitch _swNinferLmHead = new();           // 草稿头模式 --lm-head-draft（与 llama.cpp 同名旗标语义相反，tooltip 点明）
    readonly ToggleSwitch _swNinferVision = new();           // 视觉 --vision
    readonly ToggleSwitch _swNinferPreserve = new();         // 保留思考 --preserve-thinking


    // 自检用的结构引用（DumpLayoutRows 按视觉顺序导出）
    Control? _sideCol;
    Control? _pageHead;
    Control? _cardStart;      // 卡1 于 DEC-074 撤、DEC-076 候选（原型）回归
    Panel? _card1Row;         // DEC-182 甲：卡1 那一行 = 后端方块（独立一卡）+ 卡1，左右相邻不叠边
    Control? _cardParams;
    Control? _cardModel;
    Control? _cardLog;

    // 开机自启的注册表值名：沿用"项目名"（2026-10-02 产物改名后依旧不动——改了会孤儿化既有注册、
    // 且它与单实例互斥体同属内部身份，不随显示名/产物名走）
    const string RunValueName = "StrataLauncher";

    readonly NotifyIcon _tray = new();
    readonly TextBox _txtLog = new()
    {
        Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
        Dock = DockStyle.Fill, Font = new Font("Consolas", 9f),
        BackColor = Color.FromArgb(24, 24, 24), ForeColor = Color.FromArgb(200, 220, 200),
        Margin = new Padding(4),
    };

    // 版本号取 csproj <Version>（单一来源）。显示口径（DEC-180，2026-10-15 他点名"整体后移一位 → v0.1.8.1"）：
    // **照 csproj 写几段就显几段** —— 读 `AssemblyInformationalVersion`（SDK 由 <Version> 原样生成，
    // 实跑产物里形如 "0.1.8.1+64ca085f…"，'+' 后面是提交号），在 '+' 处切掉即可。窗口标题 / 托盘 / 关于页 /
    // 自检断言四处同源（都走这一条）。
    // 取代 DEC-175 的「总是三段」：那条按 `Major.Minor.Build` 拼，四段版本会被**静默吞掉末段** ——
    // 实测 `<Version>0.1.8.1</Version>` 下 `AssemblyName.Version` = 0.1.8.1，而三段拼法出来是 "0.1.8"。
    // 万一哪天属性拿不到（关掉 GenerateAssemblyInfo 之类），退回三段拼法，不显示空。
    static string VerNumber()
    {
        var asm = typeof(MainForm).Assembly;
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            int plus = info.IndexOf('+');
            var s = (plus >= 0 ? info.Substring(0, plus) : info).Trim();
            if (s.Length > 0) return s;
        }
        var v = asm.GetName().Version;
        return v is null ? "0.0.0" : $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}";
    }

    internal static string VersionTag() => "v" + VerNumber();

    public MainForm(bool startHidden = false)
    {
        _startHidden = startHidden;
        Text = AppName + "  " + VersionTag();
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 9f);
        // 必须 None：尺寸统一走 UiTheme.S()，两套缩放叠加会让控件在 125%/150% 下错位（实测过）
        AutoScaleMode = AutoScaleMode.None;
        // 先建句柄：DeviceDpi 要等 WM_DPICHANGED 进消息循环才准，所以改用 GetDpiForWindow 同步捕获一次
        CreateControl();
        UiTheme.CaptureDpi(Handle);
        // 窗口尺寸：默认 960×860、最小宽同值。
        // 970 是 DEC-011 按卡片1 六项反算的；870 是 DEC-017 去掉「启动模式」后的实测收窄；
        // B 段（DEC-025）卡1 端口格改全称「监听端口」（牌子长了约 28px），实测：144 DPI 需 870、96 DPI 会话需 884 → 取 900 留约 10px 余量。
        // DEC-096（用户拍板 900 → 940）：Ninfer 页卡2 一行三格里「模型名称」要吃余量放 model-id，
        //   出图台实测框宽 149 / 文字可用 125 / 需要 154（Consolas 9pt 的 22 字 qwen3.8-27b-swift-iq3s）→ 缺 37 逻辑；
        //   DEC-088 当年记的「217 vs 需 170 → 富余 47」是拿 mock 手填的格宽算的，没扣标签与内缩（经验库 §32 那条又应验一次）。
        //  三页同一个窗：另两页最小客户宽 885、Ninfer 页 889，一起按 940 摆，切页不跳宽。
        // DEC-107（940 → 960）：这一批两处把内容撑宽了 —— 左栏内宽 178→200（+22）、Ninfer 卡1 端口/思考深度
        //   改用另两页那两条算式（+约 32，地址格的下限从 S(108) 压到 S(96) 兜回 12）。自检三页各量一遍
        //   实测最小客户宽 954（`MinClientWidthAllPagesForTest`），940 已经兜不住 → 默认与最小一起到 960。
        ClientSize = new Size(UiTheme.S(this, 960), UiTheme.S(this, 860));
        // MinimumSize 是**窗口**尺寸、ClientSize 是**客户区**：补回框架宽度（原来两者取同一个数会把边框算进最小客户宽）。
        int frameW = Width - ClientSize.Width, frameH = Height - ClientSize.Height;
        MinimumSize = new Size(UiTheme.S(this, 960) + frameW, UiTheme.S(this, 560) + frameH);
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
        catch { /* default icon */ }

        _cboKv.SetItems(new[]
        {
            new DarkItem { Text = "int8", Tag = "int8" },
            new DarkItem { Text = "q4_0", Tag = "q4_0" },
            new DarkItem { Text = "k8v4", Tag = "k8v4" },
        });
        // KV 精度（llama.cpp，DEC-079）：组合下拉，一项写 cache-type-k + cache-type-v 两键；
        // **空白在首 = 把这两键从这一段删掉**（= 跟随 `[*]`/引擎默认），表外组合由 SelectWithFallback 补项
        _cboKvLlama.SetItems(LlamaKvItems());
        // 「投机方式」（DEC-145）：空白在首 = 删键回继承；表外值（如 ngram-*）由 SelectWithFallback 兜底
        _cboSpecType.SetItems(LlamaSpecTypeItems());
        // 「视觉」（DEC-145）：照 Strata 那格的形状 —— 空白 = 跟随全局，实档就 GPU / CPU 两枚
        //（"关闭"档刻意不做：引擎的 --no-mmproj 拦不住显式 mmproj 路径的加载，做了是假开关，见 DEC-144 §8）
        _cboMmproj.SetItems(new[]
        {
            new DarkItem { Text = "", Tag = "" },
            new DarkItem { Text = "GPU", Tag = "true" },
            new DarkItem { Text = "CPU", Tag = "false" },
        });
        _cboVision.SetItems(new[]
        {
            new DarkItem { Text = "关闭", Tag = "none" },
            new DarkItem { Text = "GPU", Tag = "gpu" },
            new DarkItem { Text = "CPU", Tag = "cpu" },
        });
        // 思考深度（Strata，2026-10-04；DEC-076 起首项是**空白**——用户口径"留空不就行了"）：档位与聊天页
        // 的 Thinking 同一套词（none/low/medium/high）；空白 = 留空（不写共享文件）
        _cboReasoning.SetItems(new[]
        {
            new DarkItem { Text = "", Tag = "" },
            new DarkItem { Text = "none", Tag = "none" },
            new DarkItem { Text = "low", Tag = "low" },
            new DarkItem { Text = "medium", Tag = "medium" },
            new DarkItem { Text = "high", Tag = "high" },
        });
        // 思考深度（llama.cpp）：取值按官方帮助（server README：--reasoning-effort）——default / minimal / low /
        // medium / high / xhigh / max；首项空白 = 留空（不覆盖你 ini 里的值，选了也能回得去）
        _cboReasoningEffort.SetItems(new[]
        {
            new DarkItem { Text = "", Tag = "" },
            new DarkItem { Text = "default", Tag = "default" },
            new DarkItem { Text = "minimal", Tag = "minimal" },
            new DarkItem { Text = "low", Tag = "low" },
            new DarkItem { Text = "medium", Tag = "medium" },
            new DarkItem { Text = "high", Tag = "high" },
            new DarkItem { Text = "xhigh", Tag = "xhigh" },
            new DarkItem { Text = "max", Tag = "max" },
        });
        // 第 0 轮（DEC-094）：模块能力表必须在 BuildUi 之前建好 —— 卡片摆位与提示都在建控件时就问 ActiveView。
        BuildModuleViews();
        BuildUi();
        LoadConfig();
        // DEC-092：这份就是"磁盘上那一份参数"的起点 —— 之后只有「保存」/关窗「写进去」会更新它（CommitSavedParams）。
        // 非提交式的落盘（切页之外的顺手保存、更新链记跳过位、关窗记窗口位置）都按它写参数格，
        // 所以"改了没点保存"不会被别的路径偷偷存住。
        MirrorSavedParams();
        // 改名只发生一次（DEC-037）：说清旧文件没了、内容原样，避免用户以为配置丢了
        if (ConfigRenamed) Log($"配置文件已更名：{LegacyConfigFileName} → {ConfigFileName}（内容原样，旧文件不再保留）");
        // 模块开关（B 段地基，DEC-024）：ini 被手改成全关时兜底至少启用一个；有停用项时打一行明说
        if (ModuleRegistry.EnsureAtLeastOne(_config.Current))
            Log("模块开关全被禁用：已兜底启用 Strata（至少要启用一个模块）");
        if (!ModuleRegistry.All.All(m => m.Enabled(_config.Current)))
            Log("模块状态：" + string.Join("、", ModuleRegistry.All.Select(m =>
                m.DisplayName + (m.Enabled(_config.Current) ? " 启用" : " 停用（全隐身：不扫描 / 不探活 / 不起服务）"))));
        // B 段：恢复上次激活的模块页（第 0 轮 DEC-094：照模块表认键 —— ini 里那个值必须是**已知且启用**的键，
        // 否则落回表里第一个启用的模块；以前这里是 `== "llama" ? "llama" : "strata"` 那种二元写法）
        _activeModule = ModuleRegistry.All.Any(m => string.Equals(m.Key, _config.Current.ActiveModule, StringComparison.OrdinalIgnoreCase))
            ? _config.Current.ActiveModule : ModuleRegistry.All[0].Key;
        EnsureActiveModuleValid();
        // DEC-116 + DEC-118：老落点一次性搬家（两份家底 → data\config\<模块>\；data\run → data\config\strata；
        // 配置备份 → data\backup\<模块>\；Strata 整份快照 → 与 Strata 同级的 Strata-backup）。
        // **必须在下面那两步"缺才拷"之前** —— 不然先在老位置补种一份，搬家就变成"两边都有 = 不覆盖"，白留一份误导人。
        // 规矩照 DEC-084：Move 不是复制、两边都有不覆盖并点名、建不出来就什么都不动。
        foreach (var note in MigrateLauncherLayout(_config.Current.Root))
            Log("布局搬家：" + note, "launcher");          // 跨模块的家事 → 走 launcher 通道（三页都显示，正是想要的）
        // A/C（2026-10-22，DEC-206 / DEC-207）：`[Model:<tag>]` 那一族的家搬到
        // `data\config\strata\strata_models.ini`（一个模型一段：16 格 = 界面参数、`ArgsN` = 手加参数，DEC-210）。
        // **次序是硬要求**：必须在下面任何一次 SaveConfig 之前 —— 写侧从此不再写 `[Model:*]` 段，
        // 先存一次就把老段抹了（= 迁移还没跑、数据先没了）。放在布局搬家之后：那时 RunConfigDir 才是最终位置。
        // 自检/探针里一律不跑（与布局搬家同一道按进程闸）：搬家类真路径不许被一次 --selftest 碰，
        // 判据走 data\tmp 里的副本（见 SelfTest 的 ModelSnapFileChecks）。
        if (!LayoutMigrationDisabledForTest)
        {
            var snapNotes = new List<string>();
            var (fromIni, fromFiles, fromLegacy) = ReconcileModelSnaps(_config.ModelSnaps, snapNotes);
            foreach (var n in snapNotes) LogS(n);
            if (fromIni > 0)
                LogS($"已把 {fromIni} 份模型参数从 MultiLauncher.ini 搬到 " + DisplayDataPath(RealRunConfigDir())
                     + "\\" + ModelParamsFileName + "（老 [Model:*] 段这次保存后不再写；文件赢、老段只在文件不存在时读一次）");
            else if (fromFiles > 0)
                LogS("模型参数从 " + DisplayDataPath(RealRunConfigDir()) + "\\" + ModelParamsFileName + " 读入（" + fromFiles + " 个模型）");
            if (fromLegacy > 0)
                LogS($"已把 {fromLegacy} 份旧的每模型一份配置并进 " + ModelParamsFileName
                     + "（旧文件已存进 " + DisplayDataPath(ModelUiBackupDir()) + "）");
            // DEC-208：产物 + 引擎伴生文件**藏进 run\ 子目录**（用户手改不到它们；两者必须同目录，
            // server.py:7086 按 --config 的路径派生伴生文件）。一次性搬，两边都有就不覆盖并点名。
            foreach (var note in MigrateProductsIntoRunDir()) LogS(note);
        }
        // DEC-124：整份快照归位到 D:\Strata-data\backup\ 那一跳是**复制、不删老那份**（他的原话：旧的位置不要删除，
        // 留着我自己删，并要提醒我）。所以这条提醒每启动写一次，直到他自己把老位置清空为止；空壳不算（判据 = LegacySnapshotsPending）。
        // 真正干活的那一趟在窗开起来之后的后台（几百 MB 到 1 GB 的复制，别挡开窗）。
        if (!LayoutMigrationDisabledForTest)
        {
            var oldSnap = LegacySnapshotsPending(LauncherDataDir(), _config.Current.Root);
            if (oldSnap is not null)
                Log("升级快照搬家：老位置还留在 " + DisplayDataPath(oldSnap) + "（程序不替你删）—— 确认 " +
                    DisplayDataPath(CurrentStrataBackupRoot()) + " 里那几份齐了之后，自己删掉老的那些", "launcher");
        }
        // DEC-079（取消融合层）：llama 的家底搬到实跑目录 —— **缺才拷**，已经有就一个字不动。
        // DEC-084：改名（router_models.ini → llama_models.ini）就在这一步之前做，**搬进备份后 Move、不留第二份**。
        // 种子按次序：① 旧 ini 里那条已退役的 RouterIni 路径（那才是用户一直在改、引擎一直在读的一份）；
        // ② 开发目录（bin / publish）往上找仓库里 config\llama_models.ini（旧名那份兜底）。源文件照旧只读不写。
        if (!EnsureRouterIni(out var seedFrom, out var migrated, out var migratedBackup))
            LogL("没找到 llama_models.ini —— llama 侧的模型与参数都住在这份文件里，请把它放到：" + RouterIniPath());
        else
        {
            // DEC-084：旧名那份只有两种下场（改名 / 归档后删掉），两种都各写一行，让用户知道文件动去哪儿了
            if (migrated == "renamed")
                LogL("llama 家底已改名：" + LegacyLlamaIniFileName + " → " + LlamaIniFileName +
                     "（旧名不再保留；改名前先在 " + DisplayDataPath(RealRouterIniBackupDir()) + " 存了一份 " + migratedBackup +
                     "，设置页 llama 节「恢复…」能拿回来）");
            else if (migrated == "archived")
                LogL("老位置同时留着旧名那份（" + LegacyLlamaIniFileName + "）：已存进 "
                     + DisplayDataPath(RealRouterIniBackupDir()) + "\\" + migratedBackup +
                     " 并删掉，引擎与界面用的都是 " + LlamaIniFileName + "（不留两份家底）");
            if (seedFrom.Length > 0)
                LogL("llama_models.ini 已就位：" + RouterIniPath() + "（副本取自 " + seedFrom + "，源文件一个字没动）");
        }
        // DEC-096：Ninfer 的家底同样"缺才拷"（种子 = 仓库 config\ninfer_models.ini，源文件只读）。
        // 它没有旧名搬家那一步（这族文件从来没存在过）。
        EnsureNinferIni(out _);
        // DEC-114：首启一次性迁移 —— 段里那条 `args =` 摊成"一行一键"（形状照 llama 侧那样看得清）。
        // **先全部解析校验、全过了才动笔**：任一段认不出就整份一个字不动，并把卡住的段与那一枚写进日志（不猜、不修一半）。
        // 写之前必备份（钩子挂在 LlamaIni.EditSection 的 onBeforeWrite 上，整套与 llama 侧同一个家）。
        int ninferFlat = MigrateNinferIniToKeys(out var ninferFlatErr, out var ninferFlatDetail);
        if (ninferFlat > 0)
            LogN($"ninfer_models.ini 已摊平：{ninferFlatDetail}（一行一键，形状同 llama_models.ini；" +
                 "改前那份在 " + NinferBackupDir() + "，设置页「改动前自动备份 · 恢复…」可以退回去）");
        else if (ninferFlatErr.Length > 0)
            LogN("ninfer_models.ini 没摊平（**一个字都没动**，界面照旧按原来的 args 读写）：" + ninferFlatErr);
        // 「启动时隐藏到托盘」：任何方式启动都直接进托盘（用户反馈：勾了不管用——原来只在带 --tray 时生效）
        _startHidden = _startHidden || _config.Current.HideToTrayOnStart;
        ApplySettings(_config.Current);
        // DEC-079：老 ini 的 [Current] 里那批"靠合并覆盖每个段"的 llama 参数，一次性搬进当前选中的 llama 段；
        // 搬完立刻落盘 —— 这批键已退役出 [Current]，于是旧键随这一次保存自然消失（同 [Preset:] DEC-073）。
        // 次序要紧：必须在 ApplySettings 之后（控件有值了 CollectSettings 才不会把界面收空）、RefreshModels 之前。
        if (MigrateLlamaParamsIntoSections() > 0) SaveConfig(commitParams: false);
        RestoreWindowBounds();
        SetupTray();
        RefreshModels();
        // DEC-064 一次性迁移：老 ini 把所有模型共用的一份扁平家底记在 [Current] 里（换模型时只有 tag 落盘、
        // 参数只进界面），所以先按各模型「此刻真正生效的那份」= data\config\strata\<tag>.json 各建一份快照；
        // 从没启动过的模型没有这份文件，就回落到它的配置模板。之后快照由每次「保存」维护。
        int seeded = SeedModelSnapsFromRunConfigs(_config.Current, _config.ModelSnaps, _models.Select(m => m.Tag), RunProductDir());
        if (seeded > 0)
            LogS($"已为 {seeded} 个 Strata 模型各建一份参数快照（取自 " + DisplayDataPath(RealRunConfigDir())
                 + " 里该模型此刻生效的运行配置，之后每次「保存」跟着维护）");
        // DEC-209（2026-10-22，他点名「按 2 改」）：**逐模型补齐**。上面那条是 DEC-064 的一次性迁移 ——
        // 只在字典完全为空时跑一次，于是"已有记录"之后新加的模型永远补不上（他报的「5 个模型只有 3 份配置」）。
        // 这条从各自的**配置模板**补（不是从产物：产物是壳自己的输出，反推 = 自洽循环），缺哪个补哪个，
        // ArenaPinGib 留空（模板里那个数分不清来源，抄进去就把"沿用模板"变成钉死）。
        var filledTags = new List<string>();
        if (FillMissingModelSnapsFromTemplates(_config.ModelSnaps, _models.Select(m => m.Tag),
                _config.Current.Root, filledTags) > 0)
        {
            if (SaveModelSnapStore(_config.ModelSnaps, out _, out var fillErr))
                LogS($"已为 {filledTags.Count} 个新模型各建一份参数记录（取自各自的配置模板）：{string.Join("、", filledTags)}");
            else
                LogS($"新模型的参数记录没写进 {ModelParamsFileName}（这次只在内存里，下次保存再试）：{fillErr}");
        }
        RefreshVersions();
        WarnPortClash();        // 存量 ini 里若已有"公开端口=内部端口"的串台值，开机就在日志里说一句
        // 启动时按选中的模型把家底校正一遍（DEC-064：原来只在首次运行做，于是「下拉是 A 模型、上方模型名是
        // B 模型」能一直摆在界面上）。不走 LoadKnobsFromModel —— 那条路按当前下拉选中项判后端，
        // 而启动时可能停在 llama 页（下拉里是 llama 模型）。
        if (!_applying && _config.Current.Model.Length > 0)
        {
            _config.ModelSnaps.TryGetValue(_config.Current.Model, out var snap0);   // 没快照 = snap0 为 null（DEC-082：来源不再写日志）
            ApplyStrataRecord(_config.Current.Model, snap0);
        }
        // DEC-079：llama 页同理按选中的那一段校正一遍（这批键已从 [Current] 退役，
        // 「段 → `[*]`/文件头 → 引擎默认」才是它的家底；顺带把"生效值基线"立起来给「保存」用）。
        // 放在 Strata 那条之后：两条都会重推界面，最后一条决定当前页显示的值与脏判定基线。
        if (!_applying && _config.Current.LlamaModel.Length > 0) ApplyLlamaRecord(_config.Current.LlamaModel);
        // 自动启动必须等窗口句柄建好（构造函数里 BeginInvoke 会抛 InvalidOperationException）。
        // B 段修订（2026-10-02 实跑反馈"设置里关了「自动启动服务 · Strata」还是会自动启动"）：
        // 原来这里有一条「WakeOnRequest=true → 无条件立网关」（"请求唤醒=常驻网关"时代的遗留），
        // 与"每模块一个自动启动开关"的新语义冲突——路由模式下开关关着，打开启动器照样把网关立起来。
        // 现在**自动启动只由开关决定**：StartServer() 在路由模式下自己会先立网关（先占公开端口、
        // 服务退到内部端口，顺序不变），开关关着就什么都不起。
        // B 段（DEC-025）：「自动启动服务」拆成每模块一个；停用的模块不起、也不弹模态框，只记一行
        // 第 0 轮 DEC-094：这里改成**遍历模块表**（加一个模块 = 表里多一行，这里跟着长出一格开关，不用再改这段）
        foreach (var v in _views)
        {
            bool on = v.AutoStart(_config.Current);
            bool enabled = v.EnabledIn(_config.Current);
            if (!on) continue;
            if (!enabled) { v.Log($"「自动启动服务（{v.DisplayName}）」已跳过：{v.DisplayName} 模块未启用"); continue; }
            // DEC-147（BUG-067 结案）：这道闸以前不存在 —— 自检与出图台建的都是真 MainForm，
            // 他 ini 里 AutoStartStrata/Llama=True，探针一 Show() 就可能真去起服务（最坏撞上「结束外部进程」那枚模态确认）。
            if (!ShouldWireAutoStart(on, enabled, AutoStartDisabledForTest)) { AutoStartSkippedForTest++; continue; }
            AutoStartWiredForTest++;
            Load += (_, _) => v.StartService();
        }
        SetAutoStart(_config.Current.AutoStartWithWindows);
        // DEC-061：这里原来有一行 RefreshRollbackState()——那是个只剩 BeginInvoke 空转的空壳，已删。
        // 外部服务巡检：自己没拉起进程时，看端口上是不是已经有一个在跑
        _probeTimer.Tick += (_, _) => ProbeExternal();
        _probeTimer.Start();
        // ④ 底部状态栏（DEC-038）：5 秒一拍，与日志节流窗口同值。这里**不**并入 _probeTimer——
        // 那个 7 秒定时器是 A 段刚验过的探活链路（台账互斥、同族指纹都挂在它上面），动它的节奏等于把已验证的东西重验一遍。
        _statsTimer.Tick += (_, _) => StatsTick();
        _statsTimer.Start();
        Shown += (_, _) =>
        {
            ProbeExternal();
            // DEC-114③：启动兜底扫 data\tmp 的超期残留（实测那层 240 MB 里 232.2 MB 是历次更新的下载/解压残留）。
            // 放后台：那种量删下来要一两秒，摆在构造函数里就是"开窗先卡一下"。
            // 自检与出图台两道闸挡着（ScratchSweepDisabledForTest / LayoutMigrationDisabledForTest，都按进程收口）——
            // 探针实例也走这条 Shown，不加闸就是拿别人的草稿窝与他家底的位置开刀。
            // DEC-121：④ 快照外迁（跨卷 = 真复制几百 MB）跟在这后面同一条后台链，别摆构造函数。
            Task.Run(() =>
            {
                // DEC-202：先看上一次"换掉自己"那一趟的回执（helper 写的 result.txt）——ok 记一行日志，
                // fail 额外给一条 toast 并把手工兜底的路径写清。测试态不读（那不是他机器的账）。
                if (!AppSelfReplaceDisabledForTest) ReadAppUpdateReceipt();
                MigrateSnapshotsAtStartup(); SweepScratchAtStartup();
                // DEC-136（C-5）：WSL 那侧的件清单以前只在"设置页改过 distro / 模型目录 / WSL 开关"时才 find 一次
                // = 正常一次会话里 _ninferWslScanned 恒 false，界面上那句"VM 里没找到这个件"永不触发、
                // 起服务前那条新增的 WSL 判据也永远只能放行。启动后台补这一次（它自己判模块与开关，没开立刻返回）；
                // 测试态一次都不进 VM（DEC-097⑥ 那道按进程的闸）。
                if (!VersionProbeDisabledForTest) RefreshNinferWslArtifacts();
                // DEC-202：本程序自更新 —— 启动后台探一次（每进程一次；失败静默只进日志，启动期不打扰）。
                // 闸门同理：自检 / 出图台一律不联网（那一位取决于上游今天发了什么，不该决定用例红绿）。
                if (!AppUpdateProbeDisabledForTest) CheckAppUpdate(force: false);
            });
            // 开机自启带上 --tray 时只留在托盘，不弹主窗口（设置页「启动时隐藏到托盘」）
            if (_startHidden) Hide();
        };
        UiDark.Apply(this);
    }

}
