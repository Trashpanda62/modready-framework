# Update-Gist.ps1
# Syncs C:\dev\bannerlord\master-plan-calendar.ics to the public GitHub Gist
# that Google Calendar subscribes to.
#
# One-time setup (run this ONCE before the first use of the script):
#   cd C:\dev\bannerlord
#   git clone https://gist.github.com/Trashpanda62/069f5e4fe73e19e19e69b1700231b50a.git gist-master-plan
#
# After that, this script copies the latest .ics into the gist clone,
# commits, and pushes. Google Calendar polls the gist roughly every
# 12-24 hours and picks up the change automatically.
#
# Usage (from the nightly master-plan update protocol):
#   cd C:\dev\beta-deps
#   .\scripts\Update-Gist.ps1

$ErrorActionPreference = "Stop"

$SourceIcs = "C:\dev\bannerlord\master-plan-calendar.ics"
$GistDir   = "C:\dev\bannerlord\gist-master-plan"
$IcsName   = "master-plan-calendar.ics"
$GistUrl   = "https://gist.github.com/Trashpanda62/069f5e4fe73e19e19e69b1700231b50a.git"

if (-not (Test-Path $SourceIcs)) {
    Write-Host ""
    Write-Host "ERROR: source .ics not found at $SourceIcs" -ForegroundColor Red
    exit 1
}

if (-not (Test-Path $GistDir)) {
    Write-Host ""
    Write-Host "Gist clone not found at $GistDir." -ForegroundColor Yellow
    Write-Host "Run this one-time setup first:" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "  cd C:\dev\bannerlord"
    Write-Host "  git clone $GistUrl gist-master-plan"
    Write-Host ""
    Write-Host "Then re-run this script." -ForegroundColor Yellow
    exit 1
}

Copy-Item -Path $SourceIcs -Destination (Join-Path $GistDir $IcsName) -Force

Push-Location $GistDir
try {
    git add $IcsName | Out-Null
    $changes = git status --porcelain
    if (-not $changes) {
        Write-Host "Gist already up to date - no changes to push." -ForegroundColor Green
        exit 0
    }
    $msg = "Update calendar $(Get-Date -Format 'yyyy-MM-dd HH:mm')"
    git commit -m $msg
    if ($LASTEXITCODE -ne 0) {
        Write-Host "git commit failed" -ForegroundColor Red
        exit 1
    }
    git push
    if ($LASTEXITCODE -ne 0) {
        Write-Host "git push failed - check your git credentials" -ForegroundColor Red
        exit 1
    }
    Write-Host ""
    Write-Host "Gist pushed successfully. Google Calendar will refresh within ~12 hours." -ForegroundColor Green
} finally {
    Pop-Location
}
