namespace ProcessingService.Domain.Entities;

/// <summary>
/// Outbox pattern for publish-after-commit - SCRUM-68, SCRUM-58 shared client library mechanism
/// Write event to table in same transaction as record, then relay to Kafka separately
/// Ensures: rollback produces no event, broker outage does not fail DB write, retry + DLQ
/// </summary>
public class OutboxMessage
{
    public Guid Id { get; set; }

    /// <summary>Topic e.g. wonrich.processing.stage-events.v1</summary>
    public string Topic { get; set; } = string.Empty;

    /// <summary>Message key for ordering per key e.g. batchId or dispatchNumber</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Event type e.g. MilkAllocatedToMixingTank</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>JSON payload - versioned contract from Domain.Events</summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>Headers JSON - includes correlationId, traceable in Loki</summary>
    public string HeadersJson { get; set; } = string.Empty;

    /// <summary>Created UTC - when event was written in same transaction as record</summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Processed UTC - when relay successfully published to Kafka, null if pending</summary>
    public DateTime? ProcessedAtUtc { get; set; }

    /// <summary>Retry count - failed publishes retried, then DLQ</summary>
    public int RetryCount { get; set; }

    /// <summary>Last error message for debugging</summary>
    public string? LastError { get; set; }

    /// <summary>Status: Pending (awaiting publish), Processed (published), Poisoned (past retry budget, needs human review - requeue via Status='Pending', RetryCount=0)</summary>
    public string Status { get; set; } = "Pending"; // Pending, Processed, Poisoned

    /// <summary>Correlation ID for tracing across hop</summary>
    public string CorrelationId { get; set; } = string.Empty;
}
