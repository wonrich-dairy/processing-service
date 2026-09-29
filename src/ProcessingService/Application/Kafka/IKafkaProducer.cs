namespace ProcessingService.Application.Kafka;

/// <summary>
/// Kafka producer abstraction - shared client library under SCRUM-58
/// Publishes events to Kafka with correlation ID in headers, traceable in Loki
/// </summary>
public interface IKafkaProducer
{
    /// <summary>
    /// Publish event to Kafka topic with key for ordering, payload JSON, headers including correlationId
    /// Returns true if published, false if failed (should retry)
    /// </summary>
    Task<bool> PublishAsync(string topic, string key, string payloadJson, string headersJson, string correlationId, CancellationToken cancellationToken);
}
