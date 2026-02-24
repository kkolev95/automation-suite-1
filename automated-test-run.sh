#!/bin/bash

###############################################################################
# Automated Test Runner - Full Suite
# Runs daily at 3 AM via cron
# Generates timestamped HTML reports in reports/ folder
###############################################################################

# Configuration
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPORT_DIR="$SCRIPT_DIR/reports"
TIMESTAMP=$(date +"%Y-%m-%d_%H-%M-%S")
DESCRIPTION="automated"
REPORT_FILE="$REPORT_DIR/test-report-$TIMESTAMP-$DESCRIPTION.html"
LOG_FILE="$REPORT_DIR/test-run-$TIMESTAMP-$DESCRIPTION.log"
TRX_FILE="$SCRIPT_DIR/TestResults/results-$TIMESTAMP.trx"

# Ensure directories exist
mkdir -p "$REPORT_DIR"
mkdir -p "$SCRIPT_DIR/TestResults"

# Start logging
echo "=========================================" | tee "$LOG_FILE"
echo "Automated Test Run: $TIMESTAMP" | tee -a "$LOG_FILE"
echo "=========================================" | tee -a "$LOG_FILE"
echo "" | tee -a "$LOG_FILE"

# Navigate to project directory
cd "$SCRIPT_DIR" || exit 1

# Run tests with detailed logging
echo "[$(date +%H:%M:%S)] Starting full test suite..." | tee -a "$LOG_FILE"
dotnet test \
    --logger "trx;LogFileName=results-$TIMESTAMP.trx" \
    --logger "console;verbosity=normal" \
    --results-directory ./TestResults \
    2>&1 | tee -a "$LOG_FILE"

TEST_EXIT_CODE=${PIPESTATUS[0]}

# Generate HTML report from TRX
echo "" | tee -a "$LOG_FILE"
echo "[$(date +%H:%M:%S)] Generating HTML report..." | tee -a "$LOG_FILE"

if [ -f "$SCRIPT_DIR/generate_report.py" ]; then
    python3 "$SCRIPT_DIR/generate_report.py" \
        "$SCRIPT_DIR/TestResults/results-$TIMESTAMP.trx" \
        "$REPORT_FILE" \
        2>&1 | tee -a "$LOG_FILE"

    if [ -f "$REPORT_FILE" ]; then
        echo "[$(date +%H:%M:%S)] ✓ Report generated: $REPORT_FILE" | tee -a "$LOG_FILE"
    else
        echo "[$(date +%H:%M:%S)] ✗ Failed to generate HTML report" | tee -a "$LOG_FILE"
    fi
else
    echo "[$(date +%H:%M:%S)] Warning: generate_report.py not found" | tee -a "$LOG_FILE"
fi

# Summary
echo "" | tee -a "$LOG_FILE"
echo "=========================================" | tee -a "$LOG_FILE"
if [ $TEST_EXIT_CODE -eq 0 ]; then
    echo "✓ Test run PASSED" | tee -a "$LOG_FILE"
else
    echo "✗ Test run FAILED (exit code: $TEST_EXIT_CODE)" | tee -a "$LOG_FILE"
fi
echo "Report: $REPORT_FILE" | tee -a "$LOG_FILE"
echo "Log: $LOG_FILE" | tee -a "$LOG_FILE"
echo "Completed: $(date)" | tee -a "$LOG_FILE"
echo "=========================================" | tee -a "$LOG_FILE"

# Create a symlink AND a real copy for Windows compatibility
ln -sf "$(basename "$REPORT_FILE")" "$REPORT_DIR/latest-report.html"
ln -sf "$(basename "$LOG_FILE")" "$REPORT_DIR/latest-log.log"
cp "$REPORT_FILE" "$REPORT_DIR/current-report.html"
cp "$LOG_FILE" "$REPORT_DIR/current-log.log"

exit $TEST_EXIT_CODE
