# SP-025 Run B driver (owner DISPLAY3 convention): the Warren portal's FIRST click opens
# the intro VN (warren.js:1523-1530 — one-time seenIntroGuide) → vn-speaking + the
# page-rendered tinted VN portrait (cheshireVn.js:84-96/134; §3.2 in-page tint evidence)
# + the mix gate observed on real protocol traffic (fx-drive sfx steps before/during VN).
$ErrorActionPreference = "Stop"
$root = "E:\Code\Conditioning-Control-Panel\.worktrees\spine-20260721T225836\lane-1"
$ev = "$root\spine-tasks\SP-025-dtrh-host-b3\evidence\wh"
$exe = "$root\client\src\CcpClient.Desktop\bin\Debug\net10.0\CcpClient.Desktop.exe"
$drive = "sfx:wave_clear@8; sfx:wave_clear@25; sfx:wave_clear@40"

$proc = Start-Process $exe -PassThru -ArgumentList "--dtrh-demo --dtrh-quick --dtrh-fx-drive `"$drive`" --dtrh-auto-close 55" -RedirectStandardError "$ev\runB.log" -RedirectStandardOutput "$ev\runB.out.log"
Write-Output "launched pid=$($proc.Id)"
Start-Sleep -Seconds 7

# Host window onto DISPLAY3 (non-modal; GetWindowRect verified by the drive before capture).
pwsh -NoProfile -File "$ev\drive.ps1" -Action capture -Arg "$ev\runB-hub.png" -TitleLike "Down the Rabbit Hole"

# Portal click (canvas click on a NON-modal window — topmost raise allowed per the rule).
Start-Sleep -Seconds 3   # t≈10
pwsh -NoProfile -File "$ev\drive.ps1" -Action clickrel -Arg "648,420" -TitleLike "Down the Rabbit Hole" -NoMove
Start-Sleep -Seconds 6   # t≈16 — intro VN should be playing
pwsh -NoProfile -File "$ev\drive.ps1" -Action capture -Arg "$ev\runB-vn-a.png" -TitleLike "Down the Rabbit Hole" -NoMove
Start-Sleep -Seconds 8   # t≈24
pwsh -NoProfile -File "$ev\drive.ps1" -Action capture -Arg "$ev\runB-vn-b.png" -TitleLike "Down the Rabbit Hole" -NoMove
Start-Sleep -Seconds 12  # t≈36
pwsh -NoProfile -File "$ev\drive.ps1" -Action capture -Arg "$ev\runB-vn-c.png" -TitleLike "Down the Rabbit Hole" -NoMove

$proc.WaitForExit(40000) | Out-Null
$proc.Refresh()
Write-Output "EXIT=$($proc.ExitCode)"
