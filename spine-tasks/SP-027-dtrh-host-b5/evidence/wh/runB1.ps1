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

# Real ESC-hold on the focused game window (drive.ps1 keyhold = keybd_event).
pwsh -NoProfile -File "$ev\drive.ps1" -Action keyhold -Arg "1500" -NoMove *>&1 | Out-File $tx -Append

$proc.WaitForExit(30000) | Out-Null
$proc.Refresh()
"EXIT=$($proc.ExitCode)" | Out-File $tx -Append
Write-Output "EXIT=$($proc.ExitCode) live=$live"
