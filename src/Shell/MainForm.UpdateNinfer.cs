using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MultiLauncher;

// ============================================================================
// Ninfer 侧的更新链（WIN 那条 = DEC-096「一支 exe 一个资产」；WSL 那条 = DEC-106，
// git fetch/ff-only + cmake 重编 + 快照留 5 份 + 免重编回退，形状整体搬自旧壳）
// ============================================================================
// 与 llama.cpp 那条同形（DEC-049/067 现成套路），但有三处不一样，都来自实测与设计稿 §1.5 / 支点 F-本机：
//   · **一支 exe 就是一个资产**：`ninfer-serve-89.exe` / `-86` / `-120a`，各自带 `digest` + `size`
//     —— 不解压、不搬目录，**只换那一只 exe，DLL 一律不动**（CUDA/FFmpeg/curl 的运行库不在 Release 里；
//     若哪天新构建链了更高的 CUDA，只换 exe 会得到 0xC0000135 → 所以「备份旧 exe + 设置页恢复」是硬要求）。
//   · 选资产**按本机 compute capability**（4090 = 8.9 → `-89`），不是"最新即最优"：
//     一支 exe 只含一个 cubin、**没有 PTX 回退**，装错架构直接起不来 → 表里没有匹配架构的资产 = 拒绝并说明。
//   · **版本号 = 拿本地 exe 的 sha256 去 Release 资产表反查 tag**（exe 自己打的日志是 `build unknown`，
//     源码里也没有版本号）；反查不到就报「未知构建（sha 前 16 位）」，ini 里**不存版本快照**。
//
// 校验三条口径照 DEC-067：`DigestMatches` 只认 `sha256:` 前缀或裸 hex；上游没给 digest/size = 按"没这条校验"通过；
// 校验不过 = 删包收手（此刻还没停服务、没备份、没覆盖 = 本机零影响）。
public partial class MainForm
{
    // ---------------- 状态（跳过位/取消位/进度各一份，DEC-061：共用一份就等于一边操作把另一边一起停掉） ----------------

    bool _ninferUpdating;
    bool _ninferHasUpdate, _ninferSkippedShown;
    string _ninferRemoteTag = "", _ninferRemoteNotes = "", _ninferRemoteUrl = "", _ninferRemoteAssetName = "", _ninferRemotePublished = "";
    string _ninferRemoteSha256 = "";
    long _ninferRemoteAssetSize;
    string _ninferMemo = "";
    System.Drawing.Color _ninferMemoColor = UiTheme.TextMuted;
    string _ninferPhase = "";
    double _ninferFrac = -1;
    volatile bool _ninferCancelWanted;
    string _ninferLocalTag = "";        // 本会话刚反查到的本地版本（优先于缓存显示）
    string _ninferLocalSha = "";
    string? _ninferWinShaNow;           // 本地 exe 的 sha256（null = 本会话还没算过，见 EnsureNinferWinSha）
    bool _ninferWinShaComputing;        // 那一趟只发起一次（刷视图会反复问「版本」，别每次都读一遍 1.5 GB）
    /// <summary>检查那一步算出来的本地值一并交给缓存这条路（UI 线程调）：sha 存进 <see cref="_ninferWinShaNow"/>
    /// 免得再读一遍盘；反查到了 tag 就把这一对钉进 ini（DEC-097④，下次开界面直接读得到，不必再点一次检查）。</summary>
    void AdoptNinferLocal(string tag, string sha)
    {
        _ninferLocalTag = tag; _ninferLocalSha = sha;
        if (sha.Length > 0) _ninferWinShaNow = sha;
        RememberNinferWinVersion(tag, sha);
    }

    internal bool NinferUiBusy => _ninferUpdating;
    internal bool NinferCancellable => _ninferUpdating && PhaseCancellable(_ninferPhase) && _ninferRemoteAssetName.Length > 0;
    internal void SetNinferBusyForTest(bool on) { _ninferUpdating = on; PokeUpdateUi(); }

}
