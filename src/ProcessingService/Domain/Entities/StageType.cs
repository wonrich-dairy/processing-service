namespace ProcessingService.Domain.Entities;

/// <summary>
/// Processing stage type. Stored as varchar per SCRUM-57 AC. Single table per DOD 65.
/// </summary>
public enum StageType
{
    Heating = 0,
    Homogeniser = 1,
    Pasteuriser = 2,
    Cooling = 3
}
