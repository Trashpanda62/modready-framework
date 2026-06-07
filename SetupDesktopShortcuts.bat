@echo off
REM Run once: creates desktop shortcuts for Verify Slice / Quick Test / Launch Game.
cd /d C:\dev\beta-deps
powershell -NoProfile -ExecutionPolicy Bypass -Command ".\scripts\Setup-DesktopShortcuts.ps1"
echo.
echo Shortcuts created on your desktop. You can close this window.
pause
