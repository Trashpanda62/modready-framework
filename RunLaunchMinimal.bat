@echo off
REM Diagnostic: launch Standalone with a MINIMAL valid module set (base game +
REM BUTR libs + BetaDeps + one settings mod). If this reaches the main menu
REM (selftest.log appears) but the full 46-mod list doesn't, the problem is a
REM specific mod / the big list -- not Standalone itself.
cd /d C:\dev\modready\framework
taskkill /F /T /IM "Bannerlord.exe" >nul 2>&1
taskkill /F /T /IM "Bannerlord.BLSE.Standalone.exe" >nul 2>&1
taskkill /F /T /IM "Bannerlord.BLSE.LauncherEx.exe" >nul 2>&1
powershell -NoProfile -ExecutionPolicy Bypass -NoExit -Command ".\scripts\Launch-Standalone.ps1 -ModulesOverride @('Native','SandBoxCore','Sandbox','StoryMode','Bannerlord.Harmony','Bannerlord.ButterLib','Bannerlord.UIExtenderEx','Bannerlord.MBOptionScreen','BetaDeps','AIInfluence')"
