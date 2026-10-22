namespace MultiLauncher;

// DEC-127④ / DEC-128③ / DEC-129⑤：拖动调次序（定位条 + 松手归位）。
// 2026-10-15 从 SettingsForm.cs 整段搬出，一行未改。
internal sealed partial class SettingsForm : Form
{
    // ---------------- DEC-127④（DEC-128③ 横着拖、DEC-129⑤ 改成"定位条 + 松手归位"）：拖动调次序 ----------------
    // 抓手 = 整格（名字与格子空白处；复选自己吃点击，按住框子不会被当成拖）。
    // DEC-129⑤：**拖动期间阵列不动** —— 只画"被拖那一格的挑中描边 + 一条落点定位条"（品牌色竖条），
    // 松手才真搬。旧版每跨一格就 SetChildIndex，网格里一格动、旁边全跟着重排，跳动感就是他这条点名的。
    // 幽灵浮层**不做**（他给了"你评估"）：格子留原地 + 定位条已经能看清"从哪来、落到哪"，
    // 而透明跟随窗要多一层分层窗口管理（跟随/DPI/清理），收益小。
    const int StatChipLogicalH = 28;       // 格子高（横排的一格；窗高不再跟它走 —— DEC-128⑤ 定案窗高恒定）
    const int StatGapXLogical = 12;        // DEC-130②：改卡片之后横向间隔收窄（裸格子时代是 22）
    const int StatGapYLogical = 10;        // 折行后两行卡片之间的上下距
    const int StatCardPadLogical = 8;      // DEC-130②：卡片里左右的内衬（圆角才不啃到字）
    const int StatBlockPadYLogical = 12;   // DEC-131①：那块与上/下**分隔线**之间的间隔（原来从 y=0 起摆 = 贴着线）
    internal const int StatCardPadForTest = StatCardPadLogical;   // 自检按同一常量量内衬
    internal const int StatBlockPadYForTest = StatBlockPadYLogical;
    const int DragArmLogical = 4;          // 手抖 4 逻辑像素以内不算拖（免得想点格子结果把顺序搬了）

    void WireStatRowDrag(Panel chip, Label? lbl, params CheckItem?[] boxes)
    {
        Tip.Set(chip, "按住这一格拖动可以调整状态栏里的显示次序（横着拖，放不下自动折行）");
        if (lbl is not null) Tip.Set(lbl, "按住这一格拖动可以调整状态栏里的显示次序（横着拖，放不下自动折行）");
        // DEC-133（他："拖拽图标不用变,保持默认即可"）：拖动区不给特殊光标（默认箭头）—— 能拖的暗示归 tooltip 与定位条；
        // 复选那边由 CheckItem 按区给（框上=手形、别处=默认）

        void Down(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || !chip.Visible) return;
            _dragRow = chip;
            _dropInsertAt = -1;                                // 拖动期间只记落点，阵列不动（松手才归位）
            _dragStartScreenX = Control.MousePosition.X;       // 起点按**屏幕**坐标记：门槛与位移都不看控件内坐标
            _dragStartScreenY = Control.MousePosition.Y;
            chip.Capture = true;           // 之后的移动/抬起都归这一格（指针滑出它的范围也照样跟手）
            _statWrap?.Invalidate();
        }
        void Move(object? sender, MouseEventArgs e)
        {
            if (!ReferenceEquals(_dragRow, chip)) return;
            StepTowardCursor(chip);
        }
        void Up(object? sender, MouseEventArgs e)
        {
            if (!ReferenceEquals(_dragRow, chip)) return;
            var dragged = _dragRow;
            _dragRow = null;
            chip.Capture = false;
            if (_dropInsertAt >= 0)                            // 到过门槛才谈得上"搬"
            {
                var block = StatChipOrder();
                int from = block.IndexOf(dragged!);
                if (from >= 0)
                {
                    int final = DropTargetToIndex(from, _dropInsertAt, block.Count);
                    if (final != from) MoveRowToBlockIndex(block, from, final);
                }
            }
            _dropInsertAt = -1;
            // DEC-137（F-4）：这里以前把起点复位成 0 —— 一旦 MouseUp 没回到这一格（被模态窗抢走捕获、Alt-Tab、
            // 锁屏），_dragRow 还留着而起点是 (0,0)，门槛判据 |屏幕坐标 - 0| >= S(4) 从此**永远为真**：
            // 光是移动鼠标就会把定位条和被拖描边画出来，看着像它自己开始拖了。起点只在 Down 里赋值。
            _statWrap?.Invalidate();
        }
        // DEC-137（F-4）：捕获丢了就当场收手（没有这一条，上面那个残态就是可达路径）
        chip.MouseCaptureChanged += (_, _) =>
        {
            if (chip.Capture || !ReferenceEquals(_dragRow, chip)) return;
            _dragRow = null; _dropInsertAt = -1; _statWrap?.Invalidate();
        };
        chip.MouseDown += Down; if (lbl is not null) lbl.MouseDown += Down;
        chip.MouseMove += Move; if (lbl is not null) lbl.MouseMove += Move;
        chip.MouseUp += Up;     if (lbl is not null) lbl.MouseUp += Up;
        // DEC-131②（他点名）：拖动锚点 = **除方框之外的所有地方** —— 复选的字上/空白处也要能起拖；
        // 框上那一下留给勾选（`ClickTogglesAt` 同一判据），不进这里。接到哪一格、松手在哪，全走上面那三条家。
        foreach (var cb in boxes)
        {
            if (cb is null) continue;
            cb.DragForwardWired = true;      // 证人位：自检读它证明这枚真接上了（不是只写了个纯函数）
            Tip.Set(cb, "按住这一格拖动可以调整状态栏里的显示次序（横着拖，放不下自动折行）");
            cb.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left && !cb.ClickTogglesAt(e.Location)) Down(chip, e); };
            cb.MouseMove += (_, e) => Move(chip, e);
            cb.MouseUp += (_, e) => Up(chip, e);
        }
    }

    Panel? _dragRow;
    int _dragStartScreenX;
    int _dragStartScreenY;
    int _dropInsertAt = -1;      // 落点 = "光标数过去前面有几格"（0..块长；-1 = 没在拖/没过门槛）

    // 移动 = 只更新"落点"并让定位条重画（横排折行版：先看行带、再数这一行里光标前（过中线）有几格）。
    // "光标在这一格之后" = 光标更低（上一行整行都算后面）或同一行里更靠右。
    // 门槛按**横竖任一方向**过 4 逻辑像素（DEC-129⑤：横排之后主要位移是横向，旧版只看 Y 的那句在横拖里点不着）。
    void StepTowardCursor(Panel row)
    {
        int x = Control.MousePosition.X, y = Control.MousePosition.Y;
        if (Math.Max(Math.Abs(x - _dragStartScreenX), Math.Abs(y - _dragStartScreenY))
            < UiTheme.S(this, DragArmLogical)) return;      // 手抖不算拖
        var block = StatChipOrder();
        if (block.IndexOf(row) < 0) return;
        int insertAt = 0;
        foreach (var r in block)
        {
            var scr = r.RectangleToScreen(r.ClientRectangle);
            if (y > scr.Bottom) insertAt++;
            else if (y >= scr.Top && x > scr.Left + scr.Width / 2) insertAt++;
        }
        if (insertAt != _dropInsertAt) { _dropInsertAt = insertAt; _statWrap?.Invalidate(); }
    }

    /// <summary>落点（= 光标数过去前面有几格，0..count）换算成"这一格该搬到第几格"（纯函数，自检穷举）：
    /// 插在自己后面时要减一（自己先让位），两端都夹住。</summary>
    internal static int DropTargetToIndex(int from, int insertAt, int count)
        => Math.Clamp(insertAt > from ? insertAt - 1 : insertAt, 0, Math.Max(0, count - 1));

    // DEC-129⑤：拖动时在承载格上画两样 —— 被拖那格的"挑中"描边（阵列不动，总得看出在拖谁）+ 落点定位条
    //（插在哪一格的前/后；末尾插点站最后一格的右缘）。
    void PaintStatWrap(Graphics g)
    {
        if (_statWrap is null) return;
        // DEC-131①：那一块的分隔线**归承载格自己画**（上一行那条已停用）—— 老画法挂在"上一行的最后一像素"上，
        // 实测在"滚到底"这种态里会丢（r20 有 / r21 没，机制未钉死）；线随块画，任何滚动态都在。
        // 线在块顶、卡片从 padY 起摆 —— 中间那段就是他要的"间隔"。
        int padL = UiTheme.S(this, PadX);
        // DEC-138（F-6）：这条线只在"开着那块"时由这里画 —— 关掉时那块不产 Paint，线归上一行自己画
        //（`StatLineFromWrap` / `StatLineFromRow` 一对互斥判据，两边共用）。
        if (StatLineFromWrap(_statMaster.Checked))
        {
            using var sep = new Pen(UiTheme.Border, 1f);
            g.DrawLine(sep, padL, 0, _statWrap.Width - padL, 0);
        }
        if (_dragRow is null) return;
        // DEC-130②：卡片自带 Border 描边 —— 源格描边改用品牌色，不然两圈同色根本看不出在拖谁
        // DEC-137（F-2）：这圈以前贴着子控件边界画在父窗上 —— 父窗的更新区域被系统按子窗裁掉，
        // 于是每个像素都被裁没：拖动时只剩落点定位条，"从哪一格拖起来"没有落点（同文件那条分隔线画在
        // 自己的地面上，所以看得见，正好当对照）。改成画在子窗矩形之外（格间 12 / 行距 10 本来就够）。
        // DEC-140（他："淡一档,并且也应该做成卡片一样的圆角啊,现在这种方框太丑了"）：**圆角 + 淡一档**。
        // 三个数都不另立家（见 DragRingFor）：圆角跟着那一格卡片自己的圆角同心、撑出量还是 F-2 那 3 逻辑、
        // 取色用色表里既有的一档 BrandHover（比 Brand 淡一档）；落点那条定位条照旧用最亮的 Brand —— 落点要比"拖的是谁"抢眼。
        var (ringRadiusLogical, ringPadLogical, ringColor) = DragRingFor(_dragRow);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using (var pen = new Pen(ringColor, 1f))
        {
            var r = _dragRow.Bounds;
            r.Inflate(UiTheme.S(this, ringPadLogical), UiTheme.S(this, ringPadLogical));
            r.Width -= 1; r.Height -= 1;          // 1px 线落在像素上（同 StatDropWrap 那枚卡片的做法）
            using var ring = UiTheme.RoundRect(r, UiTheme.S(this, ringRadiusLogical));
            g.DrawPath(pen, ring);
        }
        if (DropIndicatorRect(_dropInsertAt) is { } bar)
        {
            using var brush = new SolidBrush(UiTheme.Brand);
            g.FillRectangle(brush, bar);
        }
    }

    Rectangle? DropIndicatorRect(int insertAt)
    {
        if (insertAt < 0 || _statWrap is null) return null;
        var block = StatChipOrder();
        if (block.Count == 0) return null;
        int gapX = UiTheme.S(this, StatGapXLogical);
        int at = Math.Clamp(insertAt, 0, block.Count);
        var refR = at < block.Count ? block[at].Bounds : block[^1].Bounds;
        int bx = at < block.Count ? refR.Left - gapX / 2 : refR.Right + gapX / 2;
        return new Rectangle(bx - UiTheme.S(this, 1), refR.Top, UiTheme.S(this, 3), refR.Height);
    }
    internal Rectangle? DropIndicatorRectForTest(int insertAt) => DropIndicatorRect(insertAt);

    // DEC-140（他："淡一档,并且也应该做成卡片一样的圆角啊,现在这种方框太丑了"）：拖动时"被拖那一格"外面
    // 那一圈的三个数 = 一个家。**圆角不另立常量**：跟着那一格卡片自己的 `CardRadiusLogical` 一起加撑出量
    //（同心才像卡片长出来的一圈；写死一个数，退档成"裸格子"那一档时就会浮出一张方纸）。
    // 取色只用色表里既有的那一档 `BrandHover`（比 `Brand` 淡一档），**不长新颜色** —— 口径同 DEC-127③ 那两态取色。
    internal const int DragRingPadLogical = 3;
    internal static (int RadiusLogical, int PadLogical, Color Color) DragRingFor(Panel? dragged)
        => dragged is StatDropWrap w && w.DrawCard
            ? (w.CardRadiusLogical + DragRingPadLogical, DragRingPadLogical, UiTheme.BrandHover)
            : (0, DragRingPadLogical, UiTheme.BrandHover);
    // 自检接缝：按 id 找到那一格，走**同一个** DragRingFor（绘制与断言一个家，别处不许再拼一套算式）。
    internal (int RadiusLogical, int PadLogical, Color Color) StatRingForTest(string id)
        => DragRingFor(StatChipOrder().FirstOrDefault(c => _statRowIds[c] == id));
    // 出图台接缝：把"正在拖某一格"那一态画出来（这一态只有真按住才存在，观感要看就得能造它）。
    // 传空 id = 收回去（等于松手），别当真假手势用 —— 真手势的门槛、落点、松手归位仍归他鼠标判。
    internal void ShowDragRingForTest(string id, int insertAt)
    {
        _dragRow = StatChipOrder().FirstOrDefault(c => _statRowIds[c] == id) as Panel;
        _dropInsertAt = _dragRow is null ? -1 : insertAt;
        _statWrap?.Invalidate();
    }

    /// <summary>把块里第 from 格搬到第 target 格。这一块是承载格里**连续**的一段，所以块内索引差可以直接加到绝对下标上，
    /// 再夹到本块首尾 —— 绝不把不相干的格换进来。真搬动了才写回 csv 并返回 true。
    /// 拖动与自检走的都是这一个家（`MoveStatRowForTest` 只是把"目标格"直接给它，跳过屏幕坐标那一步）。</summary>
    bool MoveRowToBlockIndex(List<Panel> block, int from, int target)
    {
        if (target == from || block.Count == 0 || _statWrap is null) return false;
        var row = block[from];
        int abs = _statWrap.Controls.GetChildIndex(row);
        int first = block.Min(_statWrap.Controls.GetChildIndex);
        int last = block.Max(_statWrap.Controls.GetChildIndex);
        _statWrap.Controls.SetChildIndex(row, Math.Clamp(abs + (target - from), first, last));
        _statWrap.PerformLayout();
        LayoutStatChips();             // 次序变了 = 重新摆一遍（横排折行按新次序算）
        WriteBackInVisualOrder();      // 边拖边记：点「保存」之前 csv 的次序就已经是拖出来的那一串
        return true;
    }

    /// <summary>DEC-127④ + DEC-128②：三键一律按**界面上此刻的格序**拼（次序的家从「表序」改到这里）。
    /// `Order` = 九行全量视觉次序（拖出来的先后，勾没勾都记）；`Items` = 「实时 / 唯一那一枚」的勾选集合
    /// （语义没动）；`Avg` = 平均集合（与实时**各自独立**，不再要求先勾实时）。</summary>
    void WriteBackInVisualOrder()
    {
        var order = StatRowVisualOrder();
        _s.StatusBarOrder = string.Join(",", order);
        _s.StatusBarItems = string.Join(",", order.Where(id => _statShow[id].Checked));
        _s.StatusBarAvg = string.Join(",", order.Where(id => MainForm.IsStatAvgable(id)
                                                             && _statAvg.TryGetValue(id, out var a) && a.Checked));
        PushStatusBarPreview();        // DEC-128①：边调边渲染到主窗状态栏（存不存归「保存 / 取消」）
    }

    /// <summary>这一块在承载格里的视觉次序（横排折行 = Controls 集合的次序就是看到的次序）。</summary>
    List<Panel> StatChipOrder() => _statWrap is null ? new() : _statWrap.Controls.Cast<Control>()
        .OfType<Panel>().Where(p => _statRowIds.ContainsKey(p)).Select(p => p).ToList();

    /// <summary>视觉次序对应的 id 串（三个 csv 都按这一串拼）。</summary>
    internal List<string> StatRowVisualOrder() => StatChipOrder().Select(c => _statRowIds[c]).ToList();

    FlowLayoutPanel? _statPage;
    Panel? _statWrap;
    internal IReadOnlyList<string> StatRowVisualOrderForTest => StatRowVisualOrder();
    internal string StatusBarItemsPendingForTest => _s.StatusBarItems;
    internal string StatusBarOrderPendingForTest => _s.StatusBarOrder;
    internal string StatusBarAvgPendingForTest => _s.StatusBarAvg;

    // DEC-129③④：某一格的"面子"（自检读）—— 有没有独立名字标签、主勾选的 caption/字号/颜色、平均那枚的字号/颜色
    internal (bool HasLabel, string Caption, float CaptionPt, Color CaptionColor,
              float AvgPt, Color AvgColor) StatChipFaceForTest(string id)
    {
        var chip = StatChipOrder().FirstOrDefault(c => _statRowIds[c] == id);
        bool hasLbl = chip?.Controls.OfType<Label>().Any() ?? false;
        if (!_statShow.TryGetValue(id, out var cb)) return (hasLbl, "", 0f, Color.Empty, 0f, Color.Empty);
        float avgPt = 0f;
        var avgColor = Color.Empty;
        if (_statAvg.TryGetValue(id, out var ca)) { avgPt = ca.Font.SizeInPoints; avgColor = ca.CaptionColor; }
        return (hasLbl, cb.Caption, cb.Font.SizeInPoints, cb.CaptionColor, avgPt, avgColor);
    }

    // DEC-129③：某一格在承载格里的左缘（边距那条断言用）
    internal int StatChipLeftForTest(string id)
        => StatChipOrder().FirstOrDefault(c => _statRowIds[c] == id)?.Left ?? -1;

    // DEC-131①：某一格的顶缘（"与分隔线留间隔"那条断言用 —— 第一格顶应 = 上内衬）
    internal int StatChipTopForTest(string id)
        => StatChipOrder().FirstOrDefault(c => _statRowIds[c] == id)?.Top ?? -1;

    // DEC-131②：某一枚"实时/唯一"/「平均」的复选本体（"勾选只认方框"两条断言要用真控件）
    internal CheckItem? StatBoxForTest(string id)
        => _statShow.TryGetValue(id, out var cb) ? cb : null;
    internal CheckItem? StatAvgBoxForTest(string id)
        => _statAvg.TryGetValue(id, out var cb) ? cb : null;

    // DEC-130②：某一格的"卡片面子"（自检读）—— 卡片三件（底/描边/圆角）+ 内衬两条 + 里面垫的底色
    internal (bool DrawCard, Color Fill, Color Line, int Radius, int PadIn,
              int FirstControlLeft, int RightGap,
              Color LabelBackdrop, Color BoxBackdrop, Color AvgBackdrop) StatChipCardForTest(string id)
    {
        var chip = StatChipOrder().FirstOrDefault(c => _statRowIds[c] == id) as StatDropWrap;
        if (chip is null) return (false, Color.Empty, Color.Empty, 0, 0, 0, 0, Color.Empty, Color.Empty, Color.Empty);
        var lbl = chip.Controls.OfType<Label>().FirstOrDefault();
        _statShow.TryGetValue(id, out var cb);
        _statAvg.TryGetValue(id, out var ca);
        var ctrls = chip.Controls.Cast<Control>().ToList();
        int leftMost = ctrls.Count == 0 ? -1 : ctrls.Min(c => c.Left);
        int rightMost = ctrls.Count == 0 ? 0 : ctrls.Max(c => c.Right);
        return (chip.DrawCard, chip.CardFill, chip.CardLine, chip.CardRadiusLogical,
                UiTheme.S(this, StatCardPadLogical), leftMost, chip.Width - rightMost,
                lbl?.BackColor ?? Color.Empty, cb?.Backdrop ?? Color.Empty, ca?.Backdrop ?? Color.Empty);
    }

    // DEC-129①：那一页挂着的自绘滑条（自检读范围与"重同步线"的证人位；真路径的修复在 UiScrollbar 的 Layout 挂钩上）
    internal UiScrollbar? StatBarForTest => BarForPage(_statPage);
    UiScrollbar? BarForPage(FlowLayoutPanel? page)
    {
        if (page is null || _bars is null) return null;
        for (int i = 0; i < _pages.Length && i < _bars.Length; i++)
            if (ReferenceEquals(_pages[i], page)) return _bars[i];
        return null;
    }
    internal int StatPageScrollMaxForTest => _statPage?.VerticalScroll.Maximum ?? 0;
    // DEC-130①：把这一页滚到最底（出图与自检都走这一条，别各拼一次）
    internal void StatPageScrollToMaxForTest()
    {
        if (_statPage is not null) _statPage.AutoScrollPosition = new Point(0, int.MaxValue);
    }

    /// <summary>出图用：状态栏那一排在**窗体客户区**里的合矩（九行合起来，含行高）。
    /// 只给矩形不画别的东西 —— 拍的是真行、真复选，两态同框。</summary>
    internal Rectangle StatRowsBoundsForTest()
    {
        if (_statPage is null || _statRows.Count == 0) return Rectangle.Empty;
        var u = Rectangle.Empty;
        foreach (var r in _statRows)
        {
            var scr = r.RectangleToScreen(r.ClientRectangle);
            u = u.IsEmpty ? scr : Rectangle.Union(u, scr);
        }
        return RectangleToClient(u);
    }

    /// <summary>出图用：把那一排的复选都推到 hover 态（真机由鼠标进出触发，这里只是替鼠标点一下名；
    /// 拍完必须归 false，别把出图台的那一枚留在悬停色上 —— 它是临时件，不影响真机）。</summary>
    internal void SetStatRowHoverForTest(bool on)
    {
        foreach (var cb in _statShow.Values) cb.HoverForTest(on);
        foreach (var cb in _statAvg.Values) cb.HoverForTest(on);
    }

    /// <summary>出图用：把这一块滚到看得见（窗高恒定之后它就坐在通用页末尾，拍图前要把它带进视野）。</summary>
    internal void ScrollStatRowsIntoViewForTest()
    {
        var page = _statPage;
        if (page is null || _statWrap is null || page.Height <= 0) return;
        page.ScrollControlIntoView(_statWrap);
        page.PerformLayout();
    }

    /// <summary>出图/自检用：那一页此刻的内容总高（窗高恒定 = 由它承担"长缩"，判据看它变没变）。</summary>
    internal int StatPageContentHeightForTest() => _statPage?.DisplayRectangle.Height ?? -1;

    /// <summary>自检用：那一块此刻占的内容高（横排折行算出来的；开关开/关它一正一零）。</summary>
    /// <summary>自检出口：那一块此刻的真实高度（DEC-137 F-3 的判据就打在它上面）。
    /// 关掉时必须归零 —— 只藏不归零滚动区量不出差别；老那条断言读的是自己填进去的意图清单 = 按构造必真。
    /// 不读 Visible（无头恒 false，BUG-043 那一族）。</summary>
    internal int StatBlockHeightForTest => _statWrap?.Height ?? -1;
    /// <summary>DEC-137（F-3）：`SyncRows` 那一拍真正写进去的块高（0 = 关掉时确实连高度一起收）。</summary>
    internal int StatBlockHeightAppliedForTest = -1;

    /// <summary>自检用：把某一格按**拖动同一条算式**挪到块里的第 targetIndex 格
    /// （无头时 RectangleToScreen 给不出可信的屏幕坐标，所以目标格直接给，剩下的一步一模一样）。</summary>
    internal void MoveStatRowForTest(string id, int targetIndex)
    {
        var block = StatChipOrder();
        int from = block.FindIndex(r => _statRowIds[r] == id);
        if (from < 0) return;
        MoveRowToBlockIndex(block, from, Math.Clamp(targetIndex, 0, block.Count - 1));
    }

    /// <summary>自检用：替用户勾 / 取消某一格的「实时 / 唯一那一枚」（触发同一条 CheckedChanged 链）。</summary>
    internal void SetStatCheckedForTest(string id, bool on)
    {
        if (_statShow.TryGetValue(id, out var cb)) cb.Checked = on;
    }

    /// <summary>自检用：替用户勾 / 取消某一格的「平均」。</summary>
    internal void SetStatAvgCheckedForTest(string id, bool on)
    {
        if (_statAvg.TryGetValue(id, out var cb)) cb.Checked = on;
    }

    // DEC-128①：预览的兜底收口 —— FormClosed 之外，Dispose（含 using / 异常路径）也清一次（幂等）。
    // 自检里那个 never-shown 的实例走的就是 Dispose 这条；真机上两条都过一遍也无妨。
    protected override void Dispose(bool disposing)
    {
        if (disposing) _main.ClearStatusBarPreview();
        base.Dispose(disposing);
    }

    // 基准窗高（构造函数用它；DEC-128⑤ 之后这是**恒定**窗高 —— 那排显隐只改页面内容长度，滚动条自己跟）
    const int ClientLogicalH = 640;
    internal int ClientLogicalHForTest => ClientLogicalH;
    internal int StatChipLogicalHForTest => StatChipLogicalH;
    readonly Dictionary<Control, string> _statRowIds = new();
    readonly List<Panel> _statRows = new();
    // 无头时 Control.Visible 是"含祖先"的有效可见性、恒 false（BUG-043 那一族）—— 自检读这份**意图**清单
    readonly List<bool> _statRowIntent = new();
    ToggleSwitch _statMaster = new();
    readonly Dictionary<string, CheckItem> _statShow = new();
    readonly Dictionary<string, CheckItem> _statAvg = new();
    internal (int Rows, int AvgBoxes, int ShowBoxes) StatusBarRowsForTest
        => (_statRows.Count, _statAvg.Count, _statShow.Count);
        internal IReadOnlyList<bool> StatusBarRowIntentForTest => _statRowIntent;
    internal ToggleSwitch StatusBarMasterForTest => _statMaster;

    FlowLayoutPanel PageStrata()
    {
        var page = NewPage();
        GroupTitle(page, "路径");
        ValueRow(page, "Strata 目录", "含 .venv / serve / strata-*.json", _s.Root, v => _s.Root = v, pb: PathBtn.PickFolder);
        ValueRow(page, "模型配置目录", "引擎实际读取的配置目录（每次启动前重建；Strata 目录本身只读）："
            + MainForm.DisplayDataPath(MainForm.RealRunConfigDir()),
            MainForm.EffectiveConfigDir(), _ => { }, pb: PathBtn.OpenFolder);
        GroupTitle(page, "运行");
        ValueRow(page, "内部端口", "留空 = 公开端口 + 1（不能等于公开端口）", _s.InternalPort, v => _s.InternalPort = v);
        ValueRow(page, "空闲停服(分钟)", "这么久没有请求就停服释放显存；留空 = 不停", _s.IdleStopMinutes, v => _s.IdleStopMinutes = v);
        GroupTitle(page, "维护");
        // DEC-110④：三节的「维护」组同一个形状，从上往下 = **更新源 → 版本 → 回退 → 各自模块独有**。
        // Strata 这一格以前没有（仓库地址写死在代码里），按用户"补一行"补上；填坏回落默认，见 StrataRepoApiOf。
        ValueRow(page, "更新源", "GitHub 仓库地址。", _s.StrataUpdateUrl, v => _s.StrataUpdateUrl = v);
        // DEC-058：检查、升级、进度、取消全进了「更新」窗（一模块一窗），设置页这里只留本机版本 + 一枚入口。
        // 原来那行说明文字（"只查 GitHub…会在下面长出…"）按用户要求删掉——窗里每一态都有字，不用再解释一遍。
        var (vSrc0, vEng0) = _main.VersionsForSettings();
        _lblVerLocal = AboutRow(page, "版本", $"源码 {vSrc0} · 引擎 {vEng0}",
            openText: "更新…", onOpen: () => _main.OpenUpdateWindow("strata", this));
        _btnRollback = ButtonRow(page, "回退到备份", "源码 + 引擎的整份快照，每次升级前自动存一份。",
            "回退…", () => _main.Rollback());
        // DEC-055：Strata 自带的那份 ggml/llama（原来挂在关于页）归到 Strata 节。
        // DEC-056：「查 b 号」删了——本机只有 CMakeLists 钉的 commit，那就只显示 commit。
        AboutRow(page, "内建 llama.cpp", _main.PinnedLlamaText());
        ButtonRow(page, "配置新模型", "用官方 SETUP 装/配新模型。", "运行 SETUP", () => _main.RunSetup());
        return page;
    }

    FlowLayoutPanel PageLlama()
    {
        var page = NewPage();
        GroupTitle(page, "路径");
        _boxLlamaDir = ValueRow(page, "llama.cpp 目录", "含 llama-server.exe；更新后自动切到新版本目录", _s.LlamaDir, v => _s.LlamaDir = v, pb: PathBtn.PickFolder);
        // DEC-079（取消融合层）：这一份 ini 就是引擎实读、也是界面就地写的那一份，**路径固定在程序目录、不再可配**，
        // 所以这行从「可改的路径 + 选文件」改成「显示位置 + 一枚「打开」直达」（关于页那三行的口径，DEC-055）
        AboutRow(page, "模型配置", MainForm.RouterIniPath(), openText: "打开", onOpen: () => _main.RevealRouterIni());
        // 用户 2026-10-04 第 1 条：这行解释删掉——界面上改的就是引擎读的那一份，位置也已经固定，没什么要交代的。
        ValueRow(page, "模型目录", "可选。留空 = 只用 ini", _s.LlamaModelsDir, v => _s.LlamaModelsDir = v, pb: PathBtn.PickFolder);
        GroupTitle(page, "运行");
        // 「内部端口」已挪到主界面 llama.cpp 页卡1（命名「监听端口」，2026-10-02 反馈）——设置节里不再重复
        ValueRow(page, "同时加载上限", "--models-max", _s.LlamaModelsMax, v => _s.LlamaModelsMax = v);
        // DEC-151/152：单位统一为分钟、标签统一为「空闲停服(分钟)」（与另两页一字不差）；传参时 ×60 折成 --sleep-idle-seconds
        ValueRow(page, "空闲停服(分钟)", "留空 = 不自动卸载", _s.LlamaSleepIdleMinutes, v => _s.LlamaSleepIdleMinutes = v);
        GroupTitle(page, "维护");
        // DEC-110④：原来那独立的「更新」组并进「维护」，行序与另两节一致 = 更新源 → 版本 → 回退 → 各自独有
        //（"更新源"与"版本"以前分在两个组里，切到这一页要看两处）。
        ValueRow(page, "更新源", "GitHub releases 地址。下哪只包按当前目录名里的 CUDA 版本自动匹配。", _s.LlamaUpdateUrl, v => _s.LlamaUpdateUrl = v);
        // DEC-058：与 Strata 节同一形状——本机版本 + 一枚「更新…」开它自己的窗（进度与结果都在窗里）
        _lblLlamaVer = AboutRow(page, "版本", _main.LlamaVersionForSettings(),
            openText: "更新…", onOpen: () => _main.OpenUpdateWindow("llama", this));
        // DEC-079：就地写家底就要给"改崩了退得回去"留一枚入口（与 Strata 节「回退到备份」同形状）
        ButtonRow(page, "改动前自动备份", "每次写配置前自动存一份备份。",
            "恢复…", () => _main.RestoreRouterIniFromBackup());
        return page;
    }

    FlowLayoutPanel PageAbout()
    {
        var page = NewPage();
        GroupTitle(page, "本程序");
        AboutRow(page, "版本", MainForm.VersionTag());
        AboutRow(page, "作者", "iCeGiTa");
        // DEC-202：行序 = 版本 / 作者 / 更新源 / 更新 / 帮助。
        // 「更新源」与另三节「维护」组那一格同形状（可改；留空或认不出 = 回落默认，填坏不拦保存）；
        // 「更新」= 只读状态值 + 一枚「更新…」开第 4 扇窗（与三个模块节那三行同一套字）。
        _boxAppUpdateUrl = ValueRow(page, "更新源", "GitHub 仓库地址。留空 = 用默认。", _s.AppUpdateUrl, v => _s.AppUpdateUrl = v);
        var (appUpdText, appUpdColor) = _main.AppUpdateRowForSettings();
        _lblAppUpdate = AboutRow(page, "更新", appUpdText, appUpdColor,
            openText: "更新…", onOpen: () => _main.OpenAppUpdateWindow(this));
        // 说明书 = 内嵌在 exe 里的那份（csproj 的 EmbeddedResource / LogicalName=manual.md）。
        // 行名用「帮助」而不是「使用说明」：它跟「版本 / 作者」一样描述**本程序自己**，不是某个位置。
        // 按钮仍叫「打开」，与另三行同一套字 —— 一眼看得出"这一类行都是去某个地方"。
        AboutRow(page, "帮助", "使用说明书",
            openText: "打开", onOpen: () => Dialogs.ShowManual(this));
        // 2026-10-22（DEC-209②，他点名）：这一节原来的「位置」组（数据目录 / 日志文件）**整组搬到「通用 → 位置」** ——
        // "本程序落在哪儿"是全局的事，不该一半在通用、一半在关于；两处各说一半正是他说的"分类不对"。
        // 关于页从此只描述**本程序自己**：版本 / 作者 / 更新源 / 更新 / 帮助。
        // 「内嵌 llama.cpp」原来挂在这里——那是 **Strata 自带**的那份 ggml/llama，已挪到 Strata 节（DEC-055）
        return page;
    }

    // 自检用：各节的名称与每页里各行的顺序（按视觉位置导出，只建控件树不开窗口）。
    // 2026-10-02 反馈修订：节 = 通用 / 每个启用模块 / 关于。
    internal List<string> DumpLayoutRows()
    {
        var names = _segTabs.ItemsForTest;
        var rows = new List<string> { "settings-tabs: " + string.Join(" | ", names) };
        for (int i = 0; i < _pages.Length; i++)
            rows.Add("settings-" + names[i] + ": " + MainForm.DumpChildren(_pages[i]));
        return rows;
    }

    // 自检用：逐行导出「标题 vs 右侧控件」的垂直中心差（px，0 = 同一水平线）。
    // 存在的意义：这一版之前标题按固定 Top 摆，150% DPI 下比输入框中心高约 10px（用户反馈"太靠上"）。
    internal List<string> DumpRowAlignment()
        => _alignPairs.Select(p => p.what + ": dy=" +
            (TextCenterY(p.a) - TextCenterY(p.b)).ToString("F1", System.Globalization.CultureInfo.InvariantCulture)
            + $" (h {p.a.Height}/{p.b.Height})").ToList();

    // 自检用：按钮行（「改动前自动备份」这类）说明文字的**行数**与**顶边减按钮底边**的差（px，负数 = 压到按钮上）。
    // 用户 2026-10-04 第 4 条要求那句「每次要写 llama_models.ini 之前先存一份到 data\llama\，只留最近 5 份。」
    // 整行显示不折行 —— 光看截图不算核过，量出来行数 = 1 且不与按钮重叠才算。
    internal List<(string Title, int Lines, int GapBelowButton, int RowWidth, int TextWidth, int DescMaxWidth, int ClientWidth)> ButtonRowDescForTest()
    {
        var list = new List<(string, int, int, int, int, int, int)>();
        foreach (var page in _pages)
            foreach (Control row in page.Controls)
            {
                var btn = row.Controls.OfType<FlatButton>().FirstOrDefault();
                var d = row.Controls.OfType<Label>().FirstOrDefault(l =>
                    l.Tag is string t && t.StartsWith("btn-desc:", StringComparison.Ordinal));
                if (btn is null || d is null) continue;
                int lineH = Math.Max(1, d.Font.Height);
                string what = ((string)d.Tag!).Substring("btn-desc:".Length);
                int lines = Math.Max(1, (int)Math.Round(d.Height / (double)lineH));
                // 窗没显示时 FlowLayoutPanel 给到的行宽只有百来 px（自检里量不到真实页宽），
                // 所以"是不是整行"用两个可移植的数判：DescMaxWidth 有没有铺满整行、
                // 以及单行字宽 TextWidth 放进"页宽扣掉滚动条与内边距"里放得住。
                list.Add((what, lines, d.Top - btn.Bottom, row.Width,
                          TextRenderer.MeasureText(d.Text, d.Font, Size.Empty,
                              TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width,
                          d.MaximumSize.Width, ClientSize.Width));
            }
        return list;
    }

    // 自检用：各 Tab 页的自绘滑条（看滚轮路由有没有挂上）
    internal UiScrollbar[] BarsForTest => _bars;

    /// <summary>出图 / 自检用：一共几节（「关于」= 最后一节）。DEC-202 的出图要按名字找不到 Tab，
    /// 用它算末节索引 —— 加模块后节数会变，写死 4 就漂了。</summary>
    internal int TabCountForTest => _pages.Length;

    /// <summary>DEC-202 自检用：关于页「更新源」那一格是不是可编辑（`ValueRow` 的输入框，ReadOnly=false）。</summary>
    internal bool AppUpdateUrlEditableForTest => _boxAppUpdateUrl is { } b && !b.Inner.ReadOnly;

    // 自检用：页签胶囊的净高（2026-10-02 反馈：应与按钮同高，见 SegmentedControl.ApplyDpiHeight）
    internal int TabsPillHeightForTest => _segTabs.PillHeightForTest;

    // 自检用：底部保存栏的底色（用户 2026-10-03：不要用单独的背景色）
    internal System.Drawing.Color FooterColorForTest => _footer?.BackColor ?? System.Drawing.Color.Empty;
}
