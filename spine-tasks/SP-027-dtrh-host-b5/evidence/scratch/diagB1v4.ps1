# SP-027 B1 diagnostic v4: does prep.ps1 (data-dir reset) change the ESC-exit outcome?
# Run 1: prep + keyhold. Run 2 (only if 1 fails): no-prep + keyhold.
$ErrorActionPreference = "Stop"
$root = "E:\Code\Conditioning-Control-Panel\.worktrees\spine-20260722T051051\lane-1"
$ev = "$root\spine-tasks\SP-027-dtrh-host-b5\evidence\wh"
$exe = "$root\client\src\CcpClient.Desktop\bin\Debug\net10.0\CcpClient.Desktop.exe"

function Run-Once($tag, [bool]$doPrep) {
  if ($doPrep) { pwsh -NoProfile -File "$ev\prep.ps1" | Out-Host }
  $p = Start-Process $exe -PassThru -ArgumentList "--dtrh-demo --dtrh-quick" `
    -RedirectStandardError "$ev\diag4-$tag.log" -RedirectStandardOutput "$ev\diag4-$tag.out.log"
  try {
    for ($i = 0; $i -lt 30; $i++) {
      Start-Sleep -Seconds 1
      if ((Get-Content "$ev\diag4-$tag.log" -Raw -ErrorAction SilentlyContinue) -match "ENGINE LIVE") { break }
    }
    pwsh -NoProfile -File "$ev\drive.ps1" -Action capture -Arg "$ev\diag4-$tag.png" *>&1 | Out-Host
    pwsh -NoProfile -File "$ev\drive.ps1" -Action keyhold -Arg "1500" -NoMove *>&1 | Out-Host
    $e = $p.WaitForExit(10000)
    Write-Output "RUN $tag (prep=$doPrep): exited=$e"
    return $e
  } finally { if (-not $p.HasExited) { $p.Kill($true); $p.WaitForExit(10000) | Out-Null } }
}

$e1 = Run-Once "PREP" $true
if (-not $e1) { Run-Once "NOPREP" $false | Out-Null }
