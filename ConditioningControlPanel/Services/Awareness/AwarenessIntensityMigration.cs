using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Awareness
{
    /// <summary>
    /// The one-time move from the old cooldown slider to the intensity dial (doc 02 §8).
    ///
    /// <para><c>AwarenessReactionCooldownSeconds</c> is kept — the legacy pipeline still reads it when
    /// <c>UseAwarenessV2</c> is off, and throwing away a setting the kill switch depends on would make
    /// the kill switch a downgrade. It simply stops being surfaced: the Workshop cell shows the dial
    /// instead, and the sliders only reappear if v2 is switched off.</para>
    ///
    /// <para><b>Why thresholds and not arithmetic.</b> A cooldown is a floor, not a rate: at the shipped
    /// default of 10s the old system could in principle have said 360 things an hour, which maps to
    /// nothing sane. What the number actually expressed was appetite, so it is read as appetite —
    /// a short cooldown means "talk to me", a long one means "not much".</para>
    /// </summary>
    public static class AwarenessIntensityMigration
    {
        /// <summary>
        /// The cooldown every user who never touched the slider has
        /// (<c>AppSettings._awarenessReactionCooldownSeconds</c>, which is also the slider's minimum).
        /// A value AT this number expresses no preference at all, so it must not be read as one.
        /// </summary>
        public const int ShippedDefaultCooldownSeconds = 10;

        /// <summary>At or below this cooldown the user was asking for as much as she had.</summary>
        public const int UnhingedAtOrBelowSeconds = 30;

        /// <summary>At or below this, the middle setting. Above it, scarcity was the point.</summary>
        public const int ChattyAtOrBelowSeconds = 120;

        /// <summary>Maps a legacy cooldown to the nearest intensity.</summary>
        public static AwarenessIntensity FromCooldownSeconds(int cooldownSeconds)
        {
            if (cooldownSeconds <= UnhingedAtOrBelowSeconds) return AwarenessIntensity.Unhinged;
            if (cooldownSeconds <= ChattyAtOrBelowSeconds) return AwarenessIntensity.Chatty;
            return AwarenessIntensity.Subtle;
        }

        /// <summary>
        /// Runs the migration once and records that it ran. Returns true when it actually wrote.
        ///
        /// <para>Once only, by design: after this the dial is the user's setting, and a second run would
        /// silently undo whatever they chose the next time the app started.</para>
        ///
        /// <para><b>A slider left where it shipped is not a preference.</b> The old default was 10s,
        /// which is also the slider's floor, and <see cref="FromCooldownSeconds"/> maps 10 to
        /// <see cref="AwarenessIntensity.Unhinged"/> — so migrating it unconditionally would set every
        /// user who never dragged the slider (including brand-new installs, which run this from the
        /// consent flow) to twice the documented line rate and twice the documented LLM call rate on
        /// the train whose stated goal is fewer, better lines. At the shipped default the dial keeps
        /// its own default of Chatty and only the "already migrated" flag is written.</para>
        /// </summary>
        public static bool EnsureMigrated(AppSettings? settings)
        {
            if (settings == null) return false;
            if (settings.AwarenessIntensityMigrated) return false;

            int cooldown = settings.AwarenessReactionCooldownSeconds;
            if (cooldown <= ShippedDefaultCooldownSeconds)
            {
                settings.AwarenessIntensityMigrated = true;
                App.Logger?.Information(
                    "Awareness: reaction cooldown was still the shipped {Seconds}s default — keeping intensity {Intensity}",
                    cooldown, settings.AwarenessIntensity);
                return true;
            }

            var mapped = FromCooldownSeconds(cooldown);
            settings.AwarenessIntensity = mapped;
            settings.AwarenessIntensityMigrated = true;

            App.Logger?.Information(
                "Awareness: migrated the {Seconds}s reaction cooldown to intensity {Intensity}",
                cooldown, mapped);
            return true;
        }
    }
}
