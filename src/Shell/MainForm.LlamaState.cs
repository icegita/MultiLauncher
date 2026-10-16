using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MultiLauncher;

// llama 的界面状态与模型加载状态：段级参数视图、/models 轮询解析、DEC-128⑥ 跟页。
// 2026-10-15 从 MainForm.Llama.cs 整段搬出，一行未改。
public partial class MainForm
{
    // ---------------- llama 段级参数的界面状态（DEC-079） ----------------
    // 取消融合层之后，界面上这些格子就是 llama_models.ini「选中那一段」的视图：
    //   读 = 段 → `[*]`/文件头 → 引擎默认；写 = 只写变过的键，且只写那一段。
    // 这两份状态由 ApplyLlamaRecord（按模型取家底）填，「保存」按它们判「哪些格子真的变了」。
    // _llamaEffective 是**生效值**（含继承）而不是"段里写了什么"——这正是「继承不摊平」的支点：
    //   新值等于生效值就一个字不动，免得"打开看看、点保存"把继承摊成显式值。
    Dictionary<string, string> _llamaEffective = new(StringComparer.OrdinalIgnoreCase);
    bool _llamaHasMmproj;   // DEC-145：「视觉」格的可用性 —— 这一段有没有 mmproj 路径（RefreshLlamaInheritanceTips 用它置灰）
    HashSet<string> _llamaInherited = new(StringComparer.OrdinalIgnoreCase);
    string _llamaLoadedTag = "";      // 上面两份状态是按哪一段填的（切页/重扫后选中段变了就要补载一次，见 EnsureLlamaRecordLoaded）

    // ---------------- 模型加载状态（llama.cpp router 的管理端点 /models） ----------------
    // 用户规格（2026-10-01）：路由模式「启动」只起服务不加载模型；「加载」= 加载选中模型（服务没起先起）；
    // 「卸载」= 只卸模型不停服务；单体模式「启动」连模型一起起、模型行按钮变「重载」（按当前界面参数重新加载）。
    readonly HashSet<string> _llamaLoaded = new(StringComparer.OrdinalIgnoreCase);  // status 为 loading/loaded
    readonly HashSet<string> _llamaFailed = new(StringComparer.OrdinalIgnoreCase);  // status 为 failed
    volatile string _llamaOp = "";                                                  // 忙态文案（"加载中…"等；空 = 闲）

    // /models 里按 status.value 归类。自检直接喂假 JSON。id = preset 的 section 名（与界面下拉同名）。
    internal static void ParseModelStates(string? json, ISet<string> loaded, ISet<string> failed)
    {
        loaded.Clear();
        failed.Clear();
        try
        {
            if (string.IsNullOrEmpty(json) || json.Length > 5_000_000) return;
            if (JsonNode.Parse(json) is not JsonObject o || o["data"] is not JsonArray arr) return;
            foreach (var it in arr)
            {
                var name = it?["id"]?.GetValue<string>() ?? "";
                var st = it?["status"]?["value"]?.GetValue<string>() ?? "";
                if (name.Length == 0) continue;
                if (st is "loading" or "loaded") loaded.Add(name);
                else if (st == "failed") failed.Add(name);
            }
        }
        catch { }
    }

    // 拉一次 /models 更新状态缓存并刷新界面（壳的或外部的 llama-server 都查；没服务就清空）
    async Task<bool> RefreshLlamaStatesAsync()
    {
        if (!LlamaRunning && !_llamaExternal)
        {
            if (_llamaLoaded.Count > 0 || _llamaFailed.Count > 0) { _llamaLoaded.Clear(); _llamaFailed.Clear(); RefreshStatusUi(); }
            return false;
        }
        try
        {
            using var resp = await ProbeGetAsync($"http://{ProbeHost()}:{LlamaPortValue()}/models");
            if (!resp.IsSuccessStatusCode) return false;
            var body = await resp.Content.ReadAsStringAsync();
            if (IsDisposed) return true;
            // DEC-125② + DEC-128⑥：集合更新、统计清零与"真被加载就跟页"都在下面那一条里
            //（真轮询与自检喂样本走同一条路 —— 上一条同族的账就是"调用点没人管"）。
            ApplyLlamaModelStates(body, LlamaRunning);
            RefreshStatusUi();
            return true;
        }
        catch { return false; }
    }

    // ---------------- DEC-128⑥：llama 的"真被加载 → 跟页" ----------------
    // 他实跑点名「自动切换到运行中 tab」在 llama 上不工作。为什么以前没有：Strata / Ninfer 的唤醒都走壳网关
    //（请求进来 → 壳去拉起，拉起来它就知道），而 llama 的模型是 llama-server **自己**按需加载的，
    // 壳只能从这份 /models 轮询里"看到" —— 这条线当时没人接（检查过：全代码里 Follow 那一族的调用点只有网关一处）。
    // 判据与 DEC-125① 同一套语义：**真被加载的那一枪才跟**（集合里新增了 tag）；壳自己在忙
    //（_llamaOp 非空 = 手动加载/卸载/重载、单体模式启动连模型起 —— 那时用户本来就停在这一页）不跟；
    // 壳外起的实例不跟（llamaRunning = false）。跟页本身（窗口不在眼前只记待办、开窗落地前复核灯还是绿的）
    // 全在 FollowModuleAfterWake 那一处，别在这里长第二套。
    // DEC-155：多件同帧出现时跟**最后一个**（他定的"4.如果多个,跟随最后一个"）—— 同帧多件只在
    // llama 路由 `models-max > 1` + ini 里配了多个 `load-on-startup` 那种场合出现，默认 1 件时无歧义。
    internal static string? LlamaLoadToFollow(IReadOnlyCollection<string> added, bool llamaRunning, bool shellBusy)
        => added.Count > 0 && llamaRunning && !shellBusy ? added.Last() : null;

    int _llamaFollowCalls;      // 证人位：证明"喂一份报文真会走到跟页那一枪"（这条线曾经缺过，别再写空判据）
    internal int LlamaFollowCallsForTest => _llamaFollowCalls;

    /// <summary>吃一份 /models 报文后的全部动作：更新集合 → 集合一变就清统计（DEC-125②：换件/全卸完 <c>ResetLlamaStats</c>）
    /// → 判定要不要跟页。真轮询与自检喂样本**走同一条**。</summary>
    internal void ApplyLlamaModelStates(string json, bool llamaRunning)
    {
        var loadedBefore = new HashSet<string>(_llamaLoaded, StringComparer.OrdinalIgnoreCase);
        ParseModelStates(json, _llamaLoaded, _llamaFailed);
        if (!loadedBefore.SetEquals(_llamaLoaded)) ResetLlamaStats();
        var added = _llamaLoaded.Where(t => !loadedBefore.Contains(t)).ToList();
        var follow = LlamaLoadToFollow(added, llamaRunning, _llamaOp.Length > 0);
        if (follow is null) return;
        _llamaFollowCalls++;
        // DEC-155：把"要跟的那一件"一起交出去（原来只当布尔用、写死传 "llama"，跟不到模型那一半）
        UiPost(() => FollowModuleAfterWake(LlamaKey, follow));
    }

    // 自检出口：摆 / 读 _llamaOp（"壳自己在忙就不跟"那半条要能被真走到，不能只测纯函数）。
    // 用方法不用属性 —— WinForms 分析器 WFO1000 会把可写属性当设计期序列化成员拦下。
    internal string GetLlamaOpForTest() => _llamaOp;
    internal void SetLlamaOpForTest(string s) => _llamaOp = s;

    async Task<string?> PostLlamaAsync(string path, string json)
    {
        try
        {
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var resp = await _probe.PostAsync($"http://{ProbeHost()}:{LlamaPortValue()}{path}", content);
            return await resp.Content.ReadAsStringAsync();
        }
        catch { return null; }
    }

    // 让 router 重读 preset 源（合并 ini 重写后调用；参数没变的模型不会被卸）
    async Task ReloadLlamaPresetAsync()
    {
        try { using var _ = await ProbeGetAsync($"http://{ProbeHost()}:{LlamaPortValue()}/models?reload=1"); }
        catch { /* 重读失败不致命，后面的加载会用旧版 preset */ }
    }

    // 等 llama-server 就绪（/health 200）。返回 false = 超时或进程没了。
    async Task<bool> WaitLlamaReadyAsync(int timeoutMs = 30000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (IsDisposed || (!LlamaRunning && !_llamaExternal)) return false;
            try
            {
                using var resp = await ProbeGetAsync($"http://{ProbeHost()}:{LlamaPortValue()}/health");
                if (resp.IsSuccessStatusCode) return true;
            }
            catch { /* 还没监听 */ }
            try { await Task.Delay(500); } catch { return false; }
        }
        return false;
    }

    // 确保 llama-server 在跑并已就绪（没有就拉起）
    async Task<bool> EnsureLlamaReadyAsync()
    {
        if (!LlamaRunning && !_llamaExternal) StartLlamaServer();
        if (!LlamaRunning && !_llamaExternal) return false;
        return await WaitLlamaReadyAsync();
    }

    // 轮询某模型到"已加载 / 未加载 / 失败"；返回终态（"loaded"/"unloaded"/"failed"/"" 超时）
    async Task<string> WaitLlamaModelStateAsync(string name, bool wantLoaded, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (IsDisposed || (!LlamaRunning && !_llamaExternal)) return "";
            await RefreshLlamaStatesAsync();
            if (_llamaFailed.Contains(name)) return "failed";
            if (_llamaLoaded.Contains(name) == wantLoaded) return wantLoaded ? "loaded" : "unloaded";
            try { await Task.Delay(1500); } catch { return ""; }
        }
        return "";
    }

    // 等"除 name 之外的已加载模型"都卸掉（models-max 一般是 1，加载前先腾位）
    async Task WaitOtherModelsGoneAsync(string name, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (_llamaLoaded.Any(x => !string.Equals(x, name, StringComparison.OrdinalIgnoreCase))
               && sw.ElapsedMilliseconds < timeoutMs)
        {
            if (IsDisposed || (!LlamaRunning && !_llamaExternal)) return;
            try { await Task.Delay(1000); } catch { return; }
            await RefreshLlamaStatesAsync();
        }
    }

    // 模型行按钮（选中 llama 模型）：路由模式 = 加载/卸载；单体模式 = 重载（按当前界面参数重新加载）。
    // DEC-141：路由模式下**只要加载着东西就卸那些**（不再要求下拉正指在已加载的那件上 —— 那样跑着 A 选 B 时
    // 按钮写「加载」，显存占着却卸不掉）。卸的对象 = UnloadTargetsForLlama 算出来的那一份。
    void LlamaModelAction()
    {
        var m = SelectedModel();
        if (m is null || _llamaOp.Length > 0) return;
        if (!_routeModeLlama) { ReloadLlamaModel(m.Tag); return; }
        var targets = UnloadTargetsForLlama(_llamaLoaded, m.Tag);
        if (targets.Count > 0) UnloadLlamaModels(targets);
        else LoadLlamaModel(m.Tag);
    }

    // 「卸载」这一击要卸哪些件（DEC-141，纯函数 = 自检可穷举）：加载集合里的那些，
    // 下拉恰好指着的那件排最前（他选中的就是已加载那件时，行为与从前一字相同）。集合空 = 空名单，
    // 调用方据此改走「加载」。
    internal static List<string> UnloadTargetsForLlama(IEnumerable<string> loaded, string? selectedTag)
    {
        var list = loaded.Where(t => !string.IsNullOrEmpty(t))
                         .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var hit = list.FirstOrDefault(t => string.Equals(t, selectedTag, StringComparison.OrdinalIgnoreCase));
        if (hit is not null) { list.Remove(hit); list.Insert(0, hit); }
        return list;
    }

    // 加载：确保服务在跑 → 用最新界面参数重生成合并 ini 并让 router 重读 → 卸掉别的 → 加载选中模型
    async void LoadLlamaModel(string name)
    {
        if (_llamaOp.Length > 0) return;
        // DEC-157（他批的第 1 条）：手动「加载」= 人点按钮那一路 → **当场拒绝并点名**（分入口口径，DEC-083 第 6 条）。
        // 注意这一族**路由模式内部**由它自己的 router 按需加载子实例，那一发没有壳的入口、拦不住（登记在诊断文档 §1）；
        // 拦得住的是这里（壳发的 /models/load）与下面单体模式那一下连模型一起起。
        if (GpuMutexRefusal(LlamaKey) is { } held)
        {
            FailLlama("没有加载：" + name, held);
            return;
        }
        _llamaOp = "加载中…";
        RefreshStatusUi();
        try
        {
            if (!await EnsureLlamaReadyAsync()) { LogL($"加载 {name} 失败：llama-server 没起来（看日志）"); return; }
            var s = CollectSettings();
            // DEC-079：家底就是引擎读的那一份，没有合并产物了；但**加载前一定要让 router 重读一次文件**，
            // 不然界面上刚「保存」进段里的参数还停在服务上次读进去的旧值上。
            if (ResolveLlamaPreset(s, out var perr) is null) { LogL("加载中止：" + perr); return; }
            await ReloadLlamaPresetAsync();
            foreach (var other in _llamaLoaded.Where(x => !string.Equals(x, name, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                LogL($"$ 先卸载其它模型：{other}");
                await PostLlamaAsync("/models/unload", "{\"model\":" + JsonSerializer.Serialize(other) + "}");
            }
            if (_llamaLoaded.Any(x => !string.Equals(x, name, StringComparison.OrdinalIgnoreCase)))
                await WaitOtherModelsGoneAsync(name, 20000);
            LogL($"$ 加载模型 {name} …");
            if (await PostLlamaAsync("/models/load", "{\"model\":" + JsonSerializer.Serialize(name) + "}") is null)
            { LogL("加载请求失败（连不上 llama-server）"); return; }
            var st = await WaitLlamaModelStateAsync(name, wantLoaded: true, 480000);
            LogL(st switch
            {
                "loaded" => $"模型 {name} 已加载",
                "failed" => $"模型 {name} 加载失败（看 llama-server 的输出）",
                _ => $"模型 {name} 仍在加载（等超时了，可稍后看日志）",
            });
        }
        finally { _llamaOp = ""; await RefreshLlamaStatesAsync(); if (!IsDisposed) RefreshStatusUi(); }
    }

    // 卸载：只卸模型、不停服务。DEC-141 起收一份**名单**（跑着别的那件、下拉没指过去时也要卸得掉）；
    // 只有一件时日志那两句与升级前逐字节相同。
    async void UnloadLlamaModels(IReadOnlyList<string> names)
    {
        if (_llamaOp.Length > 0 || names.Count == 0) return;
        _llamaOp = "卸载中…";
        RefreshStatusUi();
        try
        {
            if (!LlamaRunning && !_llamaExternal) { LogL("llama-server 没在跑，没什么可卸载的"); return; }
            foreach (var name in names)
            {
                LogL($"$ 卸载模型 {name}（服务保留）…");
                if (await PostLlamaAsync("/models/unload", "{\"model\":" + JsonSerializer.Serialize(name) + "}") is null)
                { LogL("卸载请求失败（连不上 llama-server）"); return; }
                var st = await WaitLlamaModelStateAsync(name, wantLoaded: false, 90000);
                LogL(st == "unloaded" ? $"模型 {name} 已卸载（服务保留）" : $"模型 {name} 卸载超时（看日志）");
            }
        }
        finally { _llamaOp = ""; await RefreshLlamaStatesAsync(); if (!IsDisposed) RefreshStatusUi(); }
    }

    // 重载（单体模式）：按当前界面参数重生成合并 ini + router 重读 → 卸载选中模型 → 重新加载
    async void ReloadLlamaModel(string name)
    {
        if (_llamaOp.Length > 0) return;
        _llamaOp = "重载中…";
        RefreshStatusUi();
        try
        {
            if (!await EnsureLlamaReadyAsync()) { LogL($"重载 {name} 失败：llama-server 没起来（看日志）"); return; }
            var s = CollectSettings();
            // DEC-079：同「加载」——段是就地改的，服务内存里还是启动时读进去的那份，重载前先让 router 重读文件
            if (ResolveLlamaPreset(s, out var perr) is null) { LogL("重载中止：" + perr); return; }
            await ReloadLlamaPresetAsync();
            if (_llamaLoaded.Contains(name))
            {
                await PostLlamaAsync("/models/unload", "{\"model\":" + JsonSerializer.Serialize(name) + "}");
                await WaitLlamaModelStateAsync(name, wantLoaded: false, 90000);
            }
            LogL($"$ 重载模型 {name}（按当前界面参数）…");
            if (await PostLlamaAsync("/models/load", "{\"model\":" + JsonSerializer.Serialize(name) + "}") is null)
            { LogL("重载请求失败（连不上 llama-server）"); return; }
            var st = await WaitLlamaModelStateAsync(name, wantLoaded: true, 480000);
            LogL(st switch
            {
                "loaded" => $"模型 {name} 已重载",
                "failed" => $"模型 {name} 重载失败（看 llama-server 的输出）",
                _ => $"模型 {name} 仍在重载（等超时了，可稍后看日志）",
            });
        }
        finally { _llamaOp = ""; await RefreshLlamaStatesAsync(); if (!IsDisposed) RefreshStatusUi(); }
    }

}
