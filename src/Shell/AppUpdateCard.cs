namespace MultiLauncher;

/// <summary>「本程序」那一面（DEC-202）：更新窗的 <see cref="IUpdatePane"/> 实现。
///
/// 它**不存状态**：第 4 条链的状态全在 `MainForm.AppUpdate.cs` 的字段上，这里只是取数口与转发口 ——
/// 与 <see cref="MainForm.ModuleView"/> 的做法一致（视图不算判据、不另起一套算法）。
/// 也**不进模块表**：模块表是引擎后端的能力面，进去会带出模块开关 / 状态灯 / 卡1 格子 / 设置节。
///
/// 窗上的三处身份由它自己报名：
///   · 标题栏 = `更新 · MultiLauncher 混合启动器`（<see cref="DisplayName"/>）
///   · 第一行上面那行小字 = `本程序`（<see cref="PrimarySectionTitle"/>）
///   · 没有第二段（<see cref="Secondary"/> = null，形状与 Strata / llama 那两扇一致）</summary>
internal sealed class AppUpdateCard : IUpdatePane
{
    readonly MainForm _m;

    internal AppUpdateCard(MainForm main) => _m = main;

    public string Key => MainForm.AppUpdateKey;
    public string DisplayName => "MultiLauncher 混合启动器";
    public string? PrimarySectionTitle => "本程序";
    public UpdateSection? Secondary => null;

    public UpdateOffer Offer() => _m.AppOfferForSettings();
    public (string Memo, Color Color) StatusMemo() => _m.AppUpdateStatusForSettings();
    public bool UpdateBusy() => _m.AppUpdateBusy;
    public bool UpdateOfferSeen() => _m.AppOfferSeen;
    public string CurrentVersion() => _m.AppVersionForSettings();

    public void CheckUpdateNow() => _m.CheckAppUpdate(force: true);
    public void ShowUpdateDetail() => _m.AppShowUpdateDetail();
    public void StartUpgrade() => _m.StartAppUpgrade();
    public void SkipOffer() => _m.AppSkip();
    public void UnskipOffer() => _m.AppUnskip();
    public void RequestCancelUpdate() => _m.RequestCancelAppUpdate();
}
