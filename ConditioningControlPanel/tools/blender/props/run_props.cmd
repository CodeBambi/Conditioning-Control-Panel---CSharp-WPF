@echo off
rem Build the prop pack, export props.glb, audit it in a fresh Blender and (RENDER=1) render the
rem contact-sheet cells and assemble the sheet. Runs from wherever it lives. Log: props.log,
rem which ends with ALLDONE. Override GLB / SHOTS / BLENDER in the environment if needed.
cd /d %~dp0
set PYTHONIOENCODING=utf-8
if "%BLENDER%"=="" set BLENDER=C:\Tools\blender\blender-5.2.1-windows-x64\blender.exe
if "%GLB%"=="" set GLB=C:\wt-race-b4-props\ConditioningControlPanel\Resources\web\dtrh\race\assets\props.glb
if "%SHOTS%"=="" set SHOTS=C:\Users\PC\Pictures\Screenshots\emi-3d\props
if "%RENDER%"=="" set RENDER=0
echo START > props.log
"%BLENDER%" -b --factory-startup -P build_props.py -- --glb %GLB% --out %~dp0out --render %RENDER% --res 512 --pix 4 >> props.log 2>&1
"%BLENDER%" -b --factory-startup -P check_props.py -- --glb %GLB% >> props.log 2>&1
if "%RENDER%"=="1" python sheet.py %~dp0out %SHOTS% >> props.log 2>&1
echo ALLDONE >> props.log
