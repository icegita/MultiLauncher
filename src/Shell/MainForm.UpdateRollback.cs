using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;

namespace MultiLauncher;

// 回退（设置页各节「回退…」那一族：选快照 → 确认 → 覆盖 → 收尾）。
// 2026-10-15 从 MainForm.Update.cs 整段搬出，一行未改。
public partial class MainForm
{
    // ---------------- 回退 ----------------

    /// <summary>Strata 升级快照的家（DEC-124 改落点）= Strata 自己的数据目录里那一格 `&lt;目录名&gt;-data\backup`（本机 = `D:\Strata-data\backup\`）。
    /// 用户口径：「还是把备份放到 `D:\Strata-data` 这个文件夹里吧，文件夹名使用 backup 这个名称即可」——
    /// 旁边那只 `D:\Strata-data\`（models / mtp / wheels / packs 那一层，本机 160 GB）现成就存在，不再另开一只同级 `D:\Strata-backup\`。
    /// **这一条是他点名授权往 Strata 的数据目录里写的**，与 `D:\Strata`（配置与源码，最硬边界）仍是两回事：那条一字未动。
    /// 三条老理由只换了一半 —— "脱离壳也能恢复"与"不进 `D:\Strata`"照旧，"不进 Strata 任何目录"由他这句话作废。
    /// 纯函数：**不建目录也不探针** —— 列清单这种读操作不许有副作用，真要写快照的那一步自己建。
    /// Strata 目录压根没配 → 回落到 `data\strata-snap\`（**不是** `data\backup\`，那一层只装配置备份）。</summary>
    internal static string StrataBackupRoot(string strataRoot)
    {
        try
        {
            var trimmed = (strataRoot ?? "").Trim().TrimEnd('\\', '/');
            var parent = Path.GetDirectoryName(trimmed);
            var leaf = Path.GetFileName(trimmed);
            if (!string.IsNullOrEmpty(parent) && !string.IsNullOrEmpty(leaf))
                return Path.Combine(parent, leaf + "-data", "backup");
        }
        catch { /* 形状不对就走回落 */ }
        return Path.Combine(LauncherDataDir(), "strata-snap");
    }

    /// <summary>DEC-116 那一版的快照落点 = 同级 `&lt;目录名&gt;-backup`（本机 `D:\Strata-backup\`）。
    /// DEC-124 之后它只是**搬家要读的老位置**：里面的快照**复制**到新落点、老那份一个字不删（用户："旧的位置不要删除，留着我手动删除并提醒我"），
    /// 所以这一头还留着 <see cref="LegacySnapshotsPending"/> 那道提醒判据 —— 别再当落点用。</summary>
    internal static string StrataBackupRootLegacy(string strataRoot)
    {
        try
        {
            var trimmed = (strataRoot ?? "").Trim().TrimEnd('\\', '/');
            var parent = Path.GetDirectoryName(trimmed);
            var leaf = Path.GetFileName(trimmed);
            if (!string.IsNullOrEmpty(parent) && !string.IsNullOrEmpty(leaf))
                return Path.Combine(parent, leaf + "-backup");
        }
        catch { /* 形状不对就没有老位置可言 */ }
        return "";
    }

    /// <summary>当前配置下快照的家（日志、关于页、回退入口、搬家都用它，别各处拼字面量）。</summary>
    internal string CurrentStrataBackupRoot() => StrataBackupRoot(_config.Current.Root);

    /// <summary>DEC-110⑤：整份快照也留最近 3 份（用户口径「所有模块的备份数量调整成 3 份」，原为不限份数一直堆）。</summary>
    internal const int StrataBackupKeep = 3;

    /// <summary>按**写入时间**新→旧排（只给轮转用）。清单那一头仍按名字排（人看的次序），
    /// 但"删最旧"不能靠名字里的版本号比大小 —— `0.1.9` 与 `0.1.10` 的字典序会把新的排在后面，
    /// 拿它去删就是删错方向。时间戳目录的 LastWriteTime 就是那次覆盖完成的时候，判据更硬。</summary>
    static List<string> ListBackupDirsByTime(string strataRoot)
    {
        var root = StrataBackupRoot(strataRoot);
        if (!Directory.Exists(root)) return new List<string>();
        try
        {
            // DEC-134②：轮转这一头也必须认"这是不是壳造的整份快照"，判据与清单（ListBackups）、搬家
            //（LegacySnapshotsPending）用的是**同一两条**，不许写第三套。那一格住在用户 Strata 的数据目录里
            //（D:\Strata-data\backup），原先这里一句过滤都没有 = 名字不像快照的目录（他手工留的、别的工具的输出、
            // 或退役前那种形状）只要排在第 4 位之后，就被 Directory.Delete(recursive:true) 连根删掉、不留退路。
            // 经验库 config/ini就地读写保真.md §6 最后一条写的正是这件事（共用父目录时"列所有子目录当备份"会被污染）。
            return Directory.GetDirectories(root)
                .Where(d => LooksLikeSnapshot(Path.GetFileName(d)) && SnapshotHasPayload(d))
                .OrderByDescending(d => SafeWriteTime(d))
                .Select(Path.GetFileName).Where(n => n is { Length: > 0 }).ToList()!;
        }
        catch { return new List<string>(); }
    }

    static DateTime SafeWriteTime(string dir)
    {
        try { return Directory.GetLastWriteTime(dir); } catch { return DateTime.MinValue; }
    }

    /// <summary>超额收口：只留最近 keep 份整份快照，删最旧（返回删掉的份数）。删不动就留着，别把升级流程带崩。
    /// DEC-116：快照住在 <see cref="StrataBackupRoot"/>，所以这里要有 Strata 目录这个参数 —— 拿不到根就没法知道家在哪。</summary>
    internal static int TrimStrataBackups(string strataRoot, int keep = StrataBackupKeep)
    {
        int del = 0;
        foreach (var name in ListBackupDirsByTime(strataRoot).Skip(keep))
        {
            try { Directory.Delete(Path.Combine(StrataBackupRoot(strataRoot), name), recursive: true); del++; }
            catch { /* 被占用之类：多留一份不影响正确性，删错了才是 */ }
        }
        return del;
    }

    // 可回退的备份（目录名 = 升级前版本-时间戳），最新在前。
    // DEC-116 之后这一头读的是 D:\Strata-backup\；`data\backup\` 下的子目录现在是**模块名**（llama/ninfer/...），
    // 再按"子目录全当 Strata 备份"列就会把配置备份格端上「回退到哪一份备份」那个列表 —— 这条判据随搬家改写。
    // DEC-121 再加一面：**形状不完整的快照不上桌**。搬家跨卷那一步以前会留下"只剩 about.txt、source\ 与 engine\ 还在老位置"
    // 的半截格子（他实跑抓到），那种格子列出来点下去必炸 —— 宁可列表空着说"还没有升级备份"，也不端一份点不动的。
    internal static List<string> ListBackups(string strataRoot)
    {
        var root = StrataBackupRoot(strataRoot);
        if (!Directory.Exists(root)) return new List<string>();
        try
        {
            return Directory.GetDirectories(root)
                .Where(SnapshotHasPayload)
                .Select(Path.GetFileName)
                .Where(n => n is { Length: > 0 })
                .OrderByDescending(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList()!;
        }
        catch { return new List<string>(); }
    }

    /// <summary>这一格是不是"有货"的快照 = `source\` 与 `engine\` 至少有一格**且不为空**。
    /// 只有 about.txt 那种半截格子判 false（老实现跨卷搬家留下的形状）；判据只认目录，不认版本号写法。</summary>
    internal static bool SnapshotHasPayload(string dir)
    {
        foreach (var part in new[] { "source", "engine" })
        {
            try
            {
                var p = Path.Combine(dir, part);
                if (Directory.Exists(p) && Directory.EnumerateFileSystemEntries(p).Any()) return true;
            }
            catch { /* 读不到就当没这一格，两面都没有才判不完整 */ }
        }
        return false;
    }

    // DEC-061：删掉 RefreshRollbackState()——它只剩一个 BeginInvoke 空转，「回退…」按钮长在设置页 Strata 节，
    // 没有需要它刷新的常驻控件（RollbackWork 的 finally 里也不再调它）。

    internal void Rollback()
    {
        if (_updating) return;
        var snaps = ListBackups(_config.Current.Root);
        if (snaps.Count == 0) { LogS("还没有升级备份，无法回退。"); return; }
        string? pick = snaps.Count == 1 ? snaps[0] : Dialogs.PickSnapshot(this, snaps, 0);
        if (pick is null) return;
        var old = _process;
        if (old is { HasExited: false })
        {
            if (Dialogs.Confirm(this, "回退前先停服务", "回退需要先停止正在运行的服务。", "现在停止并继续回退？",
                    yes: "停止并回退", no: "取消") != DialogResult.Yes)
                return;
            StopServer();
        }
        var s = CollectSettings();
        _updating = true;
        _updateBusy = "回退中…";
        SetState("● 回退中…", Color.DarkOrange);
        PokeUpdateUi();
        Task.Run(() => RollbackWork(s, old, pick));
    }

    void RollbackWork(Settings s, Process? old, string entry)
    {
        try
        {
            if (old is not null) { try { old.WaitForExit(20000); } catch { /* 已退出 */ } }
            var dir = Path.Combine(StrataBackupRoot(s.Root), entry);
            int files = 0, engines = 0;
            var srcDir = Path.Combine(dir, "source");
            if (Directory.Exists(srcDir))
            {
                foreach (var f in Directory.EnumerateFiles(srcDir, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(srcDir, f);
                    var dst = Path.Combine(s.Root, rel);
                    var d = Path.GetDirectoryName(dst);
                    if (d is not null) Directory.CreateDirectory(d);
                    File.Copy(f, dst, true);
                    files++;
                }
            }
            var engDir = Path.Combine(dir, "engine");
            if (Directory.Exists(engDir))
            {
                foreach (var f in Directory.EnumerateFiles(engDir))
                {
                    File.Copy(f, Path.Combine(s.Root, "engine", Path.GetFileName(f)), true);
                    engines++;
                }
            }
            LogS($"已回退到 {entry}：源码还原 {files} 个文件、引擎文件 {engines} 个（免重编）");
            BeginInvoke(new Action(() =>
            {
                RefreshVersions();
                RefreshModels();
                LogS("回退完成，点「启动」即可用回退后的版本跑模型");
                Toast.Show(this, "已回退到 " + entry + "，点「启动」即用回退后的版本跑模型", Toast.Kind.Ok);
            }));
            SetState("已停止（已回退，未启动）", Color.Gray);
        }
        catch (Exception ex)
        {
            LogS("回退出错: " + ex.Message);
            Toast.Show(this, "回退没有完成：" + ex.Message, Toast.Kind.Error);
        }
        finally
        {
            FinishUpdate();
        }
    }

    // 覆盖时保留本机的东西：见文件头的说明；另外 upstream 的源码包里本来就没有这些。
    internal static bool Preserve(string rel)
    {
        var parts = rel.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;
        if (PreserveTop.Contains(parts[0], StringComparer.OrdinalIgnoreCase)) return true;
        var name = parts[^1];
        // 壳自己的设置文件：新旧两个名字都要保住（DEC-037 改名，但升级可能发生在改名之前）
        if (name.Equals(ConfigFileName, StringComparison.OrdinalIgnoreCase)
            || name.Equals(LegacyConfigFileName, StringComparison.OrdinalIgnoreCase)) return true;
        if (name.EndsWith(".log", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.EndsWith(".shared-settings.json", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.StartsWith("strata-", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.StartsWith("run-", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    static int RunStreaming(string exe, string[] args, string? cwd, Action<string> onLine)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        if (cwd is not null) psi.WorkingDirectory = cwd;
        using var p = Process.Start(psi);
        if (p is null) return -1;
        p.OutputDataReceived += (_, e) => { if (e.Data is not null) onLine(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data is not null) onLine(e.Data); };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        p.WaitForExit();
        return p.ExitCode;
    }

    internal static int CompareVersion(string a, string b)
    {
        var pa = ParseVersion(a);
        var pb = ParseVersion(b);
        for (int i = 0; i < Math.Max(pa.Length, pb.Length); i++)
        {
            int x = i < pa.Length ? pa[i] : 0;
            int y = i < pb.Length ? pb[i] : 0;
            if (x != y) return x.CompareTo(y);
        }
        return 0;
    }

    // DEC-061：每段只取**开头的数字**，后面的后缀丢掉。原来整段 int.TryParse，"0.1.36-rc1" 的第三段解析失败当 0
    // → 比本地 0.1.35 还小 → 明明有新版却报「已是最新」；现在按 36 比，后缀不进判据。
    static int[] ParseVersion(string v) => v.Split('.', StringSplitOptions.RemoveEmptyEntries)
        .Select(LeadingInt).ToArray();

    static int LeadingInt(string s)
    {
        long n = 0;
        foreach (var ch in s) { if (!char.IsAsciiDigit(ch)) break; n = Math.Min(n * 10 + (ch - '0'), int.MaxValue); }
        return (int)n;
    }
}
