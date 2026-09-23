using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ProcessingService.Application.Kafka;

/// <summary>
/// Kafka producer implementation using Confluent.Kafka
/// SCRUM-68: Broker unavailability does not fail originating DB write - producer only called from OutboxRelayService after commit
/// Failed publishes retried, then routed to DLQ
/// Correlation ID present in message headers, traceable across hop in Loki
/// </summary>
public sealed class KafkaProducer : IKafkaProducer, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly ILogger<KafkaProducer> _logger;
    private readonly IConfiguration _config;

    public KafkaProducer(IConfiguration config, ILogger<KafkaProducer> logger)
    {
        _config = config;
        _logger = logger;

        var bootstrapServers = config["Kafka:BootstrapServers"] ?? config["Kafka__BootstrapServers"] ?? "localhost:29092";
        var securityProtocol = config["Kafka:SecurityProtocol"] ?? config["Kafka__SecurityProtocol"] ?? "Plaintext";
        var saslMechanism = config["Kafka:SaslMechanism"] ?? config["Kafka__SaslMechanism"];
        var saslUsername = config["Kafka:SaslUsername"] ?? config["Kafka__SaslUsername"];
        var saslPassword = config["Kafka:SaslPassword"] ?? config["Kafka__SaslPassword"];

        var producerConfig = new ProducerConfig
        {
            BootstrapServers = bootstrapServers,
            ClientId = "processing-service",
            Acks = Acks.All, // Wait for all replicas - ensures durability
            MessageTimeoutMs = 10000, // 10s timeout
            EnableIdempotence = true, // Idempotent producer for exactly-once per partition
            CompressionType = CompressionType.Snappy,
            // Security
            SecurityProtocol = Enum.TryParse<SecurityProtocol>(securityProtocol, true, out var sp) ? sp : SecurityProtocol.Plaintext,
        };

        if (!string.IsNullOrWhiteSpace(saslMechanism) && Enum.TryParse<SaslMechanism>(saslMechanism, true, out var sm))
        {
            producerConfig.SaslMechanism = sm;
            producerConfig.SaslUsername = saslUsername;
            producerConfig.SaslPassword = saslPassword;
        }

        _producer = new ProducerBuilder<string, string>(producerConfig)
            .SetErrorHandler((_, e) => _logger.LogError("Kafka producer error: {Reason}", e.Reason))
            .Build();

        _logger.LogInformation("Kafka producer created - BootstrapServers: {BootstrapServers}, SecurityProtocol: {SecurityProtocol}", bootstrapServers, securityProtocol);
    }

    public async Task<bool> PublishAsync(string topic, string key, string payloadJson, string headersJson, string correlationId, CancellationToken cancellationToken)
    {
        try
        {
            var headers = new Headers();
            headers.Add("correlationId", Encoding.UTF8.GetBytes(correlationId));
            headers.Add("x-correlation-id", Encoding.UTF8.GetBytes(correlationId)); // For Loki tracing per SCRUM-90
            headers.Add("eventType", Encoding.UTF8.GetBytes(ExtractEventType(headersJson)));
            headers.Add("timestamp", Encoding.UTF8.GetBytes(DateTime.UtcNow.ToString("o")));

            // Add all headers from headersJson
            try
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(headersJson);
                if (dict != null)
                {
                    foreach (var kv in dict)
                    {
                        if (kv.Key == "correlationId") continue; // already added
                        headers.Add(kv.Key, Encoding.UTF8.GetBytes(kv.Value ?? ""));
                    }
                }
            }
            catch { /* ignore header parse error */ }

            var message = new Message<string, string>
            {
                Key = key,
                Value = payloadJson,
                Headers = headers,
                Timestamp = new Timestamp(DateTime.UtcNow)
            };

            var result = await _producer.ProduceAsync(topic, message, cancellationToken);

            _logger.LogInformation("Published event to {Topic} key={Key} correlationId={CorrelationId} offset={Offset} partition={Partition}",
                topic, key, correlationId, result.Offset, result.Partition);

            return result.Status == PersistenceStatus.Persisted;
        }
        catch (ProduceException<string, string> ex)
        {
            _logger.LogWarning(ex, "Failed to publish to {Topic} key={Key} correlationId={CorrelationId} - will retry, error: {Error}",
                topic, key, correlationId, ex.Error.Reason);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish to {Topic} key={Key} correlationId={CorrelationId} - will retry",
                topic, key, correlationId);
            return false;
        }
    }

    public async Task<bool> PublishToDlqAsync(string originalTopic, string key, string payloadJson, string headersJson, string correlationId, string lastError, CancellationToken cancellationToken)
    {
        // DLQ topic per consumer group, not per source topic: what matters when replaying is which consumer failed
        // For processing service, DLQ topics: wonrich.dlq.processing-stage-events.v1, wonrich.dlq.processing-hold-events.v1, etc.
        // Map original topic to DLQ topic
        var dlqTopic = MapToDlqTopic(originalTopic);

        try
        {
            var headers = new Headers();
            headers.Add("correlationId", Encoding.UTF8.GetBytes(correlationId));
            headers.Add("originalTopic", Encoding.UTF8.GetBytes(originalTopic));
            headers.Add("lastError", Encoding.UTF8.GetBytes(lastError));
            headers.Add("failedAt", Encoding.UTF8.GetBytes(DateTime.UtcNow.ToString("o")));

            var dlqPayload = new
            {
                originalTopic,
                key,
                payload = JsonSerializer.Deserialize<object>(payloadJson),
                lastError,
                failedAt = DateTime.UtcNow,
                correlationId
            };
            var dlqJson = JsonSerializer.Serialize(dlqPayload);

            var message = new Message<string, string>
            {
                Key = key,
                Value = dlqJson,
                Headers = headers
            };

            var result = await _producer.ProduceAsync(dlqTopic, message, cancellationToken);
            _logger.LogWarning("Routed failed event from {OriginalTopic} to DLQ {DlqTopic} key={Key} correlationId={CorrelationId}",
                originalTopic, dlqTopic, key, correlationId);

            return result.Status == PersistenceStatus.Persisted;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish to DLQ {DlqTopic} for original {OriginalTopic} key={Key}",
                dlqTopic, originalTopic, key);
            return false;
        }
    }

    private static string MapToDlqTopic(string originalTopic)
    {
        // wonrich.processing.stage-events.v1 -> wonrich.dlq.processing-stage-events.v1
        // wonrich.processing.hold-events.v1 -> wonrich.dlq.processing-hold-events.v1
        if (originalTopic.Contains("stage-events"))
            return "wonrich.dlq.processing-stage-events.v1";
        if (originalTopic.Contains("hold-events"))
            return "wonrich.dlq.processing-hold-events.v1";
        if (originalTopic.Contains("lab-results"))
            return "wonrich.dlq.processing-lab-results.v1";
        // Fallback: prefix with dlq
        if (originalTopic.StartsWith("wonrich."))
            return originalTopic.Replace("wonrich.", "wonrich.dlq.");
        return $"wonrich.dlq.{originalTopic}.v1";
    }

    private static string ExtractEventType(string headersJson)
    {
        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(headersJson);
            if (dict != null && dict.TryGetValue("eventType", out var et))
                return et;
        }
        catch { }
        return "Unknown";
    }

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }
}

/// <summary>
/// No-op producer for local dev without Kafka or when Kafka disabled - ensures DB write still succeeds when broker unavailable
/// </summary>
public sealed class NoOpKafkaProducer : IKafkaProducer
{
    private readonly ILogger<NoOpKafkaProducer> _logger;

    public NoOpKafkaProducer(ILogger<NoOpKafkaProducer> logger)
    {
        _logger = logger;
    }

    public Task<bool> PublishAsync(string topic, string key, string payloadJson, string headersJson, string correlationId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("[NoOp] Would publish to {Topic} key={Key} correlationId={CorrelationId} - Kafka disabled or unavailable, treating as success for retry logic",
            topic, key, correlationId);
        // Return false to trigger retry logic, but DB write already succeeded
        // Actually for NoOp we return true to avoid infinite retry in dev without Kafka
        return Task.FromResult(true);
    }

    public Task<bool> PublishToDlqAsync(string originalTopic, string key, string payloadJson, string headersJson, string correlationId, string lastError, CancellationToken cancellationToken)
    {
        _logger.LogWarning("[NoOp] Would publish to DLQ for {OriginalTopic} key={Key} - Kafka disabled", originalTopic, key);
        return Task.FromResult(true);
    }
}
