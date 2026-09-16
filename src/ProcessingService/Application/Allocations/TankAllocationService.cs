using Microsoft.EntityFrameworkCore;
using ProcessingService.Domain.Entities;
using ProcessingService.Infrastructure.Persistence;
using ProcessingService.Api.Infrastructure.Observability;

namespace ProcessingService.Application.Allocations;

/// <summary>
/// Allocation from storing to mixing tank with batch code generation per real process:
/// Batch code [dayNumber]-[productCode]-[batchLetter] e.g., 1-SY-A, 258-FM-A, 258-DY-A
/// DayNumber = DayOfYear 1-365, ProductCode = SY,SK,FM,FLM,DY (was DK fixed), BatchLetter = A-Z per product per day concurrency-safe
/// Product line pre-selected from alcohol result: Passed 80% -> FM,FLM Fresh/Flavoured, Passed 75%/68%/COB -> SY,SK,DY Yogurt
/// No Kafka this sprint - direct DB writes, abstraction ready for next sprint
/// </summary>
public sealed class TankAllocationService : ITankAllocationService
{
    private readonly ProcessingDbContext _db;
    private readonly ProcessingMetrics _metrics;
    private readonly TimeProvider _time;

    public TankAllocationService(ProcessingDbContext db, ProcessingMetrics metrics, TimeProvider time)
    {
        _db = db;
        _metrics = metrics;
        _time = time;
    }

    public async Task<TankAllocation> AllocateAsync(CreateAllocationRequest request, string userId, CancellationToken cancellationToken)
    {
        if (request.QuantityKg <= 0)
            throw new AllocationValidationException("Quantity must be positive");

        // Validate source storing tank
        var sourceTank = await _db.Tanks.FirstOrDefaultAsync(t => t.Id == request.SourceStoringTankId, cancellationToken);
        if (sourceTank == null)
            throw new AllocationValidationException($"Source tank not found");

        if (sourceTank.Kind != TankKind.Storing)
            throw new AllocationValidationException($"Tank '{sourceTank.Code}' is not a storing tank. Only storing tanks can supply mixing tanks.");

        if (sourceTank.Status != TankStatus.Active)
            throw new AllocationValidationException($"Source tank '{sourceTank.Code}' is not active.");

        if (sourceTank.RemainingKg < request.QuantityKg - 0.01m)
            throw new AllocationValidationException($"Source tank '{sourceTank.Code}' only holds {sourceTank.RemainingKg:F0} KG, but trying to allocate {request.QuantityKg:F0} KG. Split remaining or choose another source.");

        // Validate destination mixing tank
        var destTank = await _db.Tanks.FirstOrDefaultAsync(t => t.Id == request.DestinationMixingTankId, cancellationToken);
        if (destTank == null)
            throw new AllocationValidationException($"Destination tank not found");

        if (destTank.Kind != TankKind.Mixing)
            throw new AllocationValidationException($"Tank '{destTank.Code}' is not a mixing tank.");

        if (destTank.Status != TankStatus.Active)
            throw new AllocationValidationException($"Destination tank '{destTank.Code}' is not active.");

        // Mixing tank must be empty (one batch at a time) per real process
        if (destTank.RemainingKg > 0.01m)
        {
            // Check if same batch (adding to existing batch from another storing tank is allowed)
            var existingBatch = await _db.TankAllocations
                .Where(a => a.DestinationMixingTankId == destTank.Id)
                .OrderByDescending(a => a.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);

            if (existingBatch != null)
                throw new AllocationValidationException($"Mixing tank '{destTank.Code}' already holds batch {existingBatch.BatchCode} ({destTank.RemainingKg:F0} KG). Must be empty before new allocation. If adding to same batch from another storing tank, use same batch code.");
        }

        // Find ProcessingRun for traceability - which dispatch's milk is in source tank
        ProcessingRun? processingRun = null;

        if (request.ProcessingRunId.HasValue)
        {
            processingRun = await _db.ProcessingRuns
                .Include(r => r.QualityPanel)
                .FirstOrDefaultAsync(r => r.Id == request.ProcessingRunId.Value, cancellationToken);

            if (processingRun == null)
                throw new AllocationValidationException($"Processing run not found");
        }
        else
        {
            // Find latest ReleasedForAllocation run in source tank
            processingRun = await _db.ProcessingRuns
                .Include(r => r.QualityPanel)
                .Where(r => r.StoringTankId == sourceTank.Id && r.State == ProcessingRunState.ReleasedForAllocation && r.QualityTestStatus == QualityTestStatus.Passed)
                .OrderByDescending(r => r.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);

            if (processingRun == null)
            {
                // Check if any run in source tank is not yet Passed
                var anyRunInTank = await _db.ProcessingRuns
                    .Where(r => r.StoringTankId == sourceTank.Id)
                    .OrderByDescending(r => r.CreatedAtUtc)
                    .FirstOrDefaultAsync(cancellationToken);

                if (anyRunInTank == null)
                    throw new AllocationValidationException($"No milk found in source tank '{sourceTank.Code}'. Record an unload first.");

                if (anyRunInTank.QualityTestStatus == QualityTestStatus.Pending)
                    throw new AllocationValidationException($"Quality test not yet started for dispatch {anyRunInTank.DispatchNumber} in tank {sourceTank.Code}. Status Pending - cannot allocate.");

                if (anyRunInTank.QualityTestStatus == QualityTestStatus.InProgress)
                    throw new AllocationValidationException($"Lab is testing dispatch {anyRunInTank.DispatchNumber} in tank {sourceTank.Code}. Status InProgress - cannot allocate.");

                if (anyRunInTank.QualityTestStatus == QualityTestStatus.Failed)
                    throw new AllocationValidationException($"Dispatch {anyRunInTank.DispatchNumber} in tank {sourceTank.Code} Failed quality test ({anyRunInTank.HoldReason}). Cannot allocate - check Held Consignments.");

                throw new AllocationValidationException($"No ReleasedForAllocation run found in tank {sourceTank.Code}. Quality must be Passed.");
            }
        }

        // Product line pre-selection validation from alcohol result (real cascade)
        var preSelectedProducts = GetPreSelectedProducts(processingRun);
        var isOverride = !preSelectedProducts.Contains(request.ProductType);

        if (isOverride && string.IsNullOrWhiteSpace(request.OverrideReason))
            throw new AllocationValidationException($"Product {request.ProductType} is not pre-selected from alcohol result {processingRun.QualityPanel?.AlcoholResult ?? "Unknown"} (pre-selected: {string.Join(", ", preSelectedProducts)}). Override reason required when changing product line.");

        var now = _time.GetUtcNow().UtcDateTime;
        var batchNumber = now.DayOfYear; // 1-365

        // Generate batch letter concurrency-safe with retry
        TankAllocation allocation;
        var maxRetries = 5;
        var retry = 0;

        while (true)
        {
            var batchLetter = await GenerateNextBatchLetterAsync(batchNumber, request.ProductType, cancellationToken);
            var batchCode = $"{batchNumber}-{request.ProductType}-{batchLetter}";

            allocation = new TankAllocation
            {
                Id = Guid.NewGuid(),
                ProcessingRunId = processingRun.Id,
                SourceStoringTankId = sourceTank.Id,
                DestinationMixingTankId = destTank.Id,
                QuantityKg = Math.Round(request.QuantityKg, 2),
                ProductType = request.ProductType,
                BatchNumber = batchNumber,
                BatchLetter = batchLetter,
                BatchCode = batchCode,
                AllocatedAtUtc = now,
                OverrideReason = isOverride ? request.OverrideReason : null,
                CreatedBy = userId,
                CreatedAtUtc = now
            };

            _db.TankAllocations.Add(allocation);

            // Update tanks transactional
            sourceTank.RemainingKg -= request.QuantityKg;
            sourceTank.UpdatedAtUtc = now;
            sourceTank.UpdatedBy = userId;

            destTank.RemainingKg += request.QuantityKg;
            destTank.UpdatedAtUtc = now;
            destTank.UpdatedBy = userId;

            try
            {
                await _db.SaveChangesAsync(cancellationToken);
                break; // Success
            }
            catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("ux_tank_allocations_batchcode") == true || ex.InnerException?.Message.Contains("ux_tank_allocations_day_product_letter") == true)
            {
                // Concurrency: batch letter already taken by another tech, retry with next letter
                _db.Entry(allocation).State = EntityState.Detached;
                sourceTank.RemainingKg += request.QuantityKg; // rollback in memory
                destTank.RemainingKg -= request.QuantityKg;

                retry++;
                if (retry >= maxRetries)
                    throw new AllocationValidationException($"Failed to generate unique batch code after {maxRetries} retries. Please try again.");

                // Wait small random delay
                await Task.Delay(50 * retry, cancellationToken);
                continue;
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new AllocationValidationException($"Tank '{sourceTank.Code}' or '{destTank.Code}' was modified by another user. Please reload and try again.");
            }
        }

        _metrics.AllocationsTotal.Add(1, new System.Diagnostics.TagList { { "product", request.ProductType.ToString() }, { "source", sourceTank.Code }, { "dest", destTank.Code } });

        return allocation;
    }

    public async Task<IReadOnlyList<TankAllocation>> ListAsync(CancellationToken cancellationToken)
    {
        return await _db.TankAllocations
            .Include(a => a.ProcessingRun)
            .Include(a => a.SourceStoringTank)
            .Include(a => a.DestinationMixingTank)
            .OrderByDescending(a => a.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<TankAllocation?> GetByBatchCodeAsync(string batchCode, CancellationToken cancellationToken)
    {
        var normalized = batchCode.Trim().ToUpperInvariant();
        return await _db.TankAllocations
            .Include(a => a.ProcessingRun)
                .ThenInclude(r => r.QualityPanel)
            .Include(a => a.SourceStoringTank)
            .Include(a => a.DestinationMixingTank)
            .FirstOrDefaultAsync(a => a.BatchCode == normalized, cancellationToken);
    }

    private async Task<string> GenerateNextBatchLetterAsync(int batchNumber, ProductType productType, CancellationToken cancellationToken)
    {
        // Find max letter for today + product
        var existingLetters = await _db.TankAllocations
            .Where(a => a.BatchNumber == batchNumber && a.ProductType == productType)
            .Select(a => a.BatchLetter)
            .ToListAsync(cancellationToken);

        if (existingLetters.Count == 0)
            return "A";

        // Letters are A-Z, find max
        var maxLetter = existingLetters
            .Select(l => l.Length > 0 ? l[0] : 'A')
            .Max();

        if (maxLetter >= 'Z')
            throw new AllocationValidationException($"Batch letters exhausted for day {batchNumber} product {productType}. Maximum 26 batches per product per day (A-Z).");

        var nextLetter = (char)(maxLetter + 1);
        return nextLetter.ToString();
    }

    private static IReadOnlyList<ProductType> GetPreSelectedProducts(ProcessingRun run)
    {
        var alcoholResult = run.QualityPanel?.AlcoholResult ?? "";

        // Real cascade mapping:
        // Passed 80% -> Fresh/Flavoured best quality -> FM, FLM
        // Passed 75%, Passed 68%, Passed COB -> Yogurt acceptable -> SY, SK, DY (was DK fixed to DY)
        // Failed COB -> cannot allocate (already blocked earlier)

        if (alcoholResult.Contains("80%"))
            return new[] { ProductType.FM, ProductType.FLM };

        if (alcoholResult.Contains("75%") || alcoholResult.Contains("68%") || alcoholResult.Contains("COB"))
            return new[] { ProductType.SY, ProductType.SK, ProductType.DY };

        // If no alcohol result (old data), allow all
        return new[] { ProductType.SY, ProductType.SK, ProductType.FM, ProductType.FLM, ProductType.DY };
    }
}
