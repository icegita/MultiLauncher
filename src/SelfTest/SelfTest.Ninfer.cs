using System.Text;
namespace MultiLauncher;

// 用例 7f：Ninfer（DEC-096 第三个模块）—— 家底层纯函数 + 起前护栏 + 一句话状态 + 方块/格子/端口那一族。
// 2026-10-15 从 RunBody 整体搬出，块内一行未改。
internal static partial class SelfTest
{
    internal static void NinferChecks()
    {
            // --- 用例 7f：Ninfer（DEC-096 第三个模块）—— 家底层纯函数 + 起前护栏 + 一句话状态 =====
            {
                const string Wn = NinferIni.BackendWin, S = NinferIni.BackendWsl;

                // ① argv 的拆与装：模板里已有的旗标**在原位置换值**（顺序不变）、没有的追加末尾、留空 = 摘掉
                var t0 = "--max-context 262144 --kv-dtype k8v4 --spec dflash2 --lm-head-draft";
                var r1 = NinferIni.Rewrite(t0, new[] { new NinferIni.Edit("kv-dtype", "int8", false) }, out int c1);
                Check("Ninfer argv：改一枚只动那一处，顺序与其余旗标一个字不变",
                    r1 == "--max-context 262144 --kv-dtype int8 --spec dflash2 --lm-head-draft" && c1 == 1, r1);
                var r2 = NinferIni.Rewrite(t0, new[] { new NinferIni.Edit("kv-dtype", "k8v4", false) }, out int c2);
                Check("Ninfer argv：值本来就一样 → changed=0（「打开看看、点保存」不许污染文件）",
                    c2 == 0 && r2 == t0, c2 + "｜" + r2);
                var r3 = NinferIni.Rewrite(t0, new[] { new NinferIni.Edit("kv-dtype", "", false) }, out int c3);
                Check("Ninfer argv：留空 = 把这枚旗标连同它的值一起摘掉",
                    r3 == "--max-context 262144 --spec dflash2 --lm-head-draft" && c3 == 1, r3);
                var r4 = NinferIni.Rewrite(t0, new[] { new NinferIni.Edit("lm-head-draft", "", true) }, out int c4);
                Check("Ninfer argv：布尔关档 = 从命令行里消失（没有值，只认在不在）",
                    !r4.Contains("--lm-head-draft") && c4 == 1, r4);
                var r5 = NinferIni.Rewrite(t0, new[] { new NinferIni.Edit("top-k", "20", false) }, out int c5);
                Check("Ninfer argv：模板里没有而界面给了值 = 追加到末尾",
                    r5.EndsWith("--top-k 20") && c5 == 1, r5);

                // ② 起服务那条路（支点 B1）：先摘壳管理的旗标再按界面值追加；**界面上没格子的带值旗标原样透传**
                var built = NinferIni.Build(t0 + " --max-concurrency 1 --prefill-chunk 4096",
                    MainForm.UiFlagsFor(Wn).ToList(),
                    new[] { new NinferIni.Edit("kv-dtype", "bf16", false), new NinferIni.Edit("max-context", "", false) },
                    new[] { ("model-id", "qwen3.8-27b-gsq-iq3s"), ("host", "0.0.0.0"), ("port", "8069") });
                // DEC-138（E-1）：这条原来写成「A && B && C || (D && E)」—— `&&` 优先于 `||`，
                // 于是只要 `--model-id` 与 `--port` 在串里**任意位置**出现过就绿，"摘模板旗标 / 界面值覆盖 / 三枚排末尾"
                // 三条一个都没守住。拆成三条各判各的（判据内容一字未改，只是不再互相顶缸）。
                Check("DEC-107/E-1 界面值覆盖：摘掉「这一侧有格子」的旗标、按界面上的值重加（bf16 进来、段里的 max-context 不许留在串里）",
                    built.Contains("--kv-dtype bf16") && !built.Contains("--max-context"), built);
                Check("DEC-107/E-1 壳定那三枚恒摘恒加，且**排在末尾**（件是位置参数、旗标在后才有意义）",
                    built.EndsWith("--model-id qwen3.8-27b-gsq-iq3s --host 0.0.0.0 --port 8069"), built);
                Check("DEC-107/E-1 反面：那三枚不许在串里别处再出现一次（旧写法 `Contains(...)||Contains(...)` 就是被这条抓住的）",
                    System.Text.RegularExpressions.Regex.Matches(built, "--model-id").Count == 1
                    && System.Text.RegularExpressions.Regex.Matches(built, "--port").Count == 1, built);
                Check("Ninfer 命令行：WIN 不给格的那几枚（并发数 / 状态槽数 / 排队两枚）照旧透传 —— 摘了就等于把「环硬拒 mc>1」那条护栏自己拆掉",
                    built.Contains("--max-concurrency 1") && !built.Contains("--prefill-chunk"), built);
                // DEC-187：`gpu-need-gib` 是壳自己的键（那一件的显存估算覆盖值），不是引擎旗标 —— 进命令行就是 unknown argument
                var needArgv = NinferIni.KeysToArgv(new[]
                {
                    new KeyValuePair<string, string>("backend", "win"),
                    new KeyValuePair<string, string>("gpu-need-gib", "22.3"),
                    new KeyValuePair<string, string>("max-context", "262144"),
                });
                Check("DEC-187 壳自己的键 gpu-need-gib 不进命令行（与 backend / file / args 同一类：文件里有、argv 里没有）",
                    !needArgv.Contains("gpu-need-gib") && needArgv == "--max-context 262144", needArgv);

                // ============ DEC-136（审查清单 M3 那一批）：形状漏洞各钉一面 ============
                // C-2：段里"有旗标没值"以前让「保存」在 UI 线程上抛 IndexOutOfRangeException
                var bare = "";
                bool threw = false;
                try { bare = NinferIni.Rewrite("--max-context 8192 --top-k",
                        new[] { new NinferIni.Edit("top-k", "20", false) }, out _); }
                catch (Exception ex) { threw = true; bare = ex.GetType().Name; }
                Check("DEC-136 C-2 Rewrite：旗标排在段尾（后面没有值）也不抛，值就地补在它后面",
                    !threw && bare == "--max-context 8192 --top-k 20", bare);
                Check("DEC-136 C-2 KeysToArgv：非布尔而值空 = 整枚不发（发了就是裸 token，BUG-041 那个形状的镜像）",
                    NinferIni.KeysToArgv(new[] { new KeyValuePair<string, string>("top-k", "") }) == ""
                    && NinferIni.KeysToArgv(new[] { new KeyValuePair<string, string>("top-k", "20") }) == "--top-k 20",
                    NinferIni.KeysToArgv(new[] { new KeyValuePair<string, string>("top-k", "") }));
                // C-4：布尔读侧以前完全忽略值 —— 他照 llama 侧的习惯写 `vision = false` 会被折成「开」（视觉真被起，约 0.6 GiB）
                Check("DEC-136 C-4 KeysToArgv：布尔认假值表（false/0/no/off = 不发这枚；空值仍算开 = 摊平出来的形状）",
                    !NinferIni.KeysToArgv(new[] { new KeyValuePair<string, string>("vision", "false") }).Contains("--vision")
                    && !NinferIni.KeysToArgv(new[] { new KeyValuePair<string, string>("vision", "0") }).Contains("--vision")
                    && NinferIni.KeysToArgv(new[] { new KeyValuePair<string, string>("vision", "true") }).Contains("--vision")
                    && NinferIni.KeysToArgv(new[] { new KeyValuePair<string, string>("vision", "") }).Contains("--vision"),
                    NinferIni.KeysToArgv(new[] { new KeyValuePair<string, string>("vision", "false") }));
                // C-8：Build 以前对布尔"一律摘掉再按界面追加"，WIN 段里的 WSL-only 布尔被摘了就没人加回来
                var winFlags = MainForm.UiFlagsFor(Wn).ToList();
                var boolPassthrough = NinferIni.Build("--vision --max-context 8192", winFlags,
                    new NinferIni.Edit[0], new[] { ("model-id", "x"), ("host", "0.0.0.0"), ("port", "8069") });
                Check("DEC-136 C-8 Build：这一侧没格子的布尔原样透传（文件里在 = 命令行也在，不再出现三处各说一话）",
                    boolPassthrough.Contains("--vision"), boolPassthrough);
                // C-1 的两面判据要真走 `NinferPreflight`（实例方法），挪到下面那块有 form 的用例里
                //（DEC-136 C-1：禁用旗标以前判段里的模板，附加参数挂在实际 argv 末尾 = 正好是它的盲区）
                // C-10：公开口填到顶格时内部口算出 65536 还被当合法值写盘
                var edgePort = MainForm.NinferPortChoose(65535, "", 8078, 8079, 0, 0, 0);
                Check("DEC-136 C-10 公开口 = 65535 时内部口仍夹在 1-65535（旧写法退回的就是那个越界值，日志还自称在范围内）",
                    edgePort is >= 1 and <= 65535 && edgePort != 65535, edgePort.ToString());

                // ③ 空白思考档在两代不是同一件事（原型对照表第 18 问）：WSL 空白 = 写 --no-thinking，WIN 空白 = 不写字
                var ws = new Settings { NinferEffort = "", NinferPool = "55296" };
                Check("Ninfer 思考档：WSL 侧界面空白 → --no-thinking 这一枚要给出去",
                    MainForm.NinferNoThinkingValue(ws, S) == "1" && MainForm.NinferNoThinkingValue(ws, Wn) == "",
                    MainForm.NinferNoThinkingValue(ws, S) + "/" + MainForm.NinferNoThinkingValue(ws, Wn));
                Check("Ninfer 思考档：填了档位就不再给 --no-thinking（两枚互斥由这一处说了算）",
                    MainForm.NinferNoThinkingValue(new Settings { NinferEffort = "medium" }, S) == "");
                Check("Ninfer 取值表按后端各一张（WSL 没有 dflash2；空白档一律在首）",
                    NinferIni.EffortsFor(S).SequenceEqual(new[] { "low", "medium", "xhigh" })
                    && NinferIni.EffortsFor(Wn).SequenceEqual(new[] { "default", "minimal", "low", "medium", "high", "xhigh", "max" })
                    && NinferIni.WslSpecs.Length == 2 && !NinferIni.WslSpecs.Contains("dflash2")
                    && MainForm.NinferSelectItems(NinferIni.EffortsFor(S))[0].Text == "");

                // ④ 起前护栏（DEC-088：非法值不改界面，改成"起前拒绝并说明"）——逐条都必须是拒绝而不是放行
                Check("Ninfer 护栏：设备池 auto 与 <17,920 拒绝（超池逐字节重发会把实例打砖）",
                    NinferIni.ValueProblem("kv-capacity", "auto", Wn) is not null
                    && NinferIni.ValueProblem("kv-capacity", "17919", Wn) is not null
                    && NinferIni.ValueProblem("kv-capacity", "17920", Wn) is null);
                Check("Ninfer 护栏：共享前缀非 0 / 预填充 >4096 / 主机缓存 >16384 一律拒绝（只 WIN 那代有这三档上限）",
                    NinferIni.ValueProblem("max-shared-prefixes", "1", Wn) is not null
                    && NinferIni.ValueProblem("max-shared-prefixes", "0", Wn) is null
                    && NinferIni.ValueProblem("prefill-chunk", "4097", Wn) is not null
                    && NinferIni.ValueProblem("prefill-chunk", "4097", S) is null      // WSL 那代没这条实测上限，不误杀
                    && NinferIni.ValueProblem("host-kv-mib", "16385", Wn) is not null);
                Check("Ninfer 护栏：留空 = 不写这枚旗标 = 用引擎默认（一律放行，别把留空当非法）",
                    NinferIni.ValueProblem("max-context", "", Wn) is null && NinferIni.ValueProblem("spec", "", Wn) is null);
                Check("Ninfer 护栏：行粒度 KV 认得 rk* 那一族与 int8/fp8，k8v4/bf16 不算（swift 件×行粒度 = 静默答错）",
                    NinferIni.IsRowGranularKv("rk4v4-e8") && NinferIni.IsRowGranularKv("int8")
                    && !NinferIni.IsRowGranularKv("k8v4") && !NinferIni.IsRowGranularKv("bf16"));
                Check("Ninfer 护栏：spec 表外的值按后端拒（WSL 选 dflash2 / WIN 选 dflash = 件里没那个头 = FATAL missing component）",
                    NinferIni.ValueProblem("spec", "dflash2", S) is not null && NinferIni.ValueProblem("spec", "mtp", S) is null
                    // DEC-187：WIN 那代补进 mtp（Coder390 那两件带的是 MTP 头，2026-10-17 本机跑通），dflash2 照旧在
                    && NinferIni.ValueProblem("spec", "mtp", Wn) is null && NinferIni.ValueProblem("spec", "dflash2", Wn) is null
                    && NinferIni.ValueProblem("spec", "dflash", Wn) is not null);
                var math = NinferIni.HostPages("262144", "55296", "16384", "k8v4");
                Check("Ninfer 起前算数：256K 上下文 + 55,296 池 + 16 GiB 主机缓存 = 够（包文档那两个换算）",
                    math.Checked && math.HaveHostKib >= math.NeedHostKib, math.Text);
                var math2 = NinferIni.HostPages("262144", "55296", "1024", "k8v4");
                Check("Ninfer 起前算数：主机缓存只给 1 GiB = 不够，起之前就拒绝（省一次冷起 + 一行 FATAL）",
                    math2.Checked && math2.HaveHostKib < math2.NeedHostKib, math2.Text);
                Check("Ninfer 起前算数：表外精度档按「没这条校验」通过，不拿猜出来的数拦人（DEC-067 口径）",
                    !NinferIni.HostPages("262144", "55296", "1", "rk4v4-e8").Checked);
                Check("Ninfer 段判据：model-id 两两不同名（同名 = 引擎静默命中第一个 = 起错件）与段名可用性",
                    NinferIni.DuplicateModelIds(new[] { new NinferIni.Section { Name = "a" }, new NinferIni.Section { Name = "A" } }) == "A"
                    && NinferIni.DuplicateModelIds(new[] { new NinferIni.Section { Name = "a" }, new NinferIni.Section { Name = "b" } }) is null
                    && !NinferIni.UsableSectionName("带 空格") && NinferIni.UsableSectionName("qwen3.8-27b-wsl"));
                // 包文档点名的禁用旗标：出现在模板里 = 拒绝启动，别"顺手剥掉"继续跑
                var banned = NinferIni.FindBanned(NinferIni.Split("--disk-cache 8 --disk-kv-mib 4096 --max-context 262144"));
                Check("Ninfer 禁用旗标：--disk-cache（这代根本不存在）与 --disk-kv-*（预分配整块文件）都点名",
                    banned.Count == 2 && banned.Contains("--disk-cache") && banned.Contains("--disk-kv-mib"),
                    string.Join(" / ", banned));

                // ⑤ 端口：内部口留空 = 公开口 + 1，还要避开另两模块的内部口与**实际占用**；wslrelay 代绑不算撞车
                Check("Ninfer 端口：留空 = 公开 + 1，撞了另两模块的内部口就一级一级往上让（公开口只有一个，谁也不能抢）",
                    MainForm.NinferPortChoose(8068, "", 8069, 8070, 0, 0, 0) == 8071
                    && MainForm.NinferPortChoose(8068, "8068", 8069, 8070, 0, 0, 0) == 8071   // 填了公开口自己 = 让开
                    && MainForm.NinferPortChoose(8068, "1", 1, 2, 0, 0, 0) == 8070,           // 撞 Strata 又撞 llama → 让到公开 + 2
                    MainForm.NinferPortChoose(8068, "", 8069, 8070, 0, 0, 0).ToString());
                Check("Ninfer 端口：实际占用也避让（网关/服务改过端口要下次启动才让出；wslrelay 代绑不在这三个数里 = 不算撞车）",
                    MainForm.NinferPortChoose(8068, "", 8069, 8071, 8070, 0, 0) == 8072
                    && MainForm.NinferPortChoose(8068, "8070", 8069, 8071, 8070, 0, 0) == 8072
                    && MainForm.NinferPortChoose(8068, "8070", 8069, 8071, 0, 0, 0) == 8070);  // 没人占着就用它
                Check("Ninfer 端口：改值确认只认「合法且真变了」（取消 = 文本回滚 + 不标脏，DEC-091）",
                    MainForm.PortChangeNeedsConfirm("8068", "8078")
                    && !MainForm.PortChangeNeedsConfirm("8068", "8068")
                    && !MainForm.PortChangeNeedsConfirm("8068", "abc")
                    && !MainForm.PortChangeNeedsConfirm("8068", "70000")
                    && !MainForm.PortChangeNeedsConfirm("8068", "  "));
                Check("Ninfer 空闲停服：两份各存（DEC-083 阈值按后端一张），留空/非法 = 0 = 不自动停",
                    MainForm.NinferIdleMinutes(new Settings { NinferIdleWin = "10", NinferIdleWsl = "20" }, Wn) == 10
                    && MainForm.NinferIdleMinutes(new Settings { NinferIdleWin = "10", NinferIdleWsl = "20" }, S) == 20
                    && MainForm.NinferIdleMinutes(new Settings { NinferIdleWin = "" }, Wn) == 0
                    && MainForm.NinferIdleMinutes(new Settings { NinferIdleWin = "abc" }, Wn) == 0);

                // ⑥ 就绪 marker（§4 实测表）：形 `listening on http://0.0.0.0:8068 | model <对外名> | auth disabled`
                //    —— **IP 与模型名都不能进匹配串**，只认前半句 + 那个端口
                Check("Ninfer 就绪行：0.0.0.0 + 任意模型名都认（IP 与模型名不进判据）",
                    MainForm.NinferReadyMarker("2026-10-06 01:20:25.955 I listening on http://0.0.0.0:8068 | model qwen3.8-27b-gsq-iq3s | auth disabled", 8068)
                    && MainForm.NinferReadyMarker("listening on http://127.0.0.1:8069", 8069));
                Check("Ninfer 就绪行：端口不对 / 只是别的日志 → 不算就绪（别把模型名里的数字当端口）",
                    !MainForm.NinferReadyMarker("listening on http://0.0.0.0:8069 | model x", 8068)
                    && !MainForm.NinferReadyMarker("loading model 8068 tokens", 8068)
                    && !MainForm.NinferReadyMarker("", 8068));

                // ⑦ 灯态与按钮文字：与另两页同一句话（DEC-055 那条一致性判据现在管三页）
                var nIdle = MainForm.NinferLight(false, false, false, false, true, 8068);
                var sIdle = MainForm.StrataLight(false, false, false, false, true, true, 8068);
                var lIdle = MainForm.LlamaLight(false, "", true, false, 8068);
                Check("Ninfer 灯：待机那一句与 Strata/llama 一字不差（DEC-055/082/107「待机 · 模型未加载 · 端口 X」）",
                    nIdle.Desc == sIdle.desc && nIdle.Desc == lIdle.desc && nIdle.State == MainForm.LightState.Orange,
                    nIdle.Desc + " / " + sIdle.desc + " / " + lIdle.desc);
                Check("Ninfer 灯：只有两态动作没有重载 —— 起停按钮与模型行按钮的措辞各归各",
                    MainForm.StartStopTextForNinfer(true, false, false) == "停止"
                    && MainForm.StartStopTextForNinfer(false, false, true) == "停止"      // 2026-10-15：三档统一「停止」（原「停止网关」）
                    && MainForm.StartStopTextForNinfer(false, false, false) == "启动"
                    && MainForm.ModelActionTextForNinfer("", false) == "加载"
                    && MainForm.ModelActionTextForNinfer("", true) == "卸载"
                    && MainForm.ModelActionTextForNinfer("加载中…", true) == "加载中…");
                Check("Ninfer 灯：失败优先于一切，外部服务单独一句（不是本壳拉起的不能说成在跑）",
                    MainForm.NinferLight(true, true, true, true, true, 1).State == MainForm.LightState.Red
                    && MainForm.NinferLight(false, false, false, true, false, 8068).Desc.Contains("外部"));

                // ⑧ 后端方块（DEC-087 形④ / DEC-089 单枚只画那一枚）
                Check("Ninfer 方块：两侧都启用 = 两枚面（WIN 在前 WSL 在后），只剩一枚 = 只画那一枚（Solo、不可点、无动画）",
                    MainForm.NinferFaces(new Settings { NinferWinOn = true, NinferWslOn = true }).SequenceEqual(new[] { "WIN", "WSL" })
                    && MainForm.NinferFaces(new Settings { NinferWinOn = true, NinferWslOn = false })[1] == ""
                    && MainForm.NinferFaces(new Settings { NinferWinOn = false, NinferWslOn = true })[0] == "WSL");
                Check("Ninfer 方块：点一下换到还开着的另一侧；另一侧关了就没有另一侧",
                    MainForm.NextNinferBackend(new Settings { NinferBackend = Wn, NinferWinOn = true, NinferWslOn = true }) == S
                    && MainForm.NextNinferBackend(new Settings { NinferBackend = S, NinferWinOn = true, NinferWslOn = true }) == Wn
                    && MainForm.NextNinferBackend(new Settings { NinferBackend = Wn, NinferWinOn = true, NinferWslOn = false }) == "");
                Check("Ninfer 方块：只有引擎真在跑时切换要先确认（DEC-157 起**只立网关不再弹** —— 那一档不停任何东西、"
                    + "文案里那件名本来就是空的）",
                    MainForm.NinferSwapNeedsConfirm(true) && !MainForm.NinferSwapNeedsConfirm(false));
                Check("Ninfer 后端归属：关掉那一侧 = 那一侧全隐身（不扫、不起、下拉不列、高级区不出那侧的格）",
                    !MainForm.NinferBackendOn(new Settings { NinferWslOn = false }, S)
                    && MainForm.NinferBackendOn(new Settings { NinferWslOn = false }, Wn)
                    // 只剩一侧开着时界面兜到那一侧（"至少启用一个"由保存校验拦）
                    && MainForm.ActiveNinferBackend(new Settings { NinferBackend = S, NinferWslOn = false, NinferWinOn = true }) == Wn);
                Check("Ninfer 格子表：WIN 不给并发数那一格（环架构级硬拒 mc>1，给格子 = 给陷阱），WSL 不给主机缓存与采样四项",
                    !MainForm.FlagShown("max-concurrency", Wn) && MainForm.FlagShown("max-concurrency", S)
                    && !MainForm.FlagShown("host-kv-mib", S) && MainForm.FlagShown("host-kv-mib", Wn)
                    && !MainForm.FlagShown("temperature", S) && MainForm.FlagShown("temperature", Wn)
                    && !MainForm.FlagShown("vision", Wn) && MainForm.FlagShown("vision", S)
                    && MainForm.FlagShown("max-context", Wn) && MainForm.FlagShown("max-context", S));

                // ⑨ 网关那一头：/v1/models 要列**全部三件**（与 Strata"只列已就绪"相反），owned_by = ninfer
                var three = new[]
                {
                    new ModelEntry("qwen3.8-27b-wsl", "", "x", true, "") { Backend = ModelEntry.BackendNinfer },
                    new ModelEntry("qwen3.8-27b-gsq-iq3s", "", "y", false, "件不存在") { Backend = ModelEntry.BackendNinfer },
                    new ModelEntry("qwen3.8-27b-swift-iq3s", "", "z", true, "") { Backend = ModelEntry.BackendNinfer },
                };
                var mj = MainForm.BuildNinferModelsJson(three);
                Check("Ninfer 网关：/v1/models 列全三件（未就绪的那件也要看得见，否则客户端切会话被引擎秒拒 404）",
                    mj.Contains("qwen3.8-27b-wsl") && mj.Contains("qwen3.8-27b-gsq-iq3s") && mj.Contains("qwen3.8-27b-swift-iq3s")
                    && mj.Contains("\"owned_by\":\"ninfer\"") && !mj.Contains("\"owned_by\":\"strata\""), mj);
                Check("Ninfer 外部实例按形状认（同族指纹：owned_by=ninfer，不能只看 200）",
                    MainForm.LooksLikeNinferModelsList(mj)
                    && !MainForm.LooksLikeNinferModelsList("{\"data\":[{\"id\":\"a\",\"owned_by\":\"llamacpp\"}]}")
                    && !MainForm.LooksLikeNinferModelsList("not json"));

                // ⑩ 显存账与"判不了就不判"（全项目第一条 nvidia-smi 判据）
                Check("Ninfer 显存账 = 权重 + 设备池那一档（DEC-156：不再加 0.5 GiB 余量；表外精度按 k8v4 那档估）",
                    MainForm.NinferGpuNeedGiB(14L * 1073741824, 55296, "k8v4") == Math.Round(14.0 + 55296 * 132.0 / 1048576, 1)
                    && MainForm.NinferGpuNeedGiB(0, 0, "k8v4") == 15.5
                    && MainForm.NinferGpuNeedGiB(14L * 1073741824, 262144, "rk4v4-e8")
                       < MainForm.NinferGpuNeedGiB(14L * 1073741824, 262144, "k8v4"));
                var scanRows = MainForm.NinferWslScanRows("18210531328 /m/a.ninfer\n/m/b.ninfer\n\n");
                Check("DEC-156 WSL 扫描行解析：`-printf '%s %p'` 的「字节数 路径」带上字节数；只有路径的行照收（字节 0 = 尺寸那格空着）",
                    scanRows.Count == 2
                    && scanRows[0].File == "/m/a.ninfer" && scanRows[0].Bytes == 18210531328
                    && scanRows[1].File == "/m/b.ninfer" && scanRows[1].Bytes == 0,
                    string.Join(" | ", scanRows.Select(r => r.File + "=" + r.Bytes)));
                Check("显存互斥：排队超时默认 300 秒、可配到 600，表外值回默认（DEC-083 第 6 条；DEC-157 起三模块共用）",
                    MainForm.GpuWakeWaitSeconds("") == 300
                    && MainForm.GpuWakeWaitSeconds("600") == 600
                    && MainForm.GpuWakeWaitSeconds("5") == 300
                    && MainForm.GpuWakeWaitSeconds("9999") == 300);
                Check("显存互斥处理那一格只认两个词（DEC-097② 撤了 off；填错 = 排队那一档静默失效，保存时就拦）",
                    MainForm.GpuMutexProblem(new Settings { GpuMutex = "queue" }) is null
                    && MainForm.GpuMutexProblem(new Settings { GpuMutex = " QUEUE " }) is null
                    && MainForm.GpuMutexProblem(new Settings { GpuMutex = "off" }) is not null
                    && MainForm.GpuMutexProblem(new Settings { GpuMutex = "wait" }) is not null);
                Check("显存互斥：旧 ini 里残留的 off 在读取侧兜成 queue（护栏不许因为改档而静默失效）",
                    MainForm.GpuMutexMode(new Settings { GpuMutex = "off" }) == "queue"
                    && MainForm.GpuMutexMode(new Settings { GpuMutex = "" }) == "queue"
                    && MainForm.GpuMutexMode(new Settings { GpuMutex = "REJECT" }) == "reject");
                // ============ DEC-159 第 2 条：那枚"排队上限"不再与网关写死的 300 秒打架 ============
                Check("DEC-159 网关等这一次唤醒的总时限跟着排队上限走（30→300 不动历史值、300→360、600→660；点名那句 504 从此到得了客户端）",
                    MainForm.GatewayWakeTimeoutSeconds(30) == 300
                    && MainForm.GatewayWakeTimeoutSeconds(300) == 360
                    && MainForm.GatewayWakeTimeoutSeconds(600) == 660
                    && MainForm.GatewayWakeTimeoutSeconds(0) == 300);
                Check("DEC-159 那枚 300 秒只留一个家：StartGateway 不许再写死 FromSeconds(300)（钉住 = 以后谁改回写死，那一档又静默失效）",
                    CodeHits("TimeSpan.FromSeconds(30" + "0),") is -1 or 0,
                    "命中 " + CodeHits("TimeSpan.FromSeconds(30" + "0),"));
                Check("DEC-159「排队等显存上限(秒)」只认 30~600 的整数，表外值在设置页保存时就拦下（口径同「显存互斥处理」：填错不许静默回默认）",
                    MainForm.GpuWakeWaitProblem(new Settings { GpuWakeWaitSec = "300" }) is null
                    && MainForm.GpuWakeWaitProblem(new Settings { GpuWakeWaitSec = " 45 " }) is null
                    && MainForm.GpuWakeWaitProblem(new Settings { GpuWakeWaitSec = "600" }) is null
                    && MainForm.GpuWakeWaitProblem(new Settings { GpuWakeWaitSec = "29" }) is not null
                    && MainForm.GpuWakeWaitProblem(new Settings { GpuWakeWaitSec = "601" }) is not null
                    && MainForm.GpuWakeWaitProblem(new Settings { GpuWakeWaitSec = "" }) is not null
                    && MainForm.GpuWakeWaitProblem(new Settings { GpuWakeWaitSec = "一分钟" }) is not null);
                // ============ DEC-159 第 1 条：起不来的一句话按**入口**分落点（远端那一击不许弹模态）============
                {
                    // 真入口（DEC-126 那条硬规矩：走真路径先喂样本 —— 这里喂的是"起不来"那两句本身）
                    var att0 = Dialogs.AttemptsForTest;
                    using var fModal = new MainForm();
                    fModal.FailNinferForTest("自检 · 手动那一击", "这一句该弹框（点了必须有看得见的落点）。", modal: true);
                    var manualTries = Dialogs.AttemptsForTest - att0;
                    var manualTitle = Dialogs.LastTitleForTest;
                    var att1 = Dialogs.AttemptsForTest;
                    fModal.FailNinferForTest("自检 · 唤醒那一击", "这一句该只进日志并原样回客户端。", modal: false);
                    Check("DEC-159 手动那一击真走到弹框那一步（尝试 +1、标题对得上），而唤醒那一击一次都不要求弹（同一处代码分两条落点）",
                        manualTries == 1 && manualTitle == "自检 · 手动那一击" && Dialogs.AttemptsForTest == att1,
                        $"手动尝试 {manualTries}｜唤醒尝试 {Dialogs.AttemptsForTest - att1}｜最近标题 [{manualTitle}]");
                    Check("DEC-159 自检期间一道模态都不真弹（DEC-127⑥ 那一族的最后一道：ShownForTest == 0 而AttemptsForTest > 0 = 闸上着、证人位仍在涨，不是空判据）",
                        Dialogs.ShownForTest == 0 && Dialogs.AttemptsForTest > 0,
                        $"尝试 {Dialogs.AttemptsForTest} / 真弹 {Dialogs.ShownForTest}");
                    Check("DEC-159 起不来的那一句话两条路共用一个家：FailNinfer 记下的原句 = 唤醒那一路交出去的句子（含段名与落点，不说「上面几行日志」）",
                        fModal.NinferStartFailure == "自检 · 唤醒那一击：这一句该只进日志并原样回客户端。"
                        && MainForm.NinferWakeFailureLine("需要约 20.8 GiB，此刻只剩 3.0 GiB") == "需要约 20.8 GiB，此刻只剩 3.0 GiB"
                        && MainForm.NinferWakeFailureLine(null) == "引擎没能起来（上面几行日志写了原因）。"
                        && MainForm.NinferWakeFailureLine("").StartsWith("引擎没能起来"),
                        $"[{fModal.NinferStartFailure}] / 兜底 [{MainForm.NinferWakeFailureLine("")}]");
                }
                // **needle 必须拆开拼**：整串写在这里会被自己扫到 = 必假红（BUG-066① 那条先例）
                // 期望次数 = 数出来的真形状：4 处 FailNinfer 传参 + 1 处接缝 = 5；两处按 modal 决定弹不弹 = preflight 那一处 + FailNinfer 本体那一处
                Check("DEC-159「起不来」的落点都表了态：四处 FailNinfer 传 modal + preflight 与 FailNinfer 本体各按 modal 走（漏一处 = 远端又能弹出框）",
                    CodeHits(", modal" + ");") is -1 or 5 && CodeHits("if (modal) Dialogs" + ".Message") is -1 or 2,
                    $"传参 {CodeHits(", modal" + ");")} / 护栏 {CodeHits("if (modal) Dialogs" + ".Message")}");
                Check("DEC-159 唤醒那一路把记下的原因原样 throw 出去（不是只回「引擎没能起来」），网关才有真句子回客户端",
                    CodeHits("NinferWakeFailureLine(started" + ".Why)") is -1 or 1,
                    "命中 " + CodeHits("NinferWakeFailureLine(started" + ".Why)"));
                Check("③（BUG-073）客户区那一张只给自检用：那一枚开关全代码只有一处置真（出图台照旧只留给人看的全窗图，含深色标题栏）",
                    CodeHits("PreviewClientShot = tr" + "ue;") is -1 or 1,
                    "命中 " + CodeHits("PreviewClientShot = tr" + "ue;"));
                // ============ DEC-157 第 1 条：那张卡上"另一枚真在显存里"这一档 = 三模块共用（以前只有 Ninfer 问这张卡）============
                var holdersEmpty = new List<MainForm.GpuHolder>();
                var holdersNinferWin = new List<MainForm.GpuHolder>
                {
                    new MainForm.GpuHolder(MainForm.NinferKey, "Ninfer 的引擎（WIN · qwen-gsq）"),
                };
                var holdersTwo = new List<MainForm.GpuHolder>
                {
                    new MainForm.GpuHolder(MainForm.StrataKey, "Strata 的服务（iq3_s）"),
                    new MainForm.GpuHolder(MainForm.NinferKey, "Ninfer 的引擎（WIN · a）"),
                };
                Check("DEC-157 显存台账：除自己之外有人占着 = 点名那枚；只剩自己 = 放行（同族切件不算抢卡）",
                    MainForm.GpuHeldByOther(MainForm.StrataKey, holdersNinferWin) is { } heldStrata
                    && heldStrata.Contains("Ninfer") && heldStrata.Contains("WIN") && heldStrata.Contains("qwen-gsq")
                    && MainForm.GpuHeldByOther(MainForm.NinferKey, holdersNinferWin) is null
                    && MainForm.GpuHeldByOther(MainForm.LlamaKey, holdersTwo) is { } heldTwo
                    && heldTwo.Contains("Strata") && heldTwo.Contains("Ninfer")
                    && MainForm.GpuHeldByOther(MainForm.StrataKey, holdersEmpty) is null,
                    MainForm.GpuHeldByOther(MainForm.StrataKey, holdersTwo)?.Replace("\n", " ") ?? "null（不该放行：Strata 自己也在台账里、还有 Ninfer）");
                // 真路径那一头：手动起件问的就是这份台账（AGENTS 那条"凡是走真路径的断言先把样本喂进去"）。
                // 只测纯函数会漏掉"快照那一步接错了线"这一族（BUG-044 / DEC-135 那条证人位的教训）。
                string? refuseStrata, refuseNinfer, refuseLlama, refuseEmpty;
                using (var fGpu = new MainForm())
                {
                    fGpu.CreateControl();
                    MainForm.GpuHoldersForTest = holdersNinferWin;
                    refuseStrata = fGpu.GpuMutexRefusal(MainForm.StrataKey);
                    refuseNinfer = fGpu.GpuMutexRefusal(MainForm.NinferKey);
                    MainForm.GpuHoldersForTest = holdersTwo;
                    refuseLlama = fGpu.GpuMutexRefusal(MainForm.LlamaKey);
                    MainForm.GpuHoldersForTest = null;
                    refuseEmpty = fGpu.GpuMutexRefusal(MainForm.StrataKey);
                }
                Check("DEC-157 真路径：手动起件那一步读的就是这份台账（Ninfer 在跑 → Strata/llama 被拒、Ninfer 自己放行；台账空 = 放行）",
                    refuseStrata is not null && refuseLlama is not null
                    && refuseNinfer is null && refuseEmpty is null,
                    refuseStrata?.Replace("\n", " ") ?? "null（Strata 该被拒却放行了）");
                // DEC-160：那一圈等待吃**这一发请求的**令牌 —— 取消 = 抛出去（调用方因此不会去起件）。
                // 两面都断：卡被占 + 取消 → 抛；卡净空 + 未取消 → 放行（只断正面那条就是空判据，DEC-127⑥ 同形）。
                bool gateThrew = false, gatePassedClean = false, gateFreePass = false;
                using (var fGate = new MainForm())
                {
                    fGate.CreateControl();
                    fGate.SetGpuMutexForTest("queue", 60);
                    using var gateCts = new CancellationTokenSource(1200);
                    try
                    {
                        // Task.Run = 把这一步挪到线程池上跑：自检跑在界面线程上，直接 GetResult() 会把
                        // `await Task.Delay(...)` 的续体排给正在被自己挡住的消息循环 = 死等（本轮实测挂过一次）
                        Task.Run(() => fGate.GpuGateAsync(MainForm.StrataKey, _ => { },
                            () => "需要约 20.8 GiB，此刻只剩 3.1 GiB", gateCts.Token)).GetAwaiter().GetResult();
                        gatePassedClean = true;                 // 走到这里 = 取消被当成"排到了"→ 会去起件（正是要治的那一条）
                    }
                    catch (OperationCanceledException) { gateThrew = true; }
                }
                using (var fFree = new MainForm())
                {
                    fFree.CreateControl();
                    fFree.SetGpuMutexForTest("queue", 60);
                    using var freeCts = new CancellationTokenSource();
                    try
                    {
                        Task.Run(() => fFree.GpuGateAsync(MainForm.StrataKey, _ => { },
                            () => null, freeCts.Token)).GetAwaiter().GetResult();
                        gateFreePass = true;
                    }
                    catch (OperationCanceledException) { }
                }
                Check("DEC-160 排队那一圈随这一发的令牌收手 = 抛取消（旧写法把取消当放行、接着去起件）；卡净空照旧放行",
                    gateThrew && !gatePassedClean && gateFreePass,
                    "排队中被取消=" + gateThrew + " 当放行=" + gatePassedClean + " 净空放行=" + gateFreePass);
                Check("DEC-157 排队那一句要说清前面是谁（支点 I：别让客户端以为是卡死），并且只取判据第一行",
                    MainForm.GpuWaitingLine("这张卡上此刻正跑着别的一枚：Ninfer 的引擎（WIN · a）。\n任一时刻只能有一件")
                        == "在排队等显存：这张卡上此刻正跑着别的一枚：Ninfer 的引擎（WIN · a）",
                    MainForm.GpuWaitingLine("这张卡上此刻正跑着别的一枚：Ninfer 的引擎（WIN · a）。\n任一时刻只能有一件"));
                // DEC-167（第 45 轮只读审查 F1）：取消**不记退避** —— 旧写法把 "A task was canceled." 也记进 5 秒退避，
                // 客户端 504 / 挂断后 5 秒内重试同一模型吃到退避、回一句英文异常原文的 503（且那 5 秒禁的与模型好坏无关）。
                Check("DEC-167 F1 取消不记退避：OCE+已取消=不记；OCE+没取消=照记；别的异常两种都照记（退避只记「这件起不来」的证据）",
                    !MainForm.WakeFailureCountsAsModelIssue(new OperationCanceledException(), new CancellationToken(true))
                    && MainForm.WakeFailureCountsAsModelIssue(new OperationCanceledException(), new CancellationToken(false))
                    && MainForm.WakeFailureCountsAsModelIssue(new InvalidOperationException("起不来"), new CancellationToken(true))
                    && MainForm.WakeFailureCountsAsModelIssue(new InvalidOperationException("起不来"), new CancellationToken(false)),
                    "OCE+取消=" + MainForm.WakeFailureCountsAsModelIssue(new OperationCanceledException(), new CancellationToken(true))
                    + " OCE+没取消=" + MainForm.WakeFailureCountsAsModelIssue(new OperationCanceledException(), new CancellationToken(false)));
                // 纯函数必须真接在 WakeAsync 的 catch 上（漏接 = 白判）。needle 拆开拼（BUG-066①：整串会被自己扫到）。
                var wakeFailHits = CodeHits("if (WakeFailureCountsAs" + "ModelIssue(ex, ct))");
                Check("DEC-167 F1 接线：WakeAsync 的 catch 按这个判据决定记不记退避（源码 1 处）",
                    wakeFailHits is -1 or 1, "命中 " + wakeFailHits);
                Check("DEC-157 等满那一句带上原判据（三族共用一个家，不是各写一套短句）",
                    MainForm.GpuWakeTimeoutLine("需要约 20.8 GiB，此刻只剩 3.1 GiB")
                        .StartsWith("在排队等显存，排到超时还没轮到：需要约 20.8 GiB"));
                // 三条真起进程的路都挂上这一档（端到端要点真进程才看得见 —— 归他实跑；这里钉源码调用点 = 判据只能这么取）
                var gpuWakeHits = CodeHits("await GpuGateAsync(" + "StrataKey, LogS, null, ct)");
                var gpuStrataHits = CodeHits("GpuMutexRefusal(" + "StrataKey)");
                var gpuLlamaHits = CodeHits("GpuMutexRefusal(" + "LlamaKey)");
                Check("DEC-157 三条路都挂上这一档：Strata 唤醒排队 1 处 + Strata 手动 1 处 + llama 手动 2 处（加载 / 单体启动）",
                    gpuWakeHits is -1 or 1 && gpuStrataHits is -1 or 1 && gpuLlamaHits is -1 or 2,
                    $"唤醒 {gpuWakeHits} / Strata 手动 {gpuStrataHits} / llama 手动 {gpuLlamaHits}"
                    + (gpuWakeHits == -1 ? "（源码不在旁边 = 这条只登记）" : ""));
                Check("DEC-157 第 5 条：只剩引擎真在跑时点方块才要确认（只立网关那一档不再弹 —— 它不停任何东西）",
                    MainForm.NinferSwapNeedsConfirm(true) && !MainForm.NinferSwapNeedsConfirm(false));
                Check("DEC-157 第 4 条配套：段那一侧被关掉时兜回开着那一侧 → StartNinferServer 里那句「后端没启用」才拦得住",
                    !MainForm.NinferBackendOn(new Settings { NinferWinOn = false, NinferWslOn = true }, "win")
                    && MainForm.ActiveNinferBackend(new Settings { NinferBackend = "win", NinferWinOn = false, NinferWslOn = true }) == "wsl");

                // ⑪ 更新链纯函数（一支 exe 一个资产；按本机算力选；版本 = 本地哈希反查资产表）
                Check("Ninfer 资产名按 compute capability：8.9 → -89、12.0a → -120a、拿不到就不猜",
                    MainForm.NinferAssetForCap("8.9") == "ninfer-serve-89.exe"
                    && MainForm.NinferAssetForCap("12.0a") == "ninfer-serve-120a.exe"
                    && MainForm.NinferAssetForCap("") == "" && MainForm.NinferAssetForCap("8.x") == "");
                const string ninferRelJson = """
[
 {"tag_name":"engine-v0.12.0-x","draft":false,"prerelease":true,"published_at":"2026-10-05T01:00:00Z","body":"新构建",
  "assets":[
    {"name":"ninfer-serve-120a.exe","browser_download_url":"https://x/c.exe","size":10,"digest":"sha256:cccc"},
    {"name":"ninfer-serve-89.exe","browser_download_url":"https://x/b.exe","size":1511845888,"digest":"sha256:19222a2a68df6f7d"}]},
 {"tag_name":"engine-v0.11.0-kvmem-20261003","draft":false,"prerelease":true,"published_at":"2026-10-03T01:00:00Z","body":"旧构建",
  "assets":[{"name":"ninfer-serve-89.exe","browser_download_url":"https://x/a.exe","size":1,"digest":"sha256:aaaa"},
            {"name":"SHA256SUMS.txt","browser_download_url":"https://x/s.txt","size":2,"digest":"sha256:bbbb"}]}
]
""";
                var rel = MainForm.ParseNinferRelease(ninferRelJson, "ninfer-serve-89.exe");
                Check("Ninfer 更新：取**带匹配资产的最新一条**（pre-release 照收、草稿不算、没匹配架构的资产不算）",
                    rel.Tag == "engine-v0.12.0-x" && rel.Asset == "ninfer-serve-89.exe"
                    && rel.Url == "https://x/b.exe" && rel.Sha == "sha256:19222a2a68df6f7d" && rel.Size == 1511845888,
                    rel.Tag + " / " + rel.Asset + " / " + rel.Sha);
                Check("Ninfer 更新：本机哈希反查 tag（逐字节对上资产 digest 才算同一个构建）",
                    MainForm.NinferTagForLocalSha(ninferRelJson, "19222a2a68df6f7d") == "engine-v0.12.0-x"
                    && MainForm.NinferTagForLocalSha(ninferRelJson, "aaaa") == "engine-v0.11.0-kvmem-20261003"
                    && MainForm.NinferTagForLocalSha(ninferRelJson, "ffffffff") == "");
                Check("Ninfer 更新：列表里没有本机那支资产 = 拒绝并说明（没有 PTX 回退，装错架构直接起不来）",
                    MainForm.ParseNinferRelease(ninferRelJson, "ninfer-serve-86.exe").Tag == "");

                // ⑫ 家底写回 = 保真法则（DEC-079 那套：按字节读、按"一行 + 它自带的行尾"拼回）
                //     这一条在 data\tmp\ 的副本上做，真件一个字不碰。
                var fidDir = MainForm.Scratch("ninfer-fidelity");
                try { Directory.CreateDirectory(fidDir); } catch { /* 建不出来就跳过这一条 */ }
                var fidPath = Path.Combine(fidDir, "ninfer_models.ini");
                var fidText = "# 文件头注释\r\n[qwen3.8-27b-gsq-iq3s]\nbackend = win \nfile = D:\\infer-engine\\engine\\a.ninfer\n"
                            + "args = --max-context 262144 --kv-dtype k8v4  --lm-head-draft\n"
                            + "# ==== 下一件 ====\n\n[qwen3.8-27b-wsl]\nbackend = wsl\nfile = /home/user/a.ninfer\nargs = --max-context 262144\n";
                var fidBytes = new UTF8Encoding(false).GetBytes(fidText);      // 无 BOM；CRLF/LF 混着 + 行尾空格 + 注释块 = 本机那份的形状
                var fidOk = false;
                try { File.WriteAllBytes(fidPath, fidBytes); fidOk = true; } catch { /* 写不动就跳过 */ }
                if (fidOk)
                {
                    var secs = NinferIni.ReadSections(fidPath);
                    Check("Ninfer 家底：段与三个键都读得出来（注释行里的 = 不算键、段尾注释块不算段）",
                        secs.Count == 2 && secs[0].Backend == "win" && secs[0].Args.Contains("--kv-dtype k8v4")
                        && secs[1].Backend == "wsl", secs.Count + " 段");
                    var ok = LlamaIni.EditSection(fidPath, "qwen3.8-27b-gsq-iq3s",
                        new[] { new LlamaIni.IniEdit("args", "--max-context 262144 --kv-dtype int8 --lm-head-draft") },
                        MainForm.NinferWriteKeys, out int ch, out string ferr, _ => null);
                    var after = File.ReadAllBytes(fidPath);
                    var afterText = new UTF8Encoding(false).GetString(after);
                    Check("Ninfer 家底：就地改写整条 args 成功（changed=1）", ok && ch == 1, ferr + "｜changed=" + ch);
                    Check("Ninfer 家底保真：只动那一行 —— 别的段、注释块、行尾空格、LF/CRLF 混排、无 BOM 全部原样",
                        after.Length != 0
                        && afterText.Contains("backend = win \n")                       // 行尾那个空格还在
                        && afterText.Contains("# 文件头注释\r\n")                        // 它那对 CRLF 没被改成 LF
                        && afterText.Contains("# ==== 下一件 ====")                     // 注释块没被当成键行
                        && afterText.Contains("[qwen3.8-27b-wsl]")                      // 别的段一个字没动
                        && !afterText.Contains("--kv-dtype k8v4") && afterText.Contains("--kv-dtype int8")
                        && !(after.Length >= 3 && after[0] == 0xEF && after[1] == 0xBB && after[2] == 0xBF));   // 仍然无 BOM
                    var rejected = LlamaIni.EditSection(fidPath, "qwen3.8-27b-gsq-iq3s",
                        new[] { new LlamaIni.IniEdit("file", "D:\\别处\\x.ninfer") },
                        MainForm.NinferWriteKeys, out int rch, out string rerr, _ => null);
                    Check("Ninfer 白名单：名单外的键（file / backend）一律拒绝，且拒绝 = 零改动",
                        !rejected && rch == 0 && new UTF8Encoding(false).GetString(File.ReadAllBytes(fidPath)) == afterText,
                        rerr);
                }
                else Check("DEC-138（E-7）用例前提：Ninfer 那份保真副本写出来了（写不出来 = 这一族用例整块没跑，看得见才算数）",
                           false, "写不出：" + fidPath);
                // 白名单只有一个家：文件层壳只写 args（backend / file / model-id 归用户手编）
                Check("Ninfer 文件层白名单 = 只有 args 这一键",
                    MainForm.NinferWriteKeys.Length == 1 && MainForm.NinferWriteKeys[0] == "args",
                    string.Join(",", MainForm.NinferWriteKeys));

                // ⑭ DEC-097（实跑第二条）：排队超时的单位换算**一处只能发生一次**
                //     基线 = 界面口径（秒）、写出去 = argv 口径（毫秒）。ApplyNinferRecord 里多除一次 1000
                //     = 界面显示 0 = 「保存」被 `>= 1` 那条判据当场拒掉（WSL 那一侧永远存不下去）。
                var wslArgs = "--max-context 262144 --kv-dtype rk4v4-e8 --kv-capacity 262144 --max-concurrency 2 --default-reasoning-effort medium --pending-timeout-ms 600000";
                var effW = MainForm.NinferEffectiveFromArgs(wslArgs);
                Check("DEC-097 排队超时基线按界面口径存 = 600（不是 600000、也不是被二次换算弄成的 0）",
                    effW["pending-timeout-ms"] == "600", effW["pending-timeout-ms"]);
                // 夹具照 wslArgs 那一把的**界面口径**取值（effort 要给值，否则 WSL 侧的空白档 = --no-thinking，
                // 那是设计行为、不是"值没变也被写"）
                var stW = new Settings { NinferCtx = "262144", NinferKvDtype = "rk4v4-e8", NinferPool = "262144",
                                         NinferConcurrency = "2", NinferPendingSec = "600", NinferEffort = "medium" };
                var pendArgv = MainForm.NinferEditsFromUi(stW, S).First(e => e.Flag == "pending-timeout-ms").Value ?? "";
                Check("DEC-097 排队超时：界面值原样取出（600），只有写出去才换算成毫秒（600000）",
                    MainForm.NinferUiValue("pending-timeout-ms", stW, S) == "600" && pendArgv == "600000", pendArgv);
                Check("DEC-097 排队超时：值没变就一条编辑都不产生（「打开看看、点保存」不许污染文件）",
                    MainForm.NinferSectionEdits(stW, S, effW).Count == 0,
                    string.Join("、", MainForm.NinferSectionEdits(stW, S, effW).Select(e => e.Flag + "=" + e.Value)));
                Check("DEC-097 明细单位：argv → 界面只翻一次（600000 报成「600 秒」，不是「0 秒」）",
                    MainForm.NinferArgvToUi("pending-timeout-ms", "600000") == "600"
                    && MainForm.NinferChangeText(new NinferIni.Change("pending-timeout-ms", "600", "300"))
                       == "pending-timeout-ms 600 秒 → 300 秒",
                    MainForm.NinferChangeText(new NinferIni.Change("pending-timeout-ms", "600", "300")));
                Check("DEC-097 保存的作用域：只校验本次真会写的那一家（另一页的格子不许把这一页的保存拦死）",
                    NinferIni.ValueProblem("pending-timeout-ms", "0", S) is not null    // 判据本身还在
                    && MainForm.GpuMutexProblem(new Settings { GpuMutex = "queue" }) is null);

                // ⑮ DEC-098②：两条"起不来"的根因各钉一条纯判据（都在他实跑的日志里）
                Check("DEC-098② 「上下文 > 设备池」这条只判 WIN：WSL 那代是全驻留形状（池 = 上下文才是它调好的样子）",
                    MainForm.CtxVsPoolProblem(NinferIni.BackendWin, "262144", "262144") is not null
                    && MainForm.CtxVsPoolProblem(NinferIni.BackendWin, "262144", "55296") is null
                    && MainForm.CtxVsPoolProblem(NinferIni.BackendWsl, "262144", "262144") is null   // 就是 qwen3.8-27b-wsl 那把
                    && MainForm.CtxVsPoolProblem(NinferIni.BackendWsl, "1024", "4096") is null
                    && MainForm.CtxVsPoolProblem(NinferIni.BackendWin, "", "55296") is null,         // 没填 = 不判这条
                    MainForm.CtxVsPoolProblem(NinferIni.BackendWin, "262144", "262144") ?? "放行");
                var winArgv = MainForm.NinferWinArgv("D:\\infer-engine\\engine\\gsq.ninfer",
                    "--max-context 262144 --max-concurrency 1 --model-id gsq");
                Check("DEC-098② 件路径必须是第一个位置参数（引擎 usage = <model.ninfer> [options]，缺了就把「1」当件名）",
                    winArgv.Count == 7 && winArgv[0] == "D:\\infer-engine\\engine\\gsq.ninfer"
                    && winArgv[1] == "--max-context" && winArgv[^1] == "gsq" && !winArgv.Contains(""),
                    string.Join(" ", winArgv));
                Check("DEC-098② 起前算数那行报的是 MiB（原来写 GiB：「主机池 16384 GiB」看着像算错了）",
                    NinferIni.HostPages("262144", "55296", "16384", "k8v4").Text.Contains("16384 MiB")
                    && !NinferIni.HostPages("262144", "55296", "16384", "k8v4").Text.Contains("GiB"),
                    NinferIni.HostPages("262144", "55296", "16384", "k8v4").Text);

                // ⑯ DEC-100①：左栏那一击做什么 = 纯函数四态；次序与"都没跑 → 只立网关"是本轮的核心改动
                Check("DEC-100① 「启动」四态次序：在跑→停引擎+关网关 / 外部→先结束它 / 只剩网关→关网关 / 都没跑→只立网关",
                    MainForm.PickNinferServiceMove(true, false, true) == MainForm.NinferServiceMove.StopBoth
                    && MainForm.PickNinferServiceMove(true, true, false) == MainForm.NinferServiceMove.StopBoth
                    && MainForm.PickNinferServiceMove(false, true, true) == MainForm.NinferServiceMove.StopExternal
                    && MainForm.PickNinferServiceMove(false, false, true) == MainForm.NinferServiceMove.StopGatewayOnly
                    && MainForm.PickNinferServiceMove(false, false, false) == MainForm.NinferServiceMove.GatewayOnly);
                Check("DEC-100②/107 待机那一句三页一字不差、且带回了「模型」二字（左栏加宽到 200 之后放得下）",
                    MainForm.StrataLight(false, false, false, false, true, false, 8068).desc
                        == MainForm.LlamaLight(false, "", true, false, 8068).desc
                    && MainForm.StrataLight(false, false, false, false, true, false, 8068).desc
                        == MainForm.NinferLight(false, false, false, false, true, 8068).Desc
                    && MainForm.NinferLight(false, false, false, false, true, 8068).Desc == "待机 · 模型未加载 · 端口 8068"
                    && MainForm.NinferLight(false, true, true, false, false, 8068).Desc == "运行中 · 模型已加载 · 端口 8068",
                    MainForm.NinferLight(false, true, true, false, false, 8068).Desc);

                // ⑰ DEC-100③：网关让位只看"公开口真撞车"（用户实跑问：为什么起一个另一个就退出）
                var twoUp = new[] { ("strata", 8077), ("ninfer", 8068) };
                Check("DEC-100③ 各守各的公开口 = 两份网关能同时在线（起 Ninfer 不停 Strata 的，反之也一样）",
                    MainForm.GatewaysToDisplace(twoUp, "ninfer", 8068).Count == 0
                    && MainForm.GatewaysToDisplace(twoUp, "strata", 8077).Count == 0,
                    string.Join("、", MainForm.GatewaysToDisplace(twoUp, "ninfer", 8068)));
                Check("DEC-100③ 公开口撞车时照旧先让对方（老口径没丢：把 Ninfer 的公开口填成 8077 就会让位）",
                    MainForm.GatewaysToDisplace(twoUp, "ninfer", 8077).SequenceEqual(new[] { "strata" })
                    && MainForm.GatewaysToDisplace(twoUp, "strata", 8068).SequenceEqual(new[] { "ninfer" })
                    && MainForm.GatewaysToDisplace(twoUp, "strata", 8077).Count == 0,   // 自己不算
                    string.Join("、", MainForm.GatewaysToDisplace(twoUp, "ninfer", 8077)));
                var listenLine = Gateway.GatewayListenLine(System.Net.IPAddress.Any, 8068);
                Check("DEC-100③ 网关那句监听行不再点名 Strata（两族共用一句，归属由日志通道分）",
                    !listenLine.Contains("Strata") && listenLine.Contains("引擎跑内部端口")
                    && listenLine.Contains("8068"), listenLine);

                // ⑱ DEC-101①：KV 精度下拉 = 用户 2026-10-08 批复保留的五档（bf16/int8/rk4v4/rk2v4-e8 不再列）；
                // 去掉的那四档引擎仍认 → 校验照旧收九档（老文件读得出、保存不拦）—— 两条一起钉，别只改一半
                var kvWin = NinferIni.KvDtypesFor(NinferIni.BackendWin);
                var kvWsl = NinferIni.KvDtypesFor(NinferIni.BackendWsl);
                Check("DEC-101① KV 精度下拉 = 保留的五档、两代同一批，且四枚批掉的档一个都不在",
                    kvWin.SequenceEqual(new[] { "fp8", "nvfp4", "k8v4", "rk8v4", "rk4v4-e8" })
                    && kvWsl.SequenceEqual(kvWin)
                    && !kvWin.Intersect(new[] { "bf16", "int8", "rk4v4", "rk2v4-e8" }).Any(),
                    string.Join("/", kvWin));
                Check("DEC-101① 批掉的四档引擎仍认（校验还是全九档：段里手写过的不拦；表外的照旧拒）",
                    NinferIni.AllowedKvDtypes.Length == 9
                    && NinferIni.AllowedKvDtypes.All(v => NinferIni.ValueProblem("kv-dtype", v, NinferIni.BackendWin) is null)
                    && NinferIni.AllowedKvDtypes.All(v => NinferIni.ValueProblem("kv-dtype", v, NinferIni.BackendWsl) is null)
                    && NinferIni.ValueProblem("kv-dtype", "q8_0", NinferIni.BackendWin) is not null,   // 表外值照旧拒
                    string.Join("/", NinferIni.AllowedKvDtypes));

                // ⑲ DEC-101②：「● 待机 · 模型未加载」三页一字不差、必须同色 —— 原来 llama 那支写的是 DarkOrange。
                // DEC-125④（2026-10-09 他实跑再点名）：那句的**颜色跟着下方状态灯走**（"不要使用新颜色"），
                // 于是钉的值由 SteelBlue 改判成灯表那一档橙；改回蓝、或只动一页，这条会红。
                var sw = MainForm.StatusTextForStrata(false, false, false, false, true);
                var lw = MainForm.StatusTextForLlama(false, "", true, false, false);
                var nw = MainForm.StatusTextForNinfer(false, "", false, false, true);
                Check("DEC-125④（改判 DEC-101②）待机那句三页一字不差、同色 = 灯表那一档橙（改回 SteelBlue 或只动一页 = 这条会红）",
                    sw.Text == "● 待机 · 模型未加载" && sw.Text == lw.Text && lw.Text == nw.Text
                    && sw.Color.ToArgb() == MainForm.LightColor(MainForm.LightState.Orange).ToArgb()
                    && lw.Color.ToArgb() == sw.Color.ToArgb() && nw.Color.ToArgb() == sw.Color.ToArgb(),
                    $"{lw.Text} / {lw.Color}");
                Check("DEC-101② 运行中（绿）/ 已停止（灰）三页也别漂色（顺手核对，不额外改行为）",
                    MainForm.StatusTextForStrata(false, true, true, false, false).Color.ToArgb() == Color.ForestGreen.ToArgb()
                    && MainForm.StatusTextForLlama(false, "", true, true, false).Color.ToArgb() == Color.ForestGreen.ToArgb()
                    && MainForm.StatusTextForNinfer(false, "", true, false, false).Color.ToArgb() == Color.ForestGreen.ToArgb()
                    && MainForm.StatusTextForStrata(false, false, false, false, false).Color.ToArgb() == Color.Gray.ToArgb()
                    && MainForm.StatusTextForLlama(false, "", false, false, false).Color.ToArgb() == Color.Gray.ToArgb()
                    && MainForm.StatusTextForNinfer(false, "", false, false, false).Color.ToArgb() == Color.Gray.ToArgb(),
                    string.Join(" / ", MainForm.StatusTextForStrata(false, true, true, false, false).Color,
                        MainForm.StatusTextForLlama(false, "", true, true, false).Color,
                        MainForm.StatusTextForNinfer(false, "", true, false, false).Color));
                Check("DEC-100④ 新补进来的档不拆另一条护栏：swift 件 × 行粒度 KV（fp8/int8/rk*）仍起前拒绝",
                    NinferIni.IsRowGranularKv("rk4v4") && NinferIni.IsRowGranularKv("rk2v4-e8")
                    && NinferIni.IsRowGranularKv("fp8") && NinferIni.IsRowGranularKv("int8")
                    && NinferIni.IsRowGranularKv("rk8v4") && NinferIni.IsRowGranularKv("rk4v4-e8")
                    && !NinferIni.IsRowGranularKv("k8v4") && !NinferIni.IsRowGranularKv("bf16"),
                    // nvfp4 上游那份禁忌文档没点名（只点了 fp8 / rk8v4 / int8）→ 不猜，留在表外 = 这条不拦
                    string.Join("/", NinferIni.RowGranularKv) + "｜nvfp4 判=" + NinferIni.IsRowGranularKv("nvfp4"));

                // ⑬ DEC-097③：WSL 那一侧的版本探针（脚本从 stdin 喂给 sh -s，解析是纯函数，自检不进 VM）
                var good = "SHA=9f2b1c3d4e5f60718293a4b5c6d7e8f90123456789abcdef0123456789abcdef\nVER=v0.6.1-3-g1a2b3c4\nREPO=/home/user/ninfer-4090-yarn";
                var pg = MainForm.ParseNinferWslProbe(good);
                Check("DEC-097③ WSL 探针：sha256 + git describe 都读得出来（件在 VM 里，本机判不了路径）",
                    pg.Version == "v0.6.1-3-g1a2b3c4" && pg.Sha.StartsWith("9f2b1c3d") && pg.Problem is null,
                    pg.Version + " / " + pg.Sha + " / " + (pg.Problem ?? "无"));
                var pmiss = MainForm.ParseNinferWslProbe("MISSING");
                Check("DEC-097③ WSL 探针：VM 里没有那只引擎 = 点名说清（不混成「未知」）",
                    pmiss.Version.Length == 0 && pmiss.Problem is not null && pmiss.Problem.Contains("引擎"), pmiss.Problem ?? "无");
                var nogit = MainForm.ParseNinferWslProbe("SHA=aaaa\nVER=\nREPO=");
                Check("DEC-097③ WSL 探针：读不到 git 提交时单独说一句（没装 git / 不是检出），sha 照收",
                    nogit.Version.Length == 0 && nogit.Sha == "aaaa" && nogit.Problem!.Contains("git"), nogit.Problem ?? "无");
                Check("DEC-097③ WSL 探针：空输出与噪声都不编出版本来（判不了就当没这条）",
                    MainForm.ParseNinferWslProbe("").Version.Length == 0
                    && MainForm.ParseNinferWslProbe("bash: line 1: git: command not found").Sha.Length == 0);
                var script = MainForm.NinferWslProbeScript("/home/user/a b/ninfer-serve");
                Check("DEC-097③ WSL 探针脚本：路径按单引号嵌进正文（走 stdin 喂 sh -s，不经 Windows 那层引号）",
                    script.Contains("B='/home/user/a b/ninfer-serve'") && script.Contains("sha256sum")
                    && script.Contains("describe --always --dirty"), script[..Math.Min(60, script.Length)]);
                // DEC-097④：那一对缓存键要进得出 ini（"固定"= 活得过重启）；它们不是参数格，落 [Current]
                var vset = new Settings { NinferWinVersion = "engine-v0.12.0-x", NinferWinVersionSha = "aa",
                                          NinferWslVersion = "v0.6.1-3-g1a2b3c4", NinferWslVersionSha = "bb" };
                var vlines = MainForm.BuildConfigLines(vset, new Dictionary<string, Settings>(),
                    new MainForm.WindowRect(10, 20, 300, 400, false));
                var vback = new Settings();
                MainForm.ParseConfigLines(vlines, vback, new Dictionary<string, Settings>(),
                    new MainForm.ConfigParseResult());
                Check("DEC-097④ 「版本 + 当时的 sha」那两对键进得出 ini（查过之后固定 = 活得过重启）",
                    vback.NinferWinVersion == "engine-v0.12.0-x" && vback.NinferWinVersionSha == "aa"
                    && vback.NinferWslVersion == "v0.6.1-3-g1a2b3c4" && vback.NinferWslVersionSha == "bb",
                    string.Join(" / ", vlines.Where(l => l.StartsWith("NinferWin") || l.StartsWith("NinferWslVer"))));
            }
    }
}
