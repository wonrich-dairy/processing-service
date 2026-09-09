using ProcessingService.Domain.Common;
using ProcessingService.Domain.Unloads;

namespace ProcessingService.Domain.Runs;

/// <summary>
/// One batch of milk moving through the factory after it has been unloaded into a storing tank.
/// </summary>
/// <remarks>
/// <para>
/// A run is born the moment an unload is recorded and begins its life awaiting the factory's own
/// lab panel. It is the object that the lab result, the allocation into a mixing tank, and
/// eventually the heating and pasteurising stages all attach to - so tracing a finished product
/// back to the raw milk it came from is tracing its run back to the unload that created it.
/// </para>
/// <para>
/// Its lifecycle is a deliberately strict state machine. Rejection and completion are terminal:
/// a rejected run can never be resurrected into a Released one, and a completed run cannot be
/// pulled back into the working pipeline, because allowing either would make a run's story
/// ambiguous. Every state change records the instant it happened so that history can be
/// reconstructed.
/// </para>
/// </remarks>
public class ProcessingRun
{
    /// <summary>EF Core materialisation constructor.</summary>
    private ProcessingRun()
    {
    }

    private ProcessingRun(Guid id, Unload unload, DateTimeOffset recordedAtUtc)
    {
        Id = id;
        UnloadId = unload.Id;
        Unload = unload;
        State = RunState.AwaitingLabResult;
        StateChangedAtUtc = recordedAtUtc.UtcDateTime;
    }

    public Guid Id { get; private set; }

    /// <summary>The unload this run was created from.</summary>
    public Guid UnloadId { get; private set; }

    public Unload? Unload { get; private set; }

    /// <summary>Where the run is in its lifecycle.</summary>
    public RunState State { get; private set; }

    /// <summary>
    /// The instant <see cref="State"/> last changed, creation included. Feeding the traceability
    /// story: it tells you when the milk moved from one phase of its life to the next.
    /// </summary>
    public DateTime StateChangedAtUtc { get; private set; }

    /// <summary>
    /// Creates a run for a just-recorded unload. Runs always begin <see cref="RunState.AwaitingLabResult"/>,
    /// because the factory's independent lab panel is what earns the milk the right to move on.
    /// </summary>
    /// <param name="id">Identity for the run.</param>
    /// <param name="unload">The unload that brought the milk into the factory.</param>
    /// <param name="recordedAtUtc">Instant the run was created.</param>
    public static ProcessingRun Start(Guid id, Unload unload, DateTimeOffset recordedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(unload);

        return new ProcessingRun(id, unload, recordedAtUtc);
    }

    /// <summary>
    /// Clears the run to be allocated into a mixing tank.
    /// </summary>
    /// <remarks>
    /// Valid while the run is awaiting its lab result or parked on hold. Once released the milk is
    /// no longer ours to shelve or condemn in the same way - so a run that has already moved past
    /// this point cannot be released again.
    /// </remarks>
    public void ReleaseForAllocation(DateTimeOffset atUtc)
    {
        if (State != RunState.AwaitingLabResult && State != RunState.OnHold)
        {
            throw new DomainValidationException(
                $"A run in the {State} state cannot be released for allocation; "
                + "only runs awaiting their lab result or on hold may be released.");
        }

        MoveTo(RunState.ReleasedForAllocation, atUtc);
    }

    /// <summary>
    /// Puts the run on hold because its lab result did not clear it. Nothing is silently discarded;
    /// an explicit decision is required before the milk goes anywhere.
    /// </summary>
    /// <remarks>
    /// Valid only while the result is still outstanding. A run already parked on hold cannot be put
    /// on hold again, and once a decision has been made the milk is past the point where holding
    /// means anything.
    /// </remarks>
    public void PutOnHold(DateTimeOffset atUtc)
    {
        if (State != RunState.AwaitingLabResult)
        {
            throw new DomainValidationException(
                $"A run in the {State} state cannot be put on hold; "
                + "only a run still awaiting its lab result may be put on hold.");
        }

        MoveTo(RunState.OnHold, atUtc);
    }

    /// <summary>
    /// Formally rejects the run. Terminal.
    /// </summary>
    /// <remarks>
    /// Valid while the run is awaiting its result or on hold - those are the two states in which the
    /// decision to condemn still belongs to the people holding it. Once the milk has been released
    /// or has already run to completion, rejection is no longer a move anyone is entitled to make.
    /// </remarks>
    public void Reject(DateTimeOffset atUtc)
    {
        if (State != RunState.AwaitingLabResult && State != RunState.OnHold)
        {
            throw new DomainValidationException(
                $"A run in the {State} state cannot be rejected; "
                + "only runs awaiting their lab result or on hold may be rejected.");
        }

        MoveTo(RunState.Rejected, atUtc);
    }

    /// <summary>
    /// Marks the run as processed through to a finished, cooled product. Terminal.
    /// </summary>
    /// <remarks>
    /// Valid only from <see cref="RunState.ReleasedForAllocation"/>, because a run must have been
    /// cleared and drawn into processing before it can possibly be complete. A run still awaiting
    /// its result, parked on hold, or already finished cannot be completed again.
    /// </remarks>
    public void Complete(DateTimeOffset atUtc)
    {
        if (State != RunState.ReleasedForAllocation)
        {
            throw new DomainValidationException(
                $"A run in the {State} state cannot be completed; "
                + "only a released run may be completed.");
        }

        MoveTo(RunState.Completed, atUtc);
    }

    private void MoveTo(RunState next, DateTimeOffset atUtc)
    {
        State = next;
        StateChangedAtUtc = atUtc.UtcDateTime;
    }
}