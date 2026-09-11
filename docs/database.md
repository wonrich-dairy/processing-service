# MySQL Database — Processing Service

## Overview

Processing Service owns its own MySQL database, separate from the intake and auth services
(SCRUM-71). It shares the Azure MySQL Flexible Server instance (`mcc-db`) with the other services,
but not a schema: the service connects with an account that can reach its own database and nothing
else, so it can evolve its schema and deploy without coordinating with the other services.

## Where the local database comes from

Local development runs against a containerised MySQL. There are two ways to start one, and they
serve the same database on the same port, so pick whichever suits what you are doing:

| | Command | Brings up |
| --- | --- | --- |
| This repository | `docker compose up --build` | Processing Service and its MySQL (SCRUM-74) |
| Root workspace | `docker compose up --build` in the wonrich workspace | Every service together, for a demo or cross-service work |
| Database only | `docker compose up processing-db` | Just MySQL, for running the service with `dotnet run` |

Both expose MySQL on host port **3307** with the same database name and credentials, so
`appsettings.Development.template.json` works against either without editing. Neither is a
prerequisite of the other; they share host ports, so run one at a time.

> There is no MySQL on your machine unless one of these is running. `dotnet run` on its own will
> fail with `Unable to connect to any of the specified MySQL hosts` — start the database first.

## Database instances

| Property | Local (Docker) | Staging (Azure) | Production (Azure) |
|---|---|---|---|
| **Host** | `localhost` | `mcc-db.mysql.database.azure.com` | `mcc-db.mysql.database.azure.com` |
| **Port** | `3307` (host) / `3306` (container) | `3306` | `3306` |
| **Database** | `wonrich_processing` | `processingdb` | `processingdb_prod` |
| **User** | `processing_user` | `processing_app` | `processing_app_prod` |
| **SSL** | Not required | Required | Required |
| **Server** | MySQL 8.0.45 (Docker) | Azure MySQL Flexible Server | Azure MySQL Flexible Server |
| **Collation** | `utf8mb4_0900_ai_ci` | `utf8mb4_0900_ai_ci` | `utf8mb4_0900_ai_ci` |

**Staging and production hold separate accounts, not one account with rights over both.** A single
credential spanning the two would mean a leaked staging password reaching production data;
`processing_app` and `processing_app_prod` can each reach one database and nothing else.

> Both Azure databases are empty until something deploys against them and the migrations run.
> Wiring the App Services up to them is SCRUM-72's work, not this ticket's.

## Connection strings

Connection strings are never committed. Locally they come from the root `docker-compose.yml` and
`.env`; on Azure they are App Service application settings
(`ConnectionStrings__DefaultConnection`).

### Local (from the host, e.g. Workbench or `dotnet ef`)
```
Server=localhost;Port=3307;Database=wonrich_processing;User=processing_user;Password=ProcessingDevPassword123!
```

### Local (from inside the compose network)
```
Server=mysql;Port=3306;Database=wonrich_processing;User=processing_user;Password=ProcessingDevPassword123!
```
> `mysql` is the compose service name and resolves only within that network. The `3307` host
> mapping is irrelevant there.

### Staging (Azure)
```
Server=mcc-db.mysql.database.azure.com;Port=3306;Database=processingdb;User Id=processing_app;Password=<from App Service settings>;SslMode=Required
```

### Production (Azure)
```
Server=mcc-db.mysql.database.azure.com;Port=3306;Database=processingdb_prod;User Id=processing_app_prod;Password=<from App Service settings>;SslMode=Required
```

## Database accounts

`processing_user` (local), `processing_app` (staging) and `processing_app_prod` (production) each
hold privileges on one processing database and nothing else. That scoping is the point of the
ticket, not a formality: the auth database stores password hashes, and a shared account would let
any compromised service read them — which is exactly what `mcc_user` could do before this work.

Verify the scoping with a negative test rather than by reading the grant table — connect as the
service account and confirm another schema is refused:

```sql
SHOW GRANTS FOR 'processing_app'@'%';   -- expect USAGE on *.* plus ALL PRIVILEGES on processingdb only
SELECT COUNT(*) FROM mccdb.users;       -- expect: SELECT command denied
```

The grants are created locally by `docker/mysql-init/02-create-service-users.sql`, which the MySQL
image runs only when the data volume is first initialised. An existing volume must be recreated for
changes to take effect:

```bash
docker compose down -v && docker compose up --build
```

## Entity Framework migrations

The service owns its migration history. `processingdb` has its own `__EFMigrationsHistory`
containing only this service's migrations — unlike `mccdb`, where the intake and auth services
share one history table.

### Current migrations

| Migration | Creates |
|---|---|
| `20260904060405_AddProcessingTanksAndUnloads` | `processing_tanks`, `unloads` |

### Adding a migration
```bash
dotnet ef migrations add <Name> --project src/ProcessingService/ProcessingService.csproj
```

### Applying manually
```bash
dotnet ef database update --project src/ProcessingService/ProcessingService.csproj
```

### On startup
Pending migrations are applied automatically at startup in every environment except Production, so
a freshly provisioned database builds its own schema on first start with no manual step. The call is
guarded on the provider, because the migrations are MySQL-specific and the tests host the same
pipeline over a different provider.

Production does not auto-migrate: migrations are applied deliberately, before the deployment that
depends on them.

### Rollback limitation
EF Core applies forward migrations only; there are no generated down-migrations. Rolling code back
does not roll the schema back, so a rollback past a migration runs older code against a newer
schema. Keep migrations backward-compatible — add columns rather than renaming or dropping them —
so a rollback stays safe.

## Health check

`GET /health` is anonymous and reports database reachability, because a process that is running but
cannot reach its data is not ready to serve. Use it to confirm a deployment picked up its
connection string:

```bash
curl -s http://localhost:5239/health     # local
```

## Backups

| Environment | Method |
|---|---|
| Local | Disposable — recreated from migrations and the init scripts on `docker compose up` |
| Azure | Azure Flexible Server automated backups, managed on the shared `mcc-db` server |

Azure's default retention applies to the whole server, so `processingdb` is covered by the same
policy as `mccdb`. A restore is server-wide rather than per-database; restoring processing data
alone means restoring to a new server and copying the schema across.
