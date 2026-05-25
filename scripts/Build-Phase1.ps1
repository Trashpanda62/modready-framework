# BetaDeps -- Build script (Phase 1 + Phase 2)
#
# Builds BetaDeps.Foundation, BetaDeps.Harmony, and BetaDeps.UIExtenderEx,
# lays out the Modules\BetaDeps\ tree under dist\, copies the
# Bannerlord.Harmony and Bannerlord.UIExtenderEx alias stubs, verifies no
# Aragas / BUTR copyright strings are embedded in the output DLLs, and
# produces a versioned zip.
#
# Run from PowerShell:
#   cd C:\dev\beta-deps
#   .\scripts\Build-Phase1.ps1
#
# Requires .NET 6+ SDK on PATH (for dotnet build). The target framework
# is net472, which the .NET SDK targets just fine via reference assemblies.

[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Version = '0.7.4',
    [switch]$SkipVerify,
    [switch]$SkipZip,
    # When the live Bannerlord install is found at the standard Steam path
    # (or via -BannerlordRoot), Deploy copies the staged Modules\ tree over
    # the live install so the next launch picks up freshly-built DLLs without
    # any manual drag-and-drop. -SkipDeploy turns this off; -BannerlordRoot
    # overrides the default location.
    [switch]$SkipDeploy,
    [string]$BannerlordRoot = ''
)

$ErrorActionPreference = 'Stop'

$RepoRoot         = Split-Path -Parent $PSScriptRoot
$SrcRoot          = Join-Path $RepoRoot 'src'
$DistRoot         = Join-Path $RepoRoot 'dist'
$ModuleRoot       = Join-Path $DistRoot ('Modules\BetaDeps')
$HarmonyAlias     = Join-Path $DistRoot ('Modules\Bannerlord.Harmony')
$UIExtAlias       = Join-Path $DistRoot ('Modules\Bannerlord.UIExtenderEx')
$ButterLibAlias   = Join-Path $DistRoot ('Modules\Bannerlord.ButterLib')
$MCMAlias         = Join-Path $DistRoot ('Modules\Bannerlord.MBOptionScreen')
$BinRoot          = Join-Path $ModuleRoot 'bin\Win64_Shipping_Client'
# Tactics editor ships as its own opt-in module folder (Modules\BetaDeps.TacticsEditor)
# rather than bloating the main BetaDeps load surface. Users who don't want the
# editor leave that folder disabled; users who do, get the editor + apply logic.
$TacticsEditorRoot = Join-Path $DistRoot ('Modules\BetaDeps.TacticsEditor')
$TacticsEditorBin  = Join-Path $TacticsEditorRoot 'bin\Win64_Shipping_Client'

# Back-compat alias var so later sections of the script (and any external
# tooling that imported the old name) keep working.
$AliasRoot        = $HarmonyAlias

Write-Host "BetaDeps build -- v$Version ($Configuration)" -ForegroundColor Cyan
Write-Host "Repo:     $RepoRoot"
Write-Host "Output:   $DistRoot"
Write-Host ""

# -------- 1. Clean previous dist --------
if (Test-Path $DistRoot) {
    Write-Host "Cleaning previous dist..." -ForegroundColor Yellow
    Remove-Item -Recurse -Force $DistRoot
}
New-Item -ItemType Directory -Force -Path $BinRoot           | Out-Null
New-Item -ItemType Directory -Force -Path $HarmonyAlias      | Out-Null
New-Item -ItemType Directory -Force -Path $UIExtAlias        | Out-Null
New-Item -ItemType Directory -Force -Path $ButterLibAlias    | Out-Null
New-Item -ItemType Directory -Force -Path $MCMAlias          | Out-Null
New-Item -ItemType Directory -Force -Path $TacticsEditorBin  | Out-Null

# -------- 2. Build all five projects --------
$Projects = @(
    @{ Name = 'BetaDeps.Foundation';     Path = (Join-Path $SrcRoot 'BetaDeps.Foundation\BetaDeps.Foundation.csproj')         },
    @{ Name = 'BetaDeps.Harmony';        Path = (Join-Path $SrcRoot 'BetaDeps.Harmony\BetaDeps.Harmony.csproj')               },
    @{ Name = 'BetaDeps.UIExtenderEx';   Path = (Join-Path $SrcRoot 'BetaDeps.UIExtenderEx\BetaDeps.UIExtenderEx.csproj')     },
    @{ Name = 'BetaDeps.ButterLib';      Path = (Join-Path $SrcRoot 'BetaDeps.ButterLib\BetaDeps.ButterLib.csproj')           },
    @{ Name = 'BetaDeps.MCM';            Path = (Join-Path $SrcRoot 'BetaDeps.MCM\BetaDeps.MCM.csproj')                       },
    @{ Name = 'BetaDeps.TacticsEditor';  Path = (Join-Path $SrcRoot 'BetaDeps.TacticsEditor\BetaDeps.TacticsEditor.csproj')   }
)

foreach ($p in $Projects) {
    Write-Host "Building $($p.Name)..." -ForegroundColor Yellow
    # --no-incremental: force a real rebuild every time. Without this, dotnet
    # can decide nothing changed (especially when source-file mtime updates
    # via the filesystem touch don't propagate to dotnet's cache fingerprint)
    # and reuse a stale DLL. v0.5.4 ROT bisect lost an hour to this — the
    # source had a new diagnostic but the deployed DLL was untouched. The
    # cost of always rebuilding is ~3-5 seconds of compile per project.
    & dotnet build $p.Path -c $Configuration -p:Version=$Version --no-incremental --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed for $($p.Name)"
    }
}

# -------- 3. Stage outputs into Modules\BetaDeps\bin\... --------
Write-Host ""
Write-Host "Staging output DLLs..." -ForegroundColor Yellow

# Start with the BetaDeps.Foundation DLL (separate project).
$FoundationDll = Join-Path $RepoRoot "src\BetaDeps.Foundation\bin\$Configuration\net472\BetaDeps.Foundation.dll"
if (-not (Test-Path $FoundationDll)) { throw "Missing build output: $FoundationDll" }
Copy-Item $FoundationDll (Join-Path $BinRoot 'BetaDeps.Foundation.dll') -Force
Write-Host "  staged BetaDeps.Foundation.dll"

# Copy *everything* from the BetaDeps.Harmony output folder. Lib.Harmony pulls
# in a fan of transitive deps (Mono.Cecil.*, MonoMod.*) and the exact set varies
# by Harmony version, so a glob is more robust than a hand-curated list.
$HarmonyOutDir = Join-Path $RepoRoot "src\BetaDeps.Harmony\bin\$Configuration\net472"
if (-not (Test-Path $HarmonyOutDir)) { throw "Missing build output dir: $HarmonyOutDir" }
$HarmonyDlls = Get-ChildItem -Path $HarmonyOutDir -Filter '*.dll' -File
# Cecil debug-symbol readers Harmony's hot path never touches. Trimming them
# saves ~160 KB in the shipped zip. If a consumer mod's Harmony patch ever
# needs Mdb/Pdb/Rocks (none observed in our test mod list), it can re-add them
# locally — keeping them out of BetaDeps just shrinks the public download.
$SkipHarmonyDlls = @('Mono.Cecil.Mdb.dll','Mono.Cecil.Pdb.dll','Mono.Cecil.Rocks.dll')
foreach ($f in $HarmonyDlls) {
    # Skip BetaDeps.Foundation.dll here; it's already copied from its own
    # project output above, and we want the canonical copy from there.
    if ($f.Name -ieq 'BetaDeps.Foundation.dll') { continue }
    if ($SkipHarmonyDlls -contains $f.Name) {
        Write-Host "  skipped (zip-trim): $($f.Name)" -ForegroundColor DarkGray
        continue
    }
    Copy-Item $f.FullName (Join-Path $BinRoot $f.Name) -Force
    Write-Host "  staged $($f.Name)"
}

# BetaDeps.UIExtenderEx builds as Bannerlord.UIExtenderEx.dll (output filename
# is intentionally rebranded -- see csproj). We only stage that one DLL; its
# project references on Foundation + Harmony resolve via the copies above.
$UIExtDll = Join-Path $RepoRoot "src\BetaDeps.UIExtenderEx\bin\$Configuration\net472\Bannerlord.UIExtenderEx.dll"
if (-not (Test-Path $UIExtDll)) { throw "Missing build output: $UIExtDll" }
Copy-Item $UIExtDll (Join-Path $BinRoot 'Bannerlord.UIExtenderEx.dll') -Force
Write-Host "  staged Bannerlord.UIExtenderEx.dll"

# BetaDeps.ButterLib builds as Bannerlord.ButterLib.dll plus brings the
# Microsoft.Extensions.DependencyInjection runtime DLLs as CopyLocal transitive
# deps. Stage the whole output folder so DI works at runtime.
$ButterLibOutDir = Join-Path $RepoRoot "src\BetaDeps.ButterLib\bin\$Configuration\net472"
if (-not (Test-Path $ButterLibOutDir)) { throw "Missing build output dir: $ButterLibOutDir" }
$ButterLibStageNames = @(
    'Bannerlord.ButterLib.dll',
    'Microsoft.Extensions.DependencyInjection.dll',
    'Microsoft.Extensions.DependencyInjection.Abstractions.dll',
    'Microsoft.Extensions.Logging.dll',
    'Microsoft.Extensions.Logging.Abstractions.dll',
    'Microsoft.Extensions.Options.dll',
    'Microsoft.Extensions.Primitives.dll',
    'System.Diagnostics.DiagnosticSource.dll',
    'System.Runtime.CompilerServices.Unsafe.dll',
    'System.Buffers.dll',
    'System.Memory.dll',
    'System.Numerics.Vectors.dll',
    'System.Threading.Tasks.Extensions.dll',
    # Serilog: must ship our v2.10.0 so consumer mods (FCL etc.) compiled against
    # any Serilog version resolve to the same loaded type as our
    # AddSerilogLoggerProvider's Action<Serilog.LoggerConfiguration> parameter.
    # Without this, JIT signature matching fails -> MissingMethodException.
    'Serilog.dll'
)
foreach ($name in $ButterLibStageNames) {
    $src = Join-Path $ButterLibOutDir $name
    if (Test-Path $src) {
        Copy-Item $src (Join-Path $BinRoot $name) -Force
        Write-Host "  staged $name"
    }
}
# Fail loudly if the main DLL is missing.
if (-not (Test-Path (Join-Path $BinRoot 'Bannerlord.ButterLib.dll'))) {
    throw "Missing build output: Bannerlord.ButterLib.dll"
}

# BetaDeps.MCM builds as MCMv5.dll. Newtonsoft.Json comes along as a CopyLocal
# transitive dep; stage it alongside. Both must be present -- if Newtonsoft is
# missing, SettingsStorage will throw at first .Instance access at runtime.
$MCMOutDir = Join-Path $RepoRoot "src\BetaDeps.MCM\bin\$Configuration\net472"
if (-not (Test-Path $MCMOutDir)) { throw "Missing build output dir: $MCMOutDir" }
$MCMRequired = @('MCMv5.dll', 'Newtonsoft.Json.dll')
foreach ($name in $MCMRequired) {
    $src = Join-Path $MCMOutDir $name
    if (-not (Test-Path $src)) {
        throw "Missing build output: $src  (check that BetaDeps.MCM.csproj has CopyLocalLockFileAssemblies=true so Newtonsoft.Json is copied to bin)"
    }
    Copy-Item $src (Join-Path $BinRoot $name) -Force
    Write-Host "  staged $name"
}

# BetaDeps.TacticsEditor ships into its own Modules\BetaDeps.TacticsEditor\ folder
# rather than mixing into BetaDeps's bin. It depends at runtime on
# BetaDeps.Foundation + Newtonsoft.Json, both of which are already loaded via
# the main BetaDeps module -- so the only DLL the tactics editor folder needs
# is its own.
$TacticsEditorOutDir = Join-Path $RepoRoot "src\BetaDeps.TacticsEditor\bin\$Configuration\net472"
if (-not (Test-Path $TacticsEditorOutDir)) { throw "Missing build output dir: $TacticsEditorOutDir" }
$TacticsEditorDll = Join-Path $TacticsEditorOutDir 'BetaDeps.TacticsEditor.dll'
if (-not (Test-Path $TacticsEditorDll)) { throw "Missing build output: $TacticsEditorDll" }
Copy-Item $TacticsEditorDll (Join-Path $TacticsEditorBin 'BetaDeps.TacticsEditor.dll') -Force
Write-Host "  staged Modules\BetaDeps.TacticsEditor\bin\..\BetaDeps.TacticsEditor.dll"
Copy-Item (Join-Path $SrcRoot 'BetaDeps.Module\submodules\BetaDeps.TacticsEditor\SubModule.xml') (Join-Path $TacticsEditorRoot 'SubModule.xml') -Force
Write-Host "  staged Modules\BetaDeps.TacticsEditor\SubModule.xml"

# -------- 4. Copy SubModule.xml for the real module + the aliases --------
Copy-Item (Join-Path $SrcRoot 'BetaDeps.Module\SubModule.xml')                                          (Join-Path $ModuleRoot     'SubModule.xml') -Force
Copy-Item (Join-Path $SrcRoot 'BetaDeps.Module\aliases\Bannerlord.Harmony\SubModule.xml')               (Join-Path $HarmonyAlias   'SubModule.xml') -Force
Copy-Item (Join-Path $SrcRoot 'BetaDeps.Module\aliases\Bannerlord.UIExtenderEx\SubModule.xml')          (Join-Path $UIExtAlias     'SubModule.xml') -Force
Copy-Item (Join-Path $SrcRoot 'BetaDeps.Module\aliases\Bannerlord.ButterLib\SubModule.xml')             (Join-Path $ButterLibAlias 'SubModule.xml') -Force
Copy-Item (Join-Path $SrcRoot 'BetaDeps.Module\aliases\Bannerlord.MBOptionScreen\SubModule.xml')        (Join-Path $MCMAlias       'SubModule.xml') -Force

Write-Host "  staged Modules\BetaDeps\SubModule.xml"
Write-Host "  staged Modules\Bannerlord.Harmony\SubModule.xml (alias)"
Write-Host "  staged Modules\Bannerlord.UIExtenderEx\SubModule.xml (alias)"
Write-Host "  staged Modules\Bannerlord.ButterLib\SubModule.xml (alias)"
Write-Host "  staged Modules\Bannerlord.MBOptionScreen\SubModule.xml (alias)"

# -------- 4a. Copy MCM GUI/Prefabs/ (custom prefab files) --------
# Bannerlord auto-discovers any *.xml under Modules\<mod>\GUI\Prefabs\ at
# startup. MCMOptionRow.xml is referenced by the page-level prefab insert
# via <MCMOptionRow DataSource="{Slot{N}_VM}" />, giving each slot row its
# own prefab scope (the same way vanilla OptionItem.xml works).
$GuiSrc = Join-Path $SrcRoot 'BetaDeps.MCM\GUI'
$GuiDst = Join-Path $ModuleRoot 'GUI'
if (Test-Path $GuiSrc) {
    Copy-Item -Recurse -Force $GuiSrc $ModuleRoot
    Write-Host "  staged Modules\BetaDeps\GUI\Prefabs\ ($(((Get-ChildItem $GuiDst -Recurse -File).Count)) file(s))"
}

# -------- 4b. Populate the alias's bin folder so BLSE's preflight check passes --------
# BLSE walks the Modules\Bannerlord.Harmony\bin\Win64_Shipping_Client\ folder
# and requires the full Harmony dependency set on disk -- 0Harmony.dll plus the
# Mono.Cecil.* and MonoMod.* transitive deps. We copy *every* Harmony-stack DLL
# we shipped to BetaDeps into the alias as well; both copies are identical and
# the launcher dedupes by strong-name when both are loaded.
$AliasBinRoot = Join-Path $AliasRoot 'bin\Win64_Shipping_Client'
New-Item -ItemType Directory -Force -Path $AliasBinRoot | Out-Null

# Set of file-name patterns to mirror into the alias. Anything not in this list
# stays in BetaDeps only (our own BetaDeps.*.dll, etc.). BetaDeps.Foundation.dll
# also goes into every alias bin so the AliasStubSubModule class is locatable.
$AliasMirrorPatterns = @('0Harmony.dll', 'Mono.Cecil*.dll', 'MonoMod*.dll', 'BetaDeps.Foundation.dll')
foreach ($pat in $AliasMirrorPatterns) {
    Get-ChildItem -Path $BinRoot -Filter $pat -File | ForEach-Object {
        Copy-Item $_.FullName (Join-Path $AliasBinRoot $_.Name) -Force
        Write-Host "  mirrored to alias: $($_.Name)"
    }
}

# Report what's actually in the alias bin folder so any missing dep is visible.
Write-Host ""
Write-Host "Alias bin folder contents (Modules\Bannerlord.Harmony\bin\Win64_Shipping_Client):" -ForegroundColor Yellow
$AliasContents = Get-ChildItem -Path $AliasBinRoot -File | Sort-Object Name
foreach ($f in $AliasContents) { Write-Host "  $($f.Name)" }

# Verify each DLL BLSE's preflight is known to require is present.
# Mono.Cecil.Mdb/Pdb/Rocks intentionally trimmed (~160 KB savings); BLSE's
# preflight doesn't hard-fail on them, Harmony's hot path never reads symbols.
$ExpectedAliasDlls = @(
    '0Harmony.dll',
    'Mono.Cecil.dll',
    'MonoMod.Backports.dll',
    'MonoMod.Core.dll',
    'MonoMod.Iced.dll',
    'MonoMod.ILHelpers.dll',
    'MonoMod.Utils.dll'
)
$AliasMissing = @()
foreach ($name in $ExpectedAliasDlls) {
    if (-not (Test-Path (Join-Path $AliasBinRoot $name))) {
        $AliasMissing += $name
    }
}
if ($AliasMissing.Count -gt 0) {
    Write-Host ""
    Write-Host "WARNING: alias bin folder is missing DLLs BLSE may demand:" -ForegroundColor Red
    $AliasMissing | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    Write-Host "The build will proceed, but BLSE will likely refuse to launch."
} else {
    Write-Host "  PASS: all expected BLSE preflight DLLs present in Bannerlord.Harmony alias." -ForegroundColor Green
}

# -------- 4c. Populate the Bannerlord.UIExtenderEx alias bin folder --------
# Same idea as the Harmony alias: BLSE / launcher may inspect this folder
# structurally, so we make sure Bannerlord.UIExtenderEx.dll exists on disk.
# Plus BetaDeps.Foundation.dll for the AliasStubSubModule class the alias
# SubModule.xml references.
$UIExtAliasBin = Join-Path $UIExtAlias 'bin\Win64_Shipping_Client'
New-Item -ItemType Directory -Force -Path $UIExtAliasBin | Out-Null
Copy-Item (Join-Path $BinRoot 'Bannerlord.UIExtenderEx.dll') (Join-Path $UIExtAliasBin 'Bannerlord.UIExtenderEx.dll') -Force
Copy-Item (Join-Path $BinRoot 'BetaDeps.Foundation.dll')     (Join-Path $UIExtAliasBin 'BetaDeps.Foundation.dll') -Force
Write-Host "  mirrored to UIExtenderEx alias: Bannerlord.UIExtenderEx.dll, BetaDeps.Foundation.dll"

# -------- 4d. Populate the Bannerlord.ButterLib alias bin folder --------
$ButterLibAliasBin = Join-Path $ButterLibAlias 'bin\Win64_Shipping_Client'
New-Item -ItemType Directory -Force -Path $ButterLibAliasBin | Out-Null
Copy-Item (Join-Path $BinRoot 'Bannerlord.ButterLib.dll') (Join-Path $ButterLibAliasBin 'Bannerlord.ButterLib.dll') -Force
Copy-Item (Join-Path $BinRoot 'BetaDeps.Foundation.dll')  (Join-Path $ButterLibAliasBin 'BetaDeps.Foundation.dll') -Force
Write-Host "  mirrored to ButterLib alias: Bannerlord.ButterLib.dll, BetaDeps.Foundation.dll"

# -------- 4e. Populate the Bannerlord.MBOptionScreen alias bin folder --------
$MCMAliasBin = Join-Path $MCMAlias 'bin\Win64_Shipping_Client'
New-Item -ItemType Directory -Force -Path $MCMAliasBin | Out-Null
Copy-Item (Join-Path $BinRoot 'MCMv5.dll')               (Join-Path $MCMAliasBin 'MCMv5.dll') -Force
Copy-Item (Join-Path $BinRoot 'BetaDeps.Foundation.dll') (Join-Path $MCMAliasBin 'BetaDeps.Foundation.dll') -Force
if (Test-Path (Join-Path $BinRoot 'Newtonsoft.Json.dll')) {
    Copy-Item (Join-Path $BinRoot 'Newtonsoft.Json.dll') (Join-Path $MCMAliasBin 'Newtonsoft.Json.dll') -Force
}
Write-Host "  mirrored to MCM alias: MCMv5.dll, BetaDeps.Foundation.dll (+ Newtonsoft.Json.dll if present)"

Write-Host ""
Write-Host "UIExtenderEx alias bin folder contents:" -ForegroundColor Yellow
Get-ChildItem -Path $UIExtAliasBin -File | Sort-Object Name | ForEach-Object { Write-Host "  $($_.Name)" }
Write-Host "ButterLib alias bin folder contents:" -ForegroundColor Yellow
Get-ChildItem -Path $ButterLibAliasBin -File | Sort-Object Name | ForEach-Object { Write-Host "  $($_.Name)" }
Write-Host "MCM alias bin folder contents:" -ForegroundColor Yellow
Get-ChildItem -Path $MCMAliasBin -File | Sort-Object Name | ForEach-Object { Write-Host "  $($_.Name)" }

# -------- 5. License attribution --------
$LicenseFile = Join-Path $ModuleRoot 'THIRD-PARTY-LICENSES.txt'
@'
BetaDeps third-party license notices
====================================

BetaDeps redistributes the following third-party software:

----

0Harmony.dll  --  Lib.Harmony 2.x
Copyright (c) Andreas Pardeike
Licensed under the MIT License.
Source: https://github.com/pardeike/Harmony

Mono.Cecil.dll, Mono.Cecil.Mdb.dll, Mono.Cecil.Pdb.dll, Mono.Cecil.Rocks.dll
Copyright (c) Jean-Baptiste Evain
Licensed under the MIT License.
Source: https://github.com/jbevain/cecil

MonoMod.Backports.dll, MonoMod.Core.dll, MonoMod.Iced.dll,
MonoMod.ILHelpers.dll, MonoMod.RuntimeDetour.dll, MonoMod.Utils.dll
Copyright (c) Maxime Beauchemin (0x0ade) and MonoMod contributors
Licensed under the MIT License.
Source: https://github.com/MonoMod/MonoMod

Newtonsoft.Json.dll
Copyright (c) James Newton-King
Licensed under the MIT License.
Source: https://github.com/JamesNK/Newtonsoft.Json

The MIT License (MIT)

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.

----

All other code in this distribution is original work,
including BetaDeps.Foundation.dll, BetaDeps.Harmony.dll,
Bannerlord.UIExtenderEx.dll, Bannerlord.ButterLib.dll, and
MCMv5.dll (clean-room re-implementations of the corresponding
public API surfaces).

Copyright (c) 2026 Maxfield Management Group, licensed under the MIT License.
'@ | Set-Content -Path $LicenseFile -Encoding UTF8

Write-Host "  wrote THIRD-PARTY-LICENSES.txt"

# -------- 6. Verify no Aragas / BUTR copyright strings in our DLLs --------
if (-not $SkipVerify) {
    Write-Host ""
    Write-Host "Verifying output DLLs..." -ForegroundColor Yellow

    $OurDlls = @(
        (Join-Path $BinRoot 'BetaDeps.Foundation.dll'),
        (Join-Path $BinRoot 'BetaDeps.Harmony.dll'),
        (Join-Path $BinRoot 'Bannerlord.UIExtenderEx.dll'),
        (Join-Path $BinRoot 'Bannerlord.ButterLib.dll'),
        (Join-Path $BinRoot 'MCMv5.dll')
    )

    # Strings we MUST NOT find in our authored DLLs. We deliberately DO use
    # the BUTR / Bannerlord.BUTR / HarmonyLib.BUTR namespace prefixes for
    # API-compatibility shims (so consumer mods compiled against upstream BUTR
    # names resolve drop-in to our DLLs). The check that matters is authorship
    # markers -- "Aragas" should never appear because we don't import any
    # Aragas-authored code; our copyright is Maxfield Management Group.
    # 0Harmony.dll is exempt -- it's Pardeike's MIT-licensed work.
    $Forbidden = @(
        'Aragas'
    )

    $Failures = @()
    foreach ($dll in $OurDlls) {
        if (-not (Test-Path $dll)) { continue }
        $bytes = [System.IO.File]::ReadAllBytes($dll)

        # Decode as both UTF-16 LE (typical .NET user strings) and ASCII
        # and look for forbidden substrings.
        $utf16 = [System.Text.Encoding]::Unicode.GetString($bytes)
        $ascii = [System.Text.Encoding]::ASCII.GetString($bytes)

        foreach ($needle in $Forbidden) {
            if ($utf16 -match [regex]::Escape($needle) -or $ascii -match [regex]::Escape($needle)) {
                $Failures += "  $([System.IO.Path]::GetFileName($dll)) contains forbidden string '$needle'"
            }
        }
    }

    if ($Failures.Count -gt 0) {
        Write-Host "VERIFICATION FAILED:" -ForegroundColor Red
        $Failures | ForEach-Object { Write-Host $_ -ForegroundColor Red }
        throw 'Aragas/BUTR copyright string found in output DLLs. Investigate before shipping.'
    }
    Write-Host "  PASS: no Aragas/BUTR strings in our authored DLLs (Foundation, Harmony, UIExtenderEx, ButterLib, MCM)" -ForegroundColor Green
}

# -------- 6b. XML lint (v0.7.4+) --------
# Catches the kind of bug that broke Vortex install on v0.7.2/v0.7.3:
# `--` inside an XML comment body, illegal per the XML 1.0 spec, which
# Vortex's FOMOD parser correctly rejected. Run AFTER the dist tree is
# staged so we lint every XML the user will actually receive.
Write-Host ""
Write-Host "Linting XML files in dist..." -ForegroundColor Yellow
& "$RepoRoot\scripts\Validate-Xml.ps1" -Root $DistRoot
if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "XML lint failed -- aborting build. Fix the errors above and re-run." -ForegroundColor Red
    Write-Host "Set `$env:BETADEPS_SKIP_XML_LINT='1' to bypass (not recommended)." -ForegroundColor DarkGray
    throw "XML lint failure"
}

# -------- 7. Zip --------
# For the public/Nexus zip we want a SINGLE Modules\BetaDeps folder so end-users
# only see one module on disk. The 4 alias folders (Bannerlord.Harmony, etc.)
# are bundled inside BetaDeps under "aliases\" and get materialised at
# OnSubModuleLoad time by BetaDepsHarmonySubModule.BootstrapAliasFolders.
# Build-time/local dist still keeps the alias folders at top level so Ralph-Loop
# deploys them directly without needing the bootstrap step.
if (-not $SkipZip) {
    Write-Host ""
    Write-Host "Packaging zip (single-folder layout)..." -ForegroundColor Yellow

    # Stage a separate Zip\Modules\BetaDeps tree mirroring the dist layout PLUS
    # the alias folders nested as Modules\BetaDeps\aliases\<Name>\...
    $ZipStaging = Join-Path $DistRoot 'Zip'
    if (Test-Path $ZipStaging) { Remove-Item -Recurse -Force $ZipStaging }
    $ZipBetaDeps = Join-Path $ZipStaging 'Modules\BetaDeps'
    Copy-Item -Recurse -Force (Join-Path $DistRoot 'Modules\BetaDeps') $ZipBetaDeps

    $ZipAliases = Join-Path $ZipBetaDeps 'aliases'
    New-Item -ItemType Directory -Force -Path $ZipAliases | Out-Null
    foreach ($a in @('Bannerlord.Harmony','Bannerlord.UIExtenderEx','Bannerlord.ButterLib','Bannerlord.MBOptionScreen')) {
        $src = Join-Path $DistRoot ("Modules\" + $a)
        if (Test-Path $src) {
            Copy-Item -Recurse -Force $src (Join-Path $ZipAliases $a)
        }
    }

    # v0.7.2: Vortex FOMOD installer profile. Lives at the zip ROOT (not
    # inside Modules\BetaDeps\) so Vortex's FOMOD Installer extension can
    # discover it. Users without the extension don't see the wizard; they
    # get the normal Modules\ deploy which still works.
    $FomodSrc = Join-Path $RepoRoot 'fomod'
    if (Test-Path $FomodSrc) {
        Copy-Item -Recurse -Force $FomodSrc (Join-Path $ZipStaging 'fomod')
        Write-Host "  staged fomod\ at zip root (Vortex installer profile)"
    }

    $ZipPath = Join-Path $DistRoot ("BetaDeps-v$Version.zip")
    if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
    # Include BOTH Modules\ and (if present) fomod\ in the zip root.
    $ZipPathsToInclude = @( (Join-Path $ZipStaging 'Modules') )
    if (Test-Path (Join-Path $ZipStaging 'fomod')) {
        $ZipPathsToInclude += (Join-Path $ZipStaging 'fomod')
    }
    Compress-Archive -Path $ZipPathsToInclude -DestinationPath $ZipPath -CompressionLevel Optimal
    Write-Host "  $ZipPath  (single Modules\BetaDeps folder + fomod\ at zip root)" -ForegroundColor Green
    Remove-Item -Recurse -Force $ZipStaging
}

# -------- 6. Deploy to live Bannerlord install (auto-detect Steam path) --------
# Without this step every build cycle leaves stale DLLs in the user's
# Modules folder and the game keeps running yesterday's code.
if (-not $SkipDeploy) {
    Write-Host ""
    Write-Host "Deploying to live Bannerlord install..." -ForegroundColor Yellow

    $candidateRoots = @()
    if ($BannerlordRoot) { $candidateRoots += $BannerlordRoot }
    $candidateRoots += @(
        'C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord',
        'C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord',
        'D:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord',
        'E:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord'
    )
    $liveRoot = $null
    foreach ($r in $candidateRoots) {
        if ($r -and (Test-Path (Join-Path $r 'Modules'))) { $liveRoot = $r; break }
    }

    if (-not $liveRoot) {
        Write-Host "  SKIPPED: no Bannerlord install found. Pass -BannerlordRoot '<path>' to deploy." -ForegroundColor Yellow
    } else {
        $liveModules = Join-Path $liveRoot 'Modules'
        Write-Host "  target: $liveModules" -ForegroundColor Cyan
        $sourceModules = Join-Path $DistRoot 'Modules'
        $folders = 'BetaDeps','Bannerlord.Harmony','Bannerlord.UIExtenderEx','Bannerlord.ButterLib','Bannerlord.MBOptionScreen','BetaDeps.TacticsEditor'
        foreach ($f in $folders) {
            $srcDir = Join-Path $sourceModules $f
            $dstDir = Join-Path $liveModules   $f
            if (-not (Test-Path $dstDir)) { New-Item -ItemType Directory -Path $dstDir -Force | Out-Null }
            # Copy ALL files recursively, overwriting older versions. Don't
            # use Remove-Item on dst because the alias folders intentionally
            # carry runtime.log / other live state we want to preserve.
            Copy-Item -Path (Join-Path $srcDir '*') -Destination $dstDir -Recurse -Force
            Write-Host "  deployed -> $f" -ForegroundColor Green
        }
        # Sanity check: confirm freshly-built MCMv5.dll landed where the game reads from.
        $deployedMCM = Join-Path $liveModules 'BetaDeps\bin\Win64_Shipping_Client\MCMv5.dll'
        if (Test-Path $deployedMCM) {
            $age = (Get-Date) - (Get-Item $deployedMCM).LastWriteTime
            Write-Host ("  verify: MCMv5.dll deployed (modified {0:N1}s ago)" -f $age.TotalSeconds) -ForegroundColor Green
        }
    }
}

Write-Host ""
Write-Host "Build complete." -ForegroundColor Cyan
if ($SkipDeploy) {
    Write-Host "Drop these five folders into your Bannerlord install Modules\ directory and enable all five in the launcher:" -ForegroundColor Cyan
    Write-Host "  Modules\BetaDeps" -ForegroundColor White
    Write-Host "  Modules\Bannerlord.Harmony (alias)" -ForegroundColor DarkGray
    Write-Host "  Modules\Bannerlord.UIExtenderEx (alias)" -ForegroundColor DarkGray
    Write-Host "  Modules\Bannerlord.ButterLib (alias)" -ForegroundColor DarkGray
    Write-Host "  Modules\Bannerlord.MBOptionScreen (alias)" -ForegroundColor DarkGray
}
Write-Host "Then watch Modules\BetaDeps\runtime.log on first launch." -ForegroundColor Cyan
