@echo off
set PYTHONIOENCODING=utf-8
cd /d C:\Tools\emi3d\hs
C:\Tools\blender\blender-5.2.1-windows-x64\blender.exe -b --factory-startup -P build_hs.py -- --mode test --pose %1 --out C:\Tools\emi3d\hs\out --res 1024 --pix %2 > one.log 2>&1
python pixup.py out 1024 test_%1.png >> one.log 2>&1
echo ALLDONE >> one.log
