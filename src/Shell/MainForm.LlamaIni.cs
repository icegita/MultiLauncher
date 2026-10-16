using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MultiLauncher;

// llama 的家底就地读写（DEC-079）：白名单、KV/投机项表、写表单、编辑清单、路径与备份、
// 一次性改名与种子兜底、备份轮转。2026-10-15 从 MainForm.Llama.cs 整段搬出，一行未改。
public partial class MainForm
{
    // ================ 家底就地读写：路径、白名单、备份、写、恢复（DEC-079） ================
    // 取消融合层之后，llama_models.ini 就是 llama 侧**唯一**的一份参数文件：界面改的就是它、引擎读的也是它
    //   （merged.ini 与「用原始 ini 启动」随之退役）。
    // 路径**固定 = exe 同目录那一份**（用户 2026-10-04 拍板：旧值作废、项目 config\ 里那份不入库、也不可由设置页指走），
    //   所以 Settings.RouterIni 退役出 [Current]（读得到、不再写，随下次保存自然消失；先例 [Preset:] DEC-073、LowRam DEC-075）。
    // 三条护栏（落在 LlamaIni.EditSection 里）：只写选中模型那一段、只写白名单里的键、动笔之前必须先备份成功。

    /// <summary>界面上有格子、允许写进模型段的键（白名单外的键壳一个都不写）。</summary>
    internal static readonly string[] LlamaIniKeys =
    {
        "ctx-size", "cache-type-k", "cache-type-v", "flash-attn",
        "temperature", "top-p", "top-k", "min-p", "presence-penalty", "repeat-penalty",
        "spec-draft-n-max", "spec-type", "mmproj-offload", "reasoning-effort",
    };

    /// <summary>壳允许**清掉**的旧拼写键（读侧认过一次的别名；写侧只删不写）。DEC-145 补：`mmproj-offload`
    /// 的正名与旧写 `no-mmproj-offload`（读侧取反）在读侧都认，写侧只写正名 —— 段里残留旧写行时，
    /// "选空白回继承"改的是正名（段里根本没有那一行）= 清不动，所以这一趟顺带把旧写行删掉。</summary>
    internal static readonly string[] LlamaLegacySpellings = { "no-mmproj-offload" };

    /// <summary>写段时放行的键 = 上面 14 键（界面上有格子）+ 允许清掉的旧拼写（只删不写）。
    /// 与 <see cref="LlamaIniKeys"/> 分开：那一份是"界面上有格子"的名单（自检按它断长度），这一份是写侧放行表。</summary>
    internal static readonly string[] LlamaWriteKeys = LlamaIniKeys.Concat(LlamaLegacySpellings).ToArray();

    /// <summary>KV 精度组合下拉的定稿 6 档（交接文档 §2 表；已按用户意见去掉 bf16 / f32）。
    /// Text 与 Tag 同串「k / v」= 界面显示 = Settings.LlamaKv 的存法；**空白项在首**（= 删掉这两键，跟随 `[*]`/引擎默认）。
    /// 表外组合（用户自己在段里写过的）靠 SelectWithFallback 补成一项再选中，不静默丢（DEC-076 口径）。</summary>
    internal static List<DarkItem> LlamaKvItems()
    {
        var items = new List<DarkItem> { new() { Text = "", Tag = "" } };
        foreach (var t in new[] { "f16 / f16", "q8_0 / q8_0", "q8_0 / q4_0", "q8_0 / q5_0", "q4_0 / q4_0" })
            items.Add(new DarkItem { Text = t, Tag = t });
        return items;
    }

    internal static List<DarkItem> LlamaKvItemsForTest() => LlamaKvItems();

    /// <summary>「投机方式」下拉的定稿档（DEC-145）：空白在首（= 删键回继承），常用六档平铺；
    /// 引擎全表还有 ngram-* 一族 —— 表外值（段里手写过的）靠 SelectWithFallback 补成一项再选中，不静默丢。</summary>
    internal static List<DarkItem> LlamaSpecTypeItems()
    {
        var items = new List<DarkItem> { new() { Text = "", Tag = "" } };
        foreach (var t in new[] { "none", "draft-mtp", "draft-dspark", "draft-simple", "draft-eagle3", "draft-dflash" })
            items.Add(new DarkItem { Text = t, Tag = t });
        return items;
    }

    internal static List<DarkItem> LlamaSpecTypeItemsForTest() => LlamaSpecTypeItems();

    /// <summary>「q8_0 / q4_0」拆回两键的值；文件里只写了一半就另一路给空串（同写同删的判据见 LlamaSectionEdits）。</summary>
    internal static (string K, string V) SplitKv(string? display)
    {
        var parts = (display ?? "").Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        string One(int i) => i < parts.Length ? parts[i].Trim() : "";
        return (One(0), One(1));
    }

    internal static string KvDisplay(string k, string v) =>
        k.Length > 0 && v.Length > 0 ? k + " / " + v : (k.Length > 0 ? k : v);

    /// <summary>界面上这一格的值，换算成「要往 ini 里写什么」（空串 = 空白 = 删键）。
    /// 布尔那一档固定写成 true/false（读才宽松：本机文件里 on / True / true 都在用）。</summary>
    internal static string LlamaUiWriteForm(string iniKey, Settings s) => iniKey switch
    {
        "ctx-size" => s.LlamaCtx.Trim(),
        "cache-type-k" => SplitKv(s.LlamaKv).K,
        "cache-type-v" => SplitKv(s.LlamaKv).V,
        "flash-attn" => LlamaIni.BoolText(s.LlamaFlashAttn),
        "temperature" => s.LlamaTemperature.Trim(),
        "top-p" => s.LlamaTopP.Trim(),
        "top-k" => s.LlamaTopK.Trim(),
        "min-p" => s.LlamaMinP.Trim(),
        "presence-penalty" => s.LlamaPresencePenalty.Trim(),
        "repeat-penalty" => s.LlamaRepeatPenalty.Trim(),
        "spec-draft-n-max" => s.LlamaSpecDraftNMax.Trim(),
        "spec-type" => s.LlamaSpecType.Trim(),              // DEC-145：空白 = 删键
        "mmproj-offload" => s.LlamaMmprojOffload.Trim(),    // DEC-145：界面存的就是写入形态（"true"/"false"/""）
        "reasoning-effort" => s.LlamaReasoningEffort.Trim(),
        _ => "",
    };

    /// <summary>每个可写键「当前的生效值」（段 → `[*]`/文件头 → 空），已归一到写入形态。
    /// 这份基线是「继承不摊平」的判据：新值与它一样 = 那个键一个字都不动（交接文档 §2 第 5 条）。</summary>
    internal static Dictionary<string, string> LlamaEffectiveWriteForm(string iniPath, string section)
    {
        var sec = LlamaIni.ReadSectionValues(iniPath, section);
        var glob = LlamaIni.ReadGlobalValues(iniPath);
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var k in LlamaIniKeys)
        {
            if (k == "mmproj-offload") { map[k] = MmprojOffloadWriteForm(sec, glob).Value; continue; }
            var (v, _) = LlamaIni.EffectiveValue(sec, glob, k);
            map[k] = k == "flash-attn" ? LlamaIni.BoolText(LlamaIni.IsTrue(v)) : v;
        }
        return map;
    }

    /// <summary>「视觉」= mmproj-offload 的生效值（写形态："true" = GPU / "false" = CPU / "" = 哪都没写 = 引擎默认 = offload 开）。
    /// **两处两拼写都认**：正名 `mmproj-offload` 与旧写 `no-mmproj-offload`（旧写的值取反 —— 设置页「恢复…」
    /// 端回旧备份时要用）。段里两种都没写、全局写了任一 = 继承。表外值原样带回（界面 SelectWithFallback 兜底显示）。</summary>
    internal static (string Value, bool Inherited) MmprojOffloadWriteForm(
        Dictionary<string, string> sec, Dictionary<string, string> glob)
    {
        static string Conv(string raw, bool invert)
        {
            bool t = LlamaIni.IsTrue(raw), f = LlamaIni.IsFalsey(raw);
            if (!t && !f) return raw.Trim();                 // 表外字：原样带回，不圆谎
            return (invert ? !t : t) ? "true" : "false";
        }
        if (sec.TryGetValue("mmproj-offload", out var sp) && sp.Trim().Length > 0) return (Conv(sp, false), false);
        if (sec.TryGetValue("no-mmproj-offload", out var sn) && sn.Trim().Length > 0) return (Conv(sn, true), false);
        if (glob.TryGetValue("mmproj-offload", out var gp) && gp.Trim().Length > 0) return (Conv(gp, false), true);
        if (glob.TryGetValue("no-mmproj-offload", out var gn) && gn.Trim().Length > 0) return (Conv(gn, true), true);
        return ("", false);
    }

    /// <summary>界面值 vs 生效值 → 该写进段的编辑清单（只有真变了的键才进来；值 = 空串 = 删键）。</summary>
    internal static List<LlamaIni.IniEdit> LlamaSectionEdits(Settings s, Dictionary<string, string> effective)
    {
        var list = new List<LlamaIni.IniEdit>();
        foreach (var k in LlamaIniKeys)
        {
            var ui = LlamaUiWriteForm(k, s);
            var now = effective.TryGetValue(k, out var v) ? v : "";
            if (string.Equals(now, ui, StringComparison.OrdinalIgnoreCase)) continue;   // 没变 = 一个字不动
            list.Add(new LlamaIni.IniEdit(k, ui));
            // DEC-145 补：改 mmproj-offload 时顺带删它的旧拼写行（段里残留 `no-mmproj-offload` 会让
            // "选空白回继承"清不动 —— 改的正名段里没有，旧写行却还在生效）。旧写不存在时 EditSection 不命中 = 一个字节不动。
            if (k == "mmproj-offload")
                foreach (var legacy in LlamaLegacySpellings) list.Add(new LlamaIni.IniEdit(legacy, ""));
        }
        return list;
    }

    // ---------------- 路径与备份 ----------------

    /// <summary>家底的位置：**DEC-118 起 = `&lt;程序目录&gt;\data\config\llama\llama_models.ini`**
    ///（三个模块的配置本体按模块归位，exe 同目录从此只剩壳本体与 MultiLauncher.ini）。
    /// 引擎仍是"被我们喂路径"的那一个 —— `--models-preset` 传的就是 <see cref="RouterIniPath()"/>，所以搬家不动引擎；
    /// 这一点与 ninfer 侧不同（那份引擎根本不 open），两边合起来才使"按模块归位"成立。
    /// 老落点（exe 同目录那一份）由 <see cref="MigrateLauncherLayout"/> 首启一次性搬进来：**Move 不是复制、两边都有不覆盖并点名**。
    /// 文件名 DEC-084 由 `router_models.ini` 改成 `llama_models.ini`（与 `ninfer_models.ini` 对称，
    /// 且 "router_" 会误导 —— 路由是引擎自己的行为，文件名给不了它）；**方法名保留**，它是路径的唯一家。</summary>
    internal const string LlamaIniFileName = "llama_models.ini";
    internal const string LegacyLlamaIniFileName = "router_models.ini";    // 只用于首启一次性搬家与种子兜底查找
    /// <summary>默认落点（真件）= data\config\llama\。自检/探针不许写它，只看这个函数说的是什么。</summary>
    internal static string DefaultRouterIniPath() => Path.Combine(ModuleConfigDir("llama"), LlamaIniFileName);
    internal static string RouterIniPath() => _llamaHomeForTest is null
        ? DefaultRouterIniPath() : Path.Combine(_llamaHomeForTest, LlamaIniFileName);

    /// <summary>DEC-094 ⓪ 的 llama 这一侧：**自检与探针期间整窝改道**（家底 + 它的写前备份都挪进 data\tmp\ 下的副本）。
    /// 与 Strata 侧 data\config\strata\ 那条是同一个病：以前激活 llama 页点「保存」那类用例会走
    /// `BackupRouterIni()` + 就地写 —— 在用户机器上跑一次自检就动一次他的家底、还在 data\llama\ 里留下一份"假历史"。
    /// 备份目录一起改道，`RealRouterIniBackupDir()` 才是真件那一个（自检拿它做"没动过"的对照）。</summary>
    static string? _llamaHomeForTest;
    internal static void UseTempLlamaHomeForTest(string dir) => _llamaHomeForTest = dir;
    internal static bool LlamaHomeOverriddenForTest => _llamaHomeForTest is not null;
    internal static string RealRouterIniBackupDir() => ModuleBackupDir("llama");    // DEC-116：data\backup\llama\（老落点 data\llama\ 由首启搬家带走）

    /// <summary>DEC-084 首启一次性搬家：旧名存在 → **改名（Move）而不是复制**，
    /// 搬前先把这一份存进备份目录（沿用 DEC-079 的 5 份轮转与前缀硬判据），**不留旧名第二份** ——
    /// 否则又回到"两份家底谁生效"那个歧义（DEC-079 消灭融合层就是为了这个）。
    /// 三种结果：<paramref name="renamed"/> = 真的把旧名改成了新名；<paramref name="archived"/> = 新名已经在（引擎与界面都用它），
    /// 旧名那份是历史遗留 → 存进备份目录后删掉，原地不再留第二份；两个都 false = 无事可做。
    /// 新名存在与否永远优先：**绝不覆盖新名那一份**（用户可能已经在上面点过「保存」）。</summary>
    internal static bool MigrateLegacyIniName(string dir, string legacyName, string newName, string backupDir,
                                              string backupPrefix, out bool renamed, out bool archived,
                                              out string? backupName, out string error,
                                              // DEC-134③：这一格里"合起来数 3 份"的那一族前缀（改名之前那批叫
                                              // router_models-*）。不传就只按 backupPrefix 一家数 = 两种前缀并存时
                                              // 老那批永远轮转不掉，与 TrimRouterIniBackups 的口径漂。
                                              string[]? familyPrefixes = null)
    {
        var trim = familyPrefixes ?? new[] { backupPrefix };
        renamed = false; archived = false; backupName = null; error = "";
        var old = Path.Combine(dir, legacyName);
        var now = Path.Combine(dir, newName);
        try
        {
            if (!File.Exists(old)) return true;                       // 没有旧名 = 不用搬家（正常路径）
            if (File.Exists(now))
            {
                // 两份都在：新名那份说了算，旧名那份存进备份再删掉（"不留第二份"这条不许打折）
                if (!BackupFileTo(old, backupDir, backupPrefix, out backupName, out error)) return false;
                File.Delete(old);
                archived = true;
                TrimBackupsIn(backupDir, RouterIniBackupKeep, trim);
                return true;
            }
            // 先存一份再改名：改名失败时旧文件还在原处，用户不会两头空
            if (!BackupFileTo(old, backupDir, backupPrefix, out backupName, out error)) return false;
            File.Move(old, now);
            renamed = true;
            TrimBackupsIn(backupDir, RouterIniBackupKeep, trim);
            return true;
        }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    /// <summary>
    /// 家底到位（**缺才拷，已有就一个字不动**）——DEC-079 把路径从「设置页可配」改成「固定 exe 同目录」，
    /// 老机器上的文件不会自己搬家，所以启动时补这一步。种子按次序：
    ///   ① 旧 ini 里记着的那条路径还指着一份真实文件 → 拷它（那是用户一直在改、引擎一直在读的一份）；
    ///   ② 从 exe 目录往上找仓库里的 `config\llama_models.ini`，再兜一层旧名 `config\router_models.ini`
    ///      （DEC-084 改名前后两份仓库种子都可能在场；开发期 bin / publish 没配 ini 时用它兜底）；
    ///   ③ 都没有 → 不凭空造空文件（宁可让界面可读地报错，也不造一份不像样的家底）。
    /// 只拷不写源文件：种子那份照旧一个字节不动。from 带种子来源给日志与自检交代。
    /// </summary>
    internal static bool EnsureRouterIniPresent(string legacyPath, out string from)
    {
        from = "";
        var dst = RouterIniPath();
        if (File.Exists(dst)) return true;
        legacyPath = (legacyPath ?? "").Trim();
        string? seed = null;
        if (legacyPath.Length > 0 && !SamePath(legacyPath, dst) && File.Exists(legacyPath)) seed = legacyPath;
        if (seed is null)
        {
            try
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                for (int up = 0; up < 5 && dir is not null; up++, dir = dir.Parent)
                {
                    var cand = Path.Combine(dir.FullName, "config", LlamaIniFileName);
                    if (!File.Exists(cand)) cand = Path.Combine(dir.FullName, "config", LegacyLlamaIniFileName);
                    if (File.Exists(cand)) { seed = cand; break; }
                }
            }
            catch { /* 目录读不动就当没种子 */ }
        }
        if (seed is null) return false;
        try
        {
            // DEC-118：新落点那一层可能还不存在 → 先建目录再拷（同 Ninfer 侧那一步，口径一字不差）
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Copy(seed, dst, overwrite: false);
            from = seed;
            return true;
        }
        catch { return false; }
    }

    /// <summary>同一个文件吗（路径写法可能一边带斜杠一边不带）；比不了就按"不是"处理，宁可多拷一次。</summary>
    static bool SamePath(string a, string b)
    {
        try
        {
            return string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'),
                StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>配置备份的目录 = <see cref="RealRouterIniBackupDir"/> 那一个家（DEC-116 起 = data\backup\llama\）。
    /// 自检/探针期间改道进副本窝。这里以前自己拼过一份 data\llama（= 第二个家，DEC-118 扫出来了），现在只认函数。
    /// 备份名一律带 <see cref="RouterIniBackupPrefix"/> 前缀，这样目录里残留的 merged.ini 不会被当成备份列出来。</summary>
    internal static string RouterIniBackupDir() => _llamaHomeForTest is null
        ? RealRouterIniBackupDir()
        : Path.Combine(_llamaHomeForTest, "backups");   // 自检/探针的备份落在副本窝里

    // DEC-084：新备份写新前缀；**清单两种前缀都认** —— 改名之前那几份历史备份不许从「恢复…」里消失。
    // 前缀仍是硬判据：目录里残留的退役 merged.ini 不是备份，既不能列出来也不能被轮转删掉。
    internal const string RouterIniBackupPrefix = "llama_models-";
    internal const string LegacyRouterIniBackupPrefix = "router_models-";
    internal static readonly string[] RouterIniBackupPrefixes = { RouterIniBackupPrefix, LegacyRouterIniBackupPrefix };
    internal const int RouterIniBackupKeep = 3;      // DEC-110⑤：用户口径「所有模块的备份数量调整成 3 份」（原 5 份，§10.1 第④条那一批已按新值改写）

    // 下面三个是"路径进参数"的原件（自检在临时目录上跑轮转与恢复，绝不碰真家底），
    // 上面那三个 RouterIni* 入口只是把它们绑到实跑的位置上。

    /// <summary>备份清单（最新在前）。<paramref name="prefixes"/> 是硬判据：目录里残留的 merged.ini 不能被当成备份列出来。
    /// DEC-084 之后接受多个前缀（新名 + 改名之前的旧名），排序按名字里那段 `yyyyMMdd-HHmmss[-n]` 比 ——
    /// 两种前缀的字典序会把"老前缀"整体排到"新前缀"后面，所以不能再用整体文件名字典序。</summary>
    internal static List<string> ListBackupsIn(string dir, params string[] prefixes)
    {
        if (!Directory.Exists(dir)) return new List<string>();
        try
        {
            var names = new List<string>();
            foreach (var p in prefixes)
                // DEC-110⑤：不再写死 `*.ini` —— 备份的后缀跟着源文件走（引擎 exe 那份是 .exe）。
                // 前缀才是身份判据：同一目录里 `ninfer_models-*`（配置）与 `ninfer-serve-*`（exe）互不相认。
                names.AddRange(Directory.GetFiles(dir, p + "*").Select(Path.GetFileName)
                                         .Where(n => n is { Length: > 0 })!);
            return names.Distinct(StringComparer.Ordinal)
                .OrderByDescending(n => StampOf(n, prefixes), StringComparer.Ordinal)   // 时间戳在名字里 = 字典序就是时间序
                .ThenByDescending(n => n, StringComparer.Ordinal)
                .ToList();
        }
        catch { return new List<string>(); }
    }

    // 取出名字里那段时间戳（把已知前缀与**它自带的后缀**去掉）；取不出来就用整个文件名兜底。
    // DEC-110⑤：后缀不再只有 .ini（exe 备份是 .exe），所以用 Path.GetExtension 而不是砍死 4 个字符。
    static string StampOf(string? name, string[] prefixes)
    {
        var n = name ?? "";
        foreach (var p in prefixes) if (n.StartsWith(p, StringComparison.Ordinal)) { n = n[p.Length..]; break; }
        var ext = Path.GetExtension(n);
        return ext.Length > 0 ? n[..^ext.Length] : n;
    }

    /// <summary>超额收口：只留最近 keep 份，删最旧（返回删掉的份数）。删不动就留着，别把保存流程带崩。</summary>
    internal static int TrimBackupsIn(string dir, int keep, params string[] prefixes)
    {
        int del = 0;
        foreach (var name in ListBackupsIn(dir, prefixes).Skip(keep))
        {
            try { File.Delete(Path.Combine(dir, name)); del++; }
            catch { /* 被占用之类：留着多一份，不影响正确性 */ }
        }
        return del;
    }

    /// <summary>copy 一份备份；同一秒里连着存两次就补序号，**绝不覆盖已有备份**。
    /// DEC-110⑤：**后缀照源文件**（原来写死 `.ini` → 引擎 exe 被存成 `ninfer-serve-<时间戳>.ini`，
    /// 用户实跑抓到的"配置和 exe 混在一起"就是这个字面成因 —— 那是一份 exe 的副本顶着 .ini 的名字）。
    /// 前缀仍是身份判据（`llama_models-` / `ninfer_models-` = 配置，`ninfer-serve-` = 引擎 exe）。</summary>
    internal static bool BackupFileTo(string src, string dir, string prefix, out string? name, out string error)
    {
        name = null; error = "";
        if (!File.Exists(src)) { error = "要备份的文件不在：" + src; return false; }
        try
        {
            Directory.CreateDirectory(dir);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var ext = Path.GetExtension(src);        // 源文件是什么后缀，备份就是什么后缀（.ini / .exe 都照搬）
            var candidate = prefix + stamp + ext;
            for (int n = 2; File.Exists(Path.Combine(dir, candidate)); n++)
                candidate = prefix + stamp + "-" + n + ext;
            File.Copy(src, Path.Combine(dir, candidate), overwrite: false);
            name = candidate;
            return true;
        }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    internal static List<string> ListRouterIniBackups() => ListBackupsIn(RouterIniBackupDir(), RouterIniBackupPrefixes);

    /// 自检用：把同样的清单算式指到任意目录上（临时目录里穷举两种前缀与轮转，绝不碰真家底）
    internal static List<string> ListRouterIniBackupsForTest(string dir) => ListBackupsIn(dir, RouterIniBackupPrefixes);

    internal static int TrimRouterIniBackups() => TrimBackupsIn(RouterIniBackupDir(), RouterIniBackupKeep, RouterIniBackupPrefixes);

    /// <summary>动笔前 copy 一份到 data\backup\llama\（DEC-116 归位；老落点是 `data\llama\`），顺手轮转留 3 份（DEC-110⑤ 由 5 改 3）。</summary>
    internal static bool BackupRouterIni(out string? name, out string error)
    {
        if (!BackupFileTo(RouterIniPath(), RouterIniBackupDir(), RouterIniBackupPrefix, out name, out error)) return false;
        TrimRouterIniBackups();
        return true;
    }

    /// <summary>DEC-134①：**「从备份恢复」专用的那一次备份 = 只 copy、不轮转**（写入路径仍走上面那种"备份+轮转"）。
    /// 理由不是洁癖：恢复的次序是「备份当前 → 复制选中那份 → 收口」，若第二步之前就先轮转，
    /// 那一格满 3 份时被删掉的正是最旧那一份 = 用户刚点名要退回的那一份（复制随即抛 FileNotFoundException，
    /// 而那份原件已经没了 —— BUG-051「自动轮转挤掉用户原件」的第二个入口）。三处恢复（llama ini / ninfer ini /
    /// ninfer exe）都走这一个函数，自检钉的就是它（`SelfTest` 里按回"备份+轮转"会红）。</summary>
    internal static bool BackupBeforeRestore(string src, string dir, string prefix, out string? name, out string error)
        => BackupFileTo(src, dir, prefix, out name, out error);

    /// <summary>启动时把家底安顿好：先做 DEC-084 的**旧名一次性搬家**，再走"缺才拷"（种子 = 旧 ini 里那条已退役的 RouterIni 路径）。
    /// <paramref name="migrated"/> = "renamed" / "archived" / ""（这次真的动了旧名那份才给值，给启动日志那一行用）。</summary>
    internal bool EnsureRouterIni(out string seedFrom, out string migrated, out string migratedBackup)
    {
        migrated = ""; migratedBackup = "";
        // DEC-084：旧名 router_models.ini 还在 → 搬进备份后改名（新名已存在时只归档删掉旧名那份，绝不覆盖新名）
        // 改道期间（自检/探针）整窝都在 data	mp\ 下：搬家也只在那一窝里做，**绝不碰真件**。
        //
        // DEC-138（D-4）：改名以前只在 exe 目录找那一份 —— 而 DEC-118 的搬家 ① 会把**旧名那份一起搬进**
        // `data\config\llama\`，于是搬完没人改名：新名不存在 → 下面「缺才拷」那一步从退役种子又拷一份出来 =
        // 同一目录里躺着两份家底（用户一直在改的那份被晾在旁边，引擎读的是刚拷出来的种子）。
        // 现在两处各走一遍，**新落点在前**（搬家之后的常态），exe 目录在后（搬家没跑成的老形状）。
        // `MigrateLegacyIniName` 本来就是「没有旧名 = 什么都不做、返回 true」，多走一遍零副作用。
        foreach (var home in RouterIniHomeCandidates())
        {
            if (!MigrateLegacyIniName(home, LegacyLlamaIniFileName, LlamaIniFileName,
                                      RouterIniBackupDir(), RouterIniBackupPrefix,
                                      out bool renamed, out bool archived, out var bk, out var migErr,
                                      RouterIniBackupPrefixes))     // DEC-134③：新旧两种前缀合起来数 3 份
            {
                LogL("llama 家底的旧名那份没处理成：" + migErr + "（照旧用现在这一份继续，界面无影响）");
                continue;
            }
            if (renamed || archived)
            {
                migrated = renamed ? "renamed" : "archived";
                migratedBackup = bk ?? "";
                break;      // 只有一份要处理；处理成了就不再往第二处找
            }
        }
        var ok = EnsureRouterIniPresent(_config.Current.RouterIni, out seedFrom);
        _config.Current.RouterIni = "";      // 这个键已退役（路径固定）：清空后它随下一次保存从 [Current] 消失
        return ok;
    }

    /// <summary>DEC-138（D-4）：旧名那份可能住的两个家，顺序就是处理的顺序：
    /// ① 改道窝（自检/探针）或 <c>data\config\llama\</c>（DEC-118 归位后的常态）；
    /// ② exe 目录（搬家那一步没跑成的老形状）。两处都有旧名 = ① 先赢，搬家 ① 本来就是把 ② 那份搬进 ①。</summary>
    internal static List<string> RouterIniHomeCandidates()
    {
        var list = new List<string>();
        var first = _llamaHomeForTest ?? ModuleConfigDir("llama");
        list.Add(first);
        var exe = AppContext.BaseDirectory;
        if (!string.Equals(Path.GetFullPath(first), Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase))
            list.Add(exe);
        return list;
    }

    /// <summary>自检用：上面那两个家算出来（改道状态下只有一个）。D-4 那条用例喂样本时靠它点名落点。</summary>
    internal static List<string> RouterIniHomesForTest() => RouterIniHomeCandidates();

    /// <summary>设置页 llama 节那一枚「打开」：在资源管理器里定位到这份家底（只读位置用「打开」= DEC-062 的口径）。
    /// 就地读写之后它既是引擎读的、也是界面写的同一份，所以**不再需要"打开前先重物化"**
    /// （DEC-077 那一支只对 Strata 侧有意义；llama 侧的落点也改成它，见 Program.ActiveConfigTarget）。</summary>
    internal void RevealRouterIni()
    {
        var p = RouterIniPath();
        if (!File.Exists(p))
        {
            LogL("模型配置不在：" + p + "（把 llama_models.ini 放到程序目录，llama 侧的模型与参数都靠它）");
            Toast.Show(this, "模型配置不在程序目录", Toast.Kind.Warn);
            return;
        }
        RevealInExplorer(p);
        LogL("$ 定位模型配置：" + p);
    }

    /// <summary>
    /// 一次性搬家（DEC-079）：老 ini 的 [Current] 里那 8 个高级项 + 思考深度，以前是靠合并 ini **覆盖每个模型段**
    /// 才生效的；取消融合层之后它们的家就是模型段。所以第一次启动时把非空的旧值补进「当前选中的 llama 模型」那一段：
    /// · 只补该段**没有**的键 —— 段里你手写过的一律不动（谁显式写过谁说了算，壳不拿一个界面旧快照去压它）；
    /// · 不搬 LlamaCtx / LlamaKv —— 那两个以前是"某一段的视图"被顺手存进 [Current] 的，搬它是拿别的段的值冒充；
    /// · 写之前照旧备份；搬完立刻 SaveConfig，这批键已退役出 [Current]，于是旧键随这一次保存自然消失
    ///   （口径同 [Preset:] DEC-073、LowRam DEC-075）。
    /// 代价说明白：旧值原本是"全局覆盖每个模型"的形状，搬进一段之后别的模型就不再跟它走（日志里点名）。
    /// </summary>
    internal int MigrateLlamaParamsIntoSections()
    {
        var tag = _config.Current.LlamaModel.Trim();
        if (tag.Length == 0) return 0;
        var want = new List<LlamaIni.IniEdit>();
        void Add(string key, string val)
        {
            val = (val ?? "").Trim();
            if (val.Length == 0) return;          // 旧值本来就是空的 = 以前也没覆盖，没什么可搬
            want.Add(new LlamaIni.IniEdit(key, val));
        }
        Add("temperature", _config.Current.LlamaTemperature);
        Add("top-p", _config.Current.LlamaTopP);
        Add("top-k", _config.Current.LlamaTopK);
        Add("min-p", _config.Current.LlamaMinP);
        Add("presence-penalty", _config.Current.LlamaPresencePenalty);
        Add("repeat-penalty", _config.Current.LlamaRepeatPenalty);
        Add("spec-draft-n-max", _config.Current.LlamaSpecDraftNMax);
        Add("reasoning-effort", _config.Current.LlamaReasoningEffort);
        if (want.Count == 0) return 0;
        var sec = LlamaIni.ReadSectionValues(RouterIniPath(), tag);
        var keep = want.Where(e => !sec.TryGetValue(e.Key, out var v) || v.Trim().Length == 0).ToList();
        if (keep.Count == 0) return 0;
        if (WriteLlamaSectionEdits(tag, keep, out var err) < 1)
        {
            LogL("旧参数搬家没做成：" + (err.Length > 0 ? err : "没写进去"));
            return 0;
        }
        LogL($"已把老 ini 里 [Current] 的 llama 参数搬进 [{tag}] 段（{keep.Count} 个键：" +
            string.Join(" / ", keep.Select(e => e.Key)) + "）。以前它靠合并 ini 覆盖每个模型，现在只跟这一段 —— " +
            "别的模型要同样生效请在 `[*]` 里写（或各自那段里写）；这些旧键随下一次保存从 MultiLauncher.ini 消失");
        return keep.Count;
    }

    /// <summary>「保存」写段的唯一出口：备份挂在 EditSection 的「动笔前」钩子上，
    /// 于是「值本来就是这样」（继承不摊平）与「备份没做成」两种情况都一个字节碰不到文件。
    /// 返回实际改动的行数（0 = 什么都没写、也没留备份；-1 = 失败，error 是给用户看的原因）。</summary>
    internal int WriteLlamaSectionEdits(string section, IReadOnlyList<LlamaIni.IniEdit> edits, out string error)
        => WriteLlamaSectionEdits(section, edits, out error, out _);

    /// <summary>同上，另把「改了什麼」逐条交出去（detail，形如 <c>top-k 20 → 15</c>）——
    /// 用户 2026-10-04 第 6 条：光报"改动 1 处"不够，日志与提示都要说清改了哪个键、从多少改到多少。</summary>
    internal int WriteLlamaSectionEdits(string section, IReadOnlyList<LlamaIni.IniEdit> edits,
                                       out string error, out string detail)
    {
        error = ""; detail = "";
        if (edits.Count == 0) return 0;
        string? backup = null;
        var rec = new List<LlamaIni.IniChange>();
        bool ok = LlamaIni.EditSection(RouterIniPath(), section, edits, LlamaWriteKeys,
            out int changed, out error,
            _ => BackupRouterIni(out backup, out var berr) ? null : "备份没做成，这次不写：" + berr, rec);
        if (!ok) return -1;
        if (changed == 0) return 0;
        var kept = ListRouterIniBackups().Count;
        detail = string.Join("、", rec.Select(LlamaChangeText));
        LogL($"已写入 [{section}] 段：改动 {changed} 处：{detail}" +
             $"（已备份 {backup}，现有 {kept} 份）");
        return changed;
    }

    /// <summary>一条段级改动说成人话：改值 = <c>键 旧 → 新</c>；这一段原来没有这个键 = <c>键（原来没写）→ 新</c>；
    /// 留空删键 = <c>键 旧 →（删掉，回到继承）</c>。flash-attn 这一格界面上是开关，所以按「开 / 关」报
    /// （文件里写的仍是 true/false，读宽松的口径见 LlamaIni.IsTrue）。</summary>
    internal static string LlamaChangeText(LlamaIni.IniChange c)
    {
        string Show(string? v) => v is null ? ""
            : c.Key.Equals("flash-attn", StringComparison.OrdinalIgnoreCase)
                ? (LlamaIni.IsTrue(v) ? "开" : "关")
            : c.Key.Equals("mmproj-offload", StringComparison.OrdinalIgnoreCase)
                ? (LlamaIni.IsTrue(v) ? "GPU" : "CPU") : v;
        if (c.New is null) return $"{c.Key} {Show(c.Old)} →（删掉，回到继承）";
        if (c.Old is null) return $"{c.Key}（原来没写）→ {Show(c.New)}";
        return $"{c.Key} {Show(c.Old)} → {Show(c.New)}";
    }

    /// <summary>从备份恢复（设置页 llama 节那一枚「恢复…」）。恢复之前会把**当前**那份也存成一份新备份，
    /// 所以"恢复"本身也退得回去；服务在跑时文件换了，要「停止 → 启动」才落到进程（日志与 toast 都点名）。</summary>
    internal void RestoreRouterIniFromBackup()
    {
        var snaps = ListRouterIniBackups();
        if (snaps.Count == 0)
        {
            LogL("还没有配置备份（壳每次要写 llama_models.ini 之前才存一份），没什么可恢复。");
            Toast.Show(this, "还没有配置备份", Toast.Kind.Info);
            return;
        }
        string? pick = snaps.Count == 1 ? snaps[0] : Dialogs.PickSnapshot(this, snaps, 0);
        if (pick is null) return;                      // 取消 = 完全静默（DEC-068）
        if (Dialogs.Confirm(this, "恢复哪一家的配置",
                "会用这份备份覆盖现在的 llama_models.ini：\n" + pick + "\n\n" +
                "你现在文件里的改动会被替换（替换前会先存一份新备份，所以这一步也退得回去）。",
                null, yes: "覆盖", no: "取消", danger: true) != DialogResult.Yes)
            return;
        if (!File.Exists(RouterIniPath()))
        {
            LogL("恢复没做：先找到现在的家底再说（" + RouterIniPath() + " 不在）。");
            Toast.Show(this, "现在的 llama_models.ini 不在，恢复取消", Toast.Kind.Warn);
            return;
        }
        // DEC-134①：恢复前的那一次备份**不许顺手轮转**。原先走 BackupRouterIni（内部就 Trim）= 那一格已满 3 份时，
        // 复制之前先把最旧那一份删了，而人挑来恢复的恰恰常是最旧那一格 → 复制抛 FileNotFoundException、
        // 只报一句"恢复失败"，而他要的那份原件已经没了（BUG-051「程序替他删了退路」的第二个入口，
        // 这次删的是他刚点名要用的那一份）。次序改成：先查那一份还在 → 只 copy 不 trim → 复制成了再收口。
        var bak = Path.Combine(RouterIniBackupDir(), pick);
        if (!File.Exists(bak))
        {
            LogL("恢复没做：列表里那一份已经不在了（" + pick + "）。清单是开窗时读的，中途被收掉过就重新点一次「恢复…」。");
            Toast.Show(this, "那一份备份不在了，恢复取消", Toast.Kind.Warn);
            return;
        }
        if (!BackupBeforeRestore(RouterIniPath(), RouterIniBackupDir(), RouterIniBackupPrefix, out _, out var berr))
        {
            LogL("备份没做成，恢复取消：" + berr);
            Toast.Show(this, "备份没做成，恢复取消", Toast.Kind.Error);
            return;
        }
        try
        {
            File.Copy(bak, RouterIniPath(), overwrite: true);
            TrimRouterIniBackups();      // 复制成了再收口：此刻选中那一份的内容已经在工作文件里了，删它不丢东西
        }
        catch (Exception ex)
        {
            LogL("恢复失败：" + ex.Message);
            Toast.Show(this, "恢复失败：" + ex.Message, Toast.Kind.Error);
            return;
        }
        RefreshModels(LlamaKey);     // 只重扫 llama：这一家底刚从备份恢复回来，另两页不该多出扫描行（DEC-097⑤）
        LoadKnobsFromModel(_config.Current.LlamaModel);
        LogL($"已从备份 {pick} 恢复 llama_models.ini（服务在跑的话要「停止 → 启动」才落到进程）");
        Toast.Show(this, "已恢复：" + pick, Toast.Kind.Ok);
    }
}
