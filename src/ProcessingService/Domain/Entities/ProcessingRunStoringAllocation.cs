namespace ProcessingService.Domain.Entities;

/// <summary>
/// Allocation of a ProcessingRun (one dispatch) to a specific storing tank.
/// Allows one dispatch to be split across multiple tanks (e.g., DN-20260915-01 70KG = 10KG to ST-01 + 60KG to ST-02).
/// Keeps ProcessingRuns.DispatchNumber UNIQUE - one ProcessingRun per dispatch, many allocations.
/// </summary>
public class ProcessingRunStoringAllocation
{
    public Guid Id { get; set; }

    public Guid ProcessingRunId { get; set; }
    public ProcessingRun ProcessingRun { get; set; } = null!;

    public Guid StoringTankId { get; set; }
    public Tank StoringTank { get; set; } = null!;

    public decimal QuantityKg { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;
}
