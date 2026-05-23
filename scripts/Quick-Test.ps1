# BetaDeps -- Quick Test loop
#
# Lightweight diagnose-fix-measure cycle for the post-load era. Where
# Ralph-Loop is for "why won't BetaDeps load?" bisection (snapshot, apply
# named hypothesis, capture full diag report, revert), Quick-Test is for
# "I changed code, did it work?" iteration.
#
# Each invocation:
#   1. Kills any leftover Bannerlord / launcher processes.
#   2. Builds + deploys via Build-Phase1.ps1.
#   3. Launches BLSE LauncherEx (you click PLAY, then Options -> Mod Config
#      -> Run Self-Test in-game).
#   4. Waits for runtime.log to appear and stop growing.
#   5. Copies the final runtime.log to C:\dev\bannerlord\runtime.log so
#      Claude can read it directly.
#
# Usage:
#   cd C:\dev\beta-deps
#   .\scripts\Quick-Test.ps1

[CmdletBinding()]
param(
    [int]$LaunchTimeoutSec = 120,
    [int]$StableForSec     = 10,
    [int]$HardCapSec       = 180
)

$ErrorActionPreference = "Continue"

$bl       = "C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord"
$repo     = "C:\dev\beta-deps"
$log      = "$bl\Modules\BetaDeps\runtime.log"
$selftest = "$bl\Modules\BetaDeps\selftest.log"
$shared   = "C:\dev\bannerlord\runtime.log"
$sharedST = "C:\dev\bannerlord\selftest.log"

function Banner($msg) { Write-Host "`n========== $msg ==========" -ForegroundColor Cyan }

# --- 1. Kill leftovers ---
$leftovers = Get-Process -Name "Bannerlord","Bannerlord.BLSE.LauncherEx","Bannerlord.BLSE.Standalone","Bannerlord.BLSE.Launcher","TaleWorlds.MountAndBlade.Launcher" -ErrorAction SilentlyContinue
if ($leftovers) {
    Banner "Killing leftover Bannerlord processes"
    foreach ($p in $leftovers) {
        Write-Host "  killing $($p.ProcessName) (pid $($p.Id))"
        try { $p.Kill() } catch { Write-Host "    -- kill failed: $($_.Exception.Message)" }
    }
    Start-Sleep -Seconds 2
}

# --- 2. Build + deploy ---
Banner "Building"
Push-Location $repo
try {
    & "$repo\scripts\Build-Phase1.ps1"
} finally {
    Pop-Location
}
if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne $null) {
    Write-Host "BUILD FAILED -- aborting" -ForegroundColor Red
    exit 1
}

# --- 3. Archive old runtime.log + selftest.log so we know we're capturing this
#       run's output. WITHOUT this for selftest.log, Quick-Test's "wait for
#       selftest.log to appear" check fires immediately on the leftover file
#       from the previous session and copies the stale report. ---
if (Test-Path $log) {
    $ts = Get-Date -Format "yyyyMMdd-HHmmss"
    $archive = "$bl\Modules\BetaDeps\runtime.archive-$ts.log"
    Move-Item $log $archive -Force
    Write-Host "  archived previous runtime.log -> $archive"
}
# Prune old archives: keep only the 5 most recent so the BetaDeps folder
# doesn't balloon to 300+ MB after many test runs (especially noisy ones).
$archiveDir = "$bl\Modules\BetaDeps"
$keepCount  = 5
$old = Get-ChildItem -Path $archiveDir -Filter "runtime.archive-*.log" -ErrorAction SilentlyContinue |
       Sort-Object LastWriteTime -Descending |
       Select-Object -Skip $keepCount
if ($old) {
    $freedMb = [math]::Round(($old | Measure-Object Length -Sum).Sum / 1MB, 1)
    $old | Remove-Item -Force -ErrorAction SilentlyContinue
    Write-Host "  pruned $($old.Count) old archive(s), freed ${freedMb} MB (kept newest $keepCount)"
}
if (Test-Path $selftest) {
    Remove-Item $selftest -Force
    Write-Host "  cleared previous selftest.log (so wait loop only triggers on fresh test)"
}

# --- 4. Launch BLSE LauncherEx ---
Banner "Launching BLSE LauncherEx"
$blseExe     = "$bl\bin\Win64_Shipping_Client\Bannerlord.BLSE.LauncherEx.exe"
$blseWorkDir = "$bl\bin\Win64_Shipping_Client"

if (-not (Test-Path $blseExe)) {
    Write-Host "  ERROR: BLSE LauncherEx not found at $blseExe" -ForegroundColor Red
    exit 1
}

Write-Host "  launching: $blseExe"
Start-Process -FilePath $blseExe -WorkingDirectory $blseWorkDir | Out-Null
$launchedAt = Get-Date

Write-Host ""
Write-Host "  >>> Click PLAY in the BLSE launcher, then open" -ForegroundColor Green
Write-Host "  >>> Options -> Mod Config -> Run Self-Test." -ForegroundColor Green
Write-Host "  >>> When the button shows 'X/N mods OK', close the game." -ForegroundColor Green
Write-Host ""

# --- 5. Wait for runtime.log to appear ---
Banner "Waiting for runtime.log"
$logAppeared = $false
$deadline = (Get-Date).AddSeconds($LaunchTimeoutSec)
while ((Get-Date) -lt $deadline) {
    if (Test-Path $log) {
        $logAppeared = $true
        $appearedAt = Get-Date
        Write-Host ("  runtime.log appeared at +{0:N1}s" -f ($appearedAt - $launchedAt).TotalSeconds) -ForegroundColor Green
        break
    }
    Start-Sleep -Milliseconds 1500
}
if (-not $logAppeared) {
    Write-Host "  TIMEOUT -- runtime.log never appeared. Did you click PLAY?" -ForegroundColor Red
    exit 1
}

# --- 6. Wait for self-test report or stability ---
# Two ways out of this loop:
#   1. selftest.log appears (user clicked Run Self-Test) -- ideal, capture immediately
#   2. runtime.log stops growing for $StableForSec seconds AND user has had time
#      to click (hard floor of 30s so we don't bail before the menu is open)
# Hard cap at $HardCapSec keeps us from waiting forever.
Banner "Waiting for self-test (click Run Self-Test in-game when ready)"
$lastSize = (Get-Item $log).Length
$stableSince = Get-Date
$hardCap = (Get-Date).AddSeconds($HardCapSec)
$selftestSeenAt = $null
$minWait = (Get-Date).AddSeconds(30)  # don't bail in the first 30s no matter what
while ((Get-Date) -lt $hardCap) {
    Start-Sleep -Seconds 2
    # Self-test path: selftest.log written -> capture as soon as IT also stabilizes
    # (10s of no growth is enough; the file is small).
    if (Test-Path $selftest) {
        if ($null -eq $selftestSeenAt) {
            $selftestSeenAt = Get-Date
            Write-Host "  selftest.log appeared -- waiting 10s for it to flush" -ForegroundColor Green
        }
        if (((Get-Date) - $selftestSeenAt).TotalSeconds -ge 10) {
            Write-Host "  capturing now" -ForegroundColor Green
            break
        }
        continue
    }
    # Stability fallback for runs where the user only wants the runtime.log.
    $curSize = (Get-Item $log).Length
    if ($curSize -ne $lastSize) {
        $lastSize = $curSize
        $stableSince = Get-Date
    } elseif (((Get-Date) - $stableSince).TotalSeconds -ge $StableForSec -and (Get-Date) -gt $minWait) {
        Write-Host ("  log stable at {0} bytes; no selftest.log -- capturing anyway" -f $curSize) -ForegroundColor Yellow
        break
    }
}

# --- 7. Copy to shared location ---
Banner "Copying runtime.log + selftest.log -> C:\dev\bannerlord\"
try {
    Copy-Item $log $shared -Force
    $sz = (Get-Item $shared).Length
    Write-Host "  copied: $shared ($sz bytes)" -ForegroundColor Green
} catch {
    Write-Host "  runtime.log COPY FAILED: $($_.Exception.Message)" -ForegroundColor Red
}
# selftest.log only exists if the user clicked Run Self-Test this session.
# Missing file is normal for a baseline-only launch; warn but don't fail.
if (Test-Path $selftest) {
    try {
        Copy-Item $selftest $sharedST -Force
        $szST = (Get-Item $sharedST).Length
        Write-Host "  copied: $sharedST ($szST bytes)" -ForegroundColor Green
    } catch {
        Write-Host "  selftest.log COPY FAILED: $($_.Exception.Message)" -ForegroundColor Red
    }
} else {
    Write-Host "  (no selftest.log -- did you click Run Self-Test in-game?)" -ForegroundColor Yellow
}

Banner "Done"
Write-Host ""
Write-Host "Tell Claude the log is updated. Latest files:" -ForegroundColor Yellow
Write-Host "  $shared" -ForegroundColor Yellow
Write-Host "  $sharedST" -ForegroundColor Yellow
