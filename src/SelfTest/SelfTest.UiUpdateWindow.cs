namespace MultiLauncher;

// 界面排布组（更新窗段）：DEC-107 一模块一窗/一后端一窗、DEC-097④ 版本固定、DEC-110④ 维护组同形、
// DEC-107 七条落点、DEC-097 补（更新说明的鼠标拖选）。2026-10-15 从 SelfTest.UiLayout.cs 按子域切出，
// 块内一行未改；共享状态（uiForm/sf/uf/lf/nf/wf）见 SelfTest.State.cs。
internal static partial class SelfTest
{
    internal static void UiUpdateWindowChecks()
    {
                // ============ DEC-107：Ninfer 一个模块、两条更新链 → **一后端一窗**（他批复：之前说好分开做、各自的弹窗） ============
                // 用户口径：「ninfer更新弹窗没有分开,之前说好是2个模块分开做,各自的弹窗」——DEC-097③ 的"一窗两段"
                // 按设计稿支点 F 原本的"一后端一窗"改回来。另两模块那一窗的形状必须一字不变，所以按表穷举各断一条。
                nf = new UpdateForm(uiForm, "ninfer");              // 默认 side=win：三行窗、主段标题自报名
                nf.CreateControl();
                wf = new UpdateForm(uiForm, "ninfer", "wsl");       // 新那扇：WSL 窗（一行版本 + 一枚「更新…」）
                wf.CreateControl();
                Check("DEC-107 分窗只有 Ninfer 有（Strata / llama 两窗拿不到 WSL 窗；ninfer 的 win 窗也不再挂第二段）",
                    uf.SecondaryForTest().Length == 0 && lf.SecondaryForTest().Length == 0
                    && nf.SecondaryForTest().Length == 0
                    && wf.SecondaryForTest().Contains("WSL 后端"),
                    "strata=[" + uf.SecondaryForTest() + "] llama=[" + lf.SecondaryForTest()
                    + "] ninfer-win=[" + nf.SecondaryForTest() + "] ninfer-wsl=[" + wf.SecondaryForTest() + "]");
                Check("DEC-107 两扇窗的标题各自报名（一模块一窗改成两窗后，不说清开的是哪一扇）",
                    nf.PrimaryTitleForTest == "WIN 后端" && wf.PrimaryTitleForTest == "WSL 后端"
                    && uf.PrimaryTitleForTest == "Strata" && lf.PrimaryTitleForTest == "llama.cpp",
                    nf.PrimaryTitleForTest + " / " + wf.PrimaryTitleForTest + " / " + uf.PrimaryTitleForTest + " / " + lf.PrimaryTitleForTest);
                Check("DEC-107 WSL 窗那一行：值落在值列、按钮恒最右一枚（与主窗那三行同一硬口径）",
                    wf.SecondaryAlignsForTest() && wf.ButtonsFitForTest(), wf.SecondaryForTest());
                foreach (var v in uiForm.ModuleViews)
                    Check("模块表：" + v.DisplayName + " 那一窗的分段 = "
                          + (v.Secondary is null ? "没有 WSL 窗" : "「" + v.Secondary.Title + "」· 按钮「" + v.Secondary.ActionLabel + "」"),
                        v.Key == MainForm.NinferKey ? v.Secondary is not null : v.Secondary is null);
                Check("DEC-097③ 测试期间绝不自动进 VM 读版本（探一次会把虚拟机拉起来 = 动他机器的全局状态）",
                    MainForm.VersionProbeDisabledForTest && uiForm.NinferWslVersionTextForTest.Length > 0,
                    uiForm.NinferWslVersionTextForTest);
                // 窗标题（他的第二扇窗要能从标题栏认出来）：win 那扇 = 更新 · Ninfer · WIN 后端；wsl 那扇 = … · WSL 后端
                Check("DEC-107 窗标题带后端名（两扇开在一起也分得清）",
                    nf.Text == "更新 · Ninfer · WIN 后端" && wf.Text == "更新 · Ninfer · WSL 后端"
                    && uf.Text == "更新 · Strata",
                    nf.Text + " / " + wf.Text + " / " + uf.Text);

                // ============ DEC-097④：版本查过之后固定（旧行为 = 点过一次「更新…」才看得见） ============
                var ninferDirBak = uiForm.NinferDirForTest;
                var verDir = MainForm.Scratch("ninfer-ver");
                try
                {
                    Directory.CreateDirectory(verDir);
                    File.WriteAllBytes(Path.Combine(verDir, "ninfer-serve-89.exe"), new byte[] { 0x4d, 0x5a });
                    uiForm.SetNinferDirForTest(verDir);
                    uiForm.SetNinferWinVersionCacheForTest("engine-v0.12.0-x", "aabbcc");
                    uiForm.SetNinferWinShaForTest("aabbcc");
                    Check("DEC-097④ 本地 sha 与缓存那对得上 → 直接显示缓存版本（不联网、不必再点一次检查）",
                        uiForm.NinferVersionTextForTest == "engine-v0.12.0-x", uiForm.NinferVersionTextForTest);
                    uiForm.SetNinferWinShaForTest("ffee1234567890abcdef");
                    Check("DEC-097④ exe 被换过（sha 对不上）→ 报「未知构建 + sha 前 16」，不报一个过时的号",
                        uiForm.NinferVersionTextForTest.Contains("未知构建") && !uiForm.NinferVersionTextForTest.Contains("engine-v0.12.0-x"),
                        uiForm.NinferVersionTextForTest);
                    uiForm.SetNinferWinShaForTest(null);
                    Check("DEC-097④ 还没算出 sha 时不猜（自检期间明说「未读」，真界面里是「读取中…」）",
                        uiForm.NinferVersionTextForTest.StartsWith("未读"), uiForm.NinferVersionTextForTest);
                    // DEC-198（2026-10-20 他实跑：02:15 升到 …-20261020，03:02 手动回退后左栏一直报「未知构建」）：
                    // 「回退过」与「没出处」是两件事 —— 回退的那一支出处就是那条备份（文件名带时间戳），
                    // 这一档必须说清，不能再退化成「未知构建」。
                    uiForm.SetNinferWinShaForTest("ffee1234567890abcdef");
                    uiForm.SetNinferRolledBackFromForTest("ninfer-serve-20261020-030252.exe");
                    Check("DEC-198 本会话回退过引擎 → 版本行说「已回退（备份 <文件名>）」，不报「未知构建」",
                        uiForm.NinferVersionTextForTest.Contains("已回退")
                        && uiForm.NinferVersionTextForTest.Contains("ninfer-serve-20261020-030252.exe")
                        && !uiForm.NinferVersionTextForTest.Contains("未知构建"),
                        uiForm.NinferVersionTextForTest);
                    Check("DEC-198 左栏那格窄地上缩成一个词「已回退」（全文照旧留给设置页与更新窗）",
                        MainForm.ShortVersionForSidebar(uiForm.NinferVersionTextForTest) == "已回退",
                        MainForm.ShortVersionForSidebar(uiForm.NinferVersionTextForTest));
                    Check("DEC-198 回退来源为空串时一个字都不多说（不造「已回退（）」这种空话）",
                        MainForm.NinferRolledBackText("") == "" && MainForm.NinferRolledBackText("  ") == "", "ok");
                    uiForm.SetNinferRolledBackFromForTest("");
                    Check("DEC-198 清掉回退来源后回到原来那一档（exe 被换过 → 未知构建）",
                        uiForm.NinferVersionTextForTest.Contains("未知构建"), uiForm.NinferVersionTextForTest);
                    uiForm.SetNinferWinShaForTest(null);
                    // DEC-198 第二轮（2026-10-20 他第二次点名：「现在还是显示未知构建?」）：
                    // 「已回退」以前只认本会话那个字段 → 重启外壳就没了，而"已经回退过、之后一直显示未知构建"
                    // 正是他看到的形状。判据补一条**从备份目录推**：当前 exe 的 sha 与备份里某一份逐字节相同
                    // = 这一支是回退来的。三面：命中 / sha 对不上不误认 / 目录为空不误报。
                    {
                        var homeBakV = Path.GetDirectoryName(MainForm.NinferIniPath())!;
                        var verHome = MainForm.Scratch("selftest-ninfer-rollback");
                        try
                        {
                            var bakDir = Path.Combine(verHome, "backups-exe");
                            Directory.CreateDirectory(bakDir);
                            var bakPath = Path.Combine(bakDir, "ninfer-serve-20261020-030252.exe");
                            File.WriteAllBytes(bakPath, new byte[] { 0x4d, 0x5a, 0xAB, 0xCD });
                            var bakSha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(bakPath))).ToLowerInvariant();
                            MainForm.UseTempNinferHomeForTest(verHome);
                            uiForm.SetNinferWinVersionCacheForTest("engine-v0.12.0-x", "aabbcc");
                            uiForm.SetNinferWinShaForTest(bakSha);
                            Check("DEC-198 当前 exe 与备份里某一份逐字节相同 → 认作「已回退」，重启后照样认得出（不吃会话状态）",
                                uiForm.NinferVersionTextForTest.Contains("已回退")
                                && uiForm.NinferVersionTextForTest.Contains("ninfer-serve-20261020-030252.exe")
                                && !uiForm.NinferVersionTextForTest.Contains("未知构建"),
                                uiForm.NinferVersionTextForTest);
                            uiForm.SetNinferWinShaForTest("ffee1234567890abcdef");
                            Check("DEC-198 备份里没有与当前 exe 相同的 sha → 不许乱认（仍报未知构建）",
                                uiForm.NinferVersionTextForTest.Contains("未知构建"), uiForm.NinferVersionTextForTest);
                            Directory.Delete(bakDir, true);
                            Check("DEC-198 备份目录为空时不许误报「已回退」",
                                uiForm.NinferVersionTextForTest.Contains("未知构建"), uiForm.NinferVersionTextForTest);
                            uiForm.SetNinferWinShaForTest(null);
                        }
                        finally
                        {
                            MainForm.UseTempNinferHomeForTest(homeBakV);
                            uiForm.SetNinferWinVersionCacheForTest("", "");
                            uiForm.SetNinferWinShaForTest(null);
                            try { MainForm.TryDeleteTree(verHome); } catch { /* 清不掉不影响结论 */ }
                        }
                    }
                    uiForm.SetNinferDirForTest("");
                    Check("DEC-097④ 引擎目录不对时说的是引擎不在（不是「未知」）",
                        uiForm.NinferVersionTextForTest.StartsWith("引擎不在"), uiForm.NinferVersionTextForTest);
                }
                finally
                {
                    uiForm.SetNinferDirForTest(ninferDirBak);
                    uiForm.SetNinferWinVersionCacheForTest("", "");
                    uiForm.SetNinferWinShaForTest(null);
                    try { Directory.Delete(verDir, true); } catch { /* 清不掉不影响结论 */ }
                }
                // 「固定」要活得过重启 = 那一对键真的进得出 ini。写在 data\tmp\ 的副本上（口径同 DEC-092 那批）
                {
                    var iniReal2 = MainForm.ConfigPathForTest;
                    var iniTmp2 = MainForm.Scratch("selftest-ninfer-ver.ini");
                    if (File.Exists(iniReal2)) File.Copy(iniReal2, iniTmp2, overwrite: true);
                    MainForm.UseTempConfigForTest(iniTmp2);
                    try
                    {
                        uiForm.SetNinferWinVersionCacheForTest("engine-v0.12.0-x", "aabbcc");
                        uiForm.SaveWithoutCommitForTest();
                        Check("DEC-097④ 「版本 + 当时的 sha」这一对真的落进 MultiLauncher.ini（重启后还读得到）",
                            uiForm.CurrentValueInIniForTest("NinferWinVersion") == "engine-v0.12.0-x"
                            && uiForm.CurrentValueInIniForTest("NinferWinVersionSha") == "aabbcc",
                            uiForm.CurrentValueInIniForTest("NinferWinVersion") + " / " + uiForm.CurrentValueInIniForTest("NinferWinVersionSha"));
                    }
                    finally
                    {
                        MainForm.UseTempConfigForTest(iniReal2);
                        uiForm.SetNinferWinVersionCacheForTest("", "");
                        try { File.Delete(iniTmp2); } catch { /* 留着也无妨 */ }
                    }
                }

                // ㉑ DEC-106：WSL 更新链（原壳 git+cmake 那条整体搬运）。脚本构造与解析全是纯函数；
                // 真正进 VM 的两趟（重编 / 回退）归用户实跑 —— 自检这边钉"拼进脚本的东西长什么样 + 谁都不许进 VM"。
                var ck = MainForm.NinferWslCheckScript("/home/user/ninfer-4090-yarn/build/apps/ninfer-serve", "");
                Check("DEC-106 检查脚本：仓库根从 exe 那格反推（rev-parse --show-toplevel）、fetch 失败能认出来",
                    ck.Contains("B='/home/user/ninfer-4090-yarn/build/apps/ninfer-serve'")
                    && ck.Contains("rev-parse --show-toplevel") && ck.Contains("git fetch origin")
                    && ck.Contains("FETCH_FAIL") && ck.Contains("refs/remotes/origin/HEAD"), ck.Split('\n')[0]);
                // DEC-110④：更新源那一格非空时，取的那一头与比的那一个引用都要跟着换（FETCH_HEAD），
                // 且**不许**留下 `git fetch origin`（那就等于填了没生效）；留空那条一字不变 = 上面这条在守。
                var cku = MainForm.NinferWslCheckScript("/b/ninfer-serve", "https://github.com/alanthinker/ninfer-4090-yarn");
                Check("DEC-110④ 更新源非空：显式从那一头取分支、比的是 FETCH_HEAD，且不再走 origin",
                    cku.Contains("git fetch 'https://github.com/alanthinker/ninfer-4090-yarn' \"$BR\"")
                    && cku.Contains("git rev-parse FETCH_HEAD") && !cku.Contains("git fetch origin"),
                    cku.Replace("\n", " ｜ "));
                Check("DEC-110④ 更新源的形状关：坏地址（带 shell 元字符 / 不是仓库地址）一律不收，留空照收",
                    MainForm.NinferWslUrlOk("") && MainForm.NinferWslUrlOk("https://github.com/a/b")
                    && MainForm.NinferWslUrlOk("git@github.com:a/b.git")
                    && !MainForm.NinferWslUrlOk("https://x/$(rm -rf /)") && !MainForm.NinferWslUrlOk("a b; ls")
                    && !MainForm.NinferWslUrlOk("ftp://x/y") && !MainForm.NinferWslUrlOk("rm -rf /"), "ok");
                var sy = MainForm.NinferWslSyncScript("/b/ninfer-serve", "", "main");
                Check("DEC-106 同步脚本：脏树不构建 + ff-only 同步（绝不产生本地提交）",
                    sy.Contains("git status --porcelain") && sy.Contains("git merge --ff-only 'origin/main'")
                    && sy.Contains("git checkout 'main'") && sy.Contains("DIRTY")
                    && !sy.Contains("git fetch"), "走 origin 时同步前不多 fetch 一次（旧行为）：" + sy.Replace("\n", " ｜ "));
                var syu = MainForm.NinferWslSyncScript("/b/ninfer-serve", "https://github.com/a/b", "dev");
                Check("DEC-110④ 同步脚本走更新源那一头：先 fetch 再 merge FETCH_HEAD（不写他仓库的 remote 配置）",
                    syu.Contains("git fetch 'https://github.com/a/b' \"$BR\"")
                    && syu.Contains("git merge --ff-only FETCH_HEAD") && !syu.Contains("origin/dev"),
                    syu.Replace("\n", " ｜ "));
                var bd = MainForm.NinferWslBuildScript("/b/ninfer-serve");
                Check("DEC-106 重编脚本：缺 CMakeCache 先 configure（旧壳同参数）+ 目标 `ninfer ninfer-serve`",
                    bd.Contains("if [ ! -f build/CMakeCache.txt ]")
                    && bd.Contains("-DCMAKE_BUILD_TYPE=Release -DNINFER_BUILD_APPS=ON -DBUILD_TESTING=OFF -DNINFER_BUILD_BENCHMARKS=OFF")
                    && bd.Contains("cmake --build build -j --target ninfer ninfer-serve"), bd.Replace("\n", " ｜ "));
                var kp = MainForm.NinferWslSnapshotScript("/b/ninfer-serve");
                Check("DEC-106 快照脚本：按当前短提交存一份 + 按 mtime 只留最近 3 份（tail -n +4，DEC-110⑤ 由 5 份改下来）",
                    kp.Contains("cp -f build/apps/ninfer-serve")
                    && kp.Contains("$HOME/.ninfer-launcher/snapshots") && kp.Contains("tail -n +4")
                    && kp.Contains("xargs -0")          // DEC-136（B-4）：带空格的老快照名以前会被 xargs 按空白拆开，
                                                        //   而脚本开头 cd 在用户仓库根 = 删到人家仓库里的同名目录
                    && !kp.Contains("tail -n +6"),
                    kp.Replace("\n", " ｜ "));
                var rb = MainForm.NinferWslRollbackScript("/b/ninfer-serve", "1a2b3c4");
                Check("DEC-106 回退脚本：先确认快照在、再 checkout、再拷回（免重编 = 秒级）",
                    rb.IndexOf("NOSNAP", StringComparison.Ordinal) < rb.IndexOf("git checkout", StringComparison.Ordinal)
                    && rb.Contains("git checkout 1a2b3c4")
                    && rb.Contains("cp -f \"$HOME/.ninfer-launcher/snapshots/1a2b3c4/ninfer-serve\""),
                    rb.Replace("\n", " ｜ "));
                Check("DEC-106 注入串进不了脚本：形状关拦住坏提交号/坏分支（回退/同步/提交清单三处一起断）",
                    MainForm.NinferWslRollbackScript("/b", "1a2b; rm -rf /").Contains("BADCOMMIT")
                    && !MainForm.NinferWslRollbackScript("/b", "1a2b; rm -rf /").Contains("rm -rf")
                    && MainForm.NinferWslSyncScript("/b", "", "ma in").Contains("BADBRANCH")
                    && MainForm.NinferWslLogScript("/b", "", "main", "zzz").Contains("BADREF")
                    && MainForm.NinferWslSyncScript("/b", "https://x/; rm -rf /", "main").Contains("BADBRANCH")
                    && MainForm.NinferWslLogScript("/b", "ftp://a`b", "main", "abcdef1").Contains("BADREF")
                    && MainForm.NinferWslHexOk("1a2b3c4") && !MainForm.NinferWslHexOk("1a2b3c4;x")
                    && MainForm.NinferWslBranchOk("feature/x-2") && !MainForm.NinferWslBranchOk("-x"), "ok");
                var pk = MainForm.ParseNinferWslCheck("BRANCH=main\nLOCAL=" + new string('a', 40) + "\nREMOTE=" + new string('b', 40));
                Check("DEC-106 检查解析：分支/两侧提交读得出来，有差异 = update",
                    pk.Problem is null && pk.Branch == "main" && MainForm.NinferWslCheckVerdict(pk.Local, pk.Remote) == "update",
                    pk.Branch + " / " + (pk.Problem ?? "无"));
                Check("DEC-106 检查解析：两侧相同 = 已是最新、读不齐 = unknown、NOREPO / FETCH_FAIL 各说自己那句",
                    MainForm.NinferWslCheckVerdict("abc1234", "abc1234") == "uptodate"
                    && MainForm.NinferWslCheckVerdict("", "abc1234") == "unknown"
                    && MainForm.ParseNinferWslCheck("NOREPO").Problem!.Contains("git")
                    && MainForm.ParseNinferWslCheck("FETCH_FAIL").Problem!.Contains("fetch"));
                Check("DEC-106 检查解析：分支名/提交号形状不对当场拦（后面的脚本拿不到脏串）",
                    MainForm.ParseNinferWslCheck("BRANCH=bad branch\nLOCAL=aaaaaaa\nREMOTE=bbbbbbb").Problem is not null
                    && MainForm.ParseNinferWslCheck("BRANCH=main\nLOCAL=not-hex\nREMOTE=bbbbbbb").Problem is not null);
                var sl = MainForm.ParseNinferWslSnapshotList(
                    "/home/user/.ninfer-launcher/snapshots/f13fc43/\n/home/user/.ninfer-launcher/snapshots/9d89dd6/\n\njunk line");
                Check("DEC-106 快照清单：只收短提交、保持「最新在前」原序、别的行一律丢",
                    sl.SequenceEqual(new[] { "f13fc43", "9d89dd6" }), string.Join(",", sl));
                // 判据打在**调用点**上（BUG-044 的教训：机制写好了 ≠ 接上了）：那一窗第二段的按钮动作
                // 必须真的转到这条链；测试态下它被当场挡下，wsl.exe 一次没起（计数零增长 = 能证"没摸 VM"）。
                var runs0 = MainForm.RunWslScriptCountForTest;
                uiForm.ResetNinferWslWitnessForTest();
                uiForm.ModuleViews.First(v => v.Key == MainForm.NinferKey).Secondary!.OnAction();
                Check("DEC-106 干跑：第二段按钮 → 链入口被当场挡下（attempts=1、blocked=test、忙态没动、wsl.exe 一次没起）",
                    uiForm.NinferWslAttemptsForTest == 1 && uiForm.NinferWslBlockedForTest == "test"
                    && !uiForm.NinferUiBusy && MainForm.RunWslScriptCountForTest == runs0,
                    "blocked=" + uiForm.NinferWslBlockedForTest + " / runs+" + (MainForm.RunWslScriptCountForTest - runs0));
                // DEC-109：WSL 窗"开窗即查"（他实跑："点开窗之后，还要在窗里再点一次更新按钮"）——
                // 收口在 TriggerOpenCheck（OpenUpdateWindow 尾部调用，与另三扇同一个形状）；这里干跑同一条入口：
                // 忙时不叠第二次；闲时当场走到链入口（测试态被测试闸挡下 = attempts+1、wsl.exe 零增长）。
                uiForm.ResetNinferWslWitnessForTest();
                uiForm.SetNinferBusyForTest(true);
                uiForm.TriggerOpenCheckForTest("ninfer", "wsl");
                var busyAttempts = uiForm.NinferWslAttemptsForTest;
                uiForm.SetNinferBusyForTest(false);
                var runs2 = MainForm.RunWslScriptCountForTest;
                uiForm.TriggerOpenCheckForTest("ninfer", "wsl");
                Check("DEC-109 WSL 窗开窗即查（与另三扇一致）：忙时不叠、闲时当场走到链入口（测试态挡下：attempts+1、wsl.exe 零增长）",
                    busyAttempts == 0 && uiForm.NinferWslAttemptsForTest == 1 && uiForm.NinferWslBlockedForTest == "test"
                    && MainForm.RunWslScriptCountForTest == runs2,
                    $"busyAttempts={busyAttempts} idleAttempts={uiForm.NinferWslAttemptsForTest} blocked={uiForm.NinferWslBlockedForTest} runs+{MainForm.RunWslScriptCountForTest - runs2}");
                Check("DEC-106 纯判据：链开跑三条件穷举（测试态 / 那一侧启用 / 两条链互斥）",
                    MainForm.NinferWslChainAllowed(false, true, false)
                    && !MainForm.NinferWslChainAllowed(true, true, false)
                    && !MainForm.NinferWslChainAllowed(false, false, false)
                    && !MainForm.NinferWslChainAllowed(false, true, true));
                // 第二段那枚按钮的字态：字从这一段自己报；忙时置灰（灰的按钮 BM_CLICK 也点不动）
                using (var nf2 = new UpdateForm(uiForm, "ninfer", "wsl"))     // DEC-107：看的就是 WSL 那扇窗
                {
                    nf2.CreateControl();
                    Check("DEC-106 WSL 窗闲态：按钮字 =「更新…」（DEC-097③ 的形状一字未动）",
                        nf2.SecondaryForTest().EndsWith("更新…") && nf2.SecondaryEnabledForTest(), nf2.SecondaryForTest());
                    uiForm.SetNinferWslBusyForTest("重编中…", "重编 WSL 引擎（VM 里 cmake，可能要几分钟）…");
                    uiForm.SetNinferBusyForTest(true);
                    Check("DEC-106 WSL 窗忙态：按钮字跟着读「重编中…」且置灰",
                        nf2.SecondaryForTest().EndsWith("重编中…") && !nf2.SecondaryEnabledForTest(), nf2.SecondaryForTest());
                    Check("DEC-106 忙时阶段行说得出来是「哪条链在忙」（阶段名走 offer.Phase，不是那句「正在查询最新发布…」）",
                        nf2.LayoutTextForTest().Contains("phase=重编 WSL 引擎")
                        && !nf2.LayoutTextForTest().Contains("正在查询最新发布"), nf2.LayoutTextForTest());
                    // ============ DEC-110③：这条链的结果也要落在窗里那一行，不能只长在日志区 ============
                    // 用户原话：「wsl更新弹窗也应该显示已是最新, 不能仅仅显示在日志区」。
                    // 忙时先放进度条与阶段名，闲下来才轮到结果回显（同一行、同一个家，不并存）。
                    uiForm.SetNinferBusyForTest(false);
                    uiForm.SetNinferWslBusyForTest("", "");
                    uiForm.SetNinferWslMemoForTest("已是最新", false);
                    Check("DEC-110③ WSL 窗闲时看得见「已是最新」（意图位：文本 + 出面 + 进度条不在）",
                        nf2.WslStatusForTest() == "已是最新|1|0", nf2.WslStatusForTest());
                    uiForm.SetNinferWslBusyForTest("检查中…", "检查 WSL 更新（git fetch）…");
                    uiForm.SetNinferBusyForTest(true);
                    // DEC-138（B-6）：这一条从 `|1|1`（旧结果**藏在原地还在**，靠布局去压）改成 `|0|1` ——
                    // 开新一轮时那句结果回显当场清掉，"检查失败"与这一轮的 fetch 不再并存一瞬。
                    // 阶段名照旧要说得出（同一条判据的另外半句没动）。
                    Check("DEC-110③ / DEC-138 B-6 忙时那一行只剩阶段名与进度条（上一轮的结果回显当场清掉，不再并存一瞬）",
                        nf2.WslStatusForTest().EndsWith("|0|1")
                        && nf2.LayoutTextForTest().Contains("phase=检查 WSL 更新"), nf2.WslStatusForTest());
                    uiForm.SetNinferBusyForTest(false);
                    uiForm.SetNinferWslBusyForTest("", "");
                    uiForm.SetNinferWslMemoForTest("", false);      // 清掉，别影响后面的用例
                    Check("DEC-110③ 没查过时那一行是空的（不拿空串冒充「已查过」）",
                        nf2.WslStatusForTest() == "|0|0", nf2.WslStatusForTest());
                    uiForm.SetNinferBusyForTest(false);
                    uiForm.SetNinferWslBusyForTest("", "");
                    Check("DEC-106 收尾归位：按钮字回「更新…」、可点、阶段行那句也没了",
                        nf2.SecondaryForTest().EndsWith("更新…") && nf2.SecondaryEnabledForTest()
                        && !nf2.LayoutTextForTest().Contains("phase=重编"), nf2.LayoutTextForTest());
                }
                // 四扇窗都有结果回显那一行 = 按表穷举（用户："梳理一下，是否只有 wsl 是这样，还有没有遗漏"）
                Check("DEC-110③ 结果回显那一行四扇窗都有：三模块主段 memo + Ninfer 的 WSL 第二段 memo",
                    uiForm.ModuleViews.All(v => v.StatusMemo is not null)
                    && uiForm.ModuleViews.All(v => v.Secondary is null || v.Secondary.StatusMemo is not null)
                    && uiForm.ModuleViews.First(v => v.Key == MainForm.NinferKey).Secondary!.StatusMemo is not null,
                    string.Join(",", uiForm.ModuleViews.Select(v => v.Key + ":"
                        + (v.Secondary?.StatusMemo is not null ? "两扇" : "一扇"))));
                // DEC-107：两扇窗互不串页 —— WSL 链忙时 win 窗一个字都不该说"在查发布"（它自己那条链没动）
                using (var nf3 = new UpdateForm(uiForm, "ninfer"))
                {
                    nf3.CreateControl();
                    uiForm.SetNinferWslBusyForTest("重编中…", "重编 WSL 引擎（VM 里 cmake，可能要几分钟）…");
                    uiForm.SetNinferBusyForTest(true);
                    Check("DEC-107 WSL 链忙时：win 窗不出进度条、不写「正在查询最新发布…」（它认的是自己那条链）",
                        !nf3.LayoutTextForTest().Contains("bar=1")
                        && !nf3.LayoutTextForTest().Contains("正在查询最新发布"),
                        nf3.LayoutTextForTest());
                    uiForm.SetNinferBusyForTest(false);
                    uiForm.SetNinferWslBusyForTest("", "");
                }
                Check("DEC-106 设置页 WSL 组：说明已换新、多一行「编译快照回退…」，不再提「第 3 轮」",
                    SRow("settings-Ninfer").Contains("编译快照回退")
                    && !SRow("settings-Ninfer").Contains("排在第 3 轮"), SRow("settings-Ninfer"));
                // ============ DEC-110④：Ninfer 的维护组与另两节同形（更新源 → 版本 → 回退 → 各自独有）============
                Check("DEC-110④ Ninfer 节维护组：两条链各排一遍「更新源 → 版本 → 回退」，最后才是共用的家底备份"
                    + "（DEC-157：显存那一格已挪到「通用」，这一节从 内部端口 直接到 模型配置）",
                    InOrder(SRow("settings-Ninfer"), "共用（两个后端一起用的）", "内部端口", "模型配置",
                        "WSL 后端", "启用 WSL 后端", "发行版", "模型目录（VM 内）", "引擎可执行（VM 内）", "空闲停服(分钟)",
                        "WIN 后端", "启用 WIN 后端", "引擎目录", "空闲停服(分钟)",
                        "维护", "更新源 · WSL", "版本 · WSL", "更新…", "编译快照回退 · WSL", "选择快照…",
                        "更新源 · WIN", "版本 · WIN", "引擎 exe 回退 · WIN", "恢复 exe…",
                        "改动前自动备份", "恢复…"), SRow("settings-Ninfer"));
                Check("DEC-110④ WSL 那一侧也有「更新源」了（用户点名）；DEC-110⑤ 套件校验整行退役",
                    SRow("settings-Ninfer").Contains("更新源 · WSL")
                    && !SRow("settings-Ninfer").Contains("套件完整性校验")
                    && !SRow("settings-Ninfer").Contains("校验套件"), SRow("settings-Ninfer"));
                // DEC-110②：三处常驻说明按用户点名删掉 —— 删的是文字，行也要跟着不占位
                //（NoteRow 是独立一行，留着空行就是"删了字没删布局"）。
                Check("DEC-110② Ninfer 节三条说明行删净（整行不在，不是只剩空行）",
                    !SRow("settings-Ninfer").Contains("版本 = 拿本地 exe")
                    && !SRow("settings-Ninfer").Contains("只剩一个后端启用时")
                    && !SRow("settings-Ninfer").Contains("更新 = VM 里 git fetch"), SRow("settings-Ninfer"));
                Check("DEC-110② Ninfer 模块说明删掉那两处短语（图1 红框）",
                    ModuleRegistry.Ninfer.Description == "Ninfer 引擎模块：两条后端（WSL / WIN）。"
                    && !ModuleRegistry.Ninfer.Description.Contains("三件套")
                    && !ModuleRegistry.Ninfer.Description.Contains("按需唤醒"),
                    ModuleRegistry.Ninfer.Description);
                // 三节同形：按模块表穷举「维护」组里 更新源 在 版本 前、版本 在它自己的回退/恢复前
                foreach (var mv in ModuleRegistry.All)
                {
                    var srow = SRow("settings-" + mv.DisplayName);
                    string[] toks = srow.Split('|').Select(x => x.Trim()).ToArray();
                    int iSrc = Array.FindIndex(toks, t => t.StartsWith("更新源", StringComparison.Ordinal));
                    int iVer = Array.FindIndex(toks, t => t.StartsWith("版本", StringComparison.Ordinal));
                    int iBak = Array.FindIndex(toks, t =>
                        t.StartsWith("回退", StringComparison.Ordinal) || t.StartsWith("改动前自动备份", StringComparison.Ordinal)
                        || t.StartsWith("编译快照回退", StringComparison.Ordinal) || t.StartsWith("引擎 exe 回退", StringComparison.Ordinal));
                    Check("DEC-110④ 「" + mv.DisplayName + "」节的维护组同形：更新源 → 版本 → 回退（-1 = 这一档没有，跳过该位）",
                        srow.Contains("维护") && (iSrc < 0 || iVer < 0 || iSrc < iVer)
                        && (iVer < 0 || iBak < 0 || iVer < iBak),
                        $"更新源={iSrc} 版本={iVer} 回退={iBak}");
                }
                Check("DEC-110④ Strata 的更新源认得出 owner/repo，填坏或留空回落默认那一头（不许一格填坏把检查弄瘫）",
                    MainForm.StrataRepoApiOf("https://github.com/alice/fork") == "https://api.github.com/repos/alice/fork"
                    && MainForm.StrataRepoApiOf("https://api.github.com/repos/bob/x/releases") == "https://api.github.com/repos/bob/x"
                    && MainForm.StrataRepoApiOf("") == "https://api.github.com/repos/Niko1221/Strata"
                    && MainForm.StrataRepoApiOf("不是地址") == "https://api.github.com/repos/Niko1221/Strata",
                    MainForm.StrataRepoApiOf("https://github.com/alice/fork"));
                uiForm.ResetNinferWslWitnessForTest();

                // ============ DEC-107：他七条一次批复的落点（① 去格 ② 输入框 ③ 左栏 ④ 卡形 ⑤⑥ 版本行与按钮 ⑦ 两窗）============
                {
                    const string winB = NinferIni.BackendWin, wslB = NinferIni.BackendWsl;
                    var pageBefore107 = uiForm.ActiveModuleForTest;   // 块内来回切页，出门还原（后面那些用例认的是 Strata 页）

                    // ① 共享前缀上限去格：两侧都没格 = 走"表外旗标原样透传"；起前护栏照旧判实际命令行（护栏用例另有专门一条）
                    Check("DEC-107① 「共享前缀上限」两侧都不再给格（旗标从此只从段里透传，护栏仍在）",
                        !MainForm.FlagShown("max-shared-prefixes", winB) && !MainForm.FlagShown("max-shared-prefixes", wslB)
                        && !MainForm.UiFlagsFor(winB).Contains("max-shared-prefixes")
                        && !MainForm.UiFlagsFor(wslB).Contains("max-shared-prefixes"));
                    {
                        var sec0 = new NinferIni.Section
                        {
                            Name = "dec107-a", Backend = winB,
                            Args = "--max-context 262144 --max-shared-prefixes 0 --kv-capacity 55296",
                        };
                        var line0 = uiForm.BuildNinferArgvLine(sec0,
                            new Settings { NinferCtx = "262144", NinferHostKib = "16384", NinferTemperature = "", Host = "0.0.0.0" }, winB, 8069);
                        Check("DEC-107① 透传成立：段里那枚 `--max-shared-prefixes 0` 照旧进实际命令行（护栏读的就是它）",
                            line0.Contains("--max-shared-prefixes 0"), line0);
                        // 附加参数（DEC-107 ④）：挂在命令行末尾、且那一把就是护栏判的那一把（Split 之后看得见）
                        var line1 = uiForm.BuildNinferArgvLine(sec0,
                            new Settings { NinferCtx = "262144", NinferHostKib = "16384", NinferExtraArgs = "--dec107-x 1" }, winB, 8069);
                        Check("DEC-107④ 附加参数追加在最后（空则一字不多），护栏判的正是这一把（Split 后看得见）",
                            line1.EndsWith("--dec107-x 1")
                            && NinferIni.FlagValue(NinferIni.Split(line1), "dec107-x") == "1"
                            && uiForm.BuildNinferArgvLine(sec0, new Settings { NinferCtx = "262144" }, winB, 8069)
                                == uiForm.BuildNinferArgvLine(sec0, new Settings { NinferCtx = "262144", NinferExtraArgs = "  " }, winB, 8069),
                            line1);
                    }

                    // ② 投机方式 / 投机深度改输入框：值即收即回；深度按当前后端读对应那只框（另一只串不进来）
                    uiForm.SwitchModuleForTest("ninfer");
                    var specBak = uiForm.NinferSpecBoxForTest;
                    var draftWinBak = uiForm.NinferDraftWinBoxForTest;
                    var draftWslBak = uiForm.NinferDraftWslBoxForTest;
                    try
                    {
                        uiForm.SetNinferSpecForTest("mtp");
                        Check("DEC-107② 投机方式（输入框）：值即收（表外写法也进得来 —— 起前校验兜底）",
                            uiForm.CollectedForTest().NinferSpec == "mtp", uiForm.CollectedForTest().NinferSpec);
                        uiForm.SetNinferDraftWinBoxForTest("");
                        uiForm.SetNinferDraftWslBoxForTest("");
                        if (uiForm.NinferBackendForTest == winB) { uiForm.SetNinferDraftWinBoxForTest("7"); uiForm.SetNinferDraftWslBoxForTest("9"); }
                        else { uiForm.SetNinferDraftWslBoxForTest("7"); uiForm.SetNinferDraftWinBoxForTest("9"); }
                        Check("DEC-107② 投机深度改输入框：读的是当前后端那只框（另一只的值串不进来）",
                            uiForm.CollectedForTest().NinferDraft == "7",
                            "后端=" + uiForm.NinferBackendForTest + " 收到=" + uiForm.CollectedForTest().NinferDraft);
                    }
                    finally
                    {
                        uiForm.SetNinferSpecForTest(specBak);
                        uiForm.SetNinferDraftWinBoxForTest(draftWinBak);
                        uiForm.SetNinferDraftWslBoxForTest(draftWslBak);
                    }

                    // ③ 左栏加宽：侧栏 / API 框 / 灯行三处同一常量（196），API 字号 9pt 放得下（具体数在 GridFit 那条里）
                    var sw = uiForm.SideWidthsForTest();
                    Check("DEC-107③ 左栏内宽 = 200（侧栏本体 /「API 地址」框 / 灯行三处同一常量，别再写 178）",
                        sw.side == UiTheme.S(uiForm, MainForm.SideInnerLogical + 32)
                        && sw.api == UiTheme.S(uiForm, MainForm.SideInnerLogical)
                        && sw.light == UiTheme.S(uiForm, MainForm.SideInnerLogical),
                        $"{sw.side}/{sw.api}/{sw.light}（逻辑 {UiTheme.DpiToLogical(sw.side)}/{UiTheme.DpiToLogical(sw.api)}/{UiTheme.DpiToLogical(sw.light)}）");

                    // ④ 卡形对齐：Ninfer 卡1 的端口/思考深度 = Strata 页同宽；卡2 前两格三页同宽；打开网页位留空占位
                    {
                        var pageBak4 = uiForm.ActiveModuleForTest;
                        uiForm.SwitchModuleForTest("strata");
                        var sp1 = uiForm.Card1ActiveSlotsForTest();
                        var cp1 = uiForm.Card2LeadingSlotsForTest();
                        var h1c = uiForm.CardParamsBoundsInClient().Height;
                        var slotsStrata = uiForm.Card2SideSlotsForTest();
                        uiForm.SwitchModuleForTest("ninfer");
                        var sp2 = uiForm.Card1ActiveSlotsForTest();
                        var cp2 = uiForm.Card2LeadingSlotsForTest();
                        var slotsNinfer = uiForm.Card2SideSlotsForTest();
                        Check("DEC-107④/108 卡1 端口/思考深度与 Strata 页同宽 + 卡2 前三格（模型名称/上下文/KV 精度）三页同宽、卡高同高",
                            sp1 == sp2 && cp1 == cp2 && uiForm.CardParamsBoundsInClient().Height == h1c,
                            $"卡1 {sp1} vs {sp2}；卡2 前三格 {cp1} vs {cp2}；卡高 {h1c} vs {uiForm.CardParamsBoundsInClient().Height}");
                        Check("DEC-107④ 打开网页那格在 Ninfer 页留空占位：它挪出画外、但「高级」仍留在下面那一格（与 Strata 页同位）",
                            slotsNinfer.web.X < 0 && slotsNinfer.adv.Y == slotsStrata.adv.Y
                            && slotsStrata.web.X >= 0 && slotsStrata.web.Y < slotsStrata.adv.Y,
                            $"strata web={slotsStrata.web.X},{slotsStrata.web.Y} adv={slotsStrata.adv.X},{slotsStrata.adv.Y}｜ninfer web={slotsNinfer.web.X},{slotsNinfer.web.Y} adv={slotsNinfer.adv.X},{slotsNinfer.adv.Y}");
                        uiForm.SwitchModuleForTest(pageBak4);
                    }

                    // ⑤ 版本行：短写法纯函数 + 左栏那一行真的跟着激活模块换
                    Check("DEC-107⑤ 版本短写法：WIN 只显示 -kvmem 前面的部分；状态类各缩到一个词；空 = 短横",
                        MainForm.ShortVersionForSidebar("engine-v0.11.0-kvmem-20261003") == "engine-v0.11.0"
                        && MainForm.ShortVersionForSidebar("c18e9c67") == "c18e9c67"
                        && MainForm.ShortVersionForSidebar("未启用（启用那一侧才会读 VM 里的版本）") == "未启用"
                        && MainForm.ShortVersionForSidebar("未查过（打开 Ninfer 的更新窗会自动读一次）") == "未查过"
                        && MainForm.ShortVersionForSidebar("未知构建（abc）· 点「更新…」查一次") == "未知构建"
                        && MainForm.ShortVersionForSidebar("") == "-");
                    // 2026-10-15：strata 那一档从"不写"变成"报引擎版本"（两行合并成一行），签名多一个入参
                    Check("DEC-107⑤ 左栏版本行的取值判据（纯函数穷举）：三页都写 —— Strata 报引擎版本、llama 报运行时版本、Ninfer 按当前后端取那侧",
                        MainForm.SidebarVersionText("strata", winB, () => "w", () => "s", "", "v0.1.43.1") == "v0.1.43.1"
                        && MainForm.SidebarVersionText("strata", winB, () => "w", () => "s", "", "") == "-"
                        && MainForm.SidebarVersionText("llama", winB, () => "w", () => "s", "v0.5.0-b11259", "v0.1.43.1") == "v0.5.0-b11259"
                        && MainForm.SidebarVersionText("llama", winB, () => "w", () => "s", "", "v0.1.43.1") == "-"
                        && MainForm.SidebarVersionText("ninfer", winB, () => "engine-v0.11.0-kvmem-20261003", () => "s", "", "v0.1.43.1") == "engine-v0.11.0"
                        && MainForm.SidebarVersionText("ninfer", wslB, () => "w", () => "c18e9c67", "", "v0.1.43.1") == "c18e9c67");
                    {
                        var pageBak5 = uiForm.ActiveModuleForTest;
                        uiForm.SwitchModuleForTest("ninfer");
                        var t5 = uiForm.SidebarVersionTextForTest;
                        uiForm.SwitchModuleForTest("strata");
                        var t5s = uiForm.SidebarVersionTextForTest;
                        // 2026-10-15：旧断言是"Strata 页那行不写（行也不出面）"—— 两行合并之后语义反转，三页都写
                        Check("DEC-107⑤ 真页面上：三页「版本」行都有值（strata 报引擎版本）",
                            t5.Length > 0 && t5s.Length > 0, "ninfer=[" + t5 + "] strata=[" + t5s + "]");
                        uiForm.SwitchModuleForTest(pageBak5);
                    }

                    // ⑥ 版本行末尾那枚小按钮：2026-10-15 起只剩「版本」一行有（strata 的「引擎」行已合并进来）；
                    //    DEC-108 = 缩到 S(12)、行里垂直居中、右缘贴边；灰/绿两态跟着"当前模块有没有探到新版本"
                    {
                        var bg = uiForm.UpdateBtnGeometryForTest("版本");
                        int half = (bg.Row.Height - bg.Btn.Height) / 2;
                        Check("DEC-107⑥/108 小更新按钮：只剩「版本」一行有；缩到 S(12)（-40%）、行里垂直居中、右缘贴边、行高行宽没被撑大",
                            uiForm.UpdateBtnRowsForTest() && bg.Inside
                            && bg.Row.Height == UiTheme.S(uiForm, 22) && bg.Row.Width == UiTheme.S(uiForm, MainForm.SideInnerLogical)
                            && bg.Btn == new Size(UiTheme.S(uiForm, 12), UiTheme.S(uiForm, 12))
                            && Math.Abs(bg.Bounds.Top - half) <= 1 && Math.Abs(bg.Bounds.Right - bg.Row.Width) <= 1,
                            $"按钮 {bg.Btn.Width}x{bg.Btn.Height}@({bg.Bounds.Left},{bg.Bounds.Top})｜行 {bg.Row.Width}x{bg.Row.Height}（居中位 {half}）｜Inside={bg.Inside}");
                        // 灰/绿两态（DEC-108）：默认灰；给当前模块造一个 offer（走真广播）→ 转绿；清掉 → 回灰
                        {
                            var pageBak6 = uiForm.ActiveModuleForTest;
                            uiForm.SwitchModuleForTest("ninfer");
                            uiForm.RefreshSidebarButtonsForTest();
                            var gray = uiForm.UpdateBtnGeometryForTest("版本").Green == false;
                            uiForm.SetNinferOfferForTest("engine-v0.12.0-x", "2026-10-05T01:00:00Z", skipped: false);
                            var green = uiForm.UpdateBtnGeometryForTest("版本").Green == true;
                            uiForm.ClearNinferOfferForTest();
                            uiForm.RefreshSidebarButtonsForTest();
                            var backToGray = uiForm.UpdateBtnGeometryForTest("版本").Green == false;
                            Check("DEC-108 小箭头两态：没探到新版本 = 灰；探到（offer 经真广播进来）= 品牌绿；清掉回灰",
                                gray && green && backToGray,
                                $"gray={gray} green={green} backToGray={backToGray}");
                            uiForm.SwitchModuleForTest(pageBak6);
                        }
                        // 点了开哪扇窗（纯判据）：Strata/llama 恒 win；Ninfer 按当前后端；两扇窗的容器键必须分开
                        Check("DEC-107⑥⑦ 小按钮与托盘开哪扇窗的判据：Ninfer 按当前后端、两扇容器键分开",
                            uiForm.UpdateSideFor("strata") == "win" && uiForm.UpdateSideFor("llama") == "win"
                            && uiForm.UpdateSideFor("ninfer") == (uiForm.NinferBackendForTest == NinferIni.BackendWsl ? "wsl" : "win")
                            && MainForm.UpdateSidesFor("ninfer").SequenceEqual(new[] { "win", "wsl" })
                            && MainForm.UpdateSidesFor("strata").SequenceEqual(new[] { "win" }),
                            uiForm.UpdateSideFor("ninfer") + " / " + string.Join(",", MainForm.UpdateSidesFor("ninfer")));
                    }
                    uiForm.SwitchModuleForTest(pageBefore107);        // 出门还原（后面那些用例认的是 Strata 页）
                }

                // ⑥（DEC-055）：状态灯口径统一——三个模块的"服务在跑/在听、模型没加载"必须是同一句话
                // DEC-107：他点名把「模型」二字加回来（左栏加宽到 196 之后放得下）
                var (u1, u2) = (MainForm.StrataLight(false, false, false, false, true, true, 8076),
                                MainForm.LlamaLight(false, "", true, false, 8078));
                Check("⑥ 状态灯统一口径：待机 · 模型未加载 · 端口 xxxx（Strata 与 llama 一字不差；DEC-107 起带「模型」）",
                    u1.desc.EndsWith("· 端口 8076") && u2.desc.EndsWith("· 端口 8078")
                    && u1.desc.StartsWith("待机 · 模型未加载") && u2.desc.StartsWith("待机 · 模型未加载")
                    && u1.desc[..u1.desc.IndexOf("· 端口", StringComparison.Ordinal)]
                    == u2.desc[..u2.desc.IndexOf("· 端口", StringComparison.Ordinal)],
                    u1.desc + " || " + u2.desc);
                // ⑦（DEC-055）：关于页三处位置都能直达；内建 llama.cpp 挪进 Strata 节
                Check("⑦ 关于页三行都带「打开」，且不再有引擎组",
                    SRow("settings-关于").Contains("打开") && !SRow("settings-关于").Contains("内嵌 llama.cpp")
                    && !SRow("settings-关于").Contains("引擎"), SRow("settings-关于"));
                Check("⑦ 内建 llama.cpp 归到 Strata 节（只显示 commit，不再有「查 b 号」）",
                    SRow("settings-Strata").Contains("内建 llama.cpp") && !SRow("settings-Strata").Contains("查 b 号"),
                    SRow("settings-Strata"));
                // ⑤（DEC-055）：取消只在能安全停的阶段可用
                Check("⑤ 可取消阶段：下载/解压可停，覆盖与同步引擎不给停",
                    MainForm.PhaseCancellable("下载源码包") && MainForm.PhaseCancellable("解压")
                    && !MainForm.PhaseCancellable("覆盖到 Strata 目录") && !MainForm.PhaseCancellable("同步官方预编译引擎"));
                // ---------------- DEC-061：只读审查后的整改（每条一个断言打在改过的判据上） ----------------
                uiForm.SetUpdateBusyForTest(true); uiForm.SetLlamaBusyForTest(true);
                uiForm.RequestCancelUpdate(false);
                Check("DEC-061 取消位按模块各一份：Strata 点小 X 不连累 llama（一模块一窗可同时在跑）",
                    uiForm.CancelWantedForTest(false) && !uiForm.CancelWantedForTest(true));
                // DEC-138（E-8）：以前这两行调 `uiForm.ClearCancelFlagsForTest()` —— 测试专用接缝自己清、再断"清干净了"，
                // 把更新链**真收尾**里那两行 `_strataCancelWanted = false` / `_llamaCancelWanted = false` 删掉它照样绿。
                // 现在走真收尾那两个口，并叠上证人位：收尾没跑 = 计数不涨 = 红（接缝本身已删）。
                //（stage 传 "cancel" = 只回显一句 + Toast（自检里被 DEC-127⑥ 那道闸挡着），不弹窗、不碰家底。）
                // 上面那条用例把 Strata 那枚点着了就留在原地 —— 先用真收尾清干净，再谈"各一份"。
                uiForm.FinishUpgradeForTest(false, "cancel");
                uiForm.FinishLlamaUpgradeForTest(false, "");
                int resets0 = MainForm.CancelResetsForTest;
                uiForm.RequestCancelUpdate(true);
                Check("DEC-061 取消位各一份：只请求 llama 那一侧时，Strata 那枚不许被顺手点着",
                    uiForm.CancelWantedForTest(true) && !uiForm.CancelWantedForTest(false),
                    $"strata={uiForm.CancelWantedForTest(false)} llama={uiForm.CancelWantedForTest(true)}");
                uiForm.RequestCancelUpdate(false);
                uiForm.FinishLlamaUpgradeForTest(false, "");
                Check("DEC-138 E-8 llama 的真收尾只归 llama 那一枚（Strata 那枚照旧亮着 = DEC-061「各一份」没退）",
                    uiForm.CancelWantedForTest(false) && !uiForm.CancelWantedForTest(true)
                    && MainForm.CancelResetsForTest == resets0 + 1,
                    $"计数 {resets0} → {MainForm.CancelResetsForTest}");
                uiForm.FinishUpgradeForTest(false, "cancel");
                Check("DEC-061 / DEC-138 E-8 Strata 的真收尾归零 Strata 那一枚，两位全灭（证人位再 +1 = 把收尾里那两行删掉就红）",
                    !uiForm.CancelWantedForTest(false) && !uiForm.CancelWantedForTest(true)
                    && MainForm.CancelResetsForTest == resets0 + 2,
                    $"计数 {resets0} → {MainForm.CancelResetsForTest}");
                uiForm.SetUpdateBusyForTest(false); uiForm.SetLlamaBusyForTest(false);
                Check("DEC-061 版本号带后缀按前导数字比：0.1.36-rc1 高于 0.1.35（以前整段解析失败当 0，误报已是最新）",
                    MainForm.CompareVersion("0.1.36-rc1", "0.1.35") > 0,
                    MainForm.CompareVersion("0.1.36-rc1", "0.1.35").ToString());
                Check("DEC-061 最小化的父窗不当居中锚点（Visible 仍为 true 但 Bounds 在屏幕外）",
                    !MainForm.CenterAnchorUsable(FormWindowState.Minimized) && MainForm.CenterAnchorUsable(FormWindowState.Normal));

                // ============ DEC-097 补：更新说明的**鼠标**拖选（DEC-092 那条只验了 SetSelectionForTest，鼠标路径零覆盖） ============
                // 用户实跑报"只能选中第一行"。先把命中测试本身量一遍：Y 变大 → 下标必须跟着变大。
                {
                    using var doc = new MdDoc
                    {
                        Markdown = string.Join("\n\n", Enumerable.Range(1, 8).Select(i =>
                            "第" + i + "段：这一段的文字要写得足够长，好在给定的控件宽度里被折成好几条可视行，"
                          + "这样命中测试才会真的跨行（short lorem ipsum dolor sit amet consectetur adipiscing elit）。")),
                    };
                    doc.Width = 420; doc.Height = 220;
                    doc.CreateControl();
                    doc.Relayout();
                    var hits = new[] { 0, 20, 60, 120, 200, 100_000 }.Select(y => doc.PosAtForTest(new Point(0, y))).ToList();
                    var monotonic = true;
                    for (int i = 1; i < hits.Count; i++) if (hits[i] < hits[i - 1]) monotonic = false;
                    Check("鼠标命中：往下拖 Y 变大 → 纯文本下标跟着变大，超出正文就到底（只能选中第一行 = 这条会红）",
                        monotonic && hits[^1] == doc.PlainForTest.Length && hits[3] > hits[1],
                        "Y=0/20/60/120/200/超远 → pos=" + string.Join("/", hits) + "｜plain=" + doc.PlainForTest.Length);
                    // **走真鼠标事件**那一串：按下 → 拖到下面 → 松手，选中的文字要跨行
                    doc.DragForTest(new Point(0, 20), new Point(0, 120));
                    var dragged = doc.SelectedTextForTest;
                    Check("DEC-097 拖选（走 OnMouseDown/Move/Up 本体）选区跨行、松手后不掉选",
                        dragged.Length > 0 && dragged.Contains('\n') && !doc.SelectingForTest,
                        "拖到 " + dragged.Length + " 字｜含换行=" + dragged.Contains('\n') + "｜" + dragged[..Math.Min(24, dragged.Length)].Replace("\n", "⏎"));
                    // 拖过整篇（末尾超出控件高）→ 要一直选到最后一段
                    doc.DragForTest(new Point(0, 0), new Point(0, 100_000));
                    Check("DEC-097 拖到超出正文末尾 = 选到篇尾（不是停在第一段）",
                        doc.SelectedTextForTest.Length == doc.PlainForTest.Length,
                        "选中 " + doc.SelectedTextForTest.Length + " / 全篇 " + doc.PlainForTest.Length);
                    // 用户报的是**高亮停在第一行** → 数据对了不算对，得把画面拍下来数高亮带有几条。
                    // （DEC-092 那张出图只选了开头 46 个字 ≈ 一两行，所以这条一直没人量过）
                    doc.SetSelectionForTest(0, doc.PlainForTest.Length);
                    using (var bmp = new System.Drawing.Bitmap(doc.Width, doc.Height))
                    {
                        doc.DrawToBitmap(bmp, new Rectangle(0, 0, doc.Width, doc.Height));
                        var selBack = Color.FromArgb(0x1F, 0x47, 0x2E);
                        int bands = 0; bool inBand = false;
                        for (int y = 0; y < bmp.Height; y++)
                        {
                            bool hit = false;
                            for (int x = 0; x < bmp.Width; x += 3)
                                if (bmp.GetPixel(x, y) == selBack) { hit = true; break; }
                            if (hit && !inBand) { bands++; inBand = true; }
                            else if (!hit) inBand = false;
                        }
                        int textLines = doc.PlainForTest.Count(c => c == '\n') + 1;
                        Check("全选时高亮带要跟着行数出（只画第一行 = 用户报的那条）",
                            bands >= Math.Min(4, textLines),
                            "高亮带=" + bands + "｜纯文本行数=" + textLines + "｜控件 " + doc.Width + "x" + doc.Height);
                    }
                }
                // 同一件事在**真实宿主**里再验一遍：更新内容弹窗（Shell + Body + 固定高 340 的 MdScroll + 真 release 文本）
                {
                    var notes = "## 修复\n\n- 修掉了 A，这一段要写得长一些，长到在这一行里放不下、必须折成两条可视行，"
                              + "这样拖选才会跨过换行边界（对应实现见 serve/x.py 与 engine/y.c 两处）。\n\n"
                              + "- 修掉了 B，同样长：这一段也故意写到超过一行，好让命中测试在第二段、第三段上都有落点。\n\n"
                              + "**新增** 512K 那一档：`--prefill auto:32768` 读长文快 51%，深位置连续写不掉速。";
                    using var shell = Dialogs.UpdateDetailShellForTest(uiForm, "engine-v9.9.9", "engine-v0.0.1", notes);
                    shell.CreateControl();
                    var box = SelfTest.FindChild<MdScroll>(shell);
                    var rdoc = box?.Doc;
                    Check("更新内容弹窗里找得到那块 MdScroll/MdDoc", rdoc is not null && rdoc.PlainForTest.Length > 0,
                        rdoc is null ? "没找到" : "plain=" + rdoc.PlainForTest.Length + " 控件高=" + rdoc?.Height);
                    if (rdoc is not null)
                    {
                        rdoc.DragForTest(new Point(0, 10), new Point(0, Math.Max(20, rdoc.Height - 4)));
                        var selReal = rdoc.SelectedTextForTest;
                        Check("真实弹窗里从上往下拖 = 选区跨多行（用户报「只能选中第一行」）",
                            selReal.Contains('\n') && selReal.Length > rdoc.PlainForTest.Length / 3,
                            "选中 " + selReal.Length + " / 全篇 " + rdoc.PlainForTest.Length
                            + "｜行数=" + selReal.Count(c => c == '\n') + "｜控件高=" + rdoc.Height);
                    }
                }
                uiForm.SetUpdateOfferForTest("v0.1.36", "2026-10-03T09:05:28Z", skipped: true);   // 第一枚是最长的「取消跳过」
                Check("DEC-061 换字后按钮右边缘不超出内缩边距（宽度先定、摆位在后）", uf.ButtonsFitForTest(), uf.LayoutTextForTest());
                uiForm.ClearUpdateStateForTest();
                Check("DEC-061 托盘不再挂内置 ContextMenuStrip（DEC-045「不用内置弹出」不再被抵消）",
                    !uiForm.TrayUsesBuiltinMenuForTest, "builtin=" + uiForm.TrayUsesBuiltinMenuForTest);
                int liveTp = ThinProgress.LiveForTest;
                var tpProbe = new ThinProgress(); tpProbe.CreateControl(); tpProbe.Dispose();
                Check("DEC-061 进度条的 40ms 定时器随控件释放（Dispose 覆写确实跑到）",
                    ThinProgress.LiveForTest == liveTp, "live=" + ThinProgress.LiveForTest + " base=" + liveTp);
                var probeRoot = MainForm.Scratch("root-probe");
                Directory.CreateDirectory(probeRoot);
                File.WriteAllText(Path.Combine(probeRoot, "CMakeLists.txt"), "  GIT_TAG abcdef1234567890\n");
                var savedRoot = uiForm.GetRootForTest();
                uiForm.SetRootForTest(probeRoot);
                string pinnedSha = uiForm.PinnedLlamaCommitSha();
                uiForm.SetRootForTest(savedRoot);
                Check("DEC-061「内建 llama.cpp」读配置里的 Strata 目录，不是出厂默认值",
                    pinnedSha == "abcdef1234567890", pinnedSha);
                Check("③ 弹窗族没有白底（原生 MessageBox 已全部替换）", DialogsPreviewIsDark(uiForm), Dialogs.PreviewNoteForTest);
                // ③（BUG-073）判据本体三面 + 取样节拍同一家（合成图喂，不看时机）
                CheckShotVerdict((n, ok, d) => Check(n, ok, d));
                // ⑤（DEC-050）更新内容框：markdown 要渲染、更要真能滚（用户报"滚动条异常、滚轮也不动"）
                var md = string.Concat(Enumerable.Repeat(
                    "**Faster prompts, `--kv q4_0` on tensor cores, and a batch of community PRs.** Without `api_key` the "
                    + "server now answers only requests addressed to a name it knows, see "
                    + "https://github.com/Niko1221/Strata/releases/download/v0.1.38/strata-windows-x64.zip for the engine.\r\n"
                    + "- The prompt path gathers a group of experts in one launch (#372).\r\n"
                    + "- Measured on an RTX 5070 12 GB: Q2_0 prompts +10% at 4K, +2.7% at 32K.\r\n\r\n---\r\n\r\n", 4));
                var scroll = Dialogs.ProbeDetailScrolling(uiForm, md);
                Check("⑤ 更新内容框：内容高于可视区时滑条算得出可滚范围",
                    scroll.Scrollable && scroll.DocHeight > scroll.ViewHeight,
                    $"内容 {scroll.DocHeight} / 可视 {scroll.ViewHeight}");
                Check("⑤ 更新内容框：滚三格滚轮真的把内容滚下去了（不是只画了根条）", scroll.WheelMoves,
                    "滚后位移 " + scroll.After);
                // DEC-092 第 3 条：自绘之后不能连字都选不了（用户："里面的内容现在无法选中复制了"）
                var sel = Dialogs.ProbeDetailSelection(uiForm, md);
                Check("选中复制：复制的是渲染后的纯文本（** 与行内 `码` 的记号不进复制结果；粗体内再嵌记号按原样显示=不递归）",
                    !sel.Plain.Contains("**") && !sel.Plain.Contains("`api_key`")
                    && sel.Plain.Contains("Without api_key the") && sel.Plain.Contains("--kv q4_0")
                    && sel.Plain.Contains("The prompt path gathers"),
                    sel.Plain.Length + " 字，首行：" + sel.Plain.Split('\n')[0]);
                Check("选中复制：全选（Ctrl+A 与双击都汇到 SelectAll）拿到的就是整篇",
                    sel.SelectedAll == sel.Plain && sel.Plain.Length > 200, sel.SelectedAll.Length + "/" + sel.Plain.Length);
                Check("选中复制：按坐标命中的下标从篇首 0 单调排到篇尾，选一段就只拿到那一段",
                    sel.PosTop == 0 && sel.PosBottom == sel.Plain.Length && sel.Sub == sel.Plain.Substring(3, 6),
                    $"top={sel.PosTop} bottom={sel.PosBottom} 篇长={sel.Plain.Length} 片段=[{sel.Sub}]");
                Check("选中复制：什么都没选时拒绝写剪贴板（自检跑在用户机器上，不许占他的剪贴板）",
                    sel.CopyRefusesEmpty, "空选中写剪贴板=" + !sel.CopyRefusesEmpty);
                // 可拖窗（帮助 / 查看更新内容）：边框可拖 + 内容高度跟着窗体走。MdDoc 是 Dock.Fill 且
                // OnResize 整篇重排 —— 拖宽时表格列宽会重算，正好接上"表格太窄"那条。
                var sz = Dialogs.ProbeSizableShell(uiForm);
                Check("查看更新内容窗可拖，且内容高度跟着窗体走（拖大 120 逻辑 → 内容也长 120）",
                    sz.Sizable && sz.Gap == UiTheme.S(uiForm, 120), $"可拖={sz.Sizable} 内容高 {sz.BoxH0} → {sz.BoxH1}（差 {sz.Gap}）");
                // 表格分支（本轮新增）：上游 release body 与说明书里都有表，以前会被当普通段落排成"一堆竖线"
                var tb = new MdDoc().TableProbeForTest(
                    "| 列A | 列B |\n| --- | --- |\n| a1 | b1 |\n| a2 | b2 |\n", 600);
                Check("表格分支：`|a|b|` + `|---|` 被认成表格（有定位行）且表头加粗",
                    tb.TableLines >= 3 && tb.MaxXs >= 2 && tb.HeaderBold,
                    $"表行={tb.TableLines} Xs最多={tb.MaxXs} 表头粗={tb.HeaderBold}");
                Check("表格分支：表头那一带涂底色、每行下面一条分隔线（表头 1 条 + 3 行各一条）",
                    tb.HeadBands >= 1 && tb.BandEnds >= 3 && tb.BandR > 0,
                    $"表头带={tb.HeadBands} 行分隔线={tb.BandEnds} 带右界={tb.BandR}");
                // 列宽**容器驱动**：放得下也把剩余宽度按各列自然宽等比分回去 —— 早先是内容驱动，
                // "放得下就原样用自然宽"，于是表格比正文窄一截、右侧一大块空白（他实跑抓到的）。
                // w = Avail = 600 − 左右内衬 20 = 580，所以带右界应当几乎贴到 580。
                Check("表格撑满可用宽度（列宽容器驱动，右侧不留大块空白）",
                    tb.BandR >= 560, $"带右界={tb.BandR} / 可用 580");
                Check("表格分支：单元格有左右内衬（第一段不再贴着带的左边界）",
                    tb.MinCellPad >= 4, $"最小左内衬={tb.MinCellPad}px");
                // 这一条专门钉"刚好放得下一行的单元格不许平白多折一行"：内衬必须**算进列宽**（不是从折行宽度里扣），
                // 而且列宽的自然宽必须与折行用同一种算法（逐字量）。他实跑截图里 `llama.cpp` 折成 `llama.c` + `p`、
                // 表头「界面显示名」折成两行，两处都是这个病 —— 少任一条件，这条就红。
                var nowrap = new MdDoc().TableProbeForTest(
                    "| llama.cpp | 界面显示名 |\n| --- | --- |\n| llama.cpp | 界面显示名 |\n| llama.cpp | 界面显示名 |\n", 600);
                Check("表格：刚好放得下一行的单元格不折行（3 行 = 1 表头 + 2 数据，每行一行）",
                    nowrap.TableLines == 3, $"表行={nowrap.TableLines}（期望 3）");
                const string TblMd = "| 列A | 列B |\n| --- | --- |\n| a1 | b1 |\n| a2 | b2 |\n";
                var ov = new MdDoc().TableOverflowForTest(TblMd, 600);
                Check("表格里的文字不得重叠：每段画出去的右边界都在下一段起点 / 带右界之内",
                    ov <= 0, "越界 " + ov + "px");
                // 滚动偏移的**上界**也要钳：只钳 0 的话，拖选到上下边缘的自动滚动（OnMouseMove 里 ±S(14)）
                // 会把偏移推出「内容总高 − 可视高」之外 → 内容被推出视野、OnPaint 开头 g.Clear 之后画不出
                // 东西 → 一大片空白（他实跑在两个内容窗上抓到的）。滚轮与滑条两条路各有钳，唯独这条没有。
                var cl = new MdDoc().ScrollYClampForTest("| 列A | 列B |\n| --- | --- |\n" + new string('补', 3000), 600, 99999);
                Check("滚动偏移上界也要钳（拖选到边缘不许把内容推出视野、留大片空白）",
                    cl.Max > 0 && cl.Got == cl.Max, $"上界={cl.Max} 实际={cl.Got}");
                Check("表格分支：行内代码文字用默认色（不再是品牌绿；胶囊底另画）",
                    tb.CodeRuns == 0 || tb.CodeRunsBranded == 0, $"代码段={tb.CodeRuns} 其中品牌绿={tb.CodeRunsBranded}");
                Check("表格分支：复制出来的纯文本带全部单元格（列间有空白分隔、不带竖线）",
                    tb.Plain.Contains("列A") && tb.Plain.Contains("列B") && tb.Plain.Contains("b2")
                    && !tb.Plain.Contains("|"), tb.Plain.Replace("\n", "\\n"));
                // 反面：窄到单列放不下 → 整体退回段落（一行都不吃）= 旧行为
                var tn = new MdDoc().TableProbeForTest("| 列A | 列B |\n| --- | --- |\n| a1 | b1 |\n", 40);
                Check("表格分支反面：放不下就整体退回段落（没有定位行）",
                    tn.TableLines == 0, $"表行={tn.TableLines} 宽=40");
                Check("关于节行顺序正确（⑦ DEC-055：三处位置各带「打开」；DEC-202 加「更新源 / 更新」两行；引擎那组已挪到 Strata 节）",
                    InOrder(SRow("settings-关于"), "本程序", "版本", "作者", "更新源", "更新", "帮助", "位置", "配置文件", "数据目录", "日志文件")
                    && new[] { "配置文件", "数据目录", "日志文件" }.All(k =>
                        SRow("settings-关于").Split('|').Any(seg => seg.Trim().StartsWith(k + "+") && seg.Contains("+打开")))
                    && !SRow("settings-关于").Contains("引擎"), SRow("settings-关于"));
                // 说明书 = 内嵌资源（csproj 的 EmbeddedResource + LogicalName=manual.md）；关于页「帮助」那枚读的就是它
                var manualMd = Dialogs.ManualMarkdown();
                Check("说明书内嵌资源在（关于页「帮助」那一枚读的就是它）",
                    manualMd.Contains("# MultiLauncher") && manualMd.Contains("术语表") && manualMd.Length > 3000,
                    manualMd.Length + " 字");
                Check("关于节有「帮助」那一行（说明书入口）",
                    SRow("settings-关于").Split('|').Any(seg => seg.Trim().StartsWith("帮助+")),
                    SRow("settings-关于"));
                // 说明书首行那句「适用版本」以前没人钉 → 落后三档没人发现（DEC-171 / DEC-173 各登记过一笔欠账）。
                // 现在与版本显示同一个家（`MainForm.VersionTag()`，不写字面量）：发版改号时那一行不跟着改就当场红。
                var manualVerLine = manualMd.Replace('\r', '\n').Split('\n')
                    .FirstOrDefault(l => l.Contains("适用版本")) ?? "";
                Check("说明书「适用版本」那一行 = 当前版本（与 VersionTag() 同源，发版要一起改那一行）",
                    manualVerLine.Length > 0 && manualVerLine.Contains(MainForm.VersionTag()),
                    "那一行『" + manualVerLine.Trim() + "』需含 " + MainForm.VersionTag());
                var mt = new MdDoc().TableProbeForTest(manualMd, 600);
                var mov = new MdDoc().TableOverflowForTest(manualMd, 600);
                Check("说明书里的表格走的是表格分支（不是当段落排成一堆竖线）",
                    mt.TableLines > 0, $"表行={mt.TableLines} / 全篇 {mt.TextLines} 行");
                Check("说明书里的表格同样不得重叠（全篇一起量）",
                    mov <= 0, "越界 " + mov + "px");
                Check("说明书里的表格也撑满可用宽度（**量最窄的那一条**：他说「很多表格都比较窄」）",
                    mt.MinBandR >= 560, $"最窄带右界={mt.MinBandR} / 可用 580");
                Check("说明书里的行内代码是胶囊态那一种（等宽字体 + 默认色）",
                    mt.CodeRuns > 0 && mt.CodeRunsBranded == 0,
                    $"代码段={mt.CodeRuns} 其中品牌绿={mt.CodeRunsBranded}");
                Check("llama.cpp 节行顺序正确（DEC-110④：独立的「更新」组并进「维护」，行序 = 更新源 → 版本 → 回退）",
                    InOrder(SRow("settings-llama.cpp"), "路径", "llama.cpp 目录", "模型配置", "模型目录", "运行",
                        "同时加载上限", "空闲停服(分钟)", "维护", "更新源", "版本", "更新…", "改动前自动备份", "恢复…")
                    && !SRow("settings-llama.cpp").Contains("用原始 ini 启动")   // DEC-079：没有「合并」这回事了，兜底开关一起退役
                    && !SRow("settings-llama.cpp").Contains("升级到")     // DEC-058：那三行搬进更新窗了，设置页不留
                    && !SRow("settings-llama.cpp").Contains("检查更新")
                    && !SRow("settings-llama.cpp").Contains("旧目录原样保留")   // ④ 复验：这条解压说明也删了（用户：没必要）
                    && !SRow("settings-llama.cpp").Contains("内部端口")
                    && !SRow("settings-llama.cpp").Contains("M1 已接上")
                    && !SRow("settings-llama.cpp").Contains("还没接上"), SRow("settings-llama.cpp"));
                // DEC-152：三页空闲标签一字不差 = 「空闲停服(分钟)」（他原话"统一成「空闲停服」"；
                // 「空闲阈值(分钟)」/「空闲卸载(分钟)」两种写法退役）。计数穷举：Strata 1 / Ninfer 2（WSL+WIN）/ llama 1。
                Check("DEC-152 三页空闲标签统一「空闲停服(分钟)」（空闲阈值 / 空闲卸载 两种写法退役）",
                    SRow("settings-Strata").Split('|').Count(seg => seg.Trim().StartsWith("空闲停服(分钟)+")) == 1
                    && SRow("settings-Ninfer").Split('|').Count(seg => seg.Trim().StartsWith("空闲停服(分钟)+")) == 2
                    && SRow("settings-llama.cpp").Split('|').Count(seg => seg.Trim().StartsWith("空闲停服(分钟)+")) == 1
                    && !SRow("settings-Ninfer").Contains("空闲阈值")
                    && !SRow("settings-llama.cpp").Contains("空闲卸载"),
                    "Strata=" + SRow("settings-Strata").Split('|').Count(seg => seg.Trim().StartsWith("空闲停服(分钟)+"))
                    + " Ninfer=" + SRow("settings-Ninfer").Split('|').Count(seg => seg.Trim().StartsWith("空闲停服(分钟)+"))
                    + " llama=" + SRow("settings-llama.cpp").Split('|').Count(seg => seg.Trim().StartsWith("空闲停服(分钟)+")));

                // 2026-10-02 反馈修订：不再跟随主区激活页（两节都出现，见上）；停用模块则整节消失（全隐身）
                using (var sfNoLlama = new SettingsForm(uiForm, new Settings { EnableLlama = false }))
                {
                    sfNoLlama.CreateControl();
                    var line = sfNoLlama.DumpLayoutRows().FirstOrDefault(x => x.StartsWith("settings-tabs", StringComparison.Ordinal)) ?? "";
                    Check("隐身：llama 停用时设置窗无 llama.cpp 节",
                        line == "settings-tabs: 通用 | " + string.Join(" | ",
                            ModuleRegistry.All.Where(m => m.Key != MainForm.LlamaKey).Select(m => m.DisplayName)) + " | 关于", line);
                }
                using (var sfNoStrata = new SettingsForm(uiForm, new Settings { EnableStrata = false }))
                {
                    sfNoStrata.CreateControl();
                    var line = sfNoStrata.DumpLayoutRows().FirstOrDefault(x => x.StartsWith("settings-tabs", StringComparison.Ordinal)) ?? "";
                    Check("隐身：Strata 停用时设置窗无 Strata 节",
                        line == "settings-tabs: 通用 | " + string.Join(" | ",
                            ModuleRegistry.All.Where(m => m.Key != MainForm.StrataKey).Select(m => m.DisplayName)) + " | 关于", line);
                }

    }
}
