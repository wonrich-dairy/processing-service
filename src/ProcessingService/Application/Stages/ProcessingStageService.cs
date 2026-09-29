using Microsoft.EntityFrameworkCore;
using ProcessingService.Domain.Entities;
using ProcessingService.Domain.Events;
using ProcessingService.Infrastructure.Persistence;
using ProcessingService.Application.Outbox;

namespace ProcessingService.Application.Stages;

public sealed class ProcessingStageService : IProcessingStageService
{
    private readonly ProcessingDbContext _db;
    private readonly TimeProvider _time;
    private readonly IOutboxWriter _outbox;

    public ProcessingStageService(ProcessingDbContext db, TimeProvider time, IOutboxWriter outbox)
    {
        _db = db;
        _time = time;
        _outbox = outbox;
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
        var correlationId = Guid.NewGuid().ToString();

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

        // SCRUM-68: Publish ProcessingStageRecorded on start (with deviation false initially)
        var processingRun = await _db.ProcessingRuns.FirstOrDefaultAsync(r => r.Id == request.ProcessingRunId, ct);
        var allocation = await _db.TankAllocations
            .Where(a => a.ProcessingRunId == request.ProcessingRunId && a.DestinationMixingTankId == request.MixingTankId)
            .OrderByDescending(a => a.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

        var batchId = allocation?.BatchCode ?? processingRun?.BatchCode ?? processingRun?.DispatchNumber ?? "unknown";
        var dispatchNumber = processingRun?.DispatchNumber ?? "unknown";

        var stageEvent = new ProcessingStageRecordedEvent
        {
            BatchId = batchId,
            DispatchNumber = dispatchNumber,
            TimestampUtc = now,
            IsDeviation = false,
            CorrelationId = correlationId,
            MixingTankId = tank.Id.ToString(),
            MixingTankCode = tank.Code,
            StageType = request.StageType.ToString(),
            StartTimeUtc = now,
            EndTimeUtc = null,
            EndTemperatureC = 0,
            DurationMinutes = null,
            CultureAdded = false,
            RecordedBy = userId
        };

        await _outbox.WriteAsync(
            topic: "wonrich.processing.stage-events.v1",
            key: batchId,
            @event: stageEvent,
            correlationId: correlationId,
            cancellationToken: ct);

        await _db.SaveChangesAsync(ct);
        return stage;
    }

    public async Task<ProcessingStage> EndAsync(Guid stageId, EndStageRequest request, string userId, CancellationToken ct)
    {
        var stage = await _db.ProcessingStages.Include(s => s.MixingTank).Include(s => s.ProcessingRun).FirstOrDefaultAsync(s => s.Id == stageId, ct);
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

        var correlationId = Guid.NewGuid().ToString();

        // Get batch info for event
        var allocation = await _db.TankAllocations
            .Where(a => a.ProcessingRunId == stage.ProcessingRunId && a.DestinationMixingTankId == stage.MixingTankId)
            .OrderByDescending(a => a.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

        var batchId = allocation?.BatchCode ?? stage.ProcessingRun?.BatchCode ?? stage.ProcessingRun?.DispatchNumber ?? "unknown";
        var dispatchNumber = stage.ProcessingRun?.DispatchNumber ?? "unknown";

        // SCRUM-68: Publish ProcessingStageRecorded per stage, carrying stage type, temperature, timings and deviation flag
        var stageRecordedEvent = new ProcessingStageRecordedEvent
        {
            BatchId = batchId,
            DispatchNumber = dispatchNumber,
            TimestampUtc = now,
            IsDeviation = isDeviation,
            CorrelationId = correlationId,
            MixingTankId = stage.MixingTank.Id.ToString(),
            MixingTankCode = stage.MixingTank.Code,
            StageType = stage.StageType.ToString(),
            StartTimeUtc = stage.StartTimeUtc,
            EndTimeUtc = now,
            EndTemperatureC = request.EndTemperatureC,
            DurationMinutes = stage.DurationMinutes,
            CultureAdded = request.CultureAdded,
            CultureAddedAtUtc = request.CultureAdded ? now : null,
            RecordedBy = userId
        };

        await _outbox.WriteAsync(
            topic: "wonrich.processing.stage-events.v1",
            key: batchId,
            @event: stageRecordedEvent,
            correlationId: correlationId,
            cancellationToken: ct);

        // SCRUM-68: ProcessingCompleted published when a run closes (Cooling ended)
        if (stage.StageType == StageType.Cooling)
        {
            var allStages = await _db.ProcessingStages
                .Where(s => s.MixingTankId == stage.MixingTankId && s.ProcessingRunId == stage.ProcessingRunId)
                .ToListAsync(ct);

            var hasDeviation = allStages.Any(s => s.IsDeviation) || isDeviation;
            var firstStage = allStages.OrderBy(s => s.StartTimeUtc).FirstOrDefault();
            var totalDuration = firstStage != null ? (int)(now - firstStage.StartTimeUtc).TotalMinutes : stage.DurationMinutes ?? 0;

            var completedEvent = new ProcessingCompletedEvent
            {
                BatchId = batchId,
                DispatchNumber = dispatchNumber,
                TimestampUtc = now,
                IsDeviation = hasDeviation,
                CorrelationId = Guid.NewGuid().ToString(),
                MixingTankId = stage.MixingTank.Id.ToString(),
                MixingTankCode = stage.MixingTank.Code,
                CompletedAtUtc = now,
                TotalDurationMinutes = totalDuration,
                HasDeviation = hasDeviation,
                CompletedBy = userId
            };

            await _outbox.WriteAsync(
                topic: "wonrich.processing.stage-events.v1",
                key: batchId,
                @event: completedEvent,
                correlationId: completedEvent.CorrelationId,
                cancellationToken: ct);

            // Optionally free mixing tank? For now keep RemainingKg, but mark as completed - dashboard will show no active batch if needed
            // Real process: after Cooling, batch goes to final storage, MT becomes empty
            // For traceability, we keep it but event signals completion
        }

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
