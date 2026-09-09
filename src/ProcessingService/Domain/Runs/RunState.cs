namespace ProcessingService.Domain.Runs;

/// <summary>
/// Where a processing run is in its lifecycle.
/// </summary>
/// <remarks>
/// Numeric values are part of the stored contract and must not be renumbered; new states go on the
/// end. A run is born in <see cref="AwaitingLabResult"/> and can reach at most one final outcome,
/// because the service's whole purpose is traceability - if a run's story were ever ambiguous we
/// could no longer say which raw milk a finished product came from.
/// </remarks>
public enum RunState
{
    /// <summary>
    /// The run exists, but the factory's own independent lab panel has not yet cleared it. This is
    /// every run's starting state: the load sits in its storing tank and nothing may move it on.
    /// </summary>
    AwaitingLabResult = 0,

    /// <summary>
    /// The lab result did not clear the milk, so it is parked pending an explicit human decision.
    /// Nothing is discarded silently - a run only leaves this state when someone either releases
    /// it (because a retest passed) or rejects it outright.
    /// </summary>
    OnHold = 1,

    /// <summary>
    /// The milk is cleared and may be drawn out of its storing tank into a mixing tank. This is the
    /// state that the routing decision (fresh/flavoured versus yogurt) is made from.
    /// </summary>
    ReleasedForAllocation = 2,

    /// <summary>
    /// Final. The run was formally rejected and can never be processed. Kept terminal so a load
    /// that was condemned can never quietly become a Released one.
    /// </summary>
    Rejected = 3,

    /// <summary>
    /// Final. The run was processed through to a finished, cooled product. Kept terminal so a
    /// completed run cannot be pulled back into the working pipeline.
    /// </summary>
    Completed = 4
}