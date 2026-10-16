using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace MultiLauncher;

// MainForm 的日志与状态显示：分流（按类型）、行首时间戳、每秒进度行节流（DEC-038）。
// 2026-10-15 从 Program.cs 整段搬出，一行未改。
public partial class MainForm
{
    // ---------------- 日志 / 状态 ----------------

    // 调试信息过滤：界面上隐藏引擎输出的噪声行（只挡界面；日志文件与内存历史照记 —— 口径见 DEC-163）。
    // 规则只认实测日志里确认过的形状：前缀类（ggml_/llama_/[debug]）+ llama-server 的高频噪声形状
    // （对照用户 2026-10-01 贴的全量日志逐行梳理；router 转发的子实例行带 "[ 3367] " 前缀，先剥掉再判）。
    static readonly string[] NoisePrefixes = { "ggml_", "llama_", "[debug]", "[Debug]" };
    static readonly string[] NoiseContains =
    {
        "load:   ",                                    // 每次加载打印的 args 清单（几十行）
        "load: spawning server instance with args:",   // args 清单标题行（带 name/port 的那行不滤）
        "operator(): Available models",                // 模型列表块（每次 reload/重启打一遍）
        "operator():   [    preset]",
        "common_param: common_params_print_info: verbosity",
        "llama threadpool init",                       // cmn init 的线程池元信息
        "common_speculative_impl_draft_dflash:",       // 投机解码实现细节
        "operator(): child server monitoring thread started",
        "operator(): exit command received",
        "operator(): operator(): cleaning up before exit",
        "chat template supports preserving reasoning",     // 每次加载都提示一遍
        "this warning is normal during memory fitting",    // dflash 的常规告警（自带 normal 标记；真错误不带这句）
        // 网关的探活流量（用户反馈 2026-10-01：本地服务每几秒轮询一次，把界面日志刷屏）。
        // 过滤打开时隐藏，日志文件仍写全量；唤醒路径（POST /v1/…）与错误行不滤。
        "网关本地回答 GET /v1/models（不加载模型）",       // 探活轮询模型列表（不加载模型）
        "（这个路径不唤醒模型）",                          // /health、/props、WebUI 静态资源轮询等非唤醒路径的提示
        // DEC-112（用户实跑点名）：服务在跑时每个请求都重复一句「网关收到 POST …」——他那 157 行里 23 句，
        // 与 23 条 `[strata] done:` 一一对应 = 同一件事说两遍。只有**要唤醒/要切件**那两种才逐条上屏，
        // 「只是转发给已经在跑的那一件」带这个后缀，界面滤掉、文件照写全量（口径同 DEC-038）。
        Gateway.GatewayRepeatSuffix,
    };

    // 源头丢弃档（DEC-150，他实跑点名）：连**日志文件与内存历史**都不进，不只是界面上滤。
    // 与上面两组的区别：那两组滤掉的是"引擎的啰嗦"，文件里留着还能查；这一组滤的是**壳自己按节拍造出来的**行 ——
    // 状态栏每 5 秒问一次 router 的 /metrics（DEC-148），router 的 proxy_request 每次都打一行
    //   `… I srv  proxy_reques: proxying request to model <件> on port <子实例口>`（带 pid 前缀；子实例 stderr 直接挂 router 管道）。
    // 他点名："能否把这个信息过滤掉,即不计入日志,也不显示,只默默工作?" → 挂在 Log() 入口丢（不上屏、不进历史、不写 logs\llama.log，
    // 连轮转字节数都不计）。**只认这一种形状**：转发的真实请求与错误行照旧（不误伤）；这行的诊断价值已被状态栏那串数替代，
    // 要看现场把这一条摘掉即可复活（登记在 DEC-150）。
    static readonly string[] NoiseSinkContains =
    {
        "proxying request to model ",
    };

    /// <summary>这一行是不是"源头丢弃"（连文件都不写）。pid 前缀先剥（router 转发的子实例行都带）。</summary>
    internal static bool IsSinkNoise(string text)
    {
        var t = StripPidPrefix(text);
        foreach (var shape in NoiseSinkContains)
            if (t.Contains(shape, StringComparison.Ordinal)) return true;
        return false;
    }

    // "[ 3367] xxx" / "[3367] xxx" 前缀剥离（router 转发的行）：手写比正则快，IsNoise 每行都过这里
    static string StripPidPrefix(string s)
    {
        if (s.Length < 3 || s[0] != '[') return s;
        int i = 1;
        while (i < s.Length && s[i] == ' ') i++;
        int d = i;
        while (i < s.Length && char.IsDigit(s[i])) i++;
        if (i == d) return s;
        while (i < s.Length && s[i] == ' ') i++;
        if (i >= s.Length || s[i] != ']') return s;
        return s[(i + 1)..];
    }

    internal static bool IsNoise(string text)
    {
        if (text.Trim().Length == 0) return true;
        foreach (var p in NoisePrefixes)
            if (text.StartsWith(p, StringComparison.Ordinal)) return true;
        var t = StripPidPrefix(text);
        if (t.Trim().Length == 0) return true;      // "[ 5435] " 这种带前缀的空行
        foreach (var shape in NoiseContains)
            if (t.Contains(shape, StringComparison.Ordinal)) return true;
        // DEC-163：名单里那八类引擎台账在界面上同样算"噪声"（过滤挡的只是界面；文件与内存历史照记 —— 那一边由去重闸管）。
        return LogNoise.ShownAsNoise(text);
    }

    // ---------------- 日志（B 段阶段③：按类型分流） ----------------
    // 文件：落启动器目录 logs\ 下、按类型分文件（launcher / strata / llama），照记（八类引擎台账做连发去重 —— DEC-163；
    // DEC-158 那版"默认只写信号 + 写全量开关"已随 B 路径退役）。
    // 界面：激活模块页显示「本模块行 + 启动器行」；未激活页后台照收（进历史），切页可见；「清屏」只清当前页显示。
    // 历史保留最近 5000 行（内存），文件不受影响、不截断。
    readonly List<(DateTime At, string Key, string Text)> _logHistory = new();
    readonly Dictionary<string, DateTime> _logClearedAt = new() { ["strata"] = DateTime.MinValue, ["llama"] = DateTime.MinValue };

    // DEC-111：日志这三样各有一条判据位，历史上都栽过"看着有、其实没生效"。
    internal const int LogHistoryMaxLines = 5000;      // 内存历史上限（也是界面那只框的上限，见 LogBoxNeedsRebuild）
    const int LogHistoryDrop = 1000;                   // 超了删最旧这么多（不是一行一行删，省掉每次搬表）
    internal const long LogRotateBytes = 8 * 1024 * 1024;   // 单个日志文件超这个数就滚
    internal const int LogRotateKeep = 3;              // 滚出来的历史留 3 份（.1/.2/.3）—— 与 DEC-110⑤ 那五处备份份数同口径
    internal const long LogRecheckBytes = 256 * 1024;  // 每写这么多字节才重新量一次文件大小（以前 = 一次会话只量一次，等于没有）

    // DEC-163：去重状态（键 = 类名 → 上一条的原文；`LogNoise.IsRepeat` 读它）。
    // 八条 key 的小字典，不需要定时器 —— 那一枚随 DEC-162 的合并档一起退役了。
    internal readonly Dictionary<string, string> _noiseLast = new();
    internal static bool LogFileDisabledForTest = false;   // 自检与出图台一律不写用户的 logs\（入口点名，见 SelfTest.Run / UiPreviewForm.Run）

    // DEC-147（BUG-067 结案）：「自动启动服务」是这一族按进程收口的闸里唯一漏的那一道
    // （其余六道 = LogFile / ScratchSweep / LayoutMigration / VersionProbe / GpuProbe / Toast.DisabledForTest）。
    // 为什么必须补：自检与出图台建的都是**真 MainForm**，而他 bin 旁那份真实 ini 是 AutoStartStrata=True + AutoStartLlama=True
    // —— 探针一 Show() 就真去起服务，撞上端口上他自己在跑的实例会走到「结束外部进程」那枚模态确认（旁边没人点 = 挂住，
    // 最坏把他的服务结束掉）。口径同 DEC-111②："护栏要挡的是整条路"，入口点名、不按实例。
    internal static bool AutoStartDisabledForTest;
    internal static int AutoStartWiredForTest;      // 证人位：真挂上 Load 的次数（闸上时必须恒 0）
    internal static int AutoStartSkippedForTest;    // 证人位：被这道闸挡下的次数（开关真开着才会涨 = 不是空判据）

    // DEC-164（BUG-068 第二条残留结案）：**开机自启那一枚 HKCU Run 键**同样按进程收口。
    // 与上面 DEC-147 那道是**两件事**：那道管"要不要挂 Load 去起服务"，这道管"要不要动他的注册表"。
    // 为什么必须补：自检与出图台建的都是真 MainForm，构造函数末尾那句 `SetAutoStart(_config.Current.AutoStartWithWindows)`
    // 会拿**本进程那只 exe** 去写/删那枚值 —— 他若把「开机自启」开着，值名一样、路径被换成本地开发目录那只 exe；
    // 他若关着，则把他原来那枚值**直接删掉**（自启静默失效）。两种都不该由"跑一次自检"决定（BUG-052 那一族：
    // 环境不该决定用例红绿，更不该改他的东西）。闸门与两个证人位都长在 `SetAutoStart` 本体那一个入口上（口径同 DEC-160③）。
    internal static bool AutoStartRegistryDisabledForTest;
    internal static int AutoStartRegistrySkippedForTest;   // 证人位：这条路真被走到（闸上才涨）
    internal static int AutoStartRegistryWritesForTest;    // 反面证据：**真去写注册表那一步被走到**的次数（闸上必须恒 0；自检入口挂了替身，所以红侧也不会真落盘）
    /// <summary>自检替身：非 null 时"真去写注册表"那一步改走它（自检里只记账、不落盘）。
    /// 有它才敢跑红/绿 —— 把闸门按回去那一变体照样一个字节都碰不到他的 Run 键。</summary>
    internal static Action<bool>? AutoStartRegistryWriteForTest;

    /// <summary>自检接缝：走一次真路径 <see cref="SetAutoStart"/>（闸门与证人位都在本体那一个入口上）。</summary>
    internal void SetAutoStartForTest(bool enable) => SetAutoStart(enable);

    /// <summary>纯判据：这一格「自动启动服务」要不要挂到 Load 上真去起服务（DEC-147）。三个条件缺一个都不起。</summary>
    internal static bool ShouldWireAutoStart(bool switchOn, bool moduleEnabled, bool disabledForTest)
        => switchOn && moduleEnabled && !disabledForTest;

    /// <summary>自检接缝：按模块表把上面那条判据在给定 Settings 上**重放**一遍，返回"会被挂上 Load 的格数"。
    /// 不真挂（真挂就多出一条 Load 处理，测试里收不回来）；判据与构造函数那一段共用同一个 <see cref="ShouldWireAutoStart"/>，
    /// 所以这一面证的是真判据，不是另写一套。为什么要这一面：真路径上的证人位吃他的 ini（开关全关就量不到东西），
    /// 这一面喂合成 Settings = 环境无关。</summary>
    internal int CountAutoStartWiringForTest(Settings s, bool disabledForTest)
    {
        int n = 0;
        foreach (var v in _views)
            if (ShouldWireAutoStart(v.AutoStart(s), v.EnabledIn(s), disabledForTest)) n++;
        return n;
    }

    // 日志目录只有一个家；自检/出图台用那一条改道（同 UseTempRunConfigDirForTest / UseTempNinferHomeForTest 的形状：一处收口，别在调用点各写各的）。
    // 注意真实 logs\ **不做**"跑前跑后逐字节比对"那条 ⓪ 护栏：外壳活着时引擎本来就在往里写，比了就是假红。
    // 那条护栏在这里的等价形式 = 测试态闸门（LogFileDisabledForTest）+ 用例在 data\tmp\ 的副本上验它真挡。
    static string? _logsDirForTest;
    internal static void UseTempLogsDirForTest(string? dir)
    {
        _logsDirForTest = dir;
        if (dir is not null) try { Directory.CreateDirectory(dir); } catch { /* 建不出来就还是写原处 */ }
    }
    internal static bool LogsDirOverriddenForTest => _logsDirForTest is not null;
    internal static string LogsDir() => _logsDirForTest ?? Path.Combine(AppContext.BaseDirectory, "logs");

    // 纯判据（自检直接喂数）：文件大到该滚了吗 / 框里行数该重刷了吗
    internal static bool ShouldRotateLog(long bytes) => bytes > LogRotateBytes;
    internal static bool LogBoxNeedsRebuild(int lines) => lines > LogHistoryMaxLines;

    /// <summary>把 &lt;key&gt;.log 滚成 .1，并把 .1→.2→.3 依次顶一格；第 keep 份那份删掉（最旧的走，DEC-110⑤ 同一口径）。
    /// 任何一步失败都不致命（日志是 best effort），但绝不许把正在写的那一份弄丢。</summary>
    internal static void RotateLogFiles(string dir, string key, int keep = LogRotateKeep)
    {
        try
        {
            File.Delete(Path.Combine(dir, key + ".log." + keep));
            for (int i = keep - 1; i >= 1; i--)
            {
                var from = Path.Combine(dir, key + ".log." + i);
                if (File.Exists(from)) File.Move(from, Path.Combine(dir, key + ".log." + (i + 1)), true);
            }
            var live = Path.Combine(dir, key + ".log");
            if (File.Exists(live)) File.Move(live, Path.Combine(dir, key + ".log.1"), true);
        }
        catch { /* 滚不动就继续往原文件写，别把日志链断在这里 */ }
    }

    // 设置窗等外部窗口往主界面日志写一行（Log 本体是私有的）
    internal void LogToUi(string text) => Log(text);

    void Log(string text, string key = "launcher")
    {
        if (IsDisposed) return;
        if (InvokeRequired)
        {
            var t = text; var k = key;
            try { BeginInvoke(new Action(() => Log(t, k))); } catch { }
            return;
        }
        if (IsSinkNoise(text)) return;      // DEC-150：源头丢弃（不上屏 / 不进历史 / 不写文件，见 NoiseSinkContains）
        // DEC-163（第 41 轮，他核过体积后拍"B 路径"、原话"1MB 还能接受"）：八类引擎台账**照记**
        // （文件与内存历史 = 旧口径），只做一条简单去重：同一类里与上一条逐字节相同 → 整条不落
        // （19 分钟那份实测 6.29 MB → ≈1 MB）。界面那一头由「调试信息过滤」挡（见 IsNoise 里的 ShownAsNoise）
        // —— 过滤只管看、不管记；信号行与 llama 那族**不去重**（两条一字不差的警告必须都记）。
        if (LogNoise.Classify(text) is { } rule && LogNoise.IsRepeat(_noiseLast, rule, text)) return;
        var at = DateTime.Now;
        _logHistory.Add((at, key, text));
        if (_logHistory.Count > LogHistoryMaxLines) _logHistory.RemoveRange(0, LogHistoryDrop);
        if (_config.Current.SaveLog) AppendLogFile(key, at, text);
        // 界面分流：启动器行显示在当前激活页；未激活模块的行只进历史（切页可见）
        if (key != "launcher" && key != _activeModule) return;
        var cutoff = _logClearedAt.TryGetValue(_activeModule, out var c) ? c : DateTime.MinValue;
        if (at <= cutoff) return;
        if (_config.Current.FilterDebugInfo && IsNoise(text)) return;
        // ③ 节流（2026-10-03，DEC-038）：Strata 的每秒进度行只每 5 秒放一条上界面。
        // 历史与日志文件照写**全量**（和「过滤调试信息」同一口径：只影响界面）；用户口径=大概了解，不要精确平均。
        if (IsProgressSpam(text) && !ProgressAllowed(ref _lastProgressShown, at)) return;
        _txtLog.AppendText(LogLine(at, text, withDate: false) + Environment.NewLine);
        _txtLog.SelectionStart = _txtLog.TextLength;
        _txtLog.ScrollToCaret();
        // DEC-111③：实时这一路只有 AppendText、以前没有上限（只有切页/清屏走 RebuildLogView 才按 5000 行截）。
        // Ninfer 引擎生成期间 ~60 行/秒，页开着十几分钟框里就是三万多行文本。超上限就直接按同一判据重建一次
        //（RebuildLogView 本来就做"分流 + 过滤 + 节流 + 5000 行"，别处再写一套截断算式就会漂）。
        if (LogBoxNeedsRebuild(++_logBoxLines)) RebuildLogView();
    }

    // ⑦ 日志归属（2026-10-03 用户反馈：llama 页混进 Strata 的话，反查发现 Strata 页同样混进 llama 的）：
    // 规则本身没变（标了模块的行只在本模块页显示，launcher 行两页都显示），变的是**壳自己写的话也要标归属**——
    // 以前几乎全用默认 launcher，于是两侧的话在两个页面上都刷。约定：
    //   LogS = Strata 侧（服务、网关[DEC-024：网关属 Strata 模块]、Strata 模型/更新链、SETUP）
    //   LogL = llama.cpp 侧（llama-server 拉起与合并 ini、llama 模型、llama 更新）
    //   Log  = 真正跨模块的话（端口撞车、模块开关、设置保存、退出、配置更名）——两页都该看见
    //   LogActive = 跟着当前页走的操作（打开网页 / 打开配置：话是说给这一页听的）
    void LogS(string text) => Log(text, "strata");
    void LogL(string text) => Log(text, "llama");
    void LogN(string text) => Log(text, "ninfer");      // DEC-096：第三个模块一条通道（LogActive 那一路由模块表给键）
    // 日志归属（DEC-047/092）：跟着当前激活模块那一路走，通道键由模块表给
    internal void LogActive(string text) => Log(text, ActiveView.LogKey);

    // 「清屏」按页（B 段）：只清当前页显示（历史与文件都不动）
    void ClearActiveLog()
    {
        _logClearedAt[_activeModule] = DateTime.Now;
        RebuildLogView();
    }

    // 切页后重建本页日志视图（本模块行 + 启动器行；按该页的清屏时间截断；界面过滤生效）
    void RebuildLogView()
    {
        if (IsDisposed) return;
        var cutoff = _logClearedAt.TryGetValue(_activeModule, out var c) ? c : DateTime.MinValue;
        var sb = new StringBuilder();
        var lastProgress = DateTime.MinValue;      // 节流窗口在重建时从头算（DEC-038：切页看到的和本页实时滚动的同一判据）
        int lines = 0;
        // DEC-111③：重建这一路也吃同一条上限 —— 内存历史在实时路径上会被 Log 截到 5000，但播种/异常路径可能更长，
        // 框里只该看到最近这么多行（否则"上限"就成了只在一条路径上生效的判据）。
        foreach (var (at, key, text) in _logHistory.Skip(Math.Max(0, _logHistory.Count - LogHistoryMaxLines)))
        {
            if (key != "launcher" && key != _activeModule) continue;
            if (at <= cutoff) continue;
            if (_config.Current.FilterDebugInfo && IsNoise(text)) continue;
            if (IsProgressSpam(text) && !ProgressAllowed(ref lastProgress, at)) continue;
            sb.Append(LogLine(at, text, withDate: false)).Append(Environment.NewLine);
            lines++;
        }
        _lastProgressShown = lastProgress;        // 接着滚的实时行继续按同一窗口节流
        _logBoxLines = lines;                     // DEC-111③：框里现在到底几行，重建之后要归案
        _txtLog.Text = sb.ToString();
        _txtLog.SelectionStart = _txtLog.TextLength;
        _txtLog.ScrollToCaret();
    }

    // ---------------- 行首那枚时间戳：引擎自己打了就不再补（2026-10-13 他点名「最前面的时间戳重复了」） ----------------
    // 实测**两代都带**：WIN = `2026-10-13 02:51:22.965  INFO  throughput | 5.0s | decode 145.9 tok/s …`；
    // WSL = 同一套 spdlog 图案（本机探针用一件不存在的件让它当场失败，打的是
    // `2026-10-13 02:56:42.485  INFO  starting engine`，没读盘、没起卡）。
    // 壳以前对**每一行**无条件补一枚 `HH:mm:ss`（界面）或 `yyyy-MM-dd HH:mm:ss`（文件）→ 那一类行前面就成了两个时间戳。
    // 判据只看行首那个形状、不看通道：壳自己写的话（`$ 网关已监听 …`）、Strata 的 `[strata] …`、llama-server 那些没戳的行照旧补。
    static readonly System.Text.RegularExpressions.Regex EngineStampAtHead = new(
        @"^\d{4}-\d{2}-\d{2}[ T]\d{2}:\d{2}:\d{2}", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>这一行开头是不是已经带了引擎自己那一枚完整时间戳（纯函数，自检三面喂）。</summary>
    internal static bool CarriesOwnStamp(string text) =>
        text.Length >= 19 && EngineStampAtHead.IsMatch(text);

    /// <summary>一行的写法只有一个家（界面与文件共用）：引擎自带时间戳 = 原样交回，否则补壳的那一枚。
    /// <paramref name="withDate"/> = 文件那一档要带日期，界面那档只要时分秒（照旧）。</summary>
    internal static string LogLine(DateTime at, string text, bool withDate) =>
        CarriesOwnStamp(text) ? text
        : at.ToString(withDate ? "yyyy-MM-dd HH:mm:ss" : "HH:mm:ss") + " " + text;

    // ---------------- ③ 每秒进度行的节流（2026-10-03，DEC-038） ----------------
    // 刷屏源在 Strata 的 python 服务层（serve/server.py 的 _progress(every=1.0)），按 1 秒打 stdout：
    //   [strata] reading the prompt: 8,192 of 12,703 tokens, 4 s so far
    //   [strata] thinking: 40 of max 32768 tokens, 79.4 tok/s, 9 s
    //   [strata] writing a tool call: run_code: 74 of max 32768 tokens, 48.1 tok/s, 11 s
    // 用户实测日志 968 行里 831 行是它（一次 303 秒的请求单独吐 297 行）；`every` 没有开关也没有环境变量，
    // 改 Strata 越界且下次升级即失效，所以在壳侧挡。判据："[strata] " 开头 + 含 "tokens" + 不是 done 收尾行。
    internal static bool IsProgressSpam(string text) =>
        text.StartsWith("[strata] ", StringComparison.Ordinal)
        && !text.StartsWith("[strata] done:", StringComparison.Ordinal)
        && text.Contains("tokens", StringComparison.Ordinal);

    internal const double ProgressGapSeconds = 5.0;     // 与底部状态栏的 5 秒同拍（④）

    // 纯函数版判据（自检可直接喂时间戳断言）：窗口内后面的行不放行
    internal static bool ProgressThrottleAllows(DateTime lastShown, DateTime now) =>
        lastShown == DateTime.MinValue || (now - lastShown).TotalSeconds >= ProgressGapSeconds;

    static bool ProgressAllowed(ref DateTime lastShown, DateTime now)
    {
        if (!ProgressThrottleAllows(lastShown, now)) return false;
        lastShown = now;
        return true;
    }

    DateTime _lastProgressShown = DateTime.MinValue;    // 本页实时行上一次上屏的进度行时间
    int _logBoxLines;                                   // DEC-111③：界面那只框里现在有几行（切页/清屏/超限重建时归案）

    // 日志文件：logs\<key>.log，写全量；单文件超 8MB 时轮转（留 .1/.2/.3 三份）。
    // DEC-111①：以前那道判据是 `if (_logRotated.Add(key))` —— HashSet 首次才为真 = **一次会话只量一次大小**，
    // 8MB 之后一路涨到进程退出（Ninfer 生成期间实测 60 行/秒、19 分钟 6.35MB，那条等于没有）；
    // 现在按累计写入量每 256KB 重量一次实际大小，滚的时候 .1→.2→.3 顶一格、最旧那份删（份数照 DEC-110⑤ = 3）。
    static long _logBytesSinceCheck;
    static void AppendLogFile(string key, DateTime at, string text)
    {
        if (LogFileDisabledForTest) return;            // DEC-111②：自检与出图台的测试行绝不进用户的 logs\
        try
        {
            var dir = LogsDir();
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, key + ".log");
            var line = LogLine(at, text, withDate: true) + Environment.NewLine;
            File.AppendAllText(path, line, new UTF8Encoding(false));
            _logBytesSinceCheck += Encoding.UTF8.GetByteCount(line);
            if (_logBytesSinceCheck >= LogRecheckBytes)
            {
                _logBytesSinceCheck = 0;
                var fi = new FileInfo(path);
                if (ShouldRotateLog(fi.Exists ? fi.Length : 0)) RotateLogFiles(dir, key);
            }
        }
        catch { /* best effort */ }
    }
    internal static void AppendLogFileForTest(string key, string text) => AppendLogFile(key, DateTime.Now, text);   // DEC-111①②：闸门的红/绿验用（改道到 data\tmp\ 才许调）

    // 状态灯与状态卡「状态」行按激活模块渲染（B 段：主区按模块分页）：
    // Strata = 服务/网关状态；llama.cpp = llama-server 进程状态。文案由实时标志**推导**（见 StrataStatusText/LlamaStatusText）。
    // SetState 保签名只为兼容原有调用点（起停各环节）——它现在只作为一个"该刷新了"的触发（不再存文案）。
    void SetState(string text, Color color)
    {
        if (InvokeRequired) { try { BeginInvoke(new Action(() => SetState(text, color))); } catch { } return; }
        RefreshLamp();
        RefreshStartStopButton();
    }

    void RefreshLamp()
    {
        if (InvokeRequired) { try { BeginInvoke(new Action(RefreshLamp)); } catch { } return; }
        var (text, color) = ActiveView.StatusText();
        // 合并后（2026-10-04）：原「运行状态」大行删掉，它的文案与状态色由「状态」行的值承接（一处刷新）
        if (_kv.TryGetValue("状态", out var v)) { v.Text = text; v.ForeColor = color; }
        RefreshModuleLights();
    }

    // 停止态文案：网关还开着时服务确实没跑，但状态词与左栏「状态」行/模块状态灯同一句（DEC-055 口径，
    // 用户 2026-10-04 第 1 条：「网关待唤醒」不再出现在界面上）。SetState 现在只当"该刷新了"的触发，文案由
    // StrataStatusText/LlamaStatusText 推导——这里的字面量只为保持一致，别把它当显示源。
    void SetStoppedState() => SetState(StrataGateway is not null ? "● 待机 · 模型未加载" : "已停止",
                                      StrataGateway is not null ? Color.SteelBlue : Color.Gray);

    // 左栏启停按钮的文案 = 「当前点它会发生什么」（用户反馈 2026-10-01：状态要跟随实际工作状态）：
    //   · 壳拉起的服务 / llama-server      -> 停止
    //   · 端口上是外部拉起的服务（确认后可停）-> 停止
    //   · 只有网关在跑（服务已空闲停服）      -> 停止
    //   · 都没有                            -> 启动
    // 2026-10-15（他点名「停止网关/停止按钮,统一修改为停止」）：第三档原来单独报「停止网关」
    //（2026-10-01 那条"看着像服务还在跑"的顾虑），现在三档**统一成「停止」**——按钮点下去做的事
    // 一个字没变（都把这一模块的东西停干净：服务 / 外部实例 / 只剩网关）。`gatewayUp` 仍留在签名里
    //（调用点与自检都按三个实时值喂），只是不再影响文案。
    // 第 0 轮（DEC-094）：原来这两个判据挤在一个函数里、靠 `bool llamaSel` 分岔 —— 那是界面上最后两处二元判断。
    // 现在一个模块一个纯函数（口径一字未改），模块表只负责喂自己那几个实时标志。
    internal static string StartStopTextForStrata(bool strataRunning, bool strataExternal, bool gatewayUp)
        => strataRunning || strataExternal || gatewayUp ? "停止" : "启动";

    internal static string StartStopTextForLlama(bool llamaUp) => llamaUp ? "停止" : "启动";

    void RefreshStartStopButton()
    {
        if (InvokeRequired) { try { BeginInvoke(new Action(RefreshStartStopButton)); } catch { } return; }
        var text = ActiveView.StartStopText();
        bool up = text != "启动";
        _btnStartStop.Text = text;
        _btnStartStop.FillColor = up ? UiTheme.Danger : UiTheme.Brand;
        _btnStartStop.HoverColor = up ? UiTheme.DangerHover : UiTheme.BrandHover;
        _btnStartStop.TextColor = up ? Color.White : UiTheme.BrandOn;
    }

    // llama-server 起停/退出、选中模型变化后统一刷一遍：灯 + 启动按钮 + 状态卡 + 模型行按钮宽度
    void RefreshStatusUi()
    {
        RefreshLamp();
        RefreshStartStopButton();
        RefreshRunInfo();
        RefreshModelBadges();     // DEC-065：下拉里「已加载」跟着实际加载状态走（服务在跑但模型没加载也要收掉）
        if (LayoutHook is not null) LayoutHook();
    }

    void SetButtons(bool running)
    {
        if (InvokeRequired) { try { BeginInvoke(new Action(() => SetButtons(running))); } catch { } return; }
        RefreshStartStopButton();
        RefreshRunInfo();
        if (LayoutHook is not null) LayoutHook();
    }

    void SetHint(string text)
    {
        if (InvokeRequired) { try { BeginInvoke(new Action(() => SetHint(text))); } catch { } return; }
        _lblHint.Text = text;
        _lblHint.Visible = text.Length > 0;
        // 提示行整行跟着显隐：空的时候不占位，模型卡→日志卡的距离保持 12px（用户反馈：中间空隙太大）。
        // DEC-107：显式补一次重排 —— Dock 布局是懒的（真实消息循环里会自己跟上，无头自检里不会）。
        // 以前这里靠"切页会把卡2 高度改掉"这个副作用顺带重排；三页卡2 统一成两行之后副作用没了，
        // 自检当场抓到"提示行空着却仍占着 24px"（模型卡→日志卡间距 36 ≠ 12）。
        if (_cardHint is not null)
        {
            _cardHint.Visible = text.Length > 0;
            _cardHint.Parent?.PerformLayout();
        }
    }

    void SetKv(string key, string text)
    {
        if (InvokeRequired) { try { BeginInvoke(new Action(() => SetKv(key, text))); } catch { } return; }
        if (_kv.TryGetValue(key, out var v)) v.Text = text;
    }

    // 运行状态卡的动态字段 + 左栏模式条：PID/端口跟随激活模块的进程；模式条显示激活模块自己的模式（B 段：按模块各自记忆）
    // 第 0 轮 DEC-094：这两个字段"读谁的进程"由模块表给（PidText / PortRowText），这里不再有 if/else
    void RefreshRunInfo()
    {
        if (InvokeRequired) { try { BeginInvoke(new Action(RefreshRunInfo)); } catch { } return; }
        SetKv("PID", ActiveView.PidText());
        SetKv("端口", ActiveView.PortRowText());   // 显示「实际在跑的那个」：没跑时按当前配置的口（B 段口径）
        var host = _txtHost.Text.Trim();
        SetKv("地址", host.Length > 0 ? host : "-");
        RefreshModeLabel();     // 左栏模式条的文字就是当前模式（它本身是切换按钮）
        RefreshRunStateView();
    }

    // 运行状态卡的行集随激活模块（B 段）：每模块在模块表里声明自己那几行。
    // 2026-10-15（他拍）之后三页都是 `["版本"]` —— 这一行因此恒为 true、Visible 的 setter 直接返回 = **切页零重排**。
    // 原来 strata 是 `["源码","引擎"]`（6 行）、另两页是 `["版本"]`（5 行），切页要重排且模式条跟着跳一行，
    // 中间态被画到屏幕上就是"切页时左栏模式条破碎"；合并成一行之后位移这件事不存在了。
    void RefreshRunStateView()
    {
        foreach (var key in new[] { "版本" })
            if (_kvPanels.TryGetValue(key, out var p)) p.Visible = ActiveView.RunInfoRows.Contains(key);
        UpdateVersionRowValue();
        RefreshSidebarUpdateButtons();     // DEC-108：小箭头那枚的颜色也跟着当前模块的 offer 走
    }

    /// <summary>左栏「版本」行的值随激活模块换（DEC-107，他第 5 条"ninfer 页左栏也显示版本"）：
    /// llama = 运行时版本；Ninfer = **当前后端那一侧**的版本（WIN 截到 -kvmem 之前、WSL 短提交号；
    /// 长写法与缩法都收在 <see cref="ShortVersionForSidebar"/>）；Strata = 引擎版本（2026-10-15 起这行
    /// 三页都出现 —— 原来 strata 单列的「源码」「引擎」两行已合并进它，见模块表 RunInfoRows 那条注释）。
    /// 摆在这里是因为切页（RefreshModuleScopedUi）与每次状态刷新（RefreshRunInfo）都会走到它。
    /// 判据拆成纯函数 <see cref="SidebarVersionText"/>（自检喂样本穷举，别靠切后端碰运气）。</summary>
    void UpdateVersionRowValue()
    {
        if (!_kv.TryGetValue("版本", out var v)) return;
        v.Text = SidebarVersionText(_activeModule, ActiveNinferBackend(_config.Current),
            NinferVersionForSettings, NinferWslVersionForSettings,
            ModuleRegistry.LlamaEnabled(_config.Current) ? LlamaIni.RuntimeVersion(_config.Current.LlamaDir) : "-",
            _engineVersion.Length > 0 ? "v" + _engineVersion : "-");
    }

    /// <summary>左栏「版本」行显示什么（纯判据）：见 <see cref="UpdateVersionRowValue"/>。
    /// 两个版本串（llama 的运行时版本 / strata 的引擎版本）由调用方算好传进来（带 "v" 前缀或 "-"），
    /// 这一层只按模块分流 + 兜底 —— 别把读文件/拉子进程的活搬进来（自检要穷举它）。</summary>
    internal static string SidebarVersionText(string key, string ninferBackend,
        Func<string> winVersion, Func<string> wslVersion, string llamaVersion, string strataVersion) => key switch
    {
        NinferKey => ShortVersionForSidebar(ninferBackend == NinferIni.BackendWsl ? wslVersion() : winVersion()),
        LlamaKey => llamaVersion.Length > 0 ? llamaVersion : "-",
        // 2026-10-15（他拍）：strata 报引擎版本（engine/BUILD.json 那份 = 实际跑着的产物）。
        // 源码版本只在"改了源码没重编"时与它分叉，那种情况走日志（RefreshVersions），不上界面。
        StrataKey => strataVersion.Length > 0 ? strataVersion : "-",
        _ => "",
    };

}
