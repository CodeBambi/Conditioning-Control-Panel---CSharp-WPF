# SP-027 Run D (DISPLAY3 convention; rect-persistence BINDING): missing-media (W19
# class) injection on the probe page — the fx-drive probe-missing-media drives a media
# fetch that 404s (typed failure), the page reports the typed load error and SURVIVES,
# exit 0 via graceful close. (Run C owns the blocked-route 403 cell.)
$ErrorActionPreference = "Stop"
$root = "E:\Code\Conditioning-Control-Panel\.worktrees\spine-20260722T051051\lane-1"
$ev = "$root\spine-tasks\SP-027-dtrh-host-b5\evidence\wh"
$exe = "$root\client\src\CcpClient.Desktop\bin\Debug\net10.0\CcpClient.Desktop.exe"
$tx = "$ev\runD-drive.log"
"" | Out-File $tx
pwsh -NoProfile -File "$ev\prep.ps1" *>&1 | Out-File $tx -Append

# Missing media: the probe fetch 404s (typed, logged) while the page survives.
$proc = Start-Process $exe -PassThru -ArgumentList "--dtrh-demo --dtrh-quick --dtrh-page probe.html --dtrh-fx-drive probe-missing-media@10 --dtrh-auto-close 18" `
  -RedirectStandardError "$ev\runD.log" -RedirectStandardOutput "$ev\runD.out.log"
Write-Output "launched pid=$($proc.Id)" | Out-File $tx -Append
Start-Sleep -Seconds 6
pwsh -NoProfile -File "$ev\drive.ps1" -Action capture -Arg "$ev\runD-live.png" *>&1 | Out-File $tx -Append

$exited = $proc.WaitForExit(60000)
$proc.Refresh()
if (-not $exited) { "FAIL: no exit within 60s — killing orphan pid=$($proc.Id)" | Out-File $tx -Append; $proc.Kill($true); $proc.WaitForExit(10000) | Out-Null }
"EXIT=$($proc.ExitCode)" | Out-File $tx -Append
Write-Output "EXIT=$($proc.ExitCode) exited=$exited"
