using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;

namespace MultiLauncher;

// 「更新」窗（DEC-058 一模块一窗、开窗即自动查；DEC-107 Ninfer 一后端一窗）：窗体、三行摆位、
// 按钮字态、进度与阶段名、结果回显。2026-10-15 从 MainForm.Update.cs 整段搬出，一行未改。
public partial class MainForm
{
    // ---------------- 「更新」窗（DEC-058：一模块一窗，开窗即自动查；DEC-107：Ninfer 一后端一窗） ----------------
    readonly Dictionary<string, UpdateForm> _updateForms = new();

    /// <summary>这一窗服务哪条链（DEC-107，取代 DEC-097③ 的"一窗两段"）：Strata / llama 恒 "win"；
    /// Ninfer 看当前后端（方块停哪侧就开哪侧的窗）。左栏那枚小按钮与托盘两枚都从它拿参数。</summary>
    internal string UpdateSideFor(string key) =>
        key == NinferKey && ActiveNinferBackend(_config.Current) == NinferIni.BackendWsl ? "wsl" : "win";

    /// <summary>容器键：两个后端各开各的窗（设计稿支点 F："一模块一窗"放宽成"一后端一窗"）。</summary>
    static string UpdateWindowKey(string key, string side) => side == "wsl" ? key + ":wsl" : key;

    /// <summary>打开（或唤起）该模块（该后端）的更新窗。非模态：升级要跑几分钟，模态会把主窗挡死、进度条变成必须盯着。
    /// 同一窗已开着就带到前台补一次检查，不开第二个。
    /// owner = 谁点的（设置窗 / 主窗）；托盘那一路传 null → 挂到主窗上。</summary>
    internal void OpenUpdateWindow(string key, IWin32Window? owner = null, string side = "win")
    {
        // 第 0 轮 DEC-094：键照模块表认（以前这里把 "llama" 之外的都当 strata，加第三个模块会一起漂走）
        string k = View(key).Key;
        owner ??= this;
        var winKey = UpdateWindowKey(k, side);
        if (_updateForms.TryGetValue(winKey, out var f) && !f.IsDisposed)
        {
            if (!f.Visible) f.Show(owner);   // 位置沿用第一次开窗时定下的那一个，不在唤起时改
            f.Activate(); f.BringToFront();
        }
        else
        {
            f = new UpdateForm(this, k, side);
            CenterOverOwner(f, owner);
            _updateForms[winKey] = f;
            // DEC-061：关窗就把字典里的键摘掉。UpdateForm 是 Close→Dispose，条目留着会让已销毁的窗被强引用
            // 一直挂着（下次开窗虽然靠 !IsDisposed 判掉并覆盖，但那一轮的窗回收不了）。
            f.FormClosed += (_, _) => { if (_updateForms.TryGetValue(winKey, out var cur) && ReferenceEquals(cur, f)) _updateForms.Remove(winKey); };
            f.Show(owner);
        }
        // 开窗即开查（DEC-058 定的口径，四扇窗一个形状）。手上这一轮还在忙就别叠第二次。
        // DEC-109：WSL 扇也吃这条 —— 它那条链的"检查"就长在全流程的头上（`NinferWslUpdateFlow` 第一段），
        // 开窗就替用户按一下那枚「更新…」（查到新的照旧弹「更新内容」问；没有 = toast 已是最新）。
        TriggerOpenCheck(View(k), side);
    }

    /// <summary>开窗收尾的那一下（DEC-109 收口成一处，自检可干跑）：
    /// · win 扇（Strata / llama / Ninfer-WIN）= 老形状（忙 / 这一会话探到过就不叠）；
    /// · wsl 扇（Ninfer-WSL）= 跑它那条全流程的检查段 —— 于是"点开窗"与"点窗里那枚「更新…」"同一条路，
    ///   不再出现"开窗之后还要在窗里再点一次才开工"（他 2026-10-08 实跑提的：与另三扇不一致）。
    /// 测试态不用在这里拦：WSL 那条链接入处自带测试闸（`NinferWslChainAllowed`），当场挡下、一次都不进 VM。</summary>
    void TriggerOpenCheck(MainForm.ModuleView v, string side)
    {
        if (v.UpdateBusy()) return;
        if (side == "wsl") { v.Secondary?.OnAction(); return; }
        if (!v.UpdateOfferSeen()) v.CheckUpdateNow();
    }
    /// <summary>自检干跑（不 Show 窗）：开的窗在收尾那一下会不会真的走到检查。</summary>
    internal void TriggerOpenCheckForTest(string key, string side) => TriggerOpenCheck(View(key), side);

    /// 非模态窗的"居中到父窗"只能自己算：**`FormStartPosition.CenterParent` 只对 ShowDialog 生效**，
    /// `Show(owner)` 会静默退回屏幕居中（用户实测抓到：从设置里点「更新…」弹出来在屏幕中间）。
    /// 父窗看不见（主窗藏托盘）时按托盘开设置窗那条既有口径改屏幕居中。
    void CenterOverOwner(Form f, IWin32Window? owner)
    {
        var anchor = owner as Form ?? (owner == this ? this : null);
        // DEC-061：光判 Visible 不够——**最小化的窗 Visible 仍是 true**，而 Bounds 是 (-32000,-32000)，
        // CenterOnOwner 算出负坐标后被夹回工作区 → 窗落在屏幕左上角。主窗最小化时从托盘开更新窗就是这个态。
        if (anchor is { Visible: true } && CenterAnchorUsable(anchor.WindowState))
        {
            f.StartPosition = FormStartPosition.Manual;
            f.Location = CenterOnOwner(anchor.Bounds, f.Size, Screen.FromControl(anchor).WorkingArea);
        }
        else f.StartPosition = FormStartPosition.CenterScreen;
    }

    /// 纯函数：这枚父窗能不能当居中锚点——最小化就不行（退回屏幕居中）。
    /// 最小化的窗 Visible 仍是 true，但 Bounds 在 (-32000,-32000)，直接拿来算会把窗摆到屏幕左上角。
    internal static bool CenterAnchorUsable(FormWindowState state) => state != FormWindowState.Minimized;

    /// 纯算式：把窗摆在父窗正中，再夹回工作区（父窗贴边或比屏幕大时不至于跑出可见范围）
    internal static Point CenterOnOwner(Rectangle owner, Size win, Rectangle working)
    {
        int x = owner.Left + (owner.Width - win.Width) / 2;
        int y = owner.Top + (owner.Height - win.Height) / 2;
        return new Point(Math.Max(working.Left, Math.Min(x, working.Right - win.Width)),
                         Math.Max(working.Top, Math.Min(y, working.Bottom - win.Height)));
    }

    /// 托盘那条菜单的文字：窗开着 / 正在升级时给不同说法，避免出现两个入口互相抢。
    /// DEC-107：Ninfer 的托盘拆成两枚（WIN / WSL 各一），带后缀；另两模块后缀为空 = 文字一字不变。
    internal string UpdateMenuLabel(string key, string side = "win")
    {
        string k = View(key).Key;      // 第 0 轮 DEC-094：照模块表认键
        string sfx = k == NinferKey ? (side == "wsl" ? " · WSL" : " · WIN") : "";
        var winKey = UpdateWindowKey(k, side);
        if (_updateForms.TryGetValue(winKey, out var f) && !f.IsDisposed && f.Visible) return "查看更新窗口" + sfx;
        if (View(k).UpdateBusy()) return "升级中…" + sfx;   // DEC-061：各模块同一句话（忙态位各一份）
        return "检查更新" + sfx;
    }


    // 设置窗「本地版本」行：现读（源码 = CMakeLists 版本、引擎 = engine\BUILD.json 版本）
    internal (string source, string engine) VersionsForSettings()
    {
        var engine = ReadEngineVersion(_config.Current.Root);
        return ("v" + LocalVersionOr("未知"), "v" + (engine.Length > 0 ? engine : "未知"));
    }

    // 设置窗「版本」行右侧的结果位（2026-10-02 反馈修订）：没查过/没在忙 → **空**（不显示"未检查"）；
    // 点「更新」后就地显示进度与结果
    internal (string text, Color color) UpdateStatusForSettings()
    {
        if (_updateBusy.Length > 0) return (_updateBusy, Color.DarkOrange);
        if (_updateMemoText.Length > 0) return (_updateMemoText, _updateMemoColor);
        return ("", UiTheme.TextMuted);
    }

    void CheckUpdate()
    {
        if (_updating) return;
        var s = CollectSettings();
        var python = Path.Combine(s.Root, ".venv", "Scripts", "python.exe");
        _updating = true;
        _updateBusy = "检查中…";
        SetUpdateInfo("", Color.Green);
        ClearUpdateState();
        var repoApi = StrataRepoApiOf(s.StrataUpdateUrl);      // DEC-110④：更新源归设置页那一格（填坏回落默认，见 StrataRepoApiOf）
        LogS("$ 检查更新: " + repoApi + "/releases/latest");
        Task.Run(() =>
        {
            var tmp = Path.Combine(TempDir(), "strata-release.json");
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* 留着也无妨 */ }
            string error;
            if (!Net.Download(repoApi + "/releases/latest", tmp, python, out error))
            {
                LogS("检查更新失败（网络不可达？）: " + error);
                SetUpdateInfo("检查失败", Color.Firebrick);
                FinishUpdate();
                return;
            }
            try
            {
                var node = JsonNode.Parse(File.ReadAllText(tmp)) as JsonObject;
                var tag = node?["tag_name"]?.GetValue<string>() ?? "";
                var notes = node?["body"]?.GetValue<string>() ?? "";
                var zip = node?["zipball_url"]?.GetValue<string>() ?? "";
                var published = node?["published_at"]?.GetValue<string>() ?? "";
                var remote = tag.TrimStart('v', 'V');
                if (remote.Length == 0 || zip.Length == 0)
                {
                    LogS("检查更新失败：发布信息不完整");
                    SetUpdateInfo("检查失败", Color.Firebrick);
                    FinishUpdate();
                    return;
                }
                var local = ReadLocalVersion(s.Root);
                if (CompareVersion(remote, local) <= 0)
                {
                    LogS($"已是最新（本地 {local}，远端 {tag}，发布 {published}）");
                    SetUpdateInfo("已是最新", Color.Green);
                    FinishUpdate();
                    return;
                }
                var skipped = _config.Current.SkipVersion.Trim();
                bool isSkipped = skipped.Length > 0 && remote.StartsWith(skipped, StringComparison.OrdinalIgnoreCase);
                BeginInvoke(new Action(() =>
                {
                    if (IsDisposed) return;
                    _remoteTag = tag;
                    _remoteVersion = remote;
                    _remoteNotes = notes;
                    _remoteZipUrl = zip;
                    _remotePublished = published;
                    _hasUpdate = !isSkipped;
                    _updateSkippedShown = isSkipped;      // 跳过的版本那一行照样出，只是「跳过」变「取消跳过」
                    UpdateUpdateUi();
                    LogS(isSkipped
                        ? $"远端 {tag}（本地 {local}）你跳过过一次；要升就先在设置页那一行点「取消跳过」。"
                        : $"发现新版本 {tag}（本地 {local}）：设置页「Strata」节「检查更新」下面有「查看更新内容 / 更新 / 跳过」。");
                }));
                FinishUpdate();
            }
            catch (Exception ex)
            {
                LogS("检查更新出错: " + ex.Message);
                SetUpdateInfo("检查失败", Color.Firebrick);
                FinishUpdate();
            }
        });
    }

    void SetUpdateInfo(string text, Color color)
    {
        if (InvokeRequired) { try { BeginInvoke(new Action(() => SetUpdateInfo(text, color))); } catch { } return; }
        // B 段：更新按钮已挪到设置页 Strata 节的维护组（行是动态建的）——状态进日志（进界面即拉满的信息流），
        // 同时记进 memo 供设置窗「更新状态」行回显（空串 = 回到"未检查"，开始新一轮检查时用）
        _updateMemoText = text;
        _updateMemoColor = color;
        if (text.Length > 0) LogS("更新状态：" + text);
        PokeUpdateUi();
    }

    // DEC-061：删掉 SetUpdateButtons(bool) 这个空壳（B 段起更新按钮不再常驻左栏，它什么都不做，
    // 但调用点看着像在管忙态灰置——真正的灰置由 UpdateUiBusy / LlamaUiBusy 承担）。

    void FinishUpdate()
    {
        if (InvokeRequired) { try { BeginInvoke(new Action(FinishUpdate)); } catch { } return; }
        _updating = false;
        _updateBusy = "";
        PokeUpdateUi();
    }

    void UpdateUpdateUi()
    {
        // ①：结果只写进行情与设置页那一行；按钮的"露不露"由 UpdateOfferForSettings 决定
        if (_hasUpdate) SetUpdateInfo($"发现新版本 {_remoteTag}", Color.Green);
        else if (_updateSkippedShown) SetUpdateInfo($"远端 {_remoteTag}（你已跳过）", UiTheme.TextMuted);
        PokeUpdateUi();
    }

    void ClearUpdateState()
    {
        _hasUpdate = false;
        _updateSkippedShown = false;
        _remoteTag = _remoteVersion = _remoteNotes = _remoteZipUrl = _remotePublished = "";
        UpdateUpdateUi();
    }

    // 自检出口：不联网造一次「探到新版本」的态（skipped=true = 探到的正是被跳过的那个版本）；用完 ClearUpdateState
    internal void SetUpdateOfferForTest(string tag, string published, bool skipped)
    {
        _remoteTag = tag;
        _remoteVersion = tag.TrimStart('v', 'V');
        _remoteNotes = "自检用的假说明";
        _remoteZipUrl = "";
        _remotePublished = published;
        _hasUpdate = !skipped;
        _updateSkippedShown = skipped;
        UpdateUpdateUi();
    }

    // 自检出口：把"探到新版"的态清回去（出图台与自检都用它收尾，别把假状态留给后面的用例）
    internal void ClearUpdateStateForTest() => ClearUpdateState();

    // 设置页「查看更新内容」按钮（①：入口独立，不挂在版本号上——这份说明是本地→最新之间的全部内容）
    internal void ShowUpdateDetail()
    {
        if (_remoteTag.Length == 0) { LogS("还没有远端版本信息，请先点「检查更新」。"); return; }
        var notes = _remoteNotes.Length > 0 ? _remoteNotes : "（这次发布没有写更新说明）";
        var res = Dialogs.UpdateDetailDialog(this, _remoteTag, LocalVersionOr("未知"), notes);
        if (res == DialogResult.Yes) DoUpgrade();
        else if (res == DialogResult.Ignore) SkipCurrentVersion();
    }

    internal void SkipCurrentVersion()
    {
        if (_remoteVersion.Length == 0) return;
        string skippedTag = _remoteVersion;      // DEC-061 那条老教训：要在 ClearUpdateState 之前把要说出口的值取走
        _config.Current.SkipVersion = _remoteVersion;
        SaveConfig(commitParams: false);
        SetUpdateInfo($"已跳过 {_remoteVersion}（下次探到它不再提示）", UiTheme.TextMuted);
        ClearUpdateState();
        Toast.Show(this, "已跳过 " + skippedTag + "（下次探到它不再提示；要恢复就在更新窗点「取消跳过」）", Toast.Kind.Info);
    }

    // 探到的正是"已跳过"那个版本时，那一行的第三个按钮是「取消跳过」——
    // 以前这条只能去手改 ini 的 SkipVersion（跳过之后就再也升不了，是①改造后必须补的回口）
    internal void UnskipCurrentVersion()
    {
        if (_remoteVersion.Length == 0) return;
        _config.Current.SkipVersion = "";
        SaveConfig(commitParams: false);
        _updateSkippedShown = false;
        _hasUpdate = true;
        SetUpdateInfo($"已取消对 {_remoteTag} 的跳过", Color.Green);
        LogS($"已取消对 {_remoteTag} 的跳过：现在点「更新」就能升级。");
        Toast.Show(this, "已取消对 " + _remoteTag + " 的跳过，点「更新」即可升级", Toast.Kind.Ok);
        PokeUpdateUi();
    }

    internal void DoUpgrade()
    {
        if (!_hasUpdate || _updating) return;
        var old = _process;
        // DEC-138（D-7 / 拍板⑤）：这一击只**问**，不停。以前确认完当场 `StopServer()`，
        // 而校验那一步的注释写着"校验不过 = 还没停服务、本机零影响"—— 那句在当时是假话（服务早停了）。
        // 现在把"要停"这个事实交给 `UpgradeWork`，由它在**校验通过之后、动笔覆盖之前**停（见下面那一步）。
        bool needStop = old is { HasExited: false };
        if (needStop)
        {
            var r = Dialogs.Confirm(this, "升级前先停服务",
                "升级需要先停止正在运行的服务（升级时要替换引擎文件）。", "现在停止并继续？",
                yes: "停止并升级", no: "取消");
            if (r != DialogResult.Yes) return;
        }
        var s = CollectSettings();
        _updating = true;
        _updateBusy = "升级中…";
        SetState("● 升级中…", Color.DarkOrange);
        PokeUpdateUi();
        Task.Run(() => UpgradeWork(s, old, needStop));
    }

    void UpgradeWork(Settings s, Process? old, bool needStop)
    {
        // DEC-114①：解压目录提到 try 外面，由这个方法末尾的 finally 收尾 —— 以前只有"取消/解压失败"两条分支
        // 各删一次，成功路径与"结构异常""校验不过"那几条都留着 = 实测 data\tmp 里 5 个 strata-update-* 共 160 MB
        // 的来历。**按整条路收，不是按某一条分支收**（DEC-111 那条"护栏要看它挡的是不是整条路"的同族账）。
        string? ext = null;
        try
        {
            // DEC-138（D-7）：这里原来开头就 `old.WaitForExit(20000)`（服务在入口那一击已经被停掉了）。
            // 停服务整件事挪到下面"校验通过之后"，所以开头一个字都不等。
            var python = Path.Combine(s.Root, ".venv", "Scripts", "python.exe");
            var zip = Path.Combine(TempDir(), "strata-" + _remoteVersion + ".zip");
            LogS($"$ 下载 {_remoteTag} 源码包…");
            string error;
            _strataCancelWanted = false;
            ReportProgress(true, "下载源码包", -1);
            if (!Net.Download(_remoteZipUrl, zip, python, out error,
                    (done, total) => ReportProgress(true, "下载源码包", total > 0 ? (double)done / total : -1),
                    CancelRequested(true)))
            {
                LogS("下载失败: " + error);
                FinishUpgrade(false, error.Contains("取消") ? "cancel" : "download");
                return;
            }
            LogS($"下载完成: {new FileInfo(zip).Length / 1048576.0:F1} MB");

            ext = Path.Combine(TempDir(), "strata-update-" + _remoteVersion);
            if (Directory.Exists(ext)) Directory.Delete(ext, true);
            Directory.CreateDirectory(ext);
            if (!ExtractWithProgress(zip, ext, (i, n) => ReportProgress(true, "解压", (double)i / n), CancelRequested(true)))
            {
                try { Directory.Delete(ext, true); } catch { /* 半截目录删不掉就算了 */ }
                LogS("已取消：还没往 Strata 目录里覆盖任何东西，本机保持原样（下载的包留在 data 的 tmp 里）。");
                FinishUpgrade(false, "cancel");
                return;
            }
            var roots = Directory.GetDirectories(ext);
            if (roots.Length != 1) { LogS("压缩包结构异常：顶层目录不是唯一一个"); FinishUpgrade(false, "unzip"); return; }
            var srcRoot = roots[0];

            // DEC-067：先验内容身份再动手 —— 这一刻还没备份、还没覆盖任何文件，收手就是零影响
            ReportProgress(true, "校验", 1);
            if (!SourceVersionMatches(srcRoot, _remoteVersion, out var foundVersion))
            {
                LogS($"校验失败：包里的版本是 {(foundVersion.Length > 0 ? "v" + foundVersion : "读不出来")}，" +
                     $"与探到的 {_remoteTag} 不符 —— 未覆盖任何文件，本机保持原样");
                SetUpdateInfo("校验失败", Color.Firebrick);
                Toast.Show(this, "下载校验没通过：包里的版本与探到的 " + _remoteTag + " 不符，未覆盖任何文件", Toast.Kind.Error);
                FinishUpgrade(false, "verify");
                return;
            }
            LogS("校验通过：包里的版本号与探到的一致（源码 zipball 没有官方摘要，按内容身份验 —— DEC-067）");

            // DEC-138（D-7 / 拍板⑤）：**校验过了才停服务**（上面那一步是本机零影响的收口）。
            // 停不下来就收手，别把覆盖做到一半；从这一刻起本机开始动，日志把这件事说清（不自动把服务拉回来：
            // 半途失败时"原样重启"可能把旧的、半新的混成一锅，那句"要跑就点左栏「启动」"归用户按）。
            if (needStop)
            {
                LogS("校验过了：现在停服务，然后才动 Strata 目录里的文件。");
                UiPost(StopServer);
                try { old?.WaitForExit(20000); } catch { /* 已经退出 */ }
                if (old is { HasExited: false })
                {
                    LogS("服务停不下来（20 秒），不覆盖：本机保持原样。");
                    SetUpdateInfo("停不下来，没升级", Color.Firebrick);
                    Toast.Show(this, "服务停不下来，没有升级：本机一个字没动", Toast.Kind.Warn);
                    FinishUpgrade(false, "stop");
                    return;
                }
            }

            // 升级前留一份「可回退的状态」：被覆盖的源码 + 引擎二进制（回退时秒级还原，不必重编）
            var local = ReadLocalVersion(s.Root);
            var backup = Path.Combine(StrataBackupRoot(s.Root),
                (local.Length > 0 ? local : "unknown") + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));   // DEC-116：快照的家与被备份物同级
            var srcBackup = Path.Combine(backup, "source");
            var engBackup = Path.Combine(backup, "engine");
            Directory.CreateDirectory(srcBackup);
            Directory.CreateDirectory(engBackup);
            foreach (var name in new[] { "strata.exe", "strata-vision.exe", "BUILD.json" })
            {
                var srcFile = Path.Combine(s.Root, "engine", name);
                if (File.Exists(srcFile)) File.Copy(srcFile, Path.Combine(engBackup, name), true);
            }
            File.WriteAllText(Path.Combine(backup, "about.txt"),
                "升级前状态：" + (local.Length > 0 ? local : "未知") + " → " + _remoteTag + "\r\n" +
                "时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n",
                new UTF8Encoding(false));
            int copied = 0, skipped = 0, backed = 0;
            var incoming = Directory.EnumerateFiles(srcRoot, "*", SearchOption.AllDirectories).ToList();
            for (int fi = 0; fi < incoming.Count; fi++)
            {
                var f = incoming[fi];
                var rel = Path.GetRelativePath(srcRoot, f);
                if (Preserve(rel)) { skipped++; continue; }
                var dst = Path.Combine(s.Root, rel);
                var dir = Path.GetDirectoryName(dst);
                if (dir is not null) Directory.CreateDirectory(dir);
                if (File.Exists(dst))
                {
                    var bdst = Path.Combine(srcBackup, rel);
                    var bdir = Path.GetDirectoryName(bdst);
                    if (bdir is not null) Directory.CreateDirectory(bdir);
                    File.Copy(dst, bdst, true);
                    backed++;
                }
                File.Copy(f, dst, true);
                copied++;
                ReportProgress(true, "覆盖到 Strata 目录", incoming.Count > 0 ? (double)(fi + 1) / incoming.Count : 1);
            }
            LogS($"覆盖完成：写入 {copied} 个文件，跳过本机保留 {skipped} 个，备份被覆盖的 {backed} 个到 {backup}");
            // DEC-110⑤：新快照已经落盘，再把超出 3 份的最旧那份收掉（次序按写入时间，见 TrimStrataBackups 的注释）
            int snapDel = TrimStrataBackups(s.Root);
            if (snapDel > 0) LogS($"升级快照超出 {StrataBackupKeep} 份，已删掉最旧的 {snapDel} 份");

            // 2026-10-03（DEC-036）：0.1.35 起官方预编译引擎就是正解（与 START-HERE.bat 同一条路）——
            // 让 setup 把 engine\ 同步到与源码同版本；不再本机重编（重编降为后路，只在日志里提示）。
            ReportProgress(true, "同步官方预编译引擎", -1);
            LogS("$ 同步官方预编译引擎: setup.update_installed_engine()（与 START-HERE.bat 同一条路，不再本机重编）");
            if (!File.Exists(python))
            {
                LogS("找不到 " + python + "：源码已更新，但引擎没同步（可手动跑 SETUP.bat，或用 diag\\update-engine.bat 重编）");
                FinishUpgrade(false, "engine");
                return;
            }
            var code = RunStreaming(python,
                new[] { "-c", "import setup; setup.update_installed_engine(setup.PREBUILT_URL)" },
                s.Root, s2 => LogS(s2));
            if (code == 0) LogS("引擎已同步为与源码同版本的官方预编译（版本号见设置页「版本」行）");
            else LogS($"引擎同步失败（exit code {code}）：源码已更新，可手动跑 SETUP.bat，或用 diag\\update-engine.bat 重编（后路）");
            FinishUpgrade(code == 0, "engine");
        }
        catch (Exception ex)
        {
            LogS("升级出错: " + ex.Message);
            FinishUpgrade(false, "exception");
        }
        finally
        {
            // DEC-114①：成功、失败、取消、异常四条路都从这里过 —— 解压目录（实测单份 20~41 MB）不再留在 data\tmp。
            // 下载包照旧不动（llama 那条链的形状 = 问一句要不要删，留不留归用户），历次老包由启动兜底扫按年龄收。
            CleanupStagingDir(ext);
        }
    }

    void FinishUpgrade(bool ok, string stage)
    {
        void Ui()
        {
            // DEC-061：弹窗用的版本号要在 ClearUpdateState() 之前取——ClearUpdateState 把 _remoteTag 清了，
            // 以前在它之后插值，升级成功的弹窗弹出来是「已升级到 ，引擎也同步…」（空号）。
            string doneTag = _remoteTag;
            RefreshVersions();
            RefreshModels();
            SetButtons(_process is { HasExited: false });
            SetState(_process is { HasExited: false } ? "● 运行中" : "已停止", _process is { HasExited: false } ? Color.ForestGreen : Color.Gray);
            if (ok)
            {
                ClearUpdateState();
                SetUpdateInfo("已更新", Color.Green);
                Dialogs.Message(this, "更新完成", $"已升级到 {doneTag}，引擎也同步为新版官方预编译了。", "点「启动」即可用新版本跑模型。");
            }
            else if (stage == "cancel")
            {
                // 用户主动停的：别弹红警告，只回显一句（现有安装一点没动）
                SetUpdateInfo("已取消", UiTheme.TextMuted);
                // DEC-066：更新窗关掉后流程照跑（DEC-058），取消的那一刻可能一个窗都没开着 —— 给一条提示收口
                Toast.Show(this, "已取消升级：本机一点没动，半截包已删掉", Toast.Kind.Info);
            }
            else if (stage == "download" || stage == "unzip" || stage == "verify" || stage == "exception")
            {
                SetUpdateInfo("升级没有完成（详见日志）", Color.Firebrick);
                Dialogs.Message(this, "更新", "升级没有完成。", "本机没被改坏：下载、解压或校验这一关失败的话，Strata 目录一点没动；详见日志（这一条归 Strata 页）。", danger: true);
            }
            else
            {
                SetUpdateInfo("源码已更新，但引擎没同步成功（详见日志）", Color.Firebrick);
                Dialogs.Message(this, "更新", "源码已更新，但引擎没同步成功。", "可手动跑 SETUP.bat，或用 diag 目录里的 update-engine.bat 重编（后路）；详见日志。", danger: true);
            }
            PokeUpdateUi();     // 本地版本行也随 RefreshVersions 刷新
        }
        _updating = false;
        _updateBusy = "";
        _strataPhase = ""; _strataFrac = -1;
        _strataCancelWanted = false; CancelResetsForTest++;      // DEC-138（E-8）：这一行有证人位盯着，删不得
        if (IsDisposed) return;
        if (InvokeRequired) { try { BeginInvoke(new Action(Ui)); } catch { } return; }
        Ui();
    }

}
