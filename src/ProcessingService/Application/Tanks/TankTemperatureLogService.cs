using Microsoft.EntityFrameworkCore;
using ProcessingService.Domain.Entities;
using ProcessingService.Infrastructure.Persistence;

namespace ProcessingService.Application.Tanks;

public interface ITankTemperatureLogService
{
    Task<TankTemperatureLog> LogAsync(Guid tankId, decimal temperatureC, string? note, string userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<TankTemperatureLog>> ListAsync(Guid tankId, CancellationToken cancellationToken);
    Task<TankTemperatureLog?> GetLastAsync(Guid tankId, CancellationToken cancellationToken);
}

public sealed class TankTemperatureLogService : ITankTemperatureLogService
{
    private readonly ProcessingDbContext _db;
    private readonly TimeProvider _time;

    public TankTemperatureLogService(ProcessingDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    public async Task<TankTemperatureLog> LogAsync(Guid tankId, decimal temperatureC, string? note, string userId, CancellationToken cancellationToken)
    {
        var tank = await _db.Tanks.FirstOrDefaultAsync(t => t.Id == tankId, cancellationToken);
        if (tank == null)
            throw new ArgumentException($"Tank not found", nameof(tankId));

        if (temperatureC < -20 || temperatureC > 100)
            throw new ArgumentException($"Temperature {temperatureC}°C out of valid range -20 to 100°C", nameof(temperatureC));

        var now = _time.GetUtcNow().UtcDateTime;

        var log = new TankTemperatureLog
        {
            Id = Guid.NewGuid(),
            TankId = tankId,
            TemperatureC = Math.Round(temperatureC, 2),
            Note = note?.Trim(),
            RecordedAtUtc = now,
            RecordedBy = userId,
            CreatedAtUtc = now
        };

        _db.TankTemperatureLogs.Add(log);
        await _db.SaveChangesAsync(cancellationToken);

        return log;
    }

    public async Task<IReadOnlyList<TankTemperatureLog>> ListAsync(Guid tankId, CancellationToken cancellationToken)
    {
        return await _db.TankTemperatureLogs
            .Where(l => l.TankId == tankId)
            .OrderByDescending(l => l.RecordedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<TankTemperatureLog?> GetLastAsync(Guid tankId, CancellationToken cancellationToken)
    {
        return await _db.TankTemperatureLogs
            .Where(l => l.TankId == tankId)
            .OrderByDescending(l => l.RecordedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }
}

public sealed class CreateTemperatureLogRequest
{
    public decimal TemperatureC { get; set; }
    public string? Note { get; set; }
}
