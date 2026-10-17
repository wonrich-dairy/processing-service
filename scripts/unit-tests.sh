#!/usr/bin/env bash
# One command for the demo: run the unit tests with coverage and print a coverage summary.
# Usage (from the repo root):  ./scripts/unit-tests.sh
set -euo pipefail
cd "$(dirname "$0")/.."

rm -rf TestResults coverage-report
dotnet test ProcessingService.slnx \
  --settings coverage.runsettings \
  --collect:"XPlat Code Coverage" \
  --results-directory TestResults

# Optional coverage summary (install once: dotnet tool install -g dotnet-reportgenerator-globaltool)
if command -v reportgenerator >/dev/null 2>&1; then
  reportgenerator \
    -reports:"TestResults/**/coverage.cobertura.xml" \
    -targetdir:coverage-report \
    -reporttypes:"TextSummary;Html" \
    -classfilters:"+ProcessingService.Application.*;+ProcessingService.Domain.*" >/dev/null
  cat coverage-report/Summary.txt
else
  echo "Cobertura file(s):"; find TestResults -name 'coverage.cobertura.xml'
  echo "(install reportgenerator for a readable summary)"
fi
