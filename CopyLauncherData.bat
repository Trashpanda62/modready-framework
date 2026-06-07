@echo off
REM One-off helper: copy the live LauncherData.xml into a folder Claude can read,
REM so the load-order parser in Launch-Standalone.ps1 can be matched to its real shape.
copy /Y "C:\Users\Steve\OneDrive\Documents\Mount and Blade II Bannerlord\Configs\LauncherData.xml" "C:\dev\bannerlord\LauncherData-copy.xml"
echo Done.
