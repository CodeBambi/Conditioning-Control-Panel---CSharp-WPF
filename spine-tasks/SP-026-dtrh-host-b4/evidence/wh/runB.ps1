# SP-026 Run B (DISPLAY3 convention; rect lines persisted into the transcript):
# meta-gated Cheshire welcome (the b4-unlocked tinted portrait), Loom save through the
# REAL dispatch path (fx-drive loom-file → loom-result → loom-list → file on disk), and
# the deterministic payout banking proof (run-started + run-ended-full → slot document).
$ErrorActionPreference = "Stop"
$root = "E:\Code\Conditioning-Control-Panel\.worktrees\spine-20260722T020943\lane-1"
$ev = "$root\spine-tasks\SP-026-dtrh-host-b4\evidence\wh"
$exe = "$root\client\src\CcpClient.Desktop\bin\Debug\net10.0\CcpClient.Desktop.exe"
$data = "$env:APPDATA\CcpClient"
$tx = "$ev\runB-drive.log"
"" | Out-File $tx

pwsh -NoProfile -File "$ev\prep.ps1" -Mode full *>&1 | Out-File $tx -Append

$drive = "loom-file:evidence-loom-01.gif@10; run-started@17; run-ended-full@21"
$proc = Start-Process $exe -PassThru -ArgumentList "--dtrh-demo --dtrh-quick --dtrh-fx-drive `"$drive`" --dtrh-auto-close 45" `
  -RedirectStandardError "$ev\runB.log" -RedirectStandardOutput "$ev\runB.out.log"
Write-Output "launched pid=$($proc.Id)" | Out-File $tx -Append
Start-Sleep -Seconds 7

# The Cheshire welcome arc fires on the first Warren open WITH the meta snapshot
# (cheshireGuide.ensureInit gates on it) — the b4-unlocked tinted portrait.
pwsh -NoProfile -File "$ev\drive.ps1" -Action capture -Arg "$ev\runB-cheshire-a.png" -TitleLike "Down the Rabbit Hole" *>&1 | Out-File $tx -Append
Start-Sleep -Seconds 4
pwsh -NoProfile -File "$ev\drive.ps1" -Action capture -Arg "$ev\runB-cheshire-b.png" -TitleLike "Down the Rabbit Hole" -NoMove *>&1 | Out-File $tx -Append

# Wait out loom + banking, then final capture.
Start-Sleep -Seconds 14
pwsh -NoProfile -File "$ev\drive.ps1" -Action capture -Arg "$ev\runB-after-banking.png" -TitleLike "Down the Rabbit Hole" -NoMove *>&1 | Out-File $tx -Append

$proc.WaitForExit(45000) | Out-Null
$proc.Refresh()
"EXIT=$($proc.ExitCode)" | Out-File $tx -Append
Write-Output "EXIT=$($proc.ExitCode)"

# File-content proofs: the slot document (payout banking) + the loom store.
if (Test-Path "$data\dtrh_slot1.json") { Copy-Item "$data\dtrh_slot1.json" "$ev\runB-slot1-proof.json" -Force }
if (Test-Path "$data\Spirals\loom_evidence-loom-01.gif") {
  Copy-Item "$data\Spirals\loom_evidence-loom-01.gif" "$ev\runB-loom-gif-proof.gif" -Force
  if (Test-Path "$data\Spirals\loom_evidence-loom-01.json") {
    Copy-Item "$data\Spirals\loom_evidence-loom-01.json" "$ev\runB-loom-sidecar-proof.json" -Force
  }
}
"proofs copied" | Out-File $tx -Append
