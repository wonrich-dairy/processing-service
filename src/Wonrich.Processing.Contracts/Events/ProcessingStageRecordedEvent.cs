namespace Wonrich.Processing.Contracts.Events;

/// <summary>
/// ProcessingStageRecorded published per stage, carrying stage type, temperature, timings and deviation flag
/// Topic: wonrich.processing.stage-events.v1
/// Key: batchId
/// </summary>
public sealed record ProcessingStageRecordedEvent : ProcessingEventBase
{
    public string MixingTankId { get; init; } = string.Empty;
    public string MixingTankCode { get; init; } = string.Empty;
    public string StageType { get; init; } = string.Empty; // Heating, Homogeniser, Pasteuriser, Cooling
    public DateTime StartTimeUtc { get; init; }
    public DateTime? EndTimeUtc { get; init; }
    public decimal EndTemperatureC { get; init; }
    public int? DurationMinutes { get; init; }
    public bool CultureAdded { get; init; }
    public DateTime? CultureAddedAtUtc { get; init; }
    public string RecordedBy { get; init; } = string.Empty;
}
