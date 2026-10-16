namespace MultiLauncher;

// 设置页的行工厂（ValueRow / ButtonRow / SwitchRow / NoteRow 那一族；口径：按钮恒在最右、说明整行放得下）。
// 2026-10-15 从 SettingsForm.cs 整段搬出，一行未改。
internal sealed partial class SettingsForm : Form
{
    // ---------------- 行工厂 ----------------

    // 次要按钮：与主界面同一套（高度走 DPI 换算、字号 9pt）。
    // 原来 Height / Font 是硬编码的 30 / 8.5f：在 150% DPI 下主界面按钮高 45px、这里只有 30px，明显偏矮。
    FlatButton Secondary(string text, Color backdrop) => new()
    {
        Text = text,
        Backdrop = backdrop,
        FillColor = UiTheme.Input,
        HoverColor = UiTheme.Border,
        BorderColor = UiTheme.Border,
        TextColor = UiTheme.Text,
        Font = UiTheme.Ui(9f),
        Height = UiTheme.S(this, 30),
    };

    static Label TextLabel(string text, float size, Color color) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = color,
        BackColor = UiTheme.Window,
        Font = UiTheme.Ui(size),               // DEC-138（F-5）：同一字号全页共用一支（原来每枚标签一支、控件不替谁释放）
    };

    // 文字所在的垂直中心：标签可能换行（行是顶对齐的，按**首行**算），其它控件的文字本来就居中（按整体中心算）。
    // 拿它当对齐基准，才不会在长说明换行后把整块居中、结果首行反倒跑到标题上面去。
    static double TextCenterY(Control c)
        => c is Label l && l.AutoSize && l.Height > l.Font.Height + 2
            ? c.Top + Math.Min(c.Height, c.Font.Height) / 2.0
            : c.Top + c.Height / 2.0;

    // 左侧标题与右侧控件**垂直居中**对齐。行里控件高度不一（输入框 S(30)、标题一行约 21 物理像素），
    // 按同一个 Top 摆就会让标题看着"挂在框子上半截"（用户实跑反馈：strata 目录几个字太靠上）。
    static void CenterV(Control label, Control peer, int left)
    {
        int lineH = Math.Min(label.Height, label.Font.Height);      // 换行的标签按首行参与对齐
        label.Location = new Point(left, Math.Max(0, (int)Math.Round(TextCenterY(peer) - lineH / 2.0)));
    }

    FlowLayoutPanel NewPage()
    {
        var p = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Window,
            Padding = new Padding(0, UiTheme.S(this, 8), 0, UiTheme.S(this, 8)),
            Margin = new Padding(0),
        };
        // 每行拉满页宽（FlowLayoutPanel 不会自动撑开子控件），并留出纵向滚动条的位置
        void Sync()
        {
            int w = p.ClientSize.Width - p.Padding.Horizontal;
            if (p.VerticalScroll.Visible) w -= SystemInformation.VerticalScrollBarWidth;
            foreach (Control c in p.Controls) c.Width = Math.Max(1, w - c.Margin.Horizontal);
        }
        p.ClientSizeChanged += (_, _) => Sync();
        p.ControlAdded += (_, _) => Sync();
        return p;
    }

    // 一行：底部 1px 分隔线由行的 Paint 画（`hairline:false` = 这条线归别人画 ——
    // DEC-131①：状态栏那块的分隔线改由承载格画，见 StatusBarOptionRows 的调用点与 PaintStatWrap）
    // DEC-138（F-6 / 缺口 6）：那一块上下的**分隔线只有一个画的人**，随开关换把手 ——
    //   开着：承载格自己画在块顶（DEC-131① 的修法，线随块走，任何滚动态都不丢）；
    //   关掉：承载格 `Visible=false` 不产 Paint，那一行就成了通用页唯一没有行尾线的行 → 线归本行画。
    // 两条判据互斥（一真一假），自检按"恰好一边画"穷举。判据位 = `_statMaster.Checked`（意图），**不读 Visible**
    //（无头自检里 Visible 恒 false = BUG-043 那一族空判据）。
    internal static bool StatLineFromWrap(bool masterOn) => masterOn;
    internal static bool StatLineFromRow(bool masterOn) => !masterOn;

    Panel NewRow(bool hairline = true)
    {
        var row = new Panel { BackColor = UiTheme.Window, Margin = new Padding(0) };
        if (hairline)
            row.Paint += (_, e) =>
            {
                int pad = UiTheme.S(this, PadX);
                using var pen = new Pen(UiTheme.Border, 1f);
                e.Graphics.DrawLine(pen, pad, row.Height - 1, row.Width - pad, row.Height - 1);
            };
        return row;
    }

    void GroupTitle(FlowLayoutPanel page, string text)
    {
        var row = NewRow();
        var l = TextLabel(text, 10f, UiTheme.Brand);
        l.Font = UiTheme.Ui(10f, FontStyle.Bold);
        row.Controls.Add(l);
        row.Layout += (_, _) =>
        {
            l.Location = new Point(UiTheme.S(this, PadX), UiTheme.S(this, 16));
            row.Height = l.Bottom + UiTheme.S(this, 8);
        };
        page.Controls.Add(row);
    }

    // 开关行：左侧标题+说明，右侧胶囊开关；与数值/路径行一样，点「保存」才生效（用户反馈：统一语义）
    // DEC-125②：返回那枚开关本体 —— 「显示状态栏」要在它下面长出一排子选项，得能订阅它的变更
    //（返回值不破坏原有调用点：其余十几处本来就只调不接）。
    ToggleSwitch SwitchRow(FlowLayoutPanel page, string title, string desc, Func<bool> get, Action<bool> set, bool hairline = true)
    {
        var row = NewRow(hairline);
        var t = TextLabel(title, 9.5f, UiTheme.Text);
        var d = TextLabel(desc, 8.5f, UiTheme.TextMuted);
        var sw = new ToggleSwitch { Backdrop = UiTheme.Window, Checked = get() };
        sw.Click += (_, _) => set(sw.Checked);      // 只写进设置窗副本；「保存」时统一落盘
        row.Controls.Add(t);
        row.Controls.Add(d);
        row.Controls.Add(sw);
        _alignPairs.Add((t, sw, title));
        row.Layout += (_, _) =>
        {
            int pad = UiTheme.S(this, PadX);
            sw.Location = new Point(row.Width - pad - sw.Width, UiTheme.S(this, 16));
            CenterV(t, sw, pad);          // 标题与开关同一水平线（原来各摆各的 Top）
            d.MaximumSize = new Size(Math.Max(UiTheme.S(this, 120), row.Width - pad * 2 - sw.Width - UiTheme.S(this, 16)), 0);
            d.Location = new Point(pad, t.Bottom + UiTheme.S(this, 4));
            row.Height = Math.Max(sw.Height + UiTheme.S(this, 18), d.Bottom + UiTheme.S(this, 12));
        };
        page.Controls.Add(row);
        return sw;
    }

    // 两档选择行（DEC-097③）：只有两档的东西**不该用输入框**（要人记住 queue/reject 两个词、还会填错），
    // 也不该用开关 —— 开关的语义是"开 / 关"，而这两档都不是"关"，那正是刚被删掉的 off 那个误会。
    // 用主窗页头同一套 SegmentedControl：两档同时看得见、一次点击到位，控件不引新的。
    SegmentedControl ChoiceRow(FlowLayoutPanel page, string title, string desc, string[] items,
                               int active, Action<int> set)
    {
        var row = NewRow();
        int labelW = UiTheme.S(this, 150);
        var t = TextLabel(title, 9.5f, UiTheme.Text);
        var d = TextLabel(desc, 8.5f, UiTheme.TextMuted);
        var seg = new SegmentedControl { Backdrop = UiTheme.Window };
        seg.SetItems(items);
        // 初值走运行时那个判据位（GpuMutexMode）：ini 被手改成 off / 乱词时界面显示的是"兜底后的那一档"，
        // 而不是空选或崩掉 —— 与"护栏不许因为改档而静默失效"同一条口径。
        seg.ActiveIndex = Math.Clamp(active, 0, items.Length - 1);
        seg.ValueChanged += (_, _) => set(seg.ActiveIndex);      // 只写副本，点「保存」才落盘（与开关行同语义）
        row.Controls.Add(t);
        row.Controls.Add(d);
        row.Controls.Add(seg);
        _alignPairs.Add((t, seg, title));
        row.Layout += (_, _) =>
        {
            int pad = UiTheme.S(this, PadX);
            int top = UiTheme.S(this, 12);
            seg.Location = new Point(pad + labelW, top);          // 与数值行的输入框同一值列
            CenterV(t, seg, pad);          // 标题与胶囊同一水平线（与开关行同一个算法，别自己算半高）
            d.MaximumSize = new Size(Math.Max(UiTheme.S(this, 120), row.Width - pad * 2), 0);
            d.Location = new Point(pad, Math.Max(t.Bottom, seg.Bottom) + UiTheme.S(this, 4));
            row.Height = Math.Max(seg.Height + UiTheme.S(this, 18), d.Bottom + UiTheme.S(this, 12));
        };
        page.Controls.Add(row);
        return seg;
    }

    // 数值/路径行：右侧输入框（可带「打开」按钮）；点「保存」才写入。
    // 返回输入框本身：④ 更新把 llama.cpp 目录切到新路径后，要能把这行的显示值同步改掉。
    // DEC-062：路径行右侧那枚按钮分三种语义。以前只有一种「打开」= 在资源管理器里把旧目录摊给人看，
    // 用户要的是"点了就能改目录"，所以**可改**的目录/文件走系统选择器；只读位置（模型配置目录、关于页三行）
    // 仍用「打开」直达——那里没有"改"的余地，换成选择器就丢了直达入口（DEC-055 加它们本就是为了直达）。
    internal enum PathBtn { None, OpenFolder, PickFolder, PickFile }

    RoundedTextBox ValueRow(FlowLayoutPanel page, string title, string desc, string value, Action<string> set,
                            PathBtn pb = PathBtn.None)
    {
        var row = NewRow();
        int labelW = UiTheme.S(this, 150);
        var t = TextLabel(title, 9.5f, UiTheme.Text);
        var d = TextLabel(desc, 8.5f, UiTheme.TextMuted);
        var box = new RoundedTextBox { Backdrop = UiTheme.Window, Radius = 8 };
        box.Inner.Font = UiTheme.Ui(9f);
        box.Height = UiTheme.S(this, 30);
        box.Text = value;
        box.Tag = title;      // 日志/回显里要说清改的是哪一行（PickFolder / PickFile 用它）
        box.TextChanged += (_, _) => set(box.Text);
        FlatButton? btn = null;
        if (pb != PathBtn.None)
        {
            var label = pb switch { PathBtn.OpenFolder => "打开", PathBtn.PickFile => "选文件", _ => "选择" };
            btn = Secondary(label, UiTheme.Window);
            btn.Click += (_, _) =>
            {
                if (pb == PathBtn.PickFolder) { PickFolder(box); return; }
                if (pb == PathBtn.PickFile) { PickFile(box); return; }
                try
                {
                    var p = box.Text.Trim();
                    if (Directory.Exists(p))
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(p) { UseShellExecute = true });
                    else
                        _main.LogToUi("目录不存在：" + (p.Length > 0 ? p : "（空）"));   // 原来静默无反应，点着像坏了
                }
                catch (Exception ex) { _main.LogToUi("打开目录失败：" + ex.Message); }
            };
            row.Controls.Add(btn);
        }
        row.Controls.Add(t);
        row.Controls.Add(d);
        row.Controls.Add(box);
        _alignPairs.Add((t, box, title));
        row.Layout += (_, _) =>
        {
            int pad = UiTheme.S(this, PadX);
            int top = UiTheme.S(this, 12);
            box.Location = new Point(pad + labelW, top);
            box.Width = Math.Max(UiTheme.S(this, 140), row.Width - pad - box.Left);
            if (btn is not null)
            {
                // 先按文字定宽、再摆位（DEC-061 那条教训：宽度在定位之后才改，右边缘会超出内缩边距）
                btn.Width = TextRenderer.MeasureText(btn.Text, btn.Font).Width + UiTheme.S(this, 22);
                btn.Location = new Point(row.Width - pad - btn.Width, box.Top + UiTheme.S(this, 1));
                box.Width = Math.Max(UiTheme.S(this, 140), btn.Left - UiTheme.S(this, 8) - box.Left);
            }
            CenterV(t, box, pad);         // 「Strata 目录」这类标题与右侧输入框同一水平线
            // 说明文字跨满整行、且必须落在输入框下方：原来限宽到左侧标签列（labelW - 8）会折成 2~3 行，
            // 现在放开宽度后若还按「标题下方」摆，就会压在输入框的下半部分上（实测重叠约 10px）。
            d.MaximumSize = new Size(Math.Max(UiTheme.S(this, 120), row.Width - pad * 2), 0);
            d.Location = new Point(pad, box.Bottom + UiTheme.S(this, 6));
            row.Height = Math.Max(d.Bottom, box.Bottom) + UiTheme.S(this, 12);
        };
        page.Controls.Add(row);
        return box;
    }

    // DEC-062：可改的目录 / 文件路径行走**系统选择器**（用户：那枚按钮该让人直接改目录，而不是只把旧目录打开给人看）。
    // 生效时机一条没变：选完只写进输入框并回显一行日志，**点「保存」才落盘**（AGENTS：所有项点保存才生效）。
    // 取消 = 完全静默（2026-10-04 用户口径：不写日志、不弹提示——"没改动"不是值得记的事，点了没声就是取消）。
    // 给系统文件夹选择器定位到当前值（抽成静态以便自检断言）。
    // 为什么**不用** SelectedPath：.NET 的 FolderBrowserDialog 处理 SelectedPath 时会把文件夹的**叶子名**
    // 预填进底栏「文件夹:」输入框（内部调的是 IFileDialog.SetFileName(叶子名)），光标停在尾部 → 窄框里
    // 只看得见名字的尾巴（用户 2026-10-04 截图报的 "win-cuda-13.4-x64" 就是 llama-b11379-bin-win-cuda-13.4-x64
    // 的尾部）。InitialDirectory 只做"从这开始浏览"，不开预填，底栏保持干净；点「选择文件夹」直接确认当前目录。
    internal static void SeedFolderDialog(FolderBrowserDialog fd, string cur)
    {
        try { if (Directory.Exists(cur)) fd.InitialDirectory = cur; } catch { /* 非法路径就让它从默认位置开 */ }
    }

    void PickFolder(RoundedTextBox box)
    {
        using var fd = new FolderBrowserDialog { Description = "选择目录", UseDescriptionForTitle = true };
        var cur = box.Text.Trim();
        SeedFolderDialog(fd, cur);
        if (fd.ShowDialog(this) != DialogResult.OK) return;   // 取消：静默收场
        var picked = fd.SelectedPath;   // OK 之后这个属性就是选中的目录（这个 SDK 版本没有 Path 属性）
        if (picked.Length == 0)
        {
            _main.LogActive("选出来的目录路径是空的，这一行没改动");
            Toast.Show(this, "选出来的路径是空的，这一行没改动", Toast.Kind.Warn);
            return;
        }
        box.Text = picked;
        _main.LogActive("已选择目录：" + picked + "（点「保存」才生效）");
        Toast.Show(this, "已选择目录，点「保存」才生效", Toast.Kind.Info);       // 全路径留在日志里，提示只说一句
    }

    void PickFile(RoundedTextBox box)
    {
        using var od = new OpenFileDialog
        {
            Title = "选择模型配置文件",
            Filter = "模型配置 (*.ini)|*.ini|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            RestoreDirectory = true,
        };
        var cur = box.Text.Trim();
        SeedFileDialog(od, cur);
        if (od.ShowDialog(this) != DialogResult.OK) return;   // 取消：静默收场（同 PickFolder）
        box.Text = od.FileName;
        _main.LogActive("已选择文件：" + od.FileName + "（点「保存」才生效）");
        Toast.Show(this, "已选择文件，点「保存」才生效", Toast.Kind.Info);
    }

    // 给系统文件选择器定位到当前值（抽成静态以便自检断言）。与 SeedFolderDialog 同一条理由，
    // 用户 2026-10-04 补充报的正是这里（「模型配置」= llama_models.ini 那份）：**别把整条路径塞进
    // 「文件名:」框**——WinForms 把 FileName **原样**交给 IFileDialog.SetFileName（源码级查证
    // FileDialog.Vista.cs：不拆分路径、不解析），长路径挤在窄框里 + 光标停在尾部 → 只显示得下尾巴。
    // 做法：目录走 InitialDirectory、框里只放文件名——裸文件名按"当前目录"解析点「打开」直接命中
    // （与用户在框里手打文件名同一路子）；文件不在（或路径非法）就只定位到目录、不预填。
    internal static void SeedFileDialog(OpenFileDialog od, string cur)
    {
        try
        {
            var dir = Path.GetDirectoryName(cur) ?? "";
            if (dir.Length > 0 && Directory.Exists(dir)) od.InitialDirectory = dir;
            if (File.Exists(cur) && Path.GetFileName(cur).Length > 0) od.FileName = Path.GetFileName(cur);
        }
        catch { /* 非法路径就当没提示，让用户自己走过去 */ }
    }

    // 带右侧按钮的行（检查更新 / 回退 / 运行 SETUP）。返回按钮本身：
    // ①④ 要求「升级中/检查中」时按实际状态灰置，不是点了没反应（DEC-052）
    FlatButton ButtonRow(FlowLayoutPanel page, string title, string desc, string buttonText, Action onClick)
    {
        var row = NewRow();
        var t = TextLabel(title, 9.5f, UiTheme.Text);
        var d = TextLabel(desc, 8.5f, UiTheme.TextMuted);
        d.Tag = "btn-desc:" + title;      // 给自检认出"按钮行的说明文字"（量行数用，见 ButtonRowDescForTest）
        var btn = Secondary(buttonText, UiTheme.Window);
        btn.Click += (_, _) => onClick();
        row.Controls.Add(t);
        row.Controls.Add(d);
        row.Controls.Add(btn);
        _alignPairs.Add((t, btn, title));
        row.Layout += (_, _) =>
        {
            int pad = UiTheme.S(this, PadX);
            btn.Width = TextRenderer.MeasureText(btn.Text, btn.Font).Width + UiTheme.S(this, 22);
            btn.Location = new Point(row.Width - pad - btn.Width, UiTheme.S(this, 12));
            CenterV(t, btn, pad);         // 标题与按钮同一水平线
            if (desc.Length > 0)
            {
                // 用户 2026-10-04 第 4 条：说明文字要**整行显示、不折行**。原来这里预留 S(260) 把文字
                // 挤到按钮左侧的窄栏里，「每次要写 llama_models.ini 之前先存一份到 data\llama\，只留最近 5 份。」
                // 正好被压成两行。改成铺满整行，并把这一行放到**标题与按钮都之下**（按钮在右上、
                // 高约 S(30)，文字若仍按标题下方摆就会与按钮水平相撞）。
                d.MaximumSize = new Size(Math.Max(UiTheme.S(this, 120), row.Width - pad * 2), 0);
                d.Location = new Point(pad, Math.Max(t.Bottom, btn.Bottom) + UiTheme.S(this, 4));
                row.Height = Math.Max(btn.Bottom + UiTheme.S(this, 12), d.Bottom + UiTheme.S(this, 12));
            }
            else
            {
                d.Location = new Point(-2000, -2000);       // 无说明文字：不占位、不进 dump（2026-10-02）
                row.Height = btn.Bottom + UiTheme.S(this, 12);
            }
        };
        page.Controls.Add(row);
        return btn;
    }

    // DEC-110②：`NoteRow` 这个工厂随最后一条常驻说明一起退役（四处调用点全被用户点名删掉，见 PageGeneral /
    // PageNinfer 里那三处注释）。以后要"整行只有一句灰字说明"再把它补回来 —— 别拿 ButtonRow 的 desc 硬塞一句没有主人的话。

    // 只读值行（关于页 / Strata 节的内建引擎行）：返回值 Label 供后续刷新（2026-10-02）。
    // 带 openText 时右侧多一枚「打开」——DEC-055 要求能直达 ini / data / logs 这些位置。
    Label AboutRow(FlowLayoutPanel page, string title, string value, Color? valueColor = null,
                   string? openText = null, Action? onOpen = null)
    {
        var row = NewRow();
        int labelW = UiTheme.S(this, 150);
        var t = TextLabel(title, 9.5f, UiTheme.Text);
        var v = TextLabel(value, 9f, valueColor ?? UiTheme.TextMuted);
        row.Controls.Add(t);
        row.Controls.Add(v);
        _alignPairs.Add((v, t, title));
        FlatButton? btn = null;
        if (openText is not null && onOpen is not null)
        {
            btn = Secondary(openText, UiTheme.Window);
            btn.Width = UiTheme.S(this, 60);
            btn.Click += (_, _) => onOpen();
            row.Controls.Add(btn);
        }
        row.Layout += (_, _) =>
        {
            int pad = UiTheme.S(this, PadX);
            t.Location = new Point(pad, UiTheme.S(this, 12));
            int right = row.Width - pad;
            if (btn is not null)
            {
                btn.Width = TextRenderer.MeasureText(btn.Text, btn.Font, new Size(int.MaxValue, int.MaxValue),
                    TextFormatFlags.NoPadding).Width + UiTheme.S(this, 22);
                btn.SetBounds(right - btn.Width, UiTheme.S(this, 11), btn.Width, UiTheme.S(this, 30));
                right = btn.Left - UiTheme.S(this, 10);
            }
            v.MaximumSize = new Size(Math.Max(UiTheme.S(this, 120), right - (pad + labelW)), 0);
            CenterV(v, t, pad + labelW);      // 值与标题同一水平线（字号差半号，按 Top 摆会错开一两个像素）
            row.Height = Math.Max(t.Bottom, Math.Max(v.Bottom, btn?.Bottom ?? 0)) + UiTheme.S(this, 12);
        };
        page.Controls.Add(row);
        return v;
    }

}
