# SP-011 headed capture (SP-007 pattern): raise the spike window TOPMOST, GDI-copy the
# primary screen region of the window to a PNG in scratch/. RAIL/occlusion-safe on Windows.
param(
  [Parameter(Mandatory=$true)][string]$OutPng,
  [string]$TitleLike = "SP-011 WebView spike",
  [int]$DelayMs = 300
)
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class Win32Cap {
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
$proc = Get-Process | Where-Object { $_.MainWindowTitle -like "*$TitleLike*" } | Select-Object -First 1
if (-not $proc) { Write-Error "no window titled like '$TitleLike'"; exit 1 }
$h = $proc.MainWindowHandle
[Win32Cap]::SetWindowPos($h, [IntPtr]::Zero, 0, 0, 0, 0, 0x0001 -bor 0x0002 -bor 0x0040) | Out-Null  # NOSIZE|NOMOVE|SHOWWINDOW (not topmost-flag: plain raise)
[Win32Cap]::SetWindowPos($h, [IntPtr]::new(-1), 0, 0, 0, 0, 0x0001 -bor 0x0002) | Out-Null        # HWND_TOPMOST
Start-Sleep -Milliseconds $DelayMs
$r = New-Object Win32Cap+RECT
[Win32Cap]::GetWindowRect($h, [ref]$r) | Out-Null
$w = $r.Right - $r.Left; $hh = $r.Bottom - $r.Top
$bmp = New-Object System.Drawing.Bitmap $w, $hh
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size)
New-Item -ItemType Directory -Force -Path (Split-Path $OutPng) | Out-Null
$bmp.Save($OutPng, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
# drop topmost so later captures of other windows are clean
[Win32Cap]::SetWindowPos($h, [IntPtr]::new(-2), 0, 0, 0, 0, 0x0001 -bor 0x0002) | Out-Null       # HWND_NOTOPMOST
Write-Output "captured $($w)x$($hh) -> $OutPng"
