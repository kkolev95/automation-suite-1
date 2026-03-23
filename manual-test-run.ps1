###############################################################################
# Manual Test Runner (PowerShell) - Full Suite
# Windows equivalent of manual-test-run.sh
# Usage: .\manual-test-run.ps1 [optional-description]
###############################################################################

param(
    [string]$Description = "manual-run"
)

$ScriptDir   = Split-Path -Parent $MyInvocation.MyCommand.Path
$ReportDir   = Join-Path $ScriptDir "manual-runs"
$ResultsDir  = Join-Path $ScriptDir "TestResults"
$Timestamp   = Get-Date -Format "yyyy-MM-dd_HH-mm-ss"
$SafeDesc    = $Description -replace '[^a-zA-Z0-9\-_]', '-'

$ReportFile  = Join-Path $ReportDir "test-report-$Timestamp-$SafeDesc.html"
$LogFile     = Join-Path $ReportDir "test-run-$Timestamp-$SafeDesc.log"
$TrxFile     = Join-Path $ResultsDir "manual-results-$Timestamp.trx"

# Ensure directories exist
New-Item -ItemType Directory -Force -Path $ReportDir  | Out-Null
New-Item -ItemType Directory -Force -Path $ResultsDir | Out-Null

function Log($msg) {
    $line = "[$(Get-Date -Format 'HH:mm:ss')] $msg"
    Write-Host $line
    Add-Content -Path $LogFile -Value $line
}

Add-Content -Path $LogFile -Value "========================================="
Add-Content -Path $LogFile -Value "Manual Test Run: $Timestamp"
if ($Description -ne "manual-run") {
    Add-Content -Path $LogFile -Value "Description: $Description"
}
Add-Content -Path $LogFile -Value "========================================="
Add-Content -Path $LogFile -Value ""

Set-Location $ScriptDir

# ── Run tests ─────────────────────────────────────────────────────────────────
Log "Starting full test suite..."
$testOutput = dotnet test `
    --logger "trx;LogFileName=manual-results-$Timestamp.trx" `
    --logger "console;verbosity=normal" `
    --results-directory $ResultsDir `
    2>&1

$TestExitCode = $LASTEXITCODE
$testOutput | Tee-Object -FilePath $LogFile -Append

# ── Generate HTML report ──────────────────────────────────────────────────────
Add-Content -Path $LogFile -Value ""
Log "Generating HTML report..."

$GenerateScript = Join-Path $ScriptDir "generate_report.py"

if (Test-Path $GenerateScript) {
    # Try python3 first, fall back to python
    $pythonCmd = if (Get-Command python3 -ErrorAction SilentlyContinue) { "python3" } else { "python" }

    & $pythonCmd $GenerateScript $TrxFile $ReportFile 2>&1 | Tee-Object -FilePath $LogFile -Append

    if (Test-Path $ReportFile) {
        Log "Report generated: $ReportFile"
    } else {
        Log "Failed to generate HTML report"
    }
} else {
    Log "Warning: generate_report.py not found"
}

# ── Summary ───────────────────────────────────────────────────────────────────
Add-Content -Path $LogFile -Value ""
Add-Content -Path $LogFile -Value "========================================="
if ($TestExitCode -eq 0) {
    $summary = "PASSED"
    Write-Host "✓ Test run PASSED" -ForegroundColor Green
} else {
    $summary = "FAILED (exit code: $TestExitCode)"
    Write-Host "✗ Test run FAILED (exit code: $TestExitCode)" -ForegroundColor Red
}
Add-Content -Path $LogFile -Value "$summary"
Add-Content -Path $LogFile -Value "Report: $ReportFile"
Add-Content -Path $LogFile -Value "Log:    $LogFile"
Add-Content -Path $LogFile -Value "Completed: $(Get-Date)"
Add-Content -Path $LogFile -Value "========================================="

# ── Copy to latest/current ────────────────────────────────────────────────────
if (Test-Path $ReportFile) {
    Copy-Item $ReportFile (Join-Path $ReportDir "current-manual-report.html") -Force
    Copy-Item $LogFile    (Join-Path $ReportDir "current-manual-log.log")    -Force
}

Write-Host ""
Write-Host "Report: manual-runs\current-manual-report.html"
Write-Host "Log:    manual-runs\current-manual-log.log"
Write-Host ""

exit $TestExitCode
