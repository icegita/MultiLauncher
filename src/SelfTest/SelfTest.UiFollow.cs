namespace MultiLauncher;

// 界面排布组（后段）：DEC-098③ 换后端高级区格子名 / DEC-100①b 卡3 按模块表派发 / DEC-125 四条界面批复 /
// DEC-155 跟模型与待办 / 状态栏与日志区那一族。2026-10-15 从 SelfTest.UiLayout.cs 按子域切出，块内一行未改；
// 共享状态（uiForm 等）见 SelfTest.State.cs。
internal static partial class SelfTest
{
    internal static void UiFollowChecks()
    {
                // --- 本轮反馈：高级展开不撑窗口、临时侵占日志区、折叠归还 ---
                var winHBefore = uiForm.Height;
                var logHBefore = uiForm.LogCardHeightForTest;
                uiForm.ToggleAdvancedForTest();
                var winHAfter = uiForm.Height;
                var logHAfter = uiForm.LogCardHeightForTest;
                var (barE, boxE) = uiForm.LogScrollGeomForTest;
                // 用户 2026-10-04 第 3 条：高级区的开关"看着偏高"——量出来才算核过（展开态下量，
                // 判据 = 开关与它自己的标题都落在这一行行带中心的 ±1px 内）。
                var advAlign = uiForm.AdvToggleAlignmentForTest();
                W("高级区开关摆位（开关−标题 / 开关−行带 / 标题−行带，px）：" +
                  string.Join(" ｜ ", advAlign.Select(a => $"{a.Name}={a.SwMinusLabel}/{a.SwMinusBand}/{a.LabelMinusBand}")));
                Check("高级区开关与标题都在行带里垂直居中（第 3 条：原先固定 Top、150% 下开关比行带中心高约 6px）",
                    // 数 = Strata 2（KV 驻留 / HEAD_LATE）+ llama 1（flash-attn）+ Ninfer 当前后端出面的那些
                    //（草稿头模式两侧都出面；视觉/保留思考是 WSL 那一侧的格，不出面就不该进这份测量）
                    // DEC-097：这个数**按当前后端算**，不写死 4 —— 用户机器上 WSL 后端开着 = 6 枚，
                    // 写死 4 会在他的实跑目录上假红（本轮就是这么发现的）。
                    advAlign.Count == (uiForm.NinferBackendForTest == NinferIni.BackendWsl ? 6 : 4)
                    && advAlign.All(a =>
                        Math.Abs(a.SwMinusBand) <= 1 && Math.Abs(a.LabelMinusBand) <= 1 && Math.Abs(a.SwMinusLabel) <= 2),
                    string.Join(" ｜ ", advAlign.Select(a => $"{a.Name}={a.SwMinusLabel}/{a.SwMinusBand}/{a.LabelMinusBand}")));
                // ============ DEC-098③：换两次后端再读一遍高级区，格子名字不能丢 ============
                // 用户实跑："高级里,大量参数没有参数名称"。根因 = AdvLayout 把不出面的格子（控件 + 标签）挪去 -2000
                // 并设 Visible=false，而回到明面上时**只把控件设回 true、标签漏了**。两侧都启用时这一条才走得到
                //（开发目录那份是他的真实配置，NinferWslOn=True）；只启用一侧时两次交换都是空操作，
                // 这条退化成"当前出面的格子都带名字"，仍然有效。
                {
                    var backendBak = uiForm.NinferBackendForTest;
                    uiForm.SwitchModuleForTest("ninfer");
                    // 判据用「在不在画外」而不是 Visible：无头跑时控件的 Visible 是含祖先的有效可见性，
                    // 整棵树一律报 false —— 这条自检第一版就是拿它当判据，结果成了一条永真的空判据（假绿）。
                    int PlacedNinfer() => uiForm.AdvLabelStatesForTest().Count(x => x.Panel == "ninfer" && x.Placed);
                    var placedBefore = PlacedNinfer();
                    // DEC-103（BUG-049）：这两击是**真动作** —— OnNinferBackendSwap 会把 [Current].NinferBackend
                    // 落盘（DEC-087 的口径），所以必须先改道 data\tmp\ 的副本（口径同上面保存闸门那两块）。
                    // 原来没改道：每次自检都把整份真实配置写一遍，还把保存闸门用例的测试值带进用户家底
                    //（他的 iq3_s 上下文被写成 147456 就是这条干的）。
                    var iniReal3 = MainForm.ConfigPathForTest;
                    var iniTmp3 = MainForm.Scratch("selftest-ninfer-swap.ini");
                    if (File.Exists(iniReal3)) File.Copy(iniReal3, iniTmp3, overwrite: true);
                    MainForm.UseTempConfigForTest(iniTmp3);
                    try
                    {
                        uiForm.SwapNinferBackendForTest();                   // → 另一代（格子该换一套）
                        var placedMid = PlacedNinfer();
                        uiForm.SwapNinferBackendForTest();                   // → 换回来（名字不能丢）
                        // 覆盖判据：这一击照旧真落盘、只是落在副本上（别把"写"整没了 = 假过）
                        Check("DEC-103 切后端那一击照旧真落盘、只是落在副本上（副本里 NinferBackend 在场）",
                            File.Exists(iniTmp3) && File.ReadAllText(iniTmp3).Contains("NinferBackend=" + uiForm.NinferBackendForTest),
                            Path.GetFileName(iniTmp3) + "｜后端 " + uiForm.NinferBackendForTest);
                        var lblStates = uiForm.AdvLabelStatesForTest();
                        var lostNames = lblStates.Where(x => x.Placed && !x.LbShown).Select(x => x.Panel + ":" + x.Name).ToList();
                        Check("DEC-098③ 高级区每个当面面的格子都带着名字（换过两次后端之后；漏标签 = 这条会红）",
                            lblStates.Count(x => x.Placed) >= 10 && lostNames.Count == 0
                            && lblStates.Count(x => x.Panel == "ninfer" && x.Placed) >= 6      // 这一页真被量到了
                            && placedMid != placedBefore                                       // 中间那一趟确实重排过
                            && PlacedNinfer() == placedBefore,                                 // 换回来 = 回到原来那一套
                            "当面面=" + lblStates.Count(x => x.Placed) + "｜ninfer 当面 " + placedBefore
                            + "→" + placedMid + "→" + PlacedNinfer()
                            + "｜后端 " + backendBak + "→" + uiForm.NinferBackendForTest
                            + "｜丢了名字的：" + string.Join("、", lostNames));
                    }
                    finally { MainForm.UseTempConfigForTest(iniReal3); }
                    uiForm.SwitchModuleForTest("strata");
                }
                // ============ DEC-100①b：卡3「加载/卸载」与「启动」都按模块表派发 ============
                // 原来 Program.ModelAction 是两模块时代的形状（`if (backend == llama) …` 之后整段是 Strata 流程），
                // Ninfer 的模型进来会去拉 **Strata** 引擎 —— 与 BUG-039 同族。断"委托挂在哪个方法上"，
                // 不真点（真点要绑公开口、要把 14 GB 的件读进显存，那归用户实跑）。
                {
                    var tgtStrata = uiForm.ServiceActionTargetsForTest("strata");
                    var tgtLlama = uiForm.ServiceActionTargetsForTest("llama");
                    var tgtNinfer = uiForm.ServiceActionTargetsForTest("ninfer");
                    Check("DEC-100① 三行的「启动」（含自动启动那一条）与「加载」各挂各的方法 —— Ninfer 那格 = 只立网关，不是起引擎",
                        tgtStrata == ("StartServer", "StrataModelAction")
                        && tgtLlama == ("StartLlamaServer", "LlamaModelAction")
                        && tgtNinfer == ("StartNinferGateway", "NinferModelAction"),
                        "strata=" + tgtStrata + "｜llama=" + tgtLlama + "｜ninfer=" + tgtNinfer);
                    Check("DEC-100①b 三个「加载/卸载」互不共用（Ninfer 曾落进 Strata 那一支 = 会去拉 Strata 引擎）",
                        new[] { tgtStrata.ModelTarget, tgtLlama.ModelTarget, tgtNinfer.ModelTarget }.Distinct().Count() == 3,
                        string.Join("、", tgtStrata.ModelTarget, tgtLlama.ModelTarget, tgtNinfer.ModelTarget));
                    // DEC-100②：那行灯说明放得进行宽吗（DEC-107 起左栏内宽 200 = 他拍板"+10%、就近取整"，
                    // 于是"够不够"要按新行宽重算 + 「模型」二字加回来之后也要重新量）。
                    // 喂的是五位端口（18082）= 最坏情况。
                    var fits = uiForm.LightRowFitForTest();
                    W("模块状态灯行宽（可用 " + (fits.Count > 0 ? fits[0].Room : 0) + "px）：" +
                      string.Join(" ｜ ", fits.Select(f => $"{f.Module}:{f.Need}『{f.Text}』")));
                    var over = fits.Where(f => f.Need > f.Room).Select(f => $"{f.Module}『{f.Text}』需 {f.Need} > 可用 {f.Room}").ToList();
                    Check("DEC-107 「待机 · 模型未加载」「运行中 · 模型已加载」这两句实测放得进行宽（加回「模型」后他点名的就是这两句）",
                        fits.Count(f => f.Text.Contains("加载")) >= 3
                        && fits.Where(f => f.Text.Contains("加载")).All(f => f.Need <= f.Room),
                        string.Join("；", fits.Where(f => f.Text.Contains("加载"))
                            .Select(f => $"『{f.Text}』需 {f.Need} / 可用 {f.Room}")));
                    // 还差一点的那两句 = 登记在案的欠账（旧行宽 S(162) 下五位端口才超）。
                    // 判据写成"超出的只许是这两句"：多出一句新的就红，不是一条永久豁免。
                    // DEC-107 加宽到 S(180) 后连这两句也该放得下了 —— 留着这条上限，别指望豁免。
                    var owed = new[] { "运行中 · 路由模式", "运行中 · 外部服务" };
                    // 候选写法先量出来（改哪一句等他拍，别拿估的宽当依据）；末尾两条是四位端口 = 他机器上的真实形状
                    var cands = new[]
                    {
                        "运行中 · 路由模式 · 端口 18082", "运行中 · 路由 · 端口 18082", "运行中 · 单体 · 端口 18082",
                        "运行中（外部服务） · 端口 18082", "运行中 · 外部服务 · 端口 18082", "外部在跑 · 端口 18082",
                        "运行中 · 路由模式 · 端口 8068", "运行中 · 外部服务 · 端口 8068",
                    };
                    W("灯行候选宽：" + string.Join(" ｜ ", cands.Select(c =>
                    {
                        var (need, room) = uiForm.LightTextWidthForTest(c);
                        return $"{c} = {need}{(need > room ? "（超）" : "")}";
                    })));
                    Check("DEC-100② 外部服务那句两页一字不差、且括号已换成点号（他提的写法）",
                        MainForm.StrataLight(false, false, false, true, false, false, 8068).desc
                            == MainForm.NinferLight(false, false, false, true, false, 8068).Desc
                        && MainForm.NinferLight(false, false, false, true, false, 8068).Desc
                            == "运行中 · 外部服务 · 端口 8068"
                        && !MainForm.NinferLight(false, false, false, true, false, 8068).Desc.Contains("（"),
                        MainForm.NinferLight(false, false, false, true, false, 8068).Desc);
                    // DEC-107：加宽之后连"登记在案"的那两句（五位端口）也不该再超 —— 这条是收紧，不是豁免
                    Check("DEC-107 加宽后九句全放得下（五位端口最坏情况与登记过的那两句；196 差 1px，故取 200）",
                        over.Count == 0, string.Join("；", over));
                    var overTexts = over.Select(o => o.Split('『')[1].Split('』')[0]).Distinct().ToList();
                    Check("DEC-100② 超出行宽的只有登记在案那两句（新增一句超宽 = 这条会红；等用户拍文案）",
                        fits.Count >= 9 && overTexts.Count <= owed.Length
                        && overTexts.All(t => owed.Any(k => t.StartsWith(k, StringComparison.Ordinal))),
                        string.Join("；", over));

                    // ============ 2026-10-15（他点名）：左栏「模块状态」的折叠态 ============
                    // 展开 = 现状（每模块一行）；折叠 = 一行横排「灯 + 简称」+ 悬浮出完整状态；状态记住上次。
                    uiForm.SetLightsCollapsedForTest(false);
                    var lx = uiForm.LightsStateForTest;
                    uiForm.SetLightsCollapsedForTest(true);
                    var lc = uiForm.LightsStateForTest;
                    Check("折叠态：块高按折叠那一套摆（展开 = S(36 + 3×46)，折叠 = S(54)）",
                        !lx.Collapsed && lx.BlockH == UiTheme.S(uiForm, 36 + 3 * 46)
                        && lc.Collapsed && lc.BlockH == UiTheme.S(uiForm, 54),
                        $"展开 h={lx.BlockH}（期望 {UiTheme.S(uiForm, 36 + 3 * 46)}）/ 折叠 h={lc.BlockH}（期望 {UiTheme.S(uiForm, 54)}）");
                    Check("折叠按钮箭头：展开 ▾ / 折叠 ◂（与卡2「高级」同一套方向）",
                        lx.ToggleText == "▾" && lc.ToggleText == "◂", lx.ToggleText + " / " + lc.ToggleText);
                    var strip = uiForm.LightStripForTest;
                    var stripFit = uiForm.LightStripFitForTest;
                    W($"折叠态横排：需 {stripFit.Need}px / 可用 {stripFit.Room}px —— " +
                      string.Join(" ｜ ", strip.Select(s => s.Short + "『" + s.Tip + "』")));
                    Check("折叠态三个简称横排放得进卡片内宽（量出来的数，不靠估）",
                        strip.Count == 3 && stripFit.Need <= stripFit.Room, stripFit.Need + " / " + stripFit.Room);
                    var dispOf = ModuleRegistry.All.ToDictionary(m => m.Key, m => m.DisplayName);
                    Check("折叠态简称 = Strata / llama / Ninfer（llama.cpp 全称横排放不下）",
                        uiForm.ShortNameForTest(MainForm.StrataKey) == "Strata"
                        && uiForm.ShortNameForTest(MainForm.LlamaKey) == "llama"
                        && uiForm.ShortNameForTest(MainForm.NinferKey) == "Ninfer",
                        string.Join(" / ", strip.Select(s => s.Short)));
                    Check("折叠态 tooltip = 「全称 · 完整状态（含端口）」",
                        strip.All(s => s.Tip.StartsWith(dispOf[s.Key] + " · ", StringComparison.Ordinal) && s.Tip.Contains("端口")),
                        string.Join(" ｜ ", strip.Select(s => s.Tip)));
                    // ---- DEC-177（他实跑点名三条：箭头不要底、灯与文字没对齐、简称被切）----
                    Check("折叠箭头 = 裸字形（Bare：不打底不描边；命中区仍是整块控件，他说点击范围可以保留）",
                        lc.ToggleBare, "Bare=" + lc.ToggleBare);
                    var geoms = uiForm.LightGeometryForTest;      // 展开 3 行 + 折叠 3 枚，同一套摆法
                    var skew = geoms.Where(g => g.DotTop + g.DotH / 2 != g.TextTop + g.TextLineH / 2
                                                || g.DotTop < 0 || g.DotTop + g.DotH > g.RowH).ToList();
                    Check("两种态的灯心都压在文字墨迹中心那一档（= 行盒中线，实量差 ≤0.5px）且灯整块在行内",
                        geoms.Count == 6 && skew.Count == 0,
                        string.Join("；", skew.Select(g => $"{g.Where}:{g.Key} 灯心 {g.DotTop + g.DotH / 2} / 文字行盒中线 {g.TextTop + g.TextLineH / 2}（两数必须相等）")));
                    var cut = geoms.Where(g => g.BoxW < g.TextNeedW).ToList();
                    Check("每枚文字要多少宽就给多少（盒子右缘不得切字；NoPadding 量字比 Label 自身窄 = 切尾巴那个坑）",
                        cut.Count == 0 && geoms.All(g => g.TextNeedW > 0),
                        string.Join("；", cut.Select(g => $"{g.Where}:{g.Key} 盒 {g.BoxW} < 需 {g.TextNeedW}")));
                    W("灯的几何：" + string.Join(" ｜ ", geoms.Select(g =>
                        $"{g.Where}:{g.Key} 行高 {g.RowH} 灯 {g.DotTop}+{g.DotH} 文字顶 {g.TextTop} 行盒 {g.TextLineH} 盒宽 {g.BoxW}/需 {g.TextNeedW}")));
                    // ---- DEC-178（他第二轮实跑点名四条）----
                    var tips = uiForm.LightStripTipsForTest;
                    Check("折叠态提示三处同一句（盒 / 灯 / 简称各挂一份：子窗口各有 HWND，只挂父盒 = 落在字上不弹）",
                        tips.Count == 3 && tips.All(t => t.AllOneLine && t.Dot == t.Box && t.Name == t.Box
                                                        && t.Box.StartsWith(dispOf[t.Key] + " · ", StringComparison.Ordinal)),
                        string.Join(" ｜ ", tips.Select(t => $"{t.Key} 盒『{t.Box}』灯『{t.Dot}』字『{t.Name}』单行={t.AllOneLine}")));
                    var ltC = uiForm.LightToggleForTest;        // 此刻 = 折叠态
                    // 上面那条"单行=True"在自检这一刻量的是「已停止 · 端口 8068」那一句（21 字 < 28 = 两条路都不折），
                    // 判不出走的是哪条 —— 所以单行档本身另立一条：拿一句 29 字（超 28 那一档）分别走两条路，比落进去的成品。
                    var wBox = new Label(); var oBox = new Label();
                    var longTip = new string('测', 29);
                    Tip.Set(wBox, longTip); Tip.SetOneLine(oBox, longTip);
                    Check("Tip 的单行档真不折行（同一句 29 字：Set 折两行 / SetOneLine 保持一行；状态行那一类走后者）",
                        Tip.RenderedOf(oBox) == longTip && Tip.RenderedOf(wBox).Contains('\n'),
                        $"折行版 {Tip.RenderedOf(wBox).Split('\n').Length} 行 ｜ 单行版 {Tip.RenderedOf(oBox).Length} 字一行");
                    wBox.Dispose(); oBox.Dispose();
                    uiForm.SetLightsCollapsedForTest(false);      // 还原成展开（后面的用例认的是展开态）
                    var ltE = uiForm.LightToggleForTest;
                    Check("折叠箭头：不挂 tooltip（他点名「不需要」）+ 不打底 + 与卡2「高级」那枚同源（同字号、同字色、同方向口径）",
                        ltC.Tip.Length == 0 && ltE.Tip.Length == 0 && ltC.Bare && ltC.SameFontAsAdv && ltC.SameInkAsAdv
                        && ltE.SameFontAsAdv && ltE.SameInkAsAdv && ltC.Text == "◂" && ltE.Text == "▾",
                        $"tip『{ltC.Tip}』Bare={ltC.Bare} 同字号={ltC.SameFontAsAdv} 同字色={ltC.SameInkAsAdv} 折叠『{ltC.Text}』/ 展开『{ltE.Text}』");
                    Check("折叠态收在 [Current]（他明确「记住上次」）：写出含 LightsCollapsed=True",
                        MainForm.BuildConfigLines(new Settings { LightsCollapsed = true }, new Dictionary<string, Settings>(),
                            new MainForm.WindowRect(0, 0, 100, 100, false)).Contains("LightsCollapsed=True"));
                    var lBack = new Settings();
                    MainForm.ParseConfigLines(new[] { "[Current]", "LightsCollapsed=True" }, lBack,
                        new Dictionary<string, Settings>(), new MainForm.ConfigParseResult());
                    Check("折叠态读得回来（读侧；默认 False = 展开）",
                        lBack.LightsCollapsed && new Settings().LightsCollapsed == false);
                }
                uiForm.ToggleAdvancedForTest();   // 折叠归还
                var logHBack = uiForm.LogCardHeightForTest;
                var (barC, boxC) = uiForm.LogScrollGeomForTest;
                W($"高级展开：窗口 {winHBefore}->{winHAfter}，日志区 {logHBefore}->{logHAfter}->{logHBack}");
                Check("高级展开不撑窗口、临时侵占日志区、折叠归还",
                    winHAfter == winHBefore && logHAfter < logHBefore && logHBack == logHBefore,
                    $"{winHBefore}->{winHAfter}, log {logHBefore}->{logHAfter}->{logHBack}");
                // 2026-10-02 反馈（BUG-026）：原来条高滞后一态（折叠后残留 0 → 自绘条消失、露出原生黑滚动条）
                Check("展开/折叠后自绘滚动条跟住日志框（BUG-026：条高不滞后一态）",
                    boxE.Top == barE.Top && boxE.Height == barE.Height
                    && boxC.Top == barC.Top && boxC.Height == barC.Height,
                    $"expand bar={barE} box={boxE}；collapse bar={barC} box={boxC}");
                // ④ 底部状态栏（DEC-038/039）：没服务在跑时整条不出现；给到值就按"平均|当前"三段右对齐渲染
                uiForm.SetShowStatusBarForTest(false);
                uiForm.RefreshStatusBarForTest();
                Check("开关关 -> 条区收起、底留白回 16（区域与文字一起收）",
                    Row("statusbar").StartsWith("statusbar: 隐藏", StringComparison.Ordinal)
                    && uiForm.Padding.Bottom == UiTheme.S(uiForm, 16), Row("statusbar") + " band=" + uiForm.Padding.Bottom);
                uiForm.SetShowStatusBarForTest(true);
                // 这两条断的是**默认那三档 + 两档平均**的形状；可"勾了哪几档 / 哪几档带平均"是**他的设置**（DEC-126 推送实测抓到：
                // 他在 v1.5.7 上把「平均」全取消、又勾上缓存命中与输入·输出 token，实跑目录这条就红 = 1 条 FAIL，
                // 开发目录绿 —— 用例不许靠"用户 ini 里恰好有什么"，先把样本摆进去再断）。
                uiForm.SetStatusBarItemsForTest("prefill,decode,mtp", "prefill,decode");
                uiForm.RefreshStatusBarForTest();
                Check("开关开、服务还没跑 -> 条区**立刻**在（底留白 24）+ 文字全是占位短横（不再等出数据才出现）"
                      + "（DEC-127①：没数值的档**不写单位**，所以是「预填充 -」不是「预填充 -|- t/s」）",
                    uiForm.StatusBarIntentForTest && uiForm.Padding.Bottom == UiTheme.S(uiForm, 24)
                    && uiForm.StatusBarTextForTest == "预填充 -·解码 -·MTP 接受率 -",
                    $"intent={uiForm.StatusBarIntentForTest} band={uiForm.Padding.Bottom} ｜ {uiForm.StatusBarTextForTest}");
                uiForm.ShowStatusBarForTest(463.8, 1305.4, 71.8, 55.8, 81.99);
                Check("状态栏三档 = 预填充｜解码（平均|当前）+ MTP 接受率（只累计），不塞端口、显存这类别的",
                    uiForm.StatusBarTextForTest == "预填充 463.8|1,305 t/s·解码 71.8|55.8 t/s·MTP 接受率 82.0%",
                    uiForm.StatusBarTextForTest);
                W("几何核对（显示态）：" + uiForm.GeometryDumpForTest());
                var barH = uiForm.StatusBarHeightForTest; var inkH = uiForm.StatusBarInkHeightForTest;
                int barBand = UiTheme.S(uiForm, 24);
                Check("显示时多让出 24px（常规留白 16 + 8）作为**真正的状态条区域**：卡片底边=条子上边、条子下边=窗口底边",
                    uiForm.Padding.Bottom == barBand && barH == barBand
                    && uiForm.StatusBarTopForTest == uiForm.ClientSize.Height - barBand
                    && uiForm.StatusBarBottomForTest == uiForm.ClientSize.Height,
                    $"band={uiForm.Padding.Bottom}/want {barBand} bar.top={uiForm.StatusBarTopForTest} bar.h={barH} client={uiForm.ClientSize.Height}");
                Check("文字在条区内按整高 VerticalCenter（量字带 NoPadding；行盒 17 ≤ 条区 24，不裁不顶）",
                    inkH > 0 && barH >= inkH + UiTheme.S(uiForm, 4),
                    $"h={barH} 行盒={inkH} dpi={UiTheme.Dpi}");
                uiForm.ShowStatusBarForTest(null, null, null, null, null);
                Check("状态栏：取不到的档用短横 -（不用长横杠占位），且**不跟着写单位**（DEC-127① 他点名"
                      +"「没有数值的时候,不显示单位,如 t/s,%等」，按回旧写法这条就红）",
                    uiForm.StatusBarTextForTest == "预填充 -·解码 -·MTP 接受率 -",
                    uiForm.StatusBarTextForTest);
                uiForm.HideStatusBarForTest();
                Check("隐藏时底部回到常规留白 16（条区连同文字一起收掉，不留多余高度）",
                    uiForm.Padding.Bottom == UiTheme.S(uiForm, 16),
                    "band=" + uiForm.Padding.Bottom);
                uiForm.ShowStatusBarForTest(463.8, 1305.4, 71.8, 55.8, 81.99);
                uiForm.SetShowStatusBarForTest(false);
                uiForm.RefreshStatusBarForTest();
                Check("设置里关掉「显示状态栏」-> 整条区域连同文字一起收起（底留白回到 16）",
                    Row("statusbar").StartsWith("statusbar: 隐藏", StringComparison.Ordinal)
                    && uiForm.Padding.Bottom == UiTheme.S(uiForm, 16), Row("statusbar") + " band=" + uiForm.Padding.Bottom);
                uiForm.SetShowStatusBarForTest(true);
                uiForm.HideStatusBarForTest();
                uiForm.ShowStatusBarForTest(463.8, 1305.4, 71.8, 55.8, 81.99);
                uiForm.SetShowStatusBarForTest(false);
                uiForm.RefreshStatusBarForTest();
                Check("设置里关掉「显示状态栏」-> 整条区域收起（不是只清文字）",
                    Row("statusbar").StartsWith("statusbar: 隐藏", StringComparison.Ordinal), Row("statusbar"));
                uiForm.SetShowStatusBarForTest(true);
                uiForm.HideStatusBarForTest();
                UiStatusFollowChecks();
    }

    // 主窗布局行取一行（原 UiLayoutChecks 里的局部函数，二次切分后跨文件共用 → 静态方法 + rows 字段）
    internal static string Row(string title) => rows.FirstOrDefault(x => x.StartsWith(title + ":", StringComparison.Ordinal)) ?? "";

    // 顺序断言：只要求这些词按序出现（容忍中间多出无关文字，但顺序错了就会挂）
    internal static bool InOrder(string line, params string[] tokens)
    {
        int at = -1;
        foreach (var t in tokens)
        {
            int i = line.IndexOf(t, at + 1, StringComparison.Ordinal);
            if (i < 0) return false;
            at = i;
        }
        return true;
    }

    // 设置页布局行取一行（同上，吃 srows）
    internal static string SRow(string title) => srows.FirstOrDefault(x => x.StartsWith(title + ":", StringComparison.Ordinal)) ?? "";
}
