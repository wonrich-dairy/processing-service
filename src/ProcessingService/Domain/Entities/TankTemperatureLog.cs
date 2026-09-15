namespace ProcessingService.Domain.Entities;

/// <summary>
/// Temperature log for processing tanks (ST and MT) - similar to MCC tanks.
/// Worker can log temperature when needed with optional note, timestamp stored.
/// First field when touching ST/MT card in tanks section.
/// </summary>
public class TankTemperatureLog
{
    public Guid Id { get; set; }

    public Guid TankId { get; set; }
    public Tank Tank { get; set; } = null!;

    public decimal TemperatureC { get; set; }

    public string? Note { get; set; }

    public DateTime RecordedAtUtc { get; set; }

    public string RecordedBy { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }
}
