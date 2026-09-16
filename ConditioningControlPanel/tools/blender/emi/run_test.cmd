@echo off
cd /d C:\Tools\emi3d\hs
set PYTHONIOENCODING=utf-8
echo START > test.log
python face2.py >> test.log 2>&1
for %%P in (idle glee sad) do C:\Tools\blender\blender-5.2.1-windows-x64\blender.exe -b --factory-startup -P build_hs.py -- --mode test --pose %%P --out C:\Tools\emi3d\hs\out --res 768 >> test.log 2>&1
echo ALLDONE >> test.log
