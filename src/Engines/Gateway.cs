using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MultiLauncher;

// 常驻网关（只在界面勾了「请求唤醒」时启用）：
//   壳监听公开端口，Strata 跑内部端口。请求到达时：请求体里的 model 能对上一个模型就切过去，
//   上游没起来就拉起（唤醒），然后把这一个请求原样转发、响应字节流原样回传（SSE 不受影响）。
// 网关自己不碰界面、也不认识 Strata：拉起/停止上游都由外部注入的委托完成，
// 所以自检可以注入一个假上游，把「唤醒 -> 切换 -> 流式转发 -> 空闲停服」整条路跑通。
// 只做最小 HTTP 解析（见 HttpStreams）：请求行 / 头 / 体；其余字节一律不解释、不改写。
// 网关可以直接回给客户端的请求级错误（例如请求了一个没配置的模型名 -> 400）
internal sealed class GatewayRequestException : Exception
{
    public int Status { get; }
    public GatewayRequestException(int status, string message) : base(message) => Status = status;
}

internal sealed class Gateway : IDisposable
{
    // 上游拉起委托：modelName = 请求里写的模型名（可能为 null）；wake = 这个路径允不允许唤醒模型。
    // 返回上游就绪后的内部端口；「不唤醒又没在跑」时抛异常（消息进 503 响应与日志）。
    // DEC-160（幽灵起件那半条）：第三个参数 = **这一发请求自己的**取消令牌。排队那一圈、单飞门那一段、
    // 以及"轮到自己起件"那一步全吃它 —— 客户端走了或这一发的总时限到点 = 这一件就不起了。
    // 以前这里只有网关生命周期的 token：超时/断开只让网关回一句错，那条排队任务照跑到底，
    // 于是会替一个已经拿到 504 的客户端把件读进显存（第 36/37 轮登记的那一半）。
    public delegate Task<int> UpstreamStarter(string? modelName, bool wake, CancellationToken ct);

    readonly string _bindHost;
    readonly int _publicPort;
    readonly TimeSpan _idleTimeout;
    readonly TimeSpan _wakeTimeout;
    readonly UpstreamStarter _start;
    readonly Func<bool> _stop;
    readonly Func<string> _modelsJson;      // GET /v1/models 的应答体（由壳按扫描结果生成，不加载模型）
    readonly Action<string> _log;
    // DEC-112：这条请求是不是"只是转发给已经在跑的那一件"。为真时那一行加一个后缀，界面按噪声形状滤掉、文件照写。
    // 判据不在网关里算（网关不认识任何模块），由外部注入 —— 与 _start 走同一套 hooks，别长第二套算法。
    readonly Func<string?, bool, bool>? _isRepeat;
    // 上游是不是"正在加载中"（进程起来了、还没就绪）。空闲判定要在这一档上停一拍 ——
    // 加载期间锚点可能是旧的，照旧锚点到点就会把正在读权重的进程停掉。
    readonly Func<bool>? _upstreamBusy;

    /// <summary>「上游已经在跑这一件」那句后缀的唯一写法（网关写、`IsNoise` 滤，两处共用这一个家）。</summary>
    internal const string GatewayRepeatSuffix = "（上游已在跑这一件，只转发）";

    TcpListener? _listener;
    CancellationTokenSource? _cts;
    long _lastActivityTicks = DateTime.UtcNow.Ticks;   // 最近一次请求（含在途）时刻，供空闲停服用
    int _active;
    int _stopCalls;                                     // 空闲停服实际触发次数（自检用）

    public Gateway(string bindHost, int publicPort, TimeSpan idleTimeout, TimeSpan wakeTimeout,
                   UpstreamStarter start, Func<bool> stop, Func<string> modelsJson, Action<string> log,
                   Func<string?, bool, bool>? isRepeat = null, Func<bool>? upstreamBusy = null)
    {
        _bindHost = bindHost ?? "";
        _publicPort = publicPort;
        _idleTimeout = idleTimeout;
        _wakeTimeout = wakeTimeout;
        _start = start;
        _stop = stop;
        _modelsJson = modelsJson;
        _log = log;
        _isRepeat = isRepeat;
        _upstreamBusy = upstreamBusy;
    }

    public int Port => _publicPort;
    /// <summary>请求行的唯一写法（纯函数，自检直接喂样本）：非唤醒路径带「这个路径不唤醒模型」；
    /// 唤醒路径但上游已经在跑这一件时带 `GatewayRepeatSuffix`（DEC-112，界面按噪声滤、文件照写）。</summary>
    internal static string RequestLine(string method, string target, string? modelName, bool wake, bool repeat) =>
        $"网关收到 {method} {target}" + (modelName is null ? "" : $"（model={modelName}）")
        + (wake ? "" : "（这个路径不唤醒模型）") + (repeat ? GatewayRepeatSuffix : "");
    /// <summary>网关起来那一行的唯一写法（两族共用一句：**不点名模块**，归属由日志通道分）。
    /// 抽出来是为了能被自检钉住 —— 以前这里写死「Strata 跑内部端口」，Ninfer 的网关一接上派发就在 Ninfer 页冒出来。</summary>
    internal static string GatewayListenLine(System.Net.IPAddress bindAddress, int publicPort) =>
        $"$ 网关已监听 {bindAddress}:{publicPort}（引擎跑内部端口；请求到达时唤醒或切换模型）";
    public bool Running => _listener is not null;
    public int ActiveRequests => Volatile.Read(ref _active);
    public int IdleStopCount => Volatile.Read(ref _stopCalls);
    // 自检用：最近一次「算活动」的时刻（DEC-061 之后只有唤醒路径会刷新它，非唤醒轮询不该动）
    internal long LastActivityTicksForTest => Volatile.Read(ref _lastActivityTicks);

    /// <summary>壳**自己**起了一件（手动「启动」/ 卡3「加载」/ 切件）时刷新空闲锚点。
    /// 这一路不走网关的唤醒路径，不补这一下锚点就停在网关启动那一刻 —— 用户实跑抓到的那次
    /// 「07:45:28 就绪、07:50:06 就被按『空闲 10 分钟』停掉」正是它（锚点 = 07:40:05 网关启动）。
    /// DEC-061 的口径不动：非唤醒轮询（/v1/models、/health）仍然不刷，这个入口只由壳在真起件 / 就绪时调。</summary>
    public void NoteUpstreamActivity() => Volatile.Write(ref _lastActivityTicks, DateTime.UtcNow.Ticks);

    public void Start()
    {
        if (_listener is not null) return;
        var listener = new TcpListener(BindAddress(_bindHost), _publicPort);
        listener.Start();
        _listener = listener;
        _cts = new CancellationTokenSource();
        Volatile.Write(ref _lastActivityTicks, DateTime.UtcNow.Ticks);
        var ct = _cts.Token;
        _ = Task.Run(() => AcceptLoopAsync(listener, ct));
        _ = Task.Run(() => IdleLoopAsync(ct));
        // 这一句两族共用（Strata / Ninfer 各挂自己那份网关），所以**不点名模块** ——
        // 写死「Strata 跑内部端口」会在 Ninfer 页上冒出来（DEC-100 接上派发之后用户实跑抓到的第一条）。
        // 归属由日志通道分（h.Log = LogS / LogN），界面本来就只在自己的页上显示自己那一路。
        _log(GatewayListenLine(BindAddress(_bindHost), _publicPort));
    }

    public void Dispose()
    {
        var listener = _listener;
        _listener = null;
        try { _cts?.Cancel(); } catch { /* 已释放 */ }
        try { listener?.Stop(); } catch { /* 已释放 */ }
        _cts?.Dispose();
        _cts = null;
    }

    // 只有"会真的生成内容"的推理路径才唤醒模型：/v1/models 由网关自己回答（要列表不该加载 50 GB），
    // /metrics、/health 这类探活路径也不唤醒 —— 否则监控轮询会把刚空闲停掉的模型又吵起来。
    static readonly string[] WakePaths =
        { "/v1/chat/completions", "/v1/completions", "/v1/messages", "/v1/embeddings", "/v1/rerank", "/v1/responses" };

    internal static bool IsWakePath(string target)
        => WakePaths.Contains(target.Split('?', 2)[0], StringComparer.OrdinalIgnoreCase);

    static IPAddress BindAddress(string host) => host.Trim() switch
    {
        "" or "0.0.0.0" => IPAddress.Any,
        "::" => IPAddress.IPv6Any,
        var h => IPAddress.TryParse(h, out var ip) ? ip : IPAddress.Loopback,
    };

    // ---------------- 接受与转发 ----------------

    async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(ct); }
            // DEC-061：以前是一句 catch { return; } 把一切都吞了——accept 抛出非取消类异常（例如连接队列打满）
            // 会让循环永久退出，而 _listener 还留着，Running 继续报 true：端口被一个不再接连接的监听占死，
            // 界面按"网关在跑"显示，日志一行没有。区分三种：正常停 / 监听被关 / 真出错（记一行并让 Running 变 false）。
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (ObjectDisposedException) { return; }
            catch (Exception ex)
            {
                _log("网关接受连接失败，监听已停止：" + ex.Message);
                _listener = null;
                return;
            }
            _ = Task.Run(() => HandleClientAsync(client, ct));
        }
    }

    async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        bool counted = false;
        bool wakeReq = false;      // DEC-061：只有唤醒路径才算「有活动」——finally 里刷活动时刻要用它当判据
        try
        {
            using (client)
            {
                client.NoDelay = true;
                var down = new NetReader(client.GetStream());
                var headBytes = await down.ReadHeadAsync(ct);
                if (headBytes is null) return;
                // 真正读到请求才算「在途」：客户端连上却不发字的空闲连接不该挡住空闲停服
                Interlocked.Increment(ref _active);
                counted = true;
                // DEC-061：活动时刻**不**在这里刷新（以前所有请求都刷）。/v1/models、/health 这类客户端轮询
                // 每几秒来一次，会把空闲计时一直顶掉 → 「空闲停服(分钟)」在有客户端挂着时永不触发，显存不释放。
                // 只有唤醒路径（下面算出的 wake）才算活动，与 DEC-003 第 2 条「轮询不该顶掉空闲停服」同口径。
                var lines = HttpStreams.HeadLines(headBytes);
                var requestLine = lines.Length > 0 ? lines[0] : "";
                var headers = HttpStreams.ParseHeaders(lines);
                var parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var method = parts.Length > 0 ? parts[0] : "";
                var target = parts.Length > 1 ? parts[1] : "";

                // GET /v1/models 由网关自己回答：列表来自壳扫描到的配置，不需要（也不该）加载模型
                if (method.Equals("GET", StringComparison.OrdinalIgnoreCase)
                    && target.Split('?', 2)[0].StartsWith("/v1/models", StringComparison.OrdinalIgnoreCase))
                {
                    await ServeModelsAsync(down, target, ct);
                    return;
                }

                // DEC-061：声明了正文但超过上限时不能再往下走——ReadBodyAsync 会把正文静默丢成空数组，
                // 而 BuildUpstreamHead 把 Content-Length 一行不动抄给上游，上游就一直等那批字节直到超时。
                // 分块体不受这条影响（ReadChunkedAsync 原样搬）。
                if (!HttpStreams.HasChunked(headers)
                    && long.TryParse(HttpStreams.Header(headers, "Content-Length"), out var claimed)
                    && claimed > HttpStreams.MaxBodyBytes)
                {
                    await WriteErrorAsync(down, 413, $"请求体 {claimed} 字节超过网关上限 {HttpStreams.MaxBodyBytes} 字节，这个请求不转发。", ct);
                    return;
                }

                var body = await HttpStreams.ReadBodyAsync(down, headers, ct);
                var modelName = ExtractModel(body);
                bool wake = wakeReq = IsWakePath(target);
                // DEC-061：活动时刻只由**唤醒路径**刷新——/metrics、/health 与本地回答的 /v1/models 这类轮询
                // 不该把「空闲停服」顶掉（DEC-003 第 2 条定的判据本来就是"只有推理路径算请求"）。
                if (wake) Volatile.Write(ref _lastActivityTicks, DateTime.UtcNow.Ticks);
                // DEC-112：上游正在跑的就是这一件时，这条请求只是转发 —— 那句「网关收到 POST…」每个请求重复一遍，
                // 用户实跑点名不必展示。判据由外部注入（网关不认识模块），带后缀上屏、界面按噪声形状滤掉，文件照写全量。
                bool repeat = wake && _isRepeat is not null && _isRepeat(modelName, true);
                _log(RequestLine(method, target, modelName, wake, repeat));

                int upstreamPort;
                // DEC-160：**一发请求一个令牌**（挂在网关生命周期的 token 上）。三种收手：
                //   ① 网关自己停了（原本就有）② 这一发的总时限到点 ③ 客户端把连接挂了（旧启动器那类 ~60 秒就断）。
                // ②③ 才是要治的那一半：以前这两种只让网关回一句错，**排队那一圈与起件那一步照跑**，
                // 结果就是"没人等的请求把模型读进了显存"（第 36/37 轮登记的那条幽灵起件）。
                using var reqCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                using var watchCts = CancellationTokenSource.CreateLinkedTokenSource(reqCts.Token);
                _ = Task.Run(() => WatchClientHangupAsync(client, reqCts, watchCts.Token));
                Task<int>? wakeTask = null;
                try
                {
                    // 这一句**必须留在 try 里面**：起件那一枪有"当场就抛"的形状（模型没配好 = InvalidOperationException），
                    // 挪到 try 外面会绕过下面所有 catch → 连接被直接关掉、客户端只看见传输层错误（本轮实测红过一次：
                    // 「上游起不来 -> 503」那条变成 An error occurred while sending the request，还连带跳过后面的用例）
                    wakeTask = _start(modelName, wake, reqCts.Token);
                    upstreamPort = await wakeTask.WaitAsync(_wakeTimeout, ct);
                }
                catch (GatewayRequestException ex)
                {
                    await WriteErrorAsync(down, ex.Status, ex.Message, ct);
                    return;
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    // 对面已经没人听，或总时限到点后我们主动收的 —— 不写响应（写了也是失败），只留一行落点。
                    // **这一条必须在 TimeoutException 之前接**：reqCts 取消后排队那一圈抛的就是它。
                    if (reqCts.IsCancellationRequested) _log("客户端已断开或这一发超时：唤醒收手，请求没有转发出去（件若已拉起，壳会收掉并归还显存）");
                    return;
                }
                catch (TimeoutException)
                {
                    reqCts.Cancel();          // 总时限到点：那条排队/等待立刻收手，别再替这条连接起件
                    // 收手之后的异常没有人在等了 —— 挂一条续接观察掉，别让它变成未观察任务异常
                    _ = wakeTask!.ContinueWith(static t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                    await WriteErrorAsync(down, 504, $"等待模型就绪超过 {_wakeTimeout.TotalSeconds:0} 秒，请稍后重试。", ct);
                    return;
                }
                catch (Exception ex)
                {
                    // 唤醒失败的加前缀；「这个路径本来就不唤醒」的用原话（别写成"无法唤醒"自相矛盾）
                    await WriteErrorAsync(down, 503, (wake ? "无法唤醒模型：" : "") + ex.Message, ct);
                    return;
                }
                finally
                {
                    // 起件这一段过去了 = 不用再盯着客户端挂没挂（转发阶段的断开由那一段的读写自己报错）
                    watchCts.Cancel();
                }

                using var up = new TcpClient();
                try
                {
                    await up.ConnectAsync(IPAddress.Loopback, upstreamPort, ct);
                }
                catch (Exception ex)
                {
                    await WriteErrorAsync(down, 502, $"连不上内部服务 127.0.0.1:{upstreamPort}：{ex.Message}", ct);
                    return;
                }
                up.NoDelay = true;
                var upstream = new NetReader(up.GetStream());
                await upstream.WriteAsync(BuildUpstreamHead(requestLine, headers, upstreamPort), ct);
                if (body.Length > 0) await upstream.WriteAsync(body, ct);
                await upstream.FlushAsync(ct);
                await RelayResponseAsync(upstream, down, method.Equals("HEAD", StringComparison.OrdinalIgnoreCase), ct);
            }
        }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested) _log("网关转发失败: " + ex.Message);
        }
        finally
        {
            if (counted) Interlocked.Decrement(ref _active);
            // DEC-061：这句以前无条件执行，等于每个请求（含 /v1/models、/health 轮询）都刷新活动时刻，
            // 「空闲停服(分钟)」在有客户端挂着时永不触发。现在只由唤醒路径刷新。
            if (wakeReq) Volatile.Write(ref _lastActivityTicks, DateTime.UtcNow.Ticks);
        }
    }

    /// <summary>这一发请求的总时限（DEC-159 那个家的另一半：到点就 <c>TimeoutException</c>，
    /// 而 DEC-160 起那条唤醒任务会随取消令牌一起收手）。</summary>
    internal TimeSpan WakeTimeout => _wakeTimeout;
    /// <summary>此刻在途的请求数（读到完整请求头才计）。DEC-165：壳那一步"件已起、请求已走"要不要收手，
    /// 拿它当**粗判据**（口径同 DEC-157 那条：宁可留着也不误停）。</summary>
    internal int InFlightNow() => Volatile.Read(ref _active);

    /// <summary>「客户端把连接挂了」的判据（纯函数，四面喂数）。
    /// 半关（读完请求就发 FIN）不认 —— 只有套接字**可读且一个字节都没有**才是对面走了：
    /// 我们的请求体已经读完，此时读侧有货只可能是 keep-alive 的下一发，没货 = FIN/RST。</summary>
    internal static bool ClientHungUp(bool connected, bool pollReadable, int available) =>
        !connected || (pollReadable && available == 0);

    /// <summary>挂断探测器：只在**等起件那一段**跑（300ms 一拍、不读任何字节 —— 读就会把下一发请求吃掉）。
    /// 看到对面走了就取消这一发的令牌；套接字被 Dispose（转发阶段结束 / 连接已回收）就当没看见，安静退出。</summary>
    async Task WatchClientHangupAsync(TcpClient client, CancellationTokenSource reqCts, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            bool hung;
            try
            {
                var s = client.Client;
                hung = ClientHungUp(s.Connected, s.Poll(0, SelectMode.SelectRead), s.Available);
            }
            catch { return; }          // ObjectDisposedException / 已断开：这段本来就结束了
            if (hung)
            {
                try { reqCts.Cancel(); } catch (ObjectDisposedException) { }
                return;
            }
            try { await Task.Delay(300, stop); }
            catch (OperationCanceledException) { return; }
        }
    }

    static string? ExtractModel(byte[] body)
    {
        if (body.Length == 0) return null;
        try
        {
            var node = JsonNode.Parse(Encoding.UTF8.GetString(body));
            var model = node?["model"]?.GetValue<string>();
            return string.IsNullOrWhiteSpace(model) ? null : model.Trim();
        }
        catch { return null; }   // 不是 JSON（例如 /v1/models）就交给当前模型处理
    }

    // 转发给上游时换掉 Host（上游是 127.0.0.1:<内部端口>）并要求它收尾关连接，壳这边按长度/分块收响应体
    static byte[] BuildUpstreamHead(string requestLine, List<KeyValuePair<string, string>> headers, int port)
    {
        var sb = new StringBuilder();
        sb.Append(requestLine).Append("\r\n");
        foreach (var h in headers)
        {
            if (h.Key.Equals("Host", StringComparison.OrdinalIgnoreCase)) continue;
            if (h.Key.Equals("Connection", StringComparison.OrdinalIgnoreCase)) continue;
            sb.Append(h.Key).Append(": ").Append(h.Value).Append("\r\n");
        }
        sb.Append("Host: 127.0.0.1:").Append(port).Append("\r\n");
        sb.Append("Connection: close\r\n\r\n");
        return Encoding.Latin1.GetBytes(sb.ToString());
    }

    // ---------------- 响应 ----------------

    // 响应头原样回给客户端；响应体按 Content-Length / 分块 / 读到连接关闭三种收尾方式转发（SSE 是后两种）
    static async Task RelayResponseAsync(NetReader up, NetReader down, bool headRequest, CancellationToken ct)
    {
        var headBytes = await up.ReadHeadAsync(ct);
        if (headBytes is null) return;
        var lines = HttpStreams.HeadLines(headBytes);
        var status = lines.Length > 0 ? lines[0] : "";
        var headers = HttpStreams.ParseHeaders(lines);
        bool chunked = HttpStreams.HasChunked(headers);
        long.TryParse(HttpStreams.Header(headers, "Content-Length"), out var contentLength);
        bool bodyless = headRequest || status.Contains(" 204 ", StringComparison.Ordinal) || status.Contains(" 304 ", StringComparison.Ordinal);

        await down.WriteAsync(headBytes, ct);
        await down.FlushAsync(ct);
        if (bodyless) return;
        if (chunked) { await HttpStreams.CopyChunkedAsync(up, down, ct); return; }
        if (contentLength > 0) { await HttpStreams.CopyExactlyAsync(up, down, contentLength, ct); return; }
        await up.CopyToEndAsync(down, ct);
    }

    // GET /v1/models 与 GET /v1/models/<id>：列表本地给，具体某个 id 不单独支持（客户端只用列表）
    async Task ServeModelsAsync(NetReader down, string target, CancellationToken ct)
    {
        var path = target.Split('?', 2)[0];
        if (path.TrimEnd('/').Equals("/v1/models", StringComparison.OrdinalIgnoreCase))
        {
            _log("网关本地回答 GET /v1/models（不加载模型）");
            await WriteJsonAsync(down, 200, _modelsJson(), ct);
        }
        else await WriteErrorAsync(down, 404, $"没有这个接口：{path}（模型列表是 GET /v1/models）", ct);
    }

    static async Task WriteErrorAsync(NetReader s, int status, string message, CancellationToken ct)
    {
        var json = new JsonObject
        {
            ["error"] = new JsonObject { ["message"] = message, ["type"] = "strata_launcher_gateway" },
        }.ToJsonString(new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });   // 中文错误信息原样可读
        await WriteJsonAsync(s, status, json, ct);
    }

    static async Task WriteJsonAsync(NetReader s, int status, string json, CancellationToken ct)
    {
        var body = Encoding.UTF8.GetBytes(json);
        var reason = status switch
        {
            200 => "OK", 400 => "Bad Request", 404 => "Not Found",
            502 => "Bad Gateway", 503 => "Service Unavailable", 504 => "Gateway Timeout", _ => "Error",
        };
        var head = $"HTTP/1.1 {status} {reason}\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
        await s.WriteAsync(Encoding.Latin1.GetBytes(head), ct);
        await s.WriteAsync(body, ct);
        await s.FlushAsync(ct);
    }

    // ---------------- 空闲停服 ----------------

    async Task IdleLoopAsync(CancellationToken ct)
    {
        if (_idleTimeout <= TimeSpan.Zero) return;
        var tick = _idleTimeout < TimeSpan.FromSeconds(5) ? _idleTimeout : TimeSpan.FromSeconds(5);
        if (tick < TimeSpan.FromMilliseconds(50)) tick = TimeSpan.FromMilliseconds(50);
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(tick, ct); } catch { return; }
            if (Volatile.Read(ref _active) > 0) continue;   // 有请求在途：这次不判断（也不刷新计时，否则永不空闲）
            // 上游正在加载（进程起来了、还没就绪）时也不判：Strata 要 30 秒~3 分钟才 ready，这期间
            // 锚点可能已经是旧的 —— 照旧锚点到点就会把**正在读权重的那个进程**停掉（用户实跑抓到的
            // 「加载一会儿就自动卸载」）。就绪之后由壳调 NoteUpstreamActivity 把锚点推到此刻。
            if (_upstreamBusy?.Invoke() == true) continue;
            var idle = DateTime.UtcNow - new DateTime(Volatile.Read(ref _lastActivityTicks), DateTimeKind.Utc);
            if (idle < _idleTimeout) continue;
            Volatile.Write(ref _lastActivityTicks, DateTime.UtcNow.Ticks);
            try
            {
                if (_stop())
                {
                    Interlocked.Increment(ref _stopCalls);
                    _log($"空闲 {_idleTimeout.TotalMinutes:0.#} 分钟没有请求：已停止服务，下次请求会自动唤醒");
                }
            }
            catch (Exception ex) { _log("空闲停服失败: " + ex.Message); }
        }
    }
}
