using CcpClient.Desktop;
using CcpClient.Desktop.Lifecycle;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// Contract §10.2 / A-014 anti-unwired rule: walk the REAL composition root (no mocks,
/// no substitute builders) through the REAL phase runner and assert every dependency
/// MainWindow requires resolves from it. The window-construction half of the proof is the
/// user-visible trace (contract §10.1); Avalonia.Headless is not admitted (row 7).
/// </summary>
public class IntegrationProofTests
{
    [Fact]
    public async Task RealCompositionRoot_ThroughRealPhaseRunner_ResolvesEveryWindowDependency()
    {
        var trace = new StartupTrace();
        var root = new CompositionRoot(); // the real root, exactly as Program.Main builds it
        ApplicationHost? host = null;

        var outcome = await StartupPhaseRunner.RunAsync(
            Program.CreateStartupPhases(root, trace, h => host = h), trace, CancellationToken.None);

        Assert.IsType<StartupOutcome.Success>(outcome);
        Assert.NotNull(host);

        // MainWindow's dependencies are (host, host.Trace) — both resolve from the root's product.
        Assert.Same(trace, host!.Trace);
        var heartbeat = Assert.IsType<HeartbeatParticipant>(Assert.Single(host.Participants));
        Assert.True(heartbeat.Running); // phase 3 demonstrably started it
        Assert.Equal(1, heartbeat.StartCount);

        // The trace the window displays records every phase outcome.
        Assert.Equal(3, host.Trace.Entries.Count);
        Assert.All(host.Trace.Entries, entry => Assert.EndsWith(": ok", entry));

        await host.ShutdownAsync();
        Assert.Equal(1, heartbeat.StopCount); // teardown reaches the demonstrator
    }

    [Fact]
    public async Task StartupFailure_ThroughRealRunner_TypedFailureAndTeardownLeavesNoOrphan()
    {
        // Real runner, real root shape, one participant deliberately failing to start.
        var trace = new StartupTrace();
        HeartbeatParticipant? started = null;
        var root = new CompositionRoot
        {
            ParticipantsFactory = infra =>
            {
                started = new HeartbeatParticipant(infra.OwnerFor("Heartbeat"), infra.UiDispatch);
                return [started, new FailingParticipant()];
            },
        };
        ApplicationHost? host = null;

        var outcome = await StartupPhaseRunner.RunAsync(
            Program.CreateStartupPhases(root, trace, h => host = h), trace, CancellationToken.None);

        var failed = Assert.IsType<StartupOutcome.Failed>(outcome);
        Assert.Equal("CoreServices", failed.Failure.Phase);
        Assert.Equal(InitFailureKind.Fatal, failed.Failure.Kind);

        // Main's startup-failure branch: guarded teardown of completed phases only.
        Assert.NotNull(host);
        await host!.ShutdownAsync();
        Assert.Equal(1, started!.StopCount);
    }

    private sealed class FailingParticipant : IBackgroundParticipant
    {
        public string Name => "Failing";

        public bool Running => false;

        public Task StartAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("deliberate start failure");

        public Task StopAsync() => Task.CompletedTask;
    }
}
