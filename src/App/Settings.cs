namespace MultiLauncher;

// 壳自己的设置（MultiLauncher.ini 持久化；旧名 StrataLauncher.ini 首启一次性改名，DEC-037）：只保存界面状态与「本机已存在的可调项」。
// 纪律：壳不改 Strata 项目里的任何文件；可调项在启动时物化成一份自己的运行配置（见 MainForm.Models.cs）。
public class Settings
{
    public string Root { get; set; } = @"D:\Strata";        // Strata 目录（含 .venv / serve / strata-*.json；2026-10-02 起改名去版本号）
    public string StrataUpdateUrl { get; set; } = "https://github.com/Niko1221/Strata";
                                                             // DEC-110④：Strata 那条更新链的上游仓库（网页或 api 地址都吃）。
                                                             // 以前写死在代码里；留空或认不出 owner/repo = 回落到默认那一头（不许因为改坏这格就没法检查）
    public string Model { get; set; } = "";                  // Strata 侧配置 tag，即 strata-<tag>.json
    public string LlamaModel { get; set; } = "";             // llama.cpp 侧选中的段名（B 段：两个模块各自记住选中）
    public string Port { get; set; } = "8080";
    public string Host { get; set; } = "127.0.0.1";          // 127.0.0.1 = 仅本机；0.0.0.0 = 局域网可访问
    public string ApiKey { get; set; } = "";
    public string ApiModel { get; set; } = "";               // API 调用时客户端填的 model 名（config 的 model_name）
    public string Gpu { get; set; } = "";                    // 留空 = 引擎自行选卡；0 = 单卡；0,2 = 多卡拆分（B 段后住 Strata 高级区）
    public string LayerSplit { get; set; } = "";             // 多卡时各后续卡的起始层，留空 = auto
    public string Context { get; set; } = "131072";          // --max-context
    public string Kv { get; set; } = "int8";                 // --kv：int8 / q4_0 / k8v4
    public bool KvResident { get; set; } = false;            // --kv-resident 32768（64K 以上才由 SETUP 默认打开）
    public string Spec { get; set; } = "4";                  // --spec
    public string SpecMinP { get; set; } = "0.5";            // --spec-min-p
    public string ExpertCache { get; set; } = "auto";        // --expert-cache
    public string Prefill { get; set; } = "auto";            // --prefill
    // （原 LowRam/--mmap-experts：2026-10-04 DEC-075 整链删除——界面上没有「低内存模式」了，
    //  ini 里旧键读不认、随下次保存自然消失；配置 args 里自带 --mmap-experts 则原样透传）
    public string Vision { get; set; } = "gpu";              // none / gpu / cpu（模型没配视觉时不可用）
    public string VramReserveMib { get; set; } = "700";      // --vram-reserve-mib
    public string ArenaPinGib { get; set; } = "";            // STRATA_ARENA_PIN_GIB；留空 = 沿用配置里的值（不覆盖），填值才写入
    public bool HeadLate { get; set; } = false;              // STRATA_HEAD_LATE（回旧加载位置，仅 A/B 用）
    public string ExtraArgs { get; set; } = "";              // 原样追加到 Strata 引擎参数末尾
    public string ReasoningEffort { get; set; } = "";        // 共享默认思考档（2026-10-04）：none / low / medium / high；留空 = 不写（聊天页里设的照旧）。写进运行配置同名的 <tag>.shared-settings.json
    public bool OpenBrowser { get; set; } = true;            // 就绪后自动打开网页
    public bool WakeOnRequest { get; set; } = false;         // Strata 的路由模式：壳常驻公开端口、Strata 跑内部端口，请求到达时唤醒/切换模型
    public bool LlamaRouteMode { get; set; } = true;         // llama.cpp 的路由模式（B 段：模式条按模块各自记忆；默认 = 引擎原生路由）
    public string InternalPort { get; set; } = "";           // 内部端口；留空 = 公开端口 + 1
    public string IdleStopMinutes { get; set; } = "10";      // 空闲停服分钟数；留空 = 关（只在路由模式生效）
    public bool AutoStartService { get; set; } = false;      // 遗留字段（B 段拆成每模块一个）；只为迁移旧 ini 保留读入
    public bool AutoStartStrata { get; set; } = false;       // 打开启动器后自动启动 Strata 服务/网关
    public bool AutoStartLlama { get; set; } = false;        // 打开启动器后自动拉起 llama-server
    public bool AutoStartWithWindows { get; set; } = false;  // 开机自启（HKCU Run）
    public bool SaveLog { get; set; } = false;               // 日志同时写入 logs\<模块>.log（按模块分文件）
    // —— 通用 Tab（开关类：改了即时生效并落盘）——
    public bool HideToTrayOnStart { get; set; } = false;     // 随系统启动时保持后台常驻、不弹主窗口
    public bool CloseToTray { get; set; } = true;            // 点 ✕ 隐藏窗口到托盘（原为固定行为，改为可配）
    public bool StopServiceOnExit { get; set; } = true;      // 退出启动器时同步结束由本程序启动的服务
    public bool FilterDebugInfo { get; set; } = true;        // 界面隐藏引擎的调试/噪声行（`Program.NoiseContains` 那些形状 + `LogNoise` 那八类引擎台账；只挡界面）
    // DEC-163：`EngineFullLedger`（「引擎调试日志（写全量）」）整套退役 —— 八类引擎台账文件照记（简单去重），
    // 过滤只管界面。旧键读不认、随下次保存消失（口径同 LowRam DEC-075 / LlamaUseRawIni DEC-079）。
    public bool ShowStatusBar { get; set; } = true;         // 底部状态栏（预填充/解码/MTP）；关掉整条区域都不占位（DEC-041）
    // DEC-125②（用户点名"打开开关时下方拓展出子选项，可以复选若干显示选项"）：勾了哪些档 = csv，项与次序都在
    // `MainForm.StatItemDefs` 那张表里（表外 id 读到时忽略，别为了一格手改把状态栏弄炸）。
    // **默认 = 现有那三档**（升级当天界面一个字都不变），其余档由人到设置页里勾出来。
    // DEC-128②：本键的语义 = 「实时」（可带平均的行）/「唯一勾选」（其余行）那一枚的勾选集合 —— 语义没动，
    // 只是带平均的两行多出一枚**独立**的「平均」（见下一键）。
    public string StatusBarItems { get; set; } = "prefill,decode,mtp";
    // 其中哪些档把「平均」也显示出来（`平均|当前`）。只有表里 HasAvg 的两行吃这一格 ——
    // 他的原话："平均不是每个参数都有,如勾选 decode 时,才出现 decode 这个的平均值复选"。
    // DEC-128②（他挑的"同排两枚独立"）：与上一键**各自独立** —— 勾「平均」不再要求先勾「实时」，
    // 只勾平均 = 状态栏只出平均值；老 ini（两键都存在但平均没见过独立语义）读出来照旧（默认也不会有平均独立的情形）。
    public string StatusBarAvg { get; set; } = "prefill,decode";
    // DEC-128②：九行的**全量视觉次序**（设置页拖出来的先后，勾没勾都记）。留空 = 还没存过新键的老 ini ——
    // 读取时按 `StatRowOrder(StatusBarItems)` 兜底（正是升级前那一套），任一格存过一次之后就是九个 id 全量。
    // 为什么要它：平均独立之后「只勾平均」的行也有次序，光凭 Items/Avg 两键推不出并集在整排里的位置。
    public string StatusBarOrder { get; set; } = "";
    // —— 模块开关（B 段地基，DEC-024：关闭 = 全隐身——不扫描其模型、不探活、不起进程、界面剔除）——
    // 至少启用一个（设置窗保存时拦 + 启动时兜底，见 ModuleRegistry）。
    public bool EnableStrata { get; set; } = true;           // Strata 模块
    public bool EnableLlama { get; set; } = true;            // llama.cpp 模块
    public bool EnableNinfer { get; set; } = true;           // Ninfer 模块（DEC-096：第三个模块；两个后端各一枚开关，见下面那两行）
    public string ActiveModule { get; set; } = "strata";     // B 段：主区上次激活的模块页（strata / llama / ninfer）
    // 左栏「模块状态」的折叠态（2026-10-15 他点名：折叠态横排三个「灯 + 简称」、悬浮出完整状态；
    // 并明确"记住上次"）—— 所以它落 [Current]，点一次折叠按钮就落一次盘（不是参数格，不进「保存」那套）。
    public bool LightsCollapsed { get; set; } = false;       // false = 展开（默认，与加折叠之前的界面一致）

    // ================= Ninfer（DEC-096）=================
    // 「服务怎么跑」这一档归 [Current]；「这一件怎么起」那一批格子**不落 [Current]**
    //（家底 = exe 同目录那份 ninfer_models.ini 的对应段，见 NinferIni.cs；口径同 DEC-079 的 llama 侧）。
    public string NinferModel { get; set; } = "";            // 选中的段名 = 对外 model-id
    public string NinferBackend { get; set; } = "win";       // 当前后端 = win / wsl（首启 WIN；DEC-087 形④ 那枚方块）
    public bool NinferWinOn { get; set; } = true;            // 启用 WSL 之外的另一侧：WIN 后端（关掉 = 那一侧全隐身）
    public bool NinferWslOn { get; set; } = false;           // WSL 后端默认关：开着每次启动都要 `wsl.exe find` = 会把 VM 拉起来，
                                                             // 那是动他机器全局状态的副作用，所以由他到设置页亲手打开
    public string NinferPort { get; set; } = "8068";         // 公开端口（卡1 那格可编辑 = 壳网关听的口）。**不复用全局 Settings.Port**（DEC-083）
    public string NinferInternalPort { get; set; } = "";     // 引擎实际绑的内部口；留空 = 公开口 + 1（= 8069），且「实际占用也避让」
    public string NinferIdleWin { get; set; } = "10";        // WIN 侧空闲停服（分钟）
    public string NinferIdleWsl { get; set; } = "10";        // WSL 侧空闲停服（分钟）；两份各存（DEC-083：阈值按后端各一张）
    public string GpuWakeWaitSec { get; set; } = "300";      // DEC-157 起三模块共用：请求从公开口进来时等显存/等冷起的上限（秒；可配到 600，超时回 504）。旧键 NinferWakeWaitSec 由 ParseConfigLines 认一次
    public string GpuMutex { get; set; } = "queue";         // **跨模块**显存互斥处理（DEC-157 起 Strata / llama / Ninfer 三族同一档，旧键 NinferGpuMutex 认一次）：queue 排队等 / reject 直接报错（默认排队等，DEC-083 第 6 条；DEC-097② 起 off 档已退役，旧值由 GpuMutexMode 兜成 queue）
    public string NinferDir { get; set; } = @"D:\infer-engine\engine";  // WIN 侧引擎目录（ninfer-serve-89.exe 与 *.ninfer 件都在这）
    public string NinferDistro { get; set; } = "Ubuntu-24.04";          // WSL 发行版名（wsl.exe -d 的那个）
    public string NinferWslModelDir { get; set; } = "/home/user/models"; // WSL 内模型目录（find 扫它）
    public string NinferWslBin { get; set; } = "/home/user/ninfer-4090-yarn/build/apps/ninfer-serve"; // WSL 内引擎可执行
    public string NinferUpdateUrl { get; set; } = "https://github.com/1314521gjy/ninfer-fusion-kvmem/releases";
                                                              // WIN 那条更新链的发布页（网页或 api 地址都吃，同 llama 那条）
    public string NinferWslUpdateUrl { get; set; } = "";   // DEC-110④：默认留空 = 沿用那仓库自己的 origin（与 旧壳 的 `git fetch origin` 逐字一致，最稳）；填了才显式从那一头 fetch
                                                              // DEC-110④：WSL 那条链的上游仓库（它不吃 releases——是进 VM 对本地 git 仓库取上游）。
                                                              // 填了就**显式从这一头** fetch 对应分支；留空 = 沿用那仓库自己的 origin（旧行为）。分支照旧自动认。
    public bool AutoStartNinfer { get; set; } = false;       // 打开启动器后自动启动 Ninfer（按当前后端）
    public string NinferSkipVersion { get; set; } = "";      // 已跳过的远端版本（三条链各一份：Strata / llama / Ninfer）

    // —— DEC-097④：两侧的版本**查出来就固定**（旧行为 = 只在内存里，点过一次「更新…」才看得见）——
    // 存"版本 + 当时那支可执行的 sha256"这一对，而不是只存版本串：WIN 那侧本地算 sha 很便宜（读一次盘），
    // 所以能判"缓存还作不作数"——exe 被人换过 = 缓存失效，宁可报「未知构建」也不报一个过时的号。
    // WSL 那侧的 sha 要进 VM 才算（那是要把虚拟机拉起来的副作用），所以它只在**主动探那一次**跟着更新。
    public string NinferWinVersion { get; set; } = "";       // 上次反查到的 WIN 侧版本 tag（空 = 从没查出来过）
    public string NinferWinVersionSha { get; set; } = "";    // 那次反查对应的本地 exe sha256（裸 hex，小写）
    public string NinferWslVersion { get; set; } = "";       // WSL 侧版本 = VM 内那棵检出的 git describe
    public string NinferWslVersionSha { get; set; } = "";    // 对应的 VM 内引擎可执行 sha256

    // —— Ninfer 高级区/卡2/卡1 的界面载体（家底都在 ninfer_models.ini 的那一段里，随 RetiredCurrentKeys 不落 [Current]）——
    public string NinferCtx { get; set; } = "";                  // --max-context
    public string NinferKvDtype { get; set; } = "";              // --kv-dtype
    public string NinferPool { get; set; } = "";                 // --kv-capacity（设备池）
    public string NinferHostKib { get; set; } = "";              // --host-kv-mib（主机缓存 MiB）
    public string NinferConcurrency { get; set; } = "";          // --max-concurrency（只 WSL 给格）
    public string NinferPrefill { get; set; } = "";              // --prefill-chunk
    public string NinferSlots { get; set; } = "";                // --device-state-slots（只 WSL）
    public string NinferPending { get; set; } = "";              // --max-pending-requests（只 WSL）
    public string NinferPendingSec { get; set; } = "";           // --pending-timeout-ms 的界面值（**按秒填**，传之前 ×1000；只 WSL）
    public string NinferSpec { get; set; } = "";                 // --spec（取值按后端各一张）
    public string NinferDraft { get; set; } = "";                // --draft-tokens
    public string NinferMaxTokens { get; set; } = "";            // --default-max-tokens
    // （NinferSharedPrefix 整属性已删 —— DEC-107 他第 1 条「去掉共享前缀上限」：界面不再管这枚旗标，
    //   段里写着的 `--max-shared-prefixes 0` 原样透传；旧 ini 里那个键读到就命中"没这个属性"被忽略，
    //   随下次保存自然消失 —— 口径同 LowRam，DEC-075）
    public string NinferExtraArgs { get; set; } = "";            // 原样追加到 ninfer 起服务命令行末尾（DEC-107；不进段里的 args）
    public string NinferTemperature { get; set; } = "";          // --temperature
    public string NinferTopP { get; set; } = "";                 // --top-p
    public string NinferTopK { get; set; } = "";                 // --top-k
    public string NinferPresence { get; set; } = "";             // --presence-penalty
    public string NinferEffort { get; set; } = "";               // --default-reasoning-effort / --no-thinking（空白 = WSL 侧写 --no-thinking）
    public bool NinferLmHead { get; set; }                       // --lm-head-draft
    public bool NinferVision { get; set; }                       // --vision
    public bool NinferPreserve { get; set; }                     // --preserve-thinking

    public string LlamaCtx { get; set; } = "";               // llama 卡2「上下文」= ini 段的 ctx-size（DEC-079 起可编辑、住段里，不再落 [Current]）
    public string LlamaKv { get; set; } = "";                // llama 卡2「KV 精度」= cache-type-k / cache-type-v 的显示串「k / v」
    public bool LlamaFlashAttn { get; set; }                 // llama 高级区「flash-attn」两态开关 = 段里的 flash-attn（DEC-079；界面载体，不落 [Current]）
    // —— llama.cpp Tab ——
    // M1 已接上：这些项驱动 llama-server 的拉起（合并 ini 覆盖、--models-max、--sleep-idle-seconds）。
    public string LlamaDir { get; set; } = @"D:\llama-b11259-bin-win-cuda-13.4-x64"; // 含 llama-server.exe
    public string RouterIni { get; set; } = "";                                     // 已退役（DEC-079）：家底固定 = exe 同目录的 llama_models.ini，见 MainForm.RouterIniPath()。留着只为读旧 ini 里那条老路径，一次性把文件搬到实跑目录（搬完不再写回 [Current]）
    public string LlamaModelsDir { get; set; } = "";                                 // 可选；不参与壳的扫描，也不传给 server（DEC-012）
    public string LlamaInternalPort { get; set; } = "";                              // 留空 = 公开端口 + 2
    public string LlamaModelsMax { get; set; } = "1";                                // --models-max
    public string LlamaSleepIdleMinutes { get; set; } = "5";                         // DEC-151：空闲卸载**分钟数**（与 Strata「空闲停服」/ Ninfer「空闲阈值」同单位），传 --sleep-idle-seconds 时 ×60
                                                                                     // 留空/0 = 不自动卸（不传旗标 = 引擎默认 -1）；旧键 LlamaSleepIdleSeconds（秒）由 ParseConfigLines 一次性换算后退役
    public string LlamaUpdateUrl { get; set; } = "https://github.com/ggml-org/llama.cpp/releases";
    // 「用原始 ini 启动」(LlamaUseRawIni) 与 merged.ini 一起退役（DEC-079 取消融合层）：
    // 属性直接删掉（旧 ini 里那个键读不认、随下次保存消失，口径同 LowRam DEC-075）。
    // llama 高级项（DEC-079 起：这些值住在 llama_models.ini 选中模型那一段里，[Current] 已不再写它们，
    // 属性只当界面载体 —— 见 MainForm.RetiredCurrentKeys）。留空 = 这一段与全局都没写 = 用引擎默认。
    // （flash-attn / context-shift 原来有开关，2026-10-01 被删；flash-attn 在 DEC-079 以"两态开关 + 写选中段"回来，
    //   context-shift 照旧不在界面里。）
    public string LlamaTemperature { get; set; } = "";
    public string LlamaTopP { get; set; } = "";
    public string LlamaTopK { get; set; } = "";
    public string LlamaMinP { get; set; } = "";
    public string LlamaPresencePenalty { get; set; } = "";
    public string LlamaRepeatPenalty { get; set; } = "";
    public string LlamaSpecDraftNMax { get; set; } = "";
    public string LlamaSpecType { get; set; } = "";          // llama 高级区「投机方式」= 段里的 spec-type（DEC-145；空白 = 删键回继承）
    public string LlamaMmprojOffload { get; set; } = "";     // llama 高级区「视觉」= 段里的 mmproj-offload（"true" = GPU / "false" = CPU / "" = 跟随全局；DEC-145）
    public string LlamaExtraArgs { get; set; } = "";         // 原样追加到 llama-server 命令行末尾（DEC-075；不进合并 ini）
    // 思考深度（2026-10-04，替换原「并行数」）：合并 ini 的 reasoning-effort；留空 = 用 ini 里的值（[*] 段照旧透传）
    public string LlamaReasoningEffort { get; set; } = "";
    public string SkipVersion { get; set; } = "";            // 已跳过的远端版本
    public string LlamaSkipVersion { get; set; } = "";       // llama.cpp 各跳各的（DEC-049：与 Strata 那份互不影响）

    // ================= 本程序自更新（DEC-202，第 4 条链）=================
    // 「本程序」不是引擎后端、不进模块表，但它照样要一格更新源与一份跳过位 —— 都落 [Current]。
    // [Current] 是反射全属性读写，新键不必登记读写代码；但设置页保存那一趟是**手写白名单**
    //（MainForm.CopySettingsWindowFields），新字段不加进去 = 点「保存」即丢值（静默，不是编译错）。
    public string AppUpdateUrl { get; set; } = "https://github.com/icegita/MultiLauncher";
                                                              // 本程序那条更新链的上游仓库（网页或 api 地址都吃，口径同另三条）。
                                                              // 留空或认不出 owner/repo = 回落到上面这个默认值（填坏不瘫痪整条链）
    public string AppSkipVersion { get; set; } = "";          // 已跳过的远端版本（与另三条链同族，各记各的）

    public Settings Clone() => (Settings)MemberwiseClone();
}

public class Config
{
    public Settings Current { get; set; } = new();
    // 预设（旧 [Preset:模块:名] 段）已拆（2026-10-04，DEC-073）：参数只有一份家底 —— [Current]（全局项）
    // + 每个 Strata 模型各一段（DEC-206 起搬出本 ini，现在住 data\config\strata\strata_models.ini，DEC-210）；旧 ini 里遗留的 [Preset:] 段解析时**识别并跳过**
    // （不读进任何东西，防漏进 [Current]），随下一次保存自然消失。别再把预设加回来。

    // 每个 Strata 模型自己的参数快照（DEC-064）：键 = 模型 tag，值 = 只填了「模型家底」那批字段的 Settings。
    // 为什么非要有它：以前所有模型共用 [Current] 里的一份扁平值，而换模型时只有 tag 被记进配置、
    // 参数只进了界面 —— 于是「下拉是 iq3_s、上方模型名却是 swift-1.5-iq3_xxs」（用户 2026-10-04 反馈）。
    public Dictionary<string, Settings> ModelSnaps { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    internal const string ModelSnapPrefix = "Model:";
    internal const string LegacyPresetPrefix = "Preset:";   // 只用于解析时认出来并跳过（DEC-073）

    internal static string ModelSnapKey(string tag) => ModelSnapPrefix + tag;

    internal static bool IsModelSnap(string section) => section.StartsWith(ModelSnapPrefix, StringComparison.Ordinal);

    internal static string ModelSnapTag(string section) => section[ModelSnapPrefix.Length..].Trim();
}
