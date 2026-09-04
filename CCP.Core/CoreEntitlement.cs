using System;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The entitlement seam: the two account bars <see cref="TierGate"/> decides against, the
    /// daily-free rotation it ORs in, and the surface that TELLS the user about a refusal.
    ///
    /// <para>Deciding is policy over account state and is portable, so <see cref="TierGate"/> now
    /// lives in Core. Everything it needed from the head is here: the answers come from
    /// <c>PatreonService</c> and <c>DailyFreeService</c>'s instance, and the refusal is a toast
    /// plus an EMI Desk beat - a window, a notification host and an analytics sink, none of which
    /// belong in an engine.</para>
    ///
    /// <para><b>Unseeded, every bar answers false and the refusal is log-only.</b> That is the
    /// safe direction and not merely the convenient one: a gate that cannot read an account must
    /// DENY, because the alternative is a head with no Patreon service handing out Tier 2. It is
    /// also exactly what the WPF original did when <c>App.Patreon</c> was null. A provider that
    /// THROWS reads as denied for the same reason.</para>
    ///
    /// <para>An unseeded denial is still written to the log by <see cref="TierGate.ShowDenied"/>;
    /// only the user-facing half goes quiet. A silent refusal is a worse experience than a toast,
    /// but it is not an unsafe one.</para>
    /// </summary>
    public static class CoreEntitlement
    {
        /// <summary>Tier 1+ ("premium"): pledge, whitelist, SubscribeStar or the grace cache.</summary>
        public static volatile Func<bool>? HasPremiumProvider;

        /// <summary>Tier 2+ ("Lab"), whitelist folded in as permanent tier 2.</summary>
        public static volatile Func<bool>? HasLabProvider;

        /// <summary>DailyFreeService.IsFreeToday - "is this feature the free one today?".</summary>
        public static volatile Func<string?, bool>? IsFreeTodayProvider;

        /// <summary>The head's refusal surface. No-op with no head: the gate still denies.</summary>
        public static volatile Action<TierVerdict>? ShowDeniedHandler;

        public static bool HasPremium
        {
            get { try { return HasPremiumProvider?.Invoke() ?? false; } catch { return false; } }
        }

        public static bool HasLab
        {
            get { try { return HasLabProvider?.Invoke() ?? false; } catch { return false; } }
        }

        public static bool IsFreeToday(string? featureKey)
        {
            try { return IsFreeTodayProvider?.Invoke(featureKey) ?? false; } catch { return false; }
        }

        public static void ShowDenied(TierVerdict verdict)
        {
            try { ShowDeniedHandler?.Invoke(verdict); } catch { }
        }
    }
}
