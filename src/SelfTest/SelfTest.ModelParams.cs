using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MultiLauncher;

// A/C（2026-10-22，方案「Strata 参数拆分 + 手加键覆盖层」§7 判据 6~8；DEC-206 / DEC-207 / DEC-210）：
// 模型参数与手加参数的家 = `data\config\strata\strata_models.ini`（一个模型一段：那 16 格 + `Args1..ArgsN`）。
// 这里只喂样本、只碰 data\tmp（物化改道之后的 `selftest-run\` 那一窝），不读也不写任何一份真配置。
internal static partial class SelfTest
{
    // ---------------- 判据用的读写小工具（只碰改道后的临时目录） ----------------

    /// <summary>往 `strata_models.ini` 某一段塞 `Args1..ArgsN`（读改写；没有文件就建）。判据 4/5 造"手加参数"用它。</summary>
    internal static void WriteArgsBlockForTest(string tag, IEnumerable<string> args)
    {
        var path = MainForm.ModelParamsPath();
        var doc = File.Exists(path) ? ModelParamsIni.Parse(File.ReadAllText(path)) : new ModelParamsIni.Doc();
        ModelParamsIni.WriteArgs(doc, tag, args.ToList());
        var dir = Path.GetDirectoryName(path);
        if (dir is not null) Directory.CreateDirectory(dir);
        File.WriteAllText(path, ModelParamsIni.Text(doc), new UTF8Encoding(false));
    }

    /// <summary>把某一段整个摘掉（用例收尾用 —— 别把夹具留在改道目录里给后面的用例看）。</summary>
    internal static void RemoveGroupForTest(string tag)
    {
        var path = MainForm.ModelParamsPath();
        if (!File.Exists(path)) return;
        try
        {
            var doc = ModelParamsIni.Parse(File.ReadAllText(path));
            if (ModelParamsIni.Remove(doc, tag))
                File.WriteAllText(path, ModelParamsIni.Text(doc), new UTF8Encoding(false));
        }
        catch { /* 清不掉不影响结论 */ }
    }

    internal static void ModelSnapFileChecks()
    {
        // --- 判据 6：命名约束 —— 新文件不许被"模板判定"认成模板（方案 §2.5 第 2 条）---
        // 回退/快照链那一支的判据就是 MainForm.Preserve：`strata-*.json`（连字符）= 上游模板；
        // 我们的 `strata_models.ini` 是下划线，必须不被认成内置模板。
        Check("A/C 判据6 命名约束：strata_models.ini（及老名 strata_models.json）/ <tag>.ui.json / <tag>.extra.json 都不被模板判定认成模板（对照组 strata-*.json 照旧认）",
            !MainForm.Preserve(MainForm.ModelParamsFileName) && !MainForm.Preserve("iq3_s.ui.json")
            && !MainForm.Preserve("iq3_s.extra.json") && MainForm.Preserve("strata-iq3_s.json"),
            $"store={MainForm.Preserve(MainForm.ModelParamsFileName)} ui={MainForm.Preserve("iq3_s.ui.json")} "
            + $"extra={MainForm.Preserve("iq3_s.extra.json")} 模板={MainForm.Preserve("strata-iq3_s.json")}");

        // --- 判据 7：迁移 —— 老 [Model:x] 段 → 一份文件里的两组；内容等价；老段不动；幂等；文件赢；
        //     老形态（每模型一份 ui / extra）并进来 ---
        {
            var md = MainForm.Scratch("selftest-snapmig-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(md);
            const string mtag = "mig-alpha", btag = "mig-beta";
            try
            {
                var iniLines = new[]
                {
                    "[Current]", "Port=8080", "Model=" + mtag,
                    "[Model:" + mtag + "]", "Context=262144", "Kv=q4_0", "KvResident=True", "ExtraArgs=--old 1",
                    "[Model:" + btag + "]", "Context=131072",
                    "[Window]", "X=1", "Y=2", "Width=300", "Height=200", "Maximized=False",
                };
                var snaps = new Dictionary<string, Settings>(StringComparer.OrdinalIgnoreCase);
                var cur = new Settings();
                MainForm.ParseConfigLines(iniLines, cur, snaps, new MainForm.ConfigParseResult());
                var iniPath = Path.Combine(md, "MultiLauncher.ini");
                File.WriteAllLines(iniPath, iniLines);
                var iniBefore = File.ReadAllBytes(iniPath);

                var notes = new List<string>();
                var (fromIni, fromFile, fromLegacy) = MainForm.ReconcileModelSnaps(snaps, notes);
                var storePath = MainForm.ModelParamsPath();
                var doc = ModelParamsIni.Parse(File.ReadAllText(storePath));
                Check("A 判据7a 迁移：老 ini 的两个 [Model:*] 段落成**同一份** strata_models.ini 里的两段（DEC-210 起是 ini）",
                    fromIni == 2 && fromFile == 0 && fromLegacy == 0 && File.Exists(storePath)
                    && doc.Find(mtag) is not null && doc.Find(btag) is not null,
                    $"fromIni={fromIni} fromFile={fromFile} fromLegacy={fromLegacy}｜" + string.Join(" / ", notes));
                var loaded7 = new Dictionary<string, Settings>(StringComparer.OrdinalIgnoreCase);
                MainForm.LoadModelSnapStore(loaded7, new List<string>());
                Check("A 判据7b 迁移：搬过去的键值等价（读回文件与老段逐个对得上）",
                    loaded7.TryGetValue(mtag, out var back)
                    && back.Context == "262144" && back.Kv == "q4_0" && back.KvResident && back.ExtraArgs == "--old 1",
                    loaded7.TryGetValue(mtag, out var bv7) ? $"ctx={bv7.Context} kv={bv7.Kv} resident={bv7.KvResident}" : "没读到这一件");
                Check("A 判据7c 迁移：ini 一个字节都没动（老 [Model:*] 段原样留着；删它要等下一次成功保存）",
                    File.ReadAllBytes(iniPath).AsSpan().SequenceEqual(iniBefore));

                var before = File.ReadAllBytes(storePath);
                var notes2 = new List<string>();
                var (fromIni2, fromFile2, _) = MainForm.ReconcileModelSnaps(snaps, notes2);
                Check("A 判据7d 幂等：再跑一次 = 一条都不搬、文件字节不变（跑两次结果一样）",
                    fromIni2 == 0 && fromFile2 >= 2 && File.ReadAllBytes(storePath).AsSpan().SequenceEqual(before),
                    $"fromIni={fromIni2} fromFile={fromFile2}");

                // 7e：文件赢（A6）—— 文件里改了值，以文件为准，不被老段盖回去
                var edit = ModelParamsIni.Parse(File.ReadAllText(storePath));
                ModelParamsIni.SetKeys(edit, mtag, new[]
                {
                    new KeyValuePair<string, string>("Context", "524288"),
                    new KeyValuePair<string, string>("Kv", "int8"),
                });
                File.WriteAllText(storePath, ModelParamsIni.Text(edit));
                MainForm.ReconcileModelSnaps(snaps, new List<string>());
                Check("A 判据7e 文件赢：文件里的值盖过老 ini 段（老段只在文件不存在时读一次）",
                    snaps[mtag].Context == "524288" && snaps[mtag].Kv == "int8",
                    $"ctx={snaps[mtag].Context} kv={snaps[mtag].Kv}");

                // 7f：DEC-206 那版「每模型一份 <tag>.ui.json」被并进来，原件挪去备份、目录里不再有它
                const string ltag = "mig-legacy";
                var legacyPath = Path.Combine(MainForm.RunConfigDir(), ltag + MainForm.ModelUiSuffix);
                File.WriteAllText(legacyPath, MainForm.ModelSnapJson(new Settings { Context = "65536", Spec = "9" }));
                var legacyBaksBefore = MainForm.ListBackupsIn(MainForm.ModelUiBackupDir(), MainForm.ModelUiBackupPrefix(ltag)).Count;
                var snaps3 = new Dictionary<string, Settings>(StringComparer.OrdinalIgnoreCase);
                var notes3 = new List<string>();
                var (_, _, legacy3) = MainForm.ReconcileModelSnaps(snaps3, notes3);
                var legacyBaks = MainForm.ListBackupsIn(MainForm.ModelUiBackupDir(), MainForm.ModelUiBackupPrefix(ltag));
                Check("A 判据7f 老形态并进来：<tag>.ui.json → 该组 ui 块，原件挪进备份、目录里不再有它",
                    legacy3 == 1 && !File.Exists(legacyPath) && snaps3.TryGetValue(ltag, out var got)
                    && got.Context == "65536" && got.Spec == "9" && legacyBaks.Count == legacyBaksBefore + 1,
                    $"legacy={legacy3} 备份={string.Join("、", legacyBaks)}｜" + string.Join(" / ", notes3));

                // 7g：C 那版的手加键文件也并进该段的 ArgsN 行
                const string xtag = "mig-xtra";
                var xPath = MainForm.ExtraOverlayPath(xtag);
                File.WriteAllText(xPath, "{\"args\":[\"--up-flag\",\"2\"]}");
                MainForm.ReconcileModelSnaps(new Dictionary<string, Settings>(StringComparer.OrdinalIgnoreCase), new List<string>());
                var xSec = ModelParamsIni.Parse(File.ReadAllText(storePath)).Find(xtag);
                var xa = xSec is null ? new List<string>() : ModelParamsIni.ReadArgs(xSec);
                Check("A 判据7g 老的手加键文件并进来：<tag>.extra.json 的 args → 该段的 Args1/Args2（原件挪进备份）",
                    xa.Count == 2 && xa[0] == "--up-flag" && !File.Exists(xPath),
                    xSec is null ? "没有这一段" : string.Join(" / ", xa));
                // 7i（DEC-210）：DEC-207 那一版的 `strata_models.json` 并进 ini —— 16 格与 ArgsN 都在，
                // 原件挪进备份、目录里不留第二份（ini 这一版不靠"删旧"来保证，靠移走 + 备份）
                const string jtag = "mig-json";
                var jPath = MainForm.LegacyModelParamsPath();
                File.WriteAllText(jPath,
                    "{\"" + jtag + "\":{\"ui\":{\"Context\":\"262144\",\"Kv\":\"int8\"},\"args\":[\"--j-flag\",\"5\"]}}");
                var jSnaps = new Dictionary<string, Settings>(StringComparer.OrdinalIgnoreCase);
                var jNotes = new List<string>();
                int jGot = MainForm.MigrateLegacyParamsJson(jSnaps, jNotes);
                var jSec = ModelParamsIni.Parse(File.ReadAllText(storePath)).Find(jtag);
                Check("A 判据7i DEC-207 的 strata_models.json 并进 ini：16 格与 ArgsN 都在、原件挪进备份（DEC-210）",
                    jGot == 1 && !File.Exists(jPath) && jSec is not null
                    && ModelParamsIni.TryGet(jSec, "Context", out var jctx) && jctx == "262144"
                    && ModelParamsIni.ReadArgs(jSec).SequenceEqual(new[] { "--j-flag", "5" }),
                    $"got={jGot}｜" + string.Join(" / ", jNotes));
                // 7i 的备份是夹具产物（前缀 strata_models-json-），别留到判据 8c 的"备份数 +1"里去搅局
                foreach (var jb in MainForm.ListBackupsIn(MainForm.ModelUiBackupDir(), "strata_models-json-"))
                { try { File.Delete(Path.Combine(MainForm.ModelUiBackupDir(), jb)); } catch { /* 删不掉不影响结论 */ } }
            }
            finally
            {
                // 7h：DEC-208 —— 产物与伴生文件藏进 run\ 子目录：普通 <tag>.json 搬、壳自己的文件不搬、两边都有不动
                const string ptag = "mig-prod";
                var topProd = Path.Combine(MainForm.RunConfigDir(), ptag + ".json");
                File.WriteAllText(topProd, "{\"port\":8080}");
                const string ptag2 = "mig-prod2";
                var topProd2 = Path.Combine(MainForm.RunConfigDir(), ptag2 + ".json");
                File.WriteAllText(topProd2, "{\"port\":8081}");
                var ourStore = Path.Combine(MainForm.RunConfigDir(), MainForm.ModelParamsFileName);
                File.WriteAllText(ourStore, "{}");     // 故意放一份壳自己的文件在那，证明它**不**被搬
                var runDir = MainForm.RunProductDir();
                Directory.CreateDirectory(runDir);
                var clash = Path.Combine(runDir, ptag + ".json");
                File.WriteAllText(clash, "{\"port\":9999}");          // 两边都有：都不动
                var logM = new List<string>();
                logM.AddRange(MainForm.MigrateProductsIntoRunDir());
                Check("A 判据7h run\\ 搬家：顶层产物搬进 run\\；壳自己的 strata_models.ini 留在顶层不搬",
                    File.Exists(Path.Combine(runDir, ptag2 + ".json")) && !File.Exists(topProd2)
                    && File.Exists(ourStore) && File.Exists(Path.Combine(MainForm.RunConfigDir(), MainForm.ModelParamsFileName)),
                    string.Join(" / ", logM));
                Check("A 判据7i run\\ 搬家：两边都有 = 都不动并点名（不覆盖任何一边）",
                    File.Exists(topProd) && File.ReadAllText(clash) == "{\"port\":9999}"
                    && logM.Any(l => l.Contains("两边都有") && l.Contains(ptag + ".json")),
                    string.Join(" / ", logM));
                File.Delete(topProd);
                File.Delete(ourStore);
                try { File.Delete(clash); } catch { }
                try { File.Delete(Path.Combine(runDir, ptag2 + ".json")); } catch { }
                foreach (var t in new[] { mtag, btag, "mig-legacy", "mig-xtra" })
                {
                    try { RemoveGroupForTest(t); } catch { /* 清不掉不影响结论 */ }
                }
                try { File.Delete(MainForm.ExtraOverlayPath("mig-xtra")); } catch { }
                try { Directory.Delete(md, true); } catch { /* 清不掉不影响结论 */ }
            }
        }

        // --- 判据 8：往返 —— 16 格存进 one file 再读回；内容没变不刷盘；写前有备份、留 3 份；
        //     壳只改自己那块（不冲掉你写的 args / 备注）；文件坏了不写 ---
        {
            const string rtag = "rt-snap";
            var bakDir = MainForm.ModelUiBackupDir();
            var bakPrefix = MainForm.ModelUiStoreBackupPrefix;
            var storePath = MainForm.ModelParamsPath();
            var originalStore = File.Exists(storePath) ? File.ReadAllText(storePath) : null;
            try
            {
                var s = new Settings
                {
                    ApiModel = "m", Context = "262144", Kv = "int8", KvResident = true, Spec = "6", SpecMinP = "0.55",
                    ExpertCache = "auto", Prefill = "auto:4096", Vision = "cpu", VramReserveMib = "900",
                    Gpu = "0,2", LayerSplit = "12", ArenaPinGib = "8", HeadLate = true,
                    ExtraArgs = "--foo 1", ReasoningEffort = "medium",
                };
                var snap = MainForm.SnapshotModelFields(s);
                var store = new Dictionary<string, Settings>(StringComparer.OrdinalIgnoreCase) { [rtag] = snap };
                bool ok = MainForm.SaveModelSnapStore(store, out var wrote, out var err);
                var loaded = new Dictionary<string, Settings>(StringComparer.OrdinalIgnoreCase);
                int n = ok ? MainForm.LoadModelSnapStore(loaded, new List<string>()) : 0;
                Check("A 判据8a 往返：16 格存进 strata_models.ini 再读回，值一格不差",
                    ok && wrote && n >= 1 && loaded.TryGetValue(rtag, out var rt) && MainForm.ModelSnapEquals(snap, rt),
                    ok ? (loaded.TryGetValue(rtag, out var rt2)
                            ? $"ctx={rt2.Context} kv={rt2.Kv} gpu={rt2.Gpu} split={rt2.LayerSplit} eff={rt2.ReasoningEffort}"
                            : "没读回来") : err);

                bool ok2 = MainForm.SaveModelSnapStore(store, out var wrote2, out _);
                Check("A 判据8b 内容没变 = 一个字节都不动（不刷 mtime、不占备份位）", ok2 && !wrote2, "wrote=" + wrote2);

                // 8c：改一格再存 → 写前备份落进备份家（名字 strata_models-<原件时间戳>.ini，后缀跟着源文件走）
                var changed = MainForm.SnapshotModelFields(s);
                changed.Context = "524288";
                var store2 = new Dictionary<string, Settings>(StringComparer.OrdinalIgnoreCase) { [rtag] = changed };
                int baksBefore = MainForm.ListBackupsIn(bakDir, bakPrefix).Count;
                bool ok3 = MainForm.SaveModelSnapStore(store2, out var wrote3, out var err3);
                var baks = MainForm.ListBackupsIn(bakDir, bakPrefix);
                Check("A 判据8c 写前备份：改动那一份存之前，旧的那份进了备份家（strata_models-<时间戳>.ini）",
                    ok3 && wrote3 && baks.Count == baksBefore + 1 && baks[0].StartsWith(bakPrefix) && baks[0].EndsWith(".ini"),
                    ok3 ? string.Join("、", baks) : err3);

                // 8d：备份只留 3 份（DEC-110⑤ 的口径；反复改不许无限长）
                for (int i = 0; i < 5; i++)
                {
                    var v = MainForm.SnapshotModelFields(s);
                    v.Context = (600000 + i).ToString();
                    MainForm.SaveModelSnapStore(new Dictionary<string, Settings>(StringComparer.OrdinalIgnoreCase) { [rtag] = v }, out _, out _);
                }
                var baks2 = MainForm.ListBackupsIn(bakDir, bakPrefix);
                Check("A 判据8d 备份留 3 份（连着改 6 次也不许长过 3 份）",
                    baks2.Count <= MainForm.ModelUiBackupKeep, $"{baks2.Count} 份：" + string.Join("、", baks2));

                // 8e：壳只改自己那 16 格 —— 段里的 ArgsN、自建键、注释必须原样留着
                var withUserStuff = ModelParamsIni.Parse(File.ReadAllText(storePath));
                ModelParamsIni.WriteArgs(withUserStuff, rtag, new[] { "--hand-flag", "3" });
                var secUser = withUserStuff.Find(rtag)!;
                secUser.Body.Add("note=这是我自己写的备注");
                secUser.Body.Add("; 这是我自己写的注释");
                File.WriteAllText(storePath, ModelParamsIni.Text(withUserStuff));
                var again = MainForm.SnapshotModelFields(s);
                again.Context = "393216";
                MainForm.SaveModelSnapStore(new Dictionary<string, Settings>(StringComparer.OrdinalIgnoreCase) { [rtag] = again }, out _, out _);
                var afterSec = ModelParamsIni.Parse(File.ReadAllText(storePath)).Find(rtag);
                Check("A 判据8e 壳只改那 16 格：你写的 Args1/Args2、自建键、注释在保存后原样还在",
                    afterSec is not null
                    && ModelParamsIni.ReadArgs(afterSec).SequenceEqual(new[] { "--hand-flag", "3" })
                    && ModelParamsIni.TryGet(afterSec, "note", out var noteU) && noteU == "这是我自己写的备注"
                    && afterSec.Body.Any(l => l.TrimStart().StartsWith(';'))
                    && ModelParamsIni.TryGet(afterSec, "Context", out var ctxU) && ctxU == "393216",
                    afterSec is null ? "没有这一段" : string.Join(" / ", afterSec.Body));

                // 8f（DEC-210 跟改，不是删断言）：ini 的失效模式不是"整份读不动"，而是**某一段的某个键写了非法值**。
                // 判据因此改成正反两面：读的时候**点名跳过那一段**（不崩、不半份生效）；
                // 保存后那一格被纠正成合法值，而段里的注释与 ArgsN 一行不动 —— "不会把你写的东西冲掉"仍钉得住。
                var badDoc = ModelParamsIni.Parse(File.ReadAllText(storePath));
                var secBad = badDoc.Find(rtag)!;
                secBad.Body.RemoveAll(l => ModelParamsIni.KeyOf(l) is { } kb && kb.Equals("KvResident", StringComparison.OrdinalIgnoreCase));
                secBad.Body.Insert(0, "KvResident=这不该是布尔值");
                File.WriteAllText(storePath, ModelParamsIni.Text(badDoc));
                var notesBad = new List<string>();
                var loadedBad = new Dictionary<string, Settings>(StringComparer.OrdinalIgnoreCase);
                MainForm.LoadModelSnapStore(loadedBad, notesBad);
                bool skippedBad = !loadedBad.ContainsKey(rtag) && notesBad.Count > 0;
                var fixSnap = MainForm.SnapshotModelFields(s);
                MainForm.SaveModelSnapStore(new Dictionary<string, Settings>(StringComparer.OrdinalIgnoreCase) { [rtag] = fixSnap }, out _, out _);
                var fixedSec = ModelParamsIni.Parse(File.ReadAllText(storePath)).Find(rtag);
                Check("A 判据8f 段里有非法值：读的时候点名跳过（不半份生效），保存后那一格被纠正，你的注释与 ArgsN 一行不动",
                    skippedBad && fixedSec is not null
                    && ModelParamsIni.TryGet(fixedSec, "KvResident", out var krf) && MainForm.TryParseIniBool(krf, out var krb) && krb
                    && fixedSec.Body.Any(l => l.TrimStart().StartsWith(';'))
                    && ModelParamsIni.ReadArgs(fixedSec).Count == 2,
                    $"跳过={skippedBad}｜" + string.Join(" / ", notesBad));
            }
            finally
            {
                try
                {
                    if (originalStore is null) { if (File.Exists(storePath)) File.Delete(storePath); }
                    else File.WriteAllText(storePath, originalStore);
                }
                catch { /* 清不掉不影响结论 */ }
                try
                {
                    foreach (var b in MainForm.ListBackupsIn(bakDir, bakPrefix))
                        File.Delete(Path.Combine(bakDir, b));
                }
                catch { /* 清不掉不影响结论 */ }
            }
        }

        // --- 判据 9（DEC-209）：逐模型补齐 —— 已有记录之后新加的模型，从各自的模板补上一份 ---
        {
            var md = MainForm.Scratch("selftest-fill-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(md);
            try
            {
                File.WriteAllText(Path.Combine(md, "strata-a.json"),
                    "{\"model_name\":\"ma\",\"args\":[\"--max-context\",\"262144\",\"--kv\",\"int8\"],\"env\":{\"STRATA_ARENA_PIN_GIB\":\"16\"}}");
                File.WriteAllText(Path.Combine(md, "strata-b.json"),
                    "{\"model_name\":\"mb\",\"args\":[\"--max-context\",\"524288\"],\"env\":{\"STRATA_ARENA_PIN_GIB\":\"12\"}}");
                var snaps = new Dictionary<string, Settings>(StringComparer.OrdinalIgnoreCase)
                {
                    ["a"] = new Settings { Context = "4096" },      // 已有的记录：一格都不许被模板盖掉
                };
                var filled = new List<string>();
                int got = MainForm.FillMissingModelSnapsFromTemplates(snaps, new[] { "a", "b", "c" }, md, filled);
                Check("A 判据9a 逐模型补齐：已有 1 条记录时，新模型照样补上（不再全有或全无）",
                    got == 1 && filled.Count == 1 && filled[0] == "b" && snaps.Count == 2,
                    $"got={got} filled={string.Join("、", filled)} count={snaps.Count}");
                Check("A 判据9b 补齐取的是模板值，ArenaPinGib 留空（不把模板里那个数钉成你的记录）",
                    snaps["b"].Context == "524288" && snaps["b"].ApiModel == "mb" && snaps["b"].ArenaPinGib == ""
                    && snaps["a"].Context == "4096",
                    $"b.ctx={snaps["b"].Context} b.api={snaps["b"].ApiModel} b.arena='{snaps["b"].ArenaPinGib}' a.ctx={snaps["a"].Context}");
                Check("A 判据9c 幂等：已补过的不重补，没模板的跳过（一个都不补）",
                    MainForm.FillMissingModelSnapsFromTemplates(snaps, new[] { "a", "b", "c" }, md, new List<string>()) == 0);
            }
            finally { try { Directory.Delete(md, true); } catch { /* 清不掉不影响结论 */ } }
        }

        // --- 判据 10（DEC-211）：手改 ini 之后，**拉起服务前**会重读（他报"改完停止再启动不生效"） ---
        {
            var path = MainForm.ModelParamsPath();
            var before = File.Exists(path) ? File.ReadAllText(path) : null;
            try
            {
                Directory.CreateDirectory(MainForm.RunConfigDir());
                File.WriteAllText(path, "[rl-a]\nContext=131072\n\n[rl-b]\nContext=262144\n", new UTF8Encoding(false));
                var snaps = new Dictionary<string, Settings>(StringComparer.OrdinalIgnoreCase)
                {
                    ["rl-a"] = new Settings { Context = "131072" },
                    ["rl-b"] = new Settings { Context = "262144" },
                };
                MainForm.ResetModelParamsStampForTest();
                MainForm.ReloadModelSnapsIfChanged(snaps, new List<string>(), out _);       // 首次只把版本戳记下来
                File.WriteAllText(path, "[rl-a]\nContext=524288\n\n[rl-b]\nContext=262144\n", new UTF8Encoding(false));
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(5));               // 戳要真的变（同秒内写两次会被判"没动"）
                var hits = MainForm.ReloadModelSnapsIfChanged(snaps, new List<string>(), out var reloaded2);
                Check("A 判据10a 手改 ini 之后拉起服务会重读：值跟着变、只报真的变了的那个模型（DEC-211）",
                    reloaded2 && hits.Count == 1 && hits[0] == "rl-a" && snaps["rl-a"].Context == "524288"
                    && snaps["rl-b"].Context == "262144",
                    $"hits={string.Join("、", hits)} a.ctx={snaps["rl-a"].Context} b.ctx={snaps["rl-b"].Context}");
                var again = MainForm.ReloadModelSnapsIfChanged(snaps, new List<string>(), out var reloaded3);
                Check("A 判据10b 文件没动就不重读：一条都不报（免得把界面上没保存的草稿冲掉）",
                    !reloaded3 && again.Count == 0, $"reloaded={reloaded3} n={again.Count}");
                // 10c：**两个入口都要挂**（2026-10-22 补 —— 只挂 StartService 的话，路由模式下点「停止」再「启动」
                // 走的其实是 StartServer 只立网关那一支，重读永远不跑，他实测就是"改了没反应"）。
                // -1 = 源码不在旁边（实跑目录跑自检）：按项目既有口径"判不了就不判"，别把环境当回归。
                int hookHits = CodeHitsBetween("MainForm.Service.cs", "void StartServer", "internal bool StartService",
                    "ReloadModelParamsFromDiskIfChanged");
                Check("A 判据10c StartServer（路由模式只立网关那一支）也调重读、且只调一次（DEC-211）",
                    hookHits is 1 or -1, "hits=" + hookHits);
            }
            finally
            {
                MainForm.ResetModelParamsStampForTest();
                try
                {
                    if (before is null) { if (File.Exists(path)) File.Delete(path); }
                    else File.WriteAllText(path, before);
                }
                catch { /* 清不掉不影响结论 */ }
            }
        }

        // --- 判据 11（DEC-212）：段间只留 1 个空行，且反复读写不再增长 ---
        // 他 2026-10-22 报"中间的空行太多"：段尾空行以前被当成段内容收进来，写回时再补一个 ——
        // 每保存一次多一行，攒到第八个才被看见。
        {
            var t1 = ModelParamsIni.Text(ModelParamsIni.Parse("[a]\nContext=1\n\n\n\n[b]\nContext=2\n\n\n"));
            var t2 = ModelParamsIni.Text(ModelParamsIni.Parse(t1));
            var t3 = ModelParamsIni.Text(ModelParamsIni.Parse(t2));
            Check("A 判据11a 段间空行归一：读进来再写出去只有 1 个空行，反复几轮也不再增长（DEC-212）",
                t1 == t2 && t2 == t3 && !t1.Contains("\r\n\r\n\r\n"),
                t1.Replace("\r\n", "⏎").Replace("⏎⏎", "「空行」"));
            // 11b：拉 llama 之前也要查一次家底文件有没有被改过（他报"改完 ini 重启服务界面不更新"）
            int llamaHook = CodeHitsBetween("MainForm.LlamaLifecycle.cs", "void StartLlamaServer",
                "var s = CollectSettings()", "ReloadLlamaRecordIfIniChanged");
            Check("A 判据11b 拉 llama 之前查一次 llama_models.ini 是否被手改过（DEC-212）",
                llamaHook is 1 or -1, "hits=" + llamaHook);   // -1 = 源码不在旁边（实跑目录），判不了就不判
        }

        // --- 判据 12（2026-10-23 只读审查 A / D 条）：手改的那份不许被"顺手落盘"盖回去；
        //     手删掉一整段，重读之后内存里那条也要跟着消失（否则下一次保存又把它写回来）---
        {
            // 12a：模型参数文件**只在提交那一趟**写。反面就是他 2026-10-22 那套流程会踩的坑 ——
            // 改完 ini、还没重新启动的这段时间里点一次设置页「保存」/ 关窗，16 格就被内存那份旧值盖回去了。
            int wired = CodeHitsBetween("MainForm.WakeSettings.cs", "bool SaveConfig(bool commitParams)",
                                        "var disk = commitParams ? cur : ForDisk(cur)", "SaveWritesModelStore");
            Check("A 判据12a 模型参数文件只在「提交」那一趟写（commitParams=false 不写；-1 = 源码不在旁边）",
                !MainForm.SaveWritesModelStore(false) && MainForm.SaveWritesModelStore(true)
                && wired is 1 or -1,
                $"false→{MainForm.SaveWritesModelStore(false)} true→{MainForm.SaveWritesModelStore(true)}｜hits={wired}");

            // 12b：删段这一面。他 2026-10-22 清那 5 份孤儿记录时被"关窗又把内存里的记录写回来"折腾了两遍。
            var path = MainForm.ModelParamsPath();
            var before = File.Exists(path) ? File.ReadAllText(path) : null;
            const string keepTag = "drop-keep", goneTag = "drop-gone";
            try
            {
                Directory.CreateDirectory(MainForm.RunConfigDir());
                File.WriteAllText(path, "[" + keepTag + "]\nContext=131072\n\n[" + goneTag + "]\nContext=262144\n",
                    new UTF8Encoding(false));
                var snaps = new Dictionary<string, Settings>(StringComparer.OrdinalIgnoreCase)
                {
                    [keepTag] = new Settings { Context = "131072" },
                    [goneTag] = new Settings { Context = "262144" },
                };
                MainForm.ResetModelParamsStampForTest();
                MainForm.ReloadModelSnapsIfChanged(snaps, new List<string>(), out _);      // 首次只把版本戳记下来
                // 手删掉整整一段（就是他在编辑器里按几下 Delete 那一手）
                var doc = ModelParamsIni.Parse(File.ReadAllText(path));
                ModelParamsIni.Remove(doc, goneTag);
                File.WriteAllText(path, ModelParamsIni.Text(doc), new UTF8Encoding(false));
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(5));
                var hits = MainForm.ReloadModelSnapsIfChanged(snaps, new List<string>(), out var reloaded);
                bool gone = !snaps.ContainsKey(goneTag) && snaps.ContainsKey(keepTag);
                // 再落一次盘：不许把删掉的那段写回来
                MainForm.SaveModelSnapStore(snaps, out _, out _);
                bool resurrected = ModelParamsIni.Parse(File.ReadAllText(path)).Find(goneTag) is not null;
                Check("A 判据12b 手删掉一整段：重读后内存里那条跟着消失，下一次落盘不会把它写回来",
                    reloaded && gone && hits.Contains(goneTag) && !resurrected,
                    $"reloaded={reloaded} gone={gone} hits={string.Join("、", hits)} 复活={resurrected}");
            }
            finally
            {
                MainForm.ResetModelParamsStampForTest();
                try
                {
                    if (before is null) { if (File.Exists(path)) File.Delete(path); }
                    else File.WriteAllText(path, before);
                }
                catch { /* 清不掉不影响结论 */ }
            }
        }
    }
}
