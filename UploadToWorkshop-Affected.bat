@echo off
REM ===========================================================================
REM  Update ONLY the modules affected by this ship: BetaDeps (main) + MCM.
REM  The 3 pure-dependency items (Harmony, UIExtenderEx, ButterLib) are
REM  UNCHANGED this release and are deliberately skipped so they don't get
REM  re-versioned on Steam. Use UploadToWorkshop.bat for a full 5-item push.
REM
REM  Double-click this from File Explorer (NOT a terminal) so Steam can inject
REM  its API into the uploader. Steam MUST be running and logged in.
REM  Run scripts\Build-Phase1.ps1 first so dist\ holds the current build.
REM
REM    BetaDeps  3741426797      MCM  3741428715
REM ===========================================================================

set "UPLOADER=TaleWorlds.MountAndBlade.SteamWorkshop.exe"
set "WS=C:\dev\bannerlord\workshop"
cd /d "C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\bin\Win64_Shipping_Client"

echo [1/2] BetaDeps (main)...
start "Upload BetaDeps"      /wait "%UPLOADER%" "%WS%\WorkshopUpdate-BetaDeps.xml"

echo [2/2] Bannerlord.MBOptionScreen (MCM)...
start "Upload MCM"           /wait "%UPLOADER%" "%WS%\WorkshopUpdate-MCM.xml"

echo.
echo Both upload windows have been launched and closed.
echo Check each item's Workshop page to confirm the new update timestamp.
pause
