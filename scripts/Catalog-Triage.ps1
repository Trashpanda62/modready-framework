# BetaDeps -- Catalog-Triage
#
# Reads Modules\BetaDeps\failed-mods-catalog.txt and prints a sorted
# leaderboard of which mods SaveShield has caught failures from. Drives
# the v1.0 wide-fleet bug bash:
#
#   - Mods near the top of the leaderboard are the ones to investigate
#     first (most distinct failure modes seen across sessions).
#   - Each leaderboard row shows the count of distinct (exception type,
#     owner method) tuples, the date of the most-recent hit, and a
#     bulleted list of every exception kind seen.
#
# Cumulative across all sessions BetaDeps has run on this machine -- the
# catalog file is append-only and survives game restarts.
#
# Usage:
#   .\scripts\Catalog-Triage.ps1
#   .\scripts\Catalog-Triage.ps1 -Path "C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\BetaDeps\failed-mods-catalog.txt"
#   .\scripts\Catalog-Triage.ps1 -Top 5     # only show top 5 mods
#   .\scripts\Catalog-Triage.ps1 -AsCsv     # CSV output instead of pretty table

[CmdletBinding()]
param(
    [string]$Path,
    [int]$Top = 0,
    [switch]$AsCsv
)

$ErrorActionPreference = 'Continue'

if (-not $Path) {
    # Default to the live Bannerlord install's BetaDeps folder.
    $candidates = @(
        'C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\BetaDeps\failed-mods-catalog.txt',
        'C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\BetaDeps\failed-mods-catalog.txt',
        'D:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\BetaDeps\failed-mods-catalog.txt',
        'E:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\BetaDeps\failed-mods-catalog.txt'
    )
    foreach ($c in $candidates) {
        if (Test-Path $c) { $Path = $c; break }
    }
}

if (-not $Path -or -not (Test-Path $Path)) {
    Write-Host "Catalog file not found." -ForegroundColor Yellow
    Write-Host "Pass -Path explicitly, or run after BetaDeps has caught at least one failure." -ForegroundColor DarkGray
    exit 1
}

Write-Host "BetaDeps failed-mods catalog: $Path" -ForegroundColor Cyan
Write-Host ""

# Catalog format (one line per (mod, exception, owner) triple per session):
#   2026-05-25 01:29:30 | Reinforcement_System | MISSION-INIT | System.MissingMethodException | TaleWorlds.MountAndBlade.Mission.SetMissionMode | Method not found...
# Header lines start with `#` and are skipped.

$entries = @()
foreach ($line in Get-Content -Path $Path) {
    if ($line -match '^\s*#') { continue }
    if ($line -match '^\s*$') { continue }
    $parts = $line -split '\|'
    if ($parts.Count -lt 5) { continue }
    $entries += [PSCustomObject]@{
        When      = $parts[0].Trim()
        Mod       = $parts[1].Trim()
        Category  = $parts[2].Trim()
        Exception = $parts[3].Trim()
        Owner     = $parts[4].Trim()
        Message   = if ($parts.Count -gt 5) { $parts[5].Trim() } else { '' }
    }
}

if ($entries.Count -eq 0) {
    Write-Host "Catalog is empty (no failures recorded yet)." -ForegroundColor Green
    exit 0
}

# Group by mod
$leaderboard = $entries |
    Group-Object Mod |
    ForEach-Object {
        $modEntries = $_.Group
        $distinctSignatures = ($modEntries | Group-Object { "$($_.Exception)::$($_.Owner)" } | Measure-Object).Count
        $latestDate = ($modEntries | Sort-Object When -Descending | Select-Object -First 1).When
        $exceptions = ($modEntries | Group-Object Exception | ForEach-Object { "$($_.Name) (x$($_.Count))" })
        [PSCustomObject]@{
            Mod                 = $_.Name
            TotalHits           = $modEntries.Count
            DistinctSignatures  = $distinctSignatures
            LatestDate          = $latestDate
            Exceptions          = ($exceptions -join '; ')
        }
    } |
    Sort-Object DistinctSignatures -Descending, TotalHits -Descending

if ($Top -gt 0) {
    $leaderboard = $leaderboard | Select-Object -First $Top
}

if ($AsCsv) {
    $leaderboard | Export-Csv -Path "$PSScriptRoot\..\dist\catalog-triage-$(Get-Date -Format yyyyMMdd-HHmmss).csv" -NoTypeInformation
    Write-Host "CSV exported to dist\catalog-triage-*.csv" -ForegroundColor Green
    exit 0
}

# Pretty-print as a table.
Write-Host ("{0,-32}  {1,5}  {2,5}  {3,-20}  {4}" -f "MOD", "DIST", "TOTAL", "LATEST", "EXCEPTIONS") -ForegroundColor Cyan
Write-Host ("{0,-32}  {1,5}  {2,5}  {3,-20}  {4}" -f ("-" * 32), "----", "----", ("-" * 20), ("-" * 50)) -ForegroundColor DarkGray
foreach ($row in $leaderboard) {
    $modName = if ($row.Mod.Length -gt 32) { $row.Mod.Substring(0, 29) + "..." } else { $row.Mod }
    $color = if ($row.DistinctSignatures -ge 5) { "Red" }
             elseif ($row.DistinctSignatures -ge 2) { "Yellow" }
             else { "White" }
    Write-Host ("{0,-32}  {1,5}  {2,5}  {3,-20}  {4}" -f $modName, $row.DistinctSignatures, $row.TotalHits, $row.LatestDate, $row.Exceptions) -ForegroundColor $color
}

Write-Host ""
Write-Host "Total entries: $($entries.Count) across $($leaderboard.Count) mod(s)." -ForegroundColor DarkGray
Write-Host "DIST  = distinct (exception, owner-method) tuples seen for that mod" -ForegroundColor DarkGray
Write-Host "TOTAL = total entries (one per session per tuple)" -ForegroundColor DarkGray
Write-Host ""
Write-Host "Mods with high DIST are the priority targets for the wide-fleet bug bash." -ForegroundColor DarkGray
