# Azure Environments — Processing Service (SCRUM-70)

Processing Service runs on Azure App Service in two isolated environments, **staging** and
**production**. Each has its own resource group, App Service plan and App Service, its own database,
and its own deployment credential. Nothing is shared between them except the MySQL Flexible Server
host, which holds one database per environment (see [database.md](database.md)).

Everything in this document is created by the scripts in [`infra/azure/`](../infra/azure/), not by
hand in the portal, so it can be rebuilt from a clean subscription.

## Environments

| Property | Staging | Production |
|---|---|---|
| **Resource group** | `rg-processing-staging` | `rg-processing-prod` |
| **Region** | `southeastasia` | `southeastasia` |
| **App Service plan** | `plan-processing-staging` (F1 Free, Linux) | `plan-processing-prod` (F1 Free, Linux) |
| **App Service** | `app-wonrich-processing-staging` | `app-wonrich-processing-prod` |
| **URL** | https://app-wonrich-processing-staging.azurewebsites.net | https://app-wonrich-processing-prod.azurewebsites.net |
| **Health** | `/health` | `/health` |
| **Swagger UI** | `/swagger` | Disabled |
| **`ASPNETCORE_ENVIRONMENT`** | `Staging` | `Production` |
| **EF migrations on startup** | Yes | No — applied deliberately (SCRUM-72) |
| **Deploys from** | `develop`, automatically | `main`, after manual approval |

The two plans are separate so staging traffic can never starve production, and so production can
move off the Free tier later without touching staging.

## Configuration

All configuration is resolved at **runtime** from App Service application settings, which the
runtime exposes to the process as environment variables. The deployed package carries only the
placeholder values from `appsettings.json`; nothing environment-specific is baked in at build time.

| Setting | Staging | Production |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Staging` | `Production` |
| `ConnectionStrings__DefaultConnection` | staging database, staging account | production database, production account |
| `Auth__Issuer` | `wonrich-auth` | `wonrich-auth` |
| `Auth__Audience` | `wonrich-services` | `wonrich-services` |
| `Auth__SigningKey` | the key the staging auth service signs with | the key the production auth service signs with |
| `Cors__AllowedOrigins__0` | `https://frontend-phi-sage-81.vercel.app` | `https://frontend-phi-sage-81.vercel.app` |

`Auth__*` must match the auth service deployed to the same environment, or every token is rejected
as a bad signature.

Both environments currently allow the same browser origin, because the team deploys one frontend
(`https://frontend-phi-sage-81.vercel.app`) rather than one per environment. That is not real
isolation — anyone who can open that site can be pointed at either API — and staging's value should
change to a staging-only deployment as soon as one exists. Note the origin carries no trailing
slash: a browser sends `Origin` without one, and the comparison is exact.

> **Known gap — the signing key is not yet a secret.** `wonrich-auth-app` has no `Auth__SigningKey`
> application setting, so it signs with the placeholder committed in the auth repository's
> `SRC/appsettings.json`. Processing Service is configured with that same value, because a
> different one would reject every token the auth service issues. Anyone with read access to that
> repository can therefore mint a valid token for any user and role, in either environment.
>
> Processing Service still meets its own requirement — the key is an App Service setting here, not
> in this repository's source — and rotating the shared key is a change to the auth service, so it
> belongs to that service's backlog. Rotation means setting a new `Auth__SigningKey` on auth,
> intake and processing together, per environment; they are only interoperable while all three
> agree. Staging and production should get different keys at that point, so that a staging token
> is useless against production.

## External dependencies

| Dependency | Owner | Consequence |
|---|---|---|
| MySQL Flexible Server `mcc-db` | A different Azure subscription, outside this project's Azure for Students account | Server-level operations — firewall rules, backup policy, admin credential rotation — need whoever owns that subscription. The databases and the scoped accounts are created with the MySQL client instead (`database-setup.sh`), which needs only network access and the server admin password. |
| JWT signing key | The auth service | See the note above. |

## Where the secrets live

| Secret | Held in | Who can read it |
|---|---|---|
| MySQL connection strings | App Service application settings, per environment | Azure RBAC on the resource group |
| JWT signing keys | App Service application settings, per environment | Azure RBAC on the resource group |
| Publish profiles (deployment credentials) | GitHub **environment** secrets `AZURE_WEBAPP_PUBLISH_PROFILE_STAGING` / `_PROD` | Only a workflow job that declares that `environment:` |

Nothing above is in source control. `provision.sh` reads the secrets from its own environment and
writes them straight to Azure; they are never put in a file.

## Access control

- **Staging**: every team member can read the configuration and deploy to it.
- **Production**: only the DevOps role holder (Alen) has access to `rg-processing-prod`, and is the
  sole required reviewer on the GitHub `production` environment. Deployments to production are only
  accepted from `main`.

Teammates who need to *see* production without changing it get `Reader` on the resource group:

```bash
az role assignment create --assignee <user@email> --role Reader \
  --scope "$(az group show --name rg-processing-prod --query id -o tsv)"
```

`Reader` cannot list application settings, so the connection string and signing key stay with the
role holder.

## Provisioning

Prerequisites: [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli), the
[GitHub CLI](https://cli.github.com/), and `az login` / `gh auth login` done. Run from Git Bash on
Windows.

```bash
# 1. The two databases and their scoped accounts.
#    Prompts for the mcc-db admin password, then a password for each application account
#    (Enter generates a strong one). Nothing is passed as an argument.
./infra/azure/database-setup.sh

# 2. Staging — prompts for the connection string and the signing key
CORS_ORIGIN='https://<staging frontend>' ./infra/azure/provision.sh staging

# 3. Production — same script, production values
./infra/azure/provision.sh production

# 4. GitHub environments + publish-profile secrets (both environments in one go)
./infra/azure/github-environments.sh

# 5. Prove staging works end to end before the pipeline exists
./infra/azure/deploy.sh staging
```

`provision.sh` also accepts `PROCESSING_DB_CONNECTION` and `AUTH_SIGNING_KEY` from the environment
for a scripted run. Prefer the prompt for a one-off: a secret passed on the command line is left
behind in `~/.bash_history` and is visible in `ps` while the command runs.

The scripts are idempotent. Re-run `provision.sh` to change a setting; re-run
`github-environments.sh` after rotating a publish profile.

| Script | Does |
|---|---|
| `database-setup.sh` | The `processingdb` / `processingdb_prod` databases and the scoped account for each. Prompts for every password. Run once, before `provision.sh`. |
| `env.sh` | Naming shared by the others — resource names, region, SKU, runtime. Nothing secret. |
| `provision.sh <env>` | Resource group, plan, App Service, application settings, HTTPS-only, FTPS off, TLS 1.2. |
| `github-environments.sh` | GitHub `staging` (open) and `production` (reviewer + `main` only) environments; publish profiles as environment secrets. |
| `deploy.sh <env>` | `dotnet publish` → zip deploy → poll `/health`. Manual check only; CI is SCRUM-72. |

## Verifying an environment

```bash
curl https://app-wonrich-processing-staging.azurewebsites.net/health
# {"status":"Healthy","checks":{"database":{"status":"Healthy","description":"Database reachable"}}}

curl -i https://app-wonrich-processing-staging.azurewebsites.net/api/ping
# HTTP/1.1 401 — no token, as expected

curl -i https://app-wonrich-processing-staging.azurewebsites.net/api/ping/anonymous
# HTTP/1.1 200 — the one route that does not need one

curl -i http://app-wonrich-processing-staging.azurewebsites.net/health
# HTTP/1.1 301 — https-only is on, so plain HTTP never carries a token
```

A healthy database check is the useful one: it only passes if the connection string resolved from
the application settings, the scoped account could open the database, and EF applied its migrations
on startup. `__efmigrationshistory` in `processingdb` is the record of that last part — the
migration on this branch only sets the charset, so that table being alone there is correct until
the data model lands.

Logs: `az webapp log tail --name app-wonrich-processing-staging --resource-group rg-processing-staging`.

## Notes

- Azure for Students restricts regions to `southeastasia`, `indonesiacentral`, `uaenorth`,
  `eastasia`, `malaysiawest`. `southeastasia` keeps the service next to `mcc-db`.
- F1 has no Always On: the first request after ~20 minutes idle is slow while the app cold-starts.
  Acceptable for staging and for a sprint-review demo; a paid tier fixes it if it ever matters.
- Rotating a publish profile: `az webapp deployment user`-level resets do not touch it; use
  `az webapp deployment list-publishing-profiles` after
  `az resource invoke-action --action newpassword` on the site, then re-run
  `github-environments.sh`.
