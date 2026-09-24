using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ProcessingService.Application.Kafka;

/// <summary>
/// Kafka producer implementation using Confluent.Kafka
/// SCRUM-68: Broker unavailability does not fail originating DB write - producer only called from OutboxRelayService after commit
/// Failed publishes retried by relay; rows past retry budget marked Poisoned in outbox_messages (review fix: producer-side DLQ removed - DLQ topic on an unreachable broker fails identically)
/// Correlation ID present in message headers, traceable across hop in Loki
/// </summary>
public sealed class KafkaProducer : IKafkaProducer, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly ILogger<KafkaProducer> _logger;

    public KafkaProducer(IConfiguration config, ILogger<KafkaProducer> logger)
    {
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
            // Review fix #9: ONE canonical header name - x-correlation-id (matches KafkaCorrelationHelper.KafkaHeaderName and Processing structured logs, which Loki queries key off)
            headers.Add("x-correlation-id", Encoding.UTF8.GetBytes(correlationId));
            headers.Add("eventType", Encoding.UTF8.GetBytes(ExtractEventType(headersJson)));
            headers.Add("timestamp", Encoding.UTF8.GetBytes(DateTime.UtcNow.ToString("o")));

            // Add remaining headers from headersJson
            try
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(headersJson);
                if (dict != null)
                {
                    // Skip keys already set explicitly above, plus the legacy "correlationId" name found in outbox rows stored before the rename
                    // (also fixes pre-existing duplicate eventType/timestamp headers)
                    var reserved = new HashSet<string>(StringComparer.Ordinal) { "x-correlation-id", "correlationId", "eventType", "timestamp" };
                    foreach (var kv in dict)
                    {
                        if (reserved.Contains(kv.Key)) continue;
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
/// No-op producer used when Kafka is not configured (local dev).
/// SCRUM-68 review fix: returns FALSE - outbox rows must stay Pending, never marked Processed.
/// Marking them success would silently lose events (reviewer blocking point #1).
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
        // Return false: relay keeps the row Pending and retries - event is NOT lost, it publishes when Kafka is configured
        _logger.LogWarning("[NoOp] Kafka not configured - event for {Topic} key={Key} correlationId={CorrelationId} stays PENDING in outbox_messages (not lost, will publish once Kafka is configured)",
            topic, key, correlationId);
        return Task.FromResult(false);
    }
}
