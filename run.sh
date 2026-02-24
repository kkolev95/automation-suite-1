#!/usr/bin/env bash
# run.sh — run one test class (or all tests) by name
#
# Usage:
#   ./run.sh                  → list available test classes
#   ./run.sh security         → run SecurityTests
#   ./run.sh auth             → run AuthenticationTests
#   ./run.sh all              → run every test
#
# Matching is case-insensitive and partial — "patch" matches PatchTests, etc.

set -euo pipefail
cd "$(dirname "$0")"

CLASSES=(
  "AnalyticsTests"
  "AuthenticationTests"
  "CleanupTests"
  "CompanyTests"
  "CoverageTests"
  "DataIntegrityTests"
  "EdgeCaseTests"
  "FolderTests"
  "IntegrationTests"
  "InviteTests"
  "PatchTests"
  "PerformanceTests"
  "QuestionManagementTests"
  "SchemaValidationTests"
  "SecurityTests"
  "StressTests"
  "TestsManagementTests"
  "TestTakingTests"
)

# ── No argument: print help ──────────────────────────────────────────────────
if [[ $# -eq 0 ]]; then
  echo ""
  echo "Usage: ./run.sh <class>   or   ./run.sh all"
  echo ""
  echo "Available test classes:"
  for c in "${CLASSES[@]}"; do
    echo "  ${c}"
  done
  echo ""
  echo "Examples:"
  echo "  ./run.sh analytics       → AnalyticsTests"
  echo "  ./run.sh auth            → AuthenticationTests"
  echo "  ./run.sh cleanup         → CleanupTests"
  echo "  ./run.sh company         → CompanyTests"
  echo "  ./run.sh coverage        → CoverageTests"
  echo "  ./run.sh dataintegrity   → DataIntegrityTests"
  echo "  ./run.sh edge            → EdgeCaseTests"
  echo "  ./run.sh folder          → FolderTests"
  echo "  ./run.sh integration     → IntegrationTests"
  echo "  ./run.sh invite          → InviteTests"
  echo "  ./run.sh patch           → PatchTests"
  echo "  ./run.sh performance     → PerformanceTests"
  echo "  ./run.sh question        → QuestionManagementTests"
  echo "  ./run.sh schema          → SchemaValidationTests"
  echo "  ./run.sh security        → SecurityTests"
  echo "  ./run.sh stress          → StressTests"
  echo "  ./run.sh management      → TestsManagementTests"
  echo "  ./run.sh taking          → TestTakingTests"
  echo "  ./run.sh all             → all tests"
  echo ""
  exit 0
fi

INPUT="${1,,}"   # lowercase

# ── Run all ──────────────────────────────────────────────────────────────────
if [[ "$INPUT" == "all" ]]; then
  echo "Running all tests..."
  dotnet test --logger "trx;LogFileName=results.trx" --logger "console;verbosity=normal"
  exit $?
fi

# ── Find matching class ───────────────────────────────────────────────────────
MATCH=""
for c in "${CLASSES[@]}"; do
  if [[ "${c,,}" == *"$INPUT"* ]]; then
    if [[ -n "$MATCH" ]]; then
      echo "Ambiguous match — '$1' matches multiple classes:"
      for c2 in "${CLASSES[@]}"; do
        [[ "${c2,,}" == *"$INPUT"* ]] && echo "  $c2"
      done
      echo "Be more specific."
      exit 1
    fi
    MATCH="$c"
  fi
done

if [[ -z "$MATCH" ]]; then
  echo "No test class matching '$1'. Run ./run.sh with no arguments to see available classes."
  exit 1
fi

echo "Running $MATCH..."
dotnet test \
  --filter "FullyQualifiedName~TestIT.ApiTests.Tests.${MATCH}" \
  --logger "trx;LogFileName=results.trx" \
  --logger "console;verbosity=normal"
