# SP-023 click+ESC exit driver: finds the DTRH host window, clicks its center (a real
# click is the only foreground-change Windows always honors), verifies the foreground
# window at every step, then holds ESC 1.5s (page's exit path, boot.js:190-203).
param([int]$EscHoldMs = 1500)
Add-Type -TypeDefinition @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class W2 {
  public delegate bool EnumProc(System.IntPtr h, System.IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, System.IntPtr l);
  [DllImport("user32.dll")] public static extern int GetWindowText(System.IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(System.IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(System.IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(System.IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(System.IntPtr h, System.IntPtr after, int x, int y, int cx, int cy, uint flags);
  [DllImport("user32.dll")] public static extern System.IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte s, uint f, UIntPtr e);
  public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
function ForegroundTitle {
  $h = [W2]::GetForegroundWindow()
  $sb = New-Object System.Text.StringBuilder 512
  [W2]::GetWindowText($h, $sb, 512) | Out-Null
  return $sb.ToString()
}
$proc = Get-Process CcpClient.Desktop | Select-Object -First 1
if (-not $proc) { Write-Error "no process"; exit 1 }
$tpid = $proc.Id; $found = [IntPtr]::Zero
$cb = [W2+EnumProc]{
  param($h, $l)
  $wp = 0; [W2]::GetWindowThreadProcessId($h, [ref]$wp) | Out-Null
  if ($wp -eq $tpid -and [W2]::IsWindowVisible($h)) {
    $sb = New-Object System.Text.StringBuilder 512
    [W2]::GetWindowText($h, $sb, 512) | Out-Null
    if ($sb.ToString() -like '*DTRH host*') { $script:found = $h; return $false }
  }
  return $true
}
[W2]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null
if ($found -eq [IntPtr]::Zero) { Write-Error "no DTRH window"; exit 1 }
$r = New-Object W2+RECT; [W2]::GetWindowRect($found, [ref]$r) | Out-Null
$cx = [int](($r.Left + $r.Right) / 2); $cy = [int](($r.Top + $r.Bottom) / 2)
# Raise topmost first: a click lands on whatever is TOP at the point, and the desktop
# currently has another app covering the game (empirical: foreground stayed 'Pal').
[W2]::SetWindowPos($found, [IntPtr]::new(-1), 0, 0, 0, 0, 0x0001 -bor 0x0002 -bor 0x0040) | Out-Null
Start-Sleep -Milliseconds 300
Write-Output "foreground before click: $(ForegroundTitle)"
[W2]::SetCursorPos($cx, $cy) | Out-Null
[W2]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 60; [W2]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 600
Write-Output "foreground after click: $(ForegroundTitle)"
[W2]::keybd_event(0x1B, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds $EscHoldMs; [W2]::keybd_event(0x1B, 0, 2, [UIntPtr]::Zero)
Write-Output "esc held $($EscHoldMs)ms; foreground after esc: $(ForegroundTitle)"
[W2]::SetWindowPos($found, [IntPtr]::new(-2), 0, 0, 0, 0, 0x0001 -bor 0x0002) | Out-Null
