namespace MultiLauncher;

// 深色弹窗族（2026-10-03 反馈③：回退、起名这类界面没跟项目风格）。
// 五个入口：Message（信息，替掉 12 处系统 MessageBox）/ Confirm（是·否）/ Prompt（起名字）/
//           PickSnapshot（回退选哪份备份）/ UpdateDetailDialog（更新内容）。
// 均为无状态静态方法，只依赖 owner（居中于父窗），不触碰 MainForm 实例字段。
internal static partial class Dialogs
{
    // ---------------- 骨架 ----------------
    // 一套形状：底色 UiTheme.Window、顶部一条发丝线与深色标题栏分界、正文区（图标 + 正文 + 灰色补充行）、
    // 底部按钮带（右下角右起；主按钮 Brand 实底、次要 Input 底 + Border 描边、危险动作 Danger 文字、圆角 8、高 30）。
    // 尺寸一律走 UiTheme.S（启动时捕获的窗体 DPI + AutoScaleMode.None）——原来各弹窗按 owner.DeviceDpi 换算，
    // 与 UiTheme 那套叠加过（见 UiTheme.cs 头注：同一份 exe 两次启动尺寸不一致的坑）。
    // FlatButton 不是 IButtonControl（没有 DialogResult / AcceptButton 这条路），所以回车与 Esc
    // 由 Shell.ProcessCmdKey 显式派给「默认动作」与「取消动作」——更新内容框的默认动作故意是「关闭」，
    // 防回车误触发一次下载 + 覆盖升级。
    sealed class Shell : Form
    {
        internal readonly Panel Body = new() { Dock = DockStyle.Fill, BackColor = UiTheme.Window };
        internal readonly Panel Foot = new() { Dock = DockStyle.Bottom, BackColor = UiTheme.Window };
        Action? _enter, _esc;
        int _right;

        internal Shell(Form owner, string title, int logicalW, bool sizable = false)
        {
            Text = title;
            // sizable = true 时换成可拖边框（内容窗：帮助 / 查看更新内容）。其余小框仍是固定对话框。
            FormBorderStyle = sizable ? FormBorderStyle.Sizable : FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false;
            if (sizable) MinimumSize = new Size(S(420), S(260));
            ShowIcon = false; ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.None;
            KeyPreview = true;
            BackColor = UiTheme.Window;
            ForeColor = UiTheme.Text;
            Font = UiTheme.Ui(9f);
            CreateControl();                                  // 先建句柄：UiTheme.S 与文字测量都要真实 DPI
            UiTheme.CaptureDpi(Handle);
            UiDark.ApplyDialog(this);                         // DEC-092：标题栏与内容同底色，边框仍由系统画
            ClientSize = new Size(S(logicalW), S(140));
            Foot.Height = S(52);
            var topLine = new HairLine { Dock = DockStyle.Top, Height = 1, Backdrop = UiTheme.Window, LineColor = UiTheme.Border };
            Controls.Add(Body);
            // 可拖窗：Body 是 Dock.Fill，它的 Layout 就是"窗体被拖了"的信号（内容窗靠这根线把高度接回去）
            Body.Layout += (_, _) => BodyResized?.Invoke();
            Controls.Add(topLine);
            Controls.Add(Foot);
            _right = ClientSize.Width - S(12);
        }

        /// Body 的可用高度（Body 是 Dock.Fill，上下被 1px 发丝线与 Foot 占掉）。可拖窗算内容高度用它。
        internal int BodyHeight => ClientSize.Height - 1 - Foot.Height;

        /// 窗体被拖大/拖小时发一次（Body 的 Layout）。内容窗靠它把内容高度接回去。
        internal event Action? BodyResized;

        internal static int S(int px) => (int)Math.Round(px * UiTheme.Dpi / 96.0);
        internal int ContentWidth => ClientSize.Width - S(32);

        /// 正文按最大宽度折行后量出实际高度（Label 默认 MeasureText 会把行距空白算进去，量字一律 NoPadding）
        internal static int MeasureText(string text, Font f, int width) =>
            TextRenderer.MeasureText(text, f, new Size(width, int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height;

        internal void SetHeight(int logicalH) => ClientSize = new Size(ClientSize.Width, S(logicalH));

        /// 从右往左摆一个按钮（调用顺序 = 屏幕上从右往左）
        internal FlatButton AddBtn(string text, bool primary = false, bool danger = false)
        {
            var b = new FlatButton
            {
                Text = text,
                Backdrop = UiTheme.Window,
                Radius = 8,
                Font = UiTheme.Ui(9f),
            };
            b.Height = S(30);
            if (primary) { b.FillColor = UiTheme.Brand; b.HoverColor = UiTheme.BrandHover; b.TextColor = UiTheme.BrandOn; }
            else if (danger)
            {
                b.FillColor = UiTheme.Input; b.TextColor = UiTheme.Danger;
                b.BorderColor = Color.FromArgb(0x4A, 0x28, 0x28); b.HoverColor = Color.FromArgb(0x40, 0x22, 0x22);
            }
            else { b.FillColor = UiTheme.Input; b.HoverColor = UiTheme.Border; b.TextColor = UiTheme.Text; b.BorderColor = UiTheme.Border; }
            int w = Math.Max(S(66), TextRenderer.MeasureText(text, b.Font, new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPadding).Width + S(26));
            b.SetBounds(_right - w, (Foot.Height - b.Height) / 2, w, b.Height);
            _right -= w + S(8);
            Foot.Controls.Add(b);
            return b;
        }

        internal void CloseWith(DialogResult r) { DialogResult = r; Close(); }
        internal void OnEnter(Action a) => _enter = a;
        internal void OnEsc(Action a) => _esc = a;

        // 出图台模式（`MultiLauncher.exe --uiprobe dialogs <目录>`）：ShowDialog 不阻塞，改成
        // 「显示 → 让布局与绘制跑完 → PrintWindow 拍整窗（含深色标题栏）→ 关掉」。
        // 这样拍出来的是**真组件**，不用为了截图把每个弹窗再拆一遍构造。
        internal static bool Preview;
        internal static string PreviewDir = "";
        internal static int PreviewSeq;

        public new DialogResult ShowDialog(IWin32Window owner)
        {
            Dialogs.AttemptsForTest++;               // 证人位：这一击真走到了"要给人看一框"（不是恒 0 的空判据）
            Dialogs.LastTitleForTest = Text;
            // DEC-159（DEC-127⑥ 那一族的最后一道）：自检期间一律不真弹模态框。
            // `ShowDialog` 会把无头跑**挂在那里等人点**（上一轮红侧写成"恒弹"就挂了 15 分钟），而真弹出来他坐在旁边也看得见；
            // 出图台**不开**这道闸（那八张拍的就是真弹窗的样子），预览档 `Preview` 也不受它影响（显示→拍图→关闭，本来就不阻塞）。
            if (!Preview && Dialogs.DisabledForTest) return DialogResult.Cancel;
            if (!Preview) { Dialogs.ShownForTest++; return base.ShowDialog(owner); }
            StartPosition = FormStartPosition.Manual;
            Location = PreviewOffscreen ? new Point(-32000, -32000) : new Point(80, 60);
            Show(owner);
            for (int i = 0; i < 8; i++) { Application.DoEvents(); Thread.Sleep(50); }     // 等 DWM 把非客户区也画好
            string safe = new string(Text.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_').ToArray());
            string leaf = $"dlg-{++PreviewSeq:D2}-{(safe.Length > 22 ? safe[..22] : safe)}";
            UiPreviewForm.CaptureWindow(this, Path.Combine(PreviewDir, leaf + ".png"));
            // BUG-073（第 36 轮查明）：自检那条"弹窗族没有白底"改吃**客户区那一张**（DrawToBitmap = 应用自己画的，
            // 与摆没摆在屏幕上无关）。出图台照旧只留全窗图（那是要给人看标题栏深浅的），所以这一张只在自检开关开着时多写一份。
            if (PreviewClientShot)
                PreviewClientOk = UiPreviewForm.CaptureClient(this, Path.Combine(PreviewDir, leaf + "-客户区.png"));
            Close();
            for (int i = 0; i < 4; i++) { Application.DoEvents(); Thread.Sleep(30); }
            return DialogResult.Cancel;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Enter && _enter is not null) { _enter(); return true; }
            if (keyData == Keys.Escape && _esc is not null) { _esc(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    // 图标 + 正文（+ 可选灰色补充行），返回内容底边 y（用来定窗高）
    static int AddBodyText(Shell s, string text, string? detail, string glyph, Color glyphColor)
    {
        int left = string.IsNullOrEmpty(glyph) ? Shell.S(16) : Shell.S(44);
        if (!string.IsNullOrEmpty(glyph))
            s.Body.Controls.Add(new Label
            {
                Text = glyph, AutoSize = true, Font = UiTheme.Ui(11f, FontStyle.Bold),
                ForeColor = glyphColor, BackColor = UiTheme.Window, Location = new Point(Shell.S(16), Shell.S(16)),
            });
        var f = UiTheme.Ui(9f);
        int w = s.ClientSize.Width - left - Shell.S(16);
        var t = new Label
        {
            Text = text, AutoSize = true, MaximumSize = new Size(w, 0),
            ForeColor = UiTheme.Text, BackColor = UiTheme.Window, Font = f, Location = new Point(left, Shell.S(17)),
        };
        s.Body.Controls.Add(t);
        int bottom = t.Bottom;
        if (!string.IsNullOrEmpty(detail))
        {
            var d = new Label
            {
                Text = detail, AutoSize = true, MaximumSize = new Size(w, 0),
                ForeColor = UiTheme.TextMuted, BackColor = UiTheme.Window,
                Font = UiTheme.Ui(8.5f), Location = new Point(left, t.Bottom + Shell.S(7)),
            };
            s.Body.Controls.Add(d);
            bottom = d.Bottom;
        }
        return bottom;
    }

    // 信息框（2026-10-03 反馈③）：替掉全部系统 MessageBox.Show 的"只看一眼"那类。
    // danger=true → 红叉 + 红字图标（端口冲突、无法启动、升级没完成）。
    public static DialogResult Message(Form owner, string title, string text, string? detail = null, bool danger = false)
    {
        var s = new Shell(owner, title, 360);
        int bottom = AddBodyText(s, text, detail, danger ? "✕" : "i", danger ? UiTheme.Danger : Color.SteelBlue);
        s.AddBtn("知道了", primary: true).Click += (_, _) => s.CloseWith(DialogResult.OK);
        s.OnEnter(() => s.CloseWith(DialogResult.OK));
        s.OnEsc(() => s.CloseWith(DialogResult.Cancel));
        s.ClientSize = new Size(s.ClientSize.Width, bottom + Shell.S(18) + s.Foot.Height);   // 正文底 + 净空 + 按钮带
        var r = s.ShowDialog(owner);
        s.Dispose();
        return r;
    }
    static Shell BuildUpdateDetail(Form owner, string tag, string localVersion, string notes)
    {
        var s = new Shell(owner, $"更新内容 · 远端 {tag}（本地 {localVersion}）", 660, sizable: true);
        var lbl = new Label
        {
            Text = $"远端 {tag} 相对本地 {localVersion} 的全部更新说明：", AutoSize = true,
            ForeColor = UiTheme.Text, BackColor = UiTheme.Window,
            Font = UiTheme.Ui(9f), Location = new Point(Shell.S(16), Shell.S(14)),
        };
        s.Body.Controls.Add(lbl);
        // 渲染与滚动都在 MdScroll 里（DEC-050/051）：容器与窗体同底色（不再是一块深色"卡片"），
        // 细滑条内缩在圆角里、**完全不用原生滚动条**（所以拖动时不会掉圆角残渣），
        // 滚轮按光标位置派给本区域，且只在所属窗是前台窗时才消化（不再被后面的设置页抢走）。
        var box = new MdScroll { Backdrop = UiTheme.Window };
        box.Doc.CenterLines = PreviewCenterLines;      // 出图对照用（DEC-051）：默认居中，可切顶对齐
        box.SetBounds(Shell.S(16), lbl.Bottom + Shell.S(8), s.ContentWidth, Shell.S(340));
        s.Body.Controls.Add(box);
        box.SetMarkdown(notes);
        // 可拖窗：内容高度跟着窗体走（不接这根线，窗体一拖大内容下面露白、拖小把内容裁掉）。
        // MdScroll 里的 MdDoc 是 Dock.Fill、OnResize 会整篇重排 —— **拖宽时表格列宽会重算**，正好接上"表格太窄"那条。
        s.BodyResized += () =>
        {
            box.Width = s.ContentWidth;
            box.Height = Math.Max(Shell.S(200), s.BodyHeight - box.Top - Shell.S(10));
        };
        // 出图对照用（DEC-092）：拍一张"选中"的样子 —— 必须在 SetMarkdown 之后（纯文本随排版才有）。
        // DEC-097②：跨度要**跨行**（原来只选 46 个字 ≈ 第一行，正好把"底色只画第一行"那个 bug 照没了）。
        if (PreviewSelects) box.Doc.SetSelectionForTest(0, Math.Min(box.Doc.Plain.Length, 260));
        s.AddBtn("关闭").Click += (_, _) => s.CloseWith(DialogResult.Cancel);
        s.AddBtn("跳过此版本").Click += (_, _) => s.CloseWith(DialogResult.Ignore);
        s.AddBtn("立即升级", primary: true).Click += (_, _) => s.CloseWith(DialogResult.Yes);
        s.OnEnter(() => s.CloseWith(DialogResult.Cancel));      // 回车 = 关闭：不误触发一次下载 + 覆盖升级
        s.OnEsc(() => s.CloseWith(DialogResult.Cancel));
        s.ClientSize = new Size(s.ClientSize.Width, box.Bottom + Shell.S(16) + s.Foot.Height);
        return s;
    }

    // 更新内容页：返回 Yes = 立即升级，Ignore = 跳过此版本，其它 = 关闭。
    // 标题与首行都写明「远端 vX（本地 vY）」——这份说明是**两者之间**的全部发布内容，不是本地那一版的
    // （2026-10-03 反馈①：所以入口独立成按钮，不在版本号上挂链接，免得读成"这就是当前版本更新的内容"）。
    public static DialogResult UpdateDetailDialog(Form owner, string tag, string localVersion, string notes)
    {
        var s = BuildUpdateDetail(owner, tag, localVersion,
            (notes.Length > 0 ? notes : "（这次发布没有写更新说明）").Replace("\n", Environment.NewLine));
        var r = s.ShowDialog(owner);
        s.Dispose();
        return r;
    }

    // 出图/自检用：把「更新内容」那个弹窗搭起来但**不 ShowDialog**，交回壳让调用方在里面找 MdScroll。
    // 为什么开这个口：DEC-092 那条选中只在裸 MdDoc 上验过，鼠标路径与真实宿主（Shell + Body + 固定高 340 的
    // MdScroll）没一起验过 —— 用户实跑报"只能选中第一行"，得能在自检里复现整条链。
    internal static Form UpdateDetailShellForTest(Form owner, string tag, string localVersion, string notes) =>
        BuildUpdateDetail(owner, tag, localVersion, (notes ?? "").Replace("\n", Environment.NewLine));

}
