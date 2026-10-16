using System.Text;
using System.Text.Json.Nodes;

namespace MultiLauncher;

// 物化组（用例 1 / 1b / DEC-097④ rope / 2 / 3 / 4 / 4c / 4d / 4e / 4b）：界面参数 → 物化运行配置的全套判据。
// seeded + BaseCase() 是这个组的本地基准（原 RunBody try 里的局部量，随组一起搬来，一行未改）。
// 2026-10-15 从 RunBody 整体搬出；跨块的 models/target 见 SelfTest.State.cs。
internal static partial class SelfTest
{
    internal static void MaterializeAndKnobs()
    {
        // 拆分收尾账（2026-10-15）：target 提升成字段后，编译器不再跨方法跟踪"RunBody 已判过非空"
        //（那条 `target is null` 早退在 RunBody 里），于是这里出现 CS8602。给一个本地非空别名把可空性
        // 交回编译器 —— 零行为改动（判据仍只认 RunBody 那一条早退），只是把编译器的账平掉。
        var target = SelfTest.target!;
            var baseArgs = BaseArgs(target.ConfigPath);
            W("base args: " + string.Join(" ", baseArgs));
            bool baseHasVision = (JsonNode.Parse(File.ReadAllText(target.ConfigPath)) as JsonObject)?["vision"] is JsonObject;
            W("模型自带视觉: " + baseHasVision);
            // DEC-036：配置 env 里的 arena pin 是 0.1.35 的本机 WDDM 修复，留着对照"留空 = 沿用"
            var baseEnvPin = (JsonNode.Parse(File.ReadAllText(target.ConfigPath)) as JsonObject)?["env"]?["STRATA_ARENA_PIN_GIB"]?.GetValue<string>() ?? "";
            W("模型配置 env pin: '" + baseEnvPin + "'");
            // 用例的基准 = 界面首次运行时的样子：先按模型自己的配置播种可调项
            var seeded = new Settings { Root = root, Model = target.Tag };
            MainForm.ReadKnobsFromModel(root, target.Tag, seeded);
            W("播种参数: ctx=" + seeded.Context + " kv=" + seeded.Kv + " kvResident=" + seeded.KvResident + " vision=" + seeded.Vision);
            Settings BaseCase() => new Settings
            {
                Root = root, Model = target.Tag, Port = "8080", Host = "127.0.0.1",
                Context = seeded.Context, Kv = seeded.Kv, KvResident = seeded.KvResident,
                Spec = seeded.Spec, SpecMinP = seeded.SpecMinP, ExpertCache = seeded.ExpertCache, Prefill = seeded.Prefill,
                Vision = seeded.Vision, VramReserveMib = seeded.VramReserveMib,
                Gpu = seeded.Gpu, LayerSplit = seeded.LayerSplit,
            };

            // --- 用例 1：界面默认设置 ---
            var s1 = BaseCase();
            if (Materialize(s1, out var n1, out var e1))
            {
                var a1 = ArgList(n1);
                W("默认 args: " + string.Join(" ", a1));
                Check("默认：--max-context 与模型配置一致", Value(a1, "--max-context") == seeded.Context, Value(a1, "--max-context") ?? "");
                Check("默认：--kv 与模型配置一致", Value(a1, "--kv") == seeded.Kv);
                Check("默认：模型路径参数原样保留",
                    Value(a1, "--pack") == Value(baseArgs, "--pack") && Value(a1, "--native") == Value(baseArgs, "--native") &&
                    Value(a1, "--mtp") == Value(baseArgs, "--mtp") && Value(a1, "--ple-gguf") == Value(baseArgs, "--ple-gguf") &&
                    Value(a1, "--expert-profile") == Value(baseArgs, "--expert-profile"));
                Check("默认：受管 flag 各只出现一次", new[] { "--max-context", "--kv", "--spec", "--spec-min-p", "--expert-cache", "--prefill", "--vision", "--vram-reserve-mib" }
                    .All(f => a1.Count(x => x == f) <= 1));
                Check("默认：config 的 port / host 正确", n1?["port"]?.GetValue<int>() == 8080 && n1?["host"]?.GetValue<string>() == "127.0.0.1");
                Check("默认：视觉与模型能力一致", baseHasVision ? a1.Contains("--vision") : n1?["vision"] is null);
                Check("默认：arena pin 留空 = 沿用配置里的值（不删）",
                    (n1?["env"]?["STRATA_ARENA_PIN_GIB"]?.GetValue<string>() ?? "") == baseEnvPin, "配置 pin='" + baseEnvPin + "'");
            }
            else Check("默认设置物化成功", false, e1);

            // --- 用例 1b：从模型自己的配置播种（换模型 / 首次运行的路径） ---
            var s5 = new Settings { Root = root, Model = target.Tag };
            Check("能从模型配置读出可调项", MainForm.ReadKnobsFromModel(root, target.Tag, s5));
            W("读出参数: ctx=" + s5.Context + " kv=" + s5.Kv + " kvResident=" + s5.KvResident + " spec=" + s5.Spec +
              " vision=" + s5.Vision + " gpu='" + s5.Gpu + "' vram=" + s5.VramReserveMib);
            Check("读出：上下文等于模型配置", s5.Context == Value(baseArgs, "--max-context"));
            Check("读出：KV 驻留与模型配置一致", s5.KvResident == baseArgs.Contains("--kv-resident"));
            Check("读出：视觉与模型配置一致", (s5.Vision != "none") == baseHasVision);
            Check("读出：模型名 = 配置里的 model_name",
                s5.ApiModel == ((JsonNode.Parse(File.ReadAllText(target.ConfigPath)) as JsonObject)?["model_name"]?.GetValue<string>() ?? ""));
            Check("读出：spec / expert-cache / prefill 与模型一致",
                s5.Spec == Value(baseArgs, "--spec") && s5.ExpertCache == Value(baseArgs, "--expert-cache") && s5.Prefill == Value(baseArgs, "--prefill"));
            if (Materialize(s5, out var n5, out var e5))
            {
                var a5 = ArgList(n5);
                var m5 = Managed(a5);
                var mb = Managed(baseArgs);
                var diff = m5.Where(kv => kv.Value != mb[kv.Key]).Select(kv => kv.Key + ":" + kv.Value + "!=" + mb[kv.Key]).ToList();
                Check("读出的参数物化后与 SETUP 配的那一套完全一致", diff.Count == 0, string.Join(" ", diff));
            }
            else Check("播种用例物化成功", false, e5);

            var s6 = BaseCase();
            s6.ApiModel = "my-strata";
            if (Materialize(s6, out var n6, out var e6))
                Check("模型名写进 config 的 model_name", n6?["model_name"]?.GetValue<string>() == "my-strata");
            else Check("模型名用例物化成功", false, e6);

            // --- DEC-097④：上下文超过训练长度 → 壳自动补 rope（界面上不给它长格子） ---
            Check("rope 推法：等于训练长度不开、超了就按 因子 = 上下文/训练长度",
                MainForm.RopeFor(262144, 262144) is null
                && MainForm.RopeFor(262144, 393216)!.Value.Factor == "1.5"
                && MainForm.RopeFor(262144, 524288)!.Value is ("yarn", "2")
                && MainForm.RopeFor(262144, 786432)!.Value.Factor == "3"
                && MainForm.RopeFor(262144, 1048576)!.Value.Factor == "4"
                && MainForm.RopeFor(0, 524288) is null,        // 训练长度取不到 = 这条不推（不拿猜出来的因子写坏配置）
                MainForm.RopeFor(262144, 524288)?.ToString() ?? "null");
            Check("训练长度从模板反推：模板自己带 yarn 2 @524288 时基数还是 262144（不是把 524288 当基数再乘一次）",
                MainForm.TrainedContextOf(new List<string> { "--max-context", "262144" }) == 262144
                && MainForm.TrainedContextOf(new List<string> { "--max-context", "524288", "--rope-scaling", "yarn", "--rope-scale", "2" }) == 262144
                && MainForm.TrainedContextOf(new List<string> { "--max-context", "524288", "--rope-scale", "4" }) == 131072);
            var s512 = BaseCase();
            s512.Context = "524288";
            if (Materialize(s512, out var n512, out var e512))
            {
                var a512 = ArgList(n512);
                Check("物化到 512K：--rope-scaling yarn 与 --rope-scale 2 自动出现在 args 里",
                    Value(a512, "--rope-scaling") == "yarn" && Value(a512, "--rope-scale") == "2"
                    && Value(a512, "--max-context") == "524288",
                    "--rope-scaling=" + (Value(a512, "--rope-scaling") ?? "无") + " --rope-scale=" + (Value(a512, "--rope-scale") ?? "无"));
                var sBack = BaseCase();
                sBack.Context = seeded.Context;
                if (Materialize(sBack, out var nBack, out _))
                    Check("改回训练长度以内：那两枚自动摘掉（不许把上一次的 yarn 留在文件里）",
                        Value(ArgList(nBack), "--rope-scaling") is null && Value(ArgList(nBack), "--rope-scale") is null);
                else Check("回落那一发物化成功", false);
            }
            else Check("512K 用例物化成功", false, e512);
            // DEC-098④ 续：同一个触发条件下顺带补 `fit_max_tokens`（超训练长度 = 长提示 + 大 max_tokens 的局面，
            // 引擎默认对装不下的 max_tokens 回 400）；**模板里写过就照模板的**，那是他手编的那份、壳只读。
            var sFit = BaseCase();
            sFit.Context = "524288";
            if (Materialize(sFit, out var nFit, out _))
                Check("超训练长度且模板没写过 fit_max_tokens → 物化出来是 true（客户端不该被 400 弹回来）",
                    nFit?["fit_max_tokens"]?.GetValue<bool>() == true,
                    (nFit?["fit_max_tokens"]?.ToJsonString() ?? "没有这一键"));
            else Check("fit_max_tokens 用例物化成功", false);
            var sFitBack = BaseCase();
            sFitBack.Context = seeded.Context;
            if (Materialize(sFitBack, out var nFitBack, out _))
                Check("回到训练长度以内：这一枚跟着消失（物化每次从模板全量重建，不许留上一次的 true）",
                    nFitBack?["fit_max_tokens"] is null,
                    nFitBack?["fit_max_tokens"]?.ToJsonString() ?? "没有这一键");
            {
                var fitRoot = MainForm.Scratch("fit-root");
                try
                {
                    Directory.CreateDirectory(fitRoot);
                    File.WriteAllText(Path.Combine(fitRoot, "strata-fitt.json"),
                        "{\"args\":[\"--max-context\",\"262144\",\"--pack\",\"x\"],\"fit_max_tokens\":false,\"port\":8080}",
                        new UTF8Encoding(false));
                    var sOwn = BaseCase();
                    sOwn.Root = fitRoot; sOwn.Model = "fitt"; sOwn.Context = "524288"; sOwn.Vision = "none";
                    if (Materialize(sOwn, out var nOwn, out var eOwn))
                        Check("模板里自己写了 fit_max_tokens:false → 壳不覆盖（想拒绝\"悄悄截短输出\"就有这个出口）",
                            nOwn?["fit_max_tokens"]?.GetValue<bool>() == false,
                            (nOwn?["fit_max_tokens"]?.ToJsonString() ?? "没有这一键") + "｜" + eOwn);
                    else Check("自带 false 那一发物化成功", false, eOwn);
                }
                catch (Exception ex) { Check("fit_max_tokens 的模板夹具建得出来", false, ex.Message); }
            }
            Check("视觉能力探测只读 Strata 那份几 KB 的 json（llama 的 GGUF / Ninfer 的 .ninfer 是几 GB 的二进制）",
                MainForm.ReadsModelConfigForVision(ModelEntry.BackendStrata)
                && !MainForm.ReadsModelConfigForVision(ModelEntry.BackendLlama)
                && !MainForm.ReadsModelConfigForVision(ModelEntry.BackendNinfer));

            // --- 用例 2：全量改一遍（含边界值） ---
            var s2 = new Settings
            {
                Root = root, Model = target.Tag, Port = "8123", Host = "0.0.0.0", ApiKey = "secret-key",
                Gpu = "0,2", LayerSplit = "16", Context = "262144", Kv = "q4_0", KvResident = true,
                Spec = "6", SpecMinP = "0.35", ExpertCache = "4096", Prefill = "2048",
                Vision = "none", VramReserveMib = "900",
                ArenaPinGib = "12", HeadLate = true, ExtraArgs = "--my-flag 7",
            };
            if (Materialize(s2, out var n2, out var e2))
            {
                var a2 = ArgList(n2);
                W("改后 args: " + string.Join(" ", a2));
                Check("改后：--max-context = 262144", Value(a2, "--max-context") == "262144");
                Check("改后：--kv = q4_0 且 --kv-resident = 32768", Value(a2, "--kv") == "q4_0" && Value(a2, "--kv-resident") == "32768");
                Check("改后：--spec / --spec-min-p", Value(a2, "--spec") == "6" && Value(a2, "--spec-min-p") == "0.35");
                Check("改后：--expert-cache / --prefill", Value(a2, "--expert-cache") == "4096" && Value(a2, "--prefill") == "2048");
                Check("改后：视觉关闭时无 --vision、config 无 vision", !a2.Contains("--vision") && n2?["vision"] is null);
                Check("改后：附加参数原样追加在末尾", a2.Count >= 2 && a2[^2] == "--my-flag" && a2[^1] == "7");
                Check("改后：显卡 = 0,2 且 layer_split = 16", n2?["gpu"]?.GetValue<string>() == "0,2" && n2?["layer_split"]?.GetValue<string>() == "16");
                Check("改后：env 写入 arena pin 与 head late",
                    n2?["env"]?["STRATA_ARENA_PIN_GIB"]?.GetValue<string>() == "12" && n2?["env"]?["STRATA_HEAD_LATE"]?.GetValue<string>() == "1");
                Check("改后：端口 / host / api_key", n2?["port"]?.GetValue<int>() == 8123 && n2?["host"]?.GetValue<string>() == "0.0.0.0" && n2?["api_key"]?.GetValue<string>() == "secret-key");
            }
            else Check("改设置物化成功", false, e2);

            // --- 用例 3：单卡 + 留空 pin（原「低内存」用例；「低内存模式」已整链删除，DEC-075） ---
            var s3 = BaseCase();
            s3.Gpu = "0";
            s3.KvResident = false;
            if (Materialize(s3, out var n3, out var e3))
            {
                var a3 = ArgList(n3);
                Check("单卡：界面关 KV 驻留就真的不带 --kv-resident", !a3.Contains("--kv-resident"));
                Check("配置里若自带 --mmap-experts，原样透传（壳不再删也不再补，DEC-075）",
                    !baseArgs.Contains("--mmap-experts") || a3.Contains("--mmap-experts"));
                Check("留空：arena pin 沿用配置里的值（不删）",
                    (n3?["env"]?["STRATA_ARENA_PIN_GIB"]?.GetValue<string>() ?? "") == baseEnvPin, "配置 pin='" + baseEnvPin + "'");
                Check("留空且配置无 pin：不产生 env 节点", baseEnvPin.Length > 0 || n3?["env"] is null);
                Check("head late 关：env 里没有 STRATA_HEAD_LATE", n3?["env"]?["STRATA_HEAD_LATE"] is null);
                Check("单卡：gpu = 0 且无 layer_split", n3?["gpu"]?.GetValue<int>() == 0 && n3?["layer_split"] is null);
            }
            else Check("单卡用例物化成功", false, e3);

            var s4 = BaseCase();
            s4.Gpu = "";
            if (Materialize(s4, out var n4, out var e4))
                Check("留空显卡：config 里没有 gpu / layer_split", n4?["gpu"] is null && n4?["layer_split"] is null);
            else Check("留空显卡用例物化成功", false, e4);

            // --- 用例 4f：gpu 的三档形状「读侧 ↔ 写侧」互逆（收掉 DEC-138 那条「多卡 gpu 形状缺样本」）---
            // 写侧（`MainForm.RunConfig.cs` 那三条）产三档：留空 = 删键 / 单卡 = JSON 整数 / 多卡 = 逗号串；
            // 读侧（`ReadKnobsFromFile`）以前只有整数那档有样本，另两档一路落在 `JsonNode.ToString()` 上、从没验过。
            // 界面上那一格吃的是「0」「0,2」这种写法，所以三档都得读回它，不许带 JSON 标点（带引号的串会被下一次「保存」原样写进产物）。
            foreach (var (gpuJson, want, label) in new[]
            {
                ("0", "0", "整数（写侧单卡那档）"),
                ("\"0,2\"", "0,2", "逗号串（写侧多卡那档）"),
                ("[0,2]", "0,2", "数组（SETUP / 手改的形状）"),
            })
            {
                var gpuPath = Path.Combine(runTmp, "gpu-shape.json");
                File.WriteAllText(gpuPath, "{\"gpu\":" + gpuJson + "}", new UTF8Encoding(false));
                var sg = new Settings { Root = root, Model = target.Tag };
                bool gpuRead = MainForm.ReadKnobsFromFile(gpuPath, sg);
                Check("gpu 形状「" + label + "」读回界面那种写法", gpuRead && sg.Gpu == want,
                    (gpuRead ? "读出『" + sg.Gpu + "』" : "整份没读进") + "，需『" + want + "』");
            }

            // --- 用例 4：非法输入必须被挡住 ---
            Check("非法端口被拒", Rejects(new Settings { Root = root, Model = target.Tag, Port = "abc", Vision = "none" }, "端口"));
            Check("非法上下文被拒", Rejects(new Settings { Root = root, Model = target.Tag, Context = "1" }, "上下文"));
            Check("非法显卡被拒", Rejects(new Settings { Root = root, Model = target.Tag, Gpu = "x" }, "显卡"));
            Check("非法 spec-min-p 被拒", Rejects(new Settings { Root = root, Model = target.Tag, SpecMinP = "2" }, "spec-min-p"));
            Check("非法端口越界被拒", Rejects(new Settings { Root = root, Model = target.Tag, Port = "70000" }, "端口"));
            Check("模型不存在被拒", Rejects(new Settings { Root = root, Model = "no-such-model" }, "找不到"));
            Check("非法思考深度被拒", Rejects(new Settings { Root = root, Model = target.Tag, ReasoningEffort = "ultra" }, "思考深度"));
            if (!baseHasVision)
                Check("模型无视觉块时开启视觉被拒", Rejects(new Settings { Root = root, Model = target.Tag, Vision = "gpu" }, "视觉"));

            // --- 用例 4c：思考档位的共享文件 merge 语义（2026-10-04；写临时文件，不碰真实运行目录） ---
            {
                var md = MainForm.Scratch("selftest-shared-" + Guid.NewGuid().ToString("N")[..8]);
                Directory.CreateDirectory(md);
                var sp = Path.Combine(md, "x.shared-settings.json");
                try
                {
                    MainForm.MergeSharedReasoningEffort(sp, "low");
                    Check("共享文件：不存在就新建、键名写对（服务端读的就是 reasoning_effort）",
                        File.ReadAllText(sp).Contains("\"reasoning_effort\": \"low\""));
                    File.WriteAllText(sp, "{\"temperature\": 0.7, \"reasoning_effort\": \"high\"}");
                    MainForm.MergeSharedReasoningEffort(sp, "medium");
                    var t2 = File.ReadAllText(sp);
                    Check("共享文件：覆盖旧档位、别的键（temperature）原样保留",
                        t2.Contains("\"reasoning_effort\": \"medium\"") && t2.Contains("\"temperature\": 0.7") && !t2.Contains("high"));
                    File.WriteAllText(sp, "{ 这不是 JSON");
                    MainForm.MergeSharedReasoningEffort(sp, "high");
                    Check("共享文件：坏 JSON 按空重来（服务端对它的态度也是「读不了就当没有」）",
                        File.ReadAllText(sp).Contains("\"reasoning_effort\": \"high\""));
                }
                finally { try { Directory.Delete(md, true); } catch { /* 临时目录清理失败不影响结论 */ } }
            }

            // --- 用例 4d：思考档位怎么落进运行目录（物化）：填了 = 写；留空 = 一个字不碰（2026-10-04） ---
            {
                var md = MainForm.Scratch("selftest-effort-" + Guid.NewGuid().ToString("N")[..8]);
                Directory.CreateDirectory(md);
                const string etag = "fixture-effort";
                File.WriteAllText(Path.Combine(md, "strata-" + etag + ".json"),
                    "{\"model_name\":\"m\",\"args\":[\"--max-context\",\"131072\"]}");
                var effS = new Settings { Root = md, Model = etag, Port = "8080", Context = "131072", ReasoningEffort = "low", Vision = "none" };
                if (Materialize(effS, out _, out var effErr))
                {
                    var sp = Path.Combine(MainForm.RunConfigDir(), etag + ".shared-settings.json");
                    try
                    {
                        Check("物化：填了档位就写进 <tag>.shared-settings.json",
                            File.Exists(sp) && File.ReadAllText(sp).Contains("\"reasoning_effort\": \"low\""), sp);
                        File.WriteAllText(sp, "{\"reasoning_effort\": \"high\", \"temperature\": 0.7}");
                        var keep = File.ReadAllText(sp);
                        var effS2 = effS.Clone();
                        effS2.ReasoningEffort = "";
                        Materialize(effS2, out _, out _);
                        Check("物化：档位留空 = 一个字都不碰（聊天页里设的原样留着）",
                            File.ReadAllText(sp) == keep);
                    }
                    finally
                    {
                        try { File.Delete(sp); } catch { /* 清不掉不影响结论 */ }
                        try { File.Delete(Path.Combine(MainForm.RunConfigDir(), etag + ".json")); } catch { }
                    }
                }
                else Check("思考深度物化用例跑通", false, effErr);
                try { Directory.Delete(md, true); } catch { }
            }

            // --- 用例 4e：两个系统选择器的定位方式（2026-10-04）：目录走 InitialDirectory；
            // 文件夹选择器不碰 SelectedPath、文件选择器「文件名:」框只放叶子名——两处都会把长路径挤成截断的尾巴 ---
            // 用户截图：底栏「文件夹:」框里只剩名字尾巴（"win-cuda-13.4-x64"）——.NET 对 SelectedPath 会把
            // 叶子名预填进那个框（SetFileName(叶子名)），光标停在尾部；InitialDirectory 只导航不开预填。
            {
                var exeDir = Path.GetFullPath(AppContext.BaseDirectory);
                using var fd = new FolderBrowserDialog();
                SettingsForm.SeedFolderDialog(fd, exeDir);
                Check("选择器定位：存在的目录走 InitialDirectory、SelectedPath 保持空（底栏不预填名字）",
                    fd.InitialDirectory.TrimEnd('\\').Equals(exeDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
                    && fd.SelectedPath.Length == 0, $"init='{fd.InitialDirectory}' sel='{fd.SelectedPath}'");
                using var fd2 = new FolderBrowserDialog();
                SettingsForm.SeedFolderDialog(fd2, Path.Combine(exeDir, "definitely-not-there-" + Guid.NewGuid().ToString("N")));
                Check("选择器定位：目录不存在 = 什么都不设（让它从默认位置开）",
                    fd2.InitialDirectory.Length == 0 && fd2.SelectedPath.Length == 0,
                    $"init='{fd2.InitialDirectory}'");
                // 2026-10-04 用户补充：ini 文件那份（「选文件」）同一条根因——FileName 原样交给 IFileDialog，
                // 全路径挤在「文件名:」框里被截断。口径：目录走 InitialDirectory、框里只放文件名。
                var pickFile = MainForm.Scratch("selftest-pick.ini");
                File.WriteAllText(pickFile, "");
                try
                {
                    using var od1 = new OpenFileDialog();
                    SettingsForm.SeedFileDialog(od1, pickFile);
                    Check("文件选择器定位：目录走 InitialDirectory、框里只放文件名（不是整条路径）",
                        od1.InitialDirectory.TrimEnd('\\').Equals(MainForm.TempDir().TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
                        && od1.FileName == "selftest-pick.ini",
                        $"init='{od1.InitialDirectory}' name='{od1.FileName}'");
                }
                finally { try { File.Delete(pickFile); } catch { /* 清不掉不影响结论 */ } }
                using var od2 = new OpenFileDialog();
                SettingsForm.SeedFileDialog(od2, Path.Combine(exeDir, "no-such-file-" + Guid.NewGuid().ToString("N") + ".ini"));
                Check("文件选择器定位：文件不在 = 只定位到目录、框不预填",
                    od2.InitialDirectory.TrimEnd('\\').Equals(exeDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
                    && od2.FileName.Length == 0, $"init='{od2.InitialDirectory}' name='{od2.FileName}'");
            }

            // --- 用例 4b：就绪行识别（对着 serve/server.py 真实打印的格式） ---
            Check("识别真实就绪行",
                MainForm.TryParseReady("ready: http://127.0.0.1:8080/v1  (OpenAI: /v1/chat/completions, Anthropic: /v1/messages, context 131072 tokens, images on)", out var readyUrl)
                && readyUrl == "http://127.0.0.1:8080/v1", readyUrl);
            Check("识别局域网启动时的就绪行",
                MainForm.TryParseReady("ready: http://127.0.0.1:8123/v1  (OpenAI: /v1/chat/completions)", out var readyUrl2) && readyUrl2 == "http://127.0.0.1:8123/v1");
            Check("不把普通行误判成就绪",
                !MainForm.TryParseReady("[strata] almost ready ...", out _)
                && !MainForm.TryParseReady("loading the model (the first start takes a minute or two) ...", out _)
                && !MainForm.TryParseReady("", out _));
    }
}
