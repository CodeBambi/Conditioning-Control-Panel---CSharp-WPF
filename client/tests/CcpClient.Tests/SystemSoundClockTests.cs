using CcpClient.Desktop.Audio;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-123 — the REAL clock the whole audio stack paces on, and the THIRD instance of a shape that
/// has already taken something down twice.
///
/// <para><b>Why this file exists, and it is the same finding for the third time.</b> SP-101 wrote
/// <see cref="SystemSessionClockTests"/> because every session fact substituted a manual clock, and
/// <b>the first version of that fact killed the whole test host</b>. SP-118 reproduced it exactly
/// with <c>SystemScheduleClock</c>. Both were found when somebody happened to write the first test.
/// This one was found by a MACHINE — the SP-121 execution census reported
/// <see cref="SystemSoundClock"/> as a shipped type with zero executed lines, and reading it showed
/// <c>new Timer(_ => fire(), ...)</c> with the callback bare while both siblings route through a
/// private <c>Run</c> with a try/catch and say why in their own words.</para>
///
/// <para><b>The stake.</b> This clock carries <c>SoundArbitration</c>'s device recovery probe
/// (<c>SoundArbitration.cs:780</c>), the per-item pacing fire (<c>:903</c>), the five-minute duck
/// watchdog (<c>:986</c>) and the DTRH segment-cap timer (<c>DtrhNativeEffects.cs:338</c>). Each
/// runs on a pool thread with no caller above it, so an exception escaping one is an UNHANDLED
/// exception and .NET ends the process — while the user is watching a video or listening to
/// something, with no diagnostic and nothing to report.</para>
///
/// <para><b>No wall-clock wait anywhere.</b> Every positive fact waits on a DETERMINISTIC SIGNAL
/// through the approved helper, never on an interval elapsing. The negative observation — that a
/// cancelled schedule does not fire — is proved with an ordering BARRIER: a second schedule due
/// immediately is armed AFTER the first is cancelled, and when that one has run, the cancelled one
/// must still not have. Nothing here asserts how long anything took. Structured on
/// <see cref="SystemSessionClockTests"/> and <see cref="SystemScheduleClockTests"/> deliberately:
/// the three clocks are one shape and their facts should read side by side.</para>
/// </summary>
public class SystemSoundClockTests
{
    [Fact]
    public async Task ACallbackThatThrows_IsContainedAndREPORTED_RatherThanKillingTheProcess()
    {
        // THE fact this file was written for. Without the containment this does not FAIL — it takes
        // the test host down, which is precisely what happened to SP-101 and precisely what would
        // happen to a user's session.
        var faults = new List<Exception>();
        var reported = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clock = new SystemSoundClock(ex =>
        {
            lock (faults)
            {
                faults.Add(ex);
            }

            reported.TrySetResult();
        });

        using var first = clock.Schedule(TimeSpan.Zero, () =>
            throw new InvalidOperationException("a pacing callback faulted"));

        await TestWait.Until(reported.Task, "the faulting sound-clock callback's exception to be reported");

        // Contained AND reported: a silent catch would be the worse half of the same defect —
        // arbitration would simply stop pacing and nothing would say why.
        var fault = Assert.Single(faults);
        Assert.IsType<InvalidOperationException>(fault);
        Assert.Equal("a pacing callback faulted", fault.Message);

        // ...and it is still a clock afterwards, which is what makes containment worth having:
        // the NEXT pacing fire still comes.
        var later = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var second = clock.Schedule(TimeSpan.Zero, () => later.TrySetResult());
        await TestWait.Until(later.Task, "a later schedule to run after an earlier callback faulted");
        Assert.True(later.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task ACallbackThatThrowsWithNoReporter_IsStillContained()
    {
        // The reporter is optional and one product path (BarkPipelineOptions.Clock) supplies none,
        // because that instance schedules nothing. So a null reporter is a real configuration and
        // this exercises it: the invoke inside the catch is null-conditional, and the clock keeps
        // servicing work afterwards.
        //
        // HONEST LIMIT, measured rather than assumed. This fact does NOT redden if the containment
        // is removed: with `new Timer(_ => fire(), ...)` restored it still reports Passed 1 /
        // Failed 0, because its only assertion is that a SECOND, unrelated schedule ran — which is
        // true whether or not the first throw was contained. The escaping exception surfaces
        // out-of-band as the runner's "[FATAL ERROR] ... Catastrophic failure" line, which fails
        // the suite's exit code but not this fact. The mechanism is pinned by
        // ACallbackThatThrows_IsContainedAndREPORTED above, which does redden. This fact pins the
        // null-reporter CONFIGURATION, and that is all it should be read as claiming.
        //
        // The shape is inherited verbatim from SystemScheduleClockTests.cs:74, so the same limit
        // applies to the sibling; it is named here rather than fixed because that file is outside
        // this packet's scope (SP-123 record.md, findings).
        var clock = new SystemSoundClock();
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var doomed = clock.Schedule(
            TimeSpan.Zero, () => throw new InvalidOperationException("no reporter"));
        using var alive = clock.Schedule(TimeSpan.Zero, () => barrier.TrySetResult());

        await TestWait.Until(
            barrier.Task, "the real sound clock to keep working after an unreported fault");
        Assert.True(barrier.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public void ANullCallback_IsRefusedOnTheCALLERSThread_NotLaterOnAPoolThread()
    {
        // SP-123's second finding, and it only exists BECAUSE the containment landed. Both siblings
        // carry ArgumentNullException.ThrowIfNull(fire) and this clock carried neither that nor the
        // try/catch. With the catch alone, a null callback would become a NullReferenceException
        // inside Run — on a pool thread, caught, and reported as a log line: a wiring bug demoted
        // to a diagnostic, which is worse than either the old crash or the siblings' fail-fast.
        var clock = new SystemSoundClock();

        var refused = Assert.Throws<ArgumentNullException>(
            () => clock.Schedule(TimeSpan.FromSeconds(1), null!));

        Assert.Equal("fire", refused.ParamName);
    }

    [Fact]
    public async Task AScheduleDueImmediately_ReallyFires_OnTheRealTimer()
    {
        var clock = new SystemSoundClock();
        var fired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var handle = clock.Schedule(TimeSpan.Zero, () => fired.TrySetResult());

        await TestWait.Until(fired.Task, "the real sound clock's zero-delay callback to run");
        Assert.True(fired.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task ANegativeDelay_IsClampedToImmediate_RatherThanThrowing()
    {
        // SystemSoundClock's Math.Max(0, ...): the interface promises "due <= 0 fires as soon as
        // possible", and System.Threading.Timer throws ArgumentOutOfRangeException on a negative
        // dueTime. Arbitration computes its pacing delay by SUBTRACTION (debt minus elapsed), so a
        // negative interval is an ordinary arithmetic outcome there, not a corner.
        var clock = new SystemSoundClock();
        var fired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var handle = clock.Schedule(TimeSpan.FromSeconds(-30), () => fired.TrySetResult());

        await TestWait.Until(fired.Task, "the real sound clock's negative-delay callback to run");
        Assert.True(fired.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task DisposingTheHandleBeforeItIsDue_SuppressesTheCallback()
    {
        // The property every stop in the audio stack rests on — the duck watchdog and the pacing
        // timer are both disposed and re-armed under the arbitration gate. Proved with a BARRIER,
        // not a wait: the cancelled schedule is due far enough out that it cannot legitimately have
        // fired, and the barrier is armed AFTERWARDS and due immediately, so when the barrier has
        // run the real clock has demonstrably serviced work armed after the cancellation.
        var clock = new SystemSoundClock();
        var cancelledFired = false;
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var doomed = clock.Schedule(TimeSpan.FromMinutes(10), () => cancelledFired = true);
        doomed.Dispose();

        using var handle = clock.Schedule(TimeSpan.Zero, () => barrier.TrySetResult());
        await TestWait.Until(
            barrier.Task, "a schedule armed after the cancellation to run on the real clock");

        Assert.False(cancelledFired,
            "a sound schedule whose handle was disposed still fired — a duck watchdog that can "
            + "outlive its own release is a volume ratchet, which this stack must never be");
    }

    [Fact]
    public void UtcNow_IsUTC_AndMovesForward()
    {
        // The offset is asserted rather than the value: every cooldown, min-gap and safety-hold
        // comparison in the bark gate goes through this reading, and a local-time clock would move
        // those windows by an hour twice a year.
        var clock = new SystemSoundClock();

        var first = clock.UtcNow;
        var second = clock.UtcNow;

        Assert.Equal(TimeSpan.Zero, first.Offset);
        Assert.Equal(TimeSpan.Zero, second.Offset);
        Assert.True(second >= first, $"the real clock went backwards: {first:O} then {second:O}");
    }
}
