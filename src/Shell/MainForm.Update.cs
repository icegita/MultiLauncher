using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;

namespace MultiLauncher;

// 自动更新：查 GitHub 上 Niko1221/Strata 的最新发布，一键把新源码覆盖到 Strata 目录
// （保留本机的东西：.venv / engine / diag / 运行配置 / 日志），再让 setup 把官方预编译引擎同步到
// 与源码同版本（update_installed_engine —— 与 START-HERE.bat 走同一条路）。
// 2026-10-03（DEC-036）：0.1.35 起 WDDM 修复已上游化（写在运行配置的 env STRATA_ARENA_PIN_GIB 里），
// 官方预编译引擎就是正解（实测比本机重编快近一倍）；diag\update-engine.bat 降为后路（官方引擎再踩
// 本机特有的坑时才手动用）。
/// 设置页「升级到 …」那一行的数据源（DEC-048/053/054）。
/// Phase/Fraction 只在忙时有值：Fraction < 0 表示"知道在做什么、不知道做到哪"（进度条来回扫）。
internal sealed record UpdateOffer(bool Show, string Version, string Note, string SkipLabel, string Phase, double Fraction, bool CanCancel);

public partial class MainForm
{
    const string RepoApi = "https://api.github.com/repos/Niko1221/Strata";

    /// <summary>DEC-110④：Strata 的更新源搬进设置页那一格（以前只有 <see cref="RepoApi"/> 这个字面量）。
    /// 留空或认不出 owner/repo = **回落到默认那一头**，不因为一格填坏把整条检查弄瘫（口径同 DEC-067"没这条校验就当没这条"）。
    /// 解析走 <see cref="LlamaReleasesApi"/> 那一个家（网页地址与 api 地址都吃），别再抄一份正则。</summary>
    internal static string StrataRepoApiOf(string? url)
    {
        var api = LlamaReleasesApi(url ?? "").Split('?')[0].TrimEnd('/');
        int cut = api.IndexOf("/releases", StringComparison.OrdinalIgnoreCase);
        return cut > 0 ? api[..cut] : RepoApi;
    }

    static readonly string[] PreserveTop =
        { ".venv", "engine", "diag", "build-cuda134", "build-vision", "build-strata", "StrataLauncher", "MultiLauncher", "Strata-data", ".git", "__pycache__" };

    // ①（2026-10-03 反馈）：设置页与托盘那颗按钮**只做检查**，改名「检查更新」。
    // 以前它一身两职：内存里有"上次探到新版"的标记就弹升级菜单，否则才去检查——点下去之前不知道走哪条路。
    // 现在升级/跳过/查看内容由「发现新版本」那一行长出来的三个按钮承担（见 SettingsForm 的 OfferRow）。
    internal void UpdateClick()
    {
        if (_updating) return;
        CheckUpdate();
    }

    // 设置页那一行「升级到 …」的数据源（①）：Show=false 时整行不出现（没查过、已是最新、或已经升完）。
    // 探到的正是被跳过的那个版本时行照样出，只是第三个按钮改读「取消跳过」。
    // ---------------- 进度通道（DEC-054） ----------------
    // 阶段名 + 完成度：两个模块各一套，谁忙谁报，忙完清空（进度条跟着消失）。
    // Fraction < 0 = "知道在做什么、不知道做到哪"（下载没给 Content-Length、同步引擎那种）→ 条子来回扫。
    string _strataPhase = "", _llamaPhase = "";
    double _strataFrac = -1, _llamaFrac = -1;

    void ReportProgress(bool strata, string phase, double frac)
    {
        if (InvokeRequired) { try { BeginInvoke(new Action(() => ReportProgress(strata, phase, frac))); } catch { } return; }
        if (strata) { _strataPhase = phase; _strataFrac = frac; }
        else { _llamaPhase = phase; _llamaFrac = frac; }
        PokeUpdateUi();
    }

    void ClearProgress(bool strata) => ReportProgress(strata, "", -1);

    // ---------------- 取消通道（DEC-055，用户问"更新中途能不能停"） ----------------
    // 只在下发/解压这类「还没动本机现有东西」的阶段允许停：
    //   · 下载 —— 半截 zip 直接删，现有安装一点没碰；
    //   · 解压 —— 只解压到自己的临时目录，删掉即可；
    //   · 覆盖到 Strata 目录 —— **不给停**：中途停会留下半新半旧的源码（虽然备份在、能回退，但比"跑完"更糟）；
    //   · 同步官方预编译引擎 —— 不给停（python 在往 engine 里写文件，同样的道理）。
    // 所以按钮只在这些可停阶段出现，而不是"任何时候都能点一下试试"。
    // DEC-061：取消位**按模块各一份**。DEC-058 起一模块一窗、两条流程互不拦（DoUpgrade 只看 _updating、
    // LlamaUpgrade 只看 _llamaUpdating），以前共用一个 _cancelWanted 会让 llama 窗里点的小 X 把 Strata 的
    // 下载一起取消；而且任一流程收尾时的复位会吞掉另一流程刚收到的取消请求。
    volatile bool _strataCancelWanted, _llamaCancelWanted;

    internal static bool PhaseCancellable(string phase) =>
        phase.StartsWith("下载", StringComparison.Ordinal) || phase.StartsWith("解压", StringComparison.Ordinal);

    internal bool UpdateCancellable => _updating && PhaseCancellable(_strataPhase);
    internal bool LlamaCancellable => _llamaUpdating && PhaseCancellable(_llamaPhase);

    /// 用户点了「取消」：只置该模块自己的标志，真正的停在下一个检查点（每块下载 / 每个条目）落地
    internal void RequestCancelUpdate(bool llama)
    {
        if (llama) _llamaCancelWanted = true;
        else _strataCancelWanted = true;
        if (llama) LogL("已请求取消 llama.cpp 更新：等当前这一步停下（下载/解压能停；覆盖与同步引擎不能停）。");
        else LogS("已请求取消 Strata 更新：等当前这一步停下（下载/解压能停；覆盖与同步引擎不能停）。");
        PokeUpdateUi();
    }

    /// 自检出口：看某一模块的取消位（断言两条流程互不牵连）
    internal bool CancelWantedForTest(bool llama) => llama ? _llamaCancelWanted : _strataCancelWanted;
    // DEC-138（E-8）：证人位 =「**真收尾**里那两行归零跑了几次」。自检断的是「计数 +2」加上「两位都灭了」，
    // 于是把 FinishUpgrade / FinishLlamaUpgrade 里那句 `_xxxCancelWanted = false;` 删掉就红。
    // 以前那条用例调的是下面这个测试专用接缝（`ClearCancelFlagsForTest`）= **自己清、再断自己清干净了**，
    // 真收尾里那两行删掉它照样绿 —— 正是 DEC-098/100 那一族「机制写了没接上」的判据版。接缝已删。
    internal static int CancelResetsForTest;

    // 检查点用的谓词：strata=true 读 Strata 那份，false 读 llama 那份
    Func<bool> CancelRequested(bool strata) => () => strata ? _strataCancelWanted : _llamaCancelWanted;

    // 带进度的解压：ZipFile.ExtractToDirectory 不报进度，而"解压"恰恰是用户看得见的那段等待（DEC-054）。
    // 顺手加了 zip-slip 防护：包内条目不许用 .. 跑到目标目录外面。
    static bool ExtractWithProgress(string zip, string dest, Action<int, int> onEntry, Func<bool>? cancel = null)
    {
        using var z = System.IO.Compression.ZipFile.OpenRead(zip);
        int n = Math.Max(1, z.Entries.Count), i = 0;
        var root = Path.GetFullPath(dest) + Path.DirectorySeparatorChar;
        foreach (var e in z.Entries)
        {
            i++;
            if (cancel is not null && cancel()) return false;        // 已取消：调用方删掉半截解压目录
            var path = Path.GetFullPath(Path.Combine(dest, e.FullName));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new IOException("压缩包里有不安全的条目路径：" + e.FullName);
            if (e.FullName.EndsWith('/') || e.FullName.EndsWith(Path.DirectorySeparatorChar.ToString())) { onEntry(i, n); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? dest);
            e.ExtractToFile(path, true);
            onEntry(i, n);
        }
        return true;
    }

    /// ①④：检查/升级/回退进行中——设置页那几枚按钮要按实际状态灰置，不是点了没反应
    internal bool UpdateUiBusy => _updating;

    // DEC-067：源码包（zipball）没有官方摘要可 pin —— GitHub 的 zipball 是现场生成的，release 只给二进制
    // 资产挂 digest。所以这条链改验**内容身份**：解压出来的 CMakeLists.txt 里 `project(strata VERSION x)`
    // 必须等于刚探到的那个 tag。抓得住"下错内容"（URL 指错、门户页伪装 200、CDN 串包），
    // 传输被截断则由 zip 的 CRC 与中央目录挡在解压那一关。
    // 返回 false = 对不上（found 里给读到的值，读不到就是空串），调用方必须收手、一个文件都不覆盖。
    internal static bool SourceVersionMatches(string srcRoot, string wantVersion, out string found)
    {
        found = "";
        try
        {
            var text = File.ReadAllText(Path.Combine(srcRoot, "CMakeLists.txt"));
            var m = System.Text.RegularExpressions.Regex.Match(text, @"project\(strata VERSION\s+([\d.]+)");
            if (!m.Success) return false;
            found = m.Groups[1].Value;
            return string.Equals(found.TrimStart('v', 'V'), (wantVersion ?? "").TrimStart('v', 'V'),
                StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    // 自检出口：造一个忙态，断言按钮真的置灰了
    internal void SetUpdateBusyForTest(bool on) { _updating = on; PokeUpdateUi(); }
    // DEC-138（E-8）：自检走**真收尾**这两个口（取代以前那个"自己清、再断自己清干净"的 ClearCancelFlagsForTest）。
    // stage 传 "cancel" 那一档：只 SetUpdateInfo + Toast（toast 由 DEC-127⑥ 那道闸挡着）+ 刷界面，不弹窗、不碰家底。
    internal void FinishUpgradeForTest(bool ok, string stage) => FinishUpgrade(ok, stage);
    internal void FinishLlamaUpgradeForTest(bool ok, string dst) => FinishLlamaUpgrade(ok, dst);

    /// 出图/自检出口：直接放一条「更新状态」话术（已是最新 / 检查失败那种），不联网也能拍到真实那一行
    internal void SetUpdateMemoForTest(string text, bool danger)
    {
        _updateMemoText = text;
        _updateMemoColor = danger ? Color.Firebrick : Color.Green;
        PokeUpdateUi();
    }

    // 自检出口：报一个阶段与完成度（frac < 0 = 不确定态），断言进度条与阶段名跟着变
    internal void SetUpdateProgressForTest(string phase, double frac) { _strataPhase = phase; _strataFrac = frac; PokeUpdateUi(); }

    internal UpdateOffer UpdateOfferForSettings()
    {
        if (_remoteTag.Length == 0 || (!_hasUpdate && !_updateSkippedShown))
            return new UpdateOffer(false, "", "", "跳过", "", -1, false);
        var when = _remotePublished.Length >= 10 ? _remotePublished.Substring(0, 10) : "";
        var ver = _remoteTag + (when.Length > 0 ? " · 发布 " + when : "");
        // ①④：这一行不再写「升级会做什么」——docs/.ai/参考-界面与参数对照.md 与 AGENTS 里都有，用户两次说没用；
        // 只有被跳过那种要交代记在哪，因为要靠下面那枚按钮反悔
        string note = _hasUpdate ? "" : "这个版本你之前跳过一次（记在 MultiLauncher.ini 的 SkipVersion）";
        return new UpdateOffer(true, ver, note, _hasUpdate ? "跳过" : "取消跳过", _strataPhase, _strataFrac, UpdateCancellable);
    }

    // ---------------- 设置窗「维护」组的回显（2026-10-02 反馈） ----------------
    // 本地版本点「更新」前就能看到；检查/升级的进度与结果也回显到「更新状态」行（原来只进日志）。
    // 设置窗打开时 AttachUpdateUiRefresh 登记刷新回调，更新流程各环节 PokeUpdateUi() 催它重读。

    Action? _updateUiRefresh;
    string _updateMemoText = "";
    Color _updateMemoColor = UiTheme.TextMuted;
    string _updateBusy = "";        // "检查中…" / "升级中…" / "回退中…"（流程进行期间）
    bool _updateSkippedShown;       // ①：探到的是"已跳过"的那个版本（行照出，按钮读「取消跳过」）

    // ---------------- 刷新广播（DEC-058 之后可能同时挂多个窗） ----------------
    // 以前只有一个槽位：更新窗一挂就把设置窗挤掉（或反过来），所以改成 +=/=− 的广播。
    internal void AddUpdateUiRefresh(Action cb) => _updateUiRefresh += cb;
    internal void RemoveUpdateUiRefresh(Action cb) => _updateUiRefresh -= cb;
    void PokeUpdateUi()
    {
        foreach (Action? cb in _updateUiRefresh?.GetInvocationList() ?? Array.Empty<Delegate>())
        { try { cb!(); } catch { /* 某个窗正在关 */ } }
        RefreshSidebarUpdateButtons();     // DEC-108：左栏那枚小箭头的灰/绿两态也吃这条广播
        RefreshAppBadge();                 // DEC-202：页头「本程序」那枚徽标同一条节拍
    }

}
