# SP-026 Run A (owner DISPLAY3 convention; rect-persistence BINDING — every drive.ps1
# call's output, incl. the GetWindowRect line, is APPENDED into this run's committed
# transcript): the payload's OWN m2test.js drives the full meta vocabulary + the payout
# round-trip page-originated (init m2Test:true via --dtrh-m2test; the meta engine clones
# in memory — the REAL save must be untouched). Foreground wait for EXIT (SP-024 rule).
$ErrorActionPreference = "Stop"
$root = "E:\Code\Conditioning-Control-Panel\.worktrees\spine-20260722T020943\lane-1"
$ev = "$root\spine-tasks\SP-026-dtrh-host-b4\evidence\wh"
$exe = "$root\client\src\CcpClient.Desktop\bin\Debug\net10.0\CcpClient.Desktop.exe"
$data = "$env:APPDATA\CcpClient"
$tx = "$ev\runA-drive.log"
"" | Out-File $tx

pwsh -NoProfile -File "$ev\prep.ps1" -Mode full *>&1 | Out-File $tx -Append

$proc = Start-Process $exe -PassThru -ArgumentList "--dtrh-demo --dtrh-quick --dtrh-m2test --dtrh-auto-close 80" `
  -RedirectStandardError "$ev\runA.log" -RedirectStandardOutput "$ev\runA.out.log"
Write-Output "launched pid=$($proc.Id)" | Out-File $tx -Append
Start-Sleep -Seconds 8

pwsh -NoProfile -File "$ev\drive.ps1" -Action capture -Arg "$ev\runA-host-live.png" -TitleLike "Down the Rabbit Hole" *>&1 | Out-File $tx -Append

# m2test timeline: ~3s settle + ~16s payloads + ~4s meta walk + ~3s payout. Poll for DONE.
$done = $false
for ($i = 0; $i -lt 24; $i++) {
  Start-Sleep -Seconds 2
  if ((Get-Content "$ev\runA.log" -Raw) -match "M2TEST DONE") { $done = $true; break }
}
"m2test done observed: $done" | Out-File $tx -Append
pwsh -NoProfile -File "$ev\drive.ps1" -Action capture -Arg "$ev\runA-after-m2test.png" -TitleLike "Down the Rabbit Hole" -NoMove *>&1 | Out-File $tx -Append

$proc.WaitForExit(60000) | Out-Null
$proc.Refresh()
"EXIT=$($proc.ExitCode)" | Out-File $tx -Append
Write-Output "EXIT=$($proc.ExitCode)"

# The REAL save must be untouched by the test run (clone discipline): fresh slot doc,
# zero economy, and NO test file (the greenfield clone is memory-only).
if (Test-Path "$data\dtrh_slot1.json") { Copy-Item "$data\dtrh_slot1.json" "$ev\runA-slot1-proof.json" -Force }
"slot1 proof copied" | Out-File $tx -Append
