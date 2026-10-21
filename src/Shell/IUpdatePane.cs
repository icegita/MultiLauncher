namespace MultiLauncher;

/// <summary>更新窗真正要的那一面（DEC-202 第 4 条链的解耦点）。
///
/// 背景：`UpdateForm` 以前直接持 `MainForm.ModuleView` —— 而 `ModuleView` 是**模块表的一行**
/// （40+ 个 required 成员、带模块开关 / 状态灯 / 卡1 格子 / 设置节）。「本程序」不是引擎后端，
/// 塞进模块表会把那一整套拖进来；所以把窗实际用到的这几个成员抽成接口，
/// `ModuleView` 显式实现（现有三条链零行为变化），新增的 <see cref="AppUpdateCard"/> 也实现它 ——
/// 「本程序」白得同一扇三行窗。
///
/// 成员清单 = 施工前逐处核过的 `_v.xxx` 调用点全集（DEC-202 §4.4 那 13 项 + `Key`）。
/// 加成员前先问：窗真的读它吗？模块表能不能不表态？</summary>
internal interface IUpdatePane
{
    /// <summary>窗口字典的键与日志里的身份（模块 = 模块键；本程序 = "app"）。</summary>
    string Key { get; }
    /// <summary>标题栏那句「更新 · &lt;它&gt;」。</summary>
    string DisplayName { get; }
    /// <summary>主段标题；null = 用 DisplayName（单段模块的既有口径）。</summary>
    string? PrimarySectionTitle { get; }
    /// <summary>一模块两窗的第二段（Ninfer-WSL）；null = 这一扇没有第二段。</summary>
    UpdateSection? Secondary { get; }

    /// <summary>第二、三行的数据源（有没有新版本 / 被跳过那一版 / 阶段与进度）。</summary>
    UpdateOffer Offer();
    /// <summary>第二行那句状态话与颜色（检查中 / 已是最新 / 检查失败）。</summary>
    (string Memo, Color Color) StatusMemo();
    /// <summary>这条链在不在忙（按钮置灰、进度条出面都认它）。</summary>
    bool UpdateBusy();
    /// <summary>这一进程里探到过没有（开窗即查的"不必再查"判据）。</summary>
    bool UpdateOfferSeen();
    /// <summary>「版本」那一行的当前值。</summary>
    string CurrentVersion();

    void CheckUpdateNow();
    void ShowUpdateDetail();
    void StartUpgrade();
    void SkipOffer();
    void UnskipOffer();
    /// <summary>窗里那枚小 X（只有下载/解压那两档给取消，DEC-055）。</summary>
    void RequestCancelUpdate();
}
