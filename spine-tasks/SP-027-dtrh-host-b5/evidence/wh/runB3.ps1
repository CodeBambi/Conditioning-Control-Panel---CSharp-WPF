# SP-027 Run B3 (DISPLAY3 convention; rect-persistence BINDING): graceful-exit TIMEOUT
# path — fx-drive injects a page `exit` through the REAL dispatch path with NO real
# wind-down behind it → the bounded exit-done wait (1200ms, WPF :880) expires →
# watchdog-FORCED close, exit 0. The wedged-mid-shutdown cell of the exit matrix.
$ErrorActionPreference = "Stop"
$root = "E:\Code\Conditioning-Control-Panel\.worktrees\spine-20260722T051051\lane-1"
$ev = "$root\spine-tasks\SP-027-dtrh-host-b5\evidence\wh"
$exe = "$root\client\src\CcpClient.Desktop\bin\Debug\net10.0\CcpClient.Desktop.exe"
$tx = "$ev\runB3-drive.log"
"" | Out-File $tx
pwsh -NoProfile -File "$ev\prep.ps1" *>&1 | Out-File $tx -Append

$proc = Start-Process $exe -PassThru -ArgumentList "--dtrh-demo --dtrh-quick --dtrh-fx-drive exit@12" `
  -RedirectStandardError "$ev\runB3.log" -RedirectStandardOutput "$ev\runB3.out.log"
Write-Output "launched pid=$($proc.Id)" | Out-File $tx -Append

$live = $false
for ($i = 0; $i -lt 14; $i++) {
  Start-Sleep -Seconds 1
  if ((Get-Content "$ev\runB3.log" -Raw -ErrorAction SilentlyContinue) -match "ENGINE LIVE") { $live = $true; break }
}
"engine live observed: $live" | Out-File $tx -Append
pwsh -NoProfile -File "$ev\drive.ps1" -Action capture -Arg "$ev\runB3-live.png" *>&1 | Out-File $tx -Append

$proc.WaitForExit(60000) | Out-Null
$proc.Refresh()
"EXIT=$($proc.ExitCode)" | Out-File $tx -Append
Write-Output "EXIT=$($proc.ExitCode) live=$live"
