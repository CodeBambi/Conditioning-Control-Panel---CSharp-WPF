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
/// <para><b>And how it is covered without a wall-clock wait — with ONE deliberate exception, named
/// (SP-124).</b> Every fact here waits on a DETERMINISTIC SIGNAL through the approved helper: no
/// <c>Thread.Sleep</c>, no bare <c>Task.Delay</c>, no clock poll. The negative observation — that a
/// cancelled schedule does not fire — is proved with an ordering barrier rather than by waiting.
/// <see cref="DisposingTheHandleBeforeItIsDue_SuppressesTheCallback"/> is the exception and it is
/// deliberate: <b>its subject IS a due time arriving</b>, so it schedules at a short delay and waits
/// for the resulting signals. That is not a tolerance bought to make it pass — until SP-124 the
/// doomed schedule there was due in TEN MINUTES, which made its assertion incapable of failing, and
/// observing the delay is exactly what gives it teeth. No fact here asserts how LONG anything
/// took.</para>
/// </summary>
public class SystemSessionClockTests
{
    /// <summary>
    /// SP-124. The doomed schedule's due time — the one delay in this file whose ELAPSING is the
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
        // The property every stop in the port rests on.
        //
        // SP-124 — WHY THIS FACT LOOKS LIKE THIS NOW, AND WHY IT DID NOT BITE BEFORE. The doomed
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
        var clock = new SystemSessionClock();
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
        await TestWait.Until(barrier.Task, "a schedule armed after the cancellation to run on the real clock");

        Assert.True(control.Task.IsCompletedSuccessfully,
            "the positive control never completed successfully — this fact cannot observe a suppression it never gave a chance to happen");
        Assert.False(doomedFired.Task.IsCompleted,
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
