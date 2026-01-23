# Create Preview Images from Pack ZIPs and Upload to BunnyCDN
# Usage: .\create-previews.ps1 -ApiKey "your-bunny-storage-api-key"

param(
    [string]$StorageZone = "ccp-packs",
    [string]$ApiKey = "",
    [string]$PackZipsDir = "C:\Projects\Conditioning-Control-Panel---CSharp-WPF\pack-zips",
    [string]$PreviewsDir = "C:\Projects\Conditioning-Control-Panel---CSharp-WPF\pack-previews",
    [int]$PreviewWidth = 480,
    [int]$PreviewHeight = 200,
    [switch]$UploadOnly
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

Write-Host "========================================" -ForegroundColor Magenta
Write-Host "Pack Preview Generator" -ForegroundColor Magenta
Write-Host "========================================" -ForegroundColor Magenta
Write-Host ""

# Create previews directory
if (!(Test-Path $PreviewsDir)) {
    New-Item -ItemType Directory -Path $PreviewsDir | Out-Null
}

# Pack IDs to process
$PackIds = @(
    "bambi-core",
    "bimbo-aesthetic",
    "bimbo-moo",
    "cock-drop",
    "empty-head",
    "good-girl",
    "pretty-girl",
    "toon-bimbos"
)

if (!$UploadOnly) {
    Write-Host "[STEP 1] Extracting preview images from ZIPs..." -ForegroundColor Cyan
    Write-Host ""

    foreach ($packId in $PackIds) {
        $zipPath = Join-Path $PackZipsDir "$packId.zip"
        $previewPath = Join-Path $PreviewsDir "$packId.png"

        if (!(Test-Path $zipPath)) {
            Write-Host "  [SKIP] ZIP not found: $packId.zip" -ForegroundColor Yellow
            continue
        }

        Write-Host "  Processing: $packId" -ForegroundColor White

        try {
            # Open ZIP and get first few image files
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            $zip = [System.IO.Compression.ZipFile]::OpenRead($zipPath)

            $imageExtensions = @(".gif", ".png", ".jpg", ".jpeg", ".webp")
            $imageEntries = $zip.Entries | Where-Object {
                $ext = [System.IO.Path]::GetExtension($_.Name).ToLower()
                $imageExtensions -contains $ext -and $_.Length -gt 0
            } | Select-Object -First 10

            if ($imageEntries.Count -eq 0) {
                Write-Host "    No images found in ZIP" -ForegroundColor Yellow
                $zip.Dispose()
                continue
            }

            # Create a collage from up to 4 images
            $collageImages = @()
            $maxImages = [Math]::Min(4, $imageEntries.Count)

            for ($i = 0; $i -lt $maxImages; $i++) {
                $entry = $imageEntries[$i]
                $stream = $entry.Open()
                $memStream = New-Object System.IO.MemoryStream
                $stream.CopyTo($memStream)
                $memStream.Position = 0

                try {
                    $img = [System.Drawing.Image]::FromStream($memStream)
                    $collageImages += $img
                } catch {
                    Write-Host "    Could not load image: $($entry.Name)" -ForegroundColor Gray
                }

                $stream.Dispose()
            }

            $zip.Dispose()

            if ($collageImages.Count -eq 0) {
                Write-Host "    No valid images could be loaded" -ForegroundColor Yellow
                continue
            }

            # Create collage bitmap
            $collage = New-Object System.Drawing.Bitmap($PreviewWidth, $PreviewHeight)
            $graphics = [System.Drawing.Graphics]::FromImage($collage)
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality

            # Fill with dark background
            $graphics.Clear([System.Drawing.Color]::FromArgb(26, 26, 46))

            # Calculate grid layout
            $cols = if ($collageImages.Count -le 2) { $collageImages.Count } else { 2 }
            $rows = [Math]::Ceiling($collageImages.Count / $cols)
            $cellWidth = $PreviewWidth / $cols
            $cellHeight = $PreviewHeight / $rows

            for ($i = 0; $i -lt $collageImages.Count; $i++) {
                $img = $collageImages[$i]
                $col = $i % $cols
                $row = [Math]::Floor($i / $cols)

                $x = $col * $cellWidth
                $y = $row * $cellHeight

                # Scale to fill cell while maintaining aspect ratio
                $scale = [Math]::Max($cellWidth / $img.Width, $cellHeight / $img.Height)
                $newWidth = $img.Width * $scale
                $newHeight = $img.Height * $scale
                $offsetX = ($cellWidth - $newWidth) / 2
                $offsetY = ($cellHeight - $newHeight) / 2

                $destRect = New-Object System.Drawing.RectangleF(($x + $offsetX), ($y + $offsetY), $newWidth, $newHeight)
                $srcRect = New-Object System.Drawing.RectangleF(0, 0, $img.Width, $img.Height)
                $graphics.DrawImage($img, $destRect, $srcRect, [System.Drawing.GraphicsUnit]::Pixel)
            }

            # Save as PNG
            $collage.Save($previewPath, [System.Drawing.Imaging.ImageFormat]::Png)

            # Cleanup
            $graphics.Dispose()
            $collage.Dispose()
            foreach ($img in $collageImages) { $img.Dispose() }

            $size = (Get-Item $previewPath).Length
            Write-Host "    Created: $packId.png ($([Math]::Round($size/1KB, 1)) KB)" -ForegroundColor Green

        } catch {
            Write-Host "    ERROR: $_" -ForegroundColor Red
        }
    }

    Write-Host ""
}

# Step 2: Upload to BunnyCDN
if ([string]::IsNullOrEmpty($ApiKey)) {
    Write-Host "[STEP 2] SKIPPED - No API key provided" -ForegroundColor Yellow
    Write-Host "  Run with: -ApiKey 'your-bunny-storage-api-key'" -ForegroundColor Gray
} else {
    Write-Host "[STEP 2] Uploading to BunnyCDN..." -ForegroundColor Cyan
    Write-Host "  Storage Zone: $StorageZone" -ForegroundColor Gray
    Write-Host ""

    $baseUrl = "https://storage.bunnycdn.com/$StorageZone/previews"
    $headers = @{
        "AccessKey" = $ApiKey
        "Content-Type" = "application/octet-stream"
    }

    $previewFiles = Get-ChildItem -Path $PreviewsDir -Filter "*.png"
    foreach ($file in $previewFiles) {
        Write-Host "  Uploading: $($file.Name)" -ForegroundColor White
        $uploadUrl = "$baseUrl/$($file.Name)"

        try {
            $fileBytes = [System.IO.File]::ReadAllBytes($file.FullName)
            $response = Invoke-RestMethod -Uri $uploadUrl -Method Put -Headers $headers -Body $fileBytes
            Write-Host "    Uploaded!" -ForegroundColor Green
        } catch {
            Write-Host "    FAILED: $_" -ForegroundColor Red
        }
    }
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Magenta
Write-Host "Done!" -ForegroundColor Magenta
Write-Host "========================================" -ForegroundColor Magenta
Write-Host ""
Write-Host "Preview URLs:" -ForegroundColor Cyan
foreach ($packId in $PackIds) {
    Write-Host "  https://$StorageZone.b-cdn.net/previews/$packId.png" -ForegroundColor Gray
}
Write-Host ""
