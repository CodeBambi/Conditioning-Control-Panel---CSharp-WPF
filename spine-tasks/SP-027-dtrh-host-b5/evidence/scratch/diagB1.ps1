# SP-027 B1 diagnostic: what does foreground actually do? One bounded probe run.
$ErrorActionPreference = "Stop"
$root = "E:\Code\Conditioning-Control-Panel\.worktrees\spine-20260722T051051\lane-1"
$ev = "$root\spine-tasks\SP-027-dtrh-host-b5\evidence\wh"
$exe = "$root\client\src\CcpClient.Desktop\bin\Debug\net10.0\CcpClient.Desktop.exe"

$proc = Start-Process $exe -PassThru -ArgumentList "--dtrh-demo --dtrh-quick" `
  -RedirectStandardError "$ev\diag.log" -RedirectStandardOutput "$ev\diag.out.log"
Write-Output "launched pid=$($proc.Id)"
try {
  for ($i = 0; $i -lt 30; $i++) {
    Start-Sleep -Seconds 1
    if ((Get-Content "$ev\diag.log" -Raw -ErrorAction SilentlyContinue) -match "ENGINE LIVE") { break }
  }
  # move + rect via drive.ps1 (also gives the rect line)
  pwsh -NoProfile -File "$ev\drive.ps1" -Action capture -Arg "$ev\diag-live.png" *>&1 | Write-Output

  Add-Type -TypeDefinition @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class W4 {
  public delegate bool EnumProc(System.IntPtr h, System.IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, System.IntPtr l);
  [DllImport("user32.dll")] public static extern int GetWindowText(System.IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(System.IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(System.IntPtr h);
  [DllImport("user32.dll")] public static extern System.IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(System.IntPtr h);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte s, uint f, UIntPtr e);
}
"@
  $tpid = $proc.Id
  $h = [IntPtr]::Zero
  $cb = [W4+EnumProc]{
    param($hh, $l)
    $wp = 0; [W4]::GetWindowThreadProcessId($hh, [ref]$wp) | Out-Null
    if ($wp -eq $tpid -and [W4]::IsWindowVisible($hh)) {
      $sb = New-Object System.Text.StringBuilder 512
      [W4]::GetWindowText($hh, $sb, 512) | Out-Null
      if ($sb.ToString() -like "*Down the Rabbit Hole*") { $script:h = $hh; return $false }
    }
    return $true
  }
  [W4]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null
  Write-Output "target hwnd=$h"
  Write-Output ("foreground BEFORE anything: " + [W4]::GetForegroundWindow())

  # step 1: canvas click via drive.ps1 (same as runB1)
  pwsh -NoProfile -File "$ev\drive.ps1" -Action clickrel -Arg "648,420" -NoMove *>&1 | Write-Output
  Start-Sleep -Milliseconds 500
  Write-Output ("foreground AFTER click: " + [W4]::GetForegroundWindow())

  # step 2: SetForegroundWindow attempt (spike W16 path)
  $ok = [W4]::SetForegroundWindow($h)
  Start-Sleep -Milliseconds 400
  Write-Output ("SetForegroundWindow returned $ok; foreground NOW: " + [W4]::GetForegroundWindow())

  # step 3: ESC hold with scancode 0x01
  [W4]::keybd_event(0x1B, 0x01, 0, [UIntPtr]::Zero)
  Start-Sleep -Milliseconds 1500
  [W4]::keybd_event(0x1B, 0x01, 2, [UIntPtr]::Zero)
  Write-Output "ESC held 1500ms (vk+scan)"
  $exited = $proc.WaitForExit(15000)
  Write-Output "exited after ESC: $exited"
} finally {
  if (-not $proc.HasExited) { $proc.Kill($true); $proc.WaitForExit(10000) | Out-Null; Write-Output "killed orphan" }
}
