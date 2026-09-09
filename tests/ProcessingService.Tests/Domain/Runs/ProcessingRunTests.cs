using ProcessingService.Domain.Common;
using ProcessingService.Domain.Runs;
using ProcessingService.Domain.Tanks;
using ProcessingService.Domain.Unloads;

namespace ProcessingService.Tests.Domain.Runs;

/// <summary>
/// The run lifecycle state machine (its valid and invalid transitions), tested on the entity
/// directly rather than through the HTTP layer. There is no existing in-isolation example in the
/// suite, so these follow the same conventions - xUnit facts/theories and the shared
/// <see cref="DomainValidationException"/> type - without the API scaffolding.
/// </summary>
public class ProcessingRunTests
{
    private static readonly DateTimeOffset StartOfDay =
        new(2026, 9, 9, 6, 0, 0, TimeSpan.Zero);

    /// <summary>A fixed later instant, so transition timestamps are deterministic and increasing.</summary>
    private static DateTimeOffset At(int minute) => StartOfDay.AddMinutes(minute);

    private static ProcessingRun NewRun() => NewRun(StartOfDay);

    private static ProcessingRun NewRun(DateTimeOffset recordedAtUtc)
    {
        var tank = new ProcessingTank(
            Guid.NewGuid(),
            "ST1",
            "Storing Tank One",
            TankKind.Storing,
            20000m);

        var unload = Unload.Record(
            Guid.NewGuid(),
            "UNL-20260909-01",
            "MCC-20260909-001",
            tank,
            8000m,
            4.2m,
            "factory-intake",
            recordedAtUtc.LocalDateTime,
            recordedAtUtc,
            heldLitres: 0m);

        return ProcessingRun.Start(Guid.NewGuid(), unload, recordedAtUtc);
    }

    /// <summary>Returns a run already sitting in the requested state.</summary>
    private static ProcessingRun ARunIn(RunState state)
    {
        var run = NewRun();

        switch (state)
        {
            case RunState.AwaitingLabResult:
                return run;
            case RunState.OnHold:
                run.PutOnHold(At(1));
                return run;
            case RunState.ReleasedForAllocation:
                run.ReleaseForAllocation(At(1));
                return run;
            case RunState.Rejected:
                run.Reject(At(1));
                return run;
            case RunState.Completed:
                run.ReleaseForAllocation(At(1));
                run.Complete(At(2));
                return run;
            default:
                throw new ArgumentOutOfRangeException(nameof(state), state, null);
        }
    }

    // ---- Creation ----------------------------------------------------------

    [Fact]
    public void A_run_is_born_awaiting_its_lab_result_and_stamps_that_instant()
    {
        var run = NewRun();

        Assert.Equal(RunState.AwaitingLabResult, run.State);
        Assert.Equal(StartOfDay.UtcDateTime, run.StateChangedAtUtc);
    }

    [Fact]
    public void A_run_carries_a_reference_back_to_the_unload_it_came_from()
    {
        var tank = new ProcessingTank(
            Guid.NewGuid(), "ST1", "Storing Tank One", TankKind.Storing, 20000m);

        var unload = Unload.Record(
            Guid.NewGuid(),
            "UNL-20260909-01",
            "MCC-20260909-001",
            tank,
            8000m,
            4.2m,
            "factory-intake",
            StartOfDay.LocalDateTime,
            StartOfDay,
            heldLitres: 0m);

        var run = ProcessingRun.Start(Guid.NewGuid(), unload, StartOfDay);

        Assert.Equal(unload.Id, run.UnloadId);
        Assert.Same(unload, run.Unload);
    }

    // ---- ReleaseForAllocation ---------------------------------------------

    [Fact]
    public void A_run_is_released_from_awaiting_its_lab_result()
    {
        var run = ARunIn(RunState.AwaitingLabResult);

        run.ReleaseForAllocation(At(1));

        Assert.Equal(RunState.ReleasedForAllocation, run.State);
        Assert.Equal(At(1).UtcDateTime, run.StateChangedAtUtc);
    }

    [Fact]
    public void A_run_on_hold_is_released()
    {
        var run = ARunIn(RunState.OnHold);

        run.ReleaseForAllocation(At(2));

        Assert.Equal(RunState.ReleasedForAllocation, run.State);
        Assert.Equal(At(2).UtcDateTime, run.StateChangedAtUtc);
    }

    [Theory]
    [InlineData(RunState.ReleasedForAllocation)]
    [InlineData(RunState.Rejected)]
    [InlineData(RunState.Completed)]
    public void A_run_that_is_not_awaiting_or_on_hold_cannot_be_released(RunState from)
    {
        var run = ARunIn(from);

        var ex = Assert.Throws<DomainValidationException>(() => run.ReleaseForAllocation(At(5)));

        Assert.Contains(from.ToString(), ex.Message);
        Assert.Contains("released", ex.Message);
        Assert.Equal(from, run.State);
    }

    // ---- PutOnHold ---------------------------------------------------------

    [Fact]
    public void A_run_awaiting_its_result_is_put_on_hold()
    {
        var run = ARunIn(RunState.AwaitingLabResult);

        run.PutOnHold(At(1));

        Assert.Equal(RunState.OnHold, run.State);
        Assert.Equal(At(1).UtcDateTime, run.StateChangedAtUtc);
    }

    [Theory]
    [InlineData(RunState.OnHold)]
    [InlineData(RunState.ReleasedForAllocation)]
    [InlineData(RunState.Rejected)]
    [InlineData(RunState.Completed)]
    public void Only_a_run_still_awaiting_its_result_can_be_put_on_hold(RunState from)
    {
        var run = ARunIn(from);

        var ex = Assert.Throws<DomainValidationException>(() => run.PutOnHold(At(5)));

        Assert.Contains(from.ToString(), ex.Message);
        Assert.Contains("hold", ex.Message);
    }

    // ---- Reject ------------------------------------------------------------

    [Fact]
    public void A_run_awaiting_its_result_is_rejected()
    {
        var run = ARunIn(RunState.AwaitingLabResult);

        run.Reject(At(1));

        Assert.Equal(RunState.Rejected, run.State);
        Assert.Equal(At(1).UtcDateTime, run.StateChangedAtUtc);
    }

    [Fact]
    public void A_run_on_hold_is_rejected()
    {
        var run = ARunIn(RunState.OnHold);

        run.Reject(At(2));

        Assert.Equal(RunState.Rejected, run.State);
        Assert.Equal(At(2).UtcDateTime, run.StateChangedAtUtc);
    }

    [Theory]
    [InlineData(RunState.ReleasedForAllocation)]
    [InlineData(RunState.Rejected)]
    [InlineData(RunState.Completed)]
    public void A_released_or_finished_run_cannot_be_rejected(RunState from)
    {
        var run = ARunIn(from);

        var ex = Assert.Throws<DomainValidationException>(() => run.Reject(At(5)));

        Assert.Contains(from.ToString(), ex.Message);
        Assert.Contains("rejected", ex.Message);
    }

    // ---- Complete ----------------------------------------------------------

    [Fact]
    public void A_released_run_is_completed()
    {
        var run = ARunIn(RunState.ReleasedForAllocation);

        run.Complete(At(5));

        Assert.Equal(RunState.Completed, run.State);
        Assert.Equal(At(5).UtcDateTime, run.StateChangedAtUtc);
    }

    [Theory]
    [InlineData(RunState.AwaitingLabResult)]
    [InlineData(RunState.OnHold)]
    [InlineData(RunState.Rejected)]
    [InlineData(RunState.Completed)]
    public void Only_a_released_run_can_be_completed(RunState from)
    {
        var run = ARunIn(from);

        var ex = Assert.Throws<DomainValidationException>(() => run.Complete(At(5)));

        Assert.Contains(from.ToString(), ex.Message);
        Assert.Contains("completed", ex.Message);
    }

    // ---- Terminal states are terminal -------------------------------------

    [Fact]
    public void A_rejected_run_can_never_move_again()
    {
        var run = ARunIn(RunState.Rejected);

        Assert.Throws<DomainValidationException>(() => run.ReleaseForAllocation(At(5)));
        Assert.Throws<DomainValidationException>(() => run.PutOnHold(At(5)));
        Assert.Throws<DomainValidationException>(() => run.Reject(At(5)));
        Assert.Throws<DomainValidationException>(() => run.Complete(At(5)));

        Assert.Equal(RunState.Rejected, run.State);
    }

    [Fact]
    public void A_completed_run_can_never_move_again()
    {
        var run = ARunIn(RunState.Completed);

        Assert.Throws<DomainValidationException>(() => run.ReleaseForAllocation(At(5)));
        Assert.Throws<DomainValidationException>(() => run.PutOnHold(At(5)));
        Assert.Throws<DomainValidationException>(() => run.Reject(At(5)));
        Assert.Throws<DomainValidationException>(() => run.Complete(At(5)));

        Assert.Equal(RunState.Completed, run.State);
    }
}