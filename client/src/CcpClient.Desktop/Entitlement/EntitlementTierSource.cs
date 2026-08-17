namespace CcpClient.Desktop.Entitlement;

/// <summary>
/// What an entitlement authority answered. Five of the six members mean "no answer": only
/// <see cref="NoEntitlement"/> is an authority actually saying the account does not pay, and
/// it is the only status in the whole namespace permitted to produce
/// <see cref="EntitlementOutcome.NotEntitled"/>.
/// </summary>
public enum TierLookupStatus
{
    /// <summary>The authority confirmed a pledge, and named the tier.</summary>
    Entitled,

    /// <summary>The authority answered: this account has no pledge. A real refusal.</summary>
    NoEntitlement,

    /// <summary>No authority is configured in this build. Unknown, not refused.</summary>
    NotConfigured,

    /// <summary>The authority could not be reached (offline, DNS, timeout). Unknown.</summary>
    Unreachable,

    /// <summary>
    /// The authority rejected the bearer itself — an expired or rotated login. It never
    /// reached the entitlement question, so it did not answer it. The shipping app treats
    /// exactly this case as recoverable rather than terminal: a 401 tries a refresh before
    /// anything is dropped (Services/Account/PatreonService.cs:541-555).
    /// </summary>
    Rejected,

    /// <summary>The lookup threw.</summary>
    Faulted,
}

/// <summary>
/// One authority answer. <paramref name="Detail"/> is authored classification text: never a
/// response body, never an exception message, never anything derived from the bearer.
/// </summary>
public sealed record TierLookup(TierLookupStatus Status, EntitlementTier? Tier, string Detail)
{
    public static TierLookup Entitled(EntitlementTier tier) =>
        new(TierLookupStatus.Entitled, tier, "the entitlement authority confirmed a pledge");

    public static TierLookup NoEntitlement() =>
        new(TierLookupStatus.NoEntitlement, null, "the entitlement authority answered: this account has no pledge");

    public static TierLookup NotConfigured() =>
        new(TierLookupStatus.NotConfigured, null, "no entitlement authority is configured in this build");

    public static TierLookup Unreachable(string classification) =>
        new(TierLookupStatus.Unreachable, null, "the entitlement authority could not be reached: " + classification);

    public static TierLookup Rejected() =>
        new(TierLookupStatus.Rejected, null, "the entitlement authority rejected the stored login (expired or rotated)");

    /// <param name="failureClass">An exception TYPE name, never a message.</param>
    public static TierLookup Faulted(string failureClass) =>
        new(TierLookupStatus.Faulted, null, "the entitlement lookup failed: " + failureClass);
}

/// <summary>
/// The authority seam. The tier is a server answer, not a property of the token
/// (Services/TierGate.cs:88-94 resolves it through <c>App.Patreon?.HasLabAccess</c> or the
/// server naming today's free key), so the capability asks something and never infers.
/// </summary>
public interface IEntitlementTierSource
{
    /// <param name="token">
    /// Borrowed for the duration of the call only. Implementations must not store it, log it,
    /// or copy it; the caller disposes it as soon as this returns.
    /// </param>
    Task<TierLookup> LookupAsync(HostAuthToken token, CancellationToken cancellationToken);
}

/// <summary>
/// This build's default authority: there isn't one.
///
/// <para>Deliberate, and the alternative was worse. Every WPF site that presents this bearer
/// does so as an <c>X-Auth-Token</c> header on a POST that also carries a <c>unified_id</c>
/// body (Services/Account/V2AuthService.cs:558,584,610,627-632), and <c>/patreon/validate</c>
/// authenticates with the Patreon OAuth bearer from a DIFFERENT store, not with this file
/// (Services/Account/PatreonService.cs:531-537). There is no verified "what tier is this
/// bearer" endpoint to port, and guessing one would be inventing an API.</para>
///
/// <para>So the honest answer is <see cref="TierLookupStatus.NotConfigured"/> — which the
/// capability renders as <c>Unavailable(tier-authority-absent)</c>. It never renders as
/// "not a patron", and the consuming packet supplies a real authority.</para>
/// </summary>
public sealed class UnconfiguredTierSource : IEntitlementTierSource
{
    public Task<TierLookup> LookupAsync(HostAuthToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(TierLookup.NotConfigured());
    }
}
