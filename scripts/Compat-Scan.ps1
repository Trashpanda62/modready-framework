# BetaDeps -- Compat-Scan
#
# Offline static analyzer that opens every DLL in a mod folder with
# Mono.Cecil and reports each TaleWorlds.* MemberReference whose
# signature no longer exists in the currently-installed Bannerlord.
#
# Output is a per-mod report the mod author can act on directly:
#   - the full broken reference (Type.Method(arg1, arg2, ...))
#   - the closest matching current signature (if any)
#   - the DLL + class inside the mod where the broken call lives
#
# Designed to run BEFORE launching the game. Catches every static
# import, not just the ones that happen to fire at load time. Pairs
# with SaveShield's runtime CULPRIT identification for full coverage:
# Compat-Scan tells you what WILL break, SaveShield tells you what
# DID break and where the actual call site was.
#
# Usage:
#   .\scripts\Compat-Scan.ps1 -ModPath "C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\ROT-Core"
#   .\scripts\Compat-Scan.ps1 -ModPath "...\AdjustableLeveling" -Output dist\compat-AdjustableLeveling.md
#   .\scripts\Compat-Scan.ps1 -ModPath "...\Eagle_Rising" -BannerlordPath "C:\Steam\..."  # custom Bannerlord install
#
# Requires BetaDeps already installed (script reflectively loads
# Mono.Cecil from BetaDeps\bin).

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ModPath,

    [string]$BannerlordPath = 'C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord',

    [string]$Output,

    [switch]$Quiet,

    [int]$MaxSuggestions = 3
)

$ErrorActionPreference = 'Stop'

function Write-Log {
    param([string]$msg, [ConsoleColor]$Color = [ConsoleColor]::Gray)
    if (-not $Quiet) {
        Write-Host $msg -ForegroundColor $Color
    }
}

# Closest-type helper -- defined up front so the scan loop can call it.
function Find-ClosestType {
    param($Name, $ApiTypes, $Max = 3)
    $candidates = @($ApiTypes.Keys | Where-Object { $_.EndsWith(".$Name") } | Select-Object -First $Max)
    if ($candidates.Count -gt 0) { return ($candidates -join ', ') } else { return "(no current type with name '$Name')" }
}

function Normalize-Sig {
    # Collapse generic-parameter placeholders so MethodReference (Cecil IL
    # notation "!!0", "!!1") compares cleanly against MethodDefinition (PE
    # name "T", "TSource", etc.). Also strips reference indicators (&) so
    # `out T` and `T` resolve to the same token across both sides.
    #
    # Examples:
    #   "!!0"                      -> "<T>"
    #   "!0&"                      -> "<T>"
    #   "T"                        -> "<T>"
    #   "TSource"                  -> "<T>"
    #   "Boolean"                  -> "Boolean"
    #   "Func`2<!!0,System.Boolean>" -> "Func`2<<T>,System.Boolean>"
    param([string]$sig)
    if ([string]::IsNullOrEmpty($sig)) { return $sig }
    # IL-style generic placeholders: !!N (method-level) and !N (type-level).
    $sig = [regex]::Replace($sig, '!!\d+', '<T>')
    $sig = [regex]::Replace($sig, '(?<![\w])!(\d+)', '<T>')
    # PE-style generic-parameter names: any bare identifier that is a single
    # uppercase letter (T, U, V, K, etc.) or starts with T followed by an
    # uppercase letter (TSource, TKey, TResult, TTarget, ...) is a generic
    # parameter, not a real type. Match conservatively to avoid eating real
    # type names.
    $sig = [regex]::Replace($sig, '\b(T[A-Z][A-Za-z0-9]*|T|U|V|K|R)\b(?!\.)', '<T>')
    # Drop reference markers -- "T&" and "T" compare as the same for our purposes.
    $sig = $sig -replace '&', ''
    return $sig
}

function Format-MarkdownReport {
    param($mod, $report, $totalRefs, $totalBroken, $dlls, $bannerlordPath)
    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.AppendLine("# Compat-Scan report -- $mod")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("Generated $(Get-Date -Format 'yyyy-MM-dd HH:mm') by BetaDeps Compat-Scan.")
    [void]$sb.AppendLine("Bannerlord: $bannerlordPath")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("## Summary")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("- DLLs scanned: **$($dlls.Count)**")
    [void]$sb.AppendLine("- Total TaleWorlds.* references: **$totalRefs**")
    [void]$sb.AppendLine("- Broken references (will throw at runtime): **$totalBroken**")
    [void]$sb.AppendLine("")

    if ($totalBroken -eq 0) {
        [void]$sb.AppendLine("**No breaking changes detected.** All TaleWorlds references resolve cleanly against the current API.")
        return $sb.ToString()
    }

    $byKind = $report | Group-Object Kind | Sort-Object Count -Descending
    [void]$sb.AppendLine("## Breakage by category")
    [void]$sb.AppendLine("")
    foreach ($k in $byKind) {
        [void]$sb.AppendLine("- **$($k.Name)**: $($k.Count)")
    }
    [void]$sb.AppendLine("")

    foreach ($k in $byKind) {
        [void]$sb.AppendLine("### $($k.Name) ($($k.Count))")
        [void]$sb.AppendLine("")
        foreach ($r in ($k.Group | Sort-Object Reference)) {
            [void]$sb.AppendLine("- ``$($r.Reference)``")
            [void]$sb.AppendLine("  - In: **$($r.Dll)**")
            [void]$sb.AppendLine("  - Fix: $($r.Suggestion)")
            [void]$sb.AppendLine("")
        }
    }

    return $sb.ToString()
}

# ============================================================
# 1. Locate and load Mono.Cecil (shipped in BetaDeps' bin folder)
#
# v0.7.5 fix: copy the DLL to a per-PID temp path before LoadFrom so
# we don't hold the live install file open for the rest of the
# PowerShell session. Otherwise a follow-up Quick-Test / Build-Phase1
# in the same window fails to overwrite Modules\BetaDeps\bin\...\
# Mono.Cecil.dll because PowerShell still has the handle.
# ============================================================
$cecilCandidates = @(
    (Join-Path $BannerlordPath 'Modules\BetaDeps\bin\Win64_Shipping_Client\Mono.Cecil.dll'),
    (Join-Path $BannerlordPath 'Modules\BetaDeps\bin\Win64_Shipping_Client\v2\Mono.Cecil.dll')
)
$cecilPath = $cecilCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $cecilPath) {
    Write-Log "FAIL: Mono.Cecil.dll not found in BetaDeps\bin. Is BetaDeps installed at $BannerlordPath?" Red
    Write-Log "      Searched:" DarkGray
    $cecilCandidates | ForEach-Object { Write-Log "        $_" DarkGray }
    exit 2
}
# If Mono.Cecil is already loaded in this AppDomain (e.g. from a
# previous Compat-Scan run in the same PowerShell session, or from
# Compat-Scan-Batch invoking us in a loop), reuse the loaded copy
# instead of trying to LoadFrom a fresh path. The CLR identifies
# assemblies by strong name + version, so a re-LoadFrom of an
# already-resolved assembly returns the existing one anyway -- but
# the temp-file copy attempt would FAIL because the previous temp
# file is still locked by this process. Solved by checking up front.
$existingCecil = [AppDomain]::CurrentDomain.GetAssemblies() |
    Where-Object { $_.GetName().Name -eq 'Mono.Cecil' } |
    Select-Object -First 1
if ($existingCecil) {
    Write-Log "Mono.Cecil already loaded in this AppDomain ($($existingCecil.GetName().Version)); reusing." DarkGray
}
else {
    # Copy to a per-invocation temp path and LoadFrom that, so the live
    # install file is NOT held by this process. The temp file is left
    # behind (PowerShell still has it open) but the live install file
    # is free for subsequent build deploys.
    $cecilTemp = Join-Path $env:TEMP ("BetaDeps-Compat-Scan-Cecil-{0}-{1}.dll" -f $PID, [Guid]::NewGuid().ToString('N').Substring(0, 8))
    Copy-Item -Path $cecilPath -Destination $cecilTemp -Force
    Write-Log "Loaded Mono.Cecil from $cecilTemp (copy of live install)" DarkGray
    [System.Reflection.Assembly]::LoadFrom($cecilTemp) | Out-Null
}

# ============================================================
# 2. Resolve current TaleWorlds DLLs (the API of record)
# ============================================================
$bannerlordBin = Join-Path $BannerlordPath 'bin\Win64_Shipping_Client'
if (-not (Test-Path $bannerlordBin)) {
    Write-Log "FAIL: Bannerlord bin folder not found at $bannerlordBin" Red
    exit 2
}

# Build a lookup: type-FullName -> Cecil TypeDefinition (from current TaleWorlds DLLs).
Write-Log "Indexing current TaleWorlds API surface..." DarkGray
$apiTypes = @{}    # FullName -> TypeDefinition
$apiAssemblies = @{}  # short asm name -> ModuleDefinition (held open for the run)

# v0.7.5 fix: TaleWorlds ships its API across THREE locations, not just the
# root bin folder:
#   1. Bannerlord\bin\Win64_Shipping_Client          -- TaleWorlds.* core
#   2. Modules\Native\bin\Win64_Shipping_Client      -- a few view/UI helpers
#   3. Modules\<Module>\bin\Win64_Shipping_Client    -- SandBox.dll lives here,
#      not in #1. SandBox.GauntletUI, SandBox.View, SandBox.ViewModelCollection
#      too. StoryMode.* similarly under Modules\StoryMode\bin\.
# Earlier versions of this scan only looked at #1+#2, which made every
# SandBox.* type reference look "missing" when in reality they were present
# under Modules\SandBox\bin\. That produced a flood of false-positive
# MISSING_TYPE entries on the v0.7.5 first run.
$tw = @()
$tw += Get-ChildItem -Path $bannerlordBin -Filter "TaleWorlds*.dll" -ErrorAction SilentlyContinue
$tw += Get-ChildItem -Path $bannerlordBin -Filter "SandBox*.dll" -ErrorAction SilentlyContinue
$tw += Get-ChildItem -Path $bannerlordBin -Filter "StoryMode*.dll" -ErrorAction SilentlyContinue

# Per-module API folders. Each TaleWorlds-provided module that ships
# typed DLLs gets its bin folder added here.
$apiModuleFolders = @(
    'Modules\Native\bin\Win64_Shipping_Client',
    'Modules\SandBox\bin\Win64_Shipping_Client',
    'Modules\SandBoxCore\bin\Win64_Shipping_Client',
    'Modules\StoryMode\bin\Win64_Shipping_Client',
    'Modules\CustomBattle\bin\Win64_Shipping_Client',
    'Modules\Multiplayer\bin\Win64_Shipping_Client',
    'Modules\BirthAndDeath\bin\Win64_Shipping_Client',
    'Modules\NavalDLC\bin\Win64_Shipping_Client'
)
foreach ($rel in $apiModuleFolders) {
    $abs = Join-Path $BannerlordPath $rel
    if (Test-Path $abs) {
        $tw += Get-ChildItem -Path $abs -Filter "TaleWorlds*.dll" -ErrorAction SilentlyContinue
        $tw += Get-ChildItem -Path $abs -Filter "SandBox*.dll" -ErrorAction SilentlyContinue
        $tw += Get-ChildItem -Path $abs -Filter "StoryMode*.dll" -ErrorAction SilentlyContinue
    }
}

# Dedupe by FullName -- some DLLs are mirrored across Win64_Shipping_Client and
# Win64_Shipping_wEditor; we only want to index each unique file once.
$tw = $tw | Sort-Object FullName -Unique

foreach ($dll in $tw) {
    try {
        $module = [Mono.Cecil.ModuleDefinition]::ReadModule($dll.FullName)
        $apiAssemblies[$module.Assembly.Name.Name] = $module
        foreach ($t in $module.GetTypes()) {
            if (-not $apiTypes.ContainsKey($t.FullName)) {
                $apiTypes[$t.FullName] = $t
            }
        }
    } catch {
        Write-Log "WARN: failed to read $($dll.Name): $_" DarkYellow
    }
}
Write-Log "Indexed $($apiTypes.Count) public/internal types across $($apiAssemblies.Count) assemblies." DarkGray

# ============================================================
# 3. Walk the mod's DLLs and collect every TaleWorlds.* reference
# ============================================================
if (-not (Test-Path $ModPath)) {
    Write-Log "FAIL: Mod folder not found: $ModPath" Red
    exit 2
}

$modName = Split-Path -Leaf $ModPath
Write-Log ""
Write-Log "==== Compat-Scan: $modName ====" Cyan

# Find mod-owned DLLs. Skip vendored copies of Bannerlord libs,
# Harmony, Cecil, BCL, etc. -- those don't reflect THIS mod's API
# expectations.
$skipPrefix = @(
    'TaleWorlds.', 'SandBox', 'StoryMode',
    '0Harmony', 'Mono.', 'MonoMod', 'System.',
    'Microsoft.', 'Newtonsoft.', 'YamlDotNet',
    'Bannerlord.UIExtenderEx', 'Bannerlord.ButterLib',
    'Bannerlord.MBOptionScreen', 'Bannerlord.Harmony',
    'BetaDeps.', 'BLSE',
    'TaleWorlds', 'NetStandard', 'mscorlib'
)
$modDlls = Get-ChildItem -Path $ModPath -Recurse -Filter "*.dll" -ErrorAction SilentlyContinue |
    Where-Object {
        $name = $_.BaseName
        -not ($skipPrefix | Where-Object { $name.StartsWith($_, [StringComparison]::OrdinalIgnoreCase) })
    } |
    Where-Object { $_.FullName -match '(bin\\Win64_Shipping_Client|bin\\Win64_Shipping_wEditor|bin\\Win64)' -or $_.DirectoryName -eq $ModPath }

if ($modDlls.Count -eq 0) {
    Write-Log "No mod-owned DLLs found under $ModPath\bin\..." Yellow
    exit 0
}

Write-Log "Found $($modDlls.Count) mod DLL(s):" DarkGray
foreach ($d in $modDlls) { Write-Log "  - $($d.Name)" DarkGray }

# Build the report
$report = [System.Collections.Generic.List[object]]::new()
$totalBroken = 0
$totalRefs = 0

foreach ($dll in $modDlls) {
    Write-Log ""
    Write-Log "Scanning $($dll.Name)..." White
    try {
        $module = [Mono.Cecil.ModuleDefinition]::ReadModule($dll.FullName)
    } catch {
        Write-Log "  ERROR: cannot open $($dll.Name): $_" Red
        continue
    }

    try {
        # 3a. Missing type references
        foreach ($typeRef in $module.GetTypeReferences()) {
            $scope = $typeRef.Scope
            if ($null -eq $scope) { continue }
            $scopeName = $scope.Name
            if (-not ($scopeName.StartsWith('TaleWorlds.') -or $scopeName.StartsWith('SandBox') -or $scopeName.StartsWith('StoryMode'))) { continue }
            $totalRefs++

            if (-not $apiTypes.ContainsKey($typeRef.FullName)) {
                # Type doesn't exist in current API
                $report.Add([PSCustomObject]@{
                    Dll        = $dll.Name
                    Kind       = 'MISSING_TYPE'
                    Reference  = $typeRef.FullName
                    Suggestion = (Find-ClosestType -Name $typeRef.Name -ApiTypes $apiTypes -Max $MaxSuggestions)
                })
                $totalBroken++
            }
        }

        # 3b. Missing member references (methods + fields)
        foreach ($memberRef in $module.GetMemberReferences()) {
            $declType = $memberRef.DeclaringType
            if ($null -eq $declType) { continue }
            $declFull = $declType.FullName
            if (-not ($declFull.StartsWith('TaleWorlds.') -or $declFull.StartsWith('SandBox') -or $declFull.StartsWith('StoryMode'))) { continue }
            $totalRefs++

            $apiTypeDef = $apiTypes[$declFull]
            if ($null -eq $apiTypeDef) {
                # Already reported as MISSING_TYPE
                continue
            }

            # Match by name + (for methods) parameter count + return type.
            # Both sides are normalized through Normalize-Sig so generic
            # parameter placeholders compare cleanly (!!0 / T / TSource all
            # collapse to <T>).
            $isMethod = $memberRef -is [Mono.Cecil.MethodReference]
            if ($isMethod) {
                $refParamSigs = ($memberRef.Parameters | ForEach-Object { $_.ParameterType.FullName }) -join ', '
                $refSig = Normalize-Sig "$($memberRef.ReturnType.FullName) $($memberRef.Name)($refParamSigs)"
                $refParamCount = @($memberRef.Parameters).Count

                $matchingByName = @($apiTypeDef.Methods | Where-Object { $_.Name -eq $memberRef.Name })
                if ($matchingByName.Count -eq 0) {
                    $report.Add([PSCustomObject]@{
                        Dll        = $dll.Name
                        Kind       = 'MISSING_METHOD'
                        Reference  = "$declFull.$($memberRef.Name)($refParamSigs)"
                        Suggestion = "(no method named '$($memberRef.Name)' on $declFull)"
                    })
                    $totalBroken++
                    continue
                }

                # Exact-signature match check (normalized for generics).
                # Fall-through softening: if the API has a method with the
                # same name AND same parameter arity AND at least one side
                # involves a generic param, treat that as a match. Generic
                # bound vs unbound is a Cecil-representation artifact, not
                # a real signature mismatch.
                $exactMatch = $false
                foreach ($apiMethod in $matchingByName) {
                    $apiParamSigs = ($apiMethod.Parameters | ForEach-Object { $_.ParameterType.FullName }) -join ', '
                    $apiSig = Normalize-Sig "$($apiMethod.ReturnType.FullName) $($apiMethod.Name)($apiParamSigs)"
                    if ($apiSig -eq $refSig) { $exactMatch = $true; break }

                    # Soft arity-and-generic match
                    $apiParamCount = @($apiMethod.Parameters).Count
                    $apiHasGenerics = ($apiMethod.HasGenericParameters) -or ($apiParamSigs -match 'T\b|!!|!\d')
                    $refHasGenerics = ($refParamSigs -match '!!|!\d')
                    if ($apiParamCount -eq $refParamCount -and ($apiHasGenerics -or $refHasGenerics)) {
                        $exactMatch = $true; break
                    }
                }
                if (-not $exactMatch) {
                    # Show all overloads as suggestions
                    $sugs = $matchingByName | ForEach-Object {
                        $aps = ($_.Parameters | ForEach-Object { $_.ParameterType.Name }) -join ', '
                        "$($_.ReturnType.Name) $($_.Name)($aps)"
                    }
                    $report.Add([PSCustomObject]@{
                        Dll        = $dll.Name
                        Kind       = 'METHOD_SIGNATURE_CHANGED'
                        Reference  = "$declFull.$($memberRef.Name)($refParamSigs)"
                        Suggestion = ($sugs -join '  |  ')
                    })
                    $totalBroken++
                }
            }
            else {
                # FieldReference
                $matchingField = $apiTypeDef.Fields | Where-Object { $_.Name -eq $memberRef.Name } | Select-Object -First 1
                if ($null -eq $matchingField) {
                    # Maybe it became a property
                    $matchingProp = $apiTypeDef.Properties | Where-Object { $_.Name -eq $memberRef.Name } | Select-Object -First 1
                    if ($matchingProp) {
                        $report.Add([PSCustomObject]@{
                            Dll        = $dll.Name
                            Kind       = 'FIELD_NOW_PROPERTY'
                            Reference  = "$declFull.$($memberRef.Name)"
                            Suggestion = "Now a property: $($matchingProp.PropertyType.Name) $($matchingProp.Name) { get; set; } -- use get_$($memberRef.Name)() / set_$($memberRef.Name)()"
                        })
                    }
                    else {
                        $similar = $apiTypeDef.Fields | Where-Object { $_.Name -like "*$($memberRef.Name)*" } | Select-Object -First 3
                        $report.Add([PSCustomObject]@{
                            Dll        = $dll.Name
                            Kind       = 'MISSING_FIELD'
                            Reference  = "$declFull.$($memberRef.Name)"
                            Suggestion = if ($similar) { ($similar | ForEach-Object { $_.Name }) -join ', ' } else { "(no similarly-named field)" }
                        })
                    }
                    $totalBroken++
                }
            }
        }
    }
    finally {
        try { $module.Dispose() } catch {}
    }
}

# ============================================================
# 4. Output
# ============================================================
Write-Log ""
Write-Log "==== Summary ====" Cyan
Write-Log "Mod:                 $modName"
Write-Log "DLLs scanned:        $($modDlls.Count)"
Write-Log "TaleWorlds refs:     $totalRefs"
Write-Log "Broken refs:         $totalBroken" $(if ($totalBroken -eq 0) { 'Green' } else { 'Red' })

$markdown = Format-MarkdownReport -mod $modName -report $report -totalRefs $totalRefs -totalBroken $totalBroken -dlls $modDlls -bannerlordPath $BannerlordPath

if ($Output) {
    $outDir = Split-Path -Parent $Output
    if ($outDir -and -not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }
    Set-Content -Path $Output -Value $markdown -Encoding UTF8
    Write-Log ""
    Write-Log "Report written to $Output" Green
}
else {
    Write-Host ""
    Write-Host $markdown
}

# Always also drop a JSON sidecar next to the markdown (for tooling).
if ($Output) {
    $jsonPath = [IO.Path]::ChangeExtension($Output, '.json')
    $json = @{
        mod = $modName
        timestamp = (Get-Date -Format 'yyyy-MM-ddTHH:mm:ssK')
        dlls = ($modDlls | ForEach-Object { $_.Name })
        total_references = $totalRefs
        total_broken = $totalBroken
        breakage = $report
    } | ConvertTo-Json -Depth 6
    Set-Content -Path $jsonPath -Value $json -Encoding UTF8
    Write-Log "JSON sidecar: $jsonPath" DarkGray
}

if ($totalBroken -gt 0) { exit 1 } else { exit 0 }
