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

    volatile bool _ninferWslScanned;                 // find 回来过没有（没回来之前不判"件不存在"）
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

    /// <summary>异步扫 WSL 那一侧（`wsl.exe -d &lt;distro&gt; find`）。只在模块启用 + 那一侧启用时跑；
    /// 扫不动就把 _ninferWslScanned 留着 false 并记一行，别把整页件标成"不存在"。</summary>
    void RefreshNinferWslArtifacts()
    {
        var s = _config.Current;
        if (!ModuleRegistry.NinferEnabled(s) || !s.NinferWslOn) { _ninferWslScanned = false; _ninferWslFiles = new(); return; }
        var distro = s.NinferDistro.Trim();
        var dir = s.NinferWslModelDir.Trim();
        if (distro.Length == 0 || dir.Length == 0) { _ninferWslScanned = false; return; }
        _ = Task.Run(() =>
        {
            // 2026-10-12：`-printf '%s %p\n'` 顺带把字节数带回来（占用估算那格要用；GNU find 才认，Ubuntu-24.04 实测可用）。
            var (code, output) = RunWslCapture($"-d {WslQuote(distro)} find {WslQuote(dir)} -type f -name \"*.ninfer\" -printf '%s %p\\n' 2>/dev/null", 60_000);   // DEC-136（C-11）：目录也要引号 + 给上限
            if (code != 0)
            {
                LogN($"WSL 侧扫描没做成（wsl.exe 退出码 {code}）：先按段列出来，起服务前会再核一次件在不在");
                return;
            }
            var files = NinferWslScanRows(output)
                        .Where(f => f.File.EndsWith(".ninfer", StringComparison.OrdinalIgnoreCase)).ToList();
            if (IsDisposed) return;
            try { BeginInvoke(new Action(() =>
            {
                _ninferWslFiles = files;
                _ninferWslScanned = true;
                LogN($"已扫描 WSL 侧 {files.Count} 个 .ninfer 件（{distro}:{dir}；带得到字节数的 {files.Count(f => f.Bytes > 0)} 个）");
                RefreshModels(NinferKey);       // 件在不在现在判得准了，重列一遍（只重扫 Ninfer，DEC-097⑤）
            })); } catch { /* 窗口已销毁 */ }
        });
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

}
