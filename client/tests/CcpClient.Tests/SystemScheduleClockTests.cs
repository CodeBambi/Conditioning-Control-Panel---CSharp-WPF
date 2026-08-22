using CcpClient.Desktop.Scheduling;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The REAL clock the scheduler runs on in the product, and the one thing in this packet
/// that was compiled and never executed.
///
/// <para><b>Why this file exists, and it is the same finding twice.</b> An earlier packet wrote
/// <see cref="SystemSessionClockTests"/> because every session fact substituted a manual clock, so
/// the one implementation that ships was never run — and <b>the first version of that fact killed
/// the whole test host</b>. This one reproduced the condition exactly: every scheduler fact injects
/// an <see cref="IScheduleClock"/>, so <see cref="SystemScheduleClock"/> — the default on every
/// product path (<c>SchedulerParticipant</c>'s constructor) — was covered only by two reads of
/// <c>LocalNow.Kind</c>. The review caught it; this file closes it.</para>
///
/// <para><b>And the stake here is higher than it was for the session clock.</b> This callback runs
/// while NOTHING else is happening, in an app the user has deliberately left running, and its work
/// is <c>Tick()</c> → <c>SessionEngine.Start()</c> → <c>Arm()</c> across the whole rack. An
/// exception escaping it is an UNHANDLED exception on a pool thread with no caller above it, so
/// .NET ends the process — which to a user looks like the app simply vanishing overnight, with no
/// diagnostic and nothing to report.</para>
///
/// <para><b>No wall-clock wait anywhere — with ONE deliberate exception, named.</b> Every
/// fact waits on a DETERMINISTIC SIGNAL through the approved helper: no <c>Thread.Sleep</c>, no bare
/// <c>Task.Delay</c>, no clock poll. The negative observation — that a cancelled schedule does not
/// fire — is proved with an ordering BARRIER rather than by waiting.
/// <see cref="DisposingTheHandleBeforeItIsDue_SuppressesTheCallback"/> is the exception and it is
/// deliberate: <b>its subject IS a due time arriving</b>, so it schedules at a short delay and waits
/// for the resulting signals. That is not a tolerance bought to make it pass — until it was corrected the
/// doomed schedule there was due in TEN MINUTES, which made its assertion incapable of failing, and
/// observing the delay is exactly what gives it teeth. Nothing here asserts how LONG anything took.
/// Structured on <see cref="SystemSessionClockTests"/>, deliberately: the two clocks are the same
/// shape and their facts should be readable side by side.</para>
/// </summary>
public class SystemScheduleClockTests
{
    /// <summary>
    /// The doomed schedule's due time — the one delay in this file whose ELAPSING is the
    /// subject rather than an incidental wait. Short enough to cost the suite a second, long enough
    /// that a stall between two adjacent statements would have to run into whole seconds; and if it
    /// ever did, the fact detects that and says so rather than blaming the product.
    /// </summary>
    private static readonly TimeSpan DoomedDue = TimeSpan.FromMilliseconds(1000);

    /// <summary>The ordering barrier's due time: a whole <see cref="DoomedDue"/> LATER, so when it
    /// fires the doomed schedule's own moment is not merely past, it is past by a margin in which
    /// the pool demonstrably ran other work.</summary>
    private static readonly TimeSpan BarrierDue = TimeSpan.FromMilliseconds(2000);

    [Fact]
    public async Task ACallbackThatThrows_IsContainedAndREPORTED_RatherThanKillingTheProcess()
    {
        // THE fact this file was written for. The path is reachable in production and it is not a
        // corner: SchedulerParticipant.OnDue runs Scheduler.Tick(), which calls
        // SessionEngine.Start(), which calls Arm() on every module in the rack — all of it inside
        // this callback, on a pool thread, thirty seconds at a time, for as long as the app is open.
        var faults = new List<Exception>();
        var reported = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clock = new SystemScheduleClock(ex =>
        {
            lock (faults)
            {
                faults.Add(ex);
            }

            reported.TrySetResult();
        });

        using var first = clock.Schedule(TimeSpan.Zero, () =>
            throw new InvalidOperationException("the scheduler's tick faulted"));

        await TestWait.Until(reported.Task, "the faulting scheduled callback's exception to be reported");

        // Contained AND reported: a silent catch would be the worse half of the same defect,
        // because the scheduler would simply stop starting sessions and nothing would say why.
        var fault = Assert.Single(faults);
        Assert.IsType<InvalidOperationException>(fault);
        Assert.Equal("the scheduler's tick faulted", fault.Message);

        // ...and the clock is still a clock afterwards, which is what makes the containment worth
        // having: the NEXT tick still comes.
        var later = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var second = clock.Schedule(TimeSpan.Zero, () => later.TrySetResult());
        await TestWait.Until(later.Task, "a later schedule to run after an earlier callback faulted");
        Assert.True(later.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task ACallbackThatThrowsWithNoReporter_IsStillContained()
    {
        // The constructor's parameter is optional and its doc says an unreported fault "is only
        // correct for a caller that has no log" (ScheduleClock.cs:65-67). So a null reporter is a
        // real configuration and this exercises it: the invoke inside the catch is null-conditional,
        // and the clock keeps servicing work afterwards.
        //
        // HONEST LIMIT, MEASURED ON THIS CLASS RATHER THAN ARGUED FROM ITS SIBLING. This
        // fact does NOT redden if the containment is removed. With `new Timer(_ => fire(), ...)`
        // restored at ScheduleClock.cs:78 it still reports `Failed: 0, Passed: 1`, because its only
        // assertion is that a SECOND, UNRELATED schedule ran — which is true whether or not the
        // first throw was contained. The escaping exception surfaces out-of-band as the runner's
        // `[FATAL ERROR] System.InvalidOperationException` / `Catastrophic failure: ... no reporter`
        // lines, which do not fail this fact. The comment here used to claim this fact "says the
        // CONTAINMENT does not depend on the REPORTING"; it does not say that, and the claim is
        // struck.
        //
        // The mechanism IS pinned, by the sibling above: with the same containment removed,
        // ACallbackThatThrows_IsContainedAndREPORTED_RatherThanKillingTheProcess reports
        // `Failed: 1, Passed: 0` on TIMING-VERDICT:CONDITION-NEVER-TRUE. What THIS fact pins is the
        // null-reporter CONFIGURATION, and that is all it should be read as claiming.
        var clock = new SystemScheduleClock();
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var doomed = clock.Schedule(
            TimeSpan.Zero, () => throw new InvalidOperationException("no reporter"));
        using var alive = clock.Schedule(TimeSpan.Zero, () => barrier.TrySetResult());

        await TestWait.Until(
            barrier.Task, "the real schedule clock to keep working after an unreported fault");
        Assert.True(barrier.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task AScheduleDueImmediately_ReallyFires_OnTheRealTimer()
    {
        var clock = new SystemScheduleClock();
        var fired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var handle = clock.Schedule(TimeSpan.Zero, () => fired.TrySetResult());

        await TestWait.Until(fired.Task, "the real schedule clock's zero-delay callback to run");
        Assert.True(fired.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task ANegativeDelay_IsClampedToImmediate_RatherThanThrowing()
    {
        // SystemScheduleClock's Math.Max(0, ...): the interface promises "due <= 0 fires as soon as
        // possible", and System.Threading.Timer throws ArgumentOutOfRangeException on a negative
        // dueTime. The scheduler cannot produce a negative interval from its own two constants
        // today — but Schedule is a public seam carrying that written promise, and the throw would
        // land on the CALLER's thread inside Arm, which runs in phase 3 startup.
        var clock = new SystemScheduleClock();
        var fired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var handle = clock.Schedule(TimeSpan.FromSeconds(-30), () => fired.TrySetResult());

        await TestWait.Until(fired.Task, "the real schedule clock's negative-delay callback to run");
        Assert.True(fired.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task DisposingTheHandleBeforeItIsDue_SuppressesTheCallback()
    {
        // The property every stop in this module rests on — SchedulerParticipant disposes the
        // pending one-shot in StopAsync and again on every re-arm.
        //
        // WHY THIS FACT LOOKS LIKE THIS NOW, AND WHY IT DID NOT BITE BEFORE. The doomed
        // schedule used to be due in TEN MINUTES, so `cancelledFired` was false whether or not
        // Dispose suppressed anything: the assertion was trivially true and could not fail. Making
        // it bite needs the doomed schedule's own moment to ARRIVE inside the fact, and then three
        // properties, none of them a margin bought by hoping:
        //
        //   System.Threading.Timer fixes its deadline at CONSTRUCTION, so arming order is deadline
        //   order: control < doomed < barrier.
        //
        //   1. IT WAS DISPOSED BEFORE IT WAS DUE. `control` is armed BEFORE `doomed` at the same
        //      delay, so its deadline is earlier. If control has not fired when Dispose returns,
        //      then less than DoomedDue has passed since control was armed, hence less than
        //      DoomedDue since doomed was armed. That is a deduction, not a probability. If it ever
        //      trips, this machine stalled a full second between two adjacent statements, and the
        //      message says so instead of blaming the product.
        //   2. ITS MOMENT HAS PASSED, WITH ROOM TO SPARE. control firing proves the timer queue ran
        //      the pass at DoomedDue; the barrier is due a whole DoomedDue LATER, so by the time it
        //      signals, a suppressed-in-name-only callback has had that long to run on a pool the
        //      queue was demonstrably servicing.
        //   3. THE DELAY IS OBSERVABLE AT ALL. control firing is the POSITIVE CONTROL: push
        //      DoomedDue back out to ten minutes and this leg times out. The vacuity that made the
        //      old shape unfailable is now itself a failing assertion.
        //
        // HONEST LIMIT: step 2 is an ordering-plus-settle argument, not a happens-before edge — a
        // pool starved for a whole second while still servicing two later timer callbacks could
        // mask a fired callback and let this read green. That is the same class of argument this
        // file already made, and it is named rather than hidden.
        //
        // TaskCompletionSource rather than the old `bool cancelledFired`: that bool was written on a
        // pool thread and read on the test thread with nothing ordering the two.
        var clock = new SystemScheduleClock();
        var control = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var doomedFired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var proof = clock.Schedule(DoomedDue, () => control.TrySetResult());
        var doomed = clock.Schedule(DoomedDue, () => doomedFired.TrySetResult());
        doomed.Dispose();

        Assert.False(control.Task.IsCompleted,
            "a schedule armed BEFORE the doomed one, at the same delay, had already fired by the time "
            + "Dispose returned — this machine stalled longer than the whole due time between two "
            + "adjacent statements, so the suppression below is not observable on this run. Treat as "
            + "an ENVIRONMENT failure, not a product one");

        using var handle = clock.Schedule(BarrierDue, () => barrier.TrySetResult());
        await TestWait.Until(control.Task,
            "a schedule armed before the cancelled one to fire, which is what proves this fact observes its own due time");
        await TestWait.Until(
            barrier.Task, "a schedule armed after the cancellation to run on the real clock");

        Assert.True(control.Task.IsCompletedSuccessfully,
            "the positive control never completed successfully — this fact cannot observe a suppression it never gave a chance to happen");
        Assert.False(doomedFired.Task.IsCompleted,
            "a scheduled tick whose handle was disposed still fired — a scheduler that can outlive "
            + "its own stop is the one thing this module must never be");
    }

    [Fact]
    public void LocalNow_IsLOCAL_AndMovesForward()
    {
        // Kind rather than value, and deliberately: on a machine whose timezone IS UTC the local
        // and universal readings are equal, so comparing them would pass under a UTC swap exactly
        // where the port is hardest to check. This is the property that made a NEW seam necessary
        // instead of widening ISessionClock, so it is pinned on the class that really ships.
        var clock = new SystemScheduleClock();

        var first = clock.LocalNow;
        var second = clock.LocalNow;

        Assert.Equal(DateTimeKind.Local, first.Kind);
        Assert.Equal(DateTimeKind.Local, second.Kind);
        Assert.True(second >= first, $"the real clock went backwards: {first:O} then {second:O}");
    }
}
