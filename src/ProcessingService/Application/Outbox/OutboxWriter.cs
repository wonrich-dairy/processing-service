using System.Text.Json;
using ProcessingService.Domain.Entities;
using ProcessingService.Domain.Events;
using ProcessingService.Infrastructure.Persistence;

namespace ProcessingService.Application.Outbox;

/// <summary>
/// Outbox writer implementation - writes to outbox_messages table in same transaction
/// No Kafka call here, only DB write - ensures publish-after-commit and rollback produces no event
/// </summary>
public sealed class OutboxWriter : IOutboxWriter
{
    private readonly ProcessingDbContext _db;
    private readonly TimeProvider _time;

    public OutboxWriter(ProcessingDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    public async Task WriteAsync<TEvent>(string topic, string key, TEvent @event, string correlationId, CancellationToken cancellationToken) where TEvent : ProcessingEventBase
    {
        var payloadJson = JsonSerializer.Serialize(@event, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var headers = new Dictionary<string, string>
        {
            ["correlationId"] = correlationId,
            ["eventType"] = @event.GetType().Name,
            ["schemaVersion"] = @event.SchemaVersion,
            ["timestamp"] = @event.TimestampUtc.ToString("o")
        };
        var headersJson = JsonSerializer.Serialize(headers);

        await WriteAsync(topic, key, @event.GetType().Name, payloadJson, headersJson, correlationId, cancellationToken);
    }

    public Task WriteAsync(string topic, string key, string eventType, string payloadJson, string headersJson, string correlationId, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow().UtcDateTime;

        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Topic = topic,
            Key = key,
            EventType = eventType,
            Payload = payloadJson,
            HeadersJson = headersJson,
            CreatedAtUtc = now,
            ProcessedAtUtc = null,
            RetryCount = 0,
            LastError = null,
            Status = "Pending",
            CorrelationId = correlationId
        };

        _db.OutboxMessages.Add(message);
        // Note: SaveChangesAsync is NOT called here - caller must SaveChanges in same transaction as record
        // This ensures event published only after DB transaction commits
        return Task.CompletedTask;
    }
}
