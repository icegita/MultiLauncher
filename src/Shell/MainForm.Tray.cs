using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace MultiLauncher;

// MainForm 的托盘与窗口行为（显示/隐藏、退出、单实例、开机自启那几处）。
// 2026-10-15 从 Program.cs 整段搬出，一行未改。
public partial class MainForm
{
    // ---------------- 托盘 / 窗口 ----------------

    void SetupTray()
    {
        try { _tray.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
        catch { _tray.Icon = SystemIcons.Application; }
        _tray.Text = AppName + "  " + VersionTag();    // "v" 那一档前缀只有一个家（`VersionTag()`），别在这儿再拼一次
        _tray.Visible = true;
        // 托盘菜单改版（2026-10-03，DEC-041）：按启用模块分组 + 组头带状态（不带端口）+ 启停按实际置灰 +
        // 深色自绘；「打开网页」去掉、「打开配置」不放、新增「设置」（只弹设置页不显示主窗）。构建与填充见 TrayMenu.cs。
        BuildTrayMenu();
        // DEC-061：这一行以前紧接着把 _trayMenu 又挂回 _tray.ContextMenuStrip —— 那正好抵消了
        // TrayMenu.cs 里「不用 NotifyIcon 内置弹出」的修复（DEC-045：内置路径首次右键才建句柄、
        // 会被 Items.Clear 吞掉）。现在只走自己在 MouseUp 里填内容再按方向 Show 那一条路。
        _tray.DoubleClick += (_, _) => ShowFromTray();
    }

    // DEC-061：退出时把托盘与定时器收掉。NotifyIcon 不置 Visible=false 也不 Dispose，进程结束后
    // 图标会留在托盘上成"残影"（要鼠标划过才消失）；自绘菜单与 _probeTimer/_statsTimer 同样没人释放。
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try { _tray.Visible = false; } catch { /* 已经在拆 */ }
            _tray.Dispose();
            _trayMenu?.Dispose();
            _probeTimer.Dispose();
            _statsTimer.Dispose();
        }
        base.Dispose(disposing);
    }

    public void ShowMainWindow()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
        ApplyPendingFollowPage();      // DEC-125①：藏在托盘/最小化期间被唤醒起来的那一件，现在才跟过去
    }

    // DEC-125①：从最小化恢复也算"窗口到眼前了"（ShowMainWindow 覆盖不到用户点任务栏那一条）。
    // 没有待办时这个方法第一行就返回，不会每次都去算模块表。
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState == FormWindowState.Normal) ApplyPendingFollowPage();
    }

    void ShowFromTray() => ShowMainWindow();

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // DEC-091 第三条收口：关窗会 CollectSettings，端口那格带着没确认过的手改时必须先问（不然这一格绕过确认框）
        SettleNinferPort("关窗");
        // DEC-092 第 2 条（把 DEC-079 只管 llama 那一支的确认框扩到两侧）：关窗时只要界面与**磁盘上那一份**
        // 不一致，就当面问一句「写进去 / 丢弃」，两组明细都列出来 —— 要人决策的地方不能只说"有改动"（口径同 DEC-082）。
        // 原来这里是"默默收一遍再存"，于是没点「保存」的改动也会被存住（用户实测：切页回来还在、重启也还在）。
        if (e.CloseReason == CloseReason.UserClosing && PendingParamEditsForTest)
        {
            var own = ParamDiff(ParamHome.Launcher);
            var sec = _llamaLoadedTag.Length > 0
                ? LlamaSectionEdits(CollectSettings(), _llamaEffective) : new List<LlamaIni.IniEdit>();
            // DEC-096：Ninfer 那一组的落点是选中那一段的 args（同一张确认框，第三组明细）
            var ninferSec = _ninferLoadedTag.Length > 0
                ? NinferPendingChanges(CollectSettings(), ActiveNinferBackend(_config.Current), _ninferEffective)
                : new List<string>();
            string Detail(LlamaIni.IniEdit p)
            {
                var nv = (p.Value ?? "").Trim();
                var ov = _llamaEffective.TryGetValue(p.Key, out var ev) ? ev.Trim() : "";
                return LlamaChangeText(new LlamaIni.IniChange(p.Key,
                    ov.Length > 0 ? ov : null, nv.Length > 0 ? nv : null));
            }
            var lines = new List<string>(own.Select(d => d.Label + " " + d.From + " → " + d.To));
            lines.AddRange(sec.Select(Detail));
            lines.AddRange(ninferSec);
            if (lines.Count > 0)
            {
                var r = Dialogs.Confirm(this, "这些改动还没保存",
                    string.Join(" / ", lines) + "。\n\n「写进去」= 存进文件（llama 与 Ninfer 那些照旧先留一份备份）；"
                    + "「丢弃」= 这次的改动不要了，下次打开就是上次保存的样子。",
                    null, yes: "写进去", no: "丢弃");
                // 写不下去（上下文非法 / 段写失败）就别关：否则"丢弃"是替他做的决定
                if (r == DialogResult.Yes && !SaveParameterEdits()) { e.Cancel = true; return; }
            }
        }
        // 关窗仍然收"不是参数"的那批（窗口位置、停在哪个模块页、设置页字段）—— 这些没有"未保存"这一说。
        // DEC-092：不再 MarkParamsSaved()，✕ 只是藏到托盘时（e.Cancel）按钮得继续亮着提醒还有没写的改动。
        // （DEC-079 那句"从下次开机还在变成真丢"现在成立了，所以确认框是唯一的兜底。）
        SaveConfig(commitParams: false);
        // 点 ✕ 的行为现在可配（设置页「关闭到托盘」，默认开）：关掉就是真退出
        if (!_reallyExit && e.CloseReason == CloseReason.UserClosing && _config.Current.CloseToTray)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        if (!_reallyExit) _reallyExit = true;
        if (_config.Current.StopServiceOnExit) foreach (var v in _views) v.StopQuiet();   // DEC-096：三个模块了，遍历表别再点名
        DisposeGateway();
        base.OnFormClosing(e);
    }

    void ExitApp()
    {
        // 退出时是否停服务由设置页的开关决定（默认停）——不再每次弹确认框
        _reallyExit = true;
        Close();
    }

    // 退出路径上的停服务：不弹窗、不等就绪，尽力而为
    void StopServerQuiet()
    {
        var p = _process;
        if (p is null || p.HasExited) return;
        _strataStopping = true;
        try { p.Kill(true); } catch { /* 已经退了 */ }
    }

    void RestoreWindowBounds()
    {
        if (_winW < MinimumSize.Width || _winH < MinimumSize.Height) return;
        Size = new Size(_winW, _winH);
        var virt = SystemInformation.VirtualScreen;
        if (_winX >= virt.Left - 50 && _winX < virt.Right && _winY >= virt.Top - 50 && _winY < virt.Bottom)
        {
            StartPosition = FormStartPosition.Manual;
            Location = new Point(_winX, _winY);
        }
        if (_winMax) WindowState = FormWindowState.Maximized;
    }

}
