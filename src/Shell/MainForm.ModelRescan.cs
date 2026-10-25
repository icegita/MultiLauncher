using System.Text;

namespace MultiLauncher;

// 模型列表的「家底戳 + 拉起服务时重扫」（DEC-222，2026-10-25 他报"删掉 llama.cpp 里某个模型的配置段，
// 重启服务之后模型列表里那件还在，只能退出程序再打开才行"）。
//
// 缺口在哪：三个模块的下拉都吃内存列表（_models / _llamaModels / _ninferModels），只有 RefreshModels() 会重扫，
// 而它的调用点被 DEC-033 收窄成"外壳启动 / SETUP 关闭 / 升级完成 / 设置页改了家底"——「停止 → 启动服务」不在名单里。
// DEC-211（Strata）与 DEC-212（llama）补的是**参数**那一半（拉起前比版本戳、重读界面卡）；模型**增删**那一半一直没补。
//
// 补法：拉起服务那一刻比一次**列表戳**，变了就只重扫**那一个模块**的列表 + 重摆下拉。
// 与 RefreshModels(onlyKey) 的区别只有一个，但那一个是硬要求：**不走它尾部那三段**
// （CollectSettings / RefreshModuleScopedUi / RefreshStatusUi）—— 那三段会把界面草稿收进去再整块重推一遍，
// 正是 DEC-033 当初要躲的"白扫一遍还刷一屏日志"。
//
// 三条边界（DEC-222 里定死，别顺手放开）：
//   · 删完**不重启服务**，列表照旧是旧的 —— 下拉展开不重扫是刻意的（MainForm.Card3.cs 那条注释），那是高频时点。
//   · 戳没变就**一个字节都不读**，也不打一行日志 —— 守住 DEC-033 / DEC-097⑤。
//   · 「已下载但未配置」那条提示不在本文件重算（它住在 RefreshModels 里），要等下一次全量刷新才更新。
public partial class MainForm
{
    // ---------------- 列表戳（与「参数戳」分别记） ----------------
    // 为什么非要分开：_llamaIniStamp / _modelParamsStamp 已经被"参数重读"那两条路吃掉了
    // （ApplyLlamaRecord 尾部会重记 _llamaIniStamp；ReloadModelSnapsIfChanged 内部会重记 _modelParamsStamp）。
    // 列表重扫若共用同一个戳，第一次参数重读就把它推到最新 → 列表重扫再也看不到变化。**别合回去**。
    static string _strataListStamp = "";
    static string _llamaListStamp = "";
    static string _ninferListStamp = "";

    /// <summary>自检用：把三个戳清成"未知"（下一趟调用必定当成"变了"）。</summary>
    internal static void ResetModelListStampsForTest()
    {
        _strataListStamp = "";
        _llamaListStamp = "";
        _ninferListStamp = "";
    }

    /// <summary>Strata 侧列表戳 = Root 下这一堆 <c>strata-*.json</c> 的「文件名 + 原件时刻 + 字节数」摘要。
    /// 只枚举目录、**不读文件**（读 GGUF 头是 ScanModels 的活，那才是 DEC-033 不肯在启停时扫的原因）。
    /// 目录不在 = 空串 = 判不了（口径同 ModelParamsStamp / LlamaIniStamp）；目录在但一个模型都没有 = "n:"（是个真戳：
    /// "把最后一个模型也删了"同样要跟上）。</summary>
    internal string StrataListStamp()
    {
        try
        {
            var root = _config.Current.Root;
            if (root.Length == 0 || !Directory.Exists(root)) return "";
            var sb = new StringBuilder("n:");
            foreach (var f in Directory.EnumerateFiles(root, "strata-*.json")
                                           .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(f);
                if (name.EndsWith(".shared-settings.json", StringComparison.OrdinalIgnoreCase)) continue;
                var fi = new FileInfo(f);
                sb.Append(name).Append('|').Append(fi.LastWriteTimeUtc.Ticks)
                  .Append('|').Append(fi.Length).Append(';');
            }
            return sb.ToString();
        }
        catch { return ""; }
    }

    /// <summary>llama 侧列表戳 = <c>llama_models.ini</c> 的「原件时刻 + 字节数」。</summary>
    internal string LlamaListStamp() => ListStampOfFile(RouterIniPath());

    /// <summary>Ninfer 侧列表戳 = <c>ninfer_models.ini</c> 的「原件时刻 + 字节数」。</summary>
    internal string NinferListStamp() => ListStampOfFile(NinferIniPath());

    /// <summary>文件不在给固定串「missing」而不是空串 —— 空串是"判不了"，而"整份文件被删掉"是一件**要跟上的变化**。</summary>
    static string ListStampOfFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return "missing";
            var fi = new FileInfo(path);
            return "f:" + fi.LastWriteTimeUtc.Ticks.ToString() + ":" + fi.Length.ToString();
        }
        catch { return ""; }
    }

    // ---------------- 重扫（只重扫那一个模块的列表） ----------------

    /// <summary>拉起服务那一刻调一次：这个模块的家底戳变了 → 只重扫它那一份列表 + 重摆下拉。
    /// 返回 true = 真的重扫过；false = 没动（戳没变 / 判不了 / 模块没启用）。
    /// 五个挂载点：<c>StartLlamaServer</c> / <c>StartServer</c>（路由模式只立网关那一支）/
    /// <c>StartService</c> / <c>StartNinferServer</c> / <c>StartNinferGateway</c>。
    /// 同一趟挂两处不冲突：第二处戳已是最新，直接返回 false。</summary>
    internal bool RescanModelsIfHomeChanged(string key)
    {
        if (key == LlamaKey) return RescanLlamaIfChanged();
        if (key == NinferKey) return RescanNinferIfChanged();
        if (key == StrataKey) return RescanStrataIfChanged();
        return false;
    }

    bool RescanStrataIfChanged()
    {
        if (!ModuleRegistry.StrataEnabled(_config.Current)) return false;
        var stamp = StrataListStamp();
        if (stamp.Length == 0 || stamp == _strataListStamp) return false;
        _strataListStamp = stamp;
        var before = _models.Select(m => m.Tag).ToList();
        _models = ScanModels(_config.Current.Root);
        LogS($"模型列表已按文件重扫：{_models.Count} 个 Strata 模型配置");
        LogRescanDiff(before, _models, LogS);
        ApplyRescanResult(StrataKey, LogS);
        return true;
    }

    bool RescanLlamaIfChanged()
    {
        if (!ModuleRegistry.LlamaEnabled(_config.Current)) return false;
        var stamp = LlamaListStamp();
        if (stamp.Length == 0 || stamp == _llamaListStamp) return false;
        _llamaListStamp = stamp;
        var before = _llamaModels.Select(m => m.Tag).ToList();
        _llamaModels = ScanLlamaModels(RouterIniPath());
        LogL($"模型列表已按文件重扫：{_llamaModels.Count} 个 llama.cpp 模型");
        LogRescanDiff(before, _llamaModels, LogL);
        ApplyRescanResult(LlamaKey, LogL);
        return true;
    }

    bool RescanNinferIfChanged()
    {
        if (!ModuleRegistry.NinferEnabled(_config.Current)) return false;
        var stamp = NinferListStamp();
        if (stamp.Length == 0 || stamp == _ninferListStamp) return false;
        _ninferListStamp = stamp;
        var before = _ninferModels.Select(m => m.Tag).ToList();
        _ninferModels = ScanNinferModels();
        LogN($"模型列表已按文件重扫：{_ninferModels.Count} 个 Ninfer 模型");
        LogRescanDiff(before, _ninferModels, LogN);
        ApplyRescanResult(NinferKey, LogN);
        return true;
    }

    /// <summary>重扫之后：只有这一个模块正是当前激活页时才重摆下拉（切页本来就会重摆，见 RefreshModuleScopedUiBody）；
    /// 并且**选中项被换掉了**（原来那件被删 → 落到第一件就绪的）才把卡2 参数跟着换一次 ——
    /// 少了这一步，界面会停在"已经删掉的那件"的记录上（他删完看着像没反应，就是这个形状）。</summary>
    void ApplyRescanResult(string key, Action<string> log)
    {
        if (!string.Equals(_activeModule, key, StringComparison.Ordinal)) return;
        var tagBefore = SelectedTag();
        RebuildModelList();
        var tagAfter = SelectedTag();
        if (tagAfter.Length == 0 || string.Equals(tagAfter, tagBefore, StringComparison.OrdinalIgnoreCase)) return;
        log($"  原来选的那一件不在了，已切到「{tagAfter}」");
        LoadKnobsFromModel(tagAfter);
        OnModelChanged();
        ShowAdvancedForActiveModule();
        RefreshStatusUi();
    }

    /// <summary>重扫前后差哪几件（只报"没了 / 新加了"；只改内容不增删的那种一句都不打）。</summary>
    static void LogRescanDiff(List<string> before, List<ModelEntry> after, Action<string> log)
    {
        var gone = before.Where(t => !after.Any(m => string.Equals(m.Tag, t, StringComparison.OrdinalIgnoreCase))).ToList();
        var added = after.Select(m => m.Tag)
                         .Where(t => !before.Contains(t, StringComparer.OrdinalIgnoreCase)).ToList();
        if (gone.Count > 0) log("  家底里已经没有了：" + string.Join("、", gone));
        if (added.Count > 0) log("  家底里新加了：" + string.Join("、", added));
    }
}
