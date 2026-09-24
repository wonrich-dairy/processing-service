using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProcessingService.Infrastructure.Persistence;

namespace ProcessingService.Application.Kafka;

/// <summary>
/// Outbox cleanup background service - review fix #8: outbox_messages otherwise only grows
/// Deletes Processed rows older than Outbox:ProcessedRetentionDays (default 7) once a day
/// Poisoned rows are NEVER auto-deleted - they are the failure record awaiting human review (requeue or delete manually after review)
/// Uses the existing ix_outbox_status index (Status lookup) - no schema change, no new migration
/// </summary>
public sealed class OutboxCleanupService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OutboxCleanupService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromHours(24); // once a day

    public OutboxCleanupService(IServiceProvider serviceProvider, ILogger<OutboxCleanupService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OutboxCleanupService started - deleting Processed rows past retention every {IntervalHours}h (Poisoned rows kept for human review)",
            _interval.TotalHours);

        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); // let startup + migrations settle before first pass

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupAsync(stoppingToken);
            }
            catch (Exception ex) when (ex.Message.Contains("outbox_messages") && ex.Message.Contains("doesn't exist"))
            {
                _logger.LogWarning("Outbox table not yet created - cleanup skipped, will retry next cycle");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Outbox cleanup failed - will retry next cycle");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task CleanupAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProcessingDbContext>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        var retentionDays = 7;
        if (int.TryParse(config["Outbox:ProcessedRetentionDays"], out var configured) && configured > 0)
            retentionDays = configured;

        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);

        // Disjoint from the relay's claim set (relay only locks Status='Pending') - no lock contention, no deadlock
        var deleted = await db.Database.ExecuteSqlInterpolatedAsync(
            $@"DELETE FROM outbox_messages
               WHERE Status = {"Processed"} AND ProcessedAtUtc IS NOT NULL AND ProcessedAtUtc < {cutoff}",
            cancellationToken);

        if (deleted > 0)
            _logger.LogInformation("Outbox cleanup: deleted {Count} Processed rows older than {RetentionDays}d (cutoff {Cutoff:u})",
                deleted, retentionDays, cutoff);
    }
}
