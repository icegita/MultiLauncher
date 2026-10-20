using System.Diagnostics;
using System.Drawing;
using System.Text;

namespace MultiLauncher;

// Ninfer 的家底（`ninfer_models.ini`）：路径与测试改道、段与生效值、
// 「保存」要写的编辑清单、按段取家底、恢复（整套照 llama 侧三步）。
// 2026-10-15 从 MainForm.Ninfer.cs 整段搬出，一行未改。
public partial class MainForm
{
    // ---------------- 家底与扫描（S1） ----------------

    // ---------------- 路径与测试改道（口径同 RouterIniPath / UseTempLlamaHomeForTest） ----------------

    internal const string NinferIniFileName = "ninfer_models.ini";

    /// <summary>真件默认落点 = `data\config\ninfer\`（DEC-118 按模块归位；这份文件只有壳读，
    /// 引擎不 open 它 —— 取证见 DEC-117，所以搬家零风险，与 llama 那份"引擎按路径 open"是两回事）。
    /// 自检/探针不许写它，只看这个函数说的是什么。</summary>
    internal static string DefaultNinferIniPath() => Path.Combine(ModuleConfigDir("ninfer"), NinferIniFileName);

    static string? _ninferHomeForTest;
    /// <summary>自检/探针期间整窝改道（家底 + 它的写前备份）；真件只读拷进去当用例输入。</summary>
    internal static void UseTempNinferHomeForTest(string dir) => _ninferHomeForTest = dir;
    internal static bool NinferHomeOverriddenForTest => _ninferHomeForTest is not null;
    internal static string NinferIniPath() => _ninferHomeForTest is null
        ? DefaultNinferIniPath() : Path.Combine(_ninferHomeForTest, NinferIniFileName);

    /// <summary>真实备份目录（自检拿它们做"没动过"的对照）。DEC-116 起分两格：
    /// 配置备份 = `data\backup\ninfer\`、引擎 exe 备份 = `data\backup\ninfer-exe\` ——
    /// **不再同目录靠前缀分家**，因为他抓过"把配置和exe混为一起了"（DEC-110⑤），前缀判据照旧留着当第二道。</summary>
    internal static string RealNinferIniBackupDir() => ModuleBackupDir("ninfer");
    internal static string RealNinferExeBackupDir() => ModuleBackupDir("ninfer-exe");

    /// <summary>配置备份 = `data\backup\ninfer\`（与 `data\backup\llama\`、`data\backup\ninfer-exe\`、
    /// `data\backup\strata\` 同层同规则；DEC-124 定案这一层**留在 `data\backup\` 不动**，挪窝的只有 Strata 的整份升级快照 ——
    /// 它去 `D:\Strata-data\backup\`，见 StrataBackupRoot）。
    /// 老落点 `data\infer\` 由 <see cref="MigrateLauncherLayout"/> 按前缀分成两格带走。</summary>
    internal static string NinferBackupDir() => _ninferHomeForTest is null
        ? RealNinferIniBackupDir()
        : Path.Combine(_ninferHomeForTest, "backups");

    /// <summary>引擎 exe 的备份那一格（自检/探针同样改道，落在副本窝的 backups-exe）。</summary>
    internal static string NinferExeBackupDir() => _ninferHomeForTest is null
        ? RealNinferExeBackupDir()
        : Path.Combine(_ninferHomeForTest, "backups-exe");

    internal const string NinferIniBackupPrefix = "ninfer_models-";
    internal const string NinferExeBackupPrefix = "ninfer-serve-";   // 引擎 exe 的备份（更新链用，同一个目录、另一种前缀）
    internal const int NinferBackupKeep = 3;      // DEC-110⑤：配置备份与引擎 exe 备份都留最近 3 份（原 5 份）

    internal static List<string> ListNinferIniBackups() => ListBackupsIn(NinferBackupDir(), NinferIniBackupPrefix);
    internal static int TrimNinferIniBackups() => TrimBackupsIn(NinferBackupDir(), NinferBackupKeep, NinferIniBackupPrefix);

    /// <summary>动笔前 copy 一份到 data\backup\ninfer\（DEC-116 归位；老落点是 `data\infer\`），顺手轮转留 3 份（DEC-110⑤ 由 5 改 3）。</summary>
    internal static bool BackupNinferIni(out string? name, out string error)
    {
        if (!BackupFileTo(NinferIniPath(), NinferBackupDir(), NinferIniBackupPrefix, out name, out error)) return false;
        TrimNinferIniBackups();
        return true;
    }

    /// <summary>家底到位（**缺才拷，已有就一个字不动**）。种子 = 仓库 `config\ninfer_models.ini`
    /// （那份按用户口径不入库，只当开发期/新装机的起点；源文件照旧只读）。
    /// 找不到种子就返回 false —— 界面可读地报错，不凭空造一份不像样的家底（同 llama 侧）。</summary>
    internal static bool EnsureNinferIniPresent(out string from)
    {
        from = "";
        var dst = NinferIniPath();
        if (File.Exists(dst)) return true;
        try
        {
            // DEC-118：新落点那一层可能还不存在（首启、或搬家没跑到），先建目录再拷 —— 不然 File.Copy 抛 = 报"缺家底"，
            // 而实际只是目录没建。
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int up = 0; up < 5 && dir is not null; up++, dir = dir.Parent)
            {
                var cand = Path.Combine(dir.FullName, "config", NinferIniFileName);
                if (!File.Exists(cand)) continue;
                File.Copy(cand, dst, overwrite: false);
                from = cand;
                return true;
            }
        }
        catch { /* 拷不动就当没种子，让调用方报错 */ }
        return false;
    }

    /// <summary>家底缺失时那一句说法的**唯一家**（启动日志一行 + Ninfer 页提示行同一串，别说两套）。
    /// 为什么缺文件要写清落点：种子住在仓库 <c>config\</c>（按用户口径不入库），而本函数找种子是从 exe 目录
    /// 往上找 <c>config\</c> —— 实跑目录 <c>D:\MultiLauncher</c> 上面没有仓库，所以补种这一步归 <c>deploy.ps1</c>（DEC-097①）。</summary>
    internal static string NinferHomeMissingText(string path) =>
        "找不到 ninfer_models.ini —— Ninfer 侧的模型与参数都住在这份文件里，请把它放到：" + path;

    /// <summary>启动时安顿家底（构造函数调；模块停用也照做 —— 停用只是界面剔除，不碰文件）。</summary>
    internal bool EnsureNinferIni(out string seedFrom)
    {
        var ok = EnsureNinferIniPresent(out seedFrom);
        if (!ok)
            LogN(NinferHomeMissingText(NinferIniPath()) + "（跑一次 .\\deploy.ps1 会自动从仓库 config\\ 补种）");
        else if (seedFrom.Length > 0)
            LogN($"{NinferIniFileName} 已就位：" + NinferIniPath() + "（副本取自 " + seedFrom + "，源文件一个字没动）");
        return ok;
    }

    // ---------------- 段与生效值（界面那一圈格子的读口） ----------------

    /// <summary>文件里现在有哪些段（含没启用的后端 —— 下拉那侧再按后端过滤）。</summary>
    internal List<NinferIni.Section> ReadNinferSections() => NinferIni.ReadSections(NinferIniPath());

    /// <summary>界面上每一格「此刻的生效值」= 这一段 args 里的旗标值。
    /// 它是「保存」判"哪些格子真的变了"的基线（同 llama 的 _llamaEffective：值没变就一个字不写），
    /// 也是改完 args 后重新推界面的依据。</summary>
    internal static Dictionary<string, string> NinferEffectiveFromArgs(string args)
    {
        var tokens = NinferIni.Split(args);
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in NinferIni.ValueFlags)
        {
            var v = NinferIni.FlagValue(tokens, f);
            // 排队超时在界面上按**秒**填、argv 里是毫秒：基线也要归一到秒，否则"没改也会被写成一样"
            if (f == "pending-timeout-ms" && v is not null && long.TryParse(v, out var ms)) v = (ms / 1000).ToString();
            map[f] = v ?? "";
        }
        foreach (var f in NinferIni.BoolFlags)
            map[f] = NinferIni.HasFlag(tokens, f) ? "1" : "";
        return map;
    }

    /// <summary>这一枚旗标在当前后端这一页有没有格子。两张表的出处 = 原型页那张「参数名 ↔ argv 旗标对照」
    /// （施工唯一判据源）+ DEC-088「只读那批改成格子，唯一没给格 = WIN 的并发数」：
    /// · **唯一不给格 = WIN 的并发数**（那条环架构级硬拒 mc&gt;1，给格子 = 给陷阱，用户 2026-10-05 明确）；
    /// · 只 WIN：采样四项、主机缓存（WSL 那代**根本没有 --host-kv-mib**，设计稿支点 A 第 136 行）；
    /// · 只 WSL：状态槽数、排队两枚、视觉、保留思考，以及空白思考档对应的 --no-thinking；
    /// · 其余两侧都给格（WIN 是下拉档、WSL 是文本框 —— 同一格位互斥摆两只控件）。
    /// **DEC-107：`--max-shared-prefixes` 两侧都没有格**（他第 1 条把「共享前缀上限」整格去掉）——
    /// 它从此走"表外旗标原样透传"那条路（<see cref="NinferIni.Build"/> 不摘不追加），
    /// 起前护栏照旧判**实际命令行**上有没有 `--max-shared-prefixes 0`（缺了/非 0 = 拒绝启动并说明）。
    /// 表外的旗标（界面上没格）在起服务时**原样透传**，不摘不追加（见 <see cref="NinferIni.Build"/>）。</summary>
    internal static bool FlagShown(string flag, string backend) => flag switch
    {
        "max-shared-prefixes" => false,      // DEC-107：整格去掉（旗标仍从段里透传，护栏仍在）
        "host-kv-mib" or "temperature" or "top-p" or "top-k" or "presence-penalty"
            => backend == NinferIni.BackendWin,
        "max-concurrency" or "max-pending-requests" or "pending-timeout-ms" or "device-state-slots"
            or "vision" or "preserve-thinking" or "no-thinking"
            => backend == NinferIni.BackendWsl,
        _ => true,
    };

    /// <summary>思考深度那一格在 WSL 侧的空白档 = 写 <c>--no-thinking</c>（原型对照表第 18 问那一行）；
    /// WIN 侧空白 = 一个字都不写。所以界面上"空白"在这两代不是同一件事，映射只有这一个家。</summary>
    internal static string NinferNoThinkingValue(Settings s, string backend) =>
        backend == NinferIni.BackendWsl && s.NinferEffort.Trim().Length == 0 ? "1" : "";

    // ---------------- 「保存」要写的编辑清单（界面值 vs 生效值） ----------------

    /// <summary>把界面上那些格子的值按旗标取出来（后端决定哪几格算"这页有的格子"）。</summary>
    internal static string NinferUiValue(string flag, Settings s, string backend) => flag switch
    {
        "max-context" => s.NinferCtx.Trim(),
        "kv-dtype" => s.NinferKvDtype.Trim(),
        "kv-capacity" => s.NinferPool.Trim(),
        "host-kv-mib" => s.NinferHostKib.Trim(),
        "max-concurrency" => s.NinferConcurrency.Trim(),
        "prefill-chunk" => s.NinferPrefill.Trim(),
        "device-state-slots" => s.NinferSlots.Trim(),
        "max-pending-requests" => s.NinferPending.Trim(),
        "pending-timeout-ms" => s.NinferPendingSec.Trim(),
        "spec" => s.NinferSpec.Trim(),
        "draft-tokens" => s.NinferDraft.Trim(),
        "default-max-tokens" => s.NinferMaxTokens.Trim(),
        "temperature" => s.NinferTemperature.Trim(),
        "top-p" => s.NinferTopP.Trim(),
        "top-k" => s.NinferTopK.Trim(),
        "presence-penalty" => s.NinferPresence.Trim(),
        "default-reasoning-effort" => s.NinferEffort.Trim(),
        "lm-head-draft" => s.NinferLmHead ? "1" : "",
        "vision" => s.NinferVision ? "1" : "",
        "preserve-thinking" => s.NinferPreserve ? "1" : "",
        "no-thinking" => NinferNoThinkingValue(s, backend),        // WSL 那侧的空白档 = 写这一枚（WIN 那侧没这枚旗标）
        _ => "",
    };

    /// <summary>界面上这些格子 ↔ argv 旗标（唯一一张映射表；「保存」与「起服务」都从它出发，
    /// 别在两处各写一套 switch —— 表外那一格照样会被静默改掉，就是当年 BUG-032 的形状）。</summary>
    internal static IEnumerable<string> UiFlagsFor(string backend)
        => NinferIni.ValueFlags.Concat(NinferIni.BoolFlags).Where(f => FlagShown(f, backend));

    // —— 同一格位两代各一只控件的那三档（WSL 文本框 / WIN 下拉）：**读界面只有一个家**，
    //    别在 CollectSettings 与 NinferUiValue 两处各写一遍 ternary（口径同 ModelLoadedBadgeFor）——
    string NinferPoolValue()   => ActiveNinferBackend(_config.Current) == NinferIni.BackendWsl ? _txtNinferPoolWsl.Text.Trim()   : _cboNinferPool.Selected?.Tag as string ?? "";
    string NinferDraftValue()  => ActiveNinferBackend(_config.Current) == NinferIni.BackendWsl ? _txtNinferDraftWsl.Text.Trim()  : _txtNinferDraft.Text.Trim();
    string NinferPrefillValue() => ActiveNinferBackend(_config.Current) == NinferIni.BackendWsl ? _txtNinferPrefillWsl.Text.Trim() : _cboNinferPrefill.Selected?.Tag as string ?? "";

    /// <summary>界面值 → 一份编辑清单（只装与"当前生效值"真不一样的；留空 = 摘掉那枚旗标）。
    /// 排队超时在这里换算：界面按秒填、argv 要毫秒。</summary>
    internal static List<NinferIni.Edit> NinferEditsFromUi(Settings s, string backend)
    {
        var list = new List<NinferIni.Edit>();
        foreach (var f in UiFlagsFor(backend))
        {
            var isBool = Array.IndexOf(NinferIni.BoolFlags, f) >= 0;
            var v = NinferUiValue(f, s, backend);
            if (f == "pending-timeout-ms" && v.Length > 0 && long.TryParse(v, out var sec)) v = (sec * 1000).ToString();
            list.Add(new NinferIni.Edit(f, v, isBool));
        }
        return list;
    }

    /// <summary>与生效基线比一遍，只留真变了的（「继承不摊平」在这一族的对应物 = 「模板没写的旗标不硬摊上界面值」：
    /// 界面上那一格与 args 里那枚一样就一个字符不动）。比的是**界面口径**的值（排队超时按秒），
    /// 写出去的值才是 argv 口径（毫秒）—— 换算只在一处发生。</summary>
    internal static List<NinferIni.Edit> NinferSectionEdits(Settings s, string backend, Dictionary<string, string> effective)
    {
        var list = new List<NinferIni.Edit>();
        foreach (var e in NinferEditsFromUi(s, backend))
        {
            var now = effective.TryGetValue(e.Flag, out var v) ? v : "";      // 基线存的已是界面口径（秒 / "1" / 值）
            var ui = NinferUiValue(e.Flag, s, backend);
            if (Array.IndexOf(NinferIni.BoolFlags, e.Flag) >= 0)
            {
                if ((ui == "1") == (now == "1")) continue;                     // 开关状态没变 = 一个字不动
                list.Add(e);
                continue;
            }
            if (string.Equals(now, ui, StringComparison.OrdinalIgnoreCase)) continue;
            list.Add(e);
        }
        return list;
    }

    /// <summary>关窗确认框用的明细：界面上与生效值真不一样的那些旗标，逐条说成人话（口径同 llama 那一路）。
    /// 要人决策的地方不能只给旗标名（DEC-082 第 6 条）。</summary>
    internal static List<string> NinferPendingChanges(Settings s, string backend, Dictionary<string, string> effective)
    {
        var list = new List<string>();
        foreach (var e in NinferSectionEdits(s, backend, effective))
        {
            var isBool = Array.IndexOf(NinferIni.BoolFlags, e.Flag) >= 0;
            var now = effective.TryGetValue(e.Flag, out var v) ? v : "";      // 基线 = 界面口径（秒）
            var ui = NinferUiValue(e.Flag, s, backend);                        // 新值也取界面口径，别拿 e.Value（那是 argv 口径的毫秒）
            string? Old() => isBool ? (now == "1" ? "开" : null) : (now.Length > 0 ? now : null);
            string? New() => isBool ? (ui == "1" ? "开" : null) : (ui.Length > 0 ? ui : null);
            list.Add(NinferChangeText(new NinferIni.Change(e.Flag, Old(), New())));
        }
        return list;
    }

    /// <summary>argv 口径 → 界面口径。全族只有排队超时这一枚两边单位不同（文件里毫秒、界面上秒），
    /// 所以这条换算也只有这一个家：<see cref="NinferEffectiveFromArgs"/> 管基线、
    /// <see cref="NinferEditsFromUi"/> 管写出去、这里管"把 argv 层的改动翻回界面口径给明细用"。</summary>
    internal static string NinferArgvToUi(string flag, string? v) =>
        flag == "pending-timeout-ms" && long.TryParse((v ?? "").Trim(), out var ms) ? (ms / 1000).ToString() : (v ?? "");

    /// <summary>一条旗标级改动说成人话（口径同 LlamaChangeText：只报键名不算落点）。
    /// 布尔按界面上的「开 / 关」报；**传进来的 Old/New 一律按界面口径**（排队超时 = 秒），
    /// 所以这里只补单位、不再换算 —— 两个调用点各自负责翻一次（见 <see cref="NinferArgvToUi"/>）。</summary>
    internal static string NinferChangeText(NinferIni.Change c)
    {
        bool isBool = Array.IndexOf(NinferIni.BoolFlags, c.Flag) >= 0;
        string Show(string? v) => v is null ? ""
            : isBool ? (v == "1" || v == "开" ? "开" : "关")
            : c.Flag == "pending-timeout-ms" && v.Trim().Length > 0 ? v.Trim() + " 秒" : v;
        var name = c.Flag.TrimStart('-');
        if (c.New is null) return $"{name} {Show(c.Old)} →（摘掉这枚旗标）";
        if (c.Old is null) return $"{name}（模板里原来没写）→ {Show(c.New)}";
        return $"{name} {Show(c.Old)} → {Show(c.New)}";
    }

    /// <summary>「保存」里 Ninfer 那一步：把与生效值真不一样的旗标就地写进选中那一段的 args。
    /// 三态同 llama（Nothing / Wrote / Failed —— "没东西要写"与"写失败"必须分得开，DEC-092）。</summary>
    internal SegWrite SaveNinferSectionEdits(out string detail)
    {
        detail = "";
        var s = _config.Current;
        var tag = s.NinferModel.Trim();
        if (tag.Length == 0)
        {
            LogN("还没选中 Ninfer 模型，这些参数没处写（只存了软件自己的设置）。");
            return SegWrite.Failed;
        }
        var backend = NinferBackendOf(s, tag);
        var edits = NinferSectionEdits(s, backend, _ninferEffective);
        if (edits.Count == 0) return SegWrite.Nothing;
        if (WriteNinferSectionEdits(tag, edits, out var err, out detail) < 0)
        {
            LogN("写 ninfer_models.ini 失败：" + err);
            Toast.Show(this, "没写进配置文件：" + err, Toast.Kind.Error);
            detail = "";
            return SegWrite.Failed;
        }
        ApplyNinferRecord(tag);       // 重读这一段：界面、生效值基线与脏判定都和文件对齐
        return SegWrite.Wrote;
    }

    /// <summary>这一段的后端归属：段里写的 backend 说了算（界面上切的是"当前在用哪侧"，
    /// 而选中那一段属于哪侧是家底事实 —— 两边不一致时按段走，扫描那侧也会把它滤掉）。</summary>
    internal string NinferBackendOf(Settings s, string tag)
    {
        var sec = ReadNinferSections().FirstOrDefault(x => string.Equals(x.Name, tag, StringComparison.OrdinalIgnoreCase));
        return SecBackend(sec) ?? s.NinferBackend;
    }

    internal static string SecBackend(NinferIni.Section? sec) =>
        sec is not null && (sec.Backend == NinferIni.BackendWin || sec.Backend == NinferIni.BackendWsl) ? sec.Backend : null!;

    /// <summary>写段唯一出口：只写 `args` 这一个键（白名单外一律拒绝），备份挂在动笔前钩子上 ——
    /// 于是「旗标本来就一样」与「备份没做成」两种情况都一个字节碰不到文件。</summary>
    internal int WriteNinferSectionEdits(string section, IReadOnlyList<NinferIni.Edit> edits, out string error)
        => WriteNinferSectionEdits(section, edits, out error, out _);

    internal int WriteNinferSectionEdits(string section, IReadOnlyList<NinferIni.Edit> edits, out string error, out string detail)
    {
        error = ""; detail = "";
        if (edits.Count == 0) return 0;
        var secs = ReadNinferSections();
        var sec = secs.FirstOrDefault(x => string.Equals(x.Name, section, StringComparison.OrdinalIgnoreCase));
        if (sec is null) { error = "文件里没有 [" + section + "] 这一段（壳不新建段）：" + NinferIniPath(); return -1; }
        // 先在 argv 层算一遍：改了哪几枚、整条命令行变成什么（护栏与明细都从这一层出，DEC-099 的口径不退）
        var changes = new List<NinferIni.Change>();
        var newArgs = NinferIni.Rewrite(sec.Args, edits, out int flagChanges, changes);
        if (flagChanges == 0 || newArgs == sec.Args) return 0;         // 一个字符都不用变：不写文件、也不留无谓备份

        // 文件层写什么（DEC-114 定则 1：argv 只是内部表示，换格式只动这一层）：
        //   · 段里还写着一条 `args`（迁移没做成的那类段）→ 照旧整行写回，读侧两种形状都认；
        //   · 新形状（一行一键）→ 摊成键逐条写：argv 里有的写成那个值、段里有而 argv 里没的**删键**
        //     （= 界面上留空 = 把这枚旗标摘掉，同 llama 侧"留空 = 从这一段删掉这个键"）。
        List<LlamaIni.IniEdit> fileEdits;
        List<string> allow;
        if (sec.HasLegacyArgs)
        {
            fileEdits = new List<LlamaIni.IniEdit> { new(NinferIni.LegacyArgsKey, newArgs) };
            allow = NinferWriteKeys.ToList();
        }
        else
        {
            if (!NinferIni.ArgvToKeys(newArgs, out var newKeys, out var convErr))
            {
                error = "这一把命令行摊不成键，没写文件：" + convErr;
                return -1;
            }
            fileEdits = newKeys.Select(k => new LlamaIni.IniEdit(k.Key, k.Value)).ToList();
            foreach (var old in sec.Keys)
            {
                bool identity = NinferIni.IdentityKeys.Contains(old.Key, StringComparer.OrdinalIgnoreCase)
                                || NinferIni.ShellOnlyKeys.Contains(old.Key, StringComparer.OrdinalIgnoreCase);   // DEC-187
                bool legacy = string.Equals(old.Key, NinferIni.LegacyArgsKey, StringComparison.OrdinalIgnoreCase);
                if (identity || legacy) continue;                       // 身份键 / 壳自己的键与 args 壳不写（args 归上面那个分支）
                if (!newKeys.Any(n => string.Equals(n.Key, old.Key, StringComparison.OrdinalIgnoreCase)))
                    fileEdits.Add(new LlamaIni.IniEdit(old.Key, ""));   // 空值 = 从这一段删掉这个键
            }
            allow = fileEdits.Select(e => e.Key.Trim()).ToList();       // 只放行这一次真要用到的键名
        }

        string? backup = null;
        bool ok = LlamaIni.EditSection(NinferIniPath(), section, fileEdits, allow,
            out int changed, out error,
            _ => BackupNinferIni(out backup, out var berr) ? null : "备份没做成，这次不写：" + berr);
        if (!ok) return -1;
        if (changed == 0) return 0;
        var kept = ListNinferIniBackups().Count;
        // argv 层报回来的是文件里那套值（毫秒），明细要说界面听得懂的话 → 在这里翻一次（见 NinferArgvToUi）
        detail = string.Join("、", changes.Select(c => NinferChangeText(new NinferIni.Change(
            c.Flag, NinferArgvToUi(c.Flag, c.Old), NinferArgvToUi(c.Flag, c.New)))));
        LogN($"已写入 [{section}] 段：改动 {changes.Count} 处：{detail}（已备份 {backup}，现有 {kept} 份）");
        return changes.Count;
    }

    /// <summary>文件层白名单（**旧形状兜底**用）：段里那条 `args` 是迁移没做成时的写落点。
    /// 摊平之后（DEC-114）白名单改成"这一次真要用到的旗标键名"，在 <see cref="WriteNinferSectionEdits"/> 里按次算；
    /// `backend` / `file` / 段名**任何形状下都不写**。</summary>
    internal static readonly string[] NinferWriteKeys = { "args" };

    /// <summary>DEC-114 首启一次性迁移：段里还写着 `args` 的 → 摊成"一行一键"（先例 = DEC-084 的改名）。
    /// 三条护栏与 llama 侧一字不差：**先备份、拒绝即零改动、只写选中那一段**。
    /// 次序要紧：<b>先把所有段解析并校验一遍，全过了才动笔</b> —— 任一段认不出（裸 token、旗标缺值、
    /// 名字不成键、同一枚写两次、摊出来的键与 `backend`/`file` 撞名）就整份一个字不动，
    /// 并把卡住的段与那一枚说给用户看（不猜、不修一半）。
    /// 万一动笔途中 IO 失败：读侧两种形状都认（<see cref="NinferIni.Section.HasLegacyArgs"/>），
    /// 所以家底仍读得出来、界面仍跑得动，日志会说清哪段没迁成 —— 不留一份"读不出"的家底。</summary>
    internal static int MigrateNinferIniToKeys(out string error, out string detail)
    {
        error = ""; detail = "";
        var path = NinferIniPath();
        if (!File.Exists(path)) return 0;

        var plan = new List<(string Name, List<LlamaIni.IniEdit> Edits, int Keys)>();
        foreach (var (name, keys) in LlamaIni.ReadSectionsOrdered(path))
        {
            if (name.Length == 0 || name == "*") continue;
            var argsLine = keys.FirstOrDefault(k =>
                string.Equals(k.Key, NinferIni.LegacyArgsKey, StringComparison.OrdinalIgnoreCase));
            if (argsLine.Value is null) continue;                  // 这一段没有 args = 已经是新形状
            if (!NinferIni.ArgvToKeys(argsLine.Value, out var flat, out var why))
            { error = $"段 [{name}]：" + why; return -1; }
            var clash = flat.Select(f => f.Key)
                .Where(k => keys.Any(x => !string.Equals(x.Key, NinferIni.LegacyArgsKey, StringComparison.OrdinalIgnoreCase)
                                       && string.Equals(x.Key, k, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (clash.Count > 0)
            { error = $"段 [{name}]：摊出来的键与段里已有的键撞名（{string.Join("、", clash)}）"; return -1; }
            var ed = new List<LlamaIni.IniEdit> { new(NinferIni.LegacyArgsKey, "") };     // 空值 = 删掉 args 那一行
            ed.AddRange(flat.Select(f => new LlamaIni.IniEdit(f.Key, f.Value)));
            plan.Add((name, ed, flat.Count));
        }
        if (plan.Count == 0) return 0;

        // DEC-121：**整次迁移只备份一份**（以前挂在每段的写前钩子上 = 三段三份，同一秒还分出 `-2` / `-3`），
        // 那几份中间态会把「各留最近 3 份」的保留位占满、把他真那份老备份挤掉 ——
        // 实跑抓到的后果：删注释前那份 3,258 字节的原件（`ninfer_models-20261008-231635.ini`）就是这么被顶出清单的。
        // 界面「保存」那条路照旧"一次写一次备份"，两者语义不同：**迁移是一件事，不是三次改动**。
        if (!BackupNinferIni(out var migBackup, out var berr))
        { error = "备份没做成，摊平一个字都没动：" + berr; return -1; }
        int done = 0, written = 0;
        foreach (var (name, ed, n) in plan)
        {
            var allow = ed.Select(e => e.Key.Trim()).ToList();
            // 钩子用**有返回类型的局部函数**（`string?` 写明）：内联 lambda 时编译器会从 body 推返回类型，
            // 那句 `return null` 就被当成"给非空 string 赋 null"报 CS8600（369 行那一处不报，因为参数位上有目标类型）。
            string? AlreadyBackedUp(string _) => null;      // 上面已经备份过整份，途中不再重复（这条路径 = 一次迁移一份备份）
            if (!LlamaIni.EditSection(path, name, ed, allow, out int changed, out error, AlreadyBackedUp))
            { error = $"段 [{name}] 没写成：" + error; return done; }
            if (changed > 0) { done++; written += n; }
        }
        detail = $"{done} 段摊成 {written} 个键（整份先备份了一份 " + migBackup + "）";
        return done;
    }

    // ---------------- 按段取家底（界面推值） ----------------

    /// <summary>读那一段的 args 把界面格子填上，并记下"生效值基线"（给「保存」判脏）。
    /// 段不在 = 返回 false，界面保持原样（拿别的段的值冒充 = DEC-064 在 Strata 侧踩过的坑）。
    /// 只读不写：写段是「保存」那一条路的活。</summary>
    /// <summary>段 → 界面载体那 20 格的**唯一回填算式**（DEC-135 拍板①）。两条路共用它：
    /// · <see cref="ApplyNinferRecord"/> = 换模型 / 启动校正，起点是界面上那份卡；
    /// · <see cref="BuildNinferWakeSettings"/> = **远端唤醒**，起点是 `_config.Current`（磁盘那一份），
    ///   于是"看一眼别的件"留下的格子不参与起来这一件（与 Strata 侧 DEC-122① 同一条口径）。
    /// 返回段里的生效值表（调用方拿去当 `_ninferEffective` 的基线）。</summary>
    internal static Dictionary<string, string> ApplyNinferSectionTo(NinferIni.Section sec, Settings s)
    {
        var eff = NinferEffectiveFromArgs(sec.Args);
        void SetV(string flag, Action<string> put)
        {
            // **基线已经是界面口径**（`NinferEffectiveFromArgs` 里那枚 pending-timeout-ms 已经除过 1000），
            // 这里再除一次就是 DEC-097 抓到的那个 0：WSL 段的 600000ms → 基线 600 → 界面 0 →
            // 「保存」被 `>= 1` 那条判据当场拒掉（WSL 那一侧永远存不下去）。换算只准有一处发生。
            put(eff.TryGetValue(flag, out var e) ? e : "");
        }
        SetV("max-context", v => s.NinferCtx = v);
        SetV("kv-dtype", v => s.NinferKvDtype = v);
        SetV("kv-capacity", v => s.NinferPool = v);
        SetV("host-kv-mib", v => s.NinferHostKib = v);
        SetV("max-concurrency", v => s.NinferConcurrency = v);
        SetV("prefill-chunk", v => s.NinferPrefill = v);
        SetV("device-state-slots", v => s.NinferSlots = v);
        SetV("max-pending-requests", v => s.NinferPending = v);
        SetV("pending-timeout-ms", v => s.NinferPendingSec = v);
        SetV("spec", v => s.NinferSpec = v);
        SetV("draft-tokens", v => s.NinferDraft = v);
        SetV("default-max-tokens", v => s.NinferMaxTokens = v);
        SetV("temperature", v => s.NinferTemperature = v);
        SetV("top-p", v => s.NinferTopP = v);
        SetV("top-k", v => s.NinferTopK = v);
        SetV("presence-penalty", v => s.NinferPresence = v);
        SetV("default-reasoning-effort", v => s.NinferEffort = v);
        s.NinferLmHead = eff.TryGetValue("lm-head-draft", out var lm) && lm == "1";
        s.NinferVision = eff.TryGetValue("vision", out var vi) && vi == "1";
        s.NinferPreserve = eff.TryGetValue("preserve-thinking", out var pr) && pr == "1";
        s.NinferModel = (sec.Name ?? "").Trim();
        return eff;
    }

    /// <summary>DEC-135（拍板①）：Ninfer 侧的「唤醒按什么取参」= **要起那一段自己**，不读界面卡。
    /// 根因与 Strata 那次一模一样（DEC-122① 的背景②）：换下拉是一个"看一眼"的动作、会把所看那件载上卡
    /// （`ApplyNinferRecord`），而这一族的界面格子就是起进程时的追加清单 —— 于是请求 A 会带着 B 的
    /// 上下文 / KV / 采样值起来。起点用 `_config.Current`（磁盘那份）而不是 `CollectSettings()`（含草稿），
    /// 于是「保存」= 让远端用上新参数的唯一动作，与 Strata、llama 两族同一句话。
    /// 段不存在时**不猜**：原样交回（`NinferModel = tag`），下面"没有这一件"那条判据会拒启并说清。</summary>
    internal Settings BuildNinferWakeSettings(string tag)
    {
        var s = _config.Current.Clone();
        tag = (tag ?? "").Trim();
        var sec = ReadNinferSections().FirstOrDefault(x => string.Equals(x.Name, tag, StringComparison.OrdinalIgnoreCase));
        if (sec is null) { s.NinferModel = tag; return s; }
        ApplyNinferSectionTo(sec, s);
        // DEC-157（他批的"走段"）：**后端归属也按段自己那一侧**（`backend = win|wsl` 是段的身份键之一）。
        // 以前这一句没有 → `ActiveNinferBackend(s)` 兜到界面上那枚方块 → 请求要的是另一侧那件时当场被拒
        //（"这一段不属于当前后端"+ 模态弹窗，他 2026-10-12 实跑抓的），而网关 `/v1/models` 本来就两侧全列、
        // 占用估算与显存账也早就按段取数了（DEC-156）= 同一件事两套判据。
        // 那一侧被人在设置页关掉时 `ActiveNinferBackend` 会兜回还开着的那一侧，由 StartNinferServer 里那道
        // 「段与生效后端不一致」当场说清（不是静默换一条链起）。
        var own = SecBackend(sec);
        if (own.Length > 0) s.NinferBackend = own;
        return s;
    }

    internal bool ApplyNinferRecord(string tag)
    {
        tag = (tag ?? "").Trim();
        if (tag.Length == 0) return false;
        var sec = ReadNinferSections().FirstOrDefault(x => string.Equals(x.Name, tag, StringComparison.OrdinalIgnoreCase));
        if (sec is null) return false;
        var backend = SecBackend(sec) ?? _config.Current.NinferBackend;
        var s = CollectSettings();
        var eff = ApplyNinferSectionTo(sec, s);     // DEC-135：那 20 格的回填算式只有一个家（唤醒那一条也用它）
        _ninferEffective = eff;
        _ninferLoadedTag = tag;
        _ninferSectionBackend = backend;
        _ninferSectionArgs = sec.Args;
        _ninferSectionFile = sec.File;        // 「模型名称」那格的 tooltip（argv 身份块删掉之后件路径住在这里）
        _config.Current = s;              // 界面载体同步回内存（CollectSettings / ParamSignature 读这份；少一格就会切页盖回去，DEC-064 的教训）
        ApplySettings(s);
        RefreshNinferTips();
        return true;
    }

    // 这一段的家底快照（生效值基线 + 归属后端 + 原始 args）：起服务时"摘模板 + 按界面追加"要用原始模板，
    // 不能用界面上的值反推模板 —— 否则没点保存的手改会一路沉淀进段里（DEC-079 那套"打开看看不许污染文件"的口径）。
    Dictionary<string, string> _ninferEffective = new(StringComparer.OrdinalIgnoreCase);
    string _ninferLoadedTag = "";
    string _ninferSectionBackend = NinferIni.BackendWin;
    string _ninferSectionArgs = "";

    // ---------------- 恢复（设置页 Ninfer 节那一枚「恢复…」；整套复用 llama 侧那三步） ----------------

    internal void RestoreNinferIniFromBackup()
    {
        var snaps = ListNinferIniBackups();
        if (snaps.Count == 0)
        {
            LogN("还没有配置备份（壳每次要写 ninfer_models.ini 之前才存一份），没什么可恢复。");
            Toast.Show(this, "还没有配置备份", Toast.Kind.Info);
            return;
        }
        string? pick = snaps.Count == 1 ? snaps[0] : Dialogs.PickSnapshot(this, snaps, 0);
        if (pick is null) return;                                  // 取消 = 完全静默（DEC-068）
        if (Dialogs.Confirm(this, "恢复哪一家的配置",
                "会用这份备份覆盖现在的 ninfer_models.ini：\n" + pick + "\n\n" +
                "你现在文件里的改动会被替换（替换前会先存一份新备份，所以这一步也退得回去）。",
                null, yes: "覆盖", no: "取消", danger: true) != DialogResult.Yes) return;
        if (!File.Exists(NinferIniPath()))
        {
            LogN("恢复没做：先找到现在的家底再说（" + NinferIniPath() + " 不在）。");
            Toast.Show(this, "现在的 ninfer_models.ini 不在，恢复取消", Toast.Kind.Warn);
            return;
        }
        // DEC-134①：与 llama 那一路同一条修正 —— 恢复前的备份不许顺手轮转（原先 BackupNinferIni 内部就 Trim，
        // 那一格满 3 份时会先删掉最旧那一份，而人挑的往往正是它），改成"先查在 → 只 copy → 复制成了再收口"。
        var bak = Path.Combine(NinferBackupDir(), pick);
        if (!File.Exists(bak))
        {
            LogN("恢复没做：列表里那一份已经不在了（" + pick + "）。清单是开窗时读的，中途被收掉过就重新点一次「恢复…」。");
            Toast.Show(this, "那一份备份不在了，恢复取消", Toast.Kind.Warn);
            return;
        }
        // DEC-199：与引擎 exe 那条同一套 —— 要恢复的就是现在这份 → 什么都不碰；再按内容去重、按内容决定要不要真存。
        if (string.Equals(Sha256FileOrEmpty(bak), Sha256FileOrEmpty(NinferIniPath()), StringComparison.OrdinalIgnoreCase)
            && Sha256FileOrEmpty(bak).Length > 0)
        {
            LogN("恢复没做：现在这份 ninfer_models.ini 与 " + pick + " 逐字节相同，什么都不用换（也没多存备份）。");
            Toast.Show(this, "现在这份就是要恢复的那一份，不用恢复", Toast.Kind.Info);
            return;
        }
        var deduped = DedupeBackupsByContent(NinferBackupDir(), NinferIniBackupPrefix);
        if (deduped > 0) LogN($"恢复前先收拾了这一格：按内容去重删掉 {deduped} 份重复备份（同一份内容只留最早那个名字）");
        if (!BackupBeforeRestore(NinferIniPath(), NinferBackupDir(), NinferIniBackupPrefix, out _, out var berr, out _))
        {
            LogN("备份没做成，恢复取消：" + berr);
            Toast.Show(this, "备份没做成，恢复取消", Toast.Kind.Error);
            return;
        }
        try
        {
            File.Copy(bak, NinferIniPath(), overwrite: true);
            TrimNinferIniBackups();      // 复制成了再收口：那一份的内容此刻已经在工作文件里了
        }
        catch (Exception ex)
        {
            LogN("恢复失败：" + ex.Message);
            Toast.Show(this, "恢复失败：" + ex.Message, Toast.Kind.Error);
            return;
        }
        RefreshModels(NinferKey);       // 只重扫 Ninfer：这一家底变了，另两页不该多出扫描行（DEC-097⑤）
        var tag = _config.Current.NinferModel;
        if (tag.Length > 0) ApplyNinferRecord(tag);
        LogN($"已从备份 {pick} 恢复 ninfer_models.ini（服务在跑的话要「停止 → 启动」才落到进程）");
        Toast.Show(this, "已恢复：" + pick, Toast.Kind.Ok);
    }

    /// <summary>Ninfer 侧的格子提示（文案唯一家）。值就住在选中那一段的 args 里，留空 = 把那枚旗标摘掉。</summary>
    void RefreshNinferTips()
    {
        const string W = "改了写进选中那一段的 args 里（留空 = 把这枚旗标摘掉 = 用引擎默认）";
        if (_cboNinferPool is not null) Tip.Set(_cboNinferPool, "设备池（--kv-capacity）：显存里放多少 token 的 KV，64 token = 1 页。" + W);
    }

}
