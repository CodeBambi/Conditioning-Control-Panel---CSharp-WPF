# SP-026 Run D (DISPLAY3 convention; rect lines persisted): the serve→display proof —
# the probe page renders the SERVED loom GIF + staged user media (2 images + 1 video)
# through the §4 media origin in the same engine that renders the game.
$ErrorActionPreference = "Stop"
$root = "E:\Code\Conditioning-Control-Panel\.worktrees\spine-20260722T020943\lane-1"
$ev = "$root\spine-tasks\SP-026-dtrh-host-b4\evidence\wh"
$exe = "$root\client\src\CcpClient.Desktop\bin\Debug\net10.0\CcpClient.Desktop.exe"
$tx = "$ev\runD-drive.log"
"" | Out-File $tx

$proc = Start-Process $exe -PassThru -ArgumentList "--dtrh-demo --dtrh-quick --dtrh-page probe.html --dtrh-auto-close 30" `
  -RedirectStandardError "$ev\runD.log" -RedirectStandardOutput "$ev\runD.out.log"
Write-Output "launched pid=$($proc.Id)" | Out-File $tx -Append
Start-Sleep -Seconds 9

pwsh -NoProfile -File "$ev\drive.ps1" -Action capture -Arg "$ev\runD-probe-media.png" -TitleLike "Down the Rabbit Hole" *>&1 | Out-File $tx -Append

$proc.WaitForExit(40000) | Out-Null
$proc.Refresh()
"EXIT=$($proc.ExitCode)" | Out-File $tx -Append
Write-Output "EXIT=$($proc.ExitCode)"
