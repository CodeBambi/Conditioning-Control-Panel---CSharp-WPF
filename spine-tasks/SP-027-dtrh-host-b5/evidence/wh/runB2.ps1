# SP-027 Run B2 (DISPLAY3 convention; rect-persistence BINDING): graceful-exit
# HOST-INITIATED wind-down — auto-close fires Close() on a live page → end-run posted →
# the REAL page shuts down and answers exit-done → host closes on the fast path, exit 0.
$ErrorActionPreference = "Stop"
$root = "E:\Code\Conditioning-Control-Panel\.worktrees\spine-20260722T051051\lane-1"
$ev = "$root\spine-tasks\SP-027-dtrh-host-b5\evidence\wh"
$exe = "$root\client\src\CcpClient.Desktop\bin\Debug\net10.0\CcpClient.Desktop.exe"
$tx = "$ev\runB2-drive.log"
"" | Out-File $tx
pwsh -NoProfile -File "$ev\prep.ps1" *>&1 | Out-File $tx -Append

$proc = Start-Process $exe -PassThru -ArgumentList "--dtrh-demo --dtrh-quick --dtrh-auto-close 15" `
  -RedirectStandardError "$ev\runB2.log" -RedirectStandardOutput "$ev\runB2.out.log"
Write-Output "launched pid=$($proc.Id)" | Out-File $tx -Append

$live = $false
for ($i = 0; $i -lt 16; $i++) {
  Start-Sleep -Seconds 1
  if ((Get-Content "$ev\runB2.log" -Raw -ErrorAction SilentlyContinue) -match "ENGINE LIVE") { $live = $true; break }
}
"engine live observed: $live" | Out-File $tx -Append
pwsh -NoProfile -File "$ev\drive.ps1" -Action capture -Arg "$ev\runB2-live.png" *>&1 | Out-File $tx -Append

$exited = $proc.WaitForExit(60000)
$proc.Refresh()
if (-not $exited) { "FAIL: no exit within 60s — killing orphan pid=$($proc.Id)" | Out-File $tx -Append; $proc.Kill($true); $proc.WaitForExit(10000) | Out-Null }
"EXIT=$($proc.ExitCode)" | Out-File $tx -Append
Write-Output "EXIT=$($proc.ExitCode) live=$live exited=$exited"
