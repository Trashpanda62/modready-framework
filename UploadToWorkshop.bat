@echo off
REM Upload BetaDeps to Steam Workshop (item 3741426797)
REM Double-click this from File Explorer — Steam must be running.
cd /d "C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\bin\Win64_Shipping_Client"
TaleWorlds.MountAndBlade.SteamWorkshop.exe "C:\dev\beta-deps\dist\Modules\BetaDeps\SubModule.xml"
pause
