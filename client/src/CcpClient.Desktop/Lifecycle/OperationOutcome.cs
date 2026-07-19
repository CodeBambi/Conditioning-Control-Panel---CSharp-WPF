namespace CcpClient.Desktop.Lifecycle;

/// <summary>
/// Typed terminal outcome of an owned operation (async-lifecycle-fault-contract §2).
/// Expected failures are typed values, never exceptions-as-control-flow. The failure
/// kinds reuse the SP-003 taxonomy (<see cref="InitFailureKind"/>) — Recoverable and
/// Degraded are activated here as operation-outcome classifications supplied
/// per-operation by the owner. Capability availability/probes are row 5, not this type.
/// </summary>
public abstract record OperationOutcome
{
    private OperationOutcome() { }

    /// <summary>The operation finished its work.</summary>
    public sealed record Completed : OperationOutcome
    {
        public static readonly Completed Instance = new();
    }

    /// <summary>The owner's generation was cancelled; the operation observed the token. Not an error.</summary>
    public sealed record Cancelled : OperationOutcome
    {
        public static readonly Cancelled Instance = new();
    }

    /// <summary>
    /// The operation faulted. <paramref name="Kind"/> comes from the owner-supplied
    /// classifier (default: <see cref="InitFailureKind.Fatal"/>).
    /// </summary>
    public sealed record Failed(InitFailureKind Kind, string Reason, Exception? Exception = null) : OperationOutcome;
}
