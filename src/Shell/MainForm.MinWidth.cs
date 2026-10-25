namespace MultiLauncher;

// 最小窗口宽度实测（三页各量一行，取最大 = 窗口下限）。
// 2026-10-15 从 MainForm.Layout.cs 整段搬出，一行未改。
public partial class MainForm
{
    // ---------------- 最小窗口宽度实测 ----------------
    // 存在的意义：撤卡 / 加项之后窗口能收多窄，要量出来而不是拍脑袋（DEC-011 的 970 当年也是反算的；
    // DEC-074 撤卡1 后实测 664——默认/最小宽 900 照旧兜得住）。
    // 判据：把卡片宽度从窄到宽试，第一个「卡片内没有可见控件越过右内边界」的宽度就是这张卡的下限；
    // 三张卡取最大，再加上左栏与各处内边距，就是窗口的最小客户宽。
    // 为什么直接改 card.Width 而不是缩窗口：卡片的摆位跑在它自己的 Layout 事件里，
    // 自检不开窗时缩 ClientSize 不会把布局推到每一张卡（实测过：那样永远量出"最小值=扫描起点"）。

    // 给定容器内被裁到卡片右内边界之外的控件。
    // 注意：不能用 c.Visible 过滤 —— 自检里窗体没显示，WinForms 的 Visible 是"含祖先"的有效可见性，
    // 整棵树都会报 false（实测过：那样任何宽度都量不到裁切）。所以隐藏的东西按容器身份显式跳过：
    // 高级区 _advHost 收起时不该参与最小宽度计算，卡片里其余控件本来就在明面上。
    List<string> ClippedIn(Control container)
    {
        var hits = new List<string>();
        int inner = container.ClientSize.Width - CardPad;      // 卡片内边距是手工摆的，不是 Panel.Padding
        void Walk(Control p)
        {
            foreach (Control c in p.Controls)
            {
                if (ReferenceEquals(c, _advHost)) continue;
                // 卡1 那张卡本身不判：DEC-182 甲之后它是行容器里 Dock=Fill 的孩子，右缘**按定义**等于行宽，
                // 拿它跟"行宽 − CardPad"比必然报裁 —— 要判的是卡里那些格子，所以直接递归进去。
                if (ReferenceEquals(c, _cardStart)) { Walk(c); continue; }
                // 控件坐标是相对**它自己的父层**的。DEC-182 甲给卡1 套了一层行容器之后，
                // 卡1 里的格子量出来是"卡内坐标"，直接跟行宽比会少算那一列 135 物理 → 裁切量不出来。
                // 所以逐层往上加 Left，把它换算到被量这个容器的坐标系（没套层时循环一次都不走，等价于旧写法）。
                int right = c.Right;
                for (Control? owner = c.Parent; owner is not null && !ReferenceEquals(owner, container); owner = owner.Parent)
                    right += owner.Left;
                if (right > inner + 1)
                    hits.Add($"{c.GetType().Name}[{(c.Text.Length > 0 ? c.Text : "")}] Right={right}>{inner}");
                Walk(c);
            }
        }
        Walk(container);
        return hits;
    }

    internal string ClipReport()
    {
        var parts = new List<string>();
        // DEC-182 甲：卡1 那一行（方块卡 + 卡1）整体取景 —— 只量卡1 会漏掉方块那一列
        foreach (var card in new Control?[] { _card1Row ?? _cardStart, _cardParams, _cardModel })
            if (card is not null) parts.AddRange(ClippedIn(card));
        // TEMP-DIAG 已删（它抓到的是：方块 Dock=Left 藏起来时仍占位，把卡1 顶到 Left=78）
        return string.Join(" ; ", parts);
    }

    // 单张卡的最小宽度（物理像素）；找不到就返回它当前宽度。
    // 必须先解除 Dock：停靠中的控件 Width 会被布局引擎按父宽改回去，卡片的 Layout 事件也就不会按新宽度跑
    // （直接缩 ClientSize 也一样推不到每一张卡 —— 实测扫到扫描起点都没测出裁切）。
    int MinCardWidthForTest(Control card, int fromLogical = 200)
    {
        var keep = card.Bounds;
        var dock = card.Dock;
        int answer = keep.Width;
        card.Dock = DockStyle.None;
        try
        {
            for (int w = UiTheme.S(this, fromLogical); w <= keep.Width; w += 2)
            {
                card.Width = w;
                if (ClippedIn(card).Count == 0) { answer = w; break; }
            }
        }
        finally { card.Dock = dock; card.Bounds = keep; }
        return answer;
    }

    // 返回（最小客户宽, 明细），逻辑像素。
    // 「客户宽 = 卡宽 + 其余占位」里的那串 16/210/16 不写死：直接拿当前宽度反算 chrome，
    // 以后左栏或内边距改了，这里跟着准（DEC-011 当年手算 690+210+48≈948 就漏过余量）。
    internal (int clientLogical, string detail) MinClientWidthForTest()
    {
        int maxCard = 0, chrome = ClientSize.Width - (_cardParams?.Width ?? 0);
        var detail = new List<string>();
        string[] names = { "card1", "card2", "card3" };
        // DEC-182 甲：card1 量的是**那一行**（方块卡 + 缝 + 卡1），量卡1 会把让出去的 90 逻辑当成余量、报小
        Control?[] cards = { _card1Row ?? _cardStart, _cardParams, _cardModel };
        for (int i = 0; i < cards.Length; i++)
        {
            if (cards[i] is null) continue;
            int need = MinCardWidthForTest(cards[i]!);
            maxCard = Math.Max(maxCard, need);
            detail.Add($"{names[i]}={UiTheme.DpiToLogical(need)}");
        }
        int client = UiTheme.DpiToLogical(maxCard + chrome);
        return (client, string.Join(" ", detail) + $" 卡片上限={UiTheme.DpiToLogical(maxCard)} 其余占位={UiTheme.DpiToLogical(chrome)}");
    }

    /// <summary>DEC-107：三页各量一遍最小客户宽，取最大（原来只量当页 —— 无头跑时谁在明面上不确定，
    /// Ninfer 页卡1 多一枚方块、是真正最宽的那一页，漏量它就兜不住最小窗）。量完还原原页。</summary>
    internal (int clientLogical, string detail, int pages) MinClientWidthAllPagesForTest()
    {
        var bak = _activeModule;
        int best = 0;
        var parts = new List<string>();
        // DEC-138（E-2）：这一族量的是**版式**的下限，与"这一页此刻启没启用"无关。以前循环里那句
        // `if (!EnabledModuleKeys().Contains(v.Key)) continue;` 让"三页各量一遍"实际量几页**跟着他那份 ini 走**
        //（Ninfer 的启用位是设置页里的一枚开关 = DEC-126 第②条明令不许的东西：用户能改的值不该决定用例红绿）。
        // 现在量之前把整张模块表临时全开、量完照原样关回去 —— 只动内存里那份 _config.Current，一个字都不落盘。
        var wasOn = ModuleRegistry.All.ToDictionary(m => m.Key, m => m.Enabled(_config.Current));
        foreach (var m in ModuleRegistry.All) m.SetEnabled(_config.Current, true);
        foreach (var v in _views)
        {
            SwitchModuleForTest(v.Key);
            var (c, _) = MinClientWidthForTest();
            parts.Add(v.DisplayName + "=" + c);
            best = Math.Max(best, c);
        }
        foreach (var m in ModuleRegistry.All) m.SetEnabled(_config.Current, wasOn[m.Key]);
        SwitchModuleForTest(bak);
        // DEC-138（E-11 同一族）：第三个值必须是**真量到的页数**（`parts.Count`），不是 `_views.Length` ——
        // 写 `_views.Length` 时自检那条"每一页都量到了"的判据里这一半恒真，按回旧写法全靠另一半才红（实测红过一次）。
        return (best, string.Join(" ｜ ", parts), parts.Count);
    }

    /// <summary>DEC-107 量 API 地址框字号候选（他：加宽后"可以增大顺便增大一点,保持能完整显示即可"）。
    /// 两个样本 = 本机真实形状（11 字符 IP）与最坏形状（五位端口）—— 选字号按"两个都放得下"。</summary>
    internal string ApiFontCandidatesForTest()
    {
        string M(string sample, float pt) => TextRenderer.MeasureText(sample, UiTheme.Mono(pt),      // DEC-138（F-5）：量字原来每次 new 一支、用完就丢
            new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width.ToString();
        var s4 = "http://192.168.1.9:8068/v1"; var s5 = "http://192.168.1.9:18082/v1";
        return $"内层宽={_txtApi.Inner.Width}｜四位 {s4}：7.5pt={M(s4, 7.5f)} 8pt={M(s4, 8f)} 8.5pt={M(s4, 8.5f)} 9pt={M(s4, 9f)}" +
               $"｜五位 {s5}：7.5pt={M(s5, 7.5f)} 8pt={M(s5, 8f)} 8.5pt={M(s5, 8.5f)} 9pt={M(s5, 9f)}";
    }

    // ---- 自检用：DEC-107（左栏加宽 / 版本行 / 卡1 卡2 跨页等宽 / 前缀去格 / 附加参数） ----

    /// <summary>激活页卡1 的「监听端口 / 思考深度」两格当前宽度（跨页等宽判据：与 Strata 页相等 = 他批的"完全一致"）。</summary>
    internal (int port, int reason) Card1ActiveSlotsForTest() => (ActiveView.PortBox().Width, ActiveView.ReasonBox().Width);
    /// <summary>卡2 领头三格（模型名称 / 上下文 / KV 精度）当前宽度（跨页等宽判据 —— DEC-107 批"前面 2 个参数
    /// 完全一致"、DEC-108 批"KV 精度宽度也要一致"）。</summary>
    internal (int model, int ctx, int kv) Card2LeadingSlotsForTest() =>
        (_txtModelName.Width, _txtContext.Width, ActiveView.KvBox().Width);
    /// <summary>Ninfer 页「附加参数」的收集出口（DEC-107 起这一族也有这一格；住 [Current]，不进段里的 args）。</summary>
    internal string NinferExtraCollectedForTest => CollectSettings().NinferExtraArgs;
    /// <summary>左栏「版本」行当前显示的文本（DEC-107 第 5 条：Ninfer 页也要有这一行，值跟随当前后端）。</summary>
    internal string SidebarVersionTextForTest => _kv.TryGetValue("版本", out var v) ? v.Text : "";
    /// <summary>投机方式 / 投机深度两只输入框的读写口（DEC-107 改输入框之后的自检需要）。</summary>
    internal void SetNinferSpecForTest(string v) => _txtNinferSpec.Text = v;
    internal string NinferSpecBoxForTest => _txtNinferSpec.Text;
    internal void SetNinferDraftWinBoxForTest(string v) => _txtNinferDraft.Text = v;
    internal string NinferDraftWinBoxForTest => _txtNinferDraft.Text;
    internal void SetNinferDraftWslBoxForTest(string v) => _txtNinferDraftWsl.Text = v;
    internal string NinferDraftWslBoxForTest => _txtNinferDraftWsl.Text;
    /// <summary>卡2 右侧两枚按钮位（打开网页 / 高级）的 Bounds —— DEC-107 判"打开网页位留空占位"：
    /// 藏起来的那枚停在 -2000（别读 Visible，无头时它是含祖先的有效可见性、一律 false）。</summary>
    internal (Rectangle web, Rectangle adv) Card2SideSlotsForTest() => (_btnOpenWeb.Bounds, _btnAdv.Bounds);

    /// <summary>左栏三处宽度（判据：都应等于 SideInnerLogical 的换算值，别再各处写 178）。</summary>
    internal (int side, int api, int light) SideWidthsForTest() =>
        (_sideCol!.Parent!.Width, _txtApi.Width, _lightRows.Count > 0 ? _lightRows[0].Row.Width : 0);

    /// <summary>「版本」行那枚小更新按钮的几何与两态（他第 6 条："不得撑高/宽，原本的行/列"；DEC-108：
    /// 缩到 S(12)、行里垂直居中、默认灰 / 探到新版本绿）：按钮尺寸 / 所在行尺寸 / 是否落在行内 / 按钮的 Bounds
    ///（含"居中"与"贴右缘"两个量）/ 此刻是不是新版本绿。</summary>
    internal (Size Btn, Size Row, bool Inside, Rectangle Bounds, bool Green) UpdateBtnGeometryForTest(string rowKey = "版本")
    {
        var b = _kvUpdateBtns[rowKey];
        var row = _kvPanels[rowKey];
        return (b.Size, row.Size,
                b.Left >= 0 && b.Right <= row.Width && b.Top >= 0 && b.Bottom <= row.Height,
                b.Bounds, b.NewVersion);
    }
    /// <summary>只有「版本」一行有一枚，别的行没有（自检穷举行集）。
    /// 2026-10-15：strata 的「源码」「引擎」两行合并成「版本」之后，这里从"引擎/版本各一枚"变成只剩一枚。</summary>
    internal bool UpdateBtnRowsForTest() =>
        _kvUpdateBtns.Count == 1 && _kvUpdateBtns.ContainsKey("版本");

    /// <summary>版本行那枚小更新按钮在客户区里的矩形（出图近景裁切用）。</summary>
    internal Rectangle UpdateBtnBoundsInClient(string rowKey) =>
        ((Form)this).RectangleToClient(_kvUpdateBtns[rowKey].RectangleToScreen(new Rectangle(Point.Empty, _kvUpdateBtns[rowKey].Size)));

    // ---- 自检用：llama.cpp 后端（M1）与 B 段模块页的可读出口 ----
    internal string ModelActionTextForTest => ModelActionText();
    internal string SelectedTagForTest => SelectedTag();
    internal string AdvancedBackendForTest => ActiveModuleKey;
    internal bool RouteModeForTest => ActiveView.RouteMode();
    internal List<ModelEntry> LlamaModelsForTest => _llamaModels;
    internal List<ModelEntry> ModelsForTest => _models;
    /// <summary>DEC-222：Ninfer 那一份列表的只读口（判据 14d/14e 要断"删掉一段之后跟着少 / 选中落到还剩下的那件"）。</summary>
    internal List<ModelEntry> NinferModelsForTest => _ninferModels;

    // DEC-065「已加载」标识：下拉里每一枚现在的状态词（自检据此断言"标没标、标在哪一枚"）
    internal List<(string Tag, string Badge)> ModelBadgesForTest() => _cboModel.Items
        .Where(i => i.Tag is ModelEntry).Select(i => (((ModelEntry)i.Tag!).Tag, i.Badge)).ToList();
    // 自检/出图用：伪造 router 报回来的已加载集合并刷一遍状态词（Strata 侧那枚要真进程，判据由纯函数穷举）
    internal void SetLlamaLoadedForTest(params string[] tags)
    {
        _llamaLoaded.Clear();
        foreach (var t in tags) _llamaLoaded.Add(t);
        RefreshModelBadges();
    }

    // ---- 2026-10-02 反馈的可读出口 ----
    // ① 胶囊 tab 高度（页头分段控件）：seg = 控件高、pill = 胶囊净高、btn = 按钮高（RowH）
    internal (int seg, int pill, int btn) SegmentMetricsForTest
        => (_segModules.Height, _segModules.PillHeightForTest, RowH);
    // ②③ llama 端口（从设置页挪到卡1「监听端口」）：盒子的文本 / 写回后的设置值
    internal string LlamaPortBoxForTest => _txtPortLlama.Text;
    internal string LlamaPortSettingForTest => _config.Current.LlamaInternalPort;
    internal void SetLlamaPortForTest(string v) => _txtPortLlama.Text = v;   // 走真实 TextChanged 链（含写回）
    // ④（BUG-027）"加载/停止重扫把上方参数盖回旧值"：卡2 上下文格的读写 + 手动触发重扫
    internal string ContextBoxForTest => _txtContext.Text;
    internal void SetContextForTest(string v) => _txtContext.Text = v;
    // DEC-135（A-1）：附加参数那格是"模板里根本没有的那一键"（只能来自打底）—— 跨件串参数的判据必须打在它上面
    internal string ExtraArgsBoxForTest => _txtExtra.Text;
    internal void SetExtraArgsForTest(string v) => _txtExtra.Text = v;
    internal void RefreshModelsForTest() => RefreshModels();
    /// <summary>DEC-097⑤：作用域重扫的测试口（只重扫那一个模块）。</summary>
    internal void RefreshModelsForTest(string onlyKey) => RefreshModels(onlyKey);
    // ⑤（DEC-033）"加载/停止不再重扫"：走"没进程可退"的停止分支（不该多出扫描日志）
    internal void StopServerForTest() => StopServer();

    // ---- B 段：激活模块 / 状态灯 / 下拉 / 左栏行集 ----
    internal string ActiveModuleForTest => _activeModule;
    /// <summary>DEC-160：左栏那枚「启动 / 停止」此刻写的是什么（外部服务那一档喂进去后要看得见落点）。</summary>
    internal string StartStopTextForTest => _btnStartStop.Text;
    /// <summary>DEC-097 自检用：当前出面的 Ninfer 后端（高级区该出面几枚开关由它决定，别在用例里写死数字）。</summary>
    internal string NinferBackendForTest => ActiveNinferBackend(_config.Current);
    // 自检用：直接换激活模块并刷视图（DEC-092 起与真路径一样：不落盘、不收口，脏状态照旧）
    internal void SwitchModuleForTest(string key)
    {
        if (!EnabledModuleKeys().Contains(key)) return;
        _activeModule = key;
        RefreshModuleScopedUi();
        RebuildLogView();
        RefreshSaveButton();    // 与真路径 SwitchModule 的尾部一致（切页不落盘也不收口）
    }
    internal List<string> ModelDropTextsForTest => _cboModel.Items.Select(i => i.Text).ToList();
    // 预设已拆（DEC-073）：原来这里有个 PresetNamesForTest，随预设功能一起去掉了
    internal string LlamaKvDisplayForTest => _cboKvLlama.Selected?.Tag as string ?? "";
    // DEC-079 自检入口：界面收集 / 换 KV 档 / 重推视图（都只在内存与控件上动，不碰文件）
    internal Settings CollectedForTest() => CollectSettings();
    internal void SetKvLlamaForTest(string tag) => SelectWithFallback(_cboKvLlama, tag);
    internal void RestoreViewForTest() => ApplySettings(_config.Current);
    internal string LlamaInheritanceTipForTest(string iniKey) =>
        Tip.TextOf(LlamaCtlForIni(iniKey)!);
    internal bool LlamaInheritedForTest(string iniKey) => _llamaInherited.Contains(iniKey);
    // DEC-138（E-7）：生效值这一格有没有**段里写出来的**值（= 不在继承集合里但非空）。
    // 「没继承样本」那条跳过要靠它把原因钉住：两处都没写也判不出继承，那才是标注逻辑坏了。
    internal bool LlamaEffectiveHasForTest(string iniKey) =>
        _llamaEffective.TryGetValue(iniKey, out var v) && v.Trim().Length > 0 && !_llamaInherited.Contains(iniKey);
    internal string LlamaInheritedListForTest =>
        string.Join(" / ", LlamaIniKeys.Where(k => _llamaInherited.Contains(k)));
    // 出图/自检用：KV 那一格「控件宽 / 文字可用宽 / 最长取值需要宽」+ 用的什么字体
    // （DEC-079 出图发现被省略号截断，数字要能自己说话，别再靠肉眼判断。
    //   注意 GDI 绘制自带留白：富余只留 8px 实测仍会被截 → 自检按 >= S(12) 钉住）
    internal (int width, int room, int need, string font) LlamaKvFitDiagForTest(string sample)
    {
        int need = TextRenderer.MeasureText(sample, _cboKvLlama.Font, new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.NoPadding).Width;
        return (_cboKvLlama.Width, _cboKvLlama.TextRoomForTest(), need,
            _cboKvLlama.Font.Name + " " + _cboKvLlama.Font.SizeInPoints + "pt");
    }
    internal int LlamaKvSlackForTest(string sample)
    {
        var d = LlamaKvFitDiagForTest(sample);
        return d.room - d.need;
    }
    /// <summary>DEC-096：Ninfer 页卡1「思考深度」那一格的量数台（DEC-079 那条教训 —— NoPadding 量出来的数偏乐观，
    /// GDI 绘制自带留白，"看着放得下"要拿控件自己的 TextRoomPx 量过才算）。返回 控件宽 / 文字可用 / 需要 / 字体。</summary>
    internal (int width, int room, int need, string font) NinferReasonFitDiagForTest(string sample)
    {
        int need = TextRenderer.MeasureText(sample, _cboNinferReasoning.Font, new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.NoPadding).Width;
        return (_cboNinferReasoning.Width, _cboNinferReasoning.TextRoomForTest(), need,
            _cboNinferReasoning.Font.Name + " " + _cboNinferReasoning.Font.SizeInPoints + "pt");
    }
    internal string NinferReasonTextForTest => _cboNinferReasoning.Selected?.Text ?? "";
    /// <summary>出图台诊断：那一格的下拉到底进没进控件树、摆在哪儿、谁压着它。</summary>
    internal string NinferKvBoxDiagForTest()
    {
        var p = _cboKvNinfer.Parent;
        return "父=" + (p?.GetType().Name ?? "无") + " 位置=" + _cboKvNinfer.Bounds +
               " 可见=" + _cboKvNinfer.Visible + " 层序=" + (p is null ? -1 : p.Controls.GetChildIndex(_cboKvNinfer)) +
               " / " + (p?.Controls.Count ?? 0) + "  _cboKv层序=" + (_cboKv.Parent is null ? -1 : _cboKv.Parent.Controls.GetChildIndex(_cboKv)) +
               " _cboKv位置=" + _cboKv.Bounds;
    }    internal int NinferReasonSlackForTest(string sample)    {
        var d = NinferReasonFitDiagForTest(sample);
        return d.room - d.need;
    }
    /// <summary>卡2「KV 精度」那一格在 Ninfer 页的富余（同一台量数台，三页共用判据）。</summary>
    internal int NinferKvSlackForTest(string sample)
    {
        int need = TextRenderer.MeasureText(sample, _cboKvNinfer.Font, new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.NoPadding).Width;
        return _cboKvNinfer.TextRoomForTest() - need;
    }
    /// <summary>DEC-088 那一格的实测：卡2「模型名称」吃余量，段名 = 对外 model-id 必须整条看得见
    /// （量数台教训 §32：判富余要拿算式里的格宽量，别拿 mock 手填的数）。room = 框宽 - 内缩那档（同 BoxW 的 S(24)）。</summary>
    internal (int width, int room, int need, string font) NinferModelFitForTest(string sample)
    {
        var f = _txtModelName.Inner.Font;
        int need = TextRenderer.MeasureText(sample, f, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width;
        return (_txtModelName.Width, _txtModelName.Width - UiTheme.S(this, 24), need, f.Name + " " + f.SizeInPoints + "pt");
    }
    internal int NinferModelSlackForTest(string sample)
    {
        var d = NinferModelFitForTest(sample);
        return d.room - d.need;
    }
    internal int LlamaKvItemCountForTest => _cboKvLlama.Items.Count;

    // DEC-145：两枚新格子的自检接缝（照思考深度那套的形状）
    internal List<string> LlamaSpecTypeOptionsForTest => _cboSpecType.Items.Select(i => i.Text).ToList();
    internal void SetLlamaSpecTypeForTest(string v) => SelectWithFallback(_cboSpecType, v);
    internal string LlamaSpecTypeCollectedForTest => CollectSettings().LlamaSpecType;
    internal List<string> LlamaMmprojOptionsForTest => _cboMmproj.Items.Select(i => i.Text).ToList();
    internal void SetLlamaMmprojForTest(string v) => SelectWithFallback(_cboMmproj, v);
    internal string LlamaMmprojCollectedForTest => CollectSettings().LlamaMmprojOffload;
    internal bool LlamaCtxEditableForTest => !_txtContext.Inner.ReadOnly;
    internal List<(string Key, string Desc, Color Dot)> LightRowsForTest =>
        _lightRows.Select(r => (r.Key, r.Desc.Text, r.Dot.Ink)).ToList();
    internal string ModeLabelForTest => _btnMode.ActiveText;
    internal bool ModeBarShownForTest => _btnMode.Visible;
    internal string BackendFaceForTest => _backendToggle.ActiveText;
    internal bool BackendSoloForTest => _backendToggle.Solo;
    /// <summary>DEC-222 补：把 Ninfer 两侧的启用位直接钉成确定态（只改内存、不写盘）。
    /// 为什么要它：方块「两枚面 / 只剩一枚」由这两个开关决定，于是**用户 ini 里 WSL 那一侧开着还是关着**会决定
    /// DEC-103 / DEC-098③ 那两条用例走不走得到「真换过两次后端」这个形状 —— 出厂默认 `NinferWslOn=false`，
    /// 方块是 Solo、`NextNinferBackend` 返回空串 = 换后端是空操作，那两条判据的前提就不成立（BUG-052 同族：
    /// 用户机器上恰好是什么配置不该决定用例红绿）。口径同 DEC-126 那条「走真路径先喂样本」。</summary>
    /// <summary>DEC-222 补：当前两侧启用位的读口（用例要先备份、测完还原）。</summary>
    internal bool NinferBackendOnForTest(string backend) => NinferBackendOn(_config.Current, backend);
    internal void SetNinferBackendsForTest(bool winOn, bool wslOn)
    {
        if (!winOn && !wslOn) return;                      // 至少留一侧（保存校验也是这条）；调用方给错就不动
        _config.Current.NinferWinOn = winOn;
        _config.Current.NinferWslOn = wslOn;
        RefreshNinferBackendUi();
    }
    /// <summary>DEC-090 的两条数：露图像素 + 侧栏那一列的总高（改前改后比它，才证得了"净增 0"）。</summary>
    internal (int Peek, int FaceH, int ControlH) ModeToggleGeometryForTest
        => (_btnMode.GeometryForTest().Peek, _btnMode.GeometryForTest().FaceHeight, _btnMode.Height);
    /// <summary>DEC-096：卡1 左侧那枚后端方块的实测尺寸（整枚宽/高、面的宽/高、露边）。
    /// DEC-088 拍的"78×62 逻辑 + 面 46×30 + 露边 8 物理"必须能量出来，不能靠看图。</summary>
    internal (int Width, int Height, int FaceW, int FaceH, int Peek) BackendToggleGeometryForTest
    {
        get
        {
            var g = _backendToggle.GeometryForTest();
            return (_backendToggle.Width, _backendToggle.Height, g.FaceWidth, g.FaceHeight, g.Peek);
        }
    }
    internal string ApiLabelForTest => _txtApi.Text;
    internal string RouterIniForTest => RouterIniPath();      // DEC-079：路径固定 = exe 同目录，不再来自 [Current]
    // B 段阶段③：日志分流（类型入参 / 读界面日志框 / 清屏按页）
    internal void LogForTest(string text, string key) => Log(text, key);
    // DEC-163 自检接缝：把「调试信息过滤」钉成确定态 —— 真路径那条断言（过滤开 = 界面挡 / 过滤关 = 显示）
    // 不许被他 ini 里这枚开关的值决定红绿（DEC-126 那条规矩：走真路径先喂样本）。
    internal void SetFilterDebugInfoForTest(bool on) => _config.Current.FilterDebugInfo = on;
    internal string LogBoxTextForTest => _txtLog.Text;
    internal void ClearActiveLogForTest() => ClearActiveLog();
    // DEC-111②：「自检不写用户日志」这条闸已从按实例（SetSaveLogForTest，全项目只有一处调用 = 另两个自检实例
    // 与出图台五个实例全漏）改成按进程（MainForm.LogFileDisabledForTest，在 SelfTest.Run / UiPreviewForm.Run 入口点名）。
    // 无调用点的实例版不留（口径同 DEC-110② 的 NoteRow 工厂）。
    // ③ 节流断言用：直接灌内存历史再重建本页视图（只动内存、不写文件；时间戳由样本给，才卡得准 5 秒窗口）
    internal int LogHistoryCountForTest => _logHistory.Count;
    /// <summary>DEC-097⑤：按通道 + 关键字数历史行 —— 断"只重扫 Ninfer 那一次点击没在 strata / llama 的账上留扫描行"。
    /// 判据要钉在**扫描行**上而不是总行数：外部实例探测（"检测到 …上已有 Strata 在跑"）是异步的，
    /// 按总数比会时灵时不灵，而那条本来就不归这一步管。</summary>
    internal int LogHistoryCountWhereForTest(string key, string contains) =>
        _logHistory.Count(l => l.Key == key && l.Text.Contains(contains, StringComparison.Ordinal));    /// <summary>DEC-097 排障用：最后 n 行日志历史（带通道键）。"保存为什么没落盘"这类断言失败，
    /// 光报 ok=False 不算落点 —— 三条早退路各写一行日志，把尾巴带进 detail 才一眼看出撞了哪条。</summary>
    internal string LogHistoryTailForTest(int n) =>
        string.Join(" ⏎ ", _logHistory.Skip(Math.Max(0, _logHistory.Count - n))
                      .Select(l => $"[{l.Key}] {l.Text}"));
    /// <summary>开发用：从第 n 条之后的日志历史（换后端那条量尺捞出来用）。</summary>
    internal List<string> LogHistorySinceForTest(int from) =>
        _logHistory.Skip(Math.Max(0, from)).Select(l => $"[{l.Key}] {l.Text}").ToList();
    /// <summary>DEC-097①：提示行当前写的是什么（家底缺失那一行要有落点，不能只活在日志里）。</summary>
    internal string HintForTest => _lblHint.Text;
    internal void SeedLogHistoryForTest(params (DateTime At, string Key, string Text)[] lines)
    {
        _logHistory.Clear();
        _logHistory.AddRange(lines);
        _logClearedAt[_activeModule] = DateTime.MinValue;
        RebuildLogView();
    }
    // ④ 状态栏断言用：绕开端口探测，直接给值看渲染与显隐（预填充/解码各两档，MTP 只累计一档）
    internal void ShowStatusBarForTest(double? prefillAvg, double? prefillNow, double? decodeAvg, double? decodeNow,
                                       double? accept)
        => _statusBar?.SetParts(StatusBarTextSegments(prefillAvg, prefillNow, decodeAvg, decodeNow, accept));
    // DEC-125②：整包喂进去（看新增那几档与"每项各带平均"那一档的渲染）
    // DEC-127②：喂进去的那串 csv **次序**就是状态栏排出来的次序（以前按表序硬排，拖了也白拖）
    // DEC-128②：items = 「实时 / 唯一那一枚」集合、avg = 平均集合（两枚各自独立）；排次序 = 两者并集喂进
    // `StatRowOrder`（与设置页写回那条路同一形状）。
    internal void ShowStatusBarPackForTest(StatPack p, string items, string avg)
        => _statusBar?.SetParts(StatusBarSegments(p,
               StatRowOrder(string.Join(",", StatusBarOrderOf(items).Concat(StatusBarOrderOf(avg)))),
               StatusBarIdSet(items), StatusBarAvgSet(avg)));
    internal void HideStatusBarForTest() => _statusBar?.SetParts(EmptyParts);
    internal int StatusBarInkHeightForTest => _statusBar?.InkHeightForTest ?? 0;
    internal void SetShowStatusBarForTest(bool on) => _config.Current.ShowStatusBar = on;
    internal void SetStatusBarItemsForTest(string items, string avg)
    { _config.Current.StatusBarItems = items; _config.Current.StatusBarAvg = avg; }
    internal void SetStatusBarOrderForTest(string order) => _config.Current.StatusBarOrder = order;
    // 自检要看"意图"：窗体没显示时控件 Visible 恒 false（父链），拿它断言会假阴性
    internal bool StatusBarIntentForTest => (_statusBar?.StatusForTest.Length ?? 0) > 0;
    internal void RefreshStatusBarForTest() => RefreshStatusBar();
    internal int StatusBarTopForTest => _statusBar?.Top ?? -1;
    internal int StatusBarBottomForTest => _statusBar?.Bottom ?? -1;
    internal int LogCardBottomForTest => _cardLog?.Bottom ?? -1;
    internal int MainBottomForTest => (_cardLog?.Parent)?.Bottom ?? -1;
    // 诊断用：把挡在中间的空白量出来（谁占了多少、padding 各是多少）
    internal string GeometryDumpForTest()
    {
        var main = _cardLog?.Parent;
        return $"form.Padding={((Form)this).Padding} client={ClientSize.Height} " +
               $"main.top={main?.Top} main.h={main?.Height} main.pad={main?.Padding} main.display={main?.DisplayRectangle} " +
               $"card.top={_cardLog?.Top} card.h={_cardLog?.Height} card.pad={_cardLog?.Padding} " +
               $"bar.top={_statusBar?.Top} bar.h={_statusBar?.Height} bar.dock={_statusBar?.Dock} " +
               "|| main.children=" + string.Join(", ", (main?.Controls.Cast<Control>() ?? Enumerable.Empty<Control>())
                   .Select(c => $"{c.GetType().Name}:dock={c.Dock},top={c.Top},h={c.Height},vis={c.Visible}"));
    }
    // 走 SelectedIndex（触发 SelectedIndexChanged），与用户点下拉等效 —— SelectByTag 是恢复路径、不触发事件
    internal void SelectModelForTest(string tag)
    {
        for (int i = 0; i < _cboModel.Items.Count; i++)
            if ((_cboModel.Items[i].Tag as ModelEntry)?.Tag == tag) { _cboModel.SelectedIndex = i; return; }
    }
}
