# Wonrich Processing Service

Independently deployable microservice for dairy processing - storing tanks, mixing tanks, allocations, stages, cooling and product split.

## Local Run

### Prerequisites
- .NET 10 SDK
- Docker Desktop (for MySQL)
- No secrets in repo - copy template first

### 1. Create local secrets file
```bash
cp src/ProcessingService/appsettings.Development.template.json src/ProcessingService/appsettings.Development.json
# Edit if needed - default works with docker-compose
```

Required env vars (or appsettings.Development.json):
- `ConnectionStrings__DefaultConnection` - MySQL connection, e.g. `Server=localhost;Port=3308;Database=processing;User Id=processing_user;Password=DevPassword123!`
- `Auth__Issuer` - `wonrich-auth`
- `Auth__Audience` - `wonrich-services`
- `Auth__SigningKey` - at least 32 chars, shared with Auth service

### 2. Start MySQL (isolated datastore - SCRUM-71)
```bash
docker compose up -d mysql
# or
docker run -d --name wonrich-mysql -e MYSQL_ROOT_PASSWORD=RootPassword123! -e MYSQL_DATABASE=processing -e MYSQL_USER=processing_user -e MYSQL_PASSWORD=DevPassword123! -p 3308:3306 mysql:8.4
```

### 3. Apply migrations (Pomelo - SCRUM-57)
```bash
dotnet tool restore
dotnet ef database update --project src/ProcessingService
```

### 4. Run service
```bash
dotnet run --project src/ProcessingService --environment Development
```

- Health: http://localhost:5210/health (anonymous, 200 + DB healthy)
- Swagger: http://localhost:5210/swagger (Development/Staging only)
- Metrics: http://localhost:5210/metrics (Prometheus, when SCRUM-90 merged)

### 5. Run tests
```bash
dotnet test
```

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
