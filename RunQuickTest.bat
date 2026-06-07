@echo off
REM Helper so the assistant can trigger Quick-Test by double-clicking from File
REM Explorer (the open PowerShell is a terminal the assistant can't type into).
REM Mirrors:  cd C:\dev\beta-deps  ;  .\scripts\Quick-Test.ps1
cd /d C:\dev\beta-deps
REM 2026-06-05: a Bannerlord/launcher process from a prior session survived
REM Quick-Test's own .Kill() and held the module DLLs + runtime.log locked,
REM which blocked the deploy and stopped a clean launcher from surfacing.
REM Force-clear them up front (taskkill /F is more forceful than .Kill()).
REM Harmless if nothing is running.
taskkill /F /T /IM "Bannerlord.exe" >nul 2>&1
taskkill /F /T /IM "Bannerlord.BLSE.LauncherEx.exe" >nul 2>&1
taskkill /F /T /IM "Bannerlord.BLSE.Launcher.exe" >nul 2>&1
taskkill /F /T /IM "TaleWorlds.MountAndBlade.Launcher.exe" >nul 2>&1
powershell -NoProfile -ExecutionPolicy Bypass -NoExit -Command ".\scripts\Quick-Test.ps1"
