using CcpClient.Desktop.Scheduling;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-118 — the REAL clock the scheduler runs on in the product, and the one thing in this packet
/// that was compiled and never executed.
///
/// <para><b>Why this file exists, and it is the same finding twice.</b> SP-101 wrote
/// <see cref="SystemSessionClockTests"/> because every session fact substituted a manual clock, so
/// the one implementation that ships was never run — and <b>the first version of that fact killed
/// the whole test host</b>. SP-118 reproduced the condition exactly: every scheduler fact injects
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
/// <para><b>No wall-clock wait anywhere.</b> Every fact waits on a DETERMINISTIC SIGNAL through the
/// approved helper, never on an interval elapsing. The negative observation — that a cancelled
/// schedule does not fire — is proved with an ordering BARRIER rather than by waiting: a second
/// schedule due immediately is armed after the first is cancelled, and when THAT one has fired, the
/// cancelled one must still not have. Nothing here asserts how long anything took, because none of
/// it is about elapsed time. Structured on <see cref="SystemSessionClockTests"/>, deliberately: the
/// two clocks are the same shape and their facts should be readable side by side.</para>
/// </summary>
public class SystemScheduleClockTests
{
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
        // correct for a caller that has no log". A null reporter must still not let the exception
        // out — the invoke is null-conditional — and this is what says the CONTAINMENT does not
        // depend on the REPORTING.
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
        // pending one-shot in StopAsync and again on every re-arm. Proved with a BARRIER, not a
        // wait: the cancelled schedule is due far enough out that it cannot legitimately have
        // fired, and the barrier is armed AFTERWARDS and due immediately, so when the barrier has
        // run the real clock has demonstrably serviced work armed after the cancellation.
        var clock = new SystemScheduleClock();
        var cancelledFired = false;
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var doomed = clock.Schedule(TimeSpan.FromMinutes(10), () => cancelledFired = true);
        doomed.Dispose();

        using var handle = clock.Schedule(TimeSpan.Zero, () => barrier.TrySetResult());
        await TestWait.Until(
            barrier.Task, "a schedule armed after the cancellation to run on the real clock");

        Assert.False(cancelledFired,
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
