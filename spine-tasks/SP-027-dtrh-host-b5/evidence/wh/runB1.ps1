# SP-027 Run B1 (DISPLAY3 convention; rect-persistence BINDING): graceful-exit FAST
# path — real ESC-hold (keybd_event 1500ms; the payload's 1.2s hold-to-exit threshold,
# boot.js; SP-011 W16 shape) → page exit + exit-done back-to-back → host closes BEFORE
# the 1200ms force — exit 0. ESC-path regression check rides this cell.
$ErrorActionPreference = "Stop"
$root = "E:\Code\Conditioning-Control-Panel\.worktrees\spine-20260722T051051\lane-1"
$ev = "$root\spine-tasks\SP-027-dtrh-host-b5\evidence\wh"
$exe = "$root\client\src\CcpClient.Desktop\bin\Debug\net10.0\CcpClient.Desktop.exe"
$tx = "$ev\runB1-drive.log"
"" | Out-File $tx
pwsh -NoProfile -File "$ev\prep.ps1" *>&1 | Out-File $tx -Append

$proc = Start-Process $exe -PassThru -ArgumentList "--dtrh-demo --dtrh-quick" `
  -RedirectStandardError "$ev\runB1.log" -RedirectStandardOutput "$ev\runB1.out.log"
Write-Output "launched pid=$($proc.Id)" | Out-File $tx -Append

$live = $false
for ($i = 0; $i -lt 30; $i++) {
  Start-Sleep -Seconds 1
  if ((Get-Content "$ev\runB1.log" -Raw -ErrorAction SilentlyContinue) -match "ENGINE LIVE") { $live = $true; break }
}
"engine live observed: $live" | Out-File $tx -Append
pwsh -NoProfile -File "$ev\drive.ps1" -Action capture -Arg "$ev\runB1-live.png" *>&1 | Out-File $tx -Append

# FRESH-SLOT CONTRACT (2026-07-22 forensics, cheshireVn.js:484-491 + cheshireGuide.js:355):
# a fresh slot opens the hub_welcome fullscreen VN scene whose capture-phase handler
# swallows ESC (WPF-shared payload — WPF parity, not a port bug). Click the scene
# through first (real user path), THEN drive the hold.
pwsh -NoProfile -File "$ev\drive.ps1" -Action vn-clear -Arg "40" -NoMove *>&1 | Out-File $tx -Append

# Real ESC-hold on the focused game window (drive.ps1 keyhold = real canvas click
# (foreground claim, the only reliable one per diagB1v2) + foreground verify + real
# keybd_event with scancode 0x01). Retried up to 3x: the owner may be actively using
# the machine (foreground races observed), each attempt is LOUD in the transcript.
$exited = $false
for ($attempt = 1; $attempt -le 3 -and -not $exited; $attempt++) {
  "ESC-hold attempt $attempt/3" | Out-File $tx -Append
  pwsh -NoProfile -File "$ev\drive.ps1" -Action keyhold -Arg "1500" -NoMove *>&1 | Out-File $tx -Append
  $exited = $proc.WaitForExit(10000)
  "attempt $attempt exited=$exited" | Out-File $tx -Append
}
$proc.Refresh()
if (-not $exited) {
  # Never leave an orphan (the 08:24 wedge lesson): record the miss LOUD, then kill.
  "FAIL: no exit after 3 ESC-hold attempts — killing orphan pid=$($proc.Id)" | Out-File $tx -Append
  $proc.Kill($true)
  $proc.WaitForExit(10000) | Out-Null
}
"EXIT=$($proc.ExitCode)" | Out-File $tx -Append
Write-Output "EXIT=$($proc.ExitCode) live=$live exited=$exited"
