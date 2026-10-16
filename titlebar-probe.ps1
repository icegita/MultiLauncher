param([string]$OutDir = "$PSScriptRoot\shots", [string]$Exe = "$PSScriptRoot\bin\Release\net10.0-windows\MultiLauncher.exe")
$ErrorActionPreference = 'Continue'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class W2 {
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
  [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr ctx);
  [StructLayout(LayoutKind.Sequential)] public struct R { public int L,T,Rr,B; }
}
"@
# this process must be Per-Monitor V2 aware, else 150% scaling gives virtualized coords
[W2]::SetThreadDpiAwarenessContext([IntPtr](-4)) | Out-Null

function Sample($bmp) {
  $w = $bmp.Width; $hh = $bmp.Height
  $cap = $bmp.GetPixel([int]($w/2), 14)
  $cli = $bmp.GetPixel([int]($w/2), [int]($hh*0.40))
  $edge = $bmp.GetPixel(1, [int]($hh/2))
  return @($cap.R, $cap.G, $cap.B, $cli.R, $cli.G, $cli.B, $edge.R, $edge.G, $edge.B)
}

function Shot([IntPtr]$h, [string]$file) {
  if ($h -eq [IntPtr]::Zero) { return "nohandle $file" }
  $r = New-Object W2+R
  [W2]::GetWindowRect($h, [ref]$r) | Out-Null
  $w = $r.Rr - $r.L; $hh = $r.B - $r.T

  # 1) PrintWindow: not blocked by overlapping windows, but DWM-painted caption colors may not survive it
  $pw = New-Object System.Drawing.Bitmap($w, $hh)
  $g1 = [System.Drawing.Graphics]::FromImage($pw)
  $hdc = $g1.GetHdc(); $okpw = [W2]::PrintWindow($h, $hdc, 2); $g1.ReleaseHdc($hdc)
  $g1.Dispose()
  $pwFile = $file -replace '\.png$', '-pw.png'
  $pw.Save($pwFile, [System.Drawing.Imaging.ImageFormat]::Png)

  # 2) real screen grab (composited by DWM; this is the truth for caption colors)
  $bmp = New-Object System.Drawing.Bitmap($w, $hh)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  try { $g.CopyFromScreen($r.L, $r.T, 0, 0, (New-Object System.Drawing.Size($w, $hh))) } catch { "copyfail $file" }
  $g.Dispose()
  $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)

  $a = Sample $pw; $b = Sample $bmp
  $pw.Dispose(); $bmp.Dispose()
  "{0} {1}x{2} printwin={3} | PRINTWIN caption={4},{5},{6} client={7},{8},{9} edge={10},{11},{12} | SCREEN caption={13},{14},{15}" -f (Split-Path $file -Leaf), $w, $hh, $okpw, $a[0], $a[1], $a[2], $a[3], $a[4], $a[5], $a[6], $a[7], $a[8], $b[0], $b[1], $b[2]
}

$combos = @(
  @{ name = 'off';    cap = 'off';   dlg = '0' },
  @{ name = 'flat';   cap = 'flat';  dlg = '0' },
  @{ name = 'edged';  cap = 'edged'; dlg = '0' },
  @{ name = 'dialog'; cap = 'flat';  dlg = '1' }   # settings-window flavour: dark mode only, no custom colors
)
$handles = @{}
$procs = New-Object System.Collections.ArrayList
foreach ($c in $combos) {
  $variant = $c.name
  $env:STRATA_UI_TITLEBAR = $c.cap
  if ($c.dlg -eq '1') { $env:STRATA_UI_TITLEBAR_DIALOG = '1' } else { Remove-Item Env:\STRATA_UI_TITLEBAR_DIALOG -ErrorAction SilentlyContinue }
  # UseShellExecute must stay off: via shell it raises an elevation prompt that the sandbox cancels
  $si = New-Object System.Diagnostics.ProcessStartInfo
  $si.FileName = $Exe
  $si.Arguments = '--uiprobe titlebar'
  $si.UseShellExecute = $false
  $p = [System.Diagnostics.Process]::Start($si)
  [void]$procs.Add($p)
  $h = [IntPtr]::Zero
  for ($i=0; $i -lt 40; $i++) { Start-Sleep -Milliseconds 250; $p.Refresh(); if ($p.MainWindowHandle -ne [IntPtr]::Zero) { $h = $p.MainWindowHandle; break } }
  if ($h -eq [IntPtr]::Zero) { "$variant no window handle"; continue }
  $handles[$variant] = $h
  Start-Sleep -Milliseconds 900
  [W2]::SetForegroundWindow($h) | Out-Null
  Start-Sleep -Milliseconds 500
  Shot $h "$OutDir\$variant-active.png"
}

if ($handles.Count -ge 2) {
  # two passes with different decoys, so every variant gets an inactive shot
  foreach ($decoy in @('off','edged')) {
    if (-not $handles.ContainsKey($decoy)) { continue }
    [W2]::SetForegroundWindow($handles[$decoy]) | Out-Null
    Start-Sleep -Milliseconds 700
    foreach ($k in @('off','flat','edged','dialog')) {
      if ($k -eq $decoy -or -not $handles.ContainsKey($k)) { continue }
      Shot ($handles[$k]) "$OutDir\$k-inactive.png"
    }
  }
}

foreach ($p in $procs) { try { $p.Kill() } catch {} }
$b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
"primary-screen $($b.Width)x$($b.Height)"
