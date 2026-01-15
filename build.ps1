# Build script for Conditioning Control Panel
# Run with: .\build.ps1
# For release: .\build.ps1 -Publish -CreateRelease

param(
    [string]$Configuration = "Release",
    [switch]$Clean,
    [switch]$Publish,
    [switch]$CreateRelease,
    [string]$CertPath = "",
    [string]$CertPassword = ""
)

$ErrorActionPreference = "Stop"
$ProjectDir = "ConditioningControlPanel"
$OutputDir = "publish"
$ReleaseDir = "releases"

Write-Host "Conditioning Control Panel Build Script" -ForegroundColor Magenta
Write-Host "==========================================" -ForegroundColor Magenta

# Get version from csproj
$csprojPath = "$ProjectDir\ConditioningControlPanel.csproj"
$csproj = [xml](Get-Content $csprojPath)
$version = $csproj.Project.PropertyGroup.Version
Write-Host "Version: $version" -ForegroundColor Cyan

# Clean
if ($Clean) {
    Write-Host "`nCleaning..." -ForegroundColor Yellow
    if (Test-Path $OutputDir) {
        Remove-Item -Recurse -Force $OutputDir
    }
    if (Test-Path $ReleaseDir) {
        Remove-Item -Recurse -Force $ReleaseDir
    }
    dotnet clean $ProjectDir -c $Configuration
}

# Restore
Write-Host "`nRestoring packages..." -ForegroundColor Cyan
dotnet restore $ProjectDir

# Build
Write-Host "`nBuilding ($Configuration)..." -ForegroundColor Cyan
dotnet build $ProjectDir -c $Configuration --no-restore

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}

# Publish
if ($Publish) {
    Write-Host "`nPublishing self-contained executable..." -ForegroundColor Cyan

    dotnet publish $ProjectDir `
        -c $Configuration `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -o $OutputDir

    if ($LASTEXITCODE -ne 0) {
        Write-Host "Publish failed!" -ForegroundColor Red
        exit 1
    }

    # Create asset folders
    Write-Host "`nCreating asset folder structure..." -ForegroundColor Cyan
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
    Write-Host "`nCopying Resources folder..." -ForegroundColor Cyan
    Copy-Item -Path (Join-Path $ProjectDir "Resources") -Destination $OutputDir -Recurse -Force

    Write-Host "`nPublish complete!" -ForegroundColor Green
    Write-Host "Output: $OutputDir" -ForegroundColor White

    # Show file size
    $exePath = Join-Path $OutputDir "ConditioningControlPanel.exe"
    if (Test-Path $exePath) {
        $size = (Get-Item $exePath).Length / 1MB
        Write-Host "Executable size: $([math]::Round($size, 2)) MB" -ForegroundColor White
    }
}

# Create Velopack Release
if ($CreateRelease) {
    Write-Host "`nCreating Velopack release..." -ForegroundColor Cyan

    # Ensure output dir exists
    if (!(Test-Path $OutputDir)) {
        Write-Host "Must publish first! Run with -Publish -CreateRelease" -ForegroundColor Red
        exit 1
    }

    # Create releases directory
    if (!(Test-Path $ReleaseDir)) {
        New-Item -ItemType Directory -Path $ReleaseDir | Out-Null
    }

    # Install vpk tool if not present
    $vpkInstalled = Get-Command vpk -ErrorAction SilentlyContinue
    if (!$vpkInstalled) {
        Write-Host "Installing Velopack CLI tool..." -ForegroundColor Yellow
        dotnet tool install -g vpk

        # Refresh PATH for current session
        $env:Path = [System.Environment]::GetEnvironmentVariable("Path","Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path","User")
    }

    # Build Velopack release
    $vpkArgs = @(
        "pack",
        "--packId", "ConditioningControlPanel",
        "--packVersion", $version,
        "--packDir", $OutputDir,
        "--mainExe", "ConditioningControlPanel.exe",
        "--outputDir", $ReleaseDir
    )

    # Add code signing if certificate provided
    if ($CertPath -and (Test-Path $CertPath)) {
        Write-Host "Code signing enabled" -ForegroundColor Green
        $vpkArgs += @(
            "--signParams", "/a /f `"$CertPath`" /p `"$CertPassword`" /tr http://timestamp.digicert.com /td sha256 /fd sha256"
        )
    }
    else {
        Write-Host "Warning: No code signing certificate provided" -ForegroundColor Yellow
        Write-Host "  Users may see Windows SmartScreen warnings" -ForegroundColor Yellow
    }

    Write-Host "Running: vpk $($vpkArgs -join ' ')" -ForegroundColor Gray
    & vpk @vpkArgs

    if ($LASTEXITCODE -ne 0) {
        Write-Host "Velopack pack failed!" -ForegroundColor Red
        exit 1
    }

    Write-Host "`nRelease created in $ReleaseDir/" -ForegroundColor Green
    Get-ChildItem $ReleaseDir | ForEach-Object {
        Write-Host "  $($_.Name) ($([math]::Round($_.Length/1MB, 2)) MB)" -ForegroundColor White
    }

    Write-Host "`nTo publish this release:" -ForegroundColor Cyan
    Write-Host "  1. Create a GitHub release tagged 'v$version'" -ForegroundColor White
    Write-Host "  2. Upload all files from the '$ReleaseDir' folder" -ForegroundColor White
}

if (!$Publish -and !$CreateRelease) {
    Write-Host "`nBuild complete!" -ForegroundColor Green
    Write-Host "Run with -Publish to create distributable package" -ForegroundColor Yellow
    Write-Host "Run with -Publish -CreateRelease to create Velopack release" -ForegroundColor Yellow
}

Write-Host ""
