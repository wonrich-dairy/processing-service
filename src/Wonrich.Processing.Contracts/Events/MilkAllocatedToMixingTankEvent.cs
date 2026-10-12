namespace Wonrich.Processing.Contracts.Events;

/// <summary>
/// MilkAllocatedToMixingTank published on allocation, carrying both tank identifiers, quantity and product line
/// Topic: wonrich.processing.stage-events.v1 (per docs/kafka.md, processing stage events including allocation)
/// Key: batchId for ordering per batch
/// </summary>
public sealed record MilkAllocatedToMixingTankEvent : ProcessingEventBase
{
    public string SourceStoringTankId { get; init; } = string.Empty;
    public string SourceStoringTankCode { get; init; } = string.Empty;
    public string DestinationMixingTankId { get; init; } = string.Empty;
    public string DestinationMixingTankCode { get; init; } = string.Empty;
    public decimal QuantityKg { get; init; }
    public string ProductType { get; init; } = string.Empty; // SY,SK,FM,FLM,DY
    public string BatchCode { get; init; } = string.Empty; // same as BatchId
    public int BatchNumber { get; init; } // DayOfYear 1-365
    public string BatchLetter { get; init; } = string.Empty; // A-Z
    public DateTime AllocatedAtUtc { get; init; }
    public string AllocatedBy { get; init; } = string.Empty;
    public string? OverrideReason { get; init; }
}
