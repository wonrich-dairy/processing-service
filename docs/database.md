# Processing Service - MySQL Datastore (SCRUM-71)

## Overview
Dedicated MySQL database per microservice, isolated from MCC and other services. Each service owns its schema and evolves independently.

## Local Development (Containerised - matches deployed schema)

### Docker Compose MySQL (isolated)
```yaml
# From docker-compose.yml
mysql:
  image: mysql:8.4
  container_name: wonrich-mysql
  environment:
    MYSQL_DATABASE: processing
    MYSQL_USER: processing_user
    MYSQL_PASSWORD: DevPassword123! # local only, not used in staging/prod
  ports:
    - "3308:3306" # 3308 avoids conflict with MCC's 3306/3307
  volumes:
    - mysql_data:/var/lib/mysql # persistence across restarts
  healthcheck:
    test: mysqladmin ping
```

Start:
```bash
docker compose up -d mysql
# or
docker run -d --name wonrich-mysql -e MYSQL_ROOT_PASSWORD=RootPassword123! -e MYSQL_DATABASE=processing -e MYSQL_USER=processing_user -e MYSQL_PASSWORD=DevPassword123! -p 3308:3306 mysql:8.4
```

Connection string (from appsettings.Development.template.json, never committed):
```
Server=localhost;Port=3308;Database=processing;User Id=processing_user;Password=DevPassword123!
```

Via env var (for CI/CD, matches Azure pattern):
```bash
ConnectionStrings__DefaultConnection=Server=mysql;Port=3306;Database=processing;User Id=processing_user;Password=...
```

### EF Core Provider
**Pomelo fork** `Microting.EntityFrameworkCore.MySql` v10.0.10 (supports EF Core 10, official Pomelo 9.0.0 only supports EF Core 9).
Usage:
```csharp
options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString))
```

### Migrations
- Configured independently per service (no shared DB)
- Initial migration creates empty DB (or minimal tables in SCRUM-57)
- Apply:
```bash
dotnet tool restore
dotnet ef database update --project src/ProcessingService
```
- Reversible: `Down` tested via `dotnet ef database update <previous>`

### Staging / Production (Azure)
- MySQL Flexible Server per environment, database `processing`
- Credentials scoped only to `processing` DB (not root, not shared)
- Connection string from Azure App Service Configuration / Key Vault, never in source
- Charset: utf8mb4, Collation: utf8mb4_unicode_ci
- Backup: Automated daily backup, 7-day retention (documented in Azure portal, not in code)

### Security
- No connection strings in source control - only `appsettings.Development.template.json` committed
- `appsettings.Development.json` and `appsettings.Production.json` are gitignored
- Local password `DevPassword123!` is for dev only, staging/prod use Key Vault

### Verification
- Service starts, connects to own DB, runs migrations without manual steps (DOD)
- `dotnet ef database update` on empty DB succeeds
- `/health` reports `database: Healthy`
