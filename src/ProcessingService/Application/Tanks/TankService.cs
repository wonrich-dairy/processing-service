using Microsoft.EntityFrameworkCore;
using ProcessingService.Domain.Common;
using ProcessingService.Domain.Tanks;
using ProcessingService.Infrastructure.Persistence;

namespace ProcessingService.Application.Tanks;

/// <summary>A factory tank and what it currently holds (SCRUM-61).</summary>
/// <param name="Code">Short code as painted on the plant.</param>
/// <param name="Name">Tank name.</param>
/// <param name="Kind">Storing or Mixing.</param>
/// <param name="CapacityLitres">Working volume.</param>
/// <param name="HeldLitres">What is in it now.</param>
/// <param name="AvailableLitres">Headroom before it is full.</param>
/// <param name="Status">Whether it is in service.</param>
public sealed record TankView(
    string Code,
    string Name,
    TankKind Kind,
    decimal CapacityLitres,
    decimal HeldLitres,
    decimal AvailableLitres,
    TankStatus Status);

/// <summary>The details a tank is added or amended with.</summary>
public sealed record SaveTankCommand(string Code, string Name, TankKind Kind, decimal CapacityLitres);

/// <summary>The factory's storing and mixing tanks (SCRUM-61).</summary>
public interface ITankService
{
    Task<IReadOnlyList<TankView>> ListAsync(
        TankKind? kind = null,
        CancellationToken cancellationToken = default);

    Task<TankView?> GetAsync(string code, CancellationToken cancellationToken = default);

    Task<TankView> CreateAsync(SaveTankCommand command, CancellationToken cancellationToken = default);

    Task<TankView> UpdateAsync(
        string code,
        SaveTankCommand command,
        CancellationToken cancellationToken = default);

    Task<TankView> ChangeStatusAsync(
        string code,
        TankStatus status,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="ITankService" />
public sealed class TankService : ITankService
{
    private readonly ProcessingDbContext _dbContext;

    public TankService(ProcessingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<TankView>> ListAsync(
        TankKind? kind = null,
        CancellationToken cancellationToken = default)
    {
        var tanks = _dbContext.ProcessingTanks.AsNoTracking();

        if (kind is { } wanted)
        {
            tanks = tanks.Where(tank => tank.Kind == wanted);
        }

        var found = await tanks.OrderBy(tank => tank.Code).ToListAsync(cancellationToken);
        var held = await HeldByTankAsync(cancellationToken);

        return found.Select(tank => ToView(tank, held)).ToList();
    }

    public async Task<TankView?> GetAsync(string code, CancellationToken cancellationToken = default)
    {
        var tank = await FindAsync(code, cancellationToken);

        return tank is null ? null : ToView(tank, await HeldByTankAsync(cancellationToken));
    }

    public async Task<TankView> CreateAsync(
        SaveTankCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var code = command.Code?.Trim().ToUpperInvariant() ?? string.Empty;

        if (await _dbContext.ProcessingTanks.AnyAsync(tank => tank.Code == code, cancellationToken))
        {
            throw new DuplicateCodeException("ProcessingTank", code);
        }

        var tank = new ProcessingTank(
            Guid.NewGuid(),
            code,
            command.Name,
            command.Kind,
            command.CapacityLitres);

        _dbContext.ProcessingTanks.Add(tank);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ToView(tank, await HeldByTankAsync(cancellationToken));
    }

    public async Task<TankView> UpdateAsync(
        string code,
        SaveTankCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var tank = await FindAsync(code, cancellationToken)
            ?? throw new EntityNotFoundException("ProcessingTank", code);

        // Neither the code nor the kind is amendable. The code is painted on the plant and named
        // on every record the tank appears in, and an unload names a storing tank while an
        // allocation names a mixing one - a tank that changed kind would make its own history
        // unreadable.
        tank.Describe(command.Name, command.CapacityLitres);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return ToView(tank, await HeldByTankAsync(cancellationToken));
    }

    public async Task<TankView> ChangeStatusAsync(
        string code,
        TankStatus status,
        CancellationToken cancellationToken = default)
    {
        var tank = await FindAsync(code, cancellationToken)
            ?? throw new EntityNotFoundException("ProcessingTank", code);

        var held = await HeldByTankAsync(cancellationToken);

        tank.ChangeStatus(status, held.GetValueOrDefault(tank.Id));

        await _dbContext.SaveChangesAsync(cancellationToken);

        return ToView(tank, held);
    }

    private Task<ProcessingTank?> FindAsync(string code, CancellationToken cancellationToken)
    {
        var wanted = code?.Trim().ToUpperInvariant() ?? string.Empty;

        return _dbContext.ProcessingTanks.FirstOrDefaultAsync(
            tank => tank.Code == wanted,
            cancellationToken);
    }

    /// <summary>
    /// What each storing tank holds, totalled from the unloads that went into it. Allocation out
    /// of a storing tank is SCRUM-64; until it lands, an unload is the only thing that moves the
    /// figure, and this is the one place that will have to account for the other direction.
    /// </summary>
    private async Task<Dictionary<Guid, decimal>> HeldByTankAsync(CancellationToken cancellationToken) =>
        await _dbContext.Unloads
            .AsNoTracking()
            .GroupBy(unload => unload.StoringTankId)
            .Select(group => new { TankId = group.Key, Litres = group.Sum(u => u.QuantityLitres) })
            .ToDictionaryAsync(row => row.TankId, row => row.Litres, cancellationToken);

    private static TankView ToView(ProcessingTank tank, IReadOnlyDictionary<Guid, decimal> held)
    {
        var inTank = held.TryGetValue(tank.Id, out var litres) ? litres : 0m;

        return new TankView(
            tank.Code,
            tank.Name,
            tank.Kind,
            tank.CapacityLitres,
            inTank,
            Math.Max(0m, tank.CapacityLitres - inTank),
            tank.Status);
    }
}
