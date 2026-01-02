# Build script for Conditioning Control Panel
# Run with: .\build.ps1

param(
    [string]$Configuration = "Release",
    [switch]$Clean,
    [switch]$Publish
)

$ErrorActionPreference = "Stop"
$ProjectDir = "ConditioningControlPanel"
$OutputDir = "publish"

Write-Host "🎀 Conditioning Control Panel Build Script" -ForegroundColor Magenta
Write-Host "==========================================" -ForegroundColor Magenta

# Clean
if ($Clean) {
    Write-Host "`n🧹 Cleaning..." -ForegroundColor Yellow
    if (Test-Path $OutputDir) {
        Remove-Item -Recurse -Force $OutputDir
    }
    dotnet clean $ProjectDir -c $Configuration
}

# Restore
Write-Host "`n📦 Restoring packages..." -ForegroundColor Cyan
dotnet restore $ProjectDir

# Build
Write-Host "`n🔨 Building ($Configuration)..." -ForegroundColor Cyan
dotnet build $ProjectDir -c $Configuration --no-restore

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Build failed!" -ForegroundColor Red
    exit 1
}

# Publish
if ($Publish) {
    Write-Host "`n📤 Publishing self-contained executable..." -ForegroundColor Cyan
    
    dotnet publish $ProjectDir `
        -c $Configuration `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -o $OutputDir

    if ($LASTEXITCODE -ne 0) {
        Write-Host "❌ Publish failed!" -ForegroundColor Red
        exit 1
    }

    # Create asset folders
    Write-Host "`n📁 Creating asset folder structure..." -ForegroundColor Cyan
    $assetFolders = @(
        "assets/images",
        "assets/sounds", 
        "assets/startle_videos",
        "assets/sub_audio",
        "assets/backgrounds",
        "logs"
    )

    foreach ($folder in $assetFolders) {
        $path = Join-Path $OutputDir $folder
        if (!(Test-Path $path)) {
            New-Item -ItemType Directory -Path $path -Force | Out-Null
        }
    }

    # Copy README
    Copy-Item "README.md" -Destination $OutputDir -Force

    # Create placeholder files
    $readmePlaceholder = @"
# Asset Folder

Place your content files here:
- images/: Flash images (.png, .jpg, .gif, .webp)
- sounds/: Flash sounds (.mp3, .wav, .ogg)
- startle_videos/: Mandatory videos (.mp4, .avi, .mkv)
- sub_audio/: Subliminal whispers (.mp3)
- backgrounds/: Background loops (.mp3)
"@
    Set-Content -Path (Join-Path $OutputDir "assets/README.txt") -Value $readmePlaceholder

    # Copy Resources folder
    Write-Host "`n🖼️ Copying Resources folder..." -ForegroundColor Cyan
    Copy-Item -Path (Join-Path $ProjectDir "Resources") -Destination $OutputDir -Recurse -Force

    Write-Host "`n✅ Build complete!" -ForegroundColor Green
    Write-Host "📂 Output: $OutputDir" -ForegroundColor White
    
    # Show file size
    $exePath = Join-Path $OutputDir "ConditioningControlPanel.exe"
    if (Test-Path $exePath) {
        $size = (Get-Item $exePath).Length / 1MB
        Write-Host "📊 Executable size: $([math]::Round($size, 2)) MB" -ForegroundColor White
    }
}
else {
    Write-Host "`n✅ Build complete!" -ForegroundColor Green
    Write-Host '💡 Run with -Publish to create distributable package' -ForegroundColor Yellow
}

Write-Host ""
