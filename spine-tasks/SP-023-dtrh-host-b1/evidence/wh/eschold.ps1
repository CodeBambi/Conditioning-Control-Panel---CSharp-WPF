# SP-023 ESC-hold at the DTRH host window (secondary toplevel — enum like capture.ps1).
param(
  [string]$TitleLike = "DTRH host",
  [string]$ProcessName = "CcpClient.Desktop",
  [int]$EscHoldMs = 1500
)
Add-Type -TypeDefinition @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class Win32Input {
  public delegate bool EnumProc(System.IntPtr h, System.IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, System.IntPtr l);
  [DllImport("user32.dll")] public static extern int GetWindowText(System.IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(System.IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(System.IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(System.IntPtr h);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
}
"@
$proc = Get-Process $ProcessName | Select-Object -First 1
if (-not $proc) { Write-Error "no process $ProcessName"; exit 1 }
$targetPid = $proc.Id
$found = [IntPtr]::Zero
$cb = [Win32Input+EnumProc]{
  param($h, $l)
  $wpid = 0
  [Win32Input]::GetWindowThreadProcessId($h, [ref]$wpid) | Out-Null
  if ($wpid -eq $targetPid -and [Win32Input]::IsWindowVisible($h)) {
    $sb = New-Object System.Text.StringBuilder 512
    [Win32Input]::GetWindowText($h, $sb, 512) | Out-Null
    if ($sb.ToString() -like "*$TitleLike*") { $script:found = $h; return $false }
  }
  return $true
}
[Win32Input]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null
if ($found -eq [IntPtr]::Zero) { Write-Error "no window like '$TitleLike'"; exit 1 }
[Win32Input]::SetForegroundWindow($found) | Out-Null
Start-Sleep -Milliseconds 400
[Win32Input]::keybd_event(0x1B, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds $EscHoldMs
[Win32Input]::keybd_event(0x1B, 0, 2, [UIntPtr]::Zero)
Write-Output "esc held $($EscHoldMs)ms at window $found"
