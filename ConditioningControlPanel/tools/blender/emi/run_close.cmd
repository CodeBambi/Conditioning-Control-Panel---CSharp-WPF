@echo off
set PYTHONIOENCODING=utf-8
cd /d C:\Tools\emi3d\hs
C:\Tools\blender\blender-5.2.1-windows-x64\blender.exe -b --factory-startup -P build_hs.py -- --mode test --pose shock --out C:\Tools\emi3d\hs\out --res 768 --cam 52,-22,36 --tgt 40,5,30 --lens 70 > close.log 2>&1
copy /y out\test_shock.png out\close_hand.png >nul
C:\Tools\blender\blender-5.2.1-windows-x64\blender.exe -b --factory-startup -P build_hs.py -- --mode test --pose idle --out C:\Tools\emi3d\hs\out --res 768 --cam 24,-18,14 --tgt 9,3,3 --lens 70 >> close.log 2>&1
copy /y out\test_idle.png out\close_shoe.png >nul
echo ALLDONE >> close.log
