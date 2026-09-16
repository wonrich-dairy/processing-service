using Microsoft.EntityFrameworkCore;
using ProcessingService.Domain.Entities;
using ProcessingService.Infrastructure.Persistence;

namespace ProcessingService.Application.Stages;

public sealed class ProcessingStageService : IProcessingStageService
{
    private readonly ProcessingDbContext _db;
    private readonly TimeProvider _time;

    public ProcessingStageService(ProcessingDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    public async Task<ProcessingStage> StartAsync(StartStageRequest request, string userId, CancellationToken ct)
    {
        var tank = await _db.Tanks.FirstOrDefaultAsync(t => t.Id == request.MixingTankId, ct);
        if (tank == null) throw new ArgumentException("Mixing tank not found");
        if (tank.Kind != TankKind.Mixing) throw new ArgumentException("Not a mixing tank");
        if (tank.RemainingKg <= 0.01m) throw new InvalidOperationException($"Mixing tank {tank.Code} is empty, no batch to process");

        // Check order: Heating -> Homogeniser -> Pasteuriser -> Cooling
        var existingStages = await _db.ProcessingStages
            .Where(s => s.MixingTankId == request.MixingTankId)
            .OrderBy(s => s.StartTimeUtc)
            .ToListAsync(ct);

        var activeStage = existingStages.FirstOrDefault(s => s.EndTimeUtc == null);
        if (activeStage != null)
            throw new InvalidOperationException($"Stage {activeStage.StageType} already in progress in {tank.Code}. End it first.");

        // Enforce order
        var lastEnded = existingStages.Where(s => s.EndTimeUtc != null).OrderByDescending(s => s.EndTimeUtc).FirstOrDefault();
        var expectedNext = GetNextStage(lastEnded?.StageType);
        if (lastEnded != null && request.StageType != expectedNext && request.StageType != StageType.Heating)
        {
            // Allow restart Heating if previous batch completed? For now enforce sequence
            if (GetStageOrder(request.StageType) <= GetStageOrder(lastEnded.StageType))
                throw new InvalidOperationException($"Cannot start {request.StageType} after {lastEnded.StageType}. Expected {expectedNext}.");
        }

        var now = _time.GetUtcNow().UtcDateTime;

        var stage = new ProcessingStage
        {
            Id = Guid.NewGuid(),
            ProcessingRunId = request.ProcessingRunId,
            MixingTankId = request.MixingTankId,
            StageType = request.StageType,
            StartTimeUtc = now,
            EndTimeUtc = null,
            EndTemperatureC = 0,
            CreatedBy = userId,
            CreatedAtUtc = now
        };

        _db.ProcessingStages.Add(stage);
        await _db.SaveChangesAsync(ct);
        return stage;
    }

    public async Task<ProcessingStage> EndAsync(Guid stageId, EndStageRequest request, string userId, CancellationToken ct)
    {
        var stage = await _db.ProcessingStages.Include(s => s.MixingTank).FirstOrDefaultAsync(s => s.Id == stageId, ct);
        if (stage == null) throw new ArgumentException("Stage not found");
        if (stage.EndTimeUtc != null) throw new InvalidOperationException("Stage already ended");

        var now = _time.GetUtcNow().UtcDateTime;
        if (now < stage.StartTimeUtc) throw new ArgumentException("End time cannot be before start time");

        // Validate temperature per stage type
        var isDeviation = IsDeviation(stage.StageType, request.EndTemperatureC);

        stage.EndTimeUtc = now;
        stage.EndTemperatureC = request.EndTemperatureC;
        stage.IsDeviation = isDeviation;
        stage.DurationMinutes = (int)(now - stage.StartTimeUtc).TotalMinutes;
        stage.CultureAdded = request.CultureAdded;
        if (request.CultureAdded) stage.CultureAddedAtUtc = now;

        // If Cooling ended, mark tank empty? No, cooling sends to final storage, but for now keep RemainingKg
        // If user wants to free MT after pasteuriser, they can do via separate endpoint later

        await _db.SaveChangesAsync(ct);
        return stage;
    }

    public async Task<IReadOnlyList<ProcessingStage>> ListByMixingTankAsync(Guid mixingTankId, CancellationToken ct)
    {
        return await _db.ProcessingStages
            .Include(s => s.ProcessingRun)
            .Include(s => s.MixingTank)
            .Where(s => s.MixingTankId == mixingTankId)
            .OrderBy(s => s.StartTimeUtc)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ProcessingStage>> ListActiveAsync(CancellationToken ct)
    {
        return await _db.ProcessingStages
            .Include(s => s.ProcessingRun)
            .Include(s => s.MixingTank)
            .Where(s => s.EndTimeUtc == null)
            .OrderByDescending(s => s.StartTimeUtc)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<TankAllocation>> ListActiveBatchesAsync(CancellationToken ct)
    {
        // Active batches = allocations where destination mixing tank has RemainingKg >0 and no Cooling completed
        var tanksWithMilk = await _db.Tanks.Where(t => t.Kind == TankKind.Mixing && t.RemainingKg > 0.01m).Select(t => t.Id).ToListAsync(ct);
        
        var activeAllocs = await _db.TankAllocations
            .Include(a => a.SourceStoringTank)
            .Include(a => a.DestinationMixingTank)
            .Include(a => a.ProcessingRun)
            .Where(a => tanksWithMilk.Contains(a.DestinationMixingTankId))
            .OrderByDescending(a => a.CreatedAtUtc)
            .ToListAsync(ct);

        // Deduplicate by mixing tank - latest allocation per MT is current batch
        var grouped = activeAllocs.GroupBy(a => a.DestinationMixingTankId).Select(g => g.OrderByDescending(a => a.CreatedAtUtc).First()).ToList();
        return grouped;
    }

    private static StageType? GetNextStage(StageType? current)
    {
        return current switch
        {
            null => StageType.Heating,
            StageType.Heating => StageType.Homogeniser,
            StageType.Homogeniser => StageType.Pasteuriser,
            StageType.Pasteuriser => StageType.Cooling,
            StageType.Cooling => null,
            _ => null
        };
    }

    private static int GetStageOrder(StageType type) => type switch
    {
        StageType.Heating => 0,
        StageType.Homogeniser => 1,
        StageType.Pasteuriser => 2,
        StageType.Cooling => 3,
        _ => 99
    };

    private static bool IsDeviation(StageType type, decimal temp)
    {
        return type switch
        {
            StageType.Heating => temp < 40 || temp > 65,
            StageType.Pasteuriser => temp < 80 || temp > 85,
            StageType.Cooling => false, // 0-4 fresh, 44 yogurt handled later
            _ => false
        };
    }
}
