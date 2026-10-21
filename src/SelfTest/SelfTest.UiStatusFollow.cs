namespace MultiLauncher;

// 界面排布组（状态与跟页段）：DEC-125④ 状态色跟灯表、DEC-125③ 露边对比、DEC-125① 跟页判据、
// DEC-155 跟模型与待办（同页/有草稿/窗口不在眼前）、开窗落地两道闸。2026-10-15 从 SelfTest.UiFollow.cs
// 按子域切出，块内一行未改；共享状态见 SelfTest.State.cs。
internal static partial class SelfTest
{
    internal static void UiStatusFollowChecks()
    {
                // ============ DEC-125（2026-10-09 他一次提的四条界面/交互批复） ============
                // ④ 左栏「状态」那句的**颜色跟着下方那枚灯走**（他："应与下方状态灯的颜色一致(橙色),不要使用新颜色"）。
                //    判据不钉死某个 ARGB，而是钉"四档状态色全部从灯表 LightColor 取" ——
                //    StatusTextFor… 里任何一档被改回 SteelBlue / DarkOrange 字面量，这两条就红。
                var waitS = MainForm.StatusTextForStrata(false, false, false, false, true);
                var waitL = MainForm.StatusTextForLlama(false, "", true, false, false);
                var waitN = MainForm.StatusTextForNinfer(false, "", false, false, true);
                var lampOrange = MainForm.LightColor(MainForm.LightState.Orange);
                Check("DEC-125④ 待机那句三页一字不差、且颜色 = 灯表那一档橙（DEC-101② 的 SteelBlue 按他点名作废）",
                    waitS.Text == "● 待机 · 模型未加载" && waitL.Text == "● 待机 · 模型未加载" && waitN.Text == "● 待机 · 模型未加载"
                    && waitS.Color.ToArgb() == lampOrange.ToArgb() && waitL.Color.ToArgb() == lampOrange.ToArgb()
                    && waitN.Color.ToArgb() == lampOrange.ToArgb()
                    && lampOrange.ToArgb() != System.Drawing.Color.SteelBlue.ToArgb(),
                    $"{waitS.Text} / {waitS.Color} ｜ {waitL.Color} ｜ {waitN.Color}");
                var lampTable = new[] { MainForm.LightColor(MainForm.LightState.Green), lampOrange,
                                        MainForm.LightColor(MainForm.LightState.Red), MainForm.LightColor(MainForm.LightState.Gray) };
                var reachColors = new[] {
                        MainForm.StatusTextForStrata(true, false, false, false, false).Color,
                        MainForm.StatusTextForStrata(false, true, false, false, false).Color,
                        MainForm.StatusTextForStrata(false, true, true, false, false).Color,
                        MainForm.StatusTextForStrata(false, false, false, true, false).Color,
                        MainForm.StatusTextForStrata(false, false, false, false, false).Color,
                        waitS.Color,
                        MainForm.StatusTextForLlama(true, "", false, false, false).Color,
                        MainForm.StatusTextForLlama(false, "启动中…", false, false, false).Color,
                        MainForm.StatusTextForLlama(false, "", true, true, false).Color,
                        MainForm.StatusTextForLlama(false, "", false, false, true).Color,
                        waitL.Color,
                        MainForm.StatusTextForNinfer(true, "", false, false, false).Color,
                        MainForm.StatusTextForNinfer(false, "升级中…", false, false, false).Color,
                        MainForm.StatusTextForNinfer(false, "", true, false, false).Color,
                        MainForm.StatusTextForNinfer(false, "", false, true, false).Color,
                        waitN.Color,
                        MainForm.StatusTextForNinfer(false, "", false, false, false).Color,
                    };
                Check("DEC-125④ 三页共 17 个可达状态的颜色全在灯表里（长出一个表外色 = 这条红 = 「不要使用新颜色」的机器版）",
                    reachColors.All(c => lampTable.Any(a => a.ToArgb() == c.ToArgb())),
                    string.Join(" ", reachColors.Select(c => c.Name)));

                // ③ 叠放按钮（左栏模式条 / Ninfer 卡1 后端方块）底下一枚"露出来的那条边"要看得见。
                //    按回 Input(#2A2A2A) → 与 Card(#232323) 只差 7 级 → 这条红（他实跑判"几乎看不出来"就是这个差）。
                int peekDelta = Math.Abs((int)UiTheme.StackedPeek.R - UiTheme.Card.R);
                Check("DEC-125③ 露那条边的底色与卡片底色的亮度差 >= 24（原来 Input 只差 7 = 看不出来）",
                    peekDelta >= 24 && UiTheme.StackedPeek.ToArgb() != UiTheme.Input.ToArgb(),
                    $"StackedPeek=#{UiTheme.StackedPeek.R:X2} Card=#{UiTheme.Card.R:X2} 差={peekDelta}");

                // ① 模型被真加载起来时主区自动跟到那一页 + 那一件（DEC-125① 跟页 + DEC-155 跟模型）
                // DEC-138（E-11）：这一份"所有页"的样本以前写死三个键 —— 表里加一个模块时它不会跟着长，而这条判据号称是"按表穷举"。改成从模块注册表取，键的字面量只有那一个家。
                var allPages = ModuleRegistry.All.Select(m => m.Key).ToArray();
                Check("DEC-125① 跟页判据（纯函数）：别的页→切 / 已在该页→不切 / 那一页没启用→不切",
                    MainForm.PageToFollowAfterWake("llama", "strata", allPages) == "llama"
                    && MainForm.PageToFollowAfterWake("strata", "strata", allPages) is null
                    && MainForm.PageToFollowAfterWake("ninfer", "strata", new[] { "strata" }) is null);
                var otherPage = uiForm.FirstEnabledKeyOtherThanForTest(uiForm.ActiveModuleKey);
                uiForm.OnScreenForcedForTest = true;      // 无头时 Visible 恒 false（BUG-043 那一族）：用测试位把"窗在眼前"这一档点亮
                var page0 = uiForm.ActiveModuleKey;
                var sel0 = uiForm.SelectedTagForTest;
                uiForm.FollowModuleAfterWakeForTest(page0, sel0);   // 同页 + 就是当前选中那件 = 不动
                Check("DEC-125① 真空拉起的就是当前页那一件 → 不切页、也不留待办（但确实被走到了 = 证人位 >0）",
                    uiForm.ActiveModuleKey == page0 && uiForm.PendingFollowPageForTest is null && uiForm.FollowWakeCallsForTest > 0,
                    $"页={uiForm.ActiveModuleKey} 待办={uiForm.PendingFollowPageForTest ?? "无"}");
                uiForm.FollowModuleAfterWakeForTest(otherPage, "");
                Check("DEC-125① 窗口在眼前 + 真空拉起别的页 → 立刻跟过去（走的是同一条 SwitchModule，含切页那三处收口）",
                    uiForm.ActiveModuleKey == otherPage, $"期望={otherPage} 实={uiForm.ActiveModuleKey}");
                uiForm.SwitchModuleForTest(page0);
                uiForm.OnScreenForcedForTest = false;
                const string PendTag = "zz-selftest-pend";   // 下拉里没有的 tag：只验待办机制本身，不碰模型那一半
                uiForm.FollowModuleAfterWakeForTest(otherPage, PendTag);
                Check("DEC-125① 窗口不在眼前 → 不抢页面，只把待办记下（DEC-155：连 tag 一起记）",
                    uiForm.ActiveModuleKey == page0 && uiForm.PendingFollowPageForTest == otherPage
                    && uiForm.PendingFollowTagForTest == PendTag,
                    $"页={uiForm.ActiveModuleKey} 待办={uiForm.PendingFollowPageForTest ?? "无"}/{uiForm.PendingFollowTagForTest}");
                uiForm.OnScreenForcedForTest = true;
                bool wasGreen = uiForm.LightGreenForTest(otherPage);
                bool pendLoaded = uiForm.ModelLoadedInForTest(otherPage, PendTag);   // DEC-155 那道"那件还在跑"的闸（下拉里没有的 tag = 恒 false）
                uiForm.ApplyPendingFollowPageForTest();
                var pageAfter = uiForm.ActiveModuleKey;
                uiForm.ApplyPendingFollowPageForTest();
                Check("DEC-125① 开窗才落地，落地要过两道闸（灯还绿 + DEC-155 那件确实还在跑）；待办只吃一次（第二次不许再跳）",
                    pageAfter == ((wasGreen && pendLoaded) ? otherPage : page0) && uiForm.PendingFollowPageForTest is null
                    && uiForm.PendingFollowTagForTest.Length == 0 && uiForm.ActiveModuleKey == pageAfter,
                    $"当时Green={wasGreen} 那件在跑={pendLoaded} 页={pageAfter} 期望={(wasGreen && pendLoaded ? otherPage : page0)}");

                // DEC-155：跟模型那一半 —— 同页也跟（下拉切过去 + 界面换成它的家底）；目标页有草稿就只跟页不跟模型。
                // 判据两面：纯量（PendingEdits 按目标页算）走真路径，跟模型那一枪也走真入口。
                uiForm.SwitchModuleForTest("llama");
                uiForm.MarkParamsSavedForTest();               // 干净起点（没有未保存改动）
                uiForm.OnScreenForcedForTest = true;
                var llamaTagList = uiForm.LlamaModelsForTest.Select(m => m.Tag).ToList();
                Check("DEC-155 前置：llama 页至少两件（「同页跟模型」才有另一件可跟）",
                    llamaTagList.Count >= 2, "llama 段数=" + llamaTagList.Count);
                var stayTag = uiForm.SelectedTagForTest;
                var goTag = llamaTagList.FirstOrDefault(t => !string.Equals(t, stayTag, StringComparison.OrdinalIgnoreCase));
                if (goTag is not null)
                {
                    // 先把这一段的家底重载上卡（等价于用户点一次下拉）：llama / ninfer / strata **共用**卡2 那只"上下文"格，
                    // 前面那些用例在它上面留过草稿，而测试版 SwitchModuleForTest 不走真路径那次"先 Collect 再切"，
                    // 于是生路径（点下拉 = 载入该段家底）才是把这一页拉回正的方式 —— 生产里切页本来就会重推。
                    uiForm.SelectModelForTest(goTag);
                    uiForm.SelectModelForTest(stayTag);
                    uiForm.MarkParamsSavedForTest();
                    Check("DEC-155 干净页：PendingEdits = 0（按目标页判，比的是卡面现值与那一段的家底）",
                        uiForm.PendingEditsForTest("llama") == 0,
                        $"pending={uiForm.PendingEditsForTest("llama")} sel={uiForm.SelectedTagForTest} box={uiForm.ContextBoxForTest}");
                    uiForm.FollowModelInPageForTest("llama", goTag);
                    Check("DEC-155 同页 + 被加载的是另一件 → 下拉切过去、界面换成它的家底（跟到模型那一半）",
                        uiForm.SelectedTagForTest == goTag, $"选中={uiForm.SelectedTagForTest} 期望={goTag}");
                    // 造一份"目标页草稿"（改 llama 段级那格但不保存）→ 再跟另一件时必须只跟页、不切模型
                    uiForm.MarkParamsSavedForTest();
                    uiForm.SetContextForTest("zz-selftest-draft");
                    Check("DEC-155 有草稿时 PendingEdits > 0（拦它的那道判据真亮）",
                        uiForm.PendingEditsForTest("llama") > 0, "pending=" + uiForm.PendingEditsForTest("llama"));
                    uiForm.FollowModelInPageForTest("llama", stayTag);
                    Check("DEC-155 目标页有未保存改动 → 只跟页不跟模型（下拉按回原样，草稿不被动）",
                        uiForm.SelectedTagForTest == goTag, $"选中={uiForm.SelectedTagForTest} 期望仍是 {goTag}");
                    uiForm.SelectModelForTest(stayTag);        // 还原：切回原来那段（重载家底 = 草稿清掉、基线重设）
                }
                // Strata 侧同一判据（家底 = 模板 ⊕ [Model:<tag>] 记录那份）
                uiForm.SwitchModuleForTest("strata");
                uiForm.MarkParamsSavedForTest();
                int strataPend0 = uiForm.PendingEditsForTest("strata");
                uiForm.SetContextForTest("zz-selftest-strata-draft");
                int strataPend1 = uiForm.PendingEditsForTest("strata");
                Check("DEC-155 Strata 页：改了上下文没保存 → PendingEdits 变大（判据 = ModelOwnedKeys 与家底那份比）",
                    strataPend1 > strataPend0, $"pending {strataPend0} -> {strataPend1}");
                uiForm.RestoreViewForTest();                   // 还原卡面 + 重设基线，别污染后面那些用例
                uiForm.OnScreenForcedForTest = false;

                // ⑥ DEC-128：llama 的"真被加载 → 跟页"。这一支他实跑抓出来**从没接过**（网关那条线覆盖不到 llama：
                //    它的模型是 llama-server 自己按需加载的）。两层：先纯判据穷举，再喂一份真 /models 报文走**同一条**，
                //    证人位证明"这条线真被走到"（不许写成只测纯函数的空判据 —— 这一次翻车翻的就是调用点）。
                Check("DEC-128⑥ 跟页判据：有新增 + 壳自己在跑 + 壳不忙 → 跟（DEC-155：多件取最后一个）；没新增 / 壳外实例 / 壳在忙 → 不跟",
                    MainForm.LlamaLoadToFollow(new[] { "M1" }, true, false) == "M1"
                    && MainForm.LlamaLoadToFollow(new[] { "M1", "M2" }, true, false) == "M2"
                    && MainForm.LlamaLoadToFollow(Array.Empty<string>(), true, false) is null
                    && MainForm.LlamaLoadToFollow(new[] { "M1" }, false, false) is null
                    && MainForm.LlamaLoadToFollow(new[] { "M1" }, true, true) is null);
                string LlamaJson(params string[] ids) => "{\"data\":["
                    + string.Join(",", ids.Select(i => "{\"id\":\"" + i + "\",\"status\":{\"value\":\"loaded\"}}")) + "]}";
                uiForm.SetLlamaLoadedForTest();                        // 干净的起点（别拿旧状态蒙这一枪）
                int lf0 = uiForm.LlamaFollowCallsForTest;
                string lt0 = uiForm.GetLlamaOpForTest();
                uiForm.ApplyLlamaModelStates(LlamaJson("M1"), llamaRunning: true);
                bool lfOnce = uiForm.LlamaFollowCallsForTest == lf0 + 1;
                uiForm.ApplyLlamaModelStates(LlamaJson("M1"), llamaRunning: true);        // 同一份再来一遍 = 没有新增
                bool lfNoRepeat = uiForm.LlamaFollowCallsForTest == lf0 + 1;
                uiForm.ApplyLlamaModelStates(LlamaJson(), llamaRunning: true);            // 卸掉 = 集合变小，不是"被加载"
                bool lfUnload = uiForm.LlamaFollowCallsForTest == lf0 + 1;
                uiForm.SetLlamaOpForTest("加载中…");
                uiForm.ApplyLlamaModelStates(LlamaJson("M2"), llamaRunning: true);        // 壳自己在忙（手动加载/单体启动）→ 不跟
                bool lfBusy = uiForm.LlamaFollowCallsForTest == lf0 + 1;
                uiForm.SetLlamaOpForTest(lt0);
                uiForm.SetLlamaLoadedForTest();                        // 还原：别把测试件留给后面读加载集合的用例
                Check("DEC-128⑥ 真 /models 报文走同一条路：新增跟一枪、重复不再跟、卸载不跟、壳在忙不跟（证人位恰好 +1）",
                    lfOnce && lfNoRepeat && lfUnload && lfBusy,
                    $"calls={uiForm.LlamaFollowCallsForTest - lf0}（期望 1）");

                // DEC-141 那一半在**喂参数口**：加载集合里有一件、而下拉没指过去时，卡3 那一枚必须写「卸载」。
                // 光测纯函数看不见这一档（判据位就是那一份集合），所以喂样本走真路径 + 红/绿成对：
                // 按回旧写法（`m is not null && _llamaLoaded.Contains(m.Tag)`）时 loadedText 会变成「加载」= 这条红。
                var backTo = uiForm.ActiveModuleForTest;
                uiForm.SwitchModuleForTest("llama");
                var notSel = "zz-selftest-not-selected";
                if (uiForm.SelectedTagForTest == notSel) notSel = notSel + "-2";
                uiForm.SetLlamaLoadedForTest(notSel);
                string dec141Loaded = uiForm.ModelActionTextForTest;
                uiForm.SetLlamaLoadedForTest();
                string dec141Idle = uiForm.ModelActionTextForTest;
                bool dec141Route = uiForm.RouteModeForTest;
                uiForm.SwitchModuleForTest(backTo);
                Check("DEC-141 跑着别的那件（下拉没指过去）也给「卸载」；一件都没加载才回「加载」（单体模式恒「重载」）",
                    dec141Loaded == (dec141Route ? "卸载" : "重载")
                    && dec141Idle == (dec141Route ? "加载" : "重载"),
                    $"加载着别的那件={dec141Loaded} 空集合={dec141Idle} 路由模式={dec141Route}");

                // ② 状态栏：显示项那张表 + 「平均」按参数区分
                //（他："平均不是每个参数都有,如勾选 decode 时,才出现 decode 这个的平均值复选"）
                var pack9 = new MainForm.StatPack(463.8, 1305.4, 71.8, 55.8, 81.99, 3, 37.2, 128402, 4213, 18944, 24576, 73);
                Check("DEC-125② 默认那三档 = 升级前那条串逐字节相同（新机制不许顺手改他看到的界面）",
                    MainForm.StatusBarText(463.8, 1305.4, 71.8, 55.8, 81.99)
                        == "预填充 463.8|1,305 t/s·解码 71.8|55.8 t/s·MTP 接受率 82.0%",
                    MainForm.StatusBarText(463.8, 1305.4, 71.8, 55.8, 81.99));
                Check("DEC-125② 表外 id 一律忽略（手改坏的 ini 不许把状态栏弄炸），大小写与空格都吃",
                    MainForm.StatusBarIdSet("prefill, BOGUS ,,  decode ,").Count == 2,
                    string.Join(",", MainForm.StatusBarIdSet("prefill, BOGUS ,,  decode ,")));
                Check("DEC-125② 「平均」只认表里 HasAvg 的那两行（MTP/队列/显存/温度 塞进 ini 也不会长出半档）",
                    MainForm.StatusBarAvgSet("prefill,decode,mtp,queue,vram,temp").Count == 2
                    && MainForm.IsStatAvgable("prefill") && MainForm.IsStatAvgable("decode")
                    && !MainForm.IsStatAvgable("mtp") && !MainForm.IsStatAvgable("queue")
                    && !MainForm.IsStatAvgable("vram") && !MainForm.IsStatAvgable("temp")
                    && !MainForm.IsStatAvgable("bogus"),
                    string.Join(",", MainForm.StatusBarAvgSet("prefill,decode,mtp,queue,vram,temp")));
                uiForm.SetShowStatusBarForTest(true);
                uiForm.ShowStatusBarPackForTest(pack9, "prefill,mtp", "prefill");
                Check("DEC-125② 只勾两项 = 那两项之外一个字都不出现（「解码」整段连标签一起没了）",
                    uiForm.StatusBarTextForTest == "预填充 463.8|1,305 t/s·MTP 接受率 82.0%", uiForm.StatusBarTextForTest);
                uiForm.ShowStatusBarPackForTest(pack9, "prefill,decode", "");
                Check("DEC-125② 那一行的「平均」关掉 = 只出当前档（连竖线一起没 = 他要的「否则只显示当前」）",
                    uiForm.StatusBarTextForTest == "预填充 1,305 t/s·解码 55.8 t/s", uiForm.StatusBarTextForTest);
                // DEC-128②（他挑的"同排两枚独立"）：实时与平均各自独立 —— 三态各断一条
                uiForm.ShowStatusBarPackForTest(pack9, "prefill", "prefill");
                Check("DEC-128② 两枚都勾 = 照旧「平均|当前」（升级当天那条串的形状不变）",
                    uiForm.StatusBarTextForTest == "预填充 463.8|1,305 t/s", uiForm.StatusBarTextForTest);
                uiForm.ShowStatusBarPackForTest(pack9, "", "prefill");
                Check("DEC-128② 只勾平均（实时不勾）= 状态栏只出平均值 —— 独立的核心（以前根本勾不出这一态）",
                    uiForm.StatusBarTextForTest == "预填充 463.8 t/s", uiForm.StatusBarTextForTest);
                uiForm.ShowStatusBarPackForTest(pack9, "queue,cache,tokin,tokout,vram,temp", "");
                Check("DEC-125② 新增那几档各就各位（队列不带单位、缓存命中是 %、显存 用了/整卡G、温度 °C）"
                      +"；DEC-128③ 标签去掉了 token 二字",
                    uiForm.StatusBarTextForTest == "队列 3·缓存命中 37.2%·输入 128,402·输出 4,213·显存 18.5/24.0G·温度 73°C",
                    uiForm.StatusBarTextForTest);
                uiForm.ShowStatusBarPackForTest(MainForm.EmptyPack, "queue,cache,tokin,vram,temp", "");
                Check("DEC-125② 取不到的档一律短横（显存/温度没采到数时不许写 0.0G 假装知道）"
                      +"；DEC-127① 同一条串里**一个单位都不写**（旧写法是「缓存命中 -%」「温度 -°C」）",
                    uiForm.StatusBarTextForTest == "队列 -·缓存命中 -·输入 -·显存 -·温度 -", uiForm.StatusBarTextForTest);
                // DEC-148（他实跑点名）：llama 页从此有「队列」「缓存命中」，且**输入/输出 = 0 出短横**
                //（以前 llama 页写「输入 0·输出 0」，而 Strata / Ninfer 两页写短横 —— 那两个数是壳自己的累加器，初值 0）
                uiForm.ShowStatusBarPackForTest(
                    new MainForm.StatPack(100, 100, 50, 60, 60, 8, 6.7, 1500, 260, null, null, null),
                    "prefill,decode,mtp,queue,cache,tokin,tokout", "prefill,decode");
                Check("DEC-148 llama 页那一串从此有队列 / 缓存命中 / 输入总量（含缓存复用那一半）",
                    uiForm.StatusBarTextForTest == "预填充 100.0|100.0 t/s·解码 50.0|60.0 t/s·MTP 接受率 60.0%·队列 8·"
                        + "缓存命中 6.7%·输入 1,500·输出 260", uiForm.StatusBarTextForTest);
                uiForm.ShowStatusBarPackForTest(
                    new MainForm.StatPack(null, null, null, null, null, null, null, 0, 0, null, null, null),
                    "tokin,tokout", "");
                Check("DEC-148 输入/输出 = 0 当「没收到数据」-> 短横（三页统一，画那一头判）",
                    uiForm.StatusBarTextForTest == "输入 -·输出 -", uiForm.StatusBarTextForTest);
                {
                    // 真路径两面（BUG-043 那一族：只断纯函数 = 空判据；证人位两个，分得开"没问"与"没接"）
                    int a0 = uiForm.LlamaMetricsAttemptsForTest, q0 = uiForm.LlamaMetricsAskedForTest;
                    uiForm.SetLlamaLoadedForTest();
                    uiForm.SetLlamaExternalForTest(false);
                    _ = uiForm.PollLlamaStatsForTest();
                    Check("DEC-148 真路径：一件没在跑 -> 走到了但一个字都不问，落点 = 这一路没有包（全短横）",
                        uiForm.LlamaMetricsAttemptsForTest == a0 + 1 && uiForm.LlamaMetricsAskedForTest == q0
                        && uiForm.LlamaMetricPackForTest is null,
                        $"走到={uiForm.LlamaMetricsAttemptsForTest - a0} 真问={uiForm.LlamaMetricsAskedForTest - q0}");
                    uiForm.SetLlamaLoadedForTest("zz-selftest-not-running");
                    uiForm.SetLlamaExternalForTest(true);
                    _ = uiForm.PollLlamaStatsForTest();
                    Check("DEC-148 真路径：喂一件「在跑」的（外部实例档）-> 那一发真发出去了（不是空接线）",
                        uiForm.LlamaMetricsAttemptsForTest == a0 + 2 && uiForm.LlamaMetricsAskedForTest == q0 + 1,
                        $"走到={uiForm.LlamaMetricsAttemptsForTest - a0} 真问={uiForm.LlamaMetricsAskedForTest - q0}");
                    uiForm.SetLlamaLoadedForTest();
                    uiForm.SetLlamaExternalForTest(false);
                    // 落点那两条：`LlamaPack` 到底吃哪一份、清零清得干不干净（按回去"只吃日志后路"就会红）
                    uiForm.SetLlamaMetricPackForTest(new MainForm.StatPack(
                        100, 100, 50, 60, 60, 8, 6.7, 1500, 260, null, null, null));
                    Check("DEC-148 交出去的那一份 = /metrics 那一份（不是日志后路）：喂进去的包原样出来",
                        uiForm.LlamaPack().TokIn == 1500 && uiForm.LlamaPack().Queue == 8
                        && uiForm.LlamaPack().PrefillNow == 100, uiForm.LlamaPack().ToString());
                    uiForm.ResetLlamaStatsForTest();
                    Check("DEC-148 换件 / 进程没了 -> 这一路连基准一起清（基准留着会给下一件算出一个假速度）",
                        uiForm.LlamaMetricPackForTest is null && uiForm.LlamaPack().TokIn is null
                        && uiForm.LlamaPack().PrefillNow is null, uiForm.LlamaPack().ToString());
                }
                Check("DEC-125② Ninfer「当前」档 = 两次累计采样相减；不动 / 倒退 / 缺上一拍都给 null（不写 0.0 装成卡死）",
                    Math.Abs(MainForm.DeltaRate(1000, 10, 1600, 13)!.Value - 200.0) < 0.01
                    && MainForm.DeltaRate(1000, 10, 1000, 12) is null
                    && MainForm.DeltaRate(1000, 10, 900, 12) is null
                    && MainForm.DeltaRate(null, null, 900, 12) is null
                    && MainForm.DeltaRate(1000, 10, null, 12) is null,
                    $"{MainForm.DeltaRate(1000, 10, 1600, 13)}");
                uiForm.SetStatusBarItemsForTest("", "");
                uiForm.SetShowStatusBarForTest(true);
                uiForm.RefreshStatusBarForTest();
                Check("DEC-125② 一项都不勾 = 没有字可写 = 整条连区域一起收（不是留一条空带子）",
                    uiForm.Padding.Bottom == UiTheme.S(uiForm, 16) && !uiForm.StatusBarIntentForTest,
                    $"band={uiForm.Padding.Bottom} intent={uiForm.StatusBarIntentForTest}");
                uiForm.SetShowStatusBarForTest(true);
                uiForm.SetStatusBarItemsForTest("prefill,decode,mtp,queue,cache,tokin,tokout,vram,temp", "prefill,decode");
                uiForm.RefreshStatusBarForTest();
                var fit9 = uiForm.StatusBarFitForTest();
                W($"DEC-125② 九档全勾时那条串要 {fit9.Need} 物理宽 / 条区可用 {fit9.Room} → {(fit9.Need > fit9.Room ? "放不下（得勾掉几档）" : "放得下")}");
                using (var sfBar = new SettingsForm(uiForm, new Settings()))
                {
                    var (rowCount, avgCount, showCount) = sfBar.StatusBarRowsForTest;
                    Check("DEC-125② 设置页那一排 = 显示项表有几行长几行，而「平均」只长在该长的那两行",
                        rowCount == MainForm.StatItemDefs.Length && showCount == MainForm.StatItemDefs.Length
                        && avgCount == MainForm.StatItemDefs.Count(d => d.HasAvg) && avgCount == 2,
                        $"行={rowCount} 显示={showCount} 平均={avgCount}");
                    // DEC-137（F-3）：原先这条读的是 `SyncRows` 刚把 master.Checked 复制九遍填进去的那份清单
                    // = 按构造必真，把"关掉时把高度归零"那句整条删掉它照旧绿。改成断**真判据**：块高归零 / 开着有高度。
                    // （不读 _statWrap.Visible —— 无头时它是含祖先的有效可见性、恒 false = BUG-043 那一族。）
                    sfBar.StatusBarMasterForTest.Checked = false;
                    int statOff = sfBar.StatBlockHeightAppliedForTest;
                    sfBar.StatusBarMasterForTest.Checked = true;
                    int statOn = sfBar.StatBlockHeightAppliedForTest;
                    Check("DEC-125② / DEC-137 「显示状态栏」关掉 = 那一排连高度一起收（开关管区域含占位），开着才量得到高度",
                        statOn > 0 && statOff == 0, $"那一拍写进去的块高：开着={statOn} 关掉={statOff}");

                    // ============ DEC-127（2026-10-10 他一次提的六条里的 ①②③④⑥） ============
                    // ② 那一排可以拖动调次序，拖出来的先后就是状态栏那串的先后（以前按表序重拼 = 拖了也白拖）
                    var orderBefore = sfBar.StatRowVisualOrderForTest;
                    sfBar.MoveStatRowForTest("mtp", 0);          // 走的就是拖动那一条算式（把 MTP 拖到第一格）
                    var orderAfter = sfBar.StatRowVisualOrderForTest;
                    Check("DEC-127② 拖动改的是**行序**：九行一行不少，只是位置换了（没勾的档也留在排里，否则没地方勾回来）",
                        orderBefore.Count == MainForm.StatItemDefs.Length && orderAfter.Count == orderBefore.Count
                        && orderAfter[0] == "mtp" && orderAfter.Contains("prefill") && orderAfter.Contains("temp"),
                        $"前={string.Join(",", orderBefore)} 后={string.Join(",", orderAfter)}");
                    Check("DEC-127② csv 从此按**界面上此刻的次序**拼（按回旧的表序重拼，这条就红）",
                        sfBar.StatusBarOrderPendingForTest.StartsWith("mtp,", StringComparison.Ordinal),
                        sfBar.StatusBarOrderPendingForTest);
                    Check("DEC-127② 状态栏那串跟着这个次序排（MTP 落到第一位）",
                        MainForm.StatusBarSegments(MainForm.ShowAllSample,
                            MainForm.StatusBarRowOrder(sfBar.StatusBarOrderPendingForTest, ""),
                            MainForm.StatusBarIdSet(sfBar.StatusBarItemsPendingForTest),
                            MainForm.StatusBarAvgSet(sfBar.StatusBarAvgPendingForTest)).First().Label == "MTP 接受率",
                        MainForm.StatusBarJoinText(MainForm.StatusBarSegments(MainForm.ShowAllSample,
                            MainForm.StatusBarRowOrder(sfBar.StatusBarOrderPendingForTest, ""),
                            MainForm.StatusBarIdSet(sfBar.StatusBarItemsPendingForTest),
                            MainForm.StatusBarAvgSet(sfBar.StatusBarAvgPendingForTest))));
                    Check("DEC-128② 三键同源：Order = 九格全量次序、Items/Avg 都是它的子集（拖一次三键一起重拼）",
                        sfBar.StatusBarOrderPendingForTest.Split(',').Length == MainForm.StatItemDefs.Length
                        && MainForm.StatusBarRowOrder(sfBar.StatusBarOrderPendingForTest, "").Count == MainForm.StatItemDefs.Length,
                        sfBar.StatusBarOrderPendingForTest);
                    // DEC-128②（他挑的"同排两枚独立"）：实时取消、平均保留 —— 两键各自独立
                    sfBar.SetStatCheckedForTest("prefill", false);
                    Check("DEC-128② 「实时」取消、「平均」保留：Items 里没有它、Avg 里还有（依附时代勾不出这一态）",
                        !MainForm.StatusBarIdSet(sfBar.StatusBarItemsPendingForTest).Contains("prefill")
                        && sfBar.StatusBarAvgPendingForTest.Contains("prefill"),
                        $"items={sfBar.StatusBarItemsPendingForTest} avg={sfBar.StatusBarAvgPendingForTest}");
                    sfBar.SetStatCheckedForTest("prefill", true);     // 还原
                    sfBar.MoveStatRowForTest("mtp", 2);          // 归位：出门时默认三档的次序照旧是 prefill,decode,mtp
                    Check("DEC-127② 拖动只在**这一块**里换：上面那些不相干的行为一个字没动（归位后回到默认三档次序）",
                        string.Join(",", sfBar.StatRowVisualOrderForTest.Take(3)) == "prefill,decode,mtp"
                        && string.Join(",", sfBar.StatusBarItemsPendingForTest) == "prefill,decode,mtp",
                        $"前三={string.Join(",", sfBar.StatRowVisualOrderForTest.Take(3))} csv={sfBar.StatusBarItemsPendingForTest}");

                    // ⑤（他第 5 条补话定案）窗高**恒定**：拨开关只改页面内容长度（滚动条自己跟），窗子一个像素不动
                    //（作废 DEC-127④ 的"随开关长缩 + 自动滚到眼前"整套）。
                    int baseH = UiTheme.S(sfBar, sfBar.ClientLogicalHForTest);
                    int hOn = sfBar.ClientSize.Height;
                    int contentOn = sfBar.StatPageContentHeightForTest();
                    sfBar.StatusBarMasterForTest.Checked = false;
                    int hOff = sfBar.ClientSize.Height;
                    int contentOff = sfBar.StatPageContentHeightForTest();
                    sfBar.StatusBarMasterForTest.Checked = true;
                    Check("DEC-128⑤ 窗高恒定：开 / 关 / 再开都是一个高（基准 = ClientLogicalH 的换现值）",
                        hOn == baseH && hOff == hOn && sfBar.ClientSize.Height == hOn,
                        $"开={hOn} 关={hOff} 再开={sfBar.ClientSize.Height} 基准={baseH}");
                    Check("DEC-128⑤ 变的是页内容：开着 = 那一块有高、页更长；关掉 = 页变短（滚动条自己跟着走）",
                        sfBar.StatBlockHeightForTest > 0 && contentOn > contentOff,
                        $"块={sfBar.StatBlockHeightForTest} 页 开={contentOn} 关={contentOff}");

                    // ============ DEC-129（2026-10-10 他实跑 v1.5.10 回来的五条） ============
                    // ② 方框缩 20% —— 常量断言在上面那条 CheckItem 套件里（已改口到 13/2）。
                    // ③④（他挑的"名字在前、子项弱化"）：单勾选项 = 勾在名前的一枚（无独立标签、caption 即参数名）；
                    //    带平均的 = 名字 + 两枚子勾选（小一号、降一档颜色）
                    var faceMtp = sfBar.StatChipFaceForTest("mtp");
                    var facePre = sfBar.StatChipFaceForTest("prefill");
                    Check("DEC-129③ 单勾选项 = 勾在名前（无独立标签、caption 就是参数名）；带平均的 = 名字 + 两枚子勾选",
                        !faceMtp.HasLabel && faceMtp.Caption == "MTP 接受率"
                        && facePre.HasLabel && facePre.Caption == "实时",
                        $"mtp 无标签={!faceMtp.HasLabel}/{faceMtp.Caption}；prefill 有标签={facePre.HasLabel}/{facePre.Caption}");
                    Check("DEC-129④ 子项弱化：实时/平均 字号小一档（9 → 8.5pt）、颜色降到 TextMuted",
                        facePre.CaptionPt < 9f && facePre.CaptionColor.ToArgb() == UiTheme.TextMuted.ToArgb()
                        && facePre.AvgPt < 9f && facePre.AvgColor.ToArgb() == UiTheme.TextMuted.ToArgb(),
                        $"实时={facePre.CaptionPt}/{facePre.CaptionColor.Name} 平均={facePre.AvgPt}/{facePre.AvgColor.Name}");
                    Check("DEC-129③ 那一块守边距：第一格左缘 = PadX 的换算值（他实测'太靠边'；按回从 x=0 起摆就红）",
                        sfBar.StatChipLeftForTest(sfBar.StatRowVisualOrderForTest[0]) == UiTheme.S(sfBar, SettingsForm.PadXForTest),
                        $"left={sfBar.StatChipLeftForTest(sfBar.StatRowVisualOrderForTest[0])} 期望={UiTheme.S(sfBar, SettingsForm.PadXForTest)}");

                    // ① 滑条范围重同步（他实测"拨开之后滚不动、先上再下"的根因）：内容长缩之后 Layout 布线
                    //    要喊过重同步（证人位，无头下 BeginInvoke 不会真跑），同步一次之后范围 == 宿主现值。
                    var statBar = sfBar.StatBarForTest;
                    int hits0 = statBar?.LayoutSyncHitsForTest ?? -1;
                    sfBar.StatusBarMasterForTest.Checked = false;
                    sfBar.StatusBarMasterForTest.Checked = true;
                    Check("DEC-129① 内容长/缩之后，滑条被 Layout 重同步线喊过（没这根线 = 范围停旧值 = 下滚不动）",
                        statBar is not null && statBar.LayoutSyncHitsForTest > hits0,
                        $"hits={hits0}→{statBar?.LayoutSyncHitsForTest}");
                    // 无头下量不了"范围等值"（宿主滚动条是未初始化的默认量程、Sync 又因无句柄早退），
                    // 所以改用**第二条证人位**：拖动松手那一步（内容可能因折行变行数）也要走同一条重同步线。
                    int hits1 = statBar?.LayoutSyncHitsForTest ?? -1;
                    sfBar.MoveStatRowForTest("temp", 0);
                    sfBar.MoveStatRowForTest("temp", 8);      // 归位
                    Check("DEC-129① 拖动（松手归位那一步）也走同一条重同步线 —— 内容高度变了滑条就得跟上",
                        statBar is not null && statBar.LayoutSyncHitsForTest > hits1,
                        $"hits={hits1}→{statBar?.LayoutSyncHitsForTest}");

                    // ⑤ 拖动改"定位条 + 松手归位"：落点换算纯函数穷举 + 定位条几何（按流序不后走、两端都出得来）
                    bool FlowAfter(Rectangle a, Rectangle b) => a.Top > b.Top || (a.Top == b.Top && a.Left >= b.Left);
                    Check("DEC-129⑤ 落点换算：插回自己前/后 = 原位；往下插减一；两端夹住",
                        SettingsForm.DropTargetToIndex(2, 2, 9) == 2 && SettingsForm.DropTargetToIndex(2, 3, 9) == 2
                        && SettingsForm.DropTargetToIndex(2, 5, 9) == 4 && SettingsForm.DropTargetToIndex(5, 2, 9) == 2
                        && SettingsForm.DropTargetToIndex(0, 9, 9) == 8 && SettingsForm.DropTargetToIndex(8, 0, 9) == 0,
                        $"(2,5,9)={SettingsForm.DropTargetToIndex(2, 5, 9)} (5,2,9)={SettingsForm.DropTargetToIndex(5, 2, 9)}");
                    var ind0 = sfBar.DropIndicatorRectForTest(0);
                    var ind1 = sfBar.DropIndicatorRectForTest(1);
                    var indEnd = sfBar.DropIndicatorRectForTest(9);
                    Check("DEC-129⑤ 定位条：插在哪一格前就站在它左缘（末尾插点站最后一格右缘），按流序不后走",
                        ind0 is { } r0 && ind1 is { } r1 && indEnd is { } rE
                        && r0.Height > 0 && FlowAfter(r1, r0) && FlowAfter(rE, r1),
                        $"0=({ind0?.Left},{ind0?.Top}) 1=({ind1?.Left},{ind1?.Top}) 末=({indEnd?.Left},{indEnd?.Top})");

                    // ============ DEC-130（2026-10-10 他实跑 v1.5.11 回来的两条） ============
                    // ① 滚到底时最底一排贴视口边、块下那截滚不出来（"最底部文字与复选框显示不完整"的根因 = 这一页
                    //    自己算的滚动范围比块的真底短一截）：修法 = 每次重摆后把页的 AutoScrollMinSize 补到
                    //    "块的真底 + 页底留白"；块藏起来时归零（页自己算的是对的，别叠）。
                    int syncCalls0 = sfBar.ScrollBottomSyncCallsForTest;
                    sfBar.StatusBarMasterForTest.Checked = false;      // 走一遍长缩，这条同步线必须被喊过
                    sfBar.StatusBarMasterForTest.Checked = true;
                    int needR = sfBar.ScrollBottomLastNeedForTest;
                    Check("DEC-130① 重摆之后滚动范围被补到块的真底（没这一步 = 滚到底还差一截 = 底排贴边被切）",
                        sfBar.ScrollBottomSyncCallsForTest > syncCalls0 && needR > 0
                        && sfBar.StatBlockHeightForTest > 0 && needR > sfBar.StatBlockHeightForTest,
                        $"need={needR} 块高={sfBar.StatBlockHeightForTest} 新喊={sfBar.ScrollBottomSyncCallsForTest - syncCalls0} 次");
                    Check("DEC-130① 补的范围真写进了页里（读回 AutoScrollMinSize 就是那个数；不写 = 页还是自然值 = 红）",
                        sfBar.AutoScrollMinAppliedForTest == needR,
                        $"min={sfBar.AutoScrollMinAppliedForTest} need={needR}");
                    sfBar.StatusBarMasterForTest.Checked = false;
                    Check("DEC-130① 块藏起来时归零（页自己算的是对的，不许叠一个数上去）",
                        sfBar.ScrollBottomLastNeedForTest == 0 && sfBar.AutoScrollMinAppliedForTest == 0,
                        $"need={sfBar.ScrollBottomLastNeedForTest} min={sfBar.AutoScrollMinAppliedForTest}");
                    sfBar.StatusBarMasterForTest.Checked = true;

                    // ② 试做"胶囊/小卡片"（他点名"做胶囊/小卡片试试看"）：每格 = 一枚自画小卡片（底 Card、描边 Border、
                    //    圆角 6 逻辑）；格里的名字与复选垫卡片底色（还写 Window 就会用方角盖掉圆角）；内容左右各留 PadIn。
                    var cardMtp = sfBar.StatChipCardForTest("mtp");
                    var cardPre = sfBar.StatChipCardForTest("prefill");
                    Check("DEC-130② 每格一枚小卡片：DrawCard 开着、底色 = Card、描边 = Border、圆角 6 逻辑",
                        cardMtp.DrawCard && cardMtp.Fill.ToArgb() == UiTheme.Card.ToArgb()
                        && cardMtp.Line.ToArgb() == UiTheme.Border.ToArgb() && cardMtp.Radius == 6,
                        $"draw={cardMtp.DrawCard} fill={cardMtp.Fill.Name} line={cardMtp.Line.Name} r={cardMtp.Radius}");
                    Check("DEC-130② 卡片里的名字/复选垫卡片底色（Backdrop 还写 Window = 卡上一个深色方角 = 红）",
                        cardPre.LabelBackdrop.ToArgb() == UiTheme.Card.ToArgb()
                        && cardPre.BoxBackdrop.ToArgb() == UiTheme.Card.ToArgb()
                        && cardPre.AvgBackdrop.ToArgb() == UiTheme.Card.ToArgb(),
                        $"标签={cardPre.LabelBackdrop.Name} 实时={cardPre.BoxBackdrop.Name} 平均={cardPre.AvgBackdrop.Name}");
                    Check("DEC-130② 卡片内衬：内容从 PadIn 起摆、右侧同样收 PadIn（按回从 0 起摆 = 圆角啃字 = 红）",
                        cardPre.PadIn == UiTheme.S(sfBar, SettingsForm.StatCardPadForTest)
                        && cardPre.FirstControlLeft == cardPre.PadIn && cardPre.RightGap == cardPre.PadIn
                        && cardMtp.FirstControlLeft == cardMtp.PadIn,
                        $"pad={cardPre.PadIn} 首控件左={cardPre.FirstControlLeft} 右余={cardPre.RightGap}（mtp 左={cardMtp.FirstControlLeft}）");

                    // DEC-140（他实跑回来的观感一条："淡一档,并且也应该做成卡片一样的圆角啊,现在这种方框太丑了"）：
                    // 拖动时"被拖那一格"外面那一圈 = 卡片同款**圆角**（跟着那一格自己的圆角同心）+ **淡一档**的品牌绿。
                    var ringMtp = sfBar.StatRingForTest("mtp");
                    Check("DEC-140 那一圈是圆角、且跟着那一格卡片的圆角同心（按回方角 0 = 红；写死一个数、退档裸格子时浮出方纸也算红）",
                        ringMtp.RadiusLogical == cardMtp.Radius + SettingsForm.DragRingPadLogical
                        && ringMtp.RadiusLogical > 0 && ringMtp.PadLogical == SettingsForm.DragRingPadLogical,
                        $"圆角={ringMtp.RadiusLogical} 卡片={cardMtp.Radius} 撑出={ringMtp.PadLogical}");
                    Check("DEC-140 那一圈取色淡一档 = BrandHover（还用最亮的 Brand = 红；也不许长色表外的新颜色；落点定位条照旧 Brand）",
                        ringMtp.Color.ToArgb() == UiTheme.BrandHover.ToArgb()
                        && ringMtp.Color.ToArgb() != UiTheme.Brand.ToArgb(),
                        $"圈={ringMtp.Color.ToArgb():X8} 淡档={UiTheme.BrandHover.ToArgb():X8} 亮档={UiTheme.Brand.ToArgb():X8}");

                    // ============ DEC-131（2026-10-10 他过目胶囊之后的三条：① 与分隔线留间隔 ② 勾选只认方框、其余全是拖动锚点 ③ 版本升 1.6） ============
                    // ① 那块与上/下分隔线之间的内衬（原来 chips 从 y=0 起摆 = 贴着线；实测只差 0~1 物理像素）
                    int padY = UiTheme.S(sfBar, SettingsForm.StatBlockPadYForTest);
                    int firstTop = sfBar.StatChipTopForTest(sfBar.StatRowVisualOrderForTest[0]);
                    Check("DEC-131① 第一格顶上 = 内衬高（按回 y=0 起摆 = 贴着分隔线 = 红）",
                        padY > 0 && firstTop == padY, $"内衬={padY} 第一格顶={firstTop}");
                    Check("DEC-131① 承载格高 = 上内衬 + 九格合高 + 下内衬（只加一半 = 红）",
                        sfBar.StatBlockHeightForTest == sfBar.StatRowsBoundsForTest().Height + 2 * padY,
                        $"块高={sfBar.StatBlockHeightForTest} 九格合高={sfBar.StatRowsBoundsForTest().Height} 内衬={padY}");
                    // DEC-138（M6 缺口 6 = F-6 的来源）：DEC-131① 把那条线交给承载格画，**线归属以前没有判据** ——
                    // 于是"关掉那块 = 承载格不产 Paint = 那一行成了通用页唯一没有行尾线的行"没人发现。
                    // 现在钉一对互斥的纯函数：两态各恰好一边画（都画 = 重影、都不画 = 丢线，都会红）。
                    Check("DEC-138 缺口6 那条线恒有一个人画：开着=承载格、关掉=本行（两态互斥，画归属只有一个家）",
                        SettingsForm.StatLineFromWrap(true) && !SettingsForm.StatLineFromRow(true)
                        && !SettingsForm.StatLineFromWrap(false) && SettingsForm.StatLineFromRow(false),
                        $"开:格={SettingsForm.StatLineFromWrap(true)} 行={SettingsForm.StatLineFromRow(true)} " +
                        $"关:格={SettingsForm.StatLineFromWrap(false)} 行={SettingsForm.StatLineFromRow(false)}");

                    // ② 勾选只认方框：框里 → 算勾选；字上/空白 → 不算（那一下接给拖动）—— 判据 + 接线两件都要证
                    var boxMtp = sfBar.StatBoxForTest("mtp")!;
                    var boxAvg = sfBar.StatAvgBoxForTest("prefill")!;
                    var boxR = boxMtp.BoxRectForTest();
                    var inBox = new Point(boxR.Left + boxR.Width / 2, boxR.Top + boxR.Height / 2);
                    var onText = new Point(boxMtp.Width - UiTheme.S(boxMtp, 2), boxMtp.Height / 2);
                    var onTextAvg = new Point(boxAvg.Width - UiTheme.S(boxAvg, 2), boxAvg.Height / 2);
                    Check("DEC-131② 勾选只认方框：框上算勾、字上不算（按回「整枚都能点勾」= 红）",
                        boxMtp.ClickTogglesAt(inBox) && !boxMtp.ClickTogglesAt(onText)
                        && !boxAvg.ClickTogglesAt(onTextAvg),
                        $"框心={boxMtp.ClickTogglesAt(inBox)} 字上={boxMtp.ClickTogglesAt(onText)}（平均那枚字上={boxAvg.ClickTogglesAt(onTextAvg)}）");
                    Check("DEC-131② 字上/空白那一下真接给了拖动（两枚复选都接上；只写纯函数不接线 = 红）",
                        boxMtp.DragForwardWired && boxAvg.DragForwardWired,
                        $"mtp={boxMtp.DragForwardWired} prefill 平均={boxAvg.DragForwardWired}");

                    // DEC-132（他："悬浮时复选框亮起…不应该亮起"）：悬停高亮只认方框 —— 字上/空白不亮、框上才亮
                    boxMtp.UpdatePointerForTest(onText);
                    bool litText = boxMtp.HoverLitForTest;
                    boxMtp.UpdatePointerForTest(inBox);
                    bool litBox = boxMtp.HoverLitForTest;
                    boxMtp.UpdatePointerForTest(onText);      // 复原（别把 hover 态漏给后面的用例）
                    Check("DEC-132 悬浮高亮只认方框：字上/空白不亮、框上才亮（按回「整枚都亮」= 红）",
                        !litText && litBox && !boxMtp.HoverLitForTest,
                        $"字上={litText} 框上={litBox} 复原后={boxMtp.HoverLitForTest}");

                    // DEC-133（他："拖拽图标不用变,保持默认即可"）：光标框上=手形、字上/空白=默认箭头（十字箭头那套退役）
                    boxMtp.UpdatePointerForTest(onText);
                    var curText = boxMtp.Cursor;
                    boxMtp.UpdatePointerForTest(inBox);
                    var curBox = boxMtp.Cursor;
                    boxMtp.UpdatePointerForTest(onText);
                    Check("DEC-133 光标：框上=手形、字上/空白=默认（按回十字箭头 = 红）",
                        curText == Cursors.Default && curBox == Cursors.Hand,
                        $"字上={curText} 框上={curBox}");

                    // ①（他第 1 条）实时预览：勾 / 拨 = 立刻渲染到主窗那条串；不保存关窗 = 收起预览、回到磁盘那份
                    uiForm.ClearStatusBarPreview();     // 先收掉上面 sfBar 自己推的预览，拿"磁盘那份"当基准
                    uiForm.SetShowStatusBarForTest(true);
                    uiForm.SetStatusBarItemsForTest("prefill", "prefill");
                    uiForm.SetStatusBarOrderForTest("");
                    uiForm.RefreshStatusBarForTest();
                    string cfgText = uiForm.StatusBarTextForTest;              // 磁盘那份 = 只一档（"预填充 -"）
                    using (var sfP = new SettingsForm(uiForm, new Settings())) // 默认那份：三档、两档带平均
                    {
                        Check("DEC-128① 设置窗一开就推预览：主窗立刻按「拟生效」那份渲染（不用点保存）",
                            uiForm.StatusBarTextForTest == "预填充 -·解码 -·MTP 接受率 -", uiForm.StatusBarTextForTest);
                        sfP.SetStatCheckedForTest("mtp", false);             // 勾一下
                        Check("DEC-128① 勾掉一档 = 预览里立刻少一段（此刻还没落盘）",
                            uiForm.StatusBarTextForTest == "预填充 -·解码 -", uiForm.StatusBarTextForTest);
                        sfP.StatusBarMasterForTest.Checked = false;          // 拨关主开关
                        Check("DEC-128① 拨关主开关 = 预览里整条连条带占位一起收（底留白回到常规 16）",
                            !uiForm.StatusBarIntentForTest && uiForm.Padding.Bottom == UiTheme.S(uiForm, 16),
                            $"intent={uiForm.StatusBarIntentForTest} band={uiForm.Padding.Bottom}");
                        sfP.StatusBarMasterForTest.Checked = true;
                        sfP.Close();
                        Check("DEC-128① 不保存关窗 = 预览撤销、回到磁盘那一份（他：不保存则回到之前的状态）",
                            uiForm.StatusBarTextForTest == cfgText, $"now={uiForm.StatusBarTextForTest} 期望={cfgText}");
                    }
                    uiForm.ClearStatusBarPreview();      // 幂等收尾（防无头下 FormClosed 没走到时漏网）
                    uiForm.SetStatusBarItemsForTest("prefill,decode,mtp", "prefill,decode");
                    uiForm.SetStatusBarOrderForTest("");
                    uiForm.RefreshStatusBarForTest();
                }
                // DEC-136（E-3）：两面都要有 —— 先把「显存 / 温度」那两档喂上、踩一次入口，
                // 于是"走到了"(attempts) > 0 而"真起进程"(calls) == 0 才成立；只断 calls == 0 的话，
                // 把 SelfTest 头上那道闸按回去它照样绿（那条路在无头里根本没定时器拍它）。
                uiForm.SetStatusBarItemsForTest("prefill,decode,vram,temp", "prefill,decode");
                _ = uiForm.SampleGpuForStatusBarForTest();
                var gpuTries = MainForm.GpuQueryAttemptsForTest;
                Check("DEC-125② / DEC-136 显存查询这条路走到了、但自检这一趟一个进程都没起（那道闸按回去 calls 就会涨）",
                    MainForm.GpuProbeDisabledForTest && gpuTries > 0 && MainForm.GpuQueryCallsForTest == 0,
                    $"闸={MainForm.GpuProbeDisabledForTest} 走到={gpuTries} 次 / 真起进程={MainForm.GpuQueryCallsForTest} 次");
                uiForm.SetStatusBarItemsForTest("prefill,decode,mtp", "prefill,decode");
                // ① 单位只跟着"真取到的那个数"走：两档都缺才整档不带单位，只缺一档时那一格仍是短横、单位照写
                uiForm.ShowStatusBarPackForTest(new MainForm.StatPack(null, 1305.4, 463.8, null, null,
                                                                  null, null, null, null, null, null, null),
                                              "prefill,decode", "prefill,decode");
                Check("DEC-127① 只缺平均（当前有数）/ 只缺当前（平均有数）-> 单位照写、缺的那格仍是短横",
                    uiForm.StatusBarTextForTest == "预填充 -|1,305 t/s·解码 463.8|- t/s", uiForm.StatusBarTextForTest);
                uiForm.ShowStatusBarPackForTest(new MainForm.StatPack(463.8, 1305.4, null, null, null,
                                                                  null, null, null, null, null, null, null),
                                              "prefill,decode,mtp", "prefill,decode");
                Check("DEC-127① 一整档都没取到 = 只剩一个短横（「解码 -」不写 t/s、「MTP 接受率 -」不写 %），"
                      +"旁边那一档有数就照旧带单位",
                    uiForm.StatusBarTextForTest == "预填充 463.8|1,305 t/s·解码 -·MTP 接受率 -", uiForm.StatusBarTextForTest);
                Check("DEC-127① 全空那份包按任何次序排出来的串里，t/s / % / °C / G 一个都不出现",
                    !MainForm.StatusBarJoinText(MainForm.StatusBarSegments(MainForm.EmptyPack,
                        MainForm.StatRowOrder(""), MainForm.StatusBarIdSet(""), MainForm.StatusBarAvgSet("prefill,decode")))
                        .Contains("t/s", StringComparison.Ordinal)
                    && !MainForm.StatusBarJoinText(MainForm.StatusBarSegments(MainForm.EmptyPack,
                        MainForm.StatRowOrder(""), MainForm.StatusBarIdSet("prefill,decode,mtp"), MainForm.StatusBarAvgSet("")))
                        .Contains("%", StringComparison.Ordinal)
                    && !MainForm.StatusBarJoinText(MainForm.StatusBarSegments(MainForm.EmptyPack,
                        MainForm.StatRowOrder(""), MainForm.StatusBarIdSet("prefill,decode,mtp"), MainForm.StatusBarAvgSet("")))
                        .Contains("°C", StringComparison.Ordinal),
                    MainForm.StatusBarJoinText(MainForm.StatusBarSegments(MainForm.EmptyPack,
                        MainForm.StatRowOrder(""), MainForm.StatusBarIdSet("prefill,decode,mtp"), MainForm.StatusBarAvgSet("prefill,decode"))));
                Check("DEC-127② 次序判据保序 + 去重 + 表外忽略（手改坏的 ini 不许把状态栏弄炸）",
                    string.Join(",", MainForm.StatusBarOrderOf("mtp,bogus,mtp,prefill")) == "mtp,prefill"
                    && string.Join(",", MainForm.StatusBarOrderOf(" decode , PREFILL ")) == "decode,prefill"
                    && MainForm.StatusBarOrderOf("").Count == 0,
                    string.Join(",", MainForm.StatusBarOrderOf("mtp,bogus,mtp,prefill")));
                Check("DEC-127② 行序兜底：csv 空 / 只写两档时九行都在，且写在前面的排在前面",
                    MainForm.StatRowOrder("").Count == MainForm.StatItemDefs.Length
                    && MainForm.StatRowOrder("temp,prefill").Count == MainForm.StatItemDefs.Length
                    && MainForm.StatRowOrder("temp,prefill")[0] == "temp"
                    && MainForm.StatRowOrder("temp,prefill")[1] == "prefill",
                    string.Join(",", MainForm.StatRowOrder("temp,prefill")));

                // ③ 复选框按他给的 square-check 重画：两态都换、勾是镂空、方角改圆角，且不新增颜色
                var pal = new[] { UiTheme.Window, UiTheme.Card, UiTheme.Input, UiTheme.Text, UiTheme.TextMuted,
                                  UiTheme.Border, UiTheme.Brand, UiTheme.BrandHover, UiTheme.BrandOn };
                var checkColors = new[] { CheckItem.CheckFill(true, true, false), CheckItem.CheckFill(false, true, false),
                                          CheckItem.CheckFill(true, true, true), CheckItem.CheckFill(false, true, true),
                                          CheckItem.CheckFill(true, false, false), CheckItem.CheckOutline(false, true, false),
                                          CheckItem.CheckOutline(false, true, true), CheckItem.CheckOutline(true, true, false),
                                          CheckItem.CheckLine(true, UiTheme.Window) };
                Check("DEC-127③ 选中 = 品牌绿实底、未选 = Input 底 + Border 描边（**两态都换了**，只改亮的那一态这条红）",
                    CheckItem.CheckFill(true, true, false).ToArgb() == UiTheme.Brand.ToArgb()
                    && CheckItem.CheckFill(false, true, false).ToArgb() == UiTheme.Input.ToArgb()
                    && CheckItem.CheckOutline(false, true, false).ToArgb() == UiTheme.Border.ToArgb()
                    && CheckItem.CheckFill(true, true, false).ToArgb() != CheckItem.CheckFill(false, true, false).ToArgb(),
                    $"选={CheckItem.CheckFill(true, true, false).Name} 未={CheckItem.CheckFill(false, true, false).Name}");
                Check("DEC-127③ 勾 = **镂空**（透出这一枚坐落的底色），不是白勾、也不是第二支笔",
                    CheckItem.CheckLine(true, UiTheme.Window).ToArgb() == UiTheme.Window.ToArgb()
                    && CheckItem.CheckLine(true, UiTheme.Window).ToArgb() != System.Drawing.Color.White.ToArgb(),
                    CheckItem.CheckLine(true, UiTheme.Window).Name);
                Check("DEC-127③ 方角改圆角（DEC-129② 起整体缩 20%）：枚 13 逻辑、圆角 2 逻辑（按回 16/3 就红）",
                    CheckItem.BoxLogical == 13 && CheckItem.BoxRadiusLogical == 2,
                    $"枚={CheckItem.BoxLogical} 圆角={CheckItem.BoxRadiusLogical}");
                Check("DEC-127③ 这一族不许长出新颜色：两态 + hover + 灰掉的所有取色都在既有色表里",
                    checkColors.All(c => pal.Any(p => p.ToArgb() == c.ToArgb())),
                    string.Join(" ", checkColors.Select(c => c.Name)));

                // ⑥ 自检期间一条 toast 都不许弹到屏幕上（他："自检时经常弹『界面参数已保存』…能不能不弹这个"）
                Check("DEC-127⑥ 闸上了、而且那条路**真走到了**（attempts > 0 = 不是空判据；弹出数必须 0）",
                    Toast.DisabledForTest && Toast.AttemptsForTest > 0 && Toast.ShownForTest == 0,
                    $"闸={Toast.DisabledForTest} 走到={Toast.AttemptsForTest} 次 弹出={Toast.ShownForTest} 条");
                Check("DEC-127⑥ 判据两面：闸放开时要真弹得出去（否则正面那条会假绿），空文案永远不弹",
                    Toast.WouldShow(false, "界面参数已保存") && !Toast.WouldShow(true, "界面参数已保存")
                    && !Toast.WouldShow(false, "   "),
                    $"放开={Toast.WouldShow(false, "界面参数已保存")} 闸上={Toast.WouldShow(true, "x")}");

                // DEC-147（BUG-067 结案）：「自动启动服务」= 这一族按进程收口的闸里最后漏的那一道。
                // 探针建的是真 MainForm 且会 Show()，他 ini 里 AutoStartStrata/Llama=True → 以前一开窗就去碰真服务，
                // 撞上端口上他自己在跑的实例还会走到「结束外部进程」那枚模态确认（r25 那一趟挂 13 分钟不落一张图）。
                Check("DEC-147 ShouldWireAutoStart 四面：只有「开关开 + 模块启用 + 没上闸」才挂 Load",
                    MainForm.ShouldWireAutoStart(true, true, false) && !MainForm.ShouldWireAutoStart(false, true, false)
                    && !MainForm.ShouldWireAutoStart(true, false, false) && !MainForm.ShouldWireAutoStart(true, true, true),
                    $"全真={MainForm.ShouldWireAutoStart(true, true, false)} 开关关={MainForm.ShouldWireAutoStart(false, true, false)} "
                    + $"停用={MainForm.ShouldWireAutoStart(true, false, false)} 闸上={MainForm.ShouldWireAutoStart(true, true, true)}");
                // 合成 Settings：三格开关全开、三模块全启用 → 闸放开该挂 3 格、闸上该挂 0 格。
                // 为什么不只断真路径的证人位：那一条吃他的 ini（哪天把三枚开关都关掉就量不到东西 = 环境相关假红），
                // 这一面把样本喂进去，与真路径那条配成两面（口径同 DEC-125② 那批"先喂样本再走真路径"）。
                var autoAllOn = new Settings { AutoStartStrata = true, AutoStartLlama = true, AutoStartNinfer = true };
                Check("DEC-147 这道闸挡的是整条路：按模块表重放，闸放开 = 三格全挂、闸上 = 一格都不挂",
                    uiForm.CountAutoStartWiringForTest(autoAllOn, false) == 3
                    && uiForm.CountAutoStartWiringForTest(autoAllOn, true) == 0,
                    $"放开={uiForm.CountAutoStartWiringForTest(autoAllOn, false)} 闸上={uiForm.CountAutoStartWiringForTest(autoAllOn, true)}");
                Check("DEC-147 自检这一趟真路径上一次都没挂上 Load（红/绿 = 把构造函数那句判据按回旧写法、这条必红）",
                    MainForm.AutoStartDisabledForTest && MainForm.AutoStartWiredForTest == 0,
                    $"闸={MainForm.AutoStartDisabledForTest} 挂上={MainForm.AutoStartWiredForTest} 挡下={MainForm.AutoStartSkippedForTest}");

                // DEC-164（BUG-068 第二条残留结案）：开机自启那一枚 HKCU Run 键 = 这一族**最后一道**按进程收口的闸。
                // 与 DEC-147 那道是两件事：那道管「要不要挂 Load 去起服务」，这道管「要不要动他的注册表」。
                // 为什么是硬要求：`SetAutoStart` 拿的是**本进程这只 exe** 的路径 —— 他开着自启 = 那枚值被改指到
                // 本地开发目录那只；他关着 = 他那枚值被**删掉**（自启静默失效）。跑一次自检不该改他任何东西（⓪ 那条）。
                Check("DEC-164 闸上了、而且构造函数那一条真路径**真被走到**（挡下 > 0 = 不是空判据；真写必须 0）",
                    MainForm.AutoStartRegistryDisabledForTest
                    && MainForm.AutoStartRegistrySkippedForTest > 0
                    && MainForm.AutoStartRegistryWritesForTest == 0,
                    $"闸={MainForm.AutoStartRegistryDisabledForTest} 挡下={MainForm.AutoStartRegistrySkippedForTest} 真写={MainForm.AutoStartRegistryWritesForTest}");
                // 反面证据：把闸放开再走一次真路径 —— 「走到要写那一步」的计数必须涨，否则上面那条 `真写 == 0` 是空判据。
                // 这一步**不碰真注册表**：入口已挂替身，写那一步被它吃掉（`AutoStartRegistryWriteForTest`）→ 红侧也安全。
                var regWritesBefore = MainForm.AutoStartRegistryWritesForTest;
                try
                {
                    MainForm.AutoStartRegistryDisabledForTest = false;
                    uiForm.SetAutoStartForTest(false);
                }
                finally { MainForm.AutoStartRegistryDisabledForTest = true; }
                Check("DEC-164 闸放开后真路径会走到「要写」那一步（计数 +1）= 上面那条 `真写 == 0` 不是空判据；替身吃掉落盘",
                    MainForm.AutoStartRegistryWritesForTest == regWritesBefore + 1
                    && MainForm.AutoStartRegistryDisabledForTest,
                    $"放开前={regWritesBefore} 放开后={MainForm.AutoStartRegistryWritesForTest} 还原={MainForm.AutoStartRegistryDisabledForTest}");

                // DEC-165①（清单第 5 条 a 档 = 他批"可以做"）：幽灵起件**剩的那半条** —— 件已经开始加载之后对面才走。
                // 判据 = 这一发的令牌确实被取消 + 网关此刻**没有别的在途请求**（粗判据，口径同 DEC-157：宁可留着也不误停）。
                Check("DEC-165① 纯判据四面：只有「这一发已走 + 没有别人在途」才收（那一件可能正被别人用着）",
                    MainForm.ShouldUnloadAbandonedStart(true, 0) && !MainForm.ShouldUnloadAbandonedStart(true, 1)
                    && !MainForm.ShouldUnloadAbandonedStart(false, 0) && !MainForm.ShouldUnloadAbandonedStart(false, 2),
                    $"走了+空={MainForm.ShouldUnloadAbandonedStart(true, 0)} 走了+有人={MainForm.ShouldUnloadAbandonedStart(true, 1)} "
                    + $"没走+空={MainForm.ShouldUnloadAbandonedStart(false, 0)}");
                Check("DEC-165① 那句话点名模块与件名（别写成「某一件」）",
                    MainForm.AbandonedStartLine("Strata", "iq3_s").Contains("Strata")
                    && MainForm.AbandonedStartLine("Strata", "iq3_s").Contains("iq3_s"),
                    MainForm.AbandonedStartLine("Strata", "iq3_s"));
                // 走真路径那一步（判据 / 日志 / 那一击全过本体）：闸外两档不动手，闸内那一档真去收。
                // 件名给一个跑不着的 tag = 模块那两行实现里的「此刻跑的正是这一件」当场不成立 → 一个进程都不碰。
                int unloadBefore = MainForm.UnloadAbandonedStartedCallsForTest;
                bool fired = uiForm.TryUnloadAbandonedStartedForTest(MainForm.StrataKey, "zz-selftest-not-running", true, 0);
                Check("DEC-165① 走真路径：这一档真去收了（证人位 +1）；另外两档不动手（有人等 / 没走）",
                    fired && MainForm.UnloadAbandonedStartedCallsForTest == unloadBefore + 1
                    && !uiForm.TryUnloadAbandonedStartedForTest(MainForm.StrataKey, "zz-selftest-not-running", true, 1)
                    && !uiForm.TryUnloadAbandonedStartedForTest(MainForm.StrataKey, "zz-selftest-not-running", false, 0),
                    $"真收={fired} 证人位 {unloadBefore}→{MainForm.UnloadAbandonedStartedCallsForTest}");
                // 接线三处（只能扫源码：网关那一处调用 + 两族各一行实现）—— 漏一处这一档就是空的。
                // 三个 needle 全**拆开拼**（BUG-066① 的先例：整串写在本文件里会被自己扫到 = 必假红）。
                var abandonCallHits = CodeHits("MaybeUnloadAbandonedStarted(View(h.K" + "ey), tag");
                var abandonHookHits = CodeHits("UnloadAbandonedStarted: tag" + " =>");
                Check("DEC-165① 接线三处都在：网关那一处调用（1）+ 两族各一行实现（2）",
                    (abandonCallHits is 1 or -1) && (abandonHookHits is 2 or -1),
                    $"调用 {abandonCallHits} / 实现 {abandonHookHits}");
                // DEC-167（第 45 轮只读审查 F2）：真去收了之后这一发当没发生过 —— **抛取消早退**，不 return port。
                // 旧写法 return port：网关继续把请求转发给"刚被收掉"的上游（UiPost 异步落盘，与 ConnectAsync 竞态），
                // 客户端已走 → 转发必失败，只多一行「网关转发失败」。抛出由网关那条 catch 收口 = 不写响应、只留一行落点。
                // needle 拆开拼（BUG-066①）。红侧 = 把 throw 按回 return port，这一条当场红。
                var abandonThrowHits = CodeHits("throw new OperationCanceledException(\"这一发请求已经走" + "了");
                Check("DEC-167 F2 收掉刚起来的那一件之后抛取消早退（不把端口交回网关去转发给刚被收掉的上游）",
                    abandonThrowHits is -1 or 1, "命中 " + abandonThrowHits);

                // DEC-165②（他批"放通用"）：API Key 三模块共用一枚 —— 三个物化点 + 壳自己的探活都要认它。
                var keyS = new Settings { ApiKey = "sk-selftest-abc" };
                var llamaKeyed = MainForm.BuildLlamaArgs(keyS, "127.0.0.1", 8080, @"C:\tmp\p.ini");
                var llamaBare = MainForm.BuildLlamaArgs(new Settings(), "127.0.0.1", 8080, @"C:\tmp\p.ini");
                int ki = llamaKeyed.IndexOf("--api-key");
                Check("DEC-165② llama 命令行：设了 key 就带 `--api-key <key>`；留空一枚都不带（= 引擎默认不鉴权）",
                    ki >= 0 && ki + 1 < llamaKeyed.Count && llamaKeyed[ki + 1] == "sk-selftest-abc"
                    && !llamaBare.Contains("--api-key"),
                    $"带 key 下标={ki} 留空也带={llamaBare.Contains("--api-key")}");
                var ninferSec = new NinferIni.Section { Name = "zz-selftest-key", File = "zz.ninfer", Args = "--max-context 8192" };
                var ninferKeyed = uiForm.BuildNinferArgvLine(ninferSec, keyS, NinferIni.BackendWin, 18099);
                var ninferBare = uiForm.BuildNinferArgvLine(ninferSec, new Settings(), NinferIni.BackendWin, 18099);
                Check("DEC-165② Ninfer 命令行：设了 key 就带 `--api-key <key>`；留空不碰（段里手写过的 key 照旧生效）",
                    ninferKeyed.Contains("--api-key sk-selftest-abc") && !ninferBare.Contains("--api-key"),
                    ninferKeyed);
                Check("DEC-165② 壳自己那几行 `$ 命令行` 要过 RedactApiKey（密钥不许落进界面日志、内存历史与 logs\\*.log）",
                    !MainForm.RedactApiKey("$ llama-server.exe --api-key sk-selftest-abc --port 8080", "sk-selftest-abc").Contains("sk-selftest-abc")
                    && MainForm.RedactApiKey("$ 没有密钥的一行", "").Contains("没有密钥的一行"),
                    MainForm.RedactApiKey("$ llama-server.exe --api-key sk-selftest-abc", "sk-selftest-abc"));
                Check("DEC-165② 探活请求带的是 `Authorization: Bearer <key>`；留空 = 一个头都不加（没设 key 的行为一字未变）",
                    MainForm.ApiAuthHeaderValue("sk-x") == "Bearer sk-x" && MainForm.ApiAuthHeaderValue("  ") is null,
                    $"{MainForm.ApiAuthHeaderValue("sk-x")} / {MainForm.ApiAuthHeaderValue("  ") ?? "(null)"}");
                Check("DEC-165② 落点 = 设置页「通用 → 接口」（他批的「放通用、所有模块一套」）",
                    SRow("settings-通用").Contains("API Key"), "通用页那一格在不在：" + SRow("settings-通用").Contains("API Key"));
                // 接线也只能扫源码：三处 `$ 命令行` 都要过 RedactApiKey（llama 1 + Ninfer 2），探活那一处要真带头。
                // needle 同样拆开拼；消息里不许出现整串（否则被自己扫到）。
                var redactHits = CodeHits("LogL(Redact" + "ApiKey") + CodeHits("LogN(Redact" + "ApiKey");
                var authHits = CodeHits("ApiAuthHeaderValue(Api" + "KeyValue())");
                Check("DEC-165② 三处 `$ 命令行` 都过了脱敏（1 + 2）、探活那一处真带头（1）—— 漏一处就是密钥落日志 / 设了 key 就探不通",
                    redactHits is 3 or -2 && authHits is 1 or -1,
                    $"脱敏 {redactHits} / 带头 {authHits}");
                // DEC-146（甲档）：左栏那一格改口「运行信息」。两个 needle 都**拆开拼**（BUG-066① 那条先例：
                // 整串写在本文件里会被自己扫到 = 必假红）。判的是"界面上那一枚的字面"与"旧字面已退出源码"。
                var newTitleHits = CodeHits("\"" + "运行" + "信息" + "\"");
                var oldTitleHits = CodeHits("\"" + "控" + "制台" + "\"");
                Check("DEC-146 左栏标题字面 =「运行信息」一处、旧字面「控制台」在源码里 0 命中（几何与 _fTitle 一字未动）",
                    newTitleHits is -1 or 1 && oldTitleHits is -1 or 0,
                    newTitleHits < 0 ? "用例前提：源码不在旁边（实跑目录跑 --selftest 就是这种），这条只登记"
                                     : $"新={newTitleHits} 旧={oldTitleHits}");

                uiForm.SetStatusBarItemsForTest("prefill,decode,mtp", "prefill,decode");   // 出门恢复默认三档，后面那些用例认的就是它
                uiForm.OnScreenForcedForTest = false;
                uiForm.SwitchModuleForTest(page0);

                // 托盘菜单改版（DEC-041，用户挑定：变体 A 组头带状态 + 深色）：结构、置灰、组头不带端口
                // DEC-107：Ninfer 那一组多一枚（一后端一窗 → 「检查更新 · WIN」「检查更新 · WSL」），
                // 期望行集从 UpdateSidesFor 现算 —— 别再写死每模块 4 行。
                var tray = uiForm.TrayMenuRowsForTest();
                W("tray: " + string.Join(" | ", tray));
                List<string> TrayWanted(IEnumerable<LauncherModule> mods)
                {
                    // DEC-202：第二行是「检查更新 · 本程序」（在「设置」上方；它不是模块，所以不进下面那段循环）
                    var e = new List<string> { "显示主界面/可点", "检查更新 · 本程序/可点", "设置/可点", "──" };
                    foreach (var m in mods)
                    {
                        e.Add(m.DisplayName + " · * 组头占位");   // 组头文案带实时状态，只断"以显示名开头 + 是组头"
                        e.Add("启动服务/可点"); e.Add("停止服务/置灰");
                        foreach (var side in MainForm.UpdateSidesFor(m.Key))
                            e.Add(m.Key == MainForm.NinferKey
                                ? "检查更新 · " + (side == "wsl" ? "WSL" : "WIN") + "/可点"
                                : "检查更新/可点");
                    }
                    e.Add("──"); e.Add("退出/可点");
                    return e;
                }
                bool TrayMatches(List<string> got, IEnumerable<LauncherModule> mods)
                {
                    var want = TrayWanted(mods);
                    if (got.Count != want.Count) return false;
                    for (int i = 0; i < want.Count; i++)
                        if (want[i].EndsWith("组头占位"))
                        {
                            var prefix = want[i][..want[i].IndexOf(" · ", StringComparison.Ordinal)];
                            if (!got[i].EndsWith("/组头") || !got[i].StartsWith(prefix + " · ", StringComparison.Ordinal)) return false;
                        }
                        else if (got[i] != want[i]) return false;
                    return true;
                }
                Check("托盘 = 显示主界面 / 设置 → 每启用模块一组（启动·停止·检查更新；Ninfer 两枚一后端一枚）→ 退出（按模块表穷举，DEC-107）",
                    TrayMatches(tray, ModuleRegistry.All), string.Join(" | ", tray));
                Check("托盘弹出方向：光标在下半屏自点击点向上弹、上半屏向下弹（都从点击处长出来）",
                    MainForm.TrayDropDirection(1000, new Rectangle(0, 0, 1920, 1080)) == ToolStripDropDownDirection.AboveLeft
                    && MainForm.TrayDropDirection(200, new Rectangle(0, 0, 1920, 1080)) == ToolStripDropDownDirection.BelowLeft);
                Check("托盘菜单带图标列（左侧色块）：ShowImageMargin 开、底色比菜单底深一档",
                    uiForm.TrayShowImageMarginForTest, "margin=" + uiForm.TrayShowImageMarginForTest);
                Check("托盘组头带状态但不带端口；已去掉打开网页、不放打开配置",
                    tray.All(x => !x.Contains("端口")) && tray.All(x => !x.Contains("打开网页"))
                    && tray.All(x => !x.Contains("打开配置")), string.Join(" | ", tray));
                uiForm.SetModuleEnabledForTest(strata: true, llama: false);
                var trayOne = uiForm.TrayMenuRowsForTest();
                Check("停用的模块整组不出现（与主界面「全隐身」同语义）",
                    TrayMatches(trayOne, ModuleRegistry.All.Where(m => m.Key != MainForm.LlamaKey))
                    && trayOne.All(x => !x.StartsWith("llama.cpp")), string.Join(" | ", trayOne));
                uiForm.SetModuleEnabledForTest(strata: true, llama: true);

                // ---- DEC-160（BUG-068① 结案）：外部服务这一档 = 自检不真探、形状改吃喂样本 ----
                var extSkip0 = MainForm.ExternalProbeSkippedForTest;
                var extProbe0 = MainForm.ExternalHttpProbesForTest;
                Task.Run(() => uiForm.ProbeExternalAsync()).GetAwaiter().GetResult();
                Check("DEC-160 外部探活那道按进程闸：这条路真被走到、真被挡下、一个端口都没真问过（不是空判据）",
                    MainForm.ExternalProbeDisabledForTest
                    && MainForm.ExternalProbeSkippedForTest > extSkip0
                    && MainForm.ExternalHttpProbesForTest == extProbe0,
                    $"挡下 {MainForm.ExternalProbeSkippedForTest - extSkip0} 次 / 真探 {MainForm.ExternalHttpProbesForTest - extProbe0} 次");
                // 那一档的界面形状照旧要断得到 —— 喂进各模块自己那个判据位、走同一条 RefreshStatusUi（真路径）
                var extPage0 = uiForm.ActiveModuleForTest;
                uiForm.SwitchModuleForTest(MainForm.StrataKey);
                uiForm.SetExternalForTest(MainForm.StrataKey, true);
                var strataExtBtn = uiForm.ModelActionTextForTest;
                var strataExtNav = uiForm.StartStopTextForTest;
                var strataExtTray = uiForm.TrayMenuRowsForTest();
                uiForm.SetExternalForTest(MainForm.StrataKey, false);
                var strataOffBtn = uiForm.ModelActionTextForTest;
                var strataOffNav = uiForm.StartStopTextForTest;
                Check("DEC-160 喂样本走真路径：Strata「外部服务在跑」那一档 = 模型行「卸载」+ 左栏「停止」+ 托盘那组跟着翻；喂回去立刻复原",
                    strataExtBtn == "卸载" && strataExtNav == "停止" && strataOffBtn == "加载" && strataOffNav == "启动"
                    && strataExtTray.Any(r => r.StartsWith("Strata") && r.Contains("外部服务") && r.EndsWith("/组头"))
                    && strataExtTray.Contains("启动服务/置灰") && strataExtTray.Contains("停止服务/可点"),
                    $"在跑 {strataExtBtn}/{strataExtNav} | 复位 {strataOffBtn}/{strataOffNav} | 托盘 " + string.Join(" ; ", strataExtTray));
                uiForm.SetExternalForTest(MainForm.LlamaKey, true);
                uiForm.SwitchModuleForTest(MainForm.LlamaKey);
                var llamaExtNav = uiForm.StartStopTextForTest;
                var llamaExtStatus = uiForm.ActiveStatusTextForTest.Text;
                string LlamaLight() => string.Join(";", uiForm.LightRowsForTest.Where(r => r.Key == MainForm.LlamaKey).Select(r => r.Desc));
                var llamaExtLight = LlamaLight();
                uiForm.SetExternalForTest(MainForm.LlamaKey, false);
                uiForm.SwitchModuleForTest(MainForm.LlamaKey);
                var llamaOffNav = uiForm.StartStopTextForTest;
                var llamaOffLight = LlamaLight();
                Check("DEC-160 llama 那一族同一条路：外部实例在跑 = 左栏「停止」+ 灯不再是「已停止」那一档（本族没有「外部服务」这句，DEC-141 起它走 running 那一档）；喂回去两处一起复原",
                    llamaExtNav == "停止" && !llamaExtLight.StartsWith("已停止") && llamaExtStatus != "已停止"
                    && llamaOffNav != "停止" && llamaOffLight.StartsWith("已停止"),
                    $"在跑 nav={llamaExtNav} 灯={llamaExtLight} | 复位 nav={llamaOffNav} 灯={llamaOffLight}");
                uiForm.SetExternalForTest(MainForm.NinferKey, true);
                uiForm.SwitchModuleForTest(MainForm.NinferKey);
                var ninferExtNav = uiForm.StartStopTextForTest;
                uiForm.SetExternalForTest(MainForm.NinferKey, false);
                uiForm.SwitchModuleForTest(extPage0);
                Check("DEC-160 Ninfer 也喂得动（第三家不许漏 —— 模块表那三行各有自己的判据位）",
                    ninferExtNav == "停止", "nav=" + ninferExtNav);
                // DEC-161（他点名「Ninfer 在 win 下的日志,最前面的时间戳重复了」）：引擎自己打了时间戳的行，壳不再补一枚。
                var stampWin = "2026-10-13 02:51:22.965  INFO  throughput | 5.0s | decode 145.9 tok/s (730 tok)";
                var stampShell = "$ 网关已监听 127.0.0.1:8077（引擎跑内部端口）";
                var stampNoDate = "2026-10-13 02:56:42.485  INFO  starting engine";   // WSL 那一代的同一形状（本机探针实测）
                Check("DEC-161 判据三面：两代引擎那两行都算「自己带戳」；壳自己写的话与没戳的引擎行照旧要补",
                    MainForm.CarriesOwnStamp(stampWin) && MainForm.CarriesOwnStamp(stampNoDate)
                    && !MainForm.CarriesOwnStamp(stampShell) && !MainForm.CarriesOwnStamp("[strata] answering…")
                    && !MainForm.CarriesOwnStamp("网关收到 POST /v1/chat/completions（model=qwen3.8-27b-gsq）"),
                    $"win={MainForm.CarriesOwnStamp(stampWin)} wsl={MainForm.CarriesOwnStamp(stampNoDate)} 壳={MainForm.CarriesOwnStamp(stampShell)}");
                var atR = new DateTime(2026, 10, 13, 2, 51, 22);
                Check("DEC-161 一行的写法只有一个家：带戳的原样交回（界面上只剩引擎那一枚），没戳的按原格式补、文件那一档补带日期的那枚",
                    MainForm.LogLine(atR, stampWin, false) == stampWin
                    && MainForm.LogLine(atR, stampNoDate, true) == stampNoDate
                    && MainForm.LogLine(atR, stampShell, false) == "02:51:22 " + stampShell
                    && MainForm.LogLine(atR, stampShell, true) == "2026-10-13 02:51:22 " + stampShell,
                    "界面 " + MainForm.LogLine(atR, stampWin, false).Substring(0, 12) + " / 壳 " + MainForm.LogLine(atR, stampShell, false).Substring(0, 9));
                // 三个写点各扫一条**旧字面**（needle 一律拆开拼 = BUG-066① 那条老账：整串写在本文件里会被自己扫到）
                var oldUiHits = CodeHits("AppendText($" + "\"{at:HH:mm:ss}");
                var oldBuildHits = CodeHits("at.ToString(\"HH:" + "mm:ss\")");
                var oldFileHits = CodeHits("{at:yyyy-MM-dd " + "HH:mm:ss} {text}");
                // 真路径那两面：界面上那两行才是他看见的东西（只断纯函数会漏"接错了线"这一族）
                uiForm.LogToUi("2026-10-13 02:51:22.965  INFO  throughput | 5.0s | decode 145.9 tok/s (730 tok)");
                uiForm.LogToUi("DEC-161 探针：这一行没有自带时间戳");
                var box161 = uiForm.LogBoxTextForTest.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
                var stamped161 = box161.LastOrDefault(l => l.Contains("throughput | 5.0s"));
                var plain161 = box161.LastOrDefault(l => l.Contains("DEC-161 探针：这一行没有自带时间戳"));
                Check("DEC-161 真路径：框里那一行引擎自带戳的就只有那一枚，壳自己写的那行照旧带一枚时分秒在前",
                    stamped161 is not null && stamped161.StartsWith("2026-10-13 02:51:22.965")
                    && plain161 is not null && System.Text.RegularExpressions.Regex.IsMatch(plain161, "^\\d{2}:\\d{2}:\\d{2} DEC-161 探针"),
                    "带戳行 = " + (stamped161 ?? "(没找到)") + "｜没戳行 = " + (plain161 is null ? "(没找到)" : plain161[..Math.Min(30, plain161.Length)]));
                Check("DEC-161 三个写点（实时 / 重建 / 文件）都收到同一个家，旧那三处字面拼接一处不许留",
                    oldUiHits is -1 or 0 && oldBuildHits is -1 or 0 && oldFileHits is -1 or 0,
                    $"实时 {oldUiHits} / 重建 {oldBuildHits} / 文件 {oldFileHits}");
                // DEC-160 第 2 条：降噪表 **C 档（引擎侧 `log-level` 那一枚旗标）实测否掉** ——
                // 本轮真起 WIN 引擎三档各跑一遍（件 14 GB、ctx 262144、池 55296、一发 3k token 长提示 + 一发短的）：
                //   info（默认）98 行 / 噪声 58 = 59.2%，req# 6 条、throughput 2 条、listening 1 条
                //   warning      66 行 / 噪声 55 = **83.3%**，req# 0、throughput 0、listening **0**
                //   error        61 行 / 噪声 50 = 82.0%，同上全 0
                // 那三类台账（ring budgets 36→33→28、kvmem KEPT/SELECT 各 10→10、ring retrieve 2→2）**根本没被压住**，
                // 而**信号行与就绪行倒是先没了** —— `listening on http://` 正是壳判 ready 的唯一凭据。
                // 于是这条链整个作废：壳一处都不许注入它（旧写法只会在文档里挂着，将来有人"顺手接上"就把唤醒打成永远超时）。
                var logLevelHits = CodeHits("--log" + "-level");
                Check("DEC-160 第 2 条：壳一处都不注入引擎侧的 log-level 旗标（实测压不住那三类台账、还把自己的就绪行一起压掉 = C 档作废）",
                    logLevelHits is -1 or 0, "命中 " + logLevelHits + " 处");
                var extGateHits = CodeHits("if (ExternalProbeDisabledForTest) { ExternalProbeSkippedForTest++; re" + "turn; }");
                Check("DEC-160 那道闸只长在探活本体那一个入口（别在调用点各写一遍 = 第二个家）",
                    extGateHits is -1 or 1, "命中 " + extGateHits + " 处");

                Check("组头状态词复用灯的判据（去掉端口段）",
                    MainForm.LightWords("运行中 · 路由模式 · 端口 8081") == "运行中 · 路由模式"
                    && MainForm.LightWords("已停止 · 端口 8080") == "已停止"
                    && MainForm.LightWords("待机 · 模型未加载 · 端口 8082") == "待机 · 模型未加载"
                    && MainForm.LightWords("异常 · 启动失败") == "异常 · 启动失败",
                    MainForm.LightWords("运行中 · 路由模式 · 端口 8081"));
                // ③ 每秒进度行的节流（DEC-038）：8 条 1 秒一条 -> 界面只上 2 条；done 行不误伤；历史仍全量
                var seed0 = new DateTime(2026, 10, 3, 3, 4, 5);
                var seed = new List<(DateTime, string, string)>();
                for (int si = 0; si < 8; si++)
                    seed.Add((seed0.AddSeconds(si), "strata",
                        $"[strata] thinking: {40 + si * 7} of max 32768 tokens, 72.0 tok/s, {9 + si} s"));
                seed.Add((seed0.AddSeconds(8), "strata",
                    "[strata] done: 96 tokens in 9 s (69.8 tok/s) (stop, cancel=False), expert cache 75.1% hit"));
                uiForm.SeedLogHistoryForTest(seed.ToArray());
                string throttled = uiForm.LogBoxTextForTest;
                int shownProgress = throttled.Split("[strata] thinking").Length - 1;
                Check("节流生效：8 条每秒行只放 2 条上界面（窗口开头那条 + 满 5 秒那条）",
                    shownProgress == 2, "界面里的进度行=" + shownProgress);
                Check("done 收尾行照常上屏（节流不误伤）", throttled.Contains("[strata] done:"));
                Check("历史仍是全量 9 条（节流只影响界面，文件照写全量）",
                    uiForm.LogHistoryCountForTest == 9, uiForm.LogHistoryCountForTest.ToString());

                // DEC-111③：界面那只框的上限。实时这一路以前只有 AppendText、没有上限 ——
                // Ninfer 引擎生成期间实测 60 行/秒，页开着十几分钟框里就是三万多行文本（只有切页/清屏才截）。
                // 灌 6000 行进历史、再走一次真实写入：框里只许看到最近 5000 行，最旧那行必须被挤出去、最近那行必须在。
                var big = new List<(DateTime, string, string)>();
                for (int bi = 0; bi < 6000; bi++) big.Add((seed0.AddSeconds(bi), "strata", "DEC-111 上界用例行 " + bi));
                uiForm.SeedLogHistoryForTest(big.ToArray());
                uiForm.LogForTest("DEC-111 触顶那一行", "strata");
                string boxText = uiForm.LogBoxTextForTest;
                int boxLines = boxText.Split('\n').Length - (boxText.EndsWith("\n") ? 1 : 0);
                Check("DEC-111 界面框上限：灌 6000 行 + 真实写一行 -> 框里不超过 5000 行，且最近的在、最旧的被挤掉",
                    boxLines <= MainForm.LogHistoryMaxLines
                    && boxText.Contains("DEC-111 触顶那一行")
                    && boxText.Contains("DEC-111 上界用例行 5999")
                    && !boxText.Contains("DEC-111 上界用例行 0" + Environment.NewLine),
                    "框里行数=" + boxLines + "｜最旧那行还在=" + boxText.Contains("DEC-111 上界用例行 0" + Environment.NewLine));

                // DEC-163（第 41 轮，他核过体积后拍"B 路径"）：八类引擎台账**照记**（文件与内存历史），
                // 只做一条简单去重；界面由「调试信息过滤」挡。真路径喂样本（DEC-126 那条规矩：他 ini 里开关的值
                // 不许决定用例红绿）；去重状态是**按类**的、跨用例留着，这里每条样本都带唯一后缀，互不踩。
                {
                    string nRing163 = "[ring] budgets: skeleton=4, evidence=128, recent=0, free=733 (pool 865)";
                    string nWarn163 = "DEC-163 探针：同一条警告出现两次";
                    // 先把内存历史清空：上一条用例刚把它灌到 5001，再 Add 会触发批量裁剪（5002 → 4002）=
                    // 条数算式失真 —— r40 那次就是这么红的（写条数类断言先看这条链有没有批量裁剪）。
                    uiForm.SeedLogHistoryForTest();
                    int h0 = uiForm.LogHistoryCountForTest;
                    uiForm.LogForTest(nRing163, "ninfer");
                    uiForm.LogForTest(nRing163, "ninfer");                 // 同内容连发 → 去重挡下
                    Check("DEC-163 真路径去重：同类同内容连发两条只落一条（首条放行、第二条不落）",
                        uiForm.LogHistoryCountForTest == h0 + 1 && uiForm.LogHistoryTailForTest(1).Contains("free=733"),
                        "历史 " + h0 + " -> " + uiForm.LogHistoryCountForTest);
                    uiForm.LogForTest("[ring] budgets: skeleton=4, evidence=128, recent=0, free=734 (pool 866)", "ninfer");
                    Check("DEC-163 真路径去重：内容变了就再落一条（去重不是丢弃，前后都有痕迹）",
                        uiForm.LogHistoryCountForTest == h0 + 2, "历史 " + h0 + " -> " + uiForm.LogHistoryCountForTest);
                    uiForm.LogForTest(nWarn163, "ninfer");
                    uiForm.LogForTest(nWarn163, "ninfer");
                    Check("DEC-163 不误伤：一字不差的信号行**不去重**（两条都落 —— 重复的警告是真事件）",
                        uiForm.LogHistoryCountForTest == h0 + 4, "历史 " + h0 + " -> " + uiForm.LogHistoryCountForTest);
                    // 界面那一头：过滤开 = 挡（记录不受影响）/ 过滤关 = 显示同一份去重后的流
                    string page163 = uiForm.ActiveModuleForTest;
                    uiForm.SwitchModuleForTest(MainForm.NinferKey);
                    uiForm.SetFilterDebugInfoForTest(true);
                    uiForm.LogForTest("[ninfer] mask hidden: which=text site=mapped span=9001 resident=1 hidden=2", "ninfer");
                    bool hid163 = !uiForm.LogBoxTextForTest.Contains("span=9001");
                    uiForm.SetFilterDebugInfoForTest(false);
                    uiForm.LogForTest("[ninfer] mask hidden: which=text site=mapped span=9002 resident=1 hidden=2", "ninfer");
                    bool sho163 = uiForm.LogBoxTextForTest.Contains("span=9002");
                    uiForm.SetFilterDebugInfoForTest(true);
                    uiForm.SwitchModuleForTest(page163);
                    Check("DEC-163 真路径界面面：过滤开 = 界面挡掉（记录不受影响）、过滤关 = 显示同一份去重后的流",
                        hid163 && sho163, $"过滤开隐藏={hid163} 过滤关显示={sho163}");
                }
    }
}
