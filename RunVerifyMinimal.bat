@echo off
REM Fast iteration: build + deploy + launch a 10-module set (~2-3 min load) +
REM self-test + headless Mod Config screenshot. For developing the auto-capture.
net session >nul 2>&1
if %errorlevel% neq 0 (
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)
cd /d C:\dev\beta-deps
taskkill /F /T /IM "Bannerlord.exe" >nul 2>&1
taskkill /F /T /IM "Bannerlord.BLSE.Standalone.exe" >nul 2>&1
taskkill /F /T /IM "Bannerlord.BLSE.LauncherEx.exe" >nul 2>&1
powershell -NoProfile -ExecutionPolicy Bypass -Command ".\scripts\Verify-Slice.ps1 -Minimal"
