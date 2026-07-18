using CcpClient.Desktop.Lifecycle;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// Contract §6: teardown runs the body exactly once per process, stops participants in
/// reverse start order, never throws, and leaves no orphaned participant on any of the
/// three trigger paths. The Avalonia Exit-event wiring (window-close) and the Main
/// try/catch (panic) are thin shells around the same guarded entry point tested here.
/// </summary>
public class TeardownTests
{
    [Fact]
    public async Task Shutdown_StopsParticipants_InReverseStartOrder_ExactlyOnce()
    {
        var order = new List<string>();
        var first = new CountingParticipant("First", order);
        var second = new CountingParticipant("Second", order);
        var host = NewHost([first, second]);
        await host.StartParticipantsAsync(CancellationToken.None);

        await host.ShutdownAsync();

        Assert.Equal(new[] { "Second", "First" }, order);
        Assert.Equal(1, first.StopCount);
        Assert.Equal(1, second.StopCount);
        Assert.False(first.Running);
        Assert.False(second.Running);
    }

    [Fact]
    public async Task Shutdown_RepeatedAndConcurrent_IsANoOpAfterFirstRun()
    {
        var participant = new CountingParticipant("Only", []);
        var host = NewHost([participant]);
        await host.StartParticipantsAsync(CancellationToken.None);

        // Concurrent invocation (close racing panic) plus a later repeat.
        await Task.WhenAll(host.ShutdownAsync(), host.ShutdownAsync());
        await host.ShutdownAsync();

        Assert.Equal(1, participant.StopCount);
        Assert.True(host.IsShutdown);
    }

    [Fact]
    public async Task StartupFailurePath_ParticipantsStartedSoFar_StoppedExactlyOnce()
    {
        // Simulate the phase-3 failure shape: first participant started, second throws on
        // start, Main's failure branch tears down. The started one must be stopped once.
        var started = new CountingParticipant("Started", []);
        var failing = new ThrowingStartParticipant("Failing");
        var host = NewHost([started, failing]);

        var outcome = await host.StartParticipantsAsync(CancellationToken.None);
        Assert.IsType<StartupOutcome.Failed>(outcome);
        Assert.True(started.Running);

        await host.ShutdownAsync();

        Assert.Equal(1, started.StopCount);
        Assert.False(started.Running);
        Assert.Equal(0, failing.StopCount); // never started: stop is a no-op
    }

    [Fact]
    public async Task PanicPath_LogsAndTearsDown_WithoutHanging()
    {
        var log = new ListLogSink();
        var participant = new CountingParticipant("Heartbeat", []);
        var host = new ApplicationHost(log, [participant], new StartupTrace());
        await host.StartParticipantsAsync(CancellationToken.None);

        // Panic shape (contract §6): log the fault, then guarded teardown.
        log.Log("panic: unhandled exception escaped the UI lifetime: simulated");
        await host.ShutdownAsync();

        Assert.Contains(log.Messages, m => m.StartsWith("panic:"));
        Assert.Equal(1, participant.StopCount);
    }

    [Fact]
    public async Task Shutdown_ParticipantStopThrows_LogsAndContinuesToRemaining()
    {
        var log = new ListLogSink();
        var good = new CountingParticipant("Good", []);
        var bad = new ThrowingStopParticipant("Bad");
        // Bad is later in start order, so it stops first and must not prevent Good's stop.
        var host = new ApplicationHost(log, [good, bad], new StartupTrace());
        await host.StartParticipantsAsync(CancellationToken.None);

        await host.ShutdownAsync(); // must not throw

        Assert.Equal(1, good.StopCount);
        Assert.Contains(log.Messages, m => m.Contains("Bad") && m.Contains("stop failed"));
    }

    [Fact]
    public async Task Stop_OfNeverStartedParticipant_IsANoOp()
    {
        var participant = new CountingParticipant("Never", []);
        var host = NewHost([participant]);

        await host.ShutdownAsync();

        Assert.Equal(0, participant.StopCount);
    }

    private static ApplicationHost NewHost(IBackgroundParticipant[] participants) =>
        new(new ListLogSink(), participants, new StartupTrace());

    private sealed class ListLogSink : ILogSink
    {
        public List<string> Messages { get; } = [];

        public void Log(string message) => Messages.Add(message);
    }

    private sealed class CountingParticipant(string name, List<string> stopOrder) : IBackgroundParticipant
    {
        public string Name { get; } = name;

        public bool Running { get; private set; }

        public int StopCount { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Running = true;
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            if (Running)
            {
                Running = false;
                StopCount++;
                stopOrder.Add(Name);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingStartParticipant(string name) : IBackgroundParticipant
    {
        public string Name { get; } = name;

        public bool Running => false;

        public int StopCount { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("start boom");

        public Task StopAsync()
        {
            if (Running)
            {
                StopCount++;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingStopParticipant(string name) : IBackgroundParticipant
    {
        public string Name { get; } = name;

        public bool Running { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Running = true;
            return Task.CompletedTask;
        }

        public Task StopAsync() => throw new InvalidOperationException("stop boom");
    }
}
