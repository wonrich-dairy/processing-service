using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ProcessingService.Application.MccDispatch;
using ProcessingService.Application.ProcessingRuns;
using ProcessingService.Domain.Entities;
using ProcessingService.Infrastructure.Persistence;
using ProcessingService.Tests.Support;
using Xunit;
using static ProcessingService.Tests.Support.Builders;

namespace ProcessingService.Tests.Unit;

/// <summary>
/// Tests for ProcessingRunService: unloading a milk bowser into a storing tank.
/// </summary>
public sealed class ProcessingRunServiceTests
{
    private const string Dispatch = "DN-20260922-01";

    private readonly ProcessingDbContext _db = TestDb.Create();
    private readonly IMccDispatchClient _mcc = Substitute.For<IMccDispatchClient>();
    private readonly ProcessingRunService _sut;

    public ProcessingRunServiceTests()
    {
        // STUB: By default the dispatch exists in MCC but MCC has no detail record so no quantity limit applies.
        _mcc.ExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        _mcc.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((MccDispatchDto?)null);

        _sut = new ProcessingRunService(_db, _mcc, TestMetrics.Create(), new FakeTimeProvider(Now));
    }

    private async Task<Tank> SeedTankAsync(decimal capacityKg = 100_000, decimal remainingKg = 0)
    {
        var tank = NewTank("ST-01", capacityKg, remainingKg);
        await _db.SeedAsync(tank);
        return tank;
    }

    private static CreateUnloadRequest Request(Guid tankId, string dispatch = Dispatch, decimal quantityKg = 100m,
        decimal temperatureC = 2m, bool smellOk = true) => new()
    {
        DispatchNumber = dispatch,
        StoringTankId = tankId,
        QuantityKg = quantityKg,
        TemperatureC = temperatureC,
        SmellOk = smellOk,
        ColourOk = true,
        TasteOk = true
    };

    // 1. Invalid input is rejected before the outside world is contacted.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateUnloadAsync_BlankDispatchNumber_ThrowsAndNeverCallsMcc(string? dispatch)
    {
        var tank = await SeedTankAsync();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.CreateUnloadAsync(Request(tank.Id, dispatch!), "tech-1", CancellationToken.None));

        // Mock verification - The MCC client must not be called at all.
        _ = _mcc.DidNotReceiveWithAnyArgs().ExistsAsync(default!, default);
    }

    [Theory]
    [InlineData("XX-20260910-01")]
    [InlineData("20260910-01")]
    [InlineData("dn20260910")]
    public async Task CreateUnloadAsync_DispatchWithoutDnPrefix_Throws(string dispatch)
    {
        var tank = await SeedTankAsync();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.CreateUnloadAsync(Request(tank.Id, dispatch), "tech-1", CancellationToken.None));

        Assert.Contains("invalid format", ex.Message);
    }

    // 2. Failure paths of the collaborator.
    [Fact]
    public async Task CreateUnloadAsync_DispatchNotFoundInMcc_ThrowsAndSavesNothing()
    {
        var tank = await SeedTankAsync();
        _mcc.ExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false); // STUB: "not found"

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.CreateUnloadAsync(Request(tank.Id), "tech-1", CancellationToken.None));

        Assert.Empty(await _db.ProcessingRuns.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task CreateUnloadAsync_MccIsDown_ExceptionPropagatesAndSavesNothing()
    {
        var tank = await SeedTankAsync();
        _mcc.ExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("MCC unreachable")); // STUB that throws

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            _sut.CreateUnloadAsync(Request(tank.Id), "tech-1", CancellationToken.None));

        Assert.Empty(await _db.ProcessingRuns.AsNoTracking().ToListAsync());
    }

    // 3. Boundary values around the 0.01 kg rounding tolerance.
    // Columns Below - dispatch total, already unloaded, requested, expected result
    public static TheoryData<decimal, decimal, decimal, string> QuantityCases => new()
    {
        { 1000m, 0m,      1000m,    "accepted" },   // Exactly the remaining quantity
        { 1000m, 0m,      1000.01m, "accepted" },   // At the tolerance level 
        { 1000m, 0m,      1000.02m, "too much" },   // Just past the tolerance level
        { 1000m, 999.99m, 0.01m,    "duplicate" },  // Treated as fully unloaded
    };

    [Theory]
    [MemberData(nameof(QuantityCases))]
    public async Task CreateUnloadAsync_QuantityAgainstDispatchRemaining_RespectsTheTolerance(
        decimal totalLitres, decimal alreadyUnloadedKg, decimal requestedKg, string expected)
    {
        var tank = await SeedTankAsync();
        await _db.SeedAsync(NewTrace(Dispatch, totalLitres, alreadyUnloadedKg));
        var request = Request(tank.Id, quantityKg: requestedKg);

        if (expected == "accepted")
        {
            var run = await _sut.CreateUnloadAsync(request, "tech-1", CancellationToken.None);
            Assert.Equal(requestedKg, run.QuantityKg);
        }
        else if (expected == "too much")
        {
            await Assert.ThrowsAsync<ArgumentException>(() => _sut.CreateUnloadAsync(request, "tech-1", CancellationToken.None));
        }
        else
        {
            await Assert.ThrowsAsync<DuplicateDispatchException>(() => _sut.CreateUnloadAsync(request, "tech-1", CancellationToken.None));
        }
    }

    // 4. Temperature outside 1-3 C is flagged but never blocks the unload.
    public static TheoryData<decimal, bool> TemperatureCases => new()
    {
        { 0.99m, true },   // just below the range
        { 1.00m, false },  // at the lower limit
        { 3.00m, false },  // at the upper limit
        { 3.01m, true },   // just above the range
        { 10m,   true },
    };

    [Theory]
    [MemberData(nameof(TemperatureCases))]
    public async Task CreateUnloadAsync_Temperature_SetsTheDeviationFlag(decimal temperatureC, bool expectedDeviation)
    {
        var tank = await SeedTankAsync();

        var run = await _sut.CreateUnloadAsync(Request(tank.Id, temperatureC: temperatureC), "tech-1", CancellationToken.None);

        Assert.Equal(expectedDeviation, run.IsTemperatureDeviation);
    }

    [Fact]
    public async Task CreateUnloadAsync_FailedSmellCheck_IsRejectedAndSavesNothing()
    {
        var tank = await SeedTankAsync();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.CreateUnloadAsync(Request(tank.Id, smellOk: false), "tech-1", CancellationToken.None));

        Assert.Empty(await _db.ProcessingRuns.AsNoTracking().ToListAsync());
    }

    // 5. Happy path.
    [Fact]
    public async Task CreateUnloadAsync_ValidRequest_NormalisesDispatchCreatesRunAndFillsTheTank()
    {
        var tank = await SeedTankAsync(remainingKg: 250m);

        var run = await _sut.CreateUnloadAsync(Request(tank.Id, "  dn-20260922-01 ", 100m), "tech-7", CancellationToken.None);

        Assert.Equal(Dispatch, run.DispatchNumber);                  
        Assert.Equal(ProcessingRunState.AwaitingLabResult, run.State);
        Assert.Equal(100m, run.QuantityKg);
        Assert.Equal("tech-7", run.CreatedBy);
        Assert.Equal(NowUtc, run.CreatedAtUtc);                      
        Assert.Equal(350m, tank.RemainingKg);                      

        // Mock verification - MCC was asked exactly once with the normalized dispatch number.
        _ = _mcc.Received(1).ExistsAsync(Dispatch, Arg.Any<CancellationToken>());
    }
}
