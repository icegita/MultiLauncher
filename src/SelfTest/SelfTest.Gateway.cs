using System.Net;
using System.Net.Sockets;
using System.Text;

namespace MultiLauncher;

// 网关自检：不加载真模型，用进程内的假上游把路由模式的整条路跑一遍 ——
// 「模型列表本地回答 -> 非推理路径不唤醒 -> 推理请求按 model 唤醒/切换 -> 未知名 400 ->
//  流式转发 -> 空闲停服 -> 停服后再唤醒 -> 起不来/等不到」。
internal static partial class SelfTest
{
    internal static int GatewayTests(StringBuilder sb)
    {
        int failures = 0;
        void W(string s) => sb.AppendLine(s);
        void Check(string name, bool ok, string detail = "")
        {
            if (!ok) failures++;
            W((ok ? "PASS  " : "FAIL  ") + name + (detail.Length > 0 ? "  | " + detail : ""));
        }

        static int FreePort()
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            var p = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return p;
        }
        static StringContent JsonBody(string body) => new(body, Encoding.UTF8, "application/json");

        var models = new List<ModelEntry>
        {
            new("iq3_s", "", "iq3_s", true, ""),
            new("swift", "", "swift-alias", true, ""),
            new("broken", "", "broken-model", false, "pack 目录不存在"),
        };
        bool TryResolve(string? name, out string tag, out string err)
            => MainForm.TryResolveRequestTag(models, "iq3_s", name, out tag, out err);

        Check("请求 model 命中 tag -> 用该模型", TryResolve("swift", out var t1, out _) && t1 == "swift");
        Check("请求 model 命中 model_name -> 用该模型", TryResolve("swift-alias", out var t2, out _) && t2 == "swift");
        Check("请求没写 model -> 用当前模型", TryResolve(null, out var t3, out _) && t3 == "iq3_s");
        Check("请求 model 不在列表里 -> 报错并列出可用模型名",
            !TryResolve("gpt-4o", out _, out var rerr) && rerr.Contains("gpt-4o") && rerr.Contains("iq3_s") && rerr.Contains("swift-alias"), rerr);

        var listJson = MainForm.BuildModelsJson(models);
        W("/v1/models: " + listJson);
        Check("/v1/models 只列已就绪的模型（用 model_name 当 id）",
            listJson.Contains("\"iq3_s\"") && listJson.Contains("\"swift-alias\"") && !listJson.Contains("broken-model"));
        Check("/v1/models 是 OpenAI 的 list 形状", listJson.Contains("\"object\":\"list\"") && listJson.Contains("\"data\""));

        Check("推理路径才唤醒",
            Gateway.IsWakePath("/v1/chat/completions") && Gateway.IsWakePath("/v1/messages") && Gateway.IsWakePath("/v1/embeddings")
            && Gateway.IsWakePath("/v1/chat/completions?x=1"));
        Check("列表 / 探活 / 其它路径不唤醒",
            !Gateway.IsWakePath("/v1/models") && !Gateway.IsWakePath("/v1/models/iq3_s") && !Gateway.IsWakePath("/metrics")
            && !Gateway.IsWakePath("/health") && !Gateway.IsWakePath("/"));

        // 端口校验：内部端口留空/非法/等于公开端口都要回落到「公开端口 + 1」（等于公开端口会把网关和 Strata 撞在一起）
        Check("内部端口留空 -> 公开端口 + 1", MainForm.NormalizeInternalPort(8077, "") == 8078);
        Check("内部端口等于公开端口 -> 改成公开端口 + 1", MainForm.NormalizeInternalPort(8077, "8077") == 8078,
            MainForm.NormalizeInternalPort(8077, "8077").ToString());
        Check("内部端口非法 -> 公开端口 + 1",
            MainForm.NormalizeInternalPort(8077, "70000") == 8078 && MainForm.NormalizeInternalPort(8077, "abc") == 8078);
        Check("内部端口合法且不同 -> 原样使用", MainForm.NormalizeInternalPort(8077, "9099") == 9099);

        async Task ScenarioAsync()
        {
            using var fake = new FakeUpstream();
            fake.Start();
            var starts = new List<string?>();
            var running = "";
            var lines = new System.Collections.Concurrent.ConcurrentQueue<string>();   // DEC-112：抓网关写的每一行
            var idleStopped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var gw = new Gateway("127.0.0.1", FreePort(), TimeSpan.FromMilliseconds(400), TimeSpan.FromSeconds(5),
                (modelName, wake, _) =>
                {
                    if (!MainForm.TryResolveRequestTag(models, "iq3_s", modelName, out var tag, out var bad))
                        throw new GatewayRequestException(400, bad);
                    if (running == tag) return Task.FromResult(fake.Port);          // 同一个模型已经在跑：不重复唤醒
                    if (!wake) throw new InvalidOperationException("模型没在跑，这个路径不唤醒模型（只有推理请求会加载）");
                    starts.Add(tag);
                    running = tag;
                    return Task.FromResult(fake.Port);
                },
                () =>
                {
                    if (running.Length == 0) return false;
                    running = "";
                    idleStopped.TrySetResult(true);
                    return true;
                },
                () => MainForm.BuildModelsJson(models),
                lines.Enqueue,
                // DEC-112：注入的判据走**同一条纯函数**（状态是假件的，算法是真的）—— 免得用例证明的是另一套逻辑
                (model, wake) => MainForm.GatewayRequestIsRepeat(wake, model, running.Length > 0, running, models));
            gw.Start();
            var chat = $"http://127.0.0.1:{gw.Port}/v1/chat/completions";
            var body = "{\"model\":\"iq3_s\",\"stream\":true,\"messages\":[{\"role\":\"user\",\"content\":\"你好\"}]}";
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

            // 路由模式的关键：要列表不该加载模型
            var ml = await http.GetAsync($"http://127.0.0.1:{gw.Port}/v1/models");
            var mlt = await ml.Content.ReadAsStringAsync();
            Check("GET /v1/models：网关本地回答，200 且不唤醒", ml.StatusCode == HttpStatusCode.OK && starts.Count == 0 && mlt.Contains("swift-alias"), (int)ml.StatusCode + " " + mlt);

            var m404 = await http.GetAsync($"http://127.0.0.1:{gw.Port}/v1/models/iq3_s");
            Check("GET /v1/models/<id>：404 且不唤醒", (int)m404.StatusCode == 404 && starts.Count == 0, ((int)m404.StatusCode).ToString());

            var m0 = await http.GetAsync($"http://127.0.0.1:{gw.Port}/metrics");
            var m0t = await m0.Content.ReadAsStringAsync();
            Check("非推理路径：模型没跑时回 503 且不唤醒", (int)m0.StatusCode == 503 && starts.Count == 0, (int)m0.StatusCode + " " + m0t);

            var r1 = await http.PostAsync(chat, JsonBody(body));
            var t1 = await r1.Content.ReadAsStringAsync();
            W("网关首次响应: " + t1.Replace("\n", "\\n"));
            Check("推理请求：200 且由假上游应答", r1.StatusCode == HttpStatusCode.OK, ((int)r1.StatusCode).ToString());
            Check("推理请求：分块 SSE 原样转发（含 [DONE]）", t1.Contains("\"delta\"") && t1.Contains("[DONE]"));
            Check("推理请求：请求体原样到达上游", fake.LastBody == body, fake.LastBody);
            Check("推理请求：Host 改写成上游地址", fake.LastHost == "127.0.0.1:" + fake.Port, fake.LastHost);
            Check("推理请求：唤醒一次且用请求里的 tag", starts.Count == 1 && starts[0] == "iq3_s", string.Join(",", starts));

            var rbadmodel = await http.PostAsync(chat, JsonBody(body.Replace("iq3_s", "gpt-4o")));
            var tbad = await rbadmodel.Content.ReadAsStringAsync();
            Check("请求未配置的模型名 -> 400 并列出可用模型", (int)rbadmodel.StatusCode == 400 && tbad.Contains("未知模型") && tbad.Contains("gpt-4o"),
                (int)rbadmodel.StatusCode + " " + tbad);

            var r2 = await http.PostAsync(chat, JsonBody(body));
            await r2.Content.ReadAsStringAsync();
            Check("同模型再来一次：不重复唤醒", starts.Count == 1, string.Join(",", starts));

            // DEC-112（用户点名「服务在跑时那句『网关收到 POST』不必重复展示」）：同一条请求行的两种命运 ——
            // 第一次要唤醒 = 不带后缀、界面上看得见（那正是"为什么模型突然起来了"的唯一解释）；
            // 第二次上游跑的就是这一件 = 带后缀、被噪声滤掉，文件照写全量。
            var postLines = lines.Where(l => l.Contains("网关收到 POST") && l.Contains("model=iq3_s")).ToList();
            Check("DEC-112 请求行：唤醒那一次不带后缀、转发那一次带后缀（两条都还在，只是界面只显一条）",
                postLines.Count == 2
                && postLines[0].EndsWith("（model=iq3_s）")
                && postLines[1].EndsWith(Gateway.GatewayRepeatSuffix),
                string.Join(" ⏎ ", postLines));
            Check("DEC-112 界面滤得对：转发那一句算噪声、唤醒那一句不算（文件仍全量）",
                MainForm.IsNoise(postLines[1]) && !MainForm.IsNoise(postLines[0]),
                MainForm.IsNoise(postLines[1]) + "/" + MainForm.IsNoise(postLines[0]));
            Check("DEC-112 判据不误伤：切件、非唤醒、没写 model、上游没跑、名字对不上 五种都显示，只有同件转发那一种压掉",
                !MainForm.GatewayRequestIsRepeat(true, "swift-alias", true, "iq3_s", models)
                && !MainForm.GatewayRequestIsRepeat(false, "iq3_s", true, "iq3_s", models)
                && !MainForm.GatewayRequestIsRepeat(true, null, true, "iq3_s", models)
                && !MainForm.GatewayRequestIsRepeat(true, "iq3_s", false, "", models)
                && !MainForm.GatewayRequestIsRepeat(true, "gpt-4o", true, "iq3_s", models)
                && MainForm.GatewayRequestIsRepeat(true, "swift-alias", true, "swift", models)
                && MainForm.GatewayRequestIsRepeat(true, "iq3_s", true, "iq3_s", models),
                "该压的只有一种");

            var m1 = await http.GetAsync($"http://127.0.0.1:{gw.Port}/metrics");
            var m1t = await m1.Content.ReadAsStringAsync();
            Check("非推理路径：上游在跑时照常转发，也不唤醒", m1.StatusCode == HttpStatusCode.OK && starts.Count == 1 && m1t.Contains("data"), m1t);

            var r3 = await http.PostAsync(chat, JsonBody(body.Replace("iq3_s", "swift-alias")));
            await r3.Content.ReadAsStringAsync();
            Check("请求别的 model_name：停旧起新切到 swift", r3.StatusCode == HttpStatusCode.OK && starts.Count == 2 && starts[1] == "swift", string.Join(",", starts));

            var r4 = await http.PostAsync(chat, JsonBody("{\"stream\":true}"));
            await r4.Content.ReadAsStringAsync();
            Check("没写 model 的推理请求回到界面当前模型", r4.StatusCode == HttpStatusCode.OK && starts.Count == 3 && starts[2] == "iq3_s", string.Join(",", starts));

            // DEC-061：活动时刻只由**唤醒路径**刷新。客户端挂着轮询模型列表时「空闲停服」到点就该停——
            // 以前每个请求（含 /v1/models、/health 轮询）都刷新时间戳，只要有人轮询显存就永不释放。
            // 取基线前先等唤醒路径自己那次写落定：上一条推理请求（r4）的活动时刻是在网关自己的 finally 里写的，
            // 比 ReadAsStringAsync 返回晚半毫秒，直接取基线会偶发假红（实测抖动 0.5～0.8ms，两次跑一次红一次绿）。
            long actBefore = gw.LastActivityTicksForTest;
            for (int settle = 0; settle < 25; settle++)
            {
                await Task.Delay(20);
                var now = gw.LastActivityTicksForTest;
                if (now == actBefore) break;
                actBefore = now;
            }
            var pollList = await http.GetAsync($"http://127.0.0.1:{gw.Port}/v1/models");
            await pollList.Content.ReadAsStringAsync();
            Check("DEC-061 非唤醒轮询（GET /v1/models）不刷新活动时刻",
                gw.LastActivityTicksForTest == actBefore, actBefore + " -> " + gw.LastActivityTicksForTest);

            // DEC-061：声明的正文超过上限时直接 413。以前 ReadBodyAsync 把正文静默丢成空数组，
            // 而 BuildUpstreamHead 把 Content-Length 一行不动抄给上游 → 上游一直等那批字节直到超时。
            static async Task<string> RawHeadRequest(int port, string head)
            {
                using var c = new TcpClient();
                await c.ConnectAsync("127.0.0.1", port);
                var st = c.GetStream();
                await st.WriteAsync(Encoding.ASCII.GetBytes(head));
                await st.FlushAsync();
                var buf = new byte[256];
                var acc = new StringBuilder();
                for (int i = 0; i < 8 && acc.Length < 40; i++)
                {
                    int n = await st.ReadAsync(buf);
                    if (n <= 0) break;
                    acc.Append(Encoding.ASCII.GetString(buf, 0, n));
                }
                return acc.ToString();
            }
            var rawHuge = await RawHeadRequest(gw.Port,
                "POST /v1/chat/completions HTTP/1.1\r\nHost: 127.0.0.1\r\nContent-Length: 999000000\r\n\r\n");
            Check("DEC-061 请求体声明超过上限 -> 413（不再丢正文照样转发）",
                rawHuge.StartsWith("HTTP/1.1 413"), rawHuge.Split('\r')[0]);

            var idleOk = idleStopped.Task.Wait(TimeSpan.FromSeconds(8));
            Check("空闲超过设定值后自动停服", idleOk, "idleStop=" + gw.IdleStopCount);

            // 2026-10-15（他实跑报「不到 10 分钟就被按超时卸载」）：锚点本来只在「网关启动」与唤醒请求
            // 两处刷新，而手动「启动」/ 卡3「加载」不走网关 —— 那次实测 07:40:05 立网关、07:45:28 就绪、
            // 07:50:06 就被按「空闲 10 分钟」停掉（服务实际只活了 4 分 38 秒）。修法 = 壳在起件/就绪时调
            // NoteUpstreamActivity 补刷锚点。这条钉住"补刷真能把停服推后"（补刷前那一拍就该停）。
            var idleA = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (var gwA = new Gateway("127.0.0.1", FreePort(), TimeSpan.FromMilliseconds(800), TimeSpan.FromSeconds(5),
                (_, _, _) => Task.FromResult(1), () => { idleA.TrySetResult(true); return true; }, () => "{}", _ => { }))
            {
                gwA.Start();
                await Task.Delay(400);
                gwA.NoteUpstreamActivity();          // 壳自己起了一件（起件成功 / 就绪那一刻）
                await Task.Delay(600);               // 距 Start 已 1000ms（越过 800ms 阈值），距补刷只有 600ms
                Check("壳侧补刷锚点：起件后不被旧锚点按空闲停掉", !idleA.Task.IsCompleted);
                Check("补刷之后到点照样停（没把空闲停服整个关掉）", idleA.Task.Wait(TimeSpan.FromSeconds(3)));
            }

            // 上游正在加载（进程起来了、还没就绪）时空闲判定让一拍：否则手动加载要 30 秒~3 分钟的模型
            // 会被按旧锚点在**加载途中**停掉（他实跑那句"加载一会儿就自动卸载了"）。
            var idleB = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var loading = true;
            using (var gwB = new Gateway("127.0.0.1", FreePort(), TimeSpan.FromMilliseconds(300), TimeSpan.FromSeconds(5),
                (_, _, _) => Task.FromResult(1), () => { idleB.TrySetResult(true); return true; }, () => "{}", _ => { },
                isRepeat: null, upstreamBusy: () => loading))
            {
                gwB.Start();
                await Task.Delay(900);               // 三个阈值周期都过去了
                Check("上游加载中：空闲停服让一拍（不把正在读权重的进程停掉）", !idleB.Task.IsCompleted);
                loading = false;
                Check("加载结束（就绪）后到点就停", idleB.Task.Wait(TimeSpan.FromSeconds(3)));
            }

            var r5 = await http.PostAsync(chat, JsonBody(body));
            await r5.Content.ReadAsStringAsync();
            Check("停服后再来推理请求：重新唤醒并 200", r5.StatusCode == HttpStatusCode.OK && starts.Count == 4, string.Join(",", starts));

            using var bad = new Gateway("127.0.0.1", FreePort(), TimeSpan.Zero, TimeSpan.FromSeconds(5),
                (_, _, _) => throw new InvalidOperationException("模型没配好"), () => false, () => "{}", _ => { });
            bad.Start();
            var r6 = await http.PostAsync($"http://127.0.0.1:{bad.Port}/v1/chat/completions", JsonBody(body));
            var t6 = await r6.Content.ReadAsStringAsync();
            Check("上游起不来 -> 503 + JSON 错误体", (int)r6.StatusCode == 503 && t6.Contains("无法唤醒模型"), (int)r6.StatusCode + " " + t6);

            // DEC-160（幽灵起件那一半）**超时这一头**：总时限到点 = 网关回 504，同时那条还在排/还在等的
            // 唤醒任务必须收到取消。旧写法只回错误、任务照跑到底 = 替一个已经拿到 504 的客户端把件读进显存。
            // 判据两面：① 令牌真被取消（证人位 = 假件那一枪走到 catch OperationCanceledException）
            //             ② 取消之后**没有**去"起件"（假件里 `startedLate = true` 那一行不许被走到）。
            var slowCancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool startedLate = false;
            using var slow = new Gateway("127.0.0.1", FreePort(), TimeSpan.Zero, TimeSpan.FromMilliseconds(300),
                async (_, _, reqCt) =>
                {
                    try { await Task.Delay(3000, reqCt); }
                    catch (OperationCanceledException) { slowCancelled.TrySetResult(true); throw; }
                    startedLate = true;                       // 走到这里 = 没人等了还去起件
                    return 1;
                }, () => false, () => "{}", _ => { });
            slow.Start();
            var r7 = await http.PostAsync($"http://127.0.0.1:{slow.Port}/v1/chat/completions", JsonBody(body));
            await r7.Content.ReadAsStringAsync();
            Check("等不到上游就绪 -> 504（300 秒上限的那条路）", (int)r7.StatusCode == 504, ((int)r7.StatusCode).ToString());
            Check("DEC-160 总时限到点：那条唤醒任务随这一发的令牌收手、没有替已拿到 504 的客户端起件",
                slowCancelled.Task.Wait(TimeSpan.FromSeconds(5)) && !startedLate,
                "取消=" + slowCancelled.Task.IsCompleted + " 还去起件=" + startedLate);

            // DEC-160 另一半：**客户端挂了**（旧启动器那类 ~60 秒自己断，断的是 TCP）。
            // 网关在等起件那一段每 300ms 看一次对面还在不在（只 poll、一个字节都不读 = 不吃 keep-alive 的下一发）。
            var hangupCancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool hangupStarted = false;
            using var ghost = new Gateway("127.0.0.1", FreePort(), TimeSpan.Zero, TimeSpan.FromSeconds(30),
                async (_, _, reqCt) =>
                {
                    try { await Task.Delay(Timeout.Infinite, reqCt); }
                    catch (OperationCanceledException) { hangupCancelled.TrySetResult(true); throw; }
                    hangupStarted = true;
                    return 1;
                }, () => false, () => "{}", _ => { });
            ghost.Start();
            {
                var c = new TcpClient();
                await c.ConnectAsync("127.0.0.1", ghost.Port);
                var head = "POST /v1/chat/completions HTTP/1.1\r\nHost: 127.0.0.1\r\nContent-Type: application/json"
                         + "\r\nContent-Length: 2\r\n\r\n{}";
                var s0 = c.GetStream();
                await s0.WriteAsync(Encoding.UTF8.GetBytes(head));
                await s0.FlushAsync();
                await Task.Delay(400);        // 先把这一发读走（挂断探测只在等起件那一段跑）
                c.Close();                    // 对面走了
            }
            Check("DEC-160 客户端挂断 = 唤醒链随这一发的令牌收手，没有去起件（那半条幽灵起件结案）",
                hangupCancelled.Task.Wait(TimeSpan.FromSeconds(5)) && !hangupStarted,
                "取消=" + hangupCancelled.Task.IsCompleted + " 还去起件=" + hangupStarted);
            Check("DEC-160 挂断判据四面：连不上=走了；可读且零字节=走了；不可读=还在等响应；有字节=keep-alive 的下一发（不算走）",
                Gateway.ClientHungUp(false, false, 0) && Gateway.ClientHungUp(true, true, 0)
                && !Gateway.ClientHungUp(true, false, 0) && !Gateway.ClientHungUp(true, true, 5),
                "四面都按判据给");
        }

        // 自检前面建过 MainForm，主线程上已经装了 WinForms 的同步上下文：
        // 直接在主线程阻塞等 async，会把续体排给没人泵的消息循环（死等），所以整段丢到线程池跑
        try { Task.Run(ScenarioAsync).GetAwaiter().GetResult(); }
        catch (Exception ex) { Check("网关端到端用例", false, ex.Message); }
        return failures;
    }
    // 用例 8（常驻网关：假上游、不加载真模型）+ 用例 9（llama.cpp 后端：合并 ini / 命令行 / 端口回落）。
    // 2026-10-15 从 RunBody 整体搬进本文件（与上面那份"网关自身的判据"同族），块内一行未改。
    internal static void GatewayAndLlamaBackendChecks()
    {
            // --- 用例 8：常驻网关（假上游，不加载真模型） ---
            failures += GatewayTests(sb);

            // --- 用例 9：llama.cpp 后端（合并 ini / 命令行 / 端口回落；不拉真进程） ---
            try
            {
                LlamaBackendChecks();
                // 9f. 按端口找监听进程（外部服务结束流程用；拿自检进程自己的 TcpListener 当靶子）
                using (var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0))
                {
                    l.Start();
                    int probePort = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
                    bool found = MainForm.TryFindListenerOnPort(probePort, out var foundPid, out var foundName);
                    Check("按端口找监听进程（netstat 解析）", found && foundPid > 0 && foundName.Length > 0,
                        $"port={probePort} pid={foundPid} name={foundName}");
                    l.Stop();
                }

                // 9g. RoundedTextBox 把内层输入转发成外层 TextChanged（BUG-017：设置窗保存链的根因）
                try
                {
                    var rb = new RoundedTextBox();
                    int fired = 0;
                    rb.TextChanged += (_, _) => fired++;
                    rb.Inner.Text = "8081";                    // 模拟用户输入（写内层 TextBox）
                    Check("RoundedTextBox：内层输入触发外层 TextChanged", fired == 1 && rb.Text == "8081", "fired=" + fired);
                    rb.Text = "9000";                          // 外层赋值（设置窗回填/代码改值）
                    Check("RoundedTextBox：外层赋值也触发 TextChanged", fired == 2, "fired=" + fired);
                }
                catch (Exception ex) { Check("RoundedTextBox 文本转发用例", false, ex.Message); }

                // 9h. 自绘控件响应 BM_CLICK（ui-probe.ps1 -Click 依赖；BUG-016）
                var fbT = new FlatButton(); int fbClicks = 0; fbT.Click += (_, _) => fbClicks++;
                Win32.SendMessage(fbT.Handle, 0x00F5, IntPtr.Zero, IntPtr.Zero);
                Check("FlatButton 响应 BM_CLICK", fbClicks == 1, "clicks=" + fbClicks);
                var tsT = new ToggleSwitch(); int tsClicks = 0; tsT.Click += (_, _) => tsClicks++;
                Win32.SendMessage(tsT.Handle, 0x00F5, IntPtr.Zero, IntPtr.Zero);
                Check("ToggleSwitch 响应 BM_CLICK 且翻转", tsClicks == 1 && tsT.Checked, $"clicks={tsClicks} on={tsT.Checked}");
                // DEC-138（M6 缺口 8）：AGENTS 那句「灰的按钮 BM_CLICK 也点不动」以前只验了亮的那一面 ——
                // 把控件里那句 `if (Enabled)` 删掉照样绿。补反面。
                fbT.Enabled = false;
                int fbDead0 = fbClicks;
                Win32.SendMessage(fbT.Handle, 0x00F5, IntPtr.Zero, IntPtr.Zero);
                tsT.Enabled = false;
                int tsDead0 = tsClicks;
                Win32.SendMessage(tsT.Handle, 0x00F5, IntPtr.Zero, IntPtr.Zero);
                Check("DEC-138 缺口8 反面：置灰的 FlatButton / ToggleSwitch 收到 BM_CLICK 都不许动（自动化点不动 = 那条护栏的字面意思）",
                    fbClicks == fbDead0 && tsClicks == tsDead0,
                    $"按钮 {fbDead0}→{fbClicks} 开关 {tsDead0}→{tsClicks}");
                fbT.Enabled = true; tsT.Enabled = true;

                // 9i. 下拉列表：整行命中 + 尺寸钉回（用户反馈「点选项整行都应生效」）
                var (dh1, dh2, dsz) = DarkSelect.DropHitTestForTest();
                Check("下拉行命中：行内任意 Y 都能选中（整行）", dh1 && dh2, $"top={dh1} bottom={dh2}");
                Check("弹窗尺寸被改后能钉回请求值", dsz);

                // 9j. 日志降噪规则（形状取自用户 2026-10-01 贴的全量日志）
                Check("降噪：args 清单行被滤", MainForm.IsNoise("0.10.931.028 I srv          load:   --jinja"));
                Check("降噪：Available models 列表块被滤",
                    MainForm.IsNoise("0.00.093.103 I srv    operator(): Available models (9):")
                    && MainForm.IsNoise("0.00.093.106 I srv    operator():   [    preset] MiniCPM5-2B-F16"));
                Check("降噪：[pid] 前缀空行被滤", MainForm.IsNoise("[ 5435] ") && MainForm.IsNoise("[ 7894]"));
                Check("降噪：dflash 常规告警被滤",
                    MainForm.IsNoise("[ 5435] 0.00.433.497 E llama_init_from_model: failed to initialize the context: dflash requires ctx_other to be set (this warning is normal during memory fitting)"));
                Check("降噪：关键行不滤（就绪/安全/模型已加载/壳自己的行/实例端口行）",
                    !MainForm.IsNoise("0.00.109.131 I srv  llama_server: listening on http://0.0.0.0:8079")
                    && !MainForm.IsNoise("0.00.093.263 W srv  llama_server: security: no API key is set and CORS allows all origins")
                    && !MainForm.IsNoise("0.01.825.512 I srv  llama_server: model loaded")
                    && !MainForm.IsNoise("$ 已生成合并 ini：D:\\strata\\data\\llama\\merged.ini")
                    && !MainForm.IsNoise("0.00.530.858 I srv          load: spawning server instance with name=MiniCPM5-2B-F16 on port 13856"));
                Check("降噪：真错误（不带 normal 标记的 E 行）不滤",
                    !MainForm.IsNoise("[ 5435] 0.00.100.000 E llama_init_from_model: failed to initialize the context: out of memory"));
                // 网关探活流量（用户反馈 2026-10-01：本地服务每几秒轮询，把界面日志刷屏）
                Check("降噪：网关本地回答 GET /v1/models 被滤",
                    MainForm.IsNoise("网关本地回答 GET /v1/models（不加载模型）"));
                Check("降噪：非唤醒路径提示被滤（/health、/props 这类轮询）",
                    MainForm.IsNoise("网关收到 GET /health（这个路径不唤醒模型）")
                    && MainForm.IsNoise("网关收到 GET /props?model=qwen3.8-flash-next-iq3_s&autoload=false（这个路径不唤醒模型）"));
                Check("降噪：唤醒路径请求行与网关启动行不滤",
                    !MainForm.IsNoise("网关收到 POST /v1/chat/completions（model=iq3_s）")
                    && !MainForm.IsNoise("$ 网关已监听 0.0.0.0:8077（引擎跑内部端口；请求到达时唤醒或切换模型）"));
                // DEC-150：router 的转发行是**壳自己按节拍造出来的**（状态栏每 5 秒问一次 /metrics）——
                // 挂"源头丢弃"档：界面、历史、日志文件三处都不进；只认这一种形状，别误伤真请求/错误行。
                Check("DEC-150：router 转发行按源头丢弃（带 pid 前缀与不带都认）",
                    MainForm.IsSinkNoise("0.00.094.009 I srv  proxy_reques: proxying request to model MiniCPM5-2B-F16 on port 9884")
                    && MainForm.IsSinkNoise("[ 5435] 0.39.897.225 I srv  proxy_reques: proxying request to model Qwen3.8-27B-MTP-UD-Q4_K_XL on port 4107"));
                Check("DEC-150：真请求与错误行不碰（只丢转发噪声那一族）",
                    !MainForm.IsSinkNoise("0.00.530.858 I srv  load: spawning server instance with name=MiniCPM5-2B-F16 on port 13856")
                    && !MainForm.IsSinkNoise("[ 5435] 0.00.100.000 E srv  failed to proxy request: connection refused")
                    && !MainForm.IsSinkNoise("$ 加载模型 MiniCPM5-2B-F16 …"));
                Check("端口来源：填空=自动", MainForm.LlamaPortSourceText(8077, 8078, "", 0, 0, 8079).Contains("自动"));
                Check("端口来源：自动值被 Strata 内部端口占 -> 说明避让",
                    MainForm.LlamaPortSourceText(8077, 8079, "", 0, 0, 8080).Contains("避让")
                    && MainForm.LlamaPortSourceText(8077, 8079, "", 0, 0, 8080).Contains("8079"));
                Check("端口来源：设置里填的 -> 说明来自设置",
                    MainForm.LlamaPortSourceText(8077, 8078, "9000", 0, 0, 9000).Contains("设置")
                    && MainForm.LlamaPortSourceText(8077, 8078, "abc", 0, 0, 8079).Contains("自动"));
                Check("端口来源：被网关/服务旧端口占 -> 说明自动避让",
                    MainForm.LlamaPortSourceText(8077, 18077, "8078", 8078, 0, 8079).Contains("避让")
                    && MainForm.LlamaPortSourceText(8077, 18077, "8078", 8078, 0, 8079).Contains("网关"));
                // 端口选择：避开「实际还占着」的旧端口（用户实跑：公开端口改小后网关仍占旧端口，llama bind 失败）
                Check("端口选择：撞上实际在跑的网关 -> 自动避让",
                    MainForm.LlamaPortChoose(8077, 18077, "8078", 8078, 0) == 8079,
                    MainForm.LlamaPortChoose(8077, 18077, "8078", 8078, 0).ToString());
                Check("端口选择：撞上实际在跑的服务 -> 自动避让",
                    MainForm.LlamaPortChoose(8077, 18077, "", 0, 8079) == 8080,
                    MainForm.LlamaPortChoose(8077, 18077, "", 0, 8079).ToString());

                // 9k. 全局端口冲突校验（DEC-024 A 段；BUG-023 的引信"保存不拦"）
                Check("端口校验：错开/留空 -> 通过",
                    MainForm.PortConflictAtSave(8077, "", "") is null
                    && MainForm.PortConflictAtSave(8077, "18077", "18078") is null);
                Check("端口校验：Strata 内部 == 公开 -> 拦",
                    (MainForm.PortConflictAtSave(8077, "8077", "") ?? "").Contains("公开端口"));
                Check("端口校验：llama 内部 == 公开 -> 拦",
                    (MainForm.PortConflictAtSave(8077, "18077", "8077") ?? "").Contains("公开端口"));
                Check("端口校验：两个内部端口相同 -> 拦",
                    (MainForm.PortConflictAtSave(8077, "18077", "18077") ?? "").Contains("相同"));
                Check("端口校验：llama 撞 Strata 的自动值（公开+1）-> 拦",
                    (MainForm.PortConflictAtSave(8077, "", "8078") ?? "").Contains("自动值"));
                Check("端口校验：非整数 -> 拦",
                    (MainForm.PortConflictAtSave(8077, "abc", "") ?? "").Contains("1-65535"));

                // 9l. 同族指纹：llama 的模型列表不能被认成 Strata（BUG-023 误报的另一个入口）
                var llamaBody = "{\"data\":[{\"id\":\"MiniCPM5-2B-F16\",\"owned_by\":\"llamacpp\",\"status\":{\"value\":\"unloaded\"}}]}";
                var strataBody = "{\"object\":\"list\",\"data\":[{\"id\":\"swift-1.5-iq3_xxs\",\"object\":\"model\",\"owned_by\":\"strata\"}]}";
                Check("指纹：llama 的 /v1/models 认得出", MainForm.LooksLikeLlamaModelsList(llamaBody));
                Check("指纹：Strata 列表不误认 / 容错",
                    !MainForm.LooksLikeLlamaModelsList(strataBody)
                    && !MainForm.LooksLikeLlamaModelsList("{}")
                    && !MainForm.LooksLikeLlamaModelsList(null));
            }
            catch (Exception ex) { Check("llama.cpp 后端用例", false, ex.Message); }
    }
}

// 自检用的假上游：一个最小 HTTP/1.1 服务，/v1/chat/completions 走分块 SSE，其余走 Content-Length。
internal sealed class FakeUpstream : IDisposable
{
    readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    CancellationTokenSource? _cts;
    volatile string _lastBody = "";
    volatile string _lastHost = "";
    int _requests;

    public int Port { get; private set; }
    public string LastBody => _lastBody;
    public string LastHost => _lastHost;
    public int Requests => Volatile.Read(ref _requests);

    public void Start()
    {
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await _listener.AcceptTcpClientAsync(ct); } catch { return; }
                _ = Task.Run(() => HandleAsync(client, ct));
            }
        });
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { /* 已释放 */ }
        try { _listener.Stop(); } catch { /* 已释放 */ }
        _cts?.Dispose();
        _cts = null;
    }

    async Task HandleAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            var io = new NetReader(client.GetStream());
            var head = await io.ReadHeadAsync(ct);
            if (head is null) return;
            var lines = HttpStreams.HeadLines(head);
            var headers = HttpStreams.ParseHeaders(lines);
            _lastHost = HttpStreams.Header(headers, "Host") ?? "";
            _lastBody = Encoding.UTF8.GetString(await HttpStreams.ReadBodyAsync(io, headers, ct));
            Interlocked.Increment(ref _requests);
            var path = lines[0].Split(' ').Skip(1).FirstOrDefault() ?? "";

            if (path != "/v1/chat/completions")
            {
                var json = Encoding.UTF8.GetBytes("{\"data\":[{\"id\":\"fake\"}]}");
                await io.WriteAsync(Encoding.Latin1.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {json.Length}\r\nConnection: close\r\n\r\n"), ct);
                await io.WriteAsync(json, ct);
            }
            else
            {
                await io.WriteAsync(Encoding.Latin1.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\nTransfer-Encoding: chunked\r\nConnection: close\r\n\r\n"), ct);
                foreach (var piece in new[] { "data: {\"delta\":\"he\"}\n\n", "data: {\"delta\":\"llo\"}\n\n", "data: [DONE]\n\n" })
                {
                    var bytes = Encoding.UTF8.GetBytes(piece);
                    await io.WriteAsync(Encoding.Latin1.GetBytes(bytes.Length.ToString("x") + "\r\n"), ct);
                    await io.WriteAsync(bytes, ct);
                    await io.WriteAsync(Encoding.Latin1.GetBytes("\r\n"), ct);
                }
                await io.WriteAsync(Encoding.Latin1.GetBytes("0\r\n\r\n"), ct);
            }
            await io.FlushAsync(ct);
        }
    }


}
