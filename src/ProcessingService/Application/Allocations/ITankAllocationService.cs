using ProcessingService.Domain.Entities;

namespace ProcessingService.Application.Allocations;

public interface ITankAllocationService
{
    Task<TankAllocation> AllocateAsync(CreateAllocationRequest request, string userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<TankAllocation>> ListAsync(CancellationToken cancellationToken);
    Task<TankAllocation?> GetByBatchCodeAsync(string batchCode, CancellationToken cancellationToken);
}

public sealed class CreateAllocationRequest
{
    public Guid SourceStoringTankId { get; set; }
    public Guid DestinationMixingTankId { get; set; }
    public decimal QuantityKg { get; set; }
    public ProductType ProductType { get; set; }
    public string? OverrideReason { get; set; }
    public Guid? ProcessingRunId { get; set; } // Optional - if not provided, find latest ReleasedForAllocation in source tank
}

public sealed class AllocationValidationException : Exception
{
    public AllocationValidationException(string message) : base(message) { }
}
