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
/// Failed publishes retried up to MaxRetries, then marked Poisoned in outbox_messages for human review (review fix: no producer-side DLQ)
/// Rows claimed via SELECT ... FOR UPDATE SKIP LOCKED inside one READ COMMITTED transaction (review fix: two relay instances never double-publish; at-least-once delivery, consumers dedupe on eventId)
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
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx;
        try
        {
            // Review fix #7: claim rows with FOR UPDATE SKIP LOCKED (MySQL 8.0.1+) so two relay instances never publish the same row
            await db.Database.OpenConnectionAsync(cancellationToken);
            // READ COMMITTED for the claim transaction: record locks only, no next-key/gap locks - business transactions
            // inserting NEW outbox rows are never blocked while this claim transaction is open, so the AC
            // "broker outage does not fail originating DB write" holds even while a batch is being published
            await db.Database.ExecuteSqlRawAsync("SET TRANSACTION ISOLATION LEVEL READ COMMITTED", cancellationToken);
            tx = await db.Database.BeginTransactionAsync(cancellationToken);

            // All mapped columns selected explicitly (FromSql requirement); `Key` backticked - reserved word in MySQL
            pending = await db.OutboxMessages.FromSqlInterpolated($@"
SELECT Id, Topic, `Key`, EventType, Payload, HeadersJson, CreatedAtUtc, ProcessedAtUtc, RetryCount, LastError, Status, CorrelationId
FROM outbox_messages
WHERE Status = {"Pending"}
ORDER BY CreatedAtUtc
LIMIT {BatchSize}
FOR UPDATE SKIP LOCKED")
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex) when (ex.Message.Contains("outbox_messages") && ex.Message.Contains("doesn't exist"))
        {
            _logger.LogWarning("Outbox table not yet created - migration pending, will retry after migration applied");
            return;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to claim outbox batch - will retry, may be migration pending or DB unavailable");
            return;
        }

        if (pending.Count == 0)
        {
            await tx.RollbackAsync(CancellationToken.None); // nothing claimed, release locks
            return;
        }

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
                        // Review fix: producer-side DLQ removed - a DLQ topic on the same unreachable broker fails identically.
                        // Failure sink is the outbox table itself: mark Poisoned (terminal, visible, left for a human to inspect/requeue).
                        message.Status = "Poisoned";
                        message.ProcessedAtUtc = DateTime.UtcNow;
                        _logger.LogError(
                            "Outbox message {Id} topic={Topic} key={Key} correlationId={CorrelationId} failed {MaxRetries} times - marked POISONED in outbox_messages, needs human review. Requeue by setting Status='Pending', RetryCount=0. LastError: {LastError}",
                            message.Id, message.Topic, message.Key, message.CorrelationId, MaxRetries, message.LastError);
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
                    // Review fix: same as failure path - no producer-side DLQ, terminal state is Poisoned in the outbox table
                    message.Status = "Poisoned";
                    message.ProcessedAtUtc = DateTime.UtcNow;
                    _logger.LogError(
                        "Outbox message {Id} topic={Topic} key={Key} correlationId={CorrelationId} threw exceptions {MaxRetries} times - marked POISONED in outbox_messages, needs human review. Requeue by setting Status='Pending', RetryCount=0. LastError: {LastError}",
                        message.Id, message.Topic, message.Key, message.CorrelationId, MaxRetries, message.LastError);
                }
            }
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken); // claim + status updates commit atomically; crash before this -> rollback -> rows stay Pending (at-least-once, consumers dedupe on eventId)
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(CancellationToken.None);
            _logger.LogWarning(ex, "Outbox batch rolled back - claimed rows stay Pending, retried next cycle");
            return;
        }

        if (pending.Any(m => m.Status == "Processed"))
            _logger.LogInformation("Processed {Count} outbox messages, {Pending} still pending",
                pending.Count(m => m.Status == "Processed"), pending.Count(m => m.Status == "Pending"));
    }
}
