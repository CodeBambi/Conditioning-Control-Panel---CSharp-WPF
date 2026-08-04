namespace CcpClient.Desktop.Capabilities;

/// <summary>Stable machine-readable reason codes (runtime-capability-contract §1). Codes are additive; new codes land with their consumer row.</summary>
public static class CapabilityReasonCodes
{
    /// <summary>Registered but its probe has not run (contract §3 rule 4). Registration alone never yields Available.</summary>
    public const string NotProbed = "not-probed";

    /// <summary>Query for a name no capability registered under (contract §3 rule 4).</summary>
    public const string UnknownCapability = "unknown-capability";

    /// <summary>Linux process with neither WAYLAND_DISPLAY nor DISPLAY set (contract §8.1).</summary>
    public const string NoDisplayServer = "no-display-server";

    /// <summary>Platform outside the supported Windows/Linux set (contract §8.1).</summary>
    public const string UnsupportedPlatform = "unsupported-platform";

    /// <summary>A filesystem operation failed, or permission was denied (contract §8.2).</summary>
    public const string IoFailure = "io-failure";

    /// <summary>
    /// Flush-to-disk honoring cannot be verified in-process on a passthrough/network
    /// filesystem (contract §5 rule 2): an epistemic claim, never an observed failure.
    /// </summary>
    public const string DurabilityUnverified = "durability-unverified";

    /// <summary>The probe itself faulted; the detail carries the exception class (contract §3 rule 3).</summary>
    public const string ProbeFault = "probe-fault";

    /// <summary>An animation asset exists but cannot decode on this target (SP-015 AvatarTube demonstrator).</summary>
    public const string AssetUndecodable = "asset-undecodable";

    /// <summary>
    /// No credentials exist for a provider and none are invented (SP-033; admission §2
    /// rule 6 — the cloud provider's typed absence: inventory, never admission).
    /// </summary>
    public const string CredentialsAbsent = "credentials-absent";

    /// <summary>No reachable platform secret service (SP-033; e.g. WSL2 without a session D-Bus daemon). Never a plaintext fallback.</summary>
    public const string SecretServiceUnreachable = "secret-service-unreachable";

    /// <summary>A loopback provider endpoint did not answer its probe (connection refused or bounded probe timeout — SP-035).</summary>
    public const string HostUnreachable = "host-unreachable";
}

/// <summary>Structured reason: a stable code plus human diagnostic detail (contract §1).</summary>
public sealed record CapabilityReason(string Code, string Detail);

/// <summary>
/// Typed capability-availability state (runtime-capability-contract §1). Exactly one per
/// capability query. Row-5 vocabulary: operation outcomes stay in SP-004; the only bridge
/// is the probe outcome→state mapping (contract §6).
/// </summary>
public abstract record CapabilityState
{
    private CapabilityState() { }

    /// <summary>The probe ran in the current environment and its declared evidence confirmed support (contract §2). <paramref name="Detail"/> names what was confirmed.</summary>
    public sealed record Available(string Detail) : CapabilityState;

    /// <summary>Not supported here, or not yet proven (not-probed). Not an error.</summary>
    public sealed record Unavailable(CapabilityReason Reason) : CapabilityState;

    /// <summary>Part of the capability holds. <paramref name="SurvivingSemantics"/> names exactly what survives; <paramref name="Reason"/> names what does not and why (contract §5).</summary>
    public sealed record Degraded(string SurvivingSemantics, CapabilityReason Reason) : CapabilityState;

    /// <summary>Support exists but is gated on a user/OS grant the app does not hold.</summary>
    public sealed record PermissionRequired(CapabilityReason Reason) : CapabilityState;

    /// <summary>A named external dependency (binary, service, device) is absent.</summary>
    public sealed record DependencyMissing(string Dependency, CapabilityReason Reason) : CapabilityState;

    /// <summary>The probe itself faulted; the reason carries the exception class. Truthful state, never unhandled, never Available.</summary>
    public sealed record Faulted(CapabilityReason Reason) : CapabilityState;
}
