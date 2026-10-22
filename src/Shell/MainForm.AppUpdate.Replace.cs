using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace MultiLauncher;

// ============================================================================
// 第 4 条更新链 · 替换与回滚（DEC-202 §5）
// ============================================================================
// 这一段是**唯一动 exe 的地方**，与 `MainForm.AppUpdate.cs`（状态链 / 探测 / 徽标）分开住：
//   · 那边已到 500 行门槛（conventions 的拆分规矩），而这一段自成一条路：准入 → 下载 → 校验 → 落位 → 一次确认
//     → 写 helper → 走现有退出流程 → helper 干活 → 下次启动读回执。按段搬出、块内不改。
//
// 三层回滚（§5.4）：① 落位失败 = helper 内当场把 .old 改回 exe；② 新 exe 起不来 = 3 秒复查不到进程就换回；
// ③ helper 整个没跑 = 手工兜底（程序目录里留着 `.old` 与 `.new`，日志与提示给路径）。
//
// 硬边界：`AppSelfReplaceDisabledForTest` 一开，这里**一步都不许走**（不下载、不写 helper、不退出）——
// 自检与出图台靠它保证"跑一次自检不会把自己的程序换掉"。
public partial class MainForm
{
    internal const string AppExeName = "MultiLauncher.exe";
    const string AppOldSuffix = ".old";
    const string AppNewSuffix = ".new";

    /// <summary>证人位：替换流程真的开跑了几次（闸开着时必须恒 0）。放在"过了闸门与准入、开始下载"那一刻 ——
    /// 计的是"这条替换流程开跑"，不是"exe 已被换掉"（后者在 helper 里，本进程看不到）。</summary>
    internal static int AppReplaceRunsForTest;
    /// <summary>证人位：被闸（测试态）或准入判据挡下了几次 —— 判据要能分开"没走到"与"走到了被挡"。</summary>
    internal static int AppReplaceSkippedForTest;

    // ---------------- 准入（§5.1 四条） ----------------

    /// <summary>**进程形态判据**（BUG-082，2026-10-23）：只看宿主是不是 dotnet，**不看文件名**。
    /// 起因：从 Release 下载的产物名带版本号（`MultiLauncher-0.1.8.5-windows-x64.exe` —— workflow 故意改的名），
    /// 旧判据按"名字必须等于 MultiLauncher.exe"直接判开发态 → **第一次跑就永远不能自更新**，
    /// 而且那句缘由写的是"用 dotnet 跑 dll"（另一件事），把人引向错误的排查方向（他 2026-10-23 实测撞上）。
    /// 替换链本身全用 `Environment.ProcessPath` 拼路径，helper 的 `taskkill`/`start` 也按这个文件名取，
    /// 所以改名完全自洽 —— 拦它没有理由。真正该拦的只有一件事：宿主是 dotnet（dll 形态，没有本程序的 exe）。</summary>
    internal static bool IsSelfReplaceableProcess(string? processPath, out string why)
    {
        why = "";
        var exe = (processPath ?? "").Trim();
        if (exe.Length == 0) { why = "拿不到当前进程的路径，不能替换自己"; return false; }
        if (!exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        { why = "当前不是 exe 形态（" + Path.GetFileName(exe) + "），不支持自动更新"; return false; }
        if (string.Equals(Path.GetFileName(exe), "dotnet.exe", StringComparison.OrdinalIgnoreCase))
        { why = "当前是开发态（用 dotnet 托管宿主跑 dll），不支持自动更新"; return false; }
        return true;
    }

    /// <summary>能不能自动换掉自己。一条不满足就拒绝（调用方把 `why` 原样给人看 + 给手动下载地址）。</summary>
    internal bool CanSelfReplace(out string why)
    {
        why = "";
        if (AppSelfReplaceDisabledForTest)
        {
            why = "测试态（自检 / 出图台）一律不替换自己";
            return false;
        }
        var exe = Environment.ProcessPath ?? "";
        if (!IsSelfReplaceableProcess(exe, out var shapeWhy))
        {
            why = shapeWhy;
            return false;
        }
        var dir = Path.GetDirectoryName(exe) ?? "";
        if (dir.Length == 0 || !CanWriteIn(dir))
        {
            why = "程序目录不可写：" + (dir.Length > 0 ? dir : exe);
            return false;
        }
        try
        {
            long need = Math.Max(_appRemoteAssetSize, 0) * 2;      // 下载件 + 落位后的那一份
            var free = new DriveInfo(Path.GetPathRoot(exe)!).AvailableFreeSpace;
            if (need > 0 && free < need)
            {
                why = $"磁盘空间不够（需要约 {need / 1048576} MB，可用 {free / 1048576} MB）";
                return false;
            }
        }
        catch { /* 读不到盘就按"没这条判据"走（口径同 DEC-067：判不了不猜） */ }
        return true;
    }

    /// <summary>真写一个探针文件再删 —— 只信这个，不靠 ACL 反查（同一台机器上"目录可写"最常见的坑是
    /// 程序装在 Program Files 下：写探针当场失败，比等到 `move` 那一步才炸要好得多）。</summary>
    static bool CanWriteIn(string dir)
    {
        var probe = Path.Combine(dir, ".ml-write-probe-" + Environment.ProcessId + ".tmp");
        try { File.WriteAllText(probe, "x"); File.Delete(probe); return true; }
        catch { try { if (File.Exists(probe)) File.Delete(probe); } catch { } return false; }
    }

    /// <summary>手动下载地址（准入不过时给人看的那一句要用它）：认得出是 GitHub 网页地址就给它的 latest 页，
    /// 否则回落默认仓库 —— 口径同 <see cref="AppRepoApiOf"/>（填坏不瘫痪）。</summary>
    internal string AppReleasePageUrl()
    {
        var url = (_config.Current.AppUpdateUrl ?? "").Trim();
        if (url.Contains("github.com", StringComparison.OrdinalIgnoreCase)
            && !url.Contains("api.github.com", StringComparison.OrdinalIgnoreCase))
            return url.Split('?')[0].TrimEnd('/') + "/releases/latest";
        return AppDefaultRepoUrl + "/releases/latest";
    }

    // ---------------- 入口：点「更新」 ----------------

    internal void StartAppUpgrade()
    {
        if (!_appHasUpdate || _appUpdating) return;
        if (AppSelfReplaceDisabledForTest) { AppReplaceSkippedForTest++; Log("本程序更新：测试态，替换一律不做。"); return; }
        if (!CanSelfReplace(out var why))
        {
            Log("本程序更新：不能自动替换 —— " + why);
            Dialogs.Message(this, "本程序更新", "这一版不能自动替换自己。",
                why + "\n\n可以到发布页手动下载：" + AppReleasePageUrl(), danger: true);
            return;
        }
        AppReplaceRunsForTest++;
        _appUpdating = true; _appBusy = "升级中…"; _appCancelWanted = false;
        ReportAppProgress("下载新版本", -1);
        Task.Run(AppUpgradeWork);
    }

    void ReportAppProgress(string phase, double frac)
    {
        if (InvokeRequired) { try { BeginInvoke(new Action(() => ReportAppProgress(phase, frac))); } catch { } return; }
        _appPhase = phase; _appFrac = frac;
        PokeUpdateUi();
    }

    static string SafeTag(string tag)
    {
        var bad = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(tag.Length);
        foreach (var c in tag) sb.Append(Array.IndexOf(bad, c) >= 0 ? '_' : c);
        return sb.ToString();
    }

    /// <summary>暂存窝：`data\tmp\app-update-<tag>\`（`data\tmp` 那条"超 3 天兜底扫"照旧兜底）。</summary>
    internal static string AppStagingDir(string tag) => Path.Combine(TempDir(), "app-update-" + SafeTag(tag));

    // ---------------- 下载 → 校验 → 落位（后台） ----------------

    void AppUpgradeWork()
    {
        string staging = "";
        try
        {
            var tag = _appRemoteTag;
            staging = AppStagingDir(tag);
            try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { /* 上一轮的残留删不掉就留着，下面按名字覆盖 */ }
            Directory.CreateDirectory(staging);
            var pkg = Path.Combine(staging, string.IsNullOrEmpty(_appRemoteAssetName) ? "MultiLauncher.exe" : _appRemoteAssetName);
            var shaFile = Path.Combine(staging, string.IsNullOrEmpty(_appRemoteShaName) ? "release.exe.sha256" : _appRemoteShaName);

            Log("$ 下载本程序新版本 " + tag + "：" + Path.GetFileName(pkg));
            string error;
            if (!Net.Download(_appRemoteAssetUrl, pkg, AppVenvPython(), out error,
                    (done, total) => ReportAppProgress("下载新版本", total > 0 ? (double)done / total : -1),
                    () => _appCancelWanted))
            {
                Log("本程序更新：下载失败，" + error);
                FinishAppUpgrade(false, error.Contains("取消") ? "cancel" : "download", staging);
                return;
            }

            ReportAppProgress("校验", -1);
            if (!Net.Download(_appRemoteShaUrl, shaFile, AppVenvPython(), out error))
            {
                Log("本程序更新：校验失败 —— .sha256 没下下来（" + error + "）。一个字节没动。");
                FinishAppUpgrade(false, "verify", staging);
                return;
            }
            var want = ParseSha256File(SafeReadText(shaFile));
            var got = Sha256OfFile(pkg);
            if (want.Length == 0 || !string.Equals(want, got, StringComparison.OrdinalIgnoreCase))
            {
                Log($"本程序更新：校验失败 —— sha256 对不上（期望 {want}，实得 {got}）。一个字节没动。");
                FinishAppUpgrade(false, "verify", staging);
                return;
            }
            // DEC-067 的同族：sha 拦传输，版本号拦"资产挂错 / 串包"
            var ver = FileVersionOf(pkg);
            if (!AppExeVersionMatches(ver, tag))
            {
                Log($"本程序更新：校验失败 —— 下载件的版本「{(ver.Length > 0 ? ver : "读不出来")}」与探到的 {tag} 不符。一个字节没动。");
                FinishAppUpgrade(false, "verify", staging);
                return;
            }
            Log("校验通过：sha256 一致、版本 " + ver + " = " + tag);

            // 落位到 exe 同卷（同目录 = 必然同卷，避免跨卷 Move）
            var exePath = Environment.ProcessPath!;
            var newPath = exePath + AppNewSuffix;
            try
            {
                if (File.Exists(newPath)) File.Delete(newPath);
                File.Move(pkg, newPath);
            }
            catch (Exception ex)
            {
                Log("本程序更新：落位失败 —— " + ex.Message + "。一个字节没动。");
                FinishAppUpgrade(false, "stage", staging);
                return;
            }
            Log("新版本已就位：" + newPath + "（等一次确认）");
            ReportAppProgress("准备替换", 1);
            UiPost(() => AppConfirmAndSwap(staging, exePath, newPath));
        }
        catch (Exception ex)
        {
            Log("本程序更新出错：" + ex.Message);
            FinishAppUpgrade(false, "exception", staging);
        }
    }

    // ---------------- 一次确认 → 写 helper → 走现有退出流程（UI 线程） ----------------

    void AppConfirmAndSwap(string staging, string exePath, string newPath)
    {
        if (AppSelfReplaceDisabledForTest)          // 二次拦：这一条路绝不能因为别处改坏而真换掉 exe
        {
            AppReplaceSkippedForTest++;
            TryDeleteFile(newPath);
            FinishAppUpgrade(false, "blocked", staging);
            return;
        }
        var r = Dialogs.Confirm(this, "更新本程序",
            "更新需要重启本程序，正在运行的服务会先停下。", "现在更新？", yes: "现在更新", no: "稍后");
        if (r != DialogResult.Yes)
        {
            // 方案 §5.5 的收口：取消 = 当场清（下一版重新下），别把半套东西留在程序目录旁边
            TryDeleteFile(newPath);
            Log("本程序更新：已取消，下载件已清（下次点「更新」会重新下）。");
            FinishAppUpgrade(false, "cancel", staging);
            return;
        }

        var helper = Path.Combine(staging, "swap.cmd");
        try
        {
            File.WriteAllText(helper, SwapHelperText(exePath, newPath, staging, Environment.ProcessId), new UTF8Encoding(false));
            var psi = new ProcessStartInfo("cmd.exe")
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = staging,
            };
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add(helper);
            Process.Start(psi);
            Log("本程序更新：已拉起替换 helper（" + helper + "），接下来走正常退出流程。");
        }
        catch (Exception ex)
        {
            TryDeleteFile(newPath);
            Log("本程序更新：helper 起不来 —— " + ex.Message + "。本机保持原样。");
            FinishAppUpgrade(false, "helper", staging);
            return;
        }
        // 走**现有退出流程**（停服务 → 落配置 → 收托盘 → 释放互斥体）；helper 在那边等本进程消失
        ExitApp();
    }

    /// <summary>helper 脚本的正文（纯函数：自检直接断它该有的几行，别靠读真文件）。
    /// 用 cmd 而不是 pwsh：机器上必然有 cmd；也不依赖 PowerShell 执行策略。
    /// **正文全 ASCII** —— 中文在 cmd 里要配 chcp 65001 才不糊，脚本没有对人说的字，不需要冒这个险。</summary>
    internal static string SwapHelperText(string exePath, string newPath, string staging, int pid)
    {
        var oldPath = exePath + AppOldSuffix;
        var result = Path.Combine(staging, "result.txt");
        // 进程名与 exe 文件名同源（不写字面量）：真跑时就是 MultiLauncher.exe；
        // 演练时换个名字，`taskkill /im` 才不会误伤正在跑的正牌壳。
        var procName = Path.GetFileName(exePath);
        var sb = new StringBuilder();
        sb.AppendLine("@echo off");
        sb.AppendLine("setlocal");
        // 回执里那一行带 `%DATE% %TIME%`，中文区域的日期里有"周三"——不切 65001 读回来是问号（实测）。
        // 只影响给人看的时间戳，不影响 `ok` / `fail` 那个首词的判定。
        sb.AppendLine("chcp 65001 >nul");
        sb.AppendLine("rem MultiLauncher self-update helper (written by the app; keep this file ASCII-only)");
        sb.AppendLine($"set \"EXE={exePath}\"");
        sb.AppendLine($"set \"NEW={newPath}\"");
        sb.AppendLine($"set \"OLD={oldPath}\"");
        sb.AppendLine($"set \"RESULT={result}\"");
        sb.AppendLine("set N=0");
        sb.AppendLine(":wait");
        sb.AppendLine($"tasklist /FI \"PID eq {pid}\" 2>nul | find /i \"{procName}\" >nul");
        sb.AppendLine("if errorlevel 1 goto proceed");
        sb.AppendLine("set /a N+=1");
        sb.AppendLine("if %N% GEQ 60 goto proceed");
        // 等待**不能用 `timeout`**：helper 是由壳以 CreateNoWindow 拉起的，stdin 被重定向，
        // `timeout` 会当场报 "Input redirection is not supported" 并立刻返回 → 3 秒复查变成 0 秒，
        // 新 exe 还没起来就被判「起不来」→ 每次更新都会误回滚（本仓 2026-10-21 用 swapdrill 演练抓到的）。
        // `ping -n N 127.0.0.1` 是同一件事里唯一不读 stdin 的等待（首次立即回显，所以 N+1 才约等于 N 秒）。
        sb.AppendLine("ping -n 2 127.0.0.1 >nul");
        sb.AppendLine("goto wait");
        sb.AppendLine(":proceed");
        sb.AppendLine("if exist \"%EXE%\" move /y \"%EXE%\" \"%OLD%\" >nul 2>&1");
        sb.AppendLine("move /y \"%NEW%\" \"%EXE%\" >nul 2>&1");
        sb.AppendLine("if errorlevel 1 goto rollback_move");
        sb.AppendLine("start \"\" \"%EXE%\"");
        sb.AppendLine("ping -n 4 127.0.0.1 >nul");
        sb.AppendLine($"tasklist /FI \"IMAGENAME eq {procName}\" 2>nul | find /i \"{procName}\" >nul");
        sb.AppendLine("if errorlevel 1 goto rollback_start");
        sb.AppendLine("echo ok %DATE% %TIME%> \"%RESULT%\"");
        sb.AppendLine("exit /b 0");
        sb.AppendLine(":rollback_move");
        sb.AppendLine("if exist \"%OLD%\" move /y \"%OLD%\" \"%EXE%\" >nul 2>&1");
        sb.AppendLine("echo fail move %DATE% %TIME%> \"%RESULT%\"");
        sb.AppendLine("if exist \"%EXE%\" start \"\" \"%EXE%\"");
        sb.AppendLine("exit /b 1");
        sb.AppendLine(":rollback_start");
        sb.AppendLine($"taskkill /f /im {procName} >nul 2>&1");
        sb.AppendLine("if exist \"%OLD%\" move /y \"%OLD%\" \"%EXE%\" >nul 2>&1");
        sb.AppendLine("echo fail start %DATE% %TIME%> \"%RESULT%\"");
        sb.AppendLine("if exist \"%EXE%\" start \"\" \"%EXE%\"");
        sb.AppendLine("exit /b 1");
        return sb.ToString();
    }

    // ---------------- 收口 ----------------

    /// <summary>这条流程的收尾（成功 = 已交给 helper，本进程即将退出）。失败 / 取消 / 校验不过 = 当场清暂存窝。</summary>
    void FinishAppUpgrade(bool ok, string stage, string staging)
    {
        if (InvokeRequired) { try { BeginInvoke(new Action(() => FinishAppUpgrade(ok, stage, staging))); } catch { } return; }
        _appUpdating = false;
        _appBusy = "";
        _appPhase = ""; _appFrac = -1;
        _appCancelWanted = false;
        if (!ok && stage != "prepared") CleanupStagingDir(staging);
        switch (stage)
        {
            case "cancel": SetAppInfo("已取消", UiTheme.TextMuted); break;
            case "blocked": SetAppInfo("测试态：没有替换", UiTheme.TextMuted); break;
            case "download": SetAppInfo("下载失败（详见日志）", Color.Firebrick); break;
            case "verify": SetAppInfo("校验失败（一个字节没动）", Color.Firebrick); break;
            case "stage": SetAppInfo("落位失败（一个字节没动）", Color.Firebrick); break;
            case "helper": SetAppInfo("helper 起不来（本机保持原样）", Color.Firebrick); break;
            default: SetAppInfo("升级没有完成（详见日志）", Color.Firebrick); break;
        }
        PokeUpdateUi();
    }

    static string Sha256OfFile(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
        }
        catch { return ""; }
    }

    static string FileVersionOf(string path)
    {
        try { return FileVersionInfo.GetVersionInfo(path).FileVersion ?? ""; } catch { return ""; }
    }

    static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* 删不掉就留给兜底扫 */ }
    }

    // ---------------- 下次启动读回执（§5.2 第 8 条） ----------------

    /// <summary>helper 写的 `result.txt`：一行 `ok &lt;时间&gt;` / `fail &lt;阶段&gt; &lt;时间&gt;`。
    /// 只看最近一份暂存窝；ok = 日志一行，fail = 日志 + 一条 toast（启动期不弹模态框），并把手工兜底的路径写清。</summary>
    void ReadAppUpdateReceipt()
    {
        try
        {
            var root = TempDir();
            if (!Directory.Exists(root)) return;
            foreach (var dir in Directory.GetDirectories(root, "app-update-*").OrderByDescending(d => d, StringComparer.Ordinal))
            {
                var f = Path.Combine(dir, "result.txt");
                if (!File.Exists(f)) continue;
                var first = SafeReadText(f).Replace('\r', '\n').Split('\n').FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "";
                if (first.Length == 0) continue;
                // BUG-082：兜底路径按**当前这个 exe 的真实文件名**给（可能是 `MultiLauncher-0.1.8.5-windows-x64.exe`），
                // 别拿常量拼 —— 那样指向的文件根本不存在，叫人去哪儿找 .old
                var exeFull = Environment.ProcessPath ?? "";
                var exeDir = exeFull.Length > 0 ? Path.GetDirectoryName(exeFull) ?? "" : "";
                var exeName = exeFull.Length > 0 ? Path.GetFileName(exeFull) : AppExeName;
                if (first.StartsWith("ok", StringComparison.OrdinalIgnoreCase))
                {
                    Log("本程序已更新（helper 回执：" + first + "）；旧版留在 " + Path.Combine(exeDir, exeName + AppOldSuffix));
                }
                else
                {
                    Log("本程序更新没成功（helper 回执：" + first + "）。旧版已自动换回；手工兜底的两处：" +
                        Path.Combine(exeDir, exeName + AppOldSuffix) + " 与 " + Path.Combine(exeDir, exeName + AppNewSuffix) +
                        "（把 .old 去掉后缀即可回到旧版）。");
                    Toast.Show(this, "上次更新没成功，已自动回到旧版（详见日志）", Toast.Kind.Warn);
                }
                return;
            }
        }
        catch { /* 回执读不动不影响启动 */ }
    }
}
