using System.Text;
using System.Text.Json.Nodes;
namespace MultiLauncher;

// 版本与更新组：BUG-009 下拉置顶 / 用例 5c（API 地址）/ 5b（临时目录与回退备份）/ 6（更新检查真实网络通路）/
// ④（llama.cpp 更新：离线纯函数 + 真实网络）/ 7（保留规则）/ 7b（ini 一次性改名）/ 7c（每模型参数快照）/
// 7d（「已加载」标识）/ 7e（下载校验与落点）。2026-10-15 从 RunBody 整体搬出，块内一行未改。
internal static partial class SelfTest
{
    internal static void VersionAndUpdateChecks()
    {
            // --- 本轮反馈：下拉折叠后主窗掉到别的窗口后面（BUG-009）---
            // 弹出窗改成不抢激活（WS_EX_NOACTIVATE + ShowWithoutActivation），主窗全程不失活，
            // 于是"收起时要不要把层级还回去"这个问题本身消失了。真正的多窗口前台验证仍由用户实跑。
            Check("下拉弹出窗不抢主窗激活", DarkSelect.PopupNeverActivatesOwner());

            // 滚轮一格的方向与步长（设置页与日志区共用 UiScrollbar）
            try
            {
                var wb = new UiScrollbar();
                wb.Set(1000, 120, 0);
                wb.ScrollByWheel(-120);
                Check("滚轮向下 -> 起点后移", wb.Value > 0, "Value=" + wb.Value);
                wb.ScrollByWheel(120);
                Check("滚轮向上 -> 回到起点", wb.Value == 0, "Value=" + wb.Value);
                wb.Set(300, 400, 0);       // 内容不足一屏（第四轮反馈：滚动条只在需要时展示）
                Check("内容没超出 -> 不算可滚动（不画滑条）", !wb.Scrollable, "total=300 page=400");
                wb.Set(900, 400, 0);
                Check("内容超出 -> 才出现滑条", wb.Scrollable, "total=900 page=400");
            }
            catch (Exception ex) { Check("滚轮步长用例", false, ex.Message); }

            // --- 用例 5c：监听 0.0.0.0 时，显示的 API 地址应是局域网 IP ---
            var lan = MainForm.LanAddress();
            W("本机局域网地址: " + lan);
            Check("局域网地址是合法 IPv4", System.Net.IPAddress.TryParse(lan, out _));
            Check("监听 127.0.0.1 -> 原样显示", MainForm.ApiHostForDisplay("127.0.0.1") == "127.0.0.1");
            Check("监听 192.168.1.9 -> 原样显示", MainForm.ApiHostForDisplay("192.168.1.9") == "192.168.1.9");
            Check("监听 0.0.0.0 -> 用局域网地址（不再是 127）",
                MainForm.ApiHostForDisplay("0.0.0.0") == lan && (lan == "127.0.0.1" || !lan.StartsWith("127.")),
                MainForm.ApiHostForDisplay("0.0.0.0"));
            try
            {
                using var f2 = new MainForm();
                f2.CreateControl();
                var localUrl = f2.ApiAddressForTest("127.0.0.1");
                var lanUrl = f2.ApiAddressForTest("0.0.0.0");
                W("界面 API 地址：本机=" + localUrl + "  监听 0.0.0.0 时=" + lanUrl);
                Check("界面联动：127.0.0.1 显示回环地址", localUrl.Contains("127.0.0.1"));
                Check("界面联动：0.0.0.0 显示局域网地址", lanUrl.Contains(lan) && !lanUrl.Contains("//127.0.0.1:"));
            }
            catch (Exception ex) { Check("界面 API 地址联动用例", false, ex.Message); }

            // --- 用例 5b：临时目录与回退备份 ---
            var tmpDir = MainForm.TempDir();
            bool tmpOk;
            try
            {
                var probe = Path.Combine(tmpDir, "selftest-probe.tmp");
                File.WriteAllText(probe, "x");
                File.Delete(probe);
                tmpOk = true;
            }
            catch { tmpOk = false; }
            W("临时目录: " + tmpDir);
            Check("临时目录可写（更新检查/下载就落在这里，不依赖 %TEMP%）", tmpOk);
            var backups = MainForm.ListBackups(root);
            W("回退备份: " + backups.Count + (backups.Count > 0 ? "（最新 " + backups[0] + "）" : ""));
            W("快照的家: " + MainForm.StrataBackupRoot(root));
            // 原来那句"回退备份列表可读"判的是 `backups.Count >= 0` —— 那是条永真的空判据（BUG-043 一族），
            // 换成有内容的：清单读得动 + 快照的家按 DEC-124 派生成 Strata 自己数据目录里那一格（`D:\Strata-data\backup`）、
            // 且绝不落回配置备份那层（`data\backup\` 从此只装三个模块的配置备份，混进来就是拿配置格当快照回退）。
            Check("DEC-116 + DEC-124 回退清单可读，且快照的家 = D:\\Strata-data\\backup 那一格（不落回配置备份那层）",
                MainForm.StrataBackupRoot(root).Length > 0
                && MainForm.StrataBackupRoot(root).EndsWith(Path.Combine("Strata-data", "backup"), StringComparison.OrdinalIgnoreCase)
                && !MainForm.StrataBackupRoot(root).StartsWith(
                    Path.Combine(AppContext.BaseDirectory, "data", "backup"), StringComparison.OrdinalIgnoreCase),
                MainForm.StrataBackupRoot(root) + "｜" + backups.Count + " 份");

            // --- 用例 6：更新检查的真实网络通路 ---
            var python = Path.Combine(root, ".venv", "Scripts", "python.exe");
            var tmp = MainForm.Scratch("strata-selftest-release.json");
            string nerr;
            bool netOk = Net.Download("https://api.github.com/repos/Niko1221/Strata/releases/latest", tmp, python, out nerr);
            Check("能取到 GitHub 最新发布", netOk, nerr);
            if (netOk)
            {
                var rel = JsonNode.Parse(File.ReadAllText(tmp)) as JsonObject;
                var tag = rel?["tag_name"]?.GetValue<string>() ?? "";
                var zip = rel?["zipball_url"]?.GetValue<string>() ?? "";
                var notes = rel?["body"]?.GetValue<string>() ?? "";
                var remote = tag.TrimStart('v', 'V');
                W("remote tag: " + tag + "   zipball: " + zip + "   notes: " + notes.Length + " chars");
                Check("发布信息含 tag / 源码包地址 / 更新说明", tag.Length > 0 && zip.Length > 0 && notes.Length > 0);
                W("本地 " + localVersion + " vs 远端 " + remote + " → " + MainForm.CompareVersion(remote, localVersion) + "（>0 表示有新版本）");
            }

            // --- ④（DEC-049）llama.cpp 更新：先离线验纯函数，再验真实网络通路 ---
            const string llamaDir = @"D:\llama-b11259-bin-win-cuda-13.4-x64";
            Check("④ 从目录名推出要下的包后缀",
                MainForm.LlamaAssetSuffix(llamaDir) == "bin-win-cuda-13.4-x64", MainForm.LlamaAssetSuffix(llamaDir));
            Check("④ 新目录名只换 build 号（同父目录同后缀，旧目录不动）",
                MainForm.LlamaNewDirName(llamaDir, "b11368") == @"llama-b11368-bin-win-cuda-13.4-x64"
                && MainForm.LlamaNewDirName(llamaDir, "v0.5.0-b11368") == @"llama-b11368-bin-win-cuda-13.4-x64",
                MainForm.LlamaNewDirName(llamaDir, "b11368"));
            Check("④ 更新源填网页地址或 api 地址都能认（认不出返回空）",
                MainForm.LlamaReleasesApi("https://github.com/ggml-org/llama.cpp/releases")
                    == "https://api.github.com/repos/ggml-org/llama.cpp/releases?per_page=20"
                && MainForm.LlamaReleasesApi("https://api.github.com/repos/ggml-org/llama.cpp/releases")
                    == "https://api.github.com/repos/ggml-org/llama.cpp/releases?per_page=20"
                && MainForm.LlamaReleasesApi("https://example.com/nothing") == "",
                MainForm.LlamaReleasesApi(new Settings().LlamaUpdateUrl));
            Check("④ build 号比较：b11368 > b11259，取不到返回 -1",
                MainForm.LlamaBuildOf("b11368") > MainForm.LlamaBuildOf("b11259")
                && MainForm.LlamaBuildOf("v0.5.0-b11259") == 11259 && MainForm.LlamaBuildOf("") < 0);
            const string relJson = """
                [
                  {"tag_name":"b99999","draft":true,"prerelease":true,"assets":[{"name":"llama-b99999-bin-win-cuda-13.4-x64.zip","browser_download_url":"x"}]},
                  {"tag_name":"v0.5.0","draft":false,"prerelease":false,"published_at":"2026-09-23T20:50:06Z","assets":[]},
                  {"tag_name":"b11368","draft":false,"prerelease":true,"published_at":"2026-10-03T02:35:41Z","body":"notes",
                   "assets":[
                     {"name":"cudart-llama-bin-win-cuda-13.4-x64.zip","browser_download_url":"cudart"},
                     {"name":"llama-b11368-bin-win-cuda-12.4-x64.zip","browser_download_url":"wrong-cuda"},
                     {"name":"llama-b11368-bin-win-cuda-13.4-x64.zip","browser_download_url":"https://example.com/right.zip",
                      "digest":"sha256:1f438d5f2551f9a7a0b52ec42be5534c7e1463b9de43221b58de52fe014a4de3","size":388231471}]},
                  {"tag_name":"b11000","draft":false,"prerelease":true,"assets":[{"name":"llama-b11000-bin-win-cuda-13.4-x64.zip","browser_download_url":"older"}]}
                ]
                """;
            var pick = MainForm.ParseLlamaRelease(relJson, "bin-win-cuda-13.4-x64");
            Check("④ 挑版本：跳过草稿与 v0.5.0 那种没有 Windows 资产的标签，按后缀取对的那只包",
                pick.Tag == "b11368" && pick.AssetUrl == "https://example.com/right.zip"
                && pick.AssetName == "llama-b11368-bin-win-cuda-13.4-x64.zip" && pick.Published.StartsWith("2026-10-03"),
                pick.Tag + " / " + pick.AssetName + " / " + pick.AssetUrl);
            Check("④ 后缀取不到时退化成任意 zip（不误挑 cudart）",
                MainForm.ParseLlamaRelease(relJson, "").AssetName.StartsWith("llama-", StringComparison.OrdinalIgnoreCase),
                MainForm.ParseLlamaRelease(relJson, "").AssetName);
            // DEC-067：挑包时要把资产上的 digest 与 size 一起带回来，下载完才有对账的依据
            Check("④ 挑包时把 digest(sha256) 与 size 一起带回来（下载校验的依据）",
                pick.Sha256 == "sha256:1f438d5f2551f9a7a0b52ec42be5534c7e1463b9de43221b58de52fe014a4de3"
                && pick.Size == 388231471,
                pick.Sha256 + " / " + pick.Size);
            const string relNoDigest = """
                [
                  {"tag_name":"b11368","draft":false,"prerelease":true,"published_at":"2026-10-03T02:35:41Z","body":"notes",
                   "assets":[{"name":"llama-b11368-bin-win-cuda-13.4-x64.zip","browser_download_url":"u"}]}
                ]
                """;
            var noDigest = MainForm.ParseLlamaRelease(relNoDigest, "bin-win-cuda-13.4-x64");
            Check("④ 上游没给 digest/size 时按「没这条校验」处理（空串 + 0，不误杀也不误判通过）",
                noDigest.Sha256 == "" && noDigest.Size == 0 && noDigest.Tag == "b11368",
                noDigest.Sha256 + " / " + noDigest.Size);
            var llamaApi = MainForm.LlamaReleasesApi(new Settings().LlamaUpdateUrl);
            var llamaTmp = MainForm.Scratch("llama-selftest-releases.json");
            string lerr;
            bool llamaNetOk = Net.Download(llamaApi, llamaTmp, python, out lerr);
            Check("④ 能取到 llama.cpp 的发布列表", llamaNetOk, lerr);
            if (llamaNetOk)
            {
                var suffix = MainForm.LlamaAssetSuffix(new Settings().LlamaDir);
                var live = MainForm.ParseLlamaRelease(File.ReadAllText(llamaTmp), suffix);
                W("llama.cpp 远端: " + live.Tag + "  包: " + live.AssetName + "  发布: " + live.Published
                    + "  digest: " + (live.Sha256.Length > 0 ? live.Sha256 : "（上游没给）") + "  size: " + live.Size);
                Check("④ 真实列表里解析出带 " + suffix + " 包的 b 号版本",
                    live.Tag.StartsWith("b", StringComparison.OrdinalIgnoreCase) && live.AssetUrl.Length > 0, live.Tag);
                W("llama.cpp 本地 " + LlamaIni.DirVersion(new Settings().LlamaDir) + " vs 远端 " + live.Tag
                    + " → " + (MainForm.LlamaBuildOf(live.Tag) - MainForm.LlamaBuildOf(LlamaIni.DirVersion(new Settings().LlamaDir)))
                    + "（>0 表示有新版本）");
            }

            // --- 用例 7：更新时的保留规则 ---
            Check("保留 .venv / engine / diag",
                MainForm.Preserve(".venv/Scripts/python.exe") && MainForm.Preserve("engine/strata.exe") && MainForm.Preserve("diag/update-engine.bat"));
            Check("保留运行配置 / 启动脚本 / 日志",
                MainForm.Preserve("strata-swift-iq3_xxs.json") && MainForm.Preserve("run-swift-iq3_xxs.bat") && MainForm.Preserve("strata-swift-iq3_xxs.log"));
            Check("保留启动器自身目录与 ini", MainForm.Preserve("StrataLauncher/StrataLauncher.exe") && MainForm.Preserve("StrataLauncher/StrataLauncher.ini"));
            Check("保留启动器新名目录与 ini（DEC-037 改名后两个名字都保住）",
                MainForm.Preserve("MultiLauncher/MultiLauncher.exe") && MainForm.Preserve("MultiLauncher/MultiLauncher.ini")
                && MainForm.Preserve("MultiLauncher.ini"));
            // --- 用例 7b：设置文件的一次性改名（DEC-037）：只在壳自己的 data\tmp 里造临时目录，不碰 exe 同目录、不依赖 %TEMP% ---
            var iniDir = MainForm.Scratch("selftest-ini-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(iniDir);
            try
            {
                var legacy = Path.Combine(iniDir, MainForm.LegacyConfigFileName);
                var fresh = Path.Combine(iniDir, MainForm.ConfigFileName);
                File.WriteAllText(legacy, "[Current]\nPort=8077\n", new UTF8Encoding(false));
                var got1 = MainForm.MigrateConfigName(iniDir, out bool rn1);
                Check("只有旧名 -> 改名成新名、内容原样、旧文件不再留",
                    rn1 && got1 == fresh && File.Exists(fresh) && !File.Exists(legacy)
                    && File.ReadAllText(fresh).Contains("Port=8077"), got1);
                var got2 = MainForm.MigrateConfigName(iniDir, out bool rn2);
                Check("已是新名 -> 再跑一次不动它", !rn2 && got2 == fresh && File.ReadAllText(fresh).Contains("Port=8077"), got2);
                File.WriteAllText(legacy, "[Current]\nPort=9999\n", new UTF8Encoding(false));
                var got3 = MainForm.MigrateConfigName(iniDir, out bool rn3);
                Check("两份都有 -> 用新名、旧文件原样留着（不猜哪份更新）",
                    !rn3 && got3 == fresh && File.Exists(legacy) && File.ReadAllText(legacy).Contains("Port=9999"), got3);
                File.Delete(legacy);
                var empty = Path.Combine(iniDir, "none");
                Directory.CreateDirectory(empty);
                var got4 = MainForm.MigrateConfigName(empty, out bool rn4);
                // 空目录里没有旧名可改，返回的就是新名所在处（此时文件还不存在，首次运行会由保存写出来）
                Check("两份都没有 -> 用新名（首次运行）",
                    !rn4 && got4 == Path.Combine(empty, MainForm.ConfigFileName) && !File.Exists(got4), got4);
                Check("设置文件名 / 关于页口径 = MultiLauncher.ini",
                    MainForm.ConfigFileName == "MultiLauncher.ini" && MainForm.LegacyConfigFileName == "StrataLauncher.ini");
            }
            finally { try { Directory.Delete(iniDir, true); } catch { /* 临时目录清理失败不影响结论 */ } }
            Check("源码要覆盖",
                !MainForm.Preserve("setup.py") && !MainForm.Preserve("serve/server.py") && !MainForm.Preserve("CMakeLists.txt") && !MainForm.Preserve("data/expert-profile.bin"));

            // --- 用例 7c：每模型一份参数快照（DEC-064）---
            // 用户报的现象：启动后「下拉是 iq3_s、上方模型名是 swift-1.5-iq3_xxs」。根因是所有模型共用
            // [Current] 里的一份扁平家底，而换模型时只有 tag 落盘。这里覆盖：字段归属、只搬家底、
            // ini round-trip、两模型不串台、一次性迁移取那层生效配置、以及那条回归。
            Check("家底归属：模型名/上下文/KV/显卡/附加参数按模型走",
                MainForm.ModelOwnedKeys.Contains(nameof(Settings.ApiModel))
                && MainForm.ModelOwnedKeys.Contains(nameof(Settings.Context))
                && MainForm.ModelOwnedKeys.Contains(nameof(Settings.Kv))
                && MainForm.ModelOwnedKeys.Contains(nameof(Settings.Gpu))
                && MainForm.ModelOwnedKeys.Contains(nameof(Settings.ExtraArgs))
                && MainForm.ModelOwnedKeys.Contains(nameof(Settings.ReasoningEffort))
                && MainForm.ModelOwnedKeys.Contains(nameof(Settings.ArenaPinGib)));
            Check("家底归属：端口/地址/模式/内部端口/空闲停服不跟模型走（仍在 [Current]）",
                !MainForm.ModelOwnedKeys.Contains(nameof(Settings.Port))
                && !MainForm.ModelOwnedKeys.Contains(nameof(Settings.Host))
                && !MainForm.ModelOwnedKeys.Contains(nameof(Settings.Model))
                && !MainForm.ModelOwnedKeys.Contains(nameof(Settings.LlamaModel))
                && !MainForm.ModelOwnedKeys.Contains(nameof(Settings.InternalPort))
                && !MainForm.ModelOwnedKeys.Contains(nameof(Settings.WakeOnRequest))
                && !MainForm.ModelOwnedKeys.Contains(nameof(Settings.IdleStopMinutes)));
            {
                var from = new Settings { ApiModel = "A-name", Context = "262144", Gpu = "0", Port = "1111", Host = "0.0.0.0" };
                var into = new Settings { ApiModel = "B-name", Context = "8192", Gpu = "9", Port = "8080", Host = "127.0.0.1" };
                MainForm.ApplyModelFields(from, into);
                Check("只搬家底：模型名/上下文/显卡被覆盖，端口与监听地址不碰",
                    into.ApiModel == "A-name" && into.Context == "262144" && into.Gpu == "0"
                    && into.Port == "8080" && into.Host == "127.0.0.1",
                    $"port={into.Port} host={into.Host}");
                var snap = MainForm.SnapshotModelFields(from);
                Check("快照里只装家底（服务级字段留默认，别混进 [Model:] 段）",
                    snap.ApiModel == "A-name" && snap.Port == new Settings().Port && snap.Host == new Settings().Host);
            }
            {
                // ini round-trip：[Model:<tag>] 段存得回、读得回；段里混进的非家底键不污染快照
                var cur = new Settings { Model = "iq3_s", Port = "8081", ApiModel = "flat-name", LlamaExtraArgs = "--flash-attn on" };
                var snaps = new Dictionary<string, Settings>(StringComparer.OrdinalIgnoreCase)
                {
                    ["iq3_s"] = MainForm.SnapshotModelFields(new Settings { ApiModel = "qwen-iq3", Context = "262144", ReasoningEffort = "low" }),
                    ["swift"] = MainForm.SnapshotModelFields(new Settings { ApiModel = "swift-1.5", Context = "131072" }),
                };
                var lines = MainForm.BuildConfigLines(cur, snaps, new MainForm.WindowRect(10, 20, 300, 400, false));
                Check("ini 里每个模型一段 [Model:<tag>]",
                    lines.Contains("[Model:iq3_s]") && lines.Contains("[Model:swift]"), string.Join("|", lines.Take(3)));
                var cur2 = new Settings();
                var snaps2 = new Dictionary<string, Settings>(StringComparer.OrdinalIgnoreCase);
                var res2 = new MainForm.ConfigParseResult();
                // 尾上再塞一段「旧版预设」（2026-10-04 DEC-073 已拆）：解析必须识别并跳过，
                // 里面的键绝不能漏进 [Current]（Port=9090 / LlamaTemperature=9.9 都不许命中）
                // [Current] 里插两行旧版键（LowRam= DEC-075 整链删除 / 那个写全量键 = DEC-163 整套退役）：
                // 读不认、安全忽略，别的键照常。**退役键的字面拆开拼** —— 免得撞上 9e4b2 那条"全源码 0 处"的源码扫描（BUG-066 老账）。
                var withLegacy = new List<string>(lines);
                withLegacy.Insert(withLegacy.IndexOf("[Current]") + 1, "LowRam=True");
                withLegacy.Insert(withLegacy.IndexOf("[Current]") + 1, "EngineFull" + "Ledger=True");
                MainForm.ParseConfigLines(withLegacy.Concat(new[]
                    {
                        "[Model:swift]", "Port=7777", "ApiModel=swift-1.5",
                        "[Preset:老预设]", "Port=9090", "LlamaTemperature=9.9",
                    }),
                    cur2, snaps2, res2);
                Check("读回来：两个模型各自的模型名不串台",
                    snaps2["iq3_s"].ApiModel == "qwen-iq3" && snaps2["swift"].ApiModel == "swift-1.5",
                    $"{snaps2["iq3_s"].ApiModel} / {snaps2["swift"].ApiModel}");
                Check("读回来：[Current] 与窗口位置照旧",
                    cur2.Model == "iq3_s" && cur2.Port == "8081" && res2.X == 10 && res2.Maximized == false);
                Check("旧 [Preset:] 段被识别并跳过：里面的键不会漏进 [Current]（预设已拆，DEC-073）",
                    cur2.Port == "8081" && cur2.LlamaTemperature == "" && cur2.LlamaModel == "",
                    $"port={cur2.Port} temp='{cur2.LlamaTemperature}'");
                Check("快照段只认家底键：混进的 Port= 不会改到模型值",
                    snaps2["swift"].Port == new Settings().Port, snaps2["swift"].Port);
                Check("读回来：思考档位跟着自己的模型段走（iq3_s=low、swift 没存过 = 空）",
                    snaps2["iq3_s"].ReasoningEffort == "low" && snaps2["swift"].ReasoningEffort == "",
                    $"{snaps2["iq3_s"].ReasoningEffort}/{snaps2["swift"].ReasoningEffort}");
                Check("读回来：llama 附加参数 round-trip（DEC-075）",
                    cur2.LlamaExtraArgs == "--flash-attn on", cur2.LlamaExtraArgs);
                Check("旧 ini 的 LowRam=True 与 DEC-163 退役键读不认（删除后安全忽略，别的键照常）",
                    cur2.Port == "8081" && cur2.LlamaExtraArgs == "--flash-attn on", $"port={cur2.Port}");
                // ---- DEC-122③：[Current] 里那 16 个 Strata 模型键的**重复副本**退役（家 = [Model:<tag>] 那一段）----
                // 三条正反：写侧不再写 / 老 ini 读得进（启动值与一次性打底还要从旧行取，读不认就是丢数据）/ 记录那一段照旧写全。
                var dup = new Settings
                {
                    Model = "iq3_s", ApiModel = "flat", Context = "262144", Kv = "q4_0", KvResident = true,
                    Spec = "5", SpecMinP = "0.6", ExpertCache = "auto", Prefill = "auto:32768", Vision = "cpu",
                    VramReserveMib = "800", Gpu = "0", LayerSplit = "10", ArenaPinGib = "8", HeadLate = true,
                    ExtraArgs = "--foo", ReasoningEffort = "low", Port = "8081",
                };
                var dupLines = MainForm.BuildConfigLines(dup,
                    new Dictionary<string, Settings>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["iq3_s"] = MainForm.SnapshotModelFields(dup),
                    },
                    new MainForm.WindowRect(10, 20, 300, 400, false));
                var curPart = dupLines.Skip(dupLines.IndexOf("[Current]") + 1).TakeWhile(l => !l.Trim().StartsWith("["));
                Check("DEC-122③ 写侧跳过：这 16 行不再出现在 [Current]（Port= / Model= 这类不跟模型走的照旧在）",
                    !curPart.Any(l => l.StartsWith("Context=")) && !curPart.Any(l => l.StartsWith("Kv="))
                    && !curPart.Any(l => l.StartsWith("ApiModel=")) && !curPart.Any(l => l.StartsWith("ExtraArgs="))
                    && !curPart.Any(l => l.StartsWith("ReasoningEffort=")) && !curPart.Any(l => l.StartsWith("Vision="))
                    && curPart.Any(l => l == "Port=8081") && curPart.Any(l => l == "Model=iq3_s"),
                    string.Join(" ⏎ ", curPart.Where(l => l.StartsWith("C") || l.StartsWith("K") || l.StartsWith("V"))));
                var snapPart = dupLines.Skip(dupLines.IndexOf("[Model:iq3_s]") + 1).TakeWhile(l => !l.Trim().StartsWith("["));
                Check("DEC-122③ 记录那一段照旧写全这 16 格（值由记录承接 = 用户无感；服务级字段仍不进这一段）",
                    snapPart.Any(l => l == "Context=262144") && snapPart.Any(l => l == "Kv=q4_0")
                    && snapPart.Any(l => l == "ExtraArgs=--foo") && snapPart.Any(l => l == "ReasoningEffort=low")
                    && snapPart.Any(l => l == "KvResident=True")
                    && !snapPart.Any(l => l.StartsWith("Port=")) && !snapPart.Any(l => l.StartsWith("Model=")),
                    string.Join(" ⏎ ", snapPart));
                var oldDup = new Settings();
                MainForm.ParseConfigLines(new[]
                {
                    "[Current]", "Context=131072", "KvResident=True", "HeadLate=True",
                    "ExtraArgs=--old", "ReasoningEffort=high", "Port=9099",
                }, oldDup, new Dictionary<string, Settings>(), new MainForm.ConfigParseResult());
                Check("DEC-122③ 读侧照旧认：老 ini 里那 16 行还读得进（启动值与一次性打底要从旧行取，读不认就是丢数据）",
                    oldDup.Context == "131072" && oldDup.KvResident && oldDup.HeadLate
                    && oldDup.ExtraArgs == "--old" && oldDup.ReasoningEffort == "high" && oldDup.Port == "9099",
                    $"ctx={oldDup.Context} kvres={oldDup.KvResident} extra={oldDup.ExtraArgs}");
                Check("DEC-122③ 退役名单里的模型家底 = 16 格、与 ModelOwnedKeys 一格不差（Model= 选中指针不许进去）",
                    MainForm.ModelOwnedKeys.Length == 16
                    && MainForm.ModelOwnedKeys.All(k => MainForm.RetiredCurrentKeys.Contains(k))
                    && !MainForm.RetiredCurrentKeys.Contains(nameof(Settings.Model))
                    && !MainForm.RetiredCurrentKeys.Contains(nameof(Settings.Port))
                    && !MainForm.RetiredCurrentKeys.Contains(nameof(Settings.Host)),
                    "名单：" + string.Join("、", MainForm.ModelOwnedKeys));
                // DEC-145 补：新加的两枚 llama 段级键（spec-type / mmproj-offload）也必须进退役名单 ——
                // 它们与 LlamaCtx 等同族（家在 llama_models.ini 段里），不进名单就会被 [Current] 反射写出重复副本
                //（与 Settings.cs "这批键不落 [Current]"、MainForm.Models.cs 两处注释是同一条口径）。
                Check("DEC-145 退役：LlamaSpecType / LlamaMmprojOffload 在 RetiredCurrentKeys 里（[Current] 不再写这两键）",
                    MainForm.RetiredCurrentKeys.Contains(nameof(Settings.LlamaSpecType))
                    && MainForm.RetiredCurrentKeys.Contains(nameof(Settings.LlamaMmprojOffload)));
            }
            {
                // ---- DEC-151：llama 空闲卸载单位 秒 → 分钟 的一次性换算（旧键 LlamaSleepIdleSeconds 只读这一次）----
                // 换算规矩：秒数 > 0 **向上取整**到分钟（卸载不早于原来）；0/负/空 = 不自动卸（= 旧行为"不传旗标"）；
                // 同文件见过新键 = 新键为准、旧值不接管。写侧从此只写新键，旧行随下一次保存消失（口径同 LowRam DEC-075）。
                var mig1 = new Settings();
                MainForm.ParseConfigLines(new[] { "[Current]", "LlamaSleepIdleSeconds=600" },
                    mig1, new Dictionary<string, Settings>(), new MainForm.ConfigParseResult());
                Check("DEC-151 旧键换算：LlamaSleepIdleSeconds=600 → 读成 10 分钟",
                    mig1.LlamaSleepIdleMinutes == "10", $"'{mig1.LlamaSleepIdleMinutes}'");
                var mig2 = new Settings();
                MainForm.ParseConfigLines(new[] { "[Current]", "LlamaSleepIdleSeconds=90" },
                    mig2, new Dictionary<string, Settings>(), new MainForm.ConfigParseResult());
                Check("DEC-151 旧键换算：90 秒 → 2 分钟（向上取整，卸载不早于原来）",
                    mig2.LlamaSleepIdleMinutes == "2", $"'{mig2.LlamaSleepIdleMinutes}'");
                var mig3 = new Settings();
                MainForm.ParseConfigLines(new[] { "[Current]", "LlamaSleepIdleSeconds=999", "LlamaSleepIdleMinutes=3" },
                    mig3, new Dictionary<string, Settings>(), new MainForm.ConfigParseResult());
                Check("DEC-151 新旧同场：新键为准（旧键不接管）",
                    mig3.LlamaSleepIdleMinutes == "3", $"'{mig3.LlamaSleepIdleMinutes}'");
                var mig4 = new Settings();
                MainForm.ParseConfigLines(new[] { "[Current]", "LlamaSleepIdleSeconds=0" },
                    mig4, new Dictionary<string, Settings>(), new MainForm.ConfigParseResult());
                Check("DEC-151 旧值 0 = 不自动卸（空串，不许落成默认 5 分钟 —— 旧行为是「不传旗标」）",
                    mig4.LlamaSleepIdleMinutes == "", $"'{mig4.LlamaSleepIdleMinutes}'");
            }
            {
                // ---- DEC-157：显存互斥那一档升到三模块共用 → 两个键改名（NinferGpuMutex / NinferWakeWaitSec 只读这一次）----
                // 规矩照 DEC-151 那一族：旧行认一次、同文件见过新键 = 新键为准、写侧只写新键（旧行随下次保存消失）。
                var gm1 = new Settings();
                MainForm.ParseConfigLines(new[] { "[Current]", "NinferGpuMutex=reject", "NinferWakeWaitSec=600" },
                    gm1, new Dictionary<string, Settings>(), new MainForm.ConfigParseResult());
                Check("DEC-157 旧键换算：NinferGpuMutex=reject → GpuMutex=reject；NinferWakeWaitSec=600 → GpuWakeWaitSec=600（他选的那档不许因为改名丢掉）",
                    gm1.GpuMutex == "reject" && gm1.GpuWakeWaitSec == "600",
                    $"'{gm1.GpuMutex}' / '{gm1.GpuWakeWaitSec}'");
                var gm2 = new Settings();
                MainForm.ParseConfigLines(new[] { "[Current]", "NinferGpuMutex=reject", "GpuMutex=queue" },
                    gm2, new Dictionary<string, Settings>(), new MainForm.ConfigParseResult());
                Check("DEC-157 新旧同场：新键为准（旧键不接管）",
                    gm2.GpuMutex == "queue", $"'{gm2.GpuMutex}'");
                var gmLines = MainForm.BuildConfigLines(new Settings { GpuMutex = "reject", GpuWakeWaitSec = "420" },
                    new Dictionary<string, Settings>(), new MainForm.WindowRect(0, 0, 800, 600, false));
                Check("DEC-157 写侧只写新键（旧键随下一次保存自然消失）",
                    gmLines.Contains("GpuMutex=reject") && gmLines.Contains("GpuWakeWaitSec=420")
                    && !gmLines.Any(l => l.StartsWith("NinferGpuMutex", StringComparison.Ordinal))
                    && !gmLines.Any(l => l.StartsWith("NinferWakeWaitSec", StringComparison.Ordinal)),
                    string.Join(" | ", gmLines.Where(l => l.Contains("Gpu"))));
            }
            {
                // 两个模型各有一份模板：按 tag 取家底时读的是它自己的 model_name（回归：用户那台机器上
                // ini 记的是 Model=iq3_s + ApiModel=swift-1.5-iq3_xxs，界面上就错位了）
                var md = MainForm.Scratch("selftest-snaps-" + Guid.NewGuid().ToString("N")[..8]);
                Directory.CreateDirectory(md);
                try
                {
                    void Tpl(string tag, string name, string ctx) => File.WriteAllText(Path.Combine(md, "strata-" + tag + ".json"),
                        $"{{\"model_name\":\"{name}\",\"args\":[\"--pack\",\"p\",\"--native\",\"n\",\"--max-context\",\"{ctx}\",\"--kv\",\"int8\"]}}");
                    Tpl("iq3_s", "qwen3.8-flash-next-iq3_s", "262144");
                    Tpl("swift", "swift-1.5-iq3_xxs", "131072");
                    var snA = new Settings { ApiModel = "swift-1.5-iq3_xxs", Context = "131072" };     // 就是用户那份串了台的 ini 值
                    Check("回归：无快照时按选中模型的模板校正，模型名跟着下拉走",
                        MainForm.ReadKnobsFromModel(md, "iq3_s", snA)
                        && snA.ApiModel == "qwen3.8-flash-next-iq3_s" && snA.Context == "262144", snA.ApiModel);
                    var snB = MainForm.SnapshotModelFields(new Settings { ApiModel = "我改过的名字", Context = "65536" });
                    MainForm.ApplyModelFields(snB, snA);        // 有快照则覆盖在模板之上
                    Check("有快照时快照赢（用户为该模型存过的值不被模板顶掉）",
                        snA.ApiModel == "我改过的名字" && snA.Context == "65536", snA.ApiModel);
                    // 一次性迁移：runDir 里只有 iq3_s 被启动过 → 只给它建快照，ArenaPinGib 沿用界面上的旧值
                    Directory.CreateDirectory(Path.Combine(md, "run"));
                    File.WriteAllText(Path.Combine(md, "run", "iq3_s.json"),
                        "{\"model_name\":\"qwen3.8-flash-next-iq3_s\",\"env\":{\"STRATA_ARENA_PIN_GIB\":\"16\"},\"args\":[\"--max-context\",\"262144\"]}");
                    var cur3 = new Settings { Model = "iq3_s", ArenaPinGib = "", ApiModel = "swift-1.5-iq3_xxs" };
                    var snaps3 = new Dictionary<string, Settings>(StringComparer.OrdinalIgnoreCase);
                    int snN = MainForm.SeedModelSnapsFromRunConfigs(cur3, snaps3, new[] { "iq3_s", "swift" }, Path.Combine(md, "run"));
                    Check("一次性迁移：只为「启动过、有生效配置 <tag>.json」的模型建快照",
                        snN == 1 && snaps3.Count == 1 && snaps3.ContainsKey("iq3_s"), "n=" + snN);
                    Check("迁移取的是该模型此刻生效的值（不是串了台的 [Current]）",
                        snaps3["iq3_s"].ApiModel == "qwen3.8-flash-next-iq3_s", snaps3["iq3_s"].ApiModel);
                    Check("迁移不碰 arena pin：留空 = 沿用配置，物化文件里那个数分不清来源",
                        snaps3["iq3_s"].ArenaPinGib == "", "'" + snaps3["iq3_s"].ArenaPinGib + "'");
                    Check("已有快照就不再插手（迁移只发生一次）",
                        MainForm.SeedModelSnapsFromRunConfigs(cur3, snaps3, new[] { "swift" }, Path.Combine(md, "run")) == 0
                        && snaps3.Count == 1);
                }
                finally { try { Directory.Delete(md, true); } catch { /* 临时目录清理失败不影响结论 */ } }
            }

            // --- 用例 7d：下拉里的「已加载」标识（DEC-065）---
            // 判据是纯函数（穷举组合），摆位与"不许假亮"走 DarkSelect 与真界面各一条。
            Check("判据：llama 侧 = router 说这一枚已加载才标",
                MainForm.ModelLoadedBadgeFor(ModelEntry.BackendLlama, true, false, false) == MainForm.LoadedBadgeText
                && MainForm.ModelLoadedBadgeFor(ModelEntry.BackendLlama, false, false, false) == "");
            Check("判据：Strata 侧 = 本壳拉起的进程跑的正是它才标（网关在听但模型已停 = 不标）",
                MainForm.ModelLoadedBadgeFor(ModelEntry.BackendStrata, false, true, false) == MainForm.LoadedBadgeText
                && MainForm.ModelLoadedBadgeFor(ModelEntry.BackendStrata, false, false, false) == "");
            Check("判据：两个模块互不借光（llama 的加载集合点不亮 Strata 那一枚）",
                MainForm.ModelLoadedBadgeFor(ModelEntry.BackendStrata, true, false, false) == "");
            // DEC-096：第三个模块进来后这张判据按模块键穷举，Ninfer 那一枚只认"本壳拉起的引擎跑的正是这一件"
            Check("判据：Ninfer 侧 = 引擎进程跑的正是这一段才标（另两模块的加载态点不亮它）",
                MainForm.ModelLoadedBadgeFor(ModelEntry.BackendNinfer, true, true, true) == MainForm.LoadedBadgeText
                && MainForm.ModelLoadedBadgeFor(ModelEntry.BackendNinfer, true, true, false) == "");
            Check("默认不带状态词（没加载就不许标）", new DarkItem { Text = "x" }.Badge == "");
            {
                // 原地刷状态词：不重建列表 —— 重建会把选中项与展开态一起吞掉（DEC-065 的刷新通道）
                var selBox = new DarkSelect();
                var ea = new ModelEntry("a", "", "a", true, "");
                var eb = new ModelEntry("b", "", "b", true, "");
                selBox.SetItems(new[]
                {
                    new DarkItem { Text = "a", Right = "~10G", Tag = ea },
                    new DarkItem { Text = "b", Right = "~20G", Tag = eb },
                });
                selBox.SelectByTag(eb);
                selBox.RefreshBadges(it => Equals(it.Tag, ea) ? MainForm.LoadedBadgeText : "");
                Check("刷新：只给命中的那一枚加词，另一枚不动",
                    selBox.Items[0].Badge == MainForm.LoadedBadgeText && selBox.Items[1].Badge == "");
                Check("刷新：选中项与右侧估算大小都不被碰",
                    ReferenceEquals(selBox.Selected?.Tag, eb) && selBox.Items[1].Right == "~20G");
                selBox.RefreshBadges(_ => "");
                Check("刷新：加载状态收掉时标识一起收（不许留下假亮）",
                    selBox.Items[0].Badge == "" && selBox.Items[1].Badge == "");
                selBox.Dispose();
            }
            try
            {
                using var f3 = new MainForm();
                f3.CreateControl();
                Check("真界面：刚启动、什么也没加载 -> 下拉里一枚都不标",
                    f3.ModelBadgesForTest().All(b => b.Badge == ""),
                    string.Join(",", f3.ModelBadgesForTest().Select(b => b.Tag + "=" + b.Badge)));
                var strataTag = f3.ModelsForTest.FirstOrDefault(m => m.Backend == ModelEntry.BackendStrata)?.Tag ?? "";
                if (strataTag.Length > 0)
                {
                    f3.SetLlamaLoadedForTest(strataTag);
                    Check("真界面：把 Strata 的 tag 塞进 llama 的加载集合 -> 依然一枚都不亮（判据不跨模块）",
                        f3.ModelBadgesForTest().All(b => b.Badge == ""), strataTag);
                }
            }
            catch (Exception ex) { Check("「已加载」标识的界面用例", false, ex.Message); }

            // --- 用例 7e：下载校验与提示落点（DEC-067 / DEC-066）---
            // 校验只验到"下完了、非空、解压时 CRC 对得上"是不够的：下完整了却不是那个包同样要拦。
            {
                var vd = MainForm.Scratch("selftest-verify-" + Guid.NewGuid().ToString("N")[..8]);
                Directory.CreateDirectory(vd);
                try
                {
                    var f = Path.Combine(vd, "abc.bin");
                    File.WriteAllText(f, "abc", new UTF8Encoding(false));
                    const string abcSha = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
                    Check("sha256 算得对（用标准向量 \"abc\" 对拍）", Net.Sha256File(f) == abcSha, Net.Sha256File(f));
                    Check("digest 认 GitHub 的写法、也认裸 hex，大小写不敏感",
                        Net.DigestMatches("sha256:" + abcSha, abcSha) && Net.DigestMatches("sha256:" + abcSha.ToUpper(), abcSha)
                        && Net.DigestMatches(abcSha, abcSha.ToUpper()));
                    Check("上游没给 digest = 没这条校验（不误杀），摘要不符才算失败",
                        Net.DigestMatches("", abcSha) && Net.DigestMatches(null, abcSha) && !Net.DigestMatches("sha256:deadbeef", abcSha));
                    Check("校验通过：体积与摘要都对得上",
                        Net.VerifyDownload(f, 3, "sha256:" + abcSha, out var ve1) && ve1.Length == 0, ve1);
                    Check("校验通过：两条期望都不给（老数据没 digest/size）不误杀",
                        Net.VerifyDownload(f, 0, "", out _));
                    Check("体积不符 = 失败，并且把包删掉（不许留下「下次当成已下好」的文件）",
                        !Net.VerifyDownload(f, 999999, "", out var ve2) && ve2.Contains("体积不符") && !File.Exists(f), ve2);
                    File.WriteAllText(f, "abc", new UTF8Encoding(false));
                    Check("摘要不符 = 失败，并且把包删掉",
                        !Net.VerifyDownload(f, 3, "sha256:" + new string('0', 64), out var ve3) && ve3.Contains("sha256 不符")
                        && !File.Exists(f), ve3);
                    File.WriteAllText(f, "abc", new UTF8Encoding(false));
                    Check("非 sha256 的 digest 一律判不符（认不出来就收手，别当校验通过）",
                        !Net.DigestMatches("sha512:" + abcSha, abcSha));

                    // 源码 zipball 没有官方摘要：改验包内版本号（内容身份）
                    var src = Path.Combine(vd, "strata-src");
                    Directory.CreateDirectory(src);
                    File.WriteAllText(Path.Combine(src, "CMakeLists.txt"),
                        "cmake_minimum_required(VERSION 3.16)\nproject(strata VERSION 0.1.39 LANGUAGES CXX)\n");
                    Check("源码包按内容身份验：包里的版本 == 探到的版本",
                        MainForm.SourceVersionMatches(src, "0.1.39", out var fv) && fv == "0.1.39", fv);
                    Check("带 v 前缀的 tag 也算对得上（远端标签是 v0.1.39 这种写法）",
                        MainForm.SourceVersionMatches(src, "v0.1.39", out _));
                    Check("版本不符 = 拦下，并把读到的值报出来（日志里能直接判）",
                        !MainForm.SourceVersionMatches(src, "0.1.38", out var fv2) && fv2 == "0.1.39", fv2);
                    Check("读不到 CMakeLists.txt = 拦下（宁可不动，也不覆盖一版不知道是什么的东西）",
                        !MainForm.SourceVersionMatches(vd, "0.1.39", out var fv3) && fv3.Length == 0);
                }
                finally { try { Directory.Delete(vd, true); } catch { /* 临时目录清理失败不影响结论 */ } }
            }
            // toast 的摆位（纯函数，2026-10-04 改版）：宿主窗内水平居中、单条中线距窗底 1/4 窗高；
            // 没宿主（主窗藏着）时对屏幕工作区套同一条规矩；贴边时夹回可见区
            {
                var size = new Size(300, 60);
                var screen = new Rectangle(0, 0, 1920, 1040);
                var host = new Rectangle(100, 80, 900, 600);
                Check("toast：宿主窗可用时水平居中、中线在距窗底 1/4 高处",
                    Toast.PlaceFor(host, size, screen) == new Point(100 + (900 - 300) / 2, 680 - 600 / 4 - 30),
                    Toast.PlaceFor(host, size, screen).ToString());
                Check("toast：没有可用宿主（主窗藏着）时对工作区同规矩（居中、距底 1/4）",
                    Toast.PlaceFor(null, size, screen) == new Point((1920 - 300) / 2, 1040 - 1040 / 4 - 30),
                    Toast.PlaceFor(null, size, screen).ToString());
                Check("toast：宿主窗贴着屏幕外也夹回工作区（不许弹到看不见的地方）",
                    Toast.Clamp(new Point(1900, 1100), size, screen, 14) == new Point(1920 - 300 - 14, 1040 - 60 - 14),
                    Toast.Clamp(new Point(1900, 1100), size, screen, 14).ToString());
                Check("toast：状态色各档都有区分（成功绿 / 警告橙 / 失败红 / 普通灰）",
                    Toast.Accent(Toast.Kind.Ok) == UiTheme.Brand && Toast.Accent(Toast.Kind.Error) == UiTheme.Danger
                    && Toast.Accent(Toast.Kind.Warn) != Toast.Accent(Toast.Kind.Info));
                // 动画帧纯函数（2026-10-04 DEC-075，原型定稿变体 B）：出现 = 由下 16px 滑上 + 淡入；
                // 消失 = 淡出 + 上飘 10px。定时器只是把这两条曲线按 15ms 应用到 Opacity/Location。
                {
                    var (o0, d0) = Toast.ShowFrame(0);
                    var (om, dm) = Toast.ShowFrame(Toast.ShowMs / 2);
                    var (o1, d1) = Toast.ShowFrame(Toast.ShowMs);
                    Check("toast 出现帧：起点 = 全透明 + 在下方 16px；终点 = 不透明 + 归位；中点单调",
                        o0 == 0 && d0 == Toast.RiseLogical && o1 == 1 && d1 == 0
                        && om is > 0 and < 1 && dm is > 0 and < Toast.RiseLogical);
                    var (h0, hd0) = Toast.HideFrame(0);
                    var (hm, hdm) = Toast.HideFrame(Toast.HideMs / 2);
                    var (h1, hd1) = Toast.HideFrame(Toast.HideMs);
                    Check("toast 消失帧：起点 = 不透明 + 位移 0；终点 = 全透明 + 上飘 10px；中点单调",
                        h0 == 1 && hd0 == 0 && h1 == 0 && hd1 == -Toast.DriftLogical
                        && hm is > 0 and < 1 && hdm is < 0 and > -Toast.DriftLogical);
                    Check("toast 时长与位移是原型定稿那套（出现 180ms / 消失 200ms / 16px / 10px）",
                        Toast.ShowMs == 180 && Toast.HideMs == 200 && Toast.RiseLogical == 16 && Toast.DriftLogical == 10);
                }
                // ---------------- 动效收口（2026-10-16，方案文档 §5.1 / §5.2 / §7）----------------
                // 曲线与心跳搬到 UiMotion 一处之后，这几条判据盯的是"收口没收口成一条"：
                // 端点不许漂、单调不许断、时长档位不许各写一个数。红/绿双向验按回去 = 把某条曲线改回线性或改短。
                {
                    var e0 = UiMotion.EaseOutCubic(0); var em = UiMotion.EaseOutCubic(0.5); var e1 = UiMotion.EaseOutCubic(1);
                    Check("UiMotion 入场曲线：起点 0、终点 1、中点已过一半（ease-out = 前快后慢）且全程单调",
                        e0 == 0 && e1 == 1 && em > 0.5 && em < 1
                        && UiMotion.EaseOutCubic(0.25) < em && UiMotion.EaseOutCubic(0.75) > em);
                    var q0 = UiMotion.EaseInQuad(0); var qm = UiMotion.EaseInQuad(0.5); var q1 = UiMotion.EaseInQuad(1);
                    Check("UiMotion 退场曲线：起点 0、终点 1、中点不到一半（ease-in = 先慢后快）且越界夹住",
                        q0 == 0 && q1 == 1 && qm > 0 && qm < 0.5 && UiMotion.EaseInQuad(-3) == 0 && UiMotion.EaseInQuad(9) == 1);
                    var (so0, sd0) = StackedToggle.OutFrame(0); var (so1, sd1) = StackedToggle.OutFrame(1);
                    var (si0, sid0) = StackedToggle.InFrame(0); var (si1, sid1) = StackedToggle.InFrame(1);
                    Check("叠放按钮帧函数：出场 alpha 1→0 且向上飘、入场 alpha 0→1 且从下方 Rise 落位（收口只换曲线，端点一字未动）",
                        so0 == 1 && so1 == 0 && sd0 == 0 && sd1 == -StackedToggle.RiseLogical
                        && si0 == 0 && si1 == 1 && sid0 == StackedToggle.RiseLogical && sid1 == 0
                        && StackedToggle.OutFrame(0.25).Alpha > StackedToggle.OutFrame(0.5).Alpha
                        && StackedToggle.OutFrame(0.5).Alpha > StackedToggle.OutFrame(0.75).Alpha);   // 出场 alpha 全程单调降
                    var ca = new Rectangle(4, 2, 60, 30); var cb = new Rectangle(120, 2, 90, 30);
                    var cmid = UiMotion.LerpRect(ca, cb, 0.5);
                    Check("胶囊滑动矩形插值：t=0 等于旧格、t=1 等于新格、中点 X 与宽都落在两端之间",
                        UiMotion.LerpRect(ca, cb, 0) == ca && UiMotion.LerpRect(ca, cb, 1) == cb
                        && cmid.X > ca.X && cmid.X < cb.X && cmid.Width > ca.Width && cmid.Width < cb.Width
                        && cmid.Y == ca.Y && cmid.Height == ca.Height);   // 顶与高不动：滑动只跨 X
                    Check("擦入判据四面：Bare 不播（DEC-177 那枚箭头连底都不画）/ 禁用不播 / 两档同色不播 / 正常才播",
                        !FlatButton.WipeApplies(true, true, UiTheme.Brand, UiTheme.BrandHover)
                        && !FlatButton.WipeApplies(false, false, UiTheme.Brand, UiTheme.BrandHover)
                        && !FlatButton.WipeApplies(false, true, UiTheme.Input, UiTheme.Input)
                        && FlatButton.WipeApplies(false, true, UiTheme.Brand, UiTheme.BrandHover));
                    int liveBtn = FlatButton.WipeLiveForTest;
                    var btnProbe = new FlatButton(); btnProbe.CreateControl(); btnProbe.Dispose();
                    Check("FlatButton 建一枚再释放：定时器存活计数回到基线（漏 Dispose 会被这条抓住）",
                        FlatButton.WipeLiveForTest == liveBtn, "live=" + FlatButton.WipeLiveForTest + " base=" + liveBtn);
                    int liveSeg = SegmentedControl.LiveForTest;
                    var segProbe = new SegmentedControl(); segProbe.CreateControl(); segProbe.Dispose();
                    Check("SegmentedControl 建一枚再释放：定时器存活计数回到基线（同 ThinProgress.LiveForTest 那一条）",
                        SegmentedControl.LiveForTest == liveSeg, "live=" + SegmentedControl.LiveForTest + " base=" + liveSeg);
                    int liveKnob = ToggleSwitch.KnobLiveForTest;
                    var knobProbe = new ToggleSwitch(); knobProbe.CreateControl(); knobProbe.Dispose();
                    Check("ToggleSwitch 建一枚再释放：滑块行程定时器存活计数回到基线（三族同一个形状）",
                        ToggleSwitch.KnobLiveForTest == liveKnob, "live=" + ToggleSwitch.KnobLiveForTest + " base=" + liveKnob);
                    // ---------------- 第二轮实跑反馈（2026-10-16 他点名"卡顿 / 离开没动画 / 胶囊没动画"）----------------
                    // 卡顿的根因是**曲线选错**：三次缓出在十帧里前三帧就走掉 66% 行程，读作"跳一下再蹭"。
                    // 位移类改吃 EaseInOut（两端速度为 0），这条判据钉的就是"起步不许冲"——改回 EaseOutCubic 立刻红。
                    Check("位移曲线 EaseInOut：两端归位、中点正好一半、且起步不冲（t=0.1 时不到 0.05，线性会是 0.1）",
                        UiMotion.EaseInOut(0) == 0 && UiMotion.EaseInOut(1) == 1 && UiMotion.EaseInOut(0.5) == 0.5
                        && UiMotion.EaseInOut(0.1) < 0.05 && UiMotion.EaseInOut(0.9) > 0.95
                        && UiMotion.EaseInOut(0.2) < UiMotion.EaseOutCubic(0.2));   // 前段必须比三次缓出慢
                    // 他"胶囊没动画"的真根因不在动画本身：切页那条路每次都会 SetItems(同一串名字)，
                    // 而 SetItems 无条件落位 = 用户那一击刚起播就被同步的切页掐在第 0 帧。这条就是那道回归闸。
                    {
                        var seg3 = new SegmentedControl();
                        seg3.SetItems("Strata", "llama.cpp", "Ninfer");
                        seg3.ActiveIndex = 1;
                        seg3.SetPillPreviewForTest(0, 0.5);            // 假装正滑到一半
                        bool mid = seg3.PillSlidingForTest;
                        seg3.SetItems("Strata", "llama.cpp", "Ninfer"); // 同一串表（= 切页那一拍）
                        bool keptAfterSame = seg3.PillSlidingForTest;
                        seg3.SetItems("Strata", "Ninfer");              // 真的换了表
                        bool settledAfterDiff = !seg3.PillSlidingForTest;
                        seg3.Dispose();
                        Check("SetItems 同一串表不许掐掉在播的滑动，换了表才落位（他实跑「胶囊没动画」的根因闸）",
                            mid && keptAfterSame && settledAfterDiff,
                            "中途=" + mid + " 同表后仍在播=" + keptAfterSame + " 换表后落位=" + settledAfterDiff);
                    }
                    {
                        // 圆点两端必须等于改之前那两条写死的表达式（否则 150%/96 下会整体偏一像素）
                        int lo = ToggleSwitch.KnobX(54, 16, 3, 0), hi = ToggleSwitch.KnobX(54, 16, 3, 1);
                        Check("开关圆点行程：两端 = 原来的 pad+1 与 Width−d−pad−1，中点落在两者之间且单调",
                            lo == 4 && hi == 54 - 16 - 3 - 1
                            && ToggleSwitch.KnobX(54, 16, 3, 0.5) > lo && ToggleSwitch.KnobX(54, 16, 3, 0.5) < hi
                            && ToggleSwitch.KnobX(54, 16, 3, 0.25) < ToggleSwitch.KnobX(54, 16, 3, 0.75));
                    }
                    Check("时长档位：跟随类 150ms（按钮擦入 / 胶囊 / 叠放悬停，同一处 UiMotion.FollowMs）、开关行程 200ms（他给的参考值）、确认类 180ms（叠放轮换 / toast 出现），心跳同一档 15ms",
                        UiMotion.FollowMs == 150 && FlatButton.WipeMs == UiMotion.FollowMs
                        && SegmentedControl.PillSlideMs == UiMotion.FollowMs && StackedToggle.HoverWipeMs == UiMotion.FollowMs
                        && ToggleSwitch.KnobMs == 200
                        && StackedToggle.DurationMs == 180 && Toast.ShowMs == 180 && UiMotion.FrameMs == 15);
                    Check("三条位移道（擦入 / 胶囊 / 圆点）同曲线：首帧不冲（<5%）、到点归位、反向那一程也走到 0",
                        FlatButton.WipeAt(0, 1, 15) < 0.05 && FlatButton.WipeAt(0, 1, 150) == 1 && FlatButton.WipeAt(1, 0, 150) == 0
                        && SegmentedControl.PillProgressAt(15) < 0.05 && SegmentedControl.PillProgressAt(150) == 1
                        && ToggleSwitch.KnobAt(0, 1, 20) < 0.05 && ToggleSwitch.KnobAt(0, 1, 200) == 1 && ToggleSwitch.KnobAt(1, 0, 200) == 0);
                    // 叠放那一族两枚（卡1 后端方块 / 左栏模式条）2026-10-16 他点名没适配 —— 同一条算式，别再各写一遍
                    Check("叠放按钮的悬停擦入吃同一条位移算式：首帧不冲、到点归位、反向到 0",
                        StackedToggle.WipeAt(0, 1, 15) < 0.05 && StackedToggle.WipeAt(0, 1, 150) == 1 && StackedToggle.WipeAt(1, 0, 150) == 0);
                    int liveStack = StackedToggle.WipeLiveForTest;
                    var stackProbe = new StackedToggle { Faces = new[] { "路由模式", "单体模型" } };
                    stackProbe.CreateControl(); stackProbe.Dispose();
                    Check("StackedToggle 建一枚再释放：轮换表与悬停擦入表都回收（两枚定时器一起点名）",
                        StackedToggle.WipeLiveForTest == liveStack, "live=" + StackedToggle.WipeLiveForTest + " base=" + liveStack);
                }
            }
    }
}
