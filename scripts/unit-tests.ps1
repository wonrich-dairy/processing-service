# One command for the demo (Windows): run the unit tests with coverage and print a coverage summary.
# Usage (from the repo root):  ./scripts/unit-tests.ps1
$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")

Remove-Item -Recurse -Force TestResults, coverage-report -ErrorAction SilentlyContinue
dotnet test ProcessingService.slnx `
  --settings coverage.runsettings `
  --collect:"XPlat Code Coverage" `
  --results-directory TestResults
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Optional coverage summary (install once: dotnet tool install -g dotnet-reportgenerator-globaltool)
if (Get-Command reportgenerator -ErrorAction SilentlyContinue) {
  reportgenerator `
    "-reports:TestResults/**/coverage.cobertura.xml" `
    "-targetdir:coverage-report" `
    "-reporttypes:TextSummary;Html" `
    "-classfilters:+ProcessingService.Application.*;+ProcessingService.Domain.*" | Out-Null
  Get-Content coverage-report/Summary.txt
} else {
  Get-ChildItem -Recurse TestResults -Filter coverage.cobertura.xml | ForEach-Object FullName
  Write-Host "(install reportgenerator for a readable summary)"
}
