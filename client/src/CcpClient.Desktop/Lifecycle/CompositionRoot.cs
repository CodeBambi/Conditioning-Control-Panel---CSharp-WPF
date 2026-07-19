namespace CcpClient.Desktop.Lifecycle;

/// <summary>The minimal logger seam the panic path needs (contract §9). No levels, no framework.</summary>
public interface ILogSink
{
    void Log(string message);
}

/// <summary>Default sink: stderr + debug output. Carries no secrets or user content.</summary>
public sealed class DebugLogSink : ILogSink
{
    public void Log(string message)
    {
        Console.Error.WriteLine(message);
        System.Diagnostics.Debug.WriteLine(message);
    }
}

/// <summary>
/// The composition root: a plain object graph built by explicit constructor calls
/// (contract §4, §7). No DI container, no static accessor. Validation is an explicit
/// checklist over named registrations — a missing registration is a typed Fatal failure
/// before the window exists, never a null at first use.
/// </summary>
/// <summary>
/// The infrastructure participants are constructed against: the operation registry (async
/// contract §1) and the late-bound UI dispatch boundary (async contract §5). Created by
/// the composition root in <see cref="CompositionRoot.Build"/>; participants receive their
/// single owner and the boundary here, never by locating them later.
/// </summary>
public sealed record ParticipantInfrastructure(OperationRegistry Registry, UiDispatchBoundary UiDispatch)
{
    /// <summary>The single async-operation owner for one participant (async contract §1.1).</summary>
    public AsyncOperationOwner OwnerFor(string participantName) => Registry.OwnerFor(participantName);
}

public sealed class CompositionRoot
{
    /// <summary>Factory seam so tests can deliberately blank a registration (contract §4 test).</summary>
    public Func<ILogSink?> LogSinkFactory { get; init; } = () => new DebugLogSink();

    /// <summary>Each required participant is named here; deleting one is a compile error or a validation failure.</summary>
    public Func<ParticipantInfrastructure, IReadOnlyList<IBackgroundParticipant>?> ParticipantsFactory { get; init; } = DefaultParticipants;

    private static IReadOnlyList<IBackgroundParticipant> DefaultParticipants(ParticipantInfrastructure infra) =>
        [new HeartbeatParticipant(infra.OwnerFor("Heartbeat"), infra.UiDispatch)];

    /// <summary>
    /// Phase 2 body: fail fast with a typed error naming what is missing.
    /// </summary>
    public bool Validate(out InitFailure? failure)
    {
        if (LogSinkFactory() is null)
        {
            failure = new InitFailure("CompositionRoot", InitFailureKind.Fatal, "Missing registration: LogSink");
            return false;
        }

        // Probe with a scratch infrastructure (participant constructors are cheap, contract §4.4).
        if (ParticipantsFactory(new ParticipantInfrastructure(new OperationRegistry(), new UiDispatchBoundary())) is null)
        {
            failure = new InitFailure("CompositionRoot", InitFailureKind.Fatal, "Missing registration: BackgroundParticipants");
            return false;
        }

        failure = null;
        return true;
    }

    /// <summary>Manual construction only. Precondition: <see cref="Validate"/> passed.</summary>
    public ApplicationHost Build(StartupTrace trace)
    {
        var log = LogSinkFactory() ?? throw new InvalidOperationException("Validate must run before Build.");
        var infra = new ParticipantInfrastructure(new OperationRegistry(), new UiDispatchBoundary());
        var participants = ParticipantsFactory(infra) ?? throw new InvalidOperationException("Validate must run before Build.");
        return new ApplicationHost(log, participants, trace, infra.Registry, infra.UiDispatch);
    }
}
