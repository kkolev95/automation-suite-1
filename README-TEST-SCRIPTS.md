# Test Execution Scripts

This project includes two test execution scripts that automatically generate HTML reports.

## Manual Test Runs

For manual/ad-hoc test runs with reports:

```bash
./manual-test-run.sh
```

Or with a description:

```bash
./manual-test-run.sh "after-integration-tests"
./manual-test-run.sh "bug-fix-verification"
```

**Output Location:**
- Reports saved to: `manual-runs/`
- Latest report: `manual-runs/latest-manual-report.html`
- Current report: `manual-runs/current-manual-report.html`

## Automated Test Runs

For scheduled/automated runs (currently runs daily at 3 AM via cron):

```bash
./automated-test-run.sh
```

**Output Location:**
- Reports saved to: `reports/`
- Latest report: `reports/latest-report.html`
- Current report: `reports/current-report.html`

## Quick Test Without Report

For quick test runs without generating reports:

```bash
dotnet test
```

## Test Filters

Run specific test categories:

```bash
./manual-test-run.sh "smoke-tests"
# Then filter in the command if needed, or modify script

# Or just use dotnet test with filters:
dotnet test --filter "Category=Integration"
dotnet test --filter "Priority=P0"
dotnet test --filter "Category=Smoke"
```

## Report Details

Both scripts generate:
- **HTML Report**: Visual test results with pass/fail status, timing, and error details
- **Log File**: Complete console output with timestamps
- **TRX File**: XML test results in TestResults/ folder

## Directory Structure

```
TestIT.ApiTests/
├── manual-test-run.sh          # Manual test script
├── automated-test-run.sh       # Automated test script
├── generate_report.py          # Report generator
├── manual-runs/                # Manual test reports (gitignored)
│   ├── current-manual-report.html
│   ├── latest-manual-report.html
│   └── test-report-YYYY-MM-DD_HH-MM-SS-description.html
├── reports/                    # Automated test reports (gitignored)
│   ├── current-report.html
│   ├── latest-report.html
│   └── test-report-YYYY-MM-DD_HH-MM-SS.html
└── TestResults/                # TRX files (gitignored)
```
