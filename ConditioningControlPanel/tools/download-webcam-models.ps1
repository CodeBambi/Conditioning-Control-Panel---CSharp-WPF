<#
.SYNOPSIS
    Downloads ONNX models for the webcam tracking feature.

.DESCRIPTION
    Pulls the three ONNX models used by Services/WebcamTrackingService.cs
    into Resources/Models/. These are upstream community exports of Google's
    open-source MediaPipe models.

    PRIVACY NOTE: This script is for DEVELOPER BUILD TIME ONLY. The shipped
    application NEVER downloads anything — models are bundled into the
    installer. End users have zero network involvement with this feature.

    The script:
      1. Skips files that already exist (idempotent — safe to re-run).
      2. Downloads from upstream URLs.
      3. Verifies the file is at least a sane minimum size.
      4. Computes and prints the SHA256 of each downloaded file.
      5. Writes a .sha256 sidecar so future runs can detect upstream changes.

    URL STABILITY DISCLAIMER:
      These are best-effort URLs as of 2026-04-26. Upstream community repos
      (PINTO_model_zoo, Hugging Face, etc.) reorganize occasionally. If a
      download 404s, edit the $models array below to point at a current
      mirror. See Resources/Models/README.md for alternative sources.

.PARAMETER Force
    Re-download even if files already exist.

.PARAMETER VerifyOnly
    Skip downloads. Just print SHA256 of existing files for manual cross-check.

.EXAMPLE
    .\tools\download-webcam-models.ps1
    .\tools\download-webcam-models.ps1 -Force
    .\tools\download-webcam-models.ps1 -VerifyOnly
#>

[CmdletBinding()]
param(
    [switch]$Force,
    [switch]$VerifyOnly
)

$ErrorActionPreference = 'Stop'

# Resolve target directory relative to this script.
$projectRoot = Split-Path -Parent $PSScriptRoot
$modelsDir = Join-Path $projectRoot 'Resources\Models'

if (-not (Test-Path $modelsDir)) {
    New-Item -ItemType Directory -Path $modelsDir -Force | Out-Null
}

# Best-effort upstream URLs. Update if any 404 — see README.md for guidance.
# The capture pipeline expects exactly these filenames in Resources/Models/.
$models = @(
    [PSCustomObject]@{
        Name        = 'blazeface.onnx'
        Description = 'BlazeFace short-range face detector (~200 KB)'
        Url         = 'https://github.com/PINTO0309/PINTO_model_zoo/raw/main/030_BlazeFace/01_short_range_model/saved_model_192x192/openvino/FP32/face_detection_short_range_192x192.onnx'
        MinSize     = 100KB
        FallbackUrls = @()
    },
    [PSCustomObject]@{
        Name        = 'face_mesh.onnx'
        Description = 'MediaPipe FaceMesh — 468 face landmarks (~3 MB)'
        Url         = 'https://github.com/PINTO0309/PINTO_model_zoo/raw/main/032_FaceMesh/06_landmark/saved_model_192x192/openvino/FP32/face_mesh_192x192.onnx'
        MinSize     = 1MB
        FallbackUrls = @()
    },
    [PSCustomObject]@{
        Name        = 'iris.onnx'
        Description = 'MediaPipe Iris — 5-point iris landmarks per eye (~1.5 MB)'
        Url         = 'https://github.com/PINTO0309/PINTO_model_zoo/raw/main/033_Iris/03_landmark/saved_model_64x64/openvino/FP32/iris_landmark_64x64.onnx'
        MinSize     = 500KB
        FallbackUrls = @()
    }
)

function Get-FileSha256 {
    param([string]$Path)
    if (-not (Test-Path $Path)) { return $null }
    $hash = Get-FileHash -Path $Path -Algorithm SHA256
    return $hash.Hash.ToLower()
}

function Format-Size {
    param([long]$Bytes)
    if ($Bytes -ge 1MB) { return "{0:N2} MB" -f ($Bytes / 1MB) }
    if ($Bytes -ge 1KB) { return "{0:N2} KB" -f ($Bytes / 1KB) }
    return "$Bytes B"
}

function Try-Download {
    param(
        [string]$Url,
        [string]$Destination,
        [long]$MinSize
    )
    $tempPath = "$Destination.partial"
    try {
        Write-Host "  → $Url" -ForegroundColor DarkGray
        Invoke-WebRequest -Uri $Url -OutFile $tempPath -UseBasicParsing -ErrorAction Stop
        $size = (Get-Item $tempPath).Length
        if ($size -lt $MinSize) {
            Remove-Item $tempPath -Force -ErrorAction SilentlyContinue
            Write-Host "    Got $(Format-Size $size) — under expected minimum $(Format-Size $MinSize). Likely a redirect/HTML page, not the model." -ForegroundColor Yellow
            return $false
        }
        Move-Item -Path $tempPath -Destination $Destination -Force
        return $true
    }
    catch {
        Remove-Item $tempPath -Force -ErrorAction SilentlyContinue
        Write-Host "    Failed: $($_.Exception.Message)" -ForegroundColor Yellow
        return $false
    }
}

Write-Host ""
Write-Host "Webcam tracking models — downloader" -ForegroundColor Cyan
Write-Host "Target: $modelsDir" -ForegroundColor Gray
Write-Host ""

$failures = @()

foreach ($m in $models) {
    $target = Join-Path $modelsDir $m.Name
    Write-Host ("[ {0} ] {1}" -f $m.Name, $m.Description) -ForegroundColor White

    if ($VerifyOnly) {
        if (Test-Path $target) {
            $hash = Get-FileSha256 $target
            $size = (Get-Item $target).Length
            Write-Host "  Present:  $(Format-Size $size)  sha256=$hash" -ForegroundColor Green
        } else {
            Write-Host "  Missing." -ForegroundColor Red
            $failures += $m.Name
        }
        Write-Host ""
        continue
    }

    if ((Test-Path $target) -and (-not $Force)) {
        $size = (Get-Item $target).Length
        $hash = Get-FileSha256 $target
        Write-Host "  Already present: $(Format-Size $size)  sha256=$hash  (use -Force to re-download)" -ForegroundColor Green
        Write-Host ""
        continue
    }

    $ok = Try-Download -Url $m.Url -Destination $target -MinSize $m.MinSize

    if (-not $ok) {
        foreach ($fb in $m.FallbackUrls) {
            Write-Host "  Trying fallback…" -ForegroundColor DarkGray
            $ok = Try-Download -Url $fb -Destination $target -MinSize $m.MinSize
            if ($ok) { break }
        }
    }

    if (-not $ok) {
        Write-Host "  FAILED. See Resources\Models\README.md for sourcing alternatives — drop the file at:" -ForegroundColor Red
        Write-Host "    $target" -ForegroundColor Red
        $failures += $m.Name
        Write-Host ""
        continue
    }

    $size = (Get-Item $target).Length
    $hash = Get-FileSha256 $target
    Write-Host "  Downloaded: $(Format-Size $size)  sha256=$hash" -ForegroundColor Green

    # Sidecar for future runs to detect upstream changes.
    Set-Content -Path "$target.sha256" -Value $hash -Encoding ASCII

    Write-Host ""
}

if ($failures.Count -eq 0) {
    Write-Host "All three models in place. Resources\Models\ is ready for the build." -ForegroundColor Green
    Write-Host ""
    Write-Host "Verify upstream-published hashes against the values printed above before committing." -ForegroundColor Yellow
} else {
    Write-Host "Missing or failed:" -ForegroundColor Red
    foreach ($f in $failures) { Write-Host "  - $f" -ForegroundColor Red }
    Write-Host ""
    Write-Host "Action: open Resources\Models\README.md and follow one of the listed alternative sources." -ForegroundColor Yellow
    exit 1
}
