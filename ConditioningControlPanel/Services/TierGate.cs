using System;
using ConditioningControlPanel.Models;

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
    /// Deliberately thin: it consults <see cref="PatreonService"/> and nothing else. Every other
    /// entitlement source (whitelist, SubscribeStar, the 14-day grace) is already folded into
    /// HasPremiumAccess / HasLabAccess, so reading settings here would only re-introduce the stale
    /// second opinion this class exists to delete.
    ///
    /// Two bars, and the names lie about which is which: "premium" in this codebase means TIER 1
    /// (<see cref="PatreonService.HasPremiumAccess"/>, and <see cref="PatreonService.HasAiAccess"/>
    /// resolves to the same thing despite the name); tier 2 is
    /// <see cref="PatreonService.HasLabAccess"/>.
    ///
    /// Fails CLOSED when App.Patreon is null — during startup, or if the service failed to
    /// construct, a missing gate must not read as an open door.
    /// </summary>
    public static class TierGate
    {
        /// <summary>Tier 1+ ("premium"): any pledge, whitelist, SubscribeStar, or the grace cache.</summary>
        public static TierVerdict RequiresPremium(string featureName)
        {
            var allowed = App.Patreon?.HasPremiumAccess == true;
            return new TierVerdict(allowed, featureName, PatreonTier.Level1,
                allowed ? string.Empty : $"{featureName} is for supporters - any Patreon tier unlocks it.");
        }

        /// <summary>Tier 2+ ("Lab"): the real T2 bar, whitelist folded in as permanent tier 2.</summary>
        public static TierVerdict RequiresLab(string featureName)
        {
            var allowed = App.Patreon?.HasLabAccess == true;
            return new TierVerdict(allowed, featureName, PatreonTier.Level2,
                allowed ? string.Empty : $"{featureName} is a Tier 2 perk - upgrade your pledge to unlock it.");
        }

        /// <summary>
        /// Gate-and-tell for a click handler: true to proceed, false after the user has been told
        /// why not. Callers return on false; nothing here throws.
        /// </summary>
        public static bool DemandPremium(string featureName) => Demand(RequiresPremium(featureName));

        /// <inheritdoc cref="DemandPremium"/>
        public static bool DemandLab(string featureName) => Demand(RequiresLab(featureName));

        private static bool Demand(in TierVerdict verdict)
        {
            if (verdict.Allowed) return true;
            ShowDenied(verdict);
            return false;
        }

        /// <summary>
        /// The refusal surface: a toast whose action button opens the App Info &amp; Data popup,
        /// which is where every other gated door already sends people (Programs, Blink Trainer,
        /// the For You feed all call ShowAppInfoPopup). Non-blocking on purpose - a modal would
        /// fight for focus with the window the click was about to open.
        ///
        /// Never throws: a gate that crashes the handler is worse than one that only logs.
        /// </summary>
        public static void ShowDenied(in TierVerdict verdict)
        {
            App.Logger?.Information("TierGate: blocked {Feature} (needs {Required})", verdict.Feature, verdict.Required);
            try
            {
                App.Notifications?.Show(verdict.Reason, NotificationType.Warning, TimeSpan.FromSeconds(8),
                    "See tiers", () => App.MainWindowRef?.ShowAppInfoPopup());
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("TierGate: denial toast failed: {E}", ex.Message);
            }
        }
    }
}
