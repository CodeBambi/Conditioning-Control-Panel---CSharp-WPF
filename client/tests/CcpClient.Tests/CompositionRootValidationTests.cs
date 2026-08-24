using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;
using Xunit;

namespace CcpClient.Tests;

// Co-located with the ONLY in-suite CCP_DATA_ROOT mutator (the
// ProcessEnvCollection pattern — intra-collection sequentiality). This class builds REAL
// composition roots, whose DefaultSettingsPath() reads the process-wide override variable;
// in the default collection that read can land inside DataRootOverrideEnvTests' mutation
// window (probe-proven red on this runner: 'relative/not-absolute' leaking into
// Construction_StartsNoBackgroundWork).
[Collection(nameof(ProcessEnvCollection))]
public class CompositionRootValidationTests
{
    [Fact]
    public void Validate_DefaultRoot_Passes()
    {
        var root = new CompositionRoot();

        var valid = root.Validate(out var failure);

        Assert.True(valid);
        Assert.Null(failure);
    }

    [Fact]
    public void Validate_MissingLogSink_FailsFastWithTypedFatalNamingTheRegistration()
    {
        var root = new CompositionRoot { LogSinkFactory = () => null };

        var valid = root.Validate(out var failure);

        Assert.False(valid);
        Assert.NotNull(failure);
        Assert.Equal("CompositionRoot", failure!.Phase);
        Assert.Equal(InitFailureKind.Fatal, failure.Kind);
        Assert.Contains("LogSink", failure.Reason);
    }

    [Fact]
    public void Validate_MissingParticipants_FailsFastWithTypedFatalNamingTheRegistration()
    {
        var root = new CompositionRoot { ParticipantsFactory = _ => null };

        var valid = root.Validate(out var failure);

        Assert.False(valid);
        Assert.NotNull(failure);
        Assert.Equal(InitFailureKind.Fatal, failure!.Kind);
        Assert.Contains("BackgroundParticipants", failure.Reason);
    }

    [Fact]
    public void Build_AfterValidate_ConstructsHostWithNamedParticipants()
    {
        var root = new CompositionRoot();
        Assert.True(root.Validate(out _));

        var host = root.Build(new StartupTrace());

        Assert.Equal(13, host.Participants.Count);
        // Persistence contract §4 rule 1: the store registers first, so its phase-3 load
        // completes before any consumer participant starts.
        Assert.IsType<PersistenceStore<DemoSettings>>(host.Participants[0]);
        // The MOTION preference is a settings store too, so it registers with the stores and
        // ahead of every consumer: its readers are the five hosted WebView2 surfaces and the
        // System page's picker, all phase-4.
        Assert.IsType<PersistenceStore<CcpClient.Desktop.Motion.MotionSettingsDocument>>(host.Participants[1]);
        // The APP-WIDE audio owner: its settings document has to be loaded before anything reads a
        // volume or an output-device choice off it, so it registers with the settings stores.
        Assert.IsType<CcpClient.Desktop.Audio.AudioParticipant>(host.Participants[2]);
        Assert.IsType<HeartbeatParticipant>(host.Participants[3]);
        // The demonstrator ticker registers AFTER the store — phase-3 start order
        // IS the restore-then-start ordering.
        Assert.IsType<CcpClient.Desktop.Features.StatusTickerParticipant>(host.Participants[4]);
        // The AvatarTube demonstrator (construction starts nothing).
        Assert.IsType<CcpClient.Desktop.Features.AvatarTube.AvatarTubeParticipant>(host.Participants[5]);
        // The DTRH save slots (persistence machinery per slot + index).
        Assert.IsType<CcpClient.Desktop.Features.Dtrh.DtrhSaveSlots>(host.Participants[6]);
        // The companion AI chain (pipeline + memory + awareness + executor).
        Assert.IsType<CcpClient.Desktop.Features.Companion.CompanionParticipant>(host.Participants[8]);
        // The conditioning session (its preset load runs after every other store's
        // phase-3 load).
        Assert.IsType<CcpClient.Desktop.Session.SessionParticipant>(host.Participants[9]);
        // The SCHEDULER registers last, and the position is behaviour rather than tidiness.
        // Registration order is phase-3 START order, so the session's preset load completes before
        // the scheduler can evaluate anything; and participant stop is REVERSE order, so at
        // teardown the scheduler's poll dies before the session it drives.
        Assert.IsType<CcpClient.Desktop.Scheduling.SchedulerParticipant>(host.Participants[10]);
        // The CAMERA capability's consent store and enumeration route: app-scoped like the sink
        // below it, holding no device, no socket and no handle, so its stop order carries nothing.
        Assert.IsType<CcpClient.Desktop.Camera.CameraParticipant>(host.Participants[11]);
        // The HAPTIC sink registers last, after the scheduler AND after the camera, and the position
        // is behaviour for the same reason. Participant stop is REVERSE order, so the sink is
        // released before anything that could still be driving it — and its all-stop runs earlier
        // still, in the reserved pre-drain head slot, which is upstream's own ordering
        // (ConditioningControlPanel/App.xaml.cs:4401-4407).
        Assert.IsType<CcpClient.Desktop.Haptics.HapticParticipant>(host.Participants[12]);
    }

    [Fact]
    public void Construction_StartsNoBackgroundWork()
    {
        // Contract §4.4: constructors are cheap; construction (phase 2) and start (phase 3)
        // are separate steps. Building the root must leave every participant un-started.
        var host = new CompositionRoot().Build(new StartupTrace());

        var heartbeat = Assert.IsType<HeartbeatParticipant>(host.Participants[3]);
        Assert.False(heartbeat.Running);
        Assert.Equal(0, heartbeat.StartCount);
        Assert.False(host.Participants[0].Running);
        // Construction composes the session and starts NO session with it.
        var session = Assert.IsType<CcpClient.Desktop.Session.SessionParticipant>(host.Participants[9]);
        Assert.False(session.Running);
        Assert.False(session.Engine.Running);
        Assert.Null(session.Flash.Completion);
    }

    [Fact]
    public async Task StartParticipants_StartsEachParticipant_InRegistrationOrder()
    {
        var order = new List<string>();
        var first = new RecordingParticipant("First", order);
        var second = new RecordingParticipant("Second", order);
        var host = new ApplicationHost(new ListLogSink(), [first, second], new StartupTrace());

        var outcome = await host.StartParticipantsAsync(CancellationToken.None);

        Assert.IsType<StartupOutcome.Success>(outcome);
        Assert.Equal(new[] { "First", "Second" }, order);
        Assert.True(first.Running);
        Assert.True(second.Running);
    }

    [Fact]
    public async Task StartParticipants_StartFailure_YieldsTypedFatal_NotException()
    {
        var boom = new InvalidOperationException("start boom");
        var host = new ApplicationHost(new ListLogSink(), [new ThrowingParticipant("Bad", boom)], new StartupTrace());

        var outcome = await host.StartParticipantsAsync(CancellationToken.None);

        var failed = Assert.IsType<StartupOutcome.Failed>(outcome);
        Assert.Equal("CoreServices", failed.Failure.Phase);
        Assert.Equal(InitFailureKind.Fatal, failed.Failure.Kind);
        Assert.Contains("Bad", failed.Failure.Reason);
        Assert.Same(boom, failed.Failure.Exception);
    }

    private sealed class ListLogSink : ILogSink
    {
        public List<string> Messages { get; } = [];

        public void Log(string message) => Messages.Add(message);
    }

    private sealed class RecordingParticipant(string name, List<string> order) : IBackgroundParticipant
    {
        public string Name { get; } = name;

        public bool Running { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            order.Add(Name);
            Running = true;
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            Running = false;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingParticipant(string name, Exception exception) : IBackgroundParticipant
    {
        public string Name { get; } = name;

        public bool Running => false;

        public Task StartAsync(CancellationToken cancellationToken) => throw exception;

        public Task StopAsync() => Task.CompletedTask;
    }
}
