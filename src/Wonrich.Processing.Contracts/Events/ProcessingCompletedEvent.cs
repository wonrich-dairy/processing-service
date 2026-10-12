namespace Wonrich.Processing.Contracts.Events;

/// <summary>
/// ProcessingCompleted published when a run closes (Cooling ended)
/// Topic: wonrich.processing.stage-events.v1
/// Key: batchId
/// </summary>
public sealed record ProcessingCompletedEvent : ProcessingEventBase
{
    public string MixingTankId { get; init; } = string.Empty;
    public string MixingTankCode { get; init; } = string.Empty;
    public DateTime CompletedAtUtc { get; init; }
    public int TotalDurationMinutes { get; init; }
    public bool HasDeviation { get; init; } // true if any stage had deviation
    public string CompletedBy { get; init; } = string.Empty;
}
