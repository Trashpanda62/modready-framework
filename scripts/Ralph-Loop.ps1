# BetaDeps -- Ralph Loop
#
# Automated diagnose-fix-measure cycle. Each invocation:
#   1. Snapshots mutable source files (so we can revert cleanly).
#   2. Applies a named hypothesis (small reversible change).
#   3. Archives the current runtime.log.
#   4. Builds + deploys via Build-Phase1.ps1.
#   5. Launches the game via BLSE LauncherEx.
#   6. Polls for runtime.log creation up to -LaunchTimeoutSec.
#   7. Captures everything (runtime.log, rgl_log, crash dumps, BLSE logs,
#      DLL probe via [Reflection.Assembly]::LoadFrom) into a single
#      ralph-report-<hyp>-<ts>.txt file.
#   8. Restores the snapshot so the tree is clean for the next run.
#
# Hypotheses:
#   baseline    -- no change. Just measure current state.
#   no-shim     -- comment out AssemblyVersionShim.Install() calls in both
#                  AliasStubSubModule.cs and BetaDepsHarmonySubModule.cs.
#   minimal-xml -- replace BetaDeps SubModule.xml with the small version
#                  that was known-working before our load-order list.
#   no-mcm      -- remove the MCM + MCM.UI SubModule entries from XML
#                  (in case MCMv5.dll's class load is what's failing).
#
# Usage:
#   cd C:\dev\beta-deps
#   .\scripts\Ralph-Loop.ps1 -Hypothesis baseline
#   .\scripts\Ralph-Loop.ps1 -Hypothesis no-shim
#   .\scripts\Ralph-Loop.ps1 -Hypothesis minimal-xml
#   .\scripts\Ralph-Loop.ps1 -Hypothesis no-mcm

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("baseline","no-shim","minimal-xml","no-mcm")]
    [string]$Hypothesis,

    [int]$LaunchTimeoutSec = 60
)

$ErrorActionPreference = "Continue"

$bl    = "C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord"
$repo  = "C:\dev\beta-deps"
$docs  = "$env:USERPROFILE\OneDrive\Documents\Mount and Blade II Bannerlord"
$ts    = Get-Date -Format "yyyyMMdd-HHmmss"
$rpt   = Join-Path $repo "ralph-report-$Hypothesis-$ts.txt"
$snap  = Join-Path $repo ".ralph-snapshot"
$log   = "$bl\Modules\BetaDeps\runtime.log"

function Banner($msg) { Write-Host "`n========== $msg ==========" -ForegroundColor Cyan }
function Append($line) { Add-Content -Path $rpt -Value $line }
function AppendHdr($title) { Append "`n`n========== $title ==========" }

"Ralph Loop -- hypothesis: $Hypothesis -- $ts" | Set-Content $rpt

# Make sure no Bannerlord / launcher processes are still running from a prior
# cycle. Their DLL handles lock the deploy step (Copy-Item fails with
# "being used by another process"). Kill them aggressively before build.
$leftovers = Get-Process -Name "Bannerlord","Bannerlord.BLSE.LauncherEx","Bannerlord.BLSE.Standalone","Bannerlord.BLSE.Launcher","TaleWorlds.MountAndBlade.Launcher" -ErrorAction SilentlyContinue
if ($leftovers) {
    Banner "Killing leftover Bannerlord processes from previous cycle"
    foreach ($p in $leftovers) {
        Write-Host "  killing $($p.ProcessName) (pid $($p.Id))"
        try { $p.Kill() } catch { Write-Host "    -- kill failed: $($_.Exception.Message)" }
    }
    # Give Windows a beat to release file handles
    Start-Sleep -Seconds 2
}
Append "Repo: $repo"
Append "Bannerlord: $bl"

# ----- 1. Snapshot mutable files -----
Banner "Snapshotting source files"
if (Test-Path $snap) { Remove-Item $snap -Recurse -Force }
New-Item -ItemType Directory -Force -Path $snap | Out-Null

$mutable = @{
    "SubModule.xml"                     = "$repo\src\BetaDeps.Module\SubModule.xml"
    "AliasStubSubModule.cs"             = "$repo\src\BetaDeps.Foundation\AliasStubSubModule.cs"
    "BetaDepsHarmonySubModule.cs"       = "$repo\src\BetaDeps.Harmony\BetaDepsHarmonySubModule.cs"
}
foreach ($k in $mutable.Keys) {
    if (Test-Path $mutable[$k]) {
        Copy-Item $mutable[$k] (Join-Path $snap $k) -Force
        Write-Host "  snapshot: $k"
    }
}

# ----- 2. Apply hypothesis -----
Banner "Applying hypothesis: $Hypothesis"
switch ($Hypothesis) {
    "baseline" {
        Write-Host "  (no change)"
    }
    "no-shim" {
        foreach ($cs in $mutable["AliasStubSubModule.cs"], $mutable["BetaDepsHarmonySubModule.cs"]) {
            (Get-Content $cs) -replace 'AssemblyVersionShim\.Install\(\);', '/* ralph:no-shim */' |
                Set-Content $cs -Encoding UTF8
            Write-Host "  patched: $cs"
        }
    }
    "minimal-xml" {
        $minimal = @'
<?xml version="1.0" encoding="UTF-8"?>
<Module xmlns:xsi='http://www.w3.org/2001/XMLSchema-instance'
        xsi:noNamespaceSchemaLocation="https://raw.githubusercontent.com/BUTR/Bannerlord.XmlSchemas/master/SubModule.xsd">
  <Id value="BetaDeps" />
  <Name value="Bannerlord Beta Dependencies" />
  <Version value="v0.3.0" />
  <DefaultModule value="false" />
  <ModuleCategory value="Singleplayer" />
  <ModuleType value="Community" />
  <Url value="" />
  <DependedModules />
  <ModulesToLoadAfterThis>
    <Module Id="Native" />
    <Module Id="SandBoxCore" />
    <Module Id="Sandbox" />
    <Module Id="StoryMode" />
    <Module Id="CustomBattle" />
  </ModulesToLoadAfterThis>
  <DependedModuleMetadatas />
  <SubModules>
    <SubModule>
      <Name value="BetaDeps Harmony" />
      <DLLName value="BetaDeps.Harmony.dll" />
      <SubModuleClassType value="BetaDeps.Harmony.BetaDepsHarmonySubModule" />
      <Assemblies>
        <Assembly value="0Harmony.dll" />
        <Assembly value="Mono.Cecil.dll" />
        <Assembly value="Mono.Cecil.Mdb.dll" />
        <Assembly value="Mono.Cecil.Pdb.dll" />
        <Assembly value="Mono.Cecil.Rocks.dll" />
        <Assembly value="MonoMod.Backports.dll" />
        <Assembly value="MonoMod.Core.dll" />
        <Assembly value="MonoMod.Iced.dll" />
        <Assembly value="MonoMod.ILHelpers.dll" />
        <Assembly value="MonoMod.Utils.dll" />
        <Assembly value="BetaDeps.Foundation.dll" />
      </Assemblies>
      <Tags />
    </SubModule>
    <SubModule>
      <Name value="BetaDeps UIExtenderEx" />
      <DLLName value="Bannerlord.UIExtenderEx.dll" />
      <SubModuleClassType value="Bannerlord.UIExtenderEx.BetaDepsUIExtenderExSubModule" />
      <Tags />
    </SubModule>
    <SubModule>
      <Name value="BetaDeps ButterLib" />
      <DLLName value="Bannerlord.ButterLib.dll" />
      <SubModuleClassType value="Bannerlord.ButterLib.ButterLibSubModule" />
      <Assemblies>
        <Assembly value="Microsoft.Extensions.DependencyInjection.Abstractions.dll" />
        <Assembly value="Microsoft.Extensions.DependencyInjection.dll" />
        <Assembly value="Microsoft.Extensions.Logging.Abstractions.dll" />
        <Assembly value="Microsoft.Extensions.Logging.dll" />
        <Assembly value="Microsoft.Extensions.Options.dll" />
        <Assembly value="Microsoft.Extensions.Primitives.dll" />
        <Assembly value="System.Buffers.dll" />
        <Assembly value="System.Diagnostics.DiagnosticSource.dll" />
        <Assembly value="System.Memory.dll" />
        <Assembly value="System.Numerics.Vectors.dll" />
        <Assembly value="System.Runtime.CompilerServices.Unsafe.dll" />
        <Assembly value="System.Threading.Tasks.Extensions.dll" />
      </Assemblies>
      <Tags />
    </SubModule>
    <SubModule>
      <Name value="BetaDeps MCM" />
      <DLLName value="MCMv5.dll" />
      <SubModuleClassType value="MCM.MCMSubModule" />
      <Assemblies>
        <Assembly value="Newtonsoft.Json.dll" />
      </Assemblies>
      <Tags />
    </SubModule>
    <SubModule>
      <Name value="BetaDeps MCM UI" />
      <DLLName value="MCMv5.dll" />
      <SubModuleClassType value="MCM.UI.MCMUISubModule" />
      <Tags />
    </SubModule>
  </SubModules>
</Module>
'@
        Set-Content $mutable["SubModule.xml"] -Value $minimal -Encoding UTF8
        Write-Host "  replaced SubModule.xml with minimal version (no consumer-mod load-order list)"
    }
    "no-mcm" {
        $xmlPath = $mutable["SubModule.xml"]
        $content = Get-Content $xmlPath -Raw
        # Comment out the two MCM SubModule entries (BetaDeps MCM and BetaDeps MCM UI)
        $content = $content -replace '(?s)<SubModule>\s*<Name value="BetaDeps MCM" />.*?</SubModule>', '<!-- ralph:no-mcm removed BetaDeps MCM -->'
        $content = $content -replace '(?s)<SubModule>\s*<Name value="BetaDeps MCM UI" />.*?</SubModule>', '<!-- ralph:no-mcm removed BetaDeps MCM UI -->'
        Set-Content $xmlPath -Value $content -Encoding UTF8
        Write-Host "  removed MCM and MCM.UI SubModule entries from SubModule.xml"
    }
}

# ----- 3. Archive old runtime.log -----
Banner "Archiving old runtime.log"
if (Test-Path $log) {
    $prev = "$bl\Modules\BetaDeps\runtime.archive-$ts.log"
    Move-Item $log $prev -Force
    Write-Host "  moved -> $prev"
} else {
    Write-Host "  (no existing log to archive)"
}

# ----- 4. Build + deploy -----
Banner "Building"
$buildLog = "$rpt.build"
Push-Location $repo
try {
    & "$repo\scripts\Build-Phase1.ps1" 2>&1 | Tee-Object -FilePath $buildLog | Out-Null
} finally {
    Pop-Location
}
$buildOK = $LASTEXITCODE -eq 0 -or $LASTEXITCODE -eq $null
Write-Host "  build complete (success=$buildOK, log=$buildLog)"

# ----- 5. Probe deployed DLLs via reflection -----
Banner "Probing deployed DLLs via [Reflection.Assembly]::LoadFrom"
AppendHdr "Duplicate-DLL scan (find stale copies in alias / consumer-mod folders)"
foreach ($name in "Bannerlord.ButterLib.dll","Bannerlord.UIExtenderEx.dll","MCMv5.dll","0Harmony.dll","BetaDeps.Foundation.dll","Serilog.dll") {
    $copies = Get-ChildItem "$bl\Modules" -Recurse -File -Filter $name -ErrorAction SilentlyContinue
    Append ""
    Append "--- $name ($($copies.Count) cop$(if ($copies.Count -eq 1) {"y"} else {"ies"})) ---"
    foreach ($c in $copies) {
        Append ("  {0} bytes  {1}  {2}" -f $c.Length, $c.LastWriteTime, $c.FullName)
    }
}

AppendHdr "Consumer-mod reference probe (what versions of our libs is each compiled against)"
$probeTargets = @{
    "Bannerlord.FluidCombatLite.dll"            = "$bl\Modules\Bannerlord.FluidCombatLite\bin\Win64_Shipping_Client\Bannerlord.FluidCombatLite.dll"
    "Bandit Black Hole.dll"                     = "$bl\Modules\BanditBlackHole\bin\Win64_Shipping_Client\Bandit Black Hole.dll"
    "BetterSmithingContinued.Main.dll"          = "$bl\Modules\BetterSmithingContinued\bin\Win64_Shipping_Client\BetterSmithingContinued.Main.dll"
}
# Dump ALL references (no filter) so we can spot Microsoft.Extensions, mscorlib,
# TaleWorlds version mismatches as well as the libs we ship.
foreach ($name in $probeTargets.Keys) {
    $p = $probeTargets[$name]
    Append ""
    Append "--- $name ---"
    if (-not (Test-Path $p)) { Append "  (not installed)"; continue }
    try {
        $a = [Reflection.Assembly]::ReflectionOnlyLoadFrom($p)
        $refs = $a.GetReferencedAssemblies() | Sort-Object Name
        foreach ($ref in $refs) {
            $tok = if ($ref.GetPublicKeyToken() -and $ref.GetPublicKeyToken().Length -gt 0) { ($ref.GetPublicKeyToken() | ForEach-Object { $_.ToString("x2") }) -join "" } else { "null" }
            Append "  ref: $($ref.Name), Version=$($ref.Version), PublicKeyToken=$tok"
        }
    } catch {
        Append "  probe failed: $($_.Exception.Message)"
    }
}

AppendHdr "DLL Probe"
$dlls = @(
  "$bl\Modules\BetaDeps\bin\Win64_Shipping_Client\BetaDeps.Foundation.dll",
  "$bl\Modules\BetaDeps\bin\Win64_Shipping_Client\BetaDeps.Harmony.dll",
  "$bl\Modules\BetaDeps\bin\Win64_Shipping_Client\Bannerlord.UIExtenderEx.dll",
  "$bl\Modules\BetaDeps\bin\Win64_Shipping_Client\Bannerlord.ButterLib.dll",
  "$bl\Modules\BetaDeps\bin\Win64_Shipping_Client\MCMv5.dll"
)
foreach ($dll in $dlls) {
  Append ""
  Append "--- $(Split-Path -Leaf $dll) ---"
  if (-not (Test-Path $dll)) { Append "  MISSING ON DISK"; continue }
  $i = Get-Item $dll
  Append "  size: $($i.Length), mtime: $($i.LastWriteTime)"
  try {
    $asm = [Reflection.Assembly]::LoadFrom($dll)
    Append "  OK -- Name: $($asm.GetName().Name) v$($asm.GetName().Version), CLR: $($asm.ImageRuntimeVersion)"
    Append "  Types ($($asm.GetTypes().Count)):"
    foreach ($t in $asm.GetTypes()) { Append "    $($t.FullName)" }
  } catch [System.Reflection.ReflectionTypeLoadException] {
    Append "  ReflectionTypeLoadException -- types partially loaded:"
    foreach ($le in $_.Exception.LoaderExceptions) { Append "    loader-exc: $($le.GetType().Name): $($le.Message)" }
  } catch {
    Append "  LOAD-FAIL -- $($_.Exception.GetType().Name): $($_.Exception.Message)"
  }
}

# ----- 6. Launch game -----
Banner "Launching game (will wait up to $LaunchTimeoutSec sec for runtime.log to appear)"
$launchedAt = Get-Date

# CRITICAL: BetaDeps depends on BLSE preloading the <Assemblies> listed in
# our SubModule.xml. The official TaleWorlds.MountAndBlade.Launcher does NOT
# do this -- it just loads TaleWorlds modules. So we MUST launch BLSE
# LauncherEx specifically.
#
# BLSE LauncherEx's "Failed to find the necessary game files" error happens
# when the working directory isn't bin\Win64_Shipping_Client. Setting
# -WorkingDirectory fixes that, no Steam needed.

$blseExe = "$bl\bin\Win64_Shipping_Client\Bannerlord.BLSE.LauncherEx.exe"
$blseWorkDir = "$bl\bin\Win64_Shipping_Client"

# If the WRONG launcher is already running (official TaleWorlds one), kill it
# so we can replace it with BLSE LauncherEx. Otherwise BetaDeps never loads.
$wrong = Get-Process -Name "TaleWorlds.MountAndBlade.Launcher" -ErrorAction SilentlyContinue
if ($wrong) {
    Write-Host "  killing official TaleWorlds launcher so BLSE can take over" -ForegroundColor Yellow
    $wrong | Stop-Process -Force
    Start-Sleep -Seconds 2
}

# Detect right launcher / game already running
$blseProcs = "Bannerlord","Bannerlord.BLSE.LauncherEx","Bannerlord.BLSE.Standalone","Bannerlord.BLSE.Launcher"
$alreadyRunning = (Get-Process -Name $blseProcs -ErrorAction SilentlyContinue) -ne $null

if ($alreadyRunning) {
    Write-Host "  BLSE launcher or Bannerlord already running -- polling for runtime.log"
} elseif (Test-Path $blseExe) {
    Write-Host "  launching: $blseExe"
    Write-Host "  workdir:   $blseWorkDir"
    Start-Process -FilePath $blseExe -WorkingDirectory $blseWorkDir | Out-Null
    Start-Sleep -Seconds 8
    $procNow = Get-Process -Name $blseProcs -ErrorAction SilentlyContinue
    if ($procNow) {
        Write-Host "  detected BLSE process: $(($procNow | Select-Object -First 1).ProcessName)"
    } else {
        # Maybe BLSE flashed an error dialog and died. Check.
        $tw = Get-Process -Name "TaleWorlds.MountAndBlade.Launcher" -ErrorAction SilentlyContinue
        if ($tw) {
            Write-Host "  WARNING: BLSE launch fell through to TaleWorlds launcher -- BetaDeps will NOT load" -ForegroundColor Red
        } else {
            Write-Host "  warning: no launcher process detected -- BLSE may have errored silently" -ForegroundColor Yellow
        }
    }
} else {
    Write-Host "  ERROR: BLSE LauncherEx not found at $blseExe" -ForegroundColor Red
    Write-Host "  Install BLSE first (https://www.nexusmods.com/mountandblade2bannerlord/mods/5104)" -ForegroundColor Red
}

$logAppeared = $false
$deadline = (Get-Date).AddSeconds($LaunchTimeoutSec)
$bannerlordSeenAlive = $false
Write-Host "  CLICK PLAY in the BLSE launcher window to start the game. Polling..." -ForegroundColor Green

while ((Get-Date) -lt $deadline) {
    if (Test-Path $log) {
        $logAppeared = $true
        $appearedAt = Get-Date
        Write-Host ("  runtime.log appeared at +{0:N1}s" -f ($appearedAt - $launchedAt).TotalSeconds) -ForegroundColor Green
        break
    }
    # Just record whether Bannerlord.exe was ever seen alive (for the final message)
    $game = Get-Process -Name "Bannerlord" -ErrorAction SilentlyContinue
    if ($game -and -not $bannerlordSeenAlive) {
        $bannerlordSeenAlive = $true
        Write-Host "  Bannerlord.exe started" -ForegroundColor Cyan
    }
    Start-Sleep -Milliseconds 1500
}
if (-not $logAppeared) {
    Write-Host "  TIMEOUT -- runtime.log never appeared within window" -ForegroundColor Red
    if (-not $bannerlordSeenAlive) {
        Write-Host "  (Bannerlord.exe was never seen alive -- did you click PLAY?)" -ForegroundColor Yellow
    } else {
        Write-Host "  (Bannerlord.exe ran but BetaDeps never wrote a log line)" -ForegroundColor Yellow
    }
}

# After runtime.log first appears, keep watching it until either (a) its size
# stops growing for 10 consecutive seconds (game has finished init/discovery and
# is sitting at main menu), or (b) we hit a 60-second hard cap. This gives us
# the full MCM eager-load + settings discovery + main-menu reach phase in the
# captured log, not just the first 5 seconds of SubModule.OnSubModuleLoad.
if ($logAppeared) {
    $lastSize = (Get-Item $log).Length
    $stableSince = Get-Date
    $hardCap = (Get-Date).AddSeconds(60)
    Write-Host "  watching log for growth..." -ForegroundColor Cyan
    while ((Get-Date) -lt $hardCap) {
        Start-Sleep -Seconds 2
        $curSize = (Get-Item $log).Length
        if ($curSize -ne $lastSize) {
            $lastSize = $curSize
            $stableSince = Get-Date
        } elseif (((Get-Date) - $stableSince).TotalSeconds -ge 10) {
            Write-Host ("  log stable at {0} bytes for 10s -- capturing" -f $curSize) -ForegroundColor Cyan
            break
        }
    }
}

# ----- 7. Capture diagnostics -----
Banner "Capturing diagnostics"

AppendHdr "RESULT"
Append "logAppeared: $logAppeared"
if ($logAppeared) {
    $sz = (Get-Item $log).Length
    Append "runtime.log size: $sz bytes"
}

AppendHdr "runtime.log (full contents)"
if (Test-Path $log) {
    Get-Content $log | ForEach-Object { Append $_ }
} else {
    Append "(no runtime.log file)"
}

AppendHdr "Bannerlord rgl_log (engine log -- last 200 lines of newest file)"
$rgl = Get-ChildItem $docs -Recurse -Filter "rgl_log*.txt" -EA SilentlyContinue |
       Where-Object { $_.LastWriteTime -ge $launchedAt.AddSeconds(-30) } |
       Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($rgl) {
    Append "file: $($rgl.FullName)  ($($rgl.LastWriteTime), $($rgl.Length) bytes)"
    Get-Content $rgl.FullName -Tail 200 | ForEach-Object { Append $_ }
} else {
    Append "(no recent rgl_log found)"
}

AppendHdr "Crash report search (BUTR / BLSE / CrashDoctor / native)"
# BUTR.CrashReport writes HTML files; BLSE writes zips; CrashDoctor writes
# YAML; the native engine writes .mdmp. They scatter across various paths.
$crashSearchRoots = @(
    "$docs",
    "$docs\Crashes",
    "$docs\CrashDumps",
    "$docs\CrashReports",
    "$bl\Modules\Bannerlord.LauncherEx",
    "$bl\Modules\BetterExceptionWindow",
    "$bl\Modules\CrashDoctor",
    "$env:LOCALAPPDATA\Mount and Blade II Bannerlord",
    "$env:TEMP"
)
$crashPatterns = @("crash_report*","crash-report*","*crash*.html","*crash*.htm","*crash*.zip","*crash*.json","*crash*.yaml","*crash*.yml","*crash*.txt","*.mdmp")
$crashCandidates = New-Object System.Collections.Generic.List[System.IO.FileInfo]
foreach ($root in $crashSearchRoots) {
    if (-not (Test-Path $root)) { continue }
    foreach ($pat in $crashPatterns) {
        Get-ChildItem -Path $root -Recurse -File -Filter $pat -ErrorAction SilentlyContinue |
            Where-Object { $_.LastWriteTime -gt $launchedAt.AddMinutes(-2) -and $_.Length -gt 100 -and $_.Name -notmatch "^engine_config|^BannerlordConfig|^BannerlordGameKeys|^Launcher" } |
            ForEach-Object { $crashCandidates.Add($_) }
    }
}
# Dedupe by FullName
$unique = $crashCandidates | Sort-Object FullName -Unique | Sort-Object LastWriteTime -Descending
Append "found $($unique.Count) candidate file(s)"
foreach ($f in $unique) { Append "  $($f.FullName)  ($($f.LastWriteTime), $($f.Length) bytes)" }

# Dump the newest one's content (parsed if HTML)
$newest = $unique | Select-Object -First 1
if ($newest) {
    Append ""
    Append "----- Newest crash artifact: $($newest.FullName) -----"
    try {
        if ($newest.Extension -in @(".html",".htm")) {
            $raw = Get-Content $newest.FullName -Raw
            $clean = $raw -replace "(?s)<script[^>]*>.*?</script>","" -replace "(?s)<style[^>]*>.*?</style>","" -replace "<[^>]+>","`n" -replace "&nbsp;"," " -replace "&amp;","&" -replace "&lt;","<" -replace "&gt;",">" -replace '&quot;','"' -replace "&#39;","'"
            $lines = $clean -split "`n" | Where-Object { $_.Trim() -ne "" } | Select-Object -First 600
            foreach ($l in $lines) { Append $l.Trim() }
        } elseif ($newest.Extension -eq ".zip") {
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            $tmp = Join-Path $env:TEMP ("ralph-crashzip-" + [Guid]::NewGuid().ToString("N"))
            New-Item -ItemType Directory -Force -Path $tmp | Out-Null
            try {
                [System.IO.Compression.ZipFile]::ExtractToDirectory($newest.FullName, $tmp)
                $inner = Get-ChildItem $tmp -Recurse -File | Sort-Object Length -Descending | Select-Object -First 3
                foreach ($i in $inner) {
                    Append "--- inside zip: $($i.Name) ($($i.Length) bytes) ---"
                    if ($i.Extension -in @(".html",".htm")) {
                        $raw = Get-Content $i.FullName -Raw
                        $clean = $raw -replace "(?s)<script[^>]*>.*?</script>","" -replace "(?s)<style[^>]*>.*?</style>","" -replace "<[^>]+>","`n" -replace "&nbsp;"," " -replace "&amp;","&" -replace "&lt;","<" -replace "&gt;",">" -replace '&quot;','"' -replace "&#39;","'"
                        $lines = $clean -split "`n" | Where-Object { $_.Trim() -ne "" } | Select-Object -First 400
                        foreach ($l in $lines) { Append $l.Trim() }
                    } else {
                        Get-Content $i.FullName -TotalCount 200 | ForEach-Object { Append $_ }
                    }
                }
            } finally {
                Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
            }
        } else {
            Get-Content $newest.FullName -TotalCount 400 | ForEach-Object { Append $_ }
        }
    } catch {
        Append "  (failed to read: $($_.Exception.Message))"
    }
}

AppendHdr "LauncherData.xml (Mods section snippet)"
$ldx = "$docs\Configs\LauncherData.xml"
if (Test-Path $ldx) {
    $picked = Select-String -Path $ldx -Pattern "BetaDeps|Bannerlord\.Harmony|Bannerlord\.ButterLib|Bannerlord\.UIExtenderEx|Bannerlord\.MBOptionScreen" -Context 0,2
    $picked | ForEach-Object { Append $_.Line; if ($_.Context.PostContext) { $_.Context.PostContext | ForEach-Object { Append "    $_" } } }
}

AppendHdr "Build log"
if (Test-Path $buildLog) { Get-Content $buildLog | ForEach-Object { Append $_ } }

# ----- 7b. Mirror runtime.log + selftest.log to C:\dev\bannerlord\ -----
# Claude has read access to C:\dev\bannerlord\ via the cowork mount. Copying
# both files here means we don't need a separate manual Copy-Item step after
# every cycle. selftest.log only exists if the user clicked Run Self-Test
# during the run; absence is normal for baseline / non-MCM hypotheses.
if (Test-Path $log) {
    $shared = "C:\dev\bannerlord\runtime.log"
    try {
        Copy-Item $log $shared -Force
        Write-Host "  copied runtime.log -> $shared" -ForegroundColor Green
    } catch {
        Write-Host "  runtime.log copy-to-shared failed: $($_.Exception.Message)" -ForegroundColor Yellow
    }
}
$selftest = "$bl\Modules\BetaDeps\selftest.log"
if (Test-Path $selftest) {
    $sharedST = "C:\dev\bannerlord\selftest.log"
    try {
        Copy-Item $selftest $sharedST -Force
        Write-Host "  copied selftest.log -> $sharedST" -ForegroundColor Green
    } catch {
        Write-Host "  selftest.log copy-to-shared failed: $($_.Exception.Message)" -ForegroundColor Yellow
    }
}

# ----- 8. Revert hypothesis -----
Banner "Reverting hypothesis (restoring snapshot)"
foreach ($k in $mutable.Keys) {
    $srcSnap = Join-Path $snap $k
    if (Test-Path $srcSnap) {
        Copy-Item $srcSnap $mutable[$k] -Force
        Write-Host "  restored: $k"
    }
}

Banner "Done -- report at: $rpt"
Write-Host ""
Write-Host "RESULT: logAppeared = $logAppeared" -ForegroundColor $(if ($logAppeared) { "Green" } else { "Red" })
Write-Host ""
Write-Host "Paste the report file to Claude:" -ForegroundColor Yellow
Write-Host "  Get-Content '$rpt'" -ForegroundColor Yellow
