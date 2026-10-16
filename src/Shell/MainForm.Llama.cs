using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MultiLauncher;

// llama.cpp 后端（M1 接上拉起/停止；DEC-079 起参数就地住在 llama_models.ini 的模型段里）。
// 与 Strata 侧（MainForm.Service.cs）对位：壳只管拉起进程，模型加载/卸载交给 llama.cpp 自己的 router
// （--models-preset 多模型按需加载 + --sleep-idle-seconds 空闲卸载；能力分工见 DEC-005）。
// 边界（DEC-079 改写 DEC-008）：--models-preset 直接给实跑目录里那份 llama_models.ini，
//   界面上改的就是它、引擎读的也是它（merged.ini 与「用原始 ini 启动」已退役）；
//   壳只写「选中模型那一段」里界面有格子的键，写之前先备份（见下面"家底就地读写"那一节）；
//   `D:\Strata` 照旧一个字都不写（最硬边界不动）。
public partial class MainForm
{
    Process? _llamaProcess;
    volatile bool _llamaExternal;       // llama 内部端口上探到"不是本壳拉起"的实例（ProbeExternal 维护）
    int _llamaActivePort;               // 壳拉起的 llama-server 实际监听端口（0 = 没跑）；「打开网页/端口行」用它
    DateTime _llamaStartedAt;           // 拉起时刻：退出时判断"刚起就退"给提示（多半是端口被占）

    bool LlamaRunning => _llamaProcess is { HasExited: false };

    // ---------------- 端口 ----------------
    // llama-server 的内部端口：留空/非法/与公开端口或 Strata 内部端口撞车时，回落到「公开端口 + 2」
    // （自动值先跳开 Strata 的内部端口）。自检直接测这个静态函数。
    internal static int NormalizeLlamaPort(int publicPort, int strataInternalPort, string? text)
    {
        var auto = publicPort + 2;
        if (auto == strataInternalPort) auto++;
        return int.TryParse((text ?? "").Trim(), out var p)
               && p is >= 1 and <= 65535 && p != publicPort && p != strataInternalPort
            ? p : auto;
    }

    // 在 NormalizeLlamaPort 之上再避开「**实际还占着**的端口」：网关/服务可能还跑在改设置前的旧端口上
    // （端口类设置都是"下次启动生效"）。llama 撞上会直接 bind 失败（用户实跑 2026-10-01：公开端口 8078→8077
    // 后网关仍占着 8078，而 llama 的内部端口设置值就是 8078——看着合法，一启就撞：llama-server 拉起即退出，
    // 「打开网页」于是跳到了网关）。撞了就按自动规则继续向上避让。
    internal static int LlamaPortChoose(int pub, int strataInternal, string? text, int busyGw, int busySvc)
    {
        var p = NormalizeLlamaPort(pub, strataInternal, text);
        if (p != busyGw && p != busySvc) return p;
        var auto = pub + 2;
        while (auto == pub || auto == strataInternal || auto == busyGw || auto == busySvc) auto++;
        return auto is >= 1 and <= 65535 ? auto : p;    // 极端端口号：不折腾，原样交回去让 llama 自己报错
    }

    int LlamaPortValue()
    {
        var p = LlamaPortChoose(PublicPortValue(), InternalPortValue(), _config.Current.LlamaInternalPort,
                                AnyGateway?.Port ?? 0, _serviceBindPort);
        // DEC-137（A-3）：挑完再对一遍"所有在听的网关"——多网关并存时 AnyGateway 只报第一枚，
        // 撞在另一枚网关上就是"看着合法、一启就 bind 失败"（2026-10-01 那条的同族，只是这次是第二个网关）。
        if (GatewayPortOn(p) != 0)
            p = LlamaPortChoose(PublicPortValue(), InternalPortValue(), p.ToString(), p, _serviceBindPort);
        return p;
    }

    // 内部端口"为什么是这个数"的说明（用户反馈 2026-10-01：改过内部端口后不知道 llama 为什么落到 8080）——
    // 四种来源：设置里填的 / 公开端口+2（自动）/ 自动值被 Strata 内部端口占走后避让 / 被网关或服务
    // 还没让出的旧端口占走（busyGw/busySvc 是"实际还占着"的一方，见 LlamaPortChoose）
    internal static string LlamaPortSourceText(int pub, int strataInternal, string? text, int busyGw, int busySvc, int actual)
    {
        var chosen = NormalizeLlamaPort(pub, strataInternal, text);
        if (actual == chosen)
        {
            if (int.TryParse((text ?? "").Trim(), out var p) && p is >= 1 and <= 65535 && p != pub && p != strataInternal)
                return $"来自设置页 llama.cpp Tab 的内部端口";
            var auto = pub + 2;
            if (auto == strataInternal)
                return $"自动（公开端口 {pub} + 2 = {auto} 被 Strata 内部端口 {strataInternal} 占用，避让为 {auto + 1}）";
            return $"自动（公开端口 {pub} + 2）";
        }
        var who = chosen == busyGw ? "网关" : "运行中的服务";
        return $"自动避让（{who}仍占着 {chosen}——改过的端口要下次启动才让出；本次用 {actual}）";
    }

    // ---------------- --models-preset：直接吃家底（DEC-079，merged.ini 已退役） ----------------
    // 取消融合层之后没有「合并」这回事了：引擎读的就是界面上改的那一份 llama_models.ini（exe 同目录）。
    // 所以这里只剩"在不在"这一件事；返回 null 时 error 是给用户看的原因。
    // （启动、加载、重载、「打开配置」四条路共用它，保证"用最新参数"是同一个判据。）
    string? ResolveLlamaPreset(Settings s, out string error)
    {
        error = "";
        var ini = RouterIniPath();
        if (!File.Exists(ini))
        {
            error = "找不到 llama_models.ini：" + ini + "\n\n" +
                    "llama 侧的模型与参数都住在这份文件里（把家底放到实跑目录即可，壳只搬不改）。";
            return null;
        }
        return ini;
    }

    // ---------------- 速度这一族的判据与句子（DEC-142，只点名、不改行为） ----------------
    // 现象：同一只模型、同 ctx、同一版引擎（b11530），壳这边解码 36~37 t/s，他另一只壳 95~108 t/s。
    // 根因不在壳的命令行，而在**这一族谁都没定过 parallel**：llama.cpp 的 auto 档（`--help` 写着
    // `--parallel N (default: -1, -1 = auto)`，源码 server.cpp 的 auto 分支 = `n_parallel = 4; kv_unified = true`）
    // 会把每一个实例摆成 4 槽 + 统一 KV，16 万 token 的 KV 在 24 GB 卡上摊 4 条序列装不下 ——
    // 实测那一档 预填充 74~77 t/s / 解码 36~37 t/s，`--parallel 1` 那档 808~1436 / 95~99（两份 A/B 报告：
    // `shots-m1/r25/ab-result.txt` 单体两趟一组、`shots-m1/r25/rt-result.txt` router 两趟一组）。
    // 他那一份家底每个段末尾写着 `parallel = 1`（`旧壳那份合并 ini`），
    // 我们这份没有，所以壳只输出一行提醒：要那个速度，段里写 `parallel = 1`，或卡2「附加参数」填
    // `--parallel 1`（实测 router 命令行那一发会继承到每个子实例 = r-np1 那趟的 args 里有 `--parallel 1`）。
    internal static string? LlamaParallelHintLine(bool cliHasParallel, bool iniHasParallel)
        => cliHasParallel || iniHasParallel ? null
         : "llama_models.ini 的各段与命令行都没写 parallel —— 引擎按 auto 起 = 4 槽 + 统一 KV，"
           + "本机实测比 parallel = 1 慢 2.6 倍解码、10 倍以上预填充。"
           + "要那个速度：段里写 `parallel = 1`，或卡2「附加参数」填 `--parallel 1`（壳不替你改家底）";

    // 家底里**任何一处**定过 parallel 就不提醒（`[*]` 与每一个段都算，大小写与两侧空格不计）
    internal static bool LlamaIniDefinesParallel(string presetPath)
    {
        try
        {
            if (!File.Exists(presetPath)) return false;
            if (LlamaIni.ReadGlobalValues(presetPath).Keys.Any(k => KeyIsParallel(k))) return true;
            return LlamaIni.ReadSectionsRaw(presetPath)
                        .Any(sec => sec.Values.Keys.Any(KeyIsParallel));
        }
        catch { return false; }        // 读不动就不提示（口径同 DEC-067"判不了就当没这条"）
    }
    static bool KeyIsParallel(string k) => string.Equals(k.Trim(), "parallel", StringComparison.OrdinalIgnoreCase);

    /// <summary>DEC-151：空闲卸载的分钟数（设置里按分钟 —— 与 Strata「空闲停服」/ Ninfer「空闲阈值」同一单位；唯一判据位 = 本函数）。
    /// 留空 / 非正数 = 0 = 不自动卸（不传旗标 = 引擎默认 -1 禁用），与另两模块"留空 = 不停"同语义。</summary>
    internal static int LlamaIdleMinutes(Settings s) =>
        int.TryParse(s.LlamaSleepIdleMinutes.Trim(), out var m) && m > 0 ? m : 0;

    // llama-server 的命令行：--host/--port/--models-preset 必带；--models-max 与 --sleep-idle-seconds 取设置，
    // 非法/留空就不传（用 llama-server 自己的默认：models-max=4，sleep=-1 禁用）。
    // DEC-151：空闲卸载在设置里按**分钟**（与 Strata「空闲停服」/ Ninfer「空闲阈值」同一单位），传旗标时 ×60 折成秒。
    // --models-dir 本轮不传：它不参与壳的模型扫描，传了会出现「下拉里没有、server 里能调」的模型（DEC-012）。
    // 末尾追加「附加参数」（DEC-075）：原样透传、按空白切分——与 Strata 侧的追加口径一字不差。
    // DEC-149（2026-10-11 实跑抓出）：命令行**必带 `--metrics`** —— 状态栏那一发
    //   `GET /metrics?model=…&autoload=false` 由 router 转给**子实例**，而子实例的 /metrics 有闸
    //   （`endpoint_metrics`，不带给 501「This server does not support metrics endpoint. Start it with `--metrics`」
    //   —— 他机器上实测到的就是这条：router 日志每 5 秒一行 `proxying request to model …`、状态栏跑动时全是短横）。
    //   router 命令行这一枚会**继承给每个子实例**（本机同版 b11530 实测：router PID + 子实例 PID 都带 --metrics、
    //   子实例 /metrics 直接 200 出全套 `llamacpp:*` 计数器），所以只加这一处即可，不必进家底、不进白名单。
    internal static List<string> BuildLlamaArgs(Settings s, string host, int port, string presetPath)
    {
        var a = new List<string>
        {
            "--host", host.Trim() is "" or "::" ? "127.0.0.1" : host.Trim(),
            "--port", port.ToString(),
            "--models-preset", presetPath,
            "--metrics",
        };
        if (int.TryParse(s.LlamaModelsMax.Trim(), out var max) && max >= 0)
        { a.Add("--models-max"); a.Add(max.ToString()); }
        // DEC-165②：通用那一枚 API Key（留空 = 不追加 = 引擎默认不鉴权）。走命令行而不是环境变量：
        // llama-server 两种都认（`LLAMA_API_KEY`），但这一族起进程那一步没有 env 注入的家，为它单开一条不值得；
        // 代价 = key 会出现在进程命令行里（本机工具，可接受），所以下面那行 `$ 命令行` 要过 RedactApiKey。
        if (s.ApiKey.Trim() is { Length: > 0 } apiKey) { a.Add("--api-key"); a.Add(apiKey); }
        // DEC-151：设置里按分钟（与另两模块同单位），这里 ×60 折算成引擎要的秒
        var idleMin = LlamaIdleMinutes(s);
        if (idleMin > 0)
        { a.Add("--sleep-idle-seconds"); a.Add((idleMin * 60).ToString()); }
        foreach (var extra in s.LlamaExtraArgs.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            a.Add(extra);
        return a;
    }

}
