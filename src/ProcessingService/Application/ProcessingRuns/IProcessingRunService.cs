using ProcessingService.Domain.Entities;

namespace ProcessingService.Application.ProcessingRuns;

public interface IProcessingRunService
{
    Task<ProcessingRun> CreateUnloadAsync(CreateUnloadRequest request, string userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProcessingRun>> ListAsync(CancellationToken cancellationToken);
    Task<ProcessingRun?> GetByDispatchAsync(string dispatchNumber, CancellationToken cancellationToken);
    Task<decimal> GetTotalUnloadedForDispatchAsync(string dispatchNumber, CancellationToken cancellationToken);
}

public sealed class CreateUnloadRequest
{
    public string DispatchNumber { get; set; } = string.Empty;
    public Guid StoringTankId { get; set; }
    public decimal QuantityKg { get; set; }
    public decimal TemperatureC { get; set; }
    public bool SmellOk { get; set; }
    public bool ColourOk { get; set; }
    public bool TasteOk { get; set; }
}
