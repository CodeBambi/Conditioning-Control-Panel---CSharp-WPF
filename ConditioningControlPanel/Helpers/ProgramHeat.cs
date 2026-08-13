using System;

namespace ConditioningControlPanel.Helpers
{
    /// <summary>
    /// The five states the Programs tab's lighting rig can be in, coldest first. Every visual the
    /// rig owns is switched by this and by nothing else, so "what does day 3 of a 28-day program
    /// look like" is a question with one answer that a test can pin.
    /// </summary>
    public enum ProgramHeatTier
    {
        /// <summary>Below 0.20. Exactly the flat UI that shipped: no ambient clock anywhere.</summary>
        Cold = 0,
        /// <summary>0.20-0.40. The sigil breathes, the session sheen wakes, today's pip pulses.</summary>
        Warm = 1,
        /// <summary>0.40-0.60. The Today card's border animates, particles start, the rail fill glows.</summary>
        Charged = 2,
        /// <summary>0.60-0.80. Denser particles, the heat wash blooms, the day counter shimmers.</summary>
        Hot = 3,
        /// <summary>0.80-1.00 and every boss day. Panel edge glow, the rail comet, the boss flare.</summary>
        Ignited = 4,
    }

    /// <summary>
    /// The Ignition Curve: one scalar, computed in one place, that the whole Programs restyle reads.
    ///
    /// <para><b>The thesis.</b> Day 1 of a program is deliberately cold and quiet - the panel looks
    /// exactly as it did before this existed. Every day survived earns the interface heat, and by the
    /// end of a run (and on every boss day) it is fully lit. That is the entire feature: there is no
    /// second knob, no per-element setting, and no way for two surfaces to disagree about how far
    /// along the user is.</para>
    ///
    /// <para><b>Why it is pure.</b> Nothing here touches WPF, the service layer or App - it takes the
    /// four numbers that describe "where am I in this program" and returns doubles. That is what lets
    /// the tier boundaries (0.20 / 0.40 / 0.60 / 0.80), the boss pin and the clamp be tested
    /// exhaustively without standing up a window, and it is why the values below are named constants
    /// rather than literals scattered through the FX code.</para>
    ///
    /// <para><b>The no-strobe contract.</b> Every period this class hands out is a lower bound of
    /// <see cref="MinPulseCycleSeconds"/> on a FULL luminance cycle, i.e. never faster than ~0.45Hz,
    /// at every heat including 1.0. The tuning helpers exist so that rule lives in one testable place
    /// instead of in a dozen call sites that each looked reasonable on their own.</para>
    /// </summary>
    public static class ProgramHeat
    {
        // ---- the curve ------------------------------------------------------------------

        /// <summary>Floor: even day 1 of the coldest program is not pitch dark.</summary>
        public const double BaseHeat = 0.15;

        /// <summary>How much of the range the run's own progress can earn.</summary>
        public const double ProgressWeight = 0.55;

        /// <summary>
        /// Above 1, so the early days stay flat and the back half of a run climbs. A linear ramp made
        /// day 4 of 28 already visibly warmer than day 1, which is the opposite of the point.
        /// </summary>
        public const double ProgressExponent = 1.2;

        /// <summary>How much today's authored intensity can earn on its own.</summary>
        public const double IntensityWeight = 0.30;

        // ---- tier boundaries ------------------------------------------------------------

        public const double WarmAt = 0.20;
        public const double ChargedAt = 0.40;
        public const double HotAt = 0.60;
        public const double IgnitedAt = 0.80;

        // ---- motion safety --------------------------------------------------------------

        /// <summary>
        /// The shortest FULL cycle any pulsing element is allowed, in seconds - ~0.45Hz. Under the
        /// app's own no-strobe rule this is a hard floor, not a default: <see cref="BreathSeconds"/>
        /// returns a HALF cycle (the AutoReverse duration), so its value at maximum heat is half of
        /// this by construction.
        /// </summary>
        public const double MinPulseCycleSeconds = 2.2;

        /// <summary>Particle ceiling for the Today card, per the FX budget.</summary>
        public const int MaxParticles = 60;

        // =================================================================================

        /// <summary>
        /// The scalar. <c>0.15 + 0.55·(day/length)^1.2 + 0.30·intensity</c>, clamped to 0-1, with
        /// every boss day pinned to 1.0.
        /// </summary>
        /// <param name="currentDay">The enrollment's current day, 1-based.</param>
        /// <param name="lengthDays">The definition's length. Zero or negative reads as "no progress".</param>
        /// <param name="todayIntensity">The day's authored intensity, clamped to 0-1.</param>
        /// <param name="isBoss">A boss day is always fully lit, whatever the arithmetic says.</param>
        public static double Compute(int currentDay, int lengthDays, double todayIntensity, bool isBoss)
        {
            if (isBoss) return 1.0;

            var progress = lengthDays > 0
                ? Math.Clamp(currentDay / (double)lengthDays, 0.0, 1.0)
                : 0.0;

            // NaN in an authored intensity must not poison the whole rig: Clamp propagates NaN, so
            // it is filtered here rather than left to surface as a NaN duration on a storyboard.
            var intensity = double.IsNaN(todayIntensity) ? 0.0 : Math.Clamp(todayIntensity, 0.0, 1.0);

            var heat = BaseHeat
                     + ProgressWeight * Math.Pow(progress, ProgressExponent)
                     + IntensityWeight * intensity;

            return Math.Clamp(heat, 0.0, 1.0);
        }

        /// <summary>The tier a heat value lands in. Boundaries are inclusive at the bottom.</summary>
        public static ProgramHeatTier TierOf(double heat)
        {
            if (double.IsNaN(heat)) return ProgramHeatTier.Cold;
            if (heat >= IgnitedAt) return ProgramHeatTier.Ignited;
            if (heat >= HotAt) return ProgramHeatTier.Hot;
            if (heat >= ChargedAt) return ProgramHeatTier.Charged;
            if (heat >= WarmAt) return ProgramHeatTier.Warm;
            return ProgramHeatTier.Cold;
        }

        /// <summary>Convenience: the curve and the tier in one call.</summary>
        public static ProgramHeatTier TierFor(int currentDay, int lengthDays,
                                              double todayIntensity, bool isBoss) =>
            TierOf(Compute(currentDay, lengthDays, todayIntensity, isBoss));

        // =================================================================================
        //  tuning derived from the scalar
        //
        //  All of these are monotonic in heat and bounded at both ends, so a caller can hand them
        //  anything (including a heat of exactly 0 or 1) and get a value that is safe to put on a
        //  storyboard without a second clamp.
        // =================================================================================

        /// <summary>
        /// HALF cycle for any breathing element (the AutoReverse duration), 1.9s cold down to 1.1s
        /// ignited - a full cycle of 3.8s down to <see cref="MinPulseCycleSeconds"/>.
        /// </summary>
        public static double BreathSeconds(double heat) => Lerp(1.9, MinPulseCycleSeconds / 2.0, heat);

        /// <summary>
        /// Breathing amplitude as a +/- swing around an element's resting opacity. Small enough at
        /// low heat to read as "alive", wide enough at full heat to read as lit.
        /// </summary>
        public static double BreathAmplitude(double heat) => Lerp(0.10, 0.34, heat);

        /// <summary>Seconds for one full rotation of the Today card's gradient border (Charged+).</summary>
        public static double BorderLapSeconds(double heat) => Lerp(16.0, 7.0, heat);

        /// <summary>Seconds for one pass of the session bar's sheen. The shipped value was a flat 2.8.</summary>
        public static double SheenSweepSeconds(double heat) => Lerp(4.2, 2.0, heat);

        /// <summary>Seconds for one run of the day rail's comet (Ignited only).</summary>
        public static double CometLapSeconds(double heat) => Lerp(4.6, 3.0, heat);

        /// <summary>
        /// Particle count for the Today card. Zero below Charged - the field does not merely fade
        /// out at low heat, it is never started, so cold days hold no clock at all.
        /// </summary>
        public static int ParticleCount(double heat)
        {
            if (TierOf(heat) < ProgramHeatTier.Charged) return 0;
            return (int)Math.Round(Math.Clamp(heat, 0.0, 1.0) * MaxParticles);
        }

        /// <summary>Opacity of the hero band's accent wash. 0.4 was the shipped constant.</summary>
        public static double WashOpacity(double heat) => Lerp(0.40, 0.72, heat);

        /// <summary>Peak opacity of the whole-panel edge glow (Ignited only).</summary>
        public static double EdgeGlowOpacity(double heat) => Lerp(0.26, 0.50, heat);

        /// <summary>
        /// Spark count for a celebration burst. Sized by heat so a day-1 tick is a polite pop and a
        /// boss day is the full thing. The canvas clamps to its own tier budget on top of this.
        /// </summary>
        public static int BurstCount(double heat) => (int)Math.Round(Lerp(60, 140, heat));

        /// <summary>
        /// The XP a completed day actually awards, mirrored from ProgramService.AwardDayXp so the
        /// floating "+XP" can state the real number instead of a decorative one. Computed here rather
        /// than read back from the service because the award has already landed on the progression
        /// total by the time the UI repaints, and there is nothing on the record to read it from.
        /// </summary>
        public static int DayXp(double todayIntensity, bool isBoss)
        {
            var intensity = double.IsNaN(todayIntensity) ? 0.0 : Math.Clamp(todayIntensity, 0.0, 1.0);
            var xp = 200 + (int)Math.Round(400 * intensity);
            return isBoss ? (int)Math.Round(xp * 1.5) : xp;
        }

        private static double Lerp(double cold, double ignited, double heat)
        {
            var t = double.IsNaN(heat) ? 0.0 : Math.Clamp(heat, 0.0, 1.0);
            return cold + (ignited - cold) * t;
        }
    }
}
