using System.Text.RegularExpressions;

namespace MultiLauncher;

// ============================================================================
// Ninfer 模块的家底层（config\ninfer_models.ini 的那一份，实跑 = exe 同目录）
// ============================================================================
// 与 llama 侧（LlamaIni.cs）的**同与不同**：
//   · 相同：一个段 = 一个模型件；段是壳唯一的写落点；`[*]` / 文件头 / 别的段 / 表外的键 = 永不触碰；
//           写之前必须备份成功（备份钩子挂在 LlamaIni.EditSection 的 onBeforeWrite 上，整套复用）；
//           保真法则一字不改（按字节读、按「一行 + 它自带的行尾」切开再拼回）。
//   · 不同：llama 那边"一格 = 一个 ini 键"，所以段里长出 ctx-size / top-p 这些独立键；
//           **Ninfer 这边一格 = 那条 `args =` 命令行里的对应旗标**。理由是他调优的成品就是一把逐字
//           照 final-*.bat 抄来的 argv（支点 B1：先把壳管理的旗标从模板摘掉，再按界面值追加），
//           把 20 枚旗标摊成 20 个 ini 键反而没人再维护那把模板。
//           于是本族**不读 `[*]`**：一件就是一把完整的 args，不做两级覆盖。
//
// 这里只放**纯函数**（解析、旗标级改写、起前算数、护栏判据），一条 IO 都没有 ——
// 自检可以直接喂字符串穷举，不必碰任何真实文件（写文件的出口在 MainForm.Ninfer.cs）。
internal static partial class NinferIni
{
    // ---------------- 段的形状 ----------------

    /// <summary>身份键：段里写着它们，但它们**不进命令行**（`backend` 决定哪条环、`file` 是位置参数由壳排在最前）。
    /// 摊平之后段里其余的键**就是**旗标：`max-context = 262144` ↔ `--max-context 262144`。</summary>
    internal static readonly string[] IdentityKeys = { "backend", "file" };

    /// <summary>旧形状那一条命令行用的键名（DEC-114 首启迁移把它摊成键；迁移没做成的段照旧按它读写）。</summary>
    internal const string LegacyArgsKey = "args";

    internal sealed class Section
    {
        public string Name = "";       // 段名 = 对外 model-id（客户端填的那个名）
        public string Backend = "";    // win / wsl
        public string File = "";       // 件的路径（win = Windows 路径，wsl = VM 内路径）
        public string Args = "";       // 那一把 argv —— **内部中间表示**（新形状下由 Keys 折出来，旧形状下就是 args 键的值）
        /// <summary>段里全部键，**按文件里的行序**（表外键透传的次序、以及"旗标在命令行里的先后"照的就是它）。
        /// 手工构造的段（纯函数用例）可以不给它，只喂 <see cref="Args"/> 也照旧能用。</summary>
        public List<KeyValuePair<string, string>> Keys = new();

        /// <summary>这一段还是不是旧形状（写着一条 `args =`）。</summary>
        public bool HasLegacyArgs => Keys.Any(k => Eq(k.Key, LegacyArgsKey));
    }

    static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    // ---------------- 键 ↔ argv：文件层与内部中间表示的那一层薄皮（DEC-114 定则 1） ----------------
    // 起前护栏（HostPages / ArgvProblemWin / ValueProblem / RowGranularKv）判的都是**实际那一把命令行**
    //（DEC-099 的口径），argv 这层表示不许退；换格式只动文件层，所以这两对函数是唯一的接缝。

    /// <summary>一条 `args` 命令行摊成键值对。**任何一种认不出的形状都返回错误而不是猜**——
    /// 迁移的护栏就是"任一段解析不出 → 整份文件一个字不动 + 点名卡在哪一段哪一枚"（DEC-114 / 先例 DEC-084）。</summary>
    public static bool ArgvToKeys(string args, out List<KeyValuePair<string, string>> keys, out string error)
    {
        keys = new List<KeyValuePair<string, string>>();
        error = "";
        var toks = Split(args);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < toks.Count; i++)
        {
            var t = toks[i];
            if (!t.StartsWith("--", StringComparison.Ordinal))
            { error = $"第 {i + 1} 个 token「{t}」不是旗标（旧 args 里有位置参数或裸值，摊不成键）"; return false; }
            var name = t[2..];
            if (name.Length == 0 || !System.Text.RegularExpressions.Regex.IsMatch(name, "^[a-z0-9][a-z0-9-]*$"))
            { error = $"旗标名「{t}」不成其为键（只收小写字母、数字与连字符）"; return false; }
            if (IdentityKeys.Contains(name, StringComparer.OrdinalIgnoreCase) || Eq(name, LegacyArgsKey))
            { error = $"旗标「{t}」与段里的身份键撞名（backend / file / args 不写进命令行）"; return false; }
            if (!seen.Add(name)) { error = $"旗标「{t}」在这一段里出现了两次"; return false; }
            if (BoolFlags.Contains(name, StringComparer.OrdinalIgnoreCase))
            { keys.Add(new KeyValuePair<string, string>(name, "true")); continue; }   // 布尔只认在不在 → 固定写 true
            if (i + 1 >= toks.Count || toks[i + 1].StartsWith("--", StringComparison.Ordinal))
            { error = $"旗标「{t}」后面没有值"; return false; }
            keys.Add(new KeyValuePair<string, string>(name, toks[++i]));
        }
        return true;
    }

    /// <summary>键折回命令行：**次序 = 文件里的行序**（所以他自己控的那把顺序不因换格式而变）；
    /// 身份键与 `args` 不进命令行；布尔按假值表决定发不发（DEC-136 C-4）；非布尔而值空 = 整枚不发
    /// （发了就是裸 token = BUG-041 那个 `unknown argument: 1` 的形状）；表外键照旧原样透传。</summary>
    public static string KeysToArgv(IEnumerable<KeyValuePair<string, string>> keys)
    {
        var toks = new List<string>();
        foreach (var kv in keys)
        {
            var name = kv.Key.Trim();
            if (name.Length == 0 || IdentityKeys.Contains(name, StringComparer.OrdinalIgnoreCase) ||
                Eq(name, LegacyArgsKey)) continue;
            var v = (kv.Value ?? "").Trim();
            // DEC-136（C-4）：布尔也**看值**。写侧照 DEC-119 只发旗标 / 删键，但这份家底是他手编的 ——
            // 他按 llama 侧的习惯写 `vision = false`、`lm-head-draft = 0` 完全可能（llama 那一族的布尔读法就是宽松的）。
            // 原先读侧一个字不看值、照样发旗标 = 界面拨到"开"、视觉真的被起（实测约 0.6 GiB 显存），而文件里明写着 false。
            // 收口 = 认假值表：**关掉 = 不发这枚**（与"布尔的关 = 删键"是同一条语义的两种写法）。
            if (BoolFlags.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                if (!IsFalseish(v)) toks.Add("--" + name);
                continue;
            }
            // DEC-136（C-2）：非布尔而值空 = 这枚整条不发。发出去就是一个裸 token —— BUG-041
            // （`unknown argument: 1`）那个坑的镜像形状，而且它还会把后面一枚旗标顶成"它的值"。
            if (v.Length == 0) continue;
            toks.Add("--" + name); toks.Add(v);
        }
        return string.Join(" ", toks);
    }

    /// <summary>布尔那一档的**假值表**（DEC-136 C-4）：与 `LlamaIni.IsTrue` 同一族写法，反过来用。
    /// 空串**不在**表里 = 「这一行写了、没给值」仍然算开（DEC-119 摊平出来的形状是 `lm-head-draft = true`，
    /// 他手写 `lm-head-draft =` 也是"这枚要开"的简写，两种都不该被当成关）。</summary>
    public static bool IsFalseish(string? v) =>
        (v ?? "").Trim().ToLowerInvariant() is "false" or "0" or "no" or "off";

    /// <summary>读全部段（跳过文件头与 `[*]`；注释与空行照 LlamaIni 的判据）。读不动返回空表，别让界面挂掉。</summary>
    public static List<Section> ReadSections(string iniPath)
    {
        var list = new List<Section>();
        if (string.IsNullOrWhiteSpace(iniPath) || !File.Exists(iniPath)) return list;
        foreach (var (name, keys) in LlamaIni.ReadSectionsOrdered(iniPath))
        {
            if (name.Length == 0 || name == "*") continue;
            string Val(string k) => keys.FirstOrDefault(x => Eq(x.Key, k)).Value ?? "";
            var sec = new Section
            {
                Name = name,
                Keys = keys,
                Backend = Val("backend").Trim().ToLowerInvariant(),
                File = Val("file").Trim(),
            };
            // 旧形状 = 段里还写着一条 args；新形状 = 一行一键，argv 由 Keys 折出来。
            sec.Args = sec.HasLegacyArgs ? Val(LegacyArgsKey).Trim() : KeysToArgv(keys);
            list.Add(sec);
        }
        return list;
    }

    // ---------------- 旗标表：界面对得上的那一圈 ----------------
    // 「壳管理的旗标」= 摘除清单，「界面会追加的旗标」= 追加清单，两者必须同一张表（追加 ⊆ 摘除，自检钉）。
    // 布尔旗标没有值，只认在不在；留空/关 = 从 argv 里摘掉。

    /// <summary>带值的旗标（argv 里的原文，不含 --）。</summary>
    internal static readonly string[] ValueFlags =
    {
        "max-context", "kv-dtype", "kv-capacity", "host-kv-mib", "prefill-chunk",
        "max-concurrency", "max-pending-requests", "pending-timeout-ms", "device-state-slots",
        "spec", "draft-tokens", "default-max-tokens", "max-shared-prefixes",
        "temperature", "top-p", "top-k", "presence-penalty", "default-reasoning-effort",
    };

    /// <summary>布尔旗标（只认在不在，没有值）。</summary>
    internal static readonly string[] BoolFlags =
    {
        "lm-head-draft", "vision", "preserve-thinking", "no-thinking",
    };

    /// <summary>由壳定、模板里就算写了也要摘掉的三枚（引擎绑内部口；model-id = 段名）。</summary>
    internal static readonly string[] ShellOwnedValueFlags = { "host", "port", "model-id" };

    /// <summary>包内文档（docs\05-已知问题与禁忌 + 判据）点名的禁用旗标：出现在模板里 = 拒绝启动并说明。
    /// `--disk-cache` 这一代压根不存在（传了 = unknown argument），`--disk-kv-*` 会预分配整块文件（实测落盘 68.6 GB）。</summary>
    internal static readonly string[] BannedFlags =
    {
        "kv-headroom-mib", "adaptive-mtp", "lm-head-q4", "disk-cache",
    };
    internal static readonly string[] BannedPrefixes = { "disk-kv-" };

    // ---------------- argv 的拆与装 ----------------

    /// <summary>把 args 那一条串切成 token（按空白；本族不支持带引号的值 —— 件路径是**位置参数**由壳给，
    /// 不在 args 里，所以不会出现带空格的路径。真有引号也当普通字符带着，不猜）。</summary>
    public static List<string> Split(string? args) =>
        (args ?? "").Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).ToList();

    static string D(string flag) => flag.StartsWith('-') ? flag : "--" + flag;

    /// <summary>取某枚带值旗标的值；没有返回 null（同一枚写了多次取**最后一次** = 引擎的生效次序）。</summary>
    public static string? FlagValue(IReadOnlyList<string> tokens, string flag)
    {
        string? hit = null;
        for (int i = 0; i < tokens.Count - 1; i++)
            if (string.Equals(tokens[i], D(flag), StringComparison.Ordinal)) hit = tokens[i + 1];
        return hit;
    }

    public static bool HasFlag(IReadOnlyList<string> tokens, string flag) =>
        tokens.Any(t => string.Equals(t, D(flag), StringComparison.Ordinal));

    /// <summary>模板里出现过的禁用旗标（原样报回，给用户看是哪一枚）。</summary>
    public static List<string> FindBanned(IReadOnlyList<string> tokens)
    {
        var hits = new List<string>();
        foreach (var t in tokens)
        {
            if (!t.StartsWith("--", StringComparison.Ordinal)) continue;
            var name = t[2..];
            if (BannedFlags.Contains(name, StringComparer.OrdinalIgnoreCase)) hits.Add(t);
            else if (BannedPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase))) hits.Add(t);
        }
        return hits;
    }

    // ---------------- 改写：界面值 ↔ args 串 ----------------

    /// <summary>一条旗标级编辑。Value = null/空白 = 把这枚旗标从 argv 里摘掉；
    /// Bool = true 时 Value 忽略（写了就是开，null 就是关）。</summary>
    internal readonly struct Edit
    {
        public readonly string Flag;
        public readonly string? Value;
        public readonly bool Bool;
        public Edit(string flag, string? value, bool isBool = false) { Flag = flag; Value = value; Bool = isBool; }
    }

    /// <summary>一条**旗标级**改动（给日志与关窗确认框说"改了什麼"用；口径同 LlamaIni.IniChange：
    /// null = 原来没写 / 现在摘掉了）。注意这是 argv 层的明细，文件层写回的仍是整条 args。</summary>
    internal readonly struct Change
    {
        public readonly string Flag;
        public readonly string? Old;
        public readonly string? New;
        public Change(string flag, string? old, string? @new) { Flag = flag; Old = old; New = @new; }
    }

    /// <summary>就地改写：模板里已有的旗标**在原位置换值**（顺序不变），没有的追加到末尾，
    /// 值为空 = 摘掉（布尔旗标同理）。没变化的旗标一个字符不动。changed = 实际改动条数。</summary>
    public static string Rewrite(string args, IReadOnlyList<Edit> edits, out int changed,
                                 List<Change>? details = null)
    {
        changed = 0;
        var tokens = Split(args);
        foreach (var e in edits)
        {
            var want = e.Bool ? "" : (e.Value ?? "").Trim();   // 布尔那一路不看 want（只看 Value 是不是 "1"），给空串免得 nullable 警告
            string? old = e.Bool ? (HasFlag(tokens, e.Flag) ? "开" : null) : FlagValue(tokens, e.Flag);
            bool wantOn = e.Bool && string.Equals((e.Value ?? "").Trim(), "1", StringComparison.Ordinal);

            int idx = IndexOf(tokens, e.Flag, out bool isBool);
            if (e.Bool)
            {
                if (wantOn && idx < 0) { tokens.Add(D(e.Flag)); changed++; details?.Add(new Change(e.Flag, null, "开")); }
                else if (!wantOn && idx >= 0) { RemoveAt(tokens, idx, isBool); changed++; details?.Add(new Change(e.Flag, "开", null)); }
                continue;
            }
            if (want.Length == 0)
            {
                if (idx < 0) continue;                       // 本来就没有 = 没什么可摘
                RemoveAt(tokens, idx, isBool);
                changed++;
                details?.Add(new Change(e.Flag, old, null));
                continue;
            }
            if (idx < 0) { tokens.Add(D(e.Flag)); tokens.Add(want); changed++; details?.Add(new Change(e.Flag, null, want)); }
            else
            {
                // DEC-136（C-2）：这一枚后面**没有值**（旧形状的 args 里手写成裸旗标 `--top-k`）时，
                // 原先直接读 `tokens[idx + 1]` —— 排在段尾就 IndexOutOfRangeException（「保存」在 UI 线程上炸），
                // 后面还跟着别枚就把那枚旗标当值换掉（吞掉一整面旗）。同文件的 `RemoveAt` 一直有这个边界判，
                // 只有 Rewrite 漏了 —— 是这一族的漏项，不是设计。
                bool hasValue = idx + 1 < tokens.Count && !tokens[idx + 1].StartsWith("--", StringComparison.Ordinal);
                if (!hasValue) { tokens.Insert(idx + 1, want); changed++; details?.Add(new Change(e.Flag, old, want)); }
                else if (!string.Equals(tokens[idx + 1], want, StringComparison.Ordinal))
                { tokens[idx + 1] = want; changed++; details?.Add(new Change(e.Flag, old, want)); }
            }
        }
        return string.Join(" ", tokens);
    }

    /// <summary>起服务那条路 = 支点 B1：**先摘掉壳管理的旗标**（= 当前后端有格子那一圈；界面上根本没有的旗标
    /// 照旧留在模板里原样透传 —— 比如 WIN 那条环的 `--max-concurrency 1`，它不给格也就不该被摘，
    /// 摘了等于把"这条环硬拒 mc&gt;1"那条护栏自己拆掉），再按界面上的值追加。
    /// 于是「模板里那把逐字抄的 argv」与「界面上没点保存的手改」共用一套判据，而界面值永远说了算
    ///（追加清单 ⊆ 摘除清单 = 传进来的 managed，自检钉死）。--host / --port / --model-id 恒摘恒加。</summary>
    public static string Build(string args, IReadOnlyCollection<string> managed, IReadOnlyList<Edit> edits,
                               IReadOnlyList<(string Flag, string Value)> shellOwned)
    {
        var tokens = Split(args);
        // 从后往前摘，免得摘掉一枚之后索引全偏（带值旗标一次要摘两个 token）
        for (int i = tokens.Count - 1; i >= 0; i--)
        {
            if (!tokens[i].StartsWith("--", StringComparison.Ordinal)) continue;
            var name = tokens[i][2..];
            bool isBool = BoolFlags.Contains(name, StringComparer.OrdinalIgnoreCase);
            bool shellSet = ShellOwnedValueFlags.Contains(name, StringComparer.OrdinalIgnoreCase);
            bool ours = !isBool && managed.Contains(name, StringComparer.OrdinalIgnoreCase);
            if (!isBool && !shellSet && !ours) continue;         // 界面上没格子的带值旗标 = 原样透传
            if (isBool)
            {
                // DEC-136（C-8）：布尔也得分"这一侧有没有格子"。原先一律摘，于是 WIN 段里写着的 WSL-only 布尔
                // （--vision / --preserve-thinking / --no-thinking）被摘掉之后再没人加回来 ——
                // 文件里还在、命令行没有、界面也没格子能加回去 = 三处各说一话。
                // 带值那一类早就有"没格子就透传"这条规矩（`--max-concurrency 1` 摘了等于自己拆护栏），布尔是同一条规矩漏掉的一半。
                if (!managed.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                tokens.RemoveAt(i); continue;
            }
            tokens.RemoveAt(i);                                  // 带值的连它后面那个 token 一起摘
            if (i < tokens.Count && !tokens[i].StartsWith("--", StringComparison.Ordinal)) tokens.RemoveAt(i);
        }
        foreach (var e in edits)
        {
            var v = (e.Value ?? "").Trim();
            if (e.Bool) { if (v == "1") tokens.Add(D(e.Flag)); continue; }
            if (v.Length == 0) continue;                     // 留空 = 不追加 = 用引擎默认（模板里那枚已被摘掉）
            tokens.Add(D(e.Flag)); tokens.Add(v);
        }
        foreach (var (flag, value) in shellOwned) { tokens.Add(D(flag)); tokens.Add(value); }
        return string.Join(" ", tokens);
    }

    static int IndexOf(List<string> tokens, string flag, out bool isBool)
    {
        isBool = BoolFlags.Contains(flag, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < tokens.Count; i++)
            if (string.Equals(tokens[i], D(flag), StringComparison.Ordinal)) return i;
        return -1;
    }

    /// <summary>摘掉一枚旗标。<paramref name="isBool"/> = false 时连它后面那个值 token 一起摘
    /// （值是裸 token，认不出"值"这个身份，只能靠"下一个 token 不是 -- 开头"判）。</summary>
    static void RemoveAt(List<string> tokens, int idx, bool isBool)
    {
        if (!isBool && idx + 1 < tokens.Count && !tokens[idx + 1].StartsWith("--", StringComparison.Ordinal))
            tokens.RemoveAt(idx + 1);
        tokens.RemoveAt(idx);
    }

}