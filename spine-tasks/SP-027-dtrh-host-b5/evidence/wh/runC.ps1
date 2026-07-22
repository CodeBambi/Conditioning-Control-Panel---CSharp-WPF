# SP-027 Run C (DISPLAY3 convention; rect-persistence BINDING): blocked-route (W18
# class) injection — --dtrh-block-route /umedia/ makes the loopback answer 403 (typed,
# HARNESS-logged) for the media fetch the fx-drive probe-missing-media drives; the page
# reports the typed load failure and SURVIVES; exit 0 via graceful close.
$ErrorActionPreference = "Stop"
$root = "E:\Code\Conditioning-Control-Panel\.worktrees\spine-20260722T051051\lane-1"
$ev = "$root\spine-tasks\SP-027-dtrh-host-b5\evidence\wh"
$exe = "$root\client\src\CcpClient.Desktop\bin\Debug\net10.0\CcpClient.Desktop.exe"
$tx = "$ev\runC-drive.log"
"" | Out-File $tx
pwsh -NoProfile -File "$ev\prep.ps1" *>&1 | Out-File $tx -Append

$proc = Start-Process $exe -PassThru -ArgumentList "--dtrh-demo --dtrh-quick --dtrh-page probe.html --dtrh-block-route /umedia/ --dtrh-fx-drive probe-missing-media@10 --dtrh-auto-close 18" `
  -RedirectStandardError "$ev\runC.log" -RedirectStandardOutput "$ev\runC.out.log"
Write-Output "launched pid=$($proc.Id)" | Out-File $tx -Append
Start-Sleep -Seconds 6
pwsh -NoProfile -File "$ev\drive.ps1" -Action capture -Arg "$ev\runC-live.png" *>&1 | Out-File $tx -Append

$exited = $proc.WaitForExit(60000)
$proc.Refresh()
if (-not $exited) { "FAIL: no exit within 60s — killing orphan pid=$($proc.Id)" | Out-File $tx -Append; $proc.Kill($true); $proc.WaitForExit(10000) | Out-Null }
"EXIT=$($proc.ExitCode)" | Out-File $tx -Append
Write-Output "EXIT=$($proc.ExitCode) exited=$exited"
