using ProcessingService.Domain.Common;

namespace ProcessingService.Domain.Tanks;

/// <summary>What a tank is used for at the factory.</summary>
/// <remarks>
/// Numeric values are part of the stored contract and must not be renumbered; new kinds go on the
/// end.
/// </remarks>
public enum TankKind
{
    /// <summary>Receives a bowser's load as it is unloaded, and holds it (SCRUM-62).</summary>
    Storing = 0,

    /// <summary>Takes allocations from storing tanks, to be processed as one run (SCRUM-64).</summary>
    Mixing = 1
}

/// <summary>Whether a tank is in service.</summary>
public enum TankStatus
{
    Active = 0,

    /// <summary>Out of service. It keeps its history, but nothing new may go into it.</summary>
    UnderMaintenance = 1
}

/// <summary>
/// A tank at the factory (SCRUM-61). Storing tanks receive what a bowser brings; mixing tanks take
/// allocations from them and are what a processing run is worked from.
/// </summary>
/// <remarks>
/// Tanks are never deleted. A tank is named on every unload and allocation it has carried, so
/// removing the row would leave those records pointing at nothing; taking one out of service is
/// what retiring a tank means.
/// </remarks>
public class ProcessingTank
{
    public const int MaxCodeLength = 10;
    public const int MaxNameLength = 100;

    /// <summary>EF Core materialisation constructor.</summary>
    private ProcessingTank()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    public ProcessingTank(Guid id, string code, string name, TankKind kind, decimal capacityLitres)
    {
        Id = id;
        Code = Require(code, MaxCodeLength, "code").ToUpperInvariant();
        Name = Require(name, MaxNameLength, "name");
        Kind = kind;
        CapacityLitres = EnsureCapacity(capacityLitres);
    }

    public Guid Id { get; private set; }

    /// <summary>Short code as painted on the plant, e.g. "ST1".</summary>
    public string Code { get; private set; }

    public string Name { get; private set; }

    /// <summary>
    /// What the tank is for. Set once: an unload names a storing tank and an allocation names a
    /// mixing tank, so a tank that changed kind would make its own history unreadable.
    /// </summary>
    public TankKind Kind { get; private set; }

    /// <summary>Working volume of the tank, in litres.</summary>
    public decimal CapacityLitres { get; private set; }

    public TankStatus Status { get; private set; } = TankStatus.Active;

    /// <summary>Renames the tank and restates its working volume.</summary>
    public void Describe(string name, decimal capacityLitres)
    {
        Name = Require(name, MaxNameLength, "name");
        CapacityLitres = EnsureCapacity(capacityLitres);
    }

    /// <summary>
    /// Takes the tank out of service, or puts it back. A tank still holding milk cannot be taken
    /// out: what is in it would have nowhere to go.
    /// </summary>
    public void ChangeStatus(TankStatus status, decimal heldLitres)
    {
        if (status == TankStatus.UnderMaintenance && heldLitres > 0)
        {
            throw new DomainValidationException(
                $"Tank {Code} still holds {heldLitres:0.##} L. Empty it before taking it out of service.");
        }

        Status = status;
    }

    /// <summary>Refuses anything going into a tank that is out of service.</summary>
    public void EnsureAvailable()
    {
        if (Status != TankStatus.Active)
        {
            throw new DomainValidationException(
                $"Tank {Code} is out of service and cannot receive milk.");
        }
    }

    /// <summary>Refuses a tank being used for something it is not.</summary>
    public void EnsureKind(TankKind expected)
    {
        if (Kind != expected)
        {
            throw new DomainValidationException(
                $"Tank {Code} is a {Kind.ToString().ToLowerInvariant()} tank, "
                + $"and this needs a {expected.ToString().ToLowerInvariant()} tank.");
        }
    }

    private static string Require(string value, int maxLength, string field)
    {
        var trimmed = value?.Trim() ?? string.Empty;

        if (trimmed.Length == 0)
        {
            throw new DomainValidationException($"A tank {field} is required.");
        }

        return trimmed.Length > maxLength
            ? throw new DomainValidationException($"A tank {field} cannot exceed {maxLength} characters.")
            : trimmed;
    }

    private static decimal EnsureCapacity(decimal capacityLitres) =>
        capacityLitres <= 0
            ? throw new DomainValidationException("A tank's capacity must be greater than zero.")
            : capacityLitres;
}
