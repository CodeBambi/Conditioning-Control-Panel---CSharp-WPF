<#
.SYNOPSIS
    Builds Resources\app.ico from Resources\logo2.png.

.DESCRIPTION
    app.ico is the most-seen mark in the product and the one a person meets BEFORE the app opens:
    the taskbar button, the tray icon (Services\Notifications\TrayIconService), Alt-Tab, the
    Start-menu and desktop shortcuts Inno creates, and the installer's own icon
    (installer.iss SetupIconFile). Until the neutral-baseline pass it was the BAMBI SLEEP dial -
    the same art as Resources\logo.png, lettering and all.

    logo2.png is that identical dial reading CONDITIONING CONTROL PANEL, so the icon is rebuilt
    from it. The file name does not change, which is why nothing else has to: the csproj
    ApplicationIcon, installer.iss and TrayIconService all keep pointing at Resources\app.ico.

    TWO LEVELS OF DETAIL, ON PURPOSE. The lettering in logo2.png occupies the bottom fifth of the
    art. Rendered at 64px or below it is not small text, it is a pink smear - three illegible bars
    under the dial that read as damage rather than as words. Measured on the real file: it starts
    resolving at 128 and is clean at 256.

    So the small frames are cropped to the dial itself (ring, the C/C/P buttons, the two arrows,
    the pulse traces and the figure) and the large frames keep the whole mark. This is ordinary
    icon practice - Windows ships different artwork per size for exactly this reason - and it
    means the thing identifying the app in a 16px taskbar slot is the dial, which is what carries
    the recognition anyway.

.PARAMETER Source
    Art to build from. Defaults to ..\Resources\logo2.png relative to this script.

.PARAMETER Out
    Target .ico. Defaults to ..\Resources\app.ico. KEEP THIS NAME - see above.

.PARAMETER PreviewDir
    When set, also writes one PNG per frame there, so the result can be eyeballed at 1:1 without
    installing anything. Nothing else changes.
#>
param(
    [string]$Source,
    [string]$Out,
    [string]$PreviewDir
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
if (-not $Source) { $Source = Join-Path $root 'Resources\logo2.png' }
if (-not $Out)    { $Out    = Join-Path $root 'Resources\app.ico' }

# Sizes Windows actually asks for: 16 lists and the taskbar, 24 tree views, 32 Alt-Tab and the
# desktop at default scaling, 48 medium icons, 64 the 150% DPI step for 48, 128 and 256 for
# large/extra-large and the Explorer preview.
$Sizes = @(16, 24, 32, 48, 64, 128, 256)

# Above this the wordmark resolves and the full art is used; at or below it, the dial crop is.
# 64 is the measured boundary, not a guess: at 64 the three lines of lettering share ~4px of height.
$LetteringLegibleAbove = 64

# The dial crop: a square of side 0.76*H centred at 0.50*W, 0.42*H. Chosen against renders at
# 16/24/32 - wider and the top of the lettering creeps back in as a pink bar along the bottom
# edge, tighter and the two side arrows get clipped.
$CropSideFraction = 0.76
$CropCentreX = 0.50
$CropCentreY = 0.42

if (-not (Test-Path -LiteralPath $Source)) { throw "Source art not found: $Source" }

$src = [System.Drawing.Image]::FromFile($Source)
try {
    [float]$sw = $src.Width
    [float]$sh = $src.Height

    # Full art, padded to a square so a near-square source is never stretched.
    [float]$fullSide = [Math]::Max($sw, $sh)
    [float]$fullL = ($sw - $fullSide) / 2.0
    [float]$fullT = ($sh - $fullSide) / 2.0

    [float]$dialSide = $sh * $CropSideFraction
    [float]$dialL = ($sw * $CropCentreX) - $dialSide / 2.0
    [float]$dialT = ($sh * $CropCentreY) - $dialSide / 2.0

    $frames = New-Object System.Collections.Generic.List[object]

    foreach ($size in $Sizes) {
        $useFull = $size -gt $LetteringLegibleAbove
        if ($useFull) {
            $crop = New-Object System.Drawing.RectangleF -ArgumentList $fullL, $fullT, $fullSide, $fullSide
        } else {
            $crop = New-Object System.Drawing.RectangleF -ArgumentList $dialL, $dialT, $dialSide, $dialSide
        }

        $bmp = New-Object System.Drawing.Bitmap -ArgumentList $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        try {
            $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
            $g.Clear([System.Drawing.Color]::Transparent)
            [float]$sz = $size
            $dst = New-Object System.Drawing.RectangleF -ArgumentList 0.0, 0.0, $sz, $sz
            $g.DrawImage($src, $dst, $crop, [System.Drawing.GraphicsUnit]::Pixel)
        } finally { $g.Dispose() }

        $frames.Add([pscustomobject]@{ Size = $size; Bitmap = $bmp; Full = $useFull })

        if ($PreviewDir) {
            if (-not (Test-Path -LiteralPath $PreviewDir)) {
                New-Item -ItemType Directory -Path $PreviewDir -Force | Out-Null
            }
            $bmp.Save((Join-Path $PreviewDir ("app-{0}.png" -f $size)), [System.Drawing.Imaging.ImageFormat]::Png)
        }
    }

    # ---- ICO container -------------------------------------------------------------------
    # Every frame is a 32bpp BMP (BITMAPINFOHEADER + bottom-up BGRA + an AND mask), NOT a PNG
    # frame. PNG-in-ICO is legal from Vista on and would be a third of the size, but this file is
    # also parsed by Inno Setup as SetupIconFile and embedded into the exe by the SDK, and the
    # classic DIB form is the one every consumer has always read. 350 KB is not worth the risk.
    #
    # The AND mask is all zeros (nothing masked): at 32bpp the alpha channel does the work, but the
    # mask must still be present and its rows 4-byte aligned, or the frame is rejected outright.
    function Get-DibBytes($bitmap) {
        $w = $bitmap.Width
        $h = $bitmap.Height
        $rect = New-Object System.Drawing.Rectangle -ArgumentList 0, 0, $w, $h
        $locked = $bitmap.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
            [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $stride = $locked.Stride
            $pixels = New-Object byte[] ($stride * $h)
            [System.Runtime.InteropServices.Marshal]::Copy($locked.Scan0, $pixels, 0, $pixels.Length)
        } finally { $bitmap.UnlockBits($locked) }

        $maskStride = [int](($w + 31) / 32) * 4
        $ms = New-Object System.IO.MemoryStream
        $bw = New-Object System.IO.BinaryWriter($ms)
        try {
            $bw.Write([uint32]40)          # biSize
            $bw.Write([int32]$w)           # biWidth
            $bw.Write([int32]($h * 2))     # biHeight: XOR image plus AND mask
            $bw.Write([uint16]1)           # biPlanes
            $bw.Write([uint16]32)          # biBitCount
            $bw.Write([uint32]0)           # biCompression = BI_RGB
            $bw.Write([uint32]($w * $h * 4 + $maskStride * $h))
            $bw.Write([int32]0); $bw.Write([int32]0)    # pels per metre
            $bw.Write([uint32]0); $bw.Write([uint32]0)  # clrUsed / clrImportant

            # Bottom-up. Format32bppArgb is already BGRA in memory, so rows copy straight across.
            for ($y = $h - 1; $y -ge 0; $y--) {
                $bw.Write($pixels, $y * $stride, $w * 4)
            }
            $zeroRow = New-Object byte[] $maskStride
            for ($y = 0; $y -lt $h; $y++) { $bw.Write($zeroRow, 0, $maskStride) }

            $bw.Flush()
            return $ms.ToArray()
        } finally { $bw.Dispose(); $ms.Dispose() }
    }

    $blobs = @()
    foreach ($f in $frames) { $blobs += ,(Get-DibBytes $f.Bitmap) }

    $outStream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($outStream)
    try {
        $writer.Write([uint16]0)                  # reserved
        $writer.Write([uint16]1)                  # type: icon
        $writer.Write([uint16]$frames.Count)

        $offset = 6 + 16 * $frames.Count
        for ($i = 0; $i -lt $frames.Count; $i++) {
            $size = $frames[$i].Size
            # 256 is written as 0 in a directory entry - the field is a single byte.
            if ($size -ge 256) { $dim = 0 } else { $dim = $size }
            $writer.Write([byte]$dim)             # width
            $writer.Write([byte]$dim)             # height
            $writer.Write([byte]0)                # palette entries (0 = no palette)
            $writer.Write([byte]0)                # reserved
            $writer.Write([uint16]1)              # colour planes
            $writer.Write([uint16]32)             # bits per pixel
            $writer.Write([uint32]$blobs[$i].Length)
            $writer.Write([uint32]$offset)
            $offset += $blobs[$i].Length
        }
        foreach ($b in $blobs) { $writer.Write($b, 0, $b.Length) }
        $writer.Flush()
        [System.IO.File]::WriteAllBytes($Out, $outStream.ToArray())
    } finally { $writer.Dispose(); $outStream.Dispose() }

    foreach ($f in $frames) { $f.Bitmap.Dispose() }

    $kb = [Math]::Round((Get-Item -LiteralPath $Out).Length / 1KB)
    $cropped = ($frames | Where-Object { -not $_.Full } | ForEach-Object { $_.Size }) -join '/'
    $whole   = ($frames | Where-Object { $_.Full } | ForEach-Object { $_.Size }) -join '/'
    Write-Host ("  wrote {0}  ({1} frames, {2} KB)" -f $Out, $frames.Count, $kb) -ForegroundColor Green
    Write-Host ("  dial crop: {0}   full mark: {1}" -f $cropped, $whole) -ForegroundColor DarkGray
} finally {
    $src.Dispose()
}
