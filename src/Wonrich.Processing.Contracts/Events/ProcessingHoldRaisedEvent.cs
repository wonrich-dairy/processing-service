namespace Wonrich.Processing.Contracts.Events;

/// <summary>
/// ProcessingHoldRaised published with reason
/// Topic: wonrich.processing.hold-events.v1
/// Key: batchId or dispatchNumber (using dispatchNumber if batch not yet allocated)
/// </summary>
public sealed record ProcessingHoldRaisedEvent : ProcessingEventBase
{
    public string HoldId { get; init; } = string.Empty; // ProcessingRun Id or QualityPanel Id
    public string Reason { get; init; } = string.Empty;
    public DateTime RaisedAtUtc { get; init; }
    public string RaisedBy { get; init; } = string.Empty;
    public string? FailedParameter { get; init; }
    public string? FailedValue { get; init; }
}
