using System.Text;

namespace MultiLauncher;

// 判据 14（DEC-222，2026-10-25 他报"删掉 llama.cpp 里某个模型的配置段，重启服务之后模型列表里那件还在，
// 只能退出程序再打开才行"）：三个模块的模型列表在**拉起服务那一刻**按家底戳重扫。
// 这里只喂样本、只碰 data\tmp 里那几窝副本（真家底由自检 preamble 的 UseTempXxxHomeForTest 改道），
// 不读也不写任何一份真配置。
internal static partial class SelfTest
{
    internal static void ModelRescanChecks()
    {
        // --- 14a~14b（llama）：删掉一段 → 拉起服务那一刻列表跟着少；戳没变则一个字节都不读 ---
        // 夹具直接搭在自检 preamble 已改道的 selftest-llama 窝里（真件在跑自检前就被拷成副本，动它不碰他的文件）。
        {
            var iniPath = MainForm.RouterIniPath();
            var before = File.Exists(iniPath) ? File.ReadAllText(iniPath) : null;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(iniPath) ?? ".");
                File.WriteAllText(iniPath, "[la1]\nmodel = C:\\no-such\\a.gguf\n\n[la2]\nmodel = C:\\no-such\\b.gguf\n",
                    new UTF8Encoding(false));
                // 造一个实例：构造那一趟 RefreshModels 会把 llama 的列表戳记成"这两个文件此刻的样子" = 基线。
                using var f = new MainForm();
                f.CreateControl();
                MainForm.ResetModelListStampsForTest();
                f.RefreshModelsForTest(MainForm.LlamaKey);                 // 只重扫 llama（DEC-097⑤ 的作用域口径）
                int n0 = f.LlamaModelsForTest.Count;
                bool hasLa2 = f.LlamaModelsForTest.Any(m => m.Tag == "la2");
                // 手删掉 la2 那一段（就是他在编辑器里按几下 Delete 那一手），并把原件时刻推到未来让戳真变
                File.WriteAllText(iniPath, "[la1]\nmodel = C:\\no-such\\a.gguf\n", new UTF8Encoding(false));
                File.SetLastWriteTimeUtc(iniPath, DateTime.UtcNow.AddSeconds(5));
                var hits = f.RescanModelsIfHomeChanged(MainForm.LlamaKey);
                var tags = f.LlamaModelsForTest.Select(m => m.Tag).ToList();
                Check("DEC-222 判据14a llama：删掉一段之后拉起服务那一刻列表跟着少（重扫真的发生了、那件真的没了）",
                    hits && n0 >= 2 && hasLa2 && tags.Count == n0 - 1 && !tags.Contains("la2"),
                    $"hit={hits} n0={n0} 现在={string.Join("、", tags)}");
                var again = f.RescanModelsIfHomeChanged(MainForm.LlamaKey);
                Check("DEC-222 判据14b 戳没变就不重扫：同一次启动里第二次调直接返回 false（不白扫、不刷日志）",
                    !again, $"第二次={again}");
            }
            finally
            {
                MainForm.ResetModelListStampsForTest();
                try
                {
                    if (before is null) { if (File.Exists(iniPath)) File.Delete(iniPath); }
                    else File.WriteAllText(iniPath, before, new UTF8Encoding(false));
                }
                catch { /* 清不掉不影响结论 */ }
            }
        }

        // --- 14c（Strata）：删掉一个 strata-<tag>.json → 列表跟着少 ---
        // 单独建一只窗体、把 Root 指到 data\tmp 的草稿窝里量（改道只在这只实例的内存里，不写他机器上的 ini）。
        {
            var md = MainForm.Scratch("selftest-rescan-strata-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(md);
            var rootBak = "";
            MainForm? f = null;
            try
            {
                // 两份最小配置：ScanModels 认 <Strata 目录>\strata-*.json，args 里缺 --pack/--native 只会标未就绪，
                // 不影响"这一件在不在列表里"这条判据（我们要的正是"条数"）。
                File.WriteAllText(Path.Combine(md, "strata-rs1.json"), "{\"model_name\":\"rs1\",\"args\":[]}");
                File.WriteAllText(Path.Combine(md, "strata-rs2.json"), "{\"model_name\":\"rs2\",\"args\":[]}");
                f = new MainForm();
                f.CreateControl();
                rootBak = f.GetRootForTest();
                f.SetRootForTest(md);
                f.RefreshModelsForTest(MainForm.StrataKey);
                int n0 = f.ModelsForTest.Count;
                File.Delete(Path.Combine(md, "strata-rs2.json"));
                var hits = f.RescanModelsIfHomeChanged(MainForm.StrataKey);
                var tags = f.ModelsForTest.Select(m => m.Tag).ToList();
                Check("DEC-222 判据14c Strata：删掉一个模型配置之后拉起服务那一刻列表跟着少",
                    hits && n0 == 2 && tags.Count == 1 && tags.Contains("rs1") && !tags.Contains("rs2"),
                    $"hit={hits} n0={n0} 现在={string.Join("、", tags)}");
            }
            finally
            {
                MainForm.ResetModelListStampsForTest();
                if (f is not null && rootBak.Length > 0) f.SetRootForTest(rootBak);
                try { f?.Dispose(); } catch { /* 释放失败不影响结论 */ }
                try { Directory.Delete(md, true); } catch { /* 清不掉不影响结论 */ }
            }
        }

        // --- 14d（Ninfer）：删掉一段 → 列表跟着少；顺带 14e：被删的正是选中那件时，下拉落到别的件、不留幽灵 ---
        {
            var iniPath = MainForm.NinferIniPath();
            var before = File.Exists(iniPath) ? File.ReadAllText(iniPath) : null;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(iniPath) ?? ".");
                // backend = win 两段：WIN 侧默认开着（NinferWinOn 出厂 true），两件都会进列表。
                File.WriteAllText(iniPath,
                    "[nr1]\nbackend = win\nfile = C:\\no-such\\a.ninfer\n\n[nr2]\nbackend = win\nfile = C:\\no-such\\b.ninfer\n",
                    new UTF8Encoding(false));
                using var f = new MainForm();
                f.CreateControl();
                f.SetModuleEnabledForTest(MainForm.NinferKey, true);         // 这一组用例要先保证模块开着（只改内存）
                f.RefreshModelsForTest(MainForm.NinferKey);
                int n0 = f.NinferModelsForTest.Count;
                f.SwitchModuleRealForTest(MainForm.NinferKey);               // 让它成为激活页，才走得进"重摆下拉"那半条
                f.SelectModelForTest("nr1");
                bool selWas = f.SelectedTagForTest == "nr1";
                // 删掉正在选中的那一件（他实跑最容易踩的就是"删的就是当前这件"）
                File.WriteAllText(iniPath, "[nr2]\nbackend = win\nfile = C:\\no-such\\b.ninfer\n", new UTF8Encoding(false));
                File.SetLastWriteTimeUtc(iniPath, DateTime.UtcNow.AddSeconds(5));
                var hits = f.RescanModelsIfHomeChanged(MainForm.NinferKey);
                var tags = f.NinferModelsForTest.Select(m => m.Tag).ToList();
                Check("DEC-222 判据14d Ninfer：删掉一段之后拉起服务那一刻列表跟着少",
                    hits && n0 >= 2 && tags.Count == n0 - 1 && !tags.Contains("nr1"),
                    $"hit={hits} n0={n0} 现在={string.Join("、", tags)}");
                Check("DEC-222 判据14e 删的正是选中那件：重摆之后落到还剩下的那件（不留一枚指向已删段的幽灵选中）",
                    selWas && f.SelectedTagForTest == "nr2",
                    $"原来选中 nr1={selWas}｜现在={f.SelectedTagForTest}");
            }
            finally
            {
                MainForm.ResetModelListStampsForTest();
                try
                {
                    if (before is null) { if (File.Exists(iniPath)) File.Delete(iniPath); }
                    else File.WriteAllText(iniPath, before, new UTF8Encoding(false));
                }
                catch { /* 清不掉不影响结论 */ }
            }
        }

        // --- 14f：五个挂载点各**恰好一处**（源码扫描；-1 = 源码不在旁边（实跑目录跑自检），按既有口径不判红）---
        // 为什么非要数它：这个缺口当初就是"函数写好了、没人挂"型的（本仓那一族已经踩过四次，见 DEC-136 C-3 注释）。
        {
            int llamaHook = CodeHitsBetween("MainForm.LlamaLifecycle.cs", "void StartLlamaServer",
                "var s = CollectSettings()", "RescanModelsIfHomeChanged");
            int strataGate = CodeHitsBetween("MainForm.Service.cs", "void StartServer",
                "internal bool StartService", "RescanModelsIfHomeChanged");
            int strataSvc = CodeHitsBetween("MainForm.Service.cs", "internal bool StartService",
                "var s = StartSettingsFor", "RescanModelsIfHomeChanged");
            Check("DEC-222 判据14f-1 llama 拉起 / Strata 的 StartServer 与 StartService 三处各挂一次"
                  + "（只挂 StartService 的话，路由模式点「停止」再「启动」永远追不上文件 —— DEC-211 那条同族）",
                llamaHook is 1 or -1 && strataGate is 1 or -1 && strataSvc is 1 or -1,
                $"llama={llamaHook} StartServer={strataGate} StartService={strataSvc}");
            int ninferSrv = CodeHitsBetween("MainForm.Ninfer.cs", "void StartNinferServer",
                "var s = wakeFromSection", "RescanModelsIfHomeChanged");
            int ninferGw = CodeHitsBetween("MainForm.Ninfer.cs", "void StartNinferGateway",
                "if (!NinferGatewayUp) return;", "RescanModelsIfHomeChanged");
            Check("DEC-222 判据14f-2 Ninfer 两处各挂一次（起件 + 只立网关 —— 左栏「启动」走的是后者）",
                ninferSrv is 1 or -1 && ninferGw is 1 or -1,
                $"StartNinferServer={ninferSrv} StartNinferGateway={ninferGw}");
        }
    }
}