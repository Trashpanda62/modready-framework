@echo off
REM Double-clickable: launch Bannerlord straight into the game (no launcher
REM window, no Play click) with your current enabled mod set. Handy for fast
REM manual testing without the build step.
cd /d C:\dev\modready\framework
taskkill /F /T /IM "Bannerlord.exe" >nul 2>&1
taskkill /F /T /IM "Bannerlord.BLSE.Standalone.exe" >nul 2>&1
taskkill /F /T /IM "Bannerlord.BLSE.LauncherEx.exe" >nul 2>&1
powershell -NoProfile -ExecutionPolicy Bypass -NoExit -Command ".\scripts\Launch-Standalone.ps1"
