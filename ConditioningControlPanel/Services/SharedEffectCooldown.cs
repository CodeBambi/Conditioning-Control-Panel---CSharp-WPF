using System;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Shared cooldown state for AI-triggered effects and Autonomy/Takeover actions.
    /// Both paths use this so they respect the lower of the two configured cooldowns.
    /// </summary>
    public static class SharedEffectCooldown
    {
        /// <summary>
        /// UTC timestamp of the last effect command executed by either AI or Autonomy.
        /// </summary>
        public static DateTime LastEffectCommandTimeUtc { get; private set; } = DateTime.MinValue;

        /// <summary>
        /// Records that an effect has just been fired by either path.
        /// </summary>
        public static void RecordEffectFired()
        {
            LastEffectCommandTimeUtc = DateTime.UtcNow;
        }

        /// <summary>
        /// Returns true if the configured cooldown has not elapsed since the last effect.
        /// </summary>
        public static bool IsCooldownActive(int cooldownSeconds)
        {
            if (cooldownSeconds <= 0) return false;
            return (DateTime.UtcNow - LastEffectCommandTimeUtc).TotalSeconds < cooldownSeconds;
        }

        /// <summary>
        /// Effective cooldown in seconds: the lower of AiEffectsCooldownSeconds and AutonomyCooldownSeconds.
        /// </summary>
        public static int GetEffectiveCooldownSeconds()
        {
            var settings = App.Settings?.Current;
            if (settings == null) return 90;
            var ai = Math.Clamp(settings.AiEffectsCooldownSeconds, 10, 300);
            var autonomy = Math.Clamp(settings.AutonomyCooldownSeconds, 10, 300);
            return Math.Min(ai, autonomy);
        }

        /// <summary>
        /// Time elapsed since the last effect command. Null if no effect has ever fired.
        /// </summary>
        public static TimeSpan TimeSinceLastEffect => DateTime.UtcNow - LastEffectCommandTimeUtc;
    }
}
