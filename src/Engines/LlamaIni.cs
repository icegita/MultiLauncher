using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace MultiLauncher;

// llama.cpp 侧 llama_models.ini 的读写（M1 起后端也已接上，见 MainForm.Llama.cs）：
//   1) 解析模型段与全局键 —— 读序 = 模型段 → 文件头/[*] → 引擎默认（DEC-079，交接文档 §2 第 2 条）；
//   2) **就地**改「选中模型那一段」的目标键（DEC-079 取代 DEC-008 的"一个字节不改"）：
//      只动目标段的目标键，**没动的行逐字节原样**（换行风格、BOM、行尾空格、`#`/`;` 注释、段顺序一概照旧）；
//   3) 从 llama.cpp 目录名取 build 号，给左栏「llama」行用。
// 边界：`[*]`、文件头全局键、别的段、界面没有格子的键 = 永不触碰（TryEditSection 直接拒绝 `[*]` 与表外键）；
//   写之前的备份与轮转在 MainForm.Llama.cs（TryBackupRouterIni），这里只管把字节写对。
// ini 形状（本机实测 config\llama_models.ini，215 行 / 4,795 字节 / LF 换行 / 无 BOM）：
//   文件头有全局键（version = 1），[*] 是公共段（flash-attn / jinja / reasoning-effort …），
//   [模型名] 段里 model = <gguf>、mmproj、ctx-size、cache-type-k/v、load-mode … —— 后半截壳不认识，原样留。
internal sealed class LlamaModelEntry
{
    public string Name = "";        // section 名 = 模型名
    public string GgufPath = "";    // model =
    public string MmprojPath = "";  // mmproj =
    public string CtxSize = "";     // ctx-size
    public string CacheTypeK = "f16";
    public string CacheTypeV = "f16";
}

internal static partial class LlamaIni
{
    /// 读某个 [段] 的全部键值（键不区分大小写；同键重复取最后一次；读不到返回空表）。
    /// **只看该段自己写的键**，不含 `[*]` 回落 —— 回落是 ReadEffective 的事（读序：段 → 全局 → 引擎默认）。
    public static Dictionary<string, string> ReadSectionValues(string iniPath, string section)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(iniPath) || !File.Exists(iniPath) || section.Trim().Length == 0) return map;
        bool inSection = false;
        try
        {
            foreach (var raw in File.ReadAllLines(iniPath))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';')) continue;
                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    inSection = string.Equals(line[1..^1].Trim(), section.Trim(), StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                if (!inSection) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                map[line[..eq].Trim()] = line[(eq + 1)..].Trim();
            }
        }
        catch { /* 读不了就当没有 */ }
        return map;
    }

    /// <summary>一次把**全部段**连键值读出来（含 `[*]` 与段顺序；段里没有键也算）。
    /// Ninfer 那一族（NinferIni.ReadSections）要的正是"每一段都列出来 + 我自己决定哪些段算模型"，
    /// 而逐段调 ReadSectionValues 会把同一份文件读 N 遍 —— 所以判行的那套（注释走在正则前、
    /// 空行与段头跳过）只有这一个家，别处别再抄第三份。</summary>
    public static List<(string Name, Dictionary<string, string> Values)> ReadSectionsRaw(string iniPath)
    {
        var list = new List<(string, Dictionary<string, string>)>();
        if (string.IsNullOrWhiteSpace(iniPath) || !File.Exists(iniPath)) return list;
        Dictionary<string, string>? cur = null;
        try
        {
            foreach (var l in ReadLines(iniPath, out _))
            {
                var t = l.Text.Trim();
                if (t.Length == 0 || IsComment(t)) continue;
                if (IsHeader(t))
                {
                    cur = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    list.Add((t[1..^1].Trim(), cur));
                    continue;
                }
                if (cur is null) continue;              // 文件头的全局键不属于任何段
                if (!KeyOf(t, out var k)) continue;
                cur[k] = ValOf(t);
            }
        }
        catch { }
        return list;
    }

    /// <summary>Ninfer 那一族（DEC-118/DEC-114 摊平之后）要的是**段里键的出现次序**：
    /// 表外键透传时"旗标在命令行里的先后"照的就是文件里的行序，用 <see cref="ReadSectionsRaw"/> 那个
    /// Dictionary 版拿不到可靠次序（.NET 不承诺），所以这里给一个保序的读口。
    /// **判行的那套（注释走在正则前、空行与段头跳过）仍然只有上面那一个家** —— 这个函数只是换个容器，
    /// 不是第三份解析器，别在 Ninfer 那边再抄一遍。</summary>
    public static List<(string Name, List<KeyValuePair<string, string>> Keys)> ReadSectionsOrdered(string iniPath)
    {
        var list = new List<(string, List<KeyValuePair<string, string>>)>();
        if (string.IsNullOrWhiteSpace(iniPath) || !File.Exists(iniPath)) return list;
        List<KeyValuePair<string, string>>? cur = null;
        try
        {
            foreach (var l in ReadLines(iniPath, out _))
            {
                var t = l.Text.Trim();
                if (t.Length == 0 || IsComment(t)) continue;
                if (IsHeader(t))
                {
                    cur = new List<KeyValuePair<string, string>>();
                    list.Add((t[1..^1].Trim(), cur));
                    continue;
                }
                if (cur is null) continue;              // 文件头的全局键不属于任何段
                if (!KeyOf(t, out var k)) continue;
                cur.Add(new KeyValuePair<string, string>(k, ValOf(t)));
            }
        }
        catch { }
        return list;
    }

    public static List<LlamaModelEntry> ReadModels(string iniPath)
    {
        var list = new List<LlamaModelEntry>();
        if (string.IsNullOrWhiteSpace(iniPath) || !File.Exists(iniPath)) return list;
        LlamaModelEntry? cur = null;
        try
        {
            foreach (var raw in File.ReadAllLines(iniPath))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';')) continue;
                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    var name = line[1..^1].Trim();
                    cur = name.Length == 0 || name == "*" ? null : new LlamaModelEntry { Name = name };
                    if (cur is not null) list.Add(cur);
                    continue;
                }
                if (cur is null) continue;          // 文件头的全局键与 [*] 段都不算模型
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                var key = line[..eq].Trim().ToLowerInvariant();
                var val = line[(eq + 1)..].Trim();
                switch (key)
                {
                    case "model": cur.GgufPath = val; break;
                    case "mmproj": cur.MmprojPath = val; break;
                    case "ctx-size": cur.CtxSize = val; break;
                    case "cache-type-k": cur.CacheTypeK = val; break;
                    case "cache-type-v": cur.CacheTypeV = val; break;
                }
            }
        }
        catch { /* 读不了就当没有模型，别让界面挂掉 */ }
        return list;
    }

    /// 从目录名取 build 号（`llama-b11259-bin-win-cuda-13.4-x64` -> `b11259`）；
    /// 目录名里没有就退回读 llama-server.exe 的文件版本信息（本机实测该项为空，故目录名是主路径）。
    public static string DirVersion(string dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return "";
        var name = Path.GetFileName(dir.TrimEnd('\\', '/'));
        var m = Regex.Match(name, @"llama-(b\d+)", RegexOptions.IgnoreCase);
        if (m.Success) return m.Groups[1].Value.ToLowerInvariant();
        try
        {
            var exe = Path.Combine(dir, "llama-server.exe");
            if (File.Exists(exe))
            {
                var v = FileVersionInfo.GetVersionInfo(exe).FileVersion;
                if (!string.IsNullOrWhiteSpace(v)) return v;
            }
        }
        catch { /* 读不到就算了 */ }
        return "";
    }

    /// 运行时版本（用户口径：显示成 `v0.5.0-b11259`）：
    /// 跑一次 `llama-server --version`（立即退出、不加载模型），输出形如
    /// `version: 0.5.0-dev (build 11259, commit d280808f5)` → 解析后拼成显示串；
    /// 跑不动/解析不到就退回目录名里的 build 号（DirVersion）。按目录缓存成功结果
    /// （拨设置页开关会频繁调 RefreshVersions，不能每次都拉一个子进程）。
    static readonly object RtLock = new();
    static string _rtCacheDir = "", _rtCacheVer = "";

    public static string RuntimeVersion(string dir)
    {
        dir = (dir ?? "").Trim();
        lock (RtLock)
        {
            if (_rtCacheVer.Length > 0 && string.Equals(dir, _rtCacheDir, StringComparison.OrdinalIgnoreCase))
                return _rtCacheVer;
        }
        var text = "";
        try
        {
            var exe = dir.Length > 0 ? Path.Combine(dir, "llama-server.exe") : "";
            if (exe.Length > 0 && File.Exists(exe))
            {
                using var p = Process.Start(new ProcessStartInfo(exe, "--version")
                {
                    UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                    CreateNoWindow = true, WorkingDirectory = dir,
                });
                if (p is not null)
                {
                    // 先挂异步读再等退出：反过来会卡在没关的管道上（超时保护只管 WaitForExit）
                    var outT = p.StandardOutput.ReadToEndAsync();
                    var errT = p.StandardError.ReadToEndAsync();
                    if (!p.WaitForExit(1500)) { try { p.Kill(true); } catch { /* 已经退了 */ } }
                    var so = outT.Wait(500) ? outT.Result : "";
                    var se = errT.Wait(500) ? errT.Result : "";
                    text = so + "\n" + se;
                }
            }
        }
        catch { /* 探不到就退回目录名 */ }
        var result = FormatRuntimeVersion(text, DirVersion(dir));
        if (result.Length > 0) lock (RtLock) { _rtCacheDir = dir; _rtCacheVer = result; }
        return result;
    }

    /// 纯函数（自检喂假文本）：抓 `version:` 与 `build N`；build 抓不到用 fallback（目录名里的 "b11259"）。
    /// `0.5.0-dev` 的 dev 后缀不显示（build 号已标识版本来源），与用户确认的显示口径一致。
    internal static string FormatRuntimeVersion(string versionText, string fallbackBuild)
    {
        var mv = Regex.Match(versionText ?? "", @"version:\s*([0-9][^\s(]*)");
        var mb = Regex.Match(versionText ?? "", @"build\s+(\d+)");
        var ver = mv.Success ? mv.Groups[1].Value : "";
        int dash = ver.IndexOf('-');
        if (dash > 0) ver = ver[..dash];
        var build = mb.Success ? "b" + mb.Groups[1].Value : (fallbackBuild ?? "");
        if (ver.Length > 0 && build.Length > 0) return "v" + ver + "-" + build;
        if (build.Length > 0) return build;
        if (ver.Length > 0) return "v" + ver;
        return "";
    }

}