#!/bin/bash

###############################################################################
# Cleanup Old Reports Script
# Removes old test reports while keeping recent ones
# Keeps only reports/ and manual-runs/ directories
###############################################################################

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR" || exit 1

echo "========================================="
echo "Report Cleanup Utility"
echo "========================================="
echo ""

# Function to count files
count_files() {
    find "$1" -maxdepth 1 -name "$2" 2>/dev/null | wc -l
}

# Function to keep only the N most recent files
keep_recent() {
    local dir="$1"
    local pattern="$2"
    local keep_count="$3"

    if [ ! -d "$dir" ]; then
        return
    fi

    local total=$(count_files "$dir" "$pattern")
    if [ "$total" -le "$keep_count" ]; then
        echo "  ✓ $dir/$pattern: $total files (keeping all)"
        return
    fi

    local to_delete=$((total - keep_count))
    echo "  → $dir/$pattern: $total files (keeping $keep_count, removing $to_delete old files)"

    # Delete all but the N most recent files
    find "$dir" -maxdepth 1 -name "$pattern" -type f -printf '%T@ %p\n' | \
        sort -n | \
        head -n "$to_delete" | \
        cut -d' ' -f2- | \
        xargs -r rm -f
}

echo "Step 1: Unifying log file naming format"
echo "---------------------------------------------------------------------"

# Rename old-format automated logs to new format (test-run-TIMESTAMP.log → test-run-TIMESTAMP-automated.log)
if compgen -G "reports/test-run-[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]_[0-9][0-9]-[0-9][0-9]-[0-9][0-9].log" > /dev/null; then
    echo "  → Renaming old-format log files to unified format"
    for old_log in reports/test-run-[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]_[0-9][0-9]-[0-9][0-9]-[0-9][0-9].log; do
        if [ -f "$old_log" ]; then
            new_log="${old_log%.log}-automated.log"
            mv "$old_log" "$new_log"
            echo "    • $(basename "$old_log") → $(basename "$new_log")"
        fi
    done
fi

# Rename old-format automated reports to new format
if compgen -G "reports/test-report-[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]_[0-9][0-9]-[0-9][0-9]-[0-9][0-9].html" > /dev/null; then
    echo "  → Renaming old-format HTML reports to unified format"
    for old_report in reports/test-report-[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]_[0-9][0-9]-[0-9][0-9]-[0-9][0-9].html; do
        if [ -f "$old_report" ]; then
            new_report="${old_report%.html}-automated.html"
            mv "$old_report" "$new_report"
            echo "    • $(basename "$old_report") → $(basename "$new_report")"
        fi
    done
fi

echo ""
echo "Step 2: Cleaning up old reports (keeping 5 most recent of each type)"
echo "---------------------------------------------------------------------"

# Keep only 5 most recent automated reports
keep_recent "reports" "test-report-*-*.html" 5
keep_recent "reports" "test-run-*-*.log" 5

# Keep only 5 most recent manual reports
keep_recent "manual-runs" "test-report-*-*.html" 5
keep_recent "manual-runs" "test-run-*-*.log" 5

# Keep only 10 most recent TRX files
keep_recent "TestResults" "results-*.trx" 10
keep_recent "TestResults" "manual-results-*.trx" 10

echo ""
echo "Step 3: Removing unwanted subdirectories and old files"
echo "---------------------------------------------------------------------"

# Remove reports/all/ subdirectory
if [ -d "reports/all" ]; then
    echo "  → Removing reports/all/ subdirectory"
    rm -rf "reports/all"
fi

# Remove backup files
if compgen -G "reports/*_BACKUP.*" > /dev/null; then
    echo "  → Removing backup files from reports/"
    rm -f reports/*_BACKUP.*
fi

# Remove old functional test reports with different naming
if compgen -G "reports/test-report_functional_*" > /dev/null; then
    echo "  → Removing old functional test reports"
    rm -f reports/test-report_functional_*
fi

# Clean up root directory report files
if [ -f "test-report.html" ]; then
    echo "  → Removing test-report.html from root"
    rm -f test-report.html
fi

if [ -f "results.trx" ]; then
    echo "  → Removing results.trx from root"
    rm -f results.trx
fi

if [ -f "TestResults/results.trx" ]; then
    echo "  → Removing generic results.trx from TestResults/"
    rm -f TestResults/results.trx
fi

echo ""
echo "Step 4: Current report structure"
echo "---------------------------------------------------------------------"

echo ""
echo "📁 Automated Reports (reports/):"
ls -lh reports/*.html 2>/dev/null | tail -5 | awk '{print "  " $9 " (" $5 ")"}'

echo ""
echo "📁 Manual Reports (manual-runs/):"
ls -lh manual-runs/*.html 2>/dev/null | tail -5 | awk '{print "  " $9 " (" $5 ")"}'

echo ""
echo "📁 TRX Files (TestResults/):"
echo "  Automated: $(count_files 'TestResults' 'results-*.trx') files"
echo "  Manual: $(count_files 'TestResults' 'manual-results-*.trx') files"

echo ""
echo "========================================="
echo "✓ Cleanup Complete"
echo "========================================="
echo ""
echo "Quick access:"
echo "  Latest automated: reports/latest-report.html"
echo "  Latest manual:    manual-runs/latest-manual-report.html"
echo ""
