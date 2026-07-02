@echo off
REM ===========================================================================
REM  Update ALL FIVE BetaDeps Steam Workshop items to the current dist build.
REM
REM  Double-click this from File Explorer (NOT a terminal) so Steam can inject
REM  its API into the uploader. Steam MUST be running and logged in.
REM
REM  The TaleWorlds uploader takes a WorkshopUPDATE task XML (NOT a SubModule.xml).
REM  Each WorkshopUpdate-*.xml carries the Workshop <ItemId>, the <ModuleFolder>
REM  (-> C:\dev\modready\framework\dist\Modules\...), the <ChangeNotes>, tags and image.
REM  Run scripts\Build-Phase1.ps1 first so dist\ holds the current build.
REM
REM    BetaDeps      3741426797   UIExtenderEx  3741428357   MCM  3741428715
REM    Harmony       3741428196   ButterLib     3741428541
REM
REM  Each item opens in its OWN window via "start /wait". If a window keeps
REM  printing "Status: k_EItemUpdateStatusInvalid 0/0", the upload already
REM  succeeded -- just CLOSE that window and the next item starts automatically.
REM ===========================================================================

set "UPLOADER=TaleWorlds.MountAndBlade.SteamWorkshop.exe"
set "WS=C:\dev\bannerlord\workshop"
cd /d "C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\bin\Win64_Shipping_Client"

echo [1/5] BetaDeps (main)...
start "Upload BetaDeps"      /wait "%UPLOADER%" "%WS%\WorkshopUpdate-BetaDeps.xml"

echo [2/5] Bannerlord.Harmony...
start "Upload Harmony"       /wait "%UPLOADER%" "%WS%\WorkshopUpdate-Harmony.xml"

echo [3/5] Bannerlord.UIExtenderEx...
start "Upload UIExtenderEx"  /wait "%UPLOADER%" "%WS%\WorkshopUpdate-UIExtenderEx.xml"

echo [4/5] Bannerlord.ButterLib...
start "Upload ButterLib"     /wait "%UPLOADER%" "%WS%\WorkshopUpdate-ButterLib.xml"

echo [5/5] Bannerlord.MBOptionScreen (MCM)...
start "Upload MCM"           /wait "%UPLOADER%" "%WS%\WorkshopUpdate-MCM.xml"

echo.
echo All five upload windows have been launched and closed.
echo Check each item's Workshop page to confirm the new update timestamp.
pause
