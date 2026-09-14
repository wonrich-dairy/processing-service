using System.ComponentModel.DataAnnotations;

namespace ProcessingService.Domain.Entities;

/// <summary>
/// Allocation from storing tank to mixing tank. Batch code created at allocation time with timestamp.
/// Remaining quantity check inside transaction with row-level locking per DOD 64.
/// </summary>
public class TankAllocation
{
    public Guid Id { get; set; }

    public Guid ProcessingRunId { get; set; }
    public ProcessingRun ProcessingRun { get; set; } = null!;

    public Guid SourceStoringTankId { get; set; }
    public Tank SourceStoringTank { get; set; } = null!;

    public Guid DestinationMixingTankId { get; set; }
    public Tank DestinationMixingTank { get; set; } = null!;

    /// <summary>Quantity allocated in KG - 2 decimal places</summary>
    public decimal QuantityKg { get; set; }

    /// <summary>Product type SY,SK,FM,FLM,DK - varchar per AC 57, pre-selected from alcohol result per AC 64</summary>
    public ProductType ProductType { get; set; }

    /// <summary>Day of year 1-365 - no year in code per batch code spec</summary>
    public int BatchNumber { get; set; }

    /// <summary>Batch letter A-Z per product per day - concurrency-safe increment</summary>
    [MaxLength(2)]
    public string BatchLetter { get; set; } = string.Empty;

    /// <summary>Batch code [day]-[product]-[letter] e.g. 1-SY-A - unique constraint</summary>
    [MaxLength(20)]
    public string BatchCode { get; set; } = string.Empty;

    /// <summary>Timestamp when batch created - noted per user requirement</summary>
    public DateTime AllocatedAtUtc { get; set; }

    /// <summary>Override reason required if product line changed from pre-selected per AC 64</summary>
    [MaxLength(500)]
    public string? OverrideReason { get; set; }

    [MaxLength(100)]
    public string CreatedBy { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }
}
