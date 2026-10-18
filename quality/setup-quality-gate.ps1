# creates our own sonarqube quality gate and uses it for the processing service project
#
# why not the default gate (sonar way): it only checks new code. in ci the sonarqube server is
# fresh every run, so there is never new code and the default gate can never fail.
# this gate checks the overall code, with limits set at todays level, so it fails if quality gets worse
#
# run:  ./quality/setup-quality-gate.ps1 -Token <a sonarqube user token>
# safe to run many times. if the gate is already there, its conditions are made again from this list

param(
    # a user token (squ_...), not the project analysis token. creating gates needs admin rights
    [Parameter(Mandatory = $true)] [string] $Token,
    [string] $HostUrl = "http://localhost:9000",
    [string] $ProjectKey = "wonrich-processing-service",
    [string] $GateName = "Wonrich Processing"
)

$ErrorActionPreference = "Stop"

# sonarqube api login: the token is the username and the password is empty
$auth = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("${Token}:"))
$headers = @{ Authorization = "Basic $auth" }

# small helper so every api call below is one short line
function Invoke-Sonar([string] $Method, [string] $Path) {
    Invoke-RestMethod -Method $Method -Uri "$HostUrl/api/$Path" -Headers $headers
}

# the conditions. op GT means fail if the value is greater than error, LT means fail if less than
# ratings are numbers in the api: 1 = A, 2 = B, 3 = C, 4 = D, 5 = E
$conditions = @(
    @{ metric = "software_quality_security_rating";    op = "GT"; error = "1" }   # security must be A
    @{ metric = "software_quality_reliability_rating"; op = "GT"; error = "3" }   # reliability C or better
    @{ metric = "coverage";                            op = "LT"; error = "50" }  # line and branch coverage at least 50% (today 54.9%)
    @{ metric = "duplicated_lines_density";            op = "GT"; error = "3" }   # less than 3% copied code
)

# 1. create the gate if it is not there yet
$gates = (Invoke-Sonar GET "qualitygates/list").qualitygates
if (-not ($gates | Where-Object { $_.name -eq $GateName })) {
    Invoke-Sonar POST "qualitygates/create?name=$([uri]::EscapeDataString($GateName))" | Out-Null
    Write-Host "created gate '$GateName'"
}
$encodedName = [uri]::EscapeDataString($GateName)

# 2. remove the old conditions, so the gate always matches the list above exactly
$current = (Invoke-Sonar GET "qualitygates/show?name=$encodedName").conditions
foreach ($c in $current) {
    Invoke-Sonar POST "qualitygates/delete_condition?id=$($c.id)" | Out-Null
}

# 3. add the conditions from the list
foreach ($c in $conditions) {
    Invoke-Sonar POST "qualitygates/create_condition?gateName=$encodedName&metric=$($c.metric)&op=$($c.op)&error=$($c.error)" | Out-Null
    Write-Host "  condition: $($c.metric) $($c.op) $($c.error)"
}

# 4. use this gate for our project instead of sonar way
Invoke-Sonar POST "qualitygates/select?gateName=$encodedName&projectKey=$ProjectKey" | Out-Null
Write-Host "project '$ProjectKey' now uses gate '$GateName'"
