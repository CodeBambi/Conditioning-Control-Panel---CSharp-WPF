using System;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Shared cooldown state for AI-triggered effects and Autonomy/Takeover actions.
    /// Both paths use this so they respect the lower of the two configured cooldowns,
    /// but only when both AI effects control and Autonomy/Takeover are enabled.
    /// </summary>
    public static class SharedEffectCooldown
    {
        /// <summary>
        /// UTC timestamp of the last effect command executed by the AI effects path.
        /// </summary>
        public static DateTime LastAiEffectTimeUtc { get; private set; } = DateTime.MinValue;

        /// <summary>
        /// UTC timestamp of the last effect command executed by the Autonomy/Takeover path.
        /// </summary>
        public static DateTime LastAutonomyEffectTimeUtc { get; private set; } = DateTime.MinValue;

        /// <summary>
        /// Records that an effect has just been fired by the specified path.
        /// </summary>
        public static void RecordEffectFired(CooldownSource source)
        {
            var now = DateTime.UtcNow;
            if (source == CooldownSource.Ai)
                LastAiEffectTimeUtc = now;
            else
                LastAutonomyEffectTimeUtc = now;
        }

        /// <summary>
        /// Returns true if the configured cooldown has not elapsed since the last effect
        /// fired by the specified path.
        /// </summary>
        public static bool IsCooldownActive(int cooldownSeconds, CooldownSource source)
        {
            if (cooldownSeconds <= 0) return false;
            var last = source == CooldownSource.Ai ? LastAiEffectTimeUtc : LastAutonomyEffectTimeUtc;
            return (DateTime.UtcNow - last).TotalSeconds < cooldownSeconds;
        }

        /// <summary>
        /// Returns true if the configured shared cooldown has not elapsed since the most
        /// recent effect fired by either the AI or Autonomy path. Use this only when both
        /// AI effects and Autonomy/Takeover are enabled.
        /// </summary>
        public static bool IsSharedCooldownActive(int cooldownSeconds)
        {
            if (cooldownSeconds <= 0) return false;
            var last = LastAiEffectTimeUtc > LastAutonomyEffectTimeUtc ? LastAiEffectTimeUtc : LastAutonomyEffectTimeUtc;
            return (DateTime.UtcNow - last).TotalSeconds < cooldownSeconds;
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
        /// Time elapsed since the most recent effect command from either path.
        /// </summary>
        public static TimeSpan TimeSinceLastEffect
        {
            get
            {
                var last = LastAiEffectTimeUtc > LastAutonomyEffectTimeUtc ? LastAiEffectTimeUtc : LastAutonomyEffectTimeUtc;
                return DateTime.UtcNow - last;
            }
        }
    }

    public enum CooldownSource
    {
        Ai,
        Autonomy
    }
}
