using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Companion
{
    /// <summary>
    /// The one truth for "may the AI drive effects right now".
    ///
    /// <para><b>The bug this exists for (#1307, and #1048 before it).</b> AI Effect Control is Tier 2.
    /// It used to be enforced by a REPAIR in <c>MainWindow.UpdateUnlockablesVisibility</c>: whenever
    /// Lab access read false after the entitlement was "resolved", the saved switch was cleared and
    /// SAVED. <c>PatreonService.EntitlementResolved</c> flips even when validation threw, and a
    /// Discord / SubscribeStar / whitelisted patron can read false for a moment before their own
    /// source answers, so a patron's switch was wiped on some restarts.</para>
    ///
    /// <para>So the saved setting is now only the user's choice, and nothing clears it because of an
    /// entitlement read. The tier gate lives here, at the point of use: effects run only while the
    /// switch is on AND Lab access is live. A lapsed account keeps its saved choice and simply gets
    /// no effects, which is what the repair was protecting against in the first place.</para>
    /// </summary>
    public static class AiEffectControlGate
    {
        /// <summary>Pure rule: the saved switch counts only while Lab access is live.</summary>
        public static bool IsOn(bool savedSetting, bool labAccess) => savedSetting && labAccess;

        /// <summary>Pure rule over the settings object; null settings means off.</summary>
        public static bool IsOn(CompanionPromptSettings? settings, bool labAccess)
            => IsOn(settings?.AllowAiToControlEffects == true, labAccess);

        /// <summary>The live answer: current settings and current Lab access.</summary>
        public static bool IsOnNow
            => IsOn(CoreSettings.Current?.CompanionPrompt, CoreAccount.HasLabAccess);
    }
}
