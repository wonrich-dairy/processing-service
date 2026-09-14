# Kafka — topics, consumer groups and connection settings (SCRUM-88)

Wonrich services talk to each other over Kafka. Locally that is a single-node broker in this
repository's compose stack; on staging it is Azure Event Hubs, which speaks the Kafka protocol, so
the client library, the topic names and the consumer group names are identical in both. Only the
bootstrap address and the security settings change.

## Running the broker locally

```bash
docker compose up -d          # broker, topic creation, and the service
docker compose logs kafka-init  # what was created
```

The broker is on `localhost:29092` from your machine and `kafka:9092` from inside the compose
network. Both are the same broker; the two addresses exist because a client is told where to
reconnect after its first metadata call, and `kafka` does not resolve outside Docker.

Topics are created by `infra/kafka/create-topics.sh`, which runs as the `kafka-init` container on
every `up`. It uses `--if-not-exists`, so it never touches a topic that is already there. Run it by
hand against any broker with:

```bash
KAFKA_BOOTSTRAP=localhost:29092 ./infra/kafka/create-topics.sh
```

Topic configuration survives a restart — the broker's data is in the `wonrich-kafka-data` volume,
not the container. `docker compose restart`, `docker compose down`, and a machine reboot all keep
it. `docker compose down -v` is the deliberate way to start from nothing.

## Naming convention

```
wonrich.<owning service>.<events, plural>.v<schema version>
```

| Part | Rule |
|---|---|
| `wonrich` | Every topic, so a shared broker stays legible if another system ever joins it. |
| owning service | The service that **produces** it. One producer per topic; anyone may consume. |
| events | Plural, hyphenated, describing what happened rather than what to do. A topic is a record of facts, not a work queue. |
| version | Bumped when the message shape changes incompatibly. |

A version bump means a new topic, both running side by side while consumers move across. Renaming
or reusing a topic for a changed shape breaks every consumer at once, at a moment nobody chose.

Dead-letter topics are named for the **consumer group** that failed, not the source topic:

```
wonrich.dlq.<consumer group>.v1
```

When a message is replayed, what matters is which consumer could not handle it, because that is
the code being fixed. A DLQ per source topic would mix failures from unrelated consumers.

## The topics

Defined in [`infra/kafka/topics.env`](../infra/kafka/topics.env), which both the local script and
the Azure provisioning script read — so the two environments cannot drift by someone editing one.

| Topic | Partitions | Retention | Produced by |
|---|---|---|---|
| `wonrich.intake.lab-results.v1` | 3 | 7 days | MCC & Intake Service |
| `wonrich.processing.stage-events.v1` | 3 | 7 days | Processing Service |
| `wonrich.processing.hold-events.v1` | 3 | 7 days | Processing Service |
| `wonrich.dlq.processing-lab-results.v1` | 1 | 30 days local / 7 staging | Processing Service |
| `wonrich.dlq.processing-stage-events.v1` | 1 | 30 days local / 7 staging | Processing Service |
| `wonrich.dlq.processing-hold-events.v1` | 1 | 30 days local / 7 staging | Processing Service |

Partition count is the ceiling on how many consumers in one group can read in parallel. It can be
raised later but never lowered, and raising it changes which partition a key lands on — so ordering
guarantees per key only hold either side of that change, not across it. Three is ample here.

> **Staging keeps dead letters for 7 days, not 30.** Event Hubs caps retention at 7 days on the
> Standard tier. A dead letter that nobody looks at within a week is lost on staging. That is
> acceptable while staging carries no real data; production would need the Premium tier, or a
> consumer that copies dead letters somewhere durable.

## Consumer groups

| Group | Reads | Dead letters to |
|---|---|---|
| `processing-lab-results` | `wonrich.intake.lab-results.v1` | `wonrich.dlq.processing-lab-results.v1` |
| `processing-stage-events` | `wonrich.processing.stage-events.v1` | `wonrich.dlq.processing-stage-events.v1` |
| `processing-hold-events` | `wonrich.processing.hold-events.v1` | `wonrich.dlq.processing-hold-events.v1` |

A group is the unit of offset tracking: every consumer in a group shares one position in the topic,
and a second group reads the same messages independently. Two services that both need lab results
must use different groups, or they will steal each other's messages.

## Connection settings

Supplied as configuration, never committed. The key names match the ASP.NET Core configuration
binder's double-underscore convention, so they work as App Service settings unchanged.

| Setting | Local | Staging (Event Hubs) |
|---|---|---|
| `Kafka__BootstrapServers` | `kafka:9092` in compose, `localhost:29092` from the host | `<namespace>.servicebus.windows.net:9093` |
| `Kafka__SecurityProtocol` | `Plaintext` | `SaslSsl` |
| `Kafka__SaslMechanism` | — | `Plain` |
| `Kafka__SaslUsername` | — | `$ConnectionString` — the literal string, not a variable |
| `Kafka__SaslPassword` | — | the namespace connection string (secret) |

`$ConnectionString` is Event Hubs' convention: the username is that literal token and the password
carries the real credential. In a shell, quote it — `'$ConnectionString'` — or it expands to
nothing and authentication fails with an unhelpful error.

Plaintext locally is deliberate: the broker is not published beyond your machine, and making every
developer manage certificates to read their own test messages buys nothing.

## Provisioning staging

```bash
az login
./infra/azure/eventhubs.sh staging
```

Creates the namespace, one event hub per topic with matching partitions and retention, and the
consumer groups. Then read the connection string and set it on the App Service:

```bash
conn=$(az eventhubs namespace authorization-rule keys list \
  --namespace-name evhns-wonrich-processing-staging \
  --resource-group rg-processing-staging \
  --name RootManageSharedAccessKey --query primaryConnectionString -o tsv)

az webapp config appsettings set --name app-wonrich-processing-staging \
  --resource-group rg-processing-staging --output none --settings \
  "Kafka__BootstrapServers=evhns-wonrich-processing-staging.servicebus.windows.net:9093" \
  "Kafka__SecurityProtocol=SaslSsl" \
  "Kafka__SaslMechanism=Plain" \
  "Kafka__SaslUsername=\$ConnectionString" \
  "Kafka__SaslPassword=$conn"
```

> `RootManageSharedAccessKey` grants manage, send and listen across the whole namespace. It is
> fine for getting staging working; narrow it to per-hub send/listen rules before this carries
> anything real, so a leaked service credential cannot delete topics.

## Proving it end to end

Against the local broker, using the tools already inside the container:

```bash
docker exec -it wonrich-kafka /opt/kafka/bin/kafka-console-producer.sh \
  --bootstrap-server localhost:9092 --topic wonrich.processing.stage-events.v1

docker exec -it wonrich-kafka /opt/kafka/bin/kafka-console-consumer.sh \
  --bootstrap-server localhost:9092 --topic wonrich.processing.stage-events.v1 --from-beginning
```

Against staging, point the same tools at the Event Hubs endpoint with a client properties file
carrying the SASL settings above.

## What this ticket does not do

There is no producer or consumer in the service yet — this is the broker, the topics and the
configuration they need. Publishing stage events and consuming lab results are their own stories,
and the `Kafka__*` settings are read by nothing until then.
