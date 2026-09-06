@echo off
cd /d C:\Tools\emi3d\hs
set PYTHONIOENCODING=utf-8
echo START > full.log
python face2.py >> full.log 2>&1
C:\Tools\blender\blender-5.2.1-windows-x64\blender.exe -b --factory-startup -P build_hs.py -- --mode poses --out C:\Tools\emi3d\hs\out --res 1024 --pix 6 >> full.log 2>&1
python pixup.py out 1024 emi_hs_*.png >> full.log 2>&1
C:\Tools\blender\blender-5.2.1-windows-x64\blender.exe -b --factory-startup -P build_hs.py -- --mode turn --out C:\Tools\emi3d\hs\out --res 768 --pix 5 --frames 40 >> full.log 2>&1
python pixup.py out 768 turn_*.png >> full.log 2>&1
"C:\Users\PC\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-8.1-full_build\bin\ffmpeg.exe" -y -framerate 16 -i out\turn_%%04d.png -vf format=yuv420p -c:v libx264 -crf 18 -movflags +faststart out\emi_hs_turntable.mp4 >> full.log 2>&1
if not exist "C:\Users\PC\Pictures\Screenshots\emi-3d\hs" mkdir "C:\Users\PC\Pictures\Screenshots\emi-3d\hs"
copy /y out\emi_hs_*.png "C:\Users\PC\Pictures\Screenshots\emi-3d\hs\" >nul
copy /y out\emi_hs_turntable.mp4 "C:\Users\PC\Pictures\Screenshots\emi-3d\hs\" >nul
copy /y out\emi_hs.blend "C:\Users\PC\Pictures\Screenshots\emi-3d\hs\" >nul
copy /y out\turn_0011.png "C:\Users\PC\Pictures\Screenshots\emi-3d\hs\emi_hs_side.png" >nul
copy /y out\turn_0021.png "C:\Users\PC\Pictures\Screenshots\emi-3d\hs\emi_hs_back.png" >nul
echo ALLDONE >> full.log
