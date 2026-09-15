using Microsoft.EntityFrameworkCore;
using ProcessingService.Application.MccDispatch;
using ProcessingService.Domain.Entities;
using ProcessingService.Infrastructure.Persistence;
using ProcessingService.Api.Infrastructure.Observability;

namespace ProcessingService.Application.ProcessingRuns;

public sealed class ProcessingRunService : IProcessingRunService
{
    private readonly ProcessingDbContext _db;
    private readonly IMccDispatchClient _mccDispatch;
    private readonly ProcessingMetrics _metrics;
    private readonly TimeProvider _time;

    public ProcessingRunService(ProcessingDbContext db, IMccDispatchClient mccDispatch, ProcessingMetrics metrics, TimeProvider time)
    {
        _db = db;
        _mccDispatch = mccDispatch;
        _metrics = metrics;
        _time = time;
    }

    public async Task<ProcessingRun> CreateUnloadAsync(CreateUnloadRequest request, string userId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.DispatchNumber))
            throw new ArgumentException("Dispatch number is required", nameof(request.DispatchNumber));

        var normalizedDispatch = request.DispatchNumber.Trim().ToUpperInvariant();

        if (!normalizedDispatch.StartsWith("DN-"))
            throw new ArgumentException($"Dispatch '{request.DispatchNumber}' invalid format. Expected DN-YYYYMMDD-XX like DN-20260910-01", nameof(request.DispatchNumber));

        var exists = await _mccDispatch.ExistsAsync(normalizedDispatch, cancellationToken);
        if (!exists)
            throw new InvalidOperationException($"Dispatch '{normalizedDispatch}' not found in MCC. Enter valid MCC dispatch ID.");

        // Get or create trace for true isolate
        var trace = await _db.MccDispatchTraces.FirstOrDefaultAsync(t => t.Reference == normalizedDispatch, cancellationToken);
        if (trace == null)
        {
            // Try to sync immediately from mccdb if not yet cached by background service
            var mccDto = await _mccDispatch.GetAsync(normalizedDispatch, cancellationToken);
            if (mccDto != null)
            {
                var now = _time.GetUtcNow().UtcDateTime;
                trace = new MccDispatchTrace
                {
                    Id = Guid.NewGuid(),
                    Reference = mccDto.Reference,
                    BowserRegistration = mccDto.BowserRegistration,
                    DispatchDate = DateTime.TryParse(mccDto.DispatchDate, out var d) ? d : null,
                    TotalQuantityLitres = mccDto.TotalQuantityLitres,
                    DispatchedBy = mccDto.DispatchedBy,
                    RecordedAtUtc = mccDto.RecordedAtUtc,
                    CreatedAtUtc = now,
                    LastSyncedAtUtc = now,
                    TotalUnloadedKg = 0
                };
                _db.MccDispatchTraces.Add(trace);
                await _db.SaveChangesAsync(cancellationToken);
            }
        }

        var totalDispatchQty = trace?.TotalQuantityLitres ?? (await _mccDispatch.GetAsync(normalizedDispatch, cancellationToken))?.TotalQuantityLitres ?? 0m;

        // Calculate already unloaded via trace + allocations (supports partial unload while keeping DispatchNumber UNIQUE)
        var alreadyUnloadedQty = trace?.TotalUnloadedKg ?? await _db.ProcessingRuns
            .Where(r => r.DispatchNumber == normalizedDispatch)
            .SumAsync(r => (decimal?)r.QuantityKg, cancellationToken) ?? 0m;

        // Also include allocations if using new allocation table
        var allocationSum = await _db.ProcessingRunStoringAllocations
            .Where(a => a.ProcessingRun.DispatchNumber == normalizedDispatch)
            .SumAsync(a => (decimal?)a.QuantityKg, cancellationToken) ?? 0m;

        // Use max of both for safety (old data uses ProcessingRun.QuantityKg, new uses allocations)
        if (allocationSum > alreadyUnloadedQty)
            alreadyUnloadedQty = allocationSum;

        var remainingQty = totalDispatchQty > 0 ? totalDispatchQty - alreadyUnloadedQty : decimal.MaxValue;

        if (totalDispatchQty > 0 && remainingQty <= 0.01m)
            throw new DuplicateDispatchException(normalizedDispatch, alreadyUnloadedQty, totalDispatchQty);

        if (totalDispatchQty > 0 && request.QuantityKg > remainingQty + 0.01m)
            throw new ArgumentException(
                $"Dispatch '{normalizedDispatch}' total is {totalDispatchQty:F2} KG, already unloaded {alreadyUnloadedQty:F2} KG, remaining {remainingQty:F2} KG. Cannot unload {request.QuantityKg:F2} KG - exceeds remaining. Split remaining to another tank if needed.",
                nameof(request.QuantityKg));

        if (!request.SmellOk || !request.ColourOk || !request.TasteOk)
            throw new ArgumentException("Sensory checks (smell, colour, taste) must all pass before unload. Real process requires PASS before unloading to storing tank.", nameof(request.SmellOk));

        if (request.QuantityKg <= 0)
            throw new ArgumentException("Quantity must be positive", nameof(request.QuantityKg));

        var isDeviation = request.TemperatureC < 1 || request.TemperatureC > 3;

        var tank = await _db.Tanks.FirstOrDefaultAsync(t => t.Id == request.StoringTankId, cancellationToken);
        if (tank == null)
            throw new ArgumentException($"Storing tank not found", nameof(request.StoringTankId));

        if (tank.Kind != TankKind.Storing)
            throw new ArgumentException($"Tank '{tank.Code}' is not a storing tank. Only storing tanks can receive bowser milk.", nameof(request.StoringTankId));

        if (tank.Status != TankStatus.Active)
            throw new ArgumentException($"Tank '{tank.Code}' is not active. Only active tanks can receive milk.", nameof(request.StoringTankId));

        var available = tank.CapacityKg - tank.RemainingKg;
        if (request.QuantityKg > available)
            throw new ArgumentException($"Tank '{tank.Code}' only has {available:F0} KG free, but trying to unload {request.QuantityKg:F0} KG. Split remaining {request.QuantityKg - available:F0} KG to another tank.", nameof(request.QuantityKg));

        var nowTime = _time.GetUtcNow().UtcDateTime;

        // Check if ProcessingRun already exists for this dispatch (keep DispatchNumber UNIQUE)
        var existingRun = await _db.ProcessingRuns
            .Include(r => r.StoringAllocations)
            .FirstOrDefaultAsync(r => r.DispatchNumber == normalizedDispatch, cancellationToken);

        ProcessingRun run;

        if (existingRun != null)
        {
            // Existing dispatch - add allocation to same run (partial unload across tanks)
            run = existingRun;
            run.QuantityKg += request.QuantityKg;
            run.UpdatedAtUtc = nowTime;

            var allocation = new ProcessingRunStoringAllocation
            {
                Id = Guid.NewGuid(),
                ProcessingRunId = run.Id,
                StoringTankId = tank.Id,
                QuantityKg = request.QuantityKg,
                CreatedAtUtc = nowTime,
                CreatedBy = userId
            };

            _db.ProcessingRunStoringAllocations.Add(allocation);
        }
        else
        {
            // First unload for this dispatch - create new run (DispatchNumber UNIQUE)
            run = new ProcessingRun
            {
                Id = Guid.NewGuid(),
                DispatchNumber = normalizedDispatch,
                StoringTankId = tank.Id,
                QuantityKg = request.QuantityKg,
                TemperatureC = request.TemperatureC,
                IsTemperatureDeviation = isDeviation,
                State = ProcessingRunState.AwaitingLabResult,
                QualityTestStatus = QualityTestStatus.Pending,
                CreatedAtUtc = nowTime,
                UpdatedAtUtc = nowTime,
                CreatedBy = userId
            };

            _db.ProcessingRuns.Add(run);

            // Create first allocation
            var allocation = new ProcessingRunStoringAllocation
            {
                Id = Guid.NewGuid(),
                ProcessingRunId = run.Id,
                StoringTankId = tank.Id,
                QuantityKg = request.QuantityKg,
                CreatedAtUtc = nowTime,
                CreatedBy = userId
            };

            _db.ProcessingRunStoringAllocations.Add(allocation);
        }

        // Update tank remaining
        tank.RemainingKg += request.QuantityKg;
        tank.UpdatedAtUtc = nowTime;
        tank.UpdatedBy = userId;

        // Update trace
        if (trace != null)
        {
            trace.TotalUnloadedKg += request.QuantityKg;
            trace.LastSyncedAtUtc = nowTime;
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException($"Tank '{tank.Code}' was modified by another user. Please reload and try again.");
        }

        _metrics.UnloadsTotal.Add(1, new System.Diagnostics.TagList { { "tank", tank.Code }, { "deviation", isDeviation.ToString() } });

        // Reload with includes for return
        return await _db.ProcessingRuns
            .Include(r => r.StoringTank)
            .Include(r => r.QualityPanel)
            .FirstAsync(r => r.Id == run.Id, cancellationToken);
    }

    public async Task<IReadOnlyList<ProcessingRun>> ListAsync(CancellationToken cancellationToken)
    {
        return await _db.ProcessingRuns
            .Include(r => r.StoringTank)
            .Include(r => r.QualityPanel)
            .Include(r => r.StoringAllocations)
                .ThenInclude(a => a.StoringTank)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<ProcessingRun?> GetByDispatchAsync(string dispatchNumber, CancellationToken cancellationToken)
    {
        var normalized = dispatchNumber.Trim().ToUpperInvariant();
        return await _db.ProcessingRuns
            .Include(r => r.StoringTank)
            .Include(r => r.QualityPanel)
            .Include(r => r.StoringAllocations)
                .ThenInclude(a => a.StoringTank)
            .FirstOrDefaultAsync(r => r.DispatchNumber == normalized, cancellationToken);
    }

    public async Task<decimal> GetTotalUnloadedForDispatchAsync(string dispatchNumber, CancellationToken cancellationToken)
    {
        var normalized = dispatchNumber.Trim().ToUpperInvariant();
        var trace = await _db.MccDispatchTraces.FirstOrDefaultAsync(t => t.Reference == normalized, cancellationToken);
        if (trace != null)
            return trace.TotalUnloadedKg;

        return await _db.ProcessingRuns
            .Where(r => r.DispatchNumber == normalized)
            .SumAsync(r => (decimal?)r.QuantityKg, cancellationToken) ?? 0m;
    }
}

public sealed class DuplicateDispatchException : Exception
{
    public decimal AlreadyUnloadedKg { get; }
    public decimal TotalDispatchKg { get; }

    public DuplicateDispatchException(string dispatch, decimal alreadyUnloaded = 0, decimal total = 0)
        : base(total > 0 && alreadyUnloaded > 0
            ? $"Dispatch '{dispatch}' has already been fully unloaded ({alreadyUnloaded:F2} KG of {total:F2} KG). Cannot unload again."
            : $"Dispatch '{dispatch}' has already been unloaded. Cannot unload same dispatch twice.")
    {
        AlreadyUnloadedKg = alreadyUnloaded;
        TotalDispatchKg = total;
    }
}
