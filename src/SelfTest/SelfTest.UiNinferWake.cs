using System.Text;
namespace MultiLauncher;

// 界面排布组（Ninfer 唤醒段）：DEC-135 唤醒按「那一段」取参不读界面卡 + 子窗体的创建与初判
//（设置窗 sf / 更新窗 uf·lf；nf·wf 两扇在 UiUpdateWindow 那一段里建）。2026-10-15 从
// SelfTest.UiLayout.cs 按子域切出，块内一行未改；窗体字段与释放见 SelfTest.State.cs。
internal static partial class SelfTest
{
    internal static void UiNinferWakeChecks()
    {
                // ============ DEC-135（拍板①）：Ninfer 的唤醒按「那一段」取参，不读界面卡 ============
                // 根因与 Strata 那次一模一样（DEC-122① 背景②）：换下拉只是看一眼、会把所看那件载上卡，
                // 而这一族的界面格子正是起进程时的追加清单 → 请求 A 会带着 B 的上下文 / KV / 采样值起来。
                // 他文件里两段 WIN 的带值旗标**实测完全一样**（拿它当证人就是空判据），所以在这趟自己的副本里
                // （⓪ 护栏早把 Ninfer 家底整窝改道 data\tmp\selftest-ninfer\）追加一段已知形状的合成样本，
                // 跑完按字节还原 —— 判据不能指望用户文件里"恰好有区分度"（DEC-126 第②条那条纪律）。
                {
                    var ninferIni = MainForm.NinferIniPath();
                    var winReal = uiForm.ReadNinferSections()
                        .Where(x => MainForm.SecBackend(x) == NinferIni.BackendWin).ToList();
                    var bakNinfer = uiForm.NinferSelectedTagForTest;
                    if (winReal.Count >= 1 && File.Exists(ninferIni))
                    {
                        const string ghostSec = "selftest-dec135-A";
                        var bytesBak = File.ReadAllBytes(ninferIni);
                        var prevDry2 = MainForm.NinferStartDryRunForTest;
                        MainForm.NinferStartDryRunForTest = true;
                        string why = "";
                        try
                        {
                            File.AppendAllText(ninferIni,
                                "\n[" + ghostSec + "]\nbackend = win\nfile = selftest-fake.ninfer\nmax-context = 123456\nkv-dtype = int8\n",
                                new UTF8Encoding(false));
                            var secA = uiForm.ReadNinferSections().First(x => x.Name == ghostSec);
                            var secB = winReal[0];
                            string ValOf(Dictionary<string, string> d, string k) => d.TryGetValue(k, out var v) ? v : "";
                            var effA = MainForm.NinferEffectiveFromArgs(secA.Args);
                            var effB = MainForm.NinferEffectiveFromArgs(secB.Args);
                            const string dk = "max-context";
                            Check("DEC-135 用例前提：合成那段与真那一段的最大上下文确实不同（相同 = 样本没区分度，宁可红）",
                                ValOf(effA, dk) == "123456" && ValOf(effB, dk).Length > 0 && ValOf(effA, dk) != ValOf(effB, dk),
                                $"A={ValOf(effA, dk)} B({secB.Name})={ValOf(effB, dk)}");
                            uiForm.ApplyNinferRecord(secB.Name);          // 界面卡此刻摆的是 B（"看一眼"就把 B 载上卡）
                            uiForm.StartNinferServerForTest(secA.Name, wakeFromSection: true);
                            var wakeS = uiForm.NinferLastStartSettingsForTest;
                            uiForm.StartNinferServerForTest(secB.Name, wakeFromSection: false);
                            var cardS = uiForm.NinferLastStartSettingsForTest;
                            var wakeArgv = uiForm.BuildNinferArgvLine(secA, wakeS, NinferIni.BackendWin, 8069);
                            var cardArgv = uiForm.BuildNinferArgvLine(secA, cardS, NinferIni.BackendWin, 8069);
                            why = $"{dk}：段={ValOf(effA, dk)} 卡上={ValOf(effB, dk)} 唤醒取={MainForm.NinferUiValue(dk, wakeS, NinferIni.BackendWin)} argv里={NinferIni.FlagValue(NinferIni.Split(wakeArgv), dk)}";
                            Check("DEC-135 Ninfer 唤醒那一路取的是那一段里的值（卡上摆着别件也不带过来；打在真入口的证人位上，不是纯算式）",
                                MainForm.NinferUiValue(dk, wakeS, NinferIni.BackendWin) == ValOf(effA, dk)
                                && NinferIni.FlagValue(NinferIni.Split(wakeArgv), dk) == ValOf(effA, dk), why);
                            Check("DEC-135 手动那一击照旧吃界面卡现值（他点加载就是想看刚改的那一格，试验流一个字没动）",
                                MainForm.NinferUiValue(dk, cardS, NinferIni.BackendWin) == ValOf(effB, dk)
                                && !string.Equals(wakeArgv, cardArgv, StringComparison.Ordinal),
                                $"手动取={MainForm.NinferUiValue(dk, cardS, NinferIni.BackendWin)} 两条 argv 不同={!string.Equals(wakeArgv, cardArgv, StringComparison.Ordinal)}");
                            Check("DEC-135 Ninfer 那一行从此要说清按哪一份起（WakeFromHomeNote 那一档不再留空）",
                                uiForm.View("ninfer").Gateway!.WakeFromHomeNote!(ghostSec) == "按 " + ghostSec + " 段里的那一把启动",
                                uiForm.View("ninfer").Gateway!.WakeFromHomeNote!(ghostSec));
                            // DEC-157（他批的第 4 条 = "走段"）：**后端归属也归段说了算**。以前这一句没有 →
                            // `ActiveNinferBackend(s)` 量的是界面那枚方块 → 方块停在 WSL 时请求 WIN 那件，
                            // 下一步就撞在「这一段不属于当前后端」上并弹模态框（他实跑抓的第二条）。
                            var prevFace = uiForm.NinferFaceForTest;
                            uiForm.SetNinferFaceForTest(NinferIni.BackendWsl);        // 故意让方块与段不一致
                            uiForm.StartNinferServerForTest(secA.Name, wakeFromSection: true);
                            var wakeBackend = uiForm.NinferLastStartSettingsForTest.NinferBackend;
                            uiForm.SetNinferFaceForTest(prevFace);
                            Check("DEC-157 走段：唤醒那一路连后端都按段自己那一侧交给起件那一步（方块停在另一侧也一样）",
                                wakeBackend == NinferIni.BackendWin,
                                $"段 = {NinferIni.BackendWin}、方块先摆成 {NinferIni.BackendWsl}、交给起件的 = {wakeBackend}");
                            Check("DEC-157 走段的用例前提：那一段自己声明的 backend 就是 WIN（段与方块不同侧才测得出跟谁，相同 = 空判据）",
                                MainForm.SecBackend(secA) == NinferIni.BackendWin, MainForm.SecBackend(secA));
                            // DEC-136（C-1）：禁用旗标以前判的是段里的模板，而「附加参数」挂在实际 argv 末尾 = 正好是盲区
                            //（`--disk-kv-*` 那条的后果是预分配整块文件，实测落盘 68.6 GB，不是"起不来"那么轻）
                            var banSec = new NinferIni.Section { Name = ghostSec, Args = "--max-context 8192" };
                            var banS = new Settings { NinferModel = ghostSec, NinferBackend = NinferIni.BackendWin,
                                                      NinferExtraArgs = "--disk-kv-size 8" };
                            var bannedInArgv = uiForm.NinferPreflight(banSec, banS, NinferIni.BackendWin,
                                NinferIni.Split("--max-context 8192 --disk-kv-size 8"));
                            Check("DEC-136 C-1 起前护栏判实际那一把命令行：附加参数里写 --disk-kv-* 也拦得下（按回「判段里的模板」这条就红）",
                                bannedInArgv is not null && bannedInArgv.Contains("禁用"), bannedInArgv ?? "null（没拦住）");
                            Check("DEC-136 C-1 反面：argv 里没有禁用旗标时不许误拒（判据在命令行上，不靠猜）",
                                (uiForm.NinferPreflight(banSec, banS, NinferIni.BackendWin,
                                    NinferIni.Split("--max-context 8192 --max-shared-prefixes 0"))
                                 ?? "").Contains("禁用") == false, "误拒了");
                        }
                        finally
                        {
                            MainForm.NinferStartDryRunForTest = prevDry2;
                            try { File.WriteAllBytes(ninferIni, bytesBak); } catch { /* 副本还原不动也只是脏了 data\tmp，真件一个字没碰 */ }
                            uiForm.ApplyNinferRecord(bakNinfer.Length > 0 ? bakNinfer : winReal[0].Name);   // 卡按回去
                        }
                    }
                    else Check("DEC-135 用例前提：Ninfer 那份家底副本在、且至少有一段 WIN", false,
                        "副本=" + (File.Exists(ninferIni) ? "在" : "不在") + "，WIN 段数=" + winReal.Count);
                }
                // DEC-078（用户第②③条）：弹列表宽度必须与输入框对得上；只有长名字那两只保留地板
                {
                    var dw = uiForm.DropWidthsForTest();
                    W($"下拉「弹列表宽/控件宽」实测: {dw}");
                    bool Same(string seg) { var p = seg.Split('=')[1].Split('/'); return int.Parse(p[0]) == int.Parse(p[1]); }
                    bool NotNarrower(string seg) { var p = seg.Split('=')[1].Split('/'); return int.Parse(p[0]) >= int.Parse(p[1]); }
                    var parts = dw.Split(' ');
                    Check("KV 精度（两页各一只）/ 思考深度（当前页那只）/ 高级区下拉（含 DEC-145 两枚）：弹列表与输入框同宽",
                        parts.Take(6).All(Same), dw);
                    Check("模型下拉：列表不窄于输入框（长名字 + 估算占用 + 已加载，留了地板）",
                        NotNarrower(parts[^1]), parts[^1]);
                    // 吃余量的是监听地址：卡加宽 60 逻辑，只有它跟着变宽（端口与思考深度都是固定宽）
                    var (hostD, portD, reasonD, cw0, cw1) = uiForm.Card1GrowSlotsForTest();
                    var w1 = uiForm.Card1WidthsForTest();
                    Check("卡1：监听地址吃余量、监听端口与思考深度固定宽（DEC-078 第③条宽度交换）",
                        cw1 > cw0 && hostD > 0 && portD == 0 && reasonD == 0,
                        $"卡宽 {cw0}->{cw1}｜增量 host={hostD} port={portD} reason={reasonD}" +
                        $"｜当前宽 host={w1.host} port={w1.port} reason={w1.reason}");
                    Check("模型下拉提示已精简（用户原话：废话太多）",
                        MainForm.ModelDropTip == "只列当前激活模块的模型；服务启停时重新扫描；每项尾部是估算占用"
                        && !MainForm.ModelDropTip.Contains("已加载") && !MainForm.ModelDropTip.Contains("router_models"),
                        MainForm.ModelDropTip);
                }
                Check("卡片3 顺序 = 模型｜加载｜清屏",
                    InOrder(Row("card3"), "模型", "加载", "清屏"), Row("card3"));
                Check("高级区(Strata) = 指定显卡入、KV 精度出（顺序正确；DEC-076 候选：思考深度回卡1）",
                    InOrder(Row("adv-strata"), "指定显卡", "投机解码", "最低接受率", "显存预留", "expert-cache", "prefill",
                        "视觉", "层拆分起点", "arena pin", "KV 驻留内存", "STRATA_HEAD_LATE")
                    && !Row("adv-strata").Contains("KV 精度") && !Row("adv-strata").Contains("附加参数")
                    && !Row("adv-strata").Contains("思考深度") && !Row("adv-strata").Contains("低内存"), Row("adv-strata"));
                Check("高级区(llama.cpp) 项顺序正确（2026-10-04；DEC-145：投机方式/视觉两枚接在 spec-draft-n-max 之后）",
                    InOrder(Row("adv-llama"), "temperature", "top-p", "top-k", "min-p", "presence-penalty", "repeat-penalty",
                        "spec-draft-n-max", "投机方式", "视觉") && !Row("adv-llama").Contains("并行数") && !Row("adv-llama").Contains("思考深度"), Row("adv-llama"));
                // DEC-079：flash-attn 回来了，但形状换了 —— 用户口径「fa 只做开关，不做下拉，只有开关 2 态」，
                // 写在**选中模型那一段**（不是 2026-10-01 被否掉的"全局开关 + 覆盖每个段"）
                Check("llama 高级区有 flash-attn 两态开关（排在数值项之后；上下文滑动照旧不在界面里）",
                    InOrder(Row("adv-llama"), "spec-draft-n-max", "flash-attn")
                    && !Row("adv-llama").Contains("上下文滑动"), Row("adv-llama"));
                Check("选中 Strata 模型时高级区显示 Strata 那一套",
                    Row("adv-backend") == "adv-backend: strata", Row("adv-backend"));
                Check("界面里已没有 API 密钥 / 请求唤醒 / 配置新模型",
                    !rows.Any(x => x.Contains("API 密钥")) && !rows.Any(x => x.Contains("请求唤醒")) && !rows.Any(x => x.Contains("配置新模型")));
                // 2026-10-04 DEC-077 定「打开配置 = 引擎实际读的那一份（当时是融合产物）」，DEC-079 改 llama 那一支，
                // **DEC-122② 把 Strata 这一支改开 `MultiLauncher.ini`** —— 他要的是"保存写哪 / 打开看哪 / 唤醒读哪"三处对上。
                // 产物（data\config\strata\<tag>.json）从此不设专用入口：设置页「模型配置目录」那枚「打开」直达目录。
                var keepTag = uiForm.SelectedTagForTest;
                var cfgA = uiForm.ActiveConfigTarget();
                Check("打开配置(Strata) = MultiLauncher.ini 本身（DEC-122②：保存写哪 / 打开看哪 / 唤醒读哪 三处同一个家）",
                    string.Equals(cfgA.Path, MainForm.ConfigPathForTest, StringComparison.OrdinalIgnoreCase)
                    && Path.GetFileName(cfgA.Path) == MainForm.ConfigFileName
                    && !cfgA.Path.StartsWith(MainForm.RunConfigDir() + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    && !cfgA.Path.StartsWith(root, StringComparison.OrdinalIgnoreCase),
                    $"{cfgA.Path}｜{cfgA.Note}");
                Check("打开配置(Strata) 的说明改写：点名 [Model:<当前模型>] 那一段 + 模板在哪儿 + 手改要先退壳（DEC-122②）",
                    cfgA.Note.Contains("[Model:" + keepTag + "]") && cfgA.Note.Contains("点「保存」写的就是这里")
                    && cfgA.Note.Contains($"strata-{keepTag}.json") && cfgA.Note.Contains("壳只读不写")
                    && cfgA.Note.Contains("请先退出外壳"), cfgA.Note);
                Check("打开配置(Strata) 不再写那句「物化产物、在这上面改留不住」（落点换了，老说明跟着退役）",
                    !cfgA.Note.Contains("留不住"), cfgA.Note);
                // DEC-094 ⓪：自检期间 RunConfigDir 被改成 data\tmp\ 的副本（真实产物一份不写），
                // 所以这句断的是**默认落点** —— 真件那条路一个字没动。
                // DEC-118：默认落点从 exe 同目录的 data\run 归位成 data\config\strata（老目录由首启搬家带走）。
                Check("⓪ 改道只影响自检：默认落点是 data\\config\\strata（DEC-118 归位后；真件那条路没动）",
                    MainForm.RealRunConfigDir().EndsWith(
                        "config" + Path.DirectorySeparatorChar + "strata", StringComparison.OrdinalIgnoreCase)
                    && MainForm.RealRunConfigDir().StartsWith(
                        Path.Combine(AppContext.BaseDirectory, "data"), StringComparison.OrdinalIgnoreCase),
                    MainForm.RealRunConfigDir());
                // DEC-118 第 5 条那个坑：路径一改，"拍真实家底跑后逐字节比"那几条会退化成**空判据 = 假绿**
                //（目录不存在 → 快照永远是空表 → 永远比得过）。所以这里把**落点形状**钉死（硬判据），
                // 在场性写成可见的明细：搬家还没跑的那台机器上是"老址"，这不是错，但必须看得见。
                string HomeWhere(string newDir, string fileName, string oldName)
                {
                    if (File.Exists(Path.Combine(newDir, fileName))) return "新址";
                    if (File.Exists(Path.Combine(AppContext.BaseDirectory, oldName))) return "老址（搬家还没跑）";
                    return "两处都没有";
                }
                Check("DEC-118 三份配置本体与两格备份的落点形状钉死（防 ⓪ 护栏退化成空判据）",
                    MainForm.RealRunConfigDir().EndsWith(Path.Combine("config", "strata"), StringComparison.OrdinalIgnoreCase)
                    && MainForm.RealRouterIniBackupDir().EndsWith(Path.Combine("backup", "llama"), StringComparison.OrdinalIgnoreCase)
                    && MainForm.RealNinferIniBackupDir().EndsWith(Path.Combine("backup", "ninfer"), StringComparison.OrdinalIgnoreCase)
                    && MainForm.RealNinferExeBackupDir().EndsWith(Path.Combine("backup", "ninfer-exe"), StringComparison.OrdinalIgnoreCase)
                    && !MainForm.RealRunConfigDir().EndsWith(Path.DirectorySeparatorChar + "run", StringComparison.OrdinalIgnoreCase),
                    "llama=" + HomeWhere(MainForm.ModuleConfigDir("llama"), MainForm.LlamaIniFileName, MainForm.LlamaIniFileName)
                    + "｜ninfer=" + HomeWhere(MainForm.ModuleConfigDir("ninfer"), MainForm.NinferIniFileName, MainForm.NinferIniFileName)
                    + "｜" + MainForm.RealRunConfigDir());
                {
                    // 搬家本身：沙盒里喂样本（假 exe 目录 + 假 data + 假 Strata 根），
                    // 真路径那一步由 LayoutMigrationDisabledForTest 挡着，自检一个字都不碰他家底。
                    var lay = MainForm.Scratch("selftest-layout");
                    MainForm.TryDeleteTree(lay);
                    var fakeExe = Path.Combine(lay, "app");
                    var fakeData = Path.Combine(lay, "app", "data");
                    var fakeStrata = Path.Combine(lay, "Strata");
                    Directory.CreateDirectory(fakeExe);
                    Directory.CreateDirectory(fakeStrata);
                    File.WriteAllText(Path.Combine(fakeExe, MainForm.LlamaIniFileName), "[*]\n");
                    File.WriteAllText(Path.Combine(fakeExe, MainForm.NinferIniFileName), "[a]\n");
                    Directory.CreateDirectory(Path.Combine(fakeData, "run"));
                    File.WriteAllText(Path.Combine(fakeData, "run", "iq3_s.json"), "{}");
                    File.WriteAllText(Path.Combine(fakeData, "run", "iq3_s.shared-settings.json"), "{}");
                    Directory.CreateDirectory(Path.Combine(fakeData, "llama"));
                    File.WriteAllText(Path.Combine(fakeData, "llama", "llama_models-20261006-081858.ini"), "x");
                    Directory.CreateDirectory(Path.Combine(fakeData, "infer"));
                    File.WriteAllText(Path.Combine(fakeData, "infer", "ninfer_models-20261008-231635.ini"), "y");
                    File.WriteAllText(Path.Combine(fakeData, "infer", "ninfer-serve-20261008-203246.exe"), "z");
                    Directory.CreateDirectory(Path.Combine(fakeData, "backup", "0.1.40.2-20261008-211014"));
                    File.WriteAllText(Path.Combine(fakeData, "backup", "0.1.40.2-20261008-211014", "about.txt"), "s");
                    var layNotes = MainForm.MigrateLauncherLayout(fakeExe, fakeData, fakeStrata);
                    var snapHome = MainForm.StrataBackupRoot(fakeStrata);
                    Check("DEC-118 搬家正脸：两份家底进 config\\<模块>、data\\run 整层进 config\\strata（产物与 shared 不拆散）、老位置与老目录都不留",
                        File.Exists(Path.Combine(fakeData, "config", "llama", MainForm.LlamaIniFileName))
                        && File.Exists(Path.Combine(fakeData, "config", "ninfer", MainForm.NinferIniFileName))
                        && File.Exists(Path.Combine(fakeData, "config", "strata", "iq3_s.json"))
                        && File.Exists(Path.Combine(fakeData, "config", "strata", "iq3_s.shared-settings.json"))
                        && !File.Exists(Path.Combine(fakeExe, MainForm.LlamaIniFileName))
                        && !Directory.Exists(Path.Combine(fakeData, "run")),
                        layNotes.Count + " 条：" + string.Join("；", layNotes));
                    Check("DEC-116 备份分格：配置备份进 backup\\llama 与 backup\\ninfer，引擎 exe 单独进 backup\\ninfer-exe（前缀仍是硬判据；DEC-124 定案这一层**留在 backup 不动**，挪的只有整份快照）",
                        File.Exists(Path.Combine(fakeData, "backup", "llama", "llama_models-20261006-081858.ini"))
                        && File.Exists(Path.Combine(fakeData, "backup", "ninfer", "ninfer_models-20261008-231635.ini"))
                        && File.Exists(Path.Combine(fakeData, "backup", "ninfer-exe", "ninfer-serve-20261008-203246.exe"))
                        && !Directory.Exists(Path.Combine(fakeData, "infer"))
                        && !Directory.Exists(Path.Combine(fakeData, "llama")), "");
                    Check("DEC-116 + DEC-124 整份快照归位：落点 = Strata 自己的数据目录里那一格 `D:\\Strata-data\\backup\\<版本-时间戳>`（用户点名，不再另开同级 D:\\Strata-backup），"
                          + "且**老位置只读不删**（他原话：旧的位置不要删除，留着我手动删除并提醒我）",
                        snapHome.EndsWith(Path.Combine("Strata-data", "backup"), StringComparison.OrdinalIgnoreCase)
                        && !snapHome.StartsWith(fakeStrata + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                        && Directory.Exists(Path.Combine(snapHome, "0.1.40.2-20261008-211014"))
                        && File.Exists(Path.Combine(snapHome, "0.1.40.2-20261008-211014", "about.txt"))
                        && File.Exists(Path.Combine(fakeData, "backup", "0.1.40.2-20261008-211014", "about.txt")),   // 复制，不是搬走
                        snapHome);
                    var layAgain = MainForm.MigrateLauncherLayout(fakeExe, fakeData, fakeStrata);
                    Check("DEC-118 搬家幂等 + 新形状不误伤：第二次一条都不搬（backup 下的模块名格不许被当成快照搬走；复制那一跳第二次就一个字不写）",
                        layAgain.Count == 0 && Directory.Exists(Path.Combine(fakeData, "backup", "llama"))
                        && Directory.Exists(Path.Combine(fakeData, "backup", "ninfer-exe")),
                        layAgain.Count + " 条：" + string.Join("；", layAgain));
                    File.WriteAllText(Path.Combine(fakeExe, MainForm.LlamaIniFileName), "OLD");
                    var clashNotes = MainForm.MigrateLauncherLayout(fakeExe, fakeData, fakeStrata);
                    Check("DEC-118「两边都有一份 = 不覆盖、点名」：老那份留在原地、新位置那份一个字节没被顶掉（不替他选一份家底）",
                        File.ReadAllText(Path.Combine(fakeExe, MainForm.LlamaIniFileName)) == "OLD"
                        && File.ReadAllText(Path.Combine(fakeData, "config", "llama", MainForm.LlamaIniFileName)) == "[*]\n"
                        && clashNotes.Any(x => x.Contains("新位置已经有一份")),
                        clashNotes.Count + " 条：" + string.Join("；", clashNotes));
                    // ---- DEC-121（他实跑抓的那一条）：整层搬家不能用 `Directory.Move`。
                    // 同卷时它侥幸成功，所以样本必须喂"目标已经有一半"那种半截状态 —— 实跑形状是 D:\MultiLauncher\data\backup → D:\Strata-backup（跨卷），
                    // 旧实现在第一个子目录上就抛，结果是 about.txt 搬走了、engine\ 与 source\（830 MB）原地不动。
                    var halfData = Path.Combine(lay, "half");
                    var halfExe = Path.Combine(halfData, "app");
                    var halfStrata = Path.Combine(lay, "Strata2");
                    var halfRoot = MainForm.StrataBackupRoot(halfStrata);
                    var halfSnapSrc = Path.Combine(halfData, "data", "backup", "0.1.40.4-20261009-011816");
                    var halfSnapDst = Path.Combine(halfRoot, "0.1.40.4-20261009-011816");
                    MainForm.TryDeleteTree(halfData); MainForm.TryDeleteTree(halfRoot);
                    Directory.CreateDirectory(Path.Combine(halfSnapSrc, "engine", "sub"));
                    Directory.CreateDirectory(Path.Combine(halfSnapSrc, "source"));
                    File.WriteAllText(Path.Combine(halfSnapSrc, "about.txt"), "NEW");
                    File.WriteAllText(Path.Combine(halfSnapSrc, "source", "server.py"), "S");
                    File.WriteAllText(Path.Combine(halfSnapSrc, "engine", "strata.exe"), "E");
                    File.WriteAllText(Path.Combine(halfSnapSrc, "engine", "sub", "deep.dll"), "D");
                    Directory.CreateDirectory(halfSnapDst);                                                  // 上一次没搬完：目标只剩这一格 + about.txt
                    File.WriteAllText(Path.Combine(halfSnapDst, "about.txt"), "OLD-KEPT");
                    Directory.CreateDirectory(Path.Combine(halfSnapDst, "source"));                          // 目标已有同名**子目录**（跨卷时旧实现就是在这一格上抛的）
                    File.WriteAllText(Path.Combine(halfSnapDst, "source", "README.txt"), "K");
                    var halfNotes = MainForm.MigrateLauncherLayout(halfExe, Path.Combine(halfData, "data"), halfStrata);
                    Check("DEC-121 整层归位改走递归复制（嵌套子目录也带上、目标已有同名子目录 = 并进去把半截状态补完），**老位置一个字都不删**（DEC-124 用户口径）",
                        File.Exists(Path.Combine(halfSnapDst, "source", "server.py"))
                        && File.Exists(Path.Combine(halfSnapDst, "source", "README.txt"))      // 目标里原有那份不许被顶掉
                        && File.Exists(Path.Combine(halfSnapDst, "engine", "strata.exe"))
                        && File.Exists(Path.Combine(halfSnapDst, "engine", "sub", "deep.dll"))
                        && Directory.Exists(Path.Combine(halfSnapSrc, "engine"))               // 复制，不是搬走
                        && File.ReadAllText(Path.Combine(halfSnapSrc, "engine", "strata.exe")) == "E",
                        halfNotes.Count + " 条：" + string.Join("；", halfNotes));
                    Check("DEC-121 补完半截状态时「同名文件不顶掉」照旧赢：目标那份 about.txt 一个字节没被改，日志也报了有几个同名没顶掉",
                        File.ReadAllText(Path.Combine(halfSnapDst, "about.txt")) == "OLD-KEPT"
                        && halfNotes.Any(x => x.Contains("没被顶掉")),
                        string.Join("；", halfNotes));
                    Check("DEC-124 这一跳幂等：新落点已经有同样的东西就一个字不写，老位置照旧留着等他删",
                        MainForm.MigrateStrataSnapshots(Path.Combine(halfData, "data"), halfStrata).Count == 0
                        && Directory.Exists(halfSnapSrc), halfSnapSrc);
                    var hollow = Directory.CreateDirectory(Path.Combine(halfRoot, "0.1.40.1-20261009-000000")).FullName;   // 只有壳、没货
                    Check("DEC-121「回退…」只列形状完整的快照：只剩 about.txt / 空壳那种上不了桌（点下去必炸，宁可说还没有升级备份）",
                        MainForm.SnapshotHasPayload(halfSnapDst) && !MainForm.SnapshotHasPayload(hollow)
                        && !MainForm.ListBackups(halfStrata).Contains("0.1.40.1-20261009-000000")
                        && MainForm.ListBackups(halfStrata).Contains("0.1.40.4-20261009-011816"),
                        string.Join("、", MainForm.ListBackups(halfStrata)));
                    // 构造函数那一步只做 ①②③（同卷改名 = 毫秒级），④ 交给窗开起来之后的后台：跨卷那是真复制几百 MB，摆开窗路上就是"先卡一下"
                    var lightData = Path.Combine(lay, "light");
                    var lightSnap = Path.Combine(lightData, "backup", "0.1.38-20261001-000000");
                    Directory.CreateDirectory(lightSnap);
                    File.WriteAllText(Path.Combine(lightSnap, "about.txt"), "L");
                    var lightNotes = MainForm.MigrateLauncherLayout(Path.Combine(lightData, "app"), lightData, halfStrata, snapshots: false);
                    Check("DEC-121 真路径拆两步：snapshots:false 那一次（构造函数）一个快照都不碰，后台那一次才复制过去",
                        Directory.Exists(lightSnap) && lightNotes.All(x => !x.Contains("快照"))
                        && MainForm.MigrateStrataSnapshots(lightData, halfStrata).Any(x => x.Contains("Strata 升级快照"))
                        && File.Exists(Path.Combine(MainForm.StrataBackupRoot(halfStrata), "0.1.38-20261001-000000", "about.txt"))
                        && Directory.Exists(lightSnap),                 // 老位置留着，程序不删
                        lightNotes.Count + " 条：" + string.Join("；", lightNotes));
                    // DEC-138（E-4）：这条以前喂的是 `halfStrata` —— 那个 legacy 目录**从来没被建过**（上面只建了
                    // halfRoot 里的新版格），所以"没货可搬"与"闸挡住了"两种情况都回 0 条 = 空判据：
                    // 把 Models.cs 里那道三元判断整条删掉，它照旧绿。现在喂一份**真有货**的沙盒，两面各断一次。
                    var gateData = Path.Combine(lay, "gate");
                    var gateStrata = Path.Combine(lay, "StrataGate");
                    var gateOld = MainForm.StrataBackupRootLegacy(gateStrata);          // 老同级位置 = pending 的源头
                    var gateNew = MainForm.StrataBackupRoot(gateStrata);               // DEC-124 的新落点
                    var gateSnap = Path.Combine(gateOld, "0.1.36-20261010-120000");
                    Directory.CreateDirectory(Path.Combine(gateSnap, "engine"));
                    File.WriteAllText(Path.Combine(gateSnap, "engine", "strata.exe"), "GATE");
                    File.WriteAllText(Path.Combine(gateSnap, "about.txt"), "G");
                    Check("DEC-138 E-4 闸上那一面：老位置**真有货**，但自检这一趟 `MigrateStrataSnapshotsRealForTest` 一条都不动（新落点不长东西）",
                        MainForm.LayoutMigrationDisabledForTest
                        && MainForm.MigrateStrataSnapshotsRealForTest(gateData, gateStrata).Count == 0
                        && !Directory.Exists(Path.Combine(gateNew, "0.1.36-20261010-120000")),
                        "闸=" + MainForm.LayoutMigrationDisabledForTest);
                    // 放开那一面（同一道闸按回去 = 下面那条必须不再是 0 条；这两面合起来才把"闸有没有接上"钉住）
                    MainForm.LayoutMigrationDisabledForTest = false;
                    try
                    {
                        var openNotes = MainForm.MigrateStrataSnapshotsRealForTest(gateData, gateStrata);
                        Check("DEC-138 E-4 放开那一面：同一道判据把闸按回去就**真有货、真复制**（上面那条不是空转）",
                            openNotes.Count > 0
                            && File.ReadAllText(Path.Combine(gateNew, "0.1.36-20261010-120000", "engine", "strata.exe")) == "GATE"
                            && Directory.Exists(gateSnap),                  // DEC-124：这一跳只复制，老那份留着等他删
                            openNotes.Count + " 条：" + string.Join("；", openNotes));
                    }
                    finally { MainForm.LayoutMigrationDisabledForTest = true; }
                    // ---- DEC-124：快照落点从同级 `D:\Strata-backup\` 挪进 Strata 自己的数据目录 `D:\Strata-data\backup\`，
                    // 老那一头**只读不删**（"旧的位置不要删除，留着我手动删除并提醒我"），提醒只在里面还有快照时才写。
                    var mvData = Path.Combine(lay, "moveold");
                    var mvStrata = Path.Combine(lay, "Strata3");
                    var mvOld = MainForm.StrataBackupRootLegacy(mvStrata);           // DEC-116 那版 = lay\Strata3-backup
                    var mvNew = MainForm.StrataBackupRoot(mvStrata);                  // DEC-124 新落点 = lay\Strata3-data\backup
                    var mvSnap = Path.Combine(mvOld, "0.1.35-20261003-101010");
                    MainForm.TryDeleteTree(mvOld); MainForm.TryDeleteTree(mvNew);
                    Directory.CreateDirectory(Path.Combine(mvSnap, "source"));
                    File.WriteAllText(Path.Combine(mvSnap, "source", "server.py"), "OLD-SNAP");
                    File.WriteAllText(Path.Combine(mvSnap, "about.txt"), "A");
                    Directory.CreateDirectory(Path.Combine(mvData, "backup", "llama"));            // 配置备份格：不是快照，别碰
                    File.WriteAllText(Path.Combine(mvData, "backup", "llama", "llama_models-20261009-023239.ini"), "CFG");
                    var mvNotes = MainForm.MigrateStrataSnapshots(mvData, mvStrata);
                    Check("DEC-124 老同级位置里的整份快照**复制**进 D:\\Strata-data\\backup\\<版本-时间戳>，老那份一个字没删、配置备份格不误伤",
                        File.ReadAllText(Path.Combine(mvNew, "0.1.35-20261003-101010", "source", "server.py")) == "OLD-SNAP"
                        && Directory.Exists(Path.Combine(mvOld, "0.1.35-20261003-101010"))
                        && File.Exists(Path.Combine(mvData, "backup", "llama", "llama_models-20261009-023239.ini"))
                        && !Directory.Exists(Path.Combine(mvNew, "llama")),
                        mvNotes.Count + " 条：" + string.Join("；", mvNotes));
                    Check("DEC-124 这一跳幂等：新落点已经有同样那份就一个字不写（不重复复制一份 GB 级的东西）",
                        MainForm.MigrateStrataSnapshots(mvData, mvStrata).Count == 0, "第二次：" + string.Join("；", MainForm.MigrateStrataSnapshots(mvData, mvStrata)));
                    Check("DEC-124 提醒只在老位置**还有快照**时才写（他删完就闭嘴；只剩配置备份格那种不是快照，不算没搬完）",
                        MainForm.LegacySnapshotsPending(mvData, mvStrata) == mvOld
                        && MainForm.LegacySnapshotsPending(mvData, Path.Combine(lay, "Strata4")) is null
                        && MainForm.LegacySnapshotsPending(Path.Combine(lay, "nothing-here"), Path.Combine(lay, "Strata4")) is null,
                        MainForm.LegacySnapshotsPending(mvData, mvStrata) ?? "null");
                    Check("DEC-116 快照目录的样子判据只认「版本-yyyyMMdd-HHmmss」，模块名不算（这条挡住了第二次启动把配置格端走）",
                        MainForm.LooksLikeSnapshot("0.1.40.2-20261008-211014")
                        && MainForm.LooksLikeSnapshot("unknown-20261003-174544")
                        && !MainForm.LooksLikeSnapshot("llama") && !MainForm.LooksLikeSnapshot("ninfer-exe")
                        && !MainForm.LooksLikeSnapshot("strata") && !MainForm.LooksLikeSnapshot("config"));
                    Check("DEC-116 Strata 目录没配时的回落那一格不落进 data\\backup（backup 从此只装配置备份）",
                        !MainForm.StrataBackupRoot("").Contains(Path.Combine("data", "backup"),
                            StringComparison.OrdinalIgnoreCase), MainForm.StrataBackupRoot(""));
                    // ============ DEC-138（M6 缺口 5 / 缺口 2）：两条"只能扫源码"的判据 ============
                    // AGENTS 明写「代码里不许出现 `Directory.Move`」（它跨卷当场抛 IOException，而 File.Move 不挑卷 ——
                    // DEC-121 那次实跑就是栽在这上面：829 MB 原地不动、同卷的自检 763 条全绿）。这条禁令以前只靠论证，
                    // 现在机器化：命中数必须 0。D-6 那条正是这个缺口的现形。
                    // 拆成两截再拼：不然这一行自己就成了它要扫的那个字面（实测假红过一次，命中 1 处 = 这条判据本身）。
                    var dmHits = CodeHits("Directory" + ".Move(");
                    Check("DEC-138 缺口5 全仓源码里不许出现 Directory.Move（AGENTS 明令；命中数必须 0）",
                        dmHits is -1 or 0, dmHits == -1 ? "用例前提：源码不在旁边（实跑目录跑 --selftest 就是这种），这条只登记"
                                                        : $"命中 {dmHits} 处");
                    // DEC-091「端口那格三处收口：失焦 / 点保存 / 切页与关窗，少一条 = 给用户留个绕过弹窗的后门」——
                    // 以前只有纯函数 `PortChangeNeedsConfirm` 的判据，三个调用点一条都没断。现在数 where 串：四处（失焦 / 保存 / 切页 / 关窗）都在。
                    // 三条更新链的"停服务"都必须排在**各自的校验**之后（DEC-138 D-7 / 拍板⑤）。
                    // 这条判据的形状只能是源码次序：运行时那三条链要跑完一次真升级才看得见，而"校验不过 = 本机零影响"
                    // 说的正是**没到那一步**的分支 —— 让红/绿点名比让真升级点名便宜得多。
                    var ordStrata = CodeOrder("MainForm.UpdateWindow.cs", "SourceVersionMatches(srcRoot", "UiPost(StopServer)");
                    var ordLlama = CodeOrder("MainForm.LlamaUpdate.cs", "Net.VerifyDownload(zip", "UiPost(StopLlamaServer)");
                    var ordNinfer = CodeOrder("MainForm.UpdateNinferWin.cs", "Net.VerifyDownload(tmp", "UiPost(StopNinferServer)");
                    // 三条链的**入口那一击**只许问"要不要停"，不许当场停（这两段方法体里停服务那一句的出现数必须是 0）。
                    // 2026-10-16（DEC-185 遗留②收账）：停服务那一句的**真实形状**是 `UiPost(StopXxxServer)` —— 原来数的是
                    // `StopServer();`，那个形状早随 DEC-138 D-7 那批改没了，于是这两条判据恒报 -2（= 判据自己空转）。
                    var entryStrata = CodeHitsBetween("MainForm.UpdateWindow.cs", "internal void DoUpgrade()", "void UpgradeWork(", "UiPost(StopServer)");
                    var entryLlama = CodeHitsBetween("MainForm.LlamaUpdate.cs", "internal void LlamaUpgrade()", "void LlamaUpgradeWork(", "UiPost(StopLlamaServer)");
                    // Ninfer 那条链的"停"与"校验"本来就写在同一个方法体里（Task 内联），没有隔方法的盲区 ——
                    // 它的入口只出现在 `NinferUpgrade()` 这一段，而这一段里合法的那一次停**必须**在校验之后（= 上面那条次序判据）。
                    // 这里数的是旧写法那一句的形状（`UiPost(() => { if (NinferRunning) StopNinferServer(); })`），它必须 0 次。
                    var entryNinfer = CodeHits("UiPost(() => { if (NinferRunning) " + "StopNinferServer(); });");
                    Check("DEC-138 D-7 三条更新链的「停服务」都排在校验之后（挪回下载前面 = 这条红；-1=没源码 / -2=判据自己没跑通）",
                        ordStrata is -1 or 0 && ordLlama is -1 or 0 && ordNinfer is -1 or 0,
                        $"strata={ordStrata} llama={ordLlama} ninfer={ordNinfer}");
                    Check("DEC-138 D-7 另一半：入口那一击只问不停（Strata / llama 那两段方法体里停服务必须 0 次；Ninfer 那条数旧写法那一句的形状 = 0 —— 次序判据隔着方法看不见它）",
                        entryStrata is -1 or 0 && entryLlama is -1 or 0 && entryNinfer is -1 or 0,
                        $"strata={entryStrata} llama={entryLlama} ninfer={entryNinfer}");

                    // 缺口 4（DEC-110① 同一族的第五次）：AGENTS 写「有一条按通道的断言盯着这两句」，实测只盯了
                    // 「界面参数已保存。」那一句 —— 这一条把第二句也钉住。形状只能是源码判据：这句在"没有可选模型"时才打得开，
                    // 而无头环境里模型表是用户家底喂出来的（不该再引入一条"恰好没模型"的环境判据 = E-2 那一族）。
                    var noModelHits = CodeHits("LogActive(\"还没有可选模型。\")");
                    Check("DEC-138 缺口4 「还没有可选模型。」那句走激活模块通道（源码判据；从前 AGENTS 说有两句、断言只盯了一句 = 文档与实值漂）",
                        noModelHits is -1 or 1, noModelHits == -1 ? "用例前提：源码不在旁边，这条只登记" : $"源码里 {noModelHits} 处");

                    var settleHits = CodeHits("SettleNinferPort(\"");
                    Check("DEC-138 缺口2 端口结算那条真有三个以上收口点（数源码里的调用，纯函数那条挡不住漏接的调用点）",
                        settleHits is -1 or >= 4, settleHits == -1 ? "用例前提：源码不在旁边，这条只登记"
                                                                   : $"源码里 {settleHits} 处调用（定义那行不带引号，不算）");

                    // DEC-138（E-4）：原来这条只是 `Check(MainForm.LayoutMigrationDisabledForTest)` ——
                    // 读的是自检自己在入口设的那个位，等于"我设了所以我通过"，真路径把那道三元判断删掉它照样绿。
                    // 现在并进上面那两面（同一道闸、同一个判据家）：这里只再钉一句"闸在整个自检期间是开着的"，
                    // 由两面那两条负责证明闸**有效**。（真路径的单参重载不在这里调 —— 闸万一失效它就会去搬真家底，
                    // 那正是两面判据要防的事；防它的手段是判据本身，不是再冒一次险。）
                    Check("DEC-118 布局搬家那道闸在自检全程开着（有效性见上面那两面，这里只钉状态）",
                        MainForm.LayoutMigrationDisabledForTest, "闸=" + MainForm.LayoutMigrationDisabledForTest);
                }
                // DEC-122①：唤醒那一行日志（只在真起一次时报、不带参数明细 = 他定点"没必要"）
                // 取参本身的两条在上面那段（有可控的"文件 vs 草稿"现场），这里只钉句子。
                Check("DEC-122① 切件那一句点名「按 X 的存档启动」，且不带上下文 / KV 明细",
                    MainForm.SwitchToWakeLine("swift", "iq3_s", "按 swift 的存档启动")
                        == "请求要的是 swift，当前跑的是 iq3_s：先停旧的，按 swift 的存档启动"
                    && !MainForm.SwitchToWakeLine("swift", "iq3_s", "按 swift 的存档启动").Contains("上下文"),
                    MainForm.SwitchToWakeLine("swift", "iq3_s", "按 swift 的存档启动"));
                Check("DEC-122① 这一族不说那句话时句子与升级前逐字节相同（llama 不挂 hooks = 一个字都不多）",
                    MainForm.SwitchToWakeLine("swift", "iq3_s", null) == "请求要的是 swift，当前跑的是 iq3_s：先停旧的",
                    MainForm.SwitchToWakeLine("swift", "iq3_s", null));
                // DEC-135（拍板①）：Ninfer 的唤醒也改成"按那一段自己"取参了 → 这一族从此**有另一份参数要交代**，
                // 所以那一行也给句子（形状同 Strata，只说按什么起、不带参数明细）。判据按模块表穷举：
                // **挂了网关 hooks 的行每一行都要说**，不挂的（llama）一个字都不许多 —— 别再写死"今天只有 Strata"。
                Check("DEC-122① / DEC-135 挂网关 hooks 的模块每一行都说清按哪一份起，没挂的一句都不说（按表穷举）",
                    ModuleRegistry.All.Where(m => uiForm.View(m.Key).Gateway is not null)
                        .All(m => uiForm.View(m.Key).Gateway!.WakeFromHomeNote is not null)
                    && ModuleRegistry.All.Any(m => uiForm.View(m.Key).Gateway is null)
                    && uiForm.View("strata").Gateway!.WakeFromHomeNote!("swift") == "按 swift 的存档启动"
                    && uiForm.View("ninfer").Gateway!.WakeFromHomeNote!("swift") == "按 swift 段里的那一把启动",
                    string.Join(" ｜ ", ModuleRegistry.All.Select(m => m.Key + "=" +
                        (uiForm.View(m.Key).Gateway?.WakeFromHomeNote is null ? "不说" : "说"))));
                var cfgOther = uiForm.ModelsForTest.FirstOrDefault(m => m.Tag != keepTag);
                if (cfgOther is not null)
                {
                    uiForm.SelectModelForTest(cfgOther.Tag);
                    var cfgB = uiForm.ActiveConfigTarget();
                    // DEC-122②：文件不再跟着下拉换（一份 ini 装所有模型的记录），换的是**说明里点名那一段**
                    Check("打开配置不随下拉换文件，但说明里那一段跟着换（DEC-122②：一份 ini、每个模型各看各的那一段）",
                        string.Equals(cfgB.Path, cfgA.Path, StringComparison.OrdinalIgnoreCase)
                        && cfgB.Note.Contains("[Model:" + cfgOther.Tag + "]")
                        && !cfgB.Note.Contains("[Model:" + keepTag + "]"), $"{cfgA.Path} -> {cfgB.Path}");
                    // 用户 2026-10-04 第 2 条（两侧一起，他补的"要一起删的"）：换模型不再往日志里抄一遍参数
                    // ——「已按模型 X 的 strata-<tag>.json / llama_models.ini [X] 段载入参数（…）」两行都退役，
                    // 落点就是卡2 / 高级区那些格子本身的值在变。这条断言防止以后有人把同义反复加回来。
                    Check("换模型不写「已按模型…载入参数」那行（两侧都退役：卡2/高级区才是落点）",
                        !uiForm.LogBoxTextForTest.Contains("载入参数"),
                        uiForm.LogBoxTextForTest.Split('\n').FirstOrDefault(l => l.Contains("载入参数")) ?? "日志区无此行");
                    uiForm.SelectModelForTest(keepTag);
                }
                uiForm.SwitchModuleForTest("llama");
                var cfgL = uiForm.ActiveConfigTarget();
                // DEC-079（取代 DEC-077 的 llama 那一支）：取消融合层之后**没有产物**了 —— 引擎读的就是
                // exe 同目录那份 llama_models.ini，界面上「保存」写的也是它，所以「打开配置」直接开它，
                // 也不再"打开前先重物化"（没什么可物化的）。
                Check("打开配置(llama) = 家底本身（exe 同目录的 llama_models.ini，不再有 merged.ini）",
                    string.Equals(cfgL.Path, MainForm.RouterIniPath(), StringComparison.OrdinalIgnoreCase)
                    && !cfgL.Path.Contains("merged"), $"{cfgL.Path}｜{cfgL.Note}");
                // DEC-092（用户第 1 条）：这句说明整条退役 —— 打开的就是引擎读的那一份本身，卡2/高级区就是落点，
                // 备份与恢复入口写在设置页；日志里再抄一遍「保存写哪一段」只是同义反复（先例：DEC-082 的「载入参数」两行）。
                Check("打开配置(llama) 不再往日志里补一句「这就是引擎读的那一份」（DEC-092 起 note 为空）",
                    cfgL.Note.Length == 0, cfgL.Note);
                // DEC-122②：ModuleView.Materialize 那一族（打开前先重物化 / 存在性检查）整套退役 ——
                // llama 与 Ninfer 那两支本来就只剩"文件在不在"，而 ConfigTarget 认不出文件时已经会回一句说明，
                // 同一件事不许长两家。这里按模块表穷举一遍：每一支要么给出一个**在场**的路径，要么给一句说明。
                var cfgBad = ModuleRegistry.All.Where(m =>
                {
                    var t = uiForm.View(m.Key).ConfigTarget();
                    return t.Path.Length > 0 ? !File.Exists(t.Path) : t.Note.Length == 0;
                }).Select(m => m.Key).ToList();
                Check("DEC-122②「打开配置」的判据只有 ConfigTarget 一家：三模块每一支要么路径在场、要么有话可说（按表穷举）",
                    cfgBad.Count == 0, "不对的：" + string.Join("、", cfgBad));
                // 用户 2026-10-04 第 2 条（他补的那句"要一起删的"）：llama 侧换模型同样不再往日志里抄一遍参数
                // ——「已按模型 X 的 llama_models.ini [X] 段载入参数（…）；继承全局的键：…」整条退役。
                // 挑一个**别的**段来切（选同一段不触发 SelectedIndexChanged = 白测）。
                var backTag = uiForm.SelectedTagForTest;
                var llamaOther = uiForm.LlamaModelsForTest.FirstOrDefault(m => m.Tag != backTag);
                Check("自检环境里至少有第二段 llama（下面那条换模型的断言才不会空过）",
                    llamaOther is not null, $"llama 段数={uiForm.LlamaModelsForTest.Count} 当前={backTag}");
                if (llamaOther is not null)
                {
                    uiForm.SelectModelForTest(llamaOther.Tag);
                    Check("换 llama 模型也不写「已按模型…载入参数」（与 Strata 侧同一条口径）",
                        !uiForm.LogBoxTextForTest.Contains("载入参数"),
                        uiForm.LogBoxTextForTest.Split('\n').FirstOrDefault(l => l.Contains("载入参数")) ?? "日志区无此行");
                    uiForm.SelectModelForTest(backTag);      // 还原：下面那批用例按这一段立基线
                }
                // 基线立对了没：刚推完视图时，界面值与"该段的生效值"应当逐键相等 → 一条都不该写
                var effNow = MainForm.LlamaEffectiveWriteForm(MainForm.RouterIniPath(), uiForm.SelectedTagForTest);
                var pending0 = MainForm.LlamaSectionEdits(uiForm.CollectedForTest(), effNow);
                Check("刚载入一段：一条都不用写（「继承不摊平」在界面上成立）",
                    pending0.Count == 0, string.Join(",", pending0.Select(e => e.Key + "=" + e.Value)));
                // 换一档 KV（挑一档与这一段当前生效值不同的，免得撞上"本来就一样 = 不该写"那条判据）
                var kvNow = MainForm.KvDisplay(effNow["cache-type-k"], effNow["cache-type-v"]);
                var kvTo = kvNow == "q4_0 / q4_0" ? "q8_0 / q8_0" : "q4_0 / q4_0";
                var (wantK, wantV) = MainForm.SplitKv(kvTo);
                uiForm.SetKvLlamaForTest(kvTo);
                var pending1 = MainForm.LlamaSectionEdits(uiForm.CollectedForTest(), effNow);
                int wantCnt = (effNow["cache-type-k"] == wantK ? 0 : 1) + (effNow["cache-type-v"] == wantV ? 0 : 1);
                Check("只把 KV 换档：只写真的变了的那一两键（同写两键、别的键一条都不多写）",
                    pending1.Count == wantCnt && wantCnt > 0 && pending1.All(e =>
                        e.Key == "cache-type-k" ? e.Value == wantK : e.Key == "cache-type-v" && e.Value == wantV),
                    $"{kvNow} -> {kvTo}｜" + string.Join(",", pending1.Select(e => e.Key + "=" + e.Value)));
                uiForm.RestoreViewForTest();       // 还原视图（别把这份改动留在基线里影响后面的脏判定）
                var pending2 = MainForm.LlamaSectionEdits(uiForm.CollectedForTest(), effNow);
                Check("还原之后又是一条都不用写（视图重推不会被旧 [Current] 值盖回去）",
                    pending2.Count == 0, string.Join(",", pending2.Select(e => e.Key + "=" + e.Value)));
                Check("llama 页：上下文这格已经可编辑（DEC-079，写回那一段的 ctx-size）",
                    uiForm.LlamaCtxEditableForTest);
                Check("llama 页：KV 那一格是 6 档下拉（空白在首）",
                    uiForm.LlamaKvItemCountForTest == MainForm.LlamaKvItemsForTest().Count
                    && MainForm.LlamaKvItemsForTest()[0].Text == "", uiForm.LlamaKvItemCountForTest + " 项");
                Check("llama 页：flash-attn 是两态开关、住在高级区（dump 里标成「开关」，不是下拉/文本框）",
                    Row("adv-llama").Contains("flash-attn | 开关"), Row("adv-llama"));
                // 「继承要看得见」：这一段没写、`[*]` 写了的键，tooltip 里必须点明（用户批复 §10.2 第 2 条）
                if (uiForm.LlamaInheritedForTest("reasoning-effort"))
                    Check("继承标注：从全局拿来的值在 tooltip 里说明（reasoning-effort）",
                        uiForm.LlamaInheritanceTipForTest("reasoning-effort").Contains("全局"),
                        uiForm.LlamaInheritanceTipForTest("reasoning-effort"));
                else Check("DEC-138（E-7）用例前提：没继承样本的原因确实成立 —— 界面上选中那一段自己写了 reasoning-effort" +
                       "（若段里没写、又判不出继承，那就是标注逻辑坏了，不许当「没样本」混过去）",
                       uiForm.LlamaEffectiveHasForTest("reasoning-effort"),
                       $"继承集={uiForm.LlamaInheritedListForTest} 生效值有没有={(uiForm.LlamaEffectiveHasForTest("reasoning-effort") ? "段里写了" : "两处都没写")}");
                uiForm.SwitchModuleForTest("strata");
                // 打开器口径（DEC-037）：系统绑过默认程序就顺它，没绑定就落到记事本（不让 Win11 弹"你要用什么打开"）
                Check("已绑定的类型（.txt）交给系统默认程序",
                    MainForm.ConfigOpener("a.txt").Length == 0, "opener=[" + MainForm.ConfigOpener("a.txt") + "]");
                Check("没绑定的扩展名不弹系统选择框，直接用记事本",
                    MainForm.ConfigOpener("a.definitely-not-registered-ext").Equals("notepad.exe", StringComparison.OrdinalIgnoreCase),
                    "opener=[" + MainForm.ConfigOpener("a.definitely-not-registered-ext") + "]");
                uiForm.SwitchModuleForTest("strata");
                // 高级区圆角输入框的 Backdrop 必须是卡片色（默认窗口色会露"黑角"，用户反馈 2026-10-01）
                var advBg = uiForm.AdvancedInputBackdropsForTest();
                Check("高级区圆角输入框用卡片色铺底（不露黑角）",
                    advBg.Count >= 15 && advBg.All(c => c == UiTheme.Card), "count=" + advBg.Count);
                // 同一条纪律的另一半（DEC-182）：卡1 每一格垫的色必须等于它父层现值。
                // 原来 `CardStart()` 那个循环只设 BackColor（对自绘控件无效）→ ninfer 的端口/思考深度
                // 圆角外垫了一圈方角（他圈图点名）；方块搬进行容器后又会反向垫一圈。
                var hous = uiForm.Card1HousingForTest();
                var badHous = hous.Where(h => h.Got.ToArgb() != h.Want.ToArgb())
                                  .Select(h => $"{h.What}:{h.Got.Name}≠{h.Want.Name}").ToArray();
                Check("DEC-182 卡1 每一格（含行容器里的方块）垫的色 = 父层现值（错一格就报出是谁）",
                    hous.Length >= 9 && badHous.Length == 0,
                    badHous.Length == 0 ? "全对（" + hous.Length + " 格）" : string.Join("｜", badHous));

                // 设置窗（B 段，2026-10-02 反馈修订）：通用 / 每个启用模块各一节 / 关于（不再跟随主区激活页）
                sf = new SettingsForm(uiForm, new Settings());
                sf.CreateControl();
                srows = sf.DumpLayoutRows();
                foreach (var line in srows) W(line);
                Check("设置窗 = 通用｜每模块一节｜关于（DEC-096 起按模块表穷举：三节都在）",
                    SRow("settings-tabs") == "settings-tabs: 通用 | " +
                        string.Join(" | ", ModuleRegistry.All.Select(m => m.DisplayName)) + " | 关于", SRow("settings-tabs"));
                Check("设置窗页签胶囊高度 = 按钮高度（2026-10-02 反馈）",
                    sf.TabsPillHeightForTest == UiTheme.S(uiForm, 30), sf.TabsPillHeightForTest.ToString());
                Check("通用节行顺序正确（模块区 + 每模块自动启动；DEC-110② 那句「至少启用一个…」常驻说明已删；"
                    + "DEC-157 起「显存」那一组也在这一节 —— 三模块共用）",
                    InOrder(SRow("settings-通用"), "模块", "Strata", "llama.cpp",
                        "启动行为", "随系统启动", "启动时隐藏到托盘", "自动启动服务 · Strata", "自动启动服务 · llama.cpp", "就绪后打开网页",
                        "显存", "显存互斥处理", "排队等显存上限(秒)",
                        "关闭行为", "关闭到托盘", "退出时停止服务", "日志", "写入日志文件", "调试信息过滤")
                    && !SRow("settings-通用").Contains("至少启用一个；关闭前须先停止服务"), SRow("settings-通用"));
                // DEC-157（他批的第 2 条）：那一格是**共同的设置**，挪进「通用」，Ninfer 一节里不许留第二处
                Check("DEC-157 显存互斥处理那一格只在「通用」，不在 Ninfer 一节（同一件事不留第二个家）",
                    SRow("settings-通用").Contains("显存互斥处理")
                    && !SRow("settings-Ninfer").Contains("显存互斥处理"), SRow("settings-Ninfer"));
                // DEC-159（他问"这是什么唤醒时间?需要界面吗?"）：那一枚数上了界面，且紧跟在档位后面
                Check("DEC-159「排队等显存上限(秒)」上了设置页「通用 → 显存」，紧跟「显存互斥处理」后面（管三族的数不该只住在 ini 里）",
                    SRow("settings-通用").Contains("排队等显存上限(秒)")
                    && !SRow("settings-Ninfer").Contains("排队等显存上限(秒)"), SRow("settings-通用"));
                // DEC-163（他拍 B 路径）：那枚「引擎调试日志（写全量）」整套退役 —— 通用页不该再有它的名字，
                // 过滤那句说明要写清"只挡界面、照记"（旧句只提"隐藏"，会让人以为文件也不记）。
                Check("DEC-163 「写全量」那一行已退役（通用页不再出现），过滤说明写明「只挡界面·照记」",
                    !SRow("settings-通用").Contains("写全量")
                    && SRow("settings-通用").Contains("只挡界面"), SRow("settings-通用"));
                Check("Strata 节行顺序正确（DEC-110④：维护组 = 更新源 → 版本 → 回退 → 各自独有）",
                    InOrder(SRow("settings-Strata"), "路径", "Strata 目录", "模型配置目录", "运行", "内部端口", "空闲停服(分钟)",
                        "维护", "更新源", "版本", "更新…", "回退到备份", "回退…", "内建 llama.cpp", "运行 SETUP")
                    && !SRow("settings-Strata").Contains("检查更新")
                    && !SRow("settings-Strata").Contains("只查 GitHub"), SRow("settings-Strata"));
                var wa = new Rectangle(0, 0, 1920, 1040);
                Check("⑤ 更新窗摆位：落在父窗正中（CenterParent 对非模态窗不管用，只能自己算）",
                    MainForm.CenterOnOwner(new Rectangle(100, 60, 900, 700), new Size(400, 300), wa) == new Point(350, 260),
                    MainForm.CenterOnOwner(new Rectangle(100, 60, 900, 700), new Size(400, 300), wa).ToString());
                Check("⑤ 父窗贴着屏幕右下角时，窗被夹回工作区，不跑出可见范围",
                    MainForm.CenterOnOwner(new Rectangle(1600, 700, 400, 400), new Size(400, 300), wa) == new Point(1520, 740),
                    MainForm.CenterOnOwner(new Rectangle(1600, 700, 400, 400), new Size(400, 300), wa).ToString());
                Check("⑤ 设置页底部保存栏与内容区同底色（不再单独一块 Card，DEC-059）",
                    sf.FooterColorForTest == UiTheme.Window, sf.FooterColorForTest.ToString());
                Check("llama 节同样只留「版本 + 更新…」（DEC-058）",
                    InOrder(SRow("settings-llama.cpp"), "维护", "版本", "更新…")
                    && !SRow("settings-llama.cpp").Contains("检查更新"), SRow("settings-llama.cpp"));
                // ---------------- DEC-062：路径行右侧的按钮语义（可改的走系统选择器，只读位置仍直达） ----------------
                Check("DEC-062 Strata 目录 =「选择」（点了弹文件夹选择器；按钮文字 2026-10-04 第 5 条由「选目录」缩短），只读的模型配置目录仍「打开」直达",
                    SRow("settings-Strata").Contains("选择+Strata 目录")
                    && SRow("settings-Strata").Contains("打开+模型配置目录"), SRow("settings-Strata"));
                // DEC-079：路径固定（= exe 同目录）且就地读写，所以「模型配置」这行不再是"可改的路径"，
                // 退回"只读位置 + 一枚「打开」直达"（关于页那三行的口径，DEC-055/062）；「选文件」在这里退役。
                Check("DEC-079 llama 节：llama.cpp 目录 / 模型目录 =「选择」，模型配置 = 只读位置 +「打开」（不再有「选文件」）",
                    SRow("settings-llama.cpp").Contains("选择+llama.cpp 目录")
                    && SRow("settings-llama.cpp").Contains("选择+模型目录")
                    && !SRow("settings-llama.cpp").Contains("选文件")
                    && SRow("settings-llama.cpp").Split('|').Any(seg => seg.Trim().StartsWith("模型配置+") && seg.Contains("+打开")),
                    SRow("settings-llama.cpp"));
                Check("DEC-062 关于页三处位置仍是「打开」直达（那里没有可改的目录，换成选择器就丢了直达）",
                    SRow("settings-关于").Contains("打开") && !SRow("settings-关于").Contains("选择")
                    && !SRow("settings-关于").Contains("选文件"), SRow("settings-关于"));
                // DEC-063：弹窗族（更新窗 + Dialogs.*）与设置窗同一档 —— 不上自定义标题色/边框色。
                // DWM 的色要窗口真显示才读得回（自检不开窗），所以断言打在口径位上；出图看 shots-m1/tb2/。
                // DEC-092（恢复 DEC-059 那一半、撤销 DEC-063 的整档退档）：弹窗族标题栏与内容区同底色，
                // 但边框仍归系统画 —— DWM 的色要窗口真显示才读得回（自检不开窗），所以断言打在这两个口径位上；出图看 shots-m1/tb2/。
                Check("DEC-092 弹窗族标题栏与内容同底色（DEC-063 的整档退档已撤销）",
                    UiDark.DialogsUseCustomCaptionColors, "DialogsUseCustomCaptionColors=" + UiDark.DialogsUseCustomCaptionColors);
                Check("DEC-092 同色的同时保留系统画的窗口边框（不设 BORDER_COLOR = 边界仍看得见）",
                    UiDark.DialogsKeepSystemBorder, "DialogsKeepSystemBorder=" + UiDark.DialogsKeepSystemBorder);
                // DEC-093（用户：上一轮改标题栏颜色时漏了设置页）：设置窗必须与弹窗族同一档，
                // 判据 = 它确实经过了 ApplyDialog（口径只有那一处；自写 titleColors 会读不回来）。
                Check("DEC-093 设置窗与弹窗族同一档标题栏（走 ApplyDialog，不再自写 titleColors=false）",
                    UiDark.IsDialogStyled(sf), "IsDialogStyled(settings)=" + UiDark.IsDialogStyled(sf));
                // 主窗是另一档（Apply 默认：标题栏与边框都设成窗体色 = 全无缝），别把它也塞进弹窗族那一档。
                Check("DEC-093 主窗仍走自己那一档（不是 ApplyDialog = 弹窗族与主窗两档不混）",
                    !UiDark.IsDialogStyled(uiForm), "IsDialogStyled(main)=" + UiDark.IsDialogStyled(uiForm));
                // ---------------- DEC-058：更新窗（一模块一窗；三行各一枚按钮，摆位固定只换字） ----------------
                uf = new UpdateForm(uiForm, "strata");
                uf.CreateControl();
                Check("⑤ 更新窗位置跟其他窗同一口径：默认 CenterParent（父窗内部居中）",
                    uf.StartPositionForTest == FormStartPosition.CenterParent, uf.StartPositionForTest.ToString());
                string U() => uf.LayoutTextForTest();
                Check("① 没查过：只有前两行，第二枚是「检查更新」，不出现空着的第三行",
                    U().StartsWith("[-][检查更新][-]") && U().Contains("bar=0") && U().Contains("x=0"), U());
                uiForm.SetUpdateOfferForTest("v0.1.36", "2026-10-03T09:05:28Z", skipped: false);
                Check("① 探到新版：三行各一枚 = 跳过 / 查看更新内容 / 更新，状态写版本与发布日",
                    U().StartsWith("[跳过][查看更新内容][更新]") && U().Contains("发现新版本 v0.1.36")
                    && U().Contains("2026-10-03"), U());
                uiForm.SetUpdateOfferForTest("v0.1.36", "2026-10-03T09:05:28Z", skipped: true);
                Check("① 被跳过那一版：只把第一枚换成「取消跳过」，摆位不动",
                    U().StartsWith("[取消跳过][查看更新内容][更新]"), U());
                uiForm.SetUpdateBusyForTest(true);
                Check("①④ 升级中：三枚原地灰置（不是消失、也不是还能点）", !uf.ButtonsEnabledForTest(), U());
                uiForm.SetUpdateProgressForTest("下载源码包", 0.42);
                Check("⑤ 进度条与阶段名只走第三行，带百分比",
                    U().Contains("bar=1") && U().Contains("下载源码包 42%"), U());
                Check("⑥ 可停阶段：进度条右侧那枚红色小 X 出现（DEC-056 定稿②圆底）",
                    U().Contains("x=1"), U());
                uiForm.SetUpdateProgressForTest("同步官方预编译引擎", -1);
                Check("⑤ 不确定态：只报阶段名，不硬凑百分比",
                    U().Contains("bar=1") && U().Contains("同步官方预编译引擎") && !U().Contains("%"), U());
                Check("⑥ 覆盖/同步引擎阶段不给停：小 X 收起", U().Contains("x=0"), U());
                Check("⑥ 托盘那条忙时改读「升级中…」并置灰（不再是一个点了没反应的入口）",
                    uiForm.UpdateMenuLabel("strata") == "升级中…" && !uiForm.UpdateMenuEnabled("strata"),
                    uiForm.UpdateMenuLabel("strata"));
                uiForm.SetUpdateBusyForTest(false);
                Check("⑤ 不忙了：进度条与小 X 收起、三枚恢复可点",
                    U().Contains("bar=0") && U().Contains("x=0") && uf.ButtonsEnabledForTest(), U());
                uiForm.ClearUpdateStateForTest();
                Check("① 跳过或升完之后回到两行", U().StartsWith("[-][检查更新][-]"), U());
                // llama 那一窗：同一套摆位；版本行**不带目录与 CUDA 后缀**（用户：路径始终不该显示）
                lf = new UpdateForm(uiForm, "llama");
                lf.CreateControl();
                Check("llama 版本行只报版本号，没有盘符路径与 bin-win-cuda 后缀",
                    !lf.VersionTextForTest().Contains(":") && !lf.VersionTextForTest().Contains("bin-win")
                    && !lf.VersionTextForTest().Contains("cuda"), lf.VersionTextForTest());
                uiForm.SetLlamaOfferForTest("b11368", "2026-10-03T02:35:41Z", skipped: false);
                Check("llama 探到新版：与 Strata 同一套三枚摆位，且两窗互不牵连",
                    lf.LayoutTextForTest().StartsWith("[跳过][查看更新内容][更新]")
                    && lf.LayoutTextForTest().Contains("b11368")
                    && U().StartsWith("[-][检查更新][-]"), lf.LayoutTextForTest() + " || " + U());
                uiForm.SetLlamaOfferForTest("b11368", "2026-10-03T02:35:41Z", skipped: true);
                Check("llama 被跳过态：第一枚读「取消跳过」",
                    lf.LayoutTextForTest().StartsWith("[取消跳过]"), lf.LayoutTextForTest());
                uiForm.ClearLlamaOfferForTest();
                Check("llama 那窗收回后回到两行", lf.LayoutTextForTest().StartsWith("[-][检查更新][-]"), lf.LayoutTextForTest());

    }
}
