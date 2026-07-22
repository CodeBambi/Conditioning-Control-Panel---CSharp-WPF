# SP-027 shared prep (owner DISPLAY3 convention; rect-persistence BINDING).
# Backs up + resets the DTRH data dir (slots/index/asset-stats/Spirals/user media) so
# the injection runs start from a known slot state. restore.ps1 puts it all back.
$ErrorActionPreference = "Stop"
$root = "E:\Code\Conditioning-Control-Panel\.worktrees\spine-20260722T051051\lane-1"
$scratch = "$root\spine-tasks\SP-027-dtrh-host-b5\evidence\scratch"
$data = "$env:APPDATA\CcpClient"

New-Item -ItemType Directory -Force -Path $data | Out-Null
$backup = "$scratch\databackup"
if (Test-Path $backup) { Remove-Item $backup -Recurse -Force }
New-Item -ItemType Directory -Force -Path $backup | Out-Null
foreach ($item in @("dtrh_slot1.json", "dtrh_slot2.json", "dtrh_slot3.json", "dtrh_slots.json",
                    "dtrh_asset_stats.json", "Spirals", "assets")) {
  $p = Join-Path $data $item
  if (Test-Path $p) { Move-Item $p $backup -Force }
}
Write-Output "data dir reset (backup at evidence/scratch/databackup)"
