# SP-025 Run D (pre-completion consult fix-first A): the VOICE half of world freeze on the
# real backend — a staged long whisper (18.9s) spans the freeze window; voice position
# must freeze + resume; PlaybackEnded must arrive AFTER resume (never wedged).
$ErrorActionPreference = "Stop"
$root = "E:\Code\Conditioning-Control-Panel\.worktrees\spine-20260721T225836\lane-1"
$ev = "$root\spine-tasks\SP-025-dtrh-host-b3\evidence\wh"
$exe = "$root\client\src\CcpClient.Desktop\bin\Debug\net10.0\CcpClient.Desktop.exe"
$drive = "whisper-file:sub_evidence.mp3@4; freeze:on@7; freeze:off@13"
$proc = Start-Process $exe -PassThru -ArgumentList "--dtrh-demo --dtrh-quick --dtrh-fx-drive `"$drive`" --dtrh-auto-close 30" -RedirectStandardError "$ev\runD.log" -RedirectStandardOutput "$ev\runD.out.log"
$proc.WaitForExit(50000) | Out-Null
$proc.Refresh()
Write-Output "EXIT=$($proc.ExitCode)"
