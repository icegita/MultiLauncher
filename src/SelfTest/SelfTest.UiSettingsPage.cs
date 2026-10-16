namespace MultiLauncher;

// 界面排布组（设置页与模块切换段）：标题/控件对齐、按钮行说明、滚轮、齿轮与标题栏、最小宽度、
// B 段切模块/模块停用、模块表可穷举性、日志分流、DEC-097⑤/①、DEC-156 占用估算、卡片间距。
// 2026-10-15 从 SelfTest.UiLayout.cs 按子域切出，块内一行未改；共享状态见 SelfTest.State.cs。
internal static partial class SelfTest
{
    internal static void UiSettingsPageChecks()
    {
                // --- 本轮反馈：标题与右侧控件同一水平线（原来标题按固定 Top 摆，看着"太靠上"） ---
                var align = sf.DumpRowAlignment();
                foreach (var line in align) W("align " + line);
                bool Aligned(string s)
                {
                    var tail = s[(s.IndexOf("dy=", StringComparison.Ordinal) + 3)..].Split(' ')[0];
                    return double.TryParse(tail, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var dy) && Math.Abs(dy) <= 2.0;
                }
                Check("设置页标题与右侧控件中心对齐（≤2px，" + align.Count + " 行）",
                    align.Count >= 12 && align.All(Aligned), string.Join(" ; ", align.Where(x => !Aligned(x))));

                // --- 用户 2026-10-04 第 4 条：按钮行那句备份说明要**整行显示、不换行** ---
                //     原来 ButtonRow 给说明文字预留 S(260)（挤在按钮左边那条窄栏里），
                //     「每次要写 llama_models.ini 之前先存一份到 data\llama\，只留最近 5 份。」正好被压成两行。
                var btnDesc = sf.ButtonRowDescForTest();
                int padX = UiTheme.S(uiForm, SettingsForm.PadXForTest);
                string DescLine((string Title, int Lines, int GapBelowButton, int RowWidth, int TextWidth, int DescMaxWidth, int ClientWidth) x)
                    => $"{x.Title}: 铺满整行={x.DescMaxWidth == x.RowWidth - 2 * padX} 单行字宽={x.TextWidth}" +
                       $"/可用={x.ClientWidth - 2 * padX} 距按钮={x.GapBelowButton}px";
                W("按钮行说明文字：" + string.Join(" ｜ ", btnDesc.Select(DescLine)));
                Check("按钮行的说明文字铺满整行、单行放得住且不压到按钮（" + btnDesc.Count + " 行）",
                    btnDesc.Count >= 3 && btnDesc.All(x =>
                        x.DescMaxWidth >= x.RowWidth - 2 * padX            // MaximumSize 给到整行宽（旧的 S(260) 预留栏会小得多）
                        && x.TextWidth + 2 * padX + UiTheme.S(uiForm, 24) <= x.ClientWidth   // 单行字宽放得进页宽（再扣掉滚动条那档）
                        && x.GapBelowButton >= 0),                          // 整行时它落在按钮**下方**，不会水平相撞
                    string.Join(" ｜ ", btnDesc.Select(DescLine)));

                // --- 本轮反馈：设置页滚轮不带动自绘滑条（滚轮按焦点投递，不按光标） ---
                Check("设置页各节都装了滚轮路由", sf.BarsForTest.All(b => b.Router is not null),
                    string.Join(",", sf.BarsForTest.Select(b => b.Router is null ? "无" : "有")));

                // --- 第四轮反馈：卡片间距 / 导航按钮左对齐 / 矢量齿轮 / 设置窗标题栏 ---
                var (g12, g23) = uiForm.CardGapsForTest();
                W($"卡片间距: 页头->card2={g12}px  card2->card3={g23}px（原型 .main gap:12px）");
                Check("主界面卡片之间留出 12px 间距",
                    g12 >= UiTheme.S(uiForm, 10) && g23 >= UiTheme.S(uiForm, 10), $"{g12}/{g23}");
                Check("「打开网页」已挪到参数卡、左栏不剩动作按钮",
                    !Row("left-col").Contains("打开网页") && Row("card2").Contains("打开网页"), Row("left-col"));
                var (gear, gearText) = uiForm.SettingsButtonForTest;
                Check("设置按钮 = 矢量齿轮 + 纯文字（不再用字体里的 ⚙）", gear && !gearText.Contains('⚙'), gearText);
                // 标题栏自定义色只有窗口真的显示出来才读得回（自检不开窗），所以这里只打一行观测值，
                // 断言交给 titlebar-probe.ps1 的取色（主窗样式 vs 设置窗样式各测一次）。
                string CapOf(IntPtr h) => UiDark.CaptionColorOf(h) is null ? "未设/读不到" : UiDark.CaptionColorOf(h)!.Value.ToString("X6");
                W($"标题栏底色读回: 主窗={CapOf(uiForm.Handle)} 设置窗={CapOf(sf.Handle)}（不开窗时 DWM 读不回，别当断言用）");

                // --- 第五轮：启动模式并入左栏模式条后，卡片1 少一项、最小宽度重新量 ---
                Check("左栏模式条已变成显示当前模式的切换按钮",
                    Row("left-col").Contains("路由模式") || Row("left-col").Contains("单体模型"), Row("left-col"));
                // DEC-107：三页各量一遍取最大（Ninfer 页卡1 多一枚方块 = 真正最宽的那页，只量当页会漏）
                // DEC-138（E-2）：先把 Ninfer 临时停掉再量 —— 那一族老写法（`if (!Enabled...) continue;`）在这种情况下
                // 只会量到两页，而"三页全量"这条判据就当场红。判据不再跟着用户那份 ini 走。
                uiForm.SetModuleEnabledForTest("ninfer", false);
                var (minClient, minDetail, minPages) = uiForm.MinClientWidthAllPagesForTest();
                uiForm.SetModuleEnabledForTest("ninfer", true);
                W($"实测最小客户宽 = {minClient} 逻辑像素（{minDetail}；DEC-011 原为 970、DEC-017 为 870、DEC-074 撤卡1 后实测 664、DEC-096 起 940、DEC-107 起 960）");
                // DEC-138（E-2）：这条就是"用例前提"本身 —— 三页**每一页都真被量到**，不是只量当时启用的那几页。
                // 以前少一页照样绿（判据跟着用户 ini 里那枚开关走），这条按回去（`continue` 那句恢复）就红。
                Check("DEC-138 E-2 用例前提：最小宽这一趟把模块表里每一页都量了（临时全开再还原，判据不跟用户设置走）",
                    minPages == ModuleRegistry.All.Length && minDetail.Split('｜').Length == ModuleRegistry.All.Length,
                    $"量了 {minPages} 页 / 表里 {ModuleRegistry.All.Length} 页 ｜ {minDetail}");
                Check("最小宽度实测合理（<=960，DEC-107 左栏+卡1 那两笔账：940 → 960）", minClient is > 480 && minClient <= 960, "minClient=" + minClient);
                // 默认宽不能直接断言 ClientSize：构造函数末尾会 RestoreWindowBounds()，
                // 从实跑目录（D:\MultiLauncher）跑时读到的是用户存过的位置尺寸（实测 1966）。真正要保的不变量是"最小宽兜得住内容"。
                Check("窗口最小宽 >= 实测最小客户宽（缩到最小也不裁内容）",
                    UiTheme.DpiToLogical(uiForm.MinimumSize.Width) >= minClient,
                    $"MinimumSize={UiTheme.DpiToLogical(uiForm.MinimumSize.Width)} vs 实测 {minClient}");
                Check("当前窗口宽度下三张卡片都不被裁", uiForm.ClipReport().Length == 0, uiForm.ClipReport());

                // --- B 段：切模块页 = 换视角（不动服务）；下拉只列本模块模型、高级区/KV 格随模块换 ---
                var llamaFirst = uiForm.LlamaModelsForTest.FirstOrDefault();
                var llamaDisplays = uiForm.LlamaModelsForTest.Select(m => m.Display).ToHashSet();
                var strataDisplays = uiForm.ModelsForTest.Select(m => m.Display).ToHashSet();
                if (llamaFirst is not null)
                {
                    uiForm.SwitchModuleForTest("llama");
                    Check("切到 llama 页：高级区 = llama.cpp 那一套", uiForm.AdvancedBackendForTest == "llama", uiForm.AdvancedBackendForTest);
                    Check("切到 llama 页：下拉只列本模块模型",
                        uiForm.ModelDropTextsForTest.All(t => llamaDisplays.Contains(t) || t.StartsWith("（")),
                        string.Join(";", uiForm.ModelDropTextsForTest.Take(3)));
                    var llamaExpect = uiForm.RouteModeForTest ? "加载" : "重载";
                    Check($"llama 页：模型行按钮「{llamaExpect}」（随该模块模式）", uiForm.ModelActionTextForTest == llamaExpect, uiForm.ModelActionTextForTest);
                    // 2026-10-04 DEC-076 候选：卡1 两只格子各换 llama 那只（端口：公开→llama-server；思考深度：下拉→文本框）
                    var rowsLlama = uiForm.DumpLayoutRows();
                    string RowL(string title) => rowsLlama.FirstOrDefault(x => x.StartsWith(title + ":", StringComparison.Ordinal)) ?? "";
                    Check("llama 页：卡1 端口格 / 思考深度格都换成 llama 那只（都是下拉）",
                        uiForm.PortSlotForTest == "llama" && uiForm.ReasoningSlotForTest == "llama",
                        $"port={uiForm.PortSlotForTest} reason={uiForm.ReasoningSlotForTest}");
                    Check("llama 页：卡2 同版式两行",
                        InOrder(RowL("card2"), "模型名称", "上下文", "KV 精度", "附加参数"), RowL("card2"));
                    uiForm.SetExtraForTest("--threads 16");
                    Check("llama 页：附加参数可编辑且收集进 LlamaExtraArgs（DEC-075）",
                        !uiForm.ExtraReadOnlyForTest && uiForm.ExtraCollectedForTest == "--threads 16",
                        $"readonly={uiForm.ExtraReadOnlyForTest} collected={uiForm.ExtraCollectedForTest}");
                    uiForm.SetExtraForTest("");
                    var llamaPortKeep = uiForm.LlamaPortSettingForTest;
                    uiForm.SetLlamaPortForTest("18888");
                    Check("llama 页「监听端口」改值即时写回设置（18888）",
                        uiForm.LlamaPortSettingForTest == "18888" && uiForm.LlamaPortBoxForTest == "18888",
                        $"box={uiForm.LlamaPortBoxForTest} setting={uiForm.LlamaPortSettingForTest}");
                    uiForm.SetLlamaPortForTest(llamaPortKeep);   // 还原，别污染后面的用例
                    // DEC-079：这两格从"只读展示"变成段级编辑（KV 是 6 项下拉、上下文是文本框），
                    // 值取「段 → `[*]`/文件头 → 空」；这一条同时验证「切到 llama 页确实按段载入了家底」
                    //（新 ini 里没记 llama 模型时，自动选中那一段也要读进来 —— 曾经漏过一次，界面全空）。
                    Check("llama 页：KV 精度格是可编辑下拉，值 = 该段（或继承）的 cache-type-k / v",
                        !uiForm.LlamaKvDisplayForTest.Contains("只读") &&
                        (uiForm.LlamaKvDisplayForTest.Contains('/') || uiForm.LlamaKvDisplayForTest.Length > 0),
                        uiForm.LlamaKvDisplayForTest);
                    Check("llama 页：上下文格可编辑，值 = 该段（或继承）的 ctx-size",
                        uiForm.LlamaCtxEditableForTest && uiForm.ContextBoxForTest.Length > 0,
                        $"readonly={uiForm.LlamaCtxEditableForTest} ctx={uiForm.ContextBoxForTest}");
                    Check("llama 页：模式条显示 llama 自己的模式",
                        uiForm.ModeLabelForTest == (uiForm.RouteModeForTest ? "路由模式" : "单体模型"), uiForm.ModeLabelForTest);
                    uiForm.SwitchModuleForTest("strata");
                    Check("切回 Strata 页：两只格子都换回 Strata 那只（都是下拉）",
                        uiForm.PortSlotForTest == "public" && uiForm.ReasoningSlotForTest == "strata",
                        $"port={uiForm.PortSlotForTest} reason={uiForm.ReasoningSlotForTest}");
                    Check("切回 Strata 页：高级区回 Strata 那一套", uiForm.AdvancedBackendForTest == "strata", uiForm.AdvancedBackendForTest);
                    if (strataForTest is not null)
                        Check("切回 Strata 页：模型行按钮恢复「加载」", uiForm.ModelActionTextForTest == "加载", uiForm.ModelActionTextForTest);
                }
                // DEC-138（E-7）：同上，跳过要留断言（这一族一共四处，形状一致 = 少跑必须看得见）
                else Check("DEC-138（E-7）用例前提：llama.cpp 侧至少有一件可用（没有 = 上面那一整块切页断言全没跑）",
                           false, "LlamaModelsForTest 里没有 Ready 的那件");

                // --- B 段地基：模块停用 = 全隐身（模型列表 / 下拉；探活与起停闸门见沙箱端到端） ---
                int strataCount = uiForm.ModelsForTest.Count;
                uiForm.SetModuleEnabledForTest(strata: true, llama: false);
                Check("隐身：llama 停用后模型列表与下拉里都没有 llama.cpp",
                    uiForm.LlamaModelsForTest.Count == 0 && !uiForm.ModelDropTextsForTest.Any(t => llamaDisplays.Contains(t)),
                    "drop=" + string.Join(";", uiForm.ModelDropTextsForTest.Take(3)));
                Check("隐身：llama 停用不影响 Strata 侧", uiForm.ModelsForTest.Count == strataCount);
                uiForm.SetModuleEnabledForTest(strata: false, llama: true);
                Check("隐身：Strata 停用后激活页落回 llama.cpp、模型列表清空",
                    uiForm.ModelsForTest.Count == 0 && uiForm.ActiveModuleForTest == "llama"
                    && !uiForm.ModelDropTextsForTest.Any(t => strataDisplays.Contains(t)),
                    "active=" + uiForm.ActiveModuleForTest);
                uiForm.SetModuleEnabledForTest(strata: true, llama: true);      // 还原，别影响后面的用例
                uiForm.SwitchModuleForTest("strata");
                Check("还原：两个模块都启用后 Strata 侧模型回来", uiForm.ModelsForTest.Count == strataCount, uiForm.ModelsForTest.Count + " vs " + strataCount);

                // --- 第 0 轮 DEC-094：模块表本身的可穷举性（加一个模块 = 这里自动多三条，不用回来改用例）---
                Check("模块表：行数与 ModuleRegistry 一致、键唯一、顺序就是界面顺序",
                    uiForm.ViewsForTest.Count == ModuleRegistry.All.Length
                    && uiForm.ViewsForTest.Select(v => v.Key).Distinct().Count() == uiForm.ViewsForTest.Count
                    && uiForm.ViewsForTest.Select(v => v.Key).SequenceEqual(ModuleRegistry.All.Select(m => m.Key)),
                    string.Join(" / ", uiForm.ViewsForTest.Select(v => v.Key)));
                foreach (var v in uiForm.ViewsForTest)
                {
                    // ① 关它之前要先停的东西：每模块自己说一句话，而且要点自己的名（校验不会替它编）
                    // 其余模块照旧"启用中"，否则先命中"至少要启用一个"那条
                    string msg = uiForm.SwitchOffMessageForTest(v.Key);
                    var replies = uiForm.ViewsForTest.Select(o => new MainForm.SwitchReply(
                        o.DisplayName, o.Key == v.Key, o.Key == v.Key, o.SwitchOffMessage)).ToList();
                    Check($"模块表：停用 {v.DisplayName} 且它还在跑 -> 拦下的那句话点名它并说怎么停",
                        msg.Contains(v.DisplayName) && msg.Contains("停")
                        && MainForm.ModuleSwitchAtSave(replies) == msg, msg);
                    // ② 此刻没有进程，"壳自己拉的东西在跑"必须一律 false（不许误报，否则关模块会被莫名拦下）
                    Check($"模块表：{v.DisplayName} 的「壳在跑」判据此刻为 false", !uiForm.ShellBusyForTest(v.Key), v.Key);
                    // ③ 自动启动一格只动自己那一格（两模块时代最容易共用的那一位）
                    var sAuto = new Settings();
                    v.SetAutoStart(sAuto, true);
                    Check($"模块表：勾 {v.DisplayName} 的自动启动只写自己那格（别的模块那格不受牵连）",
                        v.AutoStart(sAuto) && uiForm.ViewsForTest.Where(o => o.Key != v.Key).All(o => !o.AutoStart(sAuto)), v.Key);
                    // ④ 卡1 那两格（监听端口 / 思考深度）与 KV 格、高级区都得是**这个模块自己的控件**，
                    //    而且**真的在控件树里**（DEC-096 出图抓到的：_cboKvNinfer 没被 Add 进卡2 → 那一格画不出来，
                    //    "互不共用"这条照样通过，所以判据必须带 Parent）
                    Check($"模块表：{v.DisplayName} 的卡1/卡2/高级区控件齐全、互不共用、且都挂进了界面",
                        v.PortBox() is not null && v.ReasonBox() is not null && v.KvBox() is not null && v.AdvPanel() is not null
                        && v.PortBox().Parent is not null && v.ReasonBox().Parent is not null
                        && v.KvBox().Parent is not null && v.AdvPanel().Parent is not null
                        && (v.BackendBox is null || v.BackendBox.Parent is not null)
                        && uiForm.ViewsForTest.Where(o => o.Key != v.Key).All(o =>
                            !ReferenceEquals(o.PortBox(), v.PortBox()) && !ReferenceEquals(o.ReasonBox(), v.ReasonBox())
                            && !ReferenceEquals(o.KvBox(), v.KvBox()) && !ReferenceEquals(o.AdvPanel(), v.AdvPanel())),
                        $"port父={v.PortBox()?.Parent?.GetType().Name} kv父={v.KvBox()?.Parent?.GetType().Name} " +
                        $"adv父={v.AdvPanel()?.Parent?.GetType().Name} 方块父={v.BackendBox?.Parent?.GetType().Name ?? "无这一格"}");
                    // ⑤ 日志通道与家底落点各归各模块（写到别人的通道里 = 两个页面互相串，DEC-047 的老账）
                    Check($"模块表：{v.DisplayName} 的日志通道键就是它自己", v.LogKey == v.Key, "logKey=" + v.LogKey);
                }
                Check("模块表：只有挂了网关 hooks 的模块才会立壳网关（DEC-096 起 = Strata 与 Ninfer；llama 的路由由引擎自己做）",
                    uiForm.ViewsForTest.Where(v => v.Gateway is not null).Select(v => v.Key).SequenceEqual(new[] { "strata", "ninfer" })
                    && uiForm.ViewsForTest.First(v => v.Key == "llama").Gateway is null,
                    string.Join(" / ", uiForm.ViewsForTest.Select(v => v.Key + (v.Gateway is not null ? "有" : "无"))));
                // 没挂 hooks 的模块点网关这一条路必须**收手**（接第二个模块时最容易踩的坑就是顺手多立一个监听）
                uiForm.StartGatewayForTest("llama");
                Check("模块表：不挂 hooks 的模块点不动壳网关（一个监听都不立，也不报错）",
                    uiForm.GatewayCountForTest == 0 && !uiForm.GatewayUpForTest("llama"), "现在有几份=" + uiForm.GatewayCountForTest);
                // /v1/models 的 owned_by 参数化（单例网关时代这里写死 "strata"；JSON 是紧凑格式，别按带空格的写法断）
                var oneModel = new[] { new ModelEntry("t1", "p", "", true, "") };
                string jN = MainForm.BuildModelsJson(oneModel, "ninfer");
                string jS = MainForm.BuildModelsJson(oneModel);
                Check("网关：/v1/models 的 owned_by 由模块那一行给（不再写死 strata）",
                    jN.Contains("ninfer") && !jN.Contains("strata") && jS.Contains("strata"), jN);

                // --- B 段阶段③：日志分流（文件按类型；界面按激活模块；清屏按页） ---
                uiForm.LogForTest("测试：启动器行", "launcher");
                uiForm.LogForTest("测试：strata 行", "strata");
                uiForm.LogForTest("测试：llama 行", "llama");
                Check("日志分流：Strata 页只显示 启动器 + strata 行",
                    uiForm.LogBoxTextForTest.Contains("测试：启动器行") && uiForm.LogBoxTextForTest.Contains("测试：strata 行")
                    && !uiForm.LogBoxTextForTest.Contains("测试：llama 行"));
                uiForm.SwitchModuleForTest("llama");
                Check("日志分流：切到 llama 页显示 启动器 + llama 行（未激活页后台照收）",
                    uiForm.LogBoxTextForTest.Contains("测试：启动器行") && uiForm.LogBoxTextForTest.Contains("测试：llama 行")
                    && !uiForm.LogBoxTextForTest.Contains("测试：strata 行"));
                uiForm.ClearActiveLogForTest();
                Check("清屏按页：清掉 llama 页后本页无测试行", !uiForm.LogBoxTextForTest.Contains("测试："));
                uiForm.SwitchModuleForTest("strata");
                Check("清屏按页：切回 Strata 页历史仍在（不受 llama 页清屏影响）",
                    uiForm.LogBoxTextForTest.Contains("测试：strata 行"));
                uiForm.ClearActiveLogForTest();      // 清掉测试行，别影响后续观测
                uiForm.SwitchModuleForTest("strata");

                // --- ⑥⑦（2026-10-03 用户反馈）：切页不写日志 + 壳自己写的话也要标模块归属 ---
                // ⑦ 的真因：分流规则一直是「标了模块的行只在本模块页显示，launcher 行两页都显示」，
                // 但壳自己写的那些话几乎全用默认 launcher，于是 llama 页看到 Strata 的扫描/网关/更新，
                // Strata 页反过来也看到 llama 的。这里走**真的扫描入口** RefreshModels 断言两侧都不再串。
                int histBeforeSwitch = uiForm.LogHistoryCountForTest;
                uiForm.SwitchModuleForTest("llama");
                Check("⑥ 切模块页不在日志里留痕（历史行数不变、也没有「已切到」行）",
                    uiForm.LogHistoryCountForTest == histBeforeSwitch && !uiForm.LogBoxTextForTest.Contains("已切到"),
                    "history " + histBeforeSwitch + " -> " + uiForm.LogHistoryCountForTest);
                uiForm.RefreshModelsForTest();
                Check("⑦ llama 页只看到 llama 的扫描行（不再混进 Strata 的）",
                    uiForm.LogBoxTextForTest.Contains("llama.cpp 模型") && !uiForm.LogBoxTextForTest.Contains("Strata 模型配置"),
                    uiForm.LogBoxTextForTest.Replace("\r\n", " ⏎ ").Replace("\n", " ⏎ "));
                uiForm.SwitchModuleForTest("strata");
                Check("⑦ Strata 页反过来也一样（不再混进 llama 的扫描行）",
                    uiForm.LogBoxTextForTest.Contains("Strata 模型配置") && !uiForm.LogBoxTextForTest.Contains("llama.cpp 模型"),
                    uiForm.LogBoxTextForTest.Replace("\r\n", " ⏎ ").Replace("\n", " ⏎ "));
                uiForm.ClearActiveLogForTest();      // 清掉本轮扫描行，别影响后面的观测
                uiForm.SwitchModuleForTest("strata");

                // ============ DEC-097⑤：只重扫一个模块时，别在另两家的账上留扫描行 ============
                // 用户实测：切 WSL / WIN 后端会刷新模型（本来正常），但那一次点击把 Strata 与 llama 的
                // 扫描行一起写进了它们的历史与 logs\*.log —— 切到那两页就像自己刚扫过一遍。
                uiForm.SwitchModuleForTest("ninfer");
                int sB = uiForm.LogHistoryCountWhereForTest("strata", "已扫描"), lB = uiForm.LogHistoryCountWhereForTest("llama", "已解析");
                int nB = uiForm.LogHistoryCountWhereForTest("ninfer", "已解析");
                uiForm.RefreshModelsForTest("ninfer");
                Check("DEC-097⑤ 作用域重扫只给 Ninfer 记扫描行，strata / llama 的账上一条都不多",
                    uiForm.LogHistoryCountWhereForTest("strata", "已扫描") == sB
                    && uiForm.LogHistoryCountWhereForTest("llama", "已解析") == lB
                    && uiForm.LogHistoryCountWhereForTest("ninfer", "已解析") > nB,
                    $"strata {sB}->{uiForm.LogHistoryCountWhereForTest("strata", "已扫描")} " +
                    $"llama {lB}->{uiForm.LogHistoryCountWhereForTest("llama", "已解析")} " +
                    $"ninfer {nB}->{uiForm.LogHistoryCountWhereForTest("ninfer", "已解析")}");
                uiForm.RefreshModelsForTest();
                Check("DEC-097⑤ 全量重扫照旧三家各记（启动 / SETUP / Strata 升级那三个时点没被削掉）",
                    uiForm.LogHistoryCountWhereForTest("strata", "已扫描") > sB && uiForm.LogHistoryCountWhereForTest("llama", "已解析") > lB,
                    $"strata {sB}->{uiForm.LogHistoryCountWhereForTest("strata", "已扫描")} " +
                    $"llama {lB}->{uiForm.LogHistoryCountWhereForTest("llama", "已解析")}");
                uiForm.ClearActiveLogForTest();
                uiForm.SwitchModuleForTest("strata");

                // ============ DEC-097①：家底缺失要在界面上看得见，不只是启动时那一行日志 ============
                // 用户实测：ninfer_models.ini 没到位 → 界面一片空白、下拉没东西、也不知道缺的是什么。
                // 补种归 deploy.ps1（实跑目录上面没有仓库，壳那侧往上找 config\ 永远找不到），
                // 这里验的是"真缺的时候界面要说得出缺哪一份、放哪儿"。
                {
                    var homeBak = Path.GetDirectoryName(MainForm.NinferIniPath())!;
                    var emptyHome = MainForm.Scratch("selftest-ninfer-empty");
                    try { Directory.CreateDirectory(emptyHome); } catch { /* 建不出来就跳过这一条 */ }
                    MainForm.UseTempNinferHomeForTest(emptyHome);
                    uiForm.SwitchModuleForTest("ninfer");
                    uiForm.RefreshModelsForTest("ninfer");
                    Check("DEC-097① 家底缺失时 Ninfer 页那一行提示说得出文件名与落点（不只活在日志里）",
                        uiForm.HintForTest.Contains(MainForm.NinferIniFileName)
                        && uiForm.HintForTest.Contains(emptyHome)
                        && uiForm.HintForTest == MainForm.NinferHomeMissingText(MainForm.NinferIniPath()),
                        uiForm.HintForTest);
                    uiForm.SwitchModuleForTest("strata");
                    uiForm.RefreshModelsForTest();       // 另两页不该看到 Ninfer 那句缺文件提示
                    Check("DEC-097① 那句提示只在 Ninfer 页出面（Strata 页的提示行不受它影响）",
                        !uiForm.HintForTest.Contains(MainForm.NinferIniFileName), uiForm.HintForTest);
                    MainForm.UseTempNinferHomeForTest(homeBak);
                    uiForm.SwitchModuleForTest("ninfer");
                    uiForm.RefreshModelsForTest("ninfer");
                    uiForm.ClearActiveLogForTest();
                    uiForm.SwitchModuleForTest("strata");
                }

                // ============ DEC-156：Ninfer 那格占用估算 —— 池与精度按**这一段自己**的键取；量不到就不显示 ============
                {
                    var sizeHome = MainForm.Scratch("selftest-ninfer-size");
                    var homeBak2 = Path.GetDirectoryName(MainForm.NinferIniPath())!;
                    MainForm.TryDeleteTree(sizeHome);
                    Directory.CreateDirectory(sizeHome);
                    var fakeWin = Path.Combine(sizeHome, "fake.ninfer");
                    File.WriteAllBytes(fakeWin, new byte[1024 * 1024]);          // 1 MiB 的小件：只用来量字节数
                    File.WriteAllText(Path.Combine(sizeHome, MainForm.NinferIniFileName),
                        "[fake-win]\nbackend = win\nfile = " + fakeWin + "\nkv-capacity = 17920\nkv-dtype = k8v4\n" +
                        "\n[fake-wsl]\nbackend = wsl\nfile = /vm/fake.ninfer\nkv-capacity = 262144\nkv-dtype = rk4v4-e8\n");
                    MainForm.UseTempNinferHomeForTest(sizeHome);
                    var needWin = uiForm.NinferNeedGiBFor("fake-win");
                    var needWsl = uiForm.NinferNeedGiBFor("fake-wsl");
                    Check("DEC-156 占用估算：件字节 + **段自己那枚** kv-capacity（1 MiB 件 + 17,920 → ≈2.3 GiB；按界面上那格算会给 7.0）；WSL 没扫到字节数 = 空着不显示、这条也不判",
                        needWin is > 2.25 and < 2.35 && needWsl is null,
                        $"win={needWin} wsl={needWsl}");
                    MainForm.UseTempNinferHomeForTest(homeBak2);
                    uiForm.RefreshModelsForTest("ninfer");
                }

                // --- 本轮反馈：模型卡→日志卡间距与其他卡片一致（提示行空着时不占位） ---
                var (g12c, g23c) = uiForm.CardGapsForTest();
                var gapML = uiForm.ModelToLogGapForTest;
                W($"模型卡→日志卡间距 = {gapML}px（card2→3 = {g23c}px；当前页={uiForm.ActiveModuleForTest} 提示行=[{uiForm.HintForTest}]）");
                Check("模型卡→日志卡间距与其他卡片一致（无提示时）",
                    gapML == g23c && gapML == UiTheme.S(uiForm, 12), $"model→log={gapML} card2→3={g23c}");

    }
}
