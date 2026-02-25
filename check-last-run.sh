#!/bin/bash

###############################################################################
# Check Last Automated Test Run
# Shows when the last automated test run occurred and its results
###############################################################################

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR" || exit 1

echo "========================================"
echo "Last Automated Test Run Status"
echo "========================================"
echo ""

# Find most recent automated report
LATEST_REPORT=$(ls -t reports/test-report-*-automated.html 2>/dev/null | head -1)
LATEST_LOG=$(ls -t reports/test-run-*-automated.log 2>/dev/null | head -1)

if [ -z "$LATEST_REPORT" ]; then
    echo "❌ No automated test reports found"
    echo ""
    exit 1
fi

# Extract timestamp from filename
TIMESTAMP=$(basename "$LATEST_REPORT" | sed 's/test-report-\(.*\)-automated.html/\1/')
echo "📅 Last Run: $TIMESTAMP"
echo ""

# Check if run was today
TODAY=$(date +"%Y-%m-%d")
if [[ "$TIMESTAMP" == "$TODAY"* ]]; then
    echo "✓ Tests ran today"
else
    DAYS_AGO=$(( ($(date +%s) - $(date -d "${TIMESTAMP:0:10}" +%s 2>/dev/null || echo 0)) / 86400 ))
    if [ "$DAYS_AGO" -eq 1 ]; then
        echo "⚠️  Tests ran yesterday (1 day ago)"
    elif [ "$DAYS_AGO" -gt 1 ]; then
        echo "❌ Tests ran $DAYS_AGO days ago - may not be running!"
    fi
fi
echo ""

# Show test results from log
if [ -f "$LATEST_LOG" ]; then
    echo "📊 Results:"
    grep -E "Total tests:|Passed:|Skipped:|Failed:|Total time:" "$LATEST_LOG" | sed 's/^/  /'
    echo ""

    if grep -q "Test Run Successful" "$LATEST_LOG"; then
        echo "✓ Status: PASSED"
    else
        echo "❌ Status: FAILED"
    fi
else
    echo "⚠️  Log file not found"
fi

echo ""
echo "📁 Report: $LATEST_REPORT"
echo "📁 Log: $LATEST_LOG"
echo ""

# Check cron log
if [ -f "cron.log" ]; then
    CRON_SIZE=$(wc -l < cron.log 2>/dev/null || echo 0)
    if [ "$CRON_SIZE" -gt 0 ]; then
        echo "📋 Cron Log (last 5 lines):"
        tail -5 cron.log | sed 's/^/  /'
    fi
fi

echo ""
echo "========================================"
