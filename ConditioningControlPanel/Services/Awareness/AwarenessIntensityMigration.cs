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
        /// </summary>
        public static bool EnsureMigrated(AppSettings? settings)
        {
            if (settings == null) return false;
            if (settings.AwarenessIntensityMigrated) return false;

            var mapped = FromCooldownSeconds(settings.AwarenessReactionCooldownSeconds);
            settings.AwarenessIntensity = mapped;
            settings.AwarenessIntensityMigrated = true;

            App.Logger?.Information(
                "Awareness: migrated the {Seconds}s reaction cooldown to intensity {Intensity}",
                settings.AwarenessReactionCooldownSeconds, mapped);
            return true;
        }
    }
}
