# SP-025 Run A driver (owner DISPLAY3 convention): fx-drive SFX + whisper + real-media
# video + freeze/unwedge evidence. Launches the app, positions both windows on DISPLAY3
# (GetWindowRect-verified before EVERY capture), captures the video window
# before/during/after freeze, waits for auto-close, reports EXIT CODE (foreground wait).
$ErrorActionPreference = "Stop"
$root = "E:\Code\Conditioning-Control-Panel\.worktrees\spine-20260721T225836\lane-1"
$ev = "$root\spine-tasks\SP-025-dtrh-host-b3\evidence\wh"
$exe = "$root\client\src\CcpClient.Desktop\bin\Debug\net10.0\CcpClient.Desktop.exe"
$drive = "sfx:wave_clear@4; sfx:ripple_cast@6; sfx:Pop@7; payload:audio@9; video-file:evidence-video-04.mp4@13; freeze:on@18; freeze:off@26; run-ended@28; payload:audio@30; video-file:evidence-video-04.mp4@34; freeze:on@37"

# ONE pre-quoted argument string: an -ArgumentList ARRAY space-joins WITHOUT quoting and
# the drive script splits at '; ' (run A attempt 1: only the first step survived).
$proc = Start-Process $exe -PassThru -ArgumentList "--dtrh-demo --dtrh-quick --dtrh-fx-drive `"$drive`" --dtrh-auto-close 42" -RedirectStandardError "$ev\runA.log" -RedirectStandardOutput "$ev\runA.out.log"
Write-Output "launched pid=$($proc.Id)"
Start-Sleep -Seconds 6

# Host window onto DISPLAY3 (non-modal; SetWindowPos move for captures is the b2 norm).
pwsh -NoProfile -File "$ev\drive.ps1" -Action capture -Arg "$ev\runA-host-live.png" -TitleLike "Down the Rabbit Hole"

# Video window appears at ~13s; move + capture frozen frames (19s, 23s) and resumed (28s).
Start-Sleep -Seconds 8   # t≈14
pwsh -NoProfile -File "$ev\drive.ps1" -Action capture -Arg "$ev\runA-video-playing.png" -TitleLike "DTRH video"
Start-Sleep -Seconds 5   # t≈19 (frozen since ~18)
pwsh -NoProfile -File "$ev\drive.ps1" -Action capture -Arg "$ev\runA-video-frozen-a.png" -TitleLike "DTRH video" -NoMove
Start-Sleep -Seconds 4   # t≈23 (still frozen)
pwsh -NoProfile -File "$ev\drive.ps1" -Action capture -Arg "$ev\runA-video-frozen-b.png" -TitleLike "DTRH video" -NoMove
Start-Sleep -Seconds 5   # t≈28 (resumed at 26)
pwsh -NoProfile -File "$ev\drive.ps1" -Action capture -Arg "$ev\runA-video-resumed.png" -TitleLike "DTRH video" -NoMove
# second video at 34 + freeze at 37 + auto-close at 42 → teardown mid-freeze unwedge.
Start-Sleep -Seconds 9   # t≈37.5 — second video up, freezing
pwsh -NoProfile -File "$ev\drive.ps1" -Action capture -Arg "$ev\runA-video2-frozen.png" -TitleLike "DTRH video" -NoMove

$proc.WaitForExit(60000) | Out-Null
$proc.Refresh()
Write-Output "EXIT=$($proc.ExitCode)"
