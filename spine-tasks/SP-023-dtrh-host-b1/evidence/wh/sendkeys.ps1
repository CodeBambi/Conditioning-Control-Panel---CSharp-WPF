# SP-011 real-input sender (SP-007 pattern): keybd_event SendInput-grade keystrokes at the
# focused spike window. Modes: -Key <vk> tap; -EscHoldMs <ms> hold Escape (page's exit path);
# -Click x,y relative to window client area (spike-pointer probe).
param(
  [string]$TitleLike = "CCP*DTRH host",
  [int]$Key = -1,
  [int]$EscHoldMs = -1,
  [int]$ClickX = -1,
  [int]$ClickY = -1
)
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class Win32Input {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
$proc = Get-Process | Where-Object { $_.MainWindowTitle -like "*$TitleLike*" } | Select-Object -First 1
if (-not $proc) { Write-Error "no window titled like '$TitleLike'"; exit 1 }
$h = $proc.MainWindowHandle
[Win32Input]::ShowWindow($h, 9) | Out-Null  # SW_RESTORE
[Win32Input]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 400
if ($EscHoldMs -gt 0) {
  [Win32Input]::keybd_event(0x1B, 0, 0, [UIntPtr]::Zero)          # ESC down
  Start-Sleep -Milliseconds $EscHoldMs
  [Win32Input]::keybd_event(0x1B, 0, 2, [UIntPtr]::Zero)          # ESC up (KEYEVENTF_KEYUP)
  Write-Output "esc held $($EscHoldMs)ms"
}
if ($Key -ge 0) {
  [Win32Input]::keybd_event([byte]$Key, 0, 0, [UIntPtr]::Zero)
  Start-Sleep -Milliseconds 60
  [Win32Input]::keybd_event([byte]$Key, 0, 2, [UIntPtr]::Zero)
  Write-Output "key vk=$Key tapped"
}
if ($ClickX -ge 0 -and $ClickY -ge 0) {
  $r = New-Object Win32Input+RECT
  [Win32Input]::GetWindowRect($h, [ref]$r) | Out-Null
  [Win32Input]::SetCursorPos($r.Left + $ClickX, $r.Top + $ClickY) | Out-Null
  Start-Sleep -Milliseconds 120
  [Win32Input]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)     # LEFTDOWN
  Start-Sleep -Milliseconds 40
  [Win32Input]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)     # LEFTUP
  Write-Output "clicked at $($ClickX),$($ClickY) client"
}
