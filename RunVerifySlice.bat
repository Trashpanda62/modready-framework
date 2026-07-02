@echo off
REM Double-clickable wrapper so the verify sub-agent can trigger the slice
REM verify loop from File Explorer (it can't type into a terminal).
REM Mirrors:  cd C:\dev\modready\framework  ;  .\scripts\Verify-Slice.ps1
REM
REM Verify-Slice.ps1 force-kills stale Bannerlord/BLSE processes itself, but
REM we also clear them here up front for good measure (harmless if none run).
REM Self-elevate: the BLSE Standalone launch needs admin to bring the engine
REM up (a non-elevated launch starts the process but silently hangs before the
REM menu). If not already admin, relaunch elevated (a UAC "allow?" prompt
REM appears -- click Yes).
net session >nul 2>&1
if %errorlevel% neq 0 (
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)
cd /d C:\dev\modready\framework
taskkill /F /T /IM "Bannerlord.exe" >nul 2>&1
taskkill /F /T /IM "Bannerlord.BLSE.Standalone.exe" >nul 2>&1
taskkill /F /T /IM "Bannerlord.BLSE.LauncherEx.exe" >nul 2>&1
powershell -NoProfile -ExecutionPolicy Bypass -NoExit -Command ".\scripts\Verify-Slice.ps1"
