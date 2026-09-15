#!/usr/bin/env bash
# One-off zip deploy from this machine, for proving an environment works before the pipeline
# exists (SCRUM-70 DoD: "Test deployment to staging succeeds and responds"). Day-to-day deploys
# are SCRUM-72's GitHub Actions workflow, not this.
#
#   ./infra/azure/deploy.sh staging
set -euo pipefail
cd "$(dirname "$0")"
. ./env.sh "$@"
cd ../..

out=$(mktemp -d)
dotnet publish src/ProcessingService/ProcessingService.csproj -c Release -o "$out/publish"
(cd "$out/publish" && zip -qr "$out/app.zip" .)

az webapp deploy --name "$APP" --resource-group "$RESOURCE_GROUP" \
  --src-path "$out/app.zip" --type zip --output none
rm -rf "$out"

# Program.cs applies migrations on startup outside Production, so the first staging request also
# proves the connection string and the database grants.
echo "== deployed to $APP_URL; waiting for /health"
for _ in $(seq 1 30); do
  if body=$(curl -sf "$APP_URL/health"); then echo "$body"; exit 0; fi
  sleep 10
done
echo "!! $APP_URL/health did not answer 200 — check: az webapp log tail --name $APP --resource-group $RESOURCE_GROUP" >&2
exit 1
