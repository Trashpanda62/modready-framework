# BetaDeps -- XML validator (build-time lint)
#
# Walks every .xml file that's about to ship (fomod\*.xml plus every
# Modules\*\SubModule.xml in the dist tree) and checks for:
#   1. The literal sequence "--" inside an XML comment body. Forbidden
#      by the XML 1.0 spec and a real-world install blocker for Vortex's
#      FOMOD parser (v0.7.2/v0.7.3 shipped a broken ModuleConfig.xml
#      that this lint would have caught -- see runtime issue 2026-05-25).
#   2. Basic structural validity via [xml] cast in PowerShell. Fast,
#      catches missing closing tags / encoding bugs.
#
# Exit code is non-zero if anything failed -- Build-Phase1.ps1 wraps a
# call to this and aborts the build on lint failure.
#
# Run standalone:
#   .\scripts\Validate-Xml.ps1 -Root C:\dev\beta-deps\dist
#
# Or skipping if you're iterating:
#   $env:BETADEPS_SKIP_XML_LINT = '1'

[CmdletBinding()]
param(
    [string]$Root = (Join-Path (Split-Path -Parent $PSScriptRoot) 'dist'),
    [switch]$Quiet
)

$ErrorActionPreference = 'Continue'

if ($env:BETADEPS_SKIP_XML_LINT -eq '1') {
    if (-not $Quiet) { Write-Host "XML lint: SKIPPED (BETADEPS_SKIP_XML_LINT=1)" -ForegroundColor Yellow }
    exit 0
}

if (-not (Test-Path $Root)) {
    Write-Host "XML lint: root not found ($Root); nothing to check" -ForegroundColor Yellow
    exit 0
}

if (-not $Quiet) { Write-Host "XML lint: scanning $Root" -ForegroundColor Cyan }

$failed = $false
$checked = 0

# Walk every .xml we ship. dist\fomod\*.xml + dist\Modules\**\*.xml.
$xmlFiles = Get-ChildItem -Path $Root -Recurse -Filter '*.xml' -File -ErrorAction SilentlyContinue
foreach ($f in $xmlFiles) {
    $checked++
    $rel = Resolve-Path -Relative $f.FullName
    $text = Get-Content -Raw -Path $f.FullName -ErrorAction SilentlyContinue
    if (-not $text) { continue }

    # Lint #1: '--' inside XML comments.
    # Strategy: enumerate every "<!-- ... -->" block, then look for "--" in
    # the body (between the open and close delimiters). The closing "-->"
    # contains "--" by definition, so we MUST extract the body first
    # rather than just substring-grepping the whole file.
    $bad = $false
    $reCommentBody = [regex]'<!--(.*?)-->'
    $matchOptions = [System.Text.RegularExpressions.RegexOptions]::Singleline
    $reBody = New-Object System.Text.RegularExpressions.Regex '<!--(.*?)-->', $matchOptions
    foreach ($m in $reBody.Matches($text)) {
        $body = $m.Groups[1].Value
        if ($body.Contains('--')) {
            # Find the line + column of the first '--' inside this body
            # so the error message is grep-able.
            $offsetInFile = $m.Index + 4  # skip "<!--"
            $relativeBadIdx = $body.IndexOf('--')
            $absoluteBadIdx = $offsetInFile + $relativeBadIdx

            # Compute line + col.
            $upto = $text.Substring(0, $absoluteBadIdx)
            $line = ($upto -split "`n").Count
            $lastNewline = $upto.LastIndexOf("`n")
            $col = if ($lastNewline -lt 0) { $absoluteBadIdx + 1 } else { $absoluteBadIdx - $lastNewline }

            Write-Host "  FAIL  $rel  line $line col $col  -- '--' inside XML comment (Xml_InvalidCommentChars)" -ForegroundColor Red
            $bad = $true
            $failed = $true
        }
    }

    # Lint #2: basic well-formedness.
    try {
        $xml = New-Object System.Xml.XmlDocument
        $xml.LoadXml($text) | Out-Null
    }
    catch {
        Write-Host "  FAIL  $rel  XML parse error: $($_.Exception.Message)" -ForegroundColor Red
        $bad = $true
        $failed = $true
    }

    if (-not $bad -and -not $Quiet) {
        Write-Host "  PASS  $rel" -ForegroundColor Green
    }
}

if (-not $Quiet) {
    Write-Host ""
    if ($failed) {
        Write-Host "XML lint: $checked checked, FAILED (fix the above before shipping)" -ForegroundColor Red
    } else {
        Write-Host "XML lint: $checked checked, OK" -ForegroundColor Green
    }
}

if ($failed) { exit 1 } else { exit 0 }
