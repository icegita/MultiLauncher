using System.Text;
namespace MultiLauncher;

// 用例 9 尾段（9d–9e）：在用户真实家底的副本上就地写（保真只有拿真件验才算数）、写前备份与轮转、
// [Current] 退役、按钮文案穷举。2026-10-15 从 SelfTest.LlamaBackend.cs 按子域切出，块内一行未改。
internal static partial class SelfTest
{
    internal static void LlamaIniFidelityChecks()
    {
                // 9d. 在**用户真实家底的副本**上就地写（保真只有拿真件验才算数；原件只读，一个字都不写）
                if (haveLive)
                {
                    var realCopy = MainForm.Scratch("selftest-real-copy.ini");
                    File.Copy(routerLive, realCopy, overwrite: true);
                    var real0 = File.ReadAllBytes(realCopy);
                    var firstSec = LlamaIni.ReadModels(realCopy).FirstOrDefault();
                    // DEC-138（E-9）：目标值不能写死 131072 —— 用户段里本来就是这个数时"什么都没变"（changed=0），
                    // 下面那条"恰好 1"就成了一条看运气的判据。照生效值现算一个**一定不一样**的。
                    var curCtx = firstSec is not null
                        ? LlamaIni.EffectiveValue(LlamaIni.ReadSectionValues(realCopy, firstSec.Name),
                                                  LlamaIni.ReadGlobalValues(realCopy), "ctx-size").Value
                        : "";
                    var wantCtx = curCtx == "262144" ? "131072" : "262144";
                    int wCh = 0; string wErr = "";
                    bool wOk = firstSec is not null && LlamaIni.EditSection(realCopy, firstSec.Name,
                        new[] { new LlamaIni.IniEdit("ctx-size", wantCtx) }, MainForm.LlamaIniKeys, out wCh, out wErr);
                    var real1 = File.ReadAllBytes(realCopy);
                    Check("真件副本上就地写成功（段 " + (firstSec?.Name ?? "?") + "）", wOk, wErr);
                    // DEC-138（E-9）：`<= 1` 是半条空判据 —— 一个字节没动也算"通过"。改成**恰好 1**（写侧那条用例本来就要它改一行）。
                    Check("真件副本：改动只落在目标键那一行（逐行 diff **恰好 1**，不是 <=1）",
                        wCh == 1 && DiffLines(real0, real1) == 1, $"changed={wCh} diff={DiffLines(real0, real1)} 目标 {curCtx} → {wantCtx}");
                    Check("真件副本：换行风格与 BOM 一件没变（本机那份是 LF 无 BOM —— File.WriteAllLines 会翻成 CRLF，这条就是它的反例）",
                        real0.Contains((byte)'\r') == real1.Contains((byte)'\r')
                        && (real0.Length >= 3 && real0[0] == 0xEF) == (real1.Length >= 3 && real1[0] == 0xEF),
                        $"CR={real0.Contains((byte)'\r')}/{real1.Contains((byte)'\r')} 字节 {real0.Length}->{real1.Length}");
                    // DEC-138（E-9）：这一条以前钉的是用户文件里的一句**字面注释**（"# 添加更多模型配置"）——
                    // 他删过注释是登记在案的事（DEC-119 那一批），钉字面 = 一改就红、而且与夹具那条冗余。
                    // 判据换成结构不变量：注释行的**行号集合**与段名集合，写前写后一字不差（"在原位"说的本来就是这件事）。
                    static List<int> CommentLinesOf(byte[] b)
                    {
                        var list = new List<int>();
                        var ls = SplitLf(Encoding.UTF8.GetString(b));
                        for (int i = 0; i < ls.Length; i++) if (ls[i].TrimStart().StartsWith("#")) list.Add(i);
                        return list;
                    }
                    static List<string> SectionNamesOf(byte[] b)
                    {
                        var list = new List<string>();
                        foreach (var l in SplitLf(Encoding.UTF8.GetString(b)))
                        {
                            var t = l.Trim();
                            if (t.Length > 1 && t[0] == '[' && t[^1] == ']') list.Add(t);
                        }
                        return list;
                    }
                    var cm0 = CommentLinesOf(real0); var cm1 = CommentLinesOf(real1);
                    var sc0 = SectionNamesOf(real0); var sc1 = SectionNamesOf(real1);
                    Check("真件副本：注释行的行号一个都没动、段名集合一个都没少（结构不变量，不钉用户那句字面注释）",
                        cm0.SequenceEqual(cm1) && sc0.SequenceEqual(sc1) && sc0.Count > 0,
                        $"注释行 {cm0.Count}->{cm1.Count} 段 {sc0.Count}->{sc1.Count}");
                    Check("原件仍逐字节没动（**自检绝不写用户家底**这条护栏）",
                        BytesEqual(real0, File.ReadAllBytes(routerLive)));
                }
                else Check("DEC-138（E-7）用例前提：真件在场（不在 = 那 5 条『就地写 / 只动一行 / 换行风格 / 注释原位 / 原件不动』整块没跑）",
                           false, "真件不在：" + routerLive);

                // 9f. 写前备份与轮转（用户批复 §10.1 第④条：各留最近 5 份；入口在设置页 llama 节「恢复…」）
                var bkDir = MainForm.Scratch("selftest-bk-" + Guid.NewGuid().ToString("N")[..8]);
                try
                {
                    Directory.CreateDirectory(bkDir);
                    var bkSrc = Path.Combine(bkDir, "llama_models.ini");
                    File.WriteAllText(bkSrc, "version = 1\n", new UTF8Encoding(false));
                    // 这目录里还可能留着退役前的 merged.ini —— 它不是备份，绝不该被列出来、也不该被轮转删掉
                    File.WriteAllText(Path.Combine(bkDir, "merged.ini"), "合并产物（已退役）\n", new UTF8Encoding(false));
                    int made = 0;
                    string firstName = "", lastName = "";
                    for (int i = 0; i < 7; i++)
                    {
                        File.AppendAllText(bkSrc, "# 第 " + i + " 次改动前\n", new UTF8Encoding(false));
                        if (MainForm.BackupFileTo(bkSrc, bkDir, MainForm.RouterIniBackupPrefix, out var madeName, out var berr))
                        {
                            made++;
                            if (i == 0) firstName = madeName!;
                            lastName = madeName!;
                        }
                        else { Check("备份 7 次都做成", false, berr); break; }
                    }
                    Check("备份 7 次都做成（文件名带 llama_models- 前缀，DEC-084 改名之后）", made == 7, "做成 " + made);
                    var list7 = MainForm.ListBackupsIn(bkDir, MainForm.RouterIniBackupPrefix);
                    Check("备份清单只认 llama_models- / router_models- 这两种前缀（残留的 merged.ini 不会被当成备份）",
                        list7.Count == 7 && list7.All(n => n.StartsWith(MainForm.RouterIniBackupPrefix)),
                        string.Join("|", list7));
                    // 同一秒里连着存几份会带 -2 / -3 后缀：原来按整个文件名字典序排，"没后缀那份"（= 最早）会跑到最前，
                    // 轮转就会删错。DEC-084 改成按名字里那段时间戳比，顺手把这条修掉（跨前缀排序也必须要它）
                    // DEC-097 改判据：原来断"第一份的名字以 -7 结尾"，**循环跨过一秒就不成立**（第 7 份会落进新的一秒、
                    // 没有后缀）—— 那是测试自己看时钟脸色。改成拿"实际存出来的第一份 / 最后一份"比，与秒边界无关。
                    Check("备份清单最新在前（最后存的那份排第一、最早存的那份排最后）",
                        list7.Count == 7 && list7[0] == lastName && list7[^1] == firstName,
                        string.Join("|", list7) + "｜首=" + firstName + " 尾=" + lastName);
                    // 同秒内的次序要**确定**地验一次（不靠时钟）：没后缀 = 那一秒里最早的一份
                    var sameSec = new[]
                    {
                        "llama_models-20260101-000001.ini", "llama_models-20260101-000002.ini",
                        "llama_models-20260101-000002-2.ini", "llama_models-20260101-000002-3.ini",
                    };
                    var sameSecDir = Path.Combine(bkDir, "same-sec");
                    Directory.CreateDirectory(sameSecDir);
                    foreach (var n in sameSec) File.WriteAllText(Path.Combine(sameSecDir, n), "x\n", new UTF8Encoding(false));
                    var ordered = MainForm.ListBackupsIn(sameSecDir, MainForm.RouterIniBackupPrefix);
                    Check("同一秒里连着存三份：次序按后缀数排，且「没后缀那份」= 最早（DEC-084 修的就是这个方向）",
                        ordered.SequenceEqual(new[] { "llama_models-20260101-000002-3.ini",
                                                      "llama_models-20260101-000002-2.ini",
                                                      "llama_models-20260101-000002.ini",
                                                      "llama_models-20260101-000001.ini" }),
                        string.Join("|", ordered));
                    var bare = list7.FirstOrDefault(n =>
                        n.Length == MainForm.RouterIniBackupPrefix.Length + "20261006-042236".Length + ".ini".Length);
                    Check("清单里认得出「没后缀那份」的形状（前缀 + 20261006-042236 + .ini）", bare is not null, bare ?? "无");
                    // DEC-084：改名之前的历史备份叫 router_models-*.ini，「恢复…」的清单必须还认它们（不许悄悄消失），
                    // 而且两种前缀混在一起时次序仍按名字里那段时间戳排（整体字典序会把老前缀整体排到新前缀后面）
                    File.WriteAllText(Path.Combine(bkDir, "router_models-20990101-000000.ini"), "旧名时代的一份\n", new UTF8Encoding(false));
                    var both = MainForm.ListBackupsIn(bkDir, MainForm.RouterIniBackupPrefixes);
                    Check("DEC-084 清单两种前缀都认，且按名字里那段时间戳排（旧名那份 2099 年的排最前，不是按前缀字典序）",
                        both.Count == list7.Count + 1 && both[0].StartsWith(MainForm.LegacyRouterIniBackupPrefix),
                        string.Join("|", both.Take(3)));
                    int beforeTrim = both.Count;
                    int dropped = MainForm.TrimBackupsIn(bkDir, MainForm.RouterIniBackupKeep, MainForm.RouterIniBackupPrefixes);
                    var keepAll = MainForm.ListBackupsIn(bkDir, MainForm.RouterIniBackupPrefixes);
                    Check("轮转只留最近 3 份（DEC-110⑤ 由 5 份改下来；两种前缀合起来数，不是各数 3 份）",
                        MainForm.RouterIniBackupKeep == 3
                        && dropped == beforeTrim - MainForm.RouterIniBackupKeep
                        && keepAll.Count == MainForm.RouterIniBackupKeep,
                        $"留 {MainForm.RouterIniBackupKeep}：删 {dropped} 剩 {keepAll.Count}（原有 {beforeTrim}）");
                    Check("轮转不误删别的文件（merged.ini 还在）",
                        File.Exists(Path.Combine(bkDir, "merged.ini")));
                    // DEC-110⑤：**备份的后缀跟着源文件走**。用户实跑抓到的 `ninfer-serve-20261008-203246.ini`
                    // 就是这条的字面成因 —— BackupFileTo 以前把后缀写死成 .ini，于是引擎 exe 的副本顶着一个
                    // 配置文件的名字躺在同一个目录里（"把配置和 exe 混为一起了"）。
                    var engDir = Path.Combine(bkDir, "engine-src");
                    Directory.CreateDirectory(engDir);
                    var exeSrc = Path.Combine(engDir, "ninfer-serve-89.exe");
                    File.WriteAllText(exeSrc, "MZ 假 exe", new UTF8Encoding(false));
                    bool bakOk = MainForm.BackupFileTo(exeSrc, bkDir, MainForm.NinferExeBackupPrefix, out var exeBak, out _);
                    Check("DEC-110⑤ 备份一支 exe 出来的名字带 .exe（不再顶着 .ini）",
                        bakOk && exeBak!.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                        && !exeBak.EndsWith(".ini", StringComparison.OrdinalIgnoreCase), exeBak ?? "null");
                    Check("DEC-110⑤ 清单认得出非 .ini 的备份，且两种前缀互不相认（配置那份不会被当成 exe、反之亦然）",
                        MainForm.ListBackupsIn(bkDir, MainForm.NinferExeBackupPrefix).SequenceEqual(new[] { exeBak! })
                        && !MainForm.ListBackupsIn(bkDir, MainForm.NinferIniBackupPrefix).Any(n => n.EndsWith(".exe")),
                        string.Join("|", MainForm.ListBackupsIn(bkDir, MainForm.NinferExeBackupPrefix)));
                    Check("DEC-110⑤ exe 备份也走同一道轮转（留 3 份），且不会顺手删掉配置那一种前缀",
                        MainForm.TrimBackupsIn(bkDir, MainForm.NinferBackupKeep, MainForm.NinferExeBackupPrefix) == 0
                        && MainForm.ListBackupsIn(bkDir, MainForm.RouterIniBackupPrefixes).Count == MainForm.RouterIniBackupKeep,
                        "配置那几种前缀合起来仍是 3 份，没被 exe 的轮转带走");
                    Check("备份份数常量就是 3（界面上的说法与代码一致；DEC-110⑤ 用户口径：所有模块的备份都留 3 份）",
                        MainForm.RouterIniBackupKeep == 3 && MainForm.NinferBackupKeep == 3
                        && MainForm.StrataBackupKeep == 3,
                        $"llama {MainForm.RouterIniBackupKeep} / ninfer {MainForm.NinferBackupKeep} / strata {MainForm.StrataBackupKeep}");

                    // ============ DEC-200（2026-10-20 他理清后拍板）：备份名里的时间戳 = 原件时间 ============
                    // 他的原话：「原来是这样,按这个逻辑改」—— 名字要当版本身份用，不能是「哪天备份的」。
                    {
                        var nd = Path.Combine(bkDir, "dec200-stamp");
                        Directory.CreateDirectory(nd);
                        var srcF = Path.Combine(nd, "llama_models.ini");
                        File.WriteAllText(srcF, "版本X\n", new UTF8Encoding(false));
                        var fixedTime = new DateTime(2026, 10, 8, 20, 32, 45);      // 一个与"现在"明确不同的时刻
                        File.SetLastWriteTime(srcF, fixedTime);
                        Check("DEC-200 StampForBackup 取的是原件最后写入时间，不是 DateTime.Now（用例前提：这个时刻不在今天）",
                            MainForm.StampForBackup(srcF) == "20261008-203245", MainForm.StampForBackup(srcF));
                        bool okProp = MainForm.BackupFileTo(srcF, nd, MainForm.RouterIniBackupPrefix, out var propName, out var propErr);
                        Check("DEC-200 备份文件名里那段时间戳 = 原件的 LastWriteTime（名字即版本身份，不再随备份时刻漂）",
                            okProp && propName == MainForm.RouterIniBackupPrefix + "20261008-203245" + ".ini",
                            propErr + " / " + propName);
                        Check("DEC-200 备份出来的那份 mtime 也还是原件那个时刻（把事实钉住，别以后被人改档）",
                            propName is { Length: > 0 } && File.GetLastWriteTime(Path.Combine(nd, propName)) == fixedTime,
                            propName is { Length: > 0 } ? File.GetLastWriteTime(Path.Combine(nd, propName)).ToString("yyyyMMdd-HHmmss") : "");
                        var missing = Path.Combine(nd, "根本不存在的文件.ini");
                        var fallback = MainForm.StampForBackup(missing);
                        Check("DEC-200 原件读不出来 → 退回当前时刻（名字总得有一个，但不许抛异常也不许是空串）",
                            fallback.Length == 15 && fallback[8] == '-', fallback);
                    }
                    // ============ DEC-199（2026-10-20 他实跑点名）：恢复前的三件事 ============
                    // 「如 20261001, 20261010, 20261020, 恢复到 1001 后, 再恢复到 1020, 不应该覆盖原 1001,
                    //   备份前应检查 hash, 如果有相同的版本, 不要碰原文件」。
                    // 三面：① 同内容已有备份 → 复用、不新建；② 按内容去重只留最早那个名字；③ 内容不同时照旧新建。
                    {
                        var hd = Path.Combine(bkDir, "dec199-hash");
                        Directory.CreateDirectory(hd);
                        var live = Path.Combine(hd, "llama_models.ini");
                        File.WriteAllText(live, "版本A\n", new UTF8Encoding(false));
                        File.WriteAllText(Path.Combine(hd, "llama_models-20261001-000000.ini"), "版本B\n", new UTF8Encoding(false));
                        Check("DEC-199 用例前提：这一格只有一份、且与工作文件内容不同（否则复用那条判不出东西）",
                            MainForm.ListBackupsIn(hd, MainForm.RouterIniBackupPrefix).Count == 1
                            && MainForm.Sha256FileOrEmpty(live) != MainForm.Sha256FileOrEmpty(Path.Combine(hd, "llama_models-20261001-000000.ini")), "");
                        // ① 内容不同的那一次：照旧新建一份
                        bool hOk = MainForm.BackupBeforeRestore(live, hd, MainForm.RouterIniBackupPrefix,
                                                               out var hName, out var hErr, out var hReused);
                        Check("DEC-199 目录里没有同内容备份 → 照旧新建一份（reused=false，这份文件真写出来了）",
                            hOk && !hReused && hName is { Length: > 0 } && File.Exists(Path.Combine(hd, hName)),
                            hErr + " / " + hName + " / reused=" + hReused);
                        // ② 同一份内容再来一次：复用，一个字节都不多写
                        int before2 = MainForm.ListBackupsIn(hd, MainForm.RouterIniBackupPrefix).Count;
                        bool hOk2 = MainForm.BackupBeforeRestore(live, hd, MainForm.RouterIniBackupPrefix,
                                                                out var hName2, out var hErr2, out var hReused2);
                        int after2 = MainForm.ListBackupsIn(hd, MainForm.RouterIniBackupPrefix).Count;
                        Check("DEC-199 同内容再来一次 → 复用已有那份、不新建（份数不变、名字与上次那份是同一份）",
                            hOk2 && hReused2 && hName2 == hName && after2 == before2,
                            hErr2 + " / " + hName2 + " / reused=" + hReused2 + " / 份数 " + before2 + "→" + after2);
                        // ③ 按内容去重：手造重复（同内容不同名），只留最早那个名字
                        var first = Path.Combine(hd, "llama_models-20261002-000000.ini");
                        File.WriteAllText(first, "版本A\n", new UTF8Encoding(false));     // 与 live 内容相同 = 与 hName 那份也相同
                        File.WriteAllText(Path.Combine(hd, "llama_models-20261003-000000.ini"), "版本A\n", new UTF8Encoding(false));
                        int nBefore = MainForm.ListBackupsIn(hd, MainForm.RouterIniBackupPrefix).Count;
                        int removed = MainForm.DedupeBackupsByContent(hd, MainForm.RouterIniBackupPrefix);
                        var left = MainForm.ListBackupsIn(hd, MainForm.RouterIniBackupPrefix);
                        var leftSha = left.Select(n => MainForm.Sha256FileOrEmpty(Path.Combine(hd, n))).ToList();
                        Check("DEC-199 按内容去重：同一份内容只留一份，且留下的名字是清单里最早那个（时间戳最小的那个）",
                            removed == 2 && left.Count == nBefore - 2 && leftSha.Distinct().Count() == leftSha.Count
                            && left.Contains("llama_models-20261001-000000.ini"),
                            "删 " + removed + " 份，剩 " + string.Join("|", left));
                        Check("DEC-199 去重后每份内容都不一样（判据的反面：不许把不同内容的也删掉）",
                            leftSha.Distinct().Count() == leftSha.Count, string.Join("|", left));
                    }
                    // ============ DEC-134（2026-10-10 只读审查那一族的整改） ============
                    // ①三处「从备份恢复」原先都是「备份+轮转 → 复制」= 那一格满 3 份时，被删掉的最旧那一份
                    //   正是用户点名要退回的那一份（BUG-051 的第二个入口）；②整份快照的轮转不认形状，
                    //   对 D:\Strata-data\backup 下任意子目录 recursive delete；③改名那一步的轮转只数新前缀。
                    {
                        var rd = Path.Combine(bkDir, "restore-order");
                        Directory.CreateDirectory(rd);
                        var live = Path.Combine(rd, "llama_models.ini");
                        File.WriteAllText(live, "当前那一版\n", new UTF8Encoding(false));
                        foreach (var n in new[] { "llama_models-20260101-000001.ini", "llama_models-20260101-000002.ini",
                                                  "llama_models-20260101-000003.ini" })
                            File.WriteAllText(Path.Combine(rd, n), "内容 " + n + "\n", new UTF8Encoding(false));
                        Check("DEC-134① 用例前提：那一格正好满 3 份（不满 3 就撞不出原先那个次序的坑）",
                            MainForm.ListBackupsIn(rd, MainForm.RouterIniBackupPrefix).Count == 3, "");
                        bool rb = MainForm.BackupBeforeRestore(live, rd, MainForm.RouterIniBackupPrefix, out var rbName, out var rbErr);
                        var afterBak = MainForm.ListBackupsIn(rd, MainForm.RouterIniBackupPrefix);
                        var oldest = afterBak.Count > 0 ? afterBak[^1] : "";
                        Check("DEC-134① 恢复前的那一次备份「只 copy 不轮转」：4 份都在、最旧那一份还活着（按回成备份+轮转这条就红）",
                            rb && afterBak.Count == 4 && oldest.Length > 0 && File.Exists(Path.Combine(rd, oldest)),
                            rbErr + " 清单 " + afterBak.Count + " 份，最旧 = " + oldest);
                        File.Copy(Path.Combine(rd, oldest), live, overwrite: true);
                        int rDrop = MainForm.TrimBackupsIn(rd, MainForm.RouterIniBackupKeep, MainForm.RouterIniBackupPrefix);
                        Check("DEC-134① 复制成了再收口：工作文件的内容 = 他选中的那一份，那一格回到恰好 3 份（此时删它不丢东西）",
                            rDrop == 1 && MainForm.ListBackupsIn(rd, MainForm.RouterIniBackupPrefix).Count == 3
                            && File.ReadAllText(live) == "内容 " + oldest + "\n"
                            && rbName is { Length: > 0 } && File.Exists(Path.Combine(rd, rbName)),
                            $"删 {rDrop} 份，选中的是 {oldest}");
                    }
                    {
                        var nd = Path.Combine(bkDir, "rename-family");
                        var nbd = Path.Combine(nd, "bk");
                        Directory.CreateDirectory(nd); Directory.CreateDirectory(nbd);
                        File.WriteAllText(Path.Combine(nd, MainForm.LegacyLlamaIniFileName), "; 旧名家底\n", new UTF8Encoding(false));
                        File.WriteAllText(Path.Combine(nbd, "llama_models-20260101-000001.ini"), "a\n", new UTF8Encoding(false));
                        File.WriteAllText(Path.Combine(nbd, "router_models-20260101-000002.ini"), "b\n", new UTF8Encoding(false));
                        File.WriteAllText(Path.Combine(nbd, "router_models-20260101-000003.ini"), "c\n", new UTF8Encoding(false));
                        bool nOk = MainForm.MigrateLegacyIniName(nd, MainForm.LegacyLlamaIniFileName, MainForm.LlamaIniFileName,
                            nbd, MainForm.RouterIniBackupPrefix, out _, out _, out _, out var nErr,
                            MainForm.RouterIniBackupPrefixes);
                        var nLeft = MainForm.ListBackupsIn(nbd, MainForm.RouterIniBackupPrefixes);
                        Check("DEC-134③ 改名那一步的轮转把两种前缀合起来数 3 份（只数新前缀时这里会剩 4 份 = 份数判据漂）",
                            nOk && nLeft.Count == 3, nErr + " 剩 " + nLeft.Count + "：" + string.Join("|", nLeft));
                    }
                    {
                        var fakeRoot = Path.Combine(bkDir, "StrataFake");
                        Directory.CreateDirectory(fakeRoot);
                        var snapRoot = MainForm.StrataBackupRoot(fakeRoot);
                        Directory.CreateDirectory(snapRoot);
                        void Snap(string name, DateTime t, bool payload)
                        {
                            var d = Path.Combine(snapRoot, name);
                            Directory.CreateDirectory(Path.Combine(d, "source"));
                            if (payload) File.WriteAllText(Path.Combine(d, "source", "about.txt"), "x\n", new UTF8Encoding(false));
                            Directory.SetLastWriteTime(d, t);      // 轮转按 LastWriteTime 比新旧 = 测试里也得按这个摆
                        }
                        Snap("0.1.38-20261001-000000", new DateTime(2026, 10, 1), true);
                        Snap("0.1.39-20261002-000000", new DateTime(2026, 10, 2), true);
                        Snap("0.1.40-20261003-000000", new DateTime(2026, 10, 3), true);
                        Snap("0.1.41-20261004-000000", new DateTime(2026, 10, 4), false);      // 形状像、里面没货（半截格）
                        Snap("我的手工存档", new DateTime(2026, 1, 1), true);                   // 名字不像快照
                        int del = MainForm.TrimStrataBackups(fakeRoot);
                        Check("DEC-134② 快照轮转只动「壳造的、里面有货」那几格：3 份有效之外一条都不删（手工那格与半截格都留着）",
                            del == 0 && Directory.Exists(Path.Combine(snapRoot, "我的手工存档"))
                            && Directory.Exists(Path.Combine(snapRoot, "0.1.41-20261004-000000")), "删了 " + del + " 份");
                        Snap("0.1.42-20261005-000000", new DateTime(2026, 10, 5), true);
                        del = MainForm.TrimStrataBackups(fakeRoot);
                        Check("DEC-134② 第 4 份有效快照进来时，删的是最旧那一格且「恰好 1 份」（>=1 是半条空判据），外来那两格照旧不动",
                            del == 1 && !Directory.Exists(Path.Combine(snapRoot, "0.1.38-20261001-000000"))
                            && Directory.Exists(Path.Combine(snapRoot, "0.1.42-20261005-000000"))
                            && Directory.Exists(Path.Combine(snapRoot, "我的手工存档")), "删了 " + del + " 份");
                    }
                }
                finally { try { Directory.Delete(bkDir, true); } catch { /* 临时目录删不掉不影响结论 */ } }

                // 9f2. DEC-084：llama 家底改名 router_models.ini → llama_models.ini（首启一次性搬家，全在临时目录上跑）
                {
                    var md = MainForm.Scratch("selftest-rename-" + Guid.NewGuid().ToString("N")[..8]);
                    var bd = Path.Combine(md, "bk");
                    const string body = "; 用户的家底\n[模型]\nctx-size = 131072\n";
                    try
                    {
                        Directory.CreateDirectory(md); Directory.CreateDirectory(bd);
                        File.WriteAllText(Path.Combine(md, MainForm.LegacyLlamaIniFileName), body, new UTF8Encoding(false));
                        if (!MainForm.MigrateLegacyIniName(md, MainForm.LegacyLlamaIniFileName, MainForm.LlamaIniFileName,
                                bd, MainForm.RouterIniBackupPrefix, out bool renamed1, out bool archived1, out string? bk1, out string migErr))
                            Check("DEC-084 搬家跑通", false, migErr);
                        else
                        {
                            Check("DEC-084 首启：旧名改成新名（Move 而不是复制，原地不留第二份）",
                                renamed1 && !archived1 && File.Exists(Path.Combine(md, MainForm.LlamaIniFileName))
                                && !File.Exists(Path.Combine(md, MainForm.LegacyLlamaIniFileName)), bk1 ?? "");
                            Check("DEC-084 改名前先把那一份存进备份目录（前缀用新名，「恢复…」列得出来，内容逐字节一样）",
                                bk1 is { Length: > 0 } && File.Exists(Path.Combine(bd, bk1))
                                && bk1.StartsWith(MainForm.RouterIniBackupPrefix)
                                && File.ReadAllText(Path.Combine(bd, bk1)) == body, bk1 ?? "");
                            Check("DEC-084 新名那份内容逐字节就是原来那份（搬家不改一个字节）",
                                File.ReadAllText(Path.Combine(md, MainForm.LlamaIniFileName)) == body, "");
                            MainForm.MigrateLegacyIniName(md, MainForm.LegacyLlamaIniFileName, MainForm.LlamaIniFileName,
                                bd, MainForm.RouterIniBackupPrefix, out bool renamed2, out bool archived2, out _, out _);
                            Check("DEC-084 第二次启动什么都不做（幂等；新名那份一个字不动）",
                                !renamed2 && !archived2 && File.ReadAllText(Path.Combine(md, MainForm.LlamaIniFileName)) == body, "");
                            // 两份都在（用户自己拷回来的 / 种子先落了新名）：新名说了算，旧名那份归档后删掉
                            File.WriteAllText(Path.Combine(md, MainForm.LegacyLlamaIniFileName), "; 旧的那份\n", new UTF8Encoding(false));
                            MainForm.MigrateLegacyIniName(md, MainForm.LegacyLlamaIniFileName, MainForm.LlamaIniFileName,
                                bd, MainForm.RouterIniBackupPrefix, out bool renamed3, out bool archived3, out _, out _);
                            Check("DEC-084 两份都在时：新名那份原样保留、旧名那份存进备份再删（绝不拿旧名当权威）",
                                !renamed3 && archived3 && !File.Exists(Path.Combine(md, MainForm.LegacyLlamaIniFileName))
                                && File.ReadAllText(Path.Combine(md, MainForm.LlamaIniFileName)) == body, "");
                        }
                        var md2 = Path.Combine(md, "clean");
                        Directory.CreateDirectory(md2);
                        File.WriteAllText(Path.Combine(md2, MainForm.LlamaIniFileName), "x\n", new UTF8Encoding(false));
                        MainForm.MigrateLegacyIniName(md2, MainForm.LegacyLlamaIniFileName, MainForm.LlamaIniFileName,
                            bd, MainForm.RouterIniBackupPrefix, out bool renamed4, out bool archived4, out _, out _);
                        Check("DEC-084 只有新名时不插手（新装机器上不会凭空多出备份）", !renamed4 && !archived4, "");
                    }
                    catch (Exception ex) { Check("DEC-084 搬家用例", false, ex.Message); }
                    finally { try { Directory.Delete(md, true); } catch { /* 临时目录删不掉不影响结论 */ } }
                }

                // 9g. [Current] 退役（DEC-079）：llama 的每模型参数不再落 MultiLauncher.ini ——
                //     旧键**读得进**（一次性搬家要从旧值取），但**写不出去**，于是随下一次保存自然消失。
                {
                    var retired = MainForm.BuildConfigLines(
                        new Settings
                        {
                            LlamaTemperature = "0.7", LlamaTopP = "0.9", LlamaReasoningEffort = "high",
                            LlamaCtx = "131072", LlamaKv = "q8_0 / q8_0", LlamaFlashAttn = true,
                            RouterIni = @"X:\llama\llama_models.ini", Port = "8081",
                        },
                        new MainForm.WindowRect(10, 20, 300, 400, false));
                    Check("退役键不再写出：[Current] 里没有 LlamaTemperature / LlamaCtx / LlamaKv / LlamaFlashAttn / RouterIni",
                        !retired.Any(l => l.StartsWith("LlamaTemperature=")) && !retired.Any(l => l.StartsWith("LlamaCtx="))
                        && !retired.Any(l => l.StartsWith("LlamaKv=")) && !retired.Any(l => l.StartsWith("LlamaFlashAttn="))
                        && !retired.Any(l => l.StartsWith("RouterIni=")),
                        string.Join(" ⏎ ", retired.Where(l => l.StartsWith("Llama"))));
                    Check("软件自己的键照旧落盘（端口这类不受影响）",
                        retired.Any(l => l == "Port=8081"), "");
                    var back = new Settings();
                    MainForm.ParseConfigLines(new[]
                    {
                        "[Current]", "LlamaTemperature=0.7", "RouterIni=X:\\llama\\llama_models.ini",
                        "LlamaUseRawIni=True", "Port=9090",
                    }, back, new Dictionary<string, Settings>(), new MainForm.ConfigParseResult());
                    Check("旧键还读得进（搬家种子要从这里取值）",
                        back.LlamaTemperature == "0.7" && back.RouterIni.Contains("llama_models.ini"),
                        $"temp={back.LlamaTemperature} ini={back.RouterIni}");
                    Check("属性已整条删掉的旧键读不认、且不牵连别的键（LlamaUseRawIni / merged 一起退役，同 LowRam DEC-075）",
                        back.Port == "9090", "port=" + back.Port);
                }

                // 9e. 按钮文案判定（用户反馈：按钮状态要跟随实际工作状态）——穷举组合
                // 第 0 轮 DEC-094：判据拆成一模块一个纯函数（原来共用一个带 `bool llamaSel` 的函数 = 界面上最后两处二元判断）
                Check("左栏按钮：llama 在跑 -> 停止", MainForm.StartStopTextForLlama(true) == "停止");
                Check("左栏按钮：llama 没跑 -> 启动", MainForm.StartStopTextForLlama(false) == "启动");
                Check("左栏按钮：壳的服务在跑 -> 停止", MainForm.StartStopTextForStrata(true, false, false) == "停止");
                Check("左栏按钮：外部服务在跑 -> 停止", MainForm.StartStopTextForStrata(false, true, false) == "停止");
                // 2026-10-15（他点名「停止网关/停止按钮,统一修改为停止」）：原来"只剩网关"那一档报「停止网关」，
                // 现在三档统一「停止」—— 这三条钉住的是"三档同一个字"，不是"哪一档优先"。
                Check("左栏按钮：只剩网关（空闲停服后）-> 停止", MainForm.StartStopTextForStrata(false, false, true) == "停止");
                Check("左栏按钮：都没有 -> 启动", MainForm.StartStopTextForStrata(false, false, false) == "启动");
                Check("左栏按钮：三档统一「停止」（服务在跑 / 外部实例 / 只剩网关都报同一个字）",
                    MainForm.StartStopTextForStrata(true, false, false) == "停止"
                    && MainForm.StartStopTextForStrata(false, true, false) == "停止"
                    && MainForm.StartStopTextForStrata(false, false, true) == "停止"
                    && MainForm.StartStopTextForStrata(true, true, true) == "停止");
                // 模型行（用户规格 2026-10-01；**DEC-141 改口径 = 三页统一：本模块有东西在跑就一直给「卸载」**，
                // 点下去卸的是实际在跑的那一件，不再要求下拉正指过去 —— 他的原话：
                // "卸载按钮在已有模型处于加载状态时,应始终显示吧,而不是当前这样,只有选中已加载模型时才显示卸载"）
                Check("模型行按钮：Strata 服务在跑 -> 卸载", MainForm.ModelActionTextForStrata(true, false) == "卸载");
                Check("模型行按钮：Strata 外部服务在跑 -> 卸载", MainForm.ModelActionTextForStrata(false, true) == "卸载");
                Check("模型行按钮：Strata 没跑也不是外部服务 -> 加载", MainForm.ModelActionTextForStrata(false, false) == "加载");
                Check("模型行按钮：llama 路由未加载 -> 加载", MainForm.ModelActionTextForLlama("", false, true) == "加载");
                Check("模型行按钮：DEC-141 llama 路由里有东西加载着 -> 卸载（不看选中的是谁）",
                    MainForm.ModelActionTextForLlama("", true, true) == "卸载");
                Check("模型行按钮：llama 单体模式 -> 重载（不管加载没加载）",
                    MainForm.ModelActionTextForLlama("", true, false) == "重载"
                    && MainForm.ModelActionTextForLlama("", false, false) == "重载");
                Check("模型行按钮：忙态优先（加载中…）", MainForm.ModelActionTextForLlama("加载中…", true, true) == "加载中…");
                Check("模型行按钮不再出现启动/停止字样",
                    !new[] { MainForm.ModelActionTextForLlama("", false, true), MainForm.ModelActionTextForLlama("", true, false),
                             MainForm.ModelActionTextForStrata(true, false) }
                        .Any(x => x.Contains("启动") || x.Contains("停止")));
                // DEC-141「卸载」落到哪一件 = 跑着的那件（三页共用一个判据家；llama 那一族是集合，另两页是单件）
                Check("DEC-141 卸载落点：跑着的件优先，没记下件名才退回选中的",
                    MainForm.UnloadTagFor("gsq", "mtp") == "gsq"
                    && MainForm.UnloadTagFor("", "mtp") == "mtp"
                    && MainForm.UnloadTagFor("gsq", "gsq") == "gsq");
                Check("DEC-141 llama 卸载落点 = 加载集合里那些、选中的排最前；集合空 = 交给「加载」；同名只算一件",
                    string.Join(",", MainForm.UnloadTargetsForLlama(new[] { "A", "B" }, "B")) == "B,A"
                    && string.Join(",", MainForm.UnloadTargetsForLlama(new[] { "A" }, "Z")) == "A"
                    && MainForm.UnloadTargetsForLlama(Array.Empty<string>(), "A").Count == 0
                    && string.Join(",", MainForm.UnloadTargetsForLlama(new[] { "A", "a" }, "A")) == "A");
                // 旧形状不许回来：那一档"跑的是不是选中那件"比较在两处喂参数口都不许再出现。
                // 两个 needle 都**拆开拼**（DEC-138 那条 "Directory" + ".Move(" 的先例）：整串写在一起的话，
                // 这条判据自己的源码就是两处命中 = 必假红（实测抓到过一次）。
                var oldShapeHits = CodeHits("_runningTag == (m" + "?.Tag") + CodeHits("NinferRunning && _ninferRunning" + "Tag ==");
                Check("DEC-141 旧形状已退出源码（两处喂参数口都不再比较选中件）",
                    oldShapeHits is -2 or 0, oldShapeHits == -2 ? "用例前提：源码不在旁边（实跑目录跑 --selftest 就是这种），这条只登记"
                                                                : $"命中 {oldShapeHits} 处");
                // DEC-142：llama 速度那一族的点名（判据与句子只有一个家；**壳不改行为、不替他改家底**）
                Check("DEC-142 只有命令行与家底都没定过 parallel 才提醒，任一处定过就闭嘴",
                    MainForm.LlamaParallelHintLine(false, false)!.Contains("parallel")
                    && MainForm.LlamaParallelHintLine(true, false) is null
                    && MainForm.LlamaParallelHintLine(false, true) is null);
                // 2026-10-16（DEC-185 遗留②收账）：起服务那一段住在 `MainForm.LlamaLifecycle.cs`（不在 `MainForm.Llama.cs`），
                // 文件指针写错了 → 这条判据改前恒 -2（判据自己空转）。
                var hintWired = CodeHitsBetween("MainForm.LlamaLifecycle.cs", "string.Join(\" \", args)", "ProbeLlamaReady(p, port)",
                                               "LlamaParallelHintLine(");
                Check("DEC-142 那一句真挂在起服务那条路上（不是只测纯函数的空判据）",
                    hintWired is 1 or -1, hintWired == -1 ? "源码不在旁边 = 只登记" : $"那一发出现在起服务方法体里 {hintWired} 次");
                // 家底认不认 parallel：拿三份真文件量（`[*]` 写过 / 只在段里写过且大小写不同 / 两处都没写过），
                // 外加"文件不在"那一面 —— 只测纯函数的两 bool 看不见读文件那一半走没走通
                var parHome = MainForm.Scratch("selftest-llama-parallel");
                Directory.CreateDirectory(parHome);
                File.WriteAllText(Path.Combine(parHome, "g.ini"), "version = 1\n\n[*]\nparallel = 1\n\n[A]\nmodel = a.gguf\n");
                File.WriteAllText(Path.Combine(parHome, "sec.ini"), "version = 1\n\n[*]\nflash-attn = on\n\n[A]\nmodel = a.gguf\nParallel = 1\n");
                File.WriteAllText(Path.Combine(parHome, "none.ini"), "version = 1\n\n[*]\nflash-attn = on\n\n[A]\nmodel = a.gguf\nctx-size = 4096\n");
                Check("DEC-142 家底里认不认 parallel 用真文件量（[*] 写过 / 只在段里写过（大小写不计）/ 两处都没写过 / 文件不在）",
                    MainForm.LlamaIniDefinesParallel(Path.Combine(parHome, "g.ini"))
                    && MainForm.LlamaIniDefinesParallel(Path.Combine(parHome, "sec.ini"))
                    && !MainForm.LlamaIniDefinesParallel(Path.Combine(parHome, "none.ini"))
                    && !MainForm.LlamaIniDefinesParallel(Path.Combine(parHome, "no-such-file.ini")),
                    "读不动 / 不在 = 判\"没定过\"（不提示，口径同 DEC-067）");

                // 9e4. 模块开关（B 段地基，DEC-024/025）：至少启用一个 + 关闭前先停服务（都在保存时拦）
                Check("模块注册表：默认三个模块都启用（DEC-096 起 Ninfer 也在表里）",
                    ModuleRegistry.AnyEnabled(new Settings())
                    && ModuleRegistry.All.All(m => m.Enabled(new Settings())),
                    string.Join(" / ", ModuleRegistry.All.Select(m => m.DisplayName + (m.Enabled(new Settings()) ? "启用" : "停用"))));
                var modOff = new Settings { EnableStrata = false, EnableLlama = false, EnableNinfer = false };
                Check("模块 ini 全关 -> 启动兜底至少启用一个（Strata）",
                    ModuleRegistry.EnsureAtLeastOne(modOff) && modOff.EnableStrata);
                var modNinferOnly = new Settings { EnableStrata = false, EnableLlama = false, EnableNinfer = true };
                Check("模块只启用 Ninfer 时兜底不动作（第三个模块进来后，兜底只认「全关」那一种情况）",
                    !ModuleRegistry.EnsureAtLeastOne(modNinferOnly) && !modNinferOnly.EnableStrata && modNinferOnly.EnableNinfer);
                var modLlamaOnly = new Settings { EnableStrata = false, EnableLlama = true };
                Check("模块只启用 llama 时兜底不动作",
                    !ModuleRegistry.EnsureAtLeastOne(modLlamaOnly) && !modLlamaOnly.EnableStrata && modLlamaOnly.EnableLlama);
                // 第 0 轮 DEC-094：模块开关校验改成「按模块一条批复」——纯函数版在这里穷举，
                // 按表穷举（每个模块一条）在主窗那一段（见 ViewsForTest 那个循环）
                MainForm.SwitchReply Rpl(string name, bool off, bool busy, string msg) => new(name, off, busy, msg);
                Check("保存校验：两个模块全关 -> 拦下（至少要启用一个）（纯函数版）",
                    (MainForm.ModuleSwitchAtSave(new[] { Rpl("Strata", true, false, "甲"), Rpl("llama.cpp", true, false, "乙") }) ?? "").Contains("至少"));
                Check("保存校验：停用 Strata 但服务/网关在跑 -> 拦下先停（纯函数版）",
                    (MainForm.ModuleSwitchAtSave(new[] { Rpl("Strata", true, true, "它的服务/网关还在跑"), Rpl("llama.cpp", false, false, "乙") }) ?? "")
                    .Contains("网关还在跑"));
                Check("保存校验：停用 llama 但 llama-server 在跑 -> 拦下先停（纯函数版）",
                    (MainForm.ModuleSwitchAtSave(new[] { Rpl("Strata", false, false, "甲"), Rpl("llama.cpp", true, true, "llama-server 还在跑") }) ?? "")
                    .Contains("llama-server 还在跑"));
                Check("保存校验：只关空闲模块 -> 放行（纯函数版）",
                    MainForm.ModuleSwitchAtSave(new[] { Rpl("Strata", true, false, "甲"), Rpl("llama.cpp", false, false, "乙") }) is null
                    && MainForm.ModuleSwitchAtSave(new[] { Rpl("Strata", false, false, "甲"), Rpl("llama.cpp", true, false, "乙") }) is null
                    && MainForm.ModuleSwitchAtSave(new[] { Rpl("Strata", false, false, "甲"), Rpl("llama.cpp", false, false, "乙") }) is null,
                    "只关没在跑的模块不该拦");

                // 9e4b. ③④（DEC-038）：节流判据 + 状态栏两个数据源的解析与文案（纯函数，不碰真服务）
                string spamRead = "[strata] reading the prompt: 8,192 of 12,703 tokens, 4 s so far";
                string spamThink = "[strata] thinking: 40 of max 32768 tokens, 79.4 tok/s, 9 s";
                string spamTool = "[strata] writing a tool call: run_code: 74 of max 32768 tokens, 48.1 tok/s, 11 s";
                string lineDone = "[strata] done: 169 tokens in 11 s (70.1 tok/s) (stop, cancel=False), expert cache 84.3% hit";
                string lineLoad = "[strata] experts loaded: 39.97 GiB at 2.43 GiB/s (29 s so far)";
                string lineStillStarting = "[strata] still starting (24 s) - please wait ...";
                Check("节流判据：预填充/思考/工具三种每秒进度行都认出来",
                    MainForm.IsProgressSpam(spamRead) && MainForm.IsProgressSpam(spamThink) && MainForm.IsProgressSpam(spamTool));
                Check("节流判据不误伤：done 收尾行、加载阶段行、不含 tokens 的行照旧显示",
                    !MainForm.IsProgressSpam(lineDone) && !MainForm.IsProgressSpam(lineLoad)
                    && !MainForm.IsProgressSpam(lineStillStarting));
                var thr0 = new DateTime(2026, 10, 3, 1, 2, 3);
                Check("节流窗口：5 秒内的后续行不放行、满 5 秒放行、第一条总是放行",
                    !MainForm.ProgressThrottleAllows(thr0, thr0.AddSeconds(4))
                    && MainForm.ProgressThrottleAllows(thr0, thr0.AddSeconds(5))
                    && MainForm.ProgressThrottleAllows(DateTime.MinValue, thr0));

                // 9e4b2. DEC-158 → DEC-162 → DEC-163：三模块共用的那张降噪表（DEC-111 §5 第 2、3 条那两条的第三版形状）。
                // 现行版（DEC-163，第 41 轮，他核过体积后拍"B 路径"、原话"1MB 还能接受"）：八类引擎台账**文件与内存历史照记**，
                // 只做一条**简单去重**（同一类里与上一条逐字节相同就不落）；界面由「调试信息过滤」挡；信号行与 llama 那族不去重。
                // 样本行全部取自他 2026-10-12 贴回来那一份（4,586 行 / 85 秒），逐类一条原文 —— 判据不靠转述的形状。
                string nRing = "[ring] budgets: skeleton=4, evidence=128, recent=0, free=732 (pool 864) [skeleton+evidence+recent+free == pool: yes]";
                string nKeep = "kvmem_score: KEPT 0 1 2 3 4 5 6 7 8 9 10 200 201 205 211 214 223 320 321";
                string nMapped = "[ninfer] mask hidden: which=text site=mapped span=1074 resident=856 hidden=218 first_hidden=32 last_hidden=298 mid_hidden=0 mid_lo=375 mid_hi=751";
                string nCommit = "[ninfer] mask hidden: which=text site=commit span=1073 resident=855 hidden=217 first_hidden=32 last_hidden=297 mid_hidden=0 mid_lo=375 mid_hi=751";
                string nSelect = "kvmem_score: SELECT label=text_prefill_chunk n_blocks=14 budget_blocks=12 sink_blocks=1";
                string nIdle = "[ninfer] demote-other short: freed=0 want=1 slots=0 invalid=0 noresident=0 pins=0 stalehost=0 partial=0 dup=0 flushfail=0 declined=0 pending=0";
                string nFreed = "[ninfer] demote-other short: freed=3 want=1 slots=0 invalid=0 noresident=0 pins=0 stalehost=0 partial=0 dup=0 flushfail=0 declined=0 pending=0";
                string nFlush = "[ninfer] demote-other short: freed=0 want=1 slots=0 invalid=0 noresident=0 pins=0 stalehost=0 partial=0 dup=0 flushfail=1 declined=0 pending=0";
                string nRetrieve = "[ninfer] ring retrieve: which=text preferred=48 restored=48 first=12 last=88";
                string nBacked = "[ninfer] publish host-backed: frontier=1074 host_bytes=8589934592";
                Check("DEC-163 名单：八类引擎台账都在（六类纯台账 + 两类真证据 —— ring budgets / kvmem KEPT / mask hidden 两类 / kvmem SELECT / demote 空转 / ring retrieve / host-backed）",
                    LogNoise.Classify(nRing) is not null && LogNoise.Classify(nKeep) is not null
                    && LogNoise.Classify(nMapped) is not null && LogNoise.Classify(nCommit) is not null
                    && LogNoise.Classify(nSelect) is not null && LogNoise.Classify(nRetrieve) is not null
                    && LogNoise.Classify(nBacked) is not null
                    && LogNoise.Classify(nMapped)!.Id != LogNoise.Classify(nCommit)!.Id);
                Check("DEC-158 条件放行：demote 那行只有「一个都没解放、失败位全 0」才归名单；freed 非零或 flushfail 非零 = 真消息，原样留（也不去重）",
                    LogNoise.Classify(nIdle) is not null
                    && LogNoise.Classify(nFreed) is null && LogNoise.Classify(nFlush) is null,
                    (LogNoise.Classify(nFreed)?.Id ?? "null") + " / " + (LogNoise.Classify(nFlush)?.Id ?? "null"));
                Check("DEC-163 不误伤：req# done / throughput / 池超界 / 壳自己写的行为主线索引 = 一条都不进名单（也就不会被去重）",
                    LogNoise.Classify("2026-10-12 07:06:08.310  INFO  req#29 done | openai-chat | tool calls 1 | prompt 50,589 | output 198 | TTFT 541 ms | decode 138.0 tok/s") is null
                    && LogNoise.Classify("2026-10-12 07:07:32.215  INFO  throughput | 5.0s | decode 46.0 tok/s (230 tok) | running 0 | batch 1.00 | host 59.7%") is null
                    && LogNoise.Classify("[warning] ninfer-serve: prompt exceeds the resident Device KV pool: prompt 262144 tokens") is null
                    && LogNoise.Classify("引擎已拉起（win · 内部端口 8069 · 这一件 = qwen3.8-27b-gsq）") is null
                    && LogNoise.Classify("[strata] done: 169 tokens in 11 s (70.1 tok/s)") is null);
                Check("DEC-163 Strata 进度行**不在名单里**（界面走它自己的 5 秒节流、文件恢复全量；DEC-158 把它搬进表的那一行已随 B 路径退出）",
                    LogNoise.Classify(spamRead) is null && LogNoise.Classify(spamThink) is null
                    && LogNoise.Classify(lineDone) is null && LogNoise.Classify(lineLoad) is null);
                Check("DEC-163 界面口径在 IsNoise 这一处并起来（过滤只管看；llama 那 14 条照旧）",
                    MainForm.IsNoise(nRing) && MainForm.IsNoise(nMapped) && MainForm.IsNoise(nRetrieve)
                    && !MainForm.IsNoise("2026-10-12 07:06:08.310  INFO  req#29 done | prompt 50,589"));
                // 去重三面（纯函数喂字典，不等真日志）：首条放行 / 同内容挡下 / 变了再放 / 不同类互不干扰
                var dlast = new Dictionary<string, string>();
                var rRing = LogNoise.Classify(nRing)!; var rMapped = LogNoise.Classify(nMapped)!;
                Check("DEC-163 去重三面：首条放行、同内容挡下、变了再放；**按类各记各的**（中间插一条 mapped，ring 的同内容仍要挡得住）",
                    !LogNoise.IsRepeat(dlast, rRing, nRing)
                    && !LogNoise.IsRepeat(dlast, rMapped, nMapped)
                    && LogNoise.IsRepeat(dlast, rRing, nRing)
                    && !LogNoise.IsRepeat(dlast, rRing, nRing + " changed")
                    && LogNoise.IsRepeat(dlast, rMapped, nMapped),
                    string.Join(" | ", dlast.Select(kv => kv.Key + " => " + kv.Value.Substring(0, Math.Min(16, kv.Value.Length)))));
                // 合成行的守卫（needle 拆开拼 = BUG-066① 那条老账；r40 已跑过红侧 —— 保留原样）
                var aggScan = CodeHits("[" + "降噪] ");
                Check("DEC-162/163 壳不产出合成行：那个「降噪」标记全源码 0 处（谁要把它捡回来这条就红）",
                    aggScan is -1 or 0, "命中 " + aggScan + " 处");
                // 「写全量」整套退役的守卫（新扫描，第一次必须跑红侧 = BUG-075 那条规矩；用例与夹具里的字面同样拆开拼）
                var ledgerScan = CodeHits("EngineFull" + "Ledger");
                Check("DEC-163 「写全量」整套退役：那个旧键全源码 0 处（属性 / 开关 / 接缝 / 文案里都不许留；注释不算）",
                    ledgerScan is -1 or 0, "命中 " + ledgerScan + " 处");
                Check("DEC-163 名单穷举：八类引擎台账、一行不多一行不少（加形状 = 加行，别退回「各写一套」）",
                    LogNoise.Rules.Length == 8, "名单 " + LogNoise.Rules.Length + " 行");

                // 9e4c. DEC-111：日志三条护栏（轮转 / 测试态闸 / 界面上限）。
                // 真实 logs\ 不进 ⓪ 那一族"跑前拍快照、跑完逐字节比"的护栏：外壳活着时引擎本来就在往里写，
                // 比了必假红。这条护栏在这里的等价形式 = 改道到 data\tmp\ 的副本上，验"闸门按上一个字节都不多"
                // 并且**再验反面的那一条**（放开就写得进去）—— 只有正面那条会假绿（历史教训 = BUG-043）。
                Check("DEC-111 常量：日志留 3 份（与 DEC-110⑤ 那五处备份同口径）、单文件阈值 8MB、内存历史上限 5000",
                    MainForm.LogRotateKeep == 3 && MainForm.LogRotateBytes == 8L * 1024 * 1024
                    && MainForm.LogHistoryMaxLines == 5000,
                    "keep=" + MainForm.LogRotateKeep + " bytes=" + MainForm.LogRotateBytes + " hist=" + MainForm.LogHistoryMaxLines);
                Check("DEC-111 轮转判据：8MB 整不滚、超一字节才滚（旧判据 = 一次会话只量一次，8MB 之后一路涨到进程退出）",
                    !MainForm.ShouldRotateLog(MainForm.LogRotateBytes) && MainForm.ShouldRotateLog(MainForm.LogRotateBytes + 1));
                Check("DEC-111 界面上限判据：5000 行不动、5001 行走同一条重建判据（RebuildLogView 是唯一的截断落点，别处不再写一套）",
                    !MainForm.LogBoxNeedsRebuild(MainForm.LogHistoryMaxLines)
                    && MainForm.LogBoxNeedsRebuild(MainForm.LogHistoryMaxLines + 1));
                {
                    var rot = MainForm.Scratch("selftest-rotlog");
                    try { Directory.CreateDirectory(rot); } catch { }
                    // 开跑前先把这个窝扫干净：上一趟（含红侧那一趟）留下来的 a.log.4 会让"不许长出 .4"这条永远红，
                    // 判据只看这一趟滚出来的结果。
                    foreach (var f in Directory.GetFiles(rot)) File.Delete(f);
                    File.WriteAllText(Path.Combine(rot, "a.log"), "live");
                    File.WriteAllText(Path.Combine(rot, "a.log.1"), "one");
                    File.WriteAllText(Path.Combine(rot, "a.log.2"), "two");
                    File.WriteAllText(Path.Combine(rot, "a.log.3"), "three");
                    MainForm.RotateLogFiles(rot, "a");
                    string Rd(string n) { var p = Path.Combine(rot, n); return File.Exists(p) ? File.ReadAllText(p) : "∅"; }
                    Check("DEC-111 轮转真动作：当前那份滚成 .1、.1→.2→.3 依次顶一格、最旧那格删掉（不许长出 .4）",
                        Rd("a.log") == "∅" && Rd("a.log.1") == "live" && Rd("a.log.2") == "one"
                        && Rd("a.log.3") == "two" && Rd("a.log.4") == "∅",
                        ".1=" + Rd("a.log.1") + " .2=" + Rd("a.log.2") + " .3=" + Rd("a.log.3") + " .4=" + Rd("a.log.4"));
                }
                {
                    var lg = MainForm.Scratch("selftest-logs");
                    MainForm.UseTempLogsDirForTest(lg);
                    foreach (var f in Directory.GetFiles(lg)) File.Delete(f);       // 判据只看这一趟，上一趟留的清掉
                    bool gate = MainForm.LogFileDisabledForTest;                     // 入口点名那道闸（SelfTest.Run / UiPreviewForm.Run）
                    MainForm.AppendLogFileForTest("ninfer", "DEC-111 护栏用例行（闸门按上）");
                    var gated = Directory.GetFiles(lg);
                    Check("DEC-111 测试态闸：自检/出图台期间 AppendLogFile 一个字节都不写（用户的 logs\\ 里不该有测试行）",
                        gate && MainForm.LogsDirOverriddenForTest && gated.Length == 0,
                        "改道位=" + MainForm.LogsDirOverriddenForTest + "｜" + gated.Length + " 份：" + string.Join("、", gated.Select(Path.GetFileName)));
                    MainForm.LogFileDisabledForTest = false;                         // 反面：临时放开必须写得进去
                    MainForm.AppendLogFileForTest("ninfer", "DEC-111 护栏用例行（闸门放开）");
                    MainForm.LogFileDisabledForTest = gate;
                    var written = Path.Combine(lg, "ninfer.log");
                    Check("DEC-111 闸门的反面：放开就真写得进 ninfer.log（上面那条不是空判据的证人）",
                        File.Exists(written) && File.ReadAllText(written).Contains("闸门放开"),
                        File.Exists(written) ? File.ReadAllText(written).Trim() : "ninfer.log 不在");
                    MainForm.UseTempLogsDirForTest(null);                            // 改道位收回去，别影响后面的用例
                }
                // ---------------- DEC-114：data\tmp 的三条收口 ----------------
                // ① 更新链 finally 收尾 ② 自检/出图台跑完清自己那一窝（**认名字**）③ 启动兜底扫（**认年龄**）。
                // 两套判据不混用，各有纯函数当家；正面与反面都验（只有正面那条会假绿 = DEC-111 立的规矩），
                // 而且一律打在 fixture 目录上，不去动真实 data\tmp 里别人正在用的东西。
                Check("DEC-114② 名字判据：selftest-/uiprobe- 两个前缀 + 三个登记在案的旧名字 + 词根在中间的（strata-selftest-release.json 那种）都算窝，其余一律不算",
                    MainForm.IsTestScratch("selftest-run") && MainForm.IsTestScratch("uiprobe-ninfer")
                    && MainForm.IsTestScratch("fit-root") && MainForm.IsTestScratch("root-probe")
                    && MainForm.IsTestScratch("ninfer-fidelity")
                    && MainForm.IsTestScratch("strata-selftest-release.json")     // "selftest" 在中间，只按前缀会漏（实测漏过）
                    && MainForm.IsTestScratch("llama-selftest-releases.json")
                    && !MainForm.IsTestScratch("llama-releases.json") && !MainForm.IsTestScratch("strata-0.1.40.4.zip")
                    && !MainForm.IsTestScratch("strata-update-0.1.40.4")      // 更新残留归①的 finally 与③的年龄，②不许顺手认它
                    && !MainForm.IsTestScratch("strata-launcher-net.py")      // 下载用的那份 python 助手是**运行期**的，不是测试窝
                    && !MainForm.IsTestScratch("MultiLauncher.ini"));         // （原判据"selftestx 不带连字符就不算"随词根判据作废：现在带词根就算）
                var t0 = new DateTime(2026, 10, 8, 12, 0, 0);                 // 固定"现在"，用例不跟真实时钟漂
                Check("DEC-114③ 年龄判据：严格超过保留期才算过期（正好踩在那一分钟的不动）",
                    MainForm.IsStaleScratch(t0.AddDays(-4), t0) && MainForm.IsStaleScratch(t0.AddDays(-3).AddMinutes(-1), t0)
                    && !MainForm.IsStaleScratch(t0.AddDays(-2), t0) && !MainForm.IsStaleScratch(t0, t0)
                    && !MainForm.IsStaleScratch(t0.AddDays(-3), t0));
                Check("DEC-114③ 的保留期 = 3 天（不许是 0：那等于每次启动把这一趟刚写的草稿也扫掉）",
                    MainForm.TmpRetentionDays == 3, MainForm.TmpRetentionDays.ToString());
                Check("DEC-114③ 的根只认自家 data\\tmp，永远不等于 %TEMP%（TempDir() 那条退回系统临时目录的路不许接进来）",
                    MainForm.ScratchSweepRoot() is { } sr
                    && Path.GetFileName(sr) == "tmp"
                    && Path.GetFileName(Path.GetDirectoryName(sr)!) == "data"
                    && !string.Equals(sr.TrimEnd(Path.DirectorySeparatorChar),
                                      Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar),
                                      StringComparison.OrdinalIgnoreCase),
                    MainForm.ScratchSweepRoot() ?? "（data\\tmp 不在 = 什么都不扫）");
                Check("DEC-114②③ 两道测试闸按进程收口（与 DEC-111 那道日志闸同一个形状，入口点名）",
                    MainForm.LogFileDisabledForTest && MainForm.ScratchSweepDisabledForTest,
                    "日志闸=" + MainForm.LogFileDisabledForTest + "｜扫窝闸=" + MainForm.ScratchSweepDisabledForTest);
                {
                    var fix = MainForm.Scratch("selftest-sweep");   // 这名字自己就是窝 → 跑完由②收
                    MainForm.TryDeleteTree(fix);
                    Directory.CreateDirectory(fix);
                    static string Mk(string root, string name, DateTime when, DateTime? innerWhen = null)
                    {
                        // 目录与里面的文件**都要**按样本时间摆：只摆目录那一层的话，文件的真实 mtime 就是"现在"，
                        // NewestWriteIn 会把它算进去（本轮实测踩到过：那条用例变成"删了 0 项"的假红）。
                        // innerWhen 给值 = 多一层子目录、子目录里的文件用那个时间 —— 摆"目录壳旧、里面刚写"这一面。
                        var d = Path.Combine(root, name);
                        Directory.CreateDirectory(d);
                        var f = innerWhen is null ? Path.Combine(d, "a.bin") : Path.Combine(d, "sub", "a.bin");
                        if (innerWhen is not null) Directory.CreateDirectory(Path.Combine(d, "sub"));
                        File.WriteAllText(f, "seed");
                        File.SetLastWriteTime(f, innerWhen ?? when);
                        Directory.SetLastWriteTime(d, when);
                        return d;
                    }
                    var gone = Mk(fix, "selftest-gone", t0.AddDays(-10));      // 窝 + 过期：②该删
                    var kept = Mk(fix, "selftest-keep", t0);                   // 窝 + 刚写：②也删（名字判据不看年龄）
                    var old = Mk(fix, "old", t0.AddDays(-10));                 // 非窝 + 过期：②不许碰、③要删
                    var fresh = Mk(fix, "fresh", t0);                          // 非窝 + 刚写：两条都不许碰
                    var deep = Mk(fix, "deepfresh", t0.AddDays(-10), innerWhen: t0);   // 目录壳很旧、里面文件刚写
                    int byName = MainForm.CleanTestScratchIn(fix);
                    Check("DEC-114② 真动作（认名字，不看年龄）：两个 selftest-* 窝删掉，非窝的三条一个都不动",
                        byName == 2 && !Directory.Exists(gone) && !Directory.Exists(kept)
                        && Directory.Exists(old) && Directory.Exists(fresh) && Directory.Exists(deep),
                        "删了 " + byName + " 项；old/fresh/deepfresh 还在="
                        + Directory.Exists(old) + "/" + Directory.Exists(fresh) + "/" + Directory.Exists(deep));
                    var (n, bytes) = MainForm.SweepIn(fix, t0);
                    Check("DEC-114③ 真动作（认年龄）：过期的 old 删掉、刚写的 fresh 留着（两面判据，防只正面那条假绿）",
                        n == 1 && !Directory.Exists(old) && Directory.Exists(fresh) && bytes > 0,
                        "删了 " + n + " 项 / " + bytes + " 字节");
                    Check("DEC-114③ NewestWriteIn 那条不是空判据：目录壳旧但里面的文件是刚写的 → 不许删"
                          + "（把 NewestWriteIn 按回成只看目录 mtime，这条就红）",
                        Directory.Exists(deep));
                    Check("DEC-114③ 第二次扫已经没有过期项（幂等，不会把刚留下的也吃掉）",
                        MainForm.SweepIn(fix, t0).Count == 0 && Directory.Exists(fresh));
                }
                {
                    var staging = MainForm.Scratch("selftest-staging");
                    Directory.CreateDirectory(staging);
                    File.WriteAllText(Path.Combine(staging, "seed.txt"), "x");
                    bool first = MainForm.CleanupStagingDir(staging);
                    bool second = MainForm.CleanupStagingDir(staging);
                    Check("DEC-114① 更新链 finally 那个收尾函数：删得掉、第二次幂等返回 false、传 null 不抛",
                        first && !Directory.Exists(staging) && !second && !MainForm.CleanupStagingDir(null),
                        "第一次=" + first + "｜第二次=" + second);
                }

                // ---------------- DEC-119：ninfer_models.ini 摊平成"一行一键"（DEC-114 批复的那五条定则）----------------
                // 支点：argv 仍是**内部中间表示**（起前护栏判"实际那一把命令行"，DEC-099 的口径不退），
                // 换格式只动文件层 → 所以这一族用例打在"键 ↔ argv 那一层薄皮 + 迁移 + 写段落键"上。
                const string gsqArgs = "--max-context 262144 --kv-dtype k8v4 --lm-head-draft --temperature 0.7 --max-concurrency 1";
                Check("DEC-119 argv 摊成键：带值的各归一行，布尔那枚固定写 true（界面上「关」= 删键，所以这里不产 false）",
                    NinferIni.ArgvToKeys(gsqArgs, out var flatGsq, out var flatGsqErr) && flatGsqErr.Length == 0
                    && flatGsq.Count == 5
                    && flatGsq[0].Key == "max-context" && flatGsq[0].Value == "262144"
                    && flatGsq.Any(k => k.Key == "lm-head-draft" && k.Value == "true")
                    && flatGsq.Any(k => k.Key == "max-concurrency" && k.Value == "1"),   // 表外带值旗标照旧带着
                    flatGsq.Count + " 键｜" + flatGsqErr);
                Check("DEC-119 往返免费：键折回 argv 与原来那一把逐字节相同（次序 = 行序，他自己控的那把顺序不因换格式而变）",
                    NinferIni.KeysToArgv(flatGsq) == gsqArgs, NinferIni.KeysToArgv(flatGsq));
                Check("DEC-119 布尔绝不产出裸 token：折回来不许是「--lm-head-draft true」（BUG-041 那个 unknown argument 的形状）",
                    !NinferIni.KeysToArgv(flatGsq).Contains("--lm-head-draft true")
                    && NinferIni.KeysToArgv(flatGsq).Split(' ').Length == gsqArgs.Split(' ').Length,
                    NinferIni.KeysToArgv(flatGsq));
                var rBare = !NinferIni.ArgvToKeys("--max-context 262144 262144", out _, out var eBare);
                var rNoVal = !NinferIni.ArgvToKeys("--max-context", out _, out var eNoVal);
                var rName = !NinferIni.ArgvToKeys("--Max_Context 1", out _, out var eName);
                var rDup = !NinferIni.ArgvToKeys("--top-k 20 --top-k 21", out _, out var eDup);
                var rIdt = !NinferIni.ArgvToKeys("--file x.ninfer", out _, out var eIdt);
                Check("DEC-119 认不出的形状一律拒、并说清是哪一枚（裸 token / 缺值 / 名字不成键 / 同一枚写两次 / 撞身份键）",
                    rBare && eBare.Contains("不是旗标")
                    && rNoVal && eNoVal.Contains("没有值")
                    && rName && eName.Contains("不成其为键")
                    && rDup && eDup.Contains("两次")
                    && rIdt && eIdt.Contains("身份键"),
                    eBare + " / " + eNoVal + " / " + eName + " / " + eDup + " / " + eIdt);
                Check("DEC-119 身份键不进命令行、段名与 backend/file 永不被壳写：KeysToArgv 把它们筛掉",
                    NinferIni.KeysToArgv(new[]
                    {
                        new KeyValuePair<string, string>("backend", "win"),
                        new KeyValuePair<string, string>("file", "a.ninfer"),
                        new KeyValuePair<string, string>("kv-dtype", "k8v4"),
                    }) == "--kv-dtype k8v4",
                    NinferIni.KeysToArgv(new[]
                    {
                        new KeyValuePair<string, string>("backend", "win"),
                        new KeyValuePair<string, string>("file", "a.ninfer"),
                        new KeyValuePair<string, string>("kv-dtype", "k8v4"),
                    }));
                {
                    // 迁移的正脸与反面都在这一个窝里跑（改道位 = UseTempNinferHomeForTest，真件一个字节不碰）
                    var homeBak = Path.GetDirectoryName(MainForm.NinferIniPath())!;
                    var migHome = MainForm.Scratch("selftest-ninfer-flat");
                    MainForm.TryDeleteTree(migHome);
                    Directory.CreateDirectory(migHome);
                    var migIni = Path.Combine(migHome, MainForm.NinferIniFileName);
                    File.WriteAllText(migIni,
                        "[gsq]\nbackend = win\nfile = a.ninfer\nargs = --max-context 262144 --lm-head-draft --top-k 20\n" +
                        "\n[swift]\nbackend = wsl\nfile = b.ninfer\nargs = --temperature 0.7 --vision\n");
                    MainForm.UseTempNinferHomeForTest(migHome);
                    var migBytesBefore = File.ReadAllBytes(migIni);
                    int migN = MainForm.MigrateNinferIniToKeys(out var migErr, out var migDetail);
                    var migTxt = File.ReadAllText(migIni);
                    var migSecs = NinferIni.ReadSections(migIni);
                    Check("DEC-119 首启迁移正脸：两段都摊成一行一键、args 那行消失、折回来的命令行与迁移前等价（含两枚布尔）",
                        migN == 2 && migErr.Length == 0 && !migTxt.Contains("args =")
                        && migTxt.Contains("max-context = 262144") && migTxt.Contains("lm-head-draft = true")
                        && migTxt.Contains("vision = true")
                        && migSecs.Count == 2
                        && migSecs[0].Args == "--max-context 262144 --lm-head-draft --top-k 20"
                        && migSecs[1].Args == "--temperature 0.7 --vision"
                        && migSecs[0].Backend == "win" && migSecs[1].Backend == "wsl"
                        && migSecs[0].File == "a.ninfer" && !migSecs[0].HasLegacyArgs,
                        migN + " 段｜" + migDetail + "｜" + migErr);
                    // DEC-121：这条从 `>= 1` 收成**恰好 1 份**（`>= 1` 是半条空判据：旧实现每段各备份一次，三段 = 三份中间态，
                    // 把「各留最近 3 份」的保留位占满 → 他那份删注释前的原件就是这么被挤出清单的）。
                    // 反面还要验"那一份 = 迁移前的整份"，只数份数验不出内容。
                    var migBaks = MainForm.ListNinferIniBackups();
                    Check("DEC-121 一次迁移只备份一份（两段也只一份），且那份逐字节 = 迁移前的整份"
                          + "（旧实现挂在每段的写前钩子上 = 同一秒分出 -2 / -3 的中间态，会挤掉老备份）",
                        migBaks.Count == 1 && File.Exists(Path.Combine(MainForm.NinferBackupDir(), migBaks[0]))
                        && BytesEqual(migBytesBefore, File.ReadAllBytes(Path.Combine(MainForm.NinferBackupDir(), migBaks[0]))),
                        MainForm.NinferBackupDir() + "｜" + migBaks.Count + " 份：" + string.Join("、", migBaks));
                    var migAfterBytes = File.ReadAllBytes(migIni);
                    int migAgain = MainForm.MigrateNinferIniToKeys(out _, out _);
                    Check("DEC-119 迁移幂等：已经是新形状的段不再动（第二次返回 0、文件一个字节都不多）",
                        migAgain == 0 && BytesEqual(migAfterBytes, File.ReadAllBytes(migIni)), migAgain + " 段");
                    File.WriteAllText(migIni,
                        "[gsq]\nbackend = win\nfile = a.ninfer\nargs = --max-context 262144 --top-k\n" +
                        "\n[swift]\nbackend = win\nfile = b.ninfer\nargs = --temperature 0.7\n");
                    var badBytes = File.ReadAllBytes(migIni);
                    int badN = MainForm.MigrateNinferIniToKeys(out var badErr, out _);
                    Check("DEC-119 迁移的「拒绝即零改动」：有一段认不出（--top-k 后面没值）就**整份一个字不动**，并点名是哪一段哪一枚"
                          + "（反面判据：只迁成功那一半就是混形状的家底）",
                        badN < 0 && badErr.Contains("gsq") && badErr.Contains("没有值")
                        && BytesEqual(badBytes, File.ReadAllBytes(migIni)), badErr);
                    MainForm.UseTempNinferHomeForTest(homeBak);
                }
                {
                    // 写路径：新形状逐键写（旧形状段仍写 args 那一行，两形状读侧都认）
                    var homeBak = Path.GetDirectoryName(MainForm.NinferIniPath())!;
                    var wHome = MainForm.Scratch("selftest-ninfer-write");
                    MainForm.TryDeleteTree(wHome);
                    Directory.CreateDirectory(wHome);
                    var wIni = Path.Combine(wHome, MainForm.NinferIniFileName);
                    File.WriteAllText(wIni,
                        "[gsq]\nbackend = win\nfile = a.ninfer\ntemperature = 0.7\ntop-k = 20\nlm-head-draft = true\n");
                    MainForm.UseTempNinferHomeForTest(wHome);
                    // WriteNinferSectionEdits 是实例方法（它要 LogN 那一行落点），所以这里要一个真窗；
                    // 建在它之后：此刻家底已是键形状 → 构造函数里那道迁移返回 0、不动文件（迁移用例另有一窝）。
                    using var wf = new MainForm();
                    var before = File.ReadAllText(wIni);
                    int wrote = wf.WriteNinferSectionEdits("gsq",
                        new[] { new NinferIni.Edit("top-k", "15"), new NinferIni.Edit("lm-head-draft", "", true) },
                        out var wErr, out var wDetail);
                    var after = File.ReadAllText(wIni);
                    Check("DEC-119「保存」写新形状 = 只动该动的那一行：top-k 20 → 15 原位换值、布尔留空 = 整行删掉、args 行不长出来",
                        wrote == 2 && wErr.Length == 0
                        && after.Contains("top-k = 15") && !after.Contains("lm-head-draft") && !after.Contains("args =")
                        && after.Contains("backend = win") && after.Contains("file = a.ninfer")      // 身份键一个字节没被碰
                        && after.Contains("temperature = 0.7")
                        && wDetail.Contains("top-k 20 → 15") && wDetail.Contains("lm-head-draft"),
                        wrote + " 处｜" + wDetail + "｜" + wErr);
                    int same = wf.WriteNinferSectionEdits("gsq",
                        new[] { new NinferIni.Edit("top-k", "15") }, out _, out _);
                    Check("DEC-119「继承不摊平」那条在键形状下照样成立：值本来就一样 = 一个字节不写、连备份都不留",
                        same == 0 && after == File.ReadAllText(wIni), same + " 处");
                    // 旧形状兜底：迁移没做成的段（坏 args）保存时仍写回 args 那一行，不混两种形状
                    File.WriteAllText(wIni, "[gsq]\nbackend = win\nfile = a.ninfer\nargs = --max-context 262144 999\n");
                    int legacyWrote = wf.WriteNinferSectionEdits("gsq",
                        new[] { new NinferIni.Edit("top-k", "20") }, out var lErr, out _);
                    var legacyTxt = File.ReadAllText(wIni);
                    Check("DEC-119 旧形状兜底：段里还写着 args（迁移被拒的那类）时保存仍整行写 args、不长出键、也不报错"
                          + "（读侧两形状都认，界面不许因为摊平失败而写不进家底）",
                        legacyWrote == 1 && lErr.Length == 0 && legacyTxt.Contains("args = --max-context 262144 999 --top-k 20")
                        && !legacyTxt.Contains("top-k = 20"), legacyWrote + " 处｜" + lErr + "｜" + legacyTxt);
                    MainForm.UseTempNinferHomeForTest(homeBak);
                }
                Check("DEC-119 Ninfer 家底的默认落点 = data\\config\\ninfer\\ninfer_models.ini（形状钉死；在场性看明细）",
                    MainForm.DefaultNinferIniPath().EndsWith(
                        Path.Combine("data", "config", "ninfer", MainForm.NinferIniFileName), StringComparison.OrdinalIgnoreCase),
                    MainForm.DefaultNinferIniPath() + "｜在场=" + File.Exists(MainForm.DefaultNinferIniPath()));

                Check("节流窗口 = 5 秒（与状态栏同拍）", MainForm.ProgressGapSeconds == 5.0);

                Check("状态栏文案：预填充/解码 每档 `平均|当前`、MTP 只累计一档、取不到的档用短横 -",
                    MainForm.StatusBarText(463.8, 1305.4, 71.8, 55.8, 81.99)
                        == "预填充 463.8|1,305 t/s·解码 71.8|55.8 t/s·MTP 接受率 82.0%",
                    MainForm.StatusBarText(463.8, 1305.4, 71.8, 55.8, 81.99));
                const string metricsJson = """
                    {"engine":{"version":"0.1.35","spec":6},
                     "live":{"state":"generating","tok_s":55.8,"tok_s_mean":52.1,"prefill_tok_s_mean":1305.4},
                     "totals":{"requests":26,"prompt_tokens":1332219,"reused":1315197,"output_tokens":54451,
                               "prompt_ms":36697.5,"decode_ms":758322.6,
                               "drafts_accepted":36078,"drafts_offered":44002},
                     "requests":[{"output_tokens":486,"drafts_accepted":296,"drafts_offered":455}]}
                    """;
                var m1 = MainForm.ParseStrataMetrics(metricsJson);
                Check("/metrics 平均档 = 启动以来累计（预填充只算真读进去的 17022/36.7s≈464、解码 54451/758.3s≈71.8）",
                    Math.Abs(m1.PrefillAvg!.Value - 463.85) < 0.5 && Math.Abs(m1.DecodeAvg!.Value - 71.8) < 0.2,
                    $"{m1.PrefillAvg} | {m1.DecodeAvg}");
                Check("/metrics 当前档 = live.prefill_tok_s_mean / live.tok_s（MTP 本轮档 296/455≈65.1% 也解析得到，只是不显示）",
                    m1.PrefillNow == 1305.4 && m1.DecodeNow == 55.8
                    && Math.Abs(m1.AcceptAvg!.Value - 81.99) < 0.02 && Math.Abs(m1.AcceptNow!.Value - 65.05) < 0.02,
                    $"{m1.PrefillNow} | {m1.DecodeNow} | {m1.AcceptAvg} | {m1.AcceptNow}");
                var idleMetrics = MainForm.ParseStrataMetrics("""
                    {"live":{"state":"idle","tok_s":null,"tok_s_mean":null,"prefill_tok_s_mean":null},
                     "totals":{"drafts_offered":0,"prompt_ms":0,"decode_ms":0},"requests":[]}
                    """);
                Check("Strata 空闲（live 全 null、totals 分母 0、requests 空）-> 六档都取不到",
                    idleMetrics.PrefillAvg is null && idleMetrics.PrefillNow is null
                    && idleMetrics.DecodeAvg is null && idleMetrics.DecodeNow is null
                    && idleMetrics.AcceptAvg is null && idleMetrics.AcceptNow is null);
                var badMetrics = MainForm.ParseStrataMetrics("not json at all");
                Check("格式意外（升级后字段变了）不炸、只回短横",
                    badMetrics.PrefillAvg is null && badMetrics.DecodeNow is null && badMetrics.AcceptNow is null);
                Check("llama 进度行 -> 当前值（预填充取 tokens per second、解码取 tg_3s 的 3 秒窗口）",
                    MainForm.ReadLlamaStatLine("slot  0: prompt processing, n_tokens =   4096, progress = 0.42, t =   3.20 s / 1280.00 tokens per second")
                        is ("prefill", var lp) && Math.Abs(lp - 1280.0) < 0.01
                    && MainForm.ReadLlamaStatLine("slot  0: n_gen =    123, tg =  45.20 t/s, tg_3s =  47.10 t/s")
                        is ("decode", var ld) && Math.Abs(ld - 47.10) < 0.01);
                Check("llama 请求收尾行 -> 累计用的 token 与毫秒（prompt eval time / eval time 分得清）",
                    MainForm.ReadLlamaRequestLine("slot  0: prompt eval time =   1175.10 ms /  70992 tokens (    0.02 ms per token, 60410.00 tokens per second)")
                        is (var pt, var pm, null, null, null, null, null) && pt == 70992 && pm == 1175.10
                    && MainForm.ReadLlamaRequestLine("slot  0:        eval time =   8705.50 ms /    486 tokens (   17.91 ms per token,    55.83 tokens per second)")
                        is (null, null, var dt, var dm, null, null, null) && dt == 486 && dm == 8705.50);
                Check("llama 请求收尾行 -> 草稿分子分母与本轮接受率（1234/1888、0.65432）",
                    MainForm.ReadLlamaRequestLine("slot  0: draft acceptance = 0.65432 (  1234 accepted /   1888 generated), mean len =  1.65")
                        is (null, null, null, null, var ac, var of, var rr)
                    && ac == 1234 && of == 1888 && Math.Abs(rr!.Value - 0.65432) < 1e-5);
                Check("router 转发带的 [pid] 前缀行也认（与日志同一套剥离）",
                    MainForm.ReadLlamaStatLine("[ 5435] slot  0: n_gen = 200, tg = 40.00 t/s, tg_3s = 51.20 t/s")
                        is ("decode", var ldp) && Math.Abs(ldp - 51.20) < 0.01);
                Check("普通日志行不误命中（含 Strata 侧的行与 total time 行）",
                    MainForm.ReadLlamaStatLine("llama_init_from_gguf: model = qwen2") is null
                    && MainForm.ReadLlamaStatLine(spamThink) is null
                    && MainForm.ReadLlamaRequestLine("slot  0:       total time =   1234.00 ms /   1000 tokens")
                        is (null, null, null, null, null, null, null));

                // ---------------- DEC-148：llama 页的实时取数改问 router 的 /metrics ----------------
                // 他实跑点名："llama 端在跑的时候，状态栏文字并不生效，需要等到任务结束才能正常显示"。
                // 根因不在壳的节拍，在数据源：b11530 的 `print_timings_pp()` 开头 `if (t_prompt_total < 3000.0) return;`、
                // `print_timings_tg()` 开头 `if (stats.n_gen < 100) return;`（+3 秒窗口）—— 短任务整段一个字都不打；
                // 而「平均」档吃的 `prompt eval time =` / `eval time =` 只在**请求收尾**时才打 → 只能等任务结束。
                // 新数据源 = `/metrics?model=<在跑那件>&autoload=false`：`is_autoload()` 认这个查询参数，
                // `router_validate_model()` 在"autoload=false 且那件没在跑"时直接回 400、一个进程都不起
                //（本机实测：8078 上问一件 unloaded 的 → 400，之后 `/v1/models` 里它照旧 unloaded）。
                {
                    Check("DEC-148 该问哪一件：选中那件在跑就选它 / 都没跑取在跑的任一件 / 一件没跑 = 空（一个字都不打）",
                        MainForm.LlamaStatsModel(new[] { "b", "a" }, "a") == "a"
                        && MainForm.LlamaStatsModel(new[] { "b", "c" }, "a") == "b"
                        && MainForm.LlamaStatsModel(Array.Empty<string>(), "a") == "");
                    var lmurl = MainForm.LlamaMetricsUrl("127.0.0.1", 8078, "Qwen3.8-27B High");
                    Check("DEC-148 那一发的 URL 带着 autoload=false（这条护栏掉了 = 状态栏每 5 秒把"
                          +"「下拉里选中但那件没在跑」的模型真加载起来）+ 段名转义",
                        lmurl.Contains("&autoload=false", StringComparison.Ordinal)
                        && lmurl.Contains("Qwen3.8-27B%20High", StringComparison.Ordinal), lmurl);
                    string Lm(double pt, double ps, double dt, double ds, double acc, double draft, double def, double cached)
                        => "llamacpp:prompt_tokens_total " + pt + "\nllamacpp:prompt_tokens_cached_total " + cached
                         + "\nllamacpp:prompt_seconds_total " + ps + "\nllamacpp:tokens_predicted_total " + dt
                         + "\nllamacpp:tokens_predicted_seconds_total " + ds
                         + "\nllamacpp:spec_decode_num_accepted_tokens_total " + acc
                         + "\nllamacpp:spec_decode_num_draft_tokens_total " + draft
                         + "\nllamacpp:requests_deferred " + def + "\n";
                    var lc1 = MainForm.LlamaCounters.Parse(Lm(1000, 10, 200, 4, 60, 100, 8, 100));
                    var lp1 = MainForm.LlamaMetricsPack(lc1, null);
                    Check("DEC-148 第一拍只出平均（1000/10s=100、200/4s=50），「当前」档是短横不是 0（没有上一拍）",
                        Math.Abs(lp1.PrefillAvg!.Value - 100) < 0.01 && Math.Abs(lp1.DecodeAvg!.Value - 50) < 0.01
                        && lp1.PrefillNow is null && lp1.DecodeNow is null,
                        $"{lp1.PrefillAvg} | {lp1.PrefillNow} | {lp1.DecodeAvg} | {lp1.DecodeNow}");
                    var lc2 = MainForm.LlamaCounters.Parse(Lm(1400, 14, 260, 5, 90, 150, 8, 100));
                    var lp2 = MainForm.LlamaMetricsPack(lc2, lc1);
                    Check("DEC-148 第二拍差分 = 当前档（400/4s=100、60/1s=60）；队列 8、MTP 90/150=60%、"
                          +"缓存命中 100/1500=6.7%、输入总量含缓存 = 1500（`prompt_tokens_total` 明写 excluding cached）",
                        Math.Abs(lp2.PrefillNow!.Value - 100) < 0.01 && Math.Abs(lp2.DecodeNow!.Value - 60) < 0.01
                        && lp2.Queue == 8 && Math.Abs(lp2.Accept!.Value - 60) < 0.01
                        && Math.Abs(lp2.CacheHitPct!.Value - 6.6667) < 0.01
                        && lp2.TokIn == 1500 && lp2.TokOut == 260,
                        $"{lp2.PrefillNow} | {lp2.DecodeNow} | {lp2.Queue} | {lp2.Accept} | {lp2.CacheHitPct} | {lp2.TokIn}");
                    Check("DEC-148 水表没动 -> 「当前」档短横（不写 0.0 装成卡死，与 Ninfer 那条 DeltaRate 同一个家）",
                        MainForm.LlamaMetricsPack(lc2, lc2).PrefillNow is null
                        && MainForm.LlamaMetricsPack(lc2, lc2).DecodeNow is null);
                    Check("DEC-148 读不到/表外（升级后形状变了）-> 九档全 null、不炸（口径同 DEC-067）",
                        MainForm.LlamaMetricsPack(MainForm.LlamaCounters.Parse("not prometheus at all"), null)
                            == MainForm.EmptyPack);
                    // 旧形状不许回来：`LlamaPack` 把两个累加器直接交出去 = 0 也写一个 0（他实跑抓到的那两条之一）。
                    // needle 拆开拼（BUG-066① 那条先例：整串写在一起会被这条判据自己扫到 = 必假红）。
                    // DEC-153：这里是**单次** CodeHits —— 无源码 = -1（-2 是 DEC-141 那种"两次相加"的形状：
                    // -1 + -1 = -2。**接受集别跨检查抄**：照抄过一版 = 实跑目录恒红，三份 st-deploy 报告点名这条）。
                    var zeroHits = CodeHits("_llamaPromptTok, _llama" + "DecodeTok, null, null, null");
                    Check("DEC-148 旧形状已退出源码（累加器不许直接交出去，0 由画那一头判成短横）",
                        zeroHits is -1 or 0, zeroHits == -1 ? "用例前提：源码不在旁边（实跑目录跑 --selftest 就是这种），这条只登记"
                                                            : $"命中 {zeroHits} 处");
                    var zeroThree = MainForm.StatusBarSegments(
                        new MainForm.StatPack(null, null, null, null, null, 0, null, 0, 12, null, null, null),
                        new[] { "queue", "tokin", "tokout" }, new[] { "queue", "tokin", "tokout" }, Array.Empty<string>());
                    Check("DEC-148 判 0 只有一个家（画那一头，三页共用）；「队列」的 0 是真数（没人排队）照旧写 0",
                        string.Join("·", zeroThree.Select(s => s.Value)) == "0·-·12",
                        string.Join("·", zeroThree.Select(s => s.Value)));
                }

                Check("显隐判据只认开关：开 = 条区+文字立刻在（哪怕没数据/服务没跑），关 = 整条收起",
                    !MainForm.StatusBarVisible(false) && MainForm.StatusBarVisible(true));

                // 9e5. B 段：状态灯四态（纯函数穷举；文案含模式与工作端口）
                var (lst1, lsd1) = MainForm.StrataLight(false, true, true, false, false, true, 8081);
                Check("灯：Strata 运行就绪 -> 绿（运行中 · 模式 · 端口）",
                    lst1 == MainForm.LightState.Green && lsd1 == "运行中 · 路由模式 · 端口 8081", lsd1);
                var (lst2, lsd2) = MainForm.StrataLight(false, false, false, false, true, true, 8081);
                Check("灯：Strata 网关在听、模型没加载 -> 橙（⑥ 统一口径：待机 · 模型未加载；DEC-107 加回「模型」二字）",
                    lst2 == MainForm.LightState.Orange && lsd2 == "待机 · 模型未加载 · 端口 8081", lsd2);
                var (lst3, lsd3) = MainForm.StrataLight(false, false, false, false, false, false, 8080);
                Check("灯：Strata 没在跑 -> 灰（按当前配置的口）", lst3 == MainForm.LightState.Gray && lsd3 == "已停止 · 端口 8080", lsd3);
                var (lst4, lsd4) = MainForm.StrataLight(false, true, false, false, false, true, 8081);
                Check("灯：Strata 启动中（过渡）-> 橙", lst4 == MainForm.LightState.Orange && lsd4 == "启动中 · 端口 8081", lsd4);
                var (lst5, _) = MainForm.StrataLight(false, false, false, true, false, true, 8081);
                Check("灯：Strata 外部服务在跑 -> 绿", lst5 == MainForm.LightState.Green);
                var (lst6, _) = MainForm.StrataLight(true, false, false, false, false, true, 8081);
                Check("灯：Strata 失败 -> 红", lst6 == MainForm.LightState.Red);
                var (llt1, lld1) = MainForm.LlamaLight(false, "", true, false, 8082);
                Check("灯：llama 在跑但模型没加载 -> 橙（与 Strata 同一句话）",
                    llt1 == MainForm.LightState.Orange && lld1 == "待机 · 模型未加载 · 端口 8082", lld1);
                var (llt2, _) = MainForm.LlamaLight(false, "", true, true, 8082);
                Check("灯：llama 有模型加载 -> 绿", llt2 == MainForm.LightState.Green);
                var (llt3, _) = MainForm.LlamaLight(false, "加载中…", true, false, 8082);
                Check("灯：llama 忙态（过渡）-> 橙", llt3 == MainForm.LightState.Orange);
                var (llt4, _) = MainForm.LlamaLight(true, "", false, false, 8082);
                Check("灯：llama 失败 -> 红", llt4 == MainForm.LightState.Red);
                Check("灯色映射：绿=ForestGreen / 橙=DarkOrange / 红=Danger / 灰=Gray",
                    MainForm.LightColor(MainForm.LightState.Green) == Color.ForestGreen
                    && MainForm.LightColor(MainForm.LightState.Orange) == Color.DarkOrange
                    && MainForm.LightColor(MainForm.LightState.Red) == UiTheme.Danger
                    && MainForm.LightColor(MainForm.LightState.Gray) == Color.Gray);

                // 9e6.（原「预设」三条用例）：预设已拆（2026-10-04 DEC-073）——旧 [Preset:] 段的
                // "识别并跳过"由上面 ini round-trip 那条用例覆盖；这里不再有需要断言的东西。

                // 9e3. /models 状态解析（加载/卸载按钮"跟随实际"的数据源）
                var st1 = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var st2 = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                MainForm.ParseModelStates("{\"data\":[{\"id\":\"A\",\"status\":{\"value\":\"loaded\"}},{\"id\":\"B\",\"status\":{\"value\":\"loading\"}},{\"id\":\"C\",\"status\":{\"value\":\"unloaded\"}},{\"id\":\"D\",\"status\":{\"value\":\"failed\"}}]}", st1, st2);
                Check("解析 /models 状态：loaded/loading 计入、failed 单列",
                    st1.SetEquals(new[] { "A", "B" }) && st2.SetEquals(new[] { "D" }),
                    $"loaded={string.Join(",", st1)} failed={string.Join(",", st2)}");
                MainForm.ParseModelStates("not json", st1, st2);
                Check("解析 /models：坏输入清空不炸", st1.Count == 0 && st2.Count == 0);

                // 9e2. 外部服务判据：读起来像 OpenAI 模型列表才算（cc-switch 那种 200 但形状不对的不算）
                Check("外部服务判据：OpenAI 模型列表形状才算",
                    MainForm.LooksLikeOpenAiModelsList("{\"object\":\"list\",\"data\":[{\"id\":\"x\"}]}")
                    && !MainForm.LooksLikeOpenAiModelsList("{\"models\":[{\"name\":\"gpt\"}]}")
                    && !MainForm.LooksLikeOpenAiModelsList("")
                    && !MainForm.LooksLikeOpenAiModelsList("not json"),
                    "cc-switch 的 {\"models\":[…]} 必须判 false");

    }
}
