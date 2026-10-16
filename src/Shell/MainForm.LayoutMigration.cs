using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MultiLauncher;

// DEC-116 + DEC-118：老落点的一次性搬家（Move 不是复制；两处都有 = 都不动并点名）。
// 2026-10-15 从 MainForm.Models.cs 整段搬出，一行未改。
public partial class MainForm
{
    // ---------------- DEC-116 + DEC-118：老落点的一次性搬家 ----------------
    // 规矩照 DEC-084 那次改名：**Move 而不是复制（原地不留第二份）**；两边都有一份 = **不覆盖、点名**
    //（宁可留着两份让人去看，也不替他选一份）；新目录建不出来 = 什么都不动并把原因说清。
    // 调用时机要紧：必须在 `EnsureLlamaIniPresent` / `EnsureNinferIniPresent` 那两步"缺才拷"**之前** ——
    // 不然先在老位置补种一份，搬家这一步就变成"两边都有 = 不覆盖"，白留一份误导人。

    /// <summary>搬一个文件。老位置没有 = 无事；新位置已有同名 = 不覆盖并点名；否则 Move。</summary>
    static void MoveFileInto(string? srcFile, string dstDir, string label, List<string> notes)
    {
        if (srcFile is null || !File.Exists(srcFile)) return;
        try
        {
            Directory.CreateDirectory(dstDir);
            var dst = Path.Combine(dstDir, Path.GetFileName(srcFile));
            if (File.Exists(dst))
            {
                notes.Add(label + "：新位置已经有一份（" + dst + "），老那份留在原地没动 —— 界面按新位置走，请自己定去留");
                return;
            }
            File.Move(srcFile, dst);
            notes.Add(label + "：已搬到 " + dst);
        }
        catch (Exception ex) { notes.Add(label + "：没搬动（原样留在老位置）：" + ex.Message); }
    }

    /// <summary>整层搬家（DEC-121 重写）：文件逐个搬（同名不覆盖、点名），子目录**递归并进**目标。
    /// **这里绝对不能用 `Directory.Move`** —— 它跨卷当场抛 IOException（实跑在 D:、快照那一头在 F:），
    /// 上一版就是"顶层 about.txt 搬走了、engine\ 与 source\ 原地不动"的半截状态，他实跑抓到（830 MB 留在 `data\backup\`，
    /// 而 `D:\Strata-backup\` 那三格里只剩 about.txt = 「回退…」列出来也点不动）。
    /// 目标已有同名**子目录** = 往那一格里并（半截状态下次启动自己接着搬完），同名**文件**照旧不覆盖并点名。
    /// 每一层只报一行（`engine\` 里几百个文件各写一行会把日志淹掉），没搬完的那一层照样点名。</summary>
    static void MoveTreeInto(string? srcDir, string dstDir, string label, List<string> notes)
    {
        if (srcDir is null || !Directory.Exists(srcDir)) return;
        var (moved, skipped) = MergeTreeInto(srcDir, dstDir, label, notes);
        if (moved > 0 || skipped > 0)
            notes.Add(label + "：整层已并进 " + dstDir + "（" + moved + " 个文件" +
                (skipped > 0 ? "，另有 " + skipped + " 个同名在新位置已经有一份、老那份留在原地" : "") + "）");
        try { if (Directory.Exists(srcDir) && Directory.GetFileSystemEntries(srcDir).Length == 0) Directory.Delete(srcDir); }
        catch { /* 空壳收不掉留着无害 */ }
    }

    /// <summary>DEC-124：**只复制、不删老那份**的那一条路，专给"整份快照归位"这一跳用。
    /// 他这条的原话 = 旧的位置不要删除、留着他自己删、并要提醒他 —— 备份本来就是"改崩了退得回去"那一头，
    /// 由程序替他把老的那一份抹掉正好是最不该自动化的一件事（跨卷、几百个文件、有的只存在一份，删错就没了）。
    /// 同名 = 不顶掉；子目录递归；每一层只报一行，而且**只在真的复制了东西的那一趟才报**（第二趟一个字不写 = 幂等判据照样过）。
    /// 与 <see cref="MoveTreeInto"/> 的分工要说清：**配置本体与那几层老目录 = Move（原地不留第二份是它们的定义）；整份快照 = Copy（原地那份就是退路）**。</summary>
    static void CopyTreeInto(string? srcDir, string dstDir, string label, List<string> notes)
    {
        if (srcDir is null || !Directory.Exists(srcDir)) return;
        var (copied, skipped) = CopyEntriesInto(srcDir, dstDir, label, notes);
        if (copied > 0)
            notes.Add(label + "：已复制 " + copied + " 个文件到 " + dstDir +
                (skipped > 0 ? "（另有 " + skipped + " 个同名，新位置那份没被顶掉）" : ""));
    }

    static (int copied, int skipped) CopyEntriesInto(string srcDir, string dstDir, string label, List<string> notes)
    {
        int copied = 0, skipped = 0;
        try
        {
            Directory.CreateDirectory(dstDir);
            foreach (var f in Directory.GetFiles(srcDir))
            {
                var dstFile = Path.Combine(dstDir, Path.GetFileName(f));
                if (File.Exists(dstFile)) { skipped++; continue; }       // 内容本来就该逐字节相同，顶掉它没有任何收益
                File.Copy(f, dstFile); copied++;
            }
            foreach (var d in Directory.GetDirectories(srcDir))
            {
                var (c2, s2) = CopyEntriesInto(d, Path.Combine(dstDir, Path.GetFileName(d)), label, notes);
                copied += c2; skipped += s2;
            }
        }
        catch (Exception ex) { notes.Add(label + "：没复制完（新位置可能只有一半，老那份照旧完整）：" + ex.Message); }
        return (copied, skipped);
    }

    /// <summary>把 srcDir 的内容并进 dstDir（递归；只有这一条路径，同卷跨卷都一样走 —— 判据只有一条路才谈得上全覆盖）。
    /// 返回（搬走的文件数，因同名留下的文件数）。</summary>
    static (int moved, int skipped) MergeTreeInto(string srcDir, string dstDir, string label, List<string> notes)
    {
        int moved = 0, skipped = 0;
        try
        {
            Directory.CreateDirectory(dstDir);
            foreach (var f in Directory.GetFiles(srcDir))
            {
                var dstFile = Path.Combine(dstDir, Path.GetFileName(f));
                if (File.Exists(dstFile))
                {
                    skipped++;
                    notes.Add(label + "：" + Path.GetFileName(f) + " 在新位置已经有一份，老那份留在原地没动 —— 界面按新位置走，请自己定去留");
                    continue;
                }
                File.Move(f, dstFile); moved++;          // File.Move 同卷是改名、跨卷自己复制+删（与 Directory.Move 不同，它不挑卷）
            }
            foreach (var d in Directory.GetDirectories(srcDir))
            {
                var leaf = Path.GetFileName(d);
                var (m, s) = MergeTreeInto(d, Path.Combine(dstDir, leaf), label, notes);   // 目标同名目录已有 = 并进去，不是"整格留在原地"
                moved += m; skipped += s;
                try { if (Directory.GetFileSystemEntries(d).Length == 0) Directory.Delete(d); }
                catch { /* 收不掉就留着，上面报了数 */ }
            }
        }
        catch (Exception ex) { notes.Add(label + "：没搬完（剩下的都在老位置）：" + ex.Message); }
        return (moved, skipped);
    }

    /// <summary>"版本-时间戳"那种快照目录的样子（DEC-116 之后 `data\backup\` 下的子目录**是模块名**，
    /// 拿这个名字判据区分"还留在老位置的快照"与"新形状的配置备份格"，不然第二次启动会把配置备份格当快照搬走）。</summary>
    internal static bool LooksLikeSnapshot(string dirName) =>
        System.Text.RegularExpressions.Regex.IsMatch(dirName ?? "", @".+-\d{8}-\d{6}$");

    /// <summary>③ 的测试闸（形状照 DEC-111 那道 = 按进程收口，`SelfTest.Run` 与 `UiPreviewForm.Run` 入口点名）：
    /// 自检与出图台跑的时候不许跑布局搬家 —— 那一步会动**真实**的家底位置（把 exe 同目录那两份搬进 data\config\），
    /// 与 DEC-094 ⓪"在用户机器上跑一次 --selftest 不许动他任何东西"是同一条边界。</summary>
    internal static bool LayoutMigrationDisabledForTest;

    /// <summary>首启一次性搬家（真路径那一层皮；判据全在有参数的 <see cref="MigrateLauncherLayout(string,string,string)"/> 里，
    /// 自检喂沙盒样本用的就是那一个）。
    /// DEC-121 拆成两步走：**构造函数里只做 ①②③**（同卷改名 = 毫秒级），
    /// ④ 那一步（Strata 整份快照外迁）实跑是**跨卷 = 真复制 ~830 MB**，摆在这里就是"开窗先卡一下"——
    /// 同 DEC-114 那条后台扫的道理，交给 <see cref="MigrateStrataSnapshotsReal()"/> 在窗开起来之后跑。</summary>
    internal static List<string> MigrateLauncherLayout(string strataRoot) =>
        LayoutMigrationDisabledForTest
            ? new List<string>()
            : MigrateLauncherLayout(AppContext.BaseDirectory, LauncherDataDir(), strataRoot, snapshots: false);

    /// <summary>④ 的真路径（后台那一步）：跨卷复制量大，只在窗开起来之后跑；闸与上面同一道（自检/出图台一个字都不碰他家底）。</summary>
    internal static List<string> MigrateStrataSnapshotsReal(string strataRoot) =>
        LayoutMigrationDisabledForTest
            ? new List<string>()
            : MigrateStrataSnapshots(LauncherDataDir(), strataRoot);

    /// <summary>DEC-138（E-4）：自检专用的可喂样本版 —— 判据与上面那条**同一个家**（同一道闸、同一个 MigrateStrataSnapshots），
    /// 只是把 dataDir 也交出来。以前自检那条断言（`MigrateStrataSnapshotsReal(halfStrata).Count == 0`）喂的是一个
    /// **从没建过的** legacy 目录 = 没货可搬，所以闸上闸下都是 0 条，把那道三元判断整条删掉它照样绿（空转）。
    /// 两面现在这么断：闸上 = 0 条且新落点一个字节不长；把闸按回去 = 真有货并复制过去。</summary>
    internal static List<string> MigrateStrataSnapshotsRealForTest(string dataDir, string strataRoot) =>
        LayoutMigrationDisabledForTest
            ? new List<string>()
            : MigrateStrataSnapshots(dataDir, strataRoot);

    /// <summary>老落点的一次性搬家总入口（返回给用户看的那几行话；空 = 什么都没搬，日志一行都不写）。
    /// 规矩照 DEC-084 那次改名：**Move 而不是复制（原地不留第二份）**；两边都有一份 = **不覆盖、点名**
    ///（宁可留着两份让人去看，也不替他选一份）；新目录建不出来 = 什么都不动并把原因说清。
    /// 参数是沙盒化的原因：自检要能喂样本验这三面（搬走 / 不覆盖 / 空壳收掉），钉死真路径就只剩"看运气"。
    /// `snapshots` 这一档只影响真路径的**调用时机**（构造函数 vs 后台），判据本身两边同一个家 —— 自检默认走全量（= true）。</summary>
    internal static List<string> MigrateLauncherLayout(string exeDir, string dataDir, string strataRoot, bool snapshots = true)
    {
        var notes = new List<string>();
        var data = dataDir;
        // 落点全从传进来的 data 派生（真路径 = LauncherDataDir()，沙盒 = 自检那个窝），
        // 不然这两个函数就只是"改真件"的副作用，判据没法被喂样本验到（DEC-118 第 5 条那条坑的根子就在这）。
        string Cfg(string module) => Path.Combine(data, "config", module);
        string Bk(string module) => Path.Combine(data, "backup", module);
        // ① 两份家底：exe 同目录 → data\config\<模块>\（旧名那份也带走，留着只会误导）
        MoveFileInto(Path.Combine(exeDir, LlamaIniFileName), Cfg("llama"), "llama 家底", notes);
        MoveFileInto(Path.Combine(exeDir, LegacyLlamaIniFileName), Cfg("llama"), "llama 旧名家底", notes);
        MoveFileInto(Path.Combine(exeDir, NinferIniFileName), Cfg("ninfer"), "Ninfer 家底", notes);
        // ② Strata 的生效配置那一层（<tag>.json 与 <tag>.shared-settings.json **整层一起走**才保得住那条派生关系）
        MoveTreeInto(Path.Combine(data, "run"), Cfg("strata"), "Strata 生效配置", notes);
        // ③ 配置备份：data\llama → data\backup\llama；data\infer 按前缀分成两格（配置 / 引擎 exe）
        MoveTreeInto(Path.Combine(data, "llama"), Bk("llama"), "llama 配置备份", notes);
        var inferOld = Path.Combine(data, "infer");
        if (Directory.Exists(inferOld))
        {
            foreach (var f in Directory.GetFiles(inferOld))
            {
                bool isExe = Path.GetFileName(f).StartsWith(NinferExeBackupPrefix, StringComparison.OrdinalIgnoreCase);
                MoveFileInto(f, Bk(isExe ? "ninfer-exe" : "ninfer"),
                    (isExe ? "Ninfer 引擎 exe 备份" : "Ninfer 配置备份") + " 里的 " + Path.GetFileName(f), notes);
            }
            try { if (Directory.GetFileSystemEntries(inferOld).Length == 0) Directory.Delete(inferOld); } catch { /* 留着无害 */ }
        }
        // ④ Strata 整份快照归位到 D:\Strata-data\backup\（DEC-124；判据只有 MigrateStrataSnapshots 那一个家，
        // 真路径下这一档由窗开起来之后的后台那一步单独调，沙盒用例仍走全量）
        if (snapshots) notes.AddRange(MigrateStrataSnapshots(data, strataRoot));
        return notes;
    }

    /// <summary>④ 那一层单独一个入口。DEC-124 定两件事：
    /// **一、落点 = `D:\Strata-data\backup\<版本-时间戳>\`**（<see cref="StrataBackupRoot"/>）；
    /// **二、老位置只读不删** —— 从「壳的 `data\backup\`」（DEC-116 之前）与「同级 `D:\Strata-backup\`」（DEC-116~123）两处**复制**过去，
    /// 源一个字都不动，因为他这条的原话是"旧的位置不要删除，留着我手动删除并提醒我"。
    /// 备份 = "改崩了退得回去"那一头，程序顺手把老一份抹掉正是最不该自动化的动作（同一批已经因为自动轮转丢过他一份原件 = BUG-051）。
    /// 只认 `LooksLikeSnapshot` 那种目录名，配置备份格（`llama` / `ninfer` / `ninfer-exe` / `strata`）一律不动。</summary>
    internal static List<string> MigrateStrataSnapshots(string dataDir, string strataRoot)
    {
        var notes = new List<string>();
        var dstSnap = StrataBackupRoot(strataRoot);
        foreach (var layer in LegacySnapshotLayers(dataDir, strataRoot))
        {
            if (!Directory.Exists(layer)) continue;
            foreach (var d in Directory.GetDirectories(layer))
            {
                var leaf = Path.GetFileName(d);
                if (!LooksLikeSnapshot(leaf)) continue;
                CopyTreeInto(d, Path.Combine(dstSnap, leaf), "Strata 升级快照 " + leaf, notes);
            }
        }
        return notes;
    }

    /// <summary>快照的两个老位置（也是 <see cref="LegacySnapshotsPending"/> 的判据来源，两处共用这一个家）。</summary>
    static IEnumerable<string> LegacySnapshotLayers(string dataDir, string strataRoot)
    {
        yield return Path.Combine(dataDir, "backup");
        var legacy = StrataBackupRootLegacy(strataRoot);
        if (legacy.Length > 0) yield return legacy;
    }

    /// <summary>老位置里还有没有快照（每启动提醒一行，直到他自己删掉为止；**空壳不算**，免得长期骚扰）。
    /// 只认 `LooksLikeSnapshot` 的目录名 —— 老 `data\backup\` 里那些配置备份格不是"没搬完"，别拿它们提醒人。</summary>
    internal static string? LegacySnapshotsPending(string dataDir, string strataRoot)
    {
        foreach (var layer in LegacySnapshotLayers(dataDir, strataRoot))
        {
            try
            {
                if (!Directory.Exists(layer)) continue;
                if (Directory.GetDirectories(layer).Any(d => LooksLikeSnapshot(Path.GetFileName(d)))) return layer;
            }
            catch { /* 读不到就当没这一处 */ }
        }
        return null;
    }
}
