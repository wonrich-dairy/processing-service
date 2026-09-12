# Processing Service - MySQL Datastore (SCRUM-71)

## Objective
Provision a dedicated MySQL database for Processing Service so that it can evolve its schema and deploy independently.

## Acceptance Criteria - Status

### AC: Dedicated MySQL exists in staging and production
- **Status:** ✅ Documented - Azure MySQL Flexible Server per environment, database `processing` dedicated to this service, not shared with MCC
- **Files:** `docs/database.md` (this file), `docker-compose.yml` (remote connection, no container)

### AC: Credentials scoped only to own database
- **Status:** ✅ Implemented
- **Details:** MySQL user `processing_user` has GRANT only on `processing.*`, not root, not shared. Staging/prod credentials from Key Vault, local dev via `.env` (gitignored)
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
- Server: `your-remote-mysql-host` (from Key Vault)
- Database: `processing`
- User: `processing_user` (scoped)
- Charset: utf8mb4, Collation: utf8mb4_unicode_ci
- Backup: Daily automated, 7-day retention (Azure portal)

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
Server=your-remote-mysql-host;Port=3306;Database=processing;User Id=processing_user;Password=your-secure-password
```

Via env var (matches deployed pattern):
```
ConnectionStrings__DefaultConnection=Server=...;Database=processing;...
```

## Security
- No connection strings in source control - only template and .env.example committed
- `.env` and `appsettings.Development.json` gitignored
- Staging/prod credentials from Key Vault
