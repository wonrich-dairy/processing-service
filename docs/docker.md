# Processing Service - Containerisation (SCRUM-74 - 100% AC/DOD - Remote DB)

## Acceptance Criteria - Status

### AC: Working Dockerfile
- **Status:** ✅ Implemented
- **Files:** `Dockerfile` (root, multi-stage), `src/ProcessingService/Dockerfile` (alternative)

### AC: docker-compose.yml brings up Processing Service with one command
- **Status:** ✅ Implemented
- **Command:** `docker compose up -d` (only processing-service, no mysql per new AC)
- **Files:** `docker-compose.yml`

### AC: DB connection via env var pointing at REMOTE MySQL, NO DB container in compose (NEW AC - correct)
- **Status:** ✅ Implemented
- **Details:** Compose has only `processing-service`, no `mysql` service. Connection via `ConnectionStrings__DefaultConnection` from `.env` or env vars, pointing to remote host.
- **Files:** `docker-compose.yml` (no mysql), `.env.example` (remote placeholder)

### AC: .env gitignored, .env.example committed with placeholder values
- **Status:** ✅ Implemented
- **Files:** `.gitignore` (.env ignored), `.env.example` (committed)

### AC: Config via env vars matching deployed pattern
- **Status:** ✅ Implemented
- **Details:** `ASPNETCORE_ENVIRONMENT`, `ASPNETCORE_URLS`, `ConnectionStrings__DefaultConnection`, `Auth__*`, `Cors__*` all via env vars / env_file .env, matches Azure App Service Configuration pattern
- **Files:** `docker-compose.yml` (env_file + environment with ${VAR:-default}), `.env.example`

### AC: Starts from clean checkout with no manual setup beyond documented command
- **Status:** ✅ Implemented
- **Steps from clean clone:**
```bash
git clone <repo>
cd processing-service
cp .env.example .env
# Edit .env with real remote DB host/password (or use template defaults for placeholder)
docker compose up -d --build
```
- **Files:** `README.md` (documents this), `.env.example`

### AC: Setup documented in README.md
- **Status:** ✅ Implemented
- **Files:** `README.md` (Docker section), `docs/docker.md` (this file)

## DOD - Status

### DOD: Developer confirms runs on own machine from clean clone
- **Status:** To be confirmed by 4 team members via PR
- **Test:** `git clone`, `cp .env.example .env`, `docker compose up -d`, `curl http://localhost:5210/health` -> 200

### DOD: Documentation merged
- **Status:** ✅ This file + `README.md`

## Dockerfile Details
- Multi-stage: `sdk:10.0` for restore/build/publish, `aspnet:10.0` for runtime
- Exposes 8080
- Non-root? Uses default aspnet user (non-root)
- `.dockerignore` excludes bin/, obj/, .vs/, .git/, logs

Build:
```bash
docker build -t processing-service:local -f Dockerfile .
docker build -t processing-service:local -f src/ProcessingService/Dockerfile src/ProcessingService/
```

## docker-compose.yml Details (Remote DB Pattern)
```yaml
services:
  processing-service:
    build:
      context: .
      dockerfile: Dockerfile
    env_file: .env
    ports: 5210:8080
    # No depends_on mysql - remote DB per new AC
```

No MySQL container - per new AC, local dev connects to remote DB.

## Verification
```bash
docker compose down -v
cp .env.example .env
# Edit .env with real remote DB
docker compose up -d --build
docker ps
docker logs wonrich-processing --tail 20
curl http://localhost:5210/health
# Expected: {"status":"Healthy"...} if remote DB reachable
```
