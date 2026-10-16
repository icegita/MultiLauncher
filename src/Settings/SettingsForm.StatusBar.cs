namespace MultiLauncher;

// DEC-125② 状态栏的子选项一排（DEC-128 起是横排的一格一格）。
// 2026-10-15 从 SettingsForm.cs 整段搬出，一行未改。
internal sealed partial class SettingsForm : Form
{
    // ---------------- DEC-125②：状态栏的子选项一排（DEC-128 起是"横排的一格一格"） ----------------
    // 硬口径（旧三条 + DEC-127 两条，仍然有效）：
    //   ① **开关管区域含占位** —— 「显示状态栏」关掉 = 这一整块不占位（不是只灰着）；
    //   ② **平均按参数区分** —— 只有 `StatItemDefs` 里 HasAvg 的那两档（预填充 / 解码）才有「平均」这一枚；
    //   ④ **次序归拖动** —— 九行的先后 = `StatusBarOrder`（全量视觉次序；老 ini 没这一键按 Items 兜底），拖出来的先后就是状态栏那串的；
    //   ⑥ 行**一行都不许少**：没勾的档也要留在排里，否则没有地方把它勾回来（`StatRowOrder` 就是为这条兜底的）。
    // DEC-128（他这一轮的批复）：
    //   ② **「实时」「平均」两枚各自独立**（他挑的"同排两枚独立"）—— 勾平均不再要求先勾实时，只勾平均 =
    //      状态栏只出平均值；带平均的两格那枚叫「实时」，**其余格去掉「显示」二字**（裸复选，标题已经说了）。
    //   ③ **横排折行**（他点名"纵向看起来有点散"）—— 一格一格从左往右排、放不下自动折行；拖动改成横着拖。
    //   ⑤ **窗高恒定**（他补话定案："设置窗高度不应该变，应该扩展内部页面长度，滚动条跟着变化就行"）——
    //      DEC-127④ 那两行（随开关长缩 + 自动滚到眼前）整套退役：那排显隐只让**页面内容**变长/变短。
    //   ① **实时预览**（DEC-128①）—— 任何一处勾选/拖动/拨主开关，都把"拟生效"的那份推去主窗状态栏
    //      （`PushStatusBarPreview`）；不保存关窗 = FormClosed 里清掉预览，回到磁盘上那一份。
    void StatusBarOptionRows(FlowLayoutPanel page, ToggleSwitch master)
    {
        var rows = _statRows;                       // 收进字段本身（readonly 出口，自检直接读它）
        var showBox = _statShow;
        var avgBox = _statAvg;
        _statPage = page;                           // 视觉次序与写回都要问这一页（拖动、自检同一条路）
        // 横排的承载格：一格一格从左往右摆、放不下折行（高度 = 算出来的，见 LayoutStatChips）。
        // 它整个是页面流里的一条，所以"开关管区域含占位"还是老口径（它自己的 Visible 跟着拨）。
        // DEC-129⑤：它换成双缓冲面板子类 —— 拖动期间要在这上面画"源格描边 + 落点定位条"。
        _statWrap = new StatDropWrap { BackColor = UiTheme.Window, Margin = new Padding(0) };
        _statWrap.Layout += (_, _) => LayoutStatChips();
        _statWrap.Paint += (_, e) => PaintStatWrap(e.Graphics);
        page.Controls.Add(_statWrap);

        // DEC-138（F-6）：那一行的行尾线 —— 开着那块时归承载格画（这里不画，免得重影），关掉时归本行画。
        // 老形状是 `NewRow(hairline: false)` 一句写死，于是"关掉那块"这一态整条线没人负责 = 通用页唯一没线的行。
        var barRow = master.Parent;
        if (barRow is not null)
            barRow.Paint += (_, e) =>
            {
                if (!StatLineFromRow(master.Checked)) return;
                int pad = UiTheme.S(this, PadX);
                using var pen = new Pen(UiTheme.Border, 1f);
                e.Graphics.DrawLine(pen, pad, barRow.Height - 1, barRow.Width - pad, barRow.Height - 1);
            };

        void SyncRows()
        {
            _statRowIntent.Clear();
            _statWrap.Visible = master.Checked;
            barRow?.Invalidate();          // DEC-138（F-6）：线的归属跟着这一拍换把手，两边都要重画一次
            foreach (var _ in rows) _statRowIntent.Add(master.Checked);
            // 关掉 = 整块**连高度一起收**（Visible=false 之外把高度归零）：滚动区的范围是由孩子边界推的，
            // 只藏不归零的话内容区量不出差别（自检那条"页内容变短"就红在这）；开着 = 按新内容重摆一遍。
            if (master.Checked) { LayoutStatChips(); StatBlockHeightAppliedForTest = _statWrap.Height; }
            // DEC-137（F-3）：关分支把"当场归零"这件事记进证人位 —— 无头时后面那次 PerformLayout 会把不可见控件的
            // Height 读回旧值（隐藏控件不参与重排），所以**读回 Height 判不了这一条**，只能读这一拍真正写进去的数。
            else { _statWrap.Height = 0; StatBlockHeightAppliedForTest = 0; SyncPageScrollBottom(); }   // DEC-130①：藏起来 = 范围归零（页自己算的是对的）
            page.PerformLayout();          // Dock/Flow 布局是懒的（DEC-107 那条：别指望副作用会重排）
            // DEC-128⑤：窗高**不动** —— 那排显隐只让页面内容变长/变短，滚动条自己跟着走。这里到此为止。
            PushStatusBarPreview();
        }

        var shown = MainForm.StatusBarIdSet(_s.StatusBarItems);
        var avgOn = MainForm.StatusBarAvgSet(_s.StatusBarAvg);
        // 格序 = 九行全量视觉次序（新键优先、老 ini 按 Items 兜底；一条都不少，见口径 ⑥）
        foreach (var id in MainForm.StatusBarRowOrder(_s.StatusBarOrder, _s.StatusBarItems))
        {
            var d = MainForm.StatDefOf(id)!.Value;
            // DEC-130②：一格 = 一枚小卡片（自画圆角底 + 描边，见 StatDropWrap.DrawCard）
            var chip = new StatDropWrap { BackColor = UiTheme.Window, Margin = new Padding(0), DrawCard = true };
            _statRowIds[chip] = d.Id;
            // DEC-129③④（他挑的"名字在前、子项弱化"）：单勾选项 = **勾在名前**的一枚（CheckItem 自带"框在左、
            // caption 在右"，caption 直接放参数名、不另起标签）；带平均的两格 = 名字（现样式）+ 两枚子勾选
            //（「实时」「平均」小一号、颜色降一档）。对象初始化器里 **Font 要写在 Caption 前**（SizeToFit 按 Font 量）。
            Label? lbl = null;
            if (d.HasAvg)
            {
                lbl = TextLabel(d.Label, 9.5f, UiTheme.Text);
                lbl.BackColor = UiTheme.Card;      // DEC-130②：垫卡片底色（默认那口写的是 Window，卡上会留深色块）
                chip.Controls.Add(lbl);
            }
            var cb = new CheckItem { Backdrop = UiTheme.Card, Checked = shown.Contains(id) };
            if (d.HasAvg)
            {
                cb.Font = SubCaptionFont();
                cb.CaptionColor = UiTheme.TextMuted;
            }
            cb.Caption = d.HasAvg ? "实时" : d.Label;
            chip.Controls.Add(cb);
            showBox[d.Id] = cb;
            CheckItem? cbAvg = null;
            if (d.HasAvg)
            {
                cbAvg = new CheckItem { Backdrop = UiTheme.Card, Checked = avgOn.Contains(id),
                                        Font = SubCaptionFont(), CaptionColor = UiTheme.TextMuted };
                cbAvg.Caption = "平均";
                chip.Controls.Add(cbAvg);
                avgBox[d.Id] = cbAvg;
            }
            rows.Add(chip);
            var avgRef = cbAvg;
            // 两枚各自独立（不再有"勾了显示才长平均"那条依附）：任意一枚动了都写回 + 重排 + 预览
            cb.CheckedChanged += (_, _) => { WriteBackInVisualOrder(); LayoutStatChips(); };
            if (avgRef is not null) avgRef.CheckedChanged += (_, _) => WriteBackInVisualOrder();
            var lblRef = lbl;
            chip.Layout += (_, _) => LayoutStatChip(lblRef, cb, avgRef);
            WireStatRowDrag(chip, lbl, cb, avgRef);      // DEC-127④ + DEC-129⑤ + DEC-131②：格子/名字/复选（除框）都能起拖
            _statWrap.Controls.Add(chip);    // 进承载格（次序 = Controls 集合的次序，拖动与自检都吃它）
        }
        master.CheckedChanged += (_, _) => SyncRows();
        _statMaster = master;
        LayoutStatChips();
        SyncRows();
        // 出口在字段里，rows/showBox/avgBox 只是本轮的别名（readonly 字段不许重指，见下面那几行）
        _ = rows; _ = showBox; _ = avgBox;
    }

    // DEC-128①：把"拟生效"的那份推去主窗预览（含主开关）。勾选 / 拖动 / 拨开关三处收口都走它。
    void PushStatusBarPreview()
        => _main.SetStatusBarPreview(_statMaster.Checked, _s.StatusBarItems, _s.StatusBarAvg, _s.StatusBarOrder);

    // 一枚"格子"内部：名字（可无）+ 一/两枚复选（左到右），高度固定、宽度 = 内容量出来（横排折行按它摆）。
    // 单勾选项没有名字标签 —— caption 就是参数名（DEC-129③：勾在名前）。
    void LayoutStatChip(Label? lbl, CheckItem cb, CheckItem? cbAvg)
    {
        int h = UiTheme.S(this, StatChipLogicalH);
        int pad = UiTheme.S(this, StatCardPadLogical);   // DEC-130②：卡片左右各留一条内衬，圆角才不啃到字
        int x = pad;
        if (lbl is not null)
        {
            lbl.Location = new Point(x, (h - lbl.Height) / 2);
            x = lbl.Right + UiTheme.S(this, 8);
        }
        cb.Location = new Point(x, (h - cb.Height) / 2);
        x = cb.Right;
        if (cbAvg is not null)
        {
            x += UiTheme.S(this, 14);
            cbAvg.Location = new Point(x, (h - cbAvg.Height) / 2);
            x = cbAvg.Right;
        }
        var chip = cb.Parent!;
        chip.Size = new Size(x + pad, h);
    }

    // 横排折行：一格一格从左往右摆，放不下就下一行；承载格的高度 = 算出来的总高（页面流靠它长缩）。
    // 宽度跟着页面（页面那边给父控件铺宽）；无头/未显示时 ClientSize 还是 0，退回一个最小宽免得全挤成一行。
    // DEC-129③：左右留出与页面其它行一致的 PadX 内边距（他实测"太靠边"—— 原来从 x=0 起摆）。
    void LayoutStatChips()
    {
        var wrap = _statWrap;
        if (wrap is null) return;
        int gapX = UiTheme.S(this, StatGapXLogical), gapY = UiTheme.S(this, StatGapYLogical);
        int inset = UiTheme.S(this, PadX);
        int padY = UiTheme.S(this, StatBlockPadYLogical);   // DEC-131①：上下与分隔线留间隔
        int maxW = Math.Max(UiTheme.S(this, 200), wrap.ClientSize.Width - inset * 2);
        var chips = StatChipOrder();
        int x = inset, y = padY, rowH = 0;
        foreach (var chip in chips)
        {
            chip.PerformLayout();                       // 先把格子内部摆好，量出它真实的宽
            int w = chip.Width;
            if (x > inset && x + w > inset + maxW) { x = inset; y += rowH + gapY; rowH = 0; }
            chip.Location = new Point(x, y);
            x += w + gapX;
            rowH = Math.Max(rowH, chip.Height);
        }
        int totalH = chips.Count == 0 ? 0 : y + rowH + padY;
        if (wrap.Height != totalH) wrap.Height = totalH;
        wrap.Parent?.PerformLayout();
        SyncPageScrollBottom();         // DEC-130①：滚动范围补到块的真底（页自己算的会短一截，见那条注释）
        wrap.Invalidate();              // 定位条/描边/卡片跟着这次重摆（拖动中重摆时用）
    }

    // DEC-130①（他实跑 v1.5.11 报"最底部文字与复选框显示不完整"的根因）：**这一页自己算的滚动范围短一截**。
    // 实测（真窗高 640 + 他的配置）：滚到底时九格合矩的底 = 888、视口底 = 876 —— 最底一排下沿贴住视口边、
    // 块下那截页底留白永远滚不出来；从外部再 PerformLayout 也修不动（页的 DisplayRect 就短这 48 物理像素）。
    // 修法 = 每次重摆后把页的 AutoScrollMinSize 补到"块的真底 + 页底留白"——滚到底时块下就有页底留白的余量。
    // 判据（这条算式）只有这一个家；自检读证人位（同步线喊过没有、补的值是多少、页里读回对不对）。
    // 用 `_statMaster.Checked`（**意图**）而不是 `wrap.Visible` —— 无头自检里 Visible 恒 false（BUG-043 那族）。
    int _scrollBottomSyncCalls;
    int _scrollBottomLastNeed = -1;
    internal int ScrollBottomSyncCallsForTest => _scrollBottomSyncCalls;
    internal int ScrollBottomLastNeedForTest => _scrollBottomLastNeed;
    internal int AutoScrollMinAppliedForTest => _statPage?.AutoScrollMinSize.Height ?? -1;
    internal int ScrollBottomNeedForTest => ScrollBottomNeed();

    int ScrollBottomNeed()
    {
        var page = _statPage;
        var wrap = _statWrap;
        if (page is null || wrap is null || !_statMaster.Checked) return 0;
        return Math.Max(0, wrap.Bottom - page.AutoScrollPosition.Y + page.Padding.Bottom);
    }

    void SyncPageScrollBottom()
    {
        var page = _statPage;
        if (page is null) return;
        int need = ScrollBottomNeed();
        _scrollBottomSyncCalls++;
        _scrollBottomLastNeed = need;
        if (page.AutoScrollMinSize.Height == need) return;
        page.AutoScrollMinSize = new Size(0, need);
    }

    static Font SubCaptionFont() => UiTheme.Ui(8.5f);   // DEC-129④：子项（实时/平均）小一档

}
