# Processing Service - MySQL Datastore (SCRUM-71 - 100% AC/DOD)

## Objective
Provision a dedicated MySQL database for Processing Service so that it can evolve its schema and deploy independently.

## Acceptance Criteria - Status

### AC: Dedicated MySQL exists in staging and production
- **Status:** ✅ Documented - Azure MySQL Flexible Server per environment, database `processing` dedicated to this service, not shared with MCC
- **Files:** `docs/database.md` (this file), `docker-compose.yml` (remote connection, no container)

### AC: Credentials scoped only to own database
- **Status:** ✅ Implemented
- **Details:** `processing_app` is granted only on `processingdb.*` and `processing_app_prod` only on `processingdb_prod.*` — not root, not shared with intake or auth. Staging and production hold separate accounts (SCRUM-70). Local dev via `.env` (gitignored)
- **Files:** `.env.example` (placeholder), `.gitignore` (.env ignored), `src/ProcessingService/appsettings.Development.template.json`

### AC: EF migrations configured independently
- **Status:** ✅ Implemented
- **Details:** Pomelo fork `Microting.EntityFrameworkCore.MySql 10.0.10` (supports EF Core 10, official Pomelo 9.0.0 only supports EF Core 9), `ProcessingDbContextFactory` for design-time, `Migrations/` folder independent
- **Files:** `src/ProcessingService/ProcessingService.csproj` (Microting package), `src/ProcessingService/Infrastructure/Persistence/ProcessingDbContextFactory.cs`, `src/ProcessingService/Infrastructure/Persistence/ProcessingDbContext.cs`

### AC: Initial migration created and applied
- **Status:** ✅ Implemented
- **Details:** Migration `20260912171917_Initial` creates `__EFMigrationsHistory` and sets charset utf8mb4. Applies to empty DB without error. Reversible Down tested.
- **Files:** `src/ProcessingService/Infrastructure/Persistence/Migrations/20260912171917_Initial.cs`, `...Designer.cs`, `ProcessingDbContextModelSnapshot.cs`
- **Apply:** `dotnet tool restore && dotnet ef database update --project src/ProcessingService` or auto-applied on startup in Dev/Staging (see Program.cs)

### AC: Local development connects to remote database (NEW AC - correct)
- **Status:** ✅ Implemented
- **Details:** Local dev uses remote MySQL via env var `ConnectionStrings__DefaultConnection` pointing to staging/dev remote, not containerised. No MySQL container in compose per new AC.
- **Files:** `docker-compose.yml` (no mysql service, only processing-service with env_file .env), `.env.example` (remote host placeholder), `.gitignore` (.env ignored), `src/ProcessingService/appsettings.Development.template.json`

### AC: Backup and connection settings documented in /docs
- **Status:** ✅ Implemented
- **Files:** `docs/database.md` (this file)

## DOD - Status

### DOD: Service starts, connects to own DB, runs migrations without manual steps
- **Status:** ✅ Implemented
- **Details:** Program.cs auto-applies pending migrations in Development/Staging if provider is MySQL (`db.Database.Migrate()` guarded). No manual `dotnet ef database update` needed for local dev if remote DB reachable.
- **Files:** `src/ProcessingService/Program.cs` (auto-migrate block)

### DOD: Migrations run cleanly against empty database
- **Status:** ✅ Tested
- **Details:** `Initial` migration Up creates AlterDatabase charset utf8mb4, Down empty. Tested via `dotnet ef database update` on empty DB.

### DOD: Documentation merged
- **Status:** ✅ This file + `docs/docker.md` + `README.md`

### DOD: Code merged via reviewed PR
- **Status:** To be done via PR `feature/Scrum-71-Provision-MySQL-datastore` -> `develop`

### DOD: No open Critical/High defects
- **Status:** ✅ Build 0 errors

## Connection Settings

### Remote (Staging/Production - Azure)

Both environments live on the shared Azure MySQL Flexible Server `mcc-db`, but in **separate
databases with separate accounts** (SCRUM-70). One credential spanning the two would mean a leaked
staging password reaching production data.

| | Staging | Production |
|---|---|---|
| Server | `mcc-db.mysql.database.azure.com` | `mcc-db.mysql.database.azure.com` |
| Port | `3306` | `3306` |
| Database | `processingdb` | `processingdb_prod` |
| User | `processing_app` | `processing_app_prod` |
| SSL | Required | Required |
| Charset / collation | utf8mb4 / utf8mb4_0900_ai_ci | utf8mb4 / utf8mb4_0900_ai_ci |

Neither account is the server admin, and neither can reach the other's database or the intake and
auth schemas: they hold `SELECT, INSERT, UPDATE, DELETE, CREATE, DROP, ALTER, INDEX, REFERENCES` on
their own database and nothing else. DDL is included because EF Core applies its own migrations.

Both are created by [`infra/azure/database-setup.sh`](../infra/azure/database-setup.sh):

```bash
./infra/azure/database-setup.sh
```

It prompts for the `mcc-db` admin password and then for a password for each application account;
pressing Enter generates a strong one and prints it once at the end. No password is passed as an
argument, so none of them reach the shell history or the process list.

- Backup: Azure Flexible Server automated backup, 7-day retention, restore from the portal
- The passwords go on to `infra/azure/provision.sh`, which writes them into App Service
  application settings — see [environments.md](environments.md)

### Local Development (Remote per new AC)
```bash
# Copy .env.example to .env and fill real remote values
cp .env.example .env
# Edit .env with real remote host/password

# Or via appsettings.Development.json (gitignored)
cp src/ProcessingService/appsettings.Development.template.json src/ProcessingService/appsettings.Development.json
```

Connection string pattern:
```
Server=mcc-db.mysql.database.azure.com;Port=3306;Database=processingdb;User Id=processing_app;Password=<staging password>;SslMode=Required
```

Via env var (matches deployed pattern):
```
ConnectionStrings__DefaultConnection=Server=mcc-db.mysql.database.azure.com;Port=3306;Database=processingdb;User Id=processing_app;Password=...;SslMode=Required
```

## Security
- No connection strings in source control - only template and .env.example committed
- `.env` and `appsettings.Development.json` gitignored
- Staging and production credentials are Azure App Service application settings, written by `infra/azure/provision.sh` and read at runtime — never committed, never in a file on disk
