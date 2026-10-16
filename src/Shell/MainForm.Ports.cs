using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MultiLauncher;

// 端口：公开口/内部口的选择与全局端口冲突校验（DEC-024 A 段；BUG-023 的引信）。
// 2026-10-15 从 MainForm.Gateway.cs 整段搬出，一行未改。
public partial class MainForm
{
    // ---------------- 端口 ----------------

    int PublicPortValue()
        => int.TryParse(_txtPort.Text.Trim(), out var p) && p is >= 1 and <= 65535 ? p : 8080;

    // DEC-138（A-5）：监听地址只有一个家 = 卡1 那一只框（三页共用，`Settings.Host` 不跟模型走）。
    // 以前网关那一头直接摸控件、界面这一头也直接摸控件，控件名散在四处；收成一个访问器后，
    // 网关的绑址由 hooks 的 `BindHost` 给（各模块那一行点名），网关本体不再认识界面。
    string HostValue() => _txtHost.Text.Trim();

    // 内部端口：填了合法值就用它，其它一律回落到「公开端口 + 1」。
    // 等于公开端口必须挡住 —— 那会让壳的网关和 Strata 抢同一个端口（实测：python 直接 exit 2，
    // 客户端每秒重试 -> 每秒重启一次，日志刷成死循环）。自检直接测这个静态函数。
    internal static int NormalizeInternalPort(int publicPort, string? text)
        => int.TryParse((text ?? "").Trim(), out var p) && p is >= 1 and <= 65535 && p != publicPort ? p : publicPort + 1;

    int InternalPortValue() => NormalizeInternalPort(PublicPortValue(), _config.Current.InternalPort);

    // 把界面上的内部端口纠正成能用的值（勾「请求唤醒」、按「启动」时各调一次）。
    // 内部端口已移入设置页（DEC-011），所以这里改的是设置值并落盘。
    void FixInternalPort()
    {
        var pub = PublicPortValue();
        var raw = _config.Current.InternalPort.Trim();
        var want = NormalizeInternalPort(pub, raw);
        if (raw == want.ToString()) return;
        LogS($"内部端口「{raw}」不能用（不能等于公开端口 {pub}，且要在 1-65535）：已改为 {want}");
        _config.Current.InternalPort = want.ToString();
        _autoInternalPort = want.ToString();
        SaveConfig(commitParams: false);
    }

    // 服务实际监听的端口：本模块的网关开着就退到内部端口，否则就是公开端口
    //（第 0 轮：网关按模块各一份，所以这里问的是 **Strata 自己那一份**，不是"有没有任何网关在跑"）
    int ServicePortValue() => GatewayFor(ModelEntry.BackendStrata) is not null ? InternalPortValue() : PublicPortValue();

    int IdleStopMinutes() => int.TryParse(_config.Current.IdleStopMinutes.Trim(), out var m) && m > 0 ? m : 0;

    // ---------------- 全局端口冲突校验（DEC-024 A 段；BUG-023 的引信"保存不拦"就是在这里堵上） ----------------
    // 公开 / Strata 内部 / llama 内部（未来含 ninfer）两两相等即拦下保存，并说清撞谁；
    // 空值 = 自动（Strata 按公开+1、llama 按公开+2 并跳开），也按**有效值**参与比对。
    internal static string? PortConflictAtSave(int pub, string? strataText, string? llamaText)
    {
        strataText = (strataText ?? "").Trim();
        llamaText = (llamaText ?? "").Trim();
        int? strata = null, llama = null;
        if (strataText.Length > 0)
        {
            if (!int.TryParse(strataText, out var v) || v is < 1 or > 65535)
                return $"Strata 内部端口「{strataText}」不是 1-65535 的整数。";
            if (v == pub) return $"Strata 内部端口 {v} 不能等于公开端口 {pub}——公开端口归网关（统一入口），两个引擎各跑各自的内部分。";
            strata = v;
        }
        if (llamaText.Length > 0)
        {
            if (!int.TryParse(llamaText, out var v) || v is < 1 or > 65535)
                return $"llama 内部端口「{llamaText}」不是 1-65535 的整数。";
            if (v == pub) return $"llama 内部端口 {v} 不能等于公开端口 {pub}——公开端口归网关（统一入口），两个引擎各跑各自的内部分。";
            llama = v;
        }
        var strataEff = strata ?? pub + 1;          // 与 NormalizeInternalPort 同口径（留空 = 公开端口 + 1）
        if (llama is { } lv && lv == strataEff)
            return strata is { }
                ? $"llama 内部端口 {lv} 与 Strata 内部端口 {lv} 相同——两个引擎可能同时在跑，内部端口必须错开。"
                : $"llama 内部端口 {lv} 与 Strata 内部的自动值（公开端口 {pub} + 1 = {strataEff}）相同——两个引擎可能同时在跑，请错开（或把 Strata 内部端口也手填成别的值）。";
        return null;
    }

    // 设置窗保存前的调用口：公开端口与 llama 端口都是主界面**即时字段**（llama 的 2026-10-02 起），
    // 取界面上的实时值；Strata 内部端口仍是设置窗字段（取保存副本里的值）
    internal string? PortConflictAtSettingsSave(Settings s)
        => PortConflictAtSave(PublicPortValue(), s.InternalPort, _txtPortLlama.Text.Trim());

    // 公开端口是主界面的即时字段（没有"保存"动作）：与内部端口相同只提示、不拦；
    // 硬校验在设置窗保存那一关（PortConflictAtSettingsSave）。同一句话只说一次。
    string _portClashMemo = "";
    void WarnPortClash()
    {
        var pub = PublicPortValue();
        string warn = "";
        if (int.TryParse(_config.Current.InternalPort.Trim(), out var si) && si == pub)
            warn = $"公开端口 {pub} 与 Strata 的内部端口相同——网关和引擎不能共用端口，请到设置页把它改开后保存";
        else if (int.TryParse(_config.Current.LlamaInternalPort.Trim(), out var li) && li == pub)
            warn = $"公开端口 {pub} 与 llama 的内部端口相同——网关和引擎不能共用端口，请到设置页把它改开后保存";
        if (warn == _portClashMemo) return;
        _portClashMemo = warn;
        if (warn.Length > 0) Log("⚠ " + warn);
    }

    // 「内部端口（默认 = 公开端口 + 1）」：公开端口一变就跟着填；用户手改过之后不再覆盖
    void SyncInternalPort()
    {
        if (!int.TryParse(_txtPort.Text.Trim(), out var pub) || pub is < 1 or > 65535) return;
        var want = (pub + 1).ToString();
        var cur = _config.Current.InternalPort.Trim();
        if (cur.Length == 0 || cur == _autoInternalPort) _config.Current.InternalPort = want;
        _autoInternalPort = want;
    }

}
