#!/usr/bin/env bash
# Create the GitHub `staging` and `production` environments for this repository and store each
# App Service's publish profile as an environment secret (SCRUM-70 AC: "Deployment credentials
# stored as GitHub repo secrets"). SCRUM-72's workflow reads these as
# secrets.AZURE_WEBAPP_PUBLISH_PROFILE_STAGING / _PROD.
#
#   az login && gh auth login
#   ./infra/azure/github-environments.sh
#
# Environment secrets rather than repository secrets: the production credential is then only
# visible to a job that declares `environment: production`, which is the job behind the approval
# gate. Same layout as mcc-intake-service.
set -euo pipefail
cd "$(dirname "$0")"

REPO="${REPO:-wonrich-dairy/processing-service}"
# GitHub login of the DevOps role holder, the only approver for production deployments.
REVIEWER="${REVIEWER:-IT24103473}"

reviewer_id=$(gh api "users/$REVIEWER" --jq .id)

# staging: no gate, deploys on every push to develop.
printf '{"deployment_branch_policy":null}' \
  | gh api --method PUT "repos/$REPO/environments/staging" --input - >/dev/null

# production: required reviewer + only main may deploy. The branch policy is the authoritative
# control; the workflow's own ref check is belt-and-braces.
printf '{"reviewers":[{"type":"User","id":%s}],"prevent_self_review":false,"deployment_branch_policy":{"protected_branches":false,"custom_branch_policies":true}}' "$reviewer_id" \
  | gh api --method PUT "repos/$REPO/environments/production" --input - >/dev/null
printf '{"name":"main","type":"branch"}' \
  | gh api --method POST "repos/$REPO/environments/production/deployment-branch-policies" --input - >/dev/null 2>&1 \
  || true   # already exists on re-run

for env in staging production; do
  . ./env.sh "$env"
  secret=AZURE_WEBAPP_PUBLISH_PROFILE_STAGING
  [ "$env" = production ] && secret=AZURE_WEBAPP_PUBLISH_PROFILE_PROD
  az webapp deployment list-publishing-profiles \
    --name "$APP" --resource-group "$RESOURCE_GROUP" --xml \
    | gh secret set "$secret" --repo "$REPO" --env "$env"
  echo "== $env: $secret set from $APP"
done
