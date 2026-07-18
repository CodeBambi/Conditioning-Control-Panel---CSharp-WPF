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
public sealed class CompositionRoot
{
    /// <summary>Factory seam so tests can deliberately blank a registration (contract §4 test).</summary>
    public Func<ILogSink?> LogSinkFactory { get; init; } = () => new DebugLogSink();

    /// <summary>Each required participant is named here; deleting one is a compile error or a validation failure.</summary>
    public Func<IReadOnlyList<IBackgroundParticipant>?> ParticipantsFactory { get; init; } = DefaultParticipants;

    private static IReadOnlyList<IBackgroundParticipant> DefaultParticipants() =>
        [new HeartbeatParticipant()];

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

        if (ParticipantsFactory() is null)
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
        var participants = ParticipantsFactory() ?? throw new InvalidOperationException("Validate must run before Build.");
        return new ApplicationHost(log, participants, trace);
    }
}
