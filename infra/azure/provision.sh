#!/usr/bin/env bash
# Provision one Processing Service environment on Azure App Service (SCRUM-70).
#
#   az login
#   PROCESSING_DB_CONNECTION='Server=...;Database=processingdb;...' \
#   AUTH_SIGNING_KEY='...' \
#   ./infra/azure/provision.sh staging
#
# Idempotent: every `az ... create` here is a no-op when the resource already exists, and app
# settings are upserted, so re-running after a change is the way to apply it. Secrets come in
# through the environment and are written straight to App Service application settings — they are
# never written to disk or committed (AC: "Secrets held in App Service settings, never in source").
set -euo pipefail
cd "$(dirname "$0")"
. ./env.sh "$@"

# Both secrets can come from the environment, for a scripted run, or be typed at the prompt. A
# typed secret stays out of the shell history and out of `ps`, so prefer it for a one-off.
ask_secret() {  # ask_secret <prompt> -> echoes the value
  local prompt="$1" value
  [ -r /dev/tty ] || { echo "no terminal to prompt on; set the variable instead" >&2; exit 1; }
  read -rsp "$prompt" value < /dev/tty
  echo >&2
  printf '%s' "$value"
}

if [ -z "${PROCESSING_DB_CONNECTION:-}" ]; then
  echo "Connection string for $ENV, e.g."
  echo "  Server=mcc-db.mysql.database.azure.com;Port=3306;Database=$( [ "$ENV" = production ] && echo processingdb_prod || echo processingdb );User Id=$( [ "$ENV" = production ] && echo processing_app_prod || echo processing_app );Password=...;SslMode=Required"
  PROCESSING_DB_CONNECTION=$(ask_secret "ConnectionStrings__DefaultConnection: ")
fi
[ -n "$PROCESSING_DB_CONNECTION" ] || { echo "a connection string is required" >&2; exit 1; }

if [ -z "${AUTH_SIGNING_KEY:-}" ]; then
  AUTH_SIGNING_KEY=$(ask_secret "Auth__SigningKey for $ENV (must match the $ENV auth service): ")
fi
[ -n "$AUTH_SIGNING_KEY" ] || { echo "a signing key is required" >&2; exit 1; }
AUTH_ISSUER="${AUTH_ISSUER:-wonrich-auth}"
AUTH_AUDIENCE="${AUTH_AUDIENCE:-wonrich-services}"
# The frontend origin the browser will call this service from. Empty means "no browser access yet".
CORS_ORIGIN="${CORS_ORIGIN:-}"

echo "== $ENV: $RESOURCE_GROUP / $PLAN / $APP ($LOCATION, $SKU, $RUNTIME)"

az group create --name "$RESOURCE_GROUP" --location "$LOCATION" --output none

# One plan per environment so staging load can never starve production, and so production's plan
# can be moved off Free later without touching staging.
az appservice plan create \
  --name "$PLAN" --resource-group "$RESOURCE_GROUP" \
  --location "$LOCATION" --sku "$SKU" --is-linux --output none

if ! az webapp show --name "$APP" --resource-group "$RESOURCE_GROUP" --output none 2>/dev/null; then
  az webapp create \
    --name "$APP" --resource-group "$RESOURCE_GROUP" --plan "$PLAN" \
    --runtime "$RUNTIME" --output none
fi

# The publish profile the pipeline deploys with is a basic-auth credential, and new App Services
# ship with basic-auth publishing switched off, so without this the deploy job fails with
# "Publish profile is invalid". SCM only; FTP stays off (ftps-state is disabled below anyway).
az resource update \
  --resource-group "$RESOURCE_GROUP" --namespace Microsoft.Web --parent "sites/$APP" \
  --resource-type basicPublishingCredentialsPolicies --name scm \
  --set properties.allow=true --output none

# Config is resolved at runtime from these settings; the deployed zip carries only the
# appsettings.json placeholders. Double-underscore is how ASP.NET Core maps env vars to sections.
settings=(
  "ASPNETCORE_ENVIRONMENT=$ASPNET_ENV"
  "ConnectionStrings__DefaultConnection=$PROCESSING_DB_CONNECTION"
  "Auth__Issuer=$AUTH_ISSUER"
  "Auth__Audience=$AUTH_AUDIENCE"
  "Auth__SigningKey=$AUTH_SIGNING_KEY"
)
if [ -n "$CORS_ORIGIN" ]; then
  settings+=("Cors__AllowedOrigins__0=$CORS_ORIGIN")
fi

az webapp config appsettings set \
  --name "$APP" --resource-group "$RESOURCE_GROUP" \
  --settings "${settings[@]}" --output none

# Tokens travel in the Authorization header; never let them go over plain HTTP.
az webapp update --name "$APP" --resource-group "$RESOURCE_GROUP" --https-only true --output none
az webapp config set --name "$APP" --resource-group "$RESOURCE_GROUP" \
  --ftps-state Disabled --min-tls-version 1.2 --output none

echo "== $ENV provisioned: $APP_URL"
echo "   publish profile: az webapp deployment list-publishing-profiles --name $APP --resource-group $RESOURCE_GROUP --xml"
