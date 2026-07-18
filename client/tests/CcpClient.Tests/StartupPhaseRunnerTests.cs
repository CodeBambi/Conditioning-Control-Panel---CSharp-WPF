using CcpClient.Desktop.Lifecycle;
using Xunit;

namespace CcpClient.Tests;

public class StartupPhaseRunnerTests
{
    [Fact]
    public async Task RunAsync_ExecutesPhasesInOrder()
    {
        var order = new List<string>();
        var phases = new[]
        {
            StartupPhase.FromSync("One", _ => { order.Add("One"); return StartupOutcome.Success.Instance; }),
            StartupPhase.FromSync("Two", _ => { order.Add("Two"); return StartupOutcome.Success.Instance; }),
            StartupPhase.FromSync("Three", _ => { order.Add("Three"); return StartupOutcome.Success.Instance; }),
        };

        var outcome = await StartupPhaseRunner.RunAsync(phases, new StartupTrace(), CancellationToken.None);

        Assert.IsType<StartupOutcome.Success>(outcome);
        Assert.Equal(new[] { "One", "Two", "Three" }, order);
    }

    [Fact]
    public async Task RunAsync_CancellationBetweenPhases_LaterPhasesNeverRun()
    {
        using var cts = new CancellationTokenSource();
        var ran = new List<string>();
        var phases = new[]
        {
            StartupPhase.FromSync("One", _ => { ran.Add("One"); cts.Cancel(); return StartupOutcome.Success.Instance; }),
            StartupPhase.FromSync("Two", _ => { ran.Add("Two"); return StartupOutcome.Success.Instance; }),
            StartupPhase.FromSync("Three", _ => { ran.Add("Three"); return StartupOutcome.Success.Instance; }),
        };

        var outcome = await StartupPhaseRunner.RunAsync(phases, new StartupTrace(), cts.Token);

        Assert.IsType<StartupOutcome.Cancelled>(outcome);
        Assert.Equal(new[] { "One" }, ran);
    }

    [Fact]
    public async Task RunAsync_CancelledBeforeStart_NoPhaseRuns()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var ran = false;
        var phases = new[]
        {
            StartupPhase.FromSync("One", _ => { ran = true; return StartupOutcome.Success.Instance; }),
        };

        var outcome = await StartupPhaseRunner.RunAsync(phases, new StartupTrace(), cts.Token);

        Assert.IsType<StartupOutcome.Cancelled>(outcome);
        Assert.False(ran);
    }

    [Fact]
    public async Task RunAsync_PhaseCancellingDuringWork_YieldsCancelledNotFailure()
    {
        using var cts = new CancellationTokenSource();
        var phases = new[]
        {
            new StartupPhase("One", ct =>
            {
                cts.Cancel();
                ct.ThrowIfCancellationRequested();
                return Task.FromResult<StartupOutcome>(StartupOutcome.Success.Instance);
            }),
        };

        var outcome = await StartupPhaseRunner.RunAsync(phases, new StartupTrace(), cts.Token);

        Assert.IsType<StartupOutcome.Cancelled>(outcome);
    }

    [Fact]
    public async Task RunAsync_FailingPhase_YieldsTypedFailure_LaterPhasesNeverRun()
    {
        var laterRan = false;
        var failure = new InitFailure("Two", InitFailureKind.Fatal, "deliberate failure");
        var phases = new[]
        {
            StartupPhase.FromSync("One", _ => StartupOutcome.Success.Instance),
            StartupPhase.FromSync("Two", _ => new StartupOutcome.Failed(failure)),
            StartupPhase.FromSync("Three", _ => { laterRan = true; return StartupOutcome.Success.Instance; }),
        };

        var outcome = await StartupPhaseRunner.RunAsync(phases, new StartupTrace(), CancellationToken.None);

        var failed = Assert.IsType<StartupOutcome.Failed>(outcome);
        Assert.Same(failure, failed.Failure);
        Assert.Equal("Two", failed.Failure.Phase);
        Assert.Equal(InitFailureKind.Fatal, failed.Failure.Kind);
        Assert.False(laterRan);
    }

    [Fact]
    public async Task RunAsync_ThrowingPhase_ConvertedToFatalFailure_NoUnhandledException()
    {
        var boom = new InvalidOperationException("boom");
        var phases = new[]
        {
            new StartupPhase("One", _ => throw boom),
        };

        var outcome = await StartupPhaseRunner.RunAsync(phases, new StartupTrace(), CancellationToken.None);

        var failed = Assert.IsType<StartupOutcome.Failed>(outcome);
        Assert.Equal("One", failed.Failure.Phase);
        Assert.Equal(InitFailureKind.Fatal, failed.Failure.Kind);
        Assert.Same(boom, failed.Failure.Exception);
    }

    [Fact]
    public async Task RunAsync_RecordsOutcomesInTrace()
    {
        var failure = new InitFailure("Two", InitFailureKind.Fatal, "deliberate failure");
        var phases = new[]
        {
            StartupPhase.FromSync("One", _ => StartupOutcome.Success.Instance),
            StartupPhase.FromSync("Two", _ => new StartupOutcome.Failed(failure)),
        };
        var trace = new StartupTrace();

        await StartupPhaseRunner.RunAsync(phases, trace, CancellationToken.None);

        Assert.Equal(2, trace.Entries.Count);
        Assert.Contains("One: ok", trace.Entries[0]);
        Assert.Contains("Two: failed (Fatal)", trace.Entries[1]);
    }
}
