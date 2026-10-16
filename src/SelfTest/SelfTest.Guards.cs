using System.Text;

namespace MultiLauncher;

// ⓪ 护栏（DEC-094 那一族 + DEC-138 缺口 9/11）："跑一次 --selftest 不许动用户的任何一份真家底"。
// 原为 RunBody 里的局部函数，2026-10-15 随共享状态提升整块搬来（一行未改）：
// 跑前快照由 preamble 填（SelfTest.State.cs 的同名字段），这里只读它们 + 现场复核。
internal static partial class SelfTest
{
        internal static void CheckRunDirUntouched()
        {
            var after = SnapshotRunDir(MainForm.RealRunConfigDir());
            var delta = RunDirDelta(realRunBefore, after);
            // DEC-138（E-11 那一族的自我复查）：两个空名单相比也是"一字不差" —— 所以先钉前提：整份快照那一格在本机得有货。
            Check("DEC-138 缺口9 用例前提：整份快照那一格在本机**有货**（没货时上面那条会退化成两个空名单相比 = 半条判据）",
                strataSnapBefore.Length > 0 && strataSnapBefore != "(不在)" && strataSnapBefore != "(读不动)",
                $"[{strataSnapBefore}]");
            Check("⓪ DEC-138 缺口9：自检没动 Strata 的两格退路 —— 配置备份格与整份快照格的**目录名单**跑前跑后一字不差",
                strataCfgBakBefore == SnapshotDirNames(MainForm.ModuleBackupDir("strata"))
                && strataSnapBefore == SnapshotDirNames(MainForm.StrataBackupRoot(root)),
                $"配置备份 [{strataCfgBakBefore}] -> [{SnapshotDirNames(MainForm.ModuleBackupDir("strata"))}]；" +
                $"整份快照 [{strataSnapBefore}] -> [{SnapshotDirNames(MainForm.StrataBackupRoot(root))}]");
            Check("⓪ 跑一次 --selftest 没动真实 data\\config\\strata\\ 的任何一份（物化全改道 " + runTmp + "）",
                delta.Length == 0, delta + "｜跑前 " + realRunBefore.Count + " 份、跑后 " + after.Count + " 份");
            // llama 家底：内容逐字节没动、文件名没被换、备份目录里也没多出自检造的"历史"
            var now = SnapshotLlamaIni();
            string iniDelta = "";
            if (llamaIniBefore.bytes.Length > 0 && (now.bytes.Length == 0 || !now.bytes.AsSpan().SequenceEqual(llamaIniBefore.bytes)))
                iniDelta = "家底内容变了";
            if (llamaIniBefore.path is not null && now.path is not null
                && !string.Equals(llamaIniBefore.path, now.path, StringComparison.OrdinalIgnoreCase))
                iniDelta += "｜文件名被换掉（" + Path.GetFileName(llamaIniBefore.path) + " → " + Path.GetFileName(now.path) + "）";
            var bkDelta = RunDirDelta(llamaBkBefore, SnapshotRunDir(MainForm.RealRouterIniBackupDir()));
            if (bkDelta.Length > 0) iniDelta += "｜备份目录被动过：" + bkDelta;
            Check("⓪ 跑一次 --selftest 没动真实 llama 家底、也没在 data\\backup\\llama\\ 留下假备份"
                    + (llamaIniBefore.path is null ? "（本机还没有家底，这条按空跑过）"
                        : "（" + Path.GetFileName(llamaIniBefore.path) + "）"),
                iniDelta.Length == 0, iniDelta);
            // DEC-096：Ninfer 那一份同理（内容逐字节没动 + 没在 data\\infer\\ 留下假备份/假 exe）
            var ninferNow = SnapshotNinferIni();
            string ninferDelta = "";
            if (ninferIniBefore.bytes.Length > 0 && (ninferNow.bytes.Length == 0
                || !ninferNow.bytes.AsSpan().SequenceEqual(ninferIniBefore.bytes)))
                ninferDelta = "家底内容变了";
            if (ninferIniBefore.path is null && ninferNow.path is not null)
                ninferDelta += "｜自检在本机凭空造出了一份家底（" + Path.GetFileName(ninferNow.path) + "）";
            var ninferBkDelta = RunDirDelta(ninferBkBefore, SnapshotRunDir(MainForm.RealNinferIniBackupDir()));
            if (ninferBkDelta.Length > 0) ninferDelta += "｜备份目录被动过：" + ninferBkDelta;
            var ninferExeBkDelta = RunDirDelta(ninferExeBkBefore, SnapshotRunDir(MainForm.RealNinferExeBackupDir()));
            if (ninferExeBkDelta.Length > 0) ninferDelta += "｜exe 备份格被动过：" + ninferExeBkDelta;
            Check("⓪ 跑一次 --selftest 没动真实 Ninfer 家底、也没在 data\\backup\\ninfer 与 ninfer-exe 两格留下假备份"
                    + (ninferIniBefore.path is null ? "（本机还没有家底，这条按空跑过）"
                        : "（" + Path.GetFileName(ninferIniBefore.path) + "）"),
                ninferDelta.Length == 0, ninferDelta);
            // DEC-103（BUG-049）：真实配置逐字节没动（口径同上三份家底；这项以前缺位 = 写漏没人抓）
            string cfgDelta = "";
            if (cfgBytesBefore is not null)
            {
                byte[] nowBytes = Array.Empty<byte>();
                try { if (File.Exists(cfgPathBefore)) nowBytes = File.ReadAllBytes(cfgPathBefore); } catch { }
                if (!BytesEqual(cfgBytesBefore, nowBytes)) cfgDelta = "配置内容变了（" + cfgPathBefore + "）";
            }
            Check("⓪ 跑一次 --selftest 没动真实 MultiLauncher.ini（BUG-049 的护栏；切后端那条用例必须走副本）",
                cfgDelta.Length == 0, cfgDelta);
        }
}
