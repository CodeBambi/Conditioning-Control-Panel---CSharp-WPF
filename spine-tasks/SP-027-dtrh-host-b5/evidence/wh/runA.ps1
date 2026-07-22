# SP-027 Run A (owner DISPLAY3 convention; rect-persistence BINDING — every drive.ps1
# call's output, incl. the GetWindowRect line, is APPENDED into this committed
# transcript): the W17 renderer-kill injection end-to-end — engine live → HARNESS kill
# (profile-matched msedgewebview2 children) → native ProcessFailed immediate detection
# (+ heartbeat watchdog armed) → relaunch-ONCE (stale-profile recovery on the relaunch
# path) → engine live again → SECOND kill → typed EXHAUSTION → honest close, exit 0.
# Foreground wait for EXIT (SP-024 rule).
$ErrorActionPreference = "Stop"
$root = "E:\Code\Conditioning-Control-Panel\.worktrees\spine-20260722T051051\lane-1"
$ev = "$root\spine-tasks\SP-027-dtrh-host-b5\evidence\wh"
$exe = "$root\client\src\CcpClient.Desktop\bin\Debug\net10.0\CcpClient.Desktop.exe"
$tx = "$ev\runA-drive.log"
"" | Out-File $tx
pwsh -NoProfile -File "$ev\prep.ps1" *>&1 | Out-File $tx -Append

# Safety net only: exhaustion must close long before this fires.
$proc = Start-Process $exe -PassThru -ArgumentList "--dtrh-demo --dtrh-quick --dtrh-kill-renderers --dtrh-auto-close 300" `
  -RedirectStandardError "$ev\runA.log" -RedirectStandardOutput "$ev\runA.out.log"
Write-Output "launched pid=$($proc.Id)" | Out-File $tx -Append

# Wait for engine live, then place on DISPLAY3 + capture the live hub.
$live = $false
for ($i = 0; $i -lt 30; $i++) {
  Start-Sleep -Seconds 1
  if ((Get-Content "$ev\runA.log" -Raw -ErrorAction SilentlyContinue) -match "ENGINE LIVE") { $live = $true; break }
}
"engine live observed: $live" | Out-File $tx -Append
pwsh -NoProfile -File "$ev\drive.ps1" -Action capture -Arg "$ev\runA-live.png" *>&1 | Out-File $tx -Append

# The HARNESS kill fires at engine-live +12s. Watch for the FIRING line, then capture
# as fast as possible (the W17 black surface exists only until recovery — ProcessFailed
# is the IMMEDIATE signal by design, so the black window is short).
for ($i = 0; $i -lt 30; $i++) {
  Start-Sleep -Milliseconds 500
  if ((Get-Content "$ev\runA.log" -Raw -ErrorAction SilentlyContinue) -match "kill-renderers FIRING") { break }
}
Start-Sleep -Milliseconds 800
pwsh -NoProfile -File "$ev\drive.ps1" -Action capture -Arg "$ev\runA-after-kill.png" -NoMove *>&1 | Out-File $tx -Append

# Watch for the relaunch + second engine-live, then the SECOND kill + exhaustion + close.
$relaunched = $false
for ($i = 0; $i -lt 60; $i++) {
  Start-Sleep -Seconds 1
  $log = Get-Content "$ev\runA.log" -Raw -ErrorAction SilentlyContinue
  if ($log -match "relaunching ONCE") { $relaunched = $true; break }
}
"relaunch-once observed: $relaunched" | Out-File $tx -Append
$live2 = $false
for ($i = 0; $i -lt 60; $i++) {
  Start-Sleep -Seconds 1
  $log = Get-Content "$ev\runA.log" -Raw -ErrorAction SilentlyContinue
  if ($log -match "SECOND kill") { $live2 = $true; break }
}
"relaunched engine live (second kill armed) observed: $live2" | Out-File $tx -Append
pwsh -NoProfile -File "$ev\drive.ps1" -Action capture -Arg "$ev\runA-relaunched.png" *>&1 | Out-File $tx -Append

$proc.WaitForExit(120000) | Out-Null
$proc.Refresh()
"EXIT=$($proc.ExitCode)" | Out-File $tx -Append
Write-Output "EXIT=$($proc.ExitCode) live=$live relaunched=$relaunched live2=$live2"
