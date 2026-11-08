using System.Diagnostics;
using System.Drawing;
using System.Text;

namespace MultiLauncher;

// Ninfer 的扫描：两个后端各扫各的（DEC-087 第④条：关掉那一侧 = 连扫都不扫）。
// 2026-10-15 从 MainForm.Ninfer.cs 整段搬出，一行未改。
public partial class MainForm
{
    // ---------------- 扫描：两个后端各扫各的（DEC-087 第④条：关掉那一侧 = 连扫都不扫） ----------------

    /// <summary>Ninfer 侧的模型列表（下拉用它）。判据是**家底里的段**，不是扫到的件：
    /// 壳不新建段、也不猜（同 llama 的"段不存在就报错不猜"），所以「扫到但没有段」的件不进下拉，
    /// 只在日志里点一句（他要是想加新件，仍然只能自己往 ini 里加一段）。</summary>
    List<ModelEntry> ScanNinferModels()
    {
        var s = _config.Current;
        var list = new List<ModelEntry>();
        var all = ReadNinferSections();
        // DEC-136（C-3）：段名 = 对外 model-id，**必须两两不同名** —— 判据 `DuplicateModelIds` 早就写好了，
        // 可全代码只有自检在喂样本（"判据写了没接线"这一族第四次：BUG-044 的 `StartGateway("ninfer")`、
        // `NinferModelAction` 无人挂、`NinferExeBackupDir` 只写不读、这次）。重名时壳与引擎都只会命中第一段
        // （下面三处 `FirstOrDefault(Name == tag)`），界面出现两枚逐字相同的项、起的是上面那一件，一行日志都没有。
        var dup = NinferIni.DuplicateModelIds(all);
        if (dup is not null)
            LogN($"段名重复：[{dup}] 在这份文件里出现了不止一次 —— model-id 必须两两不同名，" +
                 "引擎与壳都只会命中第一段，另一段的家底一个字用不上。请把其中一段改个名字。");
        foreach (var sec in all)
        {
            var backend = SecBackend(sec);
            if (backend is null) { LogN($"段 [{sec.Name}] 的 backend 不是 win / wsl，这一条不列（壳不猜归属）"); continue; }
            if (!NinferBackendOn(s, backend)) continue;      // 那一侧被关掉 = 全隐身
            if (!NinferIni.UsableSectionName(sec.Name)) { LogN($"段名 [{sec.Name}] 不能当 model-id 用（带空格或方括号），这一条不列"); continue; }
            bool ready = true; string note = ""; string size = "";
            if (backend == NinferIni.BackendWin)
            {
                var path = NinferWinArtifactPath(s, sec.File);
                if (path.Length == 0) { ready = false; note = "段里没写 file，而且引擎目录里也没有同名件"; }
                else if (!File.Exists(path)) { ready = false; note = "件不存在：" + path; }
                sec.File = path;         // 起服务用绝对路径（段里允许只写叶子名 = 相对引擎目录）
            }
            else
            {
                // WSL 那侧的件在 VM 里，本地 File.Exists 判不了；find 的结果到之前先按"段就是事实"列出来
                //（拿未扫到当"不存在"标灰，会把还没扫完的整页都标成红色）。
                // DEC-136（C-5）：原来这段注释承诺"起服务前那条核验会拦下来"是**假话** —— `NinferPreflight` 那条
                // 件不存在的判据只在 WIN 分支跑，而 find 那趟以前也没有任何启动调用点（只在设置页改过 distro/目录时
                // 跑一次）。现在两头都补上：启动后台扫一次（Program.cs 的 Shown 那一串），且真扫过之后 WSL 分支也拦。
                if (_ninferWslScanned && !_ninferWslFiles.Any(f => string.Equals(f.File, sec.File, StringComparison.Ordinal)))
                { ready = false; note = "VM 里没找到这个件：" + sec.File; }
            }
            // 占用估算那一格（2026-10-12 起）：与起前那道显存账**共用** NinferNeedGiBFor —— 件字节 + 池 × 档表，
            // 不加余量。量不到（件不在 / WSL 那侧还没扫到）就空着，与升级前同形。
            if (NinferNeedGiBFor(sec.Name) is { } need) size = "≈" + need.ToString("0.0") + "G";
            // DEC-136（C-3）：重名那两段一律标灰 —— 引擎与壳都只会命中第一段 = 起错件，宁可在界面上把话说明白
            if (dup is not null && string.Equals(sec.Name, dup, StringComparison.OrdinalIgnoreCase))
            { ready = false; note = "段名与另一段重复（model-id 必须两两不同名）：这一段的家底壳用不上"; }
            list.Add(new ModelEntry(sec.Name, sec.File, sec.Name, ready, note)
            {
                Backend = ModelEntry.BackendNinfer,
                NinferBackend = backend,
                SizeText = size,
            });
        }
        return list;
    }

    volatile bool _ninferWslScanned;                 // 扫描回来过没有（没回来之前不判"件不存在"）
    // BUG-086：UNC 那条不通、而这一趟又是静默启动（--tray）压下来的 —— 把 wsl.exe 那趟记在这儿，
    // 等窗口真到眼前（ShowMainWindow）再补。wsl.exe 是控制台程序，WSL2 冷启动会闪一个黑框。
    volatile bool _ninferWslFallbackDeferred;
    /// <summary>证人位：测试态被闸挡下的次数（`\\wsl$` 与 wsl.exe 都会拉 VM，两样都算副作用）。</summary>
    internal static int NinferWslScanSkippedForTest;
    // VM 里的 .ninfer：路径 + 字节数（2026-10-12 起那趟 find 带 `-printf '%s %p\n'`，占用估算要用它）。
    // 字节数拿不到时那位是 0 = 那一件的尺寸那一格空着，不影响"在不在"的判据。
    List<(string File, long Bytes)> _ninferWslFiles = new();
    List<ModelEntry> _ninferModels = new();          // 下拉那一路用的缓存（模块表从这儿取）

    /// <summary>WIN 侧件路径：段里写绝对路径就用它；只写叶子名（或写的目录换了）就到引擎目录里找同名件。
    /// 两个方向都不猜路径之外的东西。</summary>
    internal static string NinferWinArtifactPath(Settings s, string fileIn)
    {
        var f = (fileIn ?? "").Trim();
        var dir = s.NinferDir.Trim();
        if (f.Length > 0 && Path.IsPathRooted(f) && File.Exists(f)) return f;
        var leaf = f.Length > 0 ? Path.GetFileName(f) : "";
        if (leaf.Length > 0 && dir.Length > 0 && File.Exists(Path.Combine(dir, leaf))) return Path.Combine(dir, leaf);
        return f.Length > 0 ? f : "";              // 原样交回去，Ready 那一步会报"件不存在"
    }

    /// <summary>异步扫 WSL 那一侧（主路 = 直接读 `\\wsl$\<distro>\…`；回退 = `wsl.exe -d <distro> find`）。
    /// 只在模块启用 + 那一侧启用时跑；扫不动就把 _ninferWslScanned 留着 false 并记一行，别把整页件标成"不存在"。</summary>
    /// <param name="allowWslExe">false = 这一趟**不许起 wsl.exe**（开机静默启动走这条）。UNC 那条不通时只记一个
    /// "待回退"，等窗口真到眼前（<see cref="ShowMainWindow"/>）再补那一趟 —— wsl.exe 是控制台程序，
    /// WSL2 冷启动会闪一个黑框，摆在开机那一瞬最刺眼（BUG-086）。</param>
    void RefreshNinferWslArtifacts(bool allowWslExe = true)
    {
        // DEC-097⑥ 那道按进程的闸收在**本体这一个入口**（口径同 DEC-160③）：测试态一次都不进 VM ——
        // `\\wsl$\` 与 wsl.exe 都会把虚拟机拉起来，两样都算副作用。
        if (VersionProbeDisabledForTest) { NinferWslScanSkippedForTest++; return; }
        var s = _config.Current;
        if (!ModuleRegistry.NinferEnabled(s) || !s.NinferWslOn)
        {
            _ninferWslScanned = false; _ninferWslFiles = new(); _ninferWslFallbackDeferred = false; return;
        }
        var distro = s.NinferDistro.Trim();
        var dir = s.NinferWslModelDir.Trim();
        if (distro.Length == 0 || dir.Length == 0) { _ninferWslScanned = false; _ninferWslFallbackDeferred = false; return; }
        _ = Task.Run(() =>
        {
            // 主路（BUG-086）：`\\wsl$\` 走 9p 文件服务直接枚举 —— 不起任何进程，启动那一下不会闪控制台窗。
            // 交回来的仍是「VM 路径 + 字节数」，后面的判据一个字不用改（与 find -printf '%s %p' 那趟同形）。
            var files = TryScanNinferWslByUnc(distro, dir);
            if (files is null)
            {
                if (!allowWslExe)
                {
                    _ninferWslFallbackDeferred = true;
                    LogN(@"WSL 侧扫描：\\wsl$ 那条这次不通；静默启动期间不跑 wsl.exe（它会闪一个控制台窗），等打开主窗口再补一次");
                    return;
                }
                // 2026-10-12：`-printf '%s %p\n'` 顺带把字节数带回来（占用估算那格要用；GNU find 才认，Ubuntu-24.04 实测可用）。
                var (code, output) = RunWslCapture($"-d {WslQuote(distro)} find {WslQuote(dir)} -type f -name \"*.ninfer\" -printf '%s %p\\n' 2>/dev/null", 60_000);   // DEC-136（C-11）：目录也要引号 + 给上限
                if (code != 0)
                {
                    LogN($"WSL 侧扫描没做成（wsl.exe 退出码 {code}）：先按段列出来，起服务前会再核一次件在不在");
                    return;
                }
                files = NinferWslScanRows(output)
                            .Where(f => f.File.EndsWith(".ninfer", StringComparison.OrdinalIgnoreCase)).ToList();
            }
            ApplyNinferWslScan(files, distro, dir);
        });
    }

    /// <summary>扫描结果落地 —— 两条路（UNC / wsl.exe）共用这一个收口：日志那一句与"重列 Ninfer 页"都只在这儿写一次。</summary>
    void ApplyNinferWslScan(List<(string File, long Bytes)> files, string distro, string dir)
    {
        if (IsDisposed) return;
        try { BeginInvoke(new Action(() =>
        {
            _ninferWslFiles = files;
            _ninferWslScanned = true;
            _ninferWslFallbackDeferred = false;
            LogN($"已扫描 WSL 侧 {files.Count} 个 .ninfer 件（{distro}:{dir}；带得到字节数的 {files.Count(f => f.Bytes > 0)} 个）");
            RefreshModels(NinferKey);       // 件在不在现在判得准了，重列一遍（只重扫 Ninfer，DEC-097⑤）
        })); } catch { /* 窗口已销毁 */ }
    }

    /// <summary>解析那趟 find 的输出（`-printf '%s %p\n'` 的「字节数 路径」）。认不出字节数的行按"只有路径"收下
    ///（字节 0）—— 换一个不带 `-printf` 的发行版时，"在不在"照旧判得准，只是尺寸那一格空着。</summary>
    internal static List<(string File, long Bytes)> NinferWslScanRows(string output)
    {
        var rows = new List<(string File, long Bytes)>();
        foreach (var raw in (output ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var sp = raw.IndexOf(' ');
            if (sp > 0 && long.TryParse(raw[..sp], out var n) && raw[(sp + 1)..].Trim() is { Length: > 0 } path)
            {
                rows.Add((path, n));
                continue;
            }
            rows.Add((raw, 0));
        }
        return rows;
    }

    // ---------------- 不走 wsl.exe 的那条路（BUG-086：`\\wsl$\` 直读，开机那一瞬不闪控制台窗） ----------------

    /// <summary>VM 里的目录 → Windows 侧 `\\wsl$\&lt;distro&gt;\…`。空串 = 形状不像 Linux 绝对路径
    /// （不猜：交回退那条，宁可多跑一趟 wsl.exe 也不认错路径）。</summary>
    internal static string NinferWslUncDir(string distro, string vmDir)
    {
        var d = (distro ?? "").Trim();
        var raw = (vmDir ?? "").Trim().Replace('\\', '/');
        if (d.Length == 0 || raw.Length == 0 || raw[0] != '/') return "";   // 只认 Linux 绝对路径
        return @"\\wsl$\" + d + raw.TrimEnd('/').Replace('/', '\\');
    }

    /// <summary>UNC 下枚举到的文件 → VM 里的写法（段里 `file` 就是这个形状，判据按它逐字比）。
    /// <paramref name="relative"/> = 相对那格目录的 Windows 路径（含子目录）。</summary>
    internal static string NinferWslVmPath(string vmDir, string relative)
    {
        var root = (vmDir ?? "").Trim().Replace('\\', '/').TrimEnd('/');
        var rel = (relative ?? "").Trim().Replace('\\', '/').TrimStart('/');
        if (rel.Length == 0) return root.Length == 0 ? "/" : root;
        // 根目录那一格（`/`）裁掉尾斜杠后是空串 —— 那种要补回一个，别拼成相对路径
        return root.Length == 0 ? "/" + rel : root + "/" + rel;
    }

    /// <summary>WSL 侧清单：直接读 `\\wsl$\&lt;distro&gt;\…`（9p 文件服务）递归找 `*.ninfer`，顺手带字节数。
    /// 返回 null = 这条路这次走不通（发行版没起过 / 名字不对 / `\\wsl$` 不可用 / 读不动），调用点自行回退 wsl.exe。
    /// **为什么值得这一趟**：wsl.exe 是控制台程序，WSL2 冷启动那一下会闪一个黑框；开机静默启动时最刺眼（BUG-086）。</summary>
    internal static List<(string File, long Bytes)>? TryScanNinferWslByUnc(string distro, string vmDir)
    {
        var unc = NinferWslUncDir(distro, vmDir);
        if (unc.Length == 0) return null;
        try
        {
            if (!Directory.Exists(unc)) return null;          // 不可达就当没这条路（别让它去等 SMB 超时）
            var rootLen = unc.TrimEnd('\\').Length + 1;
            var rows = new List<(string File, long Bytes)>();
            foreach (var full in Directory.EnumerateFiles(unc, "*.ninfer", SearchOption.AllDirectories))
            {
                long bytes = 0;
                try { bytes = new FileInfo(full).Length; } catch { /* 量不到就 0 = 尺寸那格空着 */ }
                var rel = full.Length >= rootLen ? full[rootLen..] : Path.GetFileName(full);
                rows.Add((NinferWslVmPath(vmDir, rel), bytes));
            }
            rows.Sort((a, b) => string.CompareOrdinal(a.File, b.File));   // 次序只影响日志美观，排一下更好对账
            return rows;
        }
        catch { return null; }                                // 读不动 = 走回退那一趟（口径同"判不了就不判"）
    }

    /// <summary>自检接缝：走一次真路径 <see cref="RefreshNinferWslArtifacts"/>（闸门与证人位都在本体那一个入口上）。</summary>
    internal void RefreshNinferWslArtifactsForTest() => RefreshNinferWslArtifacts();

}
