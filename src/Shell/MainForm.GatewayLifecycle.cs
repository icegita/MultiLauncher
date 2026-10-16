using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MultiLauncher;

// 网关生命周期与上游决策（第 0 轮一模块一份、单飞门、唤醒退避、DEC-165 幽灵起件那半条）。
// 2026-10-15 从 MainForm.Gateway.cs 整段搬出，一行未改。
public partial class MainForm
{
    // ---------------- 网关生命周期（第 0 轮：一模块一份）----------------

    /// 一模块一份的网关接线。网关本体（Gateway.cs）只吃委托、本来就不认识 Strata，
    /// 所以这里把"上游听哪个口、模型表从哪来、怎么拉起/怎么停、日志走哪条通道、起完之后本机要做什么"
    /// 全部交给各模块那一行（MainForm.ModuleViews.cs 的 Gateway 成员；不挂 hooks = 本模块不走壳网关）。
    internal sealed record GatewayHooks(
        string Key,                       // 模块键（也是退避与单飞门的归属）
        string DisplayName,               // 说给用户听的名字（未启用/失败那几行日志）
        string OwnedBy,                   // /v1/models 里那个 owned_by（以前写死 "strata"）
        Func<int> PublicPort,             // 网关自己听的公开端口
        // DEC-138（A-5）：绑哪个地址也归各模块那一行。以前 StartGateway 直接摸 `_txtHost`（那是卡1 那只控件，
        // 属于界面那一层），而网关这一族本来"全部走注入"，唯独漏了这一项 —— 再加模块就得在网关里写 if (key=="…")。
        Func<string> BindHost,             // 网关绑定地址（三页共用卡1 那一只监听地址框，两处给同一条算式）
        Func<TimeSpan> IdleTimeout,       // 空闲多久停上游
        Func<int> UpstreamPort,           // 转发目标 = 引擎实际听的内部端口
        Action Prepare,                   // 起网关前把内部端口纠正成能用的值
        Func<IEnumerable<ModelEntry>> Models,
        Func<string> CurrentTag,          // 请求没写 model 时用界面上选中的这个
        Func<bool> UpstreamRunning,
        Func<string> RunningTag,
        Func<string, CancellationToken, Task> Wake,   // 拉起（不等 ready；本模块的就绪信号也在这里立）
                                                      // DEC-160：吃**这一发请求的**取消令牌 —— 排队那一圈与起件那一步都归它管
        Action StopUpstream,              // 停上游进程（空闲停服那条；**调用方负责排到界面线程**，这里不等）
        Func<Task> StopUpstreamAndWait,   // 切模型那条：等旧进程真退出再拉新的（原口径 = WaitForExit(20000)）
        // DEC-165（清单第 5 条 a 档）：这一发请求**已经走了**（客户端断开 / 总时限到点），而件还是被拉起来了 ——
        // 网关此刻也没有别的在途请求 → 通知壳把**刚起来的那一件**收掉（把显存还回去）。
        // **调用方负责排到界面线程**；实现里必须自己确认"此刻跑的正是这一件"（不是就什么都不做）。
        Action<string> UnloadAbandonedStarted,
        Func<Task> WaitReady,
        Action<string> Log,               // 日志通道（Strata = LogS）
        Action OnStarted,                 // 立好之后本机要做什么（服务原本占着公开口 => 重启到内部口）
        Action OnStopped,                 // 收了之后本机要做什么（公开口交还引擎）
        // DEC-122①：这一族**唤醒**是按什么的参数起的、那一行日志怎么说（Strata = 「按 {tag} 的存档启动」，
        // 因为它的取参 = 「配置模板 ⊕ [Model:<tag>] 记录」）。null = 这一族不说：llama / Ninfer 的家底本来就在
        // 它们自己那份 ini 的段里、由引擎读，没有"另一份参数"可交代（不许在网关里写 if (key=="strata")）。
        Func<string, string>? WakeFromHomeNote = null,
        // 上游是不是"正在加载中"（进程起来了、还没就绪）。空闲判定在这一档上停一拍：
        // 手动「启动」/ 卡3「加载」要 30 秒~3 分钟才 ready，锚点这时可能已经是旧的 ——
        // 不挡就会把正在读权重的进程停掉（用户实跑抓到的「加载一会儿就自动卸载」）。
        // null = 这一族不表态（照旧按锚点判）。
        Func<bool>? UpstreamBusy = null);

    internal void ApplyGatewaySetting(string key, bool on)
    {
        if (on) StartGateway(key); else StopGateway(key);
    }

    /// <summary>起某一模块的网关之前，哪些**已经在跑的别的模块网关**要让位：只有公开口真撞车的那些（DEC-100③）。
    /// 各守各的口（Strata 8077 / Ninfer 8068）就能同时在线 —— 老写法无条件互停，用户实跑就是「为什么起一个另一个就退出」。
    /// 纯函数，自检喂组合，不真绑端口。</summary>
    internal static List<string> GatewaysToDisplace(IEnumerable<(string Key, int Port)> up, string key, int publicPort) =>
        up.Where(x => x.Key != key && x.Port == publicPort).Select(x => x.Key).ToList();
    // 老写法：按当前激活模块起/停（模式条那条路只有 Strata 会走到，语义不变）
    void ApplyGatewaySetting(bool on) => ApplyGatewaySetting(ModelEntry.BackendStrata, on);   // DEC-137（A-4）：键只有一个家（ModelEntry.Backend*），别再留字面量

    void StartGateway(string key)
    {
        var h = View(key).Gateway;
        if (h is null) return;                       // 本模块不走壳网关（llama.cpp 的路由由引擎自己实现）
        if (_gateways.ContainsKey(key)) return;
        // 模块开关（B 段地基，DEC-024）：网关属于各模块自己（路由模式是它实现出来的）——停用就不立（全隐身）
        if (!View(key).EnabledIn(_config.Current))
        {
            h.Log($"{h.DisplayName} 模块未启用：不启动网关（到设置页「通用 → 模块」里开启）");
            return;
        }
        // DEC-094 原来这里是"无条件停掉别的模块那一份"，前提是「公开端口只有一个」。
        // 那个前提到 DEC-096 就不成立了：Strata 守 `Settings.Port`、Ninfer 守自己的 `NinferPort`（默认 8068）——
        // 各守各的口本来就能同时在线，无条件互停 = 用户实跑问的那句「为什么起一个另一个就退出」。
        // 所以改成**只有公开口真撞车才让位**（撞车时仍是先停对方再起自己，行为与老口径一致）。
        // 显存那一头的互斥不归网关管：起进程前由 `GpuMutex` 那一档拦（进入队列 / 返回报错；
        // DEC-157 起三模块共用，判据在 MainForm.GpuMutex.cs 那一个家）。
        var myPublicPort = h.PublicPort();
        foreach (var other in GatewaysToDisplace(_gateways.Select(g => (g.Key, g.Value.Port)), key, myPublicPort))
            StopGateway(other);
        h.Prepare();
        var host = h.BindHost();           // DEC-138（A-5）：绑址从 hooks 取，网关这一层不再碰界面控件
        var publicPort = myPublicPort;
        var gw = new Gateway(host, publicPort, h.IdleTimeout(),
            // DEC-159：这一枚不再写死 300 —— 它必须比「排队等显存上限」长一截，否则那句点名"前面是谁"的 504 永远被
            // 这句笼统的「等待模型就绪超过 300 秒」顶掉（算式只有一个家 = GatewayWakeTimeoutSeconds）
            TimeSpan.FromSeconds(GatewayWakeTimeoutSeconds(WakeWaitSeconds())),
                             (model, wake, ct) => EnsureUpstreamForRequest(h, model, wake, ct),
                             () => StopUpstreamForIdle(h),
                             () => BuildModelsJson(h.Models(), h.OwnedBy), h.Log,
                             // DEC-112：「上游是不是就在跑这一件」的判据（网关自己不认识模块，照样走注入）。
                             (model, wake) => GatewayRepeatFor(h, model, wake),
                             // 上游正在加载（还没 ready）时空闲停服让一拍：这一档也只有各模块自己知道
                             upstreamBusy: () => h.UpstreamBusy?.Invoke() == true);
        try { gw.Start(); }
        catch (Exception ex)
        {
            gw.Dispose();
            View(key).MarkGatewayFailed();    // 网关起不来 = 本模块异常（状态灯红灯）
            h.Log("网关启动失败: " + ex.Message);
            Dialogs.Message(this, "无法启动网关", $"公开端口 {publicPort} 起不了网关：{ex.Message}", "可能已被占用，或没有权限绑定该地址。", danger: true);
            return;
        }
        _gateways[key] = gw;
        h.OnStarted();
        // 这里**不**扫模型：网关只是壳在听端口，不改模型文件；勾了「请求唤醒」时开机已经扫过一轮，
        // 再扫一次就是用户看到的「启动时多了一次模型扫描」。真正要扫的时机在服务侧（StartService / OnServerExited）。
        UpdateApiLabel();
    }

    void StopGateway(string key)
    {
        if (!_gateways.TryGetValue(key, out var gw)) return;
        _gateways.Remove(key);
        gw.Dispose();
        var h = View(key).Gateway;
        h?.Log("网关已停止（公开端口交还 " + h.DisplayName + "）");
        h?.OnStopped();
        // 同 StartGateway：网关启停不扫模型（要扫由服务那一头的 StartService / OnServerExited 负责）
        UpdateApiLabel();
    }

    // 兼容老调用点（模式条 / 网关按钮都在 Strata 那一侧）
    void StartGateway() => StartGateway(ModelEntry.BackendStrata);                              // DEC-137（A-4）同上
    void StopGateway() => StopGateway(ModelEntry.BackendStrata);                                // DEC-137（A-4）同上

    // 自检出口：不挂 hooks 的模块**点不动网关**（这条必须能断言，否则接第二个模块时"顺手也立了一个网关"没人发现）
    internal void StartGatewayForTest(string key) => StartGateway(key);
    internal int GatewayCountForTest => _gateways.Count;
    internal bool GatewayUpForTest(string key) => GatewayFor(key) is not null;

    void DisposeGateway()
    {
        foreach (var key in _gateways.Keys.ToList())
        {
            var gw = _gateways[key];
            _gateways.Remove(key);
            gw?.Dispose();
        }
        foreach (var g in _wakeGates.Values) g.Dispose();
        _wakeGates.Clear();
    }

    // ---------------- 请求到达时的上游决策 ----------------

    /// <summary>DEC-122①：切件那一句怎么说（唯一家）。原来那条只说"先停旧的"，看不出**按谁的参数**起，
    /// 而他抓的那次故障要的正是这一句（"没改配置，速度怎么大降的"）。
    /// <paramref name="wakeNote"/> = 这一族唤醒取参的落点（<see cref="GatewayHooks.WakeFromHomeNote"/> 给的句子），
    /// null = 这一族不说，句子与升级前**逐字节相同**（llama / Ninfer 不许被顺手改口径）。</summary>
    internal static string SwitchToWakeLine(string tag, string runningTag, string? wakeNote)
        => $"请求要的是 {tag}，当前跑的是 {runningTag}：先停旧的" + (wakeNote is null ? "" : "，" + wakeNote);

    // 模型对外的名字：优先 config 里的 model_name，没有才用 tag（列表与匹配都以它为准）
    internal static string ModelApiName(ModelEntry m) => m.ModelName.Trim().Length > 0 ? m.ModelName : m.Tag;

    /// <summary>DEC-112：这条请求是不是**只是转发给已经在跑的那一件**（既不唤醒也不切件）→ 那行「网关收到 POST…」
    /// 就只是重复一句"有个请求进来了"，用户实跑点名不必重复展示（他那 157 行里 23 句，与 23 条 `[strata] done:` 一一对应）。
    /// 判据与 `EnsureUpstreamForRequest` 那一条**同源**（`UpstreamRunning` + `RunningTag` + `TryResolveRequestTag`），别在这儿长第二套。
    /// 三种情况一律不压：① 非唤醒路径（那句"这个路径不唤醒模型"早就被滤了）；② 上游没在跑 = 这条要唤醒，正是该看得见的一刻；
    /// ③ 请求里没写 model（要用界面上选中的那个，判它得回 UI 线程拿控件值 —— 宁可不压，也不在这儿猜）。</summary>
    internal static bool GatewayRequestIsRepeat(bool wake, string? modelName, bool upstreamRunning,
                                                string runningTag, IEnumerable<ModelEntry> models)
    {
        if (!wake || string.IsNullOrWhiteSpace(modelName) || !upstreamRunning) return false;
        return TryResolveRequestTag(models, "", modelName, out var tag, out _)
               && string.Equals(runningTag, tag, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool GatewayRepeatFor(GatewayHooks h, string? modelName, bool wake)
    {
        try
        {
            return GatewayRequestIsRepeat(wake, modelName, h.UpstreamRunning(), h.RunningTag(), h.Models());
        }
        catch { return false; }        // 判据取不到就当"要显示"（宁多一句也不许把该看见的压掉）
    }

    // 请求里的 model 名 -> 要跑的模型 tag：
    //   没写 model           -> 用界面上当前选中的模型（不算错）
    //   命中 tag / model_name -> 用那个模型
    //   写了但对不上任何配置   -> false，调用方回 400 并列出可用模型名（贴近 llama.cpp 路由模式）
    internal static bool TryResolveRequestTag(IEnumerable<ModelEntry> models, string currentTag, string? modelName,
                                              out string tag, out string error)
    {
        tag = currentTag;
        error = "";
        if (string.IsNullOrWhiteSpace(modelName)) return true;
        var name = modelName.Trim();
        foreach (var m in models)
        {
            if (string.Equals(m.Tag, name, StringComparison.OrdinalIgnoreCase)
                || string.Equals(ModelApiName(m), name, StringComparison.OrdinalIgnoreCase))
            { tag = m.Tag; return true; }
        }
        error = $"未知模型「{name}」。可用模型：" + string.Join("、", AvailableModelNames(models));
        return false;
    }

    // GET /v1/models 的应答体：由壳自己给（不用加载模型）。只列「已就绪」的——未就绪的列了也加载不起来。
    // 第 0 轮 DEC-094：owned_by 从网关 hooks 取（以前写死 "strata"，那是单例网关时代的遗留）
    internal static string BuildModelsJson(IEnumerable<ModelEntry> models, string ownedBy = "strata")
    {
        var arr = new JsonArray();
        long created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (var m in models.Where(m => m.Ready))
        {
            arr.Add(new JsonObject
            {
                ["id"] = ModelApiName(m),
                ["object"] = "model",
                ["created"] = created,
                ["owned_by"] = ownedBy,
            });
        }
        return new JsonObject { ["object"] = "list", ["data"] = arr }
            .ToJsonString(new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    static IEnumerable<string> AvailableModelNames(IEnumerable<ModelEntry> models)
        => models.Where(m => m.Ready).Select(ModelApiName);

    // 网关在转发前调它：wake=true（/v1 请求）才允许加载/切换模型；wake=false 只认已经跑起来的那个。
    // 第 0 轮 DEC-094：这一段以前直接摸 _models / _process / _runningTag / InternalPortValue / WakeAsync / StopServer，
    // 也就是"上游只可能是 Strata"；现在全部走 hooks，网关这一头不认识任何模块。
    async Task<int> EnsureUpstreamForRequest(GatewayHooks h, string? modelName, bool wake, CancellationToken ct)
    {
        var port = h.UpstreamPort();
        string current;
        try { current = await UiAsync(h.CurrentTag); } catch { current = ""; }
        if (!TryResolveRequestTag(h.Models(), current, modelName, out var tag, out var resolveError))
            throw new GatewayRequestException(400, resolveError);
        if (tag.Length == 0) throw new InvalidOperationException("还没有选模型：请先在界面上选一个。");

        // DEC-061：起/切进程这一段要**单飞**。门内只做「没有就拉起、要的是别的模型就停旧起新」，
        // 等 ready 放到门外 —— 同模型的并发请求一起等同一个信号（DEC-003 第 5 条），而不是第二个请求撞上
        // StartService 的「服务已经在运行。」被回成 503，或在「切模型」路上把前一个刚起好的服务再停一遍。
        if (!h.UpstreamRunning())
        {
            if (!wake) throw new InvalidOperationException("模型没在跑，这个路径不唤醒模型（只有 /v1 的请求会加载模型）");
        }
        else if (!wake || h.RunningTag() == tag)
        {
            // 上游在跑：不唤醒的路径原样转发给它；唤醒的路径要的就是当前模型 → 只需等它 ready
            await h.WaitReady();
            return port;
        }

        var gate = WakeGateFor(h.Key);
        // DEC-160：排在这道单飞门外面也算"还没轮到起件"—— 客户端走了就不要再进去（进去也只会替没人等的请求起进程）
        await gate.WaitAsync(ct);
        try
        {
            // DEC-122①：这一次**真要从头起一件**（两种情况：原本没跑 / 要的是另一件先停旧起新）。
            // 日志要说清"按什么的参数起的"——远端请求看到的正是这一族**文件里的那一份**，
            // 不是界面上那格此刻显示的值（他那次"没改配置只点了升级，速度却大降"就卡在这里没人说得清）。
            // 只在真起一次时报：同件已在跑那条早退分支不进这里、手动「启动」也不走这条路。
            var wakeNote = h.WakeFromHomeNote?.Invoke(tag);
            if (!h.UpstreamRunning())
            {
                if (wakeNote is not null) h.Log(wakeNote);
                await WakeAsync(h, tag, ct);            // 门里复查：等锁期间前一个请求可能已经把它起好了
            }
            else if (wake && h.RunningTag() != tag)
            {
                h.Log(SwitchToWakeLine(tag, h.RunningTag(), wakeNote));   // DEC-122①：取代原来只说"先停旧的"那行
                await h.StopUpstreamAndWait();   // 等旧进程真退出（原口径：WaitForExit(20000)），再拉新的
                await WakeAsync(h, tag, ct);
            }
        }
        finally { gate.Release(); }

        try { await h.WaitReady(); }
        catch (Exception ex)
        {
            // 「启动 → 秒退」的退避记在**这个模块的这个模型**名下（DEC-061：以前全局一份，A 起不来会连累 B）
            _wakeBackoff[h.Key + "/" + tag] = new WakeBackoff(DateTime.UtcNow.Ticks, ex.Message);
            throw;
        }
        // DEC-125①：走到这里 = **这一击真把某一件加载起来了**（原本没跑、或要的是另一件而停旧起新）。
        // 于是主区跟着切到那一页（他的口径："只跟随真被加载 + 窗口可见才切"）。
        // 上面那条"上游在跑、要的就是这一件"的早退分支**不走这里** = 只是转发，不该抢页面。
        // DEC-155：连"要跟的那一件"（tag）一起交出去，跟页之外还要把它选上下拉、换成它的家底。
        // DEC-165（清单第 5 条 a 档 = 他批的"可以做"）：这一发**已经走了**（客户端断开 / 总时限到点把令牌取消了），
        // 而件还是被拉起来了 —— 只在这时候收手：网关此刻**没有别的在途请求**（粗判据，口径同 DEC-157 那条：
        // 宁可留着也不误停）→ 把刚起来的那一件收掉、把显存还回去。
        // 收掉了就**不跟页**（没人看着，别为一个马上要收的东西切页面）。判据与落点都在 MaybeUnloadAbandonedStarted 一个家。
        if (MaybeUnloadAbandonedStarted(View(h.Key), tag, ct.IsCancellationRequested, GatewayFor(h.Key)?.InFlightNow() ?? 0))
            // DEC-167（第 45 轮审查 F2）：真去收了之后，这一发就当**没发生过** —— 抛取消早退，不 return port。
            // 旧写法 return port：网关接着把请求转发给"刚被收掉"的上游（`UiPost` 异步落盘，与 `ConnectAsync`
            // 竞态）—— 客户端已经走了，转发必失败，只多一行「网关转发失败」。抛上去由网关那条
            // `catch (OperationCanceledException)` 收口 = 不写响应、只留一行落点（那一发本来就没人等）。
            throw new OperationCanceledException("这一发请求已经走了：刚拉起的那一件已收掉并归还显存");
        UiPost(() => FollowModuleAfterWake(h.Key, tag));
        return port;
    }

    // ---------------- DEC-165：幽灵起件剩的那半条（件已开始加载之后对面才走） ----------------

    /// <summary>证人位：这一档真走到了（自检按它断"不是空判据"）。</summary>
    internal static int UnloadAbandonedStartedCallsForTest;

    /// <summary>纯判据：这一发已经走了、件还是被拉起来了 —— 要不要把它收掉。两个条件缺一不可：
    /// ① 这一发的令牌确实被取消（客户端断开 / 总时限到点）；② 网关此刻**没有别的在途请求**。
    /// 只有②成立才收：那一件可能正被另一发请求用着，宁可留着也不误停。</summary>
    internal static bool ShouldUnloadAbandonedStart(bool abandoned, int othersInFlight)
        => abandoned && othersInFlight == 0;

    /// <summary>那句话的唯一写法（模块名与件名都要点名，别写成"某一件"）。</summary>
    internal static string AbandonedStartLine(string displayName, string tag)
        => $"这一发请求已经走了（断开或超时），{displayName} 的 {tag} 还是被拉起来了：没有别人在等它，收掉并归还显存。";

    /// <summary>判据 + 落点一个家（自检接缝走的也是它，不是另写一套）。返回 true = 真去收了。</summary>
    internal bool MaybeUnloadAbandonedStarted(ModuleView v, string tag, bool abandoned, int othersInFlight)
    {
        if (!ShouldUnloadAbandonedStart(abandoned, othersInFlight)) return false;
        var h = v.Gateway;
        if (h is null) return false;                 // 本模块不走壳网关（llama 的路由归引擎自己实现）= 没有这一档
        UnloadAbandonedStartedCallsForTest++;
        h.Log(AbandonedStartLine(v.DisplayName, tag));
        UiPost(() => h.UnloadAbandonedStarted(tag));
        return true;
    }

    /// <summary>自检接缝：走一次真路径那一步（判据 / 日志 / 那一击全过本体）。</summary>
    internal bool TryUnloadAbandonedStartedForTest(string key, string tag, bool abandoned, int othersInFlight)
        => MaybeUnloadAbandonedStarted(View(key), tag, abandoned, othersInFlight);

    /// <summary>DEC-167（第 45 轮只读审查 F1）：这一次唤醒失败要不要记 5 秒退避。**取消不记**。
    /// 旧写法把 `OperationCanceledException`（"A task was canceled."）也记进去 → 客户端 504 / 挂断之后
    /// 5 秒内重试同一模型会吃到退避档，回一句 503「无法唤醒模型：A task was canceled.」——
    /// 英文异常原文漏到中文界面，而那 5 秒禁的是一次**与模型好坏无关**的取消。
    /// 口径：退避只记「这件起不来」的证据；这一发没人等了（`ct` 已取消）不算证据。
    /// 纯函数，自检四面喂；接在 <see cref="WakeAsync"/> 的 catch 上（漏接 = 白判，自检有源码扫描钉着）。</summary>
    internal static bool WakeFailureCountsAsModelIssue(Exception ex, CancellationToken ct)
        => ex is not OperationCanceledException || !ct.IsCancellationRequested;

    // 拉起上游（DEC-061：**不再在这里等 ready**，等 ready 由调用方在单飞门外做，并发请求才能共享同一个等待）。
    // 失败按模型记 5 秒退避，避免客户端重试把「启动 → 秒退」打成死循环。
    // DEC-160：<paramref name="ct"/> = 这一发请求的令牌，一路交给各模块那一行的 Wake（→ GpuGateAsync 那一圈）。
    async Task WakeAsync(GatewayHooks h, string tag, CancellationToken ct)
    {
        // 名字对但配置没配好：这是客户端能看懂的问题，回 400，也别记进退避
        var entry = h.Models().FirstOrDefault(m => m.Tag == tag);
        if (entry is not null && !entry.Ready)
            throw new GatewayRequestException(400, $"模型 {tag} 还不完整：{entry.Note}（先用「配置新模型」跑一次 SETUP）");
        if (WakeBackedOff(h.Key, tag, out var backoff)) throw new InvalidOperationException(backoff);
        ct.ThrowIfCancellationRequested();          // 已经没人等了 = 这一件不起，也别记进退避
        try
        {
            await h.Wake(tag, ct);
            _wakeBackoff.TryRemove(h.Key + "/" + tag, out _);
        }
        catch (Exception ex)
        {
            // DEC-167（第 45 轮审查 F1）：取消（这一发已经没人等了 = 客户端挂断 / 总时限到点）**不记退避**
            // —— 旧写法把它也记进去，客户端 504 后 5 秒内重试会吃到 "A task was canceled." 那句退避。
            // 也不顺手清退避（没证据说这件好了）；原样上抛，由网关那条 catch 走"不写响应、只留一行落点"。
            if (WakeFailureCountsAsModelIssue(ex, ct))
                _wakeBackoff[h.Key + "/" + tag] = new WakeBackoff(DateTime.UtcNow.Ticks, ex.Message);
            throw;
        }
    }

    // 服务在加载中时，并发请求一起等 ready —— 不能拿到端口就去连（那时上游还没 bind，会 502）
    async Task WaitReadyAsync()
    {
        var ready = _readyTcs;
        if (ready is not null) await ready.Task;
    }

    /// <summary>空闲停服那句日志的唯一写法（纯函数，自检直接喂分钟数）—— 数字必须由调用方从**本模块那道阈值**换算。</summary>
    internal static string IdleStopLine(int minutes) =>
        $"空闲停服：超过 {minutes} 分钟没有请求，停止服务释放显存（网关仍在监听，下次请求会自动唤醒）";

    // 空闲停服：网关那边到点了就调它。返回 true = 这次真的停了东西
    bool StopUpstreamForIdle(GatewayHooks h)
    {
        if (!h.UpstreamRunning()) return false;
        // DEC-137（A-2）：这一句以前恒取 Strata 的 IdleStopMinutes()，而网关构造时吃的是 h.IdleTimeout()
        //（Ninfer 那两个阈值是每后端各存一份的）—— 于是 Ninfer 页上出现两行两个数：网关那行报对的、
        // 这一行报 Strata 的。判据取回同一个家：分钟数从 h.IdleTimeout() 换算。
        var minutes = (int)h.IdleTimeout().TotalMinutes;
        UiPost(() =>
        {
            h.Log(IdleStopLine(minutes));
            h.StopUpstream();
        });
        return true;
    }

}
