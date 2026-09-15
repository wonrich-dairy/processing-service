using System.ComponentModel.DataAnnotations;

namespace ProcessingService.Domain.Entities;

/// <summary>
/// Processing run - tracks consignment from bowser arrival through to completion.
/// Created at unload time per real process, state AwaitingLabResult.
/// Batch code created at allocation time per user clarification (not at unload).
/// DispatchNumber remains UNIQUE - one run per dispatch, split across tanks via StoringAllocations.
/// </summary>
public class ProcessingRun
{
    public Guid Id { get; set; }

    /// <summary>MCC dispatch ID - must exist in DB for traceability, entered by factory tech - UNIQUE</summary>
    [MaxLength(50)]
    public string DispatchNumber { get; set; } = string.Empty;

    /// <summary>Storing tank where milk first unloaded - primary tank for backward compat</summary>
    public Guid StoringTankId { get; set; }
    public Tank StoringTank { get; set; } = null!;

    /// <summary>Quantity in KG - total unloaded for this dispatch (sum of StoringAllocations) - 2 decimal places</summary>
    public decimal QuantityKg { get; set; }

    /// <summary>Storing tank temperature at unload 1-3°C, deviation flagged but not blocked</summary>
    public decimal TemperatureC { get; set; }

    public bool IsTemperatureDeviation { get; set; }

    public ProcessingRunState State { get; set; } = ProcessingRunState.AwaitingLabResult;

    /// <summary>Quality test status for mock and real quality service - Pending/InProgress/Passed/Failed</summary>
    public QualityTestStatus QualityTestStatus { get; set; } = QualityTestStatus.Pending;

    /// <summary>Hold reason from failed panel - read-only</summary>
    [MaxLength(500)]
    public string? HoldReason { get; set; }

    /// <summary>Batch code created at allocation time: [day]-[product]-[letter] e.g. 1-SY-A - nullable at unload</summary>
    [MaxLength(20)]
    public string? BatchCode { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    [MaxLength(100)]
    public string CreatedBy { get; set; } = string.Empty;

    // Navigation
    public QualityPanel? QualityPanel { get; set; }
    public ICollection<TankAllocation> Allocations { get; set; } = new List<TankAllocation>();
    public ICollection<ProcessingStage> Stages { get; set; } = new List<ProcessingStage>();
    public ICollection<ProcessingRunStoringAllocation> StoringAllocations { get; set; } = new List<ProcessingRunStoringAllocation>();
}
