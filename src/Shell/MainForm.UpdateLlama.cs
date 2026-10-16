using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MultiLauncher;

// llama.cpp 侧的更新（DEC-049，用户反馈④）：与 Strata 同一套形状——先探版本，探到新的才给出「更新 / 跳过」，
// 但机制完全不同：Strata 是覆盖源码 + 同步官方预编译引擎；llama.cpp 是**按版本下二进制包 → 解压成同父目录的新目录
// → 把设置里的目录切过去**（旧目录原样保留，随时能改回去），完事问一句要不要删刚下载的压缩包。
//
// 查证过的两件事（别再改回去）：
//   · ggml-org/llama.cpp 的 `releases/latest` 返回的是 `v0.5.0` 那种版本标签，里面**没有 Windows 资产**
//     ——每轮构建都发成 pre-release，所以必须走 `releases?per_page=` 列表，取最新的 `b\d+`；
//   · 包名与目录名同构：目录 `llama-b11259-bin-win-cuda-13.4-x64` ↔ 资产 `llama-b11368-bin-win-cuda-13.4-x64.zip`，
//     所以"该下哪只包"直接从当前目录名推出来，不用问用户（DEC-006 第 4 条）。
//   · 那只 zip 里不含 cudart 系列 DLL（另有 `cudart-llama-bin-win-cuda-13.4-x64.zip`），本机现有目录本来也没有
//     （走系统 CUDA v13.4），所以解压即用，不需要额外搬运行库。
public partial class MainForm
{
    // 状态（与 Strata 那套分开各记一份：跳过的版本也各自独立）
    bool _llamaUpdating;
    string _llamaBusy = "";
    bool _llamaHasUpdate, _llamaSkippedShown;
    string _llamaRemoteTag = "", _llamaRemoteNotes = "", _llamaRemoteAssetUrl = "", _llamaRemoteAssetName = "", _llamaRemotePublished = "";
    string _llamaRemoteAssetSha256 = "";      // DEC-067：资产上的 digest（"sha256:…"，上游没给就空着）
    long _llamaRemoteAssetSize;               // DEC-067：资产声明的字节数，下完先比这个
    string _llamaMemo = "";
    System.Drawing.Color _llamaMemoColor = UiTheme.TextMuted;

    // 设置窗登记：更新把目录切过去之后，回调里把「llama.cpp 目录」那行的输入框也改掉
    //（设置窗持有副本，不同步的话用户随后点「保存」会把旧路径又写回来）
    internal Action<string>? LlamaDirAppliedExternally;

    // ---------------- 纯函数（自检直接喂样本断言，不联网） ----------------

    /// 目录名 → 资产后缀：`llama-b11259-bin-win-cuda-13.4-x64` → `bin-win-cuda-13.4-x64`
    internal static string LlamaAssetSuffix(string dir)
    {
        var name = Path.GetFileName((dir ?? "").TrimEnd('\\', '/'));
        var m = Regex.Match(name, @"^llama-b\d+-(.+)$", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : "";
    }

    /// 新目录名：同前缀同后缀，只换 build 号（tag 允许写成 `b11368` 或 `v…-b11368`）
    internal static string LlamaNewDirName(string dir, string tag)
    {
        var name = Path.GetFileName((dir ?? "").TrimEnd('\\', '/'));
        var t = Regex.Match(tag ?? "", @"b\d+", RegexOptions.IgnoreCase).Value;
        var m = Regex.Match(name, @"^(llama-)(b\d+)(-.+)?$", RegexOptions.IgnoreCase);
        if (t.Length == 0) return name;
        return m.Success ? m.Groups[1].Value + t + m.Groups[3].Value : "llama-" + t;
    }

    /// 「更新源」允许填网页地址或 api 地址，统一成 releases 列表接口；认不出 owner/repo 就返回空。
    /// 注意先判 api.github.com：那条路本身就是 `api.github.com/repos/<owner>/<repo>/releases`，
    /// 让通用规则先吃会把 "repos" 当成 owner（自检抓到过）。
    internal static string LlamaReleasesApi(string url)
    {
        url = (url ?? "").Trim();
        if (url.Contains("api.github.com", StringComparison.OrdinalIgnoreCase))
            return url.Split('?')[0].TrimEnd('/') + "?per_page=20";
        var m = Regex.Match(url, @"github\.com/([^/]+)/([^/?#]+)", RegexOptions.IgnoreCase);
        return m.Success ? $"https://api.github.com/repos/{m.Groups[1].Value}/{m.Groups[2].Value}/releases?per_page=20" : "";
    }

    internal static int LlamaBuildOf(string s)
    {
        var m = Regex.Match(s ?? "", @"b(\d+)", RegexOptions.IgnoreCase);
        return m.Success ? int.Parse(m.Groups[1].Value) : -1;
    }

    /// 从 releases 列表 JSON 里挑「最新的、且带着我们要的那只资产」的一版（列表本身就新→旧）。
    /// 跳过 draft 与 `v0.5.0` 这种没有二进制资产的版本标签；pre-release 照收（llama.cpp 每轮构建都是 pre-release）。
    /// suffix 为空时退化成"任意 zip"（目录名推不出后缀的场合，比如用户手改过目录名）。
    /// Sha256 / Size 来自资产上的 digest 与 size（DEC-067：下载完要对着验，上游没给就是空串 / 0，按"没这条校验"走）。
    internal static (string Tag, string Notes, string AssetUrl, string Published, string AssetName, string Sha256, long Size)
        ParseLlamaRelease(string json, string suffix)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonArray arr) return ("", "", "", "", "", "", 0);
            foreach (var n in arr)
            {
                if (n is not JsonObject r) continue;
                if (ReadBool(r["draft"])) continue;                      // 草稿不发人；pre-release 照收（每轮构建都是 pre-release，但别拿它当硬条件——上游哪天不改标记就会永远探不到）
                var tag = r["tag_name"]?.GetValue<string>() ?? "";
                if (!Regex.IsMatch(tag, @"^b\d+$", RegexOptions.IgnoreCase)) continue;
                if (r["assets"] is not JsonArray assets) continue;
                foreach (var a in assets)
                {
                    var name = a?["name"]?.GetValue<string>() ?? "";
                    var url = a?["browser_download_url"]?.GetValue<string>() ?? "";
                    // 只认 llama- 开头的二进制包：同目录里还有 cudart-llama-bin-win-cuda-13.4-x64.zip 这种
                    // CUDA 运行库包，它的名字同样以 "-bin-win-cuda-13.4-x64.zip" 结尾（自检抓到过误挑）
                    if (url.Length == 0 || !name.StartsWith("llama-", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
                    if (suffix.Length > 0 && !name.EndsWith("-" + suffix + ".zip", StringComparison.OrdinalIgnoreCase)) continue;
                    return (tag, r["body"]?.GetValue<string>() ?? "", url,
                            r["published_at"]?.GetValue<string>() ?? "", name,
                            a?["digest"]?.GetValue<string>() ?? "", ReadLong(a?["size"]));
                }
            }
        }
        catch { /* 解析不了按"没探到"处理，界面给检查失败 */ }
        return ("", "", "", "", "", "", 0);
    }

    // size 可能是整数也可能缺失；digest 老数据里没有 → 空串（校验按"上游没给"跳过，不当失败）
    static long ReadLong(JsonNode? n)
    {
        try { return n?.GetValue<long>() ?? 0; } catch { return 0; }
    }

    static bool ReadBool(JsonNode? n, bool whenMissing = false)
    {
        try { return n?.GetValue<bool>() ?? whenMissing; } catch { return whenMissing; }
    }

    // ---------------- 界面回显（与 Strata 共用设置窗那一个刷新回调） ----------------

    // 版本行只报版本：目录与 CUDA 后缀（bin-win-cuda-13.4-x64）都不显示——路径在「路径」那节看得到，
    // 后缀是选包用的内部判据，摆在版本行里读起来像半截路径（用户 2026-10-03：始终不该显示）。
    /// 只给出图用：把工作区那份内存里的 llama 目录指到实际存在的最新兄弟目录。
    /// 工作区构建目录没有 ini，默认值常指向已被用户删掉的旧版本，拍出来会是"（目录不存在）"这种假象。
    /// 只改内存、不 SaveConfig——这个进程也不会保存。
    internal void PointLlamaDirAtNewestSiblingForPreview()
    {
        var cur = _config.Current.LlamaDir.Trim().TrimEnd(Path.DirectorySeparatorChar);
        var parent = Path.GetDirectoryName(cur) ?? "";
        if (parent.Length == 0 || !Directory.Exists(parent)) return;
        foreach (var d in Directory.EnumerateDirectories(parent, "llama-b*-bin-win-*")
                     .OrderByDescending(x => x, StringComparer.OrdinalIgnoreCase))
        { _config.Current.LlamaDir = d; return; }
    }

    internal string LlamaVersionForSettings()
    {
        var dir = _config.Current.LlamaDir.Trim();
        if (dir.Length == 0 || !Directory.Exists(dir)) return "（目录不存在）";
        var rt = LlamaIni.RuntimeVersion(dir);
        return rt.Length > 0 ? rt : (LlamaIni.DirVersion(dir).Length > 0 ? LlamaIni.DirVersion(dir) : "未知");
    }

    internal (string Text, System.Drawing.Color Color) LlamaUpdateStatusForSettings()
    {
        if (_llamaBusy.Length > 0) return (_llamaBusy, System.Drawing.Color.DarkOrange);
        if (_llamaMemo.Length > 0) return (_llamaMemo, _llamaMemoColor);
        return ("", UiTheme.TextMuted);
    }

    /// ④：llama 侧的检查/更新进行中，同样要让按钮灰置
    internal bool LlamaUiBusy => _llamaUpdating;

    internal void SetLlamaBusyForTest(bool on) { _llamaUpdating = on; PokeUpdateUi(); }

    internal UpdateOffer LlamaUpdateOfferForSettings()
    {
        if (_llamaRemoteTag.Length == 0 || (!_llamaHasUpdate && !_llamaSkippedShown))
            return new UpdateOffer(false, "", "", "跳过", "", -1, false);
        var when = _llamaRemotePublished.Length >= 10 ? _llamaRemotePublished.Substring(0, 10) : "";
        var ver = _llamaRemoteTag + (when.Length > 0 ? " · 发布 " + when : "");
        var note = _llamaHasUpdate
            ? ""      // ①④：这一行不重复"更新会做什么"（用户反馈：多余）—— 设置页那条说明也按 DEC-110② 删净了，口径归 docs/.ai/参考-界面与参数对照.md
            : "这个版本你之前跳过一次（记在 MultiLauncher.ini 的 LlamaSkipVersion）";
        return new UpdateOffer(true, ver, note, _llamaHasUpdate ? "跳过" : "取消跳过", _llamaPhase, _llamaFrac, LlamaCancellable);
    }

    void SetLlamaInfo(string text, System.Drawing.Color color)
    {
        if (InvokeRequired) { try { BeginInvoke(new Action(() => SetLlamaInfo(text, color))); } catch { } return; }
        _llamaMemo = text;
        _llamaMemoColor = color;
        if (text.Length > 0) LogL("llama.cpp 更新状态：" + text);
        PokeUpdateUi();
    }

    void FinishLlama()
    {
        if (InvokeRequired) { try { BeginInvoke(new Action(FinishLlama)); } catch { } return; }
        _llamaUpdating = false;
        _llamaBusy = "";
        _llamaPhase = ""; _llamaFrac = -1;
        _llamaCancelWanted = false;
        PokeUpdateUi();
    }

    void ClearLlamaOffer()
    {
        _llamaHasUpdate = false;
        _llamaSkippedShown = false;
        _llamaRemoteTag = _llamaRemoteNotes = _llamaRemoteAssetUrl = _llamaRemoteAssetName = _llamaRemotePublished = _llamaRemoteAssetSha256 = "";
        _llamaRemoteAssetSize = 0;
        PokeUpdateUi();
    }

}
