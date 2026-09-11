### 6. Docker build (SCRUM-74)
```bash
docker build -t processing-service:local -f src/ProcessingService/Dockerfile .
docker compose up -d
```

## Solution Structure
- `src/ProcessingService` - Web API, EF Core Pomelo MySQL, JWT auth via shared library
- `tests/ProcessingService.Tests` - Unit + integration tests (SQLite for domain tests)

## Auth
Tokens issued by Auth Service, validated independently here (no call-out). Shared roles via WonrichRoles. `/health` is anonymous, all other endpoints require Bearer token -> 401 if missing.

## EF Core Provider
Uses **Pomelo.EntityFrameworkCore.MySql** (not Oracle's MySql.EntityFrameworkCore). Connection via `UseMySql(connectionString, ServerVersion.AutoDetect(connectionString))`.

No connection strings in source control - only template file committed.

## Environment Variables
See `appsettings.Development.template.json` for required keys. In Azure, set via App Service Configuration / Key Vault.

## Compose
`docker-compose.yml` registers MySQL + processing-service, reachable from API gateway. See file for ports and dependencies.
