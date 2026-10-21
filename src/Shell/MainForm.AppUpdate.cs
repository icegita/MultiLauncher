using System.Text.Json.Nodes;

namespace MultiLauncher;

// ============================================================================
// 第 4 条更新链：本程序自己（DEC-202）
// ============================================================================
// 形状照抄 llama 那条（它最简：没有副产物、没有 ini 联动），区别只有一处 —— 它更新的是**壳自己的 exe**，
// 所以后面还要一段"换掉自己"（第 3 批接上）；这一批只落状态链 + 探测 + 那一面（AppUpdateCard）。
//
// 三条硬边界（别踩）：
//   · **不进模块表**：`ModuleView` 是引擎后端的能力面（40+ required，还带模块开关 / 状态灯 / 卡1 格子）；
//     「本程序」塞进去会把这些全拖进来。它只实现 `IUpdatePane`（更新窗真正要的那几项）。
//   · **探测每进程一次**：启动后台探一次；失败静默（只进日志）。手动入口（页头徽标 / 关于页「更新…」/ 托盘）
//     一律真查，但 30 秒内不重复查（GitHub 未认证 API 60 次/小时的限流）。
//   · **闸门先于功能**：自检与出图台**一次都不联网**（`AppUpdateProbeDisabledForTest`），
//     也不许替换 exe / 写 helper / `Application.Exit`（`AppSelfReplaceDisabledForTest`）。入口在那两处点名。
public partial class MainForm
{
    /// <summary>更新窗字典与日志里的身份。「本程序」不在模块表里，所以给它一个不会与模块键撞的键。</summary>
    internal const string AppUpdateKey = "app";

    /// <summary>默认更新源（**网页地址**形状 —— 用户口径「不要用 api 那个」；界面上也只展示这一种）。</summary>
    internal const string AppDefaultRepoUrl = "https://github.com/icegita/MultiLauncher";
    /// <summary>默认更新源解析出来的 api 基址（与 <see cref="AppRepoApiOf"/> 同一套算式，别另写一份）。</summary>
    internal static readonly string AppDefaultRepoApi = "https://api.github.com/repos/icegita/MultiLauncher";

    // ---------------- 闸门与证人位（按进程收口，入口点名：SelfTest.RunBody / UiPreviewForm.Run） ----------------

    /// <summary>自检 / 出图台：**一次都不联网**探本程序的新版本。</summary>
    internal static bool AppUpdateProbeDisabledForTest;
    /// <summary>自检 / 出图台：**绝不替换 exe**、绝不写 helper、绝不 `Application.Exit`（第 3 批起才有行为）。</summary>
    internal static bool AppSelfReplaceDisabledForTest;
    /// <summary>证人位：探测真的发起了几次（闸开着时必须恒 0 —— 与 `GpuQueryCallsForTest` 同族）。</summary>
    internal static int AppProbeRunsForTest;
    /// <summary>证人位：闸挡下了几次（判据要能分开"没走到"与"走到了被挡"）。</summary>
    internal static int AppProbeSkippedForTest;
    // 注意：替换那一位证人（AppReplaceRunsForTest）**留到第 3 批**再加 —— 替换动作真正落笔时才该 ++，
    // 提前挂一个"走一次就 +1 但其实没替换"的计数 = 假证人（本仓最忌假绿，见 DEC-098/100 那一族）。

    // ---------------- 状态（与三条模块链各记一份，互不影响） ----------------

    bool _appUpdating;
    string _appBusy = "";
    bool _appHasUpdate, _appSkippedShown;
    string _appRemoteTag = "", _appRemoteNotes = "", _appRemoteAssetUrl = "", _appRemoteAssetName = "", _appRemotePublished = "";
    string _appRemoteShaUrl = "", _appRemoteShaName = "";
    long _appRemoteAssetSize;
    string _appMemo = "";
    Color _appMemoColor = UiTheme.TextMuted;
    string _appPhase = "";
    double _appFrac = -1;
    volatile bool _appCancelWanted;
    bool _appProbedThisRun;                     // 每进程一次（DEC-202 §6）
    DateTime _appLastCheckUtc = DateTime.MinValue;

    AppUpdateCard? _appPane;

    /// <summary>「本程序」那一面（更新窗的 IUpdatePane 实现，单例 —— 它不存状态，状态都在 MainForm 上）。</summary>
    internal IUpdatePane AppPane => _appPane ??= new AppUpdateCard(this);

    // ---------------- 纯函数（自检直接喂样本断言，不联网） ----------------

    /// <summary>「更新源」那一格 → api 基址。空 / 认不出 owner/repo = **回落默认**（口径同 <see cref="StrataRepoApiOf"/>：
    /// 填坏一格不该让整条链瘫痪）。解析走 <see cref="LlamaReleasesApi"/> 那一个家，别再抄一份正则。</summary>
    internal static string AppRepoApiOf(string? url)
    {
        var api = LlamaReleasesApi(url ?? "").Split('?')[0].TrimEnd('/');
        int cut = api.IndexOf("/releases", StringComparison.OrdinalIgnoreCase);
        return cut > 0 ? api[..cut] : AppDefaultRepoApi;
    }

    /// <summary>`releases/latest` 那份 JSON → 这一版要下的两只资产（exe + 同名 `.sha256`）。
    /// 挑不齐就返回空（= 这一版不给更新：**宁可等下一版，也不放弃校验**，DEC-202 §3）。
    /// draft 不要（`latest` 本来就不给 draft，这里再兜一层，纯函数才验得了）。</summary>
    internal static AppReleaseInfo ParseAppRelease(string json)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject r) return default;
            if (ReadBool(r["draft"])) return default;
            var tag = r["tag_name"]?.GetValue<string>() ?? "";
            if (tag.Length == 0) return default;
            if (r["assets"] is not JsonArray assets) return default;
            string exeName = "", exeUrl = "", shaName = "", shaUrl = "";
            long size = 0;
            foreach (var a in assets)
            {
                var name = a?["name"]?.GetValue<string>() ?? "";
                var url = a?["browser_download_url"]?.GetValue<string>() ?? "";
                if (name.Length == 0 || url.Length == 0) continue;
                // 只认发行产物的命名（Actions 打 tag 出 MultiLauncher-<tag>-windows-x64.exe + 同名 .sha256）
                if (!name.StartsWith("MultiLauncher-", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    exeName = name; exeUrl = url; size = ReadLong(a?["size"]);
                }
                else if (name.EndsWith(".exe.sha256", StringComparison.OrdinalIgnoreCase))
                {
                    shaName = name; shaUrl = url;
                }
            }
            // 判定顺序有讲究：`.sha256` 也以 `.exe` 结尾吗？不 —— 它结尾是 `.exe.sha256`，
            // 上面第一条 EndsWith(".exe") 不会命中它（自检那条"别把 .sha256 当 exe"就是钉这个）。
            if (exeName.Length == 0 || shaName.Length == 0) return default;
            if (!string.Equals(shaName, exeName + ".sha256", StringComparison.OrdinalIgnoreCase)) return default;
            return new AppReleaseInfo(tag, r["body"]?.GetValue<string>() ?? "",
                r["published_at"]?.GetValue<string>() ?? "", exeName, exeUrl, shaName, shaUrl, size);
        }
        catch { return default; }     // 解析不了按"没探到"处理，界面给检查失败
    }

    /// <summary>`.sha256` 资产的内容 → 那一串 hash。GitHub 上的通行形状是 `&lt;hash&gt;  &lt;name&gt;` 两段式，
    /// 但 Windows 的 `certutil` 会多给一行头部、`sha256sum` 可能只给 hash —— 所以只认"第一个 64 位十六进制串"，
    /// 不按列切（按列切会在多一行的场合整段失效）。认不出 = 空串（调用方按"校验不过"处理）。</summary>
    internal static string ParseSha256File(string? text)
    {
        var m = System.Text.RegularExpressions.Regex.Match(text ?? "", @"\b[0-9a-fA-F]{64}\b");
        return m.Success ? m.Value.ToLowerInvariant() : "";
    }

    /// <summary>下回来的 exe 自报的版本号与 tag 一致吗（DEC-067「验内容身份」的同族：sha 拦住传输，
    /// 这一条拦住"资产挂错/串包"）。两档空都算不一致 —— 身份验不了就不许动手。</summary>
    internal static bool AppExeVersionMatches(string? fileVersion, string? tag)
    {
        var want = (tag ?? "").Trim().TrimStart('v', 'V');
        var got = (fileVersion ?? "").Trim();
        int plus = got.IndexOf('+');
        if (plus >= 0) got = got[..plus];
        if (want.Length == 0 || got.Length == 0) return false;
        return CompareVersion(got, want) == 0;
    }

    // ---------------- 界面回显（与另三条链共用同一个刷新广播） ----------------

    internal string AppVersionForSettings() => VersionTag();

    internal (string Text, Color Color) AppUpdateStatusForSettings()
    {
        if (_appBusy.Length > 0) return (_appBusy, Color.DarkOrange);
        if (_appMemo.Length > 0) return (_appMemo, _appMemoColor);
        return ("", UiTheme.TextMuted);
    }

    /// <summary>关于页「更新」那一行的值（方案 §4.2）：`-` / `已是最新 v0.1.8.3` / `发现新版本 v0.1.8.5`（品牌绿）/
    /// `检查失败`（Firebrick）。与更新窗第二行同一个数据源 —— 别再算第二套。</summary>
    internal (string Text, Color Color) AppUpdateRowForSettings()
    {
        var (t, c) = AppUpdateStatusForSettings();
        return t.Length > 0 ? (t, c) : ("-", UiTheme.TextMuted);
    }

    internal UpdateOffer AppOfferForSettings()
    {
        if (_appRemoteTag.Length == 0 || (!_appHasUpdate && !_appSkippedShown))
            return new UpdateOffer(false, "", "", "跳过", "", -1, false);
        var when = _appRemotePublished.Length >= 10 ? _appRemotePublished[..10] : "";
        var ver = _appRemoteTag + (when.Length > 0 ? " · 发布 " + when : "");
        var note = _appHasUpdate ? "" : "这个版本你之前跳过一次（记在 MultiLauncher.ini 的 AppSkipVersion）";
        return new UpdateOffer(true, ver, note, _appHasUpdate ? "跳过" : "取消跳过", _appPhase, _appFrac, AppCancellable);
    }

    internal bool AppUpdateBusy => _appUpdating;
    /// <summary>这一进程探到过没有（开窗即查的"不必再查"判据）。口径与三条模块链的 `UpdateOfferSeen` 一致 =
    /// 「手上有没有远端版本信息」，不是「有没有要展示的 offer」—— 后者"已是最新"时是 false，会导致每次开窗都重查。</summary>
    internal bool AppOfferSeen => _appRemoteTag.Length > 0;
    internal bool AppCancellable => _appUpdating && !_appCancelWanted && PhaseCancellable(_appPhase);

    void SetAppInfo(string text, Color color)
    {
        if (InvokeRequired) { try { BeginInvoke(new Action(() => SetAppInfo(text, color))); } catch { } return; }
        _appMemo = text;
        _appMemoColor = color;
        if (text.Length > 0) Log("本程序更新状态：" + text);
        PokeUpdateUi();
    }

    void FinishAppCheck()
    {
        if (InvokeRequired) { try { BeginInvoke(new Action(FinishAppCheck)); } catch { } return; }
        _appUpdating = false;
        _appBusy = "";
        _appPhase = ""; _appFrac = -1;
        _appCancelWanted = false;
        PokeUpdateUi();
    }

    void ClearAppOffer()
    {
        _appHasUpdate = false;
        _appSkippedShown = false;
        _appRemoteTag = _appRemoteNotes = _appRemoteAssetUrl = _appRemoteAssetName = _appRemotePublished = "";
        _appRemoteShaUrl = _appRemoteShaName = "";
        _appRemoteAssetSize = 0;
        PokeUpdateUi();
    }

    // ---------------- 探测 ----------------

    /// <summary>探测闸与"每进程一次 / 30 秒防连点"的纯函数（自检喂样本，不联网也断得了）。
    /// `force` = 手动入口（页头徽标 / 关于页 / 托盘 / 开窗）；`sinceMs` = 距上一次查过去了多少毫秒。</summary>
    internal static (bool Go, string Why) AppProbeGate(bool force, bool probedThisRun, double sinceMs)
    {
        if (force)
            return sinceMs >= 0 && sinceMs < 30_000 ? (false, "刚查过（30 秒内不重复查，挡 GitHub 未认证 API 的限流）") : (true, "");
        return probedThisRun ? (false, "这一轮已经探过一次（每进程一次）") : (true, "");
    }

    /// <summary>本程序那条链的"查一次"。force=false = 启动后台那一次（每进程只探一次、失败静默）；
    /// force=true = 手动入口（页头徽标 / 关于页 / 托盘 / 开窗），必真查，但 30 秒内不重复查。</summary>
    internal void CheckAppUpdate(bool force = false)
    {
        // 闸门在最前面（DEC-202 §4：顺序反了会真炸 —— 自检一趟就把 GitHub 打满 / 把人家的 exe 换掉）
        if (AppUpdateProbeDisabledForTest) { AppProbeSkippedForTest++; return; }
        if (_appUpdating) return;
        var (go, why) = AppProbeGate(force, _appProbedThisRun, (DateTime.UtcNow - _appLastCheckUtc).TotalMilliseconds);
        if (!go) { Log("本程序更新：" + why); return; }

        _appProbedThisRun = true;
        _appLastCheckUtc = DateTime.UtcNow;
        AppProbeRunsForTest++;

        var api = AppRepoApiOf(_config.Current.AppUpdateUrl);
        // 常规探测**不写日志**（他 2026-10-21 点名：每次启动都刷两行「$ 检查本程序更新…」「已是最新…」太吵）。
        // 失败与"发现新版本"仍要留痕 —— 那两条是事件，不是流水。
        _appUpdating = true;
        _appBusy = "检查中…";
        SetAppInfo("", UiTheme.TextMuted);
        ClearAppOffer();
        bool quiet = !force;                     // 启动那一次失败静默（只进日志）
        Task.Run(() =>
        {
            var tmp = Path.Combine(TempDir(), "app-release.json");
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* 留着也无妨 */ }
            string error;
            if (!Net.Download(api + "/releases/latest", tmp, AppVenvPython(), out error))
            {
                Log("检查本程序更新失败（网络不可达？）: " + error);
                if (!quiet) SetAppInfo("检查失败", Color.Firebrick);
                FinishAppCheck();
                return;
            }
            var info = ParseAppRelease(SafeReadText(tmp));
            if (string.IsNullOrEmpty(info.Tag))
            {
                Log("检查本程序更新失败：发布信息不完整（没找到同名 .sha256 或资产名不是发行产物）");
                if (!quiet) SetAppInfo("检查失败", Color.Firebrick);
                FinishAppCheck();
                return;
            }
            var remote = info.Tag.TrimStart('v', 'V');
            var local = VerNumber();
            if (CompareVersion(remote, local) <= 0)
            {
                // 「已是最新」也不写日志（同上：常态流水静默）；手动查那一路的反馈在界面那一行（SetAppInfo）
                if (!quiet) SetAppInfo("已是最新 " + VersionTag(), Color.Green);
                FinishAppCheck();
                return;
            }
            var skipped = _config.Current.AppSkipVersion.Trim();
            bool isSkipped = skipped.Length > 0 && remote.StartsWith(skipped, StringComparison.OrdinalIgnoreCase);
            BeginInvoke(new Action(() =>
            {
                if (IsDisposed) return;
                _appRemoteTag = info.Tag;
                _appRemoteNotes = info.Notes;
                _appRemotePublished = info.Published;
                _appRemoteAssetName = info.AssetName;
                _appRemoteAssetUrl = info.AssetUrl;
                _appRemoteShaName = info.ShaName;
                _appRemoteShaUrl = info.ShaUrl;
                _appRemoteAssetSize = info.Size;
                _appHasUpdate = !isSkipped;
                _appSkippedShown = isSkipped;
                // 三处（页头徽标 / 关于页那一行 / 更新窗第二行）都读这一份 —— 文案一处在 SetAppInfo 这里定
                SetAppInfo(isSkipped ? $"{info.Tag}（你跳过了一次）" : "发现新版本 " + info.Tag,
                    isSkipped ? UiTheme.TextMuted : Color.Green);
                PokeUpdateUi();
            }));
            FinishAppCheck();
        });
    }

    static string SafeReadText(string path)
    {
        try { return File.ReadAllText(path); } catch { return ""; }
    }

    /// <summary>下载失败时退 Python 的那条后路要一个解释器：这里用 Strata 那份 .venv（本机已有的）
    /// —— 没有就返回空串，<see cref="Net.Download"/> 会只走 HttpClient。**不因此要求装 Python**。</summary>
    string AppVenvPython()
    {
        try
        {
            var p = Path.Combine(_config.Current.Root.Trim(), ".venv", "Scripts", "python.exe");
            return File.Exists(p) ? p : "";
        }
        catch { return ""; }
    }

    // ---------------- 页头徽标（方案 §4.1） ----------------

    // 为什么不在标题栏：系统标题栏那串字是 DWM 画的**整串单色**，没法只把其中一段染绿；
    // 要做只能自绘整条标题栏（拖拽/缩放/贴边/最大化/多显示器 DPI/阴影/系统菜单全得自己实现）—— 已明确不做。
    Label? _lblAppBadge;                        // 「新版本 vX.Y.Z」（9pt 品牌绿）
    TinyUpdate? _tinyAppBadge;                  // 12px 上箭头（与左栏那枚同族控件，NewVersion=true 即品牌绿）
    bool _appBadgeWanted;                       // 意图位：探到新版才该出面（窗没显示时 Control.Visible 恒 false，自检读它）
    bool _appBadgeShown;                        // 这一帧真的出面了（窄到连图标都放不下 → false）
    bool _appBadgeNarrow;                       // 窄态：只留图标，版本号进提示

    /// <summary>页头那枚徽标跟着自更新那一位走（探到新版 / 升级完成当场变，不靠重开界面）。
    /// 挂在 <see cref="PokeUpdateUi"/> 那条广播上，与左栏小箭头、更新窗、设置页同一节拍。</summary>
    void RefreshAppBadge()
    {
        if (IsDisposed) return;
        if (InvokeRequired) { try { BeginInvoke(new Action(RefreshAppBadge)); } catch { } return; }
        if (_lblAppBadge is null || _tinyAppBadge is null) return;
        bool want = _appHasUpdate;
        bool changed = want != _appBadgeWanted;
        _appBadgeWanted = want;
        _lblAppBadge.Text = "新版本 " + _appRemoteTag;
        var tip = want ? "点开更新窗（本程序 " + VersionTag() + " → " + _appRemoteTag + "）" : "";
        Tip.SetOneLine(_lblAppBadge, tip);
        _tinyAppBadge.SetTip(tip.Length > 0 ? tip : "打开更新窗（本程序）");
        _tinyAppBadge.NewVersion = want;
        // 显隐只在 changed 时重排 —— 摆位那一处（pageHead.Layout）是唯一决定出面/窄态的家
        if (changed) _pageHead?.PerformLayout();
    }



    /// <summary>徽标这一帧怎么摆（纯函数，自检喂样本；方案 §4.1 的窄态兜底就是这三行）：
    /// 放得下「文字 + 图标」→ 全出；只放得下图标 → 只留图标（版本号进提示）；连图标都放不下 → 整块不出。
    /// 抽成纯函数是为了让"兜底真的会退"这件事可断言 —— 靠拖窗口量到那一档得先破最小宽。</summary>
    internal static (bool Show, bool Narrow) AppBadgeFit(int avail, int wideW, int iconW)
    {
        if (wideW <= avail) return (true, false);
        if (iconW <= avail) return (true, true);
        return (false, false);
    }

    /// <summary>页头徽标的摆位（与 pageHead 其它控件同一段 Layout 里调）：**先量后摆**。
    /// 960 最小客户宽下放不下 → 退「只留图标」（版本号进提示），再放不下 → 整块不出（方案 §4.1）。
    /// 宽度一律用 <see cref="TextRenderer.MeasureText"/> 量，不靠肉眼。</summary>
    void LayoutAppBadge(int top)
    {
        if (_lblAppBadge is null || _tinyAppBadge is null || _pageHead is null) return;
        int gap = UiTheme.S(this, 12);
        // 图标尺寸直接取控件自己的宽（与左栏那枚同一档 S(12)）—— 圆环按 min(W,H) 画，两边必须一致
        int iconW = _tinyAppBadge.Width > 0 ? _tinyAppBadge.Width : UiTheme.S(this, 12);
        int iconGap = UiTheme.S(this, 4);
        int segRight = _segModules.Left + _segModules.Width;
        int avail = _btnOpenConfig.Left - gap - (segRight + gap);      // 徽标可用宽度（左右各留一个 gap）
        // **宽度要按 Label 的真实排版宽度算**（AutoSize 用的就是 PreferredWidth）。
        // 以前这里用 TextRenderer.MeasureText(NoPadding)：它比 Label 实际排版窄几个像素，
        // 于是文字块压到了图标上、图标左边被吃掉 —— 他看着就是"图标被裁切/宽度不够"（2026-10-21）。
        int textW = _lblAppBadge.PreferredWidth > 0 ? _lblAppBadge.PreferredWidth
            : TextRenderer.MeasureText(_lblAppBadge.Text, _lblAppBadge.Font,
                new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width;
        int wideW = textW + iconGap + iconW;
        var (show, narrow) = _appBadgeWanted ? AppBadgeFit(avail, wideW, iconW) : (false, false);
        int w = narrow ? iconW : wideW;
        int bx = _btnOpenConfig.Left - gap - w;
        _lblAppBadge.Location = new Point(bx, top + (RowH - _lblAppBadge.Height) / 2);
        // 图标紧跟文字**实际**右缘（别用 bx + textW：那还是算出来的），宁可多让几像素
        int iconX = narrow ? bx : Math.Max(bx + textW + iconGap, _lblAppBadge.Right + iconGap);
        if (!narrow && iconX + iconW > _btnOpenConfig.Left - gap)       // 让完还不够 → 退窄态，别压按钮
        {
            narrow = true; show = true;
            iconX = _btnOpenConfig.Left - gap - iconW;
        }
        _appBadgeShown = show;
        _appBadgeNarrow = narrow;
        _tinyAppBadge.SetBounds(iconX, top + (RowH - iconW) / 2, iconW, iconW);
        _lblAppBadge.Visible = show && !narrow;
        _tinyAppBadge.Visible = show;
    }

    // 自检 / 出图台出口：徽标的两态与几何（窗没显示时 Control.Visible 恒 false，所以这里读**意图位**）
    internal string AppBadgeStateForTest() =>
        (_appBadgeWanted ? "want" : "-") + "/" + (_appBadgeShown ? "show" : "-") + "/" +
        (_appBadgeNarrow ? "narrow" : "wide") + "|" + (_lblAppBadge?.Text ?? "");

    /// <summary>量出来的三个数（都 >= 0 才算没压上去）：
    /// `OverlapLeft` = 最左那枚可见控件的左缘到分段控件右缘的距离；`GapToButton` = 最右那枚的右缘到「打开配置」左缘的距离；
    /// `LabelIconGap` = 文字右缘到图标左缘的距离（**这就是他说的"留给图标的宽度"**，负数 = 文字压到图标上）。</summary>
    internal (int OverlapLeft, int GapToButton, int LabelIconGap) AppBadgeGeomForTest()
    {
        if (_lblAppBadge is null || _tinyAppBadge is null) return (int.MinValue, int.MinValue, int.MinValue);
        int segRight = _segModules.Left + _segModules.Width;
        var left = _appBadgeNarrow ? (Control)_tinyAppBadge : _lblAppBadge;
        return (left.Left - segRight, _btnOpenConfig.Left - _tinyAppBadge.Right, _tinyAppBadge.Left - _lblAppBadge.Right);
    }


    /// <summary>出图 / 自检出口：徽标图标的位置与尺寸（`w×h` 不等就是被谁改窄了 —— 圆环会被 `min(W,H)` 缩小画）。</summary>
    internal string AppBadgeIconGeomForTest() =>
        _tinyAppBadge is null ? "(没有图标控件)"
            : $"{_tinyAppBadge.Width}x{_tinyAppBadge.Height} @({_tinyAppBadge.Left},{_tinyAppBadge.Top}) 父={_tinyAppBadge.Parent?.Width}";

    // 页头的裁切矩形复用现成的 PageHeadBoundsInClient()（MainForm.Layout.cs，--uiprobe sidebar 那一族在用）。

    // ---------------- 入口 ----------------

    /// <summary>开（或唤起）「本程序」那一扇更新窗（与另四扇同形：开窗即查）。</summary>
    internal void OpenAppUpdateWindow(IWin32Window? owner = null)
    {
        owner ??= this;
        if (_updateForms.TryGetValue(AppUpdateKey, out var f) && !f.IsDisposed)
        {
            if (!f.Visible) f.Show(owner);
            f.Activate(); f.BringToFront();
        }
        else
        {
            f = new UpdateForm(this, AppPane, "win");
            CenterOverOwner(f, owner);
            _updateForms[AppUpdateKey] = f;
            // 关窗就摘键（DEC-061 那条：条目留着会强引用已销毁的窗）
            f.FormClosed += (_, _) => { if (_updateForms.TryGetValue(AppUpdateKey, out var cur) && ReferenceEquals(cur, f)) _updateForms.Remove(AppUpdateKey); };
            f.Show(owner);
        }
        // 开窗即查（手动入口 = 必真查）；这一轮还在忙就不叠第二次
        if (!_appUpdating) CheckAppUpdate(force: true);
    }

    /// <summary>托盘那一项的文字（三态照抄模块项，但**另写一处** —— 别硬塞进 `UpdateMenuLabel` 的 `View(k)`，
    /// 那一头是按模块表认键的，塞进去就是一个 "app 不在表里" 的当场抛）。</summary>
    internal string AppUpdateMenuLabel()
    {
        if (_updateForms.TryGetValue(AppUpdateKey, out var f) && !f.IsDisposed && f.Visible) return "查看更新窗口 · 本程序";
        if (_appUpdating) return "升级中… · 本程序";
        return "检查更新 · 本程序";
    }

    internal bool AppUpdateMenuEnabled() => !_appUpdating;

    // ---------------- 窗里那三枚动作 ----------------

    internal void AppShowUpdateDetail()
    {
        if (_appRemoteTag.Length == 0) { Log("本程序更新：还没有远端版本信息，请先点「检查更新」。"); return; }
        var notes = _appRemoteNotes.Length > 0 ? _appRemoteNotes : "（这次发布没有写更新说明）";
        var res = Dialogs.UpdateDetailDialog(this, _appRemoteTag, VerNumber(), notes);
        if (res == DialogResult.Yes) StartAppUpgrade();
        else if (res == DialogResult.Ignore) AppSkip();
    }

    internal void AppSkip()
    {
        if (_appRemoteTag.Length == 0) return;
        var skipped = _appRemoteTag;             // 取值要在 ClearAppOffer 之前（DEC-061 的老教训）
        _config.Current.AppSkipVersion = _appRemoteTag.TrimStart('v', 'V');
        SaveConfig(commitParams: false);
        SetAppInfo($"已跳过 {skipped}（下次探到它不再提示）", UiTheme.TextMuted);
        ClearAppOffer();
        Toast.Show(this, "已跳过 " + skipped + "（要恢复就在更新窗点「取消跳过」）", Toast.Kind.Info);
    }

    internal void AppUnskip()
    {
        if (_appRemoteTag.Length == 0) return;
        _config.Current.AppSkipVersion = "";
        SaveConfig(commitParams: false);
        _appSkippedShown = false;
        _appHasUpdate = true;
        SetAppInfo($"已取消对 {_appRemoteTag} 的跳过", Color.Green);
        Toast.Show(this, "已取消对 " + _appRemoteTag + " 的跳过，点「更新」即可升级", Toast.Kind.Ok);
        PokeUpdateUi();
    }

    internal void RequestCancelAppUpdate()
    {
        _appCancelWanted = true;
        Log("已请求取消本程序更新：等当前这一步停下（只有下载那一段能停；校验与替换不给停）。");
        PokeUpdateUi();
    }

    // 点窗里那枚「更新」= `StartAppUpgrade()`：准入四条过了才开跑。
    // **实现在另一支（`MainForm.AppUpdate.Replace.cs`，第 3 批的"下载 → 校验 → 换掉自己"那一段）** ——
    // 本文件已到 500 行门槛，那一族整段拆出去了；调用点与行为没有分叉。

    // ---------------- 自检出口（喂样本 / 造态，不联网） ----------------

    internal void SetAppOfferForTest(string tag, string published, bool skipped)
    {
        _appRemoteTag = tag;
        _appRemotePublished = published;
        _appRemoteNotes = "自检用的假说明";
        _appRemoteAssetName = tag.Length > 0 ? "MultiLauncher-" + tag + "-windows-x64.exe" : "";
        _appRemoteShaName = _appRemoteAssetName + ".sha256";
        _appHasUpdate = !skipped;
        _appSkippedShown = skipped;
        SetAppInfo(skipped ? $"{tag}（你跳过了一次）" : "发现新版本 " + tag, skipped ? UiTheme.TextMuted : Color.Green);
        PokeUpdateUi();
    }

    internal void ClearAppOfferForTest() => ClearAppOffer();

    internal void SetAppBusyForTest(bool on) { _appUpdating = on; _appBusy = on ? "检查中…" : ""; PokeUpdateUi(); }
}

/// <summary>`releases/latest` 里本程序要的那一份（空 = 这一版不给更新）。
/// 字段名与 GitHub 的实际命名对齐（tag / body / published_at / assets[]），便于自检直接喂真 JSON 样本。</summary>
internal readonly record struct AppReleaseInfo(
    string Tag, string Notes, string Published,
    string AssetName, string AssetUrl, string ShaName, string ShaUrl, long Size);
