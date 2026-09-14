namespace ProcessingService.Domain.Entities;

/// <summary>
/// Tank kind - storing vs mixing. Stored as varchar per SCRUM-57 AC (not native ENUM).
/// </summary>
public enum TankKind
{
    Storing = 0,
    Mixing = 1
}
