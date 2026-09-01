using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// CCBill AI Content Merchant Addendum — age + content acknowledgement gate logic.
    ///
    /// Centralizes the decision "does activating this preset (or flipping SlutMode on with
    /// this preset active) require the user to clear the explicit-content acknowledgement
    /// dialog?". The defense is wrapping (acknowledgement before activation), not
    /// sterilization of the underlying preset text.
    ///
    /// Rules:
    /// 1. Preset opts in unconditionally via <see cref="PersonalityPreset.RequiresExplicitAcknowledgement"/>
    ///    (currently only the "slutmode" built-in).
    /// 2. The "strict-domme" and "bimbo-cow" built-ins are gated ONLY when SlutMode is on
    ///    (they have explicit SlutModePersonality variants).
    /// 3. Any other preset (built-in or community/asset) with a non-empty SlutModePersonality
    ///    is gated when SlutMode is on. This covers asset prompts in assets/prompts/*.json
    ///    (Soft Hypnotist, Strict Domme v1, Chaotic Gremlin, Elegant Mistress) and user-installed
    ///    community prompts that ship slut variants.
    /// </summary>
    public static class ExplicitContentGate
    {
        /// <summary>
        /// True if activating <paramref name="preset"/> (with the supplied SlutMode state)
        /// requires the acknowledgement dialog. Pass <paramref name="slutModeOn"/> = the
        /// SlutMode state that WILL be in effect after the pending action.
        /// </summary>
        public static bool RequiresAcknowledgement(PersonalityPreset? preset, bool slutModeOn)
        {
            if (preset == null) return false;

            // Rule 1: unconditional preset opt-in (e.g. SlutMode built-in).
            if (preset.RequiresExplicitAcknowledgement) return true;

            // Rule 2 + 3: SlutMode-conditional. Any preset that ships a SlutModePersonality
            // expressed an intent to deliver an explicit variant when SlutMode is on.
            if (slutModeOn)
            {
                var slutText = preset.PromptSettings?.SlutModePersonality;
                if (!string.IsNullOrWhiteSpace(slutText)) return true;
            }

            return false;
        }

        /// <summary>
        /// True if the user's current <see cref="CompanionPromptSettings.ExplicitContentAcknowledged"/>
        /// + <see cref="CompanionPromptSettings.ExplicitAcknowledgedVersion"/> already satisfy the
        /// current acknowledgement version constant.
        /// </summary>
        public static bool IsAlreadyAcknowledged(CompanionPromptSettings? settings)
        {
            if (settings == null) return false;
            return settings.ExplicitContentAcknowledged
                   && settings.ExplicitAcknowledgedVersion == CompanionPromptSettings.ExplicitAcknowledgementVersion;
        }

        /// <summary>
        /// Persists an Accept on the explicit-content dialog. Caller is responsible for
        /// calling SettingsService.Save() after this. Idempotent.
        /// </summary>
        public static void MarkAcknowledged(CompanionPromptSettings settings)
        {
            settings.ExplicitContentAcknowledged = true;
            settings.ExplicitAcknowledgedVersion = CompanionPromptSettings.ExplicitAcknowledgementVersion;
        }
    }
}
