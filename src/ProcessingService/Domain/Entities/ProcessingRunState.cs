namespace ProcessingService.Domain.Entities;

/// <summary>
/// Processing run state machine. Stored as varchar per SCRUM-57 AC.
/// Real process: AwaitingLabResult -> ReleasedForAllocation or OnHold -> Completed/Rejected
/// </summary>
public enum ProcessingRunState
{
    AwaitingLabResult = 0,
    ReleasedForAllocation = 1,
    OnHold = 2,
    Rejected = 3,
    Completed = 4,
    // Additional states for future
    Allocated = 5,
    Heating = 6,
    Pasteurising = 7,
    Cooling = 8
}
