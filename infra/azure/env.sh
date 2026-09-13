#!/usr/bin/env bash
# Shared naming for the Processing Service Azure environments (SCRUM-70).
# Sourced by the other scripts in this folder; nothing here is a secret.
#
# Naming follows the intake service (rg-/plan-/app- prefixes, one resource group per environment)
# so the two services look the same in the portal. App Service names are global DNS labels, hence
# the "wonrich-" in the app name only.

ENV="${1:?usage: $0 <staging|production>}"
case "$ENV" in
  staging|production) ;;
  *) echo "environment must be 'staging' or 'production', got '$ENV'" >&2; exit 1 ;;
esac

# Azure for Students only allows a handful of regions; southeastasia is where mcc-db already lives.
LOCATION="${LOCATION:-southeastasia}"
SKU="${SKU:-F1}"
RUNTIME="${RUNTIME:-DOTNETCORE:10.0}"

# "production" -> "prod" for the resource names, matching rg-mcc-intake-prod.
SHORT="$ENV"; [ "$ENV" = production ] && SHORT=prod

RESOURCE_GROUP="rg-processing-$SHORT"
PLAN="plan-processing-$SHORT"
APP="app-wonrich-processing-$SHORT"
APP_URL="https://$APP.azurewebsites.net"

# ASPNETCORE_ENVIRONMENT drives migrations + Swagger in Program.cs: both on for Staging, off for Production.
ASPNET_ENV=Staging; [ "$ENV" = production ] && ASPNET_ENV=Production
