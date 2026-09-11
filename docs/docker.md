# Processing Service - Containerisation (SCRUM-74)

## Dockerfile
Multi-stage build:
- `sdk:10.0` for restore/build/publish
- `aspnet:10.0` for runtime
- Exposes 8080 (http)
- Entrypoint: `dotnet ProcessingService.dll`

Build from clean checkout:
```bash
docker build -t processing-service:local -f src/ProcessingService/Dockerfile .
# or from root
docker build -t processing-service:local .
```

## docker-compose.yml
Brings up Processing Service + MySQL with one command:
```bash
docker compose up -d
# or
docker compose up --build
```

Services:
- `mysql` - isolated datastore (SCRUM-71), healthcheck, volume `mysql_data`
- `processing-service` - depends_on mysql healthy, env vars for connection, port 5210:8080

Config via environment variables (matches deployed pattern in Azure App Service):
```
ASPNETCORE_ENVIRONMENT=Development
ASPNETCORE_URLS=http://+:8080
ConnectionStrings__DefaultConnection=Server=mysql;Port=3306;Database=...
Auth__Issuer, Auth__Audience, Auth__SigningKey
```

No manual setup beyond `docker compose up` and copying `appsettings.Development.template.json` to `appsettings.Development.json` (if running without compose).

## .dockerignore
Excludes bin/, obj/, .vs/, .git/, logs, etc. to keep image small and build fast.

## Verification (DOD)
- All 4 team members: `git clone <repo>`, `cp src/ProcessingService/appsettings.Development.template.json src/ProcessingService/appsettings.Development.json`, `docker compose up -d` -> service reachable at http://localhost:5210/health, /swagger
- No manual steps beyond documented command
- Documentation in README.md and this file
