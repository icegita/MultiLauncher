namespace MultiLauncher;

// 关于页「帮助」：内置说明书（嵌入资源 manual.md 的读取与展示）。
// 2026-10-15 从 Dialogs.cs 整段搬出，一行未改。
internal static partial class Dialogs
{
    // ---------------- 关于页「帮助」：内置说明书 ----------------
    // 说明书**内嵌在 exe 里**（csproj 的 EmbeddedResource + LogicalName=manual.md），不放程序目录旁边 ——
    // 它只有 20 KB 级的文本，内嵌换来"程序目录零新增"（"整个目录删掉就能重来"那条不破）。
    // 渲染走与「更新内容」同一个 MdScroll（**表格分支 1.7.7 已落地**），所以观感 / 滚动 / 选中复制与那一族一字不差。
    internal static string ManualMarkdown()
    {
        try
        {
            using var st = typeof(Dialogs).Assembly.GetManifestResourceStream("manual.md");
            if (st is null) return "";
            using var rd = new StreamReader(st, System.Text.Encoding.UTF8);
            return rd.ReadToEnd();
        }
        catch { return ""; }
    }

    /// 关于页「帮助」那枚打开的东西（模态窗，标题写清是使用说明书）。
    public static void ShowManual(Form owner)
    {
        var md = ManualMarkdown();
        var s = new Shell(owner, "帮助 · 使用说明书", 660, sizable: true);
        var box = new MdScroll { Backdrop = UiTheme.Window };
        box.SetBounds(Shell.S(16), Shell.S(14), s.ContentWidth, Shell.S(420));
        s.Body.Controls.Add(box);
        box.SetMarkdown(md.Length > 0 ? md : "（这一版没有带上说明书）");
        // 同上：内容高度跟着窗体走；拖宽时 Markdown 整篇重排（表格列宽重算）
        s.BodyResized += () =>
        {
            box.Width = s.ContentWidth;
            box.Height = Math.Max(Shell.S(200), s.BodyHeight - box.Top - Shell.S(10));
        };
        s.AddBtn("关闭", primary: true).Click += (_, _) => s.CloseWith(DialogResult.OK);
        s.OnEnter(() => s.CloseWith(DialogResult.OK));
        s.OnEsc(() => s.CloseWith(DialogResult.OK));
        s.ClientSize = new Size(s.ClientSize.Width, box.Bottom + Shell.S(16) + s.Foot.Height);
        s.ShowDialog(owner);
        s.Dispose();
    }

    // 可拖窗（帮助 / 查看更新内容）自检用：边框真是可拖的，且**内容高度跟着窗体走**。
    // 为什么值得一条判据：内容高度原本是按内容算一次的固定值 —— 不接 `BodyResized` 的话，
    // 窗体一拖大内容下面露白、拖小把内容裁掉，那就是"能拖但拖着难看"。
    internal static (bool Sizable, int BoxH0, int BoxH1, int Gap) ProbeSizableShell(Form owner)
    {
        var s = BuildUpdateDetail(owner, "vX", "vY",
            "| 列A | 列B |\n| --- | --- |\n| a1 | b1 |\n" + new string('补', 600));
        var box = s.Body.Controls.OfType<MdScroll>().First();
        int h0 = box.Height;
        int grow = UiTheme.S(s, 120);
        s.ClientSize = new Size(s.ClientSize.Width + UiTheme.S(s, 200), s.ClientSize.Height + grow);
        s.PerformLayout();
        return (s.FormBorderStyle == System.Windows.Forms.FormBorderStyle.Sizable, h0, box.Height, box.Height - h0);
    }

    // 备份选择弹窗：列出可回退的备份（最新在前），默认选中传入的索引；取消返回 null。
    // 下拉用界面上同一套自绘 DarkSelect（原生 ComboBox 在深色窗里就是一块白底）。
    // label 给 null = 配置备份那句话（Strata / llama 照旧）；WSL 编译快照那一路自报一句（DEC-106）。
    public static string? PickSnapshot(Form owner, List<string> entries, int initial, string? label = null)
    {
        var s = new Shell(owner, "回退到哪一份备份", 400);
        var lbl = new Label
        {
            Text = label ?? "选择要回退到的备份（目录名 = 升级前版本-时间）", AutoSize = true,
            ForeColor = UiTheme.TextMuted, BackColor = UiTheme.Window,
            Font = UiTheme.Ui(8.5f), Location = new Point(Shell.S(16), Shell.S(14)),
        };
        s.Body.Controls.Add(lbl);
        var cbo = new DarkSelect { Backdrop = UiTheme.Window, MinDropWidth = 340 };
        cbo.SetItems(entries.Select((e, i) => new DarkItem { Text = e + (i == 0 ? "  ← 最新" : ""), Tag = i }));
        cbo.SelectedIndex = Math.Max(0, Math.Min(initial, entries.Count - 1));
        cbo.SetBounds(Shell.S(16), lbl.Bottom + Shell.S(8), s.ContentWidth, Shell.S(30));
        s.Body.Controls.Add(cbo);
        s.AddBtn("取消").Click += (_, _) => s.CloseWith(DialogResult.Cancel);
        s.AddBtn("回退", primary: true).Click += (_, _) => s.CloseWith(DialogResult.OK);
        s.OnEnter(() => s.CloseWith(DialogResult.OK));
        s.OnEsc(() => s.CloseWith(DialogResult.Cancel));
        s.ClientSize = new Size(s.ClientSize.Width, cbo.Bottom + Shell.S(18) + s.Foot.Height);
        var r = s.ShowDialog(owner);
        int idx = cbo.SelectedIndex;
        s.Dispose();
        if (r != DialogResult.OK || idx < 0 || idx >= entries.Count) return null;
        return entries[idx];
    }

    // 输入框（起名用）：确定返回去空格后的文本，取消/Esc 返回空串。
    public static string Prompt(Form owner, string title, string label, string initial)
    {
        var s = new Shell(owner, title, 340);
        var lbl = new Label
        {
            Text = label, AutoSize = true, ForeColor = UiTheme.Text, BackColor = UiTheme.Window,
            Font = UiTheme.Ui(9f), Location = new Point(Shell.S(16), Shell.S(16)),
        };
        s.Body.Controls.Add(lbl);
        var box = new RoundedTextBox { Backdrop = UiTheme.Window, Radius = 8, Text = initial };
        box.Inner.Font = UiTheme.Ui(9.5f);      // 名字是给人读的，不用内层默认的等宽
        box.SetBounds(Shell.S(16), lbl.Bottom + Shell.S(9), s.ContentWidth, Shell.S(30));
        s.Body.Controls.Add(box);
        s.AddBtn("取消").Click += (_, _) => s.CloseWith(DialogResult.Cancel);
        s.AddBtn("确定", primary: true).Click += (_, _) => s.CloseWith(DialogResult.OK);
        s.OnEnter(() => s.CloseWith(DialogResult.OK));
        s.OnEsc(() => s.CloseWith(DialogResult.Cancel));
        s.ClientSize = new Size(s.ClientSize.Width, box.Bottom + Shell.S(18) + s.Foot.Height);
        s.Shown += (_, _) => { box.Inner.SelectAll(); box.Inner.Focus(); };
        var r = s.ShowDialog(owner);
        var text = box.Text;
        s.Dispose();
        return r == DialogResult.OK ? text.Trim() : "";
    }

    // 确认框：返回 Yes / No（Esc 与「否」同义）。danger=true → 红叹号 + 主按钮红字（结束外部进程这类）。
    public static DialogResult Confirm(Form owner, string title, string message,
                                       string? detail = null, string yes = "是", string no = "否", bool danger = false)
    {
        var s = new Shell(owner, title, 380);
        int bottom = AddBodyText(s, message, detail, danger ? "!" : "?", danger ? UiTheme.Danger : UiTheme.Brand);
        s.AddBtn(no).Click += (_, _) => s.CloseWith(DialogResult.No);
        s.AddBtn(yes, primary: !danger, danger: danger).Click += (_, _) => s.CloseWith(DialogResult.Yes);
        s.OnEnter(() => s.CloseWith(DialogResult.Yes));
        s.OnEsc(() => s.CloseWith(DialogResult.No));
        s.ClientSize = new Size(s.ClientSize.Width, bottom + Shell.S(18) + s.Foot.Height);
        var r = s.ShowDialog(owner);
        s.Dispose();
        return r;
    }

    // 出图台 / 自检开关：开了之后 ShowDialog 不阻塞，改成"显示→拍图→关闭"（见 Shell.ShowDialog）
    // DEC-138（M6 缺口 11）：自检里那几扇预览窗摆到**画外**（出图台照旧摆在 (80,60)，那是要给人看的）。
    // 理由与 DEC-127⑥ 同一件事：预览走的是真 `Show(owner)`，他在旁边会看见一框一框闪过；而 PrintWindow 拍的是
    // **窗口自己画的**内容，摆哪儿都拍得到 —— 这条不凭断言：改完回看自检那条"白点 0 / 绿点 > 20"，
    // 拍出全黑就退回 (80,60) 并把这一格登记成已知代价。
    // **第 36 轮把那句"摆哪儿都拍得到"推翻了（BUG-073）**：画外的窗 DWM 可能压根没合成过客户区，
    // PrintWindow 就拍到一片白 / 一片黑 —— 所以自检那条判据现在改吃 `PreviewClientShot` 那一张（DrawToBitmap）。
    internal static bool PreviewOffscreen;

    /// <summary>③（BUG-073）**只给自检用**：多写一份"客户区那一张"（`UiPreviewForm.CaptureClient` = DrawToBitmap）。</summary>
    internal static bool PreviewClientShot;

    /// <summary>上一张客户区图到底出没出图（证人位：判据此区分"没拍到"与"真有白底"，不许拿空帧当白底报）。</summary>
    internal static bool PreviewClientOk;

    /// <summary>DEC-159（DEC-127⑥ 那一族的最后一道闸）：自检期间**一律不真弹模态框**。
    /// 为什么现在才上：`Shell.ShowDialog` 挂在无头自检里就是"整趟跑等人来点"（上一轮红侧写成恒弹 = 挂 15 分钟被强杀），
    /// 而他坐在旁边还会看见一框框闪（同 DEC-111② 那句"护栏要挡整条路"）。
    /// **出图台不开这一位** —— `--uiprobe dialogs` 那八张拍的就是真弹窗。
    /// 两道证人位 = <see cref="AttemptsForTest"/>（这一击真走到了要弹框）与 <see cref="ShownForTest"/>（闸上必须恒 0）。</summary>
    internal static bool DisabledForTest;

    /// <summary>有人**要求**弹一框的次数（预览档也算：那条路真造了窗，只是不阻塞）。</summary>
    internal static int AttemptsForTest;

    /// <summary>真弹出去（阻塞等人点）的次数 —— 闸上时必须为 0。</summary>
    internal static int ShownForTest;

    /// <summary>最近一框的标题（自检拿它证"落点确实分了两条路"，不是只看计数）。</summary>
    internal static string LastTitleForTest = "";

    internal static void SetPreview(bool on, string dir = "")
    {
        Shell.Preview = on;
        if (!on) return;              // 关档**不清**这一位：自检那条判据是在 finally 关掉预览之后才回看的（清了就变成恒"取景失败"）
        PreviewClientOk = false;
        Shell.PreviewDir = dir;
        Shell.PreviewSeq = 0;
    }
    internal static string PreviewNoteForTest = "";

    // 出图对照（DEC-051）：行内文字垂直摆位——居中 / 顶对齐两种各拍一张给用户挑
    internal static bool PreviewCenterLines = true;
    internal static bool PreviewSelects;

    // 自检出口（DEC-050/051）：真显示一框更新说明，量"内容总高 / 可视高 / 滑条可不可滚"，
    // 再滚三格看**内容偏移**是否真的动了。用户报的是"滚动条异常、点不动、滚轮不动、拖的时候掉圆角残渣"
    // ——断言必须打在滚动本身与容器上，不能只看画得好不好。
    internal static (bool Scrollable, bool WheelMoves, int DocHeight, int ViewHeight, int After) ProbeDetailScrolling(Form owner, string notes)
    {
        var s = BuildUpdateDetail(owner, "vX", "vY", notes);
        s.StartPosition = FormStartPosition.Manual;
        s.Location = new Point(-4000, -4000);
        s.Show(owner);
        for (int i = 0; i < 6; i++) { Application.DoEvents(); Thread.Sleep(40); }
        var box = s.Body.Controls.OfType<MdScroll>().First();
        var bar = box.BarForTest;
        bool scrollable = bar.Scrollable;
        box.Doc.ScrollY = 0;
        bar.ScrollByWheel(-3 * 120);      // 负 delta = 往下滚（与 WM_MOUSEWHEEL 同口径：正数往上）
        for (int i = 0; i < 4; i++) { Application.DoEvents(); Thread.Sleep(40); }
        int after = box.Doc.ScrollY;
        int view = box.Doc.ClientSize.Height, total = box.Doc.Total;
        s.Close();
        for (int i = 0; i < 3; i++) { Application.DoEvents(); Thread.Sleep(30); }
        s.Dispose();
        return (scrollable, after > 0, total, view, after);
    }

    // DEC-092 第 3 条（用户："里面的内容现在无法选中复制了"）：自绘的 MdDoc 补了拖选/Ctrl+A/Ctrl+C 之后，
    // 断言要打在"选中的就是看到的那段字"上。两条纪律：
    //   · **不真写剪贴板** —— 自检跑在用户机器上，占了他的剪贴板等于改他的机器状态，
    //     所以只验"空选中时拒绝写"（那条路不碰剪贴板），复制的字符串本身走 SelectedText 这条同一家判据。
    //   · 坐标只取"篇首/篇尾"两个稳定端点，不量像素（150% DPI 下按字符定位由 PosAt 自己保证单调）。
    internal static (string Plain, string SelectedAll, string Sub, bool CopyRefusesEmpty, int PosTop, int PosBottom)
        ProbeDetailSelection(Form owner, string notes)
    {
        var s = BuildUpdateDetail(owner, "vX", "vY", notes);
        s.StartPosition = FormStartPosition.Manual;
        s.Location = new Point(-4000, -4000);
        s.Show(owner);
        for (int i = 0; i < 6; i++) { Application.DoEvents(); Thread.Sleep(40); }
        var doc = s.Body.Controls.OfType<MdScroll>().First().Doc;
        bool copiedEmpty = doc.CopyToClipboard();               // 什么都没选 = 不该写、也写不进
        doc.SetSelectionForTest(3, 9);
        string sub = doc.SelectedTextForTest;
        int posTop = doc.PosAtForTest(new Point(0, 0));
        int posBottom = doc.PosAtForTest(new Point(5, 100000));
        doc.SelectAll();                                        // Ctrl+A 与双击都汇到这里
        string all = doc.SelectedTextForTest;
        string plain = doc.PlainForTest;
        s.Close();
        for (int i = 0; i < 3; i++) { Application.DoEvents(); Thread.Sleep(30); }
        s.Dispose();
        return (plain, all, sub, !copiedEmpty, posTop, posBottom);
    }

    // 出图台（`--uiprobe dialogs <目录>`）：把这一族每个形态各拍一张真组件图（假数据）。
    // 文案就用真会出现在用户眼前的那几句，拍完直接和 docs/design/原型-更新行与弹窗.html 对照。
    internal static int RunPreview(string dir, Form owner)
    {
        System.IO.Directory.CreateDirectory(dir);
        Shell.Preview = true; Shell.PreviewDir = dir; Shell.PreviewSeq = 0;
        Message(owner, "端口冲突",
            "llama 内部端口 8078 与 Strata 内部端口 8078 相同——两个引擎可能同时在跑，内部端口必须错开。",
            "改开后点「保存」。", danger: true);
        Message(owner, "更新完成", "已升级到 v0.1.36，引擎也同步为新版官方预编译了。", "点「启动」即可用新版本跑模型。");
        Confirm(owner, "升级前先停服务", "升级需要先停止正在运行的服务（升级时要替换引擎文件）。",
            "现在停止并继续？", yes: "停止并升级", no: "取消");
        Confirm(owner, "已有 llama-server 在跑", "端口 8078 上是 llama-server.exe（PID 21436），不是本启动器拉起的。",
            "要结束它吗？", yes: "结束它", no: "取消", danger: true);
        Prompt(owner, "给模型起名字", "API 模型名（客户端填的 model）:", "swift-xxs-256K");
        PickSnapshot(owner, new List<string> { "0.1.35-20261003-012210", "0.1.34-20261001-203311", "0.1.33-20260929-114502" }, 0);
        UpdateDetailDialog(owner, "v0.1.38", "v0.1.37",
            "**Faster prompts, `--kv q4_0` prompts on tensor cores, a 6 GB card that starts.**\n"
            + "\n"
            + "**Security: the server without an API key.** Without `api_key`, the server now answers only\n"
            + "requests addressed to a name it knows (`localhost`, an IP address, this PC's name).\n"
            + "`/unload` and `/load` take only JSON: `curl -X POST localhost:8080/unload`.\n"
            + "\n"
            + "**Faster prompts:**\n"
            + "- The prompt path gathers a group of experts in one launch with one wait (#372).\n"
            + "- Measured on an RTX 5070 12 GB: Q2_0 prompts +10% at 4K, +2.7% at 32K, +3.0% at 128K.\n"
            + "\n"
            + "---\n"
            + "\n"
            + "**Updating:** run `UPDATE.bat`. Engines: https://github.com/Niko1221/Strata/releases/download/v0.1.38/strata-windows-x64\n"
            + "\n"
            + "**Checked before the release:** the same answers as 0.1.37 on all four quants (Q2_0, IQ3_XXS, IQ3_S, the Coder), 10/10 each\n"
            + "with a fixed cache; kernel parity tests; real use at a 57K-token prompt; Linux (WSL) Q2_0 identical 10/10;\n"
            + "tools/setup 287 tests, server 197 tests (the new security tests included).\n"
            + "A longer tail so this screenshot clearly overflows the view and the thin bar shows up: " + Environment.NewLine
            + "keep scrolling with the wheel over this area, or drag the bar; the settings page behind must not move." + Environment.NewLine
            + Environment.NewLine
            + "**Fixes from your reports:** AMD on a Linux desktop (#560): when the desktop crashes once the model is loaded, the expert cache has filled the card the desktop needs." + Environment.NewLine
            + "<a href=\"https://buymeacoffee.com/strataengine\"><img src=\"x.png\" alt=\"Buy Me A Coffee\"></a>");
        PreviewSelects = true;        // DEC-092：先拍一张"选中前两行"的样子（验证底色与按行铺的位置）
        UpdateDetailDialog(owner, "v0.1.38", "v0.1.37",
            "**Faster prompts, `--kv q4_0` prompts on tensor cores, a 6 GB card that starts.**" + Environment.NewLine
            + Environment.NewLine
            + "**Security: the server without an API key.** Without `api_key`, the server now answers only" + Environment.NewLine
            + "requests addressed to a name it knows (`localhost`, an IP address, this PC's name)." + Environment.NewLine);
        PreviewSelects = false;
        // 对齐方式对照（DEC-051）：同一份内容，顶对齐版再拍一张
        PreviewCenterLines = false;
        UpdateDetailDialog(owner, "v0.1.38", "v0.1.37",
            "**顶对齐版对照：**Faster prompts, `--kv q4_0` prompts on tensor cores, a 6 GB card that starts, and a batch of community PRs.\n"
            + "- The prompt path gathers a group of experts in one launch with one wait (#372).\n"
            + "- Measured on an RTX 5070 12 GB: Q2_0 prompts +10% at 4K, +2.7% at 32K.\n"
            + "\n"
            + "---\n"
            + "\n"
            + "**Updating:** run `UPDATE.bat`. Setup installs engine 0.1.38.\n");
        PreviewCenterLines = true;
        Confirm(owner, "llama.cpp 已更新到 b11368",
            "新目录：D:\\llama-b11368-bin-win-cuda-13.4-x64（设置里的目录已切过去）",
            "刚下载的压缩包 145.8 MB 要删掉吗？旧目录原样保留，随时能改回去。", yes: "删除", no: "保留");
        Shell.Preview = false;
        return Shell.PreviewSeq;
    }
}
