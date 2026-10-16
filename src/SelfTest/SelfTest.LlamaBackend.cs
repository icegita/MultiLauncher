using System.Text;
namespace MultiLauncher;

// 用例 9 主段（9a–9e）：内部端口回落、就地读-改-写（DEC-079）、命令行组装、在真家底副本上就地写、
// 写前备份与轮转、[Current] 退役、按钮文案穷举。2026-10-15 从 SelfTest.Gateway.cs 按子域切出，
// 块内一行未改。
internal static partial class SelfTest
{
    internal static void LlamaBackendChecks()
    {
                // 9a. 内部端口回落：留空/非法/与公开端口或 Strata 内部端口撞车都回落到「公开端口 + 2」
                Check("llama 端口留空 -> 公开端口 + 2", MainForm.NormalizeLlamaPort(8080, 8081, "") == 8082);
                Check("llama 端口非法 -> 公开端口 + 2", MainForm.NormalizeLlamaPort(8080, 8081, "abc") == 8082);
                Check("llama 端口等于公开端口 -> 回落", MainForm.NormalizeLlamaPort(8080, 8081, "8080") == 8082);
                Check("llama 端口与 Strata 内部端口撞 -> 跳过",
                    MainForm.NormalizeLlamaPort(8080, 8082, "8082") == 8083,
                    MainForm.NormalizeLlamaPort(8080, 8082, "8082").ToString());
                Check("llama 端口合法且不撞 -> 原样使用", MainForm.NormalizeLlamaPort(8080, 8081, "9099") == 9099);

                // 9b. 就地读-改-写（DEC-079 取代 DEC-008"一个字节不改"与 DEC-018"合并 ini 覆盖每个段"）
                //     夹具刻意复刻本机那份家底的形状：**LF 换行、无 BOM、多处行尾空格、段尾有 `#` 注释块**
                //     （用例只在这份临时副本上写，绝不碰 config\ 与 exe 同目录的真家底）。
                var routerIni = MainForm.Scratch("selftest-router.ini");
                File.WriteAllBytes(routerIni, new UTF8Encoding(false).GetBytes(string.Join("\n", new[]
                {
                    "version = 1",
                    "",
                    "[*]",
                    "flash-attn = on",
                    "jinja = True",
                    "reasoning-effort = medium",
                    "spec-type = none",              // DEC-145：全局档 —— model-b 走它（算继承）
                    "no-mmproj-offload = true ",     // 行尾一个空格（本机 `[*]` 那行就是这样）
                    "",
                    "[model-a]",
                    @"model = C:\models\a.gguf",
                    "temperature = 0.1",
                    "ctx-size = 4096 ",              // 行尾空格：没动的行必须原样留着
                    "cache-type-k = q8_0",
                    "cache-type-v = q4_0",
                    "spec-type = draft-mtp",         // DEC-145：段里自己写了 = 不继承
                    "no-mmproj-offload = on",        // DEC-145：视觉用**旧拼写**写在这一段（两拼写折算的现场）
                    "threads = 16      ",            // 一串行尾空格
                    "",
                    "[model-b]",
                    @"model = C:\models\b.gguf",
                    "ngl = 99 ",
                    "",
                    "# 尾部注释（插入点必须在它之前）",
                })));
                var iniHead0 = File.ReadAllBytes(routerIni);
                Check("夹具是 LF 换行、无 BOM（本机实测就是这两样）",
                    !iniHead0.Contains((byte)'\r') && !(iniHead0.Length >= 3 && iniHead0[0] == 0xEF), $"{iniHead0.Length} 字节");
                // 写侧用例都在这份副本上做，读侧用例继续用没动过的 routerIni（两条链互不踩状态）
                var editIni = MainForm.Scratch("selftest-edit.ini");
                File.WriteAllBytes(editIni, iniHead0);

                // —— 原位改值：只动那一行，其余逐字节原样 ——
                bool ed1 = LlamaIni.EditSection(editIni, "model-a",
                    new[] { new LlamaIni.IniEdit("ctx-size", "262144") }, MainForm.LlamaIniKeys, out int ch1, out var ed1err);
                var t1 = File.ReadAllText(editIni);
                Check("就地写：改成功（" + ed1err + "）", ed1 && ch1 == 1, "改动行数=" + ch1);
                Check("就地写：那一行的值换了、键与 `=` 两侧风格照旧", t1.Contains("ctx-size = 262144"));
                Check("就地写：没动的行逐字节原样（行尾空格还在）",
                    t1.Contains("threads = 16      ") && t1.Contains("ngl = 99 ") && t1.Contains("no-mmproj-offload = true "), t1);
                Check("就地写：只有一行不同（别的一个字没动）",
                    DiffLines(iniHead0, File.ReadAllBytes(editIni)) == 1, DiffLines(iniHead0, File.ReadAllBytes(editIni)).ToString());
                Check("就地写：换行风格没被翻成 CRLF、也没冒出 BOM（本机那份是 LF 无 BOM，这条就是 WriteAllLines 的反例）",
                    !File.ReadAllBytes(editIni).Contains((byte)'\r')
                    && !(File.ReadAllBytes(editIni).Length >= 3 && File.ReadAllBytes(editIni)[0] == 0xEF),
                    $"{iniHead0.Length} -> {File.ReadAllBytes(editIni).Length} 字节（差值只该来自值本身）");
                Check("就地写：别的段与 `[*]` 一个字没碰（model-b 的 ctx-size 本来就没有）",
                    t1.Split("[model-b]")[1].Contains("ctx-size") == false && t1.Contains("reasoning-effort = medium"));

                // —— 值本来就一样 = 不动（「继承不摊平」的支点，也不该备份）——
                var beforeSame = File.ReadAllBytes(editIni);
                bool ed2 = LlamaIni.EditSection(editIni, "model-a",
                    new[] { new LlamaIni.IniEdit("ctx-size", "262144") }, MainForm.LlamaIniKeys, out int ch2, out _);
                Check("就地写：新值与文件里一样 → changed=0", ed2 && ch2 == 0, "改动行数=" + ch2);
                Check("就地写：值一样时文件逐字节没变（不写 = 也不必备备份）",
                    BytesEqual(beforeSame, File.ReadAllBytes(editIni)));

                // —— 段里没有的键：插到段尾空行与 `#` 注释之前 ——
                bool ed3 = LlamaIni.EditSection(editIni, "model-b",
                    new[] { new LlamaIni.IniEdit("top-p", "0.9") }, MainForm.LlamaIniKeys, out int ch3, out var ed3err);
                var t3 = File.ReadAllText(editIni);
                var segB = t3.Split("[model-b]")[1];
                Check("就地写：段里没有的键插进本段（" + ed3err + "）", ed3 && ch3 == 1 && segB.Contains("top-p = 0.9"));
                Check("就地写：新键落在段尾注释之前（不顶开用户的注释块）",
                    segB.IndexOf("top-p = 0.9", StringComparison.Ordinal) < segB.IndexOf("# 尾部注释", StringComparison.Ordinal), segB);
                Check("就地写：注释块与 model-a 段照旧（尾部只有新增那一行变了）",
                    t3.Contains("# 尾部注释（插入点必须在它之前）") && t3.Contains("temperature = 0.1"));

                // —— 留空 = 从该段删掉这个键（= 回到继承，用户批复 §10.1 第③条）——
                bool ed4 = LlamaIni.EditSection(editIni, "model-a",
                    new[] { new LlamaIni.IniEdit("cache-type-v", null) }, MainForm.LlamaIniKeys, out int ch4, out var ed4err);
                var t4 = File.ReadAllText(editIni);
                Check("就地写：留空 = 删掉整行（" + ed4err + "）", ed4 && ch4 == 1 &&
                    !t4.Split("[model-a]")[1].Split("[model-b]")[0].Contains("cache-type-v"));
                Check("就地写：删行不误伤别的段（`[*]` 与其它键还在）",
                    t4.Contains("cache-type-k = q8_0") && t4.Contains("flash-attn = on") && t4.Contains("jinja = True"));

                // —— 改动明细（用户 2026-10-04 第 6 条：日志与 toast 要写明改了什麼，如「top-k 20 → 15」）——
                //    四种形状各来一条：段里改值 / 段里没有→新增 / 布尔（界面上是开关，报「开 / 关」）/ 留空→删键。
                var detIni = MainForm.Scratch("selftest-detail.ini");
                File.WriteAllBytes(detIni, iniHead0);
                var det = new List<LlamaIni.IniChange>();
                bool edD = LlamaIni.EditSection(detIni, "model-a", new[]
                {
                    new LlamaIni.IniEdit("ctx-size", "131072"),
                    new LlamaIni.IniEdit("top-k", "15"),
                    new LlamaIni.IniEdit("flash-attn", "false"),
                    new LlamaIni.IniEdit("cache-type-v", null),
                }, MainForm.LlamaIniKeys, out int chD, out var edDerr, null, det);
                var detText = string.Join("、", det.Select(MainForm.LlamaChangeText));
                W("改动明细：" + detText);
                Check("改动明细：写成功、条数与 changed 一致（" + edDerr + "）",
                    edD && chD == 4 && det.Count == 4, $"changed={chD} 明细={det.Count}");
                Check("改动明细：改值=「键 旧 → 新」（旧值取文件里的、含行尾空格的也先 trim）",
                    detText.Contains("ctx-size 4096 → 131072"), detText);
                Check("改动明细：段里没有的键=「键（原来没写）→ 新」，布尔按界面上的开/关报",
                    detText.Contains("top-k（原来没写）→ 15") && detText.Contains("flash-attn（原来没写）→ 关"), detText);
                Check("改动明细：留空=「键 旧 →（删掉，回到继承）」",
                    detText.Contains("cache-type-v q4_0 →（删掉，回到继承）"), detText);
                // 明细只在真写成功时才有：值一样（不写）与被护栏拒绝都必须交回空清单
                var detSame = new List<LlamaIni.IniChange>();
                LlamaIni.EditSection(detIni, "model-a", new[] { new LlamaIni.IniEdit("ctx-size", "131072") },
                    MainForm.LlamaIniKeys, out int chSame, out _, null, detSame);
                Check("改动明细：值本来就一样 → changed=0 且明细为空（不摊平也不报改动）",
                    chSame == 0 && detSame.Count == 0, $"changed={chSame} 明细={detSame.Count}");
                var detNo = new List<LlamaIni.IniChange>();
                bool rDet = LlamaIni.EditSection(detIni, "model-a", new[] { new LlamaIni.IniEdit("ngl", "99") },
                    MainForm.LlamaIniKeys, out _, out var rDetErr, null, detNo);
                Check("改动明细：护栏拒绝时不交明细（没写成功就不许说改了什麼）",
                    !rDet && detNo.Count == 0 && rDetErr.Contains("ngl"), rDetErr);

                // —— 四条拒绝：护栏优先于功能，拒绝之后文件必须一个字节都没变 ——
                var guard = File.ReadAllBytes(editIni);
                bool r1 = LlamaIni.EditSection(editIni, "*", new[] { new LlamaIni.IniEdit("ctx-size", "1") }, MainForm.LlamaIniKeys, out _, out var r1err);
                Check("拒绝写 `[*]`（只写选中模型段）", !r1 && r1err.Contains("[*]"), r1err);
                bool r2 = LlamaIni.EditSection(editIni, "model-a", new[] { new LlamaIni.IniEdit("ngl", "99") }, MainForm.LlamaIniKeys, out _, out var r2err);
                Check("拒绝写白名单外的键（界面没格子的键永不触碰）", !r2 && r2err.Contains("ngl"), r2err);
                Check("DEC-145 白名单含两枚新键（spec-type / mmproj-offload；共 14 键）",
                    MainForm.LlamaIniKeys.Length == 14
                    && MainForm.LlamaIniKeys.Contains("spec-type") && MainForm.LlamaIniKeys.Contains("mmproj-offload"),
                    string.Join(",", MainForm.LlamaIniKeys));
                bool r3 = LlamaIni.EditSection(editIni, "model-nope", new[] { new LlamaIni.IniEdit("ctx-size", "1") }, MainForm.LlamaIniKeys, out _, out var r3err);
                Check("拒绝不存在的段（不猜、不新建段）", !r3 && r3err.Contains("model-nope"), r3err);
                bool r4 = LlamaIni.EditSection(MainForm.Scratch("selftest-missing.ini"), "model-a",
                    new[] { new LlamaIni.IniEdit("ctx-size", "1") }, MainForm.LlamaIniKeys, out _, out var r4err);
                Check("拒绝不存在的文件（不造空家底）", !r4 && r4err.Contains("找不到"), r4err);
                Check("四次拒绝之后文件逐字节没变", BytesEqual(guard, File.ReadAllBytes(editIni)));

                // —— 读侧：全局键、生效值与继承标记 ——
                var glob = LlamaIni.ReadGlobalValues(routerIni);
                Check("全局键 = 文件头 + `[*]`（version / flash-attn / jinja / reasoning-effort 都认得出）",
                    glob.TryGetValue("version", out var vVer) && vVer == "1"
                    && glob.TryGetValue("reasoning-effort", out var vEff) && vEff == "medium", string.Join(",", glob.Keys));
                Check("别的模型段里的键不算全局（model-a 的 ctx-size 不漏进全局）", !glob.ContainsKey("ctx-size"),
                    string.Join(",", glob));
                var secA = LlamaIni.ReadSectionValues(routerIni, "model-a");
                var (evLocal, inhLocal) = LlamaIni.EffectiveValue(secA, glob, "temperature");
                var (evGlob, inhGlob) = LlamaIni.EffectiveValue(secA, glob, "reasoning-effort");
                Check("生效值：段里写了就用段里的（不算继承）", evLocal == "0.1" && !inhLocal, evLocal);
                Check("生效值：段里没写就用全局的（算继承，界面要标出来）", evGlob == "medium" && inhGlob, evGlob + "/" + inhGlob);
                Check("生效值：两处都没有 = 空（= 引擎默认）",
                    LlamaIni.EffectiveValue(secA, glob, "presence-penalty").Value == "" &&
                    !LlamaIni.EffectiveValue(secA, glob, "presence-penalty").Inherited);
                Check("布尔宽松判法按引擎字面表（DEC-145 对齐）：on / enabled / true / 1（大小写不敏感）都算开",
                    LlamaIni.IsTrue("on") && LlamaIni.IsTrue("Enabled") && LlamaIni.IsTrue("true") &&
                    LlamaIni.IsTrue("1") && LlamaIni.IsTrue("TRUE"));
                Check("布尔宽松判法：off / disabled / false / 0 / 空 / 表外字都算关；yes 不再算开（引擎会拒它，不许界面替它圆谎）",
                    !LlamaIni.IsTrue("off") && !LlamaIni.IsTrue("disabled") && !LlamaIni.IsTrue("false") &&
                    !LlamaIni.IsTrue("0") && !LlamaIni.IsTrue("") && !LlamaIni.IsTrue("maybe") && !LlamaIni.IsTrue("yes"));
                Check("IsFalsey（假字面表那一半，DEC-145）：off / disabled / false / 0 认；开字与表外字都不认",
                    LlamaIni.IsFalsey("off") && LlamaIni.IsFalsey("Disabled") && LlamaIni.IsFalsey("false") && LlamaIni.IsFalsey("0")
                    && !LlamaIni.IsFalsey("on") && !LlamaIni.IsFalsey("yes") && !LlamaIni.IsFalsey(""));
                Check("布尔写入固定 true/false（不再往文件里写 on/True）",
                    LlamaIni.BoolText(true) == "true" && LlamaIni.BoolText(false) == "false");

                // —— 界面 → 该写哪些键（「继承不摊平」的判据）——
                var effA = MainForm.LlamaEffectiveWriteForm(routerIni, "model-a");
                // 把"界面一格都没改"这件事表达成一个函数：每个格子都填该段当前的生效值
                Settings UiFromBaseline() => new()
                {
                    LlamaCtx = effA["ctx-size"],
                    LlamaKv = MainForm.KvDisplay(effA["cache-type-k"], effA["cache-type-v"]),
                    LlamaFlashAttn = LlamaIni.IsTrue(effA["flash-attn"]),
                    LlamaTemperature = effA["temperature"], LlamaTopP = effA["top-p"], LlamaTopK = effA["top-k"],
                    LlamaMinP = effA["min-p"], LlamaPresencePenalty = effA["presence-penalty"],
                    LlamaRepeatPenalty = effA["repeat-penalty"], LlamaSpecDraftNMax = effA["spec-draft-n-max"],
                    LlamaSpecType = effA["spec-type"], LlamaMmprojOffload = effA["mmproj-offload"],
                    LlamaReasoningEffort = effA["reasoning-effort"],
                };
                Check("不摊平：界面一格都没改 → 一条都不写（点保存不污染文件）",
                    MainForm.LlamaSectionEdits(UiFromBaseline(), effA).Count == 0,
                    string.Join(",", MainForm.LlamaSectionEdits(UiFromBaseline(), effA).Select(e => e.Key)));
                var uiA = UiFromBaseline();
                uiA.LlamaCtx = "32768";
                var editList = MainForm.LlamaSectionEdits(uiA, effA);
                Check("不摊平：只把真变了的键写进段（这里只有 ctx-size 一条）",
                    editList.Count == 1 && editList[0].Key == "ctx-size" && editList[0].Value == "32768",
                    string.Join(",", editList.Select(e => e.Key + "=" + e.Value)));
                var uiKv = UiFromBaseline();
                uiKv.LlamaKv = "q8_0 / q8_0";
                var kvEdits = MainForm.LlamaSectionEdits(uiKv, effA);
                var kvWantCnt = (effA["cache-type-k"] == "q8_0" ? 0 : 1) + (effA["cache-type-v"] == "q8_0" ? 0 : 1);
                Check($"只把 KV 换成 q8_0 / q8_0：该写的就是那一两键（段里本来就是 {effA["cache-type-k"]} / {effA["cache-type-v"]}）",
                    kvEdits.Count == kvWantCnt && kvEdits.All(e => e.Value == "q8_0")
                    && kvEdits.All(e => e.Key is "cache-type-k" or "cache-type-v"),
                    string.Join(",", kvEdits.Select(e => e.Key + "=" + e.Value)));
                var uiBlank = UiFromBaseline();
                uiBlank.LlamaKv = "";
                var uiBlankEdits = MainForm.LlamaSectionEdits(uiBlank, effA);
                int blankWant = (effA["cache-type-k"].Length > 0 ? 1 : 0) + (effA["cache-type-v"].Length > 0 ? 1 : 0);
                Check("KV 选空白 = 那两键一起删（回到继承）",
                    blankWant > 0 && uiBlankEdits.Count == blankWant && uiBlankEdits.All(e => string.IsNullOrEmpty(e.Value))
                    && uiBlankEdits.All(e => e.Key is "cache-type-k" or "cache-type-v"),
                    string.Join(",", uiBlankEdits.Select(e => e.Key + "=" + (e.Value ?? "<删>"))));
                // DEC-145：两枚新格子的写法（spec-type 换档/清空；mmproj-offload 两拼写折算 + 切 GPU 只写一条）
                Check("DEC-145 视觉折算：段里旧写 no-mmproj-offload = on → 生效 false（CPU）",
                    effA["mmproj-offload"] == "false", effA["mmproj-offload"]);
                Check("DEC-145 视觉折算：正名优先于旧写；哪都没写 = 空（引擎默认 = offload 开）",
                    MainForm.MmprojOffloadWriteForm(
                        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["mmproj-offload"] = "off", ["no-mmproj-offload"] = "on" },
                        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)) == ("false", false)
                    && MainForm.MmprojOffloadWriteForm(
                        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)) == ("", false));
                var uiSpec = UiFromBaseline();
                uiSpec.LlamaSpecType = "draft-dspark";
                var specEdits = MainForm.LlamaSectionEdits(uiSpec, effA);
                Check("DEC-145 投机方式：换档 = 只写 spec-type 一条（draft-mtp → draft-dspark）",
                    specEdits.Count == 1 && specEdits[0].Key == "spec-type" && specEdits[0].Value == "draft-dspark",
                    string.Join(",", specEdits.Select(e => e.Key + "=" + e.Value)));
                var uiSpecB = UiFromBaseline();
                uiSpecB.LlamaSpecType = "";
                var specB = MainForm.LlamaSectionEdits(uiSpecB, effA);
                Check("DEC-145 投机方式：选空白 = 删 spec-type（这一段本来写着 → 恰好 1 条删）",
                    specB.Count == 1 && specB[0].Key == "spec-type" && string.IsNullOrEmpty(specB[0].Value),
                    string.Join(",", specB.Select(e => e.Key + "=" + (e.Value ?? "<删>"))));
                var uiMm = UiFromBaseline();
                uiMm.LlamaMmprojOffload = "true";
                var mmEdits = MainForm.LlamaSectionEdits(uiMm, effA);
                Check("DEC-145 视觉：切 GPU = 写 mmproj-offload = true、并顺带删旧写行 no-mmproj-offload（两段里旧写折算成 false → 两条）",
                    mmEdits.Count == 2 && mmEdits[0].Key == "mmproj-offload" && mmEdits[0].Value == "true"
                    && mmEdits[1].Key == "no-mmproj-offload" && string.IsNullOrEmpty(mmEdits[1].Value),
                    string.Join(",", mmEdits.Select(e => e.Key + "=" + (e.Value ?? "<删>"))));
                // DEC-145 补：段里是旧写、界面选空白 = 回归继承。以前只改正名（段里没有那一行）= 旧写行残留、
                // 生效值回弹清不动 —— 这一条钉的正是那个缺口（清空那两条：正名删 + 旧写删）。
                var uiMmClear = UiFromBaseline();
                uiMmClear.LlamaMmprojOffload = "";
                var mmClear = MainForm.LlamaSectionEdits(uiMmClear, effA);
                Check("DEC-145 视觉：段里是旧写（生效 false）时选空白 = 正名删 + 旧写行删（回继承清得动）",
                    mmClear.Count == 2 && mmClear.All(e => string.IsNullOrEmpty(e.Value))
                    && mmClear.Any(e => e.Key == "mmproj-offload") && mmClear.Any(e => e.Key == "no-mmproj-offload"),
                    string.Join(",", mmClear.Select(e => e.Key + "=" + (e.Value ?? "<删>"))));
                Check("写侧放行表 LlamaWriteKeys = 14 键 + 旧拼写（白名单全含、旧拼写只出现在放行表里）",
                    MainForm.LlamaIniKeys.All(k => MainForm.LlamaWriteKeys.Contains(k))
                    && MainForm.LlamaWriteKeys.Contains("no-mmproj-offload")
                    && !MainForm.LlamaIniKeys.Contains("no-mmproj-offload"),
                    string.Join(",", MainForm.LlamaWriteKeys));
                var specItems = MainForm.LlamaSpecTypeItemsForTest();
                Check("DEC-145 投机方式下拉定稿：空白在首 + 六档实值（none / mtp / dspark / simple / eagle3 / dflash）",
                    specItems.Count == 7 && specItems[0].Text == "" && (specItems[0].Tag as string) == ""
                    && specItems.Skip(1).Select(i => i.Text).SequenceEqual(new[] { "none", "draft-mtp", "draft-dspark", "draft-simple", "draft-eagle3", "draft-dflash" }),
                    string.Join("|", specItems.Select(i => i.Text)));

                var kvItems = MainForm.LlamaKvItemsForTest();
                Check("KV 下拉定稿：空白在首 + 5 档实值 = 6 项（bf16/f32 已按用户意见去掉）",
                    kvItems.Count == 6 && kvItems[0].Text == "" &&
                    kvItems.Any(i => Equals(i.Tag, "q8_0 / q4_0"))
                    && kvItems.All(i => !((string?)i.Tag ?? "").Contains("f32") && !((string?)i.Tag ?? "").Contains("bf16")),
                    string.Join("|", kvItems.Select(i => i.Text)));
                Check("SplitKv / KvDisplay 往返：两键↔「k / v」",
                    MainForm.SplitKv("q8_0 / q4_0") == ("q8_0", "q4_0") && MainForm.KvDisplay("q8_0", "q4_0") == "q8_0 / q4_0"
                    && MainForm.KvDisplay("q8_0", "") == "q8_0" && MainForm.SplitKv("").K == "");
                Check("写段前的数值校验：ctx 必须是正整数或留空",
                    MainForm.LlamaCtxProblem("") == null && MainForm.LlamaCtxProblem("262144") == null &&
                    MainForm.LlamaCtxProblem("0") != null && MainForm.LlamaCtxProblem("abc") != null);

                // 9b2. 选中 llama 模型时把 ini 段载入（DEC-079：读序 = 段 → `[*]`/文件头 → 引擎默认，且带出"这值是继承来的"）
                //      读侧用例一律用**原件 routerIni**（写侧用例在 editIni 上跑），免得两条用例互相踩状态
                var lk = new Settings { LlamaTemperature = "9.9", LlamaTopP = "9.9" };
                var inhA = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                Check("ini 段载入：读得出该段的键值", MainForm.ReadLlamaKnobsFromIni(routerIni, "model-a", lk, inhA));
                Check("ini 段载入：temperature / ctx-size / KV 类型入位",
                    lk.LlamaTemperature == "0.1" && lk.LlamaCtx == "4096" && lk.LlamaKv == "q8_0 / q4_0",
                    $"temp={lk.LlamaTemperature} ctx={lk.LlamaCtx} kv={lk.LlamaKv}");
                Check("ini 段载入：不污染 Strata 侧字段（ApiModel/Context/Gpu 保持原样）",
                    lk.ApiModel == "" && lk.Context == "131072" && lk.Gpu == "");
                // 夹具的 [*] 里有 reasoning-effort = medium、flash-attn = on：段里没写的键现在**回落到全局**（旧口径是清空）
                Check("DEC-079 读序：段里没有的键回落 `[*]`（reasoning-effort 取到 medium）",
                    lk.LlamaReasoningEffort == "medium" && inhA.Contains("reasoning-effort"),
                    $"effort={lk.LlamaReasoningEffort} 继承={string.Join(",", inhA)}");
                Check("DEC-079 读序：flash-attn 宽松判法（段里没有、`[*]` 写 on = 开）",
                    lk.LlamaFlashAttn && inhA.Contains("flash-attn"), $"fa={lk.LlamaFlashAttn}");
                Check("DEC-145 ini 段载入：spec-type / 视觉（旧拼写折算）入位；段里写了的不算继承",
                    lk.LlamaSpecType == "draft-mtp" && lk.LlamaMmprojOffload == "false"
                    && !inhA.Contains("spec-type") && !inhA.Contains("mmproj-offload"),
                    $"spec={lk.LlamaSpecType} mmproj={lk.LlamaMmprojOffload} 继承={string.Join(",", inhA)}");
                Check("DEC-079 读序：两处都没有的键仍归零（top-p 段与全局都没写）", lk.LlamaTopP == "");
                Check("DEC-079 读序：段里写了的键不算继承（ctx-size 不进继承清单）",
                    !inhA.Contains("ctx-size") && !inhA.Contains("temperature"), string.Join(",", inhA));
                var lk2 = new Settings { LlamaTemperature = "9.9", LlamaCtx = "9.9", LlamaKv = "9.9", LlamaReasoningEffort = "9.9" };
                var inhB = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                Check("ini 段载入：换一段时同名键被覆盖 / 继承的跟着换（model-b 段只有 model 一行）",
                    MainForm.ReadLlamaKnobsFromIni(routerIni, "model-b", lk2, inhB)
                    && lk2.LlamaTemperature == "" && lk2.LlamaCtx == "" && lk2.LlamaKv == "" && lk2.LlamaReasoningEffort == "medium");
                Check("DEC-145 读序：spec-type 走 `[*]`（夹具全局 none）= 继承；视觉走 `[*]` 旧写 = false 也算继承",
                    lk2.LlamaSpecType == "none" && inhB.Contains("spec-type")
                    && lk2.LlamaMmprojOffload == "false" && inhB.Contains("mmproj-offload"),
                    $"spec={lk2.LlamaSpecType} mmproj={lk2.LlamaMmprojOffload} 继承={string.Join(",", inhB)}");
                Check("ini 段载入：段不存在时返回 false（不猜、不动界面）",
                    !MainForm.ReadLlamaKnobsFromIni(routerIni, "model-nope", new Settings()));

                // 9c. 命令行组装
                var lset = new Settings { LlamaModelsMax = "1", LlamaSleepIdleMinutes = "5" };
                var largs = MainForm.BuildLlamaArgs(lset, "127.0.0.1", 8082, @"X:\merged.ini");
                W("llama-server args: " + string.Join(" ", largs));
                Check("命令行带 --host / --port / --models-preset",
                    Value(largs, "--host") == "127.0.0.1" && Value(largs, "--port") == "8082"
                    && Value(largs, "--models-preset") == @"X:\merged.ini");
                Check("--models-max / --sleep-idle-seconds 取自设置（DEC-151：分钟 ×60 成秒，5 分钟 → 300）",
                    Value(largs, "--models-max") == "1" && Value(largs, "--sleep-idle-seconds") == "300");
                // DEC-151：换算不是恒等 —— 上面那条 5→300 恰好撞上默认值，恒真型错误也过不了这一条（10 → 600）
                var largsIdle = MainForm.BuildLlamaArgs(new Settings { LlamaSleepIdleMinutes = "10" }, "127.0.0.1", 1, "p");
                Check("DEC-151 换算：10 分钟 → --sleep-idle-seconds 600",
                    Value(largsIdle, "--sleep-idle-seconds") == "600", Value(largsIdle, "--sleep-idle-seconds") ?? "(无)");
                // DEC-151：0 = 不自动卸（与另两模块"留空/0 = 不停"同语义 = 不传旗标 = 引擎默认 -1 禁用）
                var largsIdle0 = MainForm.BuildLlamaArgs(new Settings { LlamaSleepIdleMinutes = "0" }, "127.0.0.1", 1, "p");
                Check("DEC-151：0 分钟 = 不自动卸 → 不传 --sleep-idle-seconds",
                    !largsIdle0.Contains("--sleep-idle-seconds"));
                var largsBad = MainForm.BuildLlamaArgs(new Settings { LlamaModelsMax = "abc", LlamaSleepIdleMinutes = "" }, "127.0.0.1", 1, "p");
                Check("上限/空闲非法或留空 -> 不传（用 server 默认）",
                    !largsBad.Contains("--models-max") && !largsBad.Contains("--sleep-idle-seconds"));
                Check("监听地址留空 -> --host 127.0.0.1",
                    Value(MainForm.BuildLlamaArgs(new Settings(), "", 1, "p"), "--host") == "127.0.0.1");
                // 附加参数（DEC-075）：原样追加到命令行末尾，按空白切分——与 Strata 侧的追加口径一致
                // （命令行是「旗标 + 值」两枚一组：基础三对 + --metrics = 7 枚）
                var lax = MainForm.BuildLlamaArgs(
                    new Settings { LlamaModelsMax = "", LlamaSleepIdleMinutes = "", LlamaExtraArgs = "--flash-attn on --threads 16" },
                    "127.0.0.1", 1, "p");
                Check("附加参数：原样追加在命令行末尾（按空白切分）",
                    lax.Count == 11 && lax[^4] == "--flash-attn" && lax[^3] == "on" && lax[^2] == "--threads" && lax[^1] == "16",
                    string.Join(" ", lax));
                Check("附加参数留空：命令行一个都不加（基础三对 + --metrics = 7 枚）",
                    MainForm.BuildLlamaArgs(new Settings { LlamaModelsMax = "", LlamaSleepIdleMinutes = "" }, "127.0.0.1", 1, "p").Count == 7);
                // DEC-149：状态栏那发 /metrics 由 router 转给子实例，而子实例的 /metrics 有闸（要 --metrics 启动），
                // 掉了这一枚 = 子实例回 501、状态栏跑动时永远短横（他实跑抓出的形状）。位置无所谓、在场是硬判据。
                Check("DEC-149：命令行必带 --metrics（状态栏那一发要 router 转发给子实例）",
                    lax.Contains("--metrics"));

                LlamaIniFidelityChecks();
    }
}
