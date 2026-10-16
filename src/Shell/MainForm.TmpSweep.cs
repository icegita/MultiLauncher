using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MultiLauncher;

// DEC-114：data\tmp 的三条收口（更新链 finally 收 / 自检与探针跑完清自己那窝 / 开窗兜底扫）。
// 2026-10-15 从 MainForm.Models.cs 整段搬出，一行未改。
public partial class MainForm
{
    // ---------------- DEC-114：data\tmp 的三条收口（用户 2026-10-08 批复「tmp 清理三条」）----------------
    // 实测账：data\tmp 一度 240 MB，其中 **232.2 MB 是历次 Strata 更新的下载与解压残留**
    //（5 个 `strata-update-<版本>\` + 3 个 `strata-<版本>.zip`），自检与探针那些窝总共才 3.3 MB。
    // 根因在代码里看得清清楚楚：`MainForm.Update.cs` 以前**只在取消/失败那两条分支删解压目录**，
    // 成功路径不收尾 = **升级越成功、垃圾攒越多**。
    // 三条收口 = ① 更新链跑完 `finally` 收尾（`CleanupStagingDir`，成功也收）
    //             ② 自检与出图台跑完清自己那一窝（`CleanTestScratch`，**认名字**）
    //             ③ 启动兜底扫超期残留（`SweepTempDir`，**认年龄**，给"上次崩在半路"那种收口）。
    // **不做「退出前清」**：崩了就没它了，而且会把调试现场顺手删掉。
    // 两套判据不许混：② 认名字、③ 认年龄，各有一条纯函数当家，自检按组合穷举（只有正面判据的护栏会假绿 = DEC-111 的教训）。

    /// <summary>③ 的保留天数。**不给设置页格子**（用户批复：这些东西本来就该跑完就没，天数只是兜底策略的内部参数）。</summary>
    internal const int TmpRetentionDays = 3;

    /// <summary>② 的名单：自检与出图台的窝（两条前缀 + 当年没走前缀命名的三个旧名字，登记在此一处，别在调用点各写一套）。</summary>
    internal static readonly string[] TestScratchPrefixes = { "selftest-", "uiprobe-" };
    internal static readonly string[] TestScratchNames = { "fit-root", "root-probe", "ninfer-fidelity" };

    /// <summary>兜底扫与清窝的根 = **只认自家 `&lt;程序目录&gt;\data\tmp`，永远不跟 `TempDir()` 走**。
    /// `TempDir()` 在 data\tmp 不可写时会退回 `%TEMP%`，而 `%TEMP%` 是全机器的公共抽屉 ——
    /// 拿"按年龄扫"去扫它等于删别人正在用的东西。认不出自家那一层就什么都不做。</summary>
    internal static string? ScratchSweepRoot()
    {
        var own = Path.Combine(LauncherDataDir(), "tmp");
        return Directory.Exists(own) ? own : null;
    }

    /// <summary>② 的名字判据（纯函数）：名单里的，或以 `selftest-` / `uiprobe-` 开头的，或**名字里带着这两个词根的**
    /// （后面那半不是冗余：自检的假发布列表叫 `strata-selftest-release.json`、缓存副本叫
    /// `llama-selftest-releases.json`，"selftest" 在中间不在开头，实测这两份就从前缀判据手底下漏出去了）。</summary>
    internal static bool IsTestScratch(string name) =>
        TestScratchNames.Contains(name, StringComparer.OrdinalIgnoreCase) ||
        TestScratchPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase)) ||
        name.Contains("selftest", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("uiprobe", StringComparison.OrdinalIgnoreCase);

    /// <summary>③ 的年龄判据（纯函数）：比保留期**严格更早**才算过期（正好踩在那一天的不动，免得边界上误删）。</summary>
    internal static bool IsStaleScratch(DateTime newestWrite, DateTime now, int keepDays = TmpRetentionDays) =>
        now - newestWrite > TimeSpan.FromDays(keepDays);

    /// <summary>一棵树里最新的写入时间。目录自己的 `LastWriteTime` 只看得到**直接子项**的增减，
    /// 深层刚写过的文件不会顶它 —— 单独拿它判年龄会误删正在用的东西，所以要扫全树取最大。</summary>
    internal static DateTime NewestWriteIn(string path)
    {
        DateTime newest = Directory.GetLastWriteTime(path);
        try
        {
            foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                var w = File.GetLastWriteTime(f);
                if (w > newest) newest = w;
            }
        }
        catch { /* 读不动 = 拿已经握到的那个数，宁可少删 */ }
        return newest;
    }

    /// <summary>删一棵树 / 一个文件。删不动就当没这条（被占用 = 有别的东西正在用，下一次启动再来）。</summary>
    internal static bool TryDeleteTree(string path)
    {
        try
        {
            if (Directory.Exists(path)) { Directory.Delete(path, recursive: true); return true; }
            if (File.Exists(path)) { File.Delete(path); return true; }
            return false;
        }
        catch { return false; }
    }

    /// <summary>① 的收尾：更新链在 tmp 里留下的**解压目录**，跑完（成没成都）收掉。
    /// 下载包不在这里删 —— llama 那条链完事会问一句「刚下载的压缩包要删吗」（DEC-049 的形状），
    /// 留不留归用户；历次留下的老包由 ③ 按年龄收。</summary>
    internal static bool CleanupStagingDir(string? ext) => ext is not null && TryDeleteTree(ext);

    /// <summary>② 的落点：收掉**本进程点名过**的那些测试窝，返回删掉的项数。
    /// DEC-138（D-8）：判据从"名字像测试窝就删"收成"名字像 + 这一趟真点名过"。
    /// 为什么必须收：`--selftest` 与 `--uiprobe` 都不吃单实例互斥体（AGENTS「探针实例之间会互相污染」那一行），
    /// 两个实例可以同时活着 —— 后跑完那一个按名字清窝，会把前一个正在用的输入连锅端掉，
    /// 而它自己那批用例只是走"目录不在就跳过"的分支，末行照旧 ALL PASS = **静默假绿**（正是 DEC-111 那条
    /// "护栏要看它挡的是不是整条路"的反面：这道护栏挡太多，把别人的路也挡了）。</summary>
    internal static int CleanTestScratch()
    {
        var root = ScratchSweepRoot();
        string[] own;
        lock (_ownScratch) own = _ownScratch.ToArray();
        return root is null ? 0 : CleanScratchOwnedIn(root, own);
    }

    /// <summary>DEC-138（D-8）：`Scratch(name)` = 取路径 + 登记"这一趟用过它"（名单只有一个家 = <c>_ownScratch</c>）。</summary>
    static readonly HashSet<string> _ownScratch = new(StringComparer.OrdinalIgnoreCase);
    internal static string Scratch(string name)
    {
        lock (_ownScratch) _ownScratch.Add(name);
        return Path.Combine(TempDir(), name);
    }
    /// <summary>自检用：本进程点名过几格（两面判据 = 点名的删掉了、没点名的留着）。</summary>
    internal static int ScratchOwnedCountForTest { get { lock (_ownScratch) return _ownScratch.Count; } }
    internal static void ForgetScratchForTest() { lock (_ownScratch) _ownScratch.Clear(); }

    /// <summary>② 的删手核心（只有一个）：按名单收，名单里混进"不像测试窝"的名字也删不动 —— 名字判据是第二道保险。</summary>
    internal static int CleanScratchOwnedIn(string root, IEnumerable<string> own)
    {
        int n = 0;
        if (!Directory.Exists(root)) return 0;
        foreach (var name in own.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!IsTestScratch(name)) continue;
            var path = Path.Combine(root, name);
            if ((Directory.Exists(path) || File.Exists(path)) && TryDeleteTree(path)) n++;
        }
        return n;
    }

    /// <summary>② 的旧入口（按名字清整层）：留给 fixture 与"名单=目录里全部"这一档，**真路径不再走它**。</summary>
    internal static int CleanTestScratchIn(string root)
    {
        if (!Directory.Exists(root)) return 0;
        return CleanScratchOwnedIn(root, Directory.GetFileSystemEntries(root).Select(Path.GetFileName)!);
    }

    /// <summary>③ 的落点：启动兜底扫。返回删掉的项数与回收字节（0 = 界面上一行都不写）。</summary>
    internal static (int Count, long Bytes) SweepTempDir(int keepDays = TmpRetentionDays)
    {
        var root = ScratchSweepRoot();
        return root is null ? (0, 0) : SweepIn(root, DateTime.Now, keepDays);
    }

    /// <summary>按年龄扫一层（fixture 版）：读不动的条目**不判**，绝不猜着删。</summary>
    internal static (int Count, long Bytes) SweepIn(string root, DateTime now, int keepDays = TmpRetentionDays)
    {
        int n = 0; long bytes = 0;
        if (!Directory.Exists(root)) return (0, 0);
        foreach (var e in Directory.GetFileSystemEntries(root))
        {
            DateTime newest; long size;
            try
            {
                bool isDir = Directory.Exists(e);
                newest = isDir ? NewestWriteIn(e) : File.GetLastWriteTime(e);
                size = isDir ? TreeSize(e) : new FileInfo(e).Length;
            }
            catch { continue; }
            if (!IsStaleScratch(newest, now, keepDays)) continue;
            if (TryDeleteTree(e)) { n++; bytes += size; }
        }
        return (n, bytes);
    }

    static long TreeSize(string dir)
    {
        long t = 0;
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                t += new FileInfo(f).Length;
        }
        catch { /* 半路读不动就报已握到的数，只是那句日志里的 MB 小一点 */ }
        return t;
    }

    /// <summary>③ 的测试闸（形状照 DEC-111 那条教训 = **按进程收口**，`SelfTest.Run` 与 `UiPreviewForm.Run` 入口点名）：
    /// 自检与出图台跑的时候不许兜底扫 —— `data\tmp\selftest-*` 那些窝正是这两趟自己要用的东西。</summary>
    internal static bool ScratchSweepDisabledForTest;

    /// <summary>③ 的调用点：窗开起来之后到后台扫一趟（232 MB 那种量删起来要一两秒，放构造函数里会拖慢开窗）。
    /// 只写一行日志，不弹任何东西 —— 收垃圾不是要人决策的事。测试态直接收手（那道闸按进程生效）。</summary>
    void SweepScratchAtStartup()
    {
        if (ScratchSweepDisabledForTest) return;
        var (n, bytes) = SweepTempDir();
        if (n > 0) Log($"临时目录收掉 {n} 项残留（超过 {TmpRetentionDays} 天，回收 {bytes / 1048576.0:F1} MB）", "launcher");
    }

    /// <summary>④ 的调用点（DEC-121）：Strata 整份快照外迁放到**窗开起来之后的后台**，不摆构造函数。
    /// 道理与上面那条兜底扫一模一样 —— 实跑是 `D:\MultiLauncher\data\backup` → `D:\Strata-backup`，**跨卷 = 真复制**
    /// （他机器上三份 830 MB，几秒），摆在开窗那一步就是"先卡一下"。
    /// 同卷的那些机器（家底与快照同一只盘）走的是改名，毫秒级；**判据只有一条路径**（`MoveTreeInto` 里已经没有 `Directory.Move`），
    /// 所以同卷跨卷不是两种行为，自检沙盒里跑通的就是实跑跑通的那一条。
    /// 日志逐条走 `launcher` 通道（跨模块的家事，三页都该看见）；测试闸 = `LayoutMigrationDisabledForTest`（按进程收口）。</summary>
    void MigrateSnapshotsAtStartup()
    {
        foreach (var note in MigrateStrataSnapshotsReal(_config.Current.Root))
            Log("布局搬家：" + note, "launcher");
    }

}
