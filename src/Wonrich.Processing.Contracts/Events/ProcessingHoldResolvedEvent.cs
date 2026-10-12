namespace Wonrich.Processing.Contracts.Events;

/// <summary>
/// ProcessingHoldResolved published with reason and resolution
/// Topic: wonrich.processing.hold-events.v1
/// Key: batchId or dispatchNumber
/// </summary>
public sealed record ProcessingHoldResolvedEvent : ProcessingEventBase
{
    public string HoldId { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public string Resolution { get; init; } = string.Empty;
    public DateTime RaisedAtUtc { get; init; }
    public DateTime ResolvedAtUtc { get; init; }
    public string ResolvedBy { get; init; } = string.Empty;
}
