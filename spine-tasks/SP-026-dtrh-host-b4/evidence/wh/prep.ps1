# SP-026 shared prep (owner DISPLAY3 convention; rect-persistence BINDING).
# ResetStage "full"  : back up + reset the DTRH data dir (slots/index/asset-stats/Spirals/user media)
# ResetStage "media" : stage user media only (slot state preserved)
# Media copies come from packet evidence scratch (neutral names; the owner media dir is
# named only in the record — committed scripts never reference it).
param(
  [Parameter(Mandatory=$true)][string]$Mode   # full | media | loom
)
$ErrorActionPreference = "Stop"
$root = "E:\Code\Conditioning-Control-Panel\.worktrees\spine-20260722T020943\lane-1"
$scratch = "$root\spine-tasks\SP-026-dtrh-host-b4\evidence\scratch"
$data = "$env:APPDATA\CcpClient"
$overlay = "$root\client\src\CcpClient.Desktop\bin\Debug\net10.0\payload-overlay"

New-Item -ItemType Directory -Force -Path $data | Out-Null

if ($Mode -eq "full") {
  # Back up any prior state, then reset every DTRH-owned artifact.
  $backup = "$scratch\databackup-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
  New-Item -ItemType Directory -Force -Path $backup | Out-Null
  foreach ($item in @("dtrh_slot1.json", "dtrh_slot2.json", "dtrh_slot3.json", "dtrh_slots.json",
                      "dtrh_asset_stats.json", "Spirals", "assets")) {
    $p = Join-Path $data $item
    if (Test-Path $p) { Move-Item $p $backup -Force }
  }
  Write-Output "data dir reset (backup at scratch)"
}

if ($Mode -eq "full" -or $Mode -eq "loom") {
  # Stage the loom GIF into the RUN-TIME overlay (product code reads only the overlay).
  New-Item -ItemType Directory -Force -Path "$overlay\loom" | Out-Null
  Copy-Item "$scratch\evidence-loom-01.gif" "$overlay\loom\evidence-loom-01.gif" -Force
  Write-Output "loom GIF staged into run-time overlay"
}

if ($Mode -eq "full" -or $Mode -eq "media") {
  # Stage user media (neutral names) into the user-media folder contract root.
  New-Item -ItemType Directory -Force -Path "$data\assets\images" | Out-Null
  New-Item -ItemType Directory -Force -Path "$data\assets\videos" | Out-Null
  Copy-Item "$scratch\evidence-image-01.jpg" "$data\assets\images\evidence-image-01.jpg" -Force
  Copy-Item "$scratch\evidence-image-02.png" "$data\assets\images\evidence-image-02.png" -Force
  Copy-Item "$scratch\evidence-video-01.mp4" "$data\assets\videos\evidence-video-01.mp4" -Force
  Write-Output "user media staged into data dir assets (2 images + 1 video)"
}
