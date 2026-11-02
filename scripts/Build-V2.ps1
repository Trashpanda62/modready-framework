# ModReady v2 -- build script (standalone add-on)
#
# v2.0 ModReady is one module (Modules\ModReady) that runs on top of BUTR's
# own Bannerlord.Harmony. It ships no Bannerlord.* modules and no BUTR-named
# assemblies. This script builds ModReady.Harmony (+ Foundation), stages the
# module under dist\v2\Modules\ModReady, stages the four "retired" notice
# modules that replace ModReady's old Workshop dependency items, verifies the
# output, and zips the release.
#
# Run from PowerShell:
#   cd C:\dev\modready\framework
#   .\scripts\Build-V2.ps1 -Version 2.0.0

[CmdletBinding()]
param(
    [string]$Version = '2.0.0',
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$SrcRoot  = Join-Path $RepoRoot 'src'
$OutRoot  = Join-Path $RepoRoot 'dist\v2'
$Modules  = Join-Path $OutRoot 'Modules'
$ModRoot  = Join-Path $Modules 'ModReady'
$BinRoot  = Join-Path $ModRoot 'bin\Win64_Shipping_Client'
$Retired  = Join-Path $OutRoot 'Retired'

Write-Host "ModReady v2 build -- v$Version ($Configuration)" -ForegroundColor Cyan

# 1. Build (net472 = Steam client runtime)
$proj = Join-Path $SrcRoot 'ModReady.Harmony\ModReady.Harmony.csproj'
& dotnet build $proj -c $Configuration -f net472 -nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed ($LASTEXITCODE)" }
$buildOut = Join-Path $SrcRoot "ModReady.Harmony\bin\x64\$Configuration\net472"
if (-not (Test-Path $buildOut)) { $buildOut = Join-Path $SrcRoot "ModReady.Harmony\bin\$Configuration\net472" }
if (-not (Test-Path $buildOut)) { throw "build output not found under ModReady.Harmony\bin" }

# 2. Stage a clean tree (move any previous stage to a dated backup instead of deleting)
if (Test-Path $OutRoot) {
    $bak = "$OutRoot-prev-$(Get-Date -Format yyyyMMdd-HHmmss)"
    Move-Item $OutRoot $bak
}
New-Item -ItemType Directory -Force $BinRoot | Out-Null

foreach ($dll in 'ModReady.Harmony.dll', 'ModReady.Foundation.dll') {
    Copy-Item (Join-Path $buildOut $dll) $BinRoot
}
$sub = Get-Content (Join-Path $SrcRoot 'ModReady.Module\SubModule.xml') -Raw
$sub = $sub -replace '<Version value="v[0-9.]+" />', "<Version value=`"v$Version`" />"
Set-Content (Join-Path $ModRoot 'SubModule.xml') $sub -Encoding utf8
Copy-Item (Join-Path $RepoRoot 'LICENSE') (Join-Path $ModRoot 'LICENSE.txt')

# 3. Retired notice modules (content for the four old Workshop dependency items).
#    Distinct Ids, no DLLs, no SubModules: they only tell the player to unsubscribe.
$retiredItems = @(
    @{ Key = 'Harmony';      Upstream = 'Harmony' },
    @{ Key = 'UIExtenderEx'; Upstream = 'UIExtenderEx' },
    @{ Key = 'ButterLib';    Upstream = 'ButterLib' },
    @{ Key = 'MCM';          Upstream = 'Mod Configuration Menu v5' }
)
foreach ($r in $retiredItems) {
    $dir = Join-Path $Retired "ModReadyRetired.$($r.Key)"
    New-Item -ItemType Directory -Force $dir | Out-Null
    $xml = @"
<?xml version="1.0" encoding="UTF-8"?>
<Module>
  <Id value="ModReadyRetired.$($r.Key)" />
  <Name value="RETIRED - unsubscribe (old ModReady $($r.Key))" />
  <Version value="v$Version" />
  <DefaultModule value="false" />
  <ModuleCategory value="Singleplayer" />
  <ModuleType value="Community" />
  <DependedModules />
  <SubModules />
</Module>
"@
    Set-Content (Join-Path $dir 'SubModule.xml') $xml -Encoding utf8
    $readme = @"
This Workshop item used to contain ModReady's own build of $($r.Upstream).
ModReady no longer ships its own copies of the BUTR dependency modules.

Unsubscribe from this item, and subscribe to BUTR's original $($r.Upstream) instead.
ModReady itself is now a separate optional add-on that runs on top of BUTR's stack.
"@
    Set-Content (Join-Path $dir 'README.txt') $readme -Encoding utf8
}

# 4. Verify: no BUTR-named module/assembly in the ModReady module, no stray DLLs
$bad = Get-ChildItem $ModRoot -Recurse -File | Where-Object {
    $_.Name -match '^(0Harmony|Bannerlord\.|MCMv5|Mono\.|MonoMod|Newtonsoft|Serilog|Microsoft\.)'
}
if ($bad) { throw "Forbidden files staged: $($bad.Name -join ', ')" }
$dlls = (Get-ChildItem $BinRoot -Filter *.dll).Name | Sort-Object
Write-Host "Staged DLLs: $($dlls -join ', ')"
foreach ($dll in Get-ChildItem $BinRoot -Filter *.dll) {
    $bytes = [IO.File]::ReadAllBytes($dll.FullName)
    $text = [Text.Encoding]::Unicode.GetString($bytes) + [Text.Encoding]::ASCII.GetString($bytes)
    if ($text -match 'Aragas|BUTR Team|Bannerlord\.UIExtenderEx\.dll|MCMv5\.dll') {
        Write-Warning "$($dll.Name) mentions a BUTR identifier -- review before shipping"
    }
}

# 5. Zip
$zip = Join-Path $OutRoot "ModReady-v$Version.zip"
Compress-Archive -Path $Modules -DestinationPath $zip
Write-Host "Release zip: $zip" -ForegroundColor Green
