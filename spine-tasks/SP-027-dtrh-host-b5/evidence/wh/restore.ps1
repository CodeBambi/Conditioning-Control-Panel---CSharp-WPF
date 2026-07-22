# SP-027 restore: put the backed-up DTRH data dir back after the injection runs.
$ErrorActionPreference = "Stop"
$root = "E:\Code\Conditioning-Control-Panel\.worktrees\spine-20260722T051051\lane-1"
$backup = "$root\spine-tasks\SP-027-dtrh-host-b5\evidence\scratch\databackup"
$data = "$env:APPDATA\CcpClient"
if (Test-Path $backup) {
  foreach ($item in @("dtrh_slot1.json", "dtrh_slot2.json", "dtrh_slot3.json", "dtrh_slots.json",
                      "dtrh_asset_stats.json", "Spirals", "assets")) {
    $src = Join-Path $backup $item
    $dst = Join-Path $data $item
    if (Test-Path $src) {
      if (Test-Path $dst) { Remove-Item $dst -Recurse -Force }
      Move-Item $src $dst -Force
    }
  }
  Write-Output "data dir restored from evidence/scratch/databackup"
} else {
  Write-Output "no backup present — nothing restored"
}
