using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;
using Xunit;

namespace CcpClient.Tests;

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

        Assert.Equal(5, host.Participants.Count);
        // Persistence contract §4 rule 1: the store registers first, so its phase-3 load
        // completes before any consumer participant starts.
        Assert.IsType<PersistenceStore<DemoSettings>>(host.Participants[0]);
        Assert.IsType<HeartbeatParticipant>(host.Participants[1]);
        // SP-007: the demonstrator ticker registers AFTER the store — phase-3 start order
        // IS the restore-then-start ordering.
        Assert.IsType<CcpClient.Desktop.Features.StatusTickerParticipant>(host.Participants[2]);
        // SP-015: the AvatarTube demonstrator (construction starts nothing).
        Assert.IsType<CcpClient.Desktop.Features.AvatarTube.AvatarTubeParticipant>(host.Participants[3]);
    }

    [Fact]
    public void Construction_StartsNoBackgroundWork()
    {
        // Contract §4.4: constructors are cheap; construction (phase 2) and start (phase 3)
        // are separate steps. Building the root must leave every participant un-started.
        var host = new CompositionRoot().Build(new StartupTrace());

        var heartbeat = Assert.IsType<HeartbeatParticipant>(host.Participants[1]);
        Assert.False(heartbeat.Running);
        Assert.Equal(0, heartbeat.StartCount);
        Assert.False(host.Participants[0].Running);
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
