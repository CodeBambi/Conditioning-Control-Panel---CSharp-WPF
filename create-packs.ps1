# Create and Upload Content Packs to BunnyCDN
# Usage: .\create-packs.ps1 -StorageZone "your-zone" -ApiKey "your-key"

param(
    [string]$StorageZone = "ccp-packs",
    [string]$ApiKey = "",
    [string]$SourceDir = "C:\downloads\gifs\gifs",
    [string]$OutputDir = "C:\Projects\Conditioning-Control-Panel---CSharp-WPF\pack-zips",
    [switch]$ZipOnly,
    [switch]$UploadOnly
)

$ErrorActionPreference = "Stop"

# Pack mapping: folder name -> pack id
$PackMapping = @{
    "bambi" = "bambi-core"
    "bimbo" = "bimbo-aesthetic"
    "cock drop" = "cock-drop"
    "empty head" = "empty-head"
    "girl" = "pretty-girl"
    "good girl" = "good-girl"
    "mooo" = "bimbo-moo"
    "toons" = "toon-bimbos"
}

Write-Host "========================================" -ForegroundColor Magenta
Write-Host "Content Pack Creator & Uploader" -ForegroundColor Magenta
Write-Host "========================================" -ForegroundColor Magenta
Write-Host ""

# Create output directory
if (!(Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir | Out-Null
}

# Step 1: Create ZIP files
if (!$UploadOnly) {
    Write-Host "[STEP 1] Creating ZIP files..." -ForegroundColor Cyan
    Write-Host ""

    foreach ($folder in $PackMapping.Keys) {
        $packId = $PackMapping[$folder]
        $sourcePath = Join-Path $SourceDir $folder
        $zipPath = Join-Path $OutputDir "$packId.zip"
        $tempPath = Join-Path $env:TEMP "pack-temp-$packId"

        if (!(Test-Path $sourcePath)) {
            Write-Host "  [SKIP] Folder not found: $folder" -ForegroundColor Yellow
            continue
        }

        Write-Host "  Creating: $packId.zip" -ForegroundColor White

        # Count files
        $files = Get-ChildItem -Path $sourcePath -File -Recurse | Where-Object {
            $_.Extension -match '\.(gif|png|jpg|jpeg|webp|bmp)$'
        }
        Write-Host "    Files: $($files.Count)" -ForegroundColor Gray

        # Create temp structure with images/ folder
        if (Test-Path $tempPath) { Remove-Item -Recurse -Force $tempPath }
        $imagesPath = Join-Path $tempPath "images"
        New-Item -ItemType Directory -Path $imagesPath | Out-Null

        # Copy files to temp/images/
        foreach ($file in $files) {
            Copy-Item $file.FullName -Destination $imagesPath
        }

        # Remove old zip if exists
        if (Test-Path $zipPath) { Remove-Item $zipPath }

        # Create ZIP
        Compress-Archive -Path "$tempPath\*" -DestinationPath $zipPath -CompressionLevel Optimal

        # Get size
        $size = (Get-Item $zipPath).Length
        $sizeMB = [math]::Round($size / 1MB, 2)
        Write-Host "    Size: $sizeMB MB" -ForegroundColor Gray

        # Cleanup temp
        Remove-Item -Recurse -Force $tempPath

        Write-Host "    Done!" -ForegroundColor Green
    }

    Write-Host ""
    Write-Host "ZIP files created in: $OutputDir" -ForegroundColor Green
    Write-Host ""
}

# Step 2: Upload to BunnyCDN
if (!$ZipOnly) {
    if ([string]::IsNullOrEmpty($ApiKey)) {
        Write-Host "[STEP 2] SKIPPED - No API key provided" -ForegroundColor Yellow
        Write-Host "  Run with: -ApiKey 'your-bunny-storage-api-key'" -ForegroundColor Gray
        Write-Host ""
    } else {
        Write-Host "[STEP 2] Uploading to BunnyCDN..." -ForegroundColor Cyan
        Write-Host "  Storage Zone: $StorageZone" -ForegroundColor Gray
        Write-Host ""

        $baseUrl = "https://storage.bunnycdn.com/$StorageZone"
        $headers = @{
            "AccessKey" = $ApiKey
            "Content-Type" = "application/octet-stream"
        }

        # Upload each ZIP
        $zipFiles = Get-ChildItem -Path $OutputDir -Filter "*.zip"
        foreach ($zip in $zipFiles) {
            Write-Host "  Uploading: $($zip.Name)" -ForegroundColor White
            $uploadUrl = "$baseUrl/$($zip.Name)"

            try {
                $fileBytes = [System.IO.File]::ReadAllBytes($zip.FullName)
                $response = Invoke-RestMethod -Uri $uploadUrl -Method Put -Headers $headers -Body $fileBytes
                Write-Host "    Uploaded!" -ForegroundColor Green
            } catch {
                Write-Host "    FAILED: $_" -ForegroundColor Red
            }
        }

        Write-Host ""
        Write-Host "Uploads complete!" -ForegroundColor Green
        Write-Host "Files available at: https://$StorageZone.b-cdn.net/" -ForegroundColor Cyan
    }
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Magenta
Write-Host "Summary" -ForegroundColor Magenta
Write-Host "========================================" -ForegroundColor Magenta

$zips = Get-ChildItem -Path $OutputDir -Filter "*.zip" -ErrorAction SilentlyContinue
if ($zips) {
    $totalSize = ($zips | Measure-Object -Property Length -Sum).Sum
    Write-Host "Total packs: $($zips.Count)" -ForegroundColor White
    Write-Host "Total size: $([math]::Round($totalSize / 1GB, 2)) GB" -ForegroundColor White
    Write-Host ""
    Write-Host "Pack URLs (after upload):" -ForegroundColor Cyan
    foreach ($zip in $zips) {
        Write-Host "  https://$StorageZone.b-cdn.net/$($zip.Name)" -ForegroundColor Gray
    }
}

Write-Host ""
