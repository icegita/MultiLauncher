using System.Diagnostics;
using System.Drawing;
using System.Text;

namespace MultiLauncher;

// Ninfer 的后端方块与卡1 监听端口（原 MainForm.Ninfer.cs 的「后端方块」+「卡1「监听端口」」两段，2026-10-15 整段搬出，一行未改）
public partial class MainForm
{
    // ---------------- 后端方块：两枚面、轮换、按后端的项表（DEC-087 形④ / DEC-088 动画 / DEC-089 露边与单枚） ----------------

    /// <summary>设置页保存时的 Ninfer 侧校验（形状同 <see cref="ModuleSwitchAtSave"/>，只是这一层在模块内部）：
    /// ① 两枚后端开关至少要留一枚；② 正跑着的那一侧不能被关掉 —— 先停服务再保存（DEC-087 第④条）。
    /// 判"哪一侧在跑"用进程起来那一刻记下的 <see cref="_ninferRunningBackend"/>：
    /// ActiveNinferBackend(新值) 在那一侧刚被关掉时会兜到还开着的那一侧，正好把要拦的情况看漏。</summary>
    internal string? NinferBackendAtSave(Settings s)
    {
        if (!s.NinferWinOn && !s.NinferWslOn)
            return "Ninfer 的两个后端至少要启用一个（两个都关掉 = 这一模块没有任何能跑的件）。";
        if (!ModuleRegistry.NinferEnabled(s)) return null;        // 整个模块要关掉归模块开关那条路拦（那边也查 ShellBusy）
        var running = _ninferRunningBackend;
        if ((NinferRunning || NinferGatewayUp) && running.Length > 0 && !NinferBackendOn(s, running))
            return $"Ninfer 的 {NinferFaceLabel(running)} 后端就要被关闭，但它的引擎/网关还在跑。" +
                   "先点左栏「停止」把 ninfer-serve 停掉，再保存。";
        return null;
    }

    /// <summary>「显存互斥处理」只有两档（DEC-097②：off 已退役 —— 它唯一的作用是让人自己承担 OOM，
    /// 而"不想排队"那一档 reject 已经覆盖）。**旧 ini 里残留的 off 在这里兜成 queue**：
    /// 判据只有这一个家，别在调用点各写各的（写侧的拦下 = <see cref="GpuMutexProblem"/>）。
    /// DEC-157：这一档从此**三模块共用**（Strata / llama 的唤醒与手动起件也吃它），方法名与键名一起去掉 Ninfer 前缀。</summary>
    internal static string GpuMutexMode(Settings s)
    {
        var m = ((s.GpuMutex ?? "").Trim().ToLowerInvariant());
        return m == "reject" ? "reject" : "queue";
    }

    /// <summary>「显存互斥处理」只认那两个词（DEC-083 第 6 条：人点启动 = 当场拒绝并点名，请求进来 = 排队等）。
    /// 非法值在设置页保存时就拦下，不许一路带到运行时被当成"不认识 = 不拦"。</summary>
    internal static string? GpuMutexProblem(Settings s)
    {
        var m = (s.GpuMutex ?? "").Trim().ToLowerInvariant();
        return m is "queue" or "reject" ? null
            : "「显存互斥处理」只能填 queue（请求排队等显存）/ reject（直接报错）。";
    }

    /// <summary>设置页 Ninfer 节那一枚「打开」：直达 exe 同目录的家底（口径同 llama 侧的 RevealRouterIni）。</summary>
    internal void RevealNinferIni()
    {
        var p = NinferIniPath();
        if (!File.Exists(p))
        {
            LogN("模型配置不在：" + p + "（把 ninfer_models.ini 放到程序目录，Ninfer 侧的模型与参数都靠它）");
            Toast.Show(this, "模型配置不在程序目录", Toast.Kind.Warn);
            return;
        }
        RevealInExplorer(p);
        LogN("$ 定位模型配置：" + p);
    }

    internal static string NinferFaceLabel(string backend) => backend == NinferIni.BackendWsl ? "WSL" : "WIN";

    /// <summary>方块的两枚面 = **还启用着的那两侧**（顺序恒 WIN 在前、WSL 在后）。
    /// 只剩一枚时第二枚给空串 → <see cref="StackedToggle.Solo"/> = 只画那一枚、不可点、无动画（DEC-089 硬口径）。</summary>
    internal static string[] NinferFaces(Settings s)
    {
        var on = new List<string>();
        if (NinferBackendOn(s, NinferIni.BackendWin)) on.Add(NinferFaceLabel(NinferIni.BackendWin));
        if (NinferBackendOn(s, NinferIni.BackendWsl)) on.Add(NinferFaceLabel(NinferIni.BackendWsl));
        return on.Count >= 2 ? new[] { on[0], on[1] }
                             : new[] { on.Count == 1 ? on[0] : NinferFaceLabel(NinferIni.BackendWin), "" };
    }

    /// <summary>当前后端在那两枚里的下标（面表与 <see cref="ActiveNinferBackend"/> 必须同一判据，别各处自己数）。</summary>
    internal static int NinferFaceIndex(Settings s)
    {
        var faces = NinferFaces(s);
        return faces[0] == NinferFaceLabel(ActiveNinferBackend(s)) ? 0 : 1;
    }

    /// <summary>点一下要换去的那一侧；另一侧被关掉了就没有另一侧（返回空串 = 这一枚点不动）。</summary>
    internal static string NextNinferBackend(Settings s)
    {
        if (NinferFaces(s)[1].Length == 0) return "";
        return ActiveNinferBackend(s) == NinferIni.BackendWsl ? NinferIni.BackendWin : NinferIni.BackendWsl;
    }

    /// <summary>切后端要不要先问一句：**只有那一侧真有引擎在跑（显存里有件）**才要问 —— 换过去 = 停掉那一件还显存。
    /// 纯函数，自检直接喂组合（没跑 = 不问，界面直接换）。
    /// DEC-157（他批的第 5 条）：原来那一档由 `NinferGatewayUp` 撑着（DEC-087 四联动第 4 条的老形状），
    /// 而 DEC-100① 起「左栏启动 = 只立网关、不碰引擎」是本族常态 → 一件都没跑时点方块也弹危险确认，
    /// 文案还写「当前这一件（）」（`_ninferRunningTag` 是空串）= 既假话也挡路（他 2026-10-12 实跑点名的第二处中断）。
    /// 网关守公开口、与哪条链在跑无关，所以那一档换后端不停任何东西 —— 判据按他点名改判，**不留死参数**（先例 DEC-141）。</summary>
    internal static bool NinferSwapNeedsConfirm(bool running) => running;

    /// <summary>下拉项表（**空白档一律在首** = 从 argv 里摘掉这枚旗标 = 用引擎默认；用户 2026-10-04 口径"留空不就行了"）。
    /// 表外值（段里手写过别的档）由 <see cref="SelectWithFallback"/> 补成一项再选中，不静默丢。</summary>
    internal static List<DarkItem> NinferSelectItems(IEnumerable<string> values)
    {
        var items = new List<DarkItem> { new() { Text = "", Tag = "" } };
        foreach (var v in values) items.Add(new DarkItem { Text = v, Tag = v });
        return items;
    }

    /// <summary>按当前后端把那几只下拉的项表换一遍，并重新选中当前值。
    /// DEC-107：投机方式 / 投机深度不再是下拉（改输入框，赋值在 RefreshNinferBackendUi 与 ApplySettings 里）。</summary>
    internal static List<DarkItem> NinferItemsFor(string flag, string backend) => flag switch
    {
        "reasoning" => NinferSelectItems(NinferIni.EffortsFor(backend)),
        "kv"        => NinferSelectItems(NinferIni.KvDtypesFor(backend)),
        "pool"      => NinferSelectItems(NinferIni.WinPools),
        "prefill"   => NinferSelectItems(NinferIni.WinPrefills),
        "hostkib"   => NinferSelectItems(NinferIni.WinHostKib),
        _ => NinferSelectItems(Array.Empty<string>()),
    };

    /// <summary>后端一换，这一页的三样东西跟着换：方块的两枚面 / 各下拉的项表 / 高级区哪几格出面。
    /// **界面上的手改不能被这次刷新顶掉**（DEC-092 只认手动改动），所以这里只换项表再按 Settings 里的当前值重新选中。</summary>
    void RefreshNinferBackendUi()
    {
        var s = _config.Current;
        var backend = ActiveNinferBackend(s);
        _backendToggle.Faces = NinferFaces(s);
        _backendToggle.Select(NinferFaceIndex(s), animate: false);   // 程序化推值不播动画（只有用户点出来的那一次才动，DEC-088）
        _cboNinferReasoning.SetItems(NinferItemsFor("reasoning", backend));
        _cboKvNinfer.SetItems(NinferItemsFor("kv", backend));
        _cboNinferPool.SetItems(NinferItemsFor("pool", backend));
        _cboNinferPrefill.SetItems(NinferItemsFor("prefill", backend));
        _cboNinferHostKib.SetItems(NinferItemsFor("hostkib", backend));
        SetNinferReasoning(s.NinferEffort);
        SelectWithFallback(_cboKvNinfer, s.NinferKvDtype);
        SelectWithFallback(_cboNinferPool, s.NinferPool);
        SelectWithFallback(_cboNinferPrefill, s.NinferPrefill);
        SelectWithFallback(_cboNinferHostKib, s.NinferHostKib);
        // DEC-107：投机方式 / 投机深度是输入框 —— 按当前值直接赋值（表外写法从此原样显示，不用 SelectWithFallback 补项）
        _txtNinferSpec.Text = s.NinferSpec;
        _txtNinferDraft.Text = s.NinferDraft;
        // 哪些格子出面由 AdvLayout 里的 shown 判据现算 → 重排一次（顺带把 _advHost 与卡片高度跟过去）
        ShowAdvancedForActiveModule();
    }

    /// <summary>点方块那一下：服务在跑就先问（换后端 = 先把当前那一件停掉还显存），确认了才真换。
    /// 取消 = 完全静默回到原样（DEC-068），控件自己不动 —— 所以界面上不会"点一下就换了"。</summary>
    void OnNinferBackendSwap()
    {
        // 用户实跑：WSL→WIN 要卡十几秒（00:52:36 重扫 → 00:52:50 才说"已切到"），WIN→WSL 却秒过。
        // 读代码点不出那 14 秒在哪一步，所以这一串每一步都量一下：**慢到 800ms 以上才写日志**（不慢就一个字都不多）。
        var sw = Stopwatch.StartNew();
        long mark = 0;
        void Step(string name)
        {
            long now = sw.ElapsedMilliseconds;
            if (now - mark >= 800) LogN($"换后端耗时：{name} {now - mark}ms（累计 {now}ms）");
            mark = now;
        }
        var s = _config.Current;
        var next = NextNinferBackend(s);
        if (next.Length == 0) return;                        // 只剩一枚启用 = Solo，正常情况下点不动（这里是双保险）
        if (NinferSwapNeedsConfirm(NinferRunning) &&
            Dialogs.Confirm(this, "换到 " + NinferFaceLabel(next) + " 后端",
                "引擎还在跑。换过去会先停掉当前这一件（" + _ninferRunningTag + "）并把显存还回去，" +
                "再按下一次请求或「启动」时起 " + NinferFaceLabel(next) + " 那侧的件。",
                null, yes: "确定", no: "取消", danger: true) != DialogResult.Yes)
        {
            LogN("没换后端：还是 " + NinferFaceLabel(ActiveNinferBackend(s)) + "（取消 = 什么都不动）");
            return;
        }
        bool wasRunning = NinferRunning;
        if (wasRunning) StopNinferServer();
        Step("停服/确认");
        _config.Current.NinferBackend = next;               // 落点 = [Current]（DEC-087 四联动第 3 条），不是主窗参数
        SaveConfig(commitParams: false);
        Step("落盘 ini");
        // 这一段是"整块换视图"（方块翻面 + 项表 + 高级区 + 重扫 + 按段推界面 + 状态灯），
        // 不按住重绘就会像切页那样闪（他 2026-10-16 点名）。上面那个模态确认**不在**这个区间里 ——
        // 跨着模态窗按住重绘会让主窗在别人盖上来时不重画，那是拿闪烁换一个更坏的毛病。
        HoldRepaint();
        try
        {
            _backendToggle.Select(NinferFaceIndex(_config.Current), animate: true);   // 这一次是用户点出来的 → 播淡出上移 / 淡入落位（DEC-088）
            RefreshNinferBackendUi();
            Step("换项表与高级区");
            RefreshModels(NinferKey);                           // 下拉按新后端过滤（DEC-088；只重扫 Ninfer = DEC-097⑤）
            Step("重扫模型");
            var tag = SelectedTag();
            if (tag.Length > 0) ApplyNinferRecord(tag);          // 卡1/卡2/高级区换成新一件的那一把 args
            Step("按段推界面");
            RefreshStatusUi();
            Step("刷状态与灯");
        }
        finally { ReleaseRepaint(); }
        // 「已切到 X 后端」这一行删了（DEC-098①，用户口径）：上面那条扫描行本来就带着生效后端名
        //（"已解析 N 个 Ninfer 模型（…当前后端 win）"），再说一遍是同义反复；点了之后的落点归 toast 与那条扫描行。
        Toast.Show(this, "后端已切到 " + NinferFaceLabel(next), Toast.Kind.Ok);
    }

    /// <summary>开发用（<c>--uiprobe nswap</c>）：直接走一次换后端，让上面那把量尺把每一步的毫秒数写进日志。</summary>
    internal void SwapNinferBackendForTest() => OnNinferBackendSwap();

    // ---------------- 卡1「监听端口」：可编辑 + 改值弹窗（DEC-089 撤只读 / DEC-091 定时机） ----------------

    /// <summary>要不要为这次改端口问一句：**值合法**且**与磁盘上那一份真不一样**才问（纯函数，自检喂组合）。
    /// 没改、改成非法值都不在这里拦（非法值由 <see cref="NinferPublicPortValue"/> 兜回 8068，
    /// 而"没改就不弹"是 DEC-068 取消静默那条口径）。</summary>
    internal static bool PortChangeNeedsConfirm(string oldPort, string newPort)
    {
        var o = (oldPort ?? "").Trim();
        var n = (newPort ?? "").Trim();
        if (o == n) return false;
        return int.TryParse(n, out var p) && p is >= 1 and <= 65535;
    }

    /// <summary>端口那一格的收口：失焦、点「保存」、切页与关窗都走这里（DEC-091 —— 少一条就等于给用户留了个绕过弹窗的后门）。
    /// 取消 = 文本回滚 + 不标脏（这次改动作废，保存/关窗照常往下走）；确定 = 写进内存 + 标脏（等「保存」落盘）。
    /// 返回 false 只有一种情况：值非法且用户不肯回滚 —— 此刻调用方别继续。</summary>
    internal bool SettleNinferPort(string where)
    {
        var committed = _config.Current.NinferPort.Trim();
        var text = _txtPortNinfer.Text.Trim();
        if (committed == text) return true;                                  // 没改
        if (!int.TryParse(text, out var p) || p is < 1 or > 65535)
        {
            LogN($"公开端口「{text}」不是 1-65535 的数：已回到 {committed}");
            Toast.Show(this, "端口得是 1-65535 的数，已回到 " + committed, Toast.Kind.Warn);
            _txtPortNinfer.Text = committed;
            return true;
        }
        if (!PortChangeNeedsConfirm(committed, text)) return true;
        if (Dialogs.Confirm(this, "改公开端口？",
                $"客户端连的就是这个端口（{committed} → {text}）。\n\n改这里 = 所有客户端配置都要跟着改。确认要改吗？",
                null, yes: "确定", no: "取消") != DialogResult.Yes)
        {
            _txtPortNinfer.Text = committed;              // 回滚 + 不标脏（「保存」保持原来的灰/亮状态）
            LogN($"没改端口：公开端口仍是 {committed}（{where}）");
            return true;
        }
        _config.Current.NinferPort = text;
        LogN($"公开端口已改为 {text}（原 {committed}）：{where}——这一步只写进内存，要点「保存」才落盘");
        UpdateApiLabel();
        RefreshRunInfo();
        WarnPortClash();
        return true;
    }

    // ============================================================================
    // S5：更新链（WIN 那条 = DEC-083 批复"第 1 轮就接"、DEC-096 落地；WSL 那条 = DEC-106 落地）
    // ============================================================================
}
