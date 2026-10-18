# one command for the whole code quality run, made for the live demo
#
# run from anywhere:  ./quality/run-quality.ps1
#
# what it does, in order:
# 1. starts sonarqube in docker if it is not running, and waits until it is ready
# 2. runs the scan: begin, build, test with coverage, end
# 3. waits for the quality gate and prints the result in the terminal
# 4. opens the dashboard in the browser
#
# the exit code is 0 when the gate passes and 1 when it fails, same as the ci job
# the project token comes from the token parameter, or the sonar token environment variable, or secrets.env (git ignores that file)

param(
    [string] $Token = $env:SONAR_TOKEN,
    [string] $HostUrl = "http://localhost:9000",
    [string] $ProjectKey = "wonrich-processing-service",
    # skip the browser, useful when this runs without a screen
    [switch] $NoBrowser
)

$ErrorActionPreference = "Stop"

# always work from the repo root, even if this is started from another folder
Set-Location (Join-Path $PSScriptRoot "..")

# step 0. find the token
if (-not $Token -and (Test-Path "secrets.env")) {
    $Token = [regex]::Match((Get-Content "secrets.env" -Raw), "sqp_[A-Za-z0-9]+").Value
}
if (-not $Token) {
    throw "no token. pass -Token, or set SONAR_TOKEN, or put the sqp_ token in secrets.env"
}

# small helper to read from the sonarqube api (token as user name, empty password)
$auth = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("${Token}:"))
function Get-Sonar([string] $Path) {
    Invoke-RestMethod -Uri "$HostUrl/api/$Path" -Headers @{ Authorization = "Basic $auth" }
}

# stops the script when a dotnet command fails
function Assert-Ok([string] $Step) {
    if ($LASTEXITCODE -ne 0) { throw "$Step failed (exit code $LASTEXITCODE)" }
}

# step 1. sonarqube must be running
Write-Host "== 1. sonarqube" -ForegroundColor Cyan
docker compose -f quality/docker-compose.sonarqube.yml up -d
Assert-Ok "docker compose"

Write-Host "waiting for sonarqube to be ready (1 to 2 minutes if it was stopped)"
$ready = $false
for ($i = 0; $i -lt 60; $i++) {
    try {
        if ((Invoke-RestMethod "$HostUrl/api/system/status").status -eq "UP") { $ready = $true; break }
    } catch { }
    Start-Sleep -Seconds 5
}
if (-not $ready) { throw "sonarqube did not start" }
Write-Host "sonarqube is up"

# step 2. the scan. old test results are deleted first, so old coverage files are not counted
Write-Host "== 2. scan" -ForegroundColor Cyan
if (Test-Path "TestResults") { Remove-Item -Recurse -Force "TestResults" }

dotnet tool restore | Out-Null
Assert-Ok "dotnet tool restore"

# qualitygate.wait makes the end step wait for the gate result, and exit with 1 when it fails
dotnet sonarscanner begin /k:"$ProjectKey" /s:"$PWD/quality/SonarQube.Analysis.xml" `
    /d:sonar.host.url="$HostUrl" /d:sonar.token="$Token" /d:sonar.qualitygate.wait=true
Assert-Ok "scanner begin"

dotnet build ProcessingService.slnx --no-incremental
Assert-Ok "build"

dotnet test ProcessingService.slnx --no-build --settings coverage.runsettings `
    --collect:"XPlat Code Coverage" --results-directory TestResults `
    --logger "trx;LogFileName=test-results.trx"
Assert-Ok "tests"

# the exit code is not checked here on purpose: a failed gate also gives exit code 1, and we still want to print the result
dotnet sonarscanner end /d:sonar.token="$Token"
$scannerExit = $LASTEXITCODE

# step 3. print the result
Write-Host "== 3. result" -ForegroundColor Cyan
$gate = (Get-Sonar "qualitygates/project_status?projectKey=$ProjectKey").projectStatus

foreach ($c in $gate.conditions) {
    $colour = if ($c.status -eq "OK") { "Green" } else { "Red" }
    Write-Host ("  {0,-40} actual {1,-6} limit {2,-5} {3}" -f $c.metricKey, $c.actualValue, $c.errorThreshold, $c.status) -ForegroundColor $colour
}

$keys = "software_quality_security_issues,software_quality_reliability_issues,software_quality_maintainability_issues,coverage,duplicated_lines_density,ncloc,tests"
$m = @{}
foreach ($x in (Get-Sonar "measures/component?component=$ProjectKey&metricKeys=$keys").component.measures) { $m[$x.metric] = $x.value }

Write-Host ""
Write-Host "  security issues          $($m['software_quality_security_issues'])"
Write-Host "  reliability issues       $($m['software_quality_reliability_issues'])"
Write-Host "  maintainability issues   $($m['software_quality_maintainability_issues'])"
Write-Host "  coverage                 $($m['coverage']) %"
Write-Host "  duplications             $($m['duplicated_lines_density']) %"
Write-Host "  lines of code            $($m['ncloc'])"
Write-Host "  tests                    $($m['tests'])"
Write-Host ""

if ($gate.status -eq "OK") {
    Write-Host "QUALITY GATE: PASSED" -ForegroundColor Green
} else {
    Write-Host "QUALITY GATE: FAILED" -ForegroundColor Red
}

# step 4. open the dashboard
if (-not $NoBrowser) { Start-Process "$HostUrl/dashboard?id=$ProjectKey" }

# same exit code as the ci job: 0 passed, 1 failed
if ($gate.status -eq "OK" -and $scannerExit -eq 0) { exit 0 } else { exit 1 }
