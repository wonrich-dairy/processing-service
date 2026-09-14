using System.ComponentModel.DataAnnotations;

namespace ProcessingService.Domain.Entities;

/// <summary>
/// Lab panel results - denormalised for read-only in Processing per DOD 63.
/// Lab Service remains source of truth, values read-only in Processing.
/// Timestamps UTC datetime(6) per AC 57.
/// Decimal precision 2 places per user.
/// </summary>
public class QualityPanel
{
    public Guid Id { get; set; }

    public Guid ProcessingRunId { get; set; }
    public ProcessingRun ProcessingRun { get; set; } = null!;

    /// <summary>Dispatch number for traceability</summary>
    [MaxLength(50)]
    public string DispatchNumber { get; set; } = string.Empty;

    // Quality values - 2 decimal places, HasPrecision(10,2)
    public decimal FatPercent { get; set; }
    public decimal RawLactometerReading { get; set; }
    public decimal TemperatureCelsius { get; set; }
    public decimal WaterPercent { get; set; }

    // SNF, TS, PH from AC examples - also 2 decimal places
    public decimal Snf { get; set; }
    public decimal Ts { get; set; }
    public decimal Ph { get; set; }

    [MaxLength(20)]
    public string KqColour { get; set; } = string.Empty; // varchar per AC

    /// <summary>Alcohol outcomes stored as JSON string - from cascade.ts stagesRun()</summary>
    [MaxLength(1000)]
    public string AlcoholOutcomesJson { get; set; } = string.Empty;

    /// <summary>Alcohol result: Passed 80%, Below 80% acceptable, Fail - determines product line pre-selection per AC 64</summary>
    [MaxLength(50)]
    public string AlcoholResult { get; set; } = string.Empty; // varchar

    public bool SmellOk { get; set; }
    public bool ColourOk { get; set; }
    public bool TasteOk { get; set; }

    [MaxLength(20)]
    public string Verdict { get; set; } = string.Empty; // Accept/Reject varchar

    [MaxLength(100)]
    public string? FailedParameter { get; set; }

    [MaxLength(100)]
    public string? FailedValue { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Sensory confirmation after panel per AC (but real process says before - AC wrong, real correct is before, but we implement both: sensory at unload + confirmation after panel)</summary>
    public bool IsSmellConfirmed { get; set; }
    public bool IsTasteConfirmed { get; set; }

    [MaxLength(100)]
    public string? ConfirmedBy { get; set; }

    public DateTime? ConfirmedAtUtc { get; set; }
}
