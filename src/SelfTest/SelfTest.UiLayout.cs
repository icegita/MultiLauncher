using System.Text;
namespace MultiLauncher;

// 用例 5（版本比较）+ 用例 5a（界面排布，只建控件树不显示窗口）：窗口标题/版本号、卡片行序、
// 高级区/日志区/托盘/弹窗族、切页联动那一大族。**整个 5a 是一个 try{ using var uiForm } 单作用域**，
// 2026-10-15 整块搬出（块内一行未改；locals 随块走）。文件仍大 —— 下一步按 sub 横幅再切。
internal static partial class SelfTest
{
    internal static void UiLayoutChecks()
    {
            // --- BUG-086（2026-10-23，他截图报「关于页挂着一条没用的滑条」）：滑条出面只认真几何 ---
            // 旧判据读原生 VScrollBar 的 Maximum/LargeChange，那两个值在"页面构造期被隐藏/尺寸未定"时是陈旧的
            // （关于页实测停在 max=478 large=100 → 内容 480 比可视 812 还短，却判成可滚、画出一条两成高的拇指）。
            Check("BUG-086 滑条只按真几何出面：内容不足一屏不出、多一像素才出（不读原生 VScroll 的陈旧值）",
                !UiScrollbar.BarNeeded(480, 812) && !UiScrollbar.BarNeeded(812, 812)
                && UiScrollbar.BarNeeded(813, 812) && UiScrollbar.BarNeeded(2454, 812)
                && UiScrollbar.BarNeeded(10, 0),
                $"480/812={UiScrollbar.BarNeeded(480, 812)} 812/812={UiScrollbar.BarNeeded(812, 812)} "
                + $"813/812={UiScrollbar.BarNeeded(813, 812)} 10/0={UiScrollbar.BarNeeded(10, 0)}");
            // 挂载点必须按几何同步（源码守则）：只喂样本不够，得钉住"真路径用的是 DisplayRectangle 而不是 VScroll"
            int geomHits = CodeHitsBetween("UiScrollbar.cs", "public static UiScrollbar AttachTo", "bar.SyncNow = Sync", "DisplayRectangle.Height");
            int vsHits = CodeHitsBetween("UiScrollbar.cs", "public static UiScrollbar AttachTo", "bar.SyncNow = Sync", "host.VerticalScroll");
            Check("BUG-086 挂载点按真几何同步（那一段里不许再出现 host.VerticalScroll）",
                geomHits is 1 or -1 && vsHits is 0 or -1, $"DisplayRectangle={geomHits} VerticalScroll={vsHits}");

            // --- 用例 5：版本比较 ---
            Check("0.1.29 > 0.1.28", MainForm.CompareVersion("0.1.29", "0.1.28") > 0);
            Check("0.1.9 < 0.1.10", MainForm.CompareVersion("0.1.9", "0.1.10") < 0);
            Check("0.2 == 0.2.0", MainForm.CompareVersion("0.2", "0.2.0") == 0);
            Check("相同版本按 0 判定", MainForm.CompareVersion(localVersion, localVersion) == 0);

            // --- 用例 5a：界面排布（只建控件树，不显示窗口） ---
            try
            {
                uiForm = new MainForm();
                uiForm.CreateControl();
                try   // 原 using 语义：离开这块就释放（异常路径也释放）
                {
                // 环境里可能已有 MultiLauncher.ini（旧名会自动改名过来；模块开关 / 上次激活页 / SaveLog 都不定）：先拉回两个模块、
                // 切到 Strata 页，让「按 Strata 断」的用例有确定状态（都不落盘）。
                // DEC-111②：这里原来还有一句 uiForm.SetSaveLogForTest(false) —— 文件日志那道闸已按进程收口
                //（本文件开头 MainForm.LogFileDisabledForTest = true），实例版没必要了。
                uiForm.SetModuleEnabledForTest(strata: true, llama: true);
                uiForm.SwitchModuleForTest("strata");
                W($"确定性状态: strata={uiForm.ModelsForTest.Count} llama={uiForm.LlamaModelsForTest.Count} active={uiForm.ActiveModuleForTest} RouterIni={uiForm.RouterIniForTest}");
                // 更名落地（2026-10-02，DEC-030）：显示名 = MultiLauncher 混合启动器、版本读 csproj（原 MixLauncher 作废）
                Check("窗口标题 = MultiLauncher 混合启动器 + 版本（不再有 Strata&llama）",
                    uiForm.Text.StartsWith("MultiLauncher 混合启动器  v", StringComparison.Ordinal)
                    && !uiForm.Text.Contains("Strata&llama"), uiForm.Text);
                // 版本：DEC-162 那批按他拍升到 1.7.3；DEC-163（B 路径）再升末位到 1.7.4；
                // **DEC-164（开机自启的 Run 键按进程收口）同样是修复性收口** → 按 DEC-110 再升末位。
                // DEC-171（第 46 轮：灯与「端口」行的端口口径 + 托盘悬停色回色表）同为修复性批次 → 再升末位。
                // DEC-174（空闲停服的计时锚点）他点名升一档 → 1.8.0；DEC-176 → 1.8.1。
                // 显示口径（DEC-180，2026-10-16 他点名"整体版本号后移一位 → v0.1.8.1"）：**csproj 写几段就显几段**，
                // 取代 DEC-175 的「总是三段」—— 那条按 Major.Minor.Build 拼，会把四段号的末段静默吞掉。
                // 下面这条字面断言就是那道闸：谁把 `VerNumber()` 改回三段拼法，这里立刻红成 "v0.1.8"。
                // DEC-181（动效三条）+ DEC-182（卡1 与后端方块左右相邻）= 界面形状没动 → 按 DEC-110 只升末位。
                // 档位改判记账：我先按"成批新功能"发成 0.1.9.0，他判这批算补丁位 → 同批改判重发（DEC-182 那条注释里也记了）。
                // DEC-202/203（2026-10-21）：本程序自更新 = 一整条新链（第 4 条更新链），他点名「升小版本号」并在同一条消息里
                // 纠正为「0.1.8.4」—— 本项目的"小版本号"= 末位（与 DEC-182 那次改判同一口径）。
                // DEC-219/220（2026-10-25）：花括号手加键写法 + 手加键日志出声 + ini 键值行「Key = Value」，
                // 全是读侧与外观收口、没有新功能 → 按 DEC-110 只升末位（0.1.8.6 → 0.1.8.7）。
                Check("版本号 = v0.1.8.7（csproj 0.1.8.7；花括号手加键与 ini 键值行格式那批，按末位）",
                    MainForm.VersionTag() == "v0.1.8.7", MainForm.VersionTag());
                Check("版本显示不吃进提交号与空白（InformationalVersion 形如 0.1.8.3+<sha>，'+' 之后必须切干净）",
                    !MainForm.VersionTag().Contains('+') && !MainForm.VersionTag().Contains(' '), MainForm.VersionTag());
                // DEC-180 的机器版护栏：csproj 里**只许有一个** `<Version>` 真标签。这个坑本仓库栽过两次 ——
                // 上次是历史注释把 `<Version>` 写在注释块外面 → MSBuild 后覆盖前 → 产物号悄悄回到旧值；
                // 这次是改号时新标签插在旧标签前面、旧的那行没删 → 上面那条断言当场红成 "v1.8.1"（正是它抓出来的）。
                // 实跑目录没有源码可读 → 扫到 0 个时这条不判（口径同"判不了就当没这条校验"）。
                var srcRoot = SourceDir();
                var csprojPath = srcRoot is null ? "" : Path.Combine(srcRoot, "MultiLauncher.csproj");
                int verTags = File.Exists(csprojPath)
                    ? File.ReadAllLines(csprojPath).Count(l => l.TrimStart().StartsWith("<Version>", StringComparison.Ordinal))
                    : 0;
                Check("csproj 只留一个 <Version> 真标签（多一个 = 后者覆盖前者、产物号悄悄回旧值；没源码可扫则不判）",
                    verTags is 0 or 1, $"扫到 {verTags} 个（0 = 实跑目录没源码，这条不判）");
                // 再显式选中一个 Strata 模型；随后再测「切到 llama 页」的联动。
                strataForTest = uiForm.ModelsForTest.FirstOrDefault(m => m.Ready) ?? uiForm.ModelsForTest.FirstOrDefault();
                if (strataForTest is not null) uiForm.SelectModelForTest(strataForTest.Tag);
                rows = uiForm.DumpLayoutRows();
                foreach (var line in rows) W(line);

                // 顺序断言：只要求这些词按序出现（容忍中间多出无关文字，但顺序错了就会挂）

                // B 段（DEC-025）：左栏 = 纯上下文（启停 + 状态键值行 + 模式条 + API），底部「模块状态」
                Check("左栏动作按钮只剩「启动」（打开网页 / 更新已随 B 段挪走）",
                    Row("left-nav") == "left-nav: 启动", Row("left-nav"));
                // 2026-10-15：strata 的「源码」「引擎」两行合并成一行「版本」→ 三页行集全等（都这五行）
                Check("运行状态卡键顺序 = 状态｜PID｜地址｜端口｜版本（三页同一套行）",
                    InOrder(Row("left-kv"), "状态", "PID", "地址", "端口", "版本"), Row("left-kv"));
                // 2026-10-04：原「运行状态」标题行（右带状态词）与下方「状态」行是同一串字——去掉标题行，
                // 文案与状态色由「状态」行承接（字体 8.5pt，不再用等宽 9pt）
                Check("左栏不再有「运行状态」大行（与「状态」行重复，已合并）",
                    !Row("left-col").Contains("运行状态"), Row("left-col"));
                {
                    var (stText, stColor) = uiForm.ActiveStatusTextForTest;
                    var (cellText, cellColor, cellFont) = uiForm.RunStateCellForTest;
                    Check("合并后：「状态」行值 = 原大状态行的文案与配色（同一事实源）且字体 8.5pt",
                        cellText == stText && cellColor.ToArgb() == stColor.ToArgb() && Math.Abs(cellFont - 8.5f) < 0.01f,
                        $"{cellText} / {cellColor} / {cellFont}");
                }
                Check("页头 = 模块分段（Strata｜llama.cpp）+ 打开配置 / 设置（保存已回卡1，DEC-076 候选）",
                    InOrder(Row("pagehead"), "Strata", "llama.cpp", "打开配置", "设置"), Row("pagehead"));
                // 2026-10-02 反馈：胶囊 tab 高度太矮（主窗分段是字段初始化，构造时 DPI 未捕获，150% 下只有 32px）。
                // 口径：胶囊净高 = 按钮高（RowH）；控件高 = 按钮高 + 胶囊内缩（≥按钮高）。
                var (segH, pillH, btnH) = uiForm.SegmentMetricsForTest;
                Check("页头分段胶囊高度 = 按钮高度（不矮于按钮）",
                    pillH == btnH && segH >= btnH, $"seg={segH} pill={pillH} btn={btnH}");
                Check("底部「模块状态」= 启用中的模块各一行（按模块表穷举，DEC-096 起三行）",
                    Row("lights") == "lights: " + string.Join(" | ", ModuleRegistry.All.Select(m => m.Key)), Row("lights"));
                // 2026-10-04 DEC-076 候选：卡1 回归——监听地址｜监听端口｜思考深度（替换掉的预设槽位）｜保存。
                // 「保存」保留置灰逻辑；「删除」按用户口径去掉了。
                Check("卡片1 四项 = 监听地址｜监听端口｜思考深度｜保存（DEC-076 候选）",
                    InOrder(Row("card1"), "监听地址", "监听端口", "思考深度", "保存")
                    && !Row("card1").Contains("预设") && !Row("card1").Contains("删除"), Row("card1"));
                // 2026-10-02 反馈（BUG-027）：重扫前必须先把界面编辑收进 _config.Current，否则推视图
                // （PushCard2ForActiveModule）会用旧值把用户刚改的格子盖回去。
                // 2026-10-04（DEC-074）：顺路把「保存」按钮的脏状态走一遍——灰 → 手改亮 → 重扫仍亮 → 收口回灰
                Check("「保存」按钮：无改动时置灰（启动/换模型后都是这一态）", !uiForm.SaveButtonEnabledForTest, "enabled=" + uiForm.SaveButtonEnabledForTest);
                var ctxKeep = uiForm.ContextBoxForTest;
                uiForm.SetContextForTest("199999");
                Check("「保存」按钮：手改一格即亮（只认手动改动）", uiForm.SaveButtonEnabledForTest, "enabled=" + uiForm.SaveButtonEnabledForTest);
                uiForm.RefreshModelsForTest();
                Check("重扫（SETUP 结束/升级/换目录会触发）不把上方参数盖回旧值（BUG-027）",
                    uiForm.ContextBoxForTest == "199999", uiForm.ContextBoxForTest);
                Check("「保存」按钮：重扫只把编辑收进内存、不算保存——仍亮着", uiForm.SaveButtonEnabledForTest, "enabled=" + uiForm.SaveButtonEnabledForTest);
                uiForm.MarkParamsSavedForTest();
                Check("「保存」按钮：收口后回灰（保存/切页/关窗/设置页保存都汇到 MarkParamsSaved）",
                    !uiForm.SaveButtonEnabledForTest, "enabled=" + uiForm.SaveButtonEnabledForTest);   // DEC-092：能进这里的收口点只剩「保存」/关窗「写进去」/换模型/启动
                uiForm.SetContextForTest(ctxKeep);
                uiForm.MarkParamsSavedForTest();      // 还原 + 再收口，别污染后面的用例
                uiForm.RefreshModelsForTest();
                // ---------------- DEC-092 第 2 条：没点「保存」的改动不许被别的路径偷偷落盘 ----------------
                // 用户实测：改了参数没点保存，切页回来还在、重启也还在 = 其实已经被存住了。
                // 根因是切页 / 关窗 / 设置页保存 / 模式条都顺手 CollectSettings→SaveConfig。
                // 这几条断言打在**文件**上（不是内存），所以要把 ConfigPath 指到 data\tmp\ 的副本上跑
                //（口径同 ini 保真用例：自检只在副本上写，最后核对开发目录那份真件逐字节没动）。
                var iniReal = MainForm.ConfigPathForTest;
                var iniTmp = MainForm.Scratch("selftest-save-gate.ini");
                if (File.Exists(iniReal)) File.Copy(iniReal, iniTmp, overwrite: true);
                MainForm.UseTempConfigForTest(iniTmp);
                uiForm.SaveParameterEditsForTest();       // 先把当前状态整份落进副本，下面的"旧值"才有得读
                // DEC-122③：Strata 那 16 格模型家底的**重复副本**已从 [Current] 退役，家 = [Model:<tag>] 那一段。
                // 所以这一族"保存才落盘"的判据必须跟着打在真正的落点上 —— 盯一个不会再出现的行就是空判据（BUG-043 那一族）。
                var snapSec = "Model:" + uiForm.SelectedTagForTest;
                var ctxOld = uiForm.ValueInIniForTest("Context", snapSec);
                var ctxLive = uiForm.ContextBoxForTest;
                var ctxNew = ctxOld == "147456" ? "131072" : "147456";      // 必须与磁盘那份不一样，否则整套断言空跑
                Check("DEC-122③ 用例前提：选中模型在 [Model:<tag>] 段里有 Context 这一行（不然下面全是空判据）",
                    ctxOld != "<没有这一行>" && ctxOld.Length > 0 && ctxOld != ctxNew, $"段=[{snapSec}] 文件里={ctxOld} 界面上={ctxLive}");
                uiForm.SetContextForTest(ctxNew);
                int writes0 = uiForm.DiskWritesForTest;
                uiForm.SwitchModuleRealForTest("llama");
                uiForm.SwitchModuleRealForTest("strata");
                Check("DEC-092 切页一次都不写文件（真路径 SwitchModule；原来每次切页都顺手 SaveConfig）",
                    uiForm.DiskWritesForTest == writes0 && uiForm.ValueInIniForTest("Context", snapSec) == ctxOld,
                    $"写盘 {writes0}->{uiForm.DiskWritesForTest} 旧={ctxOld} 界面上={uiForm.ContextBoxForTest}");
                Check("DEC-092 切页只是换视角：改过的那格仍留在界面上（内存留痕不能一起丢掉）",
                    uiForm.ContextBoxForTest == ctxNew, uiForm.ContextBoxForTest);
                uiForm.SaveWithoutCommitForTest();
                Check("DEC-092 非提交式落盘（更新链记跳过位 / 端口避让 / 关窗记窗口位置那一类）按冻结表写：那一格仍是旧值",
                    uiForm.ValueInIniForTest("Context", snapSec) == ctxOld && uiForm.ContextBoxForTest == ctxNew,
                    $"文件里={uiForm.ValueInIniForTest("Context", snapSec)} 界面上={uiForm.ContextBoxForTest}");
                Check("DEC-092 关窗那句「写进去 / 丢弃」报得出这一格的明细（口径同 DEC-082：只报键名不算落点）",
                    uiForm.ParamDiffForTest(MainForm.ParamHome.Launcher).Contains("上下文 " + ctxOld + " → " + ctxNew),
                    uiForm.ParamDiffForTest(MainForm.ParamHome.Launcher));
                bool gateSaved = uiForm.SaveParameterEditsForTest();
                Check("DEC-092 只有点「保存」才落盘：文件里那一格变成界面值，按钮回灰",
                    gateSaved && uiForm.ValueInIniForTest("Context", snapSec) == ctxNew && !uiForm.SaveButtonEnabledForTest,
                    $"ok={gateSaved} 文件里={uiForm.ValueInIniForTest("Context", snapSec)} 按钮亮={uiForm.SaveButtonEnabledForTest}"
                    + " ｜最后几行：" + uiForm.LogHistoryTailForTest(4));
                // ============ DEC-122③：那 16 个键只剩一个家（记录），[Current] 里的副本不再写出 ============
                {
                    var sixteen = new[]
                    {
                        nameof(Settings.ApiModel), nameof(Settings.Context), nameof(Settings.Kv), nameof(Settings.KvResident),
                        nameof(Settings.Spec), nameof(Settings.SpecMinP), nameof(Settings.ExpertCache), nameof(Settings.Prefill),
                        nameof(Settings.Vision), nameof(Settings.VramReserveMib), nameof(Settings.Gpu), nameof(Settings.LayerSplit),
                        nameof(Settings.ArenaPinGib), nameof(Settings.HeadLate), nameof(Settings.ExtraArgs),
                        nameof(Settings.ReasoningEffort),
                    };
                    var stillInCurrent = File.ReadLines(iniTmp)
                        .TakeWhile(l => !l.Trim().StartsWith("[" + snapSec + "]"))     // 只看 [Current] 那一层（到本段之前）
                        .Select(l => l.Trim().Split('=')[0])
                        .Intersect(sixteen).ToList();
                    Check("DEC-122③ 点过一次「保存」之后，[Current] 里这 16 行一条都不再写出（重复副本退役）",
                        stillInCurrent.Count == 0, "还在 [Current] 里：" + string.Join("、", stillInCurrent));
                    var missingInRecord = sixteen.Where(k => uiForm.ValueInIniForTest(k, snapSec) == "<没有这一行>").ToList();
                    Check("DEC-122③ 记录那一段照旧把这 16 格全装上（值由记录承接 = 用户无感；Model= 选中指针不在名单里）",
                        missingInRecord.Count == 0 && MainForm.ModelOwnedKeys.SequenceEqual(sixteen)
                        && MainForm.RetiredCurrentKeys.Contains(nameof(Settings.Context))
                        && !MainForm.RetiredCurrentKeys.Contains(nameof(Settings.Model)),
                        "段里缺的：" + string.Join("、", missingInRecord));
                    Check("DEC-122③ 端口 / 监听地址这类不跟模型走的键照旧落 [Current]（不许顺手一起清掉）",
                        uiForm.CurrentValueInIniForTest("Port") != "<没有这一行>"
                        && uiForm.CurrentValueInIniForTest("Host") != "<没有这一行>",
                        $"Port={uiForm.CurrentValueInIniForTest("Port")} Host={uiForm.CurrentValueInIniForTest("Host")}");
                }
                // DEC-122③ 防护①：冻结基准改从记录取 —— 换一次模型（"看一眼"那条路）之后关窗明细必须是空的。
                // 少了 SyncModelBaselineFromRecord，这一条就会红成十几行他从没改过的"131072 → 524288"
                //（基准还是上一次提交 / new Settings() 的那一份，而卡片已经换成另一个模型的记录）。
                {
                    var tagA = uiForm.SelectedTagForTest;
                    var tagB = uiForm.ModelsForTest.FirstOrDefault(m => m.Ready && m.Tag != tagA)?.Tag;
                    Check("DEC-122③ 用例前提：环境里有第二个就绪的 Strata 模型（换一次模型这条用例才有得跑）",
                        tagB is not null && tagB != tagA, $"A={tagA} B={tagB ?? "无"}");
                    if (tagB is not null)
                    {
                        uiForm.SelectModelForTest(tagB);
                        var diffB = uiForm.ParamDiffForTest(MainForm.ParamHome.Launcher);
                        Check("DEC-122③ 换到另一件（没点保存、什么都没改）：关窗明细一条都不列 = 基准跟着记录走",
                            diffB.Length == 0 && !uiForm.PendingParamEditsForTest, diffB);
                        uiForm.SelectModelForTest(tagA);
                        var diffA = uiForm.ParamDiffForTest(MainForm.ParamHome.Launcher);
                        Check("DEC-122③ 换回来照样是空的（两头都不许冒假差异；这一族出过 BUG-034/049）",
                            diffA.Length == 0 && !uiForm.PendingParamEditsForTest && !uiForm.SaveButtonEnabledForTest, diffA);
                    }
                }
                // ============ DEC-122①：唤醒取参 = 「配置模板 ⊕ [Model:<tag>] 记录」；手动「启动」照旧用界面卡 ============
                // 他抓的那次故障（"没改配置只点了升级，速度却大降 + 网关转发失败"）根子就在这条路上：
                // 唤醒以前物化的是**界面卡现值**，而换下拉只是"看一眼"就会把所看那件的存档载上卡。
                // 上面那次「保存」刚把 ctxNew 写进记录 → 现场是可控的：文件里 = ctxNew，卡片摆一个没保存的草稿。
                // 两条都物化一遍打在产物上（跑在 data\tmp\selftest-run\ 那个改道窝里，真实产物一份不写 = DEC-094 ⓪ 那道闸照旧）。
                {
                    var wakeTag = uiForm.SelectedTagForTest;
                    var fileCtx = uiForm.ValueInIniForTest("Context", "Model:" + wakeTag);
                    var draft = fileCtx == "163840" ? "147456" : "163840";      // 一定与文件里那一份不同的合法数
                    uiForm.SetContextForTest(draft);                              // 卡片上手改一格、**不点保存** = 草稿
                    var wake = uiForm.StartSettingsFor(wakeTag, openBrowser: false, wakeFromRecord: true);
                    var manual = uiForm.StartSettingsFor(wakeTag, openBrowser: false, wakeFromRecord: false);
                    Check("DEC-122① 用例前提：文件里那一格 = 刚才「保存」写进去的值（否则下面两条空跑）",
                        fileCtx == ctxNew && draft != fileCtx, $"文件={fileCtx} 草稿={draft} 保存值={ctxNew}");
                    Check("DEC-122① 唤醒不带界面草稿：上下文仍是文件里那一份（手动那一条才跟着草稿走）",
                        wake.Context == fileCtx && manual.Context == draft,
                        $"文件={fileCtx} 草稿={draft} 唤醒={wake.Context} 手动={manual.Context}");
                    Check("DEC-122① 端口 / 监听地址 / Strata 目录这类不跟模型走的字段两种起法一样取壳设置；Model 恒等于要起的那一件",
                        wake.Port == manual.Port && wake.Host == manual.Host && wake.Root == manual.Root
                        && wake.Model == wakeTag && manual.Model == wakeTag
                        && wake.OpenBrowser == manual.OpenBrowser,
                        $"port {wake.Port}/{manual.Port} host {wake.Host}/{manual.Host} model {wake.Model}");
                    bool okWake = Materialize(wake, out var wakeCfg, out var wakeErr);
                    bool okMan = Materialize(manual, out var manCfg, out var manErr);
                    if (okWake && okMan)
                        Check("DEC-122① 两条路各自物化出来的 --max-context：唤醒 = 文件里那一份、手动 = 界面上那一格",
                            Value(ArgList(wakeCfg), "--max-context") == fileCtx
                            && Value(ArgList(manCfg), "--max-context") == draft,
                            $"唤醒={Value(ArgList(wakeCfg), "--max-context")} 手动={Value(ArgList(manCfg), "--max-context")}");
                    else Check("DEC-122① 两份都该物化得出来（用例本身跑通了）", false,
                            $"ok={okWake}/{okMan} err=[{wakeErr}] [{manErr}]");
                    // 模板缺失 = 唤醒不猜：交给 BuildEffectiveConfig 那句"找不到模型配置"拒启
                    var ghost = uiForm.StartSettingsFor("selftest-没有这一件", false, wakeFromRecord: true);
                    Check("DEC-122① 模型没记录、模板也不在 → 唤醒不许猜一套默认值把进程起起来（照旧被'找不到模型配置'拦下）",
                        !MainForm.BuildEffectiveConfig(ghost, out _, out var ghostErr) && ghostErr.Contains("找不到模型配置"),
                        ghostErr);
                    // ============ DEC-135（审查 A-1）：没有记录的那一件，打底必须是出厂默认而不是"上一件那一格" ============
                    // `ExtraArgs` 是模板里**根本没有**的那一键（只能来自打底），所以它是这一族最干净的证人；
                    // 老写法拿 `_savedParams`（= 界面上看过的最后一件）打底 → 唤醒别人、或点一次保存把幽灵值写进这一件的记录。
                    {
                        var extraBak = uiForm.ExtraArgsBoxForTest;
                        uiForm.SetExtraArgsForTest("--ghost-flag 7");
                        bool extraSaved = uiForm.SaveParameterEditsForTest();       // 提交 = 写进「当前那一件」的记录 + 冻结表
                        var ghostWake = uiForm.StartSettingsFor("selftest-没有这一件", false, wakeFromRecord: true);
                        var ghostManual = uiForm.StartSettingsFor(uiForm.SelectedTagForTest, false, wakeFromRecord: false);
                        Check("DEC-135（A-1）用例前提：刚才那一次提交确实把附加参数落进了「当前那一件」的记录里",
                            extraSaved && ghostManual.ExtraArgs == "--ghost-flag 7",
                            $"saved={extraSaved} 当前件={ghostManual.ExtraArgs}");
                        Check("DEC-135（A-1）没有记录的件：唤醒不带上一件的附加参数（打底归出厂默认），那一格也不留在卡上",
                            ghostWake.ExtraArgs.Length == 0, $"唤醒那一份 ExtraArgs=「{ghostWake.ExtraArgs}」");
                        uiForm.SetExtraArgsForTest(extraBak);
                        uiForm.SaveParameterEditsForTest();      // 记录与冻结表都按回去，别污染后面的用例
                        uiForm.MarkParamsSavedForTest();
                    }
                    // DEC-137（D-3）：壳自己的 ini 写不下去时，「保存」不许报"已保存"、按钮必须保持亮
                    //（DEC-126 之后 [Model:<tag>] 是唤醒取参的唯一事实源；口径与 llama 段那条 Failed 一致）。
                    // 造失败的办法不靠运气：把 ConfigPath 指到一个**目录**上 —— tmp 写得出去、改名那一步必抛。
                    {
                        var iniBak = MainForm.ConfigPathForTest;
                        var fakeDir = MainForm.Scratch("selftest-inifail-" + Guid.NewGuid().ToString("N")[..8]);
                        Directory.CreateDirectory(fakeDir);
                        try
                        {
                            MainForm.UseTempConfigForTest(fakeDir);
                            uiForm.SetContextForTest("147456");        // 先留一笔没落盘的改动（这才是"没存上"要现形的现场）
                            bool preLit = uiForm.SaveButtonEnabledForTest;
                            bool savedAnyway = uiForm.SaveParameterEditsForTest();
                            Check("DEC-137 D-3 ini 落盘失败时「保存」返回 false、按钮保持亮（旧写法会照样按灰并写一句已保存）",
                                preLit && !savedAnyway && uiForm.SaveButtonEnabledForTest,
                                $"改前有草稿={preLit} 返回={savedAnyway} 之后按钮亮={uiForm.SaveButtonEnabledForTest}");
                            Check("DEC-137 D-3 落盘改成同目录改名替换（tmp + File.Move），不再整文件截断重写",
                                MainForm.ConfigPathForTest.Length > 0 && File.Exists(MainForm.ConfigPathForTest)
                                || Directory.Exists(MainForm.ConfigPathForTest),   // 这一趟的"文件"其实是个目录 = 失败是必然的
                                MainForm.ConfigPathForTest);
                        }
                        finally
                        {
                            MainForm.UseTempConfigForTest(iniBak);
                            try { Directory.Delete(fakeDir, true); } catch { /* 临时目录里的垃圾，收尾扫手会收 */ }
                        }
                    }
                    uiForm.SetContextForTest(ctxLive);        // 草稿收掉（下面那次还原 + 重设基线照常走一遍）
                }
                // ============ DEC-110①：保存那句只该长在**激活模块那一页**的日志里 ============
                // 用户："保存参数也应该按模块显示在各自的日志模块内, 现在是一条更新, 3个模块都显示"。
                // 以前那句走默认 launcher 通道（= 三页都显示），而"保存"只写当前页那一段，另两页根本没被碰。
                Check("DEC-137 A-2 空闲停服那句只有一个家（分钟数由调用方从**本模块**那道阈值换算，Ninfer 不再报 Strata 的数）",
                    MainForm.IdleStopLine(7).Contains("7 分钟") && !MainForm.IdleStopLine(7).Contains("{"),
                    MainForm.IdleStopLine(7));
                // DEC-138（M6 缺口 3）：AGENTS 那句「新加主窗参数控件要挂进那两组循环」第一次有了机器判据 ——
                // 拿界面上的控件树（卡1/卡2/卡3/高级区里所有输入框、下拉、开关）对着 WireEvents 的登记册比，少挂一枚就红。
                // 这正是 DEC-098/100 反复应验的那一族（机制写了没接上），例外名单里只放**登记在案的不参与件**。
                var dirtySet = new HashSet<Control>(uiForm.DirtyWiredForTest);
                var dirtyExempt = new HashSet<Control>(uiForm.DirtyExemptForTest);      // 例外名单只有一个家（在 MainForm 那边点名与写理由）
                var unwired = uiForm.ParamControlsForTest()
                    .Where(c => !dirtySet.Contains(c) && !dirtyExempt.Contains(c))
                    .Select(c => c.GetType().Name + "#" + c.GetHashCode().ToString("X8"))
                    .ToList();
                Check("DEC-138 缺口3 每一枚参数格子都真挂了脏判定事件（按界面控件树穷举 = DEC-092 那条硬口径的机器版）",
                    unwired.Count == 0, "没挂上的：" + string.Join(" / ", unwired));

                Check("DEC-110①「界面参数已保存。」落在激活模块自己的通道（此刻 = strata），launcher 通道一条都没有",
                    uiForm.LogHistoryCountWhereForTest("strata", "界面参数已保存") >= 1
                    && uiForm.LogHistoryCountWhereForTest("launcher", "界面参数已保存") == 0,
                    "最后几行：" + uiForm.LogHistoryTailForTest(3));
                uiForm.SwitchModuleForTest("llama");
                Check("DEC-110① 切到另两页就看不见那句（以前三页都显示 = 一条更新三家都响）",
                    !uiForm.LogBoxTextForTest.Contains("界面参数已保存"),
                    uiForm.LogBoxTextForTest.Replace("\r\n", " ⏎ "));
                uiForm.SwitchModuleForTest("strata");
                MainForm.UseTempConfigForTest(iniReal);
                Check("DEC-092 这套断言写在 data\\tmp\\ 副本上，跑完 ConfigPath 已还原回开发目录那份",
                    File.Exists(iniTmp) && MainForm.ConfigPathForTest == iniReal, iniTmp);
                uiForm.SetContextForTest(ctxLive);
                uiForm.MarkParamsSavedForTest();      // 还原界面值并重设基线，别污染后面的用例


                uiForm.RefreshModelsForTest();
                // 2026-10-04（DEC-076）：思考深度两项都改下拉——界面 <-> Settings 往返 + 选项表 + 空选项在最前
                uiForm.SetReasoningForTest("medium");
                Check("思考深度（Strata）：下拉选中即收集（none/low/medium/high 之一原样往返）",
                    uiForm.ReasoningCollectedForTest == "medium", uiForm.ReasoningCollectedForTest);
                uiForm.SetReasoningForTest("");
                Check("思考深度（Strata）首项是空白（选了能回空白）、其余 none/low/medium/high",
                    uiForm.StrataReasoningOptionsForTest.Count == 5 && uiForm.StrataReasoningOptionsForTest[0] == ""
                    && uiForm.StrataReasoningOptionsForTest.Skip(1).SequenceEqual(new[] { "none", "low", "medium", "high" }),
                    string.Join("|", uiForm.StrataReasoningOptionsForTest));
                Check("思考深度（llama）首项空白 + 官方取值表（default/minimal/low/medium/high/xhigh/max）",
                    uiForm.LlamaReasoningOptionsForTest.SequenceEqual(new[] { "", "default", "minimal", "low", "medium", "high", "xhigh", "max" }),
                    string.Join("|", uiForm.LlamaReasoningOptionsForTest));
                uiForm.SetLlamaReasoningForTest("xhigh");
                Check("思考深度（llama）：下拉选中即收集（xhigh 原样往返）",
                    uiForm.LlamaReasoningCollectedForTest == "xhigh", uiForm.LlamaReasoningCollectedForTest);
                uiForm.SetLlamaReasoningForTest("");
                Check("思考深度（llama）：选回空白 = 留空（收集为空）",
                    uiForm.LlamaReasoningCollectedForTest == "", uiForm.LlamaReasoningCollectedForTest);
                // DEC-145：两枚新下拉的"选中即收集"（与上面思考深度同一套接缝形状）
                uiForm.SetLlamaSpecTypeForTest("draft-dspark");
                Check("DEC-145 投机方式：下拉选中即收集（draft-dspark 原样往返）",
                    uiForm.LlamaSpecTypeCollectedForTest == "draft-dspark", uiForm.LlamaSpecTypeCollectedForTest);
                uiForm.SetLlamaSpecTypeForTest("");
                Check("DEC-145 投机方式：选回空白 = 收集为空（= 删键回继承）",
                    uiForm.LlamaSpecTypeCollectedForTest == "", uiForm.LlamaSpecTypeCollectedForTest);
                uiForm.SetLlamaMmprojForTest("true");
                Check("DEC-145 视觉：下拉选中即收集（GPU = \"true\" 原样往返）",
                    uiForm.LlamaMmprojCollectedForTest == "true", uiForm.LlamaMmprojCollectedForTest);
                uiForm.SetLlamaMmprojForTest("");
                Check("DEC-145 视觉下拉选项：空白 / GPU / CPU 三档（tag 就是写入形态；「关闭」档刻意不做）",
                    uiForm.LlamaMmprojOptionsForTest.SequenceEqual(new[] { "", "GPU", "CPU" }),
                    string.Join("|", uiForm.LlamaMmprojOptionsForTest));
                // 2026-10-02（DEC-033）：服务启停不再重扫模型——"没进程可退"的停止路径不该多出扫描日志
                string LogText() => uiForm.LogBoxTextForTest;
                var scansBefore = LogText().Split("已扫描").Length + LogText().Split("已解析").Length;
                uiForm.StopServerForTest();
                var scansAfter = LogText().Split("已扫描").Length + LogText().Split("已解析").Length;
                Check("停止（没进程可退）不再重扫模型（DEC-033）", scansAfter == scansBefore, $"{scansBefore} -> {scansAfter}");
                // 2026-10-04 DEC-076 候选：卡2 两行——模型名｜上下文 ／ KV 精度｜附加参数。
                // dump 尾部会把高级区（在 _advHost 里）的条目并进来，所以顺序断言只看「文本框+文本框+」堆叠之前的那段。
                {
                    var head = Row("card2").Split("文本框+文本框+")[0];
                    Check("卡片2 视觉顺序 = 模型名称｜上下文｜打开网页｜◂ 高级｜KV 精度｜附加参数（DEC-076 候选两行）",
                        InOrder(head, "模型名称", "上下文", "打开网页", "◂ 高级", "KV 精度", "附加参数")
                        && !Row("adv-strata").Contains("附加参数") && !Row("adv-strata").Contains("思考深度"), head);
                }
                Check("Strata 页：卡1 端口格 = 公开端口、思考深度 = Strata 下拉（llama 那两只挪出画外）",
                    uiForm.PortSlotForTest == "public" && uiForm.ReasoningSlotForTest == "strata",
                    $"port={uiForm.PortSlotForTest} reason={uiForm.ReasoningSlotForTest}");
                {
                    var fit = uiForm.GridFitForTest();
                    W($"各格「值放得下吗」实测（内层宽/文本宽，物理px）: {fit}");
                    W("API 地址字号候选（DEC-107）: " + uiForm.ApiFontCandidatesForTest());
                    bool FitOk(string seg) { var p = seg.Split('=')[1].Split('/'); return int.Parse(p[0]) >= int.Parse(p[1]); }
                    Check("各格都放得下实值（长 IP / 18082 / 262144 / 33 字长段名 / 附加参数 + 六只下拉的取值 + API 地址本条；DEC-078、DEC-079、DEC-107、DEC-145）",
                        fit.Split(' ').All(FitOk), fit);
                    // DEC-079 出图（sb9）抓出来的：GDI 绘制自带留白，富余只留 8px 时「q8_0 / q8_0」仍被省略号截断
                    // → 这一格的下限单独钉住（NoPadding 量出来的数会偏乐观，所以按物理富余判）
                    var kvSlack = uiForm.LlamaKvSlackForTest("q8_0 / q8_0");
                    Check("llama KV 那一格放得下最长一档（富余 >= 12 逻辑px）",
                        kvSlack >= UiTheme.S(uiForm, 12), "富余=" + kvSlack + " 物理px（" + uiForm.LlamaKvFitDiagForTest("q8_0 / q8_0") + "）");
                    // DEC-096：第三页进来后这条判据要跟着管它——Ninfer 页卡1「思考深度」最长档 = minimal（WIN 那张表），
                    // 出图第一版就是在这里被省略号截断的（Need+S(40) 那档太乐观，DEC-079 的教训在第三页重演）。
                    // **切完必须还原**：后面那批「打开配置」用例认的是当前这一页，别把激活页留在 Ninfer。
                    var pageBefore = uiForm.ActiveModuleForTest;
                    uiForm.SwitchModuleForTest("ninfer");
                    var nrSlack = uiForm.NinferReasonSlackForTest("minimal");
                    Check("Ninfer 卡1「思考深度」放得下最长一档（富余 >= 12 逻辑px，DEC-088 那笔宽度账的底线）",
                        nrSlack >= UiTheme.S(uiForm, 12),
                        "富余=" + nrSlack + " 物理px（宽/可用/需要 = " + uiForm.NinferReasonFitDiagForTest("minimal") + "）");
                    Check("Ninfer 卡2「KV 精度」放得下这一族最长档 rk4v4-e8",
                        uiForm.NinferKvSlackForTest("rk4v4-e8") >= UiTheme.S(uiForm, 12),
                        "富余=" + uiForm.NinferKvSlackForTest("rk4v4-e8"));
                    // DEC-088 那笔"模型名称格吃余量 → 放得下 model-id"的账必须拿算式量（第一版拿 mock 手填的 217 判，
                    // 实际算式给的是 217 - 标签 - 内缩，22 字的 qwen3.8-27b-swift-iq3s 被截断）
                    var mfit = uiForm.NinferModelFitForTest("qwen3.8-27b-swift-iq3s");
                    Check("Ninfer 卡2「模型名称」放得下最长那把 model-id（DEC-088 的余量账，出图台量过）",
                        mfit.room - mfit.need >= UiTheme.S(uiForm, 8),
                        "宽=" + mfit.width + " 可用=" + mfit.room + " 需要=" + mfit.need + " 富余=" + (mfit.room - mfit.need) + " 字体=" + mfit.font);
                    uiForm.SwitchModuleForTest(pageBefore);
                }
                // ⑳ DEC-102（BUG-048）：网关唤醒"起哪一件" —— 必须起**请求要的那件**，不许被界面下拉的当前选中顶掉。
                // 现场（他 10-08 的日志）：请求 qwen3.8-27b-swift-iq3s 但界面停在 gsq → 起的是 gsq，
                // 引擎以 gsq 身份就绪，客户端拿 swift 名被引擎 404，反复重试每次都这样。
                Check("DEC-102 「这次起哪一件」：显式 tag 优先于界面选中（唤醒/网关接管喂 tag；手动喂 null = 用选中）",
                    MainForm.NinferStartTagFor("qwen3.8-27b-swift-iq3s", "qwen3.8-27b-gsq-iq3s") == "qwen3.8-27b-swift-iq3s"
                    && MainForm.NinferStartTagFor(null, "qwen3.8-27b-gsq-iq3s") == "qwen3.8-27b-gsq-iq3s"
                    && MainForm.NinferStartTagFor("", "x") == "x" && MainForm.NinferStartTagFor(" y ", "x") == "y");
                {
                    // 真入口干跑（NinferStartDryRunForTest）：喂两个不同的 tag，证人位必须各归各的 ——
                    // 把修复按回去（改回读 s.NinferModel）时两次会拿到同一个界面值，这条会红。
                    var prevDry = MainForm.NinferStartDryRunForTest;
                    MainForm.NinferStartDryRunForTest = true;
                    try
                    {
                        uiForm.StartNinferServerForTest("qwen3.8-27b-swift-iq3s");
                        var w1 = uiForm.NinferLastStartTagForTest;
                        uiForm.StartNinferServerForTest("qwen3.8-27b-gsq-iq3s");
                        var w2 = uiForm.NinferLastStartTagForTest;
                        Check("DEC-102 干跑真入口：喂谁就起谁（两次不同 tag 各归各的，与界面选中无关）",
                            w1 == "qwen3.8-27b-swift-iq3s" && w2 == "qwen3.8-27b-gsq-iq3s", w1 + " / " + w2);
                    }
                    finally { MainForm.NinferStartDryRunForTest = prevDry; }
                }
                UiNinferWakeChecks();
                UiUpdateWindowChecks();
                UiSettingsPageChecks();
                UiFollowChecks();
                }   // inner try 收尾
                finally
                {
                    // 释放次序照原 using 的逆序（wf→nf→lf→uf→sf→主窗）；异常路径下可能还有没建起来的
                    DisposeIfAny(wf); DisposeIfAny(nf); DisposeIfAny(lf); DisposeIfAny(uf); DisposeIfAny(sf);
                    uiForm.Dispose();
                }
            }
            catch (Exception ex) { Check("界面排布用例", false, ex.Message); }
    }

    // 窗体释放助手：字段化的窗在异常路径下可能还没建起来（原 using 语义里那种情况本来就等于没建）
    static void DisposeIfAny(IDisposable? d) { try { d?.Dispose(); } catch { /* 释放失败不该盖掉真异常 */ } }
}
