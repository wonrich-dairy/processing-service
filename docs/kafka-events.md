# Kafka Events - Processing Service - SCRUM-68

## Overview

Processing Service publishes 5 event types to Kafka for Traceability Service to build batch timeline behind QCO dashboard.

**Why publish instead of query:** If Traceability queried Processing, dashboard load would put load on service technician uses on factory floor, NFR1 2-second commitment at mercy of dashboard viewers. Publishing inverts: Processing writes record, publishes, returns immediately. Traceability consumes at own pace into own store, answers dashboard queries without touching Processing. If Traceability down for hour, events accumulate in topic and consumed on restart - factory floor never notices.

**Ordering:** Event published only after DB transaction commits. Publishing inside transaction means rollback leaves Traceability holding record of stage that never happened - no way to retract. Publishing after commit without protection means broker outage loses event permanently. Outbox approach handles both: write event to table in same transaction as record, then relay to Kafka separately. Mechanism belongs in shared client library under SCRUM-58; this story consumes it correctly.

**Failure must not propagate backwards:** Broker outage cannot fail technician's data entry. Record saved, event retried, after retry limit marked Poisoned in outbox_messages for human review (no producer-side DLQ - a DLQ topic on the same unreachable broker fails identically).

**Contracts are shared, not local:** Events are interface between Processing and every future consumer, so schemas live in shared library and are versioned.

Depends on: SCRUM-88 (topics), SCRUM-58 (outbox shared client)

---

## Topics (from wonrich-infra/kafka/topics.env)

| Topic | Owner | Events | Key | Partitions | Retention |
|---|---|---|---|---|---|
| `wonrich.processing.stage-events.v1` | Processing Service | MilkAllocatedToMixingTank, ProcessingStageRecorded, ProcessingCompleted | batchId | 3 | 7 days |
| `wonrich.processing.hold-events.v1` | Processing Service | ProcessingHoldRaised, ProcessingHoldResolved | batchId | 3 | 7 days |
| `wonrich.dlq.processing-stage-events.v1` | DLQ | Unprocessable stage events parked by the consumer (consumer-side convention; producer failures stay in outbox_messages as Poisoned) | batchId | 1 | 30 days |
| `wonrich.dlq.processing-hold-events.v1` | DLQ | Unprocessable hold events parked by the consumer (consumer-side convention; producer failures stay in outbox_messages as Poisoned) | batchId | 1 | 30 days |

---

## Event Contracts - Versioned in Shared Library

**Location:** `src/ProcessingService/Domain/Events/ProcessingEvents.cs` - simulates shared library under SCRUM-58

**Versioning:** v1 initial, breaking change gets new topic .v2, non-breaking (optional field) stays same version

**Common fields per AC:** Every event carries batch ID, dispatch number, timestamp, deviation flag

### 1. MilkAllocatedToMixingTank

**Published on:** Allocation ST->MT in `TankAllocationService.AllocateAsync`

**Carries:** Both tank identifiers, quantity, product line per AC

```json
{
  "batchId": "258-DY-A",
  "dispatchNumber": "DN-20260910-01",
  "timestampUtc": "2026-09-22T10:00:00Z",
  "isDeviation": false,
  "schemaVersion": "v1",
  "correlationId": "guid",
  "sourceStoringTankId": "guid",
  "sourceStoringTankCode": "ST-01",
  "destinationMixingTankId": "guid",
  "destinationMixingTankCode": "MT-01",
  "quantityKg": 500.00,
  "productType": "DY",
  "batchCode": "258-DY-A",
  "batchNumber": 258,
  "batchLetter": "A",
  "allocatedAtUtc": "2026-09-22T10:00:00Z",
  "allocatedBy": "userId",
  "overrideReason": null
}
```

**Key:** batchCode for ordering per batch

### 2. ProcessingStageRecorded

**Published per stage:** Heating, Homogeniser, Pasteuriser, Cooling - on Start and End

**Carries:** Stage type, temperature, timings, deviation flag per AC

```json
{
  "batchId": "258-DY-A",
  "dispatchNumber": "DN-20260910-01",
  "timestampUtc": "2026-09-22T10:05:00Z",
  "isDeviation": false,
  "schemaVersion": "v1",
  "correlationId": "guid",
  "mixingTankId": "guid",
  "mixingTankCode": "MT-01",
  "stageType": "Heating",
  "startTimeUtc": "2026-09-22T10:00:00Z",
  "endTimeUtc": "2026-09-22T10:05:00Z",
  "endTemperatureC": 60.5,
  "durationMinutes": 5,
  "cultureAdded": false,
  "recordedBy": "userId"
}
```

**Key:** batchId

### 3. ProcessingCompleted

**Published when run closes:** Cooling ended

```json
{
  "batchId": "258-DY-A",
  "dispatchNumber": "DN-20260910-01",
  "timestampUtc": "2026-09-22T11:00:00Z",
  "isDeviation": false,
  "schemaVersion": "v1",
  "correlationId": "guid",
  "mixingTankId": "guid",
  "mixingTankCode": "MT-01",
  "completedAtUtc": "2026-09-22T11:00:00Z",
  "totalDurationMinutes": 60,
  "hasDeviation": false,
  "completedBy": "userId"
}
```

**Key:** batchId

### 4. ProcessingHoldRaised

**Published with reason:** When quality test fails, run goes OnHold

```json
{
  "batchId": "DN-20260910-01",
  "dispatchNumber": "DN-20260910-01",
  "timestampUtc": "2026-09-22T09:00:00Z",
  "isDeviation": true,
  "schemaVersion": "v1",
  "correlationId": "guid",
  "holdId": "processingRunGuid",
  "reason": "Failed: COB Positive - clotted on boiling",
  "raisedAtUtc": "2026-09-22T09:00:00Z",
  "raisedBy": "labTechUserId",
  "failedParameter": "COB",
  "failedValue": "Positive"
}
```

**Key:** batchId or dispatchNumber

### 5. ProcessingHoldResolved

**Published with reason and resolution:** When hold resolved via retest passing

```json
{
  "batchId": "DN-20260910-01",
  "dispatchNumber": "DN-20260910-01",
  "timestampUtc": "2026-09-22T10:00:00Z",
  "isDeviation": false,
  "schemaVersion": "v1",
  "correlationId": "guid",
  "holdId": "processingRunGuid",
  "reason": "Previous hold",
  "resolution": "Retest passed - quality Accept",
  "raisedAtUtc": "2026-09-22T09:00:00Z",
  "resolvedAtUtc": "2026-09-22T10:00:00Z",
  "resolvedBy": "labTechUserId"
}
```

**Key:** batchId or dispatchNumber

---

## Outbox Pattern - Publish-After-Commit

**Table:** `outbox_messages` - SCRUM-58 mechanism

**Flow:**
1. Technician enters data: allocation, stage, hold
2. In same DB transaction: write record + write outbox message (IOutboxWriter.WriteAsync + SaveChanges)
3. Transaction commits → record saved, outbox message Pending
4. OutboxRelayService background polls every 5s, batch 20, ordered by CreatedAtUtc FIFO
5. Relay calls IKafkaProducer.PublishAsync with topic, key, payload, headers (correlationId)
6. If publish succeeds → mark Processed, ProcessedAtUtc = now
7. If publish fails → RetryCount++, LastError, retry up to MaxRetries 5
8. After MaxRetries → mark row Poisoned in outbox_messages (terminal, visible, left for human review; requeue via Status='Pending', RetryCount=0. No producer-side DLQ - DLQ on unreachable broker fails identically)
9. Broker outage → DB write still succeeds (outbox in same transaction already committed), relay retries later - factory floor never notices
10. OutboxCleanupService runs daily: deletes Processed rows older than `Outbox:ProcessedRetentionDays` (default 7) so the table does not grow forever. Poisoned rows are never auto-deleted - they are the failure record for human review. Uses existing ix_outbox_status index, no extra schema (review fix: index + cleanup; extra (Status, ProcessedAtUtc) index only needed if volume ever demands it)

**Rollback case:** If DB transaction fails (e.g., concurrency, validation), outbox message not committed → no event published - tested via unit test forcing transaction failure

**Broker-unavailable case:** Stop broker `docker compose stop kafka` in wonrich-infra, do DB write (allocation), verify DB write succeeds and outbox message stays Pending with RetryCount increasing, then start broker `docker compose up -d kafka`, verify relay publishes and marks Processed

**Correlation ID:** Guid per event, present in message headers as `x-correlation-id` - the ONE canonical header name (review fix: legacy `correlationId` header removed; name matches KafkaCorrelationHelper.KafkaHeaderName and Processing structured logs, which Loki queries key off). Also in payload as `correlationId` contract field. Consumers must read `x-correlation-id`.

---

## Consumer - Traceability Service

Traceability consumes at own pace into own store, answers dashboard queries without touching Processing.

**Delivery guarantee + dedupe:** Relay is at-least-once (crash between broker publish and outbox commit can republish the same event). Every event carries a unique `eventId` (payload field + Kafka header). Consumers must keep a seen-`eventId` window (e.g. recent IDs in consumer store) and skip duplicates before side effects.

**Consumer groups (from wonrich-infra/kafka/consumer-groups.env):**
- `processing-stage-events` consumes `wonrich.processing.stage-events.v1` → DLQ `wonrich.dlq.processing-stage-events.v1`
- `processing-hold-events` consumes `wonrich.processing.hold-events.v1` → DLQ `wonrich.dlq.processing-hold-events.v1`

**Reconstruction:** Consumer should be able to reconstruct consignment's path from events alone, without calling back into Processing for detail - because events carry batch ID, dispatch number, timestamp, deviation flag, tank identifiers, quantity, product line, stage timings, etc.

---

## Testing - DOD

- Events consumed successfully by another service on staging, demonstrated end to end (use kafka-console-consumer or Traceability service)
- Rollback case tested - forced transaction failure produces no event (unit test)
- Broker-unavailable case tested by stopping broker; DB write still succeeds and event retried (manual test + unit test)
- Correlation ID present in headers as `x-correlation-id`, traceable in Loki (check logs for correlationId property)
- Contracts documented in shared library (this doc + Domain/Events)
- Unit tests cover publish-after-commit path and failure path (tests/ProcessingService.Tests/Kafka/OutboxTests.cs)
- Outbox backlog gauge `processing_outbox_backlog` (Pending + Poisoned) exposed on /metrics in Prometheus format for the SCRUM-111 Grafana dashboard (review fix #11: sustained Pending growth = broker outage or relay down; Poisoned > 0 = needs human review)
- Code merged via reviewed PR

---

## Local Verification

```bash
# Start shared infra first
cd ../../wonrich-infra
docker compose up -d
docker compose ps # wonrich-kafka healthy
docker compose logs kafka-init # topics created

# Start processing service
cd ../processing-check
cp .env.example .env # fill DB connection
docker compose up -d
docker compose logs processing-service # OutboxRelayService started

# Produce allocation, stage, hold via API or UI, then consume
docker exec wonrich-kafka /opt/kafka/bin/kafka-console-consumer.sh \
  --bootstrap-server localhost:9092 \
  --topic wonrich.processing.stage-events.v1 \
  --group traceability-test --from-beginning

# Check outbox table
# SELECT * FROM outbox_messages ORDER BY CreatedAtUtc DESC LIMIT 10;
# Should show Pending -> Processed after relay

# Test broker unavailable
docker compose -f ../../wonrich-infra/docker-compose.yml stop kafka
# Do allocation via UI - should succeed DB write, outbox Pending
# SELECT Status, RetryCount FROM outbox_messages WHERE Status='Pending';
docker compose -f ../../wonrich-infra/docker-compose.yml up -d kafka
# Wait 5s, check outbox Processed
```

---

## Related Work Items

- SCRUM-88: Kafka topics and consumer groups (wonrich-infra)
- SCRUM-58: Shared client library outbox mechanism
- SCRUM-68: This story
- SCRUM-110: Quality Lab topics
