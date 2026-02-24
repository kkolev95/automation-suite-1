# Test Suite Command Reference

Complete list of all available commands for the TestIT.ApiTests test suite.

## Quick Test Execution

### Simplified Test Runner (run.sh)

The easiest way to run tests by class name:

```bash
# List all available test classes
./run.sh

# Run specific test class (partial name matching)
./run.sh integration      # Runs IntegrationTests
./run.sh auth            # Runs AuthenticationTests
./run.sh security        # Runs SecurityTests
./run.sh company         # Runs CompanyTests
./run.sh analytics       # Runs AnalyticsTests
./run.sh question        # Runs QuestionManagementTests
./run.sh folder          # Runs FolderTests
./run.sh invite          # Runs InviteTests
./run.sh taking          # Runs TestTakingTests
./run.sh management      # Runs TestsManagementTests
./run.sh performance     # Runs PerformanceTests
./run.sh stress          # Runs StressTests
./run.sh edge            # Runs EdgeCaseTests
./run.sh schema          # Runs SchemaValidationTests
./run.sh coverage        # Runs CoverageTests
./run.sh dataintegrity   # Runs DataIntegrityTests
./run.sh patch           # Runs PatchTests
./run.sh cleanup         # Runs CleanupTests

# Run all tests
./run.sh all
```

### Direct dotnet Commands

### Run All Tests (No Report)
```bash
dotnet test
```

### Run All Tests with Verbose Output
```bash
dotnet test --logger "console;verbosity=detailed"
```

### Run All Tests with Normal Output
```bash
dotnet test --logger "console;verbosity=normal"
```

---

## Test Execution with Reports

### Manual Test Run (Saves to manual-runs/)
```bash
# Basic run
./manual-test-run.sh

# With description
./manual-test-run.sh "after-integration-tests"
./manual-test-run.sh "bug-fix-verification"
./manual-test-run.sh "before-release"
```

### Automated Test Run (Saves to reports/)
```bash
./automated-test-run.sh
```

---

## Filter Tests by Category

### Run Smoke Tests Only
```bash
dotnet test --filter "Category=Smoke"
```

### Run Integration Tests Only
```bash
dotnet test --filter "Category=Integration"
./manual-test-run.sh "integration-only"  # (then manually filter if needed)
```

### Run Security Tests Only
```bash
dotnet test --filter "Category=Security"
```

### Run Validation Tests Only
```bash
dotnet test --filter "Category=Validation"
```

### Run Authentication Tests Only
```bash
dotnet test --filter "Category=Authentication"
```

### Run Performance Tests Only
```bash
dotnet test --filter "Category=Performance"
```

### Run Stress Tests Only
```bash
dotnet test --filter "Category=Stress"
```

### Run Edge Case Tests Only
```bash
dotnet test --filter "Category=EdgeCase"
```

### Run Data Integrity Tests Only
```bash
dotnet test --filter "Category=DataIntegrity"
```

### Run Schema Validation Tests Only
```bash
dotnet test --filter "Category=SchemaValidation"
```

### Run Coverage Tests Only
```bash
dotnet test --filter "Category=Coverage"
```

### Run Patch Tests Only
```bash
dotnet test --filter "Category=Patch"
```

### Run Cleanup Tests Only
```bash
dotnet test --filter "Category=Cleanup"
```

---

## Filter Tests by Priority

### Run P0 (Critical) Tests Only
```bash
dotnet test --filter "Priority=P0"
```

### Run P1 (High Priority) Tests Only
```bash
dotnet test --filter "Priority=P1"
```

### Run P2 (Medium Priority) Tests Only
```bash
dotnet test --filter "Priority=P2"
```

---

## Filter Tests by Test Class

### Run Authentication Tests
```bash
dotnet test --filter "FullyQualifiedName~AuthenticationTests"
```

### Run Test Management Tests
```bash
dotnet test --filter "FullyQualifiedName~TestsManagementTests"
```

### Run Integration Tests
```bash
dotnet test --filter "FullyQualifiedName~IntegrationTests"
```

### Run Company Tests
```bash
dotnet test --filter "FullyQualifiedName~CompanyTests"
```

### Run Invite Tests
```bash
dotnet test --filter "FullyQualifiedName~InviteTests"
```

### Run Test Taking Tests
```bash
dotnet test --filter "FullyQualifiedName~TestTakingTests"
```

### Run Analytics Tests
```bash
dotnet test --filter "FullyQualifiedName~AnalyticsTests"
```

### Run Question Management Tests
```bash
dotnet test --filter "FullyQualifiedName~QuestionManagementTests"
```

### Run Folder Tests
```bash
dotnet test --filter "FullyQualifiedName~FolderTests"
```

---

## Filter Tests by Name Pattern

### Run Specific Test
```bash
dotnet test --filter "FullyQualifiedName~CompleteTestAuthorJourney"
```

### Run Tests Containing Keyword
```bash
dotnet test --filter "FullyQualifiedName~Login"
dotnet test --filter "FullyQualifiedName~Register"
dotnet test --filter "FullyQualifiedName~Create"
```

---

## Combined Filters

### Run P0 Integration Tests
```bash
dotnet test --filter "Category=Integration&Priority=P0"
```

### Run P0 Smoke Tests
```bash
dotnet test --filter "Category=Smoke&Priority=P0"
```

### Run Security and Authentication Tests
```bash
dotnet test --filter "Category=Security|Category=Authentication"
```

---

## Test Results and Reporting

### Generate TRX File Only
```bash
dotnet test --logger "trx;LogFileName=results.trx" --results-directory ./TestResults
```

### Generate HTML Report from Existing TRX
```bash
python3 generate_report.py TestResults/results.trx reports/my-report.html
```

### View Latest Manual Report
```bash
# Linux
xdg-open manual-runs/latest-manual-report.html

# Windows WSL
explorer.exe manual-runs/latest-manual-report.html

# macOS
open manual-runs/latest-manual-report.html
```

### View Latest Automated Report
```bash
# Linux
xdg-open reports/latest-report.html

# Windows WSL
explorer.exe reports/latest-report.html

# macOS
open reports/latest-report.html
```

---

## Cleanup Commands

### Clean Up Old Reports

```bash
# Remove old reports while keeping 5 most recent of each type
./cleanup-old-reports.sh
```

This script will:
- Rename old-format reports to unified naming (adds "-automated" suffix)
- Keep only 5 most recent automated reports and logs
- Keep only 5 most recent manual reports and logs
- Keep only 10 most recent TRX files
- Remove backup files and unwanted subdirectories
- Clean up root-level report files

### View Cleanup Tests
```bash
dotnet test --filter "Category=Cleanup" --logger "console;verbosity=detailed"
```

### Run Manual Cleanup (Delete All Test Data)
```bash
dotnet test --filter "FullyQualifiedName~ManualCleanup_DeleteAllTestData"
```

### Run Cleanup for Old Data (7+ days)
```bash
dotnet test --filter "FullyQualifiedName~ManualCleanup_DeleteOldTestData_7Days"
```

### Run Dry Run Cleanup (See What Would Be Deleted)
```bash
dotnet test --filter "FullyQualifiedName~ManualCleanup_DryRun"
```

### Run Account Cleanup
```bash
dotnet test --filter "FullyQualifiedName~AccountCleanup_AllTrackedAccounts_DeletesSuccessfully"
```

---

## Build and Restore

### Restore NuGet Packages
```bash
dotnet restore
```

### Build Project
```bash
dotnet build
```

### Clean Build Artifacts
```bash
dotnet clean
```

### Rebuild Project
```bash
dotnet clean && dotnet build
```

---

## Monitoring and Logs

### Watch Test Execution Live
```bash
dotnet test --logger "console;verbosity=detailed" | tee test-output.txt
```

### Tail Latest Manual Run Log
```bash
tail -f manual-runs/current-manual-log.log
```

### Tail Latest Automated Run Log
```bash
tail -f reports/current-log.log
```

### View Last 50 Lines of Latest Log
```bash
tail -50 manual-runs/latest-manual-log.log
```

---

## Directory and File Operations

### List All Reports
```bash
ls -lth reports/
ls -lth manual-runs/
```

### Count Total Tests
```bash
dotnet test --list-tests | grep -c "    "
```

### Find Specific Test
```bash
dotnet test --list-tests | grep "Integration"
```

### List All Test Classes
```bash
find Tests/ -name "*Tests.cs" | sort
```

### Count Tests Per Category
```bash
grep -r "\[Trait.*Category" Tests/ | cut -d'"' -f4 | sort | uniq -c
```

---

## Advanced Options

### Run Tests in Parallel
```bash
dotnet test --parallel
```

### Run Tests with Maximum Parallelization
```bash
dotnet test -- RunConfiguration.MaxCpuCount=0
```

### Run Tests with Specific Framework
```bash
dotnet test --framework net8.0
```

### Run Tests with No Build
```bash
dotnet test --no-build
```

### Run Tests with No Restore
```bash
dotnet test --no-restore
```

### Collect Code Coverage
```bash
dotnet test --collect:"XPlat Code Coverage"
```

---

## Configuration

### View Current Test Configuration
```bash
cat TestConfiguration.cs
```

### Check Base URL
```bash
grep -A 5 "GetBaseUrl" Helpers/TestConfiguration.cs
```

---

## Useful Combinations

### Quick Smoke Test (P0 Only)
```bash
dotnet test --filter "Priority=P0" --logger "console;verbosity=normal"
```

### Full Suite with Report
```bash
./manual-test-run.sh "full-regression"
```

### Integration Tests Only with Report
```bash
dotnet test --filter "Category=Integration" --logger "trx;LogFileName=integration.trx" --results-directory ./TestResults
python3 generate_report.py TestResults/integration.trx manual-runs/integration-report.html
```

### Security & Auth Tests
```bash
dotnet test --filter "Category=Security|Category=Authentication" --logger "console;verbosity=detailed"
```

### All P0 Tests (Critical Path)
```bash
./manual-test-run.sh "critical-path-p0"
# Then filter manually or:
dotnet test --filter "Priority=P0" --logger "console;verbosity=detailed"
```

---

## Debugging

### Run Single Test with Maximum Detail
```bash
dotnet test --filter "FullyQualifiedName~CompleteTestAuthorJourney" --logger "console;verbosity=diagnostic"
```

### Run Tests with Environment Variable
```bash
ASPNETCORE_ENVIRONMENT=Development dotnet test
```

### Run Tests and Output to File
```bash
dotnet test --logger "console;verbosity=detailed" > test-results.txt 2>&1
```

---

## Scheduled/Cron Jobs

### View Cron Schedule (if configured)
```bash
crontab -l | grep test
```

### Add Automated Run to Cron (3 AM Daily)
```bash
crontab -e
# Add: 0 3 * * * cd /path/to/TestIT.ApiTests && ./automated-test-run.sh
```

---

## Quick Reference Summary

| Command | Purpose |
|---------|---------|
| `./run.sh` | List all available test classes |
| `./run.sh integration` | Run IntegrationTests class |
| `./run.sh auth` | Run AuthenticationTests class |
| `./run.sh all` | Run all tests |
| `dotnet test` | Run all tests (no report) |
| `./manual-test-run.sh "description"` | Run all tests with HTML report (manual-runs/) |
| `./automated-test-run.sh` | Run all tests with HTML report (reports/) |
| `./cleanup-old-reports.sh` | Clean up old reports (keep 5 most recent) |
| `dotnet test --filter "Category=Integration"` | Run integration tests only |
| `dotnet test --filter "Priority=P0"` | Run critical tests only |
| `dotnet test --list-tests` | List all available tests |
| `tail -f manual-runs/current-manual-log.log` | Watch test execution live |

---

## Report Locations and Naming Format

### Unified Naming Convention

All reports and logs follow a consistent naming format:

**Automated runs** (reports/):
- Reports: `test-report-YYYY-MM-DD_HH-MM-SS-automated.html`
- Logs: `test-run-YYYY-MM-DD_HH-MM-SS-automated.log`
- TRX files: `results-YYYY-MM-DD_HH-MM-SS.trx`

**Manual runs** (manual-runs/):
- Reports: `test-report-YYYY-MM-DD_HH-MM-SS-description.html`
- Logs: `test-run-YYYY-MM-DD_HH-MM-SS-description.log`
- TRX files: `manual-results-YYYY-MM-DD_HH-MM-SS.trx`

### Quick Access Links

- **Latest manual run**: `manual-runs/latest-manual-report.html` (symlink)
- **Latest automated run**: `reports/latest-report.html` (symlink)
- **Current manual log**: `manual-runs/current-manual-log.log`
- **Current automated log**: `reports/current-log.log`

### Directory Structure

```
TestIT.ApiTests/
├── reports/                    # Automated nightly runs
│   ├── test-report-*-automated.html
│   ├── test-run-*-automated.log
│   ├── latest-report.html      (symlink)
│   └── current-log.log
├── manual-runs/                # Manual test executions
│   ├── test-report-*-description.html
│   ├── test-run-*-description.log
│   ├── latest-manual-report.html  (symlink)
│   └── current-manual-log.log
└── TestResults/                # Raw TRX files
    ├── results-*.trx           (automated)
    └── manual-results-*.trx    (manual)
```

---

## Environment Variables

You can set these before running tests:

```bash
# Set custom API base URL
export TESTIT_BASE_URL="https://your-api.com"
dotnet test

# Run with custom timeout
export TESTIT_TIMEOUT=60000
dotnet test
```

Check `Helpers/TestConfiguration.cs` for all available configuration options.
