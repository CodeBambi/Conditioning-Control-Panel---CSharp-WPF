using CcpClient.Desktop.Capabilities;

namespace CcpClient.Desktop.Entitlement;

/// <summary>
/// The entitlement capability: read the shipping app's existing login, ask an authority what
/// it entitles, and answer with one of three typed outcomes — never a fourth thing, and never
/// the wrong one of the three.
///
/// <para>WHY THIS EXISTS. WPF gates Down The Rabbit Hole at tier 2 through
/// <c>TierGate.DemandLab</c> and fails CLOSED when the Patreon service is null
/// (Services/TierGate.cs:47-48,88-94). The port has no Patreon service at all, so faithful
/// parity would open the door for nobody, ungating hands out paid content, and an
/// always-allowed stub is the fake-available shape the truthful-capability contract bans. The
/// owner supplied the fourth answer by installing the shipping app and logging in: this reads
/// THAT login (DPAPI, CurrentUser scope — Services/Auth/SecureAuthTokenStore.cs:39,65).</para>
///
/// <para>THE INVARIANT. Exactly one input produces
/// <see cref="EntitlementOutcome.NotEntitled"/>: an authority that answered
/// <see cref="TierLookupStatus.NoEntitlement"/>. Every other path — no shipping app, no token
/// file, an empty or undecryptable blob, an I/O failure, a platform without DPAPI, an absent,
/// unreachable, rejecting or faulting authority — produces
/// <see cref="EntitlementOutcome.Unavailable"/> carrying the reason. "I could not tell" is
/// never reported as "you are not a patron".</para>
///
/// <para>THE TOKEN. Read, used, dropped: the read result is disposed in a <c>finally</c> on
/// every path, the value is never logged, never persisted, never copied into the port's own
/// store, and never placed in a message or an exception. Logging is outcome CLASS plus reason
/// CODE, the same discipline as the port's route-class request logging
/// (Features/Dtrh/LoopbackServer.cs:505).</para>
///
/// <para>THIS IS A BRIDGE, NOT A DESTINATION. It presumes the shipping WPF app is installed
/// and logged in as the same Windows user. A port-only user has no token and gets
/// <c>Unavailable(host-app-data-absent)</c> — honest, and honestly not an entitlement
/// system.</para>
/// </summary>
public sealed class HostLoginEntitlement
{
    /// <summary>The name this capability's readiness would be registered under. Registration is
    /// a LATER packet's job (this one wires nothing); the constant lives here so the name is not
    /// invented twice.</summary>
    public const string CapabilityName = "host-login-entitlement";

    private readonly IHostAuthTokenReader _reader;
    private readonly IEntitlementTierSource _tierSource;
    private readonly Action<string>? _log;

    /// <param name="reader">The platform read seam.</param>
    /// <param name="tierSource">The authority; defaults to <see cref="UnconfiguredTierSource"/>.</param>
    /// <param name="log">Outcome-class sink. Receives classifications only — never values.</param>
    public HostLoginEntitlement(
        IHostAuthTokenReader reader,
        IEntitlementTierSource? tierSource = null,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        _reader = reader;
        _tierSource = tierSource ?? new UnconfiguredTierSource();
        _log = log;
    }

    /// <summary>The capability over the OS this process is running on.</summary>
    public static HostLoginEntitlement ForCurrentPlatform(
        IEntitlementTierSource? tierSource = null,
        Action<string>? log = null,
        string? shippingAppDataDirectory = null) =>
        new(HostTokenReaders.ForCurrentPlatform(shippingAppDataDirectory), tierSource, log);

    /// <summary>
    /// The three-state answer. Never throws for a determinable failure — a failure IS one of
    /// the three answers — but a cancellation requested by the caller propagates, because
    /// swallowing it would turn "you stopped me" into "I could not tell", which is its own
    /// small lie.
    /// </summary>
    public async Task<EntitlementOutcome> ResolveAsync(CancellationToken cancellationToken)
    {
        using var read = _reader.Read();
        var outcome = read.Status switch
        {
            HostTokenReadStatus.NoDataDirectory =>
                Unavailable(EntitlementReasonCodes.HostAppDataAbsent, read.Detail),
            HostTokenReadStatus.NoTokenFile =>
                Unavailable(EntitlementReasonCodes.HostTokenAbsent, read.Detail),
            HostTokenReadStatus.EmptyToken =>
                Unavailable(EntitlementReasonCodes.HostTokenEmpty, read.Detail),
            HostTokenReadStatus.DecryptFailed =>
                Unavailable(EntitlementReasonCodes.HostTokenUndecryptable, read.Detail),
            HostTokenReadStatus.ReadFailed =>
                Unavailable(EntitlementReasonCodes.HostReadFailed, read.Detail),
            HostTokenReadStatus.UnsupportedPlatform =>
                Unavailable(EntitlementReasonCodes.UnsupportedPlatform, read.Detail),
            HostTokenReadStatus.Found =>
                await AskAuthorityAsync(read.Token!, cancellationToken).ConfigureAwait(false),
            _ => Unavailable(EntitlementReasonCodes.TierAuthorityFault,
                "the read seam returned a status this capability does not know"),
        };

        _log?.Invoke("entitlement: " + outcome.Describe());
        return outcome;
    }

    /// <summary>
    /// The capability probe: can this environment read the shipping app's login at all? It
    /// deliberately answers a NARROWER question than <see cref="ResolveAsync"/> and never
    /// claims a tier — a readable login with no authority behind it is
    /// <see cref="CapabilityState.Degraded"/>, naming exactly what survives, not
    /// <see cref="CapabilityState.Available"/>.
    /// </summary>
    public Task<CapabilityState> ProbeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var read = _reader.Read();
        CapabilityState state = read.Status switch
        {
            HostTokenReadStatus.Found when _tierSource is UnconfiguredTierSource =>
                new CapabilityState.Degraded(
                    "the shipping app's login is readable; the tier is unknown",
                    new CapabilityReason(EntitlementReasonCodes.TierAuthorityAbsent,
                        "no entitlement authority is configured in this build")),
            HostTokenReadStatus.Found =>
                new CapabilityState.Available("the shipping app's login is readable (DPAPI, CurrentUser scope)"),
            HostTokenReadStatus.NoDataDirectory =>
                new CapabilityState.DependencyMissing("ConditioningControlPanel (the shipping WPF app)",
                    new CapabilityReason(EntitlementReasonCodes.HostAppDataAbsent, read.Detail)),
            HostTokenReadStatus.NoTokenFile =>
                new CapabilityState.Unavailable(new CapabilityReason(EntitlementReasonCodes.HostTokenAbsent, read.Detail)),
            HostTokenReadStatus.EmptyToken =>
                new CapabilityState.Unavailable(new CapabilityReason(EntitlementReasonCodes.HostTokenEmpty, read.Detail)),
            HostTokenReadStatus.DecryptFailed =>
                new CapabilityState.Unavailable(new CapabilityReason(EntitlementReasonCodes.HostTokenUndecryptable, read.Detail)),
            HostTokenReadStatus.ReadFailed =>
                new CapabilityState.Faulted(new CapabilityReason(EntitlementReasonCodes.HostReadFailed, read.Detail)),
            HostTokenReadStatus.UnsupportedPlatform =>
                new CapabilityState.Unavailable(new CapabilityReason(EntitlementReasonCodes.UnsupportedPlatform, read.Detail)),
            _ => new CapabilityState.Faulted(new CapabilityReason(CapabilityReasonCodes.ProbeFault,
                "the read seam returned a status this capability does not know")),
        };

        return Task.FromResult(state);
    }

    private async Task<EntitlementOutcome> AskAuthorityAsync(HostAuthToken token, CancellationToken cancellationToken)
    {
        TierLookup lookup;
        try
        {
            lookup = await _tierSource.LookupAsync(token, cancellationToken).ConfigureAwait(false)
                     ?? TierLookup.Faulted("NullResult");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Type name only: an authority's exception message can carry a URL, a header, or
            // the bearer itself, and this string is destined for a log.
            lookup = TierLookup.Faulted(ex.GetType().Name);
        }

        return lookup.Status switch
        {
            // The ONLY producer of a refusal in this namespace.
            TierLookupStatus.NoEntitlement => new EntitlementOutcome.NotEntitled(lookup.Detail),
            TierLookupStatus.Entitled when lookup.Tier is { } tier && Enum.IsDefined(tier) =>
                new EntitlementOutcome.Entitled(tier, lookup.Detail),
            TierLookupStatus.Entitled when lookup.Tier is not null =>
                // THE ONE HOLE in this capability that leaned toward GRANTING. The
                // missing-tier arm below was already closed toward refusal, but an UNDEFINED
                // enum value slipped past it: (EntitlementTier)0 or (EntitlementTier)99 from a
                // rogue or malformed authority response rendered as entitled(0) / entitled(99),
                // and a consumer comparing `tier >= Lab` would hand out paid content on the
                // second. A tier this capability does not define is a tier the authority did
                // not name in a vocabulary this build understands — unknown, never granted.
                Unavailable(EntitlementReasonCodes.TierAuthorityFault,
                    "the entitlement authority named a tier this build does not define"),
            TierLookupStatus.Entitled =>
                // "Entitled" with no tier names nothing; inventing one would be inventing a
                // tier when the server did not give one.
                Unavailable(EntitlementReasonCodes.TierAuthorityFault,
                    "the entitlement authority confirmed a pledge without naming a tier"),
            TierLookupStatus.NotConfigured =>
                Unavailable(EntitlementReasonCodes.TierAuthorityAbsent, lookup.Detail),
            TierLookupStatus.Unreachable =>
                Unavailable(EntitlementReasonCodes.TierAuthorityUnreachable, lookup.Detail),
            TierLookupStatus.Rejected =>
                Unavailable(EntitlementReasonCodes.TierAuthorityRejected, lookup.Detail),
            TierLookupStatus.Faulted =>
                Unavailable(EntitlementReasonCodes.TierAuthorityFault, lookup.Detail),
            _ => Unavailable(EntitlementReasonCodes.TierAuthorityFault,
                "the entitlement authority returned a status this capability does not know"),
        };
    }

    private static EntitlementOutcome Unavailable(string code, string detail) =>
        new EntitlementOutcome.Unavailable(new EntitlementReason(code, detail));
}
