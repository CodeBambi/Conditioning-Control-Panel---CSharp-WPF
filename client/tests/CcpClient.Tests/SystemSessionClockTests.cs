using CcpClient.Desktop.Session;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-101 — the REAL clock every module will pace on in the product.
///
/// <para><b>Why this file exists.</b> SP-098's land named it structurally: every session fact in
/// the suite substitutes a manual clock, so <see cref="SystemSessionClock"/> — the one implementation
/// that ships, and the seam all fifteen modules' pacing runs through — was compiled and never
/// executed. A schedule that never fired, a negative delay that threw, or a disposed handle that
/// went off anyway would have reached a user with a green suite behind it.</para>
///
/// <para><b>And how it is covered without a wall-clock wait.</b> Every fact here waits on a
/// DETERMINISTIC SIGNAL through the approved helper, never on an interval elapsing. The negative
/// observation — that a cancelled schedule does not fire — is proved with an ordering barrier
/// rather than by waiting: a second schedule due immediately is armed after the first is cancelled,
/// and when THAT one has fired, the cancelled one must still not have. No fact here asserts how
/// long anything took, because none of them is about elapsed time.</para>
/// </summary>
public class SystemSessionClockTests
{
    [Fact]
    public async Task AScheduleDueImmediately_ReallyFires_OnTheRealTimer()
    {
        var clock = new SystemSessionClock();
        var fired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var handle = clock.Schedule(TimeSpan.Zero, () => fired.TrySetResult());

        await TestWait.Until(fired.Task, "the real session clock's zero-delay callback to run");
        Assert.True(fired.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task ANegativeDelay_IsClampedToImmediate_RatherThanThrowing()
    {
        // SystemSessionClock's Math.Max(0, ...): the interface promises "due <= 0 fires as soon as
        // possible", and System.Threading.Timer throws ArgumentOutOfRangeException on a negative
        // dueTime. A module whose dial arithmetic ever produced a negative interval would take the
        // session down on a pool thread instead of firing early.
        var clock = new SystemSessionClock();
        var fired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var handle = clock.Schedule(TimeSpan.FromSeconds(-30), () => fired.TrySetResult());

        await TestWait.Until(fired.Task, "the real session clock's negative-delay callback to run");
        Assert.True(fired.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task DisposingTheHandleBeforeItIsDue_SuppressesTheCallback()
    {
        // The property every stop in the port rests on. Proved with a BARRIER, not a wait: the
        // cancelled schedule is due far enough out that it cannot legitimately have fired, and the
        // barrier schedule is armed afterwards and due immediately — so when the barrier has run,
        // the real clock has demonstrably serviced work armed AFTER the cancellation.
        var clock = new SystemSessionClock();
        var cancelledFired = false;
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var doomed = clock.Schedule(TimeSpan.FromMinutes(10), () => cancelledFired = true);
        doomed.Dispose();

        using var handle = clock.Schedule(TimeSpan.Zero, () => barrier.TrySetResult());
        await TestWait.Until(barrier.Task, "a schedule armed after the cancellation to run on the real clock");

        Assert.False(cancelledFired,
            "a schedule whose handle was disposed still fired — every stop in this port depends on it not doing that");
    }

    [Fact]
    public async Task DisposingTheHandleTwice_IsHarmless()
    {
        // Disarm disposes the pending handle synchronously AND the cancellation callback disposes
        // it again (async-lifecycle-fault-contract §5.5). Both paths run on a normal stop, so a
        // second dispose has to be a no-op rather than an ObjectDisposedException on a teardown
        // thread.
        var clock = new SystemSessionClock();
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var handle = clock.Schedule(TimeSpan.FromMinutes(10), () => { });
        handle.Dispose();
        handle.Dispose();

        using var alive = clock.Schedule(TimeSpan.Zero, () => barrier.TrySetResult());
        await TestWait.Until(barrier.Task, "the real clock to keep working after a double dispose");
        Assert.True(barrier.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task ManySchedulesOnOneClock_AllFire_Independently()
    {
        // Fifteen modules will share one clock instance. Nothing in it is per-caller state, and this
        // is what says so: eight schedules armed at once, each with its own callback, all serviced.
        var clock = new SystemSessionClock();
        var signals = Enumerable.Range(0, 8)
            .Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))
            .ToArray();
        var handles = signals.Select(s => clock.Schedule(TimeSpan.Zero, () => s.TrySetResult())).ToArray();

        await TestWait.Until(
            Task.WhenAll(signals.Select(s => s.Task)),
            "every one of eight schedules on one real clock to run");

        Assert.NotEmpty(signals);
        Assert.All(signals, s => Assert.True(s.Task.IsCompletedSuccessfully));

        foreach (var handle in handles)
        {
            handle.Dispose();
        }
    }

    [Fact]
    public void UtcNow_IsUtc_AndMovesForward()
    {
        var clock = new SystemSessionClock();

        var first = clock.UtcNow;
        var second = clock.UtcNow;

        // The offset is asserted rather than the value: every firing's timestamp goes through this,
        // and a local-time clock would put a user's session log an hour out twice a year.
        Assert.Equal(TimeSpan.Zero, first.Offset);
        Assert.Equal(TimeSpan.Zero, second.Offset);
        Assert.True(second >= first, $"the real clock went backwards: {first:O} then {second:O}");
    }

    [Fact]
    public async Task ACallbackThatThrows_IsContainedAndREPORTED_RatherThanKillingTheProcess()
    {
        // THE finding this file was written to catch, and it was not theoretical: the first version
        // of this fact killed the whole test host. A timer callback runs on a pool thread with no
        // caller above it, so an escaping exception is an UNHANDLED exception and the runtime ends
        // the process — from a module's scheduler, with no diagnostic. Fifteen modules will do real
        // work inside these callbacks.
        var faults = new List<Exception>();
        var reported = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clock = new SystemSessionClock(ex =>
        {
            lock (faults)
            {
                faults.Add(ex);
            }

            reported.TrySetResult();
        });

        using var first = clock.Schedule(TimeSpan.Zero, () =>
            throw new InvalidOperationException("a module's callback faulted"));

        await TestWait.Until(reported.Task, "the faulting callback's exception to be reported");

        // Contained AND reported: a silent catch would be the worse half of the same defect,
        // because the module would simply stop working and nothing would say why.
        var fault = Assert.Single(faults);
        Assert.IsType<InvalidOperationException>(fault);
        Assert.Equal("a module's callback faulted", fault.Message);

        // ...and the clock is still a clock afterwards.
        var later = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var second = clock.Schedule(TimeSpan.Zero, () => later.TrySetResult());
        await TestWait.Until(later.Task, "a later schedule to run after an earlier callback faulted");
        Assert.True(later.Task.IsCompletedSuccessfully);
    }
}
