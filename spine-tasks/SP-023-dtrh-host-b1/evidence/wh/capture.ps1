# SP-023 headed capture: find the DTRH host window (secondary toplevel — the dashboard
# owns the process MainWindowHandle), raise it, GDI-copy its screen region to PNG.
param(
  [Parameter(Mandatory=$true)][string]$OutPng,
  [string]$TitleLike = "DTRH host",
  [string]$ProcessName = "CcpClient.Desktop",
  [int]$DelayMs = 300
)
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class Win32Cap {
  public delegate bool EnumProc(System.IntPtr h, System.IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, System.IntPtr l);
  [DllImport("user32.dll")] public static extern int GetWindowText(System.IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(System.IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(System.IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(System.IntPtr h, System.IntPtr after, int x, int y, int cx, int cy, uint flags);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(System.IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(System.IntPtr h);
  public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
$proc = Get-Process $ProcessName -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $proc) { Write-Error "no process $ProcessName"; exit 1 }
$targetPid = $proc.Id
$found = [IntPtr]::Zero
$titles = @()
$cb = [Win32Cap+EnumProc]{
  param($h, $l)
  $wpid = 0
  [Win32Cap]::GetWindowThreadProcessId($h, [ref]$wpid) | Out-Null
  if ($wpid -eq $targetPid -and [Win32Cap]::IsWindowVisible($h)) {
    $sb = New-Object System.Text.StringBuilder 512
    [Win32Cap]::GetWindowText($h, $sb, 512) | Out-Null
    $script:titles += $sb.ToString()
    if ($sb.ToString() -like "*$TitleLike*") { $script:found = $h; return $false }
  }
  return $true
}
[Win32Cap]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null
if ($found -eq [IntPtr]::Zero) { Write-Error "no window like '$TitleLike' on pid $targetPid (titles: $($titles -join ' | '))"; exit 1 }
$h = $found
[Win32Cap]::SetWindowPos($h, [IntPtr]::new(-1), 0, 0, 0, 0, 0x0001 -bor 0x0002 -bor 0x0040) | Out-Null  # HWND_TOPMOST
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
[Win32Cap]::SetWindowPos($h, [IntPtr]::new(-2), 0, 0, 0, 0, 0x0001 -bor 0x0002) | Out-Null  # NOTOPMOST
Write-Output "captured $($w)x$($hh) -> $OutPng"
