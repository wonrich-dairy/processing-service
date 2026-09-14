# Observability — Prometheus, Grafana and Loki (SCRUM-89)

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

The other two services run in their own compose project on their own network, so a service name
will not resolve; `host.docker.internal` reaches back out to the host, where the root stack
publishes them. They show as **down** until that stack is running. That is accurate rather than
hidden — a target that is absent should look absent.

> ### Known gap: only Processing Service exposes `/metrics`
>
> With the root stack running, Prometheus reaches the intake and auth services and both answer
> **404 Not Found** on `/metrics`. They are up; they simply have no metrics endpoint. Instrumenting
> a service is that service's own work — SCRUM-90 did it for Processing Service — so the scrape
> jobs are configured and ready here, and will start returning data the moment those services
> expose an endpoint. No change is needed on this side when they do.
>
> Until then the AC line "Prometheus scrapes the metrics endpoint of every deployed service" is met
> for the one service that has an endpoint to scrape. The dashboard's `service` variable is driven
> by `label_values(up, service)`, so the other two appear in it automatically once they are
> instrumented.

### Logs

Promtail discovers containers through the Docker socket and ships their stdout to Loki, labelled
with the compose service name. That label matches the `service` label on the metrics, so one
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
| Logs | container logs for the selected services |

Error rate is a proportion rather than a count, because five errors means something very different
at 10 requests/second than at 1000. Response time is percentiles rather than a mean, because an
average hides the slow tail that users actually notice.

It is committed as JSON and provisioned read-only. Editing it in the UI will not write back to the
file, so change the file and restart Grafana — otherwise the next rebuild silently discards the
edit.

> ### Known gap: the panels will read zero
>
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
| `ServiceDown` | a service fails scraping for 2 minutes | **yes** |
| `HighErrorRate` | over 5% of requests error for 5 minutes | not until `/metrics` is real |

Two minutes rather than instantly: a Free-tier App Service cold-starts, and one missed scrape
during a deployment is not an incident.

`HighErrorRate` is committed now so the threshold is agreed and reviewed in the calm, rather than
invented during an incident.

## The hosted stack

**Chosen approach: Grafana Cloud free tier.**

The deployed services need somewhere to send metrics and logs that is not a laptop. The options
were a container platform in Azure, a VM, or a managed service:

| Option | Why not |
|---|---|
| Azure Container Apps | Real credit cost, and needs persistent storage wired up or retention resets on every revision. |
| VM running this compose stack | Identical to local, but the team then owns patching, uptime and disk — and a small VM struggles with four containers. |
| **Grafana Cloud free tier** | **Chosen.** Free allowance covers this system's volume comfortably, nothing to patch, reachable from the App Services, and both halves are configuration rather than infrastructure. |

The trade-offs accepted: it is another account outside Azure, and telemetry leaves the Azure
tenant. Neither matters while no real production data exists; both would need revisiting if it
did.

### Wiring it up

Grafana Cloud gives a Prometheus remote-write endpoint and a Loki push endpoint, each with its own
user id and API token.

Prometheus ships what it scrapes onward — add to `prometheus.yml`:

```yaml
remote_write:
  - url: https://prometheus-<region>.grafana.net/api/prom/push
    basic_auth:
      username: ${GRAFANA_CLOUD_PROM_USER}
      password: ${GRAFANA_CLOUD_TOKEN}
```

Promtail ships logs the same way, by changing its `clients.url` to the Grafana Cloud Loki endpoint.

Both credentials are secrets: they belong in `.env` locally and in App Service settings for a
deployed environment, never in these files.

> **Not yet provisioned.** The account and its tokens have not been created — that is a signup and
> a credential the team owns, not something this ticket could do on its own. Until then the
> "reachable from the deployed services" and "dashboard demonstrated on staging" lines of the AC
> are unmet; everything else in this document is running and verified locally.

## Verifying

```bash
docker compose up -d
curl -s http://localhost:9090/api/v1/targets | grep -o '"health":"[a-z]*"'   # scrape health
curl -s http://localhost:9090/api/v1/rules   | grep -o '"name":"[A-Za-z]*"'  # rules loaded
```

Then open Grafana, sign in, and look at **Wonrich → Service Overview**.
