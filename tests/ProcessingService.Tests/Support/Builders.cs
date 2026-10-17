using ProcessingService.Domain.Entities;

namespace ProcessingService.Tests.Support;

/// <summary>Short builders for valid entities so each test only spells out the one value it is about.</summary>
public static class Builders
{
    public static readonly DateTimeOffset Now = new(2026, 9, 22, 10, 0, 0, TimeSpan.Zero);
    public static readonly DateTime NowUtc = Now.UtcDateTime;
    private static readonly DateTime Stamp = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static Tank NewTank(string code = "ST-01", decimal capacityKg = 5000, decimal remainingKg = 0,
        TankStatus status = TankStatus.Active) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Kind = code.StartsWith("MT", StringComparison.Ordinal) ? TankKind.Mixing : TankKind.Storing,
        CapacityKg = capacityKg,
        RemainingKg = remainingKg,
        Status = status,
        RowVersion = new byte[8],
        CreatedAtUtc = Stamp,
        UpdatedAtUtc = Stamp,
        CreatedBy = "seed",
        UpdatedBy = "seed"
    };

    public static ProcessingRun NewRun(Guid storingTankId, string dispatchNumber = "DN-20260922-01",
        decimal quantityKg = 0, string? batchCode = null) => new()
    {
        Id = Guid.NewGuid(),
        DispatchNumber = dispatchNumber,
        StoringTankId = storingTankId,
        QuantityKg = quantityKg,
        TemperatureC = 2m,
        State = ProcessingRunState.AwaitingLabResult,
        QualityTestStatus = QualityTestStatus.Pending,
        BatchCode = batchCode,
        CreatedAtUtc = Stamp,
        UpdatedAtUtc = Stamp,
        CreatedBy = "seed"
    };

    public static MccDispatchTrace NewTrace(string reference, decimal totalLitres, decimal unloadedKg = 0) => new()
    {
        Id = Guid.NewGuid(),
        Reference = reference,
        BowserRegistration = "WP-1234",
        TotalQuantityLitres = totalLitres,
        TotalUnloadedKg = unloadedKg,
        DispatchedBy = "seed",
        RecordedAtUtc = Stamp,
        CreatedAtUtc = Stamp,
        LastSyncedAtUtc = Stamp
    };

    public static ProcessingStage NewStage(Guid runId, Guid mixingTankId, StageType type, DateTime startUtc,
        DateTime? endUtc = null) => new()
    {
        Id = Guid.NewGuid(),
        ProcessingRunId = runId,
        MixingTankId = mixingTankId,
        StageType = type,
        StartTimeUtc = startUtc,
        EndTimeUtc = endUtc,
        EndTemperatureC = endUtc is null ? 0 : 50,
        CreatedBy = "seed",
        CreatedAtUtc = startUtc
    };
}
