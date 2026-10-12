namespace Wonrich.Processing.Contracts.Events;

/// <summary>
/// Base for all processing events - carries common fields per AC: batch ID, dispatch number, timestamp
/// </summary>
public abstract record ProcessingEventBase
{
    /// <summary>Unique event ID for consumer-side deduplication - relay is at-least-once (crash between publish and outbox commit can republish), consumers keep a seen-EventId window and skip duplicates</summary>
    public Guid EventId { get; init; } = Guid.NewGuid();

    /// <summary>Batch code [day]-[product]-[letter] e.g. 258-DY-A - business identifier, message key for ordering per key</summary>
    public string BatchId { get; init; } = string.Empty;

    /// <summary>MCC dispatch number e.g. DN-20260910-01 - traceability query filters on this</summary>
    public string DispatchNumber { get; init; } = string.Empty;

    /// <summary>UTC timestamp of event</summary>
    public DateTime TimestampUtc { get; init; }

    /// <summary>Deviation flag - true if temperature out of range or other deviation</summary>
    public bool IsDeviation { get; init; }

    /// <summary>Schema version - v1</summary>
    public string SchemaVersion { get; init; } = "v1";

    /// <summary>Correlation ID for tracing across hop in Loki - Kafka header x-correlation-id (single canonical name), also this payload field</summary>
    public string CorrelationId { get; init; } = string.Empty;
}
