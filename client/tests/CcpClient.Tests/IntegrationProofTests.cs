using CcpClient.Desktop;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;
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
        var settingsDir = Path.Combine(Path.GetTempPath(), "ccp-integration-" + Guid.NewGuid().ToString("N"));
        var root = new CompositionRoot
        {
            // The real root, exactly as Program.Main builds it — with the settings path
            // redirected so the test never touches the real per-user file.
            SettingsPathFactory = () => Path.Combine(settingsDir, "settings.json"),
        };
        ApplicationHost? host = null;

        var outcome = await StartupPhaseRunner.RunAsync(
            Program.CreateStartupPhases(root, trace, h => host = h), trace, CancellationToken.None);

        Assert.IsType<StartupOutcome.Success>(outcome);
        Assert.NotNull(host);

        // MainWindow's dependencies are (host, host.Trace) — both resolve from the root's product.
        Assert.Same(trace, host!.Trace);
        Assert.Equal(10, host.Participants.Count);
        var store = Assert.IsType<PersistenceStore<DemoSettings>>(host.Participants[0]);
        var heartbeat = Assert.IsType<HeartbeatParticipant>(host.Participants[1]);
        var ticker = Assert.IsType<CcpClient.Desktop.Features.StatusTickerParticipant>(host.Participants[2]);
        Assert.IsType<CcpClient.Desktop.Features.AvatarTube.AvatarTubeParticipant>(host.Participants[3]);
        Assert.IsType<CcpClient.Desktop.Features.Dtrh.DtrhSaveSlots>(host.Participants[4]);
        Assert.IsType<CcpClient.Desktop.Features.Dtrh.DtrhParticipant>(host.Participants[5]);
        // SP-046: the companion AI chain composes last (memory store started in phase-3 order).
        Assert.IsType<CcpClient.Desktop.Features.Companion.CompanionParticipant>(host.Participants[6]);
        // SP-098: the conditioning session's phase-3 start loads the preset WITHOUT starting a
        // session — WPF's engine runs only when the user presses START
        // (MainWindow/MainWindow.StartStop.cs:34,105).
        var session = Assert.IsType<CcpClient.Desktop.Session.SessionParticipant>(host.Participants[7]);
        Assert.True(session.Running);
        Assert.False(session.Engine.Running);
        // SP-118: the SCHEDULER registers last and its phase-3 start ALSO starts no session. It
        // arms a 60-second grace and nothing else — and on a fresh install the enable is off, so
        // even when the grace elapses the first thing the tick does is return
        // (MainWindow/MainWindow.StartStop.cs:604). The one participant in this list that CAN
        // begin a conditioning session by itself is asserted not to have.
        var scheduler = Assert.IsType<CcpClient.Desktop.Scheduling.SchedulerParticipant>(host.Participants[8]);
        Assert.True(scheduler.Running);
        Assert.False(scheduler.GracePassed);
        Assert.False(scheduler.Scheduler.Polling);
        Assert.False(scheduler.Scheduler.Enabled);
        Assert.Null(scheduler.Scheduler.Last);
        // SP-119: the HAPTIC sink registers last, and its phase-3 start CONNECTS TO NOTHING. This
        // build admits no provider client, so the participant never asks — a product that opened a
        // WebSocket to ws://127.0.0.1:12345 with nothing able to speak the protocol would be making
        // a connection no user could benefit from. The gate is closed too, and closed through the
        // "could not verify" answer rather than through "you are not a patron", because this build's
        // entitlement authority is unconfigured.
        var haptics = Assert.IsType<CcpClient.Desktop.Haptics.HapticParticipant>(host.Participants[9]);
        Assert.True(haptics.Running);
        Assert.Equal(0, haptics.ConnectAttempts);
        Assert.Null(haptics.LastConnectOutcome);
        Assert.Null(haptics.LastObservation);
        Assert.False(haptics.Enabled);
        Assert.False(haptics.OutputAllowed);
        Assert.IsType<CcpClient.Desktop.Haptics.HapticGateDecision.RefusedUnverified>(haptics.Gate);
        Assert.Equal(CcpClient.Desktop.Haptics.HapticProviderRoute.None, haptics.Sink.Route);
        Assert.False(session.Engine.Running);
        Assert.IsType<LoadOutcome.Missing>(session.Preset.LastLoadOutcome);
        Assert.True(heartbeat.Running); // phase 3 demonstrably started it
        Assert.Equal(1, heartbeat.StartCount);
        Assert.True(ticker.Running); // phase 3 started the participant...
        Assert.False(ticker.IsOperationLive); // ...but the restored flag (fresh install) keeps the operation OFF
        Assert.IsType<LoadOutcome.Missing>(store.LastLoadOutcome); // loaded in phase 3, fresh install

        // The trace the window displays records every phase outcome (incl. SP-006's
        // CapabilityProbes phase).
        Assert.Equal(4, host.Trace.Entries.Count);
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
            SettingsPathFactory = () => Path.Combine(
                Path.GetTempPath(), "ccp-integration-fail-" + Guid.NewGuid().ToString("N"), "settings.json"),
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
