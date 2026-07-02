@echo off
REM Elevation test: launch Standalone (minimal module set) AS ADMINISTRATOR.
REM BLSE's AppDomainManager / runtime injection and the game in Program Files
REM may need admin rights that a normal launch lacks -- which would explain the
REM silent "process alive but engine never starts" hang.
REM Self-elevates via UAC: a "Do you want to allow..." prompt will appear --
REM click YES.
net session >nul 2>&1
if %errorlevel% neq 0 (
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)
cd /d C:\dev\modready\framework
taskkill /F /T /IM "Bannerlord.exe" >nul 2>&1
taskkill /F /T /IM "Bannerlord.BLSE.Standalone.exe" >nul 2>&1
taskkill /F /T /IM "Bannerlord.BLSE.LauncherEx.exe" >nul 2>&1
powershell -NoProfile -ExecutionPolicy Bypass -NoExit -Command ".\scripts\Launch-Standalone.ps1 -ModulesOverride @('Native','SandBoxCore','Sandbox','StoryMode','Bannerlord.Harmony','Bannerlord.ButterLib','Bannerlord.UIExtenderEx','Bannerlord.MBOptionScreen','BetaDeps','AIInfluence')"
