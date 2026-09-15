using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProcessingService.Domain.Entities;
using ProcessingService.Infrastructure.Persistence;

namespace ProcessingService.Application.MccDispatch;

/// <summary>
/// Background service polling mccdb.dispatch_notes every 30s and caching into processingdb.mcc_dispatch_traces.
/// True isolate: Processing dropdown reads from its own mcc_dispatch_traces table, not directly mccdb.
/// No MCC edit, only read. When MCC creates new dispatch, we auto-trace it here.
/// Also updates TotalUnloadedKg for existing traces from ProcessingRuns to handle old fully unloaded dispatches.
/// Future upgrade: Replace polling with Kafka consumer mcc.dispatch_created event -> same insert logic.
/// </summary>
public sealed class MccDispatchSyncService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MccDispatchSyncService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(30);

    public MccDispatchSyncService(IServiceProvider serviceProvider, ILogger<MccDispatchSyncService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MccDispatchSyncService started - polling mccdb.dispatch_notes every {Interval}s", _interval.TotalSeconds);
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing MCC dispatches");
            }
            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task SyncAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProcessingDbContext>();
        var time = scope.ServiceProvider.GetRequiredService<TimeProvider>();

        try
        {
            var mccDispatches = await db.Database
                .SqlQueryRaw<MccDispatchRaw>("SELECT Reference, BowserRegistration, DispatchDate, TotalQuantityLitres, DispatchedBy, RecordedAtUtc FROM mccdb.dispatch_notes ORDER BY RecordedAtUtc DESC LIMIT 100")
                .ToListAsync(cancellationToken);

            if (mccDispatches.Count == 0)
                return;

            var existingTraces = await db.MccDispatchTraces.ToListAsync(cancellationToken);
            var existingMap = existingTraces.ToDictionary(t => t.Reference, t => t, StringComparer.OrdinalIgnoreCase);

            var now = time.GetUtcNow().UtcDateTime;
            var newCount = 0;

            // Get unloaded sums from ProcessingRuns for updating traces (fixes old fully unloaded showing as partial)
            var unloadedSums = await db.ProcessingRuns
                .GroupBy(r => r.DispatchNumber)
                .Select(g => new { DispatchNumber = g.Key, Total = g.Sum(r => r.QuantityKg) })
                .ToListAsync(cancellationToken);

            var unloadedMap = unloadedSums.ToDictionary(x => x.DispatchNumber, x => x.Total, StringComparer.OrdinalIgnoreCase);

            // Also include allocations sum (new table)
            var allocSums = await db.ProcessingRunStoringAllocations
                .GroupBy(a => a.ProcessingRun.DispatchNumber)
                .Select(g => new { DispatchNumber = g.Key, Total = g.Sum(a => a.QuantityKg) })
                .ToListAsync(cancellationToken);

            foreach (var a in allocSums)
            {
                if (unloadedMap.TryGetValue(a.DispatchNumber, out var existing))
                {
                    if (a.Total > existing)
                        unloadedMap[a.DispatchNumber] = a.Total;
                }
                else
                {
                    unloadedMap[a.DispatchNumber] = a.Total;
                }
            }

            foreach (var m in mccDispatches)
            {
                if (!existingMap.TryGetValue(m.Reference, out var trace))
                {
                    // New dispatch - create trace
                    var alreadyUnloaded = unloadedMap.TryGetValue(m.Reference, out var sum) ? sum : 0m;

                    var newTrace = new MccDispatchTrace
                    {
                        Id = Guid.NewGuid(),
                        Reference = m.Reference,
                        BowserRegistration = m.BowserRegistration ?? "Unknown",
                        DispatchDate = m.DispatchDate,
                        TotalQuantityLitres = m.TotalQuantityLitres,
                        DispatchedBy = m.DispatchedBy ?? "",
                        RecordedAtUtc = m.RecordedAtUtc,
                        CreatedAtUtc = now,
                        LastSyncedAtUtc = now,
                        TotalUnloadedKg = alreadyUnloaded
                    };

                    db.MccDispatchTraces.Add(newTrace);
                    newCount++;
                }
                else
                {
                    // Existing trace - update TotalUnloadedKg from ProcessingRuns to fix old fully unloaded showing as partial
                    var alreadyUnloaded = unloadedMap.TryGetValue(m.Reference, out var sum) ? sum : 0m;
                    if (trace.TotalUnloadedKg != alreadyUnloaded)
                    {
                        trace.TotalUnloadedKg = alreadyUnloaded;
                        trace.LastSyncedAtUtc = now;
                        // Also update total if MCC total changed
                        if (trace.TotalQuantityLitres != m.TotalQuantityLitres)
                            trace.TotalQuantityLitres = m.TotalQuantityLitres;
                    }
                }
            }

            await db.SaveChangesAsync(cancellationToken);

            if (newCount > 0)
                _logger.LogInformation("Synced {Count} new MCC dispatches to trace table", newCount);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to sync MCC dispatches - mccdb may be unavailable, will retry");
        }
    }

    private sealed class MccDispatchRaw
    {
        public string Reference { get; set; } = string.Empty;
        public string? BowserRegistration { get; set; }
        public DateTime? DispatchDate { get; set; }
        public decimal TotalQuantityLitres { get; set; }
        public string? DispatchedBy { get; set; }
        public DateTime RecordedAtUtc { get; set; }
    }
}
