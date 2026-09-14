using ProcessingService.Api.Models.Tanks;
using ProcessingService.Domain.Entities;

namespace ProcessingService.Application.Tanks;

public interface ITankService
{
    Task<IReadOnlyList<TankResponse>> ListAsync(TankKind? kind, TankStatus? status, bool activeOnly, CancellationToken cancellationToken);
    Task<TankResponse?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<TankResponse> CreateAsync(CreateTankRequest request, string userId, CancellationToken cancellationToken);
    Task<TankResponse> UpdateAsync(Guid id, UpdateTankRequest request, string userId, CancellationToken cancellationToken);
    Task<TankResponse> ChangeStatusAsync(Guid id, ChangeTankStatusRequest request, string userId, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}

public sealed class DuplicateTankCodeException : Exception
{
    public DuplicateTankCodeException(string code) : base($"Tank number '{code}' is already in use.") { }
}

public sealed class TankHasHistoryException : Exception
{
    public TankHasHistoryException(string code) : base($"Tank '{code}' has processing history and can only be deactivated, not deleted.") { }
}

public sealed class TankHasMilkException : Exception
{
    public TankHasMilkException(string code, decimal remaining) : base($"Tank '{code}' still holds {remaining} KG and cannot be deactivated. Empty it first.") { }
}

public sealed class TankNotFoundException : Exception
{
    public TankNotFoundException(Guid id) : base($"Tank '{id}' not found.") { }
}
