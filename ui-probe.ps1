param([string]$ShotDir, [string]$Click, [switch]$Dump, [switch]$Log, [int]$Tail = 3000)
$ErrorActionPreference = 'Continue'
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections.Generic;
public class Win {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr h, EnumProc cb, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr wp, StringBuilder lp);
  [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr wp, IntPtr lp);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr ctx);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
  public static string Aware() { return SetThreadDpiAwarenessContext((IntPtr)(-4)) == IntPtr.Zero ? "dpi-fail" : "dpi-ok"; }
  public static IntPtr FindByText(IntPtr parent, string text) {
    IntPtr found = IntPtr.Zero;
    EnumChildWindows(parent, delegate(IntPtr h, IntPtr l) {
      var sb = new StringBuilder(512); GetWindowText(h, sb, 512);
      if (sb.ToString() == text) { found = h; return false; }
      return true;
    }, IntPtr.Zero);
    return found;
  }
  public static string Click(IntPtr parent, string text) {
    var h = FindByText(parent, text);
    if (h == IntPtr.Zero) return "NOT FOUND: " + text;
    SendMessage(h, 0x00F5, IntPtr.Zero, IntPtr.Zero);
    return "clicked " + text;
  }
  public static string LogText(IntPtr parent) {
    string result = "";
    EnumChildWindows(parent, delegate(IntPtr h, IntPtr l) {
      var cls = new StringBuilder(128); GetClassName(h, cls, 128);
      RECT r; GetWindowRect(h, out r);
      if (cls.ToString().Contains("Edit") && (r.Bottom - r.Top) > 100) {
        var sb = new StringBuilder(200000);
        SendMessage(h, 0x000D, (IntPtr)200000, sb);
        result = sb.ToString();
        return false;
      }
      return true;
    }, IntPtr.Zero);
    return result;
  }
  public static string Dump(IntPtr parent) {
    var sb2 = new StringBuilder();
    RECT pr; GetWindowRect(parent, out pr);
    EnumChildWindows(parent, delegate(IntPtr h, IntPtr l) {
      var cls = new StringBuilder(128); GetClassName(h, cls, 128);
      var txt = new StringBuilder(256); GetWindowText(h, txt, 256);
      RECT r; GetWindowRect(h, out r);
      var t = txt.ToString(); if (t.Length > 20) t = t.Substring(0, 20) + "...";
      sb2.AppendLine(string.Format("{0,-12} {1,4},{2,4} {3,4}x{4,-4} vis={5} en={6} {7}",
        cls.ToString().Replace("WindowsForms10.","").Replace(".app.0.2360855_r3_ad1",""), r.Left - pr.Left, r.Top - pr.Top,
        r.Right - r.Left, r.Bottom - r.Top, (IsWindowVisible(h) ? 1 : 0), (IsWindowEnabled(h) ? 1 : 0), t));
      return true;
    }, IntPtr.Zero);
    return sb2.ToString();
  }
  [DllImport("user32.dll")] public static extern bool IsWindowEnabled(IntPtr h);
}
"@
[Win]::Aware() | Out-Null
$proc = Get-Process MultiLauncher -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $proc) { "NO PROCESS"; exit 1 }
$h = $proc.MainWindowHandle
[Win]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 600
if ($Dump) { "---- dump ----"; [Win]::Dump($h) }
if ($Click) { "click=" + [Win]::Click($h, $Click) }
if ($Log) { $t = [Win]::LogText($h); "---- log tail ----"; if ($t.Length -gt $Tail) { $t.Substring($t.Length - $Tail) } else { $t } }
if ($ShotDir) {
  Add-Type -AssemblyName System.Drawing.Common -ErrorAction SilentlyContinue
  $rect = New-Object Win+RECT
  [Win]::GetWindowRect($h, [ref]$rect) | Out-Null
  $ww = $rect.Right - $rect.Left; $hh = $rect.Bottom - $rect.Top
  $bmp = New-Object System.Drawing.Bitmap($ww, $hh)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $hdc = $g.GetHdc()
  if (-not [Win]::PrintWindow($h, $hdc, 2)) { $g.ReleaseHdc($hdc); $g.CopyFromScreen($rect.Left, $rect.Top, 0, 0, (New-Object System.Drawing.Size($ww, $hh))) } else { $g.ReleaseHdc($hdc) }
  $bmp.Save("$ShotDir\ui.png", [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
  "shot=$ww x $hh"
}