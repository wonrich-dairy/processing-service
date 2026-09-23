using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProcessingService.Infrastructure.Persistence;

namespace ProcessingService.Application.Kafka;

/// <summary>
/// Outbox relay background service - polls outbox_messages table and publishes to Kafka
/// SCRUM-68: Events published only after DB transaction commits (outbox pattern)
/// Broker unavailability does not fail originating DB write - relay retries separately
/// Failed publishes retried, then routed to DLQ
/// Correlation ID present in headers, traceable in Loki
/// </summary>
public sealed class OutboxRelayService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OutboxRelayService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(5); // Poll every 5s
    private const int BatchSize = 20;
    private const int MaxRetries = 5;

    public OutboxRelayService(IServiceProvider serviceProvider, ILogger<OutboxRelayService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OutboxRelayService started - polling outbox_messages every {Interval}s, batch {BatchSize}, max retries {MaxRetries}",
            _interval.TotalSeconds, BatchSize, MaxRetries);

        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing outbox batch");
            }
            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProcessingDbContext>();
        var producer = scope.ServiceProvider.GetRequiredService<IKafkaProducer>();

        List<Domain.Entities.OutboxMessage> pending;
        try
        {
            // Get pending messages ordered by CreatedAtUtc (FIFO) - publish-after-commit order
            pending = await db.OutboxMessages
                .Where(m => m.Status == "Pending")
                .OrderBy(m => m.CreatedAtUtc)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex) when (ex.Message.Contains("outbox_messages") && ex.Message.Contains("doesn't exist"))
        {
            _logger.LogWarning("Outbox table not yet created - migration pending, will retry after migration applied");
            return;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query outbox_messages - will retry, may be migration pending");
            return;
        }

        if (pending.Count == 0)
            return;

        _logger.LogDebug("Processing {Count} outbox messages", pending.Count);

        foreach (var message in pending)
        {
            using var _ = _logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = message.CorrelationId, ["EventType"] = message.EventType, ["Topic"] = message.Topic });
            try
            {
                var success = await producer.PublishAsync(
                    message.Topic,
                    message.Key,
                    message.Payload,
                    message.HeadersJson,
                    message.CorrelationId,
                    cancellationToken);

                if (success)
                {
                    message.ProcessedAtUtc = DateTime.UtcNow;
                    message.Status = "Processed";
                    message.LastError = null;
                    _logger.LogInformation("Outbox message {Id} published to {Topic} key={Key} correlationId={CorrelationId}",
                        message.Id, message.Topic, message.Key, message.CorrelationId);
                }
                else
                {
                    message.RetryCount++;
                    message.LastError = $"Publish failed, retry {message.RetryCount}/{MaxRetries}";

                    if (message.RetryCount >= MaxRetries)
                    {
                        // Route to DLQ after retry limit
                        _logger.LogWarning("Outbox message {Id} failed after {MaxRetries} retries, routing to DLQ, topic={Topic} key={Key}",
                            message.Id, MaxRetries, message.Topic, message.Key);

                        var dlqSuccess = await producer.PublishToDlqAsync(
                            message.Topic,
                            message.Key,
                            message.Payload,
                            message.HeadersJson,
                            message.CorrelationId,
                            message.LastError,
                            cancellationToken);

                        if (dlqSuccess)
                        {
                            message.Status = "Failed"; // Failed but routed to DLQ
                            message.ProcessedAtUtc = DateTime.UtcNow;
                        }
                        else
                        {
                            // DLQ also failed, keep as Pending for next retry cycle
                            _logger.LogError("Failed to publish outbox message {Id} to DLQ, will retry", message.Id);
                        }
                    }
                    else
                    {
                        _logger.LogInformation("Outbox message {Id} publish failed, will retry {RetryCount}/{MaxRetries}",
                            message.Id, message.RetryCount, MaxRetries);
                    }
                }
            }
            catch (Exception ex)
            {
                message.RetryCount++;
                message.LastError = ex.Message;

                _logger.LogWarning(ex, "Exception publishing outbox message {Id} topic={Topic} key={Key} retry {RetryCount}",
                    message.Id, message.Topic, message.Key, message.RetryCount);

                if (message.RetryCount >= MaxRetries)
                {
                    try
                    {
                        var dlqSuccess = await producer.PublishToDlqAsync(
                            message.Topic,
                            message.Key,
                            message.Payload,
                            message.HeadersJson,
                            message.CorrelationId,
                            ex.Message,
                            cancellationToken);

                        if (dlqSuccess)
                        {
                            message.Status = "Failed";
                            message.ProcessedAtUtc = DateTime.UtcNow;
                        }
                    }
                    catch (Exception dlqEx)
                    {
                        _logger.LogError(dlqEx, "Failed to publish outbox message {Id} to DLQ", message.Id);
                    }
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        if (pending.Any(m => m.Status == "Processed"))
            _logger.LogInformation("Processed {Count} outbox messages, {Pending} still pending",
                pending.Count(m => m.Status == "Processed"), pending.Count(m => m.Status == "Pending"));
    }
}
