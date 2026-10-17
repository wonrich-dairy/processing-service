using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ProcessingService.Application.Outbox;
using ProcessingService.Application.Stages;
using ProcessingService.Domain.Entities;
using ProcessingService.Infrastructure.Persistence;
using ProcessingService.Tests.Support;
using Wonrich.Processing.Contracts.Events;
using Xunit;
using static ProcessingService.Tests.Support.Builders;

namespace ProcessingService.Tests.Unit;

/// <summary>
/// Tests for ProcessingStageService - For heating, homogeniser, pasteuriser and cooling in a mixing tank.
/// </summary>
public sealed class ProcessingStageServiceTests
{
    private const string Topic = "wonrich.processing.stage-events.v1";

    private readonly ProcessingDbContext _db = TestDb.Create();
    private readonly IOutboxWriter _outbox = Substitute.For<IOutboxWriter>();
    private readonly FakeTimeProvider _clock = new(Now);
    private readonly ProcessingStageService _sut;

    public ProcessingStageServiceTests()
    {
        _sut = new ProcessingStageService(_db, _clock, _outbox);
    }

    private async Task<(Tank Mixing, ProcessingRun Run)> SeedBatchAsync(decimal mixingRemainingKg = 500m)
    {
        var source = NewTank("ST-01");
        var mixing = NewTank("MT-01", capacityKg: 3000, remainingKg: mixingRemainingKg);
        var run = NewRun(source.Id, batchCode: "BATCH-1");
        await _db.SeedAsync(source, mixing, run);
        return (mixing, run);
    }

    private Task<ProcessingStage> StartStage(Tank mixing, ProcessingRun run, StageType type) =>
        _sut.StartAsync(new StartStageRequest(mixing.Id, run.Id, type), "tech-1", CancellationToken.None);

    // 1. Starting a stage: saved and exactly one event handed to the outbox.
    [Fact]
    public async Task StartAsync_ValidRequest_SavesTheStageAndWritesOneEvent()
    {
        var (mixing, run) = await SeedBatchAsync();

        await StartStage(mixing, run, StageType.Heating);

        var saved = await _db.ProcessingStages.AsNoTracking().SingleAsync();
        Assert.Equal(NowUtc, saved.StartTimeUtc);   // from the fake clock
        Assert.Null(saved.EndTimeUtc);              // still running

        // Mock verification - one call, right topic and keyed by the batch id.
        _ = _outbox.Received(1).WriteAsync(Topic, "BATCH-1", Arg.Any<ProcessingStageRecordedEvent>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());

        var evt = Assert.Single(OutboxProbe.Events<ProcessingStageRecordedEvent>(_outbox));
        Assert.Equal("Heating", evt.StageType);
        Assert.Equal("MT-01", evt.MixingTankCode);
    }

    // 2. A tank with almost no milk cannot be processed. 0.01 kg counts as empty.
    public static TheoryData<decimal, bool> TankMilkLevels => new()
    {
        { 500m,  true },
        { 0.01m, false },
        { 0m,    false },
    };

    [Theory]
    [MemberData(nameof(TankMilkLevels))]
    public async Task StartAsync_TankMilkLevel_DecidesIfAStageCanStart(decimal remainingKg, bool allowed)
    {
        var (mixing, run) = await SeedBatchAsync(remainingKg);

        if (allowed)
        {
            await StartStage(mixing, run, StageType.Heating);
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => StartStage(mixing, run, StageType.Heating));
            // Mock verification - Nothing may reach the outbox when validation fails.
            OutboxProbe.AssertNothingWritten(_outbox);
        }
    }

    // 3. Stages cannot be repeated or run backwards.
    // Columns below - stage that already ended, stage being started now
    public static TheoryData<StageType, StageType> RejectedOrder => new()
    {
        { StageType.Homogeniser, StageType.Homogeniser },  // repeat
        { StageType.Pasteuriser, StageType.Homogeniser },  // backwards
        { StageType.Cooling,     StageType.Cooling },      // repeat after the last stage
    };

    [Theory]
    [MemberData(nameof(RejectedOrder))]
    public async Task StartAsync_RepeatingOrGoingBackwards_IsRejected(StageType lastEnded, StageType requested)
    {
        var (mixing, run) = await SeedBatchAsync();
        await _db.SeedAsync(NewStage(run.Id, mixing.Id, lastEnded, NowUtc.AddHours(-2), NowUtc.AddHours(-1)));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => StartStage(mixing, run, requested));

        Assert.Contains("Cannot start", ex.Message);
        OutboxProbe.AssertNothingWritten(_outbox);
    }

    // 4. Failure path - The outbox is down so the stage must not be saved either.
    [Fact]
    public async Task StartAsync_OutboxWriterFails_TheStageIsNotSaved()
    {
        var (mixing, run) = await SeedBatchAsync();
        _outbox.WriteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ProcessingStageRecordedEvent>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("outbox down"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => StartStage(mixing, run, StageType.Heating));

        Assert.Empty(await _db.ProcessingStages.AsNoTracking().ToListAsync());
    }

    private async Task<ProcessingStage> SeedRunningStageAsync(StageType type)
    {
        var (mixing, run) = await SeedBatchAsync();
        var stage = NewStage(run.Id, mixing.Id, type, NowUtc);
        await _db.SeedAsync(stage);
        return stage;
    }

    // 5. Temperature limits are inclusive - heating 40-65 C, pasteuriser 80-85 C.
    public static TheoryData<StageType, decimal, bool> DeviationCases => new()
    {
        { StageType.Heating,     39.99m, true },   // just below
        { StageType.Heating,     40m,    false },  // at the limit
        { StageType.Heating,     65.01m, true },   // just above
        { StageType.Pasteuriser, 79.99m, true },
        { StageType.Pasteuriser, 80m,    false },
        { StageType.Pasteuriser, 85.01m, true },
    };

    [Theory]
    [MemberData(nameof(DeviationCases))]
    public async Task EndAsync_EndTemperature_IsCheckedAgainstTheStageRange(StageType type, decimal temperatureC, bool expectedDeviation)
    {
        var stage = await SeedRunningStageAsync(type);

        var ended = await _sut.EndAsync(stage.Id, new EndStageRequest(temperatureC), "tech-1", CancellationToken.None);

        Assert.Equal(expectedDeviation, ended.IsDeviation);
    }

    // 6. Duration = whole minutes between start and end.
    [Theory]
    [InlineData(59, 0)]
    [InlineData(60, 1)]
    [InlineData(3600, 60)]
    public async Task EndAsync_Duration_IsWholeMinutes(int secondsElapsed, int expectedMinutes)
    {
        var stage = await SeedRunningStageAsync(StageType.Heating);
        _clock.Advance(TimeSpan.FromSeconds(secondsElapsed));

        var ended = await _sut.EndAsync(stage.Id, new EndStageRequest(50m), "tech-1", CancellationToken.None);

        Assert.Equal(expectedMinutes, ended.DurationMinutes);
    }

    // 7. Ending the LAST stage of cooling writes a second event - processing completed.
    [Fact]
    public async Task EndAsync_CoolingStage_WritesStageEventThenCompletedEvent()
    {
        var (mixing, run) = await SeedBatchAsync();
        var heating = NewStage(run.Id, mixing.Id, StageType.Heating, NowUtc.AddMinutes(-90), NowUtc.AddMinutes(-60));
        var cooling = NewStage(run.Id, mixing.Id, StageType.Cooling, NowUtc.AddMinutes(-10));
        await _db.SeedAsync(heating, cooling);

        await _sut.EndAsync(cooling.Id, new EndStageRequest(4m), "tech-1", CancellationToken.None);

        var calls = OutboxProbe.Calls(_outbox);
        Assert.Equal(2, calls.Count);
        Assert.IsType<ProcessingStageRecordedEvent>(calls[0].Event);
        var completed = Assert.IsType<ProcessingCompletedEvent>(calls[1].Event);
        Assert.Equal(90, completed.TotalDurationMinutes);   // from the first stage's start to now
    }
}
