namespace MultiLauncher;

// B 段地基（DEC-024 / DEC-025）：模块开关的保存校验与自检出口。
// 全隐身语义点分散在各自归属的文件里（扫描见 MainForm.Models.cs / 探活见 MainForm.Service.cs /
// 起停见 Service.Gateway.Llama 三处 / 设置节显隐见 SettingsForm.cs），统一只问 ModuleRegistry 要"开没开"。
public partial class MainForm
{
    // 设置窗「保存」前的模块开关校验：① 至少启用一个；② 被关闭的模块服务还在跑 → 拦下先停。
    // 第 0 轮（DEC-094）：签名从四元组 (bool,bool,bool,bool) 改成**一条批复一个模块**的集合 ——
    // 两模块时代那份四元组加一个模块就要加两个 bool，而且哪一支配哪一支全靠调用点顺序，出错不会响。
    // 纯函数版给自检穷举（busy 只对"正在被关闭"的模块生效）；实例版从模块表把每支取出来。
    internal sealed record SwitchReply(string Name, bool TurningOff, bool Busy, string BusyMessage);

    internal static string? ModuleSwitchAtSave(IReadOnlyList<SwitchReply> replies)
    {
        if (replies.Count > 0 && replies.All(r => r.TurningOff))
            return "至少要启用一个模块（" + string.Join(" / ", replies.Select(r => r.Name)) + "）。" +
                   "关闭模块 = 全隐身：不扫描它的模型、不探活、不起服务，界面里也不出现。";
        foreach (var r in replies)
            if (r.TurningOff && r.Busy) return r.BusyMessage;
        return null;
    }

    /// 按界面上的模块开关拼一份批复（自检也用它，免得测试里再手写一套模块名）
    internal List<SwitchReply> SwitchRepliesForSave(Settings s)
    {
        var list = new List<SwitchReply>();
        foreach (var v in _views)
        {
            bool off = !v.EnabledIn(s);
            // 「在跑」只看壳自己拉起来的：进程 + 网关。外部探到的实例不算（不是壳在管的，关模块只让它隐身）
            list.Add(new SwitchReply(v.DisplayName, off, off && v.ShellBusy(), v.SwitchOffMessage));
        }
        return list;
    }

    internal string? ModuleSwitchAtSave(Settings s) => ModuleSwitchAtSave(SwitchRepliesForSave(s));

    // ---------------- 自检出口 ----------------

    // 改某个模块的启用位并重扫（只改内存、不落盘）；用例跑完记得恢复 true
    internal void SetModuleEnabledForTest(string key, bool on)
    {
        ModuleRegistry.All.First(m => m.Key == key).SetEnabled(_config.Current, on);
        RefreshModels();
    }
    // 老写法保留给"两个都要表态"的用例（第 0 轮之后这两个 bool 就是 (key, on) 两次调用）。
    // DEC-096：两次调用之间**不刷新** —— 否则第一步（把 Strata 关掉、llama 还没开回来）会让
    // EnsureActiveModuleValid 在"只剩 Ninfer"的中间态上把激活页定成 ninfer，第二步再开 llama 也不会顶回来，
    // 用例就测不到它想测的那一句（"停用 Strata 后落回还启用的那一页"）。
    internal void SetModuleEnabledForTest(bool strata, bool llama)
    {
        ModuleRegistry.All.First(m => m.Key == StrataKey).SetEnabled(_config.Current, strata);
        ModuleRegistry.All.First(m => m.Key == LlamaKey).SetEnabled(_config.Current, llama);
        RefreshModels();
    }

    internal List<string> ModelDropHeadersForTest =>
        _cboModel.Items.Where(i => i.Kind == DarkItemKind.Header).Select(i => i.Text).ToList();

    // ---------------- B 段：激活模块页 / 视图推拉 / 状态灯 ----------------

    // 第 0 轮（DEC-094）：这里的 `internal bool ActiveIsStrata` 已删除 —— 界面上"这个模块怎么呈现"的判断
    // 全部收进 MainForm.ModuleViews.cs 那张表（ActiveView.PortBox / StatusText / ToggleService …）。
    // 加一个模块 = 那张表加一行，别再长出第二处二元判断。
    string _activeModule = "strata";    // 初值 = 模块表第一行；启动时按 ini 恢复（Program 构造函数），切页由 SwitchModule 改
    internal string ActiveModuleKey => _activeModule;

    List<string> EnabledModuleKeys() => ModuleRegistry.All.Where(m => m.Enabled(_config.Current)).Select(m => m.Key).ToList();

    // 当前激活模块失效（被停用）时落回第一个启用的模块
    void EnsureActiveModuleValid()
    {
        var keys = EnabledModuleKeys();
        if (keys.Count == 0) return;    // 理论不可达：至少启用一个由保存校验 + 启动兜底保证
        if (!keys.Contains(_activeModule)) _activeModule = keys[0];
        _config.Current.ActiveModule = _activeModule;
    }

    // 切模块页（B 段，DEC-025）：切页只是换视角、不动任何服务——先把当前页的编辑收进 _config.Current（内存），再换视图。
    // DEC-092 第 2 条：**这里不落盘，也不再把按钮按回灰**。原来这两步都在，于是"没点保存的改动"会被切页顺手存进
    // MultiLauncher.ini（用户实测：切页回来还在、重启也还在 = 其实已经保存了）。内存留痕照旧（来回切不丢），
    // 但"存住了"只能由「保存」（或关窗选「写进去」）造成。ActiveModule 不是参数格，跟着下一次落盘走。
    void SwitchModule(string key)
    {
        if (key == _activeModule) return;
        if (!EnabledModuleKeys().Contains(key)) return;
        SettleNinferPort("切页");      // DEC-091 第三条收口：切页也会 CollectSettings，端口那格先结算再收
        _config.Current = CollectSettings();
        _activeModule = key;
        _config.Current.ActiveModule = key;
        // ⑥（2026-10-03 用户反馈）：切页只是换视角，不该在日志里留痕——
        // 原来每切一次就一行「已切到 … 模块页（切页不动服务）」，把服务日志冲淡了。
        // 按住重绘的范围要盖住**整次切换**（视图 + 日志视图 + 保存按钮），只盖住视图那一段的话，
        // 放开的下一瞬 RebuildLogView 又会画一遍 —— 还是两次可见帧。HoldRepaint 是计数的，嵌套安全。
        HoldRepaint();
        try
        {
            RefreshModuleScopedUi();
            RebuildLogView();       // 本页日志视图（本模块行 + 启动器行；未激活页的历史照收）
            RefreshSaveButton();    // 切页不收口：还有没写进文件的改动，按钮就该继续亮着（DEC-092）
        }
        finally { ReleaseRepaint(); }
    }

    // 激活模块相关的整块视图刷新（页头分段 / 卡2 / 卡3 / 左栏行集 / 状态灯）
    // 第 0 轮：这里不再问"是不是 Strata"，只问 ActiveView 要东西（分段项与灯行集都从模块表来）。
    //
    // 2026-10-16（他点名"切换时画面闪烁，也要修复"）：整块换视图**按住重绘**做完再一次性放出来。
    // 闪烁的成因不是画得慢，而是这一串里有**多个可见的中间态**：卡1 的格子先整批挪去画外藏起来、
    // 卡2 重排、卡3 重建模型列表、日志整块重渲染 —— 每一步都把主区画一遍，肉眼读作"闪一下再落定"。
    // 用现成的 `SendMessage`（MainForm.LogScroll.cs 早就 P/Invoke 了它）+ WM_SETREDRAW，不新增 interop。
    // 只在句柄已建好且窗体真在显示时生效：自检 / 出图台无头那一路直接放行，别把断言喂成"什么都没画"。
    const int WM_SETREDRAW = 0x000B;
    int _repaintHold;      // 计数式：套两层也不能提前放开（WM_SETREDRAW 本身不计数，内层一放就把中间态露出来了）

    void HoldRepaint()
    {
        if (!IsHandleCreated || !Visible) return;
        if (_repaintHold++ == 0) SendMessage(Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
    }

    void ReleaseRepaint()
    {
        if (_repaintHold == 0 || !IsHandleCreated) return;
        if (--_repaintHold == 0 && Visible)
        {
            SendMessage(Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
            Invalidate(true);      // 放开之后必须自己喊一次：恢复 WM_SETREDRAW 不会补画中间那些帧
        }
    }

    void RefreshModuleScopedUi()
    {
        HoldRepaint();
        try { RefreshModuleScopedUiBody(); }
        finally { ReleaseRepaint(); }
    }

    void RefreshModuleScopedUiBody()
    {
        EnsureActiveModuleValid();
        var keys = EnabledModuleKeys();
        _segModules.SetItems(keys.Select(k => View(k).DisplayName).ToArray());
        _segModules.ActiveIndex = Math.Max(0, keys.IndexOf(_activeModule));
        // 2026-10-04 DEC-076 候选：卡1 回归——端口格与思考深度格都随模块换，由它自己的 Layout 摆
        Card1Relayout?.Invoke();
        Card1RowSync?.Invoke();      // DEC-182 甲：这一页有没有后端方块，决定行容器左边那条缝留不留
        RebuildModelList();               // 卡3：只列激活模块的模型，恢复它自己的上次选中
        PushCard2ForActiveModule(_config.Current);
        ShowAdvancedForActiveModule();
        RefreshModeLabel();
        UpdateApiLabel();
        RefreshRunStateView();
        EnsureLightRows();
        RefreshStatusUi();
        RefreshStatusBar();     // ④（DEC-038）：切页就换数据来源，别等下一拍（最长 5 秒）才跟上
        ActiveView.EnsureRecordLoaded();    // DEC-079：见下面那个函数
    }

    // ---------------- DEC-125① / DEC-155：模型被真加载起来时，主区自动跟到那一页 + 那一件 ----------------
    // 他的口径（三个档里挑的）= **只跟随"真把模型加载起来"的那一次**（请求从公开口进来唤醒、或请求要的是
    // 另一件而切过去），**且窗口在眼前才切**；壳外自己起的实例、开机自动启动都不算（不是"这一击加载的"）。
    // 窗口没在眼前时先记下待办，等他显示主窗（含从托盘叫回来 / 从最小化恢复）再切 —— 切之前要**重新确认那一页
    // 此刻确实还在跑**（灯回到 Green）：唤醒到开窗之间服务可能被空闲停掉，跳过去只会看见一排短横。
    // 走的是同一条 `SwitchModule`（它含 SettleNinferPort 那三处收口之一，DEC-091 不许留后门）。
    // DEC-155：跟页扩成"跟页 + 跟模型" —— 除切到那一页外，还把被加载的那一件选上下拉、界面换成它的家底
    //（他原话"自动切换到加载的模型,页面上的参数也跟随改变为选中该模型时的参数"）。同页也要跟模型。
    string? _followPageOnShow;
    string _followTagOnShow = "";                  // DEC-155：待办连"要跟的那一件"一起记
    int _followWakeCalls;

    /// <summary>纯判据：这一击把 <paramref name="wokeKey"/> 那件加载起来了，该不该切页、切到哪。
    /// 返回 null = 不用切页（不认识那一页 / 已经在那一页）。窗口可见性与"现在还在不在跑"由调用方判 ——
    /// 这条比对本身因此可以按模块表穷举，不依赖真实窗体状态。
    /// DEC-155：它只管**切不切页**；"跟到哪一件"是另一半（同页也要跟），见 <see cref="FollowModelInPage"/>。</summary>
    internal static string? PageToFollowAfterWake(string wokeKey, string activeKey, IReadOnlyCollection<string> enabledKeys)
        => wokeKey != activeKey && enabledKeys.Contains(wokeKey) ? wokeKey : null;

    /// <summary>网关真空拉起 / 切件之后调它（必须已在 UI 线程，由 UiPost 送过来）。
    /// DEC-155：<paramref name="tag"/> = 真被加载的那一件 —— 除切页外还要把它选上下拉、界面换成它的家底。</summary>
    void FollowModuleAfterWake(string key, string tag)
    {
        _followWakeCalls++;                       // 证人位（自检那条"真空拉起确实调了跟随"读它）
        if (!EnabledModuleKeys().Contains(key)) return;   // 不认识 / 没启用的那一页：什么都不做
        if (OnScreenNow)
        {
            _followPageOnShow = null; _followTagOnShow = "";
            if (PageToFollowAfterWake(key, _activeModule, EnabledModuleKeys()) is not null) SwitchModule(key);
            FollowModelInPage(key, tag);
            return;
        }
        _followPageOnShow = key; _followTagOnShow = tag;  // 窗没在眼前：连 tag 一起记，等 ShowMainWindow / 恢复再落地
    }

    /// <summary>DEC-155：跟到"那一件" —— 选中它并把界面参数换成选中它时的家底（与点下拉同一条路）。
    /// 四种不动它的情形：没给 tag / 已在那一件上（没什么可跟，也免了多余地重载一遍家底）/
    /// **目标页有未保存改动**（DEC-092，只跟页不跟模型）/ 那件不在下拉里（模块停用或未知 tag，只跟页）。</summary>
    void FollowModelInPage(string key, string tag)
    {
        if (tag.Length == 0 || !string.Equals(_activeModule, key, StringComparison.Ordinal)) return;
        if (string.Equals(View(key).TagOf(_config.Current), tag, StringComparison.OrdinalIgnoreCase)) return;
        if (View(key).PendingEdits() > 0) return;         // 目标页有未保存改动 → 只跟页不跟模型
        View(key).SetTag(_config.Current, tag);
        SelectModel(tag);
        if (!string.Equals(SelectedTag(), tag, StringComparison.OrdinalIgnoreCase)) return;   // 那件不在下拉里
        LoadKnobsFromModel(tag);
        OnModelChanged();
        ShowAdvancedForActiveModule();
        RefreshStatusUi();
    }

    /// <summary>窗口在不在眼前（最小化与藏进托盘都算"不在"）。这一条判据只有一个家。
    /// 无头自检里 `Visible` 恒 false（父链没显示 = BUG-043 那一族），所以给一个测试专用强制位，
    /// 让"窗在眼前立刻跟"那一条也能被走到、被断言，而不是整条分支永远不亮。</summary>
    internal bool OnScreenForcedForTest = false;
    bool OnScreenNow => OnScreenForcedForTest || (Visible && WindowState != FormWindowState.Minimized);

    /// <summary>窗显示出来 / 从最小化恢复时补那一次跟随（待办只吃一次，别反复跳页）。
    /// DEC-155：落地前除"那一页的灯还是绿"外，还要复核**要跟的那一件此刻确实还在跑**（三模块各一条判据）。</summary>
    internal void ApplyPendingFollowPage()
    {
        var to = _followPageOnShow;
        var tag = _followTagOnShow;
        if (to is null || !OnScreenNow) return;
        _followPageOnShow = null; _followTagOnShow = "";
        if (!EnabledModuleKeys().Contains(to)) return;
        if (View(to).Light().State != LightState.Green) return;      // 此刻那一页已经不跑了 = 待作废掉
        if (!ModelLoadedIn(to, tag)) return;                        // DEC-155：要跟的那一件此刻确实还在跑才落地
        if (PageToFollowAfterWake(to, _activeModule, EnabledModuleKeys()) is not null) SwitchModule(to);
        FollowModelInPage(to, tag);
    }

    /// <summary>DEC-155：待办落地时"要跟的那一件此刻确实还在跑"——判据与下拉那枚「已加载」徽标同一个家
    ///（Strata = 本壳进程跑的正是它 / llama = router 报它已加载 / Ninfer = 本壳进程跑的正是它）。</summary>
    bool ModelLoadedIn(string key, string tag) =>
        ModelLoadedBadgeFor(key,
            _llamaLoaded.Contains(tag),
            _process is { HasExited: false } && string.Equals(_runningTag, tag, StringComparison.OrdinalIgnoreCase),
            NinferRunning && string.Equals(_ninferRunningTag, tag, StringComparison.OrdinalIgnoreCase)) == LoadedBadgeText;

    // 自检出口：按"网关那一击真拉起了哪一页哪一件"走一遍上面的跟随（含窗口可见性那一档）
    internal void FollowModuleAfterWakeForTest(string key, string tag) => FollowModuleAfterWake(key, tag);
    internal void FollowModelInPageForTest(string key, string tag) => FollowModelInPage(key, tag);
    internal void ApplyPendingFollowPageForTest() => ApplyPendingFollowPage();
    internal string? PendingFollowPageForTest => _followPageOnShow;
    internal string PendingFollowTagForTest => _followTagOnShow;
    internal int FollowWakeCallsForTest => _followWakeCalls;
    /// <summary>DEC-155：某模块"卡面 vs 家底"的差异格数（0 = 没有未保存改动；判据长在模块表那一行）。</summary>
    internal int PendingEditsForTest(string key) => View(key).PendingEdits();
    /// <summary>DEC-155：待办落地那道"那件还在跑"的闸（自检要能算出确定性期望值，不是靠时运）。</summary>
    internal bool ModelLoadedInForTest(string key, string tag) => ModelLoadedIn(key, tag);
    /// <summary>除 <paramref name="key"/> 之外第一个启用着的模块页（自检要一个"当前不在的那一页"，
    /// 而启用集跟着他的真实配置走，不能写死 llama/ninfer）。</summary>
    internal string FirstEnabledKeyOtherThanForTest(string key) =>
        EnabledModuleKeys().FirstOrDefault(x => x != key) ?? key;
    internal bool LightGreenForTest(string key) => View(key).Light().State == LightState.Green;

    // 视图切到 llama 页 / 重扫之后，如果选中的段变了（自动选了第一个、或 ini 里从没记过 llama 模型），
    // 要补一次「按段取家底」。DEC-079 之前这步是免费的——卡2 每次刷视图都现读文件；
    // 现在视图认的是"用户的手"（没点保存的改动不能被现读顶掉），所以没载入过就必须显式载一次，
    // 否则界面会空着（自检抓到过：新 ini 里没记 llama 模型时，切到 llama 页格子全空）。
    // 第 0 轮：这段实现搬进 MainForm.ModuleViews.cs 的 llama 那一行（Strata 那行是空操作）。

    // 卡2 通用行（B 段；DEC-074 起首行含监听地址/监听端口）：
    // 互斥项摆位 + 「这格可不可编辑」由这里统一给（每张格子都在各页共用，值由各模块那一行推）——加模块只改那张表。
    void PushCard2ForActiveModule(Settings s)
    {
        foreach (var v in _views) v.KvBox().Visible = ReferenceEquals(v, ActiveView);   // KV 那一格每模块一只下拉
        _txtModelName.Inner.ReadOnly = ActiveView.ModelNameReadOnly;   // llama 侧它是 ini 的段名 = 身份键，只读
        _txtContext.Inner.ReadOnly = false;      // DEC-079：llama 页也能改了（= 该段的 ctx-size）
        // 附加参数（DEC-075）：两个模块都能编辑——Strata = 引擎参数追加；llama = 命令行追加
        _txtExtra.Inner.ReadOnly = false;
        ActiveView.PushCard2(s);
    }

    // 状态短语（B 段：文案由实时标志推导；kv「状态」行 / 运行状态卡右侧 / 状态灯共用同一事实）
    // DEC-101②：三页的判据拆成纯函数对（与 StartStopTextFor…/ModelActionTextFor… 同一形状，自检可穷举）。
    // DEC-125④（用户实跑点名）：**颜色不再自己挑，一律从状态灯那张表取**（`LightColor(灯态)`）——
    // 「● 待机 · 模型未加载」原来是 SteelBlue，而同一状态下方那枚灯是橙（`StrataLight` 的 gatewayUp → Orange），
    // 于是"上面蓝字、下面橙点"看着像两件事；他的口径 = "应与下方状态灯的颜色一致，不要使用新颜色"。
    // 这一条**作废 DEC-101② 里"蓝=在听、模型没加载"那一档与 DEC-057 色表的"蓝=待机"**（三页同色那条判据不动，
    // 只是钉的值从 SteelBlue 换成灯表那一档橙）；"蓝"在这一族从此不再表示状态，只剩更新小按钮那类装饰。
    internal static (string Text, Color Color) StatusTextForStrata(bool failed, bool running, bool ready, bool external, bool gatewayUp)
    {
        if (failed) return ("● 启动失败", LightColor(LightState.Red));
        if (running) return ready ? ("● 运行中", LightColor(LightState.Green)) : ("● 启动中…", LightColor(LightState.Orange));
        if (external) return ("● 运行中（已有服务）", LightColor(LightState.Green));
        if (gatewayUp) return ("● 待机 · 模型未加载", LightColor(LightState.Orange));      // DEC-125④：跟灯走，别再写 SteelBlue
        return ("已停止", LightColor(LightState.Gray));
    }

    internal static (string Text, Color Color) StatusTextForLlama(bool failed, string op, bool running, bool loadedAny, bool external)
    {
        if (failed) return ("● 启动失败", LightColor(LightState.Red));
        if (op.Length > 0) return ("● " + op, LightColor(LightState.Orange));
        if (running || external) return loadedAny ? ("● 运行中", LightColor(LightState.Green))
                                                  : ("● 待机 · 模型未加载", LightColor(LightState.Orange));
        return ("已停止", LightColor(LightState.Gray));
    }

    (string text, Color color) StrataStatusText()
    {
        bool running = _process is { HasExited: false };
        return StatusTextForStrata(_strataFailed, running, _readyUrl.Length > 0, _externalRunning, StrataGateway is not null);
    }

    (string text, Color color) LlamaStatusText() =>
        StatusTextForLlama(_llamaRunFailed, _llamaOp, LlamaRunning, _llamaLoaded.Count > 0, _llamaExternal);

    // 失败标志（红灯）：起停环节维护；手动停（Kill 的退出码）不算失败
    internal bool _strataFailed, _llamaRunFailed;
    internal bool _strataStopping, _llamaStopping;

    // ---------------- 状态灯（左栏底部「模块状态」） ----------------
    // 四态（DEC-025）：灰=没在跑；绿=在跑且就绪；橙=在跑但待机 / 过渡；红=异常。纯函数，自检穷举。

    internal enum LightState { Gray, Green, Orange, Red }

    internal static (LightState state, string desc) StrataLight(bool failed, bool running, bool ready, bool external, bool gatewayUp, bool route, int port)
    {
        string mode = route ? "路由模式" : "单体模型";
        if (failed) return (LightState.Red, "异常 · 启动失败");
        if (running) return ready ? (LightState.Green, $"运行中 · {mode} · 端口 {port}") : (LightState.Orange, $"启动中 · 端口 {port}");
        if (external) return (LightState.Green, $"运行中 · 外部服务 · 端口 {port}");   // DEC-100②：括号换成点号（用户提的，省 13px 且与另几句同形）
        // ⑥（DEC-055 用户要求统一口径）：网关在听、模型没加载 = 「待机 · 模型未加载」，与 llama 侧同一句话
        // DEC-100② 曾把那两枚状态词里的「模型」二字缩掉（当时行宽只有 S(162)、长了会被截断）；
        // DEC-107：他拍板左栏加宽到内宽 196（行宽可用 180）后把「模型」二字加回来 —— 实测九句全放得下
        //（含五位端口最坏情况），别再缩回去。
        if (gatewayUp) return (LightState.Orange, $"待机 · 模型未加载 · 端口 {port}");
        return (LightState.Gray, $"已停止 · 端口 {port}");
    }

    internal static (LightState state, string desc) LlamaLight(bool failed, string busy, bool running, bool loaded, int port)
    {
        if (failed) return (LightState.Red, "异常 · 启动失败");
        if (busy.Length > 0) return (LightState.Orange, $"{busy} · 端口 {port}");
        if (running) return loaded ? (LightState.Green, $"运行中 · 模型已加载 · 端口 {port}") : (LightState.Orange, $"待机 · 模型未加载 · 端口 {port}");
        return (LightState.Gray, $"已停止 · 端口 {port}");
    }

    internal static Color LightColor(LightState s) => s switch
    {
        LightState.Green => Color.ForestGreen,
        LightState.Orange => Color.DarkOrange,
        LightState.Red => UiTheme.Danger,
        _ => Color.Gray,
    };

    readonly Panel _lightBlock = new() { BackColor = UiTheme.Card };
    readonly List<(string Key, Panel Row, LightDot Dot, Label Name, Label Desc)> _lightRows = new();
    // 折叠态（2026-10-15 他点名加）：标题右侧一枚箭头按钮 + 一行横排的「灯 + 简称」，
    // 悬浮出该模块的完整状态（含端口）。状态记在 `Settings.LightsCollapsed`（他明确"记住上次"）。
    readonly FlatButton _btnLightToggle = new();
    readonly Panel _lightStrip = new() { BackColor = UiTheme.Card };
    readonly List<(string Key, Panel Box, LightDot Dot, Label Name)> _lightStripItems = new();
    /// <summary>折叠态那一行的高（逻辑）：横排盒子高、`_lightStrip` 高、块高 S(34+20) 三处同源，别各写一遍。</summary>
    const int LightStripRowLogical = 20;

    /// <summary>一句 AutoSize 文字的**墨迹中心**在哪：实测 = 它行盒的中线（150% 下 8.5pt「Strata」行盒 22、
    /// 墨迹顶 +4..+17 → 心 +10.5，与行盒中线 11 差 0.5 物理像素；96% 差 0.4）。所以灯直接压在行盒中线上，
    /// 两种 DPI 下误差都在半像素内 —— 不必（也不该）再按某一个 DPI 补一个常数（DEC-177）。</summary>
    static int LightTextCenterFromTop(int labelHeight) => labelHeight / 2;

    // 行集 = 启用中的模块（全隐身：停用的整行消失）；行内：点 + 名（一行）+ 说明文字（含工作端口）
    void EnsureLightRows()
    {
        var keys = EnabledModuleKeys();
        for (int i = _lightRows.Count - 1; i >= 0; i--)
            if (!keys.Contains(_lightRows[i].Key))
            {
                _lightBlock.Controls.Remove(_lightRows[i].Row);
                _lightRows.RemoveAt(i);
            }
        foreach (var k in keys)
        {
            if (_lightRows.Any(r => r.Key == k)) continue;
            var row = new Panel { BackColor = UiTheme.Card };
            var dot = new LightDot { Backdrop = UiTheme.Card };
            var name = new Label { Text = ModuleRegistry.All.First(m => m.Key == k).DisplayName, AutoSize = true, ForeColor = UiTheme.Text, BackColor = UiTheme.Card, Font = _fUi };
            var desc = new Label { AutoSize = true, ForeColor = UiTheme.TextMuted, BackColor = UiTheme.Card, Font = _fSmall };
            name.Location = new Point(UiTheme.S(this, 16), 0);
            desc.Location = new Point(UiTheme.S(this, 16), UiTheme.S(this, 20));
            // 灯心压着那一行名字的墨迹中心（行里名字顶在 y=0）
            PlaceLightDot(dot, name, 0);
            row.Controls.Add(dot);
            row.Controls.Add(name);
            row.Controls.Add(desc);
            _lightBlock.Controls.Add(row);
            _lightRows.Add((k, row, dot, name, desc));
        }
        int y = UiTheme.S(this, 36);      // 标题 + 分隔线之下
        foreach (var k in keys)
        {
            var e = _lightRows.First(r => r.Key == k);
            e.Row.SetBounds(0, y, UiTheme.S(this, SideInnerLogical), UiTheme.S(this, 42));
            y += UiTheme.S(this, 46);
        }
        EnsureLightStrip();          // 折叠态那一行同源（同一份 keys，同一个顺序）
        ApplyLightsCollapsed();      // 摆位收口：两种态的几何都从这一个函数出（别在两处各摆一次）
    }

    /// <summary>把一枚灯摆进它那一行：水平方向贴着行首让出 S(1)（= 原「●」字形的墨迹左缘），
    /// 垂直方向**圆心 = 同那一行文字的墨迹中心**（`nameTop` = 文字标签在这一行里的顶）。
    /// 展开态与折叠态共用这一条，两种态的灯才可能长一样（DEC-177）。</summary>
    void PlaceLightDot(LightDot dot, Label name, int nameTop)
    {
        int d = UiTheme.S(this, LightDot.LogicalDiameter);
        int cy = nameTop + LightTextCenterFromTop(name.GetPreferredSize(Size.Empty).Height);
        dot.SetBounds(UiTheme.S(this, 1), cy - d / 2, d, d);
    }

    // 折叠态那一行：每个启用模块一枚「灯 + 简称」，横排（2026-10-15 他拍：Strata/llama/Ninfer）
    void EnsureLightStrip()
    {
        var keys = EnabledModuleKeys();
        for (int i = _lightStripItems.Count - 1; i >= 0; i--)
            if (!keys.Contains(_lightStripItems[i].Key))
            {
                _lightStrip.Controls.Remove(_lightStripItems[i].Box);
                _lightStripItems.RemoveAt(i);
            }
        foreach (var k in keys)
        {
            if (_lightStripItems.Any(x => x.Key == k)) continue;
            var box = new Panel { BackColor = UiTheme.Card };
            var dot = new LightDot { Backdrop = UiTheme.Card };
            var name = new Label { Text = View(k).ShortName, AutoSize = true, ForeColor = UiTheme.Text, BackColor = UiTheme.Card, Font = _fSmall };
            box.Controls.Add(dot);
            box.Controls.Add(name);
            _lightStrip.Controls.Add(box);
            _lightStripItems.Add((k, box, dot, name));
        }
        // 横排：宽度按**那一行文字自己的首选宽**量（简称长短不一），间距 S(10)；总宽放进卡片内宽由自检量着（别靠估）。
        // DEC-177 的坑：以前这里用 `TextRenderer.MeasureText(..., NoPadding)` 当宽 —— 它比 Label 自己
        // AutoSize 用的那个量法窄一截（Label 带内衬），于是盒子右缘正好切在最后一个字上：他图上
        // 「Strata」缺尾巴、「Ninfer」的 r 只剩一根竖。宽度必须与**渲染它的那个控件**同源。
        int rowH = UiTheme.S(this, LightStripRowLogical);
        int x = 0;
        foreach (var k in keys)
        {
            var e = _lightStripItems.First(i => i.Key == k);
            var pref = e.Name.GetPreferredSize(Size.Empty);
            e.Name.Location = new Point(UiTheme.S(this, 14), (rowH - pref.Height) / 2);   // 文字在行里上下居中
            PlaceLightDot(e.Dot, e.Name, e.Name.Top);
            int w = UiTheme.S(this, 14) + pref.Width;
            e.Box.SetBounds(x, 0, w, rowH);
            x += w + UiTheme.S(this, 10);
        }
    }

    // 按当前折叠态摆这一块（唯一的摆位家）：展开 = 每模块一行（现状）；折叠 = 只有那一行横排。
    // 按钮箭头口径与卡2「高级」同一套（`_advShown ? "▾ 高级" : "◂ 高级"`）：展开态 ▾、折叠态 ◂。
    void ApplyLightsCollapsed()
    {
        bool collapsed = _config.Current.LightsCollapsed;
        _btnLightToggle.Text = collapsed ? "◂" : "▾";       // 方向口径同卡2「高级」（`_btnAdv.Text` 那一句一模一样的两个字面）
        // 这枚箭头**不挂 tooltip**（2026-10-15 他点名"折叠按钮不需要 tooltip"）：它点下去就是整块变形，
        // 落点已经够明显；DEC-176 那版那句「折叠/展开「模块状态」…」的说明随这条撤掉。
        foreach (var (_, row, _, _, _) in _lightRows) row.Visible = !collapsed;
        _lightStrip.Visible = collapsed;
        if (collapsed)
        {
            _lightStrip.SetBounds(0, UiTheme.S(this, 34), UiTheme.S(this, SideInnerLogical),
                                  UiTheme.S(this, LightStripRowLogical));
            _lightBlock.Height = UiTheme.S(this, 34 + LightStripRowLogical);
        }
        else
        {
            int y = UiTheme.S(this, 36);
            foreach (var (_, row, _, _, _) in _lightRows)
            {
                row.SetBounds(0, y, UiTheme.S(this, SideInnerLogical), UiTheme.S(this, 42));
                y += UiTheme.S(this, 46);
            }
            _lightBlock.Height = y;
        }
    }

    // 点标题右侧那枚箭头：切换折叠态。**立刻落盘**（他明确"记住上次"，所以不等「保存」那套；
    // 它不是参数格 → commitParams: false，只把 _config.Current 写出去）。
    void ToggleLights()
    {
        _config.Current.LightsCollapsed = !_config.Current.LightsCollapsed;
        ApplyLightsCollapsed();
        SaveConfig(commitParams: false);
    }

    void RefreshModuleLights()
    {
        // 灯是自绘的（DEC-177）：改色不会自己重绘，所以要点了 Invalidate —— 但只在色真变了那一拍点，
        // 这个函数每拍状态刷新都跑（同下面 tooltip 那条纪律）。
        foreach (var (key, _, dot, _, desc) in _lightRows)
        {
            var (st, text) = ModuleLight(key);
            if (dot.Ink != LightColor(st)) { dot.Ink = LightColor(st); dot.Invalidate(); }
            desc.Text = text;
        }
        // 折叠态同一份判据、同一份文案（不新造第二套）：灯色跟着走，tooltip = 「全称 · 完整状态（含端口）」。
        // 只在文案真变了才 Set —— 这个函数每拍状态刷新都跑，无脑 Set 会让鼠标正悬停时闪。
        foreach (var (key, box, dot, name) in _lightStripItems)
        {
            var (st, text) = ModuleLight(key);
            if (dot.Ink != LightColor(st)) { dot.Ink = LightColor(st); dot.Invalidate(); }
            var tip = View(key).DisplayName + " · " + text;
            // 提示要挂在**这一枚的三个窗口上**（盒 / 灯 / 文字）：WinForms 的 ToolTip 按 HWND 认，鼠标落在子窗口上
            // 时父 Panel 那一份不弹 —— 他点名的"折叠态几个模块的 tooltip 不能稳定触发"就是这么来的
            //（同一个坑 2026-10-02 在圆角输入框上踩过一次，见 `Tip.Set` 里内层 TextBox 那一段）。
            // 走 `SetOneLine`：这一句 29 字，按全局 28 字那一档会被折成两行，他要整行不折。
            if (Tip.TextOf(box) != tip)
            {
                Tip.SetOneLine(box, tip); Tip.SetOneLine(dot, tip); Tip.SetOneLine(name, tip);
            }
        }
    }

    // 一个模块的灯态（左栏「模块状态」与托盘菜单组头共用同一份判据，别两处各算一套）
    // 第 0 轮 DEC-094：判据实现（喂给 StrataLight / LlamaLight 的那些实时值）在各模块那一行里
    internal (LightState State, string Desc) ModuleLight(string key) => View(key).Light();

    // 托盘组头的状态词：复用灯的说明文案，只把"端口 xxxx"那段挑掉（用户明确：组头不带端口）
    internal static string LightWords(string desc) => string.Join(" · ",
        desc.Split(new[] { " · " }, StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !p.StartsWith("端口", StringComparison.Ordinal)));

    /// <summary>量一句任意文案在那行灯里要占多宽（给"换哪个说法才放得下"这类决定用实测数，不靠估）。
    /// 行宽口径与 <see cref="LightRowFitForTest"/> 一致：可用 = S(196) − S(16)。</summary>
    internal (int Need, int Room) LightTextWidthForTest(string text) =>
        (TextRenderer.MeasureText(text, _fSmall, Size.Empty, TextFormatFlags.NoPadding).Width, UiTheme.S(this, SideInnerLogical - 16));

    /// <summary>DEC-100②：量「模块状态」那行灯说明**放不放得进行宽**。DEC-107 起左栏内宽 = 196
    /// （他 2026-10-08 拍板"+10%、就近取整" = 178 → 196），所以这类"够不够"必须量出来。
    /// 行宽 = S(196)、文字从 S(16) 起 → 可用 = S(180)；量字带 NoPadding（默认 MeasureText 把行距空白算进去）。
    /// 九句都用五位端口 18082 = 最坏情况（他不是五位口，但判据按最坏量）。</summary>
    internal List<(string Module, string Text, int Need, int Room)> LightRowFitForTest()
    {
        int room = UiTheme.S(this, SideInnerLogical - 16);
        var cases = new (string Module, string Text)[]
        {
            ("strata", StrataLight(false, true, true, false, false, true, 18082).desc),   // 运行中 · 路由模式 · 端口 18082
            ("strata", StrataLight(false, false, false, false, true, false, 18082).desc), // 待机 · 模型未加载 · 端口 18082
            ("strata", StrataLight(false, false, false, true, false, false, 18082).desc), // 运行中 · 外部服务 · 端口 18082
            ("llama", LlamaLight(false, "", true, true, 18082).desc),                  // 运行中 · 模型已加载 · 端口 18082
            ("llama", LlamaLight(false, "", true, false, 18082).desc),                 // 待机 · 模型未加载 · 端口 18082
            ("llama", LlamaLight(false, "卸载中…", false, false, 18082).desc),         // 忙态那句
            ("ninfer", NinferLight(false, true, true, false, false, 18082).Desc),      // 运行中 · 模型已加载 · 端口 18082
            ("ninfer", NinferLight(false, false, false, false, true, 18082).Desc),     // 待机 · 模型未加载 · 端口 18082
            ("ninfer", NinferLight(false, false, false, true, false, 18082).Desc),     // 运行中 · 外部服务 · 端口 18082
        };
        return cases.Select(c => (c.Module, c.Text,
            TextRenderer.MeasureText(c.Text, _fSmall, Size.Empty, TextFormatFlags.NoPadding).Width, room)).ToList();
    }

    // ---------------- 自检出口：左栏「模块状态」的折叠态（2026-10-15） ----------------

    /// <summary>自检用：只改内存里的折叠态并重摆 —— **不落盘**（自检不许写他的 ini）。
    /// "点按钮会落盘"那一侧由「[Current] 里含 LightsCollapsed」那条纯函数判据钉住（`BuildConfigLines`）。</summary>
    internal void SetLightsCollapsedForTest(bool collapsed)
    {
        _config.Current.LightsCollapsed = collapsed;
        ApplyLightsCollapsed();
    }

    /// <summary>自检用：这一块此刻的样子。**别读 `Control.Visible`** —— 那是"含祖先"的有效可见性，
    /// 自检不开窗时整棵树都是 false（DEC-017 那个坑，同 `_advShown` 存在的理由）。所以这里报
    /// 「折叠位 + 块高 + 按钮文案 + 按钮是否裸字形」，**块高由摆位算出** = 布局真按这个态摆了的硬证据。</summary>
    internal (bool Collapsed, int BlockH, string ToggleText, bool ToggleBare) LightsStateForTest =>
        (_config.Current.LightsCollapsed, _lightBlock.Height, _btnLightToggle.Text, _btnLightToggle.Bare);

    /// <summary>自检用：一枚灯的几何（展开态按整行、折叠态按那一枚的盒子）。
    /// `TextNeedW` = 那一行文字**自己**要多宽（Label 首选宽 + 灯那一列），与盒子实际宽 `BoxW` 一比
    /// 就是"最后一个字有没有被盒子右缘切掉"（DEC-177 那个 bug 的判据）。</summary>
    internal sealed record LightGeomForTest(string Where, string Key, int RowH, int DotTop, int DotH,
                                           int TextTop, int TextLineH, int BoxW, int TextNeedW);
    internal List<LightGeomForTest> LightGeometryForTest
    {
        get
        {
            var list = new List<LightGeomForTest>();
            foreach (var (k, row, dot, name, _) in _lightRows)
            {
                var p = name.GetPreferredSize(Size.Empty);
                list.Add(new("展开", k, row.Height, dot.Top, dot.Height, name.Top, p.Height,
                             row.Width, UiTheme.S(this, 16) + p.Width));
            }
            foreach (var (k, box, dot, name) in _lightStripItems)
            {
                var p = name.GetPreferredSize(Size.Empty);
                list.Add(new("折叠", k, box.Height, dot.Top, dot.Height, name.Top, p.Height,
                             box.Width, UiTheme.S(this, 14) + p.Width));
            }
            return list;
        }
    }

    /// <summary>自检用：折叠态每一枚的提示挂在**哪几个窗口**上、以及交给系统画的那一份有没有被折行。
    /// 三处必须同一句：鼠标落在灯或简称上时（子窗口各有 HWND），只挂父盒的那一份不弹 —— 他点名的"不稳定触发"。
    /// 读 <see cref="Tip.RenderedOf"/> 而不是 <see cref="Tip.TextOf"/>：后者存的是折行前的原文，看不出有没有折。</summary>
    internal List<(string Key, string Box, string Dot, string Name, bool AllOneLine)> LightStripTipsForTest =>
        _lightStripItems.Select(i =>
        {
            var (b, d, n) = (Tip.RenderedOf(i.Box), Tip.RenderedOf(i.Dot), Tip.RenderedOf(i.Name));
            return (i.Key, Tip.TextOf(i.Box), Tip.TextOf(i.Dot), Tip.TextOf(i.Name),
                    b.Length > 0 && d == b && n == b && !b.Contains('\n'));
        }).ToList();

    /// <summary>自检用：折叠箭头那枚 —— 他点名"不需要 tooltip"（必须为空）、"不打底"（Bare）、
    /// 与卡2「高级」那枚**同源**（同一字形 ▾/◂ + 同字号 + 同字色，判据直接拿 `_btnAdv` 比，不写死字面量）。</summary>
    internal (string Tip, string Text, bool Bare, bool SameFontAsAdv, bool SameInkAsAdv) LightToggleForTest =>
        (Tip.TextOf(_btnLightToggle), _btnLightToggle.Text, _btnLightToggle.Bare,
         _btnLightToggle.Font.Size == _btnAdv.Font.Size && _btnLightToggle.Font.Name == _btnAdv.Font.Name,
         _btnLightToggle.TextColor == _btnAdv.TextColor);

    /// <summary>自检用：折叠态那一行横排的总宽需求 vs 可用宽（= 卡片内宽，与展开态灯行同一条口径）。
    /// 三个简称横排放不放得下必须**量出来**，别靠估（他挑的简称就是照这个定的）。</summary>
    internal (int Need, int Room) LightStripFitForTest
    {
        get
        {
            int right = 0;
            foreach (var (_, box, _, _) in _lightStripItems) right = Math.Max(right, box.Right);
            return (right, UiTheme.S(this, SideInnerLogical));
        }
    }

    /// <summary>自检用：折叠态每一枚的「简称 + tooltip 原文」（tooltip 必须与展开态那行说明同一句）。</summary>
    internal List<(string Key, string Short, string Tip)> LightStripForTest =>
        _lightStripItems.Select(i => (i.Key, i.Name.Text, Tip.TextOf(i.Box))).ToList();

    /// <summary>自检用：某个模块在折叠态用的简称（口径 = 模块表那一行，别在用例里写死字面量）。</summary>
    internal string ShortNameForTest(string key) => View(key).ShortName;
}
