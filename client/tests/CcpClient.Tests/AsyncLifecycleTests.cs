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
            await Task.Delay(Timeout.Infinite, token);
            return OperationOutcome.Completed.Instance;
        });

        owner.Cancel();

        var outcome = await op; // the owned completion resolves typed, never faults
        Assert.IsType<OperationOutcome.Cancelled>(outcome);
        Assert.IsType<OperationOutcome.Cancelled>(owner.LastOutcome);
        Assert.Equal(0, registry.OutstandingOperations);
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
        Assert.True(await WaitForAsync(() => heartbeat.TickCount > 0), "heartbeat should tick before teardown");

        await host.ShutdownAsync();

        Assert.Equal(0, registry.UnobservedOperations);
        Assert.Equal(0, registry.OutstandingOperations);
        Assert.NotNull(heartbeat.Completion);
        Assert.IsType<OperationOutcome.Cancelled>(await heartbeat.Completion!); // no unhandled exception
        Assert.Equal(1, heartbeat.StopCount); // SP-003 teardown invariants undisturbed
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

        await host.ShutdownAsync(); // bounded wait expires; teardown must not throw (SP-003 invariant)

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
        Assert.True(await WaitForAsync(() => heartbeat.TickCount > 1), "ticks should run before binding");
        Assert.Equal(0, fake.Posted);

        var texts = new ConcurrentQueue<string>();
        boundary.Bind(fake); // phase 4
        heartbeat.TickReporter = texts.Enqueue;

        Assert.True(await WaitForAsync(() => !texts.IsEmpty), "a tick should reach the reporter through the boundary");
        Assert.True(fake.Posted > 0);
        Assert.Contains(texts, t => t.StartsWith("Heartbeat: tick "));

        await heartbeat.StopAsync();
        Assert.NotNull(heartbeat.Completion);
        Assert.IsType<OperationOutcome.Cancelled>(await heartbeat.Completion!);
    }

    private static async Task<bool> WaitForAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(10);
        }

        return condition();
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
                await Task.Delay(Timeout.Infinite); // never observes the token
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
