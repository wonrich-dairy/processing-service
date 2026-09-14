namespace ProcessingService.Domain.Entities;

/// <summary>
/// Tank status. Default Active on creation. Stored as varchar per SCRUM-57 AC.
/// AC 61 says Active/Inactive, real process says Active/UnderMaintenance - support both Inactive and UnderMaintenance.
/// </summary>
public enum TankStatus
{
    Active = 0,
    Inactive = 1,
    UnderMaintenance = 2
}
