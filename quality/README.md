# Code Quality and Metrics (SonarQube)

Individual contribution for SE3022: Code Quality and Metrics.

| | |
|---|---|
| Student | B.G.R.V.Senanayake |
| Student ID | IT24103456 |
| Service under analysis | Processing Service |
| Tools | SonarQube Community 26.9.0.129388, SonarScanner for .NET 12.0.0, Coverlet, Docker, GitHub Actions |

## 1. What this is

This folder holds everything for the static code analysis of the Processing Service. SonarQube reads the source code, checks it against its rules and reports bugs, security problems, code smells, technical debt, test coverage and duplicated code. A quality gate then decides pass or fail, and in the pipeline a failed gate stops the build and the deployment.

SonarQube does not run the service and does not run the tests. It only reads the code. The coverage numbers come from Coverlet, which measures the lines that run while the unit tests run.

## 2. Files

| File | What it does |
|---|---|
| `quality/docker-compose.sonarqube.yml` | Starts SonarQube in Docker on `http://localhost:9000`. The data is kept in Docker volumes, so it stays after a restart |
| `quality/SonarQube.Analysis.xml` | The scanner settings: where the server is, which files are analysed or excluded (the analysis scope), and where the coverage and test result files are |
| `quality/run-quality.ps1` | One command for the whole run: starts SonarQube, scans, waits for the gate and prints the result. See the quick run in section 5 |
| `quality/setup-quality-gate.ps1` | Creates our quality gate **Wonrich Processing** through the SonarQube web API and makes the project use it. Safe to run many times |
| `coverage.runsettings` (repo root) | Tells Coverlet how to measure coverage. It writes cobertura (used by the existing pipeline) and opencover (read by SonarQube) |
| `.config/dotnet-tools.json` (repo root) | Lists the dotnet tools of the repo, now including `dotnet-sonarscanner` 12.0.0 |
| `.github/workflows/ci-cd.yml` (repo root) | The `code-quality` job, which runs all of this in GitHub Actions |

## 3. Analysis scope

Only the service code is judged. These are excluded in `SonarQube.Analysis.xml`:

| Excluded | Why |
|---|---|
| `QA Tests/` | Selenium and Postman tests of the group and their generated reports, not service code |
| `infra/` | Monitoring configs (Prometheus, Grafana), not service code |
| `**/Migrations/` | EF Core generated code |
| `bin/`, `obj/`, `TestResults/`, `coverage-report/`, `quality/` | Build and test output, and this folder itself |

The first scan did not have this scope and showed 202 issues. About 56 of them came from the excluded files. After scoping it showed 146.

## 4. The quality gate

The default gate (Sonar way) only checks new code. In the pipeline SonarQube starts fresh on every run, so there is never any new code and the default gate could never fail. Our gate checks the overall code, with limits set at today's level, so it fails if quality gets worse.

| Condition | Fails when | Value now |
|---|---|---|
| Security rating | worse than A | A |
| Reliability rating | worse than C | C |
| Coverage | below 50% | about 55% |
| Duplicated lines | above 3% | 0.0% |

To make the gate stricter, change the numbers in the `$conditions` list in `setup-quality-gate.ps1` and run the script again.

## 5. Run it on your own PC

Needs Docker Desktop, PowerShell, and the .NET SDK from `global.json`. Run everything from the `processing-service` folder.

**Quick run.** After the project, the token and the gate exist (steps 2 and 3 below), one command does steps 1 and 4 and prints the result:

```powershell
./quality/run-quality.ps1
```

It starts SonarQube if needed, runs the scan, waits for the quality gate, prints the gate conditions and the main metrics, and opens the dashboard. The exit code is 0 when the gate passes and 1 when it fails. The token is read from `-Token`, the `SONAR_TOKEN` variable, or `secrets.env`. The steps below show the same run by hand.

**Step 1. Start SonarQube**

```powershell
docker compose -f quality/docker-compose.sonarqube.yml up -d
```

Wait 1 to 2 minutes, then open `http://localhost:9000`. The first login is `admin` and `admin`, and it asks for a new password. Stop it with the same command but `down` instead of `up -d`. The data is kept.

**Step 2. Create the project and a token** (first time only)

1. Projects, Create Project, Local project.
2. Display name `Processing Service`, project key `wonrich-processing-service`, main branch `develop`, new code definition: use the global setting.
3. Choose Locally, generate a token and copy it. It starts with `sqp_`. It is shown only once.
4. Keep the token out of the repo. A file ignored by git such as `secrets.env` is fine.

**Step 3. Create the quality gate** (first time only)

Creating a gate needs a user token, not the project token. Click your avatar, My Account, Security, then generate a **User Token** (it starts with `squ_`).

```powershell
.\quality\setup-quality-gate.ps1 -Token "squ_your_user_token"
```

If PowerShell blocks the script, run `Set-ExecutionPolicy -Scope CurrentUser RemoteSigned` once.

**Step 4. Run the scan** (every time)

```powershell
Remove-Item -Recurse -Force TestResults -ErrorAction SilentlyContinue
$env:SONAR_TOKEN = "sqp_your_project_token"

dotnet tool restore
dotnet sonarscanner begin /k:"wonrich-processing-service" /s:"$PWD\quality\SonarQube.Analysis.xml" /d:sonar.token="$env:SONAR_TOKEN"
dotnet build ProcessingService.slnx --no-incremental
dotnet test ProcessingService.slnx --no-build --settings coverage.runsettings --collect:"XPlat Code Coverage" --results-directory TestResults --logger "trx;LogFileName=test-results.trx"
dotnet sonarscanner end /d:sonar.token="$env:SONAR_TOKEN"
```

A scan has three parts. `begin` connects to the server and prepares to watch the build. `build` compiles the code, and the rules run while the compiler reads it. `test` runs the tests and writes the coverage file. `end` uploads everything to the server. The tests must run between `begin` and `end`, otherwise coverage shows 0%.

**Step 5. Read the result**

Open `http://localhost:9000/dashboard?id=wonrich-processing-service`. The overview shows the gate result, the three ratings, coverage and duplications. The Activity graph at the bottom keeps the history of every scan.

## 6. Run it in GitHub Actions

The `code-quality` job in `ci-cd.yml` runs on every pull request and every push to `develop` and `main`. It needs no secrets and no cloud account.

1. Starts SonarQube as a container inside the job (fresh on every run).
2. Waits until it is up, creates the project and a token.
3. Runs `setup-quality-gate.ps1` to create the same gate as on the PC.
4. Runs begin, build, test with coverage and end. The setting `sonar.qualitygate.wait=true` makes the end step wait for the gate and fail the job when the gate fails.
5. Writes the gate result and the metrics to the run's Summary page, also when the gate fails.

`deploy-staging` and `deploy-production` both need `code-quality`, so a failed gate means nothing is deployed. The job also runs from Actions, Run workflow (`workflow_dispatch`).

## 7. Results

| Measure | First scan (no scope) | After scope and fixes |
|---|---|---|
| Security | 2 issues, E | 0 issues, A |
| Reliability | 46 issues, D | 27 issues, C |
| Maintainability (code smells) | 156 issues, A | 117 issues, A |
| Coverage | 0.0% | about 55% (81 tests, all passing) |
| Duplications | 0.3% | 0.0% |
| Technical debt | not measured | about 8 hours |

**Fixes made**

| Issue | Rule | Fix | Effect |
|---|---|---|---|
| A database password written in `ProcessingDbContextFactory.cs` (Blocker) | `S6703` | Removed the real server, user and password. The file now only holds a local placeholder, and the real connection comes from the `ConnectionStrings__DefaultConnection` environment variable | Security E to B |
| The image runs as root (Low) | `S6471` | Added `USER app` to the Dockerfile. The base image already has this user | Security B to A |

**Known technical debt, left open on purpose.** The 25 reliability issues of rule `S6964` (API input properties that are not nullable or required), 2 uses of `Migrate` instead of `MigrateAsync`, and 117 code smells. The quality gate makes sure this debt does not grow.

**Testing the gate.** A realistic fake password was added back to `ProcessingDbContextFactory.cs` on a pull request. Security turned E, the gate failed, the pipeline failed and the deployment jobs were skipped. After the password was removed, the next run passed. A placeholder looking password such as `FakeDemoPass123` was not flagged, because SonarQube ignores obvious placeholder values to avoid false alarms.

## 8. Terms

| Term | Meaning |
|---|---|
| Bug | Code that is probably wrong |
| Vulnerability | A security problem |
| Code smell | Code that works but is hard to read or change |
| Technical debt | Estimated time to fix all the code smells |
| Rating (A to E) | One grade per area. For security and reliability the worst single issue decides the grade |
| Coverage | Percentage of code lines that ran during the tests |
| Quality gate | The pass or fail rules checked after each scan |
| Scope | Which files are analysed and which are excluded |

## 9. Problems and fixes

| Problem | Fix |
|---|---|
| The page `localhost:9000` does not open | SonarQube needs 1 to 2 minutes to start. Check `docker ps` for `(healthy)` |
| Coverage shows 0% | The tests did not run between `begin` and `end`, or the old `TestResults` folder was not deleted |
| `Unrecognized command line argument` in Git Bash | Git Bash changes the `/k:` arguments. Use PowerShell, or run `export MSYS_NO_PATHCONV=1` first |
| `Insufficient privileges` from the gate script | Use a User Token (`squ_`), not the project token (`sqp_`) |
| Warning about missing blame information | Harmless. Git has no history yet for files that are not committed |
| Docker says it is unable to start | Do not run two SonarQube containers together (each needs about 2 GB). Quit Docker Desktop, run `wsl --shutdown` and start it again. The data is safe in the volumes |
