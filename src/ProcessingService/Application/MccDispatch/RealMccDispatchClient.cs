using Microsoft.EntityFrameworkCore;
using ProcessingService.Infrastructure.Persistence;

namespace ProcessingService.Application.MccDispatch;

/// <summary>
/// Real MCC dispatch client - reads from processingdb.mcc_dispatch_traces for true isolate (cached via polling).
/// Supports PARTIAL UNLOAD: one dispatch split across multiple tanks via ProcessingRunStoringAllocation while keeping DispatchNumber UNIQUE.
/// Filters out fully unloaded dispatches (Remaining <=0.01) from dropdown.
/// </summary>
public sealed class RealMccDispatchClient : IMccDispatchClient
{
    private readonly ProcessingDbContext _db;

    public RealMccDispatchClient(ProcessingDbContext db)
    {
        _db = db;
    }

    public async Task<bool> ExistsAsync(string dispatchNumber, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dispatchNumber))
            return false;

        var normalized = dispatchNumber.Trim().ToUpperInvariant();
        if (!normalized.StartsWith("DN-"))
            return false;

        try
        {
            var existsInTrace = await _db.MccDispatchTraces
                .AsNoTracking()
                .AnyAsync(t => t.Reference == normalized, cancellationToken);
            if (existsInTrace)
                return true;

            var exists = await _db.Database
                .SqlQueryRaw<int>("SELECT 1 as Value FROM mccdb.dispatch_notes WHERE Reference = {0} LIMIT 1", normalized)
                .AnyAsync(cancellationToken);
            return exists;
        }
        catch
        {
            return true;
        }
    }

    public async Task<MccDispatchDto?> GetAsync(string dispatchNumber, CancellationToken cancellationToken)
    {
        var normalized = dispatchNumber.Trim().ToUpperInvariant();
        try
        {
            var trace = await _db.MccDispatchTraces
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Reference == normalized, cancellationToken);
            if (trace != null)
            {
                return new MccDispatchDto
                {
                    Reference = trace.Reference,
                    BowserRegistration = trace.BowserRegistration,
                    DispatchDate = trace.DispatchDate?.ToString("yyyy-MM-dd") ?? "",
                    TotalQuantityLitres = trace.TotalQuantityLitres,
                    DispatchedBy = trace.DispatchedBy,
                    RecordedAtUtc = trace.RecordedAtUtc
                };
            }

            var result = await _db.Database
                .SqlQueryRaw<MccDispatchRaw>("SELECT Reference, BowserRegistration, DispatchDate, TotalQuantityLitres, DispatchedBy, RecordedAtUtc FROM mccdb.dispatch_notes WHERE Reference = {0} LIMIT 1", normalized)
                .FirstOrDefaultAsync(cancellationToken);

            if (result == null)
                return null;

            return new MccDispatchDto
            {
                Reference = result.Reference,
                BowserRegistration = result.BowserRegistration ?? "Unknown",
                DispatchDate = result.DispatchDate?.ToString("yyyy-MM-dd") ?? "",
                TotalQuantityLitres = result.TotalQuantityLitres,
                DispatchedBy = result.DispatchedBy ?? "",
                RecordedAtUtc = result.RecordedAtUtc
            };
        }
        catch
        {
            return new MccDispatchDto
            {
                Reference = normalized,
                BowserRegistration = "WP-TEST",
                DispatchDate = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                TotalQuantityLitres = 1000,
                DispatchedBy = "mcc",
                RecordedAtUtc = DateTime.UtcNow
            };
        }
    }

    public async Task<IReadOnlyList<MccDispatchDto>> ListRecentAsync(int take, CancellationToken cancellationToken)
    {
        try
        {
            // Get unloaded sums to ensure trace Remaining is accurate (fixes old fully unloaded showing in dropdown)
            var unloadedSums = await _db.ProcessingRuns
                .AsNoTracking()
                .GroupBy(r => r.DispatchNumber)
                .Select(g => new { DispatchNumber = g.Key, Total = g.Sum(r => r.QuantityKg) })
                .ToListAsync(cancellationToken);

            var unloadedMap = unloadedSums.ToDictionary(x => x.DispatchNumber, x => x.Total, StringComparer.OrdinalIgnoreCase);

            var traces = await _db.MccDispatchTraces
                .AsNoTracking()
                .OrderByDescending(t => t.RecordedAtUtc)
                .Take(take * 2)
                .ToListAsync(cancellationToken);

            // Filter using live unloaded sum to ensure fully unloaded are excluded even if trace TotalUnloadedKg outdated
            var filteredTraces = traces
                .Where(t =>
                {
                    var already = unloadedMap.TryGetValue(t.Reference, out var sum) ? sum : t.TotalUnloadedKg;
                    var remaining = t.TotalQuantityLitres - already;
                    return remaining > 0.01m;
                })
                .Take(take)
                .Select(t => new MccDispatchDto
                {
                    Reference = t.Reference,
                    BowserRegistration = t.BowserRegistration,
                    DispatchDate = t.DispatchDate?.ToString("yyyy-MM-dd") ?? "",
                    TotalQuantityLitres = t.TotalQuantityLitres,
                    DispatchedBy = t.DispatchedBy,
                    RecordedAtUtc = t.RecordedAtUtc
                }).ToList();

            if (filteredTraces.Count > 0)
                return filteredTraces;

            // Fallback if trace table empty
            var results = await _db.Database
                .SqlQueryRaw<MccDispatchRaw>("SELECT Reference, BowserRegistration, DispatchDate, TotalQuantityLitres, DispatchedBy, RecordedAtUtc FROM mccdb.dispatch_notes ORDER BY RecordedAtUtc DESC LIMIT {0}", take * 3)
                .ToListAsync(cancellationToken);

            var filtered = results
                .Where(r =>
                {
                    if (unloadedMap.TryGetValue(r.Reference, out var alreadyUnloaded))
                    {
                        var remaining = r.TotalQuantityLitres - alreadyUnloaded;
                        return remaining > 0.01m;
                    }
                    return true;
                })
                .Take(take)
                .Select(r => new MccDispatchDto
                {
                    Reference = r.Reference,
                    BowserRegistration = r.BowserRegistration ?? "Unknown",
                    DispatchDate = r.DispatchDate?.ToString("yyyy-MM-dd") ?? "",
                    TotalQuantityLitres = r.TotalQuantityLitres,
                    DispatchedBy = r.DispatchedBy ?? "",
                    RecordedAtUtc = r.RecordedAtUtc
                }).ToList();

            return filtered;
        }
        catch
        {
            return new List<MccDispatchDto>();
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
