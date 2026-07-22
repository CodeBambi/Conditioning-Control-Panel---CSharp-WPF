# SP-026 Run C (DISPLAY3 convention; rect lines persisted): the request-run → run-config
# → REAL descent with staged user media (presence+shape logs), a background grid
# click-sweep for real gameplay (asset-stats + the b4-gated FREEZE BUBBLE catch — freeze
# bubbles exist only in-run, chaosRun.js:1149), the natural page-originated run-ended →
# payout. Prerequisite: run B banked run 1 on this slot (a fresh slot would deal the
# scripted classroom — treats only, no freeze bubbles).
$ErrorActionPreference = "Stop"
$root = "E:\Code\Conditioning-Control-Panel\.worktrees\spine-20260722T020943\lane-1"
$ev = "$root\spine-tasks\SP-026-dtrh-host-b4\evidence\wh"
$exe = "$root\client\src\CcpClient.Desktop\bin\Debug\net10.0\CcpClient.Desktop.exe"
$data = "$env:APPDATA\CcpClient"
$tx = "$ev\runC-drive.log"
"" | Out-File $tx

# Media only — the slot from run B MUST survive (runsCompleted=1 → normal config deal).
pwsh -NoProfile -File "$ev\prep.ps1" -Mode media *>&1 | Out-File $tx -Append

$proc = Start-Process $exe -PassThru -ArgumentList "--dtrh-demo --dtrh-quick --dtrh-fx-drive `"request-run@8`" --dtrh-auto-close 195" `
  -RedirectStandardError "$ev\runC.log" -RedirectStandardOutput "$ev\runC.out.log"
Write-Output "launched pid=$($proc.Id)" | Out-File $tx -Append
Start-Sleep -Seconds 7

pwsh -NoProfile -File "$ev\drive.ps1" -Action capture -Arg "$ev\runC-hub.png" -TitleLike "Down the Rabbit Hole" *>&1 | Out-File $tx -Append

# Countdown ~4s, then the 150s descent. The sweep runs in the BACKGROUND (repeated
# 6-pass chunks, 8x6 grid @400ms inter-pass); the driver polls the log every 700ms for
# the freeze (3.5s window) and captures immediately.
$sweepJob = Start-Job -ScriptBlock {
  param($ev)
  for ($i = 0; $i -lt 11; $i++) {
    pwsh -NoProfile -File "$ev\drive.ps1" -Action sweep -Arg "6,8,6" -TitleLike "Down the Rabbit Hole" -NoMove
  }
} -ArgumentList $ev

$froze = $false
$frozeAt = $null
for ($t = 0; $t -lt 160; $t++) {
  Start-Sleep -Milliseconds 700
  if ((Get-Content "$ev\runC.log" -Raw) -match "world freeze ON") { $froze = $true; $frozeAt = $t; break }
}
"freeze observed: $froze at poll-tick $frozeAt" | Out-File $tx -Append

if ($froze) {
  pwsh -NoProfile -File "$ev\drive.ps1" -Action capture -Arg "$ev\runC-freeze.png" -TitleLike "Down the Rabbit Hole" -NoMove *>&1 | Out-File $tx -Append
  Start-Sleep -Milliseconds 1200
  pwsh -NoProfile -File "$ev\drive.ps1" -Action capture -Arg "$ev\runC-freeze-b.png" -TitleLike "Down the Rabbit Hole" -NoMove *>&1 | Out-File $tx -Append
}

# Let the descent finish naturally (150s + countdown), then capture the recap.
$remaining = 168 - ($frozeAt ?? 160)
if ($remaining -gt 0) { Start-Sleep -Seconds $remaining }
pwsh -NoProfile -File "$ev\drive.ps1" -Action capture -Arg "$ev\runC-after-run.png" -TitleLike "Down the Rabbit Hole" -NoMove *>&1 | Out-File $tx -Append

Stop-Job $sweepJob -ErrorAction SilentlyContinue
Remove-Job $sweepJob -Force -ErrorAction SilentlyContinue

$proc.WaitForExit(60000) | Out-Null
$proc.Refresh()
"EXIT=$($proc.ExitCode)" | Out-File $tx -Append
Write-Output "EXIT=$($proc.ExitCode)"

if (Test-Path "$data\dtrh_slot1.json") { Copy-Item "$data\dtrh_slot1.json" "$ev\runC-slot1-proof.json" -Force }
"proofs copied" | Out-File $tx -Append
