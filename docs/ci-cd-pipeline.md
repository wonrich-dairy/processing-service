# CI/CD Pipeline — Processing Service (SCRUM-72)

Processing Service builds, tests and deploys independently of the other services. The workflow is
[`.github/workflows/ci-cd.yml`](../.github/workflows/ci-cd.yml); the App Services, databases and
publish profiles it deploys into were created in SCRUM-70 and are described in
[environments.md](environments.md).

## What runs when

| Event | Branch | What happens |
|---|---|---|
| Pull request | `develop`, `main` | Build and test only. The result shows on the PR. |
| Push | `develop` | Build, test, deploy to staging, verify `/health`. |
| Push | `main` | Build, test, **wait for approval**, deploy to production, verify `/health`. |
| Manual dispatch | any | Deploy a chosen commit to a chosen environment — the rollback path. |

```
 PR to develop/main        push to develop            push to main
        │                        │                         │
        ▼                        ▼                         ▼
  ┌───────────┐            ┌───────────┐             ┌───────────┐
  │ build &   │            │ build &   │             │ build &   │
  │ test      │            │ test      │             │ test      │
  └─────┬─────┘            └─────┬─────┘             └─────┬─────┘
        │                        │                         │
  status on PR                   ▼                         ▼
                          ┌─────────────┐          ┌────────────────┐
                          │  staging    │          │   approval     │
                          │  (auto)     │          │   required     │
                          └──────┬──────┘          └────────┬───────┘
                                 ▼                          ▼
                          verify /health            ┌────────────────┐
                                                    │  production    │
                                                    └────────┬───────┘
                                                             ▼
                                                      verify /health
```

## The jobs

### `build-and-test`

Restores, builds in Release, runs the suite, then publishes and uploads the artifact. **A failing
test fails this job, and both deploy jobs declare `needs: build-and-test`, so nothing reaches an
environment when a test is red.** That is the whole mechanism — there is no separate gate to
configure.

The suite boots the application through `WebApplicationFactory`, so it exercises real startup:
dependency injection, authentication, routing. It needs no database — the health check reports the
connection as unhealthy and the test accepts either result — which keeps CI independent of whether
a GitHub runner can reach `mcc-db`. It cannot, and should not have to.

The SDK is pinned by [`global.json`](../global.json) so the runner and a developer machine compile
against the same compiler.

### `deploy-staging`

Runs on a push to `develop`, or a manual dispatch targeting staging. No approval. It deploys the
artifact, then polls `/health` until the body contains `"status":"Healthy"`, for up to five
minutes.

Checking the body rather than the status code matters: a deployment that serves 200 while unable
to reach its database is a failed deployment, and `/health` reports the database as a separate
entry. Staging runs as `ASPNETCORE_ENVIRONMENT=Staging`, so it applies pending EF migrations on
startup — a green staging deploy therefore also proves the migration ran.

### `deploy-production`

Runs on a push to `main`, or a manual dispatch targeting production from `main`.

The approval gate is the `production` GitHub environment, which carries a required reviewer and a
`main`-only branch policy (SCRUM-70). The job's own `if:` repeats the branch condition, but the
environment policy is the control that actually holds — a workflow file can be edited in a PR, an
environment policy cannot.

## Required secrets

| Secret | Scope | Created by |
|---|---|---|
| `AZURE_WEBAPP_PUBLISH_PROFILE_STAGING` | `staging` environment | `infra/azure/github-environments.sh` |
| `AZURE_WEBAPP_PUBLISH_PROFILE_PROD` | `production` environment | `infra/azure/github-environments.sh` |

They are **environment** secrets, not repository secrets, so the production credential is only
readable by a job that declares `environment: production` — the job behind the approval gate. No
credential appears in the workflow file.

To rotate one, re-run `./infra/azure/github-environments.sh`.

## Rolling back

The pipeline rebuilds from a commit rather than re-pushing an old artifact, so a rollback is a
deploy of an earlier SHA.

1. **Actions** → **CI/CD — Processing Service** → **Run workflow**
2. Branch: `develop` for staging, `main` for production
3. **Environment to deploy to**: `staging` or `production`
4. **Commit SHA**: the full SHA of the last known-good commit — leave blank to redeploy the branch tip
5. **Run workflow**

Finding a good SHA:

```bash
git log develop --oneline -10
# or: Actions → the last green run → the SHA is at the top
```

What to know before you rely on it:

- The rollback **rebuilds and re-tests** at that commit. If its tests fail, it will not deploy —
  that is deliberate.
- A production rollback must target a commit that is an ancestor of `main`; the workflow checks
  and fails otherwise. Without that check the dispatch form could push an unreviewed commit
  straight to production.
- Production rollback still passes through the approval gate.
- **Migrations are forward-only.** Rolling application code back past a migration leaves older code
  against a newer schema. Check schema compatibility before crossing a migration boundary; EF will
  not undo one for you.

## Production migrations

Production runs as `ASPNETCORE_ENVIRONMENT=Production`, and `Program.cs` applies migrations on
startup only in Development and Staging. That is deliberate — a production schema change should be
something a person decides to do, not a side effect of a process restarting — but it means the
schema does not travel with a production deployment on its own. Two things close that gap.

### Every build carries its schema

`build-and-test` runs `dotnet ef migrations script --idempotent` and uploads the result as a
separate `migrations` artifact, alongside the application package. `--idempotent` wraps each
migration in a check against `__EFMigrationsHistory`, so the script is safe to run against a
database at any point in its history, including one that is already up to date.

It is generated but not applied. A GitHub-hosted runner has no route through the `mcc-db` firewall,
and that server is in a different Azure subscription (see [environments.md](environments.md)), so
CI cannot reach production's database even if it should. Applying it is a deliberate step by
someone who can:

```bash
# download the `migrations` artifact from the run you are deploying, then
mysql -h mcc-db.mysql.database.azure.com -u <admin> -p --ssl-mode=REQUIRED \
      processingdb_prod < migrations.sql
```

Use the `mysql` client or MySQL Workbench, not an arbitrary SQL tool: the script uses `DELIMITER`,
which is a client directive rather than SQL, and a tool that does not understand it will fail
part-way through.

### A stale schema should not report healthy

Both deploy jobs poll for `"status":"Healthy"` specifically rather than accepting any 200, so that
a deployment onto a database this build cannot use fails the run instead of going green.

That gate only bites if the health endpoint distinguishes the two cases. On its own,
`DatabaseHealthCheck` reports connectivity — `CanConnectAsync` — and an empty `processingdb_prod`
passes it. **The change that makes it report `Degraded` when migrations are pending is a separate
pull request**, kept out of this one so the pipeline change stays additive and the health-check
behaviour is reviewed on its own merit.

Until that lands, the health gate in this workflow proves the service is up and its database is
reachable, but not that the schema matches.

### Consequence for a first production release

The first deployment to production will fail its health check, because `processingdb_prod` is empty
until the script is applied. That is the intended order: apply `migrations.sql`, then re-run the
deployment. Rolling the pipeline green over an empty database would be the bug, not this.

## Verification checklist

- [ ] Pipeline runs green on a push to `develop`
- [ ] A deliberately failing test blocks the deployment
- [ ] Build status appears on a pull request
- [ ] A rollback dispatch deploys an earlier commit successfully
- [ ] Production deployment waits for approval
