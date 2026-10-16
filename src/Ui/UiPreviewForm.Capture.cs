namespace MultiLauncher;

// 出图台的取景与像素判据：PrintWindow 全窗图、客户区 DrawToBitmap、裁切放大、合成图、
// 以及 BUG-073 那一套（客户区才是应用真画的、白底判据只在有效帧上量）。
// 2026-10-15 从 UiPreviewForm.cs 整段搬出，一行未改。
internal sealed partial class UiPreviewForm : Form
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

    /// 拍整窗（含非客户区）：nFlags=2 是 PW_RENDERFULLCONTENT，DWM 画的深色标题栏才会进来
    internal static void CaptureWindow(Form f, string path)
    {
        using var bmp = Grab(f);
        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    /// <summary>③（BUG-073）取景的三个数：白点 / 品牌绿 / 色数（每 2 像素取样，`skipTopBand` = 上面那一条不取样）。
    /// 画与判共用同一张图、同一个取样节拍，免得"看到的"和"判的"不是同一件事。</summary>
    internal static (int White, int Brand, int Colors, int Total) ShotStats(System.Drawing.Bitmap bmp, int skipTopBand)
    {
        int white = 0, brand = 0, total = 0;
        var seen = new HashSet<int>();
        for (int y = Math.Max(0, skipTopBand); y < bmp.Height; y += 2)
            for (int x = 0; x < bmp.Width; x += 2)
            {
                var c = bmp.GetPixel(x, y);
                total++;
                seen.Add((c.R << 16) | (c.G << 8) | c.B);
                if (c.R > 245 && c.G > 245 && c.B > 245) white++;
                if (Math.Abs(c.R - UiTheme.Brand.R) < 26 && Math.Abs(c.G - UiTheme.Brand.G) < 26 && Math.Abs(c.B - UiTheme.Brand.B) < 26) brand++;
            }
        return (white, brand, seen.Count, total);
    }

    /// <summary>③（BUG-073）**这一帧到底是什么**的判据（纯函数，自检拿合成图三面喂，不靠时机）：
    /// · `Unpainted` = 一枚品牌绿都没有 → 应用那一枚主按钮根本没被画出来 = **相机没拍到**，不是界面是白的。
    ///   （原来这条判据把这种帧报成"弹窗有白底"，于是每次拍到空帧都要人重新猜一次产物有没有事 —— 第 36 轮他 22:09 那次实跑就只红这一条。）
    /// · `WhiteBackdrop` = 拍到了内容，但成片白底（>0.2%）= 真的浅色/系统白底，这才是那条判据要抓的东西。
    /// · `DarkAndPainted` = 拍到内容且没有成片白底。</summary>
    internal static string ShotVerdict(int white, int brand, int total) =>
        total <= 0 ? "Unpainted"
        : brand <= 20 ? "Unpainted"
        : white * 1000 > total * 2 ? "WhiteBackdrop" : "DarkAndPainted";

    /// <summary>③（BUG-073）只画客户区那一张的取景手 = <see cref="Control.DrawToBitmap"/>（WM_PRINT，WinForms 自己把控件树画进位图，
    /// 与这个窗在不在屏幕上、DWM 合成过没有一帧**完全无关**）。为什么不用 `Grab`：那条走 `PrintWindow(..., PW_RENDERFULLCONTENT)`，
    /// 而 Form/Control 不实现 `WM_PRINTCLIENT` → 客户区实际吃的是 **DWM 那份重定向表面**；自检把预览窗摆到画外
    /// （DEC-138 缺口 11 起，为了不让他旁边看见一框框闪过）之后那种窗可能从没合成过客户区 —— 实测坏图色数从 3239 塌到 156、
    /// 客户区整片纯白（另一趟整张全黑 = `Grab` 自己 `Clear(Color.Black)` 留着），而那条"弹窗族没有白底"的判据把这读成了界面缺陷。
    /// 全窗那一张照旧留给人看（标题栏深浅仍归 `--uiprobe dialogs`），**判据改吃这一张**。</summary>
    internal static System.Drawing.Bitmap GrabClient(Form f)
    {
        var size = f.ClientSize;
        if (size.Width <= 0 || size.Height <= 0) throw new InvalidOperationException("客户区尺寸为 0，拍不出这一帧");
        var bmp = new System.Drawing.Bitmap(size.Width, size.Height);
        try { f.DrawToBitmap(bmp, new Rectangle(Point.Empty, size)); }
        catch { bmp.Dispose(); throw; }
        return bmp;
    }

    /// <summary>③（BUG-073）客户区那一张落盘；出没出图交回判据位（`false` = 取景失败，调用方要说"取景失败"而不是"有白底"）。</summary>
    internal static bool CaptureClient(Form f, string path)
    {
        try
        {
            using var bmp = GrabClient(f);
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            return true;
        }
        catch (Exception ex)
        {
            // 别把异常变成"判据没跑"，也别让它冒充"界面有白底" —— 把原因留下给判据说（自检 note 里直接看得见）
            LastCaptureErrorForTest = ex.GetType().Name + ": " + ex.Message;
            return false;
        }
    }

    /// <summary>③（BUG-073）上一张客户区图失败的原因（证人位：取景这一环不许静默）。</summary>
    internal static string LastCaptureErrorForTest = "";

    static System.Drawing.Bitmap Grab(Form f)
    {
        var bmp = new System.Drawing.Bitmap(Math.Max(1, f.Width), Math.Max(1, f.Height));
        using (var g = Graphics.FromImage(bmp)) g.Clear(Color.Black);
        using (var g = Graphics.FromImage(bmp))
        {
            IntPtr hdc = g.GetHdc();
            try { PrintWindow(f.Handle, hdc, 2); }
            finally { g.ReleaseHdc(hdc); }
        }
        return bmp;
    }

    // 把窗内某一块客户区裁出来放大 n 倍（小控件在全窗图里根本看不清，出图对照必须裁）
    internal static void CaptureCrop(Form f, Rectangle clientRect, string path, int scale)
    {
        using var big = CropZoom(f, clientRect, scale);
        if (big is not null) big.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    static System.Drawing.Bitmap? CropZoom(Form f, Rectangle clientRect, int scale)
    {
        using var full = Grab(f);
        var origin = f.PointToScreen(Point.Empty);
        int dx = origin.X - f.Left, dy = origin.Y - f.Top;
        var src = Rectangle.Intersect(
            new Rectangle(clientRect.X + dx, clientRect.Y + dy, clientRect.Width, clientRect.Height),
            new Rectangle(0, 0, full.Width, full.Height));
        if (src.Width <= 0 || src.Height <= 0) return null;
        using var crop = full.Clone(src, full.PixelFormat);
        var big = new System.Drawing.Bitmap(crop.Width * scale, crop.Height * scale);
        using (var g = Graphics.FromImage(big))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            g.DrawImage(crop, new Rectangle(0, 0, big.Width, big.Height));
        }
        return big;
    }

    /// 把若干裁切图拼成一张对照表：每行一个候选，左边写名字，右边按 cols 排（用来横比同一个控件的几种画法）
    internal static void ComposeSheet(string path, (string Name, System.Drawing.Bitmap[] Cells)[] rows, string[] colHeads)
    {
        if (rows.Length == 0) return;
        int labelW = 300, gap = 14, headH = 44, rowGap = 18;
        int cellW = rows.Max(r => r.Cells.Max(c => c.Width));
        int cellH = rows.Max(r => r.Cells.Max(c => c.Height));
        int cols = rows.Max(r => r.Cells.Length);
        var size = new Size(labelW + cols * cellW + (cols + 1) * gap, headH + rows.Length * (cellH + rowGap));
        using var sheet = new System.Drawing.Bitmap(size.Width, size.Height);
        using (var g = Graphics.FromImage(sheet))
        {
            g.Clear(UiTheme.Window);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var font = new Font("Microsoft YaHei UI", 13f);
            var head = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold);
            for (int c = 0; c < cols && c < colHeads.Length; c++)
                TextRenderer.DrawText(g, colHeads[c], head,
                    new Point(labelW + gap + c * (cellW + gap), 12), UiTheme.Brand);
            int y = headH;
            foreach (var (name, cells) in rows)
            {
                TextRenderer.DrawText(g, name, font, new Rectangle(10, y + cellH / 2 - 14, labelW - 24, 30),
                    UiTheme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                for (int c = 0; c < cells.Length; c++)
                    g.DrawImageUnscaled(cells[c], labelW + gap + c * (cellW + gap), y);
                y += cellH + rowGap;
            }
            g.DrawRectangle(new Pen(UiTheme.Border), 0, headH - 1, size.Width - 1, size.Height - headH);
        }
        sheet.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        foreach (var r in rows) foreach (var c in r.Cells) c.Dispose();
    }

    static Form TitleBarProbe()
    {
        // STRATA_UI_TITLEBAR_DIALOG=1 → 走设置窗那条口径（只开暗色、不上自定义色）
        bool dialogStyle = Environment.GetEnvironmentVariable("STRATA_UI_TITLEBAR_DIALOG") == "1";
        var f = new Form
        {
            Text = dialogStyle ? "设置（标题栏口径探针 · 不上自定义色）" : MainForm.AppName + "  " + MainForm.VersionTag(),
            BackColor = UiTheme.Window,
            ForeColor = UiTheme.Text,
            Font = new Font("Microsoft YaHei UI", 9f),
            AutoScaleMode = AutoScaleMode.None,
            StartPosition = FormStartPosition.CenterScreen,
            FormBorderStyle = FormBorderStyle.Sizable,
            MinimizeBox = true,
        };
        f.CreateControl();
        UiTheme.CaptureDpi(f.Handle);
        f.ClientSize = new Size(UiTheme.S(f, 760), UiTheme.S(f, 190));
        f.Padding = new Padding(UiTheme.S(f, 16));
        var head = new Label
        {
            Text = "标题栏配色探针（STRATA_UI_TITLEBAR=" + (Environment.GetEnvironmentVariable("STRATA_UI_TITLEBAR") ?? "默认 flat")
                   + (dialogStyle ? " · 设置窗口径：不上自定义色" : " · 主窗口径") + "）",
            AutoSize = true,
            ForeColor = UiTheme.Text,
            BackColor = UiTheme.Window,
            Font = new Font("Microsoft YaHei UI", 12f),
            Location = new Point(UiTheme.S(f, 18), UiTheme.S(f, 18)),
        };
        var card = new RoundedPanel
        {
            Dock = DockStyle.Bottom,
            Height = UiTheme.S(f, 96),
            FillColor = UiTheme.Card,
            BorderColor = UiTheme.Border,
            Backdrop = UiTheme.Window,
        };
        // 顺带把本轮两个新画法摆进来：矢量齿轮 + 靠左文字（真实尺寸与配色，截图就能核）
        var gearBtn = new FlatButton
        {
            Text = "设置", ShowGear = true, Backdrop = UiTheme.Card,
            FillColor = UiTheme.Input, HoverColor = UiTheme.Border, BorderColor = UiTheme.Border,
            TextColor = UiTheme.Text, Font = new Font("Microsoft YaHei UI", 8.5f),
            Height = UiTheme.S(f, 30), Width = UiTheme.S(f, 86),
            Location = new Point(UiTheme.S(f, 208), UiTheme.S(f, 31)),
        };
        var navBtn = new FlatButton
        {
            Text = "更新 llama.cpp", TextLeft = true, Backdrop = UiTheme.Card,
            FillColor = UiTheme.Input, HoverColor = UiTheme.Border, BorderColor = UiTheme.Border,
            TextColor = UiTheme.Text, Font = new Font("Microsoft YaHei UI", 9f),
            Height = UiTheme.S(f, 36), Width = UiTheme.S(f, 178),
            Location = new Point(UiTheme.S(f, 18), UiTheme.S(f, 28)),
        };
        card.Controls.Add(navBtn);
        card.Controls.Add(gearBtn);
        // 左栏模式条（第五轮：状态显示 → 可点的切换按钮，右侧矢量 ⇄）
        var modeBtn = new FlatButton
        {
            Text = "路由模式", ShowSwap = true, FillColor = UiTheme.Brand, HoverColor = UiTheme.BrandHover,
            BorderColor = UiTheme.Brand, TextColor = UiTheme.BrandOn, Backdrop = UiTheme.Card, Radius = 8,
            Height = UiTheme.S(f, 32), Width = UiTheme.S(f, 150),
            Location = new Point(UiTheme.S(f, 306), UiTheme.S(f, 30)),
        };
        card.Controls.Add(modeBtn);
        f.Controls.Add(head);
        f.Controls.Add(card);
        // DEC-092：弹窗样式的 mock 走真弹窗那一条（ApplyDialog），别在出图台里另立一档标题色
        if (dialogStyle) UiDark.ApplyDialog(f); else UiDark.Apply(f);
        return f;
    }
}
