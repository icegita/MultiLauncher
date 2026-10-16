using System.Text;
using System.Text.Json.Nodes;

namespace MultiLauncher;

// 自检的静态helper家（2026-10-15 从 SelfTest.cs 整体迁出，纯搬家不改一行）：
// 取景判据、源码扫描、快照对照、报告收口、物化样本 —— 都是不依赖 RunBody 局部状态的静态成员。
internal static partial class SelfTest
{

    // ③（2026-10-03 反馈）弹窗族深色的断言法。**第 36 轮按 BUG-073 重做了一遍取景**：
    // 原来读的是 `PrintWindow` 那张全窗图，而自检把预览窗摆到画外（DEC-138 缺口 11 起）之后，
    // WinForms 不接 `WM_PRINTCLIENT` → 客户区实际吃 DWM 那份重定向表面 → 间歇拍到"客户区整片纯白 / 整张全黑"的空帧，
    // 于是那条判据把"相机没拍到"报成了"弹窗有白底"（实测同一构建九趟 5 绿 4 红；同一份代码摆屏上二十张全绿）。
    // 现在改吃**客户区那一张**（`DrawToBitmap` = 应用自己画进位图，与摆哪儿无关），并且把判据拆成两面：
    //   · 证人位：这一帧里有主按钮的品牌绿 = 应用真画过；没有 → 报「取景失败」，绝不冒充"有白底"，也绝不蒙绿；
    //   · 白底判据：只在有效帧上量成片白底（>0.2%）。
    // 全窗那张照旧写盘（出图台与标题栏深浅仍归它），note 里两个数都给，方便一眼看出取景器有没有退化。
    static bool DialogsPreviewIsDark(Form owner)
    {
        var dir = MainForm.Scratch("selftest-dlg");
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { /* 拍不出来时下面会报 */ }
        Directory.CreateDirectory(dir);     // 拍图是 bmp.Save，目录不存在会直接抛（RunPreview 里建了，这条路径要自己建）
        Dialogs.PreviewClientShot = true;   // ③：多写一份客户区图（判据吃它）
        Dialogs.SetPreview(true, dir);
        try { Dialogs.Message(owner, "自检 · 深色弹窗", "这一框由自检造出来，用来断言弹窗族没有白底。", "补充行：右下角是 Brand 绿主按钮。"); }
        finally { Dialogs.SetPreview(false); Dialogs.PreviewClientShot = false; }

        var client = Directory.Exists(dir) ? Directory.GetFiles(dir, "*-客户区.png") : Array.Empty<string>();
        if (client.Length == 0 || !Dialogs.PreviewClientOk)
        {
            var all = Directory.Exists(dir) ? string.Join(",", Directory.GetFiles(dir).Select(Path.GetFileName)) : "(窝不存在)";
            Dialogs.PreviewNoteForTest = "取景失败：客户区那一张没出图（" +
                (UiPreviewForm.LastCaptureErrorForTest.Length > 0 ? UiPreviewForm.LastCaptureErrorForTest : "没有异常记录") +
                $"｜出图位={Dialogs.PreviewClientOk} 窝里的文件=[{all}]）—— 这不是弹窗有白底";
            return false;
        }
        using (var bmp = new Bitmap(client[0]))
        {
            var (white, brand, colors, total) = UiPreviewForm.ShotStats(bmp, 0);   // 客户区图没有标题栏，整张都算
            var verdict = UiPreviewForm.ShotVerdict(white, brand, total);
            Dialogs.PreviewNoteForTest = $"{Path.GetFileName(client[0])} {bmp.Width}x{bmp.Height} 白点 {white}/{total}" +
                $" 绿点 {brand} 色数 {colors} 判读 {verdict}｜全窗图 {WholeWindowShotNote(dir, owner)}";
            return verdict == "DarkAndPainted";
        }
    }

    // 全窗（PrintWindow）那张只做对照登记，不再当判据 —— 但空帧要能在 note 里看见（否则下次又得从头查一遍取景）
    static string WholeWindowShotNote(string dir, Form owner)
    {
        var files = Directory.Exists(dir) ? Directory.GetFiles(dir, "dlg-01-*.png") : Array.Empty<string>();
        var whole = files.FirstOrDefault(f => !f.EndsWith("-客户区.png"));
        if (whole is null) return "无";
        using var bmp = new Bitmap(whole);
        var (white, brand, _, total) = UiPreviewForm.ShotStats(bmp, Math.Min(bmp.Height, UiTheme.S(owner, 34)));
        return $"白点 {white}/{total} 绿点 {brand}";
    }

    // ③（BUG-073）判据本体的三面 = **合成图喂进去**，不看时机、不靠运气（这条不涨也不掉都算白写）
    static void CheckShotVerdict(Action<string, bool, string> check)
    {
        check("③（BUG-073）判据三面：品牌绿不在 = Unpainted（取景失败）、在而成片白 = WhiteBackdrop（真有白底）、两者都过 = DarkAndPainted",
            UiPreviewForm.ShotVerdict(white: 48374, brand: 0, total: 48474) == "Unpainted"            // 实测那趟坏图的数（客户区整片白）
            && UiPreviewForm.ShotVerdict(white: 0, brand: 0, total: 12972) == "Unpainted"              // 整张全黑那趟 = 同样算没拍到
            && UiPreviewForm.ShotVerdict(white: 300, brand: 406, total: 12972) == "WhiteBackdrop"     // 有效帧里 2.3% 白 → 红
            && UiPreviewForm.ShotVerdict(white: 0, brand: 406, total: 12972) == "DarkAndPainted"      // 他看到的样子
            && UiPreviewForm.ShotVerdict(white: 25, brand: 406, total: 12972) == "DarkAndPainted",    // 0.19% 容差内（DEC-063 那几粒）
            "三面（含容差边界）各按字面判读");
        // 证人位不许是空判据：真拿一张合成白图去量，数必须与判据一致（画与判同一个家的取样节拍）
        using var white = new Bitmap(60, 40);
        using (var g = Graphics.FromImage(white)) g.Clear(Color.White);
        var w = UiPreviewForm.ShotStats(white, 0);
        using var dark = new Bitmap(60, 40);
        using (var g = Graphics.FromImage(dark))
        {
            g.Clear(UiTheme.Window);
            using var b = new SolidBrush(UiTheme.Brand);
            g.FillRectangle(b, 4, 4, 30, 20);          // 一枚"主按钮"
        }
        var d = UiPreviewForm.ShotStats(dark, 0);
        check("③（BUG-073）取样节拍与判据同一家：白图量出 brand=0 → Unpainted，暗图 + 品牌绿块 → DarkAndPainted（判据不是恒真）",
            w.White == w.Total && w.Brand == 0 && UiPreviewForm.ShotVerdict(w.White, w.Brand, w.Total) == "Unpainted"
            && d.Brand > 20 && d.White == 0 && UiPreviewForm.ShotVerdict(d.White, d.Brand, d.Total) == "DarkAndPainted",
            $"白图 白/总 {w.White}/{w.Total} 绿 {w.Brand}｜暗图 白 {d.White} 绿 {d.Brand} 色数 {d.Colors}");
    }

    // DEC-138（M6 缺口 5 / 缺口 2）：源码字面扫描。有些判据的形状是"这条路上根本不许出现某个调用"
    // 或"这一族收口必须有三处"，反射与运行时都拿不到调用点 —— 最省事的可靠办法就是把 .cs 当文本读。
    // 与注释同族的老坑：**注释里提到不算命中**（本仓库的注释大量在引用被禁的写法本身）。
    static string? SourceDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int up = 0; up < 6 && dir is not null; up++, dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "MultiLauncher.csproj"))) return dir.FullName;
        return null;
    }
    static int CodeHits(string needle)
    {
        var root = SourceDir();
        if (root is null) return -1;                                  // -1 = 源码不在旁边（实跑目录跑自检就是这种）
        int n = 0;
        // 2026-10-16 按域重组（DEC-185）：源码不再与 csproj 同层 → 递归扫源码树（排除表见 SourceSkipDirs）。
        foreach (var file in SourceFiles(root))
        {
            foreach (var raw in File.ReadAllLines(file))
            {
                var line = raw.TrimStart();
                if (line.StartsWith("//") || line.StartsWith("///")) continue;    // 注释里引用禁词不算命中
                // 行尾注释也得剥（`File.Move(...);  // 与 Directory.Move 不同` 就是这种，实测假红过一次）。
                // 但不能把 `https://` 那种一起切掉 —— 判据 = 前面不是冒号的 //。
                int cut = -1;
                for (int i = line.IndexOf("//", StringComparison.Ordinal); i >= 0; i = line.IndexOf("//", i + 2, StringComparison.Ordinal))
                {
                    if (i > 0 && line[i - 1] == ':') continue;                    // URL 里的那一对不算
                    cut = i; break;
                }
                if (cut >= 0) line = line[..cut];
                if (line.Contains(needle, StringComparison.Ordinal)) n++;
            }
        }
        return n;
    }

    // 2026-10-16 按域重组（DEC-185）：源码搬进 src\ 分域子目录后，扫描必须**递归**；
    // 但**绝不能**把构建产物与开发工具目录一起扫进来 —— `obj\` 里是 MSBuild 生成的 .cs、
    // `shots-m1\` 里是探针工程的 .cs、`data\tmp\` 里是临时副本，它们会让"命中数"判据整片假红
    //（本刀第一次跑就把 8 条红撑到 26 条，多出来的 18 条全来自这些目录）。
    // 用**排除表**而不是白名单：多扫到别的东西 = 当场红（看得见）；漏扫 = 假绿（看不见，本仓最忌的）。
    static readonly string[] SourceSkipDirs =
        { "bin", "obj", "data", "config", "publish", "shots", "shots-m1", ".git", ".public-snapshot", ".workbuddy-ai" };

    static List<string> SourceFiles(string root)
    {
        string[] all;
        try { all = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories); }
        catch { return new List<string>(); }
        var keep = new List<string>();
        foreach (var f in all)
        {
            var rel = f.Substring(root.Length).TrimStart('\\', '/');
            var top = rel.Split('\\', '/')[0];
            if (SourceSkipDirs.Contains(top, StringComparer.OrdinalIgnoreCase)) continue;
            keep.Add(f);
        }
        return keep;
    }

    // 2026-10-16 按域重组（DEC-185）：源码搬进 src\ 分域子目录后，"按文件名点一份源码"这一族
    // 不能再用 Path.Combine(root, file) —— 改成在整棵源码树里**按文件名**找。
    // 找不到返回 null，调用方照旧给 -2（= 判据自己没跑通，不猜）。
    static string? FindSource(string root, string name)
    {
        foreach (var f in SourceFiles(root))
            if (string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase)) return f;
        return null;
    }

    // DEC-138（D-7）的另一半：某一**段方法体**里不许出现某个调用。
    // 为什么要这一段：光断"停服务排在校验之后"抓不到旧写法 —— 旧写法是**入口那一击**（`DoUpgrade` / `LlamaUpgrade`）当场停服务，
    // 而校验在另一个方法（`UpgradeWork`）里，两个方法之间的行号次序怎么改都是"对"的。
    // 于是判据 = 从 `fromMark` 到 `toMark` 这一段里，`needle` 出现 0 次。返回 -1 = 没源码，-2 = 两个界标没都找到。
    static int CodeHitsBetween(string file, string fromMark, string toMark, string needle)
    {
        var root = SourceDir();
        if (root is null) return -1;
        var path = FindSource(root, file);
        if (path is null) return -2;
        string[] lines;
        try { lines = File.ReadAllLines(path); } catch { return -2; }
        int a = -1, b = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            if (a < 0 && lines[i].Contains(fromMark, StringComparison.Ordinal)) a = i;
            else if (a >= 0 && b < 0 && lines[i].Contains(toMark, StringComparison.Ordinal)) { b = i; break; }
        }
        if (a < 0 || b < 0 || b <= a) return -2;
        int n = 0;
        for (int i = a; i < b; i++)
        {
            var line = lines[i].TrimStart();
            if (line.StartsWith("//") || line.StartsWith("///")) continue;
            if (line.Contains(needle, StringComparison.Ordinal)) n++;
        }
        return n;
    }

    // DEC-138（D-7）的次序判据：同一份源码里，`first` 那一行必须排在 `then` 之前（只数代码行，注释不算）。
    // 返回 -1 = 源码不在旁边；-2 = 两行没都找到（判据自己不许悄悄空转）。
    static int CodeOrder(string file, string first, string then)
    {
        var root = SourceDir();
        if (root is null) return -1;
        var path = FindSource(root, file);
        if (path is null) return -2;
        string[] lines;
        try { lines = File.ReadAllLines(path); } catch { return -2; }
        int a = -1, b = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimStart();
            if (line.StartsWith("//") || line.StartsWith("///")) continue;
            if (a < 0 && line.Contains(first, StringComparison.Ordinal)) a = i;
            if (b < 0 && line.Contains(then, StringComparison.Ordinal)) b = i;
        }
        if (a < 0 || b < 0) return -2;
        return a < b ? 0 : 1;
    }

    // DEC-094 ⓪：真实运行目录的逐字节快照（文件名 -> SHA256）。目录不存在 = 空表（首次运行本来就没有产物）。
    // DEC-138（M6 缺口 9）：只拍**子目录名单**（GB 级那一格用这个，别拿 SHA256 去读整窝）。
    // 排序后拼成一串，跑前跑后一比：多出 / 少掉 / 被改名都看得见；目录不存在给"(不在)"，两边一致就算过。
    static string SnapshotDirNames(string dir)
    {
        try
        {
            if (!Directory.Exists(dir)) return "(不在)";
            return string.Join(",", Directory.GetDirectories(dir).Select(Path.GetFileName)
                                 .OrderBy(n => n, StringComparer.Ordinal));
        }
        catch { return "(读不动)"; }
    }

    static Dictionary<string, string> SnapshotRunDir(string dir)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!Directory.Exists(dir)) return d;
            foreach (var f in Directory.GetFiles(dir))
            {
                using var fs = File.OpenRead(f);
                d[Path.GetFileName(f)] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(fs));
            }
        }
        catch { /* 读不到就当空：这条断言的判据是"没被动过"，宁可两侧都空也别把自检弄炸 */ }
        return d;
    }

    // 两份快照的差异（空串 = 逐字节一致）：新增 / 内容变了 / 少了，三类都点名
    static string RunDirDelta(Dictionary<string, string> before, Dictionary<string, string> after)
    {
        var parts = new List<string>();
        foreach (var kv in after)
            if (!before.TryGetValue(kv.Key, out var b)) parts.Add("新增 " + kv.Key);
            else if (b != kv.Value) parts.Add("内容变了 " + kv.Key);
        foreach (var kv in before)
            if (!after.ContainsKey(kv.Key)) parts.Add("少了 " + kv.Key);
        return string.Join("、", parts);
    }

    /// DEC-084 之后"真件"可能是新名也可能是旧名（改名那一次由 GUI 启动做）：
    /// 快照 = 现在在场的那一份（新名优先），连文件名一起记下来 —— 自检跑完既要内容没变、也要名字没被换。
    static (string? path, byte[] bytes) SnapshotLlamaIni()
    {
        try
        {
            var now = MainForm.DefaultRouterIniPath();
            var old = Path.Combine(AppContext.BaseDirectory, MainForm.LegacyLlamaIniFileName);
            var p = File.Exists(now) ? now : (File.Exists(old) ? old : null);
            return p is null ? (null, Array.Empty<byte>()) : (p, File.ReadAllBytes(p));
        }
        catch { return (null, Array.Empty<byte>()); }
    }

    /// DEC-096：Ninfer 的家底快照（真件 = exe 同目录那份 ninfer_models.ini）。
    /// 自检期间路径已被 UseTempNinferHomeForTest 改道，这里**必须**读默认落点，才证得了"没动真件"。
    static (string? path, byte[] bytes) SnapshotNinferIni()
    {
        try
        {
            var p = MainForm.DefaultNinferIniPath();
            return File.Exists(p) ? (p, File.ReadAllBytes(p)) : (null, Array.Empty<byte>());
        }
        catch { return (null, Array.Empty<byte>()); }
    }

    /// <summary>DEC-138（E-10）的地板：跑出来的条数少到这个数以下，几乎不可能是"正常少几条"，
    /// 只可能是那条巨型 try 中途抛了、后面一整块没跑 —— 这种情况必须红，不许末行照样 ALL PASS。
    /// 数 = 本批基线（DEC-138 那一趟实测 890 条）减 40（AGENTS 与排期表记的基线只许往上抬；抬判据时把这个数一起抬）。</summary>
    // DEC-174（空闲停服的计时锚点修复：壳起件/就绪时补刷锚点 + 上游加载中让一拍）那批：基线 = 1,045 条（1,041 + 4），地板 = 996
    // DEC-176（左栏「模块状态」折叠态：一行横排「灯 + 简称」+ 悬浮出完整状态；三档启停按钮统一「停止」）那批：基线 = 1,052 条（1,045 + 7），地板 = 1003
    // DEC-177（折叠态三条实跑反馈：箭头去底 / 灯与文字同中线 / 简称不被切）那批：基线 = 1,055 条（1,052 + 3），地板 = 1006
    // DEC-178（第二轮三条：折叠按钮撤 tooltip / 折叠态提示挂三处窗口且走单行档 / 箭头字号增大 100%）那批：基线 = 1,058 条（1,055 + 3），地板 = 1009
    // DEC-180（版本号整体后移一位 → v0.1.8.1 + 显示口径改「照 csproj 原样」）那批：基线 = 1,060 条（1,058 + 2：不吃进提交号 / csproj 只留一个 <Version> 真标签），地板 = 1011
    // 动效收口批（2026-10-16，方案文档 §5.1 曲线同源 / §5.2 胶囊滑动 / §7 悬停擦入）：基线 = 1,068 条（1,060 + 8），地板 = 1019
    //   新增八条 = UiMotion 两条曲线三面 + 叠放帧函数端点与单调 + 胶囊矩形插值 + 擦入判据四面 + 两处定时器存活计数 + 时长档位口径。
    //   红侧真跑过（shots-m1/st-motion-red.txt）：把 EaseOutCubic 改回线性、把 WipeApplies 的 `!bare` 摘掉 → 恰好这两条 FAIL 并点名。
    // 卡1 摆位与底色批（2026-10-16，DEC-182 甲 + 同夜两条反馈）：基线 = 1,076 条（1,060 + 16：动效那 15 条 + 卡1 每一格垫色 = 父层现值 1 条），地板 = 1027
    //   那条垫色判据**第一次就跑红过**（报「后端方块（住行容器）:ff232323≠ff1e1e1e」）—— 它抓到的是 `MainForm.Card3.cs`
    //   里那句写死的 `_backendToggle.Backdrop = Card` 在搬层之后把行容器给的色又覆盖回去；顺带也解释了 ninfer 页
    //   「监听端口/思考深度」那两格为什么一直有方角：`CardStart()` 的摆位循环设的是 `BackColor`，对自绘控件无效。
    // 清小账批（2026-10-16，第 56 轮：DEC-138 那条「多卡 gpu 形状缺样本」+ 说明书版本号欠账）：基线 = 1,080 条（1,076 + 4），地板 = 1,031
    //   gpu 三档形状那条**第一次跑就红在数组那一档**（报「读出『[0,2]』需『0,2』」）= 缺样本这条账的真实形状；整数与逗号串两档当场绿（逗号串那档实测 `JsonValue.ToString()` 不带引号）。
    //   说明书「适用版本」那条以前没人钉 → 落后三档没人发现（DEC-171 / DEC-173 各登记过一笔），现在与 `MainForm.VersionTag()` 同源，发版改号不跟着改那一行就红。
    internal const int ChecksFloor = 1031;   // 基线 = **1,080** 条（1,076 + gpu 三档 3 条 + 说明书版本号同源 1 条），地板 = 1,080 − 49 = 1,031。**版本号未动**（他没说发版）：仍是 0.1.8.2

    // DEC-164（BUG-068 第二条残留：开机自启的 HKCU Run 键也按进程收口）那批：基线 = 1,011 条（1,009 + 2），地板 = 962
    // DEC-163（八类引擎台账改「去重照记」+ 「写全量」开关整套退役）那一批**净条数不变** = 基线仍 1,009、地板仍 960（组成变了，见 DEC-163）
    // DEC-162 那批：基线 = 1,009 条（1,011 − 2：删掉合并窗口/聚合行/fromNoiseTable 那 5 条，新增「合并档作废」枚举 + 守卫扫描 + 真路径两面 3 条，分类三合一），地板 = 1,009 − 49 = 960
    // 上一批 DEC-161：基线 = 1,011 条（1,007 + 4：时间戳判据三面 + 一个家三面 + 真路径两面 + 三个写点扫描），地板曾 = 962
    // DEC-160 那批：基线 = 1,007 条（+10：网关两条收手 + 挂断判据四面 + 排队两面 + 外部闸门两面 + 喂样本三族 + 那道闸只有一个家 + 引擎侧旗标禁令），地板 = 1,007 − 49
    // 原 DEC-159 口径：地板 948 =   // DEC-159 那批之后基线 = 997 条（+12：③ 取景三面与取样节拍 2 条 + DEC-159 五条 + 设置页那一格 1 条 + 其余 4 条），地板 = 997 − 49   // DEC-157 + DEC-158 起基线 **985**（开发目录实测 983~985：GitHub 那两条网络用例走没走到抖 1~2 条）；
                                           // 956 + 29 = DEC-157 那批 13 条（台账纯函数 + 真路径喂样本 + 三处接线 + 旧键换算三面 + 走段两面 + 方块确认 + 那一格只在「通用」+ 两句文案）
                                           //            + DEC-158 那批 16 条（分类逐档 + 不误伤 + 条件放行 + 合并/节流窗口 + 空行那面 + fromNoiseTable 命中 + 开关与设置页两面 + 按档穷举）。
                                           // 地板取最低那档再留余量 = 基线减 49

    static int Finish(StringBuilder sb, string outPath, int failures)
    {
        // 条数从**报告正文**数：`SelfTest.Gateway.cs` 那份 partial 有它自己的 Check 闭包，本地变量数不到它（少三十几条 = 第二个家）
        int checks = sb.ToString().Split('\n').Count(l => l.StartsWith("PASS ") || l.StartsWith("FAIL "));
        // 总条数进报告（"基线 N 条"这件事从此不只在文档里，deploy.ps1 的那一步也读这一行）
        sb.AppendLine($"TOTAL {checks} 条（地板 {ChecksFloor}）");
        if (checks < ChecksFloor)
            sb.AppendLine($"FAIL  条数掉到地板以下：跑了 {checks} 条 < {ChecksFloor} —— 大概率是某块断言整段没执行（看上面有没有 EXCEPTION 行）");
        sb.AppendLine();
        bool ok = failures == 0 && checks >= ChecksFloor;
        sb.AppendLine(ok ? "ALL PASS" : (failures == 0 ? "FAILURE(S): 条数不足" : failures + " FAILURE(S)"));
        try { File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false)); } catch { }
        return ok ? 0 : 1;
    }

    static bool Materialize(Settings s, out JsonObject? cfg, out string error)
    {
        cfg = null;
        if (!MainForm.BuildEffectiveConfig(s, out var eff, out error)) return false;
        try
        {
            cfg = JsonNode.Parse(File.ReadAllText(eff)) as JsonObject;
            return cfg is not null;
        }
        catch (Exception ex) { error = "读回运行配置失败: " + ex.Message; return false; }
    }

    static bool Rejects(Settings s, string keyword)
        => !MainForm.BuildEffectiveConfig(s, out _, out var err) && err.Contains(keyword);

    static List<string> ArgList(JsonObject? node) =>
        (node?["args"] as JsonArray)?.Select(x => x?.GetValue<string>() ?? "").ToList() ?? new List<string>();

    static List<string> BaseArgs(string configPath) =>
        ArgList(JsonNode.Parse(File.ReadAllText(configPath)) as JsonObject);

    static Dictionary<string, string?> Managed(List<string> args)
    {
        var keys = new[] { "--max-context", "--kv", "--kv-resident", "--spec", "--spec-min-p", "--expert-cache", "--prefill", "--vram-reserve-mib" };
        var d = keys.ToDictionary(k => k, k => Value(args, k));
        d["--vision"] = args.Contains("--vision") ? "1" : null;
        d["--mmap-experts"] = args.Contains("--mmap-experts") ? "1" : null;
        return d;
    }

    static string? Value(List<string> args, string flag)
    {
        var i = args.IndexOf(flag);
        return i >= 0 && i + 1 < args.Count ? args[i + 1] : null;
    }
        // 就地写保真用的两条逐字节比较（"没动的行必须逐字节原样"只有这么验才算数）
        static bool BytesEqual(byte[] a, byte[] b) => a.AsSpan().SequenceEqual(b);
        static int DiffLines(byte[] a, byte[] b)
        {
            var la = SplitLf(Encoding.UTF8.GetString(a));
            var lb = SplitLf(Encoding.UTF8.GetString(b));
            if (la.Length != lb.Length) return Math.Abs(la.Length - lb.Length) + 1000;   // 行数都变了：给个大数
            int n = 0;
            for (int i = 0; i < la.Length; i++) if (la[i] != lb[i]) n++;
            return n;
        }
        static string[] SplitLf(string s) => s.Replace("\r\n", "\n").Split('\n');
}

// 自检里给自绘控件发 BM_CLICK 用的最小 P/Invoke（user32）
internal static class Win32
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    internal static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

}
