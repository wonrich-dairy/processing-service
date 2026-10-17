using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using ProcessingService.Application.Tanks;
using ProcessingService.Infrastructure.Persistence;
using ProcessingService.Tests.Support;
using Xunit;
using static ProcessingService.Tests.Support.Builders;

namespace ProcessingService.Tests.Unit;

/// <summary>
/// Tests for TankTemperatureLogService which is for manual temperature readings.
/// </summary>
public sealed class TankTemperatureLogServiceTests
{
    private readonly ProcessingDbContext _db = TestDb.Create();
    private readonly TankTemperatureLogService _sut;

    public TankTemperatureLogServiceTests()
    {
        _sut = new TankTemperatureLogService(_db, new FakeTimeProvider(Now));
    }

    private async Task<Guid> SeedTankAsync()
    {
        var tank = NewTank();
        await _db.SeedAsync(tank);
        return tank.Id;
    }

    // Valid range is -20 to 100 C inclusive of the ends.
    public static TheoryData<decimal, bool> TemperatureRange => new()
    {
        { -20.01m, false },  // just below
        { -20m,    true },   // at the lower limit
        { 4m,      true },
        { 100m,    true },   // at the upper limit
        { 100.01m, false },  // just above
    };

    [Theory]
    [MemberData(nameof(TemperatureRange))]
    public async Task LogAsync_Temperature_MustBeBetweenMinus20And100(decimal temperatureC, bool accepted)
    {
        var tankId = await SeedTankAsync();

        if (accepted)
        {
            var log = await _sut.LogAsync(tankId, temperatureC, null, "tech-1", CancellationToken.None);
            Assert.Equal(temperatureC, log.TemperatureC);
        }
        else
        {
            await Assert.ThrowsAsync<ArgumentException>(() =>
                _sut.LogAsync(tankId, temperatureC, null, "tech-1", CancellationToken.None));
            Assert.Empty(await _db.TankTemperatureLogs.AsNoTracking().ToListAsync());
        }
    }

    [Fact]
    public async Task LogAsync_UnknownTank_IsRejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.LogAsync(Guid.NewGuid(), 4m, null, "tech-1", CancellationToken.None));
    }

    [Fact]
    public async Task LogAsync_ValidReading_IsSavedWithTrimmedNoteAndClockTime()
    {
        var tankId = await SeedTankAsync();

        await _sut.LogAsync(tankId, 3.456m, "  morning check  ", "tech-9", CancellationToken.None);

        var saved = await _db.TankTemperatureLogs.AsNoTracking().SingleAsync();
        Assert.Equal(3.46m, saved.TemperatureC);       
        Assert.Equal("morning check", saved.Note);     
        Assert.Equal(NowUtc, saved.RecordedAtUtc);     
        Assert.Equal("tech-9", saved.RecordedBy);
    }
}
