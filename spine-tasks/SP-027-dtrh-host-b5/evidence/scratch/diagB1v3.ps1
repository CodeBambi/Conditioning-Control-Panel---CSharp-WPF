# SP-027 B1 diagnostic v3: A/B isolation. Run 1 = diag#1 exact replay (parent-inline
# SFW + parent keybd_event after child click). Run 2 = child drive.ps1 keyhold.
$ErrorActionPreference = "Stop"
$root = "E:\Code\Conditioning-Control-Panel\.worktrees\spine-20260722T051051\lane-1"
$ev = "$root\spine-tasks\SP-027-dtrh-host-b5\evidence\wh"
$exe = "$root\client\src\CcpClient.Desktop\bin\Debug\net10.0\CcpClient.Desktop.exe"

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class W6 {
  [DllImport("user32.dll")] public static extern System.IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(System.IntPtr h);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte s, uint f, UIntPtr e);
}
"@

function Start-App($tag) {
  $p = Start-Process $exe -PassThru -ArgumentList "--dtrh-demo --dtrh-quick" `
    -RedirectStandardError "$ev\diag3-$tag.log" -RedirectStandardOutput "$ev\diag3-$tag.out.log"
  for ($i = 0; $i -lt 30; $i++) {
    Start-Sleep -Seconds 1
    if ((Get-Content "$ev\diag3-$tag.log" -Raw -ErrorAction SilentlyContinue) -match "ENGINE LIVE") { break }
  }
  pwsh -NoProfile -File "$ev\drive.ps1" -Action capture -Arg "$ev\diag3-$tag.png" *>&1 | Out-Host
  return $p
}
function Find-Hwnd($tpid) {
  Add-Type -TypeDefinition @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class W6e {
  public delegate bool EnumProc(System.IntPtr h, System.IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, System.IntPtr l);
  [DllImport("user32.dll")] public static extern int GetWindowText(System.IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(System.IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(System.IntPtr h);
}
"@ -ErrorAction SilentlyContinue
  $script:found = [IntPtr]::Zero
  $cb = [W6e+EnumProc]{
    param($hh, $l)
    $wp = 0; [W6e]::GetWindowThreadProcessId($hh, [ref]$wp) | Out-Null
    if ($wp -eq $tpid -and [W6e]::IsWindowVisible($hh)) {
      $sb = New-Object System.Text.StringBuilder 512
      [W6e]::GetWindowText($hh, $sb, 512) | Out-Null
      if ($sb.ToString() -like "*Down the Rabbit Hole*") { $script:found = $hh; return $false }
    }
    return $true
  }
  [W6e]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null
  return $script:found
}

# ---- Run 1: diag#1 replay — child click, then PARENT SFW + parent keys ----
$p1 = Start-App "A"
try {
  pwsh -NoProfile -File "$ev\drive.ps1" -Action clickrel -Arg "648,420" -NoMove *>&1 | Write-Output
  Start-Sleep -Milliseconds 500
  $h1 = Find-Hwnd $p1.Id
  Write-Output ("A: hwnd=$h1 fg=" + [W6]::GetForegroundWindow())
  [W6]::SetForegroundWindow($h1) | Out-Null
  Start-Sleep -Milliseconds 400
  Write-Output ("A: fg after SFW=" + [W6]::GetForegroundWindow())
  [W6]::keybd_event(0x1B, 0x01, 0, [UIntPtr]::Zero)
  Start-Sleep -Milliseconds 1500
  [W6]::keybd_event(0x1B, 0x01, 2, [UIntPtr]::Zero)
  $e1 = $p1.WaitForExit(10000)
  Write-Output "RUN A (parent keys): exited=$e1"
} finally { if (-not $p1.HasExited) { $p1.Kill($true); $p1.WaitForExit(10000) | Out-Null } }

# ---- Run 2: child keyhold (current drive.ps1) ----
$p2 = Start-App "B"
try {
  pwsh -NoProfile -File "$ev\drive.ps1" -Action keyhold -Arg "1500" -NoMove *>&1 | Write-Output
  $e2 = $p2.WaitForExit(10000)
  Write-Output "RUN B (child keyhold): exited=$e2"
} finally { if (-not $p2.HasExited) { $p2.Kill($true); $p2.WaitForExit(10000) | Out-Null } }
