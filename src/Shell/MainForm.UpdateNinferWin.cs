using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MultiLauncher;

// Ninfer WIN 更新链：检查 → 窗要读的三样 → 下包/校验/停服务/备份旧 exe/只换 exe → 引擎 exe 回退。
// 2026-10-15 从 MainForm.UpdateNinfer.cs 整段搬出，一行未改。
public partial class MainForm
{
    // ---------------- 检查 ----------------

    internal void NinferCheckUpdate()
    {
        // 同 WSL 链那道闸的口径（DEC-109 顺手补齐）：测试态一个字都不做 —— 这条检查会拉 nvidia-smi、
        // 发起一次真实下载、还读一遍 1.5 GB 的 exe；自检/出图台一律不许有这些副作用（DEC-097⑥ 那条老规矩）。
        if (VersionProbeDisabledForTest) { LogN("测试态：不真查更新（自检/出图台）"); return; }
        if (_ninferUpdating) return;
        var s = _config.Current;
        var api = LlamaReleasesApi(s.NinferUpdateUrl);      // 通用：网页地址或 api 地址都能吃（同一个家，别再抄一份）
        if (api.Length == 0)
        {
            SetNinferInfo("检查失败", System.Drawing.Color.Firebrick);
            LogN("检查更新失败：「更新源」里认不出 GitHub 的 owner/repo（" + s.NinferUpdateUrl + "）");
            return;
        }
        var exe = NinferWinExePath(s.NinferDir);
        if (exe.Length == 0 || !File.Exists(exe))
        {
            SetNinferInfo("检查失败", System.Drawing.Color.Firebrick);
            LogN("检查更新失败：引擎目录里找不到 ninfer-serve-*.exe（" + s.NinferDir + "）");
            return;
        }
        var cap = NinferComputeCapability();
        var want = NinferAssetForCap(cap);
        if (want.Length == 0)
        {
            SetNinferInfo("检查失败", System.Drawing.Color.Firebrick);
            LogN("检查更新失败：取不到本机 compute capability（nvidia-smi 没应答）。**没有 PTX 回退**，不猜架构就不敢下包。");
            return;
        }
        _ninferUpdating = true;
        ClearNinferOffer();
        PokeUpdateUi();
        LogN($"$ 检查 Ninfer 更新: {api}（本机算力 {cap} → 只认 {want}）");
        _ = Task.Run(() =>
        {
            var tmp = Path.Combine(TempDir(), "ninfer-releases.json");
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* 留着也无妨 */ }
            var python = Path.Combine(_config.Current.Root, ".venv", "Scripts", "python.exe");
            if (!Net.Download(api, tmp, python, out var err))
            {
                LogN("检查更新失败（网络不可达？）: " + err);
                SetNinferInfo("检查失败", System.Drawing.Color.Firebrick);
                FinishNinfer();
                return;
            }
            string json;
            try { json = File.ReadAllText(tmp); }
            catch (Exception ex) { LogN("读发布信息失败: " + ex.Message); SetNinferInfo("检查失败", System.Drawing.Color.Firebrick); FinishNinfer(); return; }
            // 本地版本 = 本地 exe 的 sha256 反查资产表（ini 里不存版本号）
            var localSha = SafeNinferSha(exe);        // DEC-136（B-1）：Sha256File 不吞异常（exe 被 AV / 索引 / 另一次覆盖占住就抛），原先裸调 = 一抛锁死忙态
            var localTag = NinferTagForLocalSha(json, localSha);
            var rel = ParseNinferRelease(json, want);
            if (localSha.Length == 0) LogN("本地 exe 的 sha256 没算出来（文件被占用？）：这一档按「未知构建」显示");
            if (rel.Tag.Length == 0)
            {
                LogN("没找到可用的发布（Release 列表里没有带 " + want + " 的资产？）");
                SetNinferInfo("检查失败", System.Drawing.Color.Firebrick);
                UiPost(() => AdoptNinferLocal(localTag, localSha));       // DEC-136（B-1）：UiPost 自带 try（窗正在关时 BeginInvoke 会抛，抛在 Task 里 = 忙态锁死）
                FinishNinfer();
                return;
            }
            // DEC-136（B-2）：DigestMatches 第一条是「digest 为空 = 上游没给摘要，按没这条校验通过」—— 那是给**下载后的实物校验**
            // 定的口径，拿它当「本地 == 远端」的版本判据就成 fail-open：资产不带 digest 时永远判「已是最新」（日志还声称逐字节
            // 相同，其实一个字节都没比过），新版本再也探不出来。同文件 NinferTagForLocalSha 那句 digest.Length > 0 && 才是对写法。
            bool same = localSha.Length > 0 && rel.Sha.Length > 0 && Net.DigestMatches(rel.Sha, localSha);
            if (same)
            {
                LogN($"已是最新（本地 = {rel.Tag} 的 {rel.Asset}，sha256 与资产 digest 逐字节相同）");
                SetNinferInfo("已是最新", System.Drawing.Color.Green);
                UiPost(() => AdoptNinferLocal(localTag, localSha));       // DEC-136（B-1）同上
                FinishNinfer();
                return;
            }
            var skipped = _config.Current.NinferSkipVersion.Trim();
            bool isSkipped = skipped.Length > 0 && string.Equals(skipped, rel.Tag, StringComparison.OrdinalIgnoreCase);
            BeginInvoke(new Action(() =>
            {
                if (IsDisposed) return;
                AdoptNinferLocal(localTag, localSha);
                _ninferRemoteTag = rel.Tag;
                _ninferRemoteNotes = rel.Notes;
                _ninferRemoteUrl = rel.Url;
                _ninferRemoteAssetName = rel.Asset;
                _ninferRemotePublished = rel.Published;
                _ninferRemoteSha256 = rel.Sha;
                _ninferRemoteAssetSize = rel.Size;
                _ninferHasUpdate = !isSkipped;
                _ninferSkippedShown = isSkipped;
                SetNinferInfo(isSkipped ? $"远端 {rel.Tag}（你已跳过）" : $"发现新版本 {rel.Tag}",
                    isSkipped ? UiTheme.TextMuted : System.Drawing.Color.Green);
                LogN(isSkipped
                    ? $"远端 {rel.Tag}（本地 {(localTag.Length > 0 ? localTag : "未知构建")}）你跳过过一次；要升就点「取消跳过」。"
                    : $"发现 Ninfer 新版本 {rel.Tag}（本地 {(localTag.Length > 0 ? localTag : "未知构建")}）：「更新」只换 {rel.Asset}，DLL 一律不动，旧 exe 先备份。");
            }));
            FinishNinfer();
        });
    }

    // ---------------- 那一窗要读的三样（与 Strata / llama 同一形状，同一个 UpdateForm） ----------------

    internal UpdateOffer NinferUpdateOfferForSettings()
    {
        // DEC-106：没有 offer 也要把**忙态的阶段名**带出去 —— WSL 那条链的"检查中/重编中"也要让第三行说得出来；
        // 空串时第三行退回既有那句"正在查询最新发布…"（WIN 链检查那一段，与之前一字不差）。
        if (_ninferRemoteTag.Length == 0 || (!_ninferHasUpdate && !_ninferSkippedShown))
            return new UpdateOffer(false, "", "", "跳过", _ninferPhase, _ninferFrac, false);
        var when = _ninferRemotePublished.Length >= 10 ? _ninferRemotePublished.Substring(0, 10) : "";
        var ver = _ninferRemoteTag + (when.Length > 0 ? " · 发布 " + when : "");
        var note = _ninferHasUpdate ? "" : "这个版本你之前跳过一次（记在 MultiLauncher.ini 的 NinferSkipVersion）";
        return new UpdateOffer(true, ver, note, _ninferHasUpdate ? "跳过" : "取消跳过", _ninferPhase, _ninferFrac, NinferCancellable);
    }

    internal (string, System.Drawing.Color) NinferUpdateStatusForSettings() => (_ninferMemo, _ninferMemoColor);

    void SetNinferInfo(string text, System.Drawing.Color color)
    {
        if (InvokeRequired) { try { BeginInvoke(new Action(() => SetNinferInfo(text, color))); } catch { } return; }
        _ninferMemo = text; _ninferMemoColor = color;
        if (text.Length > 0) LogN("Ninfer 更新状态：" + text);
        PokeUpdateUi();
    }

    void ReportNinferProgress(string phase, double frac)
    {
        if (InvokeRequired) { try { BeginInvoke(new Action(() => ReportNinferProgress(phase, frac))); } catch { } return; }
        _ninferPhase = phase; _ninferFrac = frac;
        PokeUpdateUi();
    }

    void ClearNinferOffer()
    {
        _ninferHasUpdate = false; _ninferSkippedShown = false;
        _ninferRemoteTag = _ninferRemoteNotes = _ninferRemoteUrl = _ninferRemoteAssetName = _ninferRemotePublished = _ninferRemoteSha256 = "";
        _ninferRemoteAssetSize = 0;
        PokeUpdateUi();
    }

    // 出图/自检用：喂一条假 offer（不联网），看更新窗那一支摆位与另两窗一字不差
    internal void SetNinferOfferForTest(string tag, string published, bool skipped)
    {
        _ninferRemoteTag = tag; _ninferRemotePublished = published;
        _ninferRemoteNotes = "**修复** 超池题面的重发；`ninfer-serve-89.exe` 逐资产带 sha256。\n- 只换 exe，DLL 不动";
        _ninferRemoteAssetName = "ninfer-serve-89.exe";
        _ninferRemoteUrl = "https://example.invalid/" + _ninferRemoteAssetName;
        _ninferRemoteSha256 = "sha256:19222a2a68df6f7d"; _ninferRemoteAssetSize = 1511845888;
        _ninferHasUpdate = !skipped; _ninferSkippedShown = skipped;
        PokeUpdateUi();
    }
    internal void ClearNinferOfferForTest() => ClearNinferOffer();
    internal UpdateOffer NinferOfferForTest() => NinferUpdateOfferForSettings();

    void FinishNinfer()
    {
        if (InvokeRequired) { try { BeginInvoke(new Action(FinishNinfer)); } catch { } return; }
        _ninferUpdating = false;
        _ninferPhase = ""; _ninferFrac = -1;
        _ninferCancelWanted = false;
        PokeUpdateUi();
    }

    internal void ShowNinferUpdateDetail()
    {
        if (_ninferRemoteTag.Length == 0) { LogN("还没有远端版本信息，请先点「检查更新」。"); return; }
        var notes = _ninferRemoteNotes.Length > 0 ? _ninferRemoteNotes : "（这次发布没有写更新说明）";
        var res = Dialogs.UpdateDetailDialog(this, _ninferRemoteTag, NinferVersionForSettings(), notes);
        if (res == DialogResult.Yes) NinferUpgrade();
        else if (res == DialogResult.Ignore) NinferSkip();
    }

    internal void NinferSkip()
    {
        if (_ninferRemoteTag.Length == 0) return;
        string skippedTag = _ninferRemoteTag;        // DEC-061：ClearNinferOffer 会把它清空，话要先取好
        _config.Current.NinferSkipVersion = _ninferRemoteTag;
        SaveConfig(commitParams: false);
        SetNinferInfo($"已跳过 {_ninferRemoteTag}", UiTheme.TextMuted);
        ClearNinferOffer();
        Toast.Show(this, "已跳过 " + skippedTag + "（下次探到它不再提示；要恢复就在更新窗点「取消跳过」）", Toast.Kind.Info);
    }

    internal void NinferUnskip()
    {
        if (_ninferRemoteTag.Length == 0) return;
        _config.Current.NinferSkipVersion = "";
        SaveConfig(commitParams: false);
        SetNinferInfo($"已取消对 {_ninferRemoteTag} 的跳过", System.Drawing.Color.Green);
        LogN($"已取消对 {_ninferRemoteTag} 的跳过：现在点「更新」就能升级。");
        Toast.Show(this, "已取消对 " + _ninferRemoteTag + " 的跳过，点「更新」即可升级", Toast.Kind.Ok);
    }

    // ---------------- 升级：下包 → 校验 → 停服务 → 备份旧 exe → 只换 exe ----------------
    // DEC-138（D-7）：次序改过一次 —— 以前"停服务"排在最前，于是下载失败 / 取消 / 校验不过那三条收手路上
    // 服务已经被停了，而注释还写着"本机零影响"。现在停服务落在**校验通过之后**，前面那三条真的一个字都没动。

    internal void NinferUpgrade()
    {
        if (_ninferUpdating) return;
        if (_ninferRemoteTag.Length == 0 || _ninferRemoteUrl.Length == 0)
        {
            LogN("还没有可升级的版本：先点「检查更新」。");
            Toast.Show(this, "先检查更新，探到新版本才谈升级", Toast.Kind.Info);
            return;
        }
        var s = _config.Current;
        var exe = NinferWinExePath(s.NinferDir);
        if (exe.Length == 0 || !File.Exists(exe))
        {
            LogN("升级没开始：引擎目录里找不到 ninfer-serve-*.exe（" + s.NinferDir + "）");
            Toast.Show(this, "引擎目录不对，没法升级", Toast.Kind.Error);
            return;
        }
        _ninferUpdating = true;
        PokeUpdateUi();
        var newTag = _ninferRemoteTag;                 // DEC-061：流程里读到的远端标签先取快照，ClearOffer 之后还要用它说话
        var url = _ninferRemoteUrl;
        var asset = _ninferRemoteAssetName;
        var wantSha = _ninferRemoteSha256;
        var wantSize = _ninferRemoteAssetSize;
        _ninferCancelWanted = false;      // DEC-136（B-1）：与 Strata / llama 两条同形 —— 上一次异常退出留个 true，
                                          //   下一次升级会在第一个分块就自我取消，界面还把它报成「已取消」
        LogN($"$ 开始升级 Ninfer 引擎 → {newTag}（只换 {asset}；DLL 与目录里其它东西一律不动）");
        _ = Task.Run(() =>
        {
            var tmp = Path.Combine(TempDir(), Path.GetFileName(asset));   // DEC-136（B-7）：远端给的资产名一律先收成叶子名再拼落盘路径
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* 半截文件下面会重删 */ }
            // ② 下载（只有这一档 + 下一档能取消）
            ReportNinferProgress("下载 " + asset, -1);
            var dl = Net.Download(url, tmp, Path.Combine(_config.Current.Root, ".venv", "Scripts", "python.exe"),
                out var derr,
                (got, total) => ReportNinferProgress("下载 " + asset, total > 0 ? (double)got / total : -1),
                () => _ninferCancelWanted);
            if (!dl)
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* 删不动就算了，下次覆盖 */ }
                if (_ninferCancelWanted) { LogN("已取消 Ninfer 更新（半截包已删，本机一点没碰）"); SetNinferInfo("已取消", UiTheme.TextMuted); }
                else { LogN("下载失败: " + derr); SetNinferInfo("下载失败", System.Drawing.Color.Firebrick); }
                FinishNinfer();
                return;
            }
            // ③ 校验：比体积再比摘要，不符就删包收手（此刻没备份、没覆盖 = 本机零影响，DEC-067）
            if (!Net.VerifyDownload(tmp, wantSize, wantSha, out var verr))
            {
                try { File.Delete(tmp); } catch { /* 删不动也绝不覆盖 */ }
                LogN("校验没过，已删掉刚下载的包：" + verr);
                SetNinferInfo("校验没过，没升级", System.Drawing.Color.Firebrick);
                UiPost(() => Toast.Show(this, "校验没过，没有升级：" + verr, Toast.Kind.Error));
                FinishNinfer();
                return;
            }
            ReportNinferProgress("校验通过（sha256 与 release 资产 digest 一致）", -1);
            // DEC-138（D-7 / 拍板⑤）：**校验过了才停服务**（以前这一步排在下载前面，"校验不过 = 本机零影响"
            // 那句注释当时是假话 —— 服务早停了）。停不下来就删包收手，exe 一个字都没动。
            if (NinferRunning)
            {
                LogN("校验过了：现在停掉引擎，然后才备份旧 exe 与覆盖。");
                UiPost(StopNinferServer);
            }
            if (!WaitNinferProcessGone(TimeSpan.FromSeconds(30)))
            {
                try { File.Delete(tmp); } catch { /* 删不动也绝不覆盖 */ }
                LogN("升级停在这里：引擎停不下来（进程还在），exe 没动、包已删。先手动停掉再试。");
                SetNinferInfo("停不下来，没升级", System.Drawing.Color.Firebrick);
                FinishNinfer();
                return;
            }
            // ④ 备份旧 exe（DEC-116：引擎二进制单独一格 \data\backup\ninfer-exe\，前缀硬判据 ninfer-serve- 照旧留着当第二道；老形状是同目录不同前缀，他抓过「把配置和exe混为一起了」）
            if (!BackupFileTo(exe, RealNinferExeBackupDir(), NinferExeBackupPrefix, out var bakName, out var berr))
            {
                LogN("旧 exe 备份没做成，不覆盖：" + berr);
                SetNinferInfo("备份没做成，没升级", System.Drawing.Color.Firebrick);
                FinishNinfer();
                return;
            }
            var kept = TrimBackupsIn(RealNinferExeBackupDir(), NinferBackupKeep, NinferExeBackupPrefix);
            // ⑤ 只换 exe
            ReportNinferProgress("覆盖 exe", -1);
            try { File.Copy(tmp, exe, overwrite: true); }
            catch (Exception ex)
            {
                LogN("覆盖失败（备份在 " + bakName + "，现在的 exe 还是旧的那只或半截）：" + ex.Message);
                SetNinferInfo("覆盖失败", System.Drawing.Color.Firebrick);
                FinishNinfer();
                return;
            }
            try { File.Delete(tmp); } catch { /* 半截临时包删不掉不影响结论 */ }
            var ver = newTag;
            var exeBakHome = DisplayDataPath(RealNinferExeBackupDir());
            LogN($"Ninfer 引擎已换到 {ver}（旧的那只在 {exeBakHome}\\{bakName}，现在该目录留 {kept} 份）。" +
                 "DLL 一律没动；要是新构建要更高的运行库，起不来就先用设置页「恢复…」退回去。");
            UiPost(() =>
            {
                // 换完 exe = 这一对缓存跟着换新值：新 sha 就是刚校验过的那条资产 digest（不必再读一遍盘），
                // 于是「版本」那行立刻是新 tag，而 DEC-097④ 那条"sha 变了才失效"仍然成立。
                // 用 wantSha 而不是再读 _ninferRemoteSha256：下面 ClearNinferOffer 会把它清空（DEC-061 那条快照教训）。
                var newSha = wantSha.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                    ? wantSha[7..].Trim() : wantSha.Trim();
                AdoptNinferLocal(ver, newSha);
                SetNinferInfo("已升级到 " + ver, System.Drawing.Color.Green);
                Toast.Show(this, "Ninfer 引擎已升级到 " + ver, Toast.Kind.Ok);
                ClearNinferOffer();
                RefreshModels(NinferKey);       // 只重扫 Ninfer（DEC-097⑤）
            });
            FinishNinfer();
        });
    }

    /// <summary>等引擎进程真退出（实测停 → 端口让出 ≤ 0.2 秒，与进程退出同一拍；别睡固定秒数）。
    /// 没在跑 = 立刻 true。返回 false = 30 秒还停不下来。</summary>
    bool WaitNinferProcessGone(TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            Process? p;
            try { p = _ninferProcess; } catch { return true; }
            if (p is null) return true;
            try { if (p.HasExited) return true; } catch { return true; }
            Thread.Sleep(120);
        }
        return false;
    }

    /// <summary>取消 Ninfer 更新：只置自己那一个标志，真正的停在下一个检查点（每块下载）。</summary>
    internal void RequestCancelNinferUpdate()
    {
        _ninferCancelWanted = true;
        LogN("已请求取消 Ninfer 更新：等当前这一步停下（下载能停；覆盖 exe 不能停）。");
        PokeUpdateUi();
    }

    // ---------------- 引擎 exe 回退（设置页「回退 · WIN」那一枚，DEC-110⑤） ----------------
    // 补这一口的理由：更新链每次换 exe 前都存了一份旧 exe（老落点 `data\infer\`，DEC-116 起在 `data\backup\ninfer-exe\`），但界面上没有恢复入口 ——
    // 而「套件完整性校验」那句说明写着"用上面那行退回旧 exe"，那行其实是**配置**备份，不是 exe（名不副实）。
    // 用户口径：校验那一行用处不大、去掉；备份恢复可以补，但前提是备份本身得是对的
    //（同批改掉 BackupFileTo 写死 .ini 的 bug —— 引擎备份以前被存成 `ninfer-serve-<时间戳>.ini`）。
    // 形状照搬 WSL 的快照回退：列清单 → 选一份 → 危险确认 → **先把当前这份也备份** → 覆盖。

    internal static List<string> ListNinferExeBackups() => ListBackupsIn(NinferExeBackupDir(), NinferExeBackupPrefix);
    internal static int TrimNinferExeBackups() => TrimBackupsIn(NinferExeBackupDir(), NinferBackupKeep, NinferExeBackupPrefix);

    internal void RestoreNinferExeFromBackup()
    {
        var s = _config.Current;
        var dir = s.NinferDir.Trim();
        var exe = NinferWinExePath(dir);
        var snaps = ListNinferExeBackups();
        if (snaps.Count == 0)
        {
            LogN("还没有引擎 exe 备份（壳每次要用新版本换掉 exe 之前才存一份），没什么可恢复。");
            Toast.Show(this, "还没有引擎 exe 备份", Toast.Kind.Info);
            return;
        }
        if (exe.Length == 0 || !File.Exists(exe))
        {
            LogN("恢复没做：先找到现在的引擎再说（" + dir + " 里没有 ninfer-serve-*.exe）。");
            Toast.Show(this, "现在的引擎 exe 不在，恢复取消", Toast.Kind.Warn);
            return;
        }
        // 引擎在跑 = 那只 exe 被进程占着，覆盖会失败（或留下半新半旧）—— 先请人停掉，不替他做
        if (NinferRunning && _ninferRunningBackend == NinferIni.BackendWsl)
        { LogN("恢复没做：WSL 那一侧在跑，与本机 exe 无关（这一枚只管 WIN 那一只）。"); return; }
        if (NinferRunning)
        {
            LogN("恢复没做：Ninfer 引擎正在跑（WIN 那侧）。先点「停止」，再回来恢复 exe。");
            Toast.Show(this, "引擎在跑，先「停止」再恢复 exe", Toast.Kind.Warn);
            return;
        }
        string? pick = snaps.Count == 1 ? snaps[0] : Dialogs.PickSnapshot(this, snaps, 0,
            "选择要退回的引擎 exe（文件名 = 换掉它的那一次时间戳）");
        if (pick is null) return;                                  // 取消 = 完全静默（DEC-068）
        if (Dialogs.Confirm(this, "恢复哪一支 exe",
                "会用这份备份覆盖现在的引擎：\n" + pick + "\n\n"
                + "只换那一只 exe，同目录的 DLL 一支都不碰。覆盖前会先把现在这份也备份，所以这一步也退得回去。",
                null, yes: "覆盖", no: "取消", danger: true) != DialogResult.Yes) return;
        var bak = Path.Combine(NinferExeBackupDir(), pick);
        // DEC-134①：轮转挪到复制**成**之后。原先的顺序是「备份 → TrimNinferExeBackups() → 复制」，
        // 那一格满 3 份（升过三次 exe 就是常态）时，Trim 先删掉最旧那一份 = 人点名要退回的那一支，
        // 紧接着的复制抛 FileNotFoundException，日志那句"现在这份已先备份成 X，没被改坏"还把人往沟里带。
        if (!File.Exists(bak))
        {
            LogN("恢复没做：列表里那一份 exe 已经不在了（" + pick + "）。清单是开窗时读的，重新点一次「恢复…」看现在还剩哪些。");
            Toast.Show(this, "那一份 exe 备份不在了，恢复取消", Toast.Kind.Warn);
            return;
        }
        // DEC-199（2026-10-20 他实跑点名）：恢复前先做三件事 —— ① 要恢复的就是现在这一支 → 什么都不碰；
        // ② 把这一格按内容去重一次（同一份内容只留最早那个名字）；③ 再按内容决定"要不要真存一份新备份"。
        var pickSha = Sha256FileOrEmpty(bak);
        var curSha = Sha256FileOrEmpty(exe);
        if (pickSha.Length > 0 && string.Equals(pickSha, curSha, StringComparison.OrdinalIgnoreCase))
        {
            // 现在跑的就是它：不备份、不覆盖、不新建任何文件（他原话：「备份前应检查 hash，如果有相同的版本，不要碰原文件」）
            LogN("恢复没做：现在这一支与要恢复的备份 " + pick + " 逐字节相同，什么都不用换（也没多存备份）。");
            Toast.Show(this, "现在跑的就是这一支，不用恢复", Toast.Kind.Info);
            return;
        }
        var deduped = DedupeBackupsByContent(NinferExeBackupDir(), NinferExeBackupPrefix);
        if (deduped > 0) LogN($"恢复前先收拾了这一格：按内容去重删掉 {deduped} 份重复备份（同一份内容只留最早那个名字）");
        if (!BackupBeforeRestore(exe, NinferExeBackupDir(), NinferExeBackupPrefix, out var newName, out var berr, out var reused))
        {
            LogN("备份没做成，恢复取消：" + berr);
            Toast.Show(this, "备份没做成，恢复取消", Toast.Kind.Error);
            return;
        }
        try
        {
            File.Copy(bak, exe, overwrite: true);
            TrimNinferExeBackups();      // 复制成了再收口：那一份的内容此刻已经在工作 exe 里了，删它不丢东西
        }
        catch (Exception ex)
        {
            LogN("恢复失败：" + ex.Message + "（现在这份" + (reused ? "在备份里本来就有一份 " + newName : "已先备份成 " + newName)
                 + "、没被改坏；" + pick + " 也还在，没收口 —— 多一份不影响，下次恢复照旧列得出来）");
            Toast.Show(this, "恢复失败：" + ex.Message, Toast.Kind.Error);
            return;
        }
        // 版本那一对要跟着重算：exe 换回去了，缓存里那串 sha 已经不是它（DEC-097④ 的判据）
        InvalidateNinferLocalVersion();
        // 回退这一刀不写缓存是对的（DEC-097④：没有出处就不把版本号钉进 ini）—— 但界面不该因此停在
        // 「未知构建」：那一句是给「从没查过 / 读不动哈希」准备的。回退过的那一支有个**看得见的出处**：
        // 备份清单里那条文件名 = 换掉它的那一次时间戳，而这次恢复的正是它。文案由
        // NinferRolledBackText() 给（MainForm.UpdateNinferWsl.cs），判据在 SelfTest.UiUpdateWindow。
        _ninferRolledBackFrom = pick;
        LogN($"已从备份 {pick} 恢复引擎 exe（现在这份" +
             (reused ? "在备份里本来就有一份 " + newName + "，没有多存" : "已先存成 " + newName) +
             "；「起服务」时才落到进程）");
        Toast.Show(this, "已恢复引擎 exe：" + pick, Toast.Kind.Ok);
        RefreshModels(NinferKey);       // 只重扫 Ninfer（DEC-097⑤）
    }
}
