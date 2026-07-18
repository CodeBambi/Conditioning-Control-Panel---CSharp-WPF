namespace CcpClient.Desktop.Lifecycle;

/// <summary>Typed initialization-failure kinds (startup-shutdown-contract §2).</summary>
public enum InitFailureKind
{
    Fatal,
    Recoverable,
    Degraded,
}

/// <summary>An expected initialization failure: phase, kind, human reason, optional causing exception.</summary>
public sealed record InitFailure(string Phase, InitFailureKind Kind, string Reason, Exception? Exception = null);

/// <summary>
/// Result of a startup phase or of the whole startup sequence. Expected failures are
/// typed values, never exceptions-as-control-flow (contract §2).
/// </summary>
public abstract record StartupOutcome
{
    private StartupOutcome() { }

    public sealed record Success : StartupOutcome
    {
        public static readonly Success Instance = new();
    }

    public sealed record Cancelled : StartupOutcome
    {
        public static readonly Cancelled Instance = new();
    }

    public sealed record Failed(InitFailure Failure) : StartupOutcome;
}
