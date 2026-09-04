using System;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The answer a gate gives: whether the account clears the bar, which bar it was, and one
    /// sentence to show the user. Carried around so a lockband, a launch handler and a CLI switch
    /// can all render the same refusal without each inventing its own copy.
    /// </summary>
    public readonly struct TierVerdict
    {
        public TierVerdict(bool allowed, string feature, PatreonTier required, string reason)
        {
            Allowed = allowed;
            Feature = feature;
            Required = required;
            Reason = reason;
        }

        public bool Allowed { get; }

        /// <summary>Display-ready feature name, as the user sees it on the card.</summary>
        public string Feature { get; }

        public PatreonTier Required { get; }

        /// <summary>User-facing refusal. Empty when <see cref="Allowed"/>.</summary>
        public string Reason { get; }
    }

    /// <summary>
    /// One truth for "may this account open that?", so launch handlers, CLI switches and card
    /// lockbands cannot drift apart the way the Lab smokescreen and the Goon host bar did.
    ///
    /// Deliberately thin: it consults <see cref="CoreEntitlement"/> and nothing else. Every other
    /// entitlement source (whitelist, SubscribeStar, the 14-day grace) is already folded into
    /// <c>PatreonService.HasPremiumAccess</c> / <c>HasLabAccess</c> behind that seam, so reading
    /// settings here would only re-introduce the stale second opinion this class exists to delete.
    ///
    /// Two bars, and the names lie about which is which: "premium" in this codebase means TIER 1
    /// (<c>PatreonService.HasPremiumAccess</c>, and <c>HasAiAccess</c> resolves to the same thing
    /// despite the name); tier 2 is <c>PatreonService.HasLabAccess</c>.
    ///
    /// Fails CLOSED when the entitlement seam is unseeded — a head with no account service, or a
    /// WPF startup that has not reached the seeding block, must not read as an open door. That is
    /// the same behaviour the original had when <c>App.Patreon</c> was null.
    /// </summary>
    public static class TierGate
    {
        /// <summary>Tier 1+ ("premium"): any pledge, whitelist, SubscribeStar, or the grace cache.</summary>
        public static TierVerdict RequiresPremium(string featureName)
        {
            var allowed = CoreEntitlement.HasPremium;
            return new TierVerdict(allowed, featureName, PatreonTier.Level1,
                allowed ? string.Empty : Loc.GetF("tiergate_denied_premium", featureName));
        }

        /// <summary>
        /// Premium bar with the daily free feature OR'd in: on the day
        /// <see cref="DailyFreeService"/> rotates <paramref name="dailyKey"/> in, the verdict
        /// allows, so the lockband hides and the click just works - no call site special-cases
        /// "free today". Only the six pool features pass a key; everything else keeps the plain
        /// overload and is untouched by the rotation.
        /// </summary>
        public static TierVerdict RequiresPremium(string featureName, string dailyKey)
        {
            var allowed = CoreEntitlement.HasPremium
                          || CoreEntitlement.IsFreeToday(dailyKey);
            return new TierVerdict(allowed, featureName, PatreonTier.Level1,
                allowed ? string.Empty : Loc.GetF("tiergate_denied_premium", featureName));
        }

        /// <summary>Tier 2+ ("Lab"): the real T2 bar, whitelist folded in as permanent tier 2.</summary>
        public static TierVerdict RequiresLab(string featureName)
        {
            var allowed = CoreEntitlement.HasLab;
            return new TierVerdict(allowed, featureName, PatreonTier.Level2,
                allowed ? string.Empty : Loc.GetF("tiergate_denied_lab", featureName));
        }

        /// <summary>
        /// Lab bar with the daily free feature OR'd in - the off-pool drop path. The rotation
        /// wheel never lands on T2 content; this only opens when the SERVER names the key for
        /// the day (DtRH drop days). Same shape as the keyed premium overload above.
        /// </summary>
        public static TierVerdict RequiresLab(string featureName, string dailyKey)
        {
            var allowed = CoreEntitlement.HasLab
                          || CoreEntitlement.IsFreeToday(dailyKey);
            return new TierVerdict(allowed, featureName, PatreonTier.Level2,
                allowed ? string.Empty : Loc.GetF("tiergate_denied_lab", featureName));
        }

        /// <summary>
        /// Gate-and-tell for a click handler: true to proceed, false after the user has been told
        /// why not. Callers return on false; nothing here throws.
        /// </summary>
        public static bool DemandPremium(string featureName) => Demand(RequiresPremium(featureName));

        /// <inheritdoc cref="RequiresPremium(string, string)"/>
        public static bool DemandPremium(string featureName, string dailyKey) =>
            Demand(RequiresPremium(featureName, dailyKey));

        /// <inheritdoc cref="DemandPremium"/>
        public static bool DemandLab(string featureName) => Demand(RequiresLab(featureName));

        /// <inheritdoc cref="RequiresLab(string, string)"/>
        public static bool DemandLab(string featureName, string dailyKey) =>
            Demand(RequiresLab(featureName, dailyKey));

        private static bool Demand(in TierVerdict verdict)
        {
            if (verdict.Allowed) return true;
            ShowDenied(verdict);
            return false;
        }

        /// <summary>
        /// The refusal surface. Logging it is the engine's half and always happens; SHOWING it is
        /// the head's, through <see cref="CoreEntitlement.ShowDeniedHandler"/> - on WPF a toast
        /// whose action button opens the App Info &amp; Data popup, which is where every other
        /// gated door already sends people, plus the EMI Desk beat. Non-blocking on purpose - a
        /// modal would fight for focus with the window the click was about to open.
        ///
        /// With no head attached the refusal is log-only. The door still does not open: only the
        /// telling is lost, never the denying.
        ///
        /// Never throws: a gate that crashes the handler is worse than one that only logs.
        /// </summary>
        public static void ShowDenied(in TierVerdict verdict)
        {
            Log.Information("TierGate: blocked {Feature} (needs {Required})", verdict.Feature, verdict.Required);
            CoreEntitlement.ShowDenied(verdict);
        }
    }
}
