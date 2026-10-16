namespace MultiLauncher;

// 出图台的分支实现（每支自成一格，按 args[1] 派发；原先都内联在 RunBody 里）。
// 2026-10-15 从 UiPreviewForm.cs 按分支整段搬出，块内一行未改。
internal sealed partial class UiPreviewForm : Form
{
    static void ProbeToastAndModelBadge(string[] args)
    {
        if (args.Length >= 3 && args[1] == "toast")
        {
            Directory.CreateDirectory(args[2]);
            using var owner = new Form
            {
                Text = "出图台 · 提示落点（宿主窗）",
                FormBorderStyle = FormBorderStyle.Sizable,
                StartPosition = FormStartPosition.Manual,
                Location = new Point(120, 90),
                Size = new Size(1100, 720),
                BackColor = UiTheme.Window,
            };
            owner.CreateControl();
            UiTheme.CaptureDpi(owner.Handle);
            UiDark.ApplyDialog(owner);
            owner.Show();
            for (int i = 0; i < 8; i++) { Application.DoEvents(); Thread.Sleep(50); }
            Toast.Show(owner, "设置已保存", Toast.Kind.Ok);
            for (int i = 0; i < 4; i++) { Application.DoEvents(); Thread.Sleep(50); }
            Toast.Show(owner, "已选择目录，点「保存」才生效", Toast.Kind.Info);
            for (int i = 0; i < 4; i++) { Application.DoEvents(); Thread.Sleep(50); }
            Toast.Show(owner, "选出来的路径是空的，这一行没改动", Toast.Kind.Warn);
            for (int i = 0; i < 4; i++) { Application.DoEvents(); Thread.Sleep(50); }
            Toast.Show(owner, "下载校验没通过，包已删除：本机一点没动（sha256 不符：探到 sha256:1f43…4de3，实际 8a71…c0fe）", Toast.Kind.Error);
            for (int i = 0; i < 6; i++) { Application.DoEvents(); Thread.Sleep(50); }
            var live = Toast.LiveForTest();
            Console.WriteLine("toast: 活着 " + live.Count + " 条");
            for (int i = 0; i < live.Count; i++)
            {
                // 文案里有「：」与「/」这类字面，直接当文件名会在 NTFS 上开出交替数据流（ADS）
                var safe = string.Join("", live[i].MessageForTest.Split(Path.GetInvalidFileNameChars()));
                if (safe.Length > 24) safe = safe[..24];
                CaptureWindow(live[i], Path.Combine(args[2], $"toast-{i + 1}-{safe}.png"));
            }
            // 场景图：PrintWindow 拍不到叠在上面的独立窗，所以这一块用屏幕取图（只取宿主窗那一片）
            try
            {
                var area = Rectangle.Intersect(owner.Bounds, Screen.FromControl(owner).WorkingArea);
                using var scene = new System.Drawing.Bitmap(area.Width, area.Height);
                using (var g = Graphics.FromImage(scene)) g.CopyFromScreen(area.Location, Point.Empty, area.Size);
                scene.Save(Path.Combine(args[2], "toast-场景-窗口中下居中.png"), System.Drawing.Imaging.ImageFormat.Png);
            }
            catch (Exception ex) { Console.WriteLine("toast: 场景图拍不了 " + ex.Message); }
            // 动画证据（2026-10-04 DEC-075 变体 B）：按时间采 出现(180ms)/停留(2.5s)/消失(200ms) 三段的
            // Opacity 与 Top；再抓一张"滑入中途"的屏幕取图——PrintWindow 拍"内容"看不出常量透明度的淡化，
            // 位置与合成真相只在屏幕取图上（采样值就是自检断言的那两条纯函数曲线的运行时凭证）。
            Toast.Show(owner, "动画证据 · 变体 B", Toast.Kind.Ok);
            var watch = Toast.LiveForTest().LastOrDefault();
            if (watch is not null)
            {
                int t0 = Environment.TickCount;
                for (int i = 0; i < 330 && !watch.IsDisposed; i++)
                {
                    Application.DoEvents(); Thread.Sleep(10);
                    if (i % 8 != 0) continue;
                    Console.WriteLine($"  anim {Environment.TickCount - t0,4}ms op={watch.Opacity:0.00} top={watch.Top}");
                }
            }
            // 第二条专拍滑入中途：4ms 细步进，el 落在 40~130ms（op 约 0.4~0.8 那段）就抓屏
            Toast.Show(owner, "动画证据 · 滑入中途", Toast.Kind.Info);
            var watch2 = Toast.LiveForTest().LastOrDefault();
            if (watch2 is not null)
            {
                int t1 = Environment.TickCount;
                bool shot = false;
                for (int i = 0; i < 120 && !watch2.IsDisposed; i++)
                {
                    Application.DoEvents(); Thread.Sleep(4);
                    if (shot || i % 4 != 0) continue;
                    int el = Environment.TickCount - t1;
                    if (el < 40 || el > 130) continue;
                    shot = true;
                    Console.WriteLine($"  anim-mid {el}ms op={watch2.Opacity:0.00} top={watch2.Top}");
                    try
                    {
                        var r = Rectangle.Intersect(Rectangle.Inflate(watch2.Bounds, 30, 30), Screen.FromControl(owner).WorkingArea);
                        using var bmp = new System.Drawing.Bitmap(Math.Max(1, r.Width), Math.Max(1, r.Height));
                        using (var g = System.Drawing.Graphics.FromImage(bmp)) g.CopyFromScreen(r.Location, Point.Empty, r.Size);
                        bmp.Save(Path.Combine(args[2], "toast-05-滑入中途-屏幕取图.png"), System.Drawing.Imaging.ImageFormat.Png);
                    }
                    catch { /* 屏拍失败不影响采样输出 */ }
                }
            }
            owner.Close();
            Console.WriteLine("toast: " + ShotCount(args[2], (live.Count + 1) + "（尝试数）") +
                              "  (DPI=" + UiTheme.Dpi + ")");        // DEC-138（E-12）
            return;
        }

        // 模型下拉的「已加载」标识出图（DEC-065）：MultiLauncher.exe --uiprobe modelbadge <目录>
        // 真 DarkSelect（收起框与展开列表各拍一张，再拼一张对照表），假数据用本机真实的模型名与估算占用。
        if (args.Length >= 3 && args[1] == "modelbadge")
        {
            Directory.CreateDirectory(args[2]);
            using var host = new Form
            {
                Text = "出图台 · 模型下拉的已加载标识",
                FormBorderStyle = FormBorderStyle.FixedToolWindow,
                StartPosition = FormStartPosition.Manual,
                Location = new Point(20, 20),
                BackColor = UiTheme.Card,
                ClientSize = new Size(1200, 120),
            };
            host.CreateControl();
            UiTheme.CaptureDpi(host.Handle);
            UiDark.ApplyDialog(host);       // 出图台也走深色档，免得图里顶一条系统浅色标题栏看着像坏了
            var box = new DarkSelect { Backdrop = UiTheme.Card, Font = new Font("Microsoft YaHei UI", 9f) };
            box.SetBounds(UiTheme.S(host, 12), UiTheme.S(host, 12), UiTheme.S(host, 560), UiTheme.S(host, 30));
            host.Controls.Add(box);
            host.Show();
            void Pump() { for (int i = 0; i < 6; i++) { Application.DoEvents(); Thread.Sleep(45); } }
            Pump();
            var rows = new List<(string Name, System.Drawing.Bitmap[] Cells)>();
            void Case(string label, (string Text, string Right, string Badge)[] items, int selIdx)
            {
                box.SetItems(items.Select(it => new DarkItem { Text = it.Text, Right = it.Right, Badge = it.Badge, Tag = it.Text }));
                box.SelectByTag(items[selIdx].Text);
                Pump();
                var collapsed = Path.Combine(args[2], "modelbadge-" + label + "-收起框.png");
                CaptureWindow(host, collapsed);
                box.OpenDropDownForTest();
                Pump();
                var expanded = Path.Combine(args[2], "modelbadge-" + label + "-展开列表.png");
                if (box.DropForTest is { } p) CaptureWindow(p, expanded);
                box.CloseDropDownForTest();
                Pump();
                rows.Add((label switch
                {
                    "01-Strata-一枚已加载" => "Strata 页 · 跑着的这一枚带绿色「已加载」，另一枚不带",
                    "02-llama-两枚已加载" => "llama 页 · models-max>1 时可以同时标好几枚",
                    "03-都没加载" => "对照 · 什么都没加载时一枚都不标（不许假亮）",
                    _ => label,
                }, new[] { new System.Drawing.Bitmap(collapsed), new System.Drawing.Bitmap(expanded) }));
            }
            Case("01-Strata-一枚已加载", new[]
            {
                ("iq3_s  (qwen3.8-flash-next-iq3_s)", "~85.4G", MainForm.LoadedBadgeText),
                ("swift-iq3_xxs  (swift-1.5-iq3_xxs)", "~83.8G", ""),
            }, 0);
            Case("02-llama-两枚已加载", new[]
            {
                ("Qwen3.8-27B-GSQ-IQ3_S", "~87.0G", MainForm.LoadedBadgeText),
                ("OvisOCR2-BF16", "~0.8G", MainForm.LoadedBadgeText),
                ("Qwen3.6-35B-A3B-UD-Q6_K_XL  [未就绪]", "~22.6G", ""),
            }, 0);
            Case("03-都没加载", new[]
            {
                ("iq3_s  (qwen3.8-flash-next-iq3_s)", "~85.4G", ""),
                ("swift-iq3_xxs  (swift-1.5-iq3_xxs)", "~83.8G", ""),
            }, 0);
            ComposeSheet(Path.Combine(args[2], "modelbadge-对照表.png"), rows.ToArray(),
                new[] { "收起框（选中项也带标识）", "展开列表（绿色 = 已加载，灰色 = 估算占用）" });
            host.Close();
            Console.WriteLine("modelbadge: " + ShotCount(args[2], (rows.Count * 2 + 1) + "（尝试数）") +
                              "  (DPI=" + UiTheme.Dpi + ")");        // DEC-138（E-12）：屏幕取图那几张拍不出来时这里会少，话术不许顶上
            return;
        }

        // 更新窗出图（DEC-058）：MultiLauncher.exe --uiprobe update <目录>
        // 一模块一窗；每个态一张整窗图（不建主窗界面，只借 MainForm 的数据通道造态）。
    }

    // 动效出图（2026-10-16，方案文档 §7.5）：MultiLauncher.exe --uiprobe wipe <目录>
    // 悬停擦入与胶囊滑动都是"随光标播一下"的东西，静态截图本来就证明不了 —— 这里把进度**钉死**拍三帧
    //（常态 / 擦到一半 / 终态）拼一张对照表给人横比。走真 FlatButton 真 SegmentedControl、真令牌，不是画的示意图。
    // 三行的配色逐字取自调用点：甲 = 卡3 动作按钮加载档，乙 = 同一枚的卸载档，丙 = 页头那两枚次要按钮，
    // 丁 = 页头模块分段（第 0 轮那条口径：页签项也从 ModuleRegistry 来，别在这里写死）。
    static void ProbeWipe(string[] args)
    {
        if (args.Length < 3 || args[1] != "wipe") return;
        Directory.CreateDirectory(args[2]);
        using var host = new Form
        {
            Text = "出图台 · 悬停擦入与胶囊滑动",
            FormBorderStyle = FormBorderStyle.FixedToolWindow,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(20, 20),
            BackColor = UiTheme.Window,
            ClientSize = new Size(980, 640),   // 五行（甲乙丙丁戊）× 150% 下的行距，别把最后一行挤出画布
        };
        host.CreateControl();
        UiTheme.CaptureDpi(host.Handle);
        UiDark.ApplyDialog(host);

        int RowH = UiTheme.S(host, 30);
        var fUi = UiTheme.Ui(9f); var fSmall = UiTheme.Ui(8.5f);
        FlatButton Mk(string text, Color fill, Color hover, Color ink, Color? border, Font font)
        {
            var b = new FlatButton
            {
                Text = text, Backdrop = UiTheme.Window, FillColor = fill, HoverColor = hover, TextColor = ink,
                Font = font, Height = RowH,
                Width = TextRenderer.MeasureText(text, font).Width + UiTheme.S(host, border is null ? 20 : 18),
            };
            if (border is not null) b.BorderColor = border.Value;
            host.Controls.Add(b);
            return b;
        }
        var jia = Mk("加载", UiTheme.Brand, UiTheme.BrandHover, UiTheme.BrandOn, null, fUi);          // 卡3 · 加载档
        var yi = Mk("卸载", UiTheme.Danger, UiTheme.DangerHover, Color.White, null, fUi);             // 卡3 · 卸载档
        var bing = Mk("打开配置", UiTheme.Input, UiTheme.Border, UiTheme.Text, UiTheme.Border, fSmall); // 页头 · 次要按钮
        var seg = new SegmentedControl { Backdrop = UiTheme.Window, Font = UiTheme.Ui(9.5f) };
        seg.ApplyDpiHeight();
        seg.SetItems(ModuleRegistry.All.Select(m => m.DisplayName).ToArray());
        seg.ActiveIndex = seg.ItemsForTest.Length - 1;      // 程序化推值 = 落位（滑动只跟用户那一击）
        host.Controls.Add(seg);
        int x = UiTheme.S(host, 12), y = UiTheme.S(host, 12), dy = UiTheme.S(host, 46);
        jia.Location = new Point(x, y); yi.Location = new Point(x, y + dy); bing.Location = new Point(x, y + 2 * dy);
        seg.Location = new Point(x, y + 3 * dy);
        var knob = new ToggleSwitch { Backdrop = UiTheme.Window };   // 戊：滑块行程（配色/尺寸全走控件默认 = 产品那一档）
        host.Controls.Add(knob);
        knob.Location = new Point(x, y + 4 * dy);
        host.Show();
        void Pump() { for (int i = 0; i < 6; i++) { Application.DoEvents(); Thread.Sleep(45); } }
        Pump();

        var rows = new List<(string Name, System.Drawing.Bitmap[] Cells)>();
        // 把某一帧的四个控件各裁出来放大 2 倍。用 Grab（PrintWindow 全窗）而不是 GrabClient：
        // 后者那句 DrawToBitmap(bmp, ClientRectangle) 会把**整窗**（含标题栏）塞进客户区尺寸的画布，
        // 客户区原点跟着上移一条标题栏 —— 第一版照客户区坐标裁，四格全错位、还裁进标题文字（实测踩过）。
        // 换算与 CropZoom 同一条：客户区 → 窗体坐标要加 (origin − Location)。近邻插值，擦头那条硬边不许糊。
        System.Drawing.Bitmap Cell(Control c)
        {
            using var full = Grab(host);
            var origin = host.PointToScreen(Point.Empty);
            int dx = origin.X - host.Left, dy = origin.Y - host.Top;
            var src = Rectangle.Intersect(
                new Rectangle(c.Left + dx - 3, c.Top + dy - 3, c.Width + 6, c.Height + 6),
                new Rectangle(0, 0, full.Width, full.Height));
            if (src.Width <= 0 || src.Height <= 0) throw new InvalidOperationException("控件矩形不在窗体内，裁不出来：" + c);
            using var crop = (System.Drawing.Bitmap)full.Clone(src, full.PixelFormat);
            var big = new System.Drawing.Bitmap(crop.Width * 2, crop.Height * 2);
            using (var g = Graphics.FromImage(big))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                g.DrawImage(crop, new Rectangle(0, 0, big.Width, big.Height));
            }
            return big;
        }
        void Frame(string label, double k)
        {
            foreach (var b in new[] { jia, yi, bing }) { b.WipeKForTest = k; b.Invalidate(); }
            seg.SetPillPreviewForTest(0, k);
            knob.Checked = k > 0.5;                 // 落位（程序化那条不播）
            knob.KnobTForTest = k;                  // 再钉住行程进度拍这一帧
            Pump();
            rows.Add((label, new[] { Cell(jia), Cell(yi), Cell(bing), Cell(seg), Cell(knob) }));
        }
        Frame("常态 k=0", 0);
        Frame("一半 k=0.5", 0.5);
        Frame("终态 k=1", 1);
        ComposeSheet(Path.Combine(args[2], "wipe-对照表.png"), rows.ToArray(),
            new[] { "甲 卡3 加载 Δ28", "乙 卡3 卸载 Δ27.6", "丙 页头次要 Δ9", "丁 分段胶囊", "戊 开关滑块" });
        CaptureClient(host, Path.Combine(args[2], "wipe-整窗.png"));

        // 叠放那一族两枚（他 2026-10-16 点名"win/wsl 与 路由模式/单体模型 没适配"）：单独一张表 ——
        // 模式条是通栏宽，塞进上面那张会把所有列撑到 600 像素宽，横比就没法看了。
        var sq = new StackedToggle { Band = false, Faces = new[] { "WIN", "WSL" }, Backdrop = UiTheme.Card };
        sq.SetBounds(UiTheme.S(host, 12), y + 5 * dy, UiTheme.S(host, StackedToggle.CardLogicalW), UiTheme.S(host, StackedToggle.CardLogicalH));
        var band = new StackedToggle { Band = true, Faces = new[] { "路由模式", "单体模型" }, Backdrop = UiTheme.Window };
        band.SetBounds(UiTheme.S(host, 12), y + 7 * dy, UiTheme.S(host, 200),      // 方块高 S(62) > 行距 S(46)，
                       UiTheme.S(host, StackedToggle.BandFaceLogicalH + StackedToggle.PeekLogical));  // 所以模式条要空一行，否则两枚压叠
        host.Controls.Add(sq); host.Controls.Add(band);
        Pump();
        var rows2 = new List<(string Name, System.Drawing.Bitmap[] Cells)>();
        void Frame2(double k)
        {
            sq.WipeKForTest = k; band.WipeKForTest = k;
            Pump();
            rows2.Add(("k = " + k.ToString("0.#"), new[] { Cell(sq), Cell(band) }));
        }
        Frame2(0); Frame2(0.5); Frame2(1);
        ComposeSheet(Path.Combine(args[2], "stacked-对照表.png"), rows2.ToArray(),
            new[] { "己 卡1 后端方块（Band=false）", "庚 左栏模式条（Band=true，通栏）" });
        host.Close();
        Console.WriteLine("wipe: " + ShotCount(args[2], "3（尝试数）") + "  (DPI=" + UiTheme.Dpi + ")");
    }

    static void ProbeUpdateAndSidebar(string[] args)
    {
        if (args.Length >= 3 && args[1] == "update")
        {
            Directory.CreateDirectory(args[2]);
            using var main = new MainForm();
            main.ClearStartHiddenForTest();   // DEC-107：探针实例不吃他配置里的「启动时隐藏到托盘」
            main.CreateControl();
            UiTheme.CaptureDpi(main.Handle);
            int n = 0;
            void Shoot(UpdateForm f, string name)
            {
                for (int i = 0; i < 6; i++) { Application.DoEvents(); Thread.Sleep(45); }
                CaptureWindow(f, Path.Combine(args[2], name));
                n++;
            }
            using var uf = new UpdateForm(main, "strata");
            uf.Show();
            Shoot(uf, "update-01-没查过.png");
            main.SetUpdateBusyForTest(true);
            Shoot(uf, "update-02-检查中.png");
            main.SetUpdateBusyForTest(false);
            main.SetUpdateMemoForTest("已是最新 · 远端 v0.1.38 · 发布 2026-09-28", false);
            Shoot(uf, "update-03-已是最新.png");
            main.SetUpdateMemoForTest("检查失败：网络不可达（GitHub API 没连上）", true);
            Shoot(uf, "update-04-检查失败.png");
            main.SetUpdateOfferForTest("v0.1.36", "2026-10-03T09:05:28Z", skipped: false);
            Shoot(uf, "update-05-发现新版本.png");
            main.SetUpdateOfferForTest("v0.1.36", "2026-10-03T09:05:28Z", skipped: true);
            Shoot(uf, "update-06-被跳过那版.png");
            main.SetUpdateOfferForTest("v0.1.36", "2026-10-03T09:05:28Z", skipped: false);
            main.SetUpdateBusyForTest(true);
            main.SetUpdateProgressForTest("下载源码包", 0.42);
            Shoot(uf, "update-07-升级中下载.png");
            uf.SetCancelHoverForTest(true);
            Shoot(uf, "update-08-小X悬停态.png");
            uf.SetCancelHoverForTest(false);
            main.SetUpdateProgressForTest("同步官方预编译引擎", -1);
            Shoot(uf, "update-09-同步引擎不给停.png");
            main.SetUpdateBusyForTest(false);
            main.ClearUpdateStateForTest();
            // llama 那一窗：同一套摆位，版本行不带目录与后缀
            main.PointLlamaDirAtNewestSiblingForPreview();     // 不然版本行拍到"（目录不存在）"那种假象
            using var lf = new UpdateForm(main, "llama");
            lf.Show();
            main.SetLlamaOfferForTest("b11371", "2026-10-03T02:35:41Z", skipped: false);
            Shoot(lf, "update-10-llama-发现新版本.png");
            main.ClearLlamaOfferForTest();
            Shoot(lf, "update-11-llama-没查过.png");
            // DEC-096/107：Ninfer 现在**一后端一窗** —— WIN 走三行窗（主段标题「WIN 后端」、没有第二段了），
            // WSL 走自己的小窗（一行版本 + 一枚「更新…」+ 忙时阶段行）。两扇都拍。
            using var nf = new UpdateForm(main, "ninfer");
            nf.Show();
            main.SetNinferOfferForTest("engine-v0.12.0-x", "2026-10-05T01:00:00Z", skipped: false);
            Shoot(nf, "update-12-ninfer-WIN后端-发现新版本.png");
            main.ClearNinferOfferForTest();
            Shoot(nf, "update-13-ninfer-WIN后端-没查过.png");
            using var wslf = new UpdateForm(main, "ninfer", "wsl");
            wslf.Show();
            Shoot(wslf, "update-14-ninfer-WSL窗-没查过.png");
            // DEC-106/107：WSL 那扇的忙态（按钮字 =「重编中…」+ 置灰；阶段行报"在忙哪条链"）
            main.SetNinferWslBusyForTest("重编中…", "重编 WSL 引擎（VM 里 cmake，可能要几分钟）…");
            main.SetNinferBusyForTest(true);
            Shoot(wslf, "update-15-ninfer-WSL重编中.png");
            main.SetNinferBusyForTest(false);
            main.SetNinferWslBusyForTest("", "");
            // DEC-110③：WSL 那扇闲下来的**结果回显**（用户："wsl更新弹窗也应该显示已是最新, 不能仅仅显示在日志区"）。
            // 忙态那张（15）拍的是进度条 + 阶段名；这一张拍同一条链闲下来之后同一行的另一种内容。
            main.SetNinferWslMemoForTest("已是最新", false);
            Shoot(wslf, "update-16-ninfer-WSL窗-已是最新.png");
            main.SetNinferWslMemoForTest("检查失败", true);
            Shoot(wslf, "update-17-ninfer-WSL窗-检查失败.png");
            main.SetNinferWslMemoForTest("", false);
            uf.Close(); lf.Close(); nf.Close(); wslf.Close();
            Console.WriteLine("update: " + ShotCount(args[2], n + "（尝试数）"));      // DEC-138（E-12）
            return;
        }

        // 主窗左栏出图（2026-10-04）：核对「运行状态」大行合并进「状态」键值行之后的观感。
        // MultiLauncher.exe --uiprobe sidebar <目录>
        // 真 MainForm + 工作区出厂默认（bin 目录没有 ini = 不自动起任何服务）；拍整窗 + 左栏裁切。
        // 与 dialogs/settings/update 模式同口径：不 Close()（Close 会触发 OnFormClosing → SaveConfig，
        // 在 bin 目录里凭空写出一份 MultiLauncher.ini 污染后续探针），只靠 using 的 Dispose 收掉窗口。
        if (args.Length >= 3 && args[1] == "sidebar")
        {
            Directory.CreateDirectory(args[2]);
            using var main = new MainForm();
            main.ClearStartHiddenForTest();   // DEC-107：探针实例不吃他配置里的「启动时隐藏到托盘」
            main.StartPosition = FormStartPosition.Manual;
            main.Location = new Point(60, 60);
            main.Show();
            void Pump() { for (int i = 0; i < 8; i++) { Application.DoEvents(); Thread.Sleep(50); } }
            Pump();
            // DEC-147（BUG-067 结案）：这道闸的落点要在探针控制台上看得见一次 —— 挂上 Load 必须 0，
            // 挡下的格数 > 0 才证明构造函数那条路真被走到（BUG-067 登记原话："加证人位并由探针控制台打一次"）。
            Console.WriteLine("sidebar:  自动启动这道闸挡下 " + MainForm.AutoStartSkippedForTest +
                              " 格、挂上 Load " + MainForm.AutoStartWiredForTest + " 格（挂上必须 0）");
            main.ToggleAdvancedForTest();      // 让「思考深度」这两个新格子进图（高级区默认是收起的）
            // 那一排「bin 目录没有 ini」的前提已经不成立了：现在 bin 里存的就是他那份真实配置（DEC-097
            // 抓到 BUG-038 靠的就是它），于是 ActiveModule 会跟着他的界面走 —— 上一版第 01 张标着 Strata
            // 其实拍的是 Ninfer 页（与第 13 张逐字节相同）。出图各组自己点名页面，别让首页决定后面叫什么。
            main.SwitchModuleForTest("strata");
            Pump();
            CaptureWindow(main, Path.Combine(args[2], "sidebar-01-整窗-高级展开-Strata.png"));
            CaptureCrop(main, main.SideColumnBoundsInClient(), Path.Combine(args[2], "sidebar-02-左栏裁切.png"), 1);
            // DEC-177（他实跑点名三条：折叠箭头不要底、灯与文字要同一中线、简称不能被切）：
            // 「模块状态」两态各拍一张左栏 —— 灯的几何要能在图上逐像素量出来（只靠自检断言量不到观感）。
            // 走 `SetLightsCollapsedForTest`：只改内存里的态，不落他的 ini。
            main.SetLightsCollapsedForTest(false); Pump();
            CaptureCrop(main, main.SideColumnBoundsInClient(), Path.Combine(args[2], "sidebar-02a-模块状态-展开.png"), 1);
            main.SetLightsCollapsedForTest(true); Pump();
            CaptureCrop(main, main.SideColumnBoundsInClient(), Path.Combine(args[2], "sidebar-02b-模块状态-折叠.png"), 1);
            main.SetLightsCollapsedForTest(false); Pump();
            CaptureCrop(main, main.AdvHostBoundsInClient(), Path.Combine(args[2], "sidebar-03-高级区-Strata.png"), 1);
            main.SwitchModuleForTest("llama");
            Pump();
            CaptureCrop(main, main.AdvHostBoundsInClient(), Path.Combine(args[2], "sidebar-04-高级区-llama.png"), 1);
            CaptureCrop(main, main.Card1RowBoundsInClient(), Path.Combine(args[2], "sidebar-05-卡1-llama页.png"), 1);
            CaptureCrop(main, main.CardParamsBoundsInClient(), Path.Combine(args[2], "sidebar-06-卡2-llama页.png"), 1);
            // 2026-10-04 DEC-076 候选（原型）：卡1 回归——监听地址｜监听端口｜思考深度｜保存；保存两态（灰/亮）
            main.SwitchModuleForTest("strata");
            Pump();
            CaptureCrop(main, main.Card1RowBoundsInClient(), Path.Combine(args[2], "sidebar-07-卡1-保存置灰.png"), 2);
            CaptureCrop(main, main.CardParamsBoundsInClient(), Path.Combine(args[2], "sidebar-08-卡2.png"), 1);
            main.SetContextForTest("245760");      // 手改一格 = 用户动手的场景 →「保存」亮起
            Pump();
            CaptureCrop(main, main.Card1RowBoundsInClient(), Path.Combine(args[2], "sidebar-09-卡1-保存点亮.png"), 2);
            main.MarkParamsSavedForTest();         // 收口收尾，别把"脏"带出去
            // DEC-078（用户第②③条）：展开态两张——弹列表要与输入框同宽同左边（屏幕取图，PrintWindow 拍不到叠窗）
            void DropShot(string which, string name)
            {
                main.OpenDropDownForTest(which); Pump();
                var pop = main.DropPopupForTest;
                var box = main.DropDownBoxBoundsScreenForTest();
                if (pop is { IsDisposed: false })
                {
                    var area = Rectangle.Union(box, pop.Bounds); area.Inflate(8, 8);
                    try
                    {
                        using var bmp = new System.Drawing.Bitmap(area.Width, area.Height);
                        using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(area.Location, Point.Empty, area.Size);
                        bmp.Save(Path.Combine(args[2], name), System.Drawing.Imaging.ImageFormat.Png);
                    }
                    catch (Exception ex) { Console.WriteLine("sidebar: 展开态拍不了 " + name + " " + ex.Message); }
                    Console.WriteLine($"sidebar: {name} 控件宽={box.Width} 列表宽={pop.Width} 左边对齐={(pop.Left == box.Left)}");
                }
                else Console.WriteLine("sidebar: 展开态没开出来 " + name + " ｜ " + main.DropProbeStateForTest());
                main.CloseDropDownForTest(); Pump();
            }
            main.SwitchModuleForTest("llama"); Pump();
            DropShot("reason", "sidebar-10-思考深度下拉展开-llama页.png");
            main.SwitchModuleForTest("strata"); Pump();
            DropShot("kv", "sidebar-11-KV精度下拉展开.png");        // Strata 页那只（int8/q4_0/k8v4）
            // DEC-079：llama 页的 KV 也换成组合下拉了（6 档 + 空白在首，一项写 cache-type-k / v 两键）→ 拍它的展开态
            main.SwitchModuleForTest("llama"); Pump();
            DropShot("kv", "sidebar-12-KV精度下拉展开-llama页.png");
            Console.WriteLine("sidebar:  llama 页 KV 当前显示 = " + main.LlamaKvDisplayForTest +
                              "；继承全局的键 = [" + main.LlamaInheritedListForTest + "]");
            // 量一次"这一格到底放得下最长那档吗"（出图被省略号截过，数字要能自己说话）
            var kvDiag = main.LlamaKvFitDiagForTest("q8_0 / q8_0");
            Console.WriteLine("sidebar:  KV 格实测 控件宽=" + kvDiag.width + " 文字可用=" + kvDiag.room +
                              " 需要=" + kvDiag.need + " 富余=" + (kvDiag.room - kvDiag.need) +
                              " 字体=" + kvDiag.font);
            // DEC-096 第 1 轮：Ninfer 页（第三个模块）——方块卡、两行四格（DEC-107：与另两页同形、打开网页位留空）、
            // 按后端换的高级区、模式条整条不出现、左栏多一行「版本 + 小更新按钮」
            main.SwitchModuleForTest("ninfer"); Pump();
            CaptureWindow(main, Path.Combine(args[2], "sidebar-13-整窗-Ninfer页-WIN后端.png"));
            CaptureCrop(main, main.SideColumnBoundsInClient(), Path.Combine(args[2], "sidebar-14-左栏-Ninfer版本行与小按钮.png"), 1);
            CaptureCrop(main, main.Card1RowBoundsInClient(), Path.Combine(args[2], "sidebar-15-卡1-Ninfer含后端方块.png"), 3);
            CaptureCrop(main, main.CardParamsBoundsInClient(), Path.Combine(args[2], "sidebar-16-卡2-Ninfer两行四格.png"), 1);
            CaptureCrop(main, main.AdvHostBoundsInClient(), Path.Combine(args[2], "sidebar-17-高级区-Ninfer-WIN.png"), 1);
            // DEC-107 第 6 条：版本行末尾那枚小按钮（圆环上箭头）——悬停态单独拍一张近景
            main.SetUpdateBtnHoverForTest(true); Pump();
            CaptureCrop(main, main.UpdateBtnBoundsInClient("版本"), Path.Combine(args[2], "sidebar-20-版本行小按钮悬停近景.png"), 4);
            main.SetUpdateBtnHoverForTest(false); Pump();
            // DEC-108：灰（默认）/绿（探到新版本）两态 —— 造一个 offer 走真广播，拍绿色态近景
            main.SetNinferOfferForTest("engine-v0.12.0-x", "2026-10-05T01:00:00Z", skipped: false); Pump();
            CaptureCrop(main, main.UpdateBtnBoundsInClient("版本"), Path.Combine(args[2], "sidebar-21-版本行小按钮-新版本绿色态.png"), 4);
            main.ClearNinferOfferForTest(); Pump();
            DropShot("kv", "sidebar-18-KV精度下拉展开-Ninfer页.png");
            DropShot("reason", "sidebar-19-思考深度下拉展开-Ninfer页.png");
            var be = main.BackendToggleGeometryForTest;
            var rfit = main.NinferReasonFitDiagForTest("minimal");
            var rfit2 = main.NinferReasonFitDiagForTest("medium");
            Console.WriteLine("sidebar:  Ninfer 思考深度当前显示 = [" + main.NinferReasonTextForTest + "]");
            Console.WriteLine("sidebar:  Ninfer 思考深度格实测 控件宽=" + rfit.width + " 文字可用=" + rfit.room +
                              " 需要(minimal)=" + rfit.need + " 需要(medium)=" + rfit2.need +
                              " 富余=" + (rfit.room - rfit.need) + " 字体=" + rfit.font);
            Console.WriteLine("sidebar:  Ninfer KV 格最长档 rk4v4-e8 富余=" + main.NinferKvSlackForTest("rk4v4-e8"));
            Console.WriteLine("sidebar:  Ninfer KV 格控件诊断 " + main.NinferKvBoxDiagForTest());
            Console.WriteLine("sidebar:  后端方块实测 控件=" + be.Item1 + "x" + be.Item2 +
                              " 面=" + be.Item3 + "x" + be.Item4 + " 露边=" + be.Item5 +
                              "（DEC-088/089：150% 下应为 117x93 / 69x45 / 约 8）；两张面 = " + main.BackendFaceForTest);
            var modeGeo = main.ModeToggleGeometryForTest;
            Console.WriteLine("sidebar:  左栏模式条（Strata/llama 那两页）面高=" + modeGeo.FaceH +
                              " 露边=" + modeGeo.Peek + " 控件高=" + modeGeo.ControlH + "（DEC-090：侧栏总高净增 0）");
            main.SwitchModuleForTest("strata"); Pump();
            Console.WriteLine("sidebar: " + ShotCount(args[2], "23（DEC-108 起 21 张 + DEC-177 模块状态两态各一张）") +
                              "  (DPI=" + UiTheme.Dpi + ")");        // DEC-138（E-12）：这句以前写死 19，与实际落的张数无关
            return;
        }
    }
}
