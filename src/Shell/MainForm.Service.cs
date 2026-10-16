using System.Diagnostics;
using System.Text.Json.Nodes;

namespace MultiLauncher;

// 服务生命周期：启动命令组装、python serve/server.py 的进程管理、就绪探测与状态灯。
// serve/server.py 自己用 Windows job object（serve/winjob.py）把引擎与视觉编码器绑在它的生命上，
// 所以壳只要结束这个进程（连子树），引擎就会跟着退出。
public partial class MainForm
{
    string _readyUrl = "";
    bool _externalRunning;
    int _serviceBindPort;               // 服务实际监听的端口（0 = 没跑）；llama 选端口时要避开它（见 LlamaPortChoose）
    readonly HttpClient _probe = new() { Timeout = TimeSpan.FromSeconds(3) };

    /// <summary>壳自己的探测请求（DEC-165②）：设了 API Key 就带上 `Authorization: Bearer <key>`。
    /// **为什么必须带**：引擎一设 key，`/health`、`/metrics`、`/v1/models` 也一律要鉴权 —— 不带的话
    /// llama 那条就绪判定永远 401（看起来就是"模型起不来"），状态栏与外部探活一起废。
    /// 没设 key 时发的还是同一条请求（引擎不认这枚头），所以行为一字未变。</summary>
    async Task<HttpResponseMessage> ProbeGetAsync(string url)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (ApiAuthHeaderValue(ApiKeyValue()) is { } auth)
            req.Headers.TryAddWithoutValidation("Authorization", auth);
        return await _probe.SendAsync(req);
    }
    readonly System.Windows.Forms.Timer _probeTimer = new() { Interval = 7000 };

    // 探测/打开网页用的地址：监听 0.0.0.0 时从本机回环访问
    string ProbeHost()
    {
        var host = _txtHost.Text.Trim();
        return host is "" or "0.0.0.0" or "::" ? "127.0.0.1" : host;
    }

    // 服务不是壳拉起的（例如先用 START-HERE.bat 起来的、或壳退出后服务还在）时也要看得出来：
    // 定期探一下 /v1/models，**响应得像 OpenAI 模型列表**才认。只在自己没拉起进程时才探。
    // 用户反馈（2026-10-01）：按钮状态要跟随实际。判据不能只看 HTTP 200 —— 用户机器上 8081 常驻着
    // cc-switch（D:\CC Switch\cc-switch.exe），它对 /v1/models 也回 200 JSON，只看状态码就会把它
    // 误报成「已有 Strata 服务」，灯和按钮长期停在"在跑"的样子（这就是"服务已停止但按钮还是已启动态"的真凶）。
    //
    // DEC-160（BUG-068① 结案）：这一族按**进程**收口 = 自检与出图台一次都不真问端口。
    // 为什么这次要补：`ProbeExternal` 读的是真端口，而他壳在跑时那些端口上答的正是标准 OpenAI 列表 /
    // `{"status":"ok"}` = 自检实例把它们认成「外部服务」→ 三条判据（模型行按钮 / 托盘组头 / 停用整组）
    // 在他退壳时绿、在他开壳时红（BUG-068 记的 r28 A 组三条离群红）。"用户机器上恰好有什么在跑"
    // 不许决定用例红绿 —— 同 BUG-052 那句"用户 ini 里恰好有什么"是同族，只是换了环境这一头。
    // 界面那两条形状改由**喂样本**走真路径（<see cref="SetExternalForTest"/>），两面都断得到、且与环境无关。
    internal static bool ExternalProbeDisabledForTest;
    /// <summary>证人位①：这条路真被走到过（闸门开着也计数）—— 没有它，下面那条"恒 0"就是空判据。</summary>
    internal static int ExternalProbeSkippedForTest;
    /// <summary>证人位②：实际发出去过几次端口探测（闸门开着必须恒 0）。</summary>
    internal static int ExternalHttpProbesForTest;

    async void ProbeExternal() => await ProbeExternalAsync();

    /// <summary>DEC-160：把「端口上有一枚不是本壳拉起的实例」这一档喂进**各模块自己那一个判据位**，
    /// 然后走同一条 <see cref="RefreshStatusUi"/> —— 灯 / 左栏按钮 / 模型行按钮 / 托盘组头全都跟着变。
    /// 只喂状态、不造第二套判据（形状仍由 <c>StatusTextForStrata</c> 那三对纯函数给，自检另有穷举钉着）。</summary>
    internal void SetExternalForTest(string key, bool up)
    {
        View(key).SetExternalProbe(up);
        RefreshStatusUi();
    }

    internal async Task ProbeExternalAsync()
    {
        if (ExternalProbeDisabledForTest) { ExternalProbeSkippedForTest++; return; }
        // 模块开关（B 段地基，DEC-024）：停用的模块不探活（全隐身）
        bool strataOn = ModuleRegistry.StrataEnabled(_config.Current);
        bool llamaOn = ModuleRegistry.LlamaEnabled(_config.Current);
        if (strataOn && _process is not { HasExited: false })
        {
            var svcPort = ServicePortValue();
            // 台账互斥（DEC-024 A 段）：Strata 的探测端口是"llama 的"（配置相同、或 llama 实际就落在那）→
            // 不探、不认账。BUG-023 的误报链就是这里：内部端口与 llama 相同 → 探到 llama 的 /v1/models
            // （同为 OpenAI 形状）→ 误报"已有 Strata 服务在跑" → 后面所有唤醒请求被拒。
            bool portIsLlamas =
                (_llamaProcess is { HasExited: false } && _llamaActivePort == svcPort)
                || (_llamaExternal && LlamaPortValue() == svcPort)
                || (int.TryParse(_config.Current.LlamaInternalPort.Trim(), out var lp) && lp == svcPort);
            if (portIsLlamas)
            {
                NoteStrataProbe("llama-port",
                    $"Strata 内部端口 {svcPort} 与 llama 的相同：已跳过 Strata 外部探测（那个端口认作 llama 的）。请到设置页把两者错开——保存时会拦下相同的值");
                if (_externalRunning)
                {
                    _externalRunning = false;
                    SetStoppedState();
                    RefreshStatusUi();
                    LogS("端口上的服务实为 llama：外部服务状态已复位");
                }
            }
            else
            {
                bool up;
                bool llamaLike = false;
                try
                {
                    // 探的是服务本身的端口：网关开着时是内部端口。探公开端口会经网关把模型唤醒，那是副作用
                    ExternalHttpProbesForTest++;
                    using var resp = await ProbeGetAsync($"http://{ProbeHost()}:{svcPort}/v1/models");
                    var body = await resp.Content.ReadAsStringAsync();
                    bool shaped = resp.IsSuccessStatusCode && LooksLikeOpenAiModelsList(body);
                    llamaLike = shaped && LooksLikeLlamaModelsList(body);   // 同族指纹：llama 的列表条目带 status/owned_by
                    up = shaped && !llamaLike;
                }
                catch { up = false; }
                // 一次性说明的复位只在"条件真的消失"时做（每轮先清会把同一条说明刷成每 7 秒一遍）
                if (llamaLike)
                    NoteStrataProbe("llama-like", $"端口 {svcPort} 上像 llama.cpp 的模型列表（不是 Strata）：不认作「已有 Strata 服务」");
                else
                    NoteStrataProbe("", "");
                if (IsDisposed) return;
                // 写回前复查：探测发起时壳没在跑、返回时壳自己拉起了 —— 这次结果作废（别把自家服务当"外部服务"）
                if (_process is { HasExited: false }) { /* 竞态：跳过 */ }
                else if (up != _externalRunning)
                {
                    _externalRunning = up;
                    if (up)
                    {
                        SetState("● 运行中（已有服务）", Color.ForestGreen);
                        LogS($"检测到 {ProbeHost()}:{svcPort} 上已有 Strata 服务在跑（不是本启动器拉起的），可直接用「打开网页」");
                    }
                    else
                    {
                        SetStoppedState();
                        LogS("端口上的服务已不在，状态复位为已停止");
                    }
                    RefreshStatusUi();   // 按钮（停止/卸载）与 PID/端口要跟着外部服务的出现或消失
                }
            }
        }

        // llama 侧：壳没拉起 llama-server 时，看看它的端口上有没有外部实例（只探 /health，不会加载模型）。
        // 同样要形状判据：响应里得有 "status" 字段，别的 HTTP 服务不算。
        // 模块开关（B 段地基）：停用的模块不探活（全隐身）。
        if (llamaOn)
        {
            if (_llamaProcess is null)
            {
                bool up;
                try
                {
                    ExternalHttpProbesForTest++;
                    using var resp = await ProbeGetAsync($"http://{ProbeHost()}:{LlamaPortValue()}/health");
                    var body = await resp.Content.ReadAsStringAsync();
                    up = resp.IsSuccessStatusCode && body.Contains("\"status\"");
                }
                catch { up = false; }
                if (IsDisposed) return;
                if (_llamaProcess is not null)
                {
                    // 竞态：壳刚把 llama-server 拉起 —— 作废这次探测，顺手清掉可能残留的外部标志
                    if (_llamaExternal) { _llamaExternal = false; RefreshStatusUi(); }
                }
                else if (up != _llamaExternal)
                {
                    _llamaExternal = up;
                    if (up) LogL($"检测到 {ProbeHost()}:{LlamaPortValue()} 上已有 llama-server 在跑（不是本启动器拉起的）");
                    RefreshStatusUi();
                }
            }
            else if (_llamaExternal)
            {
                _llamaExternal = false;      // 壳自己在跑：外部标志不成立
                RefreshStatusUi();
            }
        }
        else if (_llamaExternal)
        {
            _llamaExternal = false;          // 模块已停用：外部标志一并清掉（全隐身，不再显示）
            RefreshStatusUi();
        }

        // 壳的或外部的 llama-server 都查一下"哪些模型加载着"（模型行按钮要跟随实际；失败静默）
        if (llamaOn && (_llamaProcess is not null || _llamaExternal)) await RefreshLlamaStatesAsync();
    }

    // 外部服务判据：顶层是 JSON 对象且带 data 数组（OpenAI 的 {"object":"list","data":[…] }）。
    // 不做裸字符串匹配：cc-switch 那类长文本响应里什么词都可能出现（实测它的 body 塞满了英文说明）。
    internal static bool LooksLikeOpenAiModelsList(string? body)
    {
        if (string.IsNullOrWhiteSpace(body) || body.Length > 1_000_000) return false;
        try { return JsonNode.Parse(body) is JsonObject o && o["data"] is JsonArray; }
        catch { return false; }
    }

    // 同族指纹（DEC-024 A 段）：llama.cpp 的模型列表实测条目带 `status:{value,…}` 且 owned_by=llamacpp——
    // 用它把 llama 与 Strata 分开（两边形状本来就一样，只按形状分不出，BUG-023）。
    // 方向是安全的：带指纹的一律按 llama 处理；宁可漏认 Strata，也不误把 llama 当 Strata（会挡住唤醒、还可能误杀）。
    internal static bool LooksLikeLlamaModelsList(string? body)
    {
        if (string.IsNullOrWhiteSpace(body) || body.Length > 1_000_000) return false;
        try
        {
            if (JsonNode.Parse(body) is not JsonObject o || o["data"] is not JsonArray arr) return false;
            foreach (var it in arr)
            {
                if (it is not JsonObject e) continue;
                if (e["status"] is not null) return true;
                if (string.Equals(e["owned_by"]?.GetValue<string>(), "llamacpp", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
        catch { return false; }
    }

    // 探测里的一次性说明：同一句在状态不变时不重复刷；条件消失（key 归空）后下次再出现会再打一遍
    string _strataProbeNote = "";
    void NoteStrataProbe(string key, string message)
    {
        if (_strataProbeNote == key) return;
        _strataProbeNote = key;
        if (message.Length > 0) LogS(message);
    }

    void StartServer()
    {
        if (_process is { HasExited: false }) return;
        // Strata 的路由模式：只把网关立起来，模型等第一个 /v1 请求再加载（B 段：模式按模块各自记忆）
        if (_routeModeStrata)
        {
            if (StrataGateway is null) StartGateway();
            if (StrataGateway is null) return;
            SetStoppedState();
            RefreshRunInfo();
            LogS($"$ 网关已就绪（公开端口 {PublicPortValue()}，Strata 用内部端口 {InternalPortValue()}）：当前不加载模型，/v1 请求到达时自动加载");
            return;
        }
        if (StartService(_config.Current.Model, _config.Current.OpenBrowser, wakeFromRecord: false, out var startErr)) return;
        Dialogs.Message(this, "无法启动", startErr, danger: true);
        LogS("启动失败: " + startErr);
    }

    // 拉起服务：tag 指定跑哪个模型（界面点「启动」和网关的唤醒都走这里）。
    // 返回 false 时 error 是给用户看的原因；成功则等 OnServerLine 的 ready 行（_readyTcs）。
    // DEC-122①：wakeFromRecord 这一击必须表态（没有默认值，形状同 DEC-092 的 SaveConfig）——
    //   true  = 网关唤醒 / 切件：取「配置模板 ⊕ [Model:<tag>] 记录」，界面上没点保存的草稿不参与；
    //   false = 手动「启动」与卡3「加载」：取界面卡现值（他的试验流照旧）。
    internal bool StartService(string tag, bool openBrowser, bool wakeFromRecord, out string error)
    {
        error = "";
        // 模块开关（B 段地基，DEC-024）：停用的模块不起进程（全隐身）
        if (!ModuleRegistry.StrataEnabled(_config.Current))
        {
            error = "Strata 模块未启用——到设置页「通用 → 模块」里开启后再启动。";
            LogS("启动被拒：Strata 模块未启用");
            return false;
        }
        _strataFailed = false;      // 每次启动尝试先清失败标记（状态灯红灯）
        if (_process is { HasExited: false }) { error = "服务已经在运行。"; return false; }
        if (_externalRunning)
        {
            error = "检测到端口上已经有 Strata 服务在跑（不是本启动器拉起的）。\n先停掉它，或者换一个端口再启动。";
            LogS("启动被拒：端口上已有服务在跑");
            return false;
        }
        var s = StartSettingsFor(tag, openBrowser, wakeFromRecord);
        var entry = _models.Find(m => m.Tag == tag);
        if (entry is null)
        {
            error = "还没有模型可选：请先在「模型」里选一个，或用「配置新模型」跑一次 SETUP。";
            return false;
        }
        if (!entry.Ready)
        {
            error = $"模型 {entry.Tag} 还不完整：{entry.Note}\n\n先用「配置新模型」跑一次 SETUP 把它配好。";
            return false;
        }
        // 服务监听哪个端口由网关决定：网关开着就退到内部端口，公开端口留给壳
        var port = ServicePortValue();
        s.Port = port.ToString();
        // 启动前实际占用检查（DEC-024 A 段）：目标端口被另一个模块占着就拒起并说清让谁让路——
        // 以前这种情况会以 python bind 失败/误报"已有服务"的方式爆出来，看不清是谁占了（BUG-023）
        if (PortTakenByOtherModule(port, startingLlama: false) is { } takenBy)
        {
            error = $"{takenBy}，Strata 服务没法起在同一个端口。请到设置页把两个模块的内部端口错开（保存时会拦下相同的值），或先停掉它。";
            LogS("启动被拒：" + takenBy);
            _strataFailed = true;       // 端口冲突 = 异常（状态灯红灯）
            return false;
        }
        // DEC-157（他批的第 1 条）：显存这一关补到这一族 —— **人点按钮那一路 = 当场拒绝并点名**（分入口口径，
        // 支点 I / DEC-083 第 6 条），排队那一档在 StrataWakeUpstream 里。wakeFromRecord = true 是网关唤醒，
        // 那里已经等过了，这里不再拦（否则排队等到头又被自己拒掉）。
        if (!wakeFromRecord && GpuMutexRefusal(StrataKey) is { } held)
        {
            error = held;
            LogS("启动被拒：" + held.Replace("\n", " "));
            _strataFailed = true;       // 与 Ninfer 那条 preflight 同档：起不来就该看得见（红灯 + 弹窗由调用方给）
            return false;
        }
        if (!BuildEffectiveConfig(s, out var eff, out var err))
        {
            error = err;
            _strataFailed = true;
            return false;
        }
        // DEC-097④：壳替人补了 rope 就要说出来 —— 这一行是"为什么这次启动的角度与出厂不同"的唯一落点
        //（读的是刚物化出来的那一份，报的是事实，不是再推一遍）
        if (File.Exists(eff))
        {
            try
            {
                var builtNode = JsonNode.Parse(File.ReadAllText(eff)) as JsonObject;
                var built = (builtNode?["args"] as JsonArray)?.Select(x => x?.GetValue<string>() ?? "").ToList()
                            ?? new List<string>();
                int ri = built.IndexOf("--rope-scale");
                int mi = built.IndexOf("--rope-scaling");
                if (ri >= 0 && ri + 1 < built.Count && mi >= 0 && mi + 1 < built.Count)
                {
                    // 同一趟自动补的两样东西一起交代，别让人去猜"输出怎么变短了"
                    var fit = builtNode?["fit_max_tokens"]?.GetValue<bool?>() == true
                        ? "，并打开 fit_max_tokens（输出上限装不下时截断，而不是回 400）" : "";
                    LogS($"按上下文 {s.Context} 自动写 rope：{built[mi + 1]} 因子 {built[ri + 1]}" + fit +
                         "（上下文超过模型训练长度就必须开；回到训练长度以内会自动去掉）");
                }
            }
            catch { /* 这一行只是交代，读不动不影响启动 */ }
        }
        var python = Path.Combine(s.Root, ".venv", "Scripts", "python.exe");
        if (!File.Exists(python))
        {
            error = "找不到 " + python + "\n\n请先双击一次 START-HERE.bat 把环境建好。";
            _strataFailed = true;
            return false;
        }
        var server = Path.Combine(s.Root, "serve", "server.py");
        if (!File.Exists(server))
        {
            error = "找不到 " + server;
            _strataFailed = true;
            return false;
        }

        var psi = new ProcessStartInfo(python)
        {
            WorkingDirectory = s.Root,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add(server);
        psi.ArgumentList.Add("--engine"); psi.ArgumentList.Add("strata");
        psi.ArgumentList.Add("--config"); psi.ArgumentList.Add(eff);
        psi.ArgumentList.Add("--port"); psi.ArgumentList.Add(s.Port);
        if (s.OpenBrowser) psi.ArgumentList.Add("--open");
        LogS("$ " + python + " " + string.Join(" ", psi.ArgumentList));

        try
        {
            _readyUrl = "";
            _readyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            p.OutputDataReceived += (_, e) => { if (e.Data is not null) OnServerLine(e.Data); };
            p.ErrorDataReceived += (_, e) => { if (e.Data is not null) OnServerLine(e.Data); };
            // DEC-061：回调里要校验「退出的就是当前这一个进程」。旧进程的 Exited 是线程池投递的，
            // 可能排在 UI 队列里新一次 StartService **之后**才落（服务秒退后立刻重试就是这个时序）；
            // 不设防就会把刚拉起来的新服务注销掉：_process=null、端口清零、_readyTcs 抛异常，
            // python 还在跑但壳不再认识它，下次启动被报成「端口上已有外部服务」。
            var me = p;
            p.Exited += (_, _) =>
            {
                try { BeginInvoke(new Action(() => { if (ReferenceEquals(_process, me)) OnServerExited(); })); } catch { /* 窗口已销毁 */ }
            };
            if (!p.Start())
            {
                _readyTcs = null;
                error = "启动失败：进程没有起来";
                LogS(error);
                _strataFailed = true;
                return false;
            }
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            _process = p;
            _serviceBindPort = port;
            _runningTag = tag;
            // 2026-10-15（他实跑报「不到 10 分钟就被按超时卸载」）：手动「启动」/ 卡3「加载」不走网关的
            // 唤醒路径，锚点不补这一下就停在网关启动那一刻（那次实测 = 07:40:05 立网关、07:45:28 就绪、
            // 07:50:06 就被停）。起件成功算一次活动，就绪那一刻（OnServerLine）再补一次。
            StrataGateway?.NoteUpstreamActivity();
            SetButtons(true);
            SetState("● 启动中…", Color.DarkOrange);
            LogS("模型开始加载：首次启动 1-3 分钟，期间电脑可能很卡，属正常现象");
            // 2026-10-02（DEC-033）：启停**不再重扫模型**——这两个时点模型列表不会变，扫了只是刷屏噪音；
            // 拾取新模型由「SETUP 窗口关闭自动重扫」承接（见 RunSetup）
            return true;
        }
        catch (Exception ex)
        {
            _readyTcs = null;
            error = "启动失败: " + ex.Message;
            LogS(error);
            _strataFailed = true;
            SetButtons(false);
            SetStoppedState();
            return false;
        }
    }

    // serve/server.py 就绪时打印的行，例如
    //   ready: http://127.0.0.1:8080/v1  (OpenAI: /v1/chat/completions, Anthropic: /v1/messages, context 131072 tokens)
    // 这是壳判定「已就绪」的唯一依据；抽成静态函数，自检可以直接拿真实格式验证。
    internal static bool TryParseReady(string line, out string url)
    {
        url = "";
        if (!line.StartsWith("ready: ", StringComparison.Ordinal)) return false;
        var rest = line["ready: ".Length..].Trim();
        url = (rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "").TrimEnd(',');
        return url.Length > 0;
    }

    // serve/server.py 的 stdout/stderr 每行都进日志；"ready: http://..." 是它给出的就绪标志。
    // B 段阶段③：引擎行按类型记（strata）——文件进 logs\strata.log；界面只在 Strata 页显示。
    void OnServerLine(string line)
    {
        Log(line, "strata");
        if (TryParseReady(line, out var url))
        {
            _readyTcs?.TrySetResult(true);
            _readyUrl = url;
            _strataFailed = false;      // 就绪即清失败标记（状态灯红灯）
            SetState("● 运行中", Color.ForestGreen);
            LogS("服务已就绪：" + _readyUrl);
            // 就绪 = 空闲计时的真正起点（上面加载那几十秒不该算"没人用"）
            StrataGateway?.NoteUpstreamActivity();
        }
    }

    void OnServerExited()
    {
        int code = -1;
        try { if (_process is not null) code = _process.ExitCode; }
        catch { /* 还没真正启动过 */ }
        _process = null;
        _serviceBindPort = 0;
        _readyUrl = "";
        _runningTag = "";
        _readyTcs?.TrySetException(new InvalidOperationException($"服务进程已退出 (exit code {code})"));
        _readyTcs = null;
        // 状态灯（B 段）：非手动停且非零退出 = 异常（崩溃）
        if (code != 0 && !_strataStopping) _strataFailed = true;
        _strataStopping = false;
        SetButtons(false);
        SetStoppedState();
        LogS($"服务进程已退出 (exit code {code})");
        // 2026-10-02（DEC-033）：停止不再重扫模型（理由同 StartService）
    }

    void StopServer()
    {
        var p = _process;
        if (p is null || p.HasExited)
        {
            SetButtons(false);
            SetStoppedState();
            return;                 // 2026-10-02（DEC-033）：没进程可退时也不再重扫（理由同 StartService）
        }
        LogS("$ 停止服务…");
        SetState("● 停止中…", Color.DarkOrange);
        _strataStopping = true;
        try { p.Kill(true); }
        catch (Exception ex) { _strataStopping = false; LogS("停止失败: " + ex.Message); }
    }

    void RestartServer()
    {
        var p = _process;
        if (p is null || p.HasExited) { StartServer(); return; }
        LogS("$ 重启：先停止当前服务…");
        _strataStopping = true;
        try { p.Kill(true); }
        catch (Exception ex) { _strataStopping = false; LogS("停止失败: " + ex.Message); }
        Task.Run(() =>
        {
            try { p.WaitForExit(15000); } catch { }
            try { BeginInvoke(new Action(() => { if (_process is null || _process.HasExited) StartServer(); })); }
            catch { /* 窗口已销毁 */ }
        });
    }

}
