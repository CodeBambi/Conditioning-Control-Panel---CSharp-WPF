using System.Collections.Concurrent;
using CcpClient.Desktop.Lifecycle;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// Async lifecycle/fault contract conformance: generation invalidation (§3), mid-flight
/// cancellation (§3.4), deterministic fault routing through the registry (§4, §7.3 — NOT
/// TaskScheduler.UnobservedTaskException, which is GC-timing flaky), zero-unobserved at
/// teardown (§6, §7.2), and the pre-binding dispatch rule (§5.3).
/// </summary>
public class AsyncLifecycleTests
{
    [Fact]
    public void RunAsync_WithoutBegin_ThrowsSequencingError()
    {
        var owner = new OperationRegistry().OwnerFor("P");

        // RunAsync throws synchronously (sequencing bug); the void local function keeps
        // xUnit2014 from mistaking this for an async-exception assertion.
        void Act() => _ = owner.RunAsync("early", _ => Task.FromResult<OperationOutcome>(OperationOutcome.Completed.Instance));
        Assert.Throws<InvalidOperationException>(Act);
    }

    [Fact]
    public async Task StaleGenerationCompletion_IsDiscarded_CannotOverwriteNewerState()
    {
        var registry = new OperationRegistry();
        var owner = registry.OwnerFor("P");
        owner.Begin(); // generation 0
        var staleGate = new TaskCompletionSource<OperationOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var staleOp = owner.RunAsync("slow", _ => staleGate.Task); // body ignores its token

        owner.Begin(); // generation 1: cancels generation 0's token, invalidates its completions
        var freshOp = owner.RunAsync("fresh", _ => Task.FromResult<OperationOutcome>(OperationOutcome.Completed.Instance));
        Assert.IsType<OperationOutcome.Completed>(await freshOp);
        Assert.IsType<OperationOutcome.Completed>(owner.LastOutcome);

        // Out-of-order completion from the stale generation arrives late.
        staleGate.SetResult(OperationOutcome.Completed.Instance);
        Assert.IsType<OperationOutcome.Completed>(await staleOp); // owned completion is still observed

        Assert.Equal(1, registry.DiscardedStaleCompletions); // ...but its application was discarded
        Assert.IsType<OperationOutcome.Completed>(owner.LastOutcome); // newer state untouched
        Assert.Equal(0, registry.OutstandingOperations);
    }

    [Fact]
    public async Task CancellationMidFlight_YieldsTypedCancelled_NoUnhandledException()
    {
        var registry = new OperationRegistry();
        var owner = registry.OwnerFor("P");
        owner.Begin();
        var op = owner.RunAsync("loop", async token =>
        {
            await Task.Delay(Timeout.Infinite, token); // wallclock-allow: never elapses — token-observed stand-in for in-flight work
            return OperationOutcome.Completed.Instance;
        });

        owner.Cancel();

        var outcome = await op; // the owned completion resolves typed, never faults
        Assert.IsType<OperationOutcome.Cancelled>(outcome);
        Assert.IsType<OperationOutcome.Cancelled>(owner.LastOutcome);
        Assert.Equal(0, registry.OutstandingOperations);
    }

    /// <summary>
    /// Fact 1 of 3. <c>CancellationTokenSource.Cancel()</c> runs its registrations
    /// synchronously on the calling thread, so an owner that cancels inside its own lock runs
    /// FOREIGN code with that lock held — and <c>IsLive</c> is reached the other way round, from
    /// under a caller's own lock (<c>Session/OwnedSessionEffect.cs</c>'s <c>Dot</c> holds the effect
    /// gate across it). No deadlock was ever observed; the only thing preventing one was an
    /// unenforced convention that every cancellation callback stay lock-free, and an earlier packet was
    /// reverted for exactly that inversion. The existing suite could not see it because every other
    /// test drives this on ONE thread.
    /// </summary>
    [Fact]
    public async Task Cancel_RunsCancellationCallbacks_WithoutHoldingTheOwnersGate()
        => Assert.IsType<OperationOutcome.Cancelled>(
            await TheOwnersGateIsFreeWhileACancellationCallbackRuns(owner => owner.Cancel()));

    /// <summary>Fact 2 of 3: <see cref="AsyncOperationOwner.Begin"/> reaches the same cycle,
    /// because it cancels the generation it retires.</summary>
    [Fact]
    public async Task Begin_RunsThePreviousGenerationsCallbacks_WithoutHoldingTheOwnersGate()
        => Assert.IsType<OperationOutcome.Cancelled>(
            await TheOwnersGateIsFreeWhileACancellationCallbackRuns(owner => _ = owner.Begin()));

    /// <summary>
    /// Fact 3 of 3, and the one that needs no second thread at all.
    ///
    /// <para><b>Why it bites, which is not obvious.</b> A <c>lock</c> is re-entrant. In the pre-fix
    /// shape the retired generation's callback ran INSIDE <c>lock (_gate)</c> and before
    /// <c>_generation++</c>, on the same thread, so <c>owner.Generation</c> re-entered the lock
    /// happily and answered the OLD generation. Cancelling after the critical section is what makes
    /// the new generation visible there. So this asserts the ordering directly, deterministically,
    /// with no wait and no timeout — and fails on <c>Assert.Equal</c> in milliseconds if the cancel
    /// moves back under the lock.</para>
    ///
    /// <para>It also pins the half of contract §3.1 the fix must not move: <c>Begin</c> returns the
    /// generation it installed, and the increment and the install stay one atomic transition.</para>
    /// </summary>
    [Fact]
    public async Task Begin_InstallsTheNewGeneration_BeforeThePreviousGenerationsCallbackRuns()
    {
        var registry = new OperationRegistry();
        var owner = registry.OwnerFor("P");
        var first = owner.Begin();

        var registered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var seenFromTheCallback = int.MinValue;
        var op = owner.RunAsync("parked", async token =>
        {
            using var registration = token.Register(() =>
            {
                seenFromTheCallback = owner.Generation;
                stopped.TrySetResult();
            });

            registered.SetResult();
            await stopped.Task;
            return OperationOutcome.Cancelled.Instance;
        });
        await TestWait.Until(registered.Task, "the parked operation to register its cancellation callback");

        var second = owner.Begin(); // retires `first`, running its callback synchronously on this thread

        Assert.Equal(first + 1, second); // Begin returns the generation it installed (contract §3.1)
        Assert.Equal(second, owner.Generation);
        Assert.Equal(second, seenFromTheCallback); // pre-fix: the callback saw `first`, from inside the lock
        Assert.IsType<OperationOutcome.Cancelled>(await op);
        Assert.Equal(0, registry.OutstandingOperations);
    }

    /// <summary>
    /// The two-thread ordering pin behind facts 1 and 2, and the handshake it uses.
    ///
    /// <para>Thread C runs the trigger; its cancellation callback signals that it is running and
    /// then parks on a signal the <c>finally</c> below always sets — never on a clock. Thread P then
    /// asks the owner a question that needs the owner's gate (<c>IsLive</c>, the exact chain
    /// <c>Dot</c> takes) and signals when it RETURNS. If the callback were still running under that
    /// gate, P could not return until the callback did.</para>
    ///
    /// <para><b>Against the pre-fix code this FAILS rather than hangs</b>, which is what makes it
    /// shippable: <see cref="TestWait"/> expires on the shared window with
    /// <c>TIMING-VERDICT:CONDITION-NEVER-TRUE</c>, the <c>finally</c> releases the callback, the
    /// trigger returns, the gate is released, P unblocks and both threads join. The PASSING path
    /// waits on no clock at all — every signal is already set when it is awaited. The bounded window
    /// on the failing path is irreducible: absence of progress is only observable by bounding, and
    /// both unbounded shapes wedge a host that has no per-test timeout.</para>
    ///
    /// <para>Returns the parked operation's terminal outcome so each fact asserts in its own body:
    /// assertions living only in a called helper are the documented false positive of
    /// <see cref="VacuousShapeDetector"/> (<c>no-assertion</c>), and removing the shape is cheaper
    /// than dispositioning it.</para>
    /// </summary>
    private static async Task<OperationOutcome> TheOwnersGateIsFreeWhileACancellationCallbackRuns(
        Action<AsyncOperationOwner> trigger)
    {
        var registry = new OperationRegistry();
        var owner = registry.OwnerFor("P");
        var generation = owner.Begin();

        var registered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var probeReturned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseTheCallback = new ManualResetEventSlim(false);

        // The product's own parked-operation shape (Session/OwnedSessionEffect.ParkUntilCancelledAsync).
        var op = owner.RunAsync("parked", async token =>
        {
            using var registration = token.Register(() =>
            {
                callbackEntered.SetResult();
                releaseTheCallback.Wait(); // deterministic signal; the finally below always sets it
                stopped.TrySetResult();
            });

            registered.SetResult();
            await stopped.Task;
            return OperationOutcome.Cancelled.Instance;
        });
        await TestWait.Until(registered.Task, "the parked operation to register its cancellation callback");

        var canceller = new Thread(() => trigger(owner)) { IsBackground = true, Name = "sp142-canceller" };
        var probe = new Thread(() =>
        {
            _ = owner.IsLive(generation);
            probeReturned.SetResult();
        }) { IsBackground = true, Name = "sp142-probe" };

        var probeStarted = false;
        canceller.Start();
        try
        {
            await TestWait.Until(callbackEntered.Task, "the cancellation callback to start running");

            // The callback is provably mid-flight from here to the finally.
            probe.Start();
            probeStarted = true;
            await TestWait.Until(
                probeReturned.Task,
                "the owner's gate to be free while one of its cancellation callbacks runs",
                () => $"callback running={!releaseTheCallback.IsSet}, canceller={canceller.ThreadState}, probe={probe.ThreadState}");
        }
        finally
        {
            releaseTheCallback.Set();
            canceller.Join();
            if (probeStarted)
            {
                probe.Join();
            }
        }

        var outcome = await op;
        Assert.Equal(0, registry.OutstandingOperations);
        return outcome;
    }

    [Fact]
    public async Task ResourceFailure_ClassifiedByOwner_RoutedAsTypedOutcome_NotSwallowed()
    {
        var registry = new OperationRegistry();
        var owner = registry.OwnerFor("Native");
        owner.Begin();

        var op = owner.RunAsync(
            "device",
            _ => throw new ResourceLostException("device lost"),
            ex => ex is ResourceLostException ? InitFailureKind.Recoverable : InitFailureKind.Fatal);

        var failed = Assert.IsType<OperationOutcome.Failed>(await op);
        Assert.Equal(InitFailureKind.Recoverable, failed.Kind);
        Assert.Contains("device lost", failed.Reason);
        Assert.Same(failed, owner.LastOutcome); // observed registry state, not a log-only failure
    }

    [Fact]
    public async Task DegradedOutcome_ClassifiedByOwner_RoutedAsTypedOutcome()
    {
        var registry = new OperationRegistry();
        var owner = registry.OwnerFor("Backend");
        owner.Begin();

        var op = owner.RunAsync(
            "backend",
            _ => throw new ResourceLostException("backend throttled"),
            _ => InitFailureKind.Degraded);

        var failed = Assert.IsType<OperationOutcome.Failed>(await op);
        Assert.Equal(InitFailureKind.Degraded, failed.Kind);
    }

    [Fact]
    public async Task FaultingOperation_DefaultClassifier_MapsToFatal()
    {
        var registry = new OperationRegistry();
        var owner = registry.OwnerFor("P");
        owner.Begin();

        var op = owner.RunAsync("boom", _ => throw new InvalidOperationException("unexpected"));

        var failed = Assert.IsType<OperationOutcome.Failed>(await op);
        Assert.Equal(InitFailureKind.Fatal, failed.Kind);
    }

    [Fact]
    public async Task Teardown_CancelsInFlightThroughSingleEntryPoint_AndReportsZeroUnobserved()
    {
        var log = new ListLogSink();
        var registry = new OperationRegistry();
        var boundary = new UiDispatchBoundary();
        var heartbeat = new HeartbeatParticipant(registry.OwnerFor("Heartbeat"), boundary, TimeSpan.FromMilliseconds(10));
        var host = new ApplicationHost(log, [heartbeat], new StartupTrace(), registry, boundary, TimeSpan.FromMilliseconds(500));
        await host.StartParticipantsAsync(CancellationToken.None);
        await TestWait.Until(() => heartbeat.TickCount > 0, "heartbeat should tick before teardown (real timer actor)", cancellationToken: TestContext.Current.CancellationToken);

        await host.ShutdownAsync();

        Assert.Equal(0, registry.UnobservedOperations);
        Assert.Equal(0, registry.OutstandingOperations);
        Assert.NotNull(heartbeat.Completion);
        Assert.IsType<OperationOutcome.Cancelled>(await heartbeat.Completion!); // no unhandled exception
        Assert.Equal(1, heartbeat.StopCount); // lifecycle teardown invariants undisturbed
    }

    [Fact]
    public async Task Teardown_OrphanedOperation_IsRecordedInRegistryState_NeverThrows()
    {
        var log = new ListLogSink();
        var registry = new OperationRegistry();
        var boundary = new UiDispatchBoundary();
        var orphan = new OrphanParticipant(registry.OwnerFor("Orphan"));
        var host = new ApplicationHost(log, [orphan], new StartupTrace(), registry, boundary, TimeSpan.FromMilliseconds(100));
        await host.StartParticipantsAsync(CancellationToken.None);

        await host.ShutdownAsync(); // bounded wait expires; teardown must not throw (lifecycle invariant)

        Assert.Equal(1, registry.UnobservedOperations);
        Assert.Contains(log.Messages, m => m.Contains("unobserved") && m.Contains("orphan", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Post_BeforeBinding_ThrowsInvalidOperation()
    {
        var boundary = new UiDispatchBoundary();

        Assert.False(boundary.IsBound);
        Assert.Throws<InvalidOperationException>(() => boundary.Post(() => { }));
    }

    [Fact]
    public void Bind_ThenPost_Dispatches_DoubleBind_Throws()
    {
        var boundary = new UiDispatchBoundary();
        var fake = new FakeUiDispatch();
        boundary.Bind(fake);
        var ran = false;

        boundary.Post(() => ran = true);

        Assert.True(ran);
        Assert.Equal(1, fake.Posted);
        Assert.Throws<InvalidOperationException>(() => boundary.Bind(fake));
    }

    [Fact]
    public async Task Heartbeat_SkipsProjectionUntilBound_ThenFlowsThroughBoundary()
    {
        var registry = new OperationRegistry();
        var boundary = new UiDispatchBoundary();
        var fake = new FakeUiDispatch();
        var heartbeat = new HeartbeatParticipant(registry.OwnerFor("Heartbeat"), boundary, TimeSpan.FromMilliseconds(10));

        await heartbeat.StartAsync(CancellationToken.None);

        // Phase 3 starts the participant before phase 4 binds: ticks accumulate, no post
        // is attempted, and nothing faults (contract §5.3 skip-until-bound).
        await TestWait.Until(() => heartbeat.TickCount > 1, "ticks should run before binding (real timer actor)", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(0, fake.Posted);

        var texts = new ConcurrentQueue<string>();
        boundary.Bind(fake); // phase 4
        heartbeat.TickReporter = texts.Enqueue;

        // Class 2: a REAL 500ms heartbeat timer actor — the tolerant window with the
        // loud classifier; the condition (a tick reached the reporter) is unchanged.
        await TestWait.Until(() => !texts.IsEmpty, "a tick should reach the reporter through the boundary (real 500ms heartbeat)", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(fake.Posted > 0);
        Assert.Contains(texts, t => t.StartsWith("Heartbeat: tick "));

        await heartbeat.StopAsync();
        Assert.NotNull(heartbeat.Completion);
        Assert.IsType<OperationOutcome.Cancelled>(await heartbeat.Completion!);
    }

    [Fact]
    public async Task Heartbeat_StoppedBeforeFirstTick_CompletesCancelled_ZeroTick()
    {
        var registry = new OperationRegistry();
        var boundary = new UiDispatchBoundary();
        var heartbeat = new HeartbeatParticipant(registry.OwnerFor("Heartbeat"), boundary, TimeSpan.FromMilliseconds(10));

        await heartbeat.StartAsync(CancellationToken.None);
        await heartbeat.StopAsync(); // immediate: the token is cancelled before (or during) the loop's first check

        // Zero-tick regression pin: deterministic because BOTH exits now agree —
        // the OCE path (loop ticked, then Delay observed the token) and the zero-tick
        // post-loop return both yield Cancelled; no interleaving is being controlled.
        // What breaks it: the defective post-loop `return OperationOutcome.Completed.Instance`
        // shape returning — reachable only with the token already cancelled (contract §2).
        Assert.NotNull(heartbeat.Completion);
        Assert.IsType<OperationOutcome.Cancelled>(await heartbeat.Completion!);
    }

    private sealed class ResourceLostException(string message) : Exception(message);

    private sealed class FakeUiDispatch : IUiDispatch
    {
        private int _posted;

        public int Posted => Volatile.Read(ref _posted);

        public void Post(Action action)
        {
            Interlocked.Increment(ref _posted);
            action();
        }
    }

    private sealed class ListLogSink : ILogSink
    {
        public List<string> Messages { get; } = [];

        public void Log(string message) => Messages.Add(message);
    }

    /// <summary>Registers required work that pathologically ignores its generation token.</summary>
    private sealed class OrphanParticipant(AsyncOperationOwner owner) : IBackgroundParticipant
    {
        public string Name => "Orphan";

        public bool Running { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Running = true;
            owner.Begin();
            _ = owner.RunAsync("orphan", async _ =>
            {
                await Task.Delay(Timeout.Infinite); // never observes the token // wallclock-allow: never elapses — deliberately uncooperative stand-in; the test's own token cancels the operation
                return OperationOutcome.Completed.Instance;
            });
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            Running = false;
            return Task.CompletedTask;
        }
    }
}
