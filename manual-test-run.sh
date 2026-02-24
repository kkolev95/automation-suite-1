#!/bin/bash

###############################################################################
# Manual Test Runner - Full Suite
# Run this script manually to generate reports in manual-runs/ folder
# Usage: ./manual-test-run.sh [optional-description]
###############################################################################

# Configuration
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPORT_DIR="$SCRIPT_DIR/manual-runs"
TIMESTAMP=$(date +"%Y-%m-%d_%H-%M-%S")

# Optional description from command line argument
DESCRIPTION="${1:-manual-run}"
SAFE_DESCRIPTION=$(echo "$DESCRIPTION" | tr ' ' '-' | tr -cd '[:alnum:]-_')

REPORT_FILE="$REPORT_DIR/test-report-$TIMESTAMP-$SAFE_DESCRIPTION.html"
LOG_FILE="$REPORT_DIR/test-run-$TIMESTAMP-$SAFE_DESCRIPTION.log"
TRX_FILE="$SCRIPT_DIR/TestResults/manual-results-$TIMESTAMP.trx"

# Ensure directories exist
mkdir -p "$REPORT_DIR"
mkdir -p "$SCRIPT_DIR/TestResults"

# Start logging
echo "=========================================" | tee "$LOG_FILE"
echo "Manual Test Run: $TIMESTAMP" | tee -a "$LOG_FILE"
if [ -n "$1" ]; then
    echo "Description: $1" | tee -a "$LOG_FILE"
fi
echo "=========================================" | tee -a "$LOG_FILE"
echo "" | tee -a "$LOG_FILE"

# Navigate to project directory
cd "$SCRIPT_DIR" || exit 1

# Run tests with detailed logging
echo "[$(date +%H:%M:%S)] Starting full test suite..." | tee -a "$LOG_FILE"
dotnet test \
    --logger "trx;LogFileName=manual-results-$TIMESTAMP.trx" \
    --logger "console;verbosity=normal" \
    --results-directory ./TestResults \
    2>&1 | tee -a "$LOG_FILE"

TEST_EXIT_CODE=${PIPESTATUS[0]}

# Generate HTML report from TRX
echo "" | tee -a "$LOG_FILE"
echo "[$(date +%H:%M:%S)] Generating HTML report..." | tee -a "$LOG_FILE"

if [ -f "$SCRIPT_DIR/generate_report.py" ]; then
    python3 "$SCRIPT_DIR/generate_report.py" \
        "$SCRIPT_DIR/TestResults/manual-results-$TIMESTAMP.trx" \
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

# Create symlinks to latest manual run
ln -sf "$(basename "$REPORT_FILE")" "$REPORT_DIR/latest-manual-report.html"
ln -sf "$(basename "$LOG_FILE")" "$REPORT_DIR/latest-manual-log.log"

# Also create easy-to-find copies
cp "$REPORT_FILE" "$REPORT_DIR/current-manual-report.html"
cp "$LOG_FILE" "$REPORT_DIR/current-manual-log.log"

echo ""
echo "📊 Report available at: $REPORT_FILE"
echo "📝 Log available at: $LOG_FILE"
echo ""
echo "Quick access:"
echo "  - Latest report: manual-runs/latest-manual-report.html"
echo "  - Current report: manual-runs/current-manual-report.html"
echo ""

exit $TEST_EXIT_CODE
