@echo off
REM ============================================================
REM  BetaDeps -- Reset State
REM
REM  Run this if BetaDeps has gotten itself stuck (most common
REM  symptom: the "Mod Configuration" tab is missing from the
REM  in-game Options screen).
REM
REM  This wipes the state files BetaDeps writes between sessions:
REM    - betadeps-disabled-mods.log  (the crash-suspect block list)
REM    - last-good-modlist.txt       (the "loaded cleanly" baseline)
REM    - incompatible-mods.log       (latest post-load scan)
REM    - failed-mods-catalog.txt     (SaveShield's append-only ledger)
REM    - runtime.log + archives      (BetaDeps' own diagnostic log)
REM    - selftest.log + selftest.json (last self-test results)
REM    - saveshield-swallow.flag, patchshield-disabled.flag, etc.
REM
REM  Your actual mod settings (in Documents\Mount and Blade II
REM  Bannerlord\Configs\ModSettings\Global\) are NOT touched.
REM  Your mods are NOT uninstalled.
REM
REM  Equivalent to "uninstall and reinstall BetaDeps" without
REM  re-downloading the zip.
REM ============================================================

setlocal
set "TARGET=%~dp0"
echo BetaDeps Reset State
echo Target: %TARGET%
echo.

choice /C YN /N /M "This will delete BetaDeps state files in the folder above. Proceed? (Y/N): "
if errorlevel 2 (
    echo Cancelled.
    pause
    exit /b 1
)

echo.
echo Removing state files...

del /Q "%TARGET%betadeps-disabled-mods.log"     2>nul
del /Q "%TARGET%last-good-modlist.txt"          2>nul
del /Q "%TARGET%incompatible-mods.log"          2>nul
del /Q "%TARGET%failed-mods-catalog.txt"        2>nul
del /Q "%TARGET%runtime.log"                    2>nul
del /Q "%TARGET%runtime.archive-*.log"          2>nul
del /Q "%TARGET%selftest.log"                   2>nul
del /Q "%TARGET%selftest.json"                  2>nul
del /Q "%TARGET%betadeps-compat-warnings.log"   2>nul
del /Q "%TARGET%saveshield-swallow.flag"        2>nul
del /Q "%TARGET%saveshield-swallow-disabled.flag" 2>nul
del /Q "%TARGET%patchshield-disabled.flag"      2>nul
del /Q "%TARGET%auto-disable-enabled.flag"      2>nul
del /Q "%TARGET%PatchedOptions.dump.xml"        2>nul
del /Q "%TARGET%merged-*.xml"                   2>nul

REM M5 (Phase 4.2): remove the persistent User-scope env var BetaDeps sets
REM so BLSE's launcher shows the dependency modules. Harmless if absent;
REM BetaDeps re-creates it on the next launch if still installed.
reg delete "HKCU\Environment" /v CREST_SHOW_STUBS /f >nul 2>nul

echo.
echo Done. Launch Bannerlord and the Mod Configuration tab should be back.
echo If it isn't, drag the new runtime.log to the BetaDeps Nexus posts page.
pause
