using System.Text;
using System.Text.Json.Nodes;

namespace MultiLauncher;

// 开发者自检：MultiLauncher.exe --selftest <输出文件> [Strata 目录]
// 不开界面，直接跑一遍与界面无关的核心逻辑：模型扫描、版本读取、运行配置物化（含各种边界）、
// 更新检查的网络通路与版本比较。结果写进文件，供脚本核对；GUI 正常路径不经过这里。
internal static partial class SelfTest
{
    /// <summary>递归找一个子控件（弹窗那套是 Shell → Body 面板 → MdScroll，只在顶层找会扑空）。</summary>
    internal static T? FindChild<T>(Control parent) where T : Control
    {
        foreach (Control c in parent.Controls)
        {
            if (c is T hit) return hit;
            var deeper = FindChild<T>(c);
            if (deeper is not null) return deeper;
        }
        return null;
    }

    /// <summary>DEC-114②：自检跑完把自家那些草稿窝清掉（`data\tmp\selftest-*`、`uiprobe-*`、
    /// 以及 fit-root / root-probe / ninfer-fidelity 三个当年没走前缀命名的旧名字，名单在 `MainForm.IsTestScratch`）。
    /// 包一层 try/finally 而不是在几十个 return 上各点一次名 —— 漏一条就是"护栏没挡整条路"（DEC-111 的先例）。
    /// 报告写在调用方给的 outPath（不在 tmp 里），⓪ 那几条终检比的是**真实家底**（ini / 那层生效配置 / llama 与 ninfer 的备份格），
    /// 全在 RunBody 里跑完才返回，所以清 tmp 动不到任何判据。</summary>
    public static int Run(string root, string outPath)
    {
        try { return RunBody(root, outPath); }
        finally { try { MainForm.CleanTestScratch(); } catch { /* 清不动就算了：下次启动兜底扫按年龄收 */ } }
    }

    static int RunBody(string rootDir, string outPath)
    {
        sb = new StringBuilder();
        root = rootDir;   // 字段赋值点：之后 RunBody 里的 root 全部落在字段上（形参已改名 rootDir）
        // DEC-138（E-10）：总条数由 <see cref="Finish"/> 从**报告正文**数（PASS/FAIL 行的个数）。
        // 不在这里再挂一个计数器：`SelfTest.Gateway.cs` 那份 partial 有它自己的 Check 闭包，本地计数会少掉
        // 它贡献的三十几条 = 第二个家，而这条判据要防的恰恰是"整块静默没跑"。
        // 提升成字段后的赋值点（本体一字未改，见 SelfTest.State.cs 的同名注释）
        W = s => sb.AppendLine(s);
        // 2026-10-22（他问"自检为什么这么慢"）：每条判据之间隔了多久 —— 超过 1.5 秒就打一行 SLOW 点名。
        // 为什么不是给每条都打时间：正常报告要能扫读，只有"慢得异常"的那些才值得占一行。
        var swCheck = System.Diagnostics.Stopwatch.StartNew();
        long lastCheckMs = 0;
        Check = (name, ok, detail) =>
        {
            if (!ok) failures++;
            W((ok ? "PASS  " : "FAIL  ") + name + (detail.Length > 0 ? "  | " + detail : ""));
            var now = swCheck.ElapsedMilliseconds;
            if (now - lastCheckMs >= 1500) W($"SLOW  这一条花了 {now - lastCheckMs}ms：{name}");
            lastCheckMs = now;
        };

        // DEC-094 ⓪（第 0 轮排在最前面的那条欠账）：自检里的物化用例以前**直接写真实 data\run\** ——
        // BuildEffectiveConfig 落的就是引擎实际读的那一份（DEC-077 起「打开配置」也曾开它；DEC-122② 之后
        // 「打开配置」改开 MultiLauncher.ini，但每次启动照旧重写这份产物，护栏不能跟着撤），
        // 于是用户机器上跑一次 --selftest = 覆盖他的运行配置。今天看似无害（每次启动会重生成），
        // 但产物一旦变成"家底"就是数据损坏级风险。修法 = 一个覆盖位（RunConfigDir 一处收口）+ 一条逐字节对照。
        runTmp = MainForm.Scratch("selftest-run");
        try { Directory.CreateDirectory(runTmp); } catch { /* 建不出来就走系统临时目录那档，TempDir() 自己兜过 */ }
        MainForm.UseTempRunConfigDirForTest(runTmp);
        // 同一条护栏补到 llama 这一侧（DEC-094 ⓪ / DEC-085 批）：**家底 + 它的写前备份整窝改道**到
        // data\tmp\selftest-llama\，真件那一份只读地拷进去当用例输入 —— 以前激活 llama 页点「保存」那类用例
        // 会走 BackupRouterIni() + 就地写，等于在用户机器上跑一次自检就动一次他的家底、
        // 还在 data\llama\ 里留下一份"假历史"（连 DEC-084 那次改名都是这么被自检顺手做掉的）。
        llamaHome = MainForm.Scratch("selftest-llama");
        try
        {
            Directory.CreateDirectory(llamaHome);
            var realNow = MainForm.DefaultRouterIniPath();
            var realOld = Path.Combine(AppContext.BaseDirectory, MainForm.LegacyLlamaIniFileName);
            var srcIni = File.Exists(realNow) ? realNow : (File.Exists(realOld) ? realOld : null);
            if (srcIni is not null) File.Copy(srcIni, Path.Combine(llamaHome, MainForm.LlamaIniFileName), overwrite: true);
        }
        catch { /* 拷不进来就空着：要真件的用例本来就会自己跳过 */ }
        MainForm.UseTempLlamaHomeForTest(llamaHome);
        // DEC-096：同一条护栏立刻补到 Ninfer 这一侧（第三个模块进来 = 第三份家底）。
        // 真件只读拷进 data\tmp\selftest-ninfer\ 当用例输入；「缺才拷种子」那一步也跟着改道，
        // 否则跑一次自检就会在 exe 旁边凭空造出一份 ninfer_models.ini（并且把 data\infer\ 塞满假历史）。
        ninferHome = MainForm.Scratch("selftest-ninfer");
        try
        {
            Directory.CreateDirectory(ninferHome);
            var realNinfer = MainForm.DefaultNinferIniPath();
            if (File.Exists(realNinfer))
                File.Copy(realNinfer, Path.Combine(ninferHome, MainForm.NinferIniFileName), overwrite: true);
        }
        catch { /* 拷不进来就空着：要真件的用例本来就会自己跳过 */ }
        MainForm.UseTempNinferHomeForTest(ninferHome);
        // DEC-097③④：跑自检不许读 1.5 GB 的 exe 算 sha、更不许把 WSL 虚拟机拉起来（那是动他机器的全局状态）。
        MainForm.VersionProbeDisabledForTest = true;
        // DEC-111②：自检也不许把测试行写进用户的 logs\。以前这道闸只补在**第一个** MainForm 实例上
        //（SetSaveLogForTest(false) 全项目一处调用），而自检还有两个实例、出图台有五个 —— 他机器上那份
        // ninfer.log 末尾就躺着四行 21:54 那次自检写的「已解析 3 个 Ninfer 模型（…data\tmp\selftest-ninfer\…）」。
        // 现在按进程收口：入口点一下，本进程所有实例一律不落文件日志。
        MainForm.LogFileDisabledForTest = true;
        // DEC-114③：自检期间也不许跑启动兜底扫（同一族、同一道形状 = 按进程收口）。
        // 自检自己就住在 data\tmp\selftest-* 里跑，那条扫在跑的过程中过一遍会把用例脚下的输入删掉。
        MainForm.ScratchSweepDisabledForTest = true;
        // DEC-118：也不许跑布局搬家 —— 那一步会把真家底从老位置搬走，属于"动他东西"。
        // 搬家本身的判据在沙盒里喂样本验（见 DEC-120 那组用例），不靠真路径碰运气。
        MainForm.LayoutMigrationDisabledForTest = true;
        // DEC-125②：自检一律不起 nvidia-smi（一次 spawn 最长 3 秒，而且是问硬件 = 有副作用的读取，
        // 口径同 VersionProbeDisabledForTest 那条"进 VM / 读 1.5 GB exe 一律不做"）。
        // 显存/温度那两档的判据走纯函数喂样本，不靠真卡片。
        MainForm.GpuProbeDisabledForTest = true;
        // DEC-147（BUG-067 结案）：自检与出图台一律不许把「自动启动服务」挂上 Load ——
        // 自检这边不 Show 窗（Load 本来就不触发），真正会出事的是带 Show() 的探针分支，但两道闸同一处点名，
        // 别按实例收口（DEC-111② 那条"按实例关 = 漏三个自检实例 + 五个探针实例"的同族教训）。
        MainForm.AutoStartDisabledForTest = true;
        // DEC-164（BUG-068 第二条残留结案）：**开机自启那一枚 HKCU Run 键**也按进程收口。
        // 构造函数末尾那句 `SetAutoStart(_config.Current.AutoStartWithWindows)` 拿的是**本进程这只 exe**：
        // 他开着自启 = 值被改指到本地开发目录那只；他关着 = 他那枚值被**删掉**。两种都不该由一次自检决定。
        // 替身一起挂上：万一哪天这道闸被改坏，自检这一趟也只记账、绝不碰他的 Run 键（红/绿那一趟同理安全）。
        MainForm.AutoStartRegistryDisabledForTest = true;
        MainForm.AutoStartRegistryWriteForTest = _ => { };
        // DEC-160（BUG-068① 结案）：自检与出图台一律不真问端口 —— 外部服务探活读的是**真端口**，
        // 他壳在跑时 8077/8068/8078 上答的正是标准 OpenAI 列表与 {"status":"ok"}，形状判据全过 = 被认成「外部服务」，
        // 于是三条判据（模型行按钮 / 托盘组头 / 停用整组不出现）在他退壳时绿、开壳时红（r28 A 组那三条离群红 = BUG-068）。
        // "用户机器上恰好有什么在跑"不该决定用例红绿（BUG-052 同族，只是换到环境这一头）；那一档界面形状改由喂样本走真路径断。
        MainForm.ExternalProbeDisabledForTest = true;
        // DEC-202（本程序自更新）：这两道闸必须**先于**功能挂上 —— 第 4 条链要连 GitHub、还要换掉自己的 exe。
        //   ① 探一次就够，自检里一次都不许联网（那个"有没有新版"取决于上游今天发了什么，属于 BUG-052 同族：
        //      用户机器上恰好有什么不该决定用例红绿）；
        //   ② 替换流程一旦真跑，这一趟自检会把自己的 exe 改名 + 重启 = 一次自检把他装好的程序换掉。
        // 判据走纯函数喂样本 + 证人位（AppProbeRunsForTest 恒 0），不靠真联网。
        MainForm.AppUpdateProbeDisabledForTest = true;
        MainForm.AppSelfReplaceDisabledForTest = true;
        // DEC-127⑥（他点名："自检时经常弹『界面参数已保存』,这是必要的还是什么其他原因?能不能不弹这个"）：
        // 不必要 —— 自检里 `SaveParameterEditsForTest` 就是要点那一次**真保存**才能断"落盘"，而 toast 是给人的落点，
        // 宿主窗没显示时它会退回屏幕工作区照样 Show()，于是他在旁边看它一条一条闪（同 DEC-111② 那一族：按进程收口）。
        // 出图台**不开**这道闸：`--uiprobe toast` 那六张拍的就是真 toast，开了等于拍空气。
        Toast.DisabledForTest = true;
        // DEC-159（他批的第 1 条带出来的一道闸，DEC-127⑥ / DEC-111② 那一族的最后一处）：自检期间一律不真弹模态框 ——
        // `Dialogs.*` 走的是 `ShowDialog`，在无头跑里就是"整趟挂在那里等人来点"（上一轮红侧写成恒弹 = 挂了 15 分钟被强杀），
        // 真弹出来他坐在旁边也会看见。证人位两道：`AttemptsForTest`（这一击真走到了要弹）与 `ShownForTest`（闸上必须恒 0）。
        // 出图台**不开**这一位（`--uiprobe dialogs` 那八张拍的就是真弹窗），预览档 `Preview` 也不受影响。
        Dialogs.DisabledForTest = true;
        // DEC-138（M6 缺口 11）：自检里那几扇"弹窗族深色"的预览窗摆到画外 —— 同上一条的理由（他坐在旁边不该看见）。
        // 出图台**不设**这一位：那六张 / 八张拍的就是给人看的真位置。
        Dialogs.PreviewOffscreen = true;
        ninferIniBefore = SnapshotNinferIni();
        ninferBkBefore = SnapshotRunDir(MainForm.RealNinferIniBackupDir());
        ninferExeBkBefore = SnapshotRunDir(MainForm.RealNinferExeBackupDir());   // DEC-116：引擎 exe 单独一格，两格都要拍
        llamaIniBefore = SnapshotLlamaIni();
        llamaBkBefore = SnapshotRunDir(MainForm.RealRouterIniBackupDir());
        realRunBefore = SnapshotRunDir(MainForm.RealRunConfigDir());
        // DEC-138（M6 缺口 9）：Strata 的退路以前**不在 ⓪ 的对照之列** —— `data\backup\strata\`（配置备份那一格）与
        // `D:\Strata-data\backup\`（整份升级快照那一格）都没人拍。整份快照里是几百 MB 的源码 + 引擎，不读内容、
        // 只拍**子目录名单**：这条判据要防的是"自检把用户的退列出来 / 轮转掉 / 往里写一格"，那三件事在名单上都看得见。
        strataCfgBakBefore = SnapshotDirNames(MainForm.ModuleBackupDir("strata"));
        strataSnapBefore = SnapshotDirNames(MainForm.StrataBackupRoot(root));
        // DEC-103（BUG-049）：配置这份"家"也要吃同一条护栏 —— 跑前把真实 MultiLauncher.ini 拍下来、跑完逐字节比。
        // 原来没人拍它：「切后端」那条用例走的是真动作（OnNinferBackendSwap 会 SaveConfig 落 [Current]），
        // 每次自检都把整份真配置写一遍，还顺手把「保存闸门」用例的测试值（147456/131072 来回翻）带进用户的家底。
        cfgPathBefore = MainForm.ConfigPathForTest;
        cfgBytesBefore = null;
        try { if (File.Exists(cfgPathBefore)) cfgBytesBefore = File.ReadAllBytes(cfgPathBefore); } catch { }
        // DEC-118 之后这一层叫 data\config\strata\ —— 这两行话改成**从函数里取**，别再写字面量（写过一次就漂一次）
        W("物化改道: " + runTmp + "（真实 " + MainForm.DisplayDataPath(MainForm.RealRunConfigDir()) + "\\ 只读快照 " + realRunBefore.Count + " 份）");
        W("Ninfer 家底改道: " + ninferHome + "（真件 " + (ninferIniBefore.path is null ? "不在" : Path.GetFileName(ninferIniBefore.path))
          + " 只读拷入；真备份目录 " + ninferBkBefore.Count + " 份）");
        W("llama 家底改道: " + llamaHome + "（真件 " + (llamaIniBefore.path is null ? "不在" : Path.GetFileName(llamaIniBefore.path))
          + " 只读拷入；真备份目录 " + llamaBkBefore.Count + " 份）");

        try
        {
            W("root: " + root);
            W("data dir: " + MainForm.FindDataDir(root));
            W("物化改道（自检专用，真实 " + MainForm.DisplayDataPath(MainForm.RealRunConfigDir()) + "\\ 一份不写）: " + runTmp);
            localVersion = MainForm.ReadLocalVersion(root);
            var engineVersion = MainForm.ReadEngineVersion(root);
            W("local version: " + localVersion + "   engine version: " + engineVersion);
            Check("读到本地源码版本", localVersion.Length > 0);
            Check("读到引擎版本", engineVersion.Length > 0);

            models = MainForm.ScanModels(root);
            W("models: " + models.Count);
            foreach (var m in models) W("  - " + m.Display + "  ready=" + m.Ready + " note=" + m.Note);
            Check("扫描到至少一个模型配置", models.Count > 0);
            target = models.FirstOrDefault(m => m.Ready) ?? models.FirstOrDefault();
            if (target is null)
            {
                W("没有模型可测，结束（root = \"" + root + "\"；这一格不是 Strata 目录就会走到这里 —— "
                  + "第三个参数才是 Strata 目录，没带时读 ini 的 Root=，见 DEC-139）");
                CheckRunDirUntouched();
                return Finish(sb, outPath, failures);
            }
            W("测试用模型: " + target.Tag);

            // 2026-10-22（他问"自检为什么这么慢、能不能优化"）：每段各记一次耗时，报告末尾打一张表。
            // 没有这张表就只能靠猜 —— 而这一族最忌猜（以前的"慢"其实一直是网络那两条在等超时）。
            var swAll = System.Diagnostics.Stopwatch.StartNew();
            var timings = new List<string>();
            void Seg(string name, Action act)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                act();
                timings.Add(name + " " + sw.ElapsedMilliseconds + "ms");
            }

            Seg("扫描与估算", ScanAndSizes);
            Seg("物化与旋钮", MaterializeAndKnobs);
            Seg("模型参数文件", ModelSnapFileChecks);   // A（DEC-206/210）：[Model:*] 那一族的家搬到 strata_models.ini 之后的迁移、往返、命名

            Seg("模型列表重扫", ModelRescanChecks);     // DEC-222：拉起服务那一刻按家底戳重扫三个模块的模型列表

            Seg("界面布局", UiLayoutChecks);

            Seg("本程序更新", AppUpdateChecks);      // DEC-202：第 4 条链（本程序自更新）那一族 —— 纯函数喂样本，一次都不联网

            Seg("版本与引擎更新", VersionAndUpdateChecks);

            Seg("Ninfer", NinferChecks);

            Seg("网关与 llama", GatewayAndLlamaBackendChecks);

            W("各段耗时: " + string.Join("｜", timings) + "｜总计 " + swAll.ElapsedMilliseconds + "ms");
        }
        catch (Exception ex)
        {
            failures++;
            W("EXCEPTION: " + ex);
        }

        // 异常路径也要比对：半途炸了也不许已经在真实那层生效配置目录上动过笔
        CheckRunDirUntouched();
        return Finish(sb, outPath, failures);
    }

}
