<#
  Build-Installer.ps1 — assembles the BetaDeps All-in-One installer.

  What it does:
    1. Stages the five clean BetaDeps modules from dist\Modules into payload\Modules.
    2. Stages BLSE's bin\Win64_Shipping_Client into payload\BLSE.
    3. Gathers license notices (BLSE MIT + BetaDeps third-party) into payload\LICENSES.
    4. Compiles BetaDeps-AllInOne.iss with Inno Setup (ISCC.exe) -> dist\BetaDeps-AllInOne-v<ver>.exe

  Prerequisites (one-time):
    - Inno Setup 6 installed (https://jrsoftware.org/isdl.php) — provides ISCC.exe.
    - A copy of BLSE extracted somewhere, passed via -BlseDir. The folder you pass
      must contain  bin\Win64_Shipping_Client\Bannerlord.BLSE.LauncherEx.exe
      (download "Manual" BLSE from Nexus mod 1 or BUTR/Bannerlord.BLSE releases and
      unzip it; point -BlseDir at the unzipped root).
    - dist\Modules must exist (run scripts\Build-Phase1.ps1 first if it doesn't).

  Usage:
    cd C:\dev\beta-deps
    .\installer\Build-Installer.ps1 -BlseDir "C:\path\to\unzipped-BLSE" -Version 0.9.0
#>

param(
  [Parameter(Mandatory = $true)] [string] $BlseDir,
  [string] $Version = '0.9.0'
)

$ErrorActionPreference = 'Stop'
$Installer = $PSScriptRoot
$Repo      = Split-Path $Installer -Parent
$Dist      = Join-Path $Repo 'dist'
$Payload   = Join-Path $Installer 'payload'

function Fail($msg) { Write-Host "ERROR: $msg" -ForegroundColor Red; exit 1 }

# --- 1. sanity: clean module staging present ---------------------------------
$Modules = Join-Path $Dist 'Modules'
$expected = 'BetaDeps','Bannerlord.Harmony','Bannerlord.UIExtenderEx','Bannerlord.ButterLib','Bannerlord.MBOptionScreen'
if (-not (Test-Path $Modules)) {
  Fail "dist\Modules not found. Run `".\scripts\Build-Phase1.ps1`" first to stage the modules."
}
foreach ($m in $expected) {
  if (-not (Test-Path (Join-Path $Modules $m))) { Fail "dist\Modules\$m is missing — rebuild with Build-Phase1.ps1." }
}

# --- 2. sanity: BLSE source present ------------------------------------------
$BlseBin = Join-Path $BlseDir 'bin\Win64_Shipping_Client'
if (-not (Test-Path (Join-Path $BlseBin 'Bannerlord.BLSE.LauncherEx.exe'))) {
  Fail "BLSE not found at `"$BlseBin`". Download the Manual BLSE zip, unzip it, and pass -BlseDir <unzipped root> (the folder containing bin\Win64_Shipping_Client\Bannerlord.BLSE.LauncherEx.exe)."
}

# --- 3. (re)build payload ----------------------------------------------------
if (Test-Path $Payload) { Remove-Item $Payload -Recurse -Force }
New-Item -ItemType Directory -Path (Join-Path $Payload 'Modules')  | Out-Null
New-Item -ItemType Directory -Path (Join-Path $Payload 'BLSE')     | Out-Null
New-Item -ItemType Directory -Path (Join-Path $Payload 'LICENSES') | Out-Null

Write-Host "Staging the five BetaDeps modules..." -ForegroundColor Cyan
foreach ($m in $expected) {
  Copy-Item (Join-Path $Modules $m) (Join-Path $Payload 'Modules') -Recurse -Force
}

Write-Host "Staging BLSE binaries..." -ForegroundColor Cyan
Copy-Item (Join-Path $BlseBin '*') (Join-Path $Payload 'BLSE') -Recurse -Force

Write-Host "Gathering license notices..." -ForegroundColor Cyan
# BLSE's own LICENSE (MIT, (c) BUTR) — required notice. Try a few common spots.
$blseLicense = @(
  (Join-Path $BlseDir 'LICENSE'),
  (Join-Path $BlseDir 'LICENSE.txt'),
  (Join-Path $BlseBin 'LICENSE')
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $blseLicense) {
  Fail "BLSE LICENSE file not found in `"$BlseDir`". MIT requires we ship its notice — grab LICENSE from github.com/BUTR/Bannerlord.BLSE and place it in the BLSE folder."
}
Copy-Item $blseLicense (Join-Path $Payload 'LICENSES\BLSE-LICENSE.txt') -Force

# BetaDeps third-party notices (already maintained in the BetaDeps module).
$btp = Join-Path $Modules 'BetaDeps\THIRD-PARTY-LICENSES.txt'
if (Test-Path $btp) { Copy-Item $btp (Join-Path $Payload 'LICENSES\BetaDeps-THIRD-PARTY-LICENSES.txt') -Force }
# BetaDeps own license if present at repo root.
foreach ($lic in 'LICENSE','LICENSE.txt','LICENSE.md') {
  $p = Join-Path $Repo $lic
  if (Test-Path $p) { Copy-Item $p (Join-Path $Payload "LICENSES\BetaDeps-$lic") -Force; break }
}

# --- 4. find ISCC and compile -----------------------------------------------
$iscc = Get-Command ISCC.exe -ErrorAction SilentlyContinue
if (-not $iscc) {
  foreach ($c in @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe")) {
    if (Test-Path $c) { $iscc = $c; break }
  }
} else { $iscc = $iscc.Source }
if (-not $iscc) {
  Fail "Inno Setup (ISCC.exe) not found. Install Inno Setup 6 from https://jrsoftware.org/isdl.php and re-run."
}

Write-Host "Compiling installer with Inno Setup..." -ForegroundColor Cyan
& $iscc "/DAppVersion=$Version" (Join-Path $Installer 'BetaDeps-AllInOne.iss')
if ($LASTEXITCODE -ne 0) { Fail "Inno Setup compile failed (exit $LASTEXITCODE)." }

Write-Host "Done. Installer written to dist\BetaDeps-AllInOne-v$Version.exe" -ForegroundColor Green
