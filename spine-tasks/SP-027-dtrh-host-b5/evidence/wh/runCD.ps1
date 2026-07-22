# SP-027 Run C+D (DISPLAY3 convention; rect-persistence BINDING): blocked-route (W18
# class) + missing-media (W19 class) injections on the probe page — typed failures
# (403 with CORS-on-errors; 404), the page survives, exit 0 via graceful close.
$ErrorActionPreference = "Stop"
$root = "E:\Code\Conditioning-Control-Panel\.worktrees\spine-20260722T051051\lane-1"
$ev = "$root\spine-tasks\SP-027-dtrh-host-b5\evidence\wh"
$exe = "$root\client\src\CcpClient.Desktop\bin\Debug\net10.0\CcpClient.Desktop.exe"
$tx = "$ev\runCD-drive.log"
"" | Out-File $tx
pwsh -NoProfile -File "$ev\prep.ps1" *>&1 | Out-File $tx -Append

# Blocked media origin: every media fetch 403s (typed, logged) while the page survives.
$proc = Start-Process $exe -PassThru -ArgumentList "--dtrh-demo --dtrh-quick --dtrh-page probe.html --dtrh-block-route /media/ --dtrh-fx-drive probe-missing-media@10 --dtrh-auto-close 18" `
  -RedirectStandardError "$ev\runCD.log" -RedirectStandardOutput "$ev\runCD.out.log"
Write-Output "launched pid=$($proc.Id)" | Out-File $tx -Append
Start-Sleep -Seconds 6
pwsh -NoProfile -File "$ev\drive.ps1" -Action capture -Arg "$ev\runCD-live.png" *>&1 | Out-File $tx -Append

$proc.WaitForExit(60000) | Out-Null
$proc.Refresh()
"EXIT=$($proc.ExitCode)" | Out-File $tx -Append
Write-Output "EXIT=$($proc.ExitCode)"
