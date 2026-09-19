# Wonrich Processing Service

Independently deployable microservice for dairy processing - storing tanks, mixing tanks, allocations, stages, cooling and product split.

## Local Run (Remote MySQL per new AC - SCRUM-71 + 74)

### Prerequisites
- .NET 10 SDK
- Docker Desktop (for service container, not for MySQL per new AC)
- The shared [`wonrich-infra`](../wonrich-infra) repository cloned next to this one (provides the Kafka broker)
- No secrets in repo - copy template first

### 1. Create local secrets files (gitignored)

**Option A - .env file for Docker (SCRUM-74 new AC):**
```bash
cp .env.example .env
# Edit .env with real remote MySQL host/password from Key Vault / team
# .env is gitignored, .env.example is committed with placeholders
```

**Option B - appsettings.Development.json for dotnet run:**
```bash
cp src/ProcessingService/appsettings.Development.template.json src/ProcessingService/appsettings.Development.json
# Edit if needed - points to remote DB per new AC
# This file is gitignored
```

Required env vars (via .env or appsettings.Development.json):
- `ConnectionStrings__DefaultConnection` - Remote MySQL, e.g. `Server=your-remote-mysql-host;Port=3306;Database=processing;User Id=processing_user;Password=...`
- `Auth__Issuer` - `wonrich-auth`
- `Auth__Audience` - `wonrich-services`
- `Auth__SigningKey` - at least 32 chars, shared with Auth service (must match Auth Service)
- `Kafka__BootstrapServers` - `kafka:9092` inside Docker, `localhost:29092` with `dotnet run`

### 2. MySQL Datastore (SCRUM-71 - Remote per new AC)

- **Staging/Production:** Azure MySQL Flexible Server, database `processing`, user `processing_user` scoped only to this DB, backup daily 7-day retention
- **Local Development:** Connects to **remote** MySQL (per new AC), not containerised. No MySQL container in `docker-compose.yml` per new AC.
- **Old pattern (containerised on 3308) still documented in `docs/database.md` for reference, but new AC is remote.**

Connection string pattern (remote):
```
Server=your-remote-mysql-host;Port=3306;Database=processing;User Id=processing_user;Password=your-secure-password
```

### 3. Apply migrations (Pomelo fork - SCRUM-71 + 57)

EF Core provider: `Microting.EntityFrameworkCore.MySql 10.0.10` (Pomelo fork supporting EF Core 10, official Pomelo 9.0.0 only supports EF Core 9)

Migrations configured independently, initial migration `Initial` creates empty DB with charset utf8mb4.

Auto-applied on startup in Development/Staging (see Program.cs `db.Database.Migrate()` guarded on MySQL provider), or manually:

```bash
dotnet tool restore
dotnet ef database update --project src/ProcessingService
# Down tested: dotnet ef database update 0 --project src/ProcessingService
```

Migrations run cleanly against empty DB (DOD).

### 4. Run service

**Start the shared Kafka broker first** (once per session, from `wonrich-infra`):
```bash
(cd ../wonrich-infra && docker compose up -d)
```

**Via Docker (SCRUM-74 - one command, remote DB):**
```bash
docker compose up -d --build
# Service reachable at http://localhost:5210
```

**Via dotnet (for debugging):**
```bash
dotnet run --project src/ProcessingService --environment Development
```

- Health: http://localhost:5210/health (anonymous, 200 + DB healthy) - SCRUM-56 AC
- Swagger: http://localhost:5210/swagger (Development/Staging only, disabled in prod)
- Metrics: http://localhost:5210/metrics (when SCRUM-90 merged)

### 5. Run tests
```bash
dotnet test
```

### 6. Docker build (SCRUM-74)

```bash
docker build -t processing-service:local -f Dockerfile .
docker compose up -d
# No DB container in compose per new AC
# Config via .env file, .env.example committed, .env gitignored
```

## Solution Structure
- `src/ProcessingService` - Web API, EF Core Pomelo MySQL (Microting fork 10.0.10), JWT auth via shared library
- `tests/ProcessingService.Tests` - Unit + integration tests
- `docs/database.md` - Backup and connection settings (SCRUM-71 AC)
- `docs/docker.md` - Containerisation docs (SCRUM-74 AC)
- `docs/environments.md` - Azure staging/production environments, URLs, config, access (SCRUM-70 AC)
- `docs/ci-cd-pipeline.md` - Build, test, deploy and rollback workflow (SCRUM-72 AC)
- `infra/azure/` - Scripts that create those environments and their databases; nothing in them is secret (SCRUM-70)
- `docs/kafka.md` - Pointer to the shared Kafka documentation in `wonrich-infra` (SCRUM-88)
- `docs/observability.md` - Prometheus, Grafana, Loki, the dashboard and alerts (SCRUM-89 AC)
- `.env.example` - Placeholder env vars committed, `.env` gitignored (SCRUM-74 AC)

## Auth (SCRUM-56 AC)

Tokens issued by Auth Service, validated independently here (no call-out). Shared roles via WonrichRoles. `/health` is anonymous, all other endpoints require Bearer token -> 401 if missing.

User id and role available to endpoint via `HttpContext.User`.

## EF Core Provider (SCRUM-56 AC + SCRUM-71)

Uses **Microting.EntityFrameworkCore.MySql 10.0.10** (Pomelo fork, supports EF Core 10, official Pomelo.EntityFrameworkCore.MySql 9.0.0 only supports EF Core 9, Oracle's MySql.EntityFrameworkCore is NOT used).

Connection via `UseMySql(connectionString, new MySqlServerVersion(new Version(8,4,0)))` (explicit version to avoid AutoDetect needing live connection at design time).

No connection strings in source control - only `appsettings.Development.template.json` and `.env.example` committed with placeholders. Real files gitignored.

## Environment Variables (SCRUM-74 AC: config via env vars matching deployed pattern)

See `.env.example` and `src/ProcessingService/appsettings.Development.template.json` for required keys. In Azure they are App Service application settings, written by `infra/azure/provision.sh` and resolved at runtime — see `docs/environments.md`.

- `ConnectionStrings__DefaultConnection` - Remote MySQL, env var pointing at remote server, no DB container in compose per new AC
- `Auth__Issuer`, `Auth__Audience`, `Auth__SigningKey` - Shared with Auth Service
- `Cors__AllowedOrigins`
- `Kafka__BootstrapServers` - Shared broker address (`kafka:9092` in Docker)

## Compose (SCRUM-74 new AC: no DB container)

`docker-compose.yml` brings up Processing Service with one command, DB connection via env var pointing at remote MySQL, no database container included. Config via `.env` file. It also runs the observability stack (SCRUM-89).

The service container joins two networks:

| Network | Purpose |
| --- | --- |
| `default` | This project's own network, so Prometheus, Loki and Grafana can reach the service |
| `wonrich-net` (external) | The shared network from `wonrich-infra`, where the Kafka broker runs |

`wonrich-net` is created by `wonrich-infra`, so start that first. Otherwise compose fails with `network wonrich-net not found`.

## Deployment (SCRUM-70)

Staging: https://app-wonrich-processing-staging.azurewebsites.net — Production: https://app-wonrich-processing-prod.azurewebsites.net

Both are provisioned from `infra/azure/` and documented in `docs/environments.md`. Deployment is automatic: a push to `develop` goes to staging, a push to `main` goes to production after manual approval - see `docs/ci-cd-pipeline.md`.

## DODs

- **71 DOD:** Service starts connects own DB runs migrations without manual steps (auto-migrate in Program.cs), migrations clean on empty DB, docs merged
- **74 DOD:** Developer confirms runs from clean clone (`cp .env.example .env` + `docker compose up -d`), docs merged
- **72 DOD:** Pipeline green, a failing test proven to block deployment, a rollback executed, workflow documented
- **70 DOD:** Staging test deployment responds on `/health`, production reachable and configured, `docs/environments.md` merged
- **56 DOD:** Dockerfile builds from clean checkout, service registered in compose reachable from gateway, README local run + env vars, /health 200 + DB healthy, auth via shared lib, 401 for unauth except /health, Pomelo provider


## Kafka (SCRUM-88)

The Kafka broker and the topic definitions are **shared by all Wonrich services** and live in the
[`wonrich-infra`](../wonrich-infra) repository. This repository no longer runs its own broker.

| | |
| --- | --- |
| Broker, from your machine | `localhost:29092` |
| Broker, from inside Docker | `kafka:9092` (on the `wonrich-net` network) |
| Topics created by | `wonrich-infra/kafka/create-topics.sh`, run as the `kafka-init` container |
| Topic definitions | `wonrich-infra/kafka/topics.env` |
| Kafka UI | http://localhost:8085 |
| Hosted broker provisioning | `wonrich-infra/azure/eventhubs.sh` (hosting option not yet decided) |

```bash
(cd ../wonrich-infra && docker compose up -d)      # start the shared broker
(cd ../wonrich-infra && docker compose logs kafka-init)   # what was created
docker exec wonrich-kafka /opt/kafka/bin/kafka-topics.sh --bootstrap-server localhost:9092 --list
```

Processing Service topics and consumer groups:

| Topic | Role | Consumer group |
| --- | --- | --- |
| `wonrich.processing.stage-events.v1` | Produces (also consumed by `processing-stage-events`) | – |
| `wonrich.processing.hold-events.v1` | Produces (also consumed by `processing-hold-events`) | – |
| `wonrich.intake.lab-results.v1` | Consumes | `processing-lab-results` |

To add or change a topic or consumer group, open a pull request against `wonrich-infra`
(`kafka/topics.env` and `docs/kafka.md`). Naming convention, retention and connection settings are
in `wonrich-infra/docs/kafka.md`.

> Nothing in the service produces or consumes yet - the `Kafka__*` settings are read by no code
> until the stories that publish stage events and consume lab results land.

## Observability (SCRUM-89)

`docker compose up -d` also brings up Prometheus, Loki, Promtail and Grafana. Everything is
configured from files in `infra/observability/`, so there is nothing to import or click.

| | |
| --- | --- |
| Grafana | http://localhost:3000 - sign in, see below |
| Prometheus | http://localhost:9090 |
| Dashboard | **Wonrich -> Service Overview** - request rate, error rate, response time, availability, logs |

Grafana is authenticated, not open. Set a password in `.env` before first use:

```bash
GRAFANA_ADMIN_USER=admin
GRAFANA_ADMIN_PASSWORD=<not the default>
```

Retention is bounded: Prometheus keeps 7 days or 2 GB, whichever comes first; Loki keeps 7 days.

> The request-rate, error-rate and response-time panels will read **zero** until the service's
> `/metrics` endpoint exports its real meters - it currently returns hardcoded zeros (SCRUM-90's
> `ObservabilityExtensions.cs` says so in a comment). The availability panel and the `ServiceDown`
> alert work regardless, since Prometheus derives those from the scrape itself. See
> `docs/observability.md`.

Full detail, including the hosted Grafana Cloud approach: `docs/observability.md`.
