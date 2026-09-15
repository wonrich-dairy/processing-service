using ProcessingService.Domain.Entities;

namespace ProcessingService.Application.Stages;

public sealed record StartStageRequest(Guid MixingTankId, Guid ProcessingRunId, StageType StageType);
public sealed record EndStageRequest(decimal EndTemperatureC, bool CultureAdded = false);

public interface IProcessingStageService
{
    Task<ProcessingStage> StartAsync(StartStageRequest request, string userId, CancellationToken ct);
    Task<ProcessingStage> EndAsync(Guid stageId, EndStageRequest request, string userId, CancellationToken ct);
    Task<IReadOnlyList<ProcessingStage>> ListByMixingTankAsync(Guid mixingTankId, CancellationToken ct);
    Task<IReadOnlyList<ProcessingStage>> ListActiveAsync(CancellationToken ct);
    Task<IReadOnlyList<TankAllocation>> ListActiveBatchesAsync(CancellationToken ct);
}
