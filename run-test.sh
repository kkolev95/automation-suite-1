#!/bin/bash

###############################################################################
# Simple Test Runner - Generates HTML Report
# Usage: ./run-test.sh "description"
###############################################################################

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPORT_DIR="$SCRIPT_DIR/manual-runs"
TIMESTAMP=$(date +"%Y-%m-%d_%H-%M-%S")
DESCRIPTION="${1:-test-run}"
SAFE_DESCRIPTION=$(echo "$DESCRIPTION" | tr ' ' '-' | tr -cd '[:alnum:]-_')

mkdir -p "$REPORT_DIR"
mkdir -p "$SCRIPT_DIR/TestResults"

REPORT_FILE="$REPORT_DIR/test-report-$TIMESTAMP-$SAFE_DESCRIPTION.html"
LOG_FILE="$REPORT_DIR/test-run-$TIMESTAMP-$SAFE_DESCRIPTION.log"
TRX_FILE="$SCRIPT_DIR/TestResults/manual-results-$TIMESTAMP.trx"

echo "=========================================" | tee "$LOG_FILE"
echo "Test Run: $TIMESTAMP" | tee -a "$LOG_FILE"
echo "Description: $DESCRIPTION" | tee -a "$LOG_FILE"
echo "=========================================" | tee -a "$LOG_FILE"
echo "" | tee -a "$LOG_FILE"

cd "$SCRIPT_DIR" || exit 1

echo "[$(date +%H:%M:%S)] Running tests..." | tee -a "$LOG_FILE"
dotnet test \
    --logger "trx;LogFileName=manual-results-$TIMESTAMP.trx" \
    --logger "console;verbosity=normal" \
    --results-directory ./TestResults \
    2>&1 | tee -a "$LOG_FILE"

TEST_EXIT_CODE=${PIPESTATUS[0]}

echo "" | tee -a "$LOG_FILE"
echo "[$(date +%H:%M:%S)] Generating HTML report..." | tee -a "$LOG_FILE"

if [ -f "$SCRIPT_DIR/generate_report.py" ]; then
    python3 "$SCRIPT_DIR/generate_report.py" \
        "$SCRIPT_DIR/TestResults/manual-results-$TIMESTAMP.trx" \
        "$REPORT_FILE" \
        2>&1 | tee -a "$LOG_FILE"

    if [ -f "$REPORT_FILE" ]; then
        echo "[$(date +%H:%M:%S)] ✓ Report: $REPORT_FILE" | tee -a "$LOG_FILE"
    fi
fi

echo "" | tee -a "$LOG_FILE"
echo "=========================================" | tee -a "$LOG_FILE"
if [ $TEST_EXIT_CODE -eq 0 ]; then
    echo "✓ PASSED" | tee -a "$LOG_FILE"
else
    echo "✗ FAILED" | tee -a "$LOG_FILE"
fi
echo "=========================================" | tee -a "$LOG_FILE"

ln -sf "$(basename "$REPORT_FILE")" "$REPORT_DIR/latest-manual-report.html"
ln -sf "$(basename "$LOG_FILE")" "$REPORT_DIR/latest-manual-log.log"
cp "$REPORT_FILE" "$REPORT_DIR/current-manual-report.html"
cp "$LOG_FILE" "$REPORT_DIR/current-manual-log.log"

echo ""
echo "Report: manual-runs/latest-manual-report.html"
echo ""

exit $TEST_EXIT_CODE
