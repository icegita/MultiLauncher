using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace MultiLauncher;

internal static class Program
{
    [DllImport("shell32.dll", SetLastError = true)]
    static extern void SetCurrentProcessExplicitAppUserModelID(
        [MarshalAs(UnmanagedType.LPWStr)] string appId);

    // AUMID 跟显示名走（任务栏/托盘身份；2026-10-02 更名 DEC-030）；互斥体/事件名保留历史身份——
    // 改了会让新旧版本互不相认（同机可能同时跑起来、抢同一批端口），它们本来就不出现在界面上。
    const string Aumid = "iCeGiTa.MultiLauncher";
    const string MutexName = @"Local\iCeGiTa.MultiLauncher.SingleInstance";
    const string ShowEventName = @"Local\iCeGiTa.MultiLauncher.ShowWindow";

    [STAThread]
    static void Main(string[] args)
    {
        // 开发者自检（不开界面）：MultiLauncher.exe --selftest <输出文件> [Strata 目录]
        if (args.Length >= 2 && args[0] == "--selftest")
        {
            var root = args.Length >= 3 ? args[2] : MainForm.StrataRootForSelfTest();
            Environment.ExitCode = SelfTest.Run(root, args[1]);
            return;
        }

        // 自绘验证（临时，UI 改造第一步）：MultiLauncher.exe --uiprobe [titlebar]
        if (args.Length >= 1 && args[0] == "--uiprobe")
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            UiPreviewForm.Run(args);
            return;
        }

        // 固定托盘/任务栏身份，避免 Windows 把每次启动当成新应用而重置托盘设置（须在创建窗口前）。
        SetCurrentProcessExplicitAppUserModelID(Aumid);
        RegisterAumid();

        // 单实例：命名互斥体按会话共享、与 exe 路径无关。
        var mutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            SignalPrimaryToShow();
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var form = new MainForm(args.Any(a => a == "--tray"));
        var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _ = Task.Run(() =>
        {
            while (true)
            {
                bool signaled;
                try { signaled = showEvent.WaitOne(); }
                catch { break; }
                if (!signaled) continue;
                try { form.BeginInvoke(new Action(form.ShowMainWindow)); }
                catch { Thread.Sleep(100); }
            }
        });
        Application.Run(form);
        GC.KeepAlive(mutex);
    }

    static void SignalPrimaryToShow()
    {
        for (int i = 0; i < 20; i++)
        {
            try
            {
                using var ev = EventWaitHandle.OpenExisting(ShowEventName);
                ev.Set();
                return;
            }
            catch { Thread.Sleep(50); }
        }
    }

    static void RegisterAumid()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\AppUserModelId\" + Aumid);
            if (key is null) return;
            key.SetValue("DisplayName", MainForm.AppName);   // 与窗口标题/托盘同名（更名前是硬编码「Strata 启动器」）
            key.SetValue("IconUri", Application.ExecutablePath);
        }
        catch { /* best effort */ }
    }
}

// MainForm 主分部：字段、构造、托盘/窗口/退出、日志与状态显示。
// 界面搭建见 MainForm.Ui.cs，服务生命周期见 MainForm.Service.cs，配置读写见 MainForm.Config.cs，
// 模型扫描与运行配置物化见 MainForm.Models.cs，更新见 MainForm.Update.cs。
public partial class MainForm : Form
{
    // 壳自己的设置文件（2026-10-03，DEC-037）：随产物名统一为 MultiLauncher.ini（旧名 StrataLauncher.ini 只做过一次性改名）。
    // 互斥体 / 事件名 / 注册表启动值名仍沿用历史身份，理由同 DEC-032：改了新旧两版互不相认，可能同时跑抢端口。
    internal const string ConfigFileName = "MultiLauncher.ini";
    internal const string LegacyConfigFileName = "StrataLauncher.ini";
    // 本次运行做过一次性改名（字段初始化阶段还没有日志，构造函数里补一行）
    internal static bool ConfigRenamed;
    static string ConfigPath = MigrateConfigName(AppContext.BaseDirectory, out ConfigRenamed);

    // DEC-092：自检验「保存才落盘」要真的写文件，但不能覆盖开发目录那份真 ini —— 给测试一个覆盖位
    //（口径同 data	mp\ 的副本纪律：自检只往 tmp 里写，写完把路径改回来）。一处收口，别在调用点各拼各的。
    internal static void UseTempConfigForTest(string path) => ConfigPath = path;
    internal static string ConfigPathForTest => ConfigPath;
    /// <summary>壳自己这份 ini 的全路径（与 <see cref="MainForm.RouterIniPath"/> / <see cref="MainForm.NinferIniPath"/>
    /// 同一族命名）。设置页「通用 → 位置」那一行显示它、并据此开「打开」。</summary>
    internal static string LauncherIniPath() => ConfigPath;

    // DEC-139：`--selftest` 没带第三个参数时的 Strata 落点 = **他自己配的那一格**（ini 里的 `Root=`），
    // 不再是"exe 的父目录"。那条老默认在开发目录里就没对过（`开发目录` 也不是 Strata），只是每次都被显式参数盖掉了；
    // DEC-138 给 `deploy.ps1` 加"推送后在实跑目录跑自检"这一步时没人显式传，于是它量出来的是 `D:\` →
    // Strata 那一族用例一个都没东西可测、跑到"没有模型可测，结束"就退（9 条）。地板那条把它报红了 = 机制对的、默认值是错的。
    // 读不到 / 那一格不存在 → 回出厂默认（`new Settings().Root`），与界面初次启动同一条。
    internal static string StrataRootForSelfTest()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var s = new Settings();
                MainForm.ParseConfigLines(File.ReadAllLines(ConfigPath), s,
                    new Dictionary<string, Settings>(), new MainForm.ConfigParseResult());
                if (!string.IsNullOrWhiteSpace(s.Root) && Directory.Exists(s.Root)) return s.Root;
            }
        }
        catch { }
        return new Settings().Root;
    }

    // 目录里只有旧名 → 改名成新名（用户口径 2026-10-03：直接改名，旧文件不再留）；两样都有 → 用新名、旧文件原样放着
    // （不猜哪份更新）；都没有 → 新名（首次运行）。改名失败（被占用等）时仍按旧名读写，先把用户配置保住。
    internal static string MigrateConfigName(string dir, out bool renamed)
    {
        renamed = false;
        var fresh = Path.Combine(dir, ConfigFileName);
        var legacy = Path.Combine(dir, LegacyConfigFileName);
        if (!File.Exists(fresh) && File.Exists(legacy))
        {
            try { File.Move(legacy, fresh); renamed = true; }
            catch { return legacy; }
        }
        return fresh;
    }

    // 显示名（2026-10-02，DEC-030）：MultiLauncher 混合启动器——窗口标题 / 托盘 / AUMID / csproj Product；
    // 产物 exe（DEC-032）与仓库目录（DEC-035）与设置文件（DEC-037）已陆续跟到新名（原 MixLauncher 提案作废）。
    internal const string AppName = "MultiLauncher 混合启动器";

    Config _config = new();
    Process? _process;
    List<ModelEntry> _models = new();
    List<ModelEntry> _llamaModels = new();   // llama.cpp 侧（只读取自 llama_models.ini；后端在 MainForm.Llama.cs）
    bool _applying;
    bool _reallyExit;
    bool _startHidden;      // 开机自启带 --tray 时：只留在托盘，不弹主窗口

    /// <summary>出图台用（DEC-107）：探针实例不吃「启动时隐藏到托盘」——bin 目录里存着他的真实配置
    /// （HideToTrayOnStart=True），一 Shown 就被 Hide()，屏幕取图那五张（下拉展开态 / 文案滑入）全废；
    /// PrintWindow 那几张看不出问题，所以这笔欠账拖了两轮才查到根上。</summary>
    internal void ClearStartHiddenForTest() => _startHidden = false;
    bool _updating;
    int _winX = -1, _winY, _winW, _winH;
    bool _winMax;

    // 更新状态（由 MainForm.Update.cs 使用）
    string _localVersion = "";
    // Strata 引擎版本（engine/BUILD.json 那一份 = 实际跑着的产物）：左栏「版本」行在 strata 页显示的就是它。
    // 2026-10-15 起 strata 的「源码」「引擎」两行合并成这一行（三页行集全等 = 切页零重排）；
    // 源码版本（CMakeLists 的 project VERSION）只留给更新窗做对比，就是上面那个 _localVersion。
    string _engineVersion = "";
    bool _versionDivergedLogged;        // 两值分叉只在状态翻转时写一行日志（见 MainForm.Models.cs 的 RefreshVersions）
    string _remoteTag = "";
    string _remoteVersion = "";
    string _remoteNotes = "";
    string _remoteZipUrl = "";
    string _remotePublished = "";      // ①：设置页「升级到 v… · 发布 YYYY-MM-DD」那行要用
    bool _hasUpdate;

}
