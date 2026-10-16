using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MultiLauncher;

// 模型扫描与「运行配置物化」。
// Strata 的引擎参数只从 config JSON 读（serve/server.py 的 engine_args 原样转发 cfg["args"]），
// 所以壳把界面上的可调项写进一份自己的运行配置（exe 同目录 `data\config\strata\<tag>.json`；DEC-118 归位，
// 此前那句注释写的 `%APPDATA%\StrataLauncher\run\` 是老路径，2026-10-13 DEC-164 更正），
// 项目的 strata-<tag>.json 只读不改 —— SETUP 重新生成它时壳的改动也不会互相打架。
// 模型条目。三个模块共用这个形状：Backend 区分归属（strata / llama / ninfer），SizeText 是下拉里显示的估算占用。
public sealed record ModelEntry(string Tag, string ConfigPath, string ModelName, bool Ready, string Note)
{
    public const string BackendStrata = "strata";
    public const string BackendLlama = "llama";
    public const string BackendNinfer = "ninfer";      // DEC-096：第三个模块（段名 = 对外 model-id）

    public string Backend { get; init; } = BackendStrata;
    public string SizeText { get; init; } = "";      // 如 "~22.6G"；算不出来就是空
    /// <summary>Ninfer 那一条的后端标签（win / wsl）——下拉按它过滤（DEC-088：按当前后端列，所以行尾不再标 WSL/WIN）。</summary>
    public string NinferBackend { get; init; } = "";

    // llama / Ninfer 两侧的段名本身就是对外 model-id，多括号反而占宽（DEC-088 卡2 那一格的余量账就靠这个）；
    // Strata 那边 tag 是配置文件名、ModelName 才是 API 名，所以要带出来。
    public string Display => Backend == BackendStrata
        ? Tag + "  (" + ModelName + ")" + (Ready ? "" : "  [未就绪]")
        : Tag + (Ready ? "" : "  [未就绪]");
}

public partial class MainForm
{
    static readonly string[] ValueFlags =
        { "--max-context", "--kv", "--kv-resident", "--spec", "--spec-min-p", "--expert-cache", "--prefill", "--vram-reserve-mib",
          // DEC-097④：rope 这两枚也归壳接管 —— 它们在界面上没有格子，是**从上下文推出来的**（见 RopeFor）。
          "--rope-scaling", "--rope-scale" };
    // 壳要接管的布尔旗标：从配置 args 里剔掉、再按界面重加。--mmap-experts 不在列（DEC-075 整链删除），
    // 配置里自带就让它原样透传——壳不再删也不再补。
    static readonly string[] BoolFlags = { "--vision" };

    // 运行配置、备份都放启动器 exe 同目录的 data\ 下：与 ini/log 同处，自包含、可整目录删掉重来，
    // 也不会往 Strata 项目里塞文件（exe 所在目录在更新时按 StrataLauncher\ 整目录保留）。
    static string LauncherDataDir() => Path.Combine(AppContext.BaseDirectory, "data");

    // ---------------- DEC-116 + DEC-118：目录形状的两个"家" ----------------
    // `data\config\<模块>\` = 三模块**配置本体**的家；`data\backup\<模块>\` = 它们的**备份**家。
    // 两层用**同一套模块字**（llama / ninfer / strata，外加引擎二进制的 ninfer-exe），
    // 不许这边写 `infer` 那边写 `ninfer`；路径只有这两个函数能拼，别处别写字面量。
    // Strata 的**整份升级快照**不住这里 —— 它去 `<Strata 目录旁边的>\Strata-backup\`（见 StrataBackupRoot）。

    /// <summary>某模块的配置本体目录（家底 ini / Strata 的生效配置都在这）。</summary>
    internal static string ModuleConfigDir(string module) => Path.Combine(LauncherDataDir(), "config", module);

    /// <summary>某模块的备份目录（写前备份、引擎 exe 备份按模块分格）。</summary>
    // DEC-124 定案：**配置备份这一层留在 `data\backup\` 不动**（一度改成 `data\bak\`，用户澄清"备份"指的是 Strata 整份快照那一头，已退回来）。
    // 挪窝的只有 Strata 整份升级快照 —— 它去 `D:\Strata-data\backup\`，见 MainForm.Update.cs 的 StrataBackupRoot。
    internal static string ModuleBackupDir(string module) => Path.Combine(LauncherDataDir(), "backup", module);

    /// <summary>界面与日志里"这份东西落在哪儿"那一句的唯一说法：给相对程序目录的那一段（`data\backup\ninfer-exe`）。
    /// DEC-118 之前那些句子是硬写 `data\infer\` 这种字面量 —— 一归位就成了假话，所以从现在起倒推自真函数，
    /// 路径只有一个家、说法也只有一个家。拿不到 data 下面那一层就原样报绝对路径（不撒谎优先）。</summary>
    internal static string DisplayDataPath(string absolute)
    {
        var data = LauncherDataDir();
        if (absolute.StartsWith(data + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return "data" + Path.DirectorySeparatorChar + Path.GetRelativePath(data, absolute);
        return absolute;
    }

    // 物化出来的运行配置都在这（一个 tag 一份）；DEC-064 的一次性迁移也按这个目录建快照
    // DEC-094 ⓪：**自检与探针跑的时候一律改道**。这条链上 BuildEffectiveConfig 是直接
    // File.WriteAllText(EffectiveConfigPath(tag))（同文件物化那段），而这份产物是**引擎实际读的那一份**
    //（DEC-077 起它一度也是「打开配置」的落点，DEC-122② 之后「打开配置」改开 MultiLauncher.ini —— 但每次启动
    // 照旧重写它），于是用户机器上跑一次 --selftest = 覆盖他的运行配置。
    // 覆盖位只有这一个函数：设了之后 RunConfigDir / EffectiveConfigPath / 共享思考档那条全跟着改道，
    // 不许在调用点各写各的（自检里物化相关调用点十几处，散着写必漏）。真实目录仍可由 RealRunConfigDir() 读到，
    // 自检靠它拍"跑前跑后逐字节未动"的快照。
    static string? _runConfigDirForTest;
    internal static void UseTempRunConfigDirForTest(string dir) => _runConfigDirForTest = dir;
    // DEC-118：这一层从此叫 data\config\strata\（"run" 那个名字含糊 —— 里面装的是引擎实读的生效配置，
    // 不是"运行时状态"）。老目录由 MigrateLauncherLayout 首启一次性整层搬走；
    // `<tag>.json` 与 `<tag>.shared-settings.json` 的派生关系（server.py:5544）要求两者**同目录同名**，所以整层一起搬。
    internal static string RealRunConfigDir() => ModuleConfigDir("strata");
    internal static string RunConfigDir() => _runConfigDirForTest ?? RealRunConfigDir();

    // 物化出来的运行配置都在这（一个 tag 一份）——引擎实际读的就是这份（DEC-077 起也是「打开配置」的落点）
    internal static string EffectiveConfigPath(string tag) => Path.Combine(RunConfigDir(), tag + ".json");

    // ---------------- 版本显示 ----------------

    internal static string ReadLocalVersion(string root)
    {
        try
        {
            var text = File.ReadAllText(Path.Combine(root, "CMakeLists.txt"));
            var m = System.Text.RegularExpressions.Regex.Match(text, @"project\(strata VERSION\s+([\d.]+)");
            if (m.Success) return m.Groups[1].Value;
        }
        catch { /* 目录/文件缺失 */ }
        return "";
    }

    internal static string ReadEngineVersion(string root)
    {
        try
        {
            var node = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "engine", "BUILD.json"))) as JsonObject;
            return node?["version"]?.GetValue<string>() ?? "";
        }
        catch { return ""; }
    }

    void RefreshVersions()
    {
        var root = _config.Current.Root;
        _localVersion = ReadLocalVersion(root);
        _engineVersion = ReadEngineVersion(root);
        // 左栏「版本」行（三页同一行）的值不在这里写 —— DEC-107 起它的唯一家是 UpdateVersionRowValue()
        //（按激活模块 + Ninfer 当前后端现算，见 RefreshRunStateView）。这里只把两个原始版本读进字段：
        // 源码版本归更新窗做对比，引擎版本供那一行在 strata 页显示。
        // 2026-10-15（他拍）：strata 原来单列「源码」「引擎」两行，合并成这一行之后界面上只剩引擎版本；
        // 两个值只在"改了源码没重编"时会分叉，界面看不出来 → 状态翻转时补一行日志（别每次刷界面都刷屏）。
        bool diverged = _localVersion.Length > 0 && _engineVersion.Length > 0 && _localVersion != _engineVersion;
        if (diverged != _versionDivergedLogged)
        {
            _versionDivergedLogged = diverged;
            if (diverged)
                LogS($"源码版本与引擎版本不一致：源码 v{_localVersion} / 引擎 v{_engineVersion}" +
                     "（左栏「版本」行显示的是引擎版本；改了源码记得重编）");
        }
        UpdateApiLabel();
        RefreshRunStateView();      // 上去一问一答：升级完/切页后行集与版本值都跟着对齐
    }

    string LocalVersionOr(string fallback) => _localVersion.Length > 0 ? _localVersion : fallback;

    // API 地址跟随激活模块（B 段）：Strata = 公开端口（客户端契约口）；llama = 它的内部端口（实际在跑的口）
    void UpdateApiLabel()
    {
        var port = ActiveView.ApiPortText();
        _txtApi.Text = "http://" + ApiHostForDisplay(_txtHost.Text) + ":" + port + "/v1";
    }

    // 显示给别的程序填的地址：监听 0.0.0.0 的含义就是「别的设备也能连」，所以这里要给本机局域网 IP，
    // 而不是 127.0.0.1。只有探活/开网页那两条路才需要回落到回环地址（见 ProbeHost）。
    internal static string ApiHostForDisplay(string host)
    {
        host = host.Trim();
        return host is "" or "0.0.0.0" or "::" ? LanAddress() : host;
    }

    // 本机局域网 IPv4：优先「已启用、非回环/隧道、有默认网关」的网卡（那才是真正连局域网的口），
    // 都没有就退回任意一个非回环地址，再不行才 127.0.0.1。
    internal static string LanAddress()
    {
        try
        {
            var fallback = "";
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var props = ni.GetIPProperties();
                bool hasGateway = props.GatewayAddresses.Any(g =>
                    g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
                foreach (var ua in props.UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    var ip = ua.Address.ToString();
                    if (IPAddress.IsLoopback(ua.Address) || ip.StartsWith("169.254.")) continue;   // 169.254 = 没拿到 DHCP
                    if (hasGateway) return ip;
                    if (fallback.Length == 0) fallback = ip;
                }
            }
            return fallback.Length > 0 ? fallback : "127.0.0.1";
        }
        catch { return "127.0.0.1"; }
    }

    // 更新检查/下载用的临时目录：优先启动器自己的 data\tmp（与 ini 同处，权限可预期），
    // 不可写时退回系统临时目录。以前直接用 %TEMP%：在受限环境里写到那里会被拒，更新检查直接失败。
    internal static string TempDir()
    {
        var dir = Path.Combine(LauncherDataDir(), "tmp");
        try
        {
            Directory.CreateDirectory(dir);
            var probe = Path.Combine(dir, ".writable");
            File.WriteAllText(probe, "x");
            File.Delete(probe);
            return dir;
        }
        catch { return Path.GetTempPath(); }
    }

}
