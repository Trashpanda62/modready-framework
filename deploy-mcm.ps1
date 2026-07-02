<#
.SYNOPSIS
  Deploy BetaDeps.MCM to the live Bannerlord install AND the dist staging folder.

.DESCRIPTION
  The MCM mod ships as TWO artifacts that live in DIFFERENT modules:
    1. MCMv5.dll            -> Modules\Bannerlord.MBOptionScreen\bin\Win64_Shipping_Client\
    2. GUI resources        -> Modules\BetaDeps\GUI\ (Brushes\BetaDeps.xml, Prefabs\*.xml)
       (compiled prefab templates live IN the DLL; runtime BRUSHES + standalone
        prefab XML live as loose GUI files in the BetaDeps module)

  History: a manual deploy loop that copied only the DLL left the brush file 3 days
  stale in-game, so every BetaDeps.* brush change silently never took effect (the
  v0.9.2 row-hover bug). This script copies BOTH artifacts to BOTH targets so the
  two never drift again.

.PARAMETER Build
  Run `dotnet build` (Release) before deploying. Omit to deploy the existing bin output.

.EXAMPLE
  pwsh deploy-mcm.ps1 -Build
#>
param(
    [switch]$Build
)

$ErrorActionPreference = 'Stop'

$repo     = "C:\dev\modready\framework"
$proj     = "$repo\src\BetaDeps.MCM\BetaDeps.MCM.csproj"
$dllOut   = "$repo\src\BetaDeps.MCM\bin\Release\net472\MCMv5.dll"
$guiSrc   = "$repo\src\BetaDeps.MCM\GUI"
$blRoot   = "C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules"

# (label, DLL target dir, BetaDeps-module GUI target dir)
$targets = @(
    @{ Name = "live"; Dll = "$blRoot\Bannerlord.MBOptionScreen\bin\Win64_Shipping_Client";          Gui = "$blRoot\BetaDeps\GUI" },
    @{ Name = "dist"; Dll = "$repo\dist\Modules\Bannerlord.MBOptionScreen\bin\Win64_Shipping_Client"; Gui = "$repo\dist\Modules\BetaDeps\GUI" }
)

if ($Build) {
    Write-Host "==> Building (Release)..." -ForegroundColor Cyan
    & dotnet build $proj -c Release --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "Build failed (exit $LASTEXITCODE)" }
}

if (-not (Test-Path $dllOut)) { throw "DLL not found: $dllOut  (run with -Build first)" }

$locked = $false
foreach ($t in $targets) {
    Write-Host "==> Deploying to $($t.Name)" -ForegroundColor Cyan

    # 1) DLL
    if (Test-Path $t.Dll) {
        try {
            Copy-Item $dllOut (Join-Path $t.Dll "MCMv5.dll") -Force -ErrorAction Stop
            Write-Host "    MCMv5.dll      OK"
        } catch {
            $locked = $true
            Write-Host "    MCMv5.dll      LOCKED (game running?) - skipped" -ForegroundColor Yellow
        }
    } else {
        Write-Host "    MCMv5.dll      target dir missing: $($t.Dll)" -ForegroundColor Yellow
    }

    # 2) GUI resources (Brushes + Prefabs), mirrored recursively
    if (Test-Path $t.Gui) {
        Get-ChildItem $guiSrc -Recurse -File | ForEach-Object {
            $rel = $_.FullName.Substring($guiSrc.Length).TrimStart('\')
            $dest = Join-Path $t.Gui $rel
            $destDir = Split-Path $dest -Parent
            if (-not (Test-Path $destDir)) { New-Item -ItemType Directory -Path $destDir -Force | Out-Null }
            Copy-Item $_.FullName $dest -Force
            Write-Host "    GUI\$rel  OK"
        }
    } else {
        Write-Host "    GUI            target dir missing: $($t.Gui)" -ForegroundColor Yellow
    }
}

if ($locked) {
    Write-Host "`nNOTE: DLL was locked by a running game. Close Bannerlord and re-run to update the DLL." -ForegroundColor Yellow
}
Write-Host "`nDone." -ForegroundColor Green
