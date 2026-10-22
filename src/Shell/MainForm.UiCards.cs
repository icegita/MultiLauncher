namespace MultiLauncher;

// 三张卡片与「高级」面板的装配（主界面装配见 MainForm.Ui.cs）。
// 卡片宽度由父容器停靠时决定，所以卡片内一律在 Layout 事件里按实际宽度重排，不在构建时算死。
public partial class MainForm
{
    int LabelW(string text, Font? f = null) => TextRenderer.MeasureText(text, f ?? _fSmall).Width;
    int BtnW85(string text) => TextRenderer.MeasureText(text, _fSmall).Width + UiTheme.S(this, 18);

    // 「高级」展开时用来重算卡片高度（ToggleAdvanced 用）
    Action? HeightChanged;

    // 高级区的一项（Label 首次排版时创建）
    // shown = 这一格在当前后端出不出面（DEC-096：Ninfer 那一套里 WSL/WIN 的格子不同；null = 恒出面）。
    // 用判据而不是 Visible：窗没显示时控件的 Visible 是"含祖先"的有效可见性，整棵树都报 false（DEC-017 那个坑）。
    sealed class AdvItem(string label, Control ctl, string tip, bool wide, Func<bool>? shown = null)
    {
        public string Label = label;
        public Control Ctl = ctl;
        public string Tip = tip;
        public bool Wide = wide;
        public Func<bool>? Shown = shown;
        public Label? Lb;
        public bool ShownNow => Shown is null || Shown();
    }

    readonly List<(string Label, ToggleSwitch Sw, string Tip, Func<bool>? Shown)>
        _advStrataToggles = new(), _advLlamaToggles = new(), _advNinferToggles = new();

    // 高级区每枚格子「它的名字此刻该不该在明面上」= AdvLayout 两个分支各写一次的证人位。
    // 自检为什么不直接读 Label.Visible：无头跑时控件的 Visible 是"含祖先"的有效可见性，整棵树一律报 false
    //（本文件头 DEC-017 那个坑），拿它当判据就是一条永真的空判据 —— DEC-098③ 这条自检就是这么假绿过一次，
    // 直到把它按回去重跑才发现。证人位写在"恢复标签 Visible"同一处，删掉那一行 = 这里也不会有 true。
    readonly Dictionary<Control, bool> _advLabelShown = new();

    // 换页时重建卡1 的项列表（端口格 / 思考深度格都随激活模块换）
    Action? Card1Relayout;
    // 换页时同步卡1 那一行：后端方块在不在场、行容器左边的缝要不要留（DEC-182 甲）
    Action? Card1RowSync;

    // 模型下拉的提示（用户 2026-10-04："废话太多,精简为"）——文案进常量，自检盯着别又长回去
    internal const string ModelDropTip = "只列当前激活模块的模型；服务启停时重新扫描；每项尾部是估算占用";

    // ---------------- 卡片1：监听地址｜监听端口｜思考深度｜保存（随激活模块） ----------------
    // 2026-10-04 DEC-076：按旧图口径「用思考深度替换预设，保存按钮还是放在那里，删除按钮删掉即可」——
    // 卡1 回归（DEC-074 曾整张撤掉）。四个槽位一行，第三格随页换：Strata = 思考深度下拉（_cboReasoning）、
    // llama = 思考深度下拉（_cboReasoningEffort，取值表见 Program.cs）。
    // 2026-10-04 DEC-078（用户：「监听地址与思考深度输入框宽度交换」「监听端口宽度增加10%」）：
    // **吃余量的是监听地址**，思考深度拿原来地址格那档固定宽（长 IP 优先保证不截断）；端口 +10%。
    Control CardStart()
    {
        var card = new RoundedPanel
        {
            Dock = DockStyle.Top,
            Height = CardPad * 2 + RowH,
            FillColor = UiTheme.Card,
            BorderColor = UiTheme.Border,
            Backdrop = UiTheme.Window,
            Margin = new Padding(0, 0, 0, RowGap),
        };

        _txtHost.Backdrop = UiTheme.Card;
        _txtHost.Inner.Font = _fMono;
        _txtPort.Backdrop = UiTheme.Card;
        _txtPort.Inner.Font = _fMono;
        _txtPortLlama.Backdrop = UiTheme.Card;
        _txtPortLlama.Inner.Font = _fMono;
        _cboReasoning.Backdrop = UiTheme.Card;
        _cboReasoningEffort.Backdrop = UiTheme.Card;

        _btnSave.Backdrop = UiTheme.Card;
        _btnSave.FillColor = UiTheme.Input;
        _btnSave.HoverColor = UiTheme.Border;
        _btnSave.TextColor = UiTheme.Text;
        _btnSave.Font = _fSmall;
        _btnSave.Height = RowH;
        _btnSave.Width = BtnW85("保存");
        _btnSave.Enabled = false;    // 启动时没有未保存的修改 → 灰（DEC-074 口径不变）
        Tip.Set(_btnSave, "把界面上当前的参数修改保存下来（Strata 侧记进该模型自己的那份）；切页 / 关窗 / 去设置页保存时本来也会自动保存一次，这枚按钮让保存变成看得见的动作。没有未保存的修改时它是灰的");
        Tip.Set(_txtHost, "127.0.0.1 = 仅本机；0.0.0.0 = 局域网可访问（此时 API 地址栏显示本机局域网 IP）");
        Tip.Set(_txtPort, "服务公开端口（1-65535）：路由模式下它是网关的监听端口；Strata 服务实际跑在设置页的内部端口上");
        Tip.Set(_txtPortLlama, "llama-server 的实际监听端口（1-65535），手填。留空 = 公开端口 + 2；与公开/Strata 内部端口撞车、或该端口还被网关/旧服务占着时会自动避让，日志里会写来源");
        Tip.Set(_cboReasoning, "共享默认思考档（聊天页 Thinking 同款）：none / low / medium / high；留空 = 不写（聊天页里设的照旧）。填了 = 每次启动服务时按这里写入 " + DisplayDataPath(RunProductDir()) + "\\<tag>.shared-settings.json");
        Tip.Set(_cboReasoningEffort, "选中模型那一段的 reasoning-effort（官方取值：default / minimal / low / medium / high / xhigh / max）；留空 = 从这一段删掉这个键 = 回落到 [*]/文件头（DEC-079 起没有合并 ini 这一层）");

        // 宽度按运行时实测给，别写死（125%/150% 下 CJK 字宽非整倍增长）
        int BoxW(string sample, Font f) => LabelW(sample, f) + UiTheme.S(this, 24);
        int Need(string text, Font f) => TextRenderer.MeasureText(text, f, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width;
        var metrics = new ModuleView.Card1Metrics(s => BoxW(s, _fMono), Need, l => UiTheme.S(this, l));

        // 卡1 上所有会随模块换的格子（方块 + 两只端口格 + 三只思考深度下拉 …）：摆位前一律先挪出画外，
        // 再按 Items() 把激活模块那几枚摆回来。第 0 轮 DEC-094：这份名单从模块表取，加模块不用再回来补一行。
        var boxes = new List<Control> { _txtHost };
        boxes.AddRange(_views.Select(v => v.PortBox()));
        boxes.AddRange(_views.Select(v => (Control)v.ReasonBox()));
        // DEC-182（甲）：后端方块**不再是卡1 的孩子** —— 它搬去行容器 _card1Row 里独立成一卡，
        // 与卡1 左右相邻、中间留 RowGap。原来贴在卡1 里侧左沿会让它自己那圈描边的左/上/下三条
        // 与卡1 的边框重合（他 2026-10-16 圈红框点名的"两个卡片重叠"，DEC-089 那版嵌套摆位的后果）。
        boxes.Add(_btnSave);
        foreach (var c in boxes)
        {
            // DEC-182：这里原来只设 `BackColor`（对 UserPaint 自绘控件无效）→ ninfer 那两只格子
            // （监听端口 / 思考深度）圆角外一直垫着一圈窗口色的方角。改成按父层现值统一补 Backdrop。
            SetBoxBackdrop(c, UiTheme.Card);
            c.BackColor = UiTheme.Card;
            card.Controls.Add(c);
        }
        var labels = new List<Label>();     // 标签池：每项一个槽（保存那项没有标签，槽留空）
        for (int i = 0; i < 4; i++)
        {
            var lb = Lbl("", _fSmall, UiTheme.TextMuted, UiTheme.Card);
            card.Controls.Add(lb);
            labels.Add(lb);
        }

        // (标签, 控件, 固定宽, 是否吃余量)；随激活模块变化：端口格与思考深度格都各显各的。
        // DEC-078：吃余量的是**监听地址**（长 IP 优先不截断），思考深度用固定宽 wReason。
        // DEC-096：这三格的**宽度算式**由模块那一行给（Ninfer 那档 = 端口 59 / 思考深度 89 / 地址下限 108 逻辑，
        // 是 DEC-088/089 拍的那一档（DEC-096 起整窗从 900 放宽到 940，这一档原样不动）；另两页沿用 DEC-078 那三行原式，一个字没变）。
        List<(string Label, Control Ctl, int FixedW, bool Grow)> Items()
        {
            var (wPort, wReason, wHostFloor) = ActiveView.Card1Widths(metrics);
            return new()
            {
                ("监听地址", _txtHost, 0, true),
                ("监听端口", ActiveView.PortBox(), wPort, false),
                ("思考深度", ActiveView.ReasonBox(), wReason, false),
                ("", _btnSave, _btnSave.Width, false),
            };
        }

        void Layout()
        {
            var items = Items();
            foreach (var c in boxes) { c.Visible = false; c.SetBounds(-2000, -2000, c.Width, c.Height); }
            int x = CardPad;
            int y = CardPad;
            // 卡1 左侧那枚后端方块：DEC-182（甲）起它是**行容器里的兄弟卡**，不再占本卡的摆位账
            //（DEC-089 那版"贴在卡1 里侧左沿"会让两圈描边叠在一起，他圈红框点名）。
            // 本卡的内容起点因此回到 CardPad —— 让出来的那 16 逻辑由行容器那一列吃掉，
            // 整窗不加宽靠 ninfer 那档「思考深度」扣 16 逻辑守住（DEC-088 第 1 条）。
            int fixedSum = 0;
            foreach (var it in items)
            {
                fixedSum += it.FixedW;
                if (it.Label.Length > 0) fixedSum += LabelW(it.Label) + UiTheme.S(this, 8);
            }
            // 余量给监听地址（它是本行的 Grow 项），但**下限取"长 IP 放得下"那档**（各模块那一行给）：
            // 窗口缩小时宁可让卡片报裁切、要求更宽的窗，也不能把地址截断（用户口径：优先保证文本不被截断）
            var (_, _, hostFloorLogical) = ActiveView.Card1Widths(metrics);
            int free = Math.Max(hostFloorLogical, card.Width - x - CardPad - fixedSum - (items.Count - 1) * RowGap);
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                if (it.Label.Length > 0)
                {
                    labels[i].Text = it.Label;
                    labels[i].Visible = true;
                    labels[i].Location = new Point(x, y + UiTheme.S(this, 5));
                    x += labels[i].Width + UiTheme.S(this, 8);
                }
                else { labels[i].Visible = false; labels[i].Text = ""; labels[i].Location = new Point(-2000, -2000); }
                int w = it.Grow ? free : it.FixedW;
                it.Ctl.Visible = true;
                it.Ctl.SetBounds(x, y, w, RowH);
                x += w + RowGap;
            }
            for (int i = items.Count; i < labels.Count; i++) { labels[i].Visible = false; labels[i].Text = ""; }
        }
        Card1Relayout = Layout;
        card.Layout += (_, _) => Layout();
        Layout();
        return card;
    }

    // ---------------- 卡1 那一行：后端方块（独立一卡）+ 卡1（DEC-182 甲，改判 DEC-089 的嵌套摆位）----------------
    // 行容器 Dock=Top 占满主区宽；方块 Dock=Left 定宽 S(78)、与卡1 等高；卡1 Dock=Fill 吃剩下的宽；
    // 中间那条缝走行容器的 Padding.Left = 方块宽 + RowGap —— 卡1 自己的 Layout 一个字都不用改。
    // 另两页（Strata / llama，BackendBox = null）Padding.Left 归 0、方块藏掉 → 卡1 铺满整行，与改前逐像素相同。
    // 最小宽探针量的是**这一行**不是卡1（见 MainForm.MinWidth.cs），否则会把让出去的 90 逻辑当成余量少算。
    Panel Card1Row(Control card)
    {
        var row = new Panel { Dock = DockStyle.Top, Height = card.Height, BackColor = UiTheme.Window };
        _backendToggle.Width = UiTheme.S(this, StackedToggle.CardLogicalW);   // 78 逻辑 = 150% 下 117 物理（DEC-089 定的那档）
        _backendToggle.Height = row.Height;                                    // 与卡1 等高
        // **Backdrop 跟着父层换色**（experience §4 那条"圆角外露出的是 Backdrop"，这次是反向踩）：
        // 它原来住在卡1 里，`MainForm.Card3.cs` 给它设的是卡片色；搬进行容器后外面是 Window 色，
        // 再垫 Card 就会在那圈圆角外露出一层方角浅边（他 2026-10-16 圈图点名的"圆角又有杂色"）。
        SetBoxBackdrop(_backendToggle, row.BackColor);   // 行容器是什么色，它就垫什么色（判据也照这一条比）
        // 方块**不参与停靠**：Dock=Left 看着更语义化，但藏起来那一档到底占不占位，实测不可靠 ——
        // 无头跑时 `Visible` 是"含祖先"的有效可见性（§7 那族坑），诊断实测到"方块已藏、卡1 仍被顶到 Left=78"，
        // 于是 strata/llama 两页白丢 78 物理的宽。改成：方块自己摆死在 (0,0)，那一列的位**只由行容器的 Padding 留**。
        _backendToggle.Dock = DockStyle.None;
        _backendToggle.Location = Point.Empty;
        card.Dock = DockStyle.Fill;             // 卡1 吃行容器去掉那条列之后的全部宽
        row.Controls.Add(card);
        row.Controls.Add(_backendToggle);
        void Sync()
        {
            bool shown = ActiveView.BackendBox is not null;    // 判"该不该在场"认模块表，不读 Visible
            if (_backendToggle.Visible != shown) _backendToggle.Visible = shown;
            int left = shown ? _backendToggle.Width + RowGap : 0;
            if (row.Padding.Left != left) row.Padding = new Padding(left, 0, 0, 0);   // 值没变就别再触发一轮布局
        }
        row.Layout += (_, _) => Sync();
        Card1RowSync = Sync;               // 切页那一拍要喊它（RefreshModuleScopedUi 与 Card1Relayout 同一个位置）
        Sync();
        return row;
    }

}