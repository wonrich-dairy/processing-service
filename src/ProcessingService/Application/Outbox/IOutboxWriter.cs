using ProcessingService.Domain.Events;

namespace ProcessingService.Application.Outbox;

/// <summary>
/// Outbox writer - writes event to outbox table in same transaction as record
/// SCRUM-68: Event must be published only after DB transaction commits; rolled-back write publishes nothing
/// This belongs to shared client library under SCRUM-58, consumed correctly here
/// </summary>
public interface IOutboxWriter
{
    /// <summary>
    /// Write event to outbox in same DbContext transaction - not yet published to Kafka
    /// Actual publish happens in OutboxRelayService after commit
    /// </summary>
    Task WriteAsync<TEvent>(string topic, string key, TEvent @event, string correlationId, CancellationToken cancellationToken) where TEvent : ProcessingEventBase;

    /// <summary>
    /// Write with explicit event type
    /// </summary>
    Task WriteAsync(string topic, string key, string eventType, string payloadJson, string headersJson, string correlationId, CancellationToken cancellationToken);
}
