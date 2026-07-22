# SP-027 B1 diagnostic v2: one app launch, two ESC-hold attempts in sequence.
# Attempt 1: CHILD pwsh drive.ps1 keyhold (fixed P/Invoke, topmost raise).
# Attempt 2 (if still alive): PARENT-inline SetForegroundWindow + keybd_event (diag winner).
$ErrorActionPreference = "Stop"
$root = "E:\Code\Conditioning-Control-Panel\.worktrees\spine-20260722T051051\lane-1"
$ev = "$root\spine-tasks\SP-027-dtrh-host-b5\evidence\wh"
$exe = "$root\client\src\CcpClient.Desktop\bin\Debug\net10.0\CcpClient.Desktop.exe"

$proc = Start-Process $exe -PassThru -ArgumentList "--dtrh-demo --dtrh-quick" `
  -RedirectStandardError "$ev\diag2.log" -RedirectStandardOutput "$ev\diag2.out.log"
Write-Output "launched pid=$($proc.Id)"
try {
  for ($i = 0; $i -lt 30; $i++) {
    Start-Sleep -Seconds 1
    if ((Get-Content "$ev\diag2.log" -Raw -ErrorAction SilentlyContinue) -match "ENGINE LIVE") { break }
  }
  pwsh -NoProfile -File "$ev\drive.ps1" -Action capture -Arg "$ev\diag2-live.png" *>&1 | Write-Output

  Add-Type -TypeDefinition @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class W5 {
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
  $cb = [W5+EnumProc]{
    param($hh, $l)
    $wp = 0; [W5]::GetWindowThreadProcessId($hh, [ref]$wp) | Out-Null
    if ($wp -eq $tpid -and [W5]::IsWindowVisible($hh)) {
      $sb = New-Object System.Text.StringBuilder 512
      [W5]::GetWindowText($hh, $sb, 512) | Out-Null
      if ($sb.ToString() -like "*Down the Rabbit Hole*") { $script:h = $hh; return $false }
    }
    return $true
  }

  # --- Attempt 1: child drive.ps1 keyhold (with its own focus claim) ---
  pwsh -NoProfile -File "$ev\drive.ps1" -Action keyhold -Arg "1500" -NoMove *>&1 | Write-Output
  $exited = $proc.WaitForExit(8000)
  Write-Output "ATTEMPT 1 (child keyhold): exited=$exited"

  if (-not $exited) {
    # --- Attempt 2: parent-inline, diag winning path ---
    [W5]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null
    Write-Output ("target hwnd=$h foreground=" + [W5]::GetForegroundWindow())
    [W5]::SetForegroundWindow($h) | Out-Null
    Start-Sleep -Milliseconds 400
    Write-Output ("foreground after SFW=" + [W5]::GetForegroundWindow())
    [W5]::keybd_event(0x1B, 0x01, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 1500
    [W5]::keybd_event(0x1B, 0x01, 2, [UIntPtr]::Zero)
    $exited2 = $proc.WaitForExit(8000)
    Write-Output "ATTEMPT 2 (parent-inline SFW+keys): exited=$exited2"
  }
} finally {
  if (-not $proc.HasExited) { $proc.Kill($true); $proc.WaitForExit(10000) | Out-Null; Write-Output "killed orphan" }
}
