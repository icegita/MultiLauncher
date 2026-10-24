namespace MultiLauncher;

// 控件库预览窗（开发用）：MultiLauncher.exe --uiprobe
// 铺主界面时随手验证自绘控件的观感与 DPI；不参与正常启动路径。
internal sealed partial class UiPreviewForm : Form
{
    public UiPreviewForm()
    {
        Text = "自绘控件预览 · Strata 启动器";
        BackColor = UiTheme.Window;
        ForeColor = UiTheme.Text;
        Font = new Font("Microsoft YaHei UI", 9f);
        AutoScaleMode = AutoScaleMode.None;
        StartPosition = FormStartPosition.CenterScreen;
        CreateControl();
        UiTheme.CaptureDpi(Handle);
        ClientSize = new Size(UiTheme.S(this, 600), UiTheme.S(this, 340));

        var head = new Label
        {
            Text = "自绘控件：圆角卡片 / 圆角输入框 / 实底按钮 / 胶囊开关",
            ForeColor = UiTheme.Text,
            AutoSize = true,
            BackColor = UiTheme.Window,
            Font = new Font("Microsoft YaHei UI", 10.5f),
        };
        head.Location = new Point(UiTheme.S(this, 24), UiTheme.S(this, 20));
        Controls.Add(head);

        var card = new RoundedPanel
        {
            Bounds = new Rectangle(UiTheme.S(this, 24), UiTheme.S(this, 60), UiTheme.S(this, 532), UiTheme.S(this, 210)),
        };

        void AddRow(int y, string labelText, Control right, string tip)
        {
            var lab = new Label
            {
                Text = labelText,
                ForeColor = UiTheme.TextMuted,
                AutoSize = false,
                BackColor = UiTheme.Card,
                TextAlign = ContentAlignment.MiddleLeft,
            };
            lab.SetBounds(UiTheme.S(this, 20), UiTheme.S(this, y), UiTheme.S(this, 76), UiTheme.S(this, 32));
            card.Controls.Add(lab);

            right.SetBounds(UiTheme.S(this, 104), UiTheme.S(this, y), right.Width, right.Height);
            card.Controls.Add(right);

            var note = new Label
            {
                Text = tip,
                ForeColor = UiTheme.TextMuted,
                AutoSize = false,
                BackColor = UiTheme.Card,
                TextAlign = ContentAlignment.MiddleLeft,
            };
            note.SetBounds(UiTheme.S(this, 108) + right.Width, UiTheme.S(this, y), UiTheme.S(this, 220), UiTheme.S(this, 32));
            card.Controls.Add(note);
        }

        var input = new RoundedTextBox { Backdrop = UiTheme.Card, Width = UiTheme.S(this, 150) };
        input.Text = "0.0.0.0";
        AddRow(20, "监听地址", input, "圆角输入框（原生 TextBox 做不到）");

        var btnLoad = new FlatButton { Backdrop = UiTheme.Card, Text = "加载", Width = UiTheme.S(this, 76) };
        AddRow(64, "加载", btnLoad, "绿色实底按钮（hover 变深）");

        var btnUnload = new FlatButton
        {
            Backdrop = UiTheme.Card,
            Text = "卸载",
            Width = UiTheme.S(this, 76),
            FillColor = UiTheme.Danger,
            HoverColor = UiTheme.DangerHover,
            TextColor = Color.White,
        };
        AddRow(108, "卸载", btnUnload, "危险按钮（红底）");

        var sw = new ToggleSwitch { Backdrop = UiTheme.Card, Checked = true };
        AddRow(152, "随系统启动", sw, "胶囊开关（点击可切换）");

        Controls.Add(card);
    }

    // DEC-138（E-12）：出图报告里那句"拍出 N 张"**必须数盘上真有几个文件**。
    // 以前 sidebar 那行写死 19（DEC-108 起这一支其实落 21 张，AGENTS 也写 21），
    // modelbadge / toast 那两处写的是 `rows.Count * 2 + 1` 这种**尝试数** —— PrintWindow 失败一张都不落盘，
    // 话术照样说"拍出来了"。判这趟可不可信要数文件（AGENTS「出图台那句是尝试数、不是落盘文件数」那条）。
    static string ShotCount(string dir, string says)
    {
        int n = 0;
        try { if (Directory.Exists(dir)) n = Directory.GetFiles(dir, "*.png").Length; } catch { /* 数不出来就说 0 */ }
        return "落盘 " + n + " 张" + (says.Length > 0 ? "（脚本自报 " + says + "）" : "") + " -> " + dir;
    }

    /// <summary>DEC-114②：出图台跑完把自家那些草稿窝清掉（判据 = `MainForm.IsTestScratch` 那一张名单，只有一个家）。
    /// 外面包一层 try/finally，**不是在八个探针分支的出口各点一次名** —— 漏一条就是"护栏没挡整条路"
    ///（这条写法的先例就是 DEC-111：当年按实例收口，漏掉三个自检实例与五个探针实例）。</summary>
    public static void Run(string[] args)
    {
        try { RunBody(args); }
        finally { try { MainForm.CleanTestScratch(); } catch { /* 清不动就算了：下次启动兜底扫按年龄收 */ } }
    }

    static void RunBody(string[] args)
    {
        // DEC-111②：出图台同样不许把探针行写进用户的 logs\（以前只有自检的第一个实例关了文件日志，
        // 这里五个建主窗的分支全都没关 —— 判"某条护栏有没有生效"要按进程收口，别按实例收口）。
        // 放在 try 之外：下面安顿家底那一段万一抛了，这道闸不许跟着一起没生效。
        MainForm.LogFileDisabledForTest = true;
        // DEC-114③：探针期间不许跑启动兜底扫 —— 探针自己就住在 data\tmp\uiprobe-* 里，
        // 让那条扫在跑的过程中过一遍 = 拿自己脚下的草稿当过期残留清掉。
        MainForm.ScratchSweepDisabledForTest = true;
        // DEC-118：探针实例也不许跑布局搬家（它会建 MainForm 构造函数，那一步会把真家底从老位置搬走）。
        MainForm.LayoutMigrationDisabledForTest = true;
        // DEC-125②：探针也不起 nvidia-smi（同一道按进程收口的闸，判据位与自检同源）。
        MainForm.GpuProbeDisabledForTest = true;
        // DEC-147（BUG-067 结案）：探针一律不许把「自动启动服务」挂上 Load。这一道就是为这一族带 `Show()`
        // 的分支留的 —— 他 bin 旁那份真实 ini 是 AutoStartStrata=True + AutoStartLlama=True，探针一 Show()
        // 就真去起服务，撞上端口上他自己在跑的实例会走到「结束外部进程」那枚模态确认（r25 那一趟挂 13 分钟不落一张图）。
        MainForm.AutoStartDisabledForTest = true;
        // DEC-164（BUG-068 第二条残留）：开机自启那一枚 HKCU Run 键同样不许碰 —— 探针建窗会调 SetAutoStart，
        // 而它拿的是探针那只 exe 的路径（他开着自启 = 值被改指、他关着 = 他那枚值被删掉）。
        MainForm.AutoStartRegistryDisabledForTest = true;
        MainForm.AutoStartRegistryWriteForTest = _ => { };
        MainForm.ExternalProbeDisabledForTest = true;   // DEC-160（BUG-068①）：探针也不许真问端口（判据同自检那一族）
        // DEC-202：出图台要拍页头徽标 / 关于页那两行，但**一张图都不许联网**（探测结果由 SetAppOfferForTest 喂，
        // 与"外部服务"那一档同一条口径：屏幕上的形状不许取决于上游今天发了什么）；更不许换掉自己。
        MainForm.AppUpdateProbeDisabledForTest = true;
        MainForm.AppSelfReplaceDisabledForTest = true;
        // DEC-097③④：出图台也不做那两趟有副作用的读取（读 1.5 GB 的 exe 算 sha / 进 VM 起 WSL），版本行拍缓存值那一档。
        // DEC-138（E-5）：这一道以前写在下面那个 try 的**最后一条** —— 前面任一 CreateDirectory / File.Copy 抛了，
        // catch 把它一起吞掉，闸就没上，探针真去读那支 1.5 GB 的文件、甚至把虚拟机拉起来。
        // 按进程收口的闸门全排在这里（try 之外），与上面那四道同一个形状。
        MainForm.VersionProbeDisabledForTest = true;
        // DEC-094 ⓪：出图台有几条分支会真造 MainForm（切页 / 保存 / 重扫都会走物化与写段那两条），
        // 与自检同一条护栏 —— 探针期间**运行配置与 llama 家底（含它的写前备份）整窝改道** data\tmp\，
        // 真实 data\config\strata\ 与两份家底 ini 一份不写（覆盖位只有 RunConfigDir / LlamaHome / NinferHome 三处，见 MainForm）。
        try
        {
            var probeRun = MainForm.Scratch("uiprobe-run");
            Directory.CreateDirectory(probeRun);
            MainForm.UseTempRunConfigDirForTest(probeRun);
            var probeLlama = MainForm.Scratch("uiprobe-llama");
            Directory.CreateDirectory(probeLlama);
            var realNow = MainForm.DefaultRouterIniPath();
            var realOld = Path.Combine(AppContext.BaseDirectory, MainForm.LegacyLlamaIniFileName);
            var src = File.Exists(realNow) ? realNow : (File.Exists(realOld) ? realOld : null);
            if (src is not null) File.Copy(src, Path.Combine(probeLlama, MainForm.LlamaIniFileName), overwrite: true);
            MainForm.UseTempLlamaHomeForTest(probeLlama);
            // DEC-096：第三个模块 = 第三份家底，同一条护栏照搬（出图台造 MainForm 会走"安顿家底"那一步）
            var probeNinfer = MainForm.Scratch("uiprobe-ninfer");
            Directory.CreateDirectory(probeNinfer);
            var realNinfer = MainForm.DefaultNinferIniPath();
            if (File.Exists(realNinfer))
                File.Copy(realNinfer, Path.Combine(probeNinfer, MainForm.NinferIniFileName), overwrite: true);
            MainForm.UseTempNinferHomeForTest(probeNinfer);
        }
        catch { /* 建不出来就不改道，别让出图整条断在这里 */ }

        // 最小窗口宽度实测（开发用）：MultiLauncher.exe --uiprobe minwidth <输出文件>
        // 必须走这条而不是 --selftest：--selftest 那条没设 HighDpiMode，进程是 DPI 无感知的（S() 恒等于 1.0），
        // 而 CJK 字宽在 125%/150% 下非整倍增长（BUG-007），量出来的最小值会偏乐观。
        // 这里按真实 PerMonitorV2 启动，构造主窗但不显示、不进消息循环，只把数字写进文件。
        if (args.Length >= 3 && args[1] == "minwidth")
        {
            using var f = new MainForm();
            f.ClearStartHiddenForTest();   // DEC-107：探针实例不吃他配置里的「启动时隐藏到托盘」（否则屏幕取图全废）
            f.CreateControl();
            var sbw = new System.Text.StringBuilder();
            // DEC-096：三页各量一次（DEC-088 那笔"卡1 三格改窄 → 最小客户宽 898 ✓ 900 不动"的账，
            // 只有按页量出来才算核过；Ninfer 页多一枚方块，账更紧）
            foreach (var key in new[] { "strata", "llama", "ninfer" })
            {
                f.SwitchModuleForTest(key);
                for (int i = 0; i < 6; i++) { Application.DoEvents(); Thread.Sleep(30); }
                var (client, detail) = f.MinClientWidthForTest();
                sbw.AppendLine($"DPI={UiTheme.Dpi} 页={key} 最小客户宽={client} 逻辑像素（{detail}；" +
                               $"当前 ClientSize 宽={UiTheme.DpiToLogical(f.ClientSize.Width)}）");
            }
            File.WriteAllText(args[2], sbw.ToString());
            return;
        }

        // 换后端耗时实测（开发用，DEC-097 排障）：MultiLauncher.exe --uiprobe nswap <输出文件>
        // 用户实跑"WSL→WIN 卡十几秒、WIN→WSL 正常"，读代码点不出那一步 → 直接把两个方向各走几次，
        // 把 OnNinferBackendSwap 里那把量尺写出来的行捞进文件（没到 800ms 的步不会出面，所以空着就是"都很快"）。
        if (args.Length >= 3 && args[1] == "nswap")
        {
            using var f = new MainForm();
            f.ClearStartHiddenForTest();   // DEC-107：探针实例不吃他配置里的「启动时隐藏到托盘」
            f.CreateControl();
            f.SwitchModuleForTest("ninfer");
            var sbn = new System.Text.StringBuilder();
            // DEC-138（E-6）：这一支以前**开着真家底跑** —— `SwapNinferBackendForTest` 每点一次就走
            // `SaveConfig(commitParams: false)`，把他 bin 旁边那份真实 MultiLauncher.ini 整份重写六遍
            //（AGENTS 明写那份存的是他的真实配置、不许删），中途停掉还把他的后端留在反的那一侧。
            // DEC-103/BUG-049 当年只把**自检**那一支改道到副本，出图台这一支漏了
            //（全仓 grep `UseTempConfigForTest`：自检 7 处、出图台 0 处）。现在同一条三步：
            // 拷副本 → 指过去 → 出口指回来，并在报告里留一条"真 ini 跑前跑后逐字节没变"的终检。
            string ShaOf(string path)
            {
                try { return File.Exists(path) ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))) : "(不在)"; }
                catch (Exception ex) { return "(读不动:" + ex.GetType().Name + ")"; }
            }
            var realIni = MainForm.ConfigPathForTest;
            var swapIni = MainForm.Scratch("uiprobe-nswap.ini");
            string iniBefore = ShaOf(realIni);
            try { if (File.Exists(realIni)) File.Copy(realIni, swapIni, overwrite: true); }
            catch (Exception ex)
            {
                File.WriteAllText(args[2], "副本没拷成，这一支不跑（免得写到他家底）：" + ex.Message);
                return;
            }
            MainForm.UseTempConfigForTest(swapIni);
            for (int i = 0; i < 6; i++)
            {
                var before = f.NinferBackendForTest;      // 这里量的是"后端"，与上面那份 ini 的哈希无关
                int n0 = f.LogHistoryCountForTest;
                var t0 = System.Diagnostics.Stopwatch.StartNew();
                f.SwapNinferBackendForTest();
                for (int k = 0; k < 20; k++) { Application.DoEvents(); Thread.Sleep(20); }
                sbn.AppendLine($"第 {i + 1} 次：{before} → {f.NinferBackendForTest}，整趟 {t0.ElapsedMilliseconds}ms，" +
                               "日志新增：" + string.Join(" ⏎ ", f.LogHistorySinceForTest(n0)));
            }
            MainForm.UseTempConfigForTest(realIni);        // 指回真路径：那一份从头到尾没被写过，写的全在副本里
            sbn.AppendLine("⓪ 终检（DEC-138 E-6）：真实 MultiLauncher.ini 跑前跑后 = " +
                           (iniBefore == ShaOf(realIni) ? "逐字节没动" : "**动了！** " + iniBefore + " → " + ShaOf(realIni)) +
                           "；被写六遍的那份副本在 " + swapIni + "（想看它变成什么样就打开它）");
            File.WriteAllText(args[2], sbn.ToString());
            return;
        }

        // 标题栏配色探针（开发用）：MultiLauncher.exe --uiprobe titlebar
        // 只画「窗体底色 + 一块卡片」，用来看标题栏接不接得上；不建 MainForm，
        // 因此不占单实例互斥体，也不会与用户正在跑的外壳抢端口 / 写 MultiLauncher.ini。
        if (args.Length >= 2 && args[1] == "titlebar")
        {
            Application.Run(TitleBarProbe());
            return;
        }
        // 弹窗族出图（2026-10-03 反馈③）：MultiLauncher.exe --uiprobe dialogs <输出目录>
        // 真组件 + 假数据，每个形态一张 PNG（PrintWindow 连 DWM 的深色标题栏一起拍）。
        // 不建 MainForm：不占单实例互斥体，也不会与用户正在跑的外壳抢端口 / 写 MultiLauncher.ini。
        if (args.Length >= 3 && args[1] == "dialogs")
        {
            using var owner = new Form
            {
                Text = "出图台 · 弹窗族",
                FormBorderStyle = FormBorderStyle.FixedToolWindow,
                ClientSize = new Size(240, 40),
                StartPosition = FormStartPosition.Manual,
                Location = new Point(20, 20),
            };
            owner.CreateControl();
            UiTheme.CaptureDpi(owner.Handle);
            int n = Dialogs.RunPreview(args[2], owner);
            Console.WriteLine("dialogs: " + ShotCount(args[2], n + "（尝试数）") +
                              "  (DPI=" + UiTheme.Dpi + ")");        // DEC-138（E-12）
            return;
        }

        // 设置页「检查更新」那一行的三种状态（①）：settings <目录>
        // 走真控件真刷新：MainForm.SetUpdateOfferForTest → PokeUpdateUi → SettingsForm.RefreshUpdateRows，
        // 所以拍出来的行显/隐与真机一致（不是画出来的示意图）。
        if (args.Length >= 3 && args[1] == "settings")
        {
            Directory.CreateDirectory(args[2]);
            using var main = new MainForm();
            main.ClearStartHiddenForTest();   // DEC-107：探针实例不吃他配置里的「启动时隐藏到托盘」（否则屏幕取图全废）
            main.CreateControl();
            UiTheme.CaptureDpi(main.Handle);
            using var sf = new SettingsForm(main, new Settings());
            sf.ShowTabForTest(1);            // Strata 节
            // 2026-10-23：先按**真机默认窗高**量一遍每页的滚动现场（下面那句 ClientSize=1090 是为了截图看得全，
            // 会把这个症状掩掉 —— 他就是在默认窗高下看到关于页挂着一条没用的滑条）。
            sf.Show();
            for (int pg = 0; pg < 5; pg++)
            {
                sf.ShowTabForTest(pg);
                for (int i = 0; i < 4; i++) { Application.DoEvents(); Thread.Sleep(40); }
                Console.WriteLine(sf.PageScrollDiagForTest(pg));
            }
            // BUG-086 的现场：**默认窗高**下的关于页（旧版这里右边缘会挂一条两成高的空滑条）
            sf.ShowTabForTest(4);
            for (int i = 0; i < 8; i++) { Application.DoEvents(); Thread.Sleep(40); }
            CaptureWindow(sf, Path.Combine(args[2], "settings-00-关于页-默认窗高.png"));
            sf.ShowTabForTest(1);
            sf.Hide();
            // 出图要一次看全「维护」组：把窗拉高（真机仍是 640，这里只为截图）
            sf.ClientSize = new Size(sf.ClientSize.Width, UiTheme.S(sf, 1090));
            sf.Show();
            void Shoot(string name)
            {
                for (int i = 0; i < 6; i++) { Application.DoEvents(); Thread.Sleep(50); }
                CaptureWindow(sf, Path.Combine(args[2], name));
            }
            // DEC-058：更新那三行搬进「更新」窗，设置页两节各自只剩「版本 + 更新…」，一张看全就够
            Shoot("settings-01-Strata节-版本与更新入口.png");
            sf.ShowTabForTest(2);
            Shoot("settings-02-llama节-版本与更新入口.png");
            sf.ShowTabForTest(3);       // DEC-096：第三个模块 = Ninfer 节（共用 → WSL → WIN 三组）
            Shoot("settings-03-Ninfer节-两枚后端开关与更新入口.png");
            // DEC-128③⑤：通用页那一块 = **横排**的复选格（两态同框）+ 拖动出来的次序 + **窗高恒定**
            //（作废 DEC-127④ 的"随开关长缩"：那排显隐只改页面内容长度，滚动条自己跟）。
            sf.ShowTabForTest(0);
            for (int i = 0; i < 8; i++) { Application.DoEvents(); Thread.Sleep(50); }
            int hFixed = sf.ClientSize.Height;
            Console.WriteLine($"通用页：客户高={hFixed}（本趟为看全各节探针把窗拉到 1090 逻辑；'恒定'看关掉后的对照）"
                              + $" 页内容高={sf.StatPageContentHeightForTest()} 那一块={sf.StatBlockHeightForTest}");
            sf.ScrollStatRowsIntoViewForTest();      // 那一块滚到看得见再拍（它在通用页最末尾）
            for (int i = 0; i < 6; i++) { Application.DoEvents(); Thread.Sleep(50); }
            Shoot("settings-04-通用页-状态栏那一块横排复选.png");
            var rowsRect = sf.StatRowsBoundsForTest();
            Console.WriteLine($"那一块在客户区里的矩={rowsRect}");
            CaptureCrop(sf, rowsRect, Path.Combine(args[2], "settings-05-复选两态-选中与未选.png"), 2);
            sf.SetStatRowHoverForTest(true);          // hover 那一档也要看得见（描边提亮 / 实底转深）
            for (int i = 0; i < 6; i++) { Application.DoEvents(); Thread.Sleep(50); }
            CaptureCrop(sf, sf.StatRowsBoundsForTest(), Path.Combine(args[2], "settings-06-复选-hover态.png"), 2);
            sf.SetStatRowHoverForTest(false);
            sf.ShowDragRingForTest("decode", 2);      // DEC-140：拖动态那一圈 = 卡片同款圆角 + 淡一档品牌绿（他："方框太丑了"）
            for (int i = 0; i < 6; i++) { Application.DoEvents(); Thread.Sleep(50); }
            CaptureCrop(sf, sf.StatRowsBoundsForTest(), Path.Combine(args[2], "settings-06b-拖动态-源格圆角描边与落点条.png"), 2);
            sf.ShowDragRingForTest("", -1);
            sf.MoveStatRowForTest("temp", 0);         // 拖一格：次序变了 = 状态栏那串也跟着变（第 2 条）
            for (int i = 0; i < 6; i++) { Application.DoEvents(); Thread.Sleep(50); }
            CaptureCrop(sf, sf.StatRowsBoundsForTest(), Path.Combine(args[2], "settings-07-拖动之后的次序.png"), 2);
            // DEC-128⑤ 的对照：拨关那一块 → 窗高**一个像素不变**（只是页内容变短），拍一张为证
            sf.StatusBarMasterForTest.Checked = false;
            for (int i = 0; i < 6; i++) { Application.DoEvents(); Thread.Sleep(50); }
            Shoot("settings-08-关掉那一块-窗高不变.png");
            Console.WriteLine($"关掉后：客户高={sf.ClientSize.Height}（期望仍 {hFixed}）"
                              + $" 页内容高={sf.StatPageContentHeightForTest()}");
            sf.StatusBarMasterForTest.Checked = true;
            sf.Close();
            Console.WriteLine("settings: " + ShotCount(args[2], "9"));      // DEC-138（E-12）：数文件，不抄话术（DEC-140 加第 06b 张拖动态）
            return;
        }
        // DEC-130①：真窗高（640）下的那一块 —— 上面 settings 组把窗拉到 1090 是为了看全各节，
        // **拍不出"滚到底时块底贴不贴视口边"**；这一组按真实窗高走他实跑的流程
        //（默认配置 → 带进视野 → 拨关再开 → 滚到最底），修根因后底排下沿要有页底留白的余量。
        if (args.Length >= 3 && args[1] == "settingsreal")
        {
            Directory.CreateDirectory(args[2]);
            using var mainR = new MainForm();
            mainR.ClearStartHiddenForTest();
            mainR.CreateControl();
            UiTheme.CaptureDpi(mainR.Handle);
            using var sfR = new SettingsForm(mainR, new Settings());
            sfR.ShowTabForTest(0);
            sfR.Show();
            void SettleR() { for (int i = 0; i < 8; i++) { Application.DoEvents(); Thread.Sleep(50); } }
            SettleR();
            sfR.ScrollStatRowsIntoViewForTest(); SettleR();
            CaptureWindow(sfR, Path.Combine(args[2], "settingsreal-01-真窗高-块带进视野.png"));
            sfR.StatusBarMasterForTest.Checked = false; SettleR();
            sfR.StatusBarMasterForTest.Checked = true; SettleR();
            sfR.StatPageScrollToMaxForTest(); SettleR();
            CaptureWindow(sfR, Path.Combine(args[2], "settingsreal-02-拨开关再滚到底.png"));
            CaptureCrop(sfR, sfR.StatRowsBoundsForTest(), Path.Combine(args[2], "settingsreal-03-块近景2x.png"), 2);
            Console.WriteLine($"settingsreal: 块高={sfR.StatBlockHeightForTest} 页内容={sfR.StatPageContentHeightForTest()}"
                + $" 补范围={sfR.ScrollBottomLastNeedForTest}（喊过 {sfR.ScrollBottomSyncCallsForTest} 次）"
                + $" 九格合矩={sfR.StatRowsBoundsForTest()}");
            sfR.Close();
            return;
        }

        ProbeAppUpdate(args);      // DEC-202：--uiprobe appupdate <目录>（页头徽标两态 + 关于页两行）
        ProbeSwapDrill(args);      // DEC-202：--uiprobe swapdrill <目录>（helper 真跑演练：ok 路径 + 回滚路径）

        // toast 出图（DEC-066；2026-10-04 摆位改成窗口中下）：MultiLauncher.exe --uiprobe toast <目录>
        // 真 Toast 组件 + 假文案：四态各一张，再拍一张"贴着宿主窗（水平居中、距底 1/4 高）堆叠"的场景图。
        ProbeToastAndModelBadge(args);

        ProbeUpdateAndSidebar(args);

        ProbeWipe(args);   // 2026-10-16 §7.5：--uiprobe wipe <目录>（同样不自己退，跑完按 PID 收）


        Application.Run(new UiPreviewForm());
    }

}