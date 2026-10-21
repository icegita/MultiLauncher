namespace MultiLauncher;

// Ninfer 节（DEC-096 第三个模块；DEC-110④ 起 = 共用 → WSL 后端 → WIN 后端 → 维护）。
// 2026-10-15 从 SettingsForm.cs 整段搬出，一行未改。
internal sealed partial class SettingsForm : Form
{
    // ---------------- Ninfer 节（DEC-096 第三个模块；DEC-110④ 起 = 共用 → WSL 后端 → WIN 后端 → 维护）----------------
    // 「公开端口」这一行**没有**（DEC-089）：它与卡1「监听端口」是同一个值，单一入口在主界面那一格。
    // 「维护」组与另两节同一形状、同一行序：**更新源 → 版本 → 回退 → 各自模块独有**。这一族有两条链，
    // 所以那一组里按后端各排一遍（行名带 · WSL / · WIN），最后那行「改动前自动备份」是两个后端共用的家底。
    FlowLayoutPanel PageNinfer()
    {
        var page = NewPage();
        GroupTitle(page, "共用（两个后端一起用的）");
        ValueRow(page, "内部端口", "引擎实际绑定的端口。留空 = 公开端口 + 1，撞车会自动避让。",
            _s.NinferInternalPort, v => _s.NinferInternalPort = v);
        // DEC-157：「显存互斥处理」这一格**挪到「通用 → 显存」**—— 这一档从设计第一天就是跨模块的（支点 I「三模块
        // 一视同仁」），而 Strata / llama 两头今天也接上了，留在 Ninfer 一节会让人以为它只管 Ninfer（他点名"是共同的设置"）。
        AboutRow(page, "模型配置", MainForm.NinferIniPath(), openText: "打开", onOpen: () => _main.RevealNinferIni());

        GroupTitle(page, "WSL 后端");
        SwitchRow(page, "启用 WSL 后端", "关掉 = 这一侧完全隐藏：不扫模型、不起进程，下拉与相关格子都不出现。",
            () => _s.NinferWslOn, v => _s.NinferWslOn = v);
        ValueRow(page, "发行版", "虚拟机发行版名（wsl.exe -d 后面那个）。", _s.NinferDistro, v => _s.NinferDistro = v);
        ValueRow(page, "模型目录（VM 内）", "虚拟机里存放模型件的目录（扫 *.ninfer）。",
            _s.NinferWslModelDir, v => _s.NinferWslModelDir = v);
        ValueRow(page, "引擎可执行（VM 内）", "虚拟机里那只引擎可执行文件的路径。",
            _s.NinferWslBin, v => _s.NinferWslBin = v);
        ValueRow(page, "空闲停服(分钟)", "多久没有请求就停服释放显存。WSL 与 WIN 各存一份。",
            _s.NinferIdleWsl, v => _s.NinferIdleWsl = v);

        GroupTitle(page, "WIN 后端");
        SwitchRow(page, "启用 WIN 后端", "关掉 = 这一侧完全隐藏：不扫模型、不起进程，下拉与相关格子都不出现。",
            () => _s.NinferWinOn, v => _s.NinferWinOn = v);
        ValueRow(page, "引擎目录", "引擎 exe 与模型件所在目录。",
            _s.NinferDir, v => _s.NinferDir = v, pb: PathBtn.PickFolder);
        ValueRow(page, "空闲停服(分钟)", "多久没有请求就停服还显存。", _s.NinferIdleWin, v => _s.NinferIdleWin = v);

        GroupTitle(page, "维护");
        // DEC-110④：WSL 那条链以前没有「更新源」这一格（它不吃 releases，是进 VM 对本地 git 仓库取上游）。
        // 填了就显式从那一头取指定分支（分支照旧自动认），留空 = 沿用那仓库自己的 origin = 旧行为一字不变。
        ValueRow(page, "更新源 · WSL", "上游仓库地址。留空 = 用虚拟机里那个仓库自己的 origin。",
            _s.NinferWslUpdateUrl, v => _s.NinferWslUpdateUrl = v);
        _lblNinferWslVer = AboutRow(page, "版本 · WSL", _main.NinferWslVersionForSettings(),
            openText: "更新…", onOpen: () => _main.OpenUpdateWindow(MainForm.NinferKey, this, "wsl"));
        ButtonRow(page, "编译快照回退 · WSL", "回到某一份编译快照，免重编、秒级。",
            "选择快照…", () => _main.NinferWslRollback());
        // DEC-110②：那句「版本 = 拿本地 exe 的 sha256 去上游反查…没有 PTX 回退。」按用户点名删掉（docs/.ai/参考-界面与参数对照.md 里有）。
        ValueRow(page, "更新源 · WIN", "GitHub 的 releases 地址。", _s.NinferUpdateUrl, v => _s.NinferUpdateUrl = v);
        _lblNinferVer = AboutRow(page, "版本 · WIN", _main.NinferVersionForSettings(),
            openText: "更新…", onOpen: () => _main.OpenUpdateWindow(MainForm.NinferKey, this));   // 这一枚开 WIN 那扇（DEC-107）
        // DEC-110⑤：这一枚补的是"备份有、入口没有"那笔账（原来那句"用上面那行退回旧 exe"指的是配置备份，名不副实）。
        // 「套件完整性校验」整行按用户口径删掉。
        ButtonRow(page, "引擎 exe 回退 · WIN", "换 exe 前会自动存一份备份，这里挑一份退回去。",
            "恢复 exe…", () => _main.RestoreNinferExeFromBackup());
        ButtonRow(page, "改动前自动备份", "每次写配置前自动存一份备份。",
            "恢复…", () => _main.RestoreNinferIniFromBackup());
        // DEC-110②：那句「只剩一个后端启用时，卡1 左侧的方块就只画那一枚…」按用户点名删掉。
        // DEC-097③：这一侧启用着且从没读过，才进一次 VM（探一次会把虚拟机拉起来 = 有副作用，判据在 Ensure 里）
        _main.EnsureNinferWslVersionProbed();
        return page;
    }

    FlowLayoutPanel PageGeneral()
    {
        var page = NewPage();
        // 模块开关（B 段地基，DEC-024/025）：全隐身语义；至少启用一个、关闭前先停服务（都在保存时拦）
        // 第 0 轮 DEC-094：**每个启用模块一节** 的派发从 `Key=="llama" ? ... : ...` 改成按键点名的 switch（见 PageForModule）
        GroupTitle(page, "模块");
        foreach (var m in ModuleRegistry.All)
            SwitchRow(page, m.DisplayName, m.Description, () => m.Enabled(_s), v => m.SetEnabled(_s, v));
        // DEC-110②：那句「至少启用一个；关闭前须先停止服务。」按用户点名删掉 ——
        // 两条约束本来就在「保存」那一关当场拦下并说清（ModuleSwitchAtSave / NinferBackendAtSave），
        // 常驻一行解释 = 同义反复（口径同 DEC-082 那一族"能删就删"）。
        GroupTitle(page, "启动行为");
        SwitchRow(page, "随系统启动", "登录 Windows 后自动启动本启动器。", () => _s.AutoStartWithWindows, v => _s.AutoStartWithWindows = v);
        SwitchRow(page, "启动时隐藏到托盘", "随系统启动时保持后台常驻，不弹主窗口。", () => _s.HideToTrayOnStart, v => _s.HideToTrayOnStart = v);
        // B 段（DEC-025）：「自动启动服务」按模块各一个 —— 第 0 轮起这排格子由模块表长出来
        //（行名 = 「自动启动服务 · 」+ 显示名，说明文字与落点字段都在那一行里）
        foreach (var v in _main.ModuleViews)
            SwitchRow(page, "自动启动服务 · " + v.DisplayName, v.AutoStartDesc,
                () => v.AutoStart(_s), on => v.SetAutoStart(_s, on));
        SwitchRow(page, "就绪后打开网页", "服务就绪后用浏览器打开对应网页。", () => _s.OpenBrowser, v => _s.OpenBrowser = v);
        // DEC-165②（他批"放通用"）：API Key 三模块共用一枚 —— 客户端视角只有一个入口（三页对外是同一批客户端），
        // 落点各模块**各自物化**：Strata = 运行配置的 `api_key`；llama = `--api-key`；Ninfer = `--api-key`。
        // 留空 = 不鉴权（引擎默认；段里手写过的 key 壳也不碰）。
        GroupTitle(page, "接口");
        ValueRow(page, "API Key", "三模块共用。留空 = 不鉴权。设了以后，客户端要带 `Authorization: Bearer <key>`" +
            "（Ninfer 也认 `x-api-key`）。",
            _s.ApiKey, v => _s.ApiKey = v);
        // DEC-157（他批的第 2 条）：这一档住「通用」—— 一张卡上三族共用，判据与那一格都只有一份家。
        // 说明按他点名改写：原来那句只说"请求从公开口进来"，现在它同时管着 Strata / llama / Ninfer 三条唤醒路。
        GroupTitle(page, "显存");
        ChoiceRow(page, "显存互斥处理", "一张卡同时只允许一件模型驻留显存。" +
            "进入队列 = 排队等待；返回报错 = 直接拒绝。",
            new[] { "进入队列", "返回报错" },
            MainForm.GpuMutexMode(_s) == "reject" ? 1 : 0,
            i => _s.GpuMutex = i == 1 ? "reject" : "queue");
        // DEC-159（他问"这是什么唤醒时间?需要界面吗?"之后上的格）：这一档到 DEC-157 已经**管三族**了，
        // 却还只住在 ini 里；而它原来还有一处不诚实 —— 网关等这一次唤醒的总时限写死 300 秒，
        // 于是设到 300 以上时那句点名"前面是谁"的 504 永远被顶掉（算式现在收进 GatewayWakeTimeoutSeconds 一个家）。
        ValueRow(page, "排队等显存上限(秒)", "「进入队列」时最多排多久（30~600）。",
            _s.GpuWakeWaitSec, v => _s.GpuWakeWaitSec = v);
        GroupTitle(page, "关闭行为");
        SwitchRow(page, "关闭到托盘", "点 ✕ 隐藏窗口，托盘菜单可真正退出。", () => _s.CloseToTray, v => _s.CloseToTray = v);
        SwitchRow(page, "退出时停止服务", "退出启动器时同步结束由本程序启动的服务。", () => _s.StopServiceOnExit, v => _s.StopServiceOnExit = v);
        GroupTitle(page, "日志");
        SwitchRow(page, "写入日志文件", "按模块分文件写入 logs/ 目录。界面仍按模块分流显示。", () => _s.SaveLog, v => _s.SaveLog = v);
        // DEC-163（他拍 B 路径）：原来下面那枚「引擎调试日志（写全量）」整套退役 —— 八类引擎台账文件照记（简单去重），
        // 过滤只管界面。这一格的说明顺带把"记"的口径写清（旧句只提"隐藏"，会让人以为文件也不记）。
        SwitchRow(page, "调试信息过滤", "界面上隐藏引擎的调试与噪声行。只挡界面，日志文件照记。", () => _s.FilterDebugInfo, v => _s.FilterDebugInfo = v);
        // DEC-125②（用户点名）：开关一打开，下面立刻长出"这一行显示哪几档"的一排复选；关掉连那一排都不占位。
        // 说明压到一句（原来那句把三档名字抄了一遍 —— 现在那三个名字长在下面的行里，抄了就是两处家）。
        var swBar = SwitchRow(page, "显示状态栏", "窗口底部那一行数值；开着可在下面挑显示哪几档。",
            () => _s.ShowStatusBar, v => _s.ShowStatusBar = v,
            hairline: false);    // DEC-131①：这条线归那一块自己画（挂在行最后一像素的的老画法在"滚到底"时会丢）
        StatusBarOptionRows(page, swBar);
        return page;
    }

}
