namespace MultiLauncher;

// 模块注册表（B 段地基，DEC-024 / DEC-025）：strata / llama.cpp 两个可插拔模块的身份位。
// 启用位存在 Settings（EnableStrata / EnableLlama）；关闭 = **全隐身**——不扫描其模型、不探活、
// 不起进程，模型下拉 / 网关列表 / 设置节（以及 B 段界面重构后的主区页与状态灯）全部剔除。
// 能力清单（模式能力 / 主目录 / 内部端口 / 日志类型键 / 台账）在后续施工段用到时从这里取；
// ninfer 那一行已在 DEC-096 补上（形状 = 三个可开关的模块 + Ninfer 内部再分 WSL / WIN 两个后端）。
internal sealed record LauncherModule(
    string Key,                            // 内部标识（日志类型键、台账键；与 ModelEntry.Backend 同值）
    string DisplayName,                    // 界面显示名
    string Description,                    // 设置窗「模块」区的说明文字
    Func<Settings, bool> Enabled,          // 启用位读取
    Action<Settings, bool> SetEnabled);    // 启用位写入

internal static class ModuleRegistry
{
    // 模块顺序 = 界面展示顺序（设置窗「模块」区；以后的状态灯 / 页签也按它）
    internal static readonly LauncherModule Strata = new(
        Key: "strata", DisplayName: "Strata",
        Description: "内置 Strata 引擎模块：模型扫描、服务启停、网关路由。",
        Enabled: s => s.EnableStrata, SetEnabled: (s, v) => s.EnableStrata = v);

    internal static readonly LauncherModule Llama = new(
        Key: "llama", DisplayName: "llama.cpp",
        Description: "标准 llama-server：模型加载/卸载、WebUI。",
        Enabled: s => s.EnableLlama, SetEnabled: (s, v) => s.EnableLlama = v);

    // 第三个模块 = Ninfer（DEC-096）。显示名 **Ninfer**（i 不大写，用户 2026-10-05 定名）；
    // `ninfer`/`infer` 降成后端标签，界面上叫 WSL / WIN（"本机"这个叫法被他否了两次）。
    // 它这一层只有**模块开关**这一枚；两个后端各自的启用位在 Settings.NinferWinOn / NinferWslOn，
    // 长在设置页 Ninfer 节里（DEC-087 第④条），关掉哪侧那一侧就全隐身 —— 不参与这里这行的 Enabled。
    internal static readonly LauncherModule Ninfer = new(
        Key: "ninfer", DisplayName: "Ninfer",
        // DEC-110②：「按 model-id 的三件套、按需唤醒」两处按用户点名删掉（那一层的信息在 Ninfer 节与 docs/.ai/参考-界面与参数对照.md 里）。
        Description: "Ninfer 引擎模块：两条后端（WSL / WIN）。",
        Enabled: s => s.EnableNinfer, SetEnabled: (s, v) => s.EnableNinfer = v);

    internal static readonly LauncherModule[] All = { Strata, Llama, Ninfer };

    internal static bool StrataEnabled(Settings s) => s.EnableStrata;
    internal static bool LlamaEnabled(Settings s) => s.EnableLlama;
    internal static bool NinferEnabled(Settings s) => s.EnableNinfer;
    internal static bool AnyEnabled(Settings s) => StrataEnabled(s) || LlamaEnabled(s) || NinferEnabled(s);

    // ini 被手改成「全关」时的启动兜底（DEC-025：至少启用一个）：兜底启用 Strata（本项目主模块）。
    // 返回 true = 真的兜了底；调用方写一行日志明说。不额外回写 ini——下次正常保存自然带上。
    internal static bool EnsureAtLeastOne(Settings s)
    {
        if (AnyEnabled(s)) return false;
        s.EnableStrata = true;
        return true;
    }
}
