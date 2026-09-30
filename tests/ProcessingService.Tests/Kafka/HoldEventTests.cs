using Microsoft.EntityFrameworkCore;
using ProcessingService.Application.Outbox;
using ProcessingService.Application.QualityTests;
using ProcessingService.Domain.Entities;
using ProcessingService.Infrastructure.Persistence;
using Xunit;

namespace ProcessingService.Tests.Kafka;

/// <summary>
/// SCRUM-68: a failed lab result raises a hold, and a passing re-test resolves it,
/// each publishing its event through the outbox.
/// </summary>
public sealed class HoldEventTests
{
    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 9, 30, 10, 0, 0, TimeSpan.Zero);
    }

    private static SubmitQualityResultRequest Result(bool smellOk, string verdict) => new()
    {
        FatPercent = 3.85m,
        RawLactometerReading = 28.5m,
        TemperatureCelsius = 20m,
        WaterPercent = 0m,
        KqColour = "Blue",
        AlcoholOutcomesJson = "{\"Alcohol80\":\"Negative\"}",
        AlcoholResult = "Passed 80%",
        SmellOk = smellOk,
        ColourOk = true,
        TasteOk = true,
        Verdict = verdict,
        Snf = 8.69m,
        Ts = 12.54m,
        Ph = 6.7m
    };

    [Fact]
    public async Task FailedRun_CanBeRetested_AndPassingRetestPublishesHoldResolved()
    {
        var options = new DbContextOptionsBuilder<ProcessingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new ProcessingDbContext(options);
        var time = new FixedTime();
        var client = new MockQualityTestClient(db, time, new OutboxWriter(db, time));

        var tankId = Guid.NewGuid();
        db.Tanks.Add(new Tank
        {
            Id = tankId, Code = "ST-01", Kind = TankKind.Storing, Status = TankStatus.Active,
            CapacityKg = 5000, RemainingKg = 800, CreatedBy = "test", UpdatedBy = "test", RowVersion = new byte[8]
        });
        db.ProcessingRuns.Add(new ProcessingRun
        {
            Id = Guid.NewGuid(), DispatchNumber = "DN-20260930-01", StoringTankId = tankId,
            QuantityKg = 800, TemperatureC = 2.5m, CreatedBy = "test"
        });
        await db.SaveChangesAsync();

        await client.StartTestAsync("DN-20260930-01", "tester", CancellationToken.None);
        await client.SubmitResultAsync("DN-20260930-01", Result(smellOk: false, verdict: "Reject"), "tester", CancellationToken.None);

        var run = await db.ProcessingRuns.SingleAsync();
        Assert.Equal(QualityTestStatus.Failed, run.QualityTestStatus);
        Assert.Equal(ProcessingRunState.OnHold, run.State);
        Assert.Contains(await db.OutboxMessages.ToListAsync(), m => m.EventType == "ProcessingHoldRaisedEvent");

        await client.StartTestAsync("DN-20260930-01", "tester", CancellationToken.None);
        await client.SubmitResultAsync("DN-20260930-01", Result(smellOk: true, verdict: "Accept"), "tester", CancellationToken.None);

        run = await db.ProcessingRuns.SingleAsync();
        Assert.Equal(QualityTestStatus.Passed, run.QualityTestStatus);
        Assert.Equal(ProcessingRunState.ReleasedForAllocation, run.State);
        Assert.Contains(await db.OutboxMessages.ToListAsync(), m => m.EventType == "ProcessingHoldResolvedEvent");
    }

    [Fact]
    public async Task PassedRun_CannotBeStartedAgain()
    {
        var options = new DbContextOptionsBuilder<ProcessingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new ProcessingDbContext(options);
        var time = new FixedTime();
        var client = new MockQualityTestClient(db, time, new OutboxWriter(db, time));

        var tankId = Guid.NewGuid();
        db.Tanks.Add(new Tank
        {
            Id = tankId, Code = "ST-01", Kind = TankKind.Storing, Status = TankStatus.Active,
            CapacityKg = 5000, RemainingKg = 800, CreatedBy = "test", UpdatedBy = "test", RowVersion = new byte[8]
        });
        db.ProcessingRuns.Add(new ProcessingRun
        {
            Id = Guid.NewGuid(), DispatchNumber = "DN-20260930-02", StoringTankId = tankId,
            QuantityKg = 800, TemperatureC = 2.5m, CreatedBy = "test",
            QualityTestStatus = QualityTestStatus.Passed, State = ProcessingRunState.ReleasedForAllocation
        });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.StartTestAsync("DN-20260930-02", "tester", CancellationToken.None));
    }
}
