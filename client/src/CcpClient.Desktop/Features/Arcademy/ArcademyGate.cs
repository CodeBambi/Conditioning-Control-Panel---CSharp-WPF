using System.Globalization;
using CcpClient.Desktop.Entitlement;
using CcpClient.Desktop.Features.Dtrh;

namespace CcpClient.Desktop.Features.Arcademy;

/// <summary>
/// What the Arcademy's TIER bar decided — three cases, and the two refusals are DIFFERENT TYPES
/// rather than one refusal carrying a flag, for the reason <see cref="DtrhGateDecision"/> and
/// <see cref="Haptics.HapticGateDecision"/> both give: a flag is the thing a caller forgets to
/// read, and the UI is where "you are not a patron" and "I could not tell" die as one another.
/// </summary>
public abstract record ArcademyGateDecision
{
    private ArcademyGateDecision() { }

    /// <summary>An authority confirmed a pledge at or above the Lab bar. The class may open —
    /// as far as THIS gate is concerned; the door in front of it has already had its say.</summary>
    public sealed record Proceed(EntitlementTier Tier) : ArcademyGateDecision;

    /// <summary>An authority was asked and answered about THIS account: no pledge, or a pledge
    /// below the Lab bar. A real refusal, and the only kind WPF has.</summary>
    public sealed record RefusedNotEntitled(string Message) : ArcademyGateDecision;

    /// <summary>The entitlement could not be determined. Emphatically NOT a refusal of the user:
    /// no authority said anything about them.</summary>
    public sealed record RefusedUnverified(string ReasonCode, string Message) : ArcademyGateDecision;
}

/// <summary>
/// The Tier-2 bar in front of the Arcademy, as a pure function of the entitlement answer — the
/// THIRD door on this shape, after <see cref="DtrhGate"/> (tier 2) and
/// <see cref="Haptics.HapticGate"/> (tier 1).
///
/// <para><b>WPF parity.</b> <c>ArcademyHostService.Launch</c> asks
/// <c>TierGate.DemandLab(ProductName)</c> (<c>:146</c>) — the UNKEYED overload, so unlike DTRH
/// there is no daily-free term to port and nothing is missing from this gate:
/// <c>RequiresLab(featureName)</c> is <c>App.Patreon?.HasLabAccess == true</c> alone
/// (<c>Services/TierGate.cs:76-81</c>), which is tier 2. The Play card's lockband is computed
/// from the same bar and is decoration — the card is hit-test-transparent and the launch path
/// does the refusing (<c>MainWindow/MainWindow.PlayTab.cs:99-104</c>, "the lockband is still
/// computed because the card is only HIDDEN, not removed").</para>
///
/// <para><b>THE BAR IS SECOND, AND THE DOOR IS FIRST.</b> Upstream refuses on
/// <see cref="ArcademyDoor.Available"/> at <c>:136-141</c> and only then reaches this gate at
/// <c>:146</c>. <see cref="ArcademyLaunch.AttendAsync"/> keeps that order, which is why nothing in
/// a shipped build ever asks an authority about the Arcademy: the door answers first and the
/// entitlement is never read.</para>
///
/// <para><b>Where the port improves on WPF, deliberately.</b> WPF's <c>App.Patreon?.HasLabAccess
/// == true</c> renders a NULL Patreon service — "I could not tell" — as "you are not a patron".
/// This gate refuses too (an unknown entitlement may not open paid content) but through
/// <see cref="ArcademyGateDecision.RefusedUnverified"/>, which says which part could not be
/// determined. Recorded as a divergence rather than smuggled in as parity, on the D21 precedent
/// the two older doors already set.</para>
/// </summary>
public static class ArcademyGate
{
    /// <summary>The feature name upstream passes to the gate — <c>ArcademyHostService.ProductName</c>
    /// (<c>:38</c>, <c>:146</c>), which is also the Arcademy window's title.</summary>
    public const string FeatureName = ArcademyDoor.ProductName;

    /// <summary>The bar: tier 2, <c>HasLabAccess</c> (<c>Services/TierGate.cs:76-81</c> →
    /// <c>Services/Account/PatreonService.cs:172</c>). The same bar
    /// <see cref="DtrhGate.RequiredTier"/> carries, with no second grant term to port.</summary>
    public const EntitlementTier RequiredTier = EntitlementTier.Lab;

    /// <summary>The first line of every could-not-verify refusal, worded for THIS surface.</summary>
    public const string CouldNotVerifyHeader =
        "The Arcademy did not open: this build could not verify your entitlement.";

    /// <summary>The last line of every could-not-verify refusal. It exists so the user is told, in
    /// words, that nothing was decided ABOUT THEM.</summary>
    public const string CouldNotVerifyFooter =
        "Nothing was decided about your account here — the class stayed shut because the answer is "
        + "unknown, not because it was no.";

    /// <summary>WPF's refusal, with the Arcademy's name substituted
    /// (<c>Localization/Languages/en.json:4704</c>, <c>tiergate_denied_lab</c>, verbatim including
    /// its hyphen), plus the route out that <see cref="DtrhGate.UpgradeRoute"/> already words for
    /// a port with no tiers page of its own.</summary>
    public static string TierRefusalMessage { get; } =
        string.Format(CultureInfo.InvariantCulture, DtrhGate.DeniedTemplate, FeatureName)
        + "\n" + DtrhGate.UpgradeRoute;

    /// <summary>
    /// The whole gate. <see cref="EntitlementOutcome.Match"/> does not compile with a branch
    /// missing, which is what stops a later edit from quietly dropping the third case.
    /// </summary>
    public static ArcademyGateDecision Decide(EntitlementOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        return outcome.Match<ArcademyGateDecision>(
            // A confirmed pledge BELOW the bar is still an authority's answer about this account,
            // so it is the same refusal WPF gives a tier-1 patron — never "unknown". An UNDEFINED
            // tier is neither: `>=` on a raw enum would open the door for (EntitlementTier)99, so
            // the comparison never runs on a value this build does not define. The two older doors
            // take the same second guard, on the only direction where a mistake hands out paid
            // content.
            tier => !Enum.IsDefined(tier)
                ? new ArcademyGateDecision.RefusedUnverified(
                    EntitlementReasonCodes.TierAuthorityFault,
                    UnverifiedMessage(EntitlementReasonCodes.TierAuthorityFault))
                : tier >= RequiredTier
                    ? new ArcademyGateDecision.Proceed(tier)
                    : new ArcademyGateDecision.RefusedNotEntitled(TierRefusalMessage),
            _ => new ArcademyGateDecision.RefusedNotEntitled(TierRefusalMessage),
            reason => new ArcademyGateDecision.RefusedUnverified(reason.Code, UnverifiedMessage(reason.Code)));
    }

    /// <summary>
    /// The composed could-not-verify message for one reason code.
    ///
    /// <para>The middle sentence is <see cref="DtrhGate.Explain"/>'s and is REUSED rather than
    /// re-authored: those ten sentences describe the ENTITLEMENT READ (no shipping app, no stored
    /// login, an undecryptable blob, an absent authority) and say nothing about which door asked,
    /// so a second copy would be ten more strings to keep in step and one more place for a reason
    /// code to arrive unworded. What IS feature-specific — the header and the footer — is this
    /// file's own. The dependency direction is the one this folder already takes:
    /// <see cref="ArcademyParticipant"/> runs on DTRH's <see cref="LoopbackServer"/>.</para>
    /// </summary>
    public static string UnverifiedMessage(string reasonCode) =>
        CouldNotVerifyHeader + "\n" + DtrhGate.Explain(reasonCode) + "\n" + CouldNotVerifyFooter;
}
