@echo off
setlocal enabledelayedexpansion

echo ============================================
echo Conditioning Control Panel - Build Installer
echo ============================================
echo.

:: Configuration
set VERSION=5.4.6
set PROJECT_DIR=ConditioningControlPanel
set PUBLISH_DIR=%PROJECT_DIR%\bin\Release\net8.0-windows\win-x64\publish
set INSTALLER_OUTPUT=installer-output

:: Check for Inno Setup
set ISCC_PATH=
if exist "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" (
    set ISCC_PATH=C:\Program Files ^(x86^)\Inno Setup 6\ISCC.exe
) else if exist "C:\Program Files\Inno Setup 6\ISCC.exe" (
    set ISCC_PATH=C:\Program Files\Inno Setup 6\ISCC.exe
) else (
    echo ERROR: Inno Setup 6 not found!
    echo Please install from: https://jrsoftware.org/isdl.php
    echo.
    pause
    exit /b 1
)

echo [1/4] Cleaning previous builds...
if exist "%PUBLISH_DIR%" rmdir /s /q "%PUBLISH_DIR%"
if exist "%INSTALLER_OUTPUT%" rmdir /s /q "%INSTALLER_OUTPUT%"
mkdir "%INSTALLER_OUTPUT%" 2>nul

echo.
echo [2/4] Building application (Release)...
cd %PROJECT_DIR%
dotnet publish -c Release -r win-x64 --self-contained true
if errorlevel 1 (
    echo ERROR: Build failed!
    pause
    exit /b 1
)
cd ..

echo.
echo [3/4] Compiling installer with Inno Setup...
"%ISCC_PATH%" installer.iss
if errorlevel 1 (
    echo ERROR: Installer compilation failed!
    pause
    exit /b 1
)

echo.
echo [4/4] Build complete!
echo.
echo ============================================
echo Installer created: %INSTALLER_OUTPUT%\ConditioningControlPanel-%VERSION%-Setup.exe
echo ============================================
echo.

:: Open output folder
explorer "%INSTALLER_OUTPUT%"

pause
