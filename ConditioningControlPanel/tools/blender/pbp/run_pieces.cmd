@echo off
rem Build the twelve Piece by Piece glbs into out\, audit them in a fresh Blender and, only if the
rem audit passes, copy them over assets\pieces\. Renders out\sheet.png for a look. Runs from wherever
rem it lives. Log: pieces.log, which ends with ALLDONE. Override BLENDER / DEST in the environment.
cd /d %~dp0
set PYTHONIOENCODING=utf-8
if "%BLENDER%"=="" set BLENDER=C:\Program Files\Blender Foundation\Blender 5.2\blender.exe
if "%DEST%"=="" set DEST=%~dp0..\..\..\Resources\web\piecebypiece\assets\pieces
echo START > pieces.log
"%BLENDER%" -b --factory-startup -P build_pieces.py -- --out %~dp0out --sheet %~dp0out\sheet.png >> pieces.log 2>&1
"%BLENDER%" -b --factory-startup -P check_pieces.py -- --dir %~dp0out >> pieces.log 2>&1
if errorlevel 1 (
  echo CHECK FAILED, nothing copied >> pieces.log
  echo ALLDONE >> pieces.log
  exit /b 1
)
copy /y "%~dp0out\*.glb" "%DEST%\" >> pieces.log 2>&1
echo ALLDONE >> pieces.log
