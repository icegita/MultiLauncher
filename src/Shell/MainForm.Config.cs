using System.Text;

namespace MultiLauncher;

// 配置读写：界面 <-> Settings 的收集/应用、INI 持久化（预设已于 2026-10-04 DEC-073 拆掉）。
// 壳只写自己的 MultiLauncher.ini（旧名 StrataLauncher.ini 首次运行一次性改名，DEC-037）；
// Strata 项目里的文件一律不改（运行配置另行物化，见 MainForm.Models.cs）。
public partial class MainForm
{
    string SelectedTag() => SelectedModel()?.Tag ?? "";

    // 启动模式（B 段：按模块各自记忆；切换入口是左栏那根模式条 _btnMode）。
    // Strata 落盘仍是 Settings.WakeOnRequest（ini 格式不变）；llama.cpp 落新字段 Settings.LlamaRouteMode。
    // 第 0 轮 DEC-094：这两格"读哪个字段 / 写哪个字段"都在模块表里（RouteMode / WriteRouteMode），这里只留别名给老调用点。
    bool StrataRouteMode => _routeModeStrata;
    bool LlamaRouteMode => _routeModeLlama;

    // 模式条的文字（按钮本身就是状态显示；显示激活模块自己的模式）。
    // DEC-090：它现在是 StackedToggle（当前枚 + 下方露边），所以这里给的是"两枚面 + 选中哪一枚"，
    // 而不是往按钮上写一句话。**动画只在用户点出来的那一次播**（程序化推值 = 启动/切页/设置页保存不该闪）。
    bool _animateModeBar;
    void RefreshModeLabel()
    {
        bool show = ActiveView.ModeBarShown;      // Ninfer 只留按需唤醒一档 → 整条模式条不出现（DEC-083 第 4 条）
        _btnMode.Visible = show;
        bool animate = _animateModeBar;
        _animateModeBar = false;
        if (!show) return;
        _btnMode.Faces = new[] { "路由模式", "单体模型" };
        _btnMode.Select(ActiveView.RouteMode() ? 0 : 1, animate);
    }

    // 点模式条：切当前激活模块的模式；Strata 侧立刻按新模式起/停网关（llama 侧只是记忆，影响之后的启动语义）
    void ToggleStartMode()
    {
        _animateModeBar = true;      // 这一次是用户点出来的 → 轮换动画要播（DEC-088）
        // 第 0 轮 DEC-094：翻哪一格、记哪一行日志、要不要立刻动网关，全在本模块那一行里
        ActiveView.ToggleRouteMode();
        // BUG-034（2026-10-04）：模式条翻的是「主窗参数」里的 WakeOnRequest / LlamaRouteMode —— 切完 ini 必须跟着变。
        // DEC-092 改法：**只把这两格当场提交**（原来这里是 CollectSettings→SaveConfig，等于顺手把界面上其他
        // 没点「保存」的改动一起落盘了）。这两格 Freeze=false，所以走"非提交式落盘"也不会被冻结表按回去。
        foreach (var v in _views) v.WriteRouteMode(_config.Current, v.RouteMode());
        SaveConfig(commitParams: false);
        MarkParamsSaved();      // 模式条自己就是落点：翻完即"已存"，按钮回灰（这两格已不进明细）
        // 起/停网关内部有好几条分支（服务在跑=>重启 / 没跑=>置停止态 / 失败=>弹窗），
        // 这里统一收口刷一遍：按钮/灯/状态卡/模型行文案在切模式后立即对齐（用户反馈"切模式后按钮未及时更新"）
        RefreshStatusUi();
    }

    string VisionValue() => _cboVision.Selected?.Tag as string ?? "gpu";

    void SetVision(string v) => _cboVision.SelectByTag(v);

    // 思考深度（2026-10-04；DEC-076 起首项是**空白**——用户口径"留空不就行了"）：空白 = 留空（不写共享设置文件）
    string ReasoningValue() => _cboReasoning.Selected?.Tag as string ?? "";
    void SetReasoning(string v) => SelectWithFallback(_cboReasoning, v);

    // 思考深度（llama.cpp，DEC-076）：合并 ini 的 reasoning-effort，改用下拉（空白 = 留空/不覆盖你 ini 的值）
    string LlamaReasoningValue() => _cboReasoningEffort.Selected?.Tag as string ?? "";
    void SetLlamaReasoning(string v) => SelectWithFallback(_cboReasoningEffort, v);

    // 思考深度（Ninfer，DEC-096）：--default-reasoning-effort；取值表按后端各一张（WSL = low/medium/xhigh，
    // WIN = default..max），**空白在首 = 一个字都不写**。界面与 Settings 只存那一档字符串。
    string NinferReasoningValue() => _cboNinferReasoning.Selected?.Tag as string ?? "";
    void SetNinferReasoning(string v) => SelectWithFallback(_cboNinferReasoning, v);

    // 按 tag 选中；老 ini 里若存过表外的值（以前是文本框，可能手打过别的），把它补成一项再选中——
    // 别静默丢/改用户存过的值（读不认就退回空白是"静默改"，不许）
    static void SelectWithFallback(DarkSelect sel, string v)
    {
        v = (v ?? "").Trim();
        if (sel.Items.Any(i => Equals(i.Tag, v))) { sel.SelectByTag(v); return; }
        var items = sel.Items.ToList();
        items.Add(new DarkItem { Text = v, Tag = v });
        sel.SetItems(items);
        sel.SelectByTag(v);
    }

    // 界面 -> Settings（B 段：卡2 通用行是"激活模块的视图"——只收集当前激活模块那一侧；
    // Strata 高级区与 llama 高级区各是独立控件，永远收集）
    Settings CollectSettings()
    {
        var s = _config.Current.Clone();
        s.Port = _txtPort.Text.Trim();
        s.Host = HostValue();          // DEC-138（A-5）：地址取值只有一个家（与网关绑址同一条算式）
        // 每模块的启动模式各写各的字段（第 0 轮 DEC-094：模块表给的）
        foreach (var v in _views) v.WriteRouteMode(s, v.RouteMode());
        // —— Strata 侧（独立控件）——
        s.Gpu = _txtGpu.Text.Trim();
        s.LayerSplit = _txtLayerSplit.Text.Trim();
        s.KvResident = _swKvRes.Checked;
        s.Spec = _txtSpec.Text.Trim();
        s.SpecMinP = _txtSpecMinP.Text.Trim();
        s.ExpertCache = _txtExpertCache.Text.Trim();
        s.Prefill = _txtPrefill.Text.Trim();
        s.Vision = VisionValue();
        s.VramReserveMib = _txtVramReserve.Text.Trim();
        s.ArenaPinGib = _txtArenaPin.Text.Trim();
        s.HeadLate = _swHeadLate.Checked;
        s.ReasoningEffort = ReasoningValue();
        // —— llama.cpp 侧（独立控件）——
        s.LlamaInternalPort = _txtPortLlama.Text.Trim();   // llama 页参数卡首行「监听端口」（2026-10-02 从设置页挪出、2026-10-04 DEC-074 从卡1 并入卡2，即时字段）
        s.LlamaTemperature = _txtTemperature.Text.Trim();
        s.LlamaTopP = _txtTopP.Text.Trim();
        s.LlamaTopK = _txtTopK.Text.Trim();
        s.LlamaMinP = _txtMinP.Text.Trim();
        s.LlamaPresencePenalty = _txtPresencePenalty.Text.Trim();
        s.LlamaRepeatPenalty = _txtRepeatPenalty.Text.Trim();
        s.LlamaSpecDraftNMax = _txtSpecDraftNMax.Text.Trim();
        s.LlamaSpecType = _cboSpecType.Selected?.Tag as string ?? "";      // DEC-145：下拉，空白 = 删键回继承
        s.LlamaMmprojOffload = _cboMmproj.Selected?.Tag as string ?? "";   // DEC-145：GPU = "true" / CPU = "false" / 空白 = ""
        s.LlamaReasoningEffort = LlamaReasoningValue();   // DEC-076：下拉，空白 = 留空
        s.LlamaFlashAttn = _swFlashAttn.Checked;          // DEC-079：flash-attn 两态开关（住 llama 高级区，跟别的独立控件一样永远收）
        // —— Ninfer 侧（DEC-096：独立控件，永远收集；界面上是载体，下次「保存」写回那一段的 args）——
        s.NinferPort = _txtPortNinfer.Text.Trim();                  // 卡1「监听端口」= 公开口（Ninfer 自己的，不复用全局 Port）
        s.NinferEffort = NinferReasoningValue();
        s.NinferPool = NinferPoolValue();               // 三档"同一格位两代各一只控件"的读口都在 Ninfer*Value() 里
        s.NinferPrefill = NinferPrefillValue();
        s.NinferHostKib = _cboNinferHostKib.Selected?.Tag as string ?? "";
        s.NinferSpec = _txtNinferSpec.Text.Trim();      // DEC-107：这两格是输入框了
        s.NinferDraft = NinferDraftValue();
        s.NinferMaxTokens = _txtNinferMaxTokens.Text.Trim();
        s.NinferTemperature = _txtNinferTemperature.Text.Trim();
        s.NinferTopP = _txtNinferTopP.Text.Trim();
        s.NinferTopK = _txtNinferTopK.Text.Trim();
        s.NinferPresence = _txtNinferPresence.Text.Trim();
        s.NinferConcurrency = _txtNinferConcurrency.Text.Trim();
        s.NinferSlots = _txtNinferSlots.Text.Trim();
        s.NinferPending = _txtNinferPending.Text.Trim();
        s.NinferPendingSec = _txtNinferPendingSec.Text.Trim();
        s.NinferLmHead = _swNinferLmHead.Checked;
        s.NinferVision = _swNinferVision.Checked;
        s.NinferPreserve = _swNinferPreserve.Checked;
        // —— 卡2 通用行（当前激活模块的视图）——
        ActiveView.CollectCard2(s);
        return s;
    }

    // commitBaseline=false（DEC-092）：这次推视图**不算把界面参数存住了**（设置页保存只提交设置页自己那些字段），
    // 所以只刷按钮、不重设基线 —— 否则主窗那枚「保存」会被按回灰，看着像已经落盘。
    void ApplySettings(Settings s, bool commitBaseline = true)
    {
        _applying = true;
        _routeModeStrata = s.WakeOnRequest;
        _routeModeLlama = s.LlamaRouteMode;
        RefreshModeLabel();
        _txtPort.Text = s.Port;          // 先设公开端口：TextChanged 会顺带把内部端口自动填成 +1
        _txtHost.Text = s.Host;
        // —— Strata 侧（独立控件）——
        _txtGpu.Text = s.Gpu;
        _txtLayerSplit.Text = s.LayerSplit;
        _swKvRes.Checked = s.KvResident;
        _txtSpec.Text = s.Spec;
        _txtSpecMinP.Text = s.SpecMinP;
        _txtExpertCache.Text = s.ExpertCache;
        _txtPrefill.Text = s.Prefill;
        SetVision(s.Vision);
        SetReasoning(s.ReasoningEffort);
        _txtVramReserve.Text = s.VramReserveMib;
        _txtArenaPin.Text = s.ArenaPinGib;
        _swHeadLate.Checked = s.HeadLate;
        // —— llama.cpp 侧（独立控件）——
        _txtPortLlama.Text = s.LlamaInternalPort;
        _txtTemperature.Text = s.LlamaTemperature;
        _txtTopP.Text = s.LlamaTopP;
        _txtTopK.Text = s.LlamaTopK;
        _txtMinP.Text = s.LlamaMinP;
        _txtPresencePenalty.Text = s.LlamaPresencePenalty;
        _txtRepeatPenalty.Text = s.LlamaRepeatPenalty;
        _txtSpecDraftNMax.Text = s.LlamaSpecDraftNMax;
        SelectWithFallback(_cboSpecType, s.LlamaSpecType);      // DEC-145：两枚新下拉（表外值补项再选中，不静默丢）
        SelectWithFallback(_cboMmproj, s.LlamaMmprojOffload);
        SetLlamaReasoning(s.LlamaReasoningEffort);
        _swFlashAttn.Checked = s.LlamaFlashAttn;      // DEC-079：flash-attn 两态（程序化推值也走 ToggleSwitch 的事件，值真变才发）
        // —— Ninfer 侧（DEC-096）——
        _txtPortNinfer.Text = s.NinferPort;
        SetNinferReasoning(s.NinferEffort);
        _txtNinferPoolWsl.Text = s.NinferPool;
        _txtNinferPrefillWsl.Text = s.NinferPrefill;
        _txtNinferDraftWsl.Text = s.NinferDraft;
        SelectWithFallback(_cboNinferPool, s.NinferPool);
        SelectWithFallback(_cboNinferPrefill, s.NinferPrefill);
        SelectWithFallback(_cboNinferHostKib, s.NinferHostKib);
        _txtNinferDraft.Text = s.NinferDraft;           // DEC-107：投机深度 / 投机方式改输入框（原下拉的项表逻辑退役）
        _txtNinferSpec.Text = s.NinferSpec;
        _txtNinferMaxTokens.Text = s.NinferMaxTokens;
        _txtNinferTemperature.Text = s.NinferTemperature;
        _txtNinferTopP.Text = s.NinferTopP;
        _txtNinferTopK.Text = s.NinferTopK;
        _txtNinferPresence.Text = s.NinferPresence;
        _txtNinferConcurrency.Text = s.NinferConcurrency;
        _txtNinferSlots.Text = s.NinferSlots;
        _txtNinferPending.Text = s.NinferPending;
        _txtNinferPendingSec.Text = s.NinferPendingSec;
        _swNinferLmHead.Checked = s.NinferLmHead;
        _swNinferVision.Checked = s.NinferVision;
        _swNinferPreserve.Checked = s.NinferPreserve;
        RefreshNinferBackendUi();       // 方块的两枚面 / 项表 / 哪些格子出面，都跟着当前后端走
        // —— 卡2 视图 + 选中恢复（B 段）——
        var tag = ActiveView.TagOf(s);
        SelectModel(tag);
        PushCard2ForActiveModule(s);
        _applying = false;
        RefreshStatusUi();      // 灯/PID/端口/按钮随激活模块刷新（SelectModel 是恢复路径，不走选中事件）
        if (commitBaseline) MarkParamsSaved();     // 推视图即「已保存」基线（启动 / 换模型都经过这里；DEC-074）
        else RefreshSaveButton();                  // 设置页保存这一路：改动没落盘，按钮该亮就继续亮（DEC-092）
    }

    void SelectModel(string tag)
    {
        if (tag.Length == 0) return;
        // 模型下拉的条目 Tag 是 ModelEntry 对象（KV/视觉/思考深度那几个下拉才是字符串 Tag），
        // 按字符串找时得先把对象捞出来 —— 直接把字符串传给 SelectByTag 永远匹配不上（BUG-014）
        var hit = _cboModel.Items.Select(i => i.Tag).OfType<ModelEntry>()
            .FirstOrDefault(m => m.Tag == tag);
        if (hit is not null) _cboModel.SelectByTag(hit);
    }

}
