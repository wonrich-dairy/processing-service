# Performance testing (JMeter)

A JMeter load test runs against staging after every deployment, so NFR1 ("the system responds within 2 seconds") is measured on each release instead of assumed (SE3112, Load & Performance Testing).

| | |
|---|---|
| Test plan | `perf/processing.jmx` |
| Runner | `perf/run.sh <load\|stress\|spike>`, the same script locally and in CI |
| Workflow | `.github/workflows/performance.yml` |
| Runs | after every staging deploy (the `performance` job in `ci-cd.yml`), and by hand |
| Result | pass / fail on p95 latency and error rate, a per-phase table on the run page, an HTML report artifact |

---

## What it tests

Each virtual user is a processing technician working the tanks screen. Before each request it pauses 1.0–1.5 s, then it repeats this loop:

| Request | Endpoint | Kind |
|---|---|---|
| `read: active tanks` | `GET /api/tanks?activeOnly=true` | read |
| `read: active stages` | `GET /api/processing-stages/active` | read |
| `write: log tank temperature` | `POST /api/tanks/{ST-99}/temperature-logs` | write |
| `read: last temperature` | `GET /api/tanks/{ST-99}/temperature-logs/last` | read |
| `read: processing runs` | `GET /api/processing-runs` | read |

That is four reads to each write, because technicians look far more often than they record. Every request carries a bearer token from the auth service and an `X-Correlation-ID` of `jmeter-<uuid>`, so load-test traffic is easy to find in the logs.

### The performance tank (ST-99)

Writes go to a tank reserved for this test, so real tanks never receive load-test readings.

1. **Setup:** sign in. Find ST-99, or create it if it doesn't exist (Storing, 1000 kg). Set it to *Under maintenance*. Under maintenance, ST-99 drops out of the active-tank lists technicians choose from, but it still accepts temperature logs.
2. **Load phases:** every temperature log goes to ST-99, with the note `PERF-JMETER run <run>`.
3. **Teardown:** delete ST-99. `tank_temperature_logs` cascades on delete, so every log the run wrote is removed with it. Each run leaves staging as it found it.

If a run is killed before teardown, the next run reuses the leftover ST-99 and deletes it at the end. To remove it by hand:

```sql
DELETE FROM tanks WHERE Code = 'ST-99';   -- its temperature logs cascade
```

**Do not use ST-99 for a real tank.**

Setup and teardown samplers are labelled `setup:` and `teardown:`. They are filtered out of the report and never count towards the thresholds.

---

## Load profiles

Choose one with the **`profile`** input (`load` is the default, and it is what runs after a deploy). The plan has three thread groups that run one after another, and `perf/run.sh` sets the users, ramp-up and duration of each.

| Profile | Shape | Question it answers | Default length |
|---|---|---|---|
| `load` | 10 users, steady | Does the service meet NFR1 at normal peak load? | 30 s ramp + 120 s |
| `stress` | 10 → 25 → 50 users | At what load does it stop meeting NFR1? | 3 × `step_seconds` (60 s) |
| `spike` | 5 → 50 → 5 users, 2 s ramp into the spike | Does it survive a sudden burst, and recover after it? | 3 × `step_seconds` (30 s) |

| Setting | Default | Input |
|---|---|---|
| Users, ramp-up, duration (`load` only) | 10, 30 s, 120 s | `users`, `ramp_seconds`, `duration_seconds` |
| Length of each step (`stress`, `spike`) | 60 s / 30 s | `step_seconds` |
| Pause between a user's requests | 1000 ms + up to 500 ms random | `think_ms` |

`stress` and `spike` deliberately go past what the Free-tier App Service can handle, so they are expected to breach the thresholds. The point is to find out at which step that happens. Run them by hand, not after every deploy.

### Per-phase results

The HTML report aggregates the whole run. After every run, `perf/phase_summary.py` adds a table to the run summary with one row per phase:

| Phase | Peak users | Requests | Req/s | p50 (ms) | p95 (ms) | Max (ms) | Errors |
|---|---:|---:|---:|---:|---:|---:|---:|

Read it top to bottom. The step where p95 or the error rate jumps is the service's current limit. If users go up but req/s stops rising, the service is saturated.

## Thresholds

| Metric | Fails when | Input |
|---|---|---|
| p95 latency, all measured requests | above **2000 ms** (NFR1) | `p95_ms` |
| Error rate (any non-2xx response) | above **1%** | `max_error_pct` |

p95 is used rather than the average because an average hides the slow tail that users notice. If a threshold is breached, the job fails with an error annotation that names the metric, the value and the threshold.

---

## Configuration (staging environment)

| Name | Kind | Value |
|---|---|---|
| `PERF_USERNAME` | secret | a staging user with the **ProcessingTechnician** role |
| `PERF_PASSWORD` | secret | its password |
| `AUTH_HOST` | variable | the auth service's staging host, without `https://` |
| `PERF_TARGET_HOST` | variable, optional | defaults to `app-wonrich-processing-staging.azurewebsites.net` |

## Running it

**After every staging deploy:** the `performance` job in `ci-cd.yml` runs automatically.

**By hand:** Actions → **Performance test (JMeter)** → Run workflow → choose a profile. From the CLI:

```bash
gh workflow run performance.yml -f profile=stress
```

**From your own machine**, with JMeter 5.6.3 and Python 3 installed:

```bash
read -rs PERF_PASSWORD; export PERF_PASSWORD      # keeps the password out of shell history
HOST=app-wonrich-processing-staging.azurewebsites.net AUTH_HOST=<auth host> \
PERF_USERNAME=perf-processing STEP_SECONDS=20 perf/run.sh spike     # or load / stress
unset PERF_PASSWORD
```

The report lands in `results/report/index.html`. Add `P95_MS=2000 MAX_ERROR_PCT=1` to also check the thresholds. To run against a local service, add `-Jprotocol=http -Jport=5210 -Jauth_protocol=http -Jauth_port=<auth port>` after the profile.

`jmeter -t perf/processing.jmx`, without `-n`, opens the plan in the JMeter GUI.

## Reading the report

**Quick look:** the run's summary page has the per-phase table and a per-request table (samples, p95, errors), judged against the thresholds.

**Full report:** download the **`jmeter-report`** artifact, unzip it and open `report/index.html`.

| Where | What it tells you |
|---|---|
| **Statistics** table | Per request: samples, average, p95, max, error %. Look for the request with the highest p95 |
| **Errors** table | Which requests failed, and with which status code |
| **Response Times Over Time** | Latency during the run. A climb at each step shows the service struggling as users increase; a spike only at the start is a cold start |
| **Transactions Per Second** | Should rise with each step. Where it flattens while latency climbs is the limit |
| `jmeter.log` | Setup problems: sign-in failure, or ST-99 not created or deleted |

### What to watch on this service

`GET /api/processing-runs` and `GET /api/processing-stages/active` return every matching row, with related entities loaded and no paging. Their cost grows with the amount of data in staging, so they are the first places to look if p95 rises between runs.
