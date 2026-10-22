using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MultiLauncher;

// A（2026-10-22，方案「Strata 参数拆分 + 手加键覆盖层」§5；DEC-206 / DEC-210）：
// **`_config.ModelSnaps` 的持久化层**从 `MultiLauncher.ini` 的 `[Model:<tag>]` 段搬到
// `data\config\strata\strata_models.ini` —— **一份**，一个模型一段（形状见 `ModelParamsIni`）：
//
//   [swift-iq3_xxs-512k]
//   ApiModel=qwen3.8-flash-next-iq3_s     ← 16 格 = 界面参数，**壳写**（只改这 16 个键对应的行）
//   Context=524288
//   Args1=--上游新旗标                    ← 手加参数，**你写**，壳只读、写回原样保留
//   Args2=1
//
// 为什么是"一份"：原先一个模型最多四个文件（产物 / 伴生 / ui / extra），目录看不下去。
// 为什么是 ini 不是 json（DEC-210，他点名）：JSON 少个逗号整份读不动，而"坏了不写"的纪律会让保存一直失败；
// ini 一行一个键，改错只影响那一行（缺键 = 用默认），手感与另两份家底 ini 一致。
// 为什么不并进产物：产物每次启动按模板重建，拿它当输入 = 自洽循环（方案 §9 的 B 方案），且 SETUP 重跑带不走新参数。
// 代价（他 2026-10-22 明确接受，用"写前备份 + 写后读回校验"兜）：壳必须**只改自己那 16 格**，
// 段里的注释 / `ArgsN` / 他自建的键原样保留；文件读不动就**不写**（保存失败一次，但原件一个字节不动）。
//
// **内存字典 `ModelSnaps` 与全部读点一个字不动**（BuildWakeSettings / ApplyStrataRecord / 保存时写回
// / ModelScan / 启动校正都吃同一个字典）。这一刀只换"字典从哪儿读、往哪儿写"。
//
// 三条纪律（方案 §5 与 §6.1）：
//   ① 写前先备份到 `data\backup\strata\`（留 3 份、名字里的时间戳 = 原件时间、后缀照源文件）；内容没变一个字节都不动；
//   ② 写完**读回校验**：每段都要读回同样的 16 格才算写成功（A1 ①）；
//   ③ 文件赢、老段只读这一次（A6）：ini 里有这段 → 以它为准；不在 → 老 `[Model:<tag>]` 段读一次、写进去。
//      老段**不单独删**：写侧从此不再写 `[Model:*]`（BuildConfigLines），于是下一次成功的保存把整份 ini
//      重写成不含老段的样子 —— 2026-10-22 他拍的口径就是"下次保存成功后自动删"。
//      而"成功"的判据在这里：**文件没写成，这次就不写 ini**（SaveConfig 那一头据此提前返回）。
public partial class MainForm
{
    // DEC-206 那一版的老形态（每模型一份）—— 只在迁移时认它，认完挪去备份
    internal const string ModelUiSuffix = ".ui.json";
    internal const string ModelUiStoreBackupPrefix = "strata_models-";   // 备份名：strata_models-<原件时间戳>.ini（与另两模块同一套字）
    internal const int ModelUiBackupKeep = 3;                       // DEC-110⑤：三模块备份都留 3 份
    /// <summary>DEC-207 那一版的 json 分组键（`ui` / `args`）—— 只在把老 json 并进 ini 时认它，新代码不许再用。</summary>
    internal const string GroupUiKey = "ui";
    internal const string GroupArgsKey = "args";


    static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    /// <summary>模型参数的备份家：`data\backup\strata\`（DEC-116 那一族的模块格）。
    /// A 之后备份**对象**从"壳 ini"换成 `strata_models.ini`（以及并进来的老 `<tag>.ui.json` / `<tag>.extra.json`），
    /// 触发时机没变（写前备份、留 3 份）。自检/探针期间跟着 <see cref="RunConfigDir"/> 一起改道 —— 否则
    /// 跑一次自检就在真实备份格里留下一份"假历史"（DEC-094 ⓪ 与 DEC-111② 是同一个坑）。</summary>
    internal static string ModelUiBackupDir() => _runConfigDirForTest is null
        ? ModuleBackupDir("strata")
        : Path.Combine(_runConfigDirForTest, "backups");

    internal static string ModelUiBackupPrefix(string tag) => tag + "-ui-";

    /// <summary>一个模型那 16 格 → JSON 对象（只装 <see cref="ModelOwnedKeys"/>；服务级字段——端口 / 地址 /
    /// 选中指针——一个都不写，口径与原来那一段 ini 完全一致）。</summary>
    static JsonObject ModelSnapNode(Settings s)
    {
        var o = new JsonObject();
        foreach (var p in ModelOwnedProps)
        {
            var v = p.GetValue(s);
            if (p.PropertyType == typeof(bool)) o[p.Name] = (bool?)v ?? false;
            else o[p.Name] = (string?)v ?? "";
        }
        return o;
    }

    /// <summary>一份快照 → JSON 文本（自检与"记录那一段写全 16 格"那条判据吃它）。</summary>
    internal static string ModelSnapJson(Settings s) => ModelSnapNode(s).ToJsonString(IndentedJson);

    /// <summary>一个模型那一组 → 一份只装着那 16 格的 Settings。表外键忽略、类型不对就整组判坏（不猜、不半份生效）。</summary>
    internal static bool TryParseModelSnapNode(JsonNode? node, out Settings s, out string error)
    {
        s = new Settings(); error = "";
        if (node is not JsonObject o) { error = "不是 JSON 对象"; return false; }
        foreach (var p in ModelOwnedProps)
        {
            if (o[p.Name] is not JsonValue v) continue;      // 缺键 = 用默认值（与 ini 段缺键同义）
            if (p.PropertyType == typeof(bool))
            {
                if (!v.TryGetValue<bool>(out var b)) { error = p.Name + " 不是布尔值"; s = new Settings(); return false; }
                p.SetValue(s, b);
            }
            else
            {
                if (!v.TryGetValue<string>(out var str)) { error = p.Name + " 不是字符串"; s = new Settings(); return false; }
                p.SetValue(s, str);
            }
        }
        return true;
    }

    internal static bool TryParseModelSnapJson(string text, out Settings s, out string error)
    {
        s = new Settings(); error = "";
        JsonNode? node;
        try { node = JsonNode.Parse(text); }
        catch (Exception ex) { error = "不是合法 JSON：" + ex.Message; return false; }
        return TryParseModelSnapNode(node, out s, out error);
    }

    /// <summary>两份快照在那 16 格上是否一致（写后校验用的就是它）。</summary>
    internal static bool ModelSnapEquals(Settings a, Settings b)
    {
        foreach (var p in ModelOwnedProps)
            if (!Equals(p.GetValue(a), p.GetValue(b))) return false;
        return true;
    }

    /// <summary>把磁盘上那份读成行结构（不存在 = null；读不动 = null 并往 notes 写一句）。
    /// 写回时要拿它当基底 —— 只改壳自己那 16 格，注释 / `ArgsN` / 你自建的键原样留着。</summary>
    static ModelParamsIni.Doc? ReadModelParamsDoc(List<string> notes)
    {
        var path = ModelParamsPath();
        if (!File.Exists(path)) return null;
        try { return ModelParamsIni.Parse(File.ReadAllText(path)); }
        catch (Exception ex) { notes.Add($"{ModelParamsFileName} 读不动：{ex.Message}"); return null; }
    }

    /// <summary>读 `strata_models.ini` 里各段的 16 格进字典（**文件赢**，A6）。读不动的段点名跳过、不删别的东西；
    /// 手加参数（`ArgsN`）不在这里处理（那是覆盖层那一头的 <see cref="ReadExtraArgsForTag"/>）。
    /// 返回读到几个模型；<paramref name="fromFiles"/> 非空时把"文件里有的 tag"记进去。</summary>
    internal static int LoadModelSnapStore(Dictionary<string, Settings> snaps, List<string> notes, HashSet<string>? fromFiles = null)
    {
        var doc = ReadModelParamsDoc(notes);
        if (doc is null) return 0;
        int n = 0;
        foreach (var sec in doc.Sections)
        {
            var s = new Settings();
            string error = "";
            foreach (var p in ModelOwnedProps)
            {
                if (!ModelParamsIni.TryGet(sec, p.Name, out var v)) continue;     // 缺键 = 用默认（与老段缺键同义）
                if (p.PropertyType == typeof(bool))
                {
                    if (!TryParseIniBool(v, out var b)) { error = p.Name + " 不是布尔值（" + v + "）"; break; }
                    p.SetValue(s, b);
                }
                else p.SetValue(s, v);
            }
            if (error.Length > 0) { notes.Add($"{ModelParamsFileName} 里「{sec.Name}」那一段读不动，已跳过：{error}"); continue; }
            snaps[sec.Name] = s;
            fromFiles?.Add(sec.Name);
            n++;
        }
        return n;
    }

    /// <summary>ini 里的布尔：`true/false`（壳写的）与 `1/0`、`是/否`、`开/关`（手改的几种写法）都认。</summary>
    internal static bool TryParseIniBool(string text, out bool value)
    {
        var t = (text ?? "").Trim();
        if (t.Equals("1", StringComparison.Ordinal) || t.Equals("是", StringComparison.Ordinal) || t.Equals("开", StringComparison.Ordinal))
        { value = true; return true; }
        if (t.Equals("0", StringComparison.Ordinal) || t.Equals("否", StringComparison.Ordinal) || t.Equals("关", StringComparison.Ordinal))
        { value = false; return true; }
        return bool.TryParse(t, out value);
    }

    /// <summary>把字典整份写回 `strata_models.ini`：**只改每个模型那 16 格对应的行**（缺的键补上），
    /// 段里的注释 / `ArgsN` / 你自建的键一行都不动；内容没变 = 一个字节都不动；
    /// 要写就先备份（留 3 份）→ 同目录 tmp → 改名 → 逐段读回校验。
    /// 文件读不动 → **一个字节都不写**并返回 false（保存失败一次，但不会把你写的东西冲掉）。</summary>
    /// <param name="wrote">真动了盘 = true。</param>
    internal static bool SaveModelSnapStore(Dictionary<string, Settings> snaps, out bool wrote, out string error)
    {
        wrote = false; error = "";
        var path = ModelParamsPath();
        var notes = new List<string>();
        try
        {
            Directory.CreateDirectory(RunConfigDir());
            ModelParamsIni.Doc doc;
            if (File.Exists(path))
            {
                var read = ReadModelParamsDoc(notes);
                if (read is null)
                { error = ModelParamsFileName + " 读不动，这次不写（免得把你手写的内容冲掉）：" + string.Join("；", notes); return false; }
                doc = read;
            }
            else doc = new ModelParamsIni.Doc();
            // 只改自己那 16 格：其余行（注释 / ArgsN / 你自建的键 / 别的模型的段）原样留着
            foreach (var kv in snaps.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                var pairs = new List<KeyValuePair<string, string>>();
                foreach (var p in ModelOwnedProps)
                {
                    var v = p.GetValue(kv.Value);
                    pairs.Add(new(p.Name, p.PropertyType == typeof(bool) ? ((bool?)v ?? false ? "true" : "false") : (string?)v ?? ""));
                }
                ModelParamsIni.SetKeys(doc, kv.Key, pairs);
            }
            var text = ModelParamsIni.Text(doc);
            if (File.Exists(path) && File.ReadAllText(path) == text) return true;      // 已经在盘上 = 不动它
            if (File.Exists(path))
            {
                if (!BackupFileTo(path, ModelUiBackupDir(), ModelUiStoreBackupPrefix, out _, out var berr))
                { error = "备份没做成，这份不写：" + berr; return false; }              // 拒绝即零改动
                TrimBackupsIn(ModelUiBackupDir(), ModelUiBackupKeep, ModelUiStoreBackupPrefix);
            }
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, text, new UTF8Encoding(false));
            File.Move(tmp, path, overwrite: true);
            wrote = true;
            _modelParamsStamp = ModelParamsStamp();      // DEC-211：自己刚写的那一版，别在下次启动时被当成"手改"重读一遍
            // A1①：写完**读回校验** —— 写坏了就不许说"存好了"（挡的是"参数消失且没人立刻发现"）
            var back = new Dictionary<string, Settings>(StringComparer.OrdinalIgnoreCase);
            LoadModelSnapStore(back, new List<string>());
            foreach (var kv in snaps)
            {
                if (!back.TryGetValue(kv.Key, out var b) || !ModelSnapEquals(kv.Value, b))
                { error = "写完读回与内存不一致：「" + kv.Key + "」"; return false; }
            }
            return true;
        }
        catch (Exception ex)
        {
            try { if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp"); } catch { /* 半截临时文件只是垃圾 */ }
            error = ex.Message;
            return false;
        }
    }

    /// <summary>DEC-207 那一版的老存储（`strata_models.json`，组 = `{ui, args}`）并进 ini：
    /// `ui` 块的 16 格 → 该段那些键；`args` 数组 → `Args1..ArgsN`。并完原件挪去备份、目录里不留两份。
    /// ini 里已有的 tag 以它为准（文件赢），老 json 照样进备份 —— 不静默丢弃。返回真正并进来的模型数。</summary>
    internal static int MigrateLegacyParamsJson(Dictionary<string, Settings> snaps, List<string> notes, HashSet<string>? fromFiles = null)
    {
        var path = LegacyModelParamsPath();
        if (!File.Exists(path)) return 0;
        JsonObject? root;
        try { root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject; }
        catch (Exception ex) { notes.Add($"{LegacyModelParamsFileName} 不是合法 JSON（{ex.Message}），没有并进来（原件留着没动）"); return 0; }
        if (root is null) { notes.Add($"{LegacyModelParamsFileName} 的顶层不是 JSON 对象，没有并进来（原件留着没动）"); return 0; }
        int n = 0;
        var doc = ReadModelParamsDoc(new List<string>()) ?? new ModelParamsIni.Doc();
        foreach (var kv in root)
        {
            var tag = kv.Key;
            if (tag.Length == 0) continue;
            if (doc.Find(tag) is not null)
            {
                notes.Add($"「{tag}」在 {ModelParamsFileName} 里已经有一段：以 ini 为准（老的 json 已存进备份）");
                continue;
            }
            var group = kv.Value as JsonObject;
            var pairs = new List<KeyValuePair<string, string>>();
            if (group?[GroupUiKey] is JsonObject ui)
                foreach (var p in ModelOwnedProps)
                {
                    var node = ui[p.Name];
                    if (node is null) continue;
                    pairs.Add(new(p.Name, p.PropertyType == typeof(bool)
                        ? (node.GetValue<bool>() ? "true" : "false")
                        : node.GetValue<string>()));
                }
            if (pairs.Count > 0) ModelParamsIni.SetKeys(doc, tag, pairs);
            if (group?[GroupArgsKey] is JsonArray arr)
                ModelParamsIni.WriteArgs(doc, tag, arr.Select(x => x?.GetValue<string>() ?? "").ToList());
            if (pairs.Count > 0 || group?[GroupArgsKey] is JsonArray)
            {
                snaps[tag] = new Settings();
                foreach (var p in ModelOwnedProps)
                {
                    var hit = pairs.FirstOrDefault(x => x.Key == p.Name);
                    if (hit.Key is null) continue;
                    if (p.PropertyType == typeof(bool)) p.SetValue(snaps[tag], TryParseIniBool(hit.Value, out var b) && b);
                    else p.SetValue(snaps[tag], hit.Value);
                }
                fromFiles?.Add(tag);
                n++;
            }
        }
        try
        {
            Directory.CreateDirectory(RunConfigDir());
            File.WriteAllText(ModelParamsPath(), ModelParamsIni.Text(doc), new UTF8Encoding(false));
        }
        catch (Exception ex) { notes.Add($"并进 {ModelParamsFileName} 失败（老的 json 原件留着没动）：{ex.Message}"); return 0; }
        if (BackupFileTo(path, ModelUiBackupDir(), "strata_models-json-", out _, out _))
        { try { File.Delete(path); } catch { /* 删不掉就留着，下次启动再试 */ } }
        return n;
    }

    /// <summary>把 DEC-206 那一版的老文件并进 `strata_models.ini`（2026-10-22 第二版；第三版 DEC-210 换了形态）：
    /// `<tag>.ui.json` → 该段的 16 个键；`<tag>.extra.json` → 该段的 `Args1..ArgsN`。
    /// 读**完就挪去备份格再删**（只复制不删除：先在备份里留一份），目录里不留两份。
    /// 段已经在 ini 里 → 以 ini 为准（文件赢），老文件照样进备份 —— 不静默丢弃。返回真正并进来的模型数。</summary>
    internal static int ImportLegacyModelUiFiles(Dictionary<string, Settings> snaps, List<string> notes, HashSet<string>? fromFiles = null)
    {
        var dir = RunConfigDir();
        if (!Directory.Exists(dir)) return 0;
        int n = 0;
        foreach (var path in LegacyFiles(dir, "*" + ModelUiSuffix))
        {
            var tag = Path.GetFileName(path)[..^ModelUiSuffix.Length];
            if (tag.Length == 0 || tag.StartsWith("strata-", StringComparison.OrdinalIgnoreCase)) continue;   // 命名约束：不算也不算我们的文件
            Settings parsed = new();
            bool ok;
            try { ok = TryParseModelSnapJson(File.ReadAllText(path), out parsed, out var perr); }
            catch { ok = false; }
            if (ok && !snaps.ContainsKey(tag))
            {
                snaps[tag] = parsed;
                fromFiles?.Add(tag);
                n++;
            }
            else if (ok) notes.Add($"「{tag}」在 {ModelParamsFileName} 与旧的 <模型>.ui.json 里都有：以 {ModelParamsFileName} 为准（旧的已存进备份）");
            else notes.Add($"旧的 <模型>.ui.json 读不动，已挪进备份（{Path.GetFileName(path)}）");
            RetireLegacyFile(path, tag);
        }
        // 老的手加键文件（C 那一版）：并进该段的 ArgsN 行（DEC-210 起 ini 的形态，一行一个 token）
        foreach (var path in LegacyFiles(dir, "*.extra.json"))
        {
            var tag = Path.GetFileName(path)[..^".extra.json".Length];
            if (tag.Length == 0 || tag.StartsWith("strata-", StringComparison.OrdinalIgnoreCase)) continue;
            var warnings = new List<string>();
            var args = ReadExtraOverlay(path, warnings);
            var doc = ReadModelParamsDoc(notes);
            bool hasSection = doc?.Find(tag) is not null;
            if (args.Count > 0 && !hasSection)
            {
                notes.Add($"老的 <模型>.extra.json 里「{tag}」有 {args.Count} 项手加参数，已并进 {ModelParamsFileName} 的 ArgsN 行（原件已存进备份）");
                n++;
            }
            else if (args.Count > 0)
                notes.Add($"「{tag}」在 {ModelParamsFileName} 里已有一段：老的手加参数没有并进去（原件已存进备份，要留就自己抄成 ArgsN）");
            foreach (var w in warnings) notes.Add(w);
            // 并进去了就先写盘再退休（否则挪走旧文件 = 数据只在内存里）
            if (args.Count > 0 && !hasSection)
            {
                var merged = doc ?? new ModelParamsIni.Doc();
                ModelParamsIni.WriteArgs(merged, tag, args);
                try
                {
                    Directory.CreateDirectory(RunConfigDir());
                    File.WriteAllText(ModelParamsPath(), ModelParamsIni.Text(merged), new UTF8Encoding(false));
                }
                catch (Exception ex) { notes.Add($"手加参数并进 {ModelParamsFileName} 失败（原件留在备份里）：{ex.Message}"); continue; }
            }
            RetireLegacyFile(path, tag);
        }
        return n;
    }

    static List<string> LegacyFiles(string dir, string pattern)
    {
        try { return Directory.GetFiles(dir, pattern).ToList(); }
        catch { return new List<string>(); }
    }

    /// <summary>老文件退休：先复制进备份格，成功才删原位（失败就留着，下次启动再试）。</summary>
    static void RetireLegacyFile(string path, string tag)
    {
        if (BackupFileTo(path, ModelUiBackupDir(), ModelUiBackupPrefix(tag), out _, out _))
        { try { File.Delete(path); } catch { /* 删不掉就留着 */ } }
    }

    /// <summary>首启一次性对齐（方案 §5.2）：
    /// ① DEC-207 那一版的 `strata_models.json` 先并进 ini（DEC-210，原件挪备份）；
    /// ② 读 ini（**文件赢**）；③ 把老形态并进来（`<tag>.ui.json` / `<tag>.extra.json`，并完挪去备份）；
    /// ④ 只在老 ini 的 `[Model:&lt;tag&gt;]` 段里有的，写进 `strata_models.ini`。老段一个字不删（只停止写入）——
    /// 下一次成功的保存会把整份 ini 重写成不含老段的样子。
    /// 返回 (从老 ini 搬来的, 从文件读到的, 从老文件并进来的)。</summary>
    internal static (int FromIni, int FromFile, int FromLegacy) ReconcileModelSnaps(
        Dictionary<string, Settings> snaps, List<string> notes)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int fromJson = MigrateLegacyParamsJson(snaps, notes, seen);
        int fromFile = LoadModelSnapStore(snaps, notes, seen);
        int fromLegacy = ImportLegacyModelUiFiles(snaps, notes, seen);
        var pending = snaps.Keys.Where(t => !seen.Contains(t)).ToList();
        if (pending.Count > 0 || fromLegacy > 0)
        {
            if (!SaveModelSnapStore(snaps, out _, out var err))
                notes.Add($"模型参数没写进 {ModelParamsFileName}（这次只在内存里，下次保存再试）：{err}");
        }
        _modelParamsStamp = ModelParamsStamp();     // DEC-211：这一趟已经对齐过，下次启动别再当"手改"重读
        return (pending.Count, fromFile, fromLegacy + fromJson);
    }


    /// <summary>参数文件的"版本戳"（最后写入时间 + 长度）。只用来判"两次启动之间被手改过没有"。</summary>
    internal static string ModelParamsStamp()
    {
        try
        {
            var p = ModelParamsPath();
            if (!File.Exists(p)) return "";
            var fi = new FileInfo(p);
            return fi.LastWriteTimeUtc.Ticks.ToString() + ":" + fi.Length.ToString();
        }
        catch { return ""; }
    }

    // 上一次读入/写出之后记下的版本戳（DEC-211）
    static string _modelParamsStamp = "";
    internal static void ResetModelParamsStampForTest() => _modelParamsStamp = "";

    /// <summary>**手改检测 + 重载**（DEC-211，2026-10-22 他报"改 ini 停止再启动不生效"）。
    /// 拉起服务之前比一次版本戳：文件在两次启动之间被改过 → 重读进字典，返回**这份文件让哪些 tag 变了**
    /// （值变了 / 新增的 / 被删掉的 —— 删掉的那些会从字典里消失，2026-10-23 审查 D 条）。
    /// 文件没动 → 返回空、一个字节都不读（内存里的界面草稿照旧，不许被无谓重读冲掉）。
    /// 为什么必须这一步：内存字典只在启动外壳时读一次文件，之后改文件对它没有任何影响 ——
    /// 而"停止服务 → 再启动"本来只重启引擎，不重读家底。</summary>
    internal static List<string> ReloadModelSnapsIfChanged(
        Dictionary<string, Settings> snaps, List<string> notes, out bool reloaded)
    {
        reloaded = false;
        var stamp = ModelParamsStamp();
        if (stamp.Length == 0 || stamp == _modelParamsStamp) return new List<string>();
        _modelParamsStamp = stamp;
        var fresh = new Dictionary<string, Settings>(StringComparer.OrdinalIgnoreCase);
        LoadModelSnapStore(fresh, notes);
        if (fresh.Count == 0) return new List<string>();      // 读成空 = 判不了（文件不在 / 一段都不成立），这时不动内存
        var changed = new List<string>();
        // 2026-10-23（审查 D 条）：**文件里没有的记录要跟着消失**。以前只增改、不删 ——
        // 手删掉某模型的整段之后，内存里那条还在，下一次落盘又把它写回来（他 2026-10-22 清那 5 份孤儿记录
        // 就是被这一类折腾了两遍）。文件是真相：它不在文件里 = 就是被删了。
        foreach (var gone in snaps.Keys.Where(t => !fresh.ContainsKey(t)).ToList())
        {
            snaps.Remove(gone);
            changed.Add(gone);
        }
        foreach (var kv in fresh)
        {
            if (!snaps.TryGetValue(kv.Key, out var old) || !ModelSnapEquals(old, kv.Value)) changed.Add(kv.Key);
            snaps[kv.Key] = kv.Value;
        }
        reloaded = true;
        return changed;
    }

    /// <summary>**逐模型补齐**（2026-10-22，DEC-209，他点名「按 2 改」）：字典里没有这个 tag 的记录 →
    /// 从它的**配置模板**推一份。补的是"从没点过「保存」"的模型（SETUP 新加的、或建好后直接启动跑的那种）。
    ///
    /// 为什么从模板、不从产物：产物 `run\&lt;tag&gt;.json` 是壳自己按「模板 ⊕ 记录」写出来的**结果**，
    /// 拿它当输入 = 自洽循环（某次物化错了，这个错就被补成正式记录，以后每次唤醒都照它来，
    /// 而文件里看着像你亲手设的）。模板是 SETUP / 用户写的外部输入，没有这个问题。
    ///
    /// 为什么是"缺哪个补哪个"：另一条兜底 <see cref="SeedModelSnapsFromRunConfigs"/> 是 DEC-064 的一次性迁移，
    /// 只在字典**完全为空**时跑一次（`snaps.Count &gt; 0` 直接返回）—— 于是"已有 3 个记录"之后，
    /// 第 4、5 个从没保存过的模型永远补不上（2026-10-22 他报的「只有 3 个模型的配置」就是这个）。
    ///
    /// 一条硬约定：<paramref name="keepArenaPinEmpty"/> 为真时 ArenaPinGib 一律留空 —— 模板里那个
    /// `STRATA_ARENA_PIN_GIB` 分不清是 SETUP 抄的还是用户填的，抄进记录就从「留空 = 沿用模板」变成
    /// 「钉死这个数」，以后改模板不生效（AGENTS 里那条：留空 = 只覆盖不删除）。
    /// <paramref name="filled"/> 收补了哪几个（日志点名用）。返回补的个数。</summary>
    internal static int FillMissingModelSnapsFromTemplates(
        Dictionary<string, Settings> snaps, IEnumerable<string> tags, string root,
        List<string>? filled = null)
    {
        int n = 0;
        foreach (var tag in tags)
        {
            if (tag.Length == 0 || snaps.ContainsKey(tag)) continue;
            var s = new Settings();
            if (!ReadKnobsFromModel(root, tag, s)) continue;   // 模板不在 = 不补（界面照样回落到出厂默认）
            s.ArenaPinGib = "";                               // 硬约定：留空 = 沿用模板里的值
            snaps[tag] = s;
            filled?.Add(tag);
            n++;
        }
        return n;
    }

    /// <summary>DEC-208（2026-10-22）：把**产物**与**引擎伴生文件**从 `data\config\strata\` 顶层搬进 `run\` 子目录
    /// —— 用户手改不到它们（产物每次启动重建、伴生多半被界面盖），顶层只剩 `strata_models.ini` 与手加参数。
    /// 判哪些是产物 = 以 `.json` 结尾、但**不是**壳自己的文件（`strata_models.ini` / 老形态 `.ui.json` / `.extra.json`）。
    /// 规矩照 DEC-084：两边都有不覆盖并点名。返回要打进日志的话。</summary>
    internal static List<string> MigrateProductsIntoRunDir()
    {
        var log = new List<string>();
        var dir = RunConfigDir();
        var runDir = RunProductDir();
        if (!Directory.Exists(dir)) return log;
        string[] files;
        try { files = Directory.GetFiles(dir, "*.json"); }
        catch { log.Add("模型配置目录读不动：" + dir); return log; }
        foreach (var path in files)
        {
            var name = Path.GetFileName(path);
            if (name.Equals(ModelParamsFileName, StringComparison.OrdinalIgnoreCase)) continue;   // 壳自己的参数文件
            if (name.EndsWith(ModelUiSuffix, StringComparison.OrdinalIgnoreCase)) continue;        // 老形态 ui（迁移那一头管）
            if (name.EndsWith(".extra.json", StringComparison.OrdinalIgnoreCase)) continue;        // 老形态手加键（同上）
            var dst = Path.Combine(runDir, name);
            if (File.Exists(dst)) log.Add($"run\\ 搬家：两边都有（{name}），都不动，请自己定去留");
            else
            {
                try
                {
                    Directory.CreateDirectory(runDir);
                    File.Move(path, dst);
                    log.Add($"run\\ 搬家：{name} -> {ModelParamsFileName} 旁的 run\\（Move 不是复制，老位置不留第二份）");
                }
                catch (Exception ex) { log.Add($"run\\ 搬家没成（{name}）：{ex.Message}"); }
            }
        }
        return log;
    }
}
