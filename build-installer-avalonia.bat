@echo off
setlocal enabledelayedexpansion

echo =====================================================================
echo Conditioning Control Panel - Build Installer (Avalonia head, Windows)
echo =====================================================================
echo.
echo Packages CCP.Avalonia as an IN-PLACE UPGRADE of a WPF install (same AppId,
echo same mutex, exe installed as ConditioningControlPanel.exe). See installer.iss
echo "WHICH HEAD THIS PACKAGES". Nothing here signs, uploads or releases.
echo.

:: Configuration
set PROJECT_DIR=CCP.Avalonia
set CORE_DIR=CCP.Core
:: Properties\PublishProfiles\win-x64.pubxml: self-contained single file, PublishDir bin\publish\win-x64
set PUBLISH_DIR=%PROJECT_DIR%\bin\publish\win-x64
set HEAD_EXE=CCP.Avalonia.exe
:: Its own output folder: this script never touches the WPF release's installer-output.
set INSTALLER_OUTPUT=installer-output-avalonia
:: Short staging path for the Inno Setup compile (MAX_PATH, as build-installer.bat).
set STAGING_DIR=C:\ccpb\pub-ava

:: ONE version source: Version.props. <Version> is the number every version check parses;
:: <CcpVersionLabel> marks an unreleased build (7.2.0-parity) and only names the file.
set VERSION=
set LABEL=
for /f "usebackq delims=" %%V in (`powershell -NoProfile -Command "([xml](Get-Content Version.props)).Project.PropertyGroup.Version"`) do set VERSION=%%V
for /f "usebackq delims=" %%V in (`powershell -NoProfile -Command "([xml](Get-Content Version.props)).Project.PropertyGroup.CcpVersionLabel"`) do set LABEL=%%V
if "%VERSION%"=="" (
    echo ERROR: could not read ^<Version^> from Version.props
    pause
    exit /b 1
)
set VERSION_LABEL=%VERSION%
if not "%LABEL%"=="" set VERSION_LABEL=%VERSION%-%LABEL%
echo Version %VERSION%   file label %VERSION_LABEL%
echo.

:: Check for Inno Setup
set ISCC_PATH=
if exist "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" (
    set ISCC_PATH=C:\Program Files ^(x86^)\Inno Setup 6\ISCC.exe
) else if exist "C:\Program Files\Inno Setup 6\ISCC.exe" (
    set ISCC_PATH=C:\Program Files\Inno Setup 6\ISCC.exe
) else if exist "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" (
    set ISCC_PATH=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe
) else (
    echo ERROR: Inno Setup 6 not found!
    echo Please install from: https://jrsoftware.org/isdl.php
    echo.
    pause
    exit /b 1
)

echo [1/6] Cleaning previous builds...
:: Same reason as build-installer.bat: an incremental publish can wrap a stale Release dll.
if exist "%PROJECT_DIR%\bin\Release" rmdir /s /q "%PROJECT_DIR%\bin\Release"
if exist "%PROJECT_DIR%\obj\Release" rmdir /s /q "%PROJECT_DIR%\obj\Release"
if exist "%CORE_DIR%\bin\Release" rmdir /s /q "%CORE_DIR%\bin\Release"
if exist "%CORE_DIR%\obj\Release" rmdir /s /q "%CORE_DIR%\obj\Release"
if exist "%PUBLISH_DIR%" rmdir /s /q "%PUBLISH_DIR%"
if exist "%INSTALLER_OUTPUT%" rmdir /s /q "%INSTALLER_OUTPUT%"
mkdir "%INSTALLER_OUTPUT%" 2>nul

echo.
echo [2/6] Publishing the head (Release, win-x64, self-contained)...
:: Needs the .NET 10 SDK on PATH (or DOTNET_ROOT pointing at it).
dotnet publish %PROJECT_DIR%\%PROJECT_DIR%.csproj -c Release -p:PublishProfile=win-x64
if errorlevel 1 (
    echo ERROR: Publish failed!
    pause
    exit /b 1
)
if not exist "%PUBLISH_DIR%\%HEAD_EXE%" (
    echo ERROR: %PUBLISH_DIR%\%HEAD_EXE% not found after publish!
    pause
    exit /b 1
)

echo.
echo [2.5/6] Cleaning empty locale folders from publish output...
for %%D in (cs de es fr it ja ko pl pt-BR ru tr zh-Hans zh-Hant) do (
    if exist "%PUBLISH_DIR%\%%D" rmdir /s /q "%PUBLISH_DIR%\%%D"
)

echo.
echo [2.6/6] Ensuring redistributable bootstrappers are present...
if not exist "redist" mkdir "redist"
if not exist "redist\VC_redist.x64.exe" (
    echo Downloading VC_redist.x64.exe from aka.ms ...
    curl -L -f -o "redist\VC_redist.x64.exe" https://aka.ms/vs/17/release/vc_redist.x64.exe
    if errorlevel 1 (
        echo ERROR: Failed to download VC_redist.x64.exe
        echo Manually place it in the redist\ folder and re-run.
        pause
        exit /b 1
    )
)
if not exist "redist\MicrosoftEdgeWebview2Setup.exe" (
    echo redist\MicrosoftEdgeWebview2Setup.exe missing - downloading from go.microsoft.com ...
    curl -L -f -o "redist\MicrosoftEdgeWebview2Setup.exe" "https://go.microsoft.com/fwlink/p/?LinkId=2124703"
    if errorlevel 1 (
        echo ERROR: Failed to download MicrosoftEdgeWebview2Setup.exe
        echo Manually place it in the redist\ folder and re-run.
        pause
        exit /b 1
    )
)

echo.
echo ============================================
echo [3/6] CODE SIGN - Application EXE
echo ============================================
echo.
echo Sign the app exe now (it is renamed to ConditioningControlPanel.exe at INSTALL time,
echo the signature travels with the file):
echo   C:\downloads\ActalisCodeSigner-win-x64-latest\ActalisCodeSigner.exe -fu YOUR_USERNAME -fp YOUR_PASSWORD -in "%PUBLISH_DIR%\%HEAD_EXE%" -ts
echo.
echo (A local test build can skip signing: just press a key.)
pause

echo.
echo [3.5/6] Staging publish to short path (MAX_PATH workaround)...
:: Mirror AFTER signing so the staged exe carries the signature. robocopy 0-7 = success.
if exist "%STAGING_DIR%" rmdir /s /q "%STAGING_DIR%"
robocopy "%PUBLISH_DIR%" "%STAGING_DIR%" /MIR /NJH /NJS /NDL /NFL /NP
if errorlevel 8 (
    echo ERROR: Failed to stage publish output to %STAGING_DIR%!
    pause
    exit /b 1
)

echo.
echo [4/6] Compiling installer with Inno Setup (from staging path)...
:: ISPP treats backslashes in a /D value as escape chars, so double them (see build-installer.bat).
"%ISCC_PATH%" /DAvaloniaHead /DMyAppVersion=%VERSION% /DMyAppVersionLabel=%VERSION_LABEL% /DPublishDir=%STAGING_DIR:\=\\% /O"%INSTALLER_OUTPUT%" installer.iss
if errorlevel 1 (
    echo ERROR: Installer compilation failed!
    rmdir /s /q "%STAGING_DIR%" 2>nul
    pause
    exit /b 1
)

echo.
echo [4.5/6] Removing staging copy...
rmdir /s /q "%STAGING_DIR%" 2>nul

echo.
echo ============================================
echo [5/6] CODE SIGN - Installer EXE
echo ============================================
echo.
echo Sign the installer now:
echo   C:\downloads\ActalisCodeSigner-win-x64-latest\ActalisCodeSigner.exe -fu YOUR_USERNAME -fp YOUR_PASSWORD -in "%INSTALLER_OUTPUT%\ConditioningControlPanel-%VERSION_LABEL%-Setup.exe" -ts
echo.
echo (A local test build can skip signing: just press a key.)
pause

echo.
echo [6/6] Build complete!
echo.
echo ============================================
echo Installer: %INSTALLER_OUTPUT%\ConditioningControlPanel-%VERSION_LABEL%-Setup.exe
echo ============================================
echo.
echo NOT a release: the in-app updater only finds a file named ConditioningControlPanel-%VERSION%-Setup.exe
echo on a GitHub release marked Latest. Empty ^<CcpVersionLabel^> in Version.props for a real one.
echo.

endlocal
