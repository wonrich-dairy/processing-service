# Observability — Prometheus, Grafana and Loki (SCRUM-89, SCRUM-111)

Metrics in Prometheus, logs in Loki, both viewed in Grafana. Everything is configured from files in
[`infra/observability/`](../infra/observability/), never through a UI, so a rebuilt container comes
back identical and a change to what the team is alerted on arrives through code review.

This ticket deploys and configures the stack. The metrics and structured logs it reads were built
in SCRUM-90.

## Running it locally

```bash
docker compose up -d
```

| | URL | Notes |
|---|---|---|
| Grafana | http://localhost:3000 | login required — see below |
| Prometheus | http://localhost:9090 | targets at `/targets`, rules at `/rules` |
| Loki | not published | reachable only inside the compose network |
| Promtail | not published | ships container logs into Loki |

The dashboard is **Wonrich → Service Overview**, provisioned automatically; there is nothing to
import.

### Signing in

Grafana is authenticated, not open: anonymous access is off and sign-up is disabled, so the admin
credential is the only way in.

```bash
# in .env
GRAFANA_ADMIN_USER=admin
GRAFANA_ADMIN_PASSWORD=<something that is not the default>
```

The compose default (`wonrich-local-only`) exists so a clean clone starts, not because it is safe.

Loki is deliberately **not** published to the host. It runs with `auth_enabled: false`, so anything
that can reach it can read every log line; Grafana talks to it inside the compose network, which is
all that is needed.

## What is collected

### Metrics

Prometheus scrapes `/metrics` every 15 seconds:

| Job | Target | Reachable when |
|---|---|---|
| `processing-service` | `processing-service:8080` | this stack is up |
| `intake-service` | `host.docker.internal:5237` | the root workspace stack is up |
| `auth-service` | `host.docker.internal:5238` | the root workspace stack is up |
| `quality-lab-service` | `host.docker.internal:5003` | its own compose stack is up (SCRUM-111) |

Every target carries two labels, set on the scrape job: `service` (the compose service name, so
metrics and logs line up) and `environment` (`local` here, `staging` on the staging stack).

The other two services run in their own compose project on their own network, so a service name
will not resolve; `host.docker.internal` reaches back out to the host, where the root stack
publishes them. They show as **down** until that stack is running. That is accurate rather than
hidden — a target that is absent should look absent.

> ### Known gap: intake and auth services do not expose `/metrics`
>
> Processing Service and Quality Lab Service expose `/metrics`. With the root stack running,
> Prometheus reaches the intake and auth services and both answer
> **404 Not Found** on `/metrics`. They are up; they simply have no metrics endpoint. Instrumenting
> a service is that service's own work — SCRUM-90 did it for Processing Service — so the scrape
> jobs are configured and ready here, and will start returning data the moment those services
> expose an endpoint. No change is needed on this side when they do.
>
> Until then the AC line "Prometheus scrapes the metrics endpoint of every deployed service" is met
> for the one service that has an endpoint to scrape. The dashboard's `service` variable is driven
> by `label_values(up, service)`, so the other two appear in it automatically once they are
> instrumented.

### Metric naming convention

Request metrics are named `<service>_http_*`, with the labels `method`, `endpoint` and `status`:

| Metric | Type |
|---|---|
| `<service>_http_requests_total` | counter |
| `<service>_http_request_errors_total` | counter |
| `<service>_http_request_duration_seconds` | histogram |

The dashboard and the `HighErrorRate` alert select these **by name pattern**
(`{__name__=~".+_http_requests_total"}`), so a service that follows the convention appears on the
dashboard and is covered by the alert with no change here. `processing_http_*` and
`quality_lab_http_*` both match.

Quality Lab Service counts only **5xx** responses as errors: a 4xx is the caller's mistake, not
the service failing. It also labels requests by route template (`/api/batches/{id}`) rather than
raw path, so batch IDs cannot create unbounded series, and does not count Prometheus's own scrapes.

### Logs

Promtail discovers containers through the Docker socket and ships their stdout to Loki, labelled
with the compose service name and `environment="local"`. That label matches the `service` label on the metrics, so one
variable in Grafana drives both halves of the dashboard.

The observability stack's own containers are excluded. Loki logging about ingesting Loki's logs is
a loop that stops only when the disk does.

## The dashboard

[`wonrich-services.json`](../infra/observability/grafana/dashboards/wonrich-services.json), five
panels:

| Panel | Shows |
|---|---|
| Service availability | `up` per service — green/red |
| Request rate | requests per second, by service |
| Error rate | errors as a **percentage** of requests |
| Response time | p50, p95 and p99 |
| Lab determinations | Quality Lab pass / fail counts over the selected range (SCRUM-111) |
| Stage events received | Processing events arriving at Quality Lab over Kafka, by type (SCRUM-111) |
| Logs | container logs for the selected services |

Error rate is a proportion rather than a count, because five errors means something very different
at 10 requests/second than at 1000. Response time is percentiles rather than a mean, because an
average hides the slow tail that users actually notice.

It is committed as JSON and provisioned read-only. Editing it in the UI will not write back to the
file, so change the file and restart Grafana — otherwise the next rebuild silently discards the
edit.

> ### Known gap: Processing Service's request panels read zero
>
> Quality Lab Service exports real values (prometheus-net). Processing Service's
> `/metrics` currently returns hardcoded zeros. The endpoint, the counters and the middleware that
> records them were built in SCRUM-90, but the endpoint does not yet read the meters — the code
> says so itself: *"In real implementation, use MeterListener to collect actual values"*.
>
> So the request-rate, error-rate and response-time panels render correctly and stay flat at zero,
> and the `HighErrorRate` alert cannot fire. The availability panel and the `ServiceDown` alert are
> unaffected, because `up` is synthesised by Prometheus from whether the scrape succeeded and needs
> nothing from the application.
>
> Fixing it means exporting the existing `Wonrich.ProcessingService` meter properly — an
> `OpenTelemetry.Exporter.Prometheus.AspNetCore` endpoint would do it without changing any of the
> instrumentation SCRUM-90 already wrote. That is a change to the processing service's application
> code and belongs to whoever owns SCRUM-90, not to this ticket.

## Retention

Bounded, so disk usage stays flat through to the final evaluation.

| | Retention | Enforced by |
|---|---|---|
| Prometheus | 7 days **or** 2 GB, whichever comes first | `--storage.tsdb.retention.time` / `.size` |
| Loki | 7 days (`168h`) | `limits_config.retention_period` + the compactor |

Both bounds on Prometheus matter: a sudden burst of new series can fill a disk long before seven
days are up. On Loki, `retention_period` without `compactor.retention_enabled: true` is advisory —
chunks stay on disk until something deletes them, and nothing does.

The two windows match on purpose. Logs and metrics covering different periods makes correlating an
incident harder than it needs to be.

## Alerts

In [`alerts.yml`](../infra/observability/prometheus/alerts.yml), committed rather than clicked into
the UI.

| Alert | Fires when | Works today |
|---|---|---|
| `ServiceDown` | a service fails scraping for 2 minutes | **yes**, for every service |
| `HighErrorRate` | over 5% of requests error for 5 minutes | **yes** for Quality Lab; not for Processing until its `/metrics` is real |

Both select services by label and metric-name pattern, not by a list of jobs, so a new service is
covered as soon as it has a scrape job (SCRUM-111). `alerts.test.yml` unit-tests both rules and the
dashboard queries:

```bash
docker run --rm -v "$PWD/infra/observability/prometheus:/p" --entrypoint promtool \
  prom/prometheus:v3.1.0 test rules /p/alerts.test.yml
```

Two minutes rather than instantly: a Free-tier App Service cold-starts, and one missed scrape
during a deployment is not an incident.

`HighErrorRate` is committed now so the threshold is agreed and reviewed in the calm, rather than
invented during an incident.

## The staging stack

**Prometheus and Grafana run on the shared Kafka VM** (SCRUM-111), scraping the App Services'
`/metrics` over HTTPS.

SCRUM-89 chose Grafana Cloud, but the account and tokens were never created, so nothing scraped
staging. When SCRUM-111 needed staging data, the VM already existed for the Kafka broker, with
memory to spare (about 2.7 GB free). Running the same containers there keeps staging identical to
local: the same alert rules, the same dashboard file, and no second account or telemetry leaving
Azure. The VM is deleted after the final evaluation, and the stack with it.

| | |
|---|---|
| Where | `~/observability` on the Kafka VM, compose project `wonrich-observability` |
| Scrapes | each App Service at `https://<host>/metrics`, labelled `environment="staging"` |
| Alert rules, dashboard | the same files as local, copied by the deploy script |
| Exposure | **none**: Prometheus and Grafana listen on the VM's `127.0.0.1` only; reach them through SSH |
| Grafana login | `admin`, password generated on the VM into `~/observability/.env`, never committed |
| Retention | 7 days or 1 GB; memory capped at 512 MB (Prometheus) and 256 MB (Grafana) beside the broker |

### Deploying or updating

```bash
./infra/observability/staging/deploy.sh wonrich-kafka.southeastasia.cloudapp.azure.com \
    quality-lab-service=<quality-lab app host> \
    processing-service=<processing app host>
```

Each `service=host` becomes one scrape job. Re-run it to add a service or to ship changed alerts or
dashboards; data and the Grafana password are kept.

### Opening it

```bash
ssh -N -L 13000:localhost:3000 -L 19090:localhost:9090 azureuser@wonrich-kafka.southeastasia.cloudapp.azure.com
```

Grafana at http://localhost:13000, Prometheus targets at http://localhost:19090/targets. The local
ports differ from the local stack's 3000 / 9090 so both can be open at once.

### Limits

- **No staging logs in Loki.** App Service container logs are not reachable from the VM; read them in
  the App Service log stream. The dashboard's Logs panel shows "data source not found" on staging.
- **`/metrics` is public** on the App Services, as it is for Processing Service. It exposes request
  counts and latencies, no business data.
- **Alerts show in Prometheus (`/alerts`) but notify nobody**: there is no Alertmanager, locally or on
  staging.

## Tracing a request across the Kafka hop

One correlation ID should follow a request from the HTTP call into Processing Service to its
arrival in Quality Lab Service (SCRUM-111):

1. Processing Service reads or creates `X-Correlation-ID` and logs the request with it (SCRUM-90).
2. The event it writes to the outbox carries an ID in the Kafka header `x-correlation-id`, and the
   relay logs the publish with it (SCRUM-68).
3. Quality Lab Service's stage-event listener logs each received event under that header's ID.

> ### Known gap: step 1 and step 2 use different IDs
>
> Processing's services create a **new** ID for each event
> (`var correlationId = Guid.NewGuid().ToString();` in `TankAllocationService`,
> `ProcessingStageService` and `MockQualityTestClient`) instead of reusing the request's. So a
> search by the request's ID finds Processing's HTTP lines only, and a search by the event's ID
> finds Processing's publish line and Quality Lab's receive line.
>
> The fix is in Processing Service: take the request's ID where one exists,
> `KafkaCorrelationHelper.GetCurrentCorrelationId(httpContextAccessor.HttpContext) ?? Guid.NewGuid().ToString("N")`.
> The helper and `IHttpContextAccessor` are already registered (SCRUM-90); they are not yet used.

In Grafana → Explore → Loki:

```logql
{job="docker"} |= "<correlation id>"
```

The listener is passive: it never commits offsets, so the consumer that will act on these events
later still receives every retained event.

## Verifying

```bash
docker compose up -d
curl -s http://localhost:9090/api/v1/targets | grep -o '"health":"[a-z]*"'   # scrape health
curl -s http://localhost:9090/api/v1/rules   | grep -o '"name":"[A-Za-z]*"'  # rules loaded
```

Then open Grafana, sign in, and look at **Wonrich → Service Overview**.

### SCRUM-111 evidence

| DoD item | How to show it |
|---|---|
| Quality Lab `up` locally | http://localhost:9090/targets: `quality-lab-service` **UP** |
| Quality Lab `up` on staging | through the tunnel, http://localhost:19090/targets: `quality-lab-service` **UP** |
| Dashboard shows live staging data | staging Grafana → Service Overview: request rate and latency for `quality-lab-service` move after a few requests |
| Correlation across the Kafka hop | local Loki query above, returning lines from both services |
