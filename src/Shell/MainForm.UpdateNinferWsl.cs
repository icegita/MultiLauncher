using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MultiLauncher;

// Ninfer WSL 更新链入口：检查 → 弹「更新内容」→ 同步 + 重编 + 快照。
// 2026-10-15 从 MainForm.UpdateNinfer.cs 整段搬出，一行未改。
public partial class MainForm
{
    // ---------------- 入口：检查 → （有新版本才弹「更新内容」）→ 同步 + 重编 + 快照 ----------------

    /// <summary>WSL 段那枚「更新…」。甲案（用户拍板）：一次点击 = 检查；探到新版本才弹「更新内容」窗，
    /// 选「立即升级」才真去重编。全程落点 = 按钮字态 + 日志 + toast；忙态与 WIN 链互斥（共用一位）。</summary>
    internal void NinferWslUpdateFlow()
    {
        var s = _config.Current;
        _ninferWslAttemptsForTest++;
        if (!NinferWslChainAllowed(VersionProbeDisabledForTest, s.NinferWslOn, NinferUiBusy))
        {
            _ninferWslBlockedForTest = VersionProbeDisabledForTest ? "test" : !s.NinferWslOn ? "disabled" : "busy";
            if (!VersionProbeDisabledForTest && !s.NinferWslOn)
                Toast.Show(this, "WSL 那一侧没启用（设置页 Ninfer 节「启用 WSL 后端」）", Toast.Kind.Info);
            return;
        }
        _ninferWslBlockedForTest = "";
        var distro = s.NinferDistro.Trim();
        var bin = s.NinferWslBin.Trim();
        if (distro.Length == 0 || bin.Length == 0)
        {
            LogN("WSL 更新没开始：发行版与引擎可执行路径不能空（设置页 Ninfer 节 WSL 那一组）。");
            Toast.Show(this, "WSL 更新没开始：先去设置页把发行版与引擎路径填上", Toast.Kind.Warn);
            return;
        }
        // DEC-110④：更新源那一格填了就显式从那一头取；认不出合法形状就不开这条链（别拿猜出来的地址进 VM）
        var wslUrl = s.NinferWslUpdateUrl.Trim();
        if (!NinferWslUrlOk(wslUrl))
        {
            LogN("WSL 更新没开始：「更新源 · WSL」里那一串认不出合法的仓库地址（" + wslUrl + "）。留空 = 用仓库自己的 origin。");
            Toast.Show(this, "WSL 更新没开始：先去设置页把「更新源 · WSL」填对（或留空）", Toast.Kind.Warn);
            return;
        }
        _ninferUpdating = true;
        SetNinferWslBusy("检查中…", "检查 WSL 更新（git fetch）…");
        LogN($"$ 检查 WSL 侧更新：wsl -d {distro} bash -l -s（git fetch " + (wslUrl.Length > 0 ? "更新源那一头" : "origin") + " + 比两侧提交）");
        _ = Task.Run(() =>
        {
            var (code, output) = RunWslChain(distro, NinferWslCheckScript(bin, wslUrl), 60_000);
            var chk = ParseNinferWslCheck(output);
            if (code != 0 || chk.Problem is not null)
            {
                var why = chk.Problem ?? ("退出码 " + code);
                if (output.Trim().Length > 0) LogN("检查输出：" + TailText(output, 800));
                LogN("WSL 更新检查失败：" + why);
                UiPost(() => { SetNinferWslInfo("检查失败", System.Drawing.Color.Firebrick);
                    Toast.Show(this, "WSL 更新检查失败（" + why + "）", Toast.Kind.Error); FinishNinferWsl(); });
                return;
            }
            if (NinferWslCheckVerdict(chk.Local, chk.Remote) == "uptodate")
            {
                var sh = NinferWslShort(chk.Local);
                LogN($"WSL 侧已是最新（{chk.Branch} @ {sh}）。");
                UiPost(() => { SetNinferWslInfo("已是最新", System.Drawing.Color.Green);      // DEC-110③：窗里也要看得见
                    Toast.Show(this, "WSL 侧已是最新（" + chk.Branch + " @ " + sh + "）", Toast.Kind.Ok); FinishNinferWsl(); });
                return;
            }
            // 更新内容（提交清单）：读不到也不拦升级（说明栏退回一句"没有提交信息"）
            var (_, logs) = RunWslChain(distro, NinferWslLogScript(bin, wslUrl, chk.Branch, chk.Local), 30_000);
            var commits = logs.Trim();
            var branch = chk.Branch; var localShort = NinferWslShort(chk.Local); var remoteShort = NinferWslShort(chk.Remote);
            UiPost(() =>
            {
                if (IsDisposed) { FinishNinferWsl(); return; }
                var res = Dialogs.UpdateDetailDialog(this, branch + " @ " + remoteShort, localShort,
                    commits.Length > 0 ? commits : "（这几次提交没有写提交信息）");
                if (res == DialogResult.Yes)
                {
                    LogN($"$ 开始 WSL 侧更新 → {branch} @ {remoteShort}（ff-only 同步 + cmake 重编，旧二进制先快照）");
                    SetNinferWslBusy("重编中…", "重编 WSL 引擎（VM 里 cmake，可能要几分钟）…");
                    _ = Task.Run(() => RunWslUpgrade(distro, bin, wslUrl, branch, remoteShort)).ContinueWith(t =>
            {
                // DEC-136（B-1）：原先四条链的 Task 体一个兜底都没有（Strata / llama 两条都有 try/finally）。
                // 一抛就永远到不了 FinishNinferWsl → _ninferUpdating 卡在 true：两扇窗按钮全灰、托盘两条都写「升级中…」、
                // WIN 与 WSL 一起点不动（共用这一位互斥），只能重启外壳；异常在 Task 里没被 await = 不弹不记（静默）。
                if (t.Exception is null) return;
                LogN("WSL 更新链出了意外：" + t.Exception.GetBaseException().Message);
                UiPost(() => { SetNinferWslInfo("更新没完成（看日志）", System.Drawing.Color.Firebrick); FinishNinferWsl(); });
            }, System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
                }
                else
                {
                    if (res == DialogResult.Ignore) LogN("没有升级：这一版先不动（远端 " + branch + " @ " + remoteShort + " 还在）。");
                    // DEC-110③：弹过「更新内容」又选"先不动"的，窗里那一行要留下"远端是哪一版"，别查完就空白
                    SetNinferWslInfo("远端 " + branch + " @ " + remoteShort + "（未升级）", UiTheme.TextMuted);
                    FinishNinferWsl();
                }
            });
        });
    }

    /// <summary>升级主体（后台）：停那一侧引擎 → 脏树/ff-only 同步 → 缺缓存先 configure → 增量重编 → 快照 → 重读版本。
    /// WIN 那侧引擎在跑**不动** —— 重编不碰它的 exe。</summary>
    void RunWslUpgrade(string distro, string bin, string url, string branch, string remoteShort)
    {
        // ① 那一侧引擎在跑先停（要重编的就是它那只二进制）
        UiPost(() => { if (NinferRunning && _ninferRunningBackend == NinferIni.BackendWsl) StopNinferServer(); });
        if (!WaitNinferProcessGone(TimeSpan.FromSeconds(30)))
        {
            LogN("WSL 更新没开始：引擎停不下来（进程还在）。先手动停掉再试。");
            UiPost(() => { SetNinferWslInfo("停不下来，没重编", System.Drawing.Color.Firebrick);
                Toast.Show(this, "引擎停不下来，没有重编", Toast.Kind.Error); FinishNinferWsl(); });
            return;
        }
        // 顺带看一眼：VM 里还有没有别家的 ninfer-serve（外部的壳不会去停，只提醒一句）
        // DEC-136（B-3）：上面那条 Kill 停的是本机那只 wsl.exe 客户端句柄 —— VM 里的 ninfer-serve 从来没人停
        //（全仓 pkill 之前一个调用点都没有），于是"停下来了"只证明句柄没了，不满足本项目自家那条判据
        //（停止成功 = 进程退出 + 端口不再 LISTENING）。原先只写一行提醒就照样重编 = 链接器写 busy 二进制
        //（ETXTBSY）→ BUILD_FAIL，日志看不出真因，而服务已经停了也不会自己回来。
        var (pc, pout) = RunWslChain(distro, "pgrep -x ninfer-serve", 15_000);
        if (pc == 0 && pout.Trim().Length > 0)
        {
            LogN("VM 里还有 ninfer-serve（pid " + pout.Trim().Replace("\n", " ") + "）：先停它再重编");
            RunWslChain(distro, "pkill -x ninfer-serve", 15_000);
            var (pc2, pout2) = RunWslChain(distro, "pgrep -x ninfer-serve", 15_000);
            if (pc2 == 0 && pout2.Trim().Length > 0)
            {
                LogN("WSL 更新没开始：VM 里那只 ninfer-serve 停不掉（不是壳拉起的，壳不硬杀外部进程）。");
                UiPost(() => { SetNinferWslInfo("VM 里引擎还在跑，没重编", System.Drawing.Color.Firebrick);
                    Toast.Show(this, "VM 里引擎停不掉，没有重编", Toast.Kind.Warn); FinishNinferWsl(); });
                return;
            }
        }
        // ② 同步（脏树不构建；ff-only，绝不产生本地提交）
        var (sc, sout) = RunWslChain(distro, NinferWslSyncScript(bin, url, branch), 60_000);
        if (sc != 0)
        {
            LogN("WSL 更新在同步上游这一步停下（保留旧版，没重编）：\n" + TailText(sout, 1200));
            UiPost(() => { SetNinferWslInfo("同步上游失败", System.Drawing.Color.Firebrick);
                Toast.Show(this, "同步上游失败，没有重编（看日志）", Toast.Kind.Error); FinishNinferWsl(); });
            return;
        }
        if (sout.Trim().Length > 0) LogN("同步：" + TailText(sout, 600));
        // ③④ 构建
        var (bc, bout) = RunWslChain(distro, NinferWslBuildScript(bin), 30 * 60_000);
        if (bc != 0)
        {
            LogN("WSL 引擎编译失败（保留旧版；快照没动）：\n" + TailText(bout, 2500));
            UiPost(() => { SetNinferWslInfo("编译失败，没升级", System.Drawing.Color.Firebrick);
                Toast.Show(this, "编译失败，没有升级（看日志）", Toast.Kind.Error); FinishNinferWsl(); });
            return;
        }
        // ⑤ 快照（失败不算错：只是这一档少一层可回退的）
        var (kc, kout) = RunWslChain(distro, NinferWslSnapshotScript(bin), 60_000);
        if (kc == 0) LogN("已记录编译快照（VM 里 " + NinferWslSnapshotRoot + "，留最近 3 份）：" + kout.Trim());
        else LogN("记录编译快照失败（不影响这次更新）：" + TailText(kout, 400));
        // ⑥ 重读版本那一对（同 WIN 那条 AdoptNinferLocal 的形状：只有"有出处"的时刻才写缓存）
        var (vc, vout) = RunWslChain(distro, NinferWslProbeScript(bin), 90_000);
        var probe = ParseNinferWslProbe(vout);
        UiPost(() =>
        {
            AdoptNinferWslProbe(probe, vc);
            LogN($"WSL 引擎已更新 → {branch} @ {remoteShort}（未自动启动；点「启动」，或第一个 /v1 请求会自动拉起）。");
            SetNinferWslInfo("已升级到 " + remoteShort, System.Drawing.Color.Green);      // DEC-110③
            Toast.Show(this, "WSL 引擎已更新到 " + branch + " @ " + remoteShort, Toast.Kind.Ok);
            FinishNinferWsl();
        });
    }

    /// <summary>免重编回退（设置页 WSL 组那枚「选择快照…」）：列快照 → 选一份 → checkout + 拷回二进制（秒级）→ 重读版本。</summary>
    internal void NinferWslRollback()
    {
        var s = _config.Current;
        if (VersionProbeDisabledForTest) return;                 // 测试态绝不进 VM（同链那一条判据）
        if (NinferUiBusy) return;
        if (!s.NinferWslOn) { Toast.Show(this, "WSL 那一侧没启用（设置页 Ninfer 节「启用 WSL 后端」）", Toast.Kind.Info); return; }
        var distro = s.NinferDistro.Trim();
        var bin = s.NinferWslBin.Trim();
        if (distro.Length == 0 || bin.Length == 0)
        {
            LogN("WSL 回退没开始：发行版与引擎可执行路径不能空（设置页 Ninfer 节 WSL 那一组）。");
            Toast.Show(this, "WSL 回退没开始：先去设置页把发行版与引擎路径填上", Toast.Kind.Warn);
            return;
        }
        LogN("$ 读 WSL 编译快照清单（VM 里 " + NinferWslSnapshotRoot + "）…");
        _ = Task.Run(() =>
        {
            var (code, output) = RunWslChain(distro, NinferWslSnapshotsListScript(), 30_000);
            var snaps = ParseNinferWslSnapshotList(output);
            if (snaps.Count == 0)
            {
                UiPost(() =>
                {
                    LogN("没有可用的编译快照（VM 里 " + NinferWslSnapshotRoot + "），没法回退。");
                    Toast.Show(this, "没有可用的编译快照", Toast.Kind.Info);
                });
                return;
            }
            UiPost(() =>
            {
                if (IsDisposed) return;
                int initial = snaps.Count > 1 ? 1 : 0;           // 默认选"上一份"（非最新），同旧壳
                var pick = Dialogs.PickSnapshot(this, snaps, initial,
                    "选择要回退到的编译快照（目录名 = 那一次重编的提交短号）");
                if (pick is null) return;                        // 取消 = 完全静默（DEC-068）
                if (NinferUiBusy) return;
                _ninferUpdating = true;
                SetNinferWslBusy("回退中…", "回退 WSL 引擎（恢复快照，免重编）…");
                LogN($"$ 回退 WSL 引擎到 {pick}（checkout + 拷回快照二进制，免重编）");
                _ = Task.Run(() => RunWslRollback(distro, bin, pick)).ContinueWith(t =>
            {
                if (t.Exception is null) return;                       // DEC-136（B-1）同上一条：回退那条也不能一抛就把忙态锁死
                LogN("WSL 回退出了意外：" + t.Exception.GetBaseException().Message);
                UiPost(() => { SetNinferWslInfo("回退没完成（看日志）", System.Drawing.Color.Firebrick); FinishNinferWsl(); });
            }, System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
            });
        });
    }

    void RunWslRollback(string distro, string bin, string commit)
    {
        UiPost(() => { if (NinferRunning && _ninferRunningBackend == NinferIni.BackendWsl) StopNinferServer(); });
        if (!WaitNinferProcessGone(TimeSpan.FromSeconds(30)))
        {
            LogN("WSL 回退没开始：引擎停不下来（进程还在）。先手动停掉再试。");
            UiPost(() => { SetNinferWslInfo("停不下来，没回退", System.Drawing.Color.Firebrick);
                Toast.Show(this, "引擎停不下来，没有回退", Toast.Kind.Error); FinishNinferWsl(); });
            return;
        }
        var (rc, rout) = RunWslChain(distro, NinferWslRollbackScript(bin, commit), 60_000);
        if (rc != 0)
        {
            LogN("WSL 回退失败：\n" + TailText(rout, 1000));
            UiPost(() => { SetNinferWslInfo("回退失败", System.Drawing.Color.Firebrick);
                Toast.Show(this, "回退失败（看日志）", Toast.Kind.Error); FinishNinferWsl(); });
            return;
        }
        var (vc, vout) = RunWslChain(distro, NinferWslProbeScript(bin), 90_000);
        var probe = ParseNinferWslProbe(vout);
        UiPost(() =>
        {
            AdoptNinferWslProbe(probe, vc);
            LogN($"已回退到 {commit}（免重编；未自动启动，点「启动」或等第一个请求）。");
            SetNinferWslInfo("已回退到 " + commit, System.Drawing.Color.Green);      // DEC-110③
            Toast.Show(this, "WSL 引擎已回退到 " + commit, Toast.Kind.Ok);
            FinishNinferWsl();
        });
    }

    /// <summary>重编/回退之后把"版本 + 当时的 sha"这一对刷新（DEC-097④ 的写入时机第二条）。
    /// 探到未知构建（读得到 sha、describe 失败）也照记 —— 口径同 <see cref="EnsureNinferWslVersionProbed"/>。</summary>
    void AdoptNinferWslProbe((string Version, string Sha, string? Problem) p, int exitCode)
    {
        if (p.Sha.Length == 0)
        {
            LogN("重读 WSL 版本没成（退出码 " + exitCode + "）：" + (p.Problem ?? "没读到 sha"));
            return;
        }
        var ver = p.Problem is null && p.Version.Length > 0 ? p.Version : "未知构建（" + NinferShaShort(p.Sha) + "）";
        var s = _config.Current;
        if (s.NinferWslVersion == ver && string.Equals(s.NinferWslVersionSha, p.Sha, StringComparison.OrdinalIgnoreCase)) return;
        s.NinferWslVersion = ver;
        s.NinferWslVersionSha = p.Sha;
        SaveConfig(commitParams: false);
        LogN($"WSL 侧版本 = {ver}（引擎 sha256 {NinferShaShort(p.Sha)}）；已记进 MultiLauncher.ini。");
    }

    void FinishNinferWsl()
    {
        if (InvokeRequired) { try { BeginInvoke(new Action(FinishNinferWsl)); } catch { } return; }
        _ninferUpdating = false;
        _ninferWslPhase = "";
        _ninferPhase = ""; _ninferFrac = -1;
        PokeUpdateUi();
    }

    /// <summary>本机算力 → 资产名：`8.9` → `ninfer-serve-89.exe`；`12.0a` → `ninfer-serve-120a.exe`。
    /// 拿不到算力（nvidia-smi 不在/超时）就返回空串 = **不猜架构**，检查那一步会说明。</summary>
    internal static string NinferAssetForCap(string? computeCap)
    {
        var c = (computeCap ?? "").Trim();
        var m = Regex.Match(c, @"^(\d+)\.(\d+)([a-zA-Z]?)$");
        return m.Success ? $"ninfer-serve-{m.Groups[1].Value}{m.Groups[2].Value}{m.Groups[3].Value}.exe" : "";
    }

    /// <summary>问一次 nvidia-smi 要 compute capability（多卡取**第一个能对上资产表的**由调用方决定，这里只报第一张卡）。
    /// 失败返回空串 —— 判不了就当没这条判据，别拿猜出来的架构去下包。</summary>
    internal static string NinferComputeCapability()
    {
        // DEC-136（B-5）：原先这里自己拼一条 nvidia-smi（--query-gpu=compute_cap）= 全项目**第二处**问这张卡，
        // 而 `RunGpuQuery` 头上明写着"这是全项目问这张卡的唯一入口" —— 于是那道按进程的 GpuProbeDisabledForTest
        // 与证人位 GpuQueryCallsForTest 都管不到这一处（自检之所以没红，只是因为还没有用例走到它）。
        // 收成同一个入口之后：口径、超时、"先挂异步读再限时等"（DEC-061）都只剩一份。
        try
        {
            var rows = RunGpuQuery("compute_cap");
            return rows.Select(r => r.Length > 0 ? r[0].Trim() : "")
                       .FirstOrDefault(v => v.Length > 0) ?? "";
        }
        catch { return ""; }        // 判不了就当没这条判据：拿猜出来的架构去下包更糟（口径同 DEC-067）
    }

    /// <summary>DEC-136（B-1）：算 sha 不许把更新链炸掉 —— 读不动就交回空串（那一档按「未知构建」显示，
    /// 宁可不报版本号也不报一个过时的号）。`EnsureNinferWinSha` 那边一直包着 try，检查这一头以前是裸调，
    /// 两处口径不一致 = 一抛就把忙态锁死（Task 体里没兜底）。</summary>
    static string SafeNinferSha(string path)
    {
        try { return Net.Sha256File(path); } catch { return ""; }
    }

    /// <summary>releases 列表里挑**最新一条带着这支资产**的发布（列表顺序上游已按时间倒序给，但别赌，这里按 published_at 比）。
    /// 草稿照旧跳过；pre-release 照收（这条链每轮构建都发成 pre-release，与 llama.cpp 同一坑，DEC-049）。
    /// 返回七个空串 = 列表里没有匹配架构的资产 / 读不动。</summary>
    internal static (string Tag, string Notes, string Url, string Published, string Asset, string Sha, long Size)
        ParseNinferRelease(string json, string wantAsset)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonArray arr) return ("", "", "", "", "", "", 0);
            (string, string, string, string, string, string, long)? best = null;
            foreach (var n in arr)
            {
                if (n is not JsonObject r) continue;
                if (ReadBool(r["draft"])) continue;
                var tag = Str(r["tag_name"]);
                if (tag.Length == 0) continue;
                var published = Str(r["published_at"]);
                if (r["assets"] is not JsonArray assets) continue;
                foreach (var a in assets)
                {
                    if (a is not JsonObject o) continue;
                    var name = Str(o["name"]);
                    if (!string.Equals(name, wantAsset, StringComparison.OrdinalIgnoreCase)) continue;
                    var url = Str(o["browser_download_url"]);
                    if (url.Length == 0) continue;
                    var digest = Str(o["digest"]);
                    long.TryParse(Str(o["size"]), out var size);
                    var cand = (tag, Str(r["body"]), url, published, name, digest, size);
                    if (best is null || string.CompareOrdinal(cand.Item4, best.Value.Item4) > 0) best = cand;
                }
            }
            return best ?? ("", "", "", "", "", "", 0);
        }
        catch { return ("", "", "", "", "", "", 0); }
    }

    /// <summary>本地哈希反查资产表：整张表里找 `digest` 与本地 sha256 相同的那一条，返回它的 tag。
    /// 查不到返回空串（= 未知构建，界面上报 sha 前 16 位）。</summary>
    internal static string NinferTagForLocalSha(string json, string localSha)
    {
        var sha = (localSha ?? "").Trim();
        if (sha.Length == 0) return "";
        try
        {
            if (JsonNode.Parse(json) is not JsonArray arr) return "";
            foreach (var n in arr)
            {
                if (n is not JsonObject r) continue;
                var tag = Str(r["tag_name"]);
                if (r["assets"] is not JsonArray assets) continue;
                foreach (var a in assets)
                {
                    var digest = Str(a?["digest"]);
                    if (digest.Length > 0 && Net.DigestMatches(digest, sha)) return tag;
                }
            }
        }
        catch { /* 读不动 = 查不到，界面报"未知构建" */ }
        return "";
    }

    /// <summary>「版本」那一行显示什么（WIN 侧）。DEC-097④ 把"点过一次检查才有值"改掉了，次序 =
    /// 本会话刚反查到的 &gt; 缓存（本地 sha 与缓存里那对 sha 逐字节相同）&gt; 「未知构建（sha 前 16）」&gt; 读取中。
    /// **缓存不是无条件照抄**：exe 被人在本机换过（旧壳、手动覆盖）时 sha 对不上，宁可报"未知构建"
    /// 也不报一个过时的号 —— 用户口径「在成功更新前读取固定值」，前提是那支 exe 还是当初查出这个值的那一支。</summary>
    internal string NinferVersionForSettings()
    {
        EnsureNinferWinSha();
        if (_ninferLocalTag.Length > 0) return _ninferLocalTag;
        var s = _config.Current;
        var exe = NinferWinExePath(s.NinferDir);
        if (exe.Length == 0 || !File.Exists(exe)) return "引擎不在：" + s.NinferDir;
        var sha = _ninferWinShaNow ?? "";
        if (sha.Length == 0)
            return VersionProbeDisabledForTest ? "未读（自检/出图期间不读盘算 sha）"
                : _ninferWinShaComputing ? "读取中…" : "读不到哈希（exe 被占用？）· 点「更新…」查一次";
        if (s.NinferWinVersion.Length > 0
            && string.Equals(sha, s.NinferWinVersionSha, StringComparison.OrdinalIgnoreCase)) return s.NinferWinVersion;
        return "未知构建（" + NinferShaShort(sha) + "）· 点「更新…」查一次";
    }

    /// <summary>sha 短到能上界面（16 位 + 省略号；完整值只进日志与缓存字段）。</summary>
    internal static string NinferShaShort(string sha) =>
        sha.Length == 0 ? "读不到哈希" : sha[..Math.Min(16, sha.Length)] + "…";

    /// <summary>左栏「版本」行那块窄地（值列 ~S(96)、等宽 9pt）用的短写法（DEC-107，他点名：
    /// WIN 版本比较长，"只显示 -kvmem 前面的部分即可"）：
    ///   engine-v0.11.0-kvmem-20261003 → engine-v0.11.0（截到 -kvmem 之前）
    /// 其余长写法（状态类）也各缩到一个词，免得右对齐的格子里只剩尾巴：未知构建 / 读取中… / 未读 /
    /// 读不到 / 引擎不在 / 未启用 / 未查过。**只管左栏那一行** —— 设置页与更新窗照旧显示全文。</summary>
    internal static string ShortVersionForSidebar(string raw)
    {
        var v = (raw ?? "").Trim();
        if (v.Length == 0) return "-";
        var i = v.IndexOf("-kvmem", StringComparison.OrdinalIgnoreCase);
        if (i > 0) return v[..i];
        foreach (var lead in new[] { "未知构建", "读取中", "未读", "读不到", "引擎不在", "未启用", "未查过" })
            if (v.StartsWith(lead, StringComparison.Ordinal)) return lead == "读取中" ? "读取中…" : lead;
        return v;
    }

    /// <summary>本地 exe 的 sha256：会话内只算一次（那是一支 1.5 GB 的文件，读一遍盘只为刷一行字，
    /// 别在构造函数里挡界面）。算完刷一遍更新窗与设置页那三行。</summary>
    void EnsureNinferWinSha()
    {
        if (VersionProbeDisabledForTest || _ninferWinShaComputing || _ninferWinShaNow is not null) return;
        var exe = NinferWinExePath(_config.Current.NinferDir);
        if (exe.Length == 0 || !File.Exists(exe)) return;
        _ninferWinShaComputing = true;
        _ = Task.Run(() =>
        {
            string sha;
            try { sha = Net.Sha256File(exe); } catch { sha = ""; }   // 读不动（被占用）= 空串，界面按"未知"走
            if (IsDisposed) return;
            try
            {
                BeginInvoke(new Action(() =>
                {
                    _ninferWinShaNow = sha;
                    _ninferWinShaComputing = false;
                    PokeUpdateUi();                                   // 设置页那行与更新窗都跟着重读一次
                }));
            }
            catch { /* 窗口已销毁 */ }
        });
    }

    /// <summary>DEC-110⑤：exe 被换过（从备份恢复、手工覆盖）之后，"本会话刚反查到的那一串"与"本地 sha"都不再作数 ——
    /// 清掉这两个判据位，让「版本」那一行按**现在这一支**重算。DEC-097④ 那条前提就是"缓存只在 exe 还是当初查出
    /// 这个值的那一支时才照抄"，恢复正好把它打破了。抽成方法是为了自检能喂一个假值进去再验它归零。</summary>
    internal void InvalidateNinferLocalVersion()
    {
        _ninferLocalTag = ""; _ninferLocalSha = "";
        _ninferWinShaNow = null;
        PokeUpdateUi();
    }

    /// <summary>自检/出图台里**不自动做那两趟有副作用的读取**（DEC-097③④）：
    /// ① 本地 exe 的 sha256 = 读一遍 1.5 GB 的盘，只为刷一行字；
    /// ② WSL 版本 = 进一次 VM，那会把他的虚拟机拉起来（动全局状态，绝不能在跑测试时发生）。
    /// 关掉之后「版本」那两行显示的是缓存值或"读取中/未查过"，两条路径都有用例覆盖。</summary>
    internal static bool VersionProbeDisabledForTest = false;

    /// <summary>把「版本 + 当时那支 exe 的 sha256」钉进 ini（DEC-097④）。
    /// 只有两条路会调它：检查更新真反查到一个 tag、升级真把 exe 换掉了 —— 都是"这个值有出处"的时刻。
    /// 值没变就不落盘（同 llama 侧"打开看看、点保存不许污染文件"那条口径）。</summary>
    void RememberNinferWinVersion(string tag, string sha)
    {
        if (tag.Length == 0 || sha.Length == 0) return;
        var s = _config.Current;
        if (s.NinferWinVersion == tag && string.Equals(s.NinferWinVersionSha, sha, StringComparison.OrdinalIgnoreCase)) return;
        s.NinferWinVersion = tag;
        s.NinferWinVersionSha = sha;
        SaveConfig(commitParams: false);      // 顺手落盘那一档（同跳过位 / 内部端口避让），不算提交界面参数
    }

}
