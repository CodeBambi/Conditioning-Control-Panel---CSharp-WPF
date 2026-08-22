using CcpClient.Desktop.Capabilities;

namespace CcpClient.Desktop.Entitlement;

// The typed entitlement outcome.
//
// THE ONE RULE THIS FILE EXISTS TO ENFORCE: "you are not a patron" and "I could not tell"
// are different answers and must never collapse into each other. WPF's own store already
// makes the confusion easy — SecureAuthTokenStore.Retrieve() returns null both when no
// token was ever stored and when DPAPI refuses the blob (Services/Auth/SecureAuthTokenStore.cs:58-76),
// and a caller reading that null as "no entitlement" would show a paying user their paid
// features vanishing with no explanation and no way to tell a bug from a policy.
//
// So: <see cref="EntitlementOutcome.NotEntitled"/> has exactly ONE producer in this
// namespace — an entitlement authority that explicitly answered "no pledge". Everything
// else that could not determine the answer is <see cref="EntitlementOutcome.Unavailable"/>
// with a reason that names WHICH thing could not be determined.

/// <summary>
/// Stable machine-readable reasons an entitlement answer could not be determined. Codes are
/// classifications only: never a path, never a username, never a token or any part of one.
/// The shared platform vocabulary (<see cref="CapabilityReasonCodes"/>) is reused where a
/// code already exists so the two vocabularies cannot drift.
/// </summary>
public static class EntitlementReasonCodes
{
    /// <summary>The shipping app's data directory does not exist — it was never installed here.</summary>
    public const string HostAppDataAbsent = "host-app-data-absent";

    /// <summary>The data directory exists but holds no token file — installed, never logged in,
    /// or logged out (WPF deletes the file on Clear(), SecureAuthTokenStore.cs:98-122).</summary>
    public const string HostTokenAbsent = "host-token-absent";

    /// <summary>The stored blob decrypted to nothing. A login this shape cannot be presented to
    /// any authority, and it is emphatically not evidence of a missing pledge.</summary>
    public const string HostTokenEmpty = "host-token-empty";

    /// <summary>
    /// DPAPI refused the blob: corrupt, written by a DIFFERENT Windows user, or upstream
    /// rotated the entropy literal (`..._v1` → `_v2`). WPF classifies exactly this case as
    /// "may be corrupted or from different user" (SecureAuthTokenStore.cs:71-74). It is the
    /// loudest "I could not tell" in the set and the one most easily mistaken for a refusal.
    /// </summary>
    public const string HostTokenUndecryptable = "host-token-undecryptable";

    /// <summary>Reading the stored blob failed (I/O, permission, locked file).</summary>
    public const string HostReadFailed = CapabilityReasonCodes.IoFailure;

    /// <summary>This OS has no DPAPI, so the shipping app's login cannot be read here at all
    /// (the honest Linux answer — mirrors how ISecretStore already reports there).</summary>
    public const string UnsupportedPlatform = CapabilityReasonCodes.UnsupportedPlatform;

    /// <summary>A login was read, but this build has no entitlement authority configured, so
    /// the tier is unknown. The tier is never in the token (TierGate.cs:88-94).</summary>
    public const string TierAuthorityAbsent = "tier-authority-absent";

    /// <summary>A login was read and the authority could not be reached (offline, DNS, timeout).</summary>
    public const string TierAuthorityUnreachable = "tier-authority-unreachable";

    /// <summary>The authority refused the bearer itself (expired or rotated login). It did not
    /// answer the entitlement question, so the entitlement is unknown — not denied.</summary>
    public const string TierAuthorityRejected = "tier-authority-rejected";

    /// <summary>The lookup threw. The detail carries the exception TYPE name and nothing else.</summary>
    public const string TierAuthorityFault = "tier-authority-fault";
}

/// <summary>
/// Structured reason: a stable code plus authored human detail. Same shape as
/// <see cref="CapabilityReason"/> so the two read alike in a log. The detail is written by
/// this assembly and is never interpolated from a token, a path, a server body or an
/// exception message.
/// </summary>
public sealed record EntitlementReason(string Code, string Detail);

/// <summary>
/// The entitlement bars the shipping app actually has. "Premium" in the WPF codebase means
/// TIER 1 (<c>PatreonService.HasPremiumAccess</c>, Services/Account/PatreonService.cs:134);
/// the Lab bar — the one Down The Rabbit Hole is gated on — is TIER 2
/// (<c>HasLabAccess</c>, :172, consumed by TierGate.RequiresLab, Services/TierGate.cs:88-94).
/// There is deliberately no <c>None</c> member: "no tier" is the
/// <see cref="EntitlementOutcome.NotEntitled"/> outcome, not a tier value that could be
/// compared into existence.
/// </summary>
public enum EntitlementTier
{
    /// <summary>Tier 1+ ("premium" in WPF's naming).</summary>
    Supporter = 1,

    /// <summary>Tier 2+ ("Lab"): the DTRH bar.</summary>
    Lab = 2,
}

/// <summary>
/// Exactly one of three answers, and never a bool.
///
/// <para>There is deliberately NO <c>IsAllowed</c>/<c>Allows(tier)</c> convenience on this
/// type. Any such accessor has to map <see cref="Unavailable"/> onto true or false, and
/// mapping it onto false recreates the exact conflation this type exists to prevent — one
/// layer up, where it is invisible. Consumers use <see cref="Match{T}"/>, which does not
/// compile with a branch missing.</para>
/// </summary>
public abstract record EntitlementOutcome
{
    private EntitlementOutcome() { }

    /// <summary>An authority confirmed a pledge at <paramref name="Tier"/>.</summary>
    public sealed record Entitled(EntitlementTier Tier, string Detail) : EntitlementOutcome;

    /// <summary>
    /// An authority was asked and explicitly answered "no pledge". THE ONLY legitimate
    /// producer of this case is <see cref="TierLookupStatus.NoEntitlement"/>; a failure to
    /// determine the answer is <see cref="Unavailable"/>, never this.
    /// </summary>
    public sealed record NotEntitled(string Detail) : EntitlementOutcome;

    /// <summary>Could not tell, and this is which part could not be told. Not a refusal.</summary>
    public sealed record Unavailable(EntitlementReason Reason) : EntitlementOutcome;

    /// <summary>
    /// Total, branch-complete consumption. Every caller must say what it does when the answer
    /// is unknown, in the same expression where it says what it does on a refusal.
    /// </summary>
    public T Match<T>(
        Func<EntitlementTier, T> entitled,
        Func<string, T> notEntitled,
        Func<EntitlementReason, T> unavailable)
    {
        ArgumentNullException.ThrowIfNull(entitled);
        ArgumentNullException.ThrowIfNull(notEntitled);
        ArgumentNullException.ThrowIfNull(unavailable);
        return this switch
        {
            Entitled e => entitled(e.Tier),
            NotEntitled n => notEntitled(n.Detail),
            Unavailable u => unavailable(u.Reason),
            _ => throw new InvalidOperationException("unreachable: EntitlementOutcome is a closed hierarchy"),
        };
    }

    /// <summary>
    /// The log-safe rendering: outcome class plus, for the unknown case, the reason CODE.
    /// Never the detail, never anything derived from the token. Matches the port's
    /// route-classes-only logging precedent (Features/Dtrh/LoopbackServer.cs:505).
    /// </summary>
    public string Describe() => Match(
        tier => "entitled(" + tier.ToString().ToLowerInvariant() + ")",
        _ => "not-entitled",
        reason => "unavailable(" + reason.Code + ")");
}
