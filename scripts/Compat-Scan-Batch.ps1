# BetaDeps -- Compat-Scan-Batch
#
# Wrapper around Compat-Scan.ps1 that scans many mods in one pass and
# writes a single SUMMARY report ranking them clean -> broken.
#
# Designed for "I just updated 15 mods overnight, which ones are going
# to break?" workflows. Outputs land under dist\compat-reports\.
#
# Usage:
#   .\scripts\Compat-Scan-Batch.ps1                                   # scan all mods modified in the last 24h
#   .\scripts\Compat-Scan-Batch.ps1 -SinceHours 72                    # last 3 days
#   .\scripts\Compat-Scan-Batch.ps1 -Mods 'AIKickNBash','BloodGames'  # explicit list
#   .\scripts\Compat-Scan-Batch.ps1 -All                              # scan every third-party mod

[CmdletBinding()]
param(
    [string]$BannerlordPath = 'C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord',
    [int]$SinceHours = 24,
    [string[]]$Mods,
    [switch]$All,
    [string]$OutputDir
)

$ErrorActionPreference = 'Stop'

$RepoRoot   = Split-Path -Parent $PSScriptRoot
$CompatScan = Join-Path $RepoRoot 'scripts\Compat-Scan.ps1'
if (-not (Test-Path $CompatScan)) { throw "Compat-Scan.ps1 not found at $CompatScan" }

if (-not $OutputDir) { $OutputDir = Join-Path $RepoRoot 'dist\compat-reports' }
if (Test-Path $OutputDir) { Remove-Item -Recurse -Force $OutputDir }
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

$ModulesRoot = Join-Path $BannerlordPath 'Modules'
if (-not (Test-Path $ModulesRoot)) { throw "Modules folder not found at $ModulesRoot" }

# Vanilla TaleWorlds modules + BetaDeps's own — never scan these.
$skipMods = @(
    'Native','SandBox','SandBoxCore','StoryMode','CustomBattle','Multiplayer',
    'NavalDLC','BirthAndDeath',
    'BetaDeps','BetaDeps.TacticsEditor','Bannerwar',
    'Bannerlord.Harmony','Bannerlord.UIExtenderEx','Bannerlord.ButterLib','Bannerlord.MBOptionScreen'
)

# Build the target list.
if ($Mods) {
    $targets = $Mods
    Write-Host "Target: explicit list of $($targets.Count) mod(s)" -ForegroundColor Cyan
}
elseif ($All) {
    $targets = Get-ChildItem -Path $ModulesRoot -Directory |
        Where-Object { $skipMods -notcontains $_.Name } |
        Select-Object -ExpandProperty Name
    Write-Host "Target: all $($targets.Count) third-party mod(s)" -ForegroundColor Cyan
}
else {
    $cutoff = (Get-Date).AddHours(-$SinceHours)
    $targets = Get-ChildItem -Path $ModulesRoot -Directory |
        Where-Object { $skipMods -notcontains $_.Name -and $_.LastWriteTime -gt $cutoff } |
        Select-Object -ExpandProperty Name
    Write-Host "Target: $($targets.Count) mod(s) modified in last ${SinceHours}h" -ForegroundColor Cyan
}

if ($targets.Count -eq 0) {
    Write-Host "No mods matched the criteria. Nothing to scan." -ForegroundColor Yellow
    exit 0
}

# Run Compat-Scan against each target, capture summary metrics.
$results = [System.Collections.Generic.List[object]]::new()
$i = 0
foreach ($mod in $targets) {
    $i++
    $modPath = Join-Path $ModulesRoot $mod
    $reportPath = Join-Path $OutputDir "$mod.md"
    Write-Host ""
    Write-Host "[$i/$($targets.Count)] $mod" -ForegroundColor White
    if (-not (Test-Path $modPath)) {
        Write-Host "  SKIP: folder not found" -ForegroundColor DarkYellow
        continue
    }

    # Run Compat-Scan with -Quiet -Output so the per-mod md + json sidecar land in OutputDir.
    & $CompatScan -ModPath $modPath -BannerlordPath $BannerlordPath -Output $reportPath -Quiet
    $exit = $LASTEXITCODE

    # Read the JSON sidecar for headline numbers.
    $jsonPath = [IO.Path]::ChangeExtension($reportPath, '.json')
    $totalRefs = 0
    $totalBroken = 0
    $dllCount = 0
    if (Test-Path $jsonPath) {
        try {
            $j = Get-Content $jsonPath -Raw | ConvertFrom-Json
            $totalRefs = [int]$j.total_references
            $totalBroken = [int]$j.total_broken
            $dllCount = @($j.dlls).Count
        } catch {
            Write-Host "  WARN: couldn't parse $jsonPath" -ForegroundColor DarkYellow
        }
    }

    $status =
        if ($totalRefs -eq 0 -and $dllCount -eq 0) { 'NO-DLLS' }
        elseif ($totalBroken -eq 0)                { 'CLEAN' }
        elseif ($totalBroken -le 3)                { 'MINOR' }
        elseif ($totalBroken -le 20)               { 'MAJOR' }
        else                                       { 'SEVERE' }

    $results.Add([PSCustomObject]@{
        Mod      = $mod
        Status   = $status
        Broken   = $totalBroken
        Total    = $totalRefs
        Dlls     = $dllCount
        Report   = "$mod.md"
    })

    $color = switch ($status) {
        'CLEAN'   { 'Green' }
        'MINOR'   { 'Yellow' }
        'MAJOR'   { 'Red' }
        'SEVERE'  { 'Magenta' }
        default   { 'DarkGray' }
    }
    Write-Host ("  -> {0}: {1}/{2} refs broken, {3} DLL(s)" -f $status, $totalBroken, $totalRefs, $dllCount) -ForegroundColor $color
}

# Write SUMMARY.md
$summaryPath = Join-Path $OutputDir 'SUMMARY.md'
$sb = [System.Text.StringBuilder]::new()
[void]$sb.AppendLine("# Compat-Scan summary")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("Generated $(Get-Date -Format 'yyyy-MM-dd HH:mm')")
[void]$sb.AppendLine("Bannerlord: $BannerlordPath")
[void]$sb.AppendLine("Scanned: $($results.Count) mod(s)")
[void]$sb.AppendLine("")

$grouped = $results | Group-Object Status
$order = 'CLEAN','MINOR','MAJOR','SEVERE','NO-DLLS'
[void]$sb.AppendLine("## At a glance")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("| Status | Count | Meaning |")
[void]$sb.AppendLine("|---|---|---|")
foreach ($s in $order) {
    $g = $grouped | Where-Object Name -eq $s
    $count = if ($g) { $g.Count } else { 0 }
    $meaning = switch ($s) {
        'CLEAN'   { 'All TaleWorlds refs resolve. Safe to enable.' }
        'MINOR'   { '1-3 broken refs. Likely runs but may glitch at specific code paths.' }
        'MAJOR'   { '4-20 broken refs. Expect crashes on first use of affected features.' }
        'SEVERE'  { '20+ broken refs. Will likely fail at load or first mission.' }
        'NO-DLLS' { 'Content-only mod (XML / assets). Not code, nothing to break.' }
    }
    [void]$sb.AppendLine("| **$s** | $count | $meaning |")
}
[void]$sb.AppendLine("")

[void]$sb.AppendLine("## Per-mod breakdown")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("Sorted: clean first (safe to enable), then by broken-ref count ascending.")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("| Mod | Status | Broken | Total refs | DLLs | Report |")
[void]$sb.AppendLine("|---|---|---:|---:|---:|---|")

# Custom sort: CLEAN/NO-DLLS first, then by Broken asc.
$statusRank = @{ 'CLEAN' = 0; 'NO-DLLS' = 1; 'MINOR' = 2; 'MAJOR' = 3; 'SEVERE' = 4 }
$sorted = $results | Sort-Object @{Expression={$statusRank[$_.Status]}}, Broken
foreach ($r in $sorted) {
    [void]$sb.AppendLine("| $($r.Mod) | $($r.Status) | $($r.Broken) | $($r.Total) | $($r.Dlls) | [$($r.Report)]($($r.Report)) |")
}
[void]$sb.AppendLine("")

# Action plan
[void]$sb.AppendLine("## Action plan")
[void]$sb.AppendLine("")
$clean = ($sorted | Where-Object { $_.Status -in 'CLEAN','NO-DLLS','MINOR' })
$broken = ($sorted | Where-Object { $_.Status -in 'MAJOR','SEVERE' })
if ($clean.Count -gt 0) {
    [void]$sb.AppendLine("**Enable now (clean or minor):**")
    foreach ($c in $clean) { [void]$sb.AppendLine("- $($c.Mod)") }
    [void]$sb.AppendLine("")
}
if ($broken.Count -gt 0) {
    [void]$sb.AppendLine("**Hold back (will likely break):**")
    foreach ($b in $broken) { [void]$sb.AppendLine("- $($b.Mod) -- $($b.Broken) broken refs; see $($b.Report) for the patch list to send the author") }
    [void]$sb.AppendLine("")
}

Set-Content -Path $summaryPath -Value $sb.ToString() -Encoding UTF8

Write-Host ""
Write-Host "==== Done ====" -ForegroundColor Cyan
Write-Host "Per-mod reports: $OutputDir\*.md" -ForegroundColor White
Write-Host "Summary:         $summaryPath" -ForegroundColor White
Write-Host ""
Write-Host "Breakdown:" -ForegroundColor Yellow
foreach ($s in $order) {
    $g = $grouped | Where-Object Name -eq $s
    if ($g) {
        $color = switch ($s) {
            'CLEAN'   { 'Green' }
            'MINOR'   { 'Yellow' }
            'MAJOR'   { 'Red' }
            'SEVERE'  { 'Magenta' }
            default   { 'DarkGray' }
        }
        Write-Host ("  {0,-8} {1,3} mod(s)" -f $s, $g.Count) -ForegroundColor $color
    }
}
