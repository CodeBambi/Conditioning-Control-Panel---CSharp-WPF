using CcpClient.Desktop.Entitlement;

namespace CcpClient.Desktop.Haptics;

/// <summary>
/// What the haptics premium gate decided. Three cases, and the two refusals are DIFFERENT TYPES
/// rather than one refusal carrying a flag — the same reason
/// <c>Features/Dtrh/DtrhGate.cs</c> does it: a flag is the thing a caller forgets to read, and the
/// UI is where the distinction dies.
/// </summary>
public abstract record HapticGateDecision
{
    private HapticGateDecision() { }

    /// <summary>An authority confirmed a pledge at or above the Supporter bar. The box may be
    /// ticked and output may be driven.</summary>
    public sealed record Allow(EntitlementTier Tier) : HapticGateDecision;

    /// <summary>An authority was asked and answered about THIS account: no pledge. A real refusal,
    /// and the only kind WPF has.</summary>
    public sealed record RefusedNotEntitled(string Message) : HapticGateDecision;

    /// <summary>
    /// The entitlement could not be determined. <b>Emphatically not a refusal of the user</b>: no
    /// authority said anything about them. <paramref name="ReasonCode"/> is the stable
    /// classification (<see cref="EntitlementReasonCodes"/>), never rendered as a judgement.
    /// </summary>
    public sealed record RefusedUnverified(string ReasonCode, string Message) : HapticGateDecision;
}

/// <summary>
/// The Tier-1 door in front of haptics, as a pure function of the entitlement answer — no Avalonia,
/// no window, no I/O — so all three branches are provable in the unit suite and the panel is only a
/// renderer of what this decided.
///
/// <para><b>WPF parity, and there are TWO gates upstream rather than one.</b> The one this packet
/// ports is the enable checkbox: <c>if (isEnabled &amp;&amp; App.Patreon?.HasPremiumAccess != true)</c>
/// at <c>:489</c> → revert the box (<c>:491</c>), show <c>msg_haptic_feedback_patreon_only</c> in a
/// <c>title_patreon_feature</c> box (<c>:492-496</c>), and <c>return</c> (<c>:497</c>), so the write
/// at <c>:500</c> is unreachable (<c>MainWindow/MainWindow.Haptics.cs:484-502</c>). The second is deeper and is the reason the
/// gate also lives on the output path: <c>Services/Haptics/Core/HapticMixer.cs:191-204</c> evaluates
/// the same bar once per 10 Hz tick <i>"rather than in every consumer"</i>, and its open→closed
/// transition <b>drops everything and stops the toys once</b> (<c>:253-262</c>).</para>
///
/// <para><b>THE BAR IS TIER 1.</b> <c>HasPremiumAccess</c> is
/// <c>CurrentTier &gt;= PatreonTier.Level1 || IsWhitelisted || …</c>
/// (<c>Services/Account/PatreonService.cs:134</c>), which is
/// <see cref="EntitlementTier.Supporter"/>. <c>DtrhGate.RequiredTier</c> is
/// <see cref="EntitlementTier.Lab"/>. Same folder, same three answers, a different bar — which is
/// what consuming <c>Entitlement/**</c> unchanged looks like.</para>
///
/// <para><b>WPF GRANTS ON TWO CONDITIONS IN THE MIXER AND ONE ON THE CHECKBOX, and the port
/// implements one.</b> The mixer's expression is
/// <c>(App.Patreon?.HasPremiumAccess ?? false) || App.DailyFree?.IsFreeToday("haptics") == true</c>
/// (<c>HapticMixer.cs:200-201</c>) and the premium rail's lockband uses the two-term
/// <c>TierGate.RequiresPremium(…, "haptics")</c> (<c>MainWindow/MainWindow.PremiumRail.cs:573</c>);
/// <b>the checkbox handler uses only the first term</b> (<c>MainWindow.Haptics.cs:489</c> — the
/// PREDICATE line, not the <c>isEnabled</c> assignment above it at <c>:487</c>). Since
/// <c>"haptics"</c> is in <c>DailyFreeService.OverridableKeys</c> (<c>:49</c>) but was CUT from the
/// rotation <c>Pool</c> (<c>:40</c>), that disagreement is reachable only on a day the SERVER names
/// haptics — on which the rail unlocks and the mixer opens while the checkbox still refuses. The
/// port has no <c>DailyFreeService</c> and no <c>/config/daily-feature</c> fetch, so term 2 has
/// nothing to bind to; the divergence and the source discrepancy are both recorded
/// (wpf-surface-reachability.md, the haptics section).</para>
///
/// <para><b>Where the port improves on WPF, deliberately.</b> <c>App.Patreon?.HasPremiumAccess != true</c>
/// renders a NULL Patreon service — "I could not tell" — as "you are not a patron", in a modal box
/// that says so. The port refuses too (it must: an unknown entitlement may not open a paid feature)
/// but through <see cref="HapticGateDecision.RefusedUnverified"/>, which says which part could not
/// be determined and that nothing was decided about the user. Recorded as a divergence rather than
/// smuggled in as parity, on the D21 precedent.</para>
///
/// <para><b>And this is not the rare branch.</b> This build's authority is
/// <c>UnconfiguredTierSource</c>, so every real user today reaches
/// <see cref="HapticGateDecision.RefusedUnverified"/>. This is the THIRD door in that state, after
/// the two DTRH doors, with the same cause and the same owner decision behind it.</para>
/// </summary>
public static class HapticGate
{
    /// <summary>The feature as the shipping app's own refusal names it.</summary>
    public const string FeatureName = "Haptic feedback";

    /// <summary>
    /// The bar: tier 1. <c>MainWindow.Haptics.cs:489</c> reads <c>HasPremiumAccess</c>, which is
    /// <c>CurrentTier &gt;= PatreonTier.Level1</c> (<c>Services/Account/PatreonService.cs:134</c>).
    /// </summary>
    public const EntitlementTier RequiredTier = EntitlementTier.Supporter;

    /// <summary>WPF's own refusal text, verbatim (<c>Localization/Languages/en.json:3394</c>,
    /// <c>msg_haptic_feedback_patreon_only</c>).</summary>
    public const string DeniedMessage = "Haptic feedback is available for Patreon supporters.";

    /// <summary>The title of the box WPF shows it in (<c>en.json:3395</c>,
    /// <c>title_patreon_feature</c>). Kept because the port's notice is not a modal and the words
    /// have to carry what the window chrome used to.</summary>
    public const string DeniedTitle = "Patreon Feature";

    /// <summary>
    /// WPF's refusal has a route out: the tier gate's toast carries a "See tiers" action opening
    /// App Info &amp; Data (<c>Services/TierGate.cs:133</c>, <c>en.json:4705</c>). The port has no
    /// tiers page, and a button with nowhere to go is a dead control — so the route is named in
    /// words, exactly as the DTRH door names it (<c>DtrhGate.UpgradeRoute</c>).
    /// </summary>
    public const string UpgradeRoute =
        "See tiers in the shipping Conditioning Control Panel app (App Info & Data) — this port has no tiers "
        + "page of its own yet.";

    /// <summary>The first line of every could-not-verify refusal.</summary>
    public const string CouldNotVerifyHeader =
        "Haptics stayed switched off: this build could not verify your entitlement.";

    /// <summary>The last line of every could-not-verify refusal. It exists so the user is told, in
    /// words, that nothing was decided ABOUT THEM.</summary>
    public const string CouldNotVerifyFooter =
        "Nothing was decided about your account here — the switch stayed off because the answer is unknown, not "
        + "because it was no.";

    /// <summary>Marker for a reason code this surface has no authored wording for. A fact pins that
    /// no shipped code reaches it; it exists so an unknown code still renders honestly instead of
    /// falling into the refusal wording.</summary>
    public const string UnwordedReasonMarker =
        "no authored explanation exists in this build for reason code";

    /// <summary>The refusal WPF shows, plus the route out.</summary>
    public static string TierRefusalMessage { get; } = DeniedMessage + "\n" + UpgradeRoute;

    /// <summary>
    /// The whole gate. <see cref="EntitlementOutcome.Match"/> does not compile with a branch
    /// missing, which is what stops a later edit from quietly dropping the third case.
    /// </summary>
    public static HapticGateDecision Decide(EntitlementOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        return outcome.Match<HapticGateDecision>(
            // A confirmed pledge BELOW the bar is still an authority's answer about this account,
            // so it is the same refusal WPF gives. An UNDEFINED tier is neither: `>=` on a raw enum
            // would open the door for (EntitlementTier)99, so the comparison never runs on a value
            // this build does not define. HostLoginEntitlement closes the same hole at the source
            // and DtrhGate takes the same second guard; this is the third, on the only
            // direction where a mistake hands out a paid feature.
            tier => !Enum.IsDefined(tier)
                ? new HapticGateDecision.RefusedUnverified(
                    EntitlementReasonCodes.TierAuthorityFault,
                    UnverifiedMessage(EntitlementReasonCodes.TierAuthorityFault))
                : tier >= RequiredTier
                    ? new HapticGateDecision.Allow(tier)
                    : new HapticGateDecision.RefusedNotEntitled(TierRefusalMessage),
            _ => new HapticGateDecision.RefusedNotEntitled(TierRefusalMessage),
            reason => new HapticGateDecision.RefusedUnverified(reason.Code, UnverifiedMessage(reason.Code)));
    }

    /// <summary>The composed could-not-verify message for one reason code.</summary>
    public static string UnverifiedMessage(string reasonCode) =>
        CouldNotVerifyHeader + "\n" + Explain(reasonCode) + "\n" + CouldNotVerifyFooter;

    /// <summary>
    /// One authored sentence per <see cref="EntitlementReasonCodes"/> value, worded for THIS
    /// surface: a switch that stayed off, not a door that stayed shut. None of them may carry the
    /// tier-refusal wording — <c>HapticGateTests</c> holds that mechanically, so a reason code added
    /// later cannot silently inherit "you are not a patron".
    /// </summary>
    public static string Explain(string reasonCode) => reasonCode switch
    {
        EntitlementReasonCodes.HostAppDataAbsent =>
            "The shipping Conditioning Control Panel app is not installed for this Windows user, so there is no "
            + "login here to read.",
        EntitlementReasonCodes.HostTokenAbsent =>
            "The shipping Conditioning Control Panel app is installed but has stored no login. Sign in there and "
            + "try again.",
        EntitlementReasonCodes.HostTokenEmpty =>
            "The stored login decrypted to an empty value, so there was nothing to present to an authority.",
        EntitlementReasonCodes.HostTokenUndecryptable =>
            "The stored login could not be decrypted: it is corrupt, was written by a different Windows user, or "
            + "the shipping app rotated how it protects it.",
        EntitlementReasonCodes.HostReadFailed =>
            "The stored login could not be read from disk (I/O, permissions, or the file was locked).",
        EntitlementReasonCodes.UnsupportedPlatform =>
            "This operating system has no DPAPI, so the shipping app's login cannot be read here at all.",
        EntitlementReasonCodes.TierAuthorityAbsent =>
            "This build has no entitlement authority configured, so your tier cannot be looked up. That is a gap "
            + "in the port, not a finding about your account.",
        EntitlementReasonCodes.TierAuthorityUnreachable =>
            "The entitlement authority could not be reached (offline, DNS, or a timeout).",
        EntitlementReasonCodes.TierAuthorityRejected =>
            "The entitlement authority rejected the stored login as expired or rotated, so it never reached the "
            + "entitlement question at all.",
        EntitlementReasonCodes.TierAuthorityFault =>
            "The entitlement lookup itself failed.",
        _ => UnwordedReasonMarker + " '" + reasonCode + "'.",
    };
}
