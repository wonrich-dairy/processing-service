using System.ComponentModel.DataAnnotations;

namespace ProcessingService.Domain.Entities;

/// <summary>
/// Processing stage - heating, homogeniser, pasteuriser, cooling in single table per DOD 65.
/// Range thresholds in config, not hardcoded.
/// Timestamps UTC datetime(6) per AC 57.
/// </summary>
public class ProcessingStage
{
    public Guid Id { get; set; }

    public Guid ProcessingRunId { get; set; }
    public ProcessingRun ProcessingRun { get; set; } = null!;

    public Guid MixingTankId { get; set; }
    public Tank MixingTank { get; set; } = null!;

    public StageType StageType { get; set; } // varchar per AC

    public DateTime StartTimeUtc { get; set; } // datetime(6)
    public DateTime? EndTimeUtc { get; set; } // datetime(6)

    /// <summary>End temperature - validated 40-65 heating, 80-85 pasteuriser, 0-4 fresh cooling, 44 yogurt cooling</summary>
    public decimal EndTemperatureC { get; set; } // HasPrecision(10,2)

    public bool IsDeviation { get; set; }

    /// <summary>Duration derived as End - Start in minutes</summary>
    public int? DurationMinutes { get; set; }

    /// <summary>Culture added flag for Yogurt branch at cooling per AC 66</summary>
    public bool CultureAdded { get; set; }
    public DateTime? CultureAddedAtUtc { get; set; }

    [MaxLength(100)]
    public string CreatedBy { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }
}
