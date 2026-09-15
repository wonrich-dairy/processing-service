namespace ProcessingService.Domain.Entities;

/// <summary>
/// Quality test status for mock and real quality service.
/// Pending = just unloaded, not started
/// InProgress = quality tech started testing
/// Passed = all tests met quality (Accept)
/// Failed = failed quality (Reject)
/// Maps to ProcessingRunState: Pending/InProgress -> AwaitingLabResult, Passed -> ReleasedForAllocation, Failed -> OnHold
/// </summary>
public enum QualityTestStatus
{
    Pending = 0,
    InProgress = 1,
    Passed = 2,
    Failed = 3
}
