# Wonrich.Processing.Contracts

Event contracts the Processing service publishes on the Kafka topics
`wonrich.processing.stage-events.v1` and `wonrich.processing.hold-events.v1`.

| Type | `eventType` header | Topic | When |
|---|---|---|---|
| `MilkAllocatedToMixingTankEvent` | `MilkAllocatedToMixingTankEvent` | `wonrich.processing.stage-events.v1` | Milk is allocated from a storing tank to a mixing tank |
| `ProcessingStageRecordedEvent` | `ProcessingStageRecordedEvent` | `wonrich.processing.stage-events.v1` | A stage (Heating, Homogeniser, Pasteuriser, Cooling) starts or ends |
| `ProcessingCompletedEvent` | `ProcessingCompletedEvent` | `wonrich.processing.stage-events.v1` | A run closes (Cooling ended) |
| `ProcessingHoldRaisedEvent` | `ProcessingHoldRaisedEvent` | `wonrich.processing.hold-events.v1` | A quality test fails and the run goes on hold |
| `ProcessingHoldResolvedEvent` | `ProcessingHoldResolvedEvent` | `wonrich.processing.hold-events.v1` | A hold is resolved |

Namespace: `Wonrich.Processing.Contracts.Events`. Every type derives from `ProcessingEventBase`, which carries
`EventId`, `BatchId`, `DispatchNumber`, `TimestampUtc`, `IsDeviation`, `SchemaVersion` and `CorrelationId`.

## Wire format

- JSON, camelCase property names. Deserialize with `JsonSerializerDefaults.Web` (or `JsonNamingPolicy.CamelCase`).
- Message key is `batchId`, so events for one batch stay in order. A hold raised **before** the batch is
  allocated has no batch yet: it carries the dispatch number in `batchId` (and in the key).
- Headers: `x-correlation-id`, `eventId`, `eventType`, `schemaVersion`, `timestamp`.
- Delivery is at-least-once (the outbox relay can republish after a crash). Deduplicate on `eventId`.
- Pick the type from the `eventType` header before deserializing. The header holds the short type name, with no namespace.

```csharp
using System.Text.Json;
using Wonrich.Processing.Contracts.Events;

var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
ProcessingEventBase? evt = eventTypeHeader switch
{
    "MilkAllocatedToMixingTankEvent" => JsonSerializer.Deserialize<MilkAllocatedToMixingTankEvent>(payload, json),
    "ProcessingStageRecordedEvent"   => JsonSerializer.Deserialize<ProcessingStageRecordedEvent>(payload, json),
    "ProcessingCompletedEvent"       => JsonSerializer.Deserialize<ProcessingCompletedEvent>(payload, json),
    "ProcessingHoldRaisedEvent"      => JsonSerializer.Deserialize<ProcessingHoldRaisedEvent>(payload, json),
    "ProcessingHoldResolvedEvent"    => JsonSerializer.Deserialize<ProcessingHoldResolvedEvent>(payload, json),
    _ => null // unknown type on a v1 topic: ignore, do not dead-letter
};
```

## Versioning

The package's major version follows the topics' `schemaVersion`: `1.x.y` is `v1`.
An added optional field is a minor release on the same topics. A breaking change ships as `2.0.0` with new `.v2` topics.
A version is published from a tag `contracts-v<Version>`, and the tag must match `<Version>` in the project file.

## Using the package

The package is on the organisation's GitHub Packages feed, `https://nuget.pkg.github.com/wonrich-dairy/index.json`.
That feed always needs a login, even for public repositories: use a token with `read:packages`. In GitHub Actions the
repository must be granted read access to the package (package settings, Manage Actions access).
