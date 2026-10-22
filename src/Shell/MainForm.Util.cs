using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace MultiLauncher;

// MainForm 的小工具（路径/字符串/剪贴板/资源管理器直达那一族）。
// 2026-10-15 从 Program.cs 整段搬出，一行未改。
public partial class MainForm
{
    // ---------------- 小工具 ----------------

    static void OpenFolder(string path)
    {
        try
        {
            if (Directory.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch { /* best effort */ }
    }

    // 壳自己的"生效配置"目录 ——「打开配置」与设置页「模型配置目录」都用它。
    // DEC-118：这里以前自己拼 `data\run`（= 第二个家！搬家那一步没跟着走，于是自检期间还会凭空建出老目录）。
    // 现在只认 RunConfigDir() 那一个家：默认 = data\config\strata，自检/探针期间自动跟着改道。
    internal static string EffectiveConfigDir()
    {
        var dir = RunConfigDir();
        try { Directory.CreateDirectory(dir); } catch { /* 建不出来就返回路径让用户看到 */ }
        return dir;
    }

    // 开机自启：HKCU 的 Run 键（不需要管理员权限）
    // DEC-164：闸门在**这一处**（不在两个调用点各写一遍）—— 自检与出图台一律不许碰他的 Run 键。
    void SetAutoStart(bool enable)
    {
        if (AutoStartRegistryDisabledForTest) { AutoStartRegistrySkippedForTest++; return; }
        AutoStartRegistryWritesForTest++;
        if (AutoStartRegistryWriteForTest is { } fake) { fake(enable); return; }
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key is null) return;
            if (enable)
            {
                // 勾了「启动时隐藏到托盘」就带上 --tray：开机启动后只留在托盘，不弹主窗口
                var args = _config.Current.HideToTrayOnStart ? " --tray" : "";
                key.SetValue(RunValueName, "\"" + Application.ExecutablePath + "\"" + args);
            }
            else if (key.GetValue(RunValueName) is not null) key.DeleteValue(RunValueName);
        }
        catch (Exception ex) { Log("开机自启设置失败: " + ex.Message); }
    }

    // 「高级」在参数卡片内向下展开：**不撑窗口**——临时侵占下方日志区（日志区是 Fill，卡片变高时
    // Dock 布局自动把差额从它那里扣掉），折叠后日志区归还（用户反馈：展开时整个窗口变高不好用）。
    // 展开态用自管字段 _advShown，不读 _advHost.Visible —— 后者是"含祖先"的有效可见性，
    // 自检不开窗时整棵树都是 false（第五轮 DEC-017 同一个坑），读写它会让布局判断失准。
    bool _advShown;
    void ToggleAdvanced()
    {
        _advShown = !_advShown;
        _advHost.Visible = _advShown;
        _btnAdv.Text = _advShown ? "▾ 高级" : "◂ 高级";
        HeightChanged?.Invoke();
    }

    // 高级区显示哪一套项（第 0 轮 DEC-094：按模块表摆 —— 激活模块那一套可见，其余整套挪出画外并算高度）
    void ShowAdvancedForActiveModule()
    {
        foreach (var v in _views) v.AdvPanel().Visible = ReferenceEquals(v, ActiveView);
        foreach (var v in _views) v.LayoutAdv();
        _advHost.Height = AdvHeight();
        if (HeightChanged is not null) HeightChanged();
    }

    // 左栏启停：只管服务本身（DEC-011）；激活哪个模块页管的就是哪个模块（第 0 轮：动作在本模块那一行里）
    // 「停止」覆盖三种真实情况（用户反馈 2026-10-01）：壳拉起的服务、外部拉起的服务（确认后结束）、仅网关。
    void ToggleService() => ActiveView.ToggleService();

    // 结束外部（非本壳拉起的）Strata 服务：确认 + 按端口杀；成功后就地复位状态，不等 7 秒的探测周期
    void StopExternalStrata()
    {
        if (!EnsurePortFree(ServicePortValue(), "结束外部服务")) return;
        _externalRunning = false;
        SetStoppedState();
        RefreshStatusUi();
    }

    void StopExternalLlama()
    {
        if (!EnsurePortFree(LlamaPortValue(), "结束外部 llama-server")) return;
        _llamaExternal = false;
        RefreshStatusUi();
    }

    // 停止 = 停模型进程 + 关网关（成对，DEC-011）
    void StopServiceAndGateway()
    {
        StopServer();
        if (StrataGateway is not null) StopGateway(StrataKey);
        RefreshStartStopButton();
    }

    // 卡片3 的模型按钮：**动作归各模块那一行**（DEC-100①b，与 BUG-039 同族的一处 —— 原来这里是
    // `if (backend == llama) { … }` 之后整段 Strata 流程，第三个模块的模型进来就会去拉 Strata 引擎）
    // · Strata = 拉起/卸载选中模型、网关不动
    // · llama.cpp = 真加载/卸载/重载（用户规格 2026-10-01，见 MainForm.Llama.cs 的 LlamaModelAction）
    // · Ninfer = 「加载」起这一件（网关没立会先把网关立起来）、「卸载」只停引擎、网关留着
    void ModelAction()
    {
        // DEC-110①：卡3 那一枚按钮只长在激活模块那一页，这句"没模型可加载"也只该出现在那一页
        //（同「界面参数已保存。」那条：默认 launcher 通道 = 三页都显示 = 另两页凭空多一句与自己无关的话）。
        if (SelectedModel() is null) { LogActive("还没有可选模型。"); return; }
        ActiveView.ModelAction();
    }

    // 模型行按钮的文案判定（纯函数，自检穷举组合；用户规格 2026-10-01，口径由 DEC-141 改）：
    // · **三页统一：本模块只要有东西跑着/加载着就一直给「卸载」**，不再要求下拉正指在那一件上
    //（他的原话："卸载按钮在已有模型处于加载状态时,应始终显示吧,而不是当前这样,只有选中已加载模型时才显示卸载"）。
    //   点下去卸的是**实际在跑的那一件**（落点见各模块的 ModelAction），于是切件 = 先卸载再加载两步 ——
    //   与 Ninfer DEC-102③ 那条"不替用户停旧起新"的既有口径对齐；Strata 那一页也再不会出现
    //   点「加载」只弹"服务已经在运行。"的死胡同（StartService 第一行那条早退）。
    // · llama 模型：忙时显示忙态（"加载中…"等）；单体模式固定「重载」
    // 第 0 轮（DEC-094）：拆成一模块一个纯函数；DEC-141 起 Strata 那一支不再需要"跑的是不是选中的那件"这一档
    internal static string ModelActionTextForStrata(bool strataRunning, bool strataExternal)
        => (strataRunning || strataExternal) ? "卸载" : "加载";

    internal static string ModelActionTextForLlama(string? llamaBusy, bool llamaAnyLoaded, bool routeMode)
    {
        if (!string.IsNullOrEmpty(llamaBusy)) return llamaBusy;
        if (!routeMode) return "重载";
        return llamaAnyLoaded ? "卸载" : "加载";
    }

    // 「卸载」这一击落到哪一件（DEC-141，Strata / Ninfer 共用一个家）：跑着的那件优先；
    // 进程在跑却没记下件名（罕见：接管外部实例那类）才退回下拉选中的那件。
    internal static string UnloadTagFor(string runningTag, string selectedTag)
        => string.IsNullOrEmpty(runningTag) ? selectedTag : runningTag;

    // 模型行那枚按钮的文字：本模块那一行给（两边都只把实时标志喂给纯函数 ModelActionTextFor）
    string ModelActionText() => ActiveView.ModelActionText();

    // 「更新 llama.cpp」入口：④（DEC-049）已接上，检查与升级都在 MainForm.UpdateLlama.cs
    //（设置页 llama.cpp 节的维护组，与 Strata 同一套形状）。

    void OpenSettings()
    {
        using var dlg = new SettingsForm(this, _config.Current);
        dlg.ShowDialog(this);
    }

    // 设置页：**所有项点「保存」才生效**（用户反馈 2026-10-01：统一语义，开关不再"拨动即生效"）。
    // 开关与数值/路径一视同仁：换目录要重扫模型；网关在跑时改内部端口/空闲停服给"下次启动网关生效"提示。
    // from = 点「保存」的那个窗（DEC-066）：toast 要贴它右下角，别贴主窗——设置窗是模态的，
    // 贴主窗会被设置窗挡住，等于没弹（与 DEC-060「owner 要传点它的那个窗」同一条理由）。
    internal void ApplySettingsWindowValues(Settings s, Form? from = null)
    {
        var oldRoot = _config.Current.Root;
        var oldInternalPort = _config.Current.InternalPort;
        var oldIdle = _config.Current.IdleStopMinutes;
        // 第 0 轮那张表的口径：**别再点名"两个模块"** —— 启用位按模块表逐个记旧值，加模块这里自动跟着走
        var oldOn = ModuleRegistry.All.ToDictionary(m => m.Key, m => m.Enabled(_config.Current));
        var oldWslOn = _config.Current.NinferWslOn;
        var oldWslDir = _config.Current.NinferWslModelDir;
        var oldDistro = _config.Current.NinferDistro;
        CopySettingsWindowFields(s, _config.Current);
        // 2026-10-02 反馈（BUG-027）：设置窗打开前，主窗口可能刚改过参数还没落盘——先把界面实时值收进
        // _config.Current 再推（不收就会被旧的已保存值把主界面上刚改的格子盖回去）。
        // DEC-092 第 2 条：收归收、**推归推**，但这次落盘只提交设置页自己那些字段（commitParams=false），
        // 主窗那批没点「保存」的改动照旧被冻结表按回磁盘上那一份 —— 于是"从设置页点一下保存"不再顺手存住它们。
        _config.Current = CollectSettings();
        SaveConfig(commitParams: false);
        SetAutoStart(_config.Current.AutoStartWithWindows);
        ApplySettings(_config.Current, commitBaseline: false);
        RefreshVersions();
        // 模块开关（B 段地基，DEC-024）：启用位变了就重扫（新启用的模块恢复模型、停用的剔除）；
        // 被停用模块的外部探测标志一并清掉（关模块时"自家服务在跑"已被保存校验拦下，这里只清外部实例的显示态）
        bool moduleChanged = ModuleRegistry.All.Any(m => oldOn[m.Key] != m.Enabled(_config.Current));
        if (moduleChanged)
            foreach (var v in _views)
                if (!v.EnabledIn(_config.Current)) v.ClearExternalProbe();
        // DEC-096：Ninfer 那一侧的 WSL 开关 / 发行版 / 模型目录变了要重扫 VM（关掉就别再拉 wsl.exe）
        bool ninferScanNeeded = _config.Current.NinferWslOn != oldWslOn
                              || moduleChanged
                              || !string.Equals(oldWslDir, _config.Current.NinferWslModelDir, StringComparison.Ordinal)
                              || !string.Equals(oldDistro, _config.Current.NinferDistro, StringComparison.Ordinal);
        // DEC-079：llama_models.ini 的路径已固定（= exe 同目录），设置页不再能指走，
        // 所以这里只剩「换了 Strata 目录」与「模块开关变了」两种情况要重扫
        if (!string.Equals(oldRoot, _config.Current.Root, StringComparison.OrdinalIgnoreCase) || moduleChanged)
            RefreshModels();
        if (ninferScanNeeded) RefreshNinferWslArtifacts();
        if (moduleChanged)
            Log("模块状态：" + string.Join("、", ModuleRegistry.All.Select(m =>
                m.DisplayName + (m.Enabled(_config.Current) ? " 启用" : " 停用（全隐身：不扫描 / 不探活 / 不起服务）"))));
        // 网关正在跑时，内部端口 / 空闲停服是"下次启动网关才生效"的项（网关构造时读一次）——明说一句，
        // 否则保存后界面无变化，看着像没生效（用户反馈"设置内部端口保存后没有变化"）
        if (StrataGateway is not null
            && (!string.Equals(oldInternalPort, _config.Current.InternalPort, StringComparison.Ordinal)
                || !string.Equals(oldIdle, _config.Current.IdleStopMinutes, StringComparison.Ordinal)))
            LogS("网关正在运行：内部端口 / 空闲停服的新值在下次启动网关时生效（点「停止」再「启动」即可）");
        Log("设置已保存。");
        Toast.Show(from ?? this, "设置已保存", Toast.Kind.Ok);      // DEC-066：设置窗一关就没了落点，光一行日志不算反馈
        RefreshStatusBar();      // 状态栏开关（设置页保存）要立刻生效，别等下一拍
    }

    // 设置页负责的那批字段（其余字段归主界面持有，别互相覆盖）
    static void CopySettingsWindowFields(Settings from, Settings to)
    {
        to.Root = from.Root;
        to.StrataUpdateUrl = from.StrataUpdateUrl;      // DEC-110④：三节的「更新源」都归设置页（Strata 以前写死在代码里）
        to.InternalPort = from.InternalPort;
        to.IdleStopMinutes = from.IdleStopMinutes;
        to.AutoStartWithWindows = from.AutoStartWithWindows;
        to.HideToTrayOnStart = from.HideToTrayOnStart;
        to.AutoStartService = from.AutoStartService;
        to.OpenBrowser = from.OpenBrowser;
        to.CloseToTray = from.CloseToTray;
        to.StopServiceOnExit = from.StopServiceOnExit;
        to.SaveLog = from.SaveLog;
        to.FilterDebugInfo = from.FilterDebugInfo;
        to.ShowStatusBar = from.ShowStatusBar;    // ④ 底部状态栏显示开关（DEC-041）
        to.StatusBarItems = from.StatusBarItems;  // DEC-125②：显示哪几档（勾「实时 / 唯一那一枚」的集合，归设置页那一排复选）
        to.StatusBarAvg = from.StatusBarAvg;      // DEC-128②：平均勾选（与上一键各自独立）
        to.StatusBarOrder = from.StatusBarOrder;  // DEC-128②：九行全量视觉次序（拖出来的先后）
        to.EnableStrata = from.EnableStrata;      // 模块开关（B 段地基；SettingsForm 的「模块」区）
        to.EnableLlama = from.EnableLlama;
        to.AutoStartStrata = from.AutoStartStrata;   // B 段：自动启动服务每模块一个
        to.AutoStartLlama = from.AutoStartLlama;
        to.LlamaDir = from.LlamaDir;
        // DEC-079：RouterIni 与 LlamaUseRawIni 都不再走设置窗 —— 家底路径固定（= exe 同目录），
        // 而且没有「合并」这回事了（merged.ini 退役），所以这一行随那两键一起删掉
        to.LlamaModelsDir = from.LlamaModelsDir;
        // LlamaInternalPort 不再走设置窗（2026-10-02 挪到主界面 llama 页卡1）：由卡1 即时字段自行收集/落盘
        to.LlamaModelsMax = from.LlamaModelsMax;
        to.LlamaSleepIdleMinutes = from.LlamaSleepIdleMinutes;
        to.LlamaUpdateUrl = from.LlamaUpdateUrl;
        // DEC-202：关于页那一格「更新源」（本程序自更新）。**必须加在这里** —— `CollectSettings()` 是
        // `_config.Current.Clone()` 起手的，新字段天然带得过去；真正会丢值的是这张逐字段白名单。
        to.AppUpdateUrl = from.AppUpdateUrl;
        // —— Ninfer（DEC-096）：设置页 Ninfer 节负责的那一批；段级格子（args 里那些）不走这条路 ——
        to.EnableNinfer = from.EnableNinfer;
        to.NinferWinOn = from.NinferWinOn;
        to.NinferWslOn = from.NinferWslOn;
        to.NinferDir = from.NinferDir;
        to.NinferDistro = from.NinferDistro;
        to.NinferWslModelDir = from.NinferWslModelDir;
        to.NinferWslBin = from.NinferWslBin;
        to.NinferUpdateUrl = from.NinferUpdateUrl;
        to.NinferWslUpdateUrl = from.NinferWslUpdateUrl;   // DEC-110④：WSL 那条链的上游仓库（留空 = 沿用仓库自己的 origin）
        to.NinferInternalPort = from.NinferInternalPort;
        to.NinferIdleWin = from.NinferIdleWin;
        to.NinferIdleWsl = from.NinferIdleWsl;
        // DEC-157：这一对升到三模块共用（改名 GpuMutex / GpuWakeWaitSec），落点仍在 [Current]、仍由设置页「通用」那一节管
        to.GpuMutex = from.GpuMutex;
        to.GpuWakeWaitSec = from.GpuWakeWaitSec;
        to.AutoStartNinfer = from.AutoStartNinfer;
    }


    void OpenWeb()
    {
        // 按激活模块的端口打开（B 段）：URL 由本模块那一行给（含"没在跑"那一句提示与旧端口的交代）
        string url = ActiveView.WebUrl();
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); LogActive("$ 打开 " + url); }
        catch (Exception ex) { LogActive("打开网页失败: " + ex.Message); }
    }

    // 「打开配置」开的是哪一份（第 0 轮 DEC-094 起两支实现在 MainForm.ModuleViews.cs 的 *ConfigTarget）：
    //   Strata = **`data\config\strata\strata_models.ini`**（2026-10-22 他点名的第三版，DEC-211：直接开文件，
    //             与 llama / Ninfer 同一形状；模型参数那 16 格与他手加的 `ArgsN` 都在这份里，
    //             引擎实读的产物与伴生文件在旁边的 `run\` 子目录，不给入口）
    //             壳自己的 MultiLauncher.ini 搬到设置页「通用 → 路径」，主界面这一枚不再开它）
    //   llama / Ninfer = 它们各自的家底 ini（界面「保存」写的就是它、引擎读的也是它）
    // 返回空路径时 note 就是给用户看的说明。
    internal (string Path, string Note) ActiveConfigTarget() => ActiveView.ConfigTarget();

    // DEC-122②：原来这里还有一条 `MaterializeActiveConfig`（打开配置前先按界面参数重新生成产物）。
    // 打开目标换成目录之后那一步更没有读者了，整套退役（含 ModuleView.Materialize 与三个实现）——
    // 一个"看一眼"的动作不该顺手写文件；产物照旧由每次启动（手动 / 唤醒）重建。
    void OpenActiveConfig()
    {
        var (file, note) = ActiveConfigTarget();
        if (file.Length == 0)
        {
            LogActive(note);
            Toast.Show(this, note, Toast.Kind.Info);
            return;
        }
        // 2026-10-22（DEC-206）：Strata 这一支给的是**目录**（一层里三份文件）—— 目录走资源管理器，
        // 文件才走"绑过的编辑器 / 记事本"那条路（别拿 notepad 去开一个目录）。
        if (Directory.Exists(file))
        {
            try
            {
                Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
                LogActive("$ 打开配置目录：" + file);
            }
            catch (Exception ex)
            {
                LogActive("打开配置目录失败（" + ex.Message + "）：" + file);
                return;
            }
            if (note.Length > 0) LogActive(note);
            return;
        }
        var opener = ConfigOpener(file);
        try
        {
            if (opener.Length == 0) Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
            else Process.Start(new ProcessStartInfo(opener, $"\"{file}\"") { UseShellExecute = true });
            LogActive(opener.Length == 0
                ? "$ 打开配置：" + file
                : $"$ 打开配置：{file}（这类文件系统没绑定程序，先用记事本打开；想换编辑器就在资源管理器里右键 →「打开方式」→「始终」，之后本按钮跟着走）");
        }
        catch (Exception ex)
        {
            LogActive("打开配置失败（" + ex.Message + "）：改在资源管理器里定位");
            RevealInExplorer(file);
            return;
        }
        if (note.Length > 0) LogActive(note);
    }

    // 用哪个程序打开（DEC-037）：系统对该扩展名有默认程序就顺它（用户绑过 VS Code 就开 VS Code）；
    // 没绑定就**别**让 Win11 弹「你要用什么打开」的对话框——直接落到记事本。返回 "" 表示交给系统默认。
    internal static string ConfigOpener(string file) => HasDefaultHandler(Path.GetExtension(file)) ? "" : "notepad.exe";

    static bool HasDefaultHandler(string ext)
    {
        if (ext.Length == 0) return true;              // 没扩展名：没什么可探的，交给系统
        try
        {
            // 用户显式选过「打开方式」的那条最优先（Win11 记在 FileExts\...\UserChoice，值是 AppX 形式的 ProgId）
            using (var uc = Registry.CurrentUser.OpenSubKey(
                       $@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{ext}\UserChoice"))
                if (uc?.GetValue("ProgId") is string p && p.Trim().Length > 0) return true;
            foreach (var root in new[] { Registry.CurrentUser, Registry.ClassesRoot })
            {
                using var k = root.OpenSubKey(ext);
                if (k?.GetValue("") is string d && d.Trim().Length > 0) return true;
            }
            return false;
        }
        catch { return true; }   // 探不动就交给系统默认（后路还有记事本与资源管理器，不静默失败）
    }

    // 打不开时的后路：在资源管理器里选中该文件
    // ---------------- 关于页/Strata 节的直达与内建引擎（DEC-055，用户反馈） ----------------

    /// 「打开」直达壳自己的四处位置：ini 在资源管理器里选中它，data / logs / 程序目录直接开目录。
    /// 目录还没生成时不弹错误框，只给一行说明（logs 要勾了「写入日志文件」跑过一次才有）。
    internal void OpenLauncherFolder(string which)
    {
        if (which == "ini")
        {
            // 2026-10-22（DEC-211，他点名）：设置页那枚「打开」也**直接打开这一份文件**（原来是在资源管理器里选中它，
            // 等于多点一次）。文件还没生成时给一句说明，不弹错误框。
            if (!File.Exists(ConfigPath)) { Log("还没有这份设置文件：" + ConfigPath); return; }
            try { Process.Start(new ProcessStartInfo(ConfigPath) { UseShellExecute = true }); Log("$ 打开 " + ConfigPath); }
            catch (Exception ex) { Log("打开设置文件失败（" + ex.Message + "）：" + ConfigPath); }
            return;
        }
        var path = which switch
        {
            "data" => LauncherDataDir(),
            "exe" => AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar),   // 2026-10-22：程序目录（他点名加）
            _ => Path.Combine(AppContext.BaseDirectory, "logs"),
        };
        if (!Directory.Exists(path))
        {
            Log("还没有这个目录：" + path + (which == "logs" ? "（勾了「写入日志文件」并跑过一次之后才有）" : ""));
            return;
        }
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); Log("$ 打开 " + path); }
        catch (Exception ex) { Log("打开目录失败: " + ex.Message); }
    }

    // Strata 内建那份 llama.cpp：本机只有 CMakeLists 里钉的 commit（GIT_TAG）。
    // 「v0.5.0-b11370」那种口径里的版本号是 llama-server 构建时写进二进制的，源码里没有，
    // 所以这一行就老实写 commit——DEC-056 起不再去 GitHub 反查 b 号（用户判为没必要）。
    // DEC-061 自检用：临时改/恢复配置里的 Strata 目录，确认这一行跟着**配置的**目录走。
    // （写成方法而不是属性：WinForms 的设计器序列化会对 Form 上的属性报 WFO1000。）
    internal string GetRootForTest() => _config.Current.Root;
    internal void SetRootForTest(string root) => _config.Current.Root = root;
    internal string PinnedLlamaCommitSha()
    {
        try
        {
            // DEC-061：读**当前配置的** Strata 目录，不是出厂默认值——`new Settings().Root` 恒为 D:\Strata，
            // 用户把 Strata 目录改到别处后，这一行要么读不到，要么读出另一份安装的 commit。
            var cmake = File.ReadAllText(Path.Combine(_config.Current.Root, "CMakeLists.txt"));
            var m = System.Text.RegularExpressions.Regex.Match(cmake, @"GIT_TAG\s+([0-9a-fA-F]{7,40})");
            if (m.Success) return m.Groups[1].Value;
        }
        catch { /* 读不到就空着 */ }
        return "";
    }

    internal string PinnedLlamaText()
    {
        var sha = PinnedLlamaCommitSha();
        if (sha.Length == 0) return "（读不到 CMakeLists.txt）";
        return "提交 " + sha[..Math.Min(8, sha.Length)];
    }


    static void RevealInExplorer(string file)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + file + "\"") { UseShellExecute = true }); }
        catch { /* best effort */ }
    }
}
