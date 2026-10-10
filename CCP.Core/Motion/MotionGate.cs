using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Motion
{
    /// <summary>
    /// The single reduced-motion gate of WPF 7.1.5 (Services/MotionFx.cs + PerformanceProfile), the
    /// pure half: every input is a parameter, so a head feeds the user's setting, the OS flag and
    /// the performance tier and both heads answer the same question the same way.
    ///
    /// <para>Two clocks: interaction motion (80-400 ms) runs at every level but Off; ambient motion
    /// (8-60 s loops) runs only at Full on a tier that allows ambient motion. The fallback for a
    /// refused ambient loop is static art, never a slower loop.</para>
    /// </summary>
    public static class MotionGate
    {
        /// <summary>
        /// The user's setting capped to Reduced when the OS animation flag is off (Windows
        /// SystemParameters.ClientAreaAnimation, GNOME enable-animations, KDE AnimationDurationFactor).
        /// The OS preference can only ever remove motion, never add it back.
        /// </summary>
        public static MotionLevel ResolveLevel(MotionLevel setting, bool osAnimationsEnabled)
        {
            if (setting == MotionLevel.Off) return MotionLevel.Off;
            if (!osAnimationsEnabled) return MotionLevel.Reduced;
            return setting;
        }

        /// <summary>Interaction motion (hover, press, transitions, entrances): off only at Off.</summary>
        public static bool AllowTransitions(MotionLevel level) => level != MotionLevel.Off;

        /// <summary>Ambient loops: Full AND a tier that permits ambient motion.</summary>
        public static bool AllowAmbientLoops(MotionLevel level, PerformanceTier tier) =>
            level == MotionLevel.Full && AllowAmbientMotion(tier);

        /// <summary>Particles: ambient loops plus a non-zero tier particle budget.</summary>
        public static bool AllowParticles(MotionLevel level, PerformanceTier tier) =>
            AllowAmbientLoops(level, tier) && MaxAmbientParticles(tier) > 0;

        // ---- PerformanceProfile, verbatim -----------------------------------------------------

        /// <summary>The Performance tier costs exactly zero: no loop, no particles.</summary>
        public static bool AllowAmbientMotion(PerformanceTier tier) => tier != PerformanceTier.Performance;

        /// <summary>Particle budget for one ambient canvas, per tier (0 = no particle layers).</summary>
        public static int MaxAmbientParticles(PerformanceTier tier) => tier switch
        {
            PerformanceTier.Performance => 0,
            PerformanceTier.Balanced => 24,
            _ => 60,
        };

        /// <summary>Target frames per second for ambient Skia canvases, per tier (0 = do not run).</summary>
        public static int FxTargetFps(PerformanceTier tier) => tier switch
        {
            PerformanceTier.Performance => 0,
            PerformanceTier.Balanced => 24,
            _ => 30,
        };

        /// <summary>Motion-scaled milliseconds (NavRailRules.Ms / DepthRules.Ms): Full as authored,
        /// Reduced halves it (integer division), Off is instant (0).</summary>
        public static int Ms(int fullMs, MotionLevel level) => level switch
        {
            MotionLevel.Off => 0,
            MotionLevel.Reduced => fullMs / 2,
            _ => fullMs,
        };
    }
}
