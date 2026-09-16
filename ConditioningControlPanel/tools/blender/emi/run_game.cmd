@echo off
rem EMI game glb pipeline: atlas -> game LOD -> clips + glb -> re-import check -> turntable + stills.
rem   run_game.cmd          full run (renders take a few minutes)
rem   run_game.cmd quick    stop after the re-import contract check, no renders
rem Detach it (Start-Process, hidden) and poll %LOG% for ALLDONE; Blender hangs a foreground shell.
setlocal
set PYTHONIOENCODING=utf-8
set HERE=%~dp0
if "%REPO%"=="" set REPO=C:\wt-race-b3-emi\ConditioningControlPanel
if "%OUT%"=="" set OUT=C:\Tools\emi3d\game\out
set TOOLS=%REPO%\tools\blender\emi
set ASSETS=%REPO%\Resources\web\dtrh\race\assets
set SHOTS=C:\Users\PC\Pictures\Screenshots\emi-3d\game
set LOG=%OUT%\..\game.log
set BL=C:\Tools\blender\blender-5.2.1-windows-x64\blender.exe
set FFMPEG=C:\Users\PC\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-8.1-full_build\bin\ffmpeg.exe
set NORENDER=0
if "%1"=="quick" set NORENDER=1
if not exist "%OUT%" mkdir "%OUT%"
if not exist "%ASSETS%" mkdir "%ASSETS%"
cd /d %HERE%
echo START > "%LOG%"
echo == face atlas >> "%LOG%"
python "%HERE%face2.py" --game "%OUT%" >> "%LOG%" 2>&1
echo == game LOD >> "%LOG%"
"%BL%" -b --factory-startup -P "%HERE%build_hs.py" -- --game 1 --mode game --out "%OUT%" --atlas "%OUT%\emi-faces.png" >> "%LOG%" 2>&1
echo == clips + export >> "%LOG%"
"%BL%" -b --factory-startup -P "%TOOLS%\export_glb.py" -- --blend "%OUT%\emi_game.blend" --glb "%ASSETS%\emi.glb" --atlas "%OUT%\emi-faces.png" --fps 30 >> "%LOG%" 2>&1
echo == verify >> "%LOG%"
"%BL%" -b --factory-startup -P "%TOOLS%\verify_glb.py" -- --glb "%ASSETS%\emi.glb" --out "%OUT%\verify" --res 768 --pix 5 --frames 48 --still 1024 --stillpix 6 --fps 30 --norender %NORENDER% >> "%LOG%" 2>&1
if "%NORENDER%"=="1" goto done
echo == pixup + mp4 >> "%LOG%"
python "%HERE%pixup.py" "%OUT%\verify" 768 turn_*.png >> "%LOG%" 2>&1
python "%HERE%pixup.py" "%OUT%\verify" 1024 emi_game_*.png >> "%LOG%" 2>&1
"%FFMPEG%" -y -framerate 16 -i "%OUT%\verify\turn_%%04d.png" -vf format=yuv420p -c:v libx264 -crf 18 -movflags +faststart "%OUT%\verify\emi_game_turntable.mp4" >> "%LOG%" 2>&1
if not exist "%SHOTS%" mkdir "%SHOTS%"
copy /y "%OUT%\verify\emi_game_*.png" "%SHOTS%\" >nul
copy /y "%OUT%\verify\emi_game_turntable.mp4" "%SHOTS%\" >nul
copy /y "%OUT%\emi-faces.png" "%SHOTS%\" >nul
:done
echo ALLDONE >> "%LOG%"
endlocal
