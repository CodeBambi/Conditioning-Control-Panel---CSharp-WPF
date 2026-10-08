using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Fx
{
    /// <summary>What the section edge's strips run at a motion level and tier.</summary>
    public enum EdgeFogMode
    {
        /// <summary>No strips at all: the static band and the line are the design.</summary>
        None,
        /// <summary>Reduced: the fog alone, half the puffs at half the speed. No embers.</summary>
        Reduced,
        /// <summary>Full: the whole fog plus the small embers as sparkle.</summary>
        Full,
    }

    /// <summary>
    /// The pure gate and numbers of WPF 7.1.5 Controls/NavRail/EdgeParticles.cs: four strips, one
    /// per window side (Top, Right, Bottom, Left), each running EdgeFog and, at Full, EdgeDrift
    /// embers kept to a 30 px band. Nothing is allocated unless the tier allows ambient motion,
    /// has a particle budget, and the level is not Off. Four strips, never one full-window canvas.
    /// </summary>
    public static class EdgeParticleRules
    {
        /// <summary>Strip thickness in native pixels (outside the Viewbox): the fog's depth.</summary>
        public const double StripThickness = EdgeFogMath.StripPx;
        /// <summary>The embers keep to their wave 9 band inside the deeper strip.</summary>
        public const double EmberBandPx = 30;
        /// <summary>Alpha multiplier on the embers.</summary>
        public const double StripIntensity = 0.55;
        /// <summary>Dust density handed to every strip.</summary>
        public const double StripDustDensity = 0.35;

        /// <summary>The strips in build order.</summary>
        public static readonly EdgeSide[] Sides = { EdgeSide.Top, EdgeSide.Right, EdgeSide.Bottom, EdgeSide.Left };

        /// <summary>The embers' gate: Full motion and a tier with a particle budget.</summary>
        public static bool ShouldMount(MotionLevel level, bool tierAllowsParticles) =>
            ModeFor(level, tierAllowsParticles) == EdgeFogMode.Full;

        /// <summary>Off or no budget = none, Reduced = half fog, Full = everything.</summary>
        public static EdgeFogMode ModeFor(MotionLevel level, bool tierAllowsParticles) =>
            !tierAllowsParticles || level == MotionLevel.Off ? EdgeFogMode.None
            : level == MotionLevel.Reduced ? EdgeFogMode.Reduced
            : EdgeFogMode.Full;

        /// <summary>The tier half of the gate: ambient motion allowed and a non-zero particle budget.</summary>
        public static bool TierAllowsParticles(PerformanceTier tier) =>
            Motion.MotionGate.AllowAmbientMotion(tier) && Motion.MotionGate.MaxAmbientParticles(tier) > 0;

        /// <summary>The layers a strip runs in a mode.</summary>
        public static AmbientFxLayers LayersFor(EdgeFogMode mode) => mode switch
        {
            EdgeFogMode.Full => AmbientFxLayers.EdgeFog | AmbientFxLayers.EdgeDrift,
            EdgeFogMode.Reduced => AmbientFxLayers.EdgeFog,
            _ => AmbientFxLayers.None,
        };

        /// <summary>A strip is horizontal (full width, StripThickness tall) on Top and Bottom.</summary>
        public static bool IsHorizontal(EdgeSide side) => side is EdgeSide.Top or EdgeSide.Bottom;
    }
}
