namespace MultiLauncher;

// 高级区内容（各模块的开关与格子、藏/回成对、对齐判据）。
// 2026-10-15 从 MainForm.UiCards.cs 整段搬出，一行未改。
public partial class MainForm
{
    // ---------------- 高级区内容 ----------------

    void BuildAdvancedItems()
    {
        _advStrata.BackColor = UiTheme.Card;
        _advLlama.BackColor = UiTheme.Card;

        // —— Strata 一套（B 段：KV 精度挪去卡2 通用行；指定显卡由卡2 挪入这里）——
        AddAdv(_advStrata, "指定显卡", _txtGpu, "留空 = 引擎自行选卡；0 = 单卡（nvidia-smi 编号）；0,2 = 多卡按层拆分");
        AddAdv(_advStrata, "投机解码", _txtSpec, "--spec：MTP 每轮草稿 token 数");
        AddAdv(_advStrata, "最低接受率", _txtSpecMinP, "--spec-min-p：草稿被接受所需的最低概率");
        AddAdv(_advStrata, "显存预留", _txtVramReserve, "--vram-reserve-mib：给视觉编码器留的显存");
        AddAdv(_advStrata, "expert-cache", _txtExpertCache, "--expert-cache：auto = 由引擎按可用显存决定常驻专家槽");
        AddAdv(_advStrata, "prefill", _txtPrefill, "--prefill：提示词分块预填充，auto = 引擎自适应");
        AddAdv(_advStrata, "视觉", _cboVision, "让模型读图片；模型没配视觉块时不可用（要跑 SETUP）");
        AddAdv(_advStrata, "层拆分起点", _txtLayerSplit, "--layer-split：多卡时后续卡各从第几层开始，留空 = auto");
        AddAdv(_advStrata, "arena pin (GiB)", _txtArenaPin, "STRATA_ARENA_PIN_GIB：映射主机页上限；留空 = 沿用配置里的值（本机两份配置都是 16，别删）；0/auto ≈ 不设限会崩，8 = 应急降级");
        // （「思考深度」DEC-076 候选起住卡1 第三格；「附加参数」DEC-075 起住卡2 第二行；「低内存模式」整链删除——都不在高级区了）
        _advStrataToggles.Add(("KV 驻留内存", _swKvRes, "--kv-resident 32768：64K 以上上下文时把 KV 放内存", null));
        _advStrataToggles.Add(("STRATA_HEAD_LATE", _swHeadLate, "仅供 A/B 对照，正常情况下不要开", null));

        // —— Ninfer 一套（DEC-096：值都住在 ninfer_models.ini 选中那一段的 args 里；格子随当前后端换）——
        //  设备池/预填充/主机缓存 = 同一格位换项表（选项按代给，DEC-086：WSL 没有 dflash2）
        //  表外值（段里手写过、界面项表没有的）由 SelectWithFallback 补成一项再选中，不静默丢
        //  DEC-107（他第 2 条：「单选项(除了空白)下拉菜单就不用下拉形式来做，只用输入框，投机深度虽然
        //    有 2 个选项，但也纳入进来」）：**投机方式与投机深度改成输入框** —— 取值仍由起前校验拦
        //    （WSL 只认 mtp/dflash、WIN 只认 dflash2、深度须为 ≥1 的整数，非法值拒绝启动并说明）；
        //    共享前缀上限（--max-shared-prefixes，唯一安全值 0）**整格去掉**（他第 1 条），那枚旗标
        //    照旧从段里透传，起前"必须 0"的护栏仍在（判实际命令行，缺了拒绝启动）。
        bool WinOnly() => ActiveNinferBackend(_config.Current) == NinferIni.BackendWin;
        bool WslOnly() => ActiveNinferBackend(_config.Current) == NinferIni.BackendWsl;
        _advNinfer.BackColor = UiTheme.Card;
        // 同一格位两代各一只控件的三档：WSL 文本框、WIN 下拉（原型对照表「施工唯一判据源」那一行）。
        // 读值只有一个家 = NinferPoolValue()/NinferDraftValue()/NinferPrefillValue()，别在收集处再分岔一次。
        AddAdv(_advNinfer, "设备池", _txtNinferPoolWsl, "显存里的 KV 池（--kv-capacity），64 token = 1 页。" +
            "WSL 那代是全驻留形状（现值 = 上下文 262,144）；auto 与 17,920 以下一律拒绝启动（超池逐字节重发会把实例打砖）", WslOnly);
        AddAdv(_advNinfer, "设备池", _cboNinferPool, "显存里的 KV 池（--kv-capacity），64 token = 1 页。" +
            "WIN 只有这三档；auto 与 17,920 以下一律拒绝启动（超池逐字节重发会把实例打砖）", WinOnly);
        AddAdv(_advNinfer, "分块预填充", _txtNinferPrefillWsl, "长上下文的分块预填充（--prefill-chunk，WSL 文本框）。" +
            "留空 = 从命令行消失 = 用引擎默认", WslOnly);
        AddAdv(_advNinfer, "分块预填充", _cboNinferPrefill, "长上下文的分块预填充（--prefill-chunk）。WIN 大于 4,096 拒绝启动（实测会丢埋针）", WinOnly);
        AddAdv(_advNinfer, "主机缓存", _cboNinferHostKib, "内存里钉住的主机 KV 池（--host-kv-mib，只 WIN —— WSL 那代没有这枚旗标）。" +
            "大于 16,384 拒绝：256K 内实测无收益、纯吃内存", WinOnly);
        AddAdv(_advNinfer, "投机方式", _txtNinferSpec, "草稿头是哪一路（--spec）。取值按代给：WSL = mtp / dflash，WIN = dflash2；" +
            "留空 = 不写这枚；写错 = 引擎 FATAL missing component 几秒就退（起前会校验）");
        AddAdv(_advNinfer, "投机深度", _txtNinferDraftWsl, "每轮草稿 token 数（--draft-tokens，WSL 文本框）。" +
            "温度 0 下改深度会换开放文本的措辞", WslOnly);
        AddAdv(_advNinfer, "投机深度", _txtNinferDraft, "每轮草稿 token 数（--draft-tokens）。WIN 常用 4 / 12 两档（起前校验只要求 ≥1 的整数）。" +
            "温度 0 下改深度会换开放文本的措辞", WinOnly);
        AddAdv(_advNinfer, "输出上限", _txtNinferMaxTokens, "单次回复的 token 上限（--default-max-tokens）；思考 token 也计入。必须 > 0 才会写进命令行");
        AddAdv(_advNinfer, "temperature", _txtNinferTemperature, "服务级默认温度（--temperature，只 WIN）；客户端显式传参时以客户端为准", WinOnly);
        AddAdv(_advNinfer, "top-p", _txtNinferTopP, "服务级默认 top-p（--top-p，只 WIN）", WinOnly);
        AddAdv(_advNinfer, "top-k", _txtNinferTopK, "服务级默认 top-k（--top-k，只 WIN）", WinOnly);
        AddAdv(_advNinfer, "presence-penalty", _txtNinferPresence, "服务级默认 presence-penalty（--presence-penalty，只 WIN）", WinOnly);
        AddAdv(_advNinfer, "并发数", _txtNinferConcurrency, "同时接几条请求（--max-concurrency，只 WSL 给格：现值 2）。" +
            "WIN 那条环架构级硬拒 mc>1，长上下文与多通道互斥，所以那一侧根本不给这一格", WslOnly);
        AddAdv(_advNinfer, "状态槽数", _txtNinferSlots, "设备状态镜像的槽数（--device-state-slots，只 WSL）。留空 = 从命令行消失 = 用引擎默认（2）", WslOnly);
        AddAdv(_advNinfer, "排队上限", _txtNinferPending, "最多排几条请求（--max-pending-requests，只 WSL）", WslOnly);
        AddAdv(_advNinfer, "排队超时（秒）", _txtNinferPendingSec, "排在队列里最多等多久（--pending-timeout-ms，只 WSL）。**界面上按秒填**，传之前 ×1000", WslOnly);
        _advNinferToggles.Add(("草稿头模式", _swNinferLmHead,
            "勾选 = 剪枝 + Q4 优化草稿头（draft 快、接受率略低）；取消 = 主模型完整 lm-head 出草稿（接受率更高、draft 略慢）。" +
            "与 llama.cpp 的同名旗标**语义相反**，别照那边的印象拨", null));
        _advNinferToggles.Add(("视觉", _swNinferVision, "多模态/视觉支持（--vision，只 WSL 那代传）；关闭可省约 0.6 GiB 显存", WslOnly));
        _advNinferToggles.Add(("保留思考", _swNinferPreserve, "响应里保留思考过程（--preserve-thinking，只 WSL 那代传）", WslOnly));
        // （DEC-107：Ninfer 也给了卡2「附加参数」格 —— 原"给了就绕过起前护栏"的顾虑随护栏改成判**实际命令行**
        //   而消失：附加参数走 BuildNinferArgvLine 末尾追加，护栏判的正是那一把最终 argv）

        // —— llama.cpp 一套（DEC-079：这些值就住在 llama_models.ini 选中模型那一段里；
        //    读 = 段 → `[*]`/文件头 → 引擎默认，写 = 「保存」只写变过的键。原「合并 ini 覆盖每个段」已退役）——
        AddAdv(_advLlama, "temperature", _txtTemperature, "留空 = 这一段与全局都没写（用引擎默认）");
        AddAdv(_advLlama, "top-p", _txtTopP, "留空 = 这一段与全局都没写（用引擎默认）");
        AddAdv(_advLlama, "top-k", _txtTopK, "留空 = 这一段与全局都没写（用引擎默认）");
        AddAdv(_advLlama, "min-p", _txtMinP, "留空 = 这一段与全局都没写（用引擎默认）");
        AddAdv(_advLlama, "presence-penalty", _txtPresencePenalty, "留空 = 这一段与全局都没写（用引擎默认）");
        AddAdv(_advLlama, "repeat-penalty", _txtRepeatPenalty, "留空 = 这一段与全局都没写（用引擎默认）");
        AddAdv(_advLlama, "spec-draft-n-max", _txtSpecDraftNMax, "留空 = 这一段与全局都没写（用引擎默认）");
        AddAdv(_advLlama, "投机方式", _cboSpecType, "留空 = 这一段与全局都没写（引擎默认 none）");
        AddAdv(_advLlama, "视觉", _cboMmproj, "mmproj 放哪：GPU 提速 / CPU 省显存；空白 = 跟随全局（模型没配 mmproj 时不可用）");
        // （「思考深度」DEC-076 候选起住卡1 第三格，与 Strata 那格换页互换；--parallel 不再由壳覆盖）
        // flash-attn（DEC-079，用户口径「fa 只做开关，不做下拉，只有开关 2 态」）：
        //   它在本机那份文件里既是 `[*]` 的全局键、也各段都写着 —— 按「段 > 全局」读生效值，写就写进选中那一段。
        //   两态没有"空白"档，所以一旦动过就在段里长期留着（要回到跟随全局：自己在 ini 里删那行，或设置页恢复备份）。
        _advLlamaToggles.Add(("flash-attn", _swFlashAttn,
            "写进选中模型那一段的 flash-attn（没写的那一段就用全局值）；量化 KV 要靠它才省显存", null));

        _advStrata.Dock = DockStyle.Top;
        _advLlama.Dock = DockStyle.Top;
        _advNinfer.Dock = DockStyle.Top;
        _advHost.Controls.Add(_advNinfer);
        _advHost.Controls.Add(_advLlama);
        _advHost.Controls.Add(_advStrata);

        foreach (var host in new[] { _advStrata, _advLlama, _advNinfer })
        {
            var h = host;
            h.Layout += (_, _) =>
            {
                int want = AdvLayout(h);
                if (h.Height != want) { h.Height = want; HeightChanged?.Invoke(); }
            };
        }
        _advStrata.Height = AdvLayout(_advStrata);
        _advLlama.Height = AdvLayout(_advLlama);
        _advNinfer.Height = AdvLayout(_advNinfer);
    }

    void AddAdv(Panel host, string label, Control ctl, string tip, Func<bool>? shown = null)
    {
        Tip.Set(ctl, tip);
        SetAdvBackdrop(ctl);
        host.Controls.Add(ctl);        // 标签由 PlaceAdv 在排版时补上
        AdvSet(host).Items.Add(new AdvItem(label, ctl, tip, false, shown));
    }

    /// <summary>高级区的一枚两态开关（DEC-082：摆位只按行带居中，别写死 Top）。shown = 当前后端出不出面。</summary>
    void AddAdvSw(Panel host, string label, ToggleSwitch sw, string tip, Func<bool>? shown = null)
        => AdvSet(host).Toggles.Add((label, sw, tip, shown));

    // 自绘控件的圆角靠 Backdrop 填**父层**底色：默认值是窗口色（比卡片深一档），放进卡片就会在圆角外露出一圈
    // "黑角"；反过来把卡片色的控件搬到窗口色的父层（DEC-182 把后端方块从卡1 搬进行容器）就会垫出一圈浅一阶的方角。
    // 所以底色一律按"父层现值"给，别逐个控件手写 —— 卡1 上 ninfer 那两只格子（`_txtPortNinfer` / `_cboNinferReasoning`）
    // 就是这么漏了好几轮：`CardStart()` 那个摆位循环里设的是 **BackColor**，而 BackColor 对 UserPaint 自绘控件无效
    // （本文件 §4 那条老坑的另一半），只有当年手写过 `Backdrop = Card` 的那几只是对的。
    static void SetBoxBackdrop(Control ctl, Color housing)
    {
        switch (ctl)
        {
            case RoundedTextBox t: t.Backdrop = housing; break;
            case DarkSelect d: d.Backdrop = housing; break;
            case ToggleSwitch s: s.Backdrop = housing; break;
            case FlatButton b: b.Backdrop = housing; break;
            case StackedToggle k: k.Backdrop = housing; break;
        }
    }
    /// <summary>读一格现在垫的色（与 SetBoxBackdrop 同一张类型表，自检拿它对着父层现值比）。</summary>
    static Color BoxBackdropOf(Control ctl) => ctl switch
    {
        RoundedTextBox t => t.Backdrop, DarkSelect d => d.Backdrop, ToggleSwitch s => s.Backdrop,
        FlatButton b => b.Backdrop, StackedToggle k => k.Backdrop, _ => ctl.BackColor,
    };
    static void SetAdvBackdrop(Control ctl) => SetBoxBackdrop(ctl, UiTheme.Card);

    readonly List<AdvItem> _advStrataItems = new();
    readonly List<AdvItem> _advLlamaItems = new();
    readonly List<AdvItem> _advNinferItems = new();

    int AdvLayout(Panel host)
    {
        var (all, toggles) = AdvSet(host);
        // 只排"当前该出面"的格子（Ninfer 那一套里 WSL / WIN 各缺一块）；不出的整枚挪出画外，不进 dump、不参与裁切
        var items = all.Where(i => i.ShownNow).ToList();
        foreach (var off in all.Where(i => !i.ShownNow))
        {
            off.Ctl.Visible = false; off.Ctl.SetBounds(-2000, -2000, off.Ctl.Width, off.Ctl.Height);
            if (off.Lb is not null) { off.Lb.Visible = false; off.Lb.Location = new Point(-2000, -2000); }
            _advLabelShown[off.Ctl] = false;                 // 证人位（自检读这个，见 AdvLabelStatesForTest）
        }
        foreach (var it in items)
        {
            it.Ctl.Visible = true;
            // 标签要跟着控件一起回来：上一轮只把控件设回 Visible，**标签还留在"不出面"那趟里设的 false 上**
            // → 切一次后端再切回来，那一页高级区就是一堆没有名字的格子（用户实跑："大量参数没有参数名称"）。
            if (it.Lb is not null) it.Lb.Visible = true;
            _advLabelShown[it.Ctl] = true;                   // 证人位与上面那句同写：漏掉恢复标签的那一行，这里也不会有 true
        }
        var shownToggles = toggles.Where(t => t.Shown?.Invoke() ?? true).ToList();
        foreach (var t in toggles.Except(shownToggles))
        {
            t.Sw.Visible = false; t.Sw.Location = new Point(-2000, -2000);
            if (t.Sw.Tag is Label lbOff) { lbOff.Visible = false; lbOff.Location = new Point(-2000, -2000); }
            _advLabelShown[t.Sw] = false;
        }
        foreach (var t in shownToggles)
        {
            t.Sw.Visible = true;
            if (t.Sw.Tag is Label lbOn) lbOn.Visible = true;   // 同上：开关的名字也要跟着回来（视觉 / 保留思考 那两枚）
            _advLabelShown[t.Sw] = true;
        }
        int colGap = UiTheme.S(this, 12);
        int w = Math.Max(UiTheme.S(this, 320), host.ClientSize.Width > 0 ? host.ClientSize.Width : UiTheme.S(this, 600));
        int col = Math.Max(UiTheme.S(this, 80), (w - colGap) / 2);
        int y = 0;
        int i = 0;
        while (i < items.Count)
        {
            var a = items[i];
            PlaceAdv(a, 0, col, y);
            if (i + 1 < items.Count)
            {
                PlaceAdv(items[i + 1], col + colGap, col, y);
                i += 2;
            }
            else i += 1;
            y += RowH + UiTheme.S(this, 10);
        }
        int x = 0;
        foreach (var (label, sw, tip, _) in shownToggles)
        {
            var lb = sw.Tag as Label;
            if (lb is null)
            {
                lb = Lbl(label, _fSmall, UiTheme.Text, UiTheme.Card);
                sw.Tag = lb;
                host.Controls.Add(lb);
                host.Controls.Add(sw);
                Tip.Set(sw, tip);
            }
            // 用户 2026-10-04 第 3 条：高级区那几枚开关"视觉上偏高"。根因确实是**对齐方式**：
            // 原来标题按 y+S(6)、开关按 y+S(4) 各摆各的固定 Top（写死的数），既没跟这一行的行带
            // （RowH，与上面那些 RowH 高的输入框同一条带）同轴，也没跟自己的标题同轴 —— 开关净高只有
            // S(20)，顶边按 S(4) 摆就等于整枚 pill 坐在行带的上半截。改成两者都按各自高度在行带里居中。
            lb.Location = new Point(x, y + (RowH - lb.Height) / 2);
            sw.Backdrop = UiTheme.Card;
            sw.Location = new Point(x + lb.Width + UiTheme.S(this, 8), y + (RowH - sw.Height) / 2);
            x += lb.Width + UiTheme.S(this, 8) + sw.Width + UiTheme.S(this, 20);
        }
        if (shownToggles.Count > 0) y += RowH;
        return y;
    }

    /// <summary>DEC-098③ 自检用：高级区每一格"在不在明面上、它的名字该不该在明面上"（三个面板 + 三套开关全收，
    /// 连被挪出画外的那一侧一起报，好让自检能确认"另一侧确实被藏起来了"= 这一次排版真跑过）。
    /// Placed = 控件没在画外；LbShown = 证人位（AdvLayout 恢复那一格时写下的）且标签自己也不在画外；
    /// LbNull = 名字还没创建（首次排版由 PlaceAdv 补上，补出来即可见）。
    /// 切一次后端再切回来时，若只把控件设回 Visible 而漏了标签，这里就会报出"控件当面面、名字不当面面"
    ///（用户实跑："高级里,大量参数没有参数名称"）。</summary>
    internal List<(string Panel, string Name, bool Placed, bool LbShown, bool LbNull)> AdvLabelStatesForTest()
    {
        var list = new List<(string, string, bool, bool, bool)>();
        bool Witness(Control c) => _advLabelShown.TryGetValue(c, out var w) ? w : true;   // 没写过 = 从没被藏起来过
        void Probe(string panel, IEnumerable<AdvItem> items)
        {
            foreach (var it in items)
                list.Add((panel, it.Label, it.Ctl.Left > -1000,
                    it.Lb is null || (Witness(it.Ctl) && it.Lb.Left > -1000), it.Lb is null));
        }
        void ProbeSw(string panel, IEnumerable<(string Label, ToggleSwitch Sw, string Tip, Func<bool>? Shown)> tgs)
        {
            foreach (var (label, sw, _, _) in tgs)
            {
                var lb = sw.Tag as Label;
                list.Add((panel + "·开关", label, sw.Left > -1000,
                    lb is null || (Witness(sw) && lb.Left > -1000), lb is null));
            }
        }
        Probe("strata", _advStrataItems);
        Probe("llama", _advLlamaItems);
        Probe("ninfer", _advNinferItems);
        ProbeSw("strata", _advStrataToggles);
        ProbeSw("llama", _advLlamaToggles);
        ProbeSw("ninfer", _advNinferToggles);
        return list;
    }

    // 自检用：把高级区每枚开关的垂直摆位量出来（物理像素）。
    // 用户 2026-10-04 第 3 条说"开关看着偏高"，改法必须能用数验证：行带顶 = 面板高 − RowH（面板高就是
    // AdvLayout 的返回值，不是从开关位置反推的，所以这条判据不是自证）。判据 = 开关中心与标题中心都落在
    // 行带中心的 ±1px 内（旧写法按固定 Top 摆，150% 下开关中心比行带中心高约 6px）。
    internal List<(string Name, int SwMinusLabel, int SwMinusBand, int LabelMinusBand)> AdvToggleAlignmentForTest()
    {
        var list = new List<(string, int, int, int)>();
        foreach (var (label, sw, _, shown) in _advStrataToggles.Concat(_advLlamaToggles).Concat(_advNinferToggles)
                                                    .Where(t => t.Shown?.Invoke() ?? true))
        {
            var host = sw.Parent;
            var lb = sw.Tag as Label;
            if (host is null || lb is null || host.Height <= RowH)
            {
                list.Add((label, int.MinValue, int.MinValue, int.MinValue));   // 没排版：交给断言报错
                continue;
            }
            int bandCenter = (host.Height - RowH) + RowH / 2;
            int swC = sw.Top + sw.Height / 2, lbC = lb.Top + lb.Height / 2;
            list.Add((label, swC - lbC, swC - bandCenter, lbC - bandCenter));
        }
        return list;
    }

    void PlaceAdv(AdvItem it, int x, int w, int y)
    {
        var host = it.Ctl.Parent;
        if (host is null) return;
        if (it.Lb is null)
        {
            it.Lb = Lbl(it.Label, _fSmall, UiTheme.TextMuted, UiTheme.Card);
            host.Controls.Add(it.Lb);
        }
        it.Lb.Location = new Point(x, y + UiTheme.S(this, 5));
        SetAdvBackdrop(it.Ctl);
        int ix = x + it.Lb.Width + UiTheme.S(this, 8);
        it.Ctl.SetBounds(ix, y, Math.Max(UiTheme.S(this, 40), x + w - ix), RowH);
    }

}
