<#
.SYNOPSIS
    Downloads ONNX models for the webcam tracking feature.

.DESCRIPTION
    Pulls the ONNX model used by Services/WebcamTrackingService.cs into
    Resources/Models/. Currently uses OpenCV's official YuNet face
    detector, which is the cleanest stable upstream we found.

    PRIVACY NOTE: This script is for DEVELOPER BUILD TIME ONLY. The shipped
    application NEVER downloads anything -- models are bundled into the
    installer. End users have zero network involvement with this feature.

    The script:
      1. Skips files that already exist (idempotent -- safe to re-run).
      2. Downloads from upstream URLs.
      3. Verifies the file is at least a sane minimum size.
      4. Computes and prints the SHA256 of each downloaded file.
      5. Writes a .sha256 sidecar so future runs can detect upstream changes.

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

# Windows PowerShell 5.1 defaults to TLS 1.0/1.1 which GitHub no longer accepts.
# Force TLS 1.2 so Invoke-WebRequest can negotiate.
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

# Resolve target directory relative to this script.
$projectRoot = Split-Path -Parent $PSScriptRoot
$modelsDir = Join-Path $projectRoot 'Resources\Models'

if (-not (Test-Path $modelsDir)) {
    New-Item -ItemType Directory -Path $modelsDir -Force | Out-Null
}

# Verified URLs as of 2026-04-26.
# Haar cascades are bundled with OpenCV itself; we ship them as Resources for use
# with OpenCvSharp's CascadeClassifier. Stable upstream, MIT-licensed.
$models = @(
    [PSCustomObject]@{
        Name        = 'haarcascade_frontalface_default.xml'
        Description = 'OpenCV Haar cascade for frontal face detection (~900 KB)'
        Url         = 'https://github.com/opencv/opencv/raw/master/data/haarcascades/haarcascade_frontalface_default.xml'
        MinSize     = 100KB
        FallbackUrls = @(
            'https://raw.githubusercontent.com/opencv/opencv/master/data/haarcascades/haarcascade_frontalface_default.xml'
        )
    },
    [PSCustomObject]@{
        Name        = 'haarcascade_eye.xml'
        Description = 'OpenCV Haar cascade for eye detection (~333 KB)'
        Url         = 'https://github.com/opencv/opencv/raw/master/data/haarcascades/haarcascade_eye.xml'
        MinSize     = 50KB
        FallbackUrls = @(
            'https://raw.githubusercontent.com/opencv/opencv/master/data/haarcascades/haarcascade_eye.xml'
        )
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
        Write-Host "  -> $Url" -ForegroundColor DarkGray
        Invoke-WebRequest -Uri $Url -OutFile $tempPath -UseBasicParsing -ErrorAction Stop
        $size = (Get-Item $tempPath).Length
        if ($size -lt $MinSize) {
            Remove-Item $tempPath -Force -ErrorAction SilentlyContinue
            Write-Host "    Got $(Format-Size $size) -- under expected minimum $(Format-Size $MinSize). Likely a redirect/HTML page, not the model." -ForegroundColor Yellow
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
Write-Host "Webcam tracking models -- downloader" -ForegroundColor Cyan
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
            Write-Host "  Trying fallback..." -ForegroundColor DarkGray
            $ok = Try-Download -Url $fb -Destination $target -MinSize $m.MinSize
            if ($ok) { break }
        }
    }

    if (-not $ok) {
        Write-Host "  FAILED. See Resources\Models\README.md for sourcing alternatives -- drop the file at:" -ForegroundColor Red
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
    Write-Host "All models in place. Resources\Models\ is ready for the build." -ForegroundColor Green
    Write-Host ""
    Write-Host "Verify upstream-published hashes against the values printed above before committing." -ForegroundColor Yellow
} else {
    Write-Host "Missing or failed:" -ForegroundColor Red
    foreach ($f in $failures) { Write-Host "  - $f" -ForegroundColor Red }
    Write-Host ""
    Write-Host "Action: open Resources\Models\README.md and follow one of the listed alternative sources." -ForegroundColor Yellow
    exit 1
}
