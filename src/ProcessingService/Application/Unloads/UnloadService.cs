using System.Globalization;
using Microsoft.EntityFrameworkCore;
using ProcessingService.Application.Abstractions;
using ProcessingService.Domain.Common;
using ProcessingService.Domain.Runs;
using ProcessingService.Domain.Tanks;
using ProcessingService.Domain.Unloads;
using ProcessingService.Infrastructure.Persistence;

namespace ProcessingService.Application.Unloads;

/// <summary>A load unloaded into a storing tank (SCRUM-62).</summary>
/// <param name="Reference">Factory reference, <c>UNL-YYYYMMDD-NN</c>.</param>
/// <param name="DispatchNoteReference">The note the bowser arrived against.</param>
/// <param name="StoringTankCode">Tank the load went into.</param>
/// <param name="StoringTankName">That tank's name.</param>
/// <param name="QuantityLitres">Litres the factory measured.</param>
/// <param name="TemperatureCelsius">Temperature on arrival.</param>
/// <param name="IsTemperatureDeviation">True when the arrival temperature fell outside 1 to 3 °C.</param>
/// <param name="UnloadedAtLocal">Wall-clock time at the factory.</param>
/// <param name="UnloadDate">Date it is filed under.</param>
/// <param name="UnloadedBy">Who recorded it.</param>
/// <param name="RecordedAtUtc">When the record was written.</param>
public sealed record UnloadView(
    string Reference,
    string DispatchNoteReference,
    string StoringTankCode,
    string StoringTankName,
    decimal QuantityLitres,
    decimal TemperatureCelsius,
    bool IsTemperatureDeviation,
    DateTime UnloadedAtLocal,
    DateOnly UnloadDate,
    string? UnloadedBy,
    DateTime RecordedAtUtc);

/// <summary>What an officer records when a bowser is emptied.</summary>
public sealed record RecordUnloadCommand(
    string DispatchNoteReference,
    string StoringTankCode,
    decimal QuantityLitres,
    decimal TemperatureCelsius,
    DateTime? UnloadedAtLocal = null,
    string? UnloadedBy = null);

/// <summary>Bowser loads arriving at the factory (SCRUM-62).</summary>
public interface IUnloadService
{
    Task<IReadOnlyList<UnloadView>> ListAsync(
        DateOnly? unloadDate = null,
        CancellationToken cancellationToken = default);

    Task<UnloadView?> GetAsync(string reference, CancellationToken cancellationToken = default);

    Task<UnloadView> RecordAsync(
        RecordUnloadCommand command,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IUnloadService" />
public sealed class UnloadService : IUnloadService
{
    private const int MaxReferenceAttempts = 5;

    private readonly ProcessingDbContext _dbContext;
    private readonly IFactoryClock _clock;
    private readonly ILogger<UnloadService> _logger;

    public UnloadService(
        ProcessingDbContext dbContext,
        IFactoryClock clock,
        ILogger<UnloadService> logger)
    {
        _dbContext = dbContext;
        _clock = clock;
        _logger = logger;
    }

    public async Task<IReadOnlyList<UnloadView>> ListAsync(
        DateOnly? unloadDate = null,
        CancellationToken cancellationToken = default)
    {
        var unloads = _dbContext.Unloads.AsNoTracking().Include(unload => unload.StoringTank);

        var query = unloadDate is { } date
            ? unloads.Where(unload => unload.UnloadDate == date)
            : unloads;

        return await query
            .OrderByDescending(unload => unload.UnloadedAtLocal)
            .Select(unload => ToView(unload))
            .ToListAsync(cancellationToken);
    }

    public async Task<UnloadView?> GetAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        var wanted = reference?.Trim().ToUpperInvariant() ?? string.Empty;

        var unload = await _dbContext.Unloads
            .AsNoTracking()
            .Include(one => one.StoringTank)
            .FirstOrDefaultAsync(one => one.Reference == wanted, cancellationToken);

        return unload is null ? null : ToView(unload);
    }

    public async Task<UnloadView> RecordAsync(
        RecordUnloadCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var tankCode = command.StoringTankCode?.Trim().ToUpperInvariant() ?? string.Empty;

        var tank = await _dbContext.ProcessingTanks
            .FirstOrDefaultAsync(one => one.Code == tankCode, cancellationToken)
            ?? throw new EntityNotFoundException("ProcessingTank", command.StoringTankCode ?? string.Empty);

        var dispatchNote = command.DispatchNoteReference?.Trim().ToUpperInvariant() ?? string.Empty;

        if (await _dbContext.Unloads.AnyAsync(
                one => one.DispatchNoteReference == dispatchNote,
                cancellationToken))
        {
            throw new DispatchAlreadyUnloadedException(dispatchNote);
        }

        var recordedAtUtc = _clock.UtcNow;
        var unloadedAtLocal = command.UnloadedAtLocal ?? _clock.ToLocal(recordedAtUtc);

        var held = await _dbContext.Unloads
            .Where(one => one.StoringTankId == tank.Id)
            .SumAsync(one => (decimal?)one.QuantityLitres, cancellationToken) ?? 0m;

        // The reference carries the day, so two unloads on the same day race for the same number.
        // The unique index settles it; this retries so the officer is not shown a collision they
        // can do nothing about.
        for (var attempt = 1; ; attempt++)
        {
            var unload = Unload.Record(
                Guid.NewGuid(),
                await NextReferenceAsync(DateOnly.FromDateTime(unloadedAtLocal), cancellationToken),
                dispatchNote,
                tank,
                command.QuantityLitres,
                command.TemperatureCelsius,
                command.UnloadedBy,
                unloadedAtLocal,
                recordedAtUtc,
                held);

            // A run is born with its unload and the two are saved together, so an unload can never
            // exist without its run and a run never without its unload. The unique index on the
            // run's unload reference is what settles a race; this keeps the normal path to one.
            var run = ProcessingRun.Start(Guid.NewGuid(), unload, recordedAtUtc);

            _dbContext.Unloads.Add(unload);
            _dbContext.ProcessingRuns.Add(run);

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);

                return (await GetAsync(unload.Reference, cancellationToken))!;
            }
            catch (DbUpdateException) when (attempt < MaxReferenceAttempts)
            {
                _dbContext.Entry(unload).State = EntityState.Detached;
                _dbContext.Entry(run).State = EntityState.Detached;

                _logger.LogWarning(
                    "Reference {Reference} was taken while recording an unload against {Note}; "
                    + "retrying (attempt {Attempt} of {Max}).",
                    unload.Reference,
                    dispatchNote,
                    attempt,
                    MaxReferenceAttempts);
            }
        }
    }

    /// <summary>
    /// <c>UNL-YYYYMMDD-NN</c>, numbered within the factory's day. Bucketed on the local date the
    /// unload is filed under, so a load taken before dawn does not land under the previous day.
    /// </summary>
    private async Task<string> NextReferenceAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var prefix = $"UNL-{date:yyyyMMdd}-";

        var taken = await _dbContext.Unloads
            .AsNoTracking()
            .Where(unload => unload.UnloadDate == date)
            .Select(unload => unload.Reference)
            .ToListAsync(cancellationToken);

        var highest = taken
            .Where(reference => reference.StartsWith(prefix, StringComparison.Ordinal))
            .Select(reference => reference[prefix.Length..])
            .Select(tail => int.TryParse(tail, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max();

        return prefix + (highest + 1).ToString("00", CultureInfo.InvariantCulture);
    }

    private static UnloadView ToView(Unload unload) => new(
        unload.Reference,
        unload.DispatchNoteReference,
        unload.StoringTank?.Code ?? string.Empty,
        unload.StoringTank?.Name ?? string.Empty,
        unload.QuantityLitres,
        unload.TemperatureCelsius,
        unload.IsTemperatureDeviation,
        unload.UnloadedAtLocal,
        unload.UnloadDate,
        unload.UnloadedBy,
        unload.RecordedAtUtc);
}