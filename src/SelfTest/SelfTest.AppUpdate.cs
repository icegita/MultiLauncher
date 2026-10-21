namespace MultiLauncher;

// 自检 · 本程序自更新那一族（DEC-202 §8 那 10 条）
//
// 为什么单独起一个域文件：`SelfTest.UiUpdateWindow.cs` 已经 76 KB（自检族按 2026-10-15 他口径冻结、不再继续切），
// 新链的判据塞进去只会让那一份更难读；这一族自成一段（纯函数喂样本 + 一个真 MainForm 实例），规则与别处一致。
//
// 纪律（与本仓同规）：
//   · 一条都不联网 —— 探到没探到取决于上游今天发了什么，不该决定用例红绿（BUG-052 同族）；
//   · 闸门位（`AppUpdateProbeDisabledForTest` / `AppSelfReplaceDisabledForTest`）由 `SelfTest.RunBody` 先挂，
//     这里只断"闸真的挡在联网与替换之前"；
//   · 界面形状读**意图位**，不读 `Control.Visible`（窗没显示时它恒 false，BUG-043）。
internal static partial class SelfTest
{
    static void AppUpdateChecks()
    {
        // ---------------- ① 更新源解析 ----------------
        var want = "https://api.github.com/repos/icegita/MultiLauncher";
        var web = MainForm.AppRepoApiOf("https://github.com/icegita/MultiLauncher");
        var empty = MainForm.AppRepoApiOf("");
        var bad = MainForm.AppRepoApiOf("这不是地址");
        Check("DEC-202 更新源：网页地址 → api；留空与填坏都回落默认（填坏不瘫痪整条链）",
            web == want && empty == want && bad == want,
            $"网页={web}｜空={empty}｜填坏={bad}");

        // ---------------- ② 资产挑选 ----------------
        // 样本形状照真仓库那份（v0.1.8.3 实测：exe 1,549,108 字节 + 同名 .sha256）
        const string full = """
        {"tag_name":"v0.1.8.5","draft":false,"published_at":"2026-10-21T10:00:00Z","body":"notes here",
         "assets":[
          {"name":"MultiLauncher-0.1.8.5-windows-x64.exe","browser_download_url":"https://x/a.exe","size":1549108},
          {"name":"MultiLauncher-0.1.8.5-windows-x64.exe.sha256","browser_download_url":"https://x/a.exe.sha256","size":105}]}
        """;
        var r = MainForm.ParseAppRelease(full);
        Check("DEC-202 资产挑选：命中 exe 与**同名** .sha256，且不把 .sha256 误当成 exe",
            r.Tag == "v0.1.8.5"
            && r.AssetName == "MultiLauncher-0.1.8.5-windows-x64.exe"
            && r.ShaName == r.AssetName + ".sha256"
            && r.Size == 1549108,
            $"tag={r.Tag} exe={r.AssetName} sha={r.ShaName} 体积={r.Size}");

        const string noSha = """
        {"tag_name":"v0.1.8.5","draft":false,"assets":[
          {"name":"MultiLauncher-0.1.8.5-windows-x64.exe","browser_download_url":"https://x/a.exe","size":1}]}
        """;
        Check("DEC-202 资产挑选：缺了同名 .sha256 = 这一版不给更新（宁可等下一版，也不放弃校验）",
            string.IsNullOrEmpty(MainForm.ParseAppRelease(noSha).Tag), "tag=" + MainForm.ParseAppRelease(noSha).Tag);

        const string otherExe = """
        {"tag_name":"v0.1.8.5","draft":false,"assets":[
          {"name":"OtherApp-1.0-windows-x64.exe","browser_download_url":"https://x/o.exe","size":7},
          {"name":"OtherApp-1.0-windows-x64.exe.sha256","browser_download_url":"https://x/o.exe.sha256","size":9},
          {"name":"MultiLauncher-0.1.8.5-windows-x64.exe","browser_download_url":"https://x/a.exe","size":11},
          {"name":"MultiLauncher-0.1.8.5-windows-x64.exe.sha256","browser_download_url":"https://x/a.exe.sha256","size":13}]}
        """;
        var ro = MainForm.ParseAppRelease(otherExe);
        Check("DEC-202 资产挑选：同目录里别的 exe 不误挑（只认 MultiLauncher- 开头那对）",
            ro.AssetName == "MultiLauncher-0.1.8.5-windows-x64.exe" && ro.Size == 11, "选中=" + ro.AssetName);

        // ---------------- ③ .sha256 文本 ----------------
        const string hash = "ab01d92df8744158fc1fba6a3357b23ec8c00ce76532c4008cc1ddccebdafa9a";
        Check("DEC-202 .sha256 解析：`<hash>  <name>` 两段式、certutil 那种多一行头的形状、空串与垃圾串",
            MainForm.ParseSha256File(hash + "  MultiLauncher-0.1.8.3-windows-x64.exe") == hash
            && MainForm.ParseSha256File("SHA256 hash of file x:\r\n" + hash.ToUpperInvariant() + "  \r\nCertUtil: -hashfile ok")
               == hash
            && MainForm.ParseSha256File("") == ""
            && MainForm.ParseSha256File("not a hash") == "",
            "两段式 / 多行头 / 空 / 垃圾 各按字面判");

        // ---------------- ④ 产物身份 ----------------
        Check("DEC-202 产物身份：版本一致放行；不一致、读不出、tag 为空都判失败",
            MainForm.AppExeVersionMatches("0.1.8.5", "v0.1.8.5")
            && MainForm.AppExeVersionMatches("0.1.8.5.0", "v0.1.8.5")     // 四段与三段视为同一版
            && !MainForm.AppExeVersionMatches("0.1.8.4", "v0.1.8.5")
            && !MainForm.AppExeVersionMatches("", "v0.1.8.5")
            && !MainForm.AppExeVersionMatches("0.1.8.5", ""),
            "0.1.8.5 / 0.1.8.5.0 放行；0.1.8.4 / 空 / tag空 拒绝");

        // ---------------- ⑤ 准入四条 ----------------
        using var ui = new MainForm();
        // 测试态这一档：连探针文件都不写
        Check("DEC-202 准入：测试态（自检 / 出图台）一律拒绝自动替换",
            !ui.CanSelfReplace(out var whyTest) && whyTest.Contains("测试态"), "缘由=" + whyTest);
        // 放开测试闸后，按**进程形态**判（这一条不能假设形态）：
        //   · 开发目录跑自检 → ProcessPath 是 dotnet.exe → 必须拒，缘由落"开发态"档；
        //   · 实跑目录跑自检（deploy.ps1 那一趟）→ ProcessPath 就是 MultiLauncher.exe，其余三条都满足 → 必须放行。
        // 第一版写成"恒拒"，开发目录绿、实跑目录红（2026-10-21 deploy.ps1 [3/3] 实跑就是这条红的）——
        // "用户机器上恰好是什么环境"不该决定用例红绿（BUG-052 同族）。
        var gate = MainForm.AppSelfReplaceDisabledForTest;
        MainForm.AppSelfReplaceDisabledForTest = false;
        try
        {
            var hostName = Path.GetFileName(Environment.ProcessPath ?? "");
            bool isDotnetHost = !string.Equals(hostName, MainForm.AppExeName, StringComparison.OrdinalIgnoreCase);
            bool ok = ui.CanSelfReplace(out var whyNow);
            Check("DEC-202 准入：闸关着时按进程形态判 —— dotnet 宿主拒（缘由落开发态）、实跑 exe 放行",
                isDotnetHost ? (!ok && whyNow.Contains("开发态")) : ok,
                $"宿主={hostName} 放行={ok} 缘由={whyNow}");
        }
        finally { MainForm.AppSelfReplaceDisabledForTest = gate; }

        // ---------------- ⑥ 页头徽标 ----------------
        var offState = ui.AppBadgeStateForTest();
        ui.SetAppOfferForTest("v0.1.8.5", "2026-10-21T10:00:00Z", skipped: false);
        var onState = ui.AppBadgeStateForTest();
        ui.ClearAppOfferForTest();
        var backState = ui.AppBadgeStateForTest();
        Check("DEC-202 页头徽标：探到新版才出面、文字=「新版本 vX」；清掉立刻收（读意图位，不读 Visible）",
            offState.StartsWith("-/") && onState.StartsWith("want/")
            && onState.EndsWith("新版本 v0.1.8.5") && backState.StartsWith("-/"),
            $"无={offState}｜有={onState}｜清后={backState}");

        // ---------------- ⑦ 宽度兜底 ----------------
        Check("DEC-202 徽标宽度兜底三档：放得下 → 文字+图标；只放得下图标 → 只留图标；都不下 → 整块不出",
            MainForm.AppBadgeFit(300, 200, 18) == (true, false)
            && MainForm.AppBadgeFit(20, 200, 18) == (true, true)
            && MainForm.AppBadgeFit(10, 200, 18) == (false, false),
            "300/20/10 三档各按字面判");
        var g = ui.AppBadgeGeomForTest();
        Check("DEC-202 徽标不压分段、也不压「打开配置」：文字右缘到图标左缘 ≥ 0（他 2026-10-21 点的那处）",
            g.LabelIconGap >= 0 && g.OverlapLeft >= 0 && g.GapToButton >= 0,
            $"文字到图标={g.LabelIconGap} 左余量={g.OverlapLeft} 到按钮={g.GapToButton}（未显示时为 0 也过）");

        // ---------------- ⑧ 关于页两行 ----------------
        using (var sf = new SettingsForm(ui, new Settings()))
        {
            Check("DEC-202 关于页「更新源」那一格可编辑（与另三节「更新源」同形状）",
                sf.AppUpdateUrlEditableForTest, "可编辑=" + sf.AppUpdateUrlEditableForTest);
        }

        // ---------------- ⑨ 托盘那一项 ----------------
        var idleText = ui.AppUpdateMenuLabel();
        ui.SetAppBusyForTest(true);
        var busyText = ui.AppUpdateMenuLabel();
        ui.SetAppBusyForTest(false);
        Check("DEC-202 托盘那一项三态：闲=「检查更新 · 本程序」/ 忙=「升级中… · 本程序」（忙时置灰）",
            idleText == "检查更新 · 本程序" && busyText == "升级中… · 本程序" && ui.AppUpdateMenuEnabled(),
            $"闲={idleText}｜忙={busyText}｜可点={ui.AppUpdateMenuEnabled()}");

        // ---------------- ⑩ 探测纪律 ----------------
        int runs0 = MainForm.AppProbeRunsForTest, skips0 = MainForm.AppProbeSkippedForTest;
        ui.CheckAppUpdate(force: false);
        ui.CheckAppUpdate(force: true);
        Check("DEC-202 闸门先于联网：自检里调两次探测 —— 真发起 0 次、被挡 2 次（一次都不许打 GitHub）",
            MainForm.AppProbeRunsForTest == runs0 && MainForm.AppProbeSkippedForTest >= skips0 + 2,
            $"真发起={MainForm.AppProbeRunsForTest}(基线 {runs0}) 被挡={MainForm.AppProbeSkippedForTest - skips0}");
        Check("DEC-202 每进程一次 + 手动入口 30 秒防连点（纯函数三面）",
            MainForm.AppProbeGate(false, false, 0).Go
            && !MainForm.AppProbeGate(false, true, 0).Go               // 启动那一次：探过就不再探
            && !MainForm.AppProbeGate(true, false, 5_000).Go           // 手动：刚查过 5 秒
            && MainForm.AppProbeGate(true, false, 60_000).Go,          // 手动：过了 30 秒照查
            "四种组合各按字面判");

        // ---------------- ⑪ helper 脚本 ----------------
        var helper = MainForm.SwapHelperText(
            @"D:\MultiLauncher\MultiLauncher.exe",
            @"D:\MultiLauncher\MultiLauncher.exe.new",
            @"D:\MultiLauncher\data\tmp\app-update-v0.1.8.5", 4321);
        Check("DEC-202 helper 脚本：等 PID（≤60 秒）→ exe→.old → .new→exe → 起新件 → 3 秒复查 → 写回执；两处回滚都在",
            helper.Contains("PID eq 4321")
            && helper.Contains("\"%EXE%\" \"%OLD%\"") && helper.Contains("\"%NEW%\" \"%EXE%\"")
            && helper.Contains("echo ok") && helper.Contains("echo fail start") && helper.Contains("echo fail move")
            && helper.Contains(":rollback_move") && helper.Contains(":rollback_start"),
            "等 PID / 两段改名 / 三条回执 / 两处回滚 都在");
        Check("DEC-202 helper 的等待不许用 timeout（helper 以 CreateNoWindow 起、stdin 被重定向，timeout 会立刻返回 → 每次更新都误回滚）",
            !helper.Contains("timeout /t") && helper.Contains("ping -n 4 127.0.0.1"),
            "实跑演练抓到的那个 bug 钉在这里");
    }
}
