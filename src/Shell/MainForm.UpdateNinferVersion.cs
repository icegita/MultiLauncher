using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MultiLauncher;

// Ninfer 两侧的版本显示（DEC-097③④：更新窗要同时看得见 WSL 与 WIN）。
// 2026-10-15 从 MainForm.UpdateNinfer.cs 整段搬出，一行未改。
public partial class MainForm
{
    // ---------------- WSL 那一侧的版本（DEC-097③：更新窗要同时看得见 WSL 与 WIN） ----------------

    /// <summary>VM 里读版本那段脚本：件在不在 → 引擎可执行的 sha256 → 那棵检出的 git 提交。
    /// 用 <c>git -C $(dirname B)</c> 是因为 git 自己会往上找 <c>.git</c> —— 引擎住在 <c>build/apps/</c> 下，
    /// 不必壳去猜仓库根在哪。<c>describe --always</c> = 有 tag 报 tag、没 tag 报短哈希，两全。</summary>
    internal static string NinferWslProbeScript(string bin) =>
        "B=" + BashQuote(bin) + "\n"
        + "if [ ! -f \"$B\" ]; then echo MISSING; exit 0; fi\n"
        + "echo SHA=$(sha256sum \"$B\" | cut -c1-64)\n"
        + "echo VER=$(git -C \"$(dirname \"$B\")\" describe --always --dirty 2>/dev/null)\n"
        + "echo REPO=$(git -C \"$(dirname \"$B\")\" rev-parse --show-toplevel 2>/dev/null)\n";

    /// <summary>探针输出的解析（纯函数，自检直接喂样本）。MISSING / 读不到 git 都单独说，不混成"未知"。</summary>
    internal static (string Version, string Sha, string? Problem) ParseNinferWslProbe(string output)
    {
        string sha = "", ver = "", repo = "";
        var sawMissing = false;
        foreach (var raw in (output ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (raw == "MISSING") { sawMissing = true; continue; }
            var eq = raw.IndexOf('=');
            if (eq <= 0) continue;
            var k = raw[..eq];
            var v = raw[(eq + 1)..].Trim();
            if (k == "SHA") sha = v; else if (k == "VER") ver = v; else if (k == "REPO") repo = v;
        }
        if (sawMissing) return ("", "", "VM 里没有那只引擎可执行（设置页 Ninfer 节「WSL 后端 → 引擎可执行」那一格对不对）");
        if (sha.Length == 0) return ("", "", "VM 没给出 sha256（那一趟没跑成？）");
        if (ver.Length == 0) return ("", sha, "读不到 git 提交：VM 里没装 git，或那个目录不是 git 检出");
        return (ver + (ver.EndsWith("-dirty") ? "" : ""), sha, null);
    }

    /// <summary>WSL 侧「版本」那一行显示什么。没启用那一侧就**一个字都不碰 VM**（探一次会把虚拟机拉起来，
    /// 那是动他机器全局状态，DEC-096 因此把这一侧默认关掉）。</summary>
    internal string NinferWslVersionForSettings()
    {
        var s = _config.Current;
        if (!ModuleRegistry.NinferEnabled(s) || !s.NinferWslOn) return "未启用（启用那一侧才会读 VM 里的版本）";
        if (_ninferWslProbing) return "读取中…";
        return s.NinferWslVersion.Length > 0 ? s.NinferWslVersion
                                             : "未查过（打开 Ninfer 的更新窗会自动读一次）";
    }

    bool _ninferWslProbing;

    /// <summary>该探就探一次（<c>force = true</c> = 用户自己点的，那一下允许重复进 VM）。
    /// 判据三条都过才动手：模块开着 + WSL 那一侧开着 + 缓存是空的。</summary>
    internal void EnsureNinferWslVersionProbed(bool force = false)
    {
        // 测试期间一个字都不进 VM（连 force 那条路也不给：自检与出图台会点到「更新…」那一枚，
        // 而"把用户的虚拟机拉起来"不是测试该做的副作用）
        if (VersionProbeDisabledForTest) return;
        var s = _config.Current;
        if (!ModuleRegistry.NinferEnabled(s) || !s.NinferWslOn || _ninferWslProbing) return;
        if (!force && s.NinferWslVersion.Length > 0) return;
        var distro = s.NinferDistro.Trim();
        var bin = s.NinferWslBin.Trim();
        if (distro.Length == 0 || bin.Length == 0)
        {
            LogN("WSL 版本没读：发行版与引擎可执行路径不能空（设置页 Ninfer 节 WSL 那一组）");
            return;
        }
        _ninferWslProbing = true;
        PokeUpdateUi();
        LogN($"$ 读 WSL 侧版本：wsl -d {distro} sh -s（git describe + sha256sum）");
        _ = Task.Run(() =>
        {
            var (code, output) = RunWslScript(distro, NinferWslProbeScript(bin));
            var (ver, sha, problem) = ParseNinferWslProbe(output);
            if (IsDisposed) return;
            try
            {
                BeginInvoke(new Action(() =>
                {
                    _ninferWslProbing = false;
                    if (problem is not null)
                    {
                        LogN("WSL 版本没读出来（退出码 " + code + "）：" + problem);
                        if (code == 0 && sha.Length > 0)
                        {
                            _config.Current.NinferWslVersion = "未知构建（" + NinferShaShort(sha) + "）";
                            _config.Current.NinferWslVersionSha = sha;
                            SaveConfig(commitParams: false);
                        }
                    }
                    else
                    {
                        _config.Current.NinferWslVersion = ver;
                        _config.Current.NinferWslVersionSha = sha;
                        SaveConfig(commitParams: false);
                        LogN($"WSL 侧版本 = {ver}（引擎 sha256 {NinferShaShort(sha)}；查过之后固定在这里，重编完再读一次才变）");
                    }
                    PokeUpdateUi();
                }));
            }
            catch { /* 窗口已销毁 */ }
        });
    }

    // 出图/自检用：本地 sha 与版本缓存这一对可以直接喂（VersionProbeDisabledForTest 已挡住自动那两趟，
    // 所以这里给的是"算完了的样子"）；RememberNinferWinVersion 也开一个口，让自检能验"查过之后固定"那一步
    internal void SetNinferWinShaForTest(string? sha) { _ninferWinShaNow = sha; }
    internal string NinferVersionTextForTest => NinferVersionForSettings();
    internal string NinferDirForTest => _config.Current.NinferDir;
    internal void SetNinferDirForTest(string dir) => _config.Current.NinferDir = dir;
    internal (string Version, string Sha) NinferWinVersionCacheForTest
        => (_config.Current.NinferWinVersion, _config.Current.NinferWinVersionSha);
    internal void SetNinferWinVersionCacheForTest(string tag, string sha)
    {
        _config.Current.NinferWinVersion = tag;
        _config.Current.NinferWinVersionSha = sha;
    }
    internal void RememberNinferWinVersionForTest(string tag, string sha) => RememberNinferWinVersion(tag, sha);
    /// <summary>回退来源这一位的读写口（自检要验「回退过 ≠ 未知构建」那一条，又不想真去覆盖 1.5 GB 的 exe）。</summary>
    internal void SetNinferRolledBackFromForTest(string backupFileName) => _ninferRolledBackFrom = backupFileName ?? "";
    internal string NinferRolledBackFromForTest => _ninferRolledBackFrom;
    internal string NinferWslVersionTextForTest => NinferWslVersionForSettings();

    /// <summary>WIN 侧那只引擎可执行的实际路径（按目录里的 ninfer-serve-*.exe 认，与起服务同一判据）。</summary>
    internal static string NinferWinExePath(string dir)
    {
        dir = (dir ?? "").Trim();
        if (dir.Length == 0 || !Directory.Exists(dir)) return "";
        return Path.Combine(dir, NinferExeName(dir));
    }

}
