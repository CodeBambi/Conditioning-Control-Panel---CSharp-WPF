using System.Globalization;
using CcpClient.Desktop.Entitlement;

namespace CcpClient.Desktop.Features.Dtrh;

/// <summary>
/// What the DTRH door decided. Three cases, and the two refusals are DIFFERENT TYPES rather
/// than one refusal carrying a flag — because a flag is the thing a caller forgets to read.
///
/// <para>SP-092 landed the typed <see cref="EntitlementOutcome"/> precisely so "you are not a
/// patron" and "I could not tell" could not collapse into each other. The UI is where that
/// distinction dies, because both look like "no" on a card. This type carries the distinction
/// all the way to the surface that renders it.</para>
/// </summary>
public abstract record DtrhGateDecision
{
    private DtrhGateDecision() { }

    /// <summary>An authority confirmed a pledge at or above the Lab bar. The hole opens.</summary>
    public sealed record Proceed(EntitlementTier Tier) : DtrhGateDecision;

    /// <summary>
    /// An authority was asked and answered about THIS account: no pledge, or a pledge below
    /// the Lab bar. A real refusal, and the only kind WPF has.
    /// </summary>
    public sealed record RefusedNotEntitled(string Message) : DtrhGateDecision;

    /// <summary>
    /// The entitlement could not be determined. Emphatically NOT a refusal of the user: no
    /// authority said anything about them. <paramref name="ReasonCode"/> is the stable
    /// classification (<see cref="EntitlementReasonCodes"/>), never rendered as a judgement.
    /// </summary>
    public sealed record RefusedUnverified(string ReasonCode, string Message) : DtrhGateDecision;
}

/// <summary>
/// The Tier-2 door in front of Down The Rabbit Hole, as a pure function of the entitlement
/// answer — no Avalonia, no window, no I/O — so all three branches are provable in the unit
/// suite and the page below is only a renderer of what this decided.
///
/// <para><b>WPF parity.</b> The gate is the FIRST statement of both handlers:
/// <c>if (!TierGate.DemandLab("Down the Rabbit Hole", "dtrh")) return;</c>
/// (<c>MainWindow/MainWindow.Lab.cs:228</c> for FALL IN, <c>:313</c> for Quick Drop). The
/// refusal names the tier and the upgrade route (<c>Services/TierGate.cs:128,133</c>;
/// <c>Localization/Languages/en.json:4704,4705</c>).</para>
///
/// <para><b>WPF GRANTS ON TWO CONDITIONS AND THIS GATE IMPLEMENTS ONE.</b> Read the whole
/// expression, not its first line: <c>TierGate.RequiresLab</c> is
/// <c>App.Patreon?.HasLabAccess == true || App.DailyFree?.IsFreeToday(dailyKey) == true</c>
/// (<c>Services/TierGate.cs:90-91</c>), and both DTRH call sites pass the KEYED overload with
/// <c>"dtrh"</c>. <c>MainWindow.Lab.cs:225-227</c> says what that buys: "on a server-declared
/// DtRH drop day (DailyFreeService, off-pool override) the door opens for everyone".
/// <list type="bullet">
///   <item>TERM 1, the tier bar — <c>HasLabAccess</c>, tier 2. <b>Ported</b>, as
///   <see cref="RequiredTier"/>.</item>
///   <item>TERM 2, the drop-day grant — the server naming <c>"dtrh"</c> as today's free feature.
///   <b>NOT ported</b>, and it cannot be: the port has no <c>DailyFreeService</c> and no
///   <c>/config/daily-feature</c> fetch, and DTRH reaches that list only through a server
///   override, never the local rotation (<c>Services/DailyFreeService.cs:16-18,143-144</c>).</item>
/// </list>
/// The user-visible consequence — a free user who would fall in on a WPF drop day is refused
/// here — is recorded as a divergence with its close condition
/// (wpf-surface-reachability.md §10 D24), because the port has nothing to wire and inventing a
/// second grant condition out of nothing would be worse than the gap.</para>
///
/// <para><b>Where the port improves on WPF, deliberately.</b> WPF has no third answer: with a
/// null Patreon service <c>RequiresLab</c> evaluates <c>App.Patreon?.HasLabAccess == true</c>
/// to false and refuses (<c>Services/TierGate.cs:88-94</c>) — it renders "I could not tell" as
/// "you are not a patron". The port refuses too (it must: an unknown entitlement may not open
/// paid content) but it refuses with a DIFFERENT message that says which part could not be
/// determined. That is a divergence, recorded as an improvement rather than smuggled in as
/// parity (wpf-surface-reachability.md §10 D21).</para>
///
/// <para><b>And this is not the rare branch.</b> This build's authority is
/// <see cref="UnconfiguredTierSource"/>, so every real user today gets
/// <c>Unavailable(tier-authority-absent)</c> or an earlier <c>Unavailable</c> from the read.
/// <see cref="DtrhGateDecision.RefusedUnverified"/> is the branch the product actually runs.</para>
/// </summary>
public static class DtrhGate
{
    /// <summary>The feature name WPF passes to the gate (<c>MainWindow.Lab.cs:228,313</c>).</summary>
    public const string FeatureName = "Down the Rabbit Hole";

    /// <summary>
    /// The tier bar, and the ONLY one of <c>RequiresLab</c>'s two grant terms the port
    /// implements: <c>App.Patreon?.HasLabAccess == true</c>, i.e. tier 2
    /// (<c>Services/TierGate.cs:90</c>). The second term — <c>IsFreeToday("dtrh")</c> on a
    /// server-declared drop day (<c>:91</c>) — has nothing to bind to in this port; see the
    /// class remarks and wpf-surface-reachability.md §10 D24.
    /// </summary>
    public const EntitlementTier RequiredTier = EntitlementTier.Lab;

    /// <summary>The tier livery's user-facing wording for tier 2+, baked into the badge art
    /// rather than localized (<c>Controls/TierBadge.cs:21</c>; the DTRH hero wears it at
    /// <c>Views/Tabs/PlayTabView.xaml:409-411</c>). NOT the literal string "TIER 2".</summary>
    public const string TierBadgeWording = "PRIME SUBJECT";

    /// <summary><c>en.json:4704</c>, verbatim including its hyphen.</summary>
    public const string DeniedTemplate = "{0} is a Tier 2 perk - upgrade your pledge to unlock it.";

    /// <summary>
    /// WPF's refusal toast carries a "See tiers" action opening App Info &amp; Data
    /// (<c>Services/TierGate.cs:133</c>; <c>en.json:4705</c>). The port has no such page, and a
    /// "See tiers" button with nowhere to go is a dead control — so the route is named in
    /// words and the gap is admitted (divergence §10 D17).
    /// </summary>
    public const string UpgradeRoute =
        "See tiers in the shipping Conditioning Control Panel app (App Info & Data) — this port has no tiers page of its own yet.";

    /// <summary>The first line of every could-not-verify refusal.</summary>
    public const string CouldNotVerifyHeader =
        "Down the Rabbit Hole did not open: this build could not verify your entitlement.";

    /// <summary>The last line of every could-not-verify refusal. It exists so the user is told,
    /// in words, that nothing was decided ABOUT THEM.</summary>
    public const string CouldNotVerifyFooter =
        "Nothing was decided about your account here — the door stayed shut because the answer is unknown, not because it was no.";

    /// <summary>Marker for a reason code this surface has no authored wording for. A test pins
    /// that no shipped code reaches it; it exists so an unknown code still renders honestly
    /// instead of falling into the refusal wording.</summary>
    public const string UnwordedReasonMarker = "no authored explanation exists in this build for reason code";

    /// <summary>The refusal WPF shows, with the feature name substituted.</summary>
    public static string TierRefusalMessage { get; } =
        string.Format(CultureInfo.InvariantCulture, DeniedTemplate, FeatureName) + "\n" + UpgradeRoute;

    /// <summary>
    /// The whole gate. <see cref="EntitlementOutcome.Match"/> does not compile with a branch
    /// missing, which is what stops a later edit from quietly dropping the third case.
    /// </summary>
    public static DtrhGateDecision Decide(EntitlementOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        return outcome.Match<DtrhGateDecision>(
            // A confirmed pledge BELOW the bar is still an authority's answer about this
            // account, so it is the same refusal WPF gives a tier-1 patron — never "unknown".
            // An UNDEFINED tier is neither: `>=` on a raw enum would open the door for
            // (EntitlementTier)99, so the comparison never runs on a value this build does not
            // define. HostLoginEntitlement closes the same hole at the source (SP-094); this is
            // the second guard, on the only direction where a mistake hands out paid content.
            tier => !Enum.IsDefined(tier)
                ? new DtrhGateDecision.RefusedUnverified(
                    EntitlementReasonCodes.TierAuthorityFault,
                    UnverifiedMessage(EntitlementReasonCodes.TierAuthorityFault))
                : tier >= RequiredTier
                    ? new DtrhGateDecision.Proceed(tier)
                    : new DtrhGateDecision.RefusedNotEntitled(TierRefusalMessage),
            _ => new DtrhGateDecision.RefusedNotEntitled(TierRefusalMessage),
            reason => new DtrhGateDecision.RefusedUnverified(reason.Code, UnverifiedMessage(reason.Code)));
    }

    /// <summary>The composed could-not-verify message for one reason code.</summary>
    public static string UnverifiedMessage(string reasonCode) =>
        CouldNotVerifyHeader + "\n" + Explain(reasonCode) + "\n" + CouldNotVerifyFooter;

    /// <summary>
    /// One authored sentence per <see cref="EntitlementReasonCodes"/> value. Each says which
    /// part could not be determined and, where the user can act, what would settle it. None of
    /// them may carry the tier-refusal wording — <c>DtrhGateTests</c> holds that mechanically,
    /// so a reason code added later cannot silently inherit "you are not a patron".
    /// </summary>
    public static string Explain(string reasonCode) => reasonCode switch
    {
        EntitlementReasonCodes.HostAppDataAbsent =>
            "The shipping Conditioning Control Panel app is not installed for this Windows user, so there is no login here to read.",
        EntitlementReasonCodes.HostTokenAbsent =>
            "The shipping Conditioning Control Panel app is installed but has stored no login. Sign in there and try again.",
        EntitlementReasonCodes.HostTokenEmpty =>
            "The stored login decrypted to an empty value, so there was nothing to present to an authority.",
        EntitlementReasonCodes.HostTokenUndecryptable =>
            "The stored login could not be decrypted: it is corrupt, was written by a different Windows user, or the shipping app rotated how it protects it.",
        EntitlementReasonCodes.HostReadFailed =>
            "The stored login could not be read from disk (I/O, permissions, or the file was locked).",
        EntitlementReasonCodes.UnsupportedPlatform =>
            "This operating system has no DPAPI, so the shipping app's login cannot be read here at all.",
        EntitlementReasonCodes.TierAuthorityAbsent =>
            "This build has no entitlement authority configured, so your tier cannot be looked up. That is a gap in the port, not a finding about your account.",
        EntitlementReasonCodes.TierAuthorityUnreachable =>
            "The entitlement authority could not be reached (offline, DNS, or a timeout).",
        EntitlementReasonCodes.TierAuthorityRejected =>
            "The entitlement authority rejected the stored login as expired or rotated, so it never reached the entitlement question at all.",
        EntitlementReasonCodes.TierAuthorityFault =>
            "The entitlement lookup itself failed.",
        _ => UnwordedReasonMarker + " '" + reasonCode + "'.",
    };
}
