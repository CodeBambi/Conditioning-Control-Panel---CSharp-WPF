# SP-025 Run C: VN mix gate on the real backend + SFX pool overflow/drop evidence.
$ErrorActionPreference = "Stop"
$root = "E:\Code\Conditioning-Control-Panel\.worktrees\spine-20260721T225836\lane-1"
$ev = "$root\spine-tasks\SP-025-dtrh-host-b3\evidence\wh"
$exe = "$root\client\src\CcpClient.Desktop\bin\Debug\net10.0\CcpClient.Desktop.exe"
$drive = "vn:on@4; sfx:wave_clear@6; vn:off@8; sfx:Burst@10; sfx:Burst@10.5; sfx:Burst@11; sfx:Burst@11.5; sfx:Burst@12; sfx:Burst@12.5; sfx:Burst@13; sfx:Burst@13.5; sfx:Burst@14; sfx:Burst@14.5"
$proc = Start-Process $exe -PassThru -ArgumentList "--dtrh-demo --dtrh-quick --dtrh-fx-drive `"$drive`" --dtrh-auto-close 26" -RedirectStandardError "$ev\runC.log" -RedirectStandardOutput "$ev\runC.out.log"
$proc.WaitForExit(45000) | Out-Null
$proc.Refresh()
Write-Output "EXIT=$($proc.ExitCode)"
