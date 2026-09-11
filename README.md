# Processing Service

Records what happens to milk after it leaves a chilling centre — mixing, pasteurisation,
homogenisation, filling — as part of the Wonrich Dairy Quality Monitoring & Traceability System.

The processing data model (SCRUM-57) starts at the factory's tanks and the loads unloaded into
them. The stage records are SCRUM-63 onwards.

## Tech stack
- ASP.NET Core (.NET 10) + Entity Framework Core 10
- MySQL 8.0
- Swashbuckle / Swagger UI for API documentation
- Docker for local development and deployment

## Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- MySQL 8.0, or Docker
- A token from the Wonrich auth service to call anything but `/health`

## Getting started

### With Docker (SCRUM-74)

From a clean clone, one command brings up the service and the database it owns:

```bash
docker compose up --build
```

Nothing needs copying or editing first. Every setting has a development default in
`docker-compose.yml`, the database is created on first start, and the service applies its own
migrations as it boots — so the schema is there without anyone running `dotnet ef`.

| | |
| --- | --- |
| API | http://localhost:5239 |
| Swagger UI | http://localhost:5239/swagger |
| Health | http://localhost:5239/health |
| MySQL (from the host) | `localhost:3307`, database `wonrich_processing`, user `processing_user` |

Copy `.env.example` to `.env` only if you need to move a port something else already holds, or to
point at different credentials.

```bash
docker compose down      # stop, keep the data
docker compose down -v   # stop and discard the database
```

> This stack runs Processing Service on its own. The root `docker-compose.yml` in the wonrich
> workspace brings up every service together and is what a demo runs from. They use the same host
> ports, so run one or the other, not both.

> Every route except `/health` needs a bearer token, and this stack does not include the auth
> service. Use the root stack when you need to call a protected endpoint, or paste in a token
> issued by an auth service using the same `Auth__SigningKey` — the defaults here match the root
> stack's, so a token from there is accepted.

### Without Docker

```powershell
copy src\ProcessingService\appsettings.Development.template.json src\ProcessingService\appsettings.Development.json
# then put the real connection string and signing key in that file, which is not committed

dotnet run --project src\ProcessingService
```

Swagger UI is served at `/swagger` in every environment except Production. Every route requires a
token, so paste one into **Authorize** before trying an endpoint.

| Script | Purpose |
| --- | --- |
| `dotnet build ProcessingService.slnx` | Build |
| `dotnet test ProcessingService.slnx` | Run the suite |
| `docker build -t wonrich/processing-service .` | Build the container |
| `docker compose up --build` | Run the service and its database together |
| `docker compose logs -f processing` | Follow the service's logs |

## API
| Method | Route | Purpose |
| --- | --- | --- |
| `GET` | `/api/session/me` | The caller's identity, as read off the bearer token (SCRUM-34). |
| `GET` | `/api/processing/tanks` | The factory's tanks and what each holds. `kind` narrows to `Storing` or `Mixing` (SCRUM-61). |
| `GET` | `/api/processing/tanks/{code}` | One tank. |
| `POST` | `/api/processing/tanks` | Add a tank. |
| `PUT` | `/api/processing/tanks/{code}` | Rename a tank and restate its working volume. |
| `POST` | `/api/processing/tanks/{code}/deactivate` | Take a tank out of service. Refused while it holds milk. |
| `POST` | `/api/processing/tanks/{code}/reactivate` | Put a tank back into service. |
| `GET` | `/api/processing/unloads` | Unloads, newest first. `date` narrows to one factory day (SCRUM-62). |
| `GET` | `/api/processing/unloads/{reference}` | One unload by its `UNL-YYYYMMDD-NN` reference. |
| `POST` | `/api/processing/unloads` | Record a bowser load into a storing tank. |

Records the MCC and Intake Service owns - dispatch notes, batches - are referenced by text rather
than by foreign key: the two services own separate databases and neither may reach into the
other's.

### Who may do what
| Policy | Roles |
| --- | --- |
| `ReadProcessing` | System Administrator, Production Manager, Factory Intake Officer, Quality Analyst |
| `ManageTanks` | System Administrator, Production Manager |
| `RecordUnloads` | System Administrator, Production Manager, Factory Intake Officer |

## Configuration

Nothing secret is committed. `appsettings.json` names each setting and leaves it empty; the values
come from `appsettings.Development.json` locally (gitignored) and from the environment in staging
and production.

| Setting | Environment variable | Purpose |
| --- | --- | --- |
| `ConnectionStrings:DefaultConnection` | `ConnectionStrings__DefaultConnection` | MySQL connection string |
| `Auth:Issuer` | `Auth__Issuer` | Token issuer, must match the auth service |
| `Auth:Audience` | `Auth__Audience` | Shared audience across the Wonrich services |
| `Auth:SigningKey` | `Auth__SigningKey` | Symmetric signing key, at least 32 characters |

The service refuses to start without a connection string, naming the setting rather than failing
later with a dependency-injection error that never mentions the real cause.

## Health

`GET /health` is anonymous, because the container runtime and the load balancer probe it before
anyone holds a token. It reports the database connection, not merely that the process is running:

```json
{ "status": "Healthy", "checks": { "database": { "status": "Healthy", "description": "The database is reachable." } } }
```

An unreachable database answers `503` with `"status": "Unhealthy"`, so a container that is up but
cannot serve data is not rolled into the load balancer.

## Authentication

Tokens are issued by the auth service (SCRUM-34) and validated here independently — signature,
issuer, audience and expiry — so this service never calls out to authenticate a request. Every
endpoint except `/health` requires an authenticated caller by default, so a new controller is
guarded without anyone remembering an attribute.

`GET /api/session/me` returns the caller's id, name and role. It exists so the auth wiring is
provable rather than taken on trust; it will sit alongside the processing endpoints as they land.

> **Known duplication.** `Api/Infrastructure/ProcessingAuth.cs` repeats the token validation from
> `Wonrich.Auth` in the mcc-intake-service repository. That library is a project reference there and
> no package feed is configured for another repository to consume it from — `Wonrich.QualityPanel`'s
> csproj notes that publishing is the pipeline's job (SCRUM-37), but the pipeline shipped without a
> feed. What is duplicated is the validation contract, not business logic, so drift shows up
> immediately as a rejected token. Replacing it with the published package is a one-line change and
> wants its own ticket.

## Dates

The MySQL provider is Oracle's `MySql.EntityFrameworkCore`, which writes a `DateOnly` but cannot
read one back: `MySqlDataReader` has no `DateOnly` support, so loading any entity holding one throws
`InvalidCastException`. `ProcessingDbContext.ConfigureConventions` stores every `DateOnly` through
`DateOnlyToDateTimeConverter`, keeping the column `date`. A date added to a new entity is covered
automatically.

SCRUM-56's acceptance criteria name Pomelo, which materialises `DateOnly` natively. Pomelo's newest
release (9.0.0) targets EF Core 9 and this platform is on EF Core 10, so adopting it would pin this
service to the previous major while every other project targets `net10.0`. The criterion needs
amending; the convention above is what makes the Oracle provider safe in the meantime.

## Branching strategy
- `main`: protected, production-ready
- `develop`: protected integration branch
- `feature/SCRUM-<key>-<description>`: work branches, merged into `develop` via reviewed PR
