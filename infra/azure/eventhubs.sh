#!/usr/bin/env bash
# Provision the staging Kafka endpoint on Azure Event Hubs (SCRUM-88).
#
#   az login
#   ./infra/azure/eventhubs.sh staging
#
# Event Hubs speaks the Kafka protocol, so the services use the same client library, the same topic
# names and the same consumer group names as they do against the local broker; only the bootstrap
# address and the security settings differ. That keeps "it works locally" meaningful.
#
# Idempotent: every create here is skipped when the resource already exists.
#
# Tier note: the Kafka endpoint requires the **Standard** tier. Basic does not expose it at all, so
# unlike the App Service plans this cannot be free. Standard bills per hour for the namespace plus
# per throughput unit; one throughput unit is far beyond what this system sends.
set -euo pipefail
cd "$(dirname "$0")"

ENV="${1:-}"
case "$ENV" in
  staging|production) ;;
  *) echo "usage: $0 <staging|production>" >&2; exit 1 ;;
esac

# "production" -> "prod" in resource names, matching the App Service naming.
if [ "$ENV" = production ]; then SHORT=prod; else SHORT="$ENV"; fi

LOCATION="${LOCATION:-southeastasia}"
RESOURCE_GROUP="${RESOURCE_GROUP:-rg-processing-$SHORT}"
NAMESPACE="${EVENTHUBS_NAMESPACE:-evhns-wonrich-processing-$SHORT}"

# Event Hubs caps retention at 7 days on Standard. The dead-letter topics ask for 30 locally and
# cannot have it here; see docs/kafka.md.
MAX_RETENTION_DAYS=7

echo "== $ENV: Event Hubs namespace $NAMESPACE in $RESOURCE_GROUP ($LOCATION)"

az group create --name "$RESOURCE_GROUP" --location "$LOCATION" --output none

if ! az eventhubs namespace show --name "$NAMESPACE" --resource-group "$RESOURCE_GROUP" --output none 2>/dev/null; then
  az eventhubs namespace create \
    --name "$NAMESPACE" --resource-group "$RESOURCE_GROUP" \
    --location "$LOCATION" --sku Standard --enable-kafka true \
    --output none
fi

# An event hub is a Kafka topic; a namespace is the cluster. Names are identical to the local
# broker's so nothing has to be translated between environments.
definitions=../kafka/topics.env
[ -f "$definitions" ] || { echo "cannot find $definitions" >&2; exit 1; }

while IFS=: read -r name partitions retention_ms; do
  case "${name# }" in ''|'#'*) continue ;; esac

  # topics.env holds milliseconds, because that is what Kafka takes; Event Hubs takes whole days.
  days=$(( retention_ms / 86400000 ))
  [ "$days" -lt 1 ] && days=1
  [ "$days" -gt "$MAX_RETENTION_DAYS" ] && days=$MAX_RETENTION_DAYS

  if ! az eventhubs eventhub show --name "$name" --namespace-name "$NAMESPACE" \
       --resource-group "$RESOURCE_GROUP" --output none 2>/dev/null; then
    az eventhubs eventhub create \
      --name "$name" --namespace-name "$NAMESPACE" --resource-group "$RESOURCE_GROUP" \
      --partition-count "$partitions" --retention-time "$days" --cleanup-policy Delete \
      --output none
  fi
  echo "   topic $name  partitions=$partitions retention=${days}d"
done < "$definitions"

# Consumer groups are declared per hub. Event Hubs always has a $Default group; the services use
# named ones so two consumers of the same topic keep separate offsets.
declare_group() {  # declare_group <hub> <group>
  if ! az eventhubs eventhub consumer-group show \
       --consumer-group-name "$2" --eventhub-name "$1" \
       --namespace-name "$NAMESPACE" --resource-group "$RESOURCE_GROUP" --output none 2>/dev/null; then
    az eventhubs eventhub consumer-group create \
      --consumer-group-name "$2" --eventhub-name "$1" \
      --namespace-name "$NAMESPACE" --resource-group "$RESOURCE_GROUP" --output none
  fi
  echo "   group $2 on $1"
}

declare_group wonrich.intake.lab-results.v1      processing-lab-results
declare_group wonrich.processing.stage-events.v1 processing-stage-events
declare_group wonrich.processing.hold-events.v1  processing-hold-events

cat <<DONE

== done. The connection string is a secret: read it when you need it, do not save it to a file.

   az eventhubs namespace authorization-rule keys list \\
     --namespace-name $NAMESPACE --resource-group $RESOURCE_GROUP \\
     --name RootManageSharedAccessKey --query primaryConnectionString -o tsv

   Then set it on the App Service, with the username being that literal token:

     Kafka__BootstrapServers = $NAMESPACE.servicebus.windows.net:9093
     Kafka__SecurityProtocol = SaslSsl
     Kafka__SaslMechanism    = Plain
     Kafka__SaslUsername     = \$ConnectionString
     Kafka__SaslPassword     = <the connection string>

   RootManageSharedAccessKey grants manage, send and listen across the whole namespace. Narrow it
   to per-hub send/listen rules before this carries anything real, so that a leaked service
   credential cannot delete topics.
DONE
