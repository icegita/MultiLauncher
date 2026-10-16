using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MultiLauncher;

// llama 更新链：检查（releases 列表 → 比体积/摘要 → 选资产）与更新（下包 → 校验 → 停服务 → 解压新目录 → 切路径 → 问删包）。
// 2026-10-15 从 MainForm.UpdateLlama.cs 整段搬出，一行未改。
public partial class MainForm
{
    // ---------------- 检查 ----------------

    internal void LlamaCheckUpdate()
    {
        if (_llamaUpdating) return;
        var s = _config.Current;
        var api = LlamaReleasesApi(s.LlamaUpdateUrl);
        var dir = s.LlamaDir.Trim();
        var local = LlamaIni.DirVersion(dir);
        if (api.Length == 0) { SetLlamaInfo("检查失败", System.Drawing.Color.Firebrick); LogL("检查更新失败：「更新源」里认不出 GitHub 的 owner/repo（" + s.LlamaUpdateUrl + "）"); return; }
        if (LlamaBuildOf(local) < 0)
        {
            SetLlamaInfo("检查失败", System.Drawing.Color.Firebrick);
            LogL("检查更新失败：从目录名里取不到 build 号（" + dir + "）——这个目录不像 llama.cpp 的解压目录（形如 llama-b11259-bin-win-cuda-13.4-x64）");
            return;
        }
        var suffix = LlamaAssetSuffix(dir);
        _llamaUpdating = true;
        _llamaBusy = "检查中…";
        ClearLlamaOffer();
        PokeUpdateUi();
        LogL($"$ 检查 llama.cpp 更新: {api}（按目录名匹配 {suffix}）");
        Task.Run(() =>
        {
            var tmp = Path.Combine(TempDir(), "llama-releases.json");
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* 留着也无妨 */ }
            var python = Path.Combine(s.Root, ".venv", "Scripts", "python.exe");
            string err;
            if (!Net.Download(api, tmp, python, out err))
            {
                LogL("检查更新失败（网络不可达？）: " + err);
                SetLlamaInfo("检查失败", System.Drawing.Color.Firebrick);
                FinishLlama();
                return;
            }
            string json;
            try { json = File.ReadAllText(tmp); }
            catch (Exception ex) { LogL("读发布信息失败: " + ex.Message); SetLlamaInfo("检查失败", System.Drawing.Color.Firebrick); FinishLlama(); return; }
            var rel = ParseLlamaRelease(json, suffix);
            var (tag, notes, url, published, asset) = (rel.Tag, rel.Notes, rel.AssetUrl, rel.Published, rel.AssetName);
            if (tag.Length == 0)
            {
                LogL("没找到可用的发布（列表里没有带 " + suffix + " 包的 b 号版本？）");
                SetLlamaInfo("检查失败", System.Drawing.Color.Firebrick);
                FinishLlama();
                return;
            }
            int remoteBuild = LlamaBuildOf(tag), localBuild = LlamaBuildOf(local);
            if (remoteBuild <= localBuild)
            {
                LogL($"已是最新（本地 {local}，远端 {tag}，发布 {published}）");
                SetLlamaInfo("已是最新", System.Drawing.Color.Green);
                FinishLlama();
                return;
            }
            var skipped = _config.Current.LlamaSkipVersion.Trim();
            bool isSkipped = skipped.Length > 0 && tag.StartsWith(skipped, StringComparison.OrdinalIgnoreCase);
            BeginInvoke(new Action(() =>
            {
                if (IsDisposed) return;
                _llamaRemoteTag = tag;
                _llamaRemoteNotes = notes;
                _llamaRemoteAssetUrl = url;
                _llamaRemoteAssetName = asset;
                _llamaRemotePublished = published;
                _llamaRemoteAssetSha256 = rel.Sha256;       // DEC-067：跟着这一版一起记，下载完照它验
                _llamaRemoteAssetSize = rel.Size;
                _llamaHasUpdate = !isSkipped;
                _llamaSkippedShown = isSkipped;
                SetLlamaInfo(isSkipped ? $"远端 {tag}（你已跳过）" : $"发现新版本 {tag}",
                    isSkipped ? UiTheme.TextMuted : System.Drawing.Color.Green);
                LogL(isSkipped
                    ? $"远端 {tag}（本地 {local}）你跳过过一次；要升就在设置页那一行点「取消跳过」。"
                    : $"发现 llama.cpp 新版本 {tag}（本地 {local}）：设置页「llama.cpp」节「检查更新」下面有「查看更新内容 / 更新 / 跳过」。");
            }));
            FinishLlama();
        });
    }

    internal void ShowLlamaUpdateDetail()
    {
        if (_llamaRemoteTag.Length == 0) { LogL("还没有远端版本信息，请先点「检查更新」。"); return; }
        var notes = _llamaRemoteNotes.Length > 0 ? _llamaRemoteNotes : "（这次发布没有写更新说明）";
        var res = Dialogs.UpdateDetailDialog(this, _llamaRemoteTag, LlamaIni.DirVersion(_config.Current.LlamaDir), notes);
        if (res == DialogResult.Yes) LlamaUpgrade();
        else if (res == DialogResult.Ignore) LlamaSkip();
    }

    internal void LlamaSkip()
    {
        if (_llamaRemoteTag.Length == 0) return;
        string skippedTag = _llamaRemoteTag;     // 同 DEC-061：ClearLlamaOffer 会把它清空，话要先取好
        _config.Current.LlamaSkipVersion = _llamaRemoteTag;
        SaveConfig(commitParams: false);
        SetLlamaInfo($"已跳过 {_llamaRemoteTag}", UiTheme.TextMuted);
        ClearLlamaOffer();
        Toast.Show(this, "已跳过 " + skippedTag + "（下次探到它不再提示；要恢复就在更新窗点「取消跳过」）", Toast.Kind.Info);
    }

    internal void LlamaUnskip()
    {
        if (_llamaRemoteTag.Length == 0) return;
        _config.Current.LlamaSkipVersion = "";
        SaveConfig(commitParams: false);
        _llamaSkippedShown = false;
        _llamaHasUpdate = true;
        SetLlamaInfo($"已取消对 {_llamaRemoteTag} 的跳过", System.Drawing.Color.Green);
        LogL($"已取消对 {_llamaRemoteTag} 的跳过：现在点「更新」就能升级。");
        Toast.Show(this, "已取消对 " + _llamaRemoteTag + " 的跳过，点「更新」即可升级", Toast.Kind.Ok);
        PokeUpdateUi();
    }

    // ---------------- 更新（下载 → 解压到新目录 → 切路径 → 问是否删包） ----------------

    internal void LlamaUpgrade()
    {
        if (!_llamaHasUpdate || _llamaUpdating) return;
        var s = _config.Current;
        var old = _llamaProcess;
        // DEC-138（D-7）：与 Strata / Ninfer 两条同一批收口 —— 确认只问不不停，停这动作由工作体在
        // **下载并校验通过之后**才做（校验不过 = 服务照旧在跑 = 注释里那句"本机零影响"才是真的）。
        bool needStop = old is { HasExited: false };
        if (needStop)
        {
            if (Dialogs.Confirm(this, "更新前先停服务", "更新 llama.cpp 需要先停掉正在跑的 llama-server。",
                    "现在停止并继续？", yes: "停止并更新", no: "取消") != DialogResult.Yes) return;
        }
        _llamaUpdating = true;
        _llamaBusy = "更新中…";
        SetState("● 更新中…", System.Drawing.Color.DarkOrange);
        PokeUpdateUi();
        LogL($"$ 下载 {_llamaRemoteAssetName}…");
        Task.Run(() => LlamaUpgradeWork(s, old, needStop));
    }

    void LlamaUpgradeWork(Settings s, Process? old, bool needStop)
    {
        // DEC-114①：解压目录一起提到 try 外面，由方法末尾的 finally 收尾（照 Strata 那条同一个形状）
        string zip = "", dst = "", ext = "";
        try
        {
            // DEC-138（D-7）：这里原来开头就等旧进程退出（服务在入口那一击已被停掉）。现在挪到校验之后，开头不等。
            // DEC-061：远端标签在**这一刻**取成局部量。ApplyLlamaDir 的回调会在 UI 线程把 _llamaRemoteTag 清掉，
            // 后台线程再直接读它就有可能读到空串（弹窗标题、解压目录名、新目录名都从它派生）。
            var newTag = _llamaRemoteTag;
            var dir = s.LlamaDir.TrimEnd('\\', '/');
            var parent = Path.GetDirectoryName(dir) ?? "";
            var newName = LlamaNewDirName(dir, newTag);
            dst = Path.Combine(parent, newName);
            if (string.Equals(Path.GetFullPath(dst), Path.GetFullPath(dir), StringComparison.OrdinalIgnoreCase))
            {
                LogL("新目录与当前目录同名，没法解压过去（远端版本号和本地一样？）");
                FinishLlamaUpgrade(false, "");
                return;
            }
            zip = Path.Combine(TempDir(), _llamaRemoteAssetName);
            try { if (File.Exists(zip)) File.Delete(zip); } catch { /* 覆盖不了会报在下载那步 */ }
            var python = Path.Combine(s.Root, ".venv", "Scripts", "python.exe");
            _llamaCancelWanted = false;
            ReportProgress(false, "下载 llama.cpp 包", -1);
            if (!Net.Download(_llamaRemoteAssetUrl, zip, python, out var err,
                    (done, total) => ReportProgress(false, "下载 llama.cpp 包", total > 0 ? (double)done / total : -1),
                    CancelRequested(false)))
            {
                // DEC-061：取消不是失败（与 Strata 侧同一判据、同一口径的回显），以前一律报「更新失败」弹红字
                if (err.Contains("取消"))
                {
                    LogL("已取消：半截包已由 Net.Download 删掉，llama.cpp 目录与设置都没动。");
                    SetLlamaInfo("已取消", UiTheme.TextMuted);
                    Toast.Show(this, "已取消更新：llama.cpp 目录与设置都没动，半截包已删掉", Toast.Kind.Info);
                    FinishLlamaUpgrade(false, "");
                    return;
                }
                LogL("下载失败: " + err);
                SetLlamaInfo("更新失败", System.Drawing.Color.Firebrick);
                FinishLlamaUpgrade(false, "");
                return;
            }
            // DEC-067：下完了先对账 —— 体积按 release 资产声明的 size、摘要按它给的 digest（"sha256:…"）。
            // 不符就删包收手：这时还没停服务、没解压、没切目录，本机一点没动。
            // 上游没给 digest 时只比体积（Size 为 0 就两条都跳过，不误杀）。
            ReportProgress(false, "校验", 1);
            if (!Net.VerifyDownload(zip, _llamaRemoteAssetSize, _llamaRemoteAssetSha256, out var verifyErr))
            {
                LogL("校验失败: " + verifyErr + "（包已删除，llama.cpp 目录与设置都没动）");
                SetLlamaInfo("校验失败", System.Drawing.Color.Firebrick);
                Toast.Show(this, "下载校验没通过，包已删除：本机一点没动（" + verifyErr + "）", Toast.Kind.Error);
                FinishLlamaUpgrade(false, "");
                return;
            }
            LogL(_llamaRemoteAssetSha256.Length > 0
                ? "校验通过：sha256 与 release 资产上的 digest 一致（" + _llamaRemoteAssetName + "）"
                : "校验通过：体积与 release 资产声明的 size 一致（上游没给 digest，只比了体积）");
            var mb = new FileInfo(zip).Length / 1048576.0;
            LogL($"下载完成: {mb:F1} MB → {zip}");

            // DEC-138（D-7 / 拍板⑤）：**校验过了才停服务**（这一族的解压落的是新目录，旧目录本来不动，
            // 但切 `LlamaDir` 之前停一下与另两条链同一口径）。停不下来就收手：本机一个字都还没改。
            if (needStop)
            {
                LogL("校验过了：现在停掉 llama-server，再往下才动目录与设置。");
                UiPost(StopLlamaServer);
                try { old?.WaitForExit(20000); } catch { /* 已经退出 */ }
                if (old is { HasExited: false })
                {
                    LogL("llama-server 停不下来（20 秒），到此收手：目录与设置都没切。");
                    SetLlamaInfo("停不下来，没更新", System.Drawing.Color.Firebrick);
                    Toast.Show(this, "llama-server 停不下来，没有更新：目录与设置都没动", Toast.Kind.Warn);
                    FinishLlamaUpgrade(false, "");
                    return;
                }
            }

            if (Directory.Exists(dst))
            {
                // 同版本目录已存在（以前下过一半、或用户自己放过）：不覆盖用户的东西，直接用它或收手
                if (File.Exists(Path.Combine(dst, "llama-server.exe")))
                {
                    LogL("目标目录已存在且有 llama-server.exe：不再解压，直接把目录切过去");
                    ApplyLlamaDir(dst);
                    AskAboutZip(zip, mb, dst, newTag);
                    return;
                }
                LogL("目标目录已存在但里面没有 llama-server.exe：不动它，请到设置页换个 llama.cpp 目录再试（" + dst + "）");
                SetLlamaInfo("更新没完成", System.Drawing.Color.Firebrick);
                FinishLlamaUpgrade(false, "");
                return;
            }

            ext = Path.Combine(TempDir(), "llama-ext-" + newTag);
            try { if (Directory.Exists(ext)) Directory.Delete(ext, true); } catch { /* 下面会再报 */ }
            Directory.CreateDirectory(ext);
            if (!ExtractWithProgress(zip, ext, (i, n) => ReportProgress(false, "解压", (double)i / n), CancelRequested(false)))
            {
                try { Directory.Delete(ext, true); } catch { /* 半截目录删不掉就算了 */ }
                LogL("已取消：只删掉临时解压目录，llama.cpp 目录与设置都没动。");
                SetLlamaInfo("已取消", UiTheme.TextMuted);
                FinishLlamaUpgrade(false, "");
                return;
            }
            // 包内布局两种都见过：文件平铺在根、或包一层同名目录。统一收成"真正的根"。
            var tops = Directory.GetDirectories(ext);
            var srcRoot = tops.Length == 1 && Directory.GetFiles(ext).Length == 0 ? tops[0] : ext;
            if (!File.Exists(Path.Combine(srcRoot, "llama-server.exe")))
            {
                LogL("解压完了但找不到 llama-server.exe（包结构不认识）：" + srcRoot);
                SetLlamaInfo("更新没完成", System.Drawing.Color.Firebrick);
                FinishLlamaUpgrade(false, "");
                return;
            }
            ReportProgress(false, "搬到新目录", -1);
            // DEC-136（D-6）：这里原来是全项目**仅存的一处** `Directory.Move`（DEC-121 之后搬家链已清干净，
            // 而 AGENTS 写的是"代码里不许出现"）。问题不在慢，在那句不分类的 `catch` —— 跨卷、目标已存在、
            // ACL、杀软拦写全被吞成"那就复制"；而复制半途抛（磁盘满 / 文件被占）就留下一个只有部分文件的
            // 新目录，下面那道"llama-server.exe 在不在"又可能恰好放过它 → 把 `Settings.LlamaDir` 切到缺 DLL 的目录，
            // 服务起不来还看不出错。收成一条路径：逐文件复制 + 与源目录的数对一次，对不上就删掉目标并收手。
            int wantFiles = Directory.GetFiles(srcRoot, "*", SearchOption.AllDirectories).Length;
            int gotFiles = CopyTree(srcRoot, dst);
            if (gotFiles != wantFiles)
            {
                LogL($"搬到新目录没完成：应复制 {wantFiles} 个文件、实际 {gotFiles} 个 —— 目标目录已删，" +
                     "llama.cpp 目录与设置都没切（旧目录照原样，随时能重试）");
                try { if (Directory.Exists(dst)) Directory.Delete(dst, true); } catch { /* 留着：下次同版本那条"已存在"会点名 */ }
                FinishLlamaUpgrade(false, "extract");
                return;
            }
            try { if (Directory.Exists(ext)) Directory.Delete(ext, true); } catch { /* 空壳留着无害 */ }
            if (!File.Exists(Path.Combine(dst, "llama-server.exe")))
            {
                LogL("搬完之后 llama-server.exe 不在（" + dst + "）：不切目录");
                SetLlamaInfo("更新没完成", System.Drawing.Color.Firebrick);
                FinishLlamaUpgrade(false, "");
                return;
            }
            ApplyLlamaDir(dst);
            LogL($"已解压到新目录 {dst}（旧目录 {dir} 原样保留，想回去就在设置页把目录改回来）");
            AskAboutZip(zip, mb, dst, newTag);
        }
        catch (Exception ex)
        {
            LogL("更新 llama.cpp 出错: " + ex.Message);
            SetLlamaInfo("更新没完成", System.Drawing.Color.Firebrick);
            FinishLlamaUpgrade(false, "");
        }
        finally
        {
            // DEC-114①：四条路（成功 / 包结构不认识 / 搬完缺 exe / 异常）都从这里过 —— 解压目录不留。
            // Move 成功那条 ext 已经被搬走了，CleanupStagingDir 这里就是"没这条目录、什么都不做"，不抛。
            // zip 照旧问一句（AskAboutZip），留不留归用户。
            CleanupStagingDir(ext.Length > 0 ? ext : null);
        }
    }

    // 落盘 + 界面跟上：设置窗里那行「llama.cpp 目录」的输入框要同步改，
    // 否则用户随后点「保存」会把旧路径又写回来（设置窗持有的是副本）
    void ApplyLlamaDir(string dst)
    {
        BeginInvoke(new Action(() =>
        {
            _config.Current.LlamaDir = dst;
            SaveConfig(commitParams: false);
            LlamaDirAppliedExternally?.Invoke(dst);
            RefreshVersions();
            RefreshModels();
            RefreshRunStateView();
            SetLlamaInfo("已更新", System.Drawing.Color.Green);
            ClearLlamaOffer();
        }));
    }

    // DEC-061：标题里的版本号改成**参数**传入（在后台线程求值，此刻还在）。以前直接读 _llamaRemoteTag，
    // 而 ApplyLlamaDir 排在前面的 BeginInvoke 回调里已经 ClearLlamaOffer() 把它清了 →
    // 弹出来是「llama.cpp 已更新到 」（空号，与 Strata 侧同一形状的错）。
    void AskAboutZip(string zip, double mb, string dst, string tag)
    {
        BeginInvoke(new Action(() =>
        {
            var r = Dialogs.Confirm(this, "llama.cpp 已更新到 " + tag,
                "新目录：" + dst + "（设置里的目录已切过去）",
                $"刚下载的压缩包 {mb:F1} MB 要删掉吗？旧目录原样保留，随时能改回去。",
                yes: "删除", no: "保留");
            if (r == DialogResult.Yes)
            {
                try { File.Delete(zip); LogL("已删除压缩包：" + zip); }
                catch (Exception ex) { LogL("压缩包没删掉（留着也无妨）: " + ex.Message); }
            }
            else LogL("压缩包保留在：" + zip);
            FinishLlamaUpgrade(true, dst);
        }));
    }

    void FinishLlamaUpgrade(bool ok, string dst)
    {
        _llamaUpdating = false;
        _llamaBusy = "";
        _llamaPhase = ""; _llamaFrac = -1;
        _llamaCancelWanted = false; CancelResetsForTest++;       // DEC-138（E-8）：与 Strata 那条同一个证人位
        if (IsDisposed) return;
        try { BeginInvoke(new Action(() => { SetState(_llamaProcess is { HasExited: false } ? "● 运行中" : "已停止", System.Drawing.Color.Gray); PokeUpdateUi(); })); } catch { }
        if (ok) LogL("llama.cpp 更新完成：选中 llama 模型点「启动」就是新版本（旧目录还在，随时能在设置页改回去）");
    }

    /// <summary>逐文件复制（DEC-136 D-6：返回复制了几个文件，调用方要拿它跟源目录的数对一次）。
    /// 只数文件不数空目录 —— 空目录在 llama 那套发布包里不存在，计数对不上就是半截，宁可收手。</summary>
    static int CopyTree(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        int n = 0;
        foreach (var f in Directory.GetFiles(src)) { File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true); n++; }
        foreach (var d in Directory.GetDirectories(src)) n += CopyTree(d, Path.Combine(dst, Path.GetFileName(d)));
        return n;
    }

    // 自检出口：不联网造一次「探到 llama 新版本」的态
    internal void SetLlamaOfferForTest(string tag, string published, bool skipped)
    {
        _llamaRemoteTag = tag;
        _llamaRemoteNotes = "自检用的假说明";
        _llamaRemotePublished = published;
        _llamaHasUpdate = !skipped;
        _llamaSkippedShown = skipped;
        PokeUpdateUi();
    }

    internal void ClearLlamaOfferForTest() => ClearLlamaOffer();
}
