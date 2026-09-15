using Microsoft.EntityFrameworkCore;
using ProcessingService.Api.Models.Tanks;
using ProcessingService.Domain.Entities;
using ProcessingService.Infrastructure.Persistence;
using ProcessingService.Api.Infrastructure.Observability;

namespace ProcessingService.Application.Tanks;

public sealed class TankService : ITankService
{
    private readonly ProcessingDbContext _db;
    private readonly ProcessingMetrics _metrics;
    private readonly TimeProvider _time;

    public TankService(ProcessingDbContext db, ProcessingMetrics metrics, TimeProvider time)
    {
        _db = db;
        _metrics = metrics;
        _time = time;
    }

    public async Task<IReadOnlyList<TankResponse>> ListAsync(TankKind? kind, TankStatus? status, bool activeOnly, CancellationToken cancellationToken)
    {
        var query = _db.Tanks.AsNoTracking().AsQueryable();

        if (kind.HasValue)
            query = query.Where(t => t.Kind == kind.Value);

        if (activeOnly)
            query = query.Where(t => t.Status == TankStatus.Active);
        else if (status.HasValue)
            query = query.Where(t => t.Status == status.Value);

        var tanks = await query.OrderBy(t => t.Code).ToListAsync(cancellationToken);

        return tanks.Select(ToResponse).ToList();
    }

    public async Task<TankResponse?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var tank = await _db.Tanks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        return tank == null ? null : ToResponse(tank);
    }

    public async Task<TankResponse> CreateAsync(CreateTankRequest request, string userId, CancellationToken cancellationToken)
    {
        // Normalize code: st1 -> ST-01, MT2 -> MT-02 per user requirement
        var normalizedCode = Tank.NormalizeCode(request.Code);

        // Unique check - case-insensitive via normalized uppercase
        if (await _db.Tanks.AnyAsync(t => t.Code == normalizedCode, cancellationToken))
            throw new DuplicateTankCodeException(normalizedCode);

        if (!request.Kind.HasValue)
            throw new ArgumentException("Type is required.", nameof(request.Kind));

        if (request.CapacityKg <= 0)
            throw new ArgumentException("Capacity must be positive.", nameof(request.CapacityKg));

        // Fix inconsistency: ST must be Storing, MT must be Mixing - auto-derive validation
        if (normalizedCode.StartsWith("ST-") && request.Kind.Value != TankKind.Storing)
            throw new ArgumentException($"Code '{normalizedCode}' is ST (Storing) but kind is {request.Kind.Value}. ST must be Storing tank.", nameof(request.Kind));

        if (normalizedCode.StartsWith("MT-") && request.Kind.Value != TankKind.Mixing)
            throw new ArgumentException($"Code '{normalizedCode}' is MT (Mixing) but kind is {request.Kind.Value}. MT must be Mixing tank.", nameof(request.Kind));

        var now = _time.GetUtcNow().UtcDateTime;

        var tank = new Tank
        {
            Id = Guid.NewGuid(),
            Code = normalizedCode,
            Kind = request.Kind.Value,
            CapacityKg = request.CapacityKg,
            RemainingKg = 0, // empty on creation, will be updated on unload/allocation for next-day retrieval
            Status = TankStatus.Active, // default Active per user
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            CreatedBy = userId,
            UpdatedBy = userId
        };

        _db.Tanks.Add(tank);
        await _db.SaveChangesAsync(cancellationToken);

        _metrics.TanksInUse.Add(1, new System.Diagnostics.TagList { { "kind", tank.Kind.ToString() }, { "status", tank.Status.ToString() } });

        return ToResponse(tank);
    }

    public async Task<TankResponse> UpdateAsync(Guid id, UpdateTankRequest request, string userId, CancellationToken cancellationToken)
    {
        var tank = await _db.Tanks.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (tank == null)
            throw new TankNotFoundException(id);

        if (request.CapacityKg <= 0)
            throw new ArgumentException("Capacity must be positive.", nameof(request.CapacityKg));

        // Only CapacityKg editable per user clarification - Code and Kind NOT amendable for history readability
        // Check if new capacity less than remaining -> reject
        if (request.CapacityKg < tank.RemainingKg)
            throw new ArgumentException($"Capacity {request.CapacityKg} KG cannot be less than remaining {tank.RemainingKg} KG.", nameof(request.CapacityKg));

        // Concurrency token check
        if (request.RowVersion != null)
        {
            _db.Entry(tank).Property(t => t.RowVersion).OriginalValue = request.RowVersion;
        }

        tank.CapacityKg = request.CapacityKg;
        tank.UpdatedAtUtc = _time.GetUtcNow().UtcDateTime;
        tank.UpdatedBy = userId;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException($"Tank '{tank.Code}' was modified by another user. Please reload and try again.");
        }

        return ToResponse(tank);
    }

    public async Task<TankResponse> ChangeStatusAsync(Guid id, ChangeTankStatusRequest request, string userId, CancellationToken cancellationToken)
    {
        var tank = await _db.Tanks.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (tank == null)
            throw new TankNotFoundException(id);

        if (!request.Status.HasValue)
            throw new ArgumentException("Status is required.", nameof(request.Status));

        if (request.RowVersion != null)
        {
            _db.Entry(tank).Property(t => t.RowVersion).OriginalValue = request.RowVersion;
        }

        // If deactivating and tank still holds milk -> block per essential logic
        if ((request.Status.Value == TankStatus.Inactive || request.Status.Value == TankStatus.UnderMaintenance) && tank.RemainingKg > 0)
        {
            throw new TankHasMilkException(tank.Code, tank.RemainingKg);
        }

        var oldStatus = tank.Status;
        tank.Status = request.Status.Value;
        tank.UpdatedAtUtc = _time.GetUtcNow().UtcDateTime;
        tank.UpdatedBy = userId;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException($"Tank '{tank.Code}' was modified by another user. Please reload and try again.");
        }

        // Metrics
        if (oldStatus == TankStatus.Active && tank.Status != TankStatus.Active)
            _metrics.TanksInUse.Add(-1, new System.Diagnostics.TagList { { "kind", tank.Kind.ToString() } });
        else if (oldStatus != TankStatus.Active && tank.Status == TankStatus.Active)
            _metrics.TanksInUse.Add(1, new System.Diagnostics.TagList { { "kind", tank.Kind.ToString() } });

        return ToResponse(tank);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var tank = await _db.Tanks.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (tank == null)
            throw new TankNotFoundException(id);

        // Check if tank has history - appears in processing_runs, tank_allocations, processing_stages
        var hasRuns = await _db.ProcessingRuns.AnyAsync(r => r.StoringTankId == id, cancellationToken);
        var hasSourceAlloc = await _db.TankAllocations.AnyAsync(a => a.SourceStoringTankId == id, cancellationToken);
        var hasDestAlloc = await _db.TankAllocations.AnyAsync(a => a.DestinationMixingTankId == id, cancellationToken);
        var hasStages = await _db.ProcessingStages.AnyAsync(s => s.MixingTankId == id, cancellationToken);

        if (hasRuns || hasSourceAlloc || hasDestAlloc || hasStages)
            throw new TankHasHistoryException(tank.Code);

        // Also block if still holds milk
        if (tank.RemainingKg > 0)
            throw new TankHasMilkException(tank.Code, tank.RemainingKg);

        _db.Tanks.Remove(tank);
        await _db.SaveChangesAsync(cancellationToken);

        _metrics.TanksInUse.Add(-1, new System.Diagnostics.TagList { { "kind", tank.Kind.ToString() } });
    }

    private static TankResponse ToResponse(Tank tank)
    {
        // No Name stored - UI generates "Storing tank 1" etc. per user clarification
        var name = GenerateName(tank.Code, tank.Kind);

        return new TankResponse
        {
            Id = tank.Id,
            Code = tank.Code,
            Name = name,
            Kind = tank.Kind,
            KindName = tank.Kind.ToString(),
            CapacityKg = tank.CapacityKg,
            RemainingKg = tank.RemainingKg,
            AvailableKg = tank.CapacityKg - tank.RemainingKg,
            Status = tank.Status,
            StatusName = tank.Status.ToString(),
            RowVersion = tank.RowVersion,
            CreatedAtUtc = tank.CreatedAtUtc,
            UpdatedAtUtc = tank.UpdatedAtUtc,
            CreatedBy = tank.CreatedBy
        };
    }

    private static string GenerateName(string code, TankKind kind)
    {
        // If code ST-01, generate "Storing tank 1", MT-02 -> "Mixing tank 2"
        // No Name stored per user
        try
        {
            var parts = code.Split('-');
            if (parts.Length == 2 && int.TryParse(parts[1], out var num))
            {
                var kindName = kind == TankKind.Storing ? "Storing" : "Mixing";
                return $"{kindName} tank {num}";
            }
        }
        catch { }
        return $"{kind} {code}";
    }
}
