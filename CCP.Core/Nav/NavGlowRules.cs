using System;
using ConditioningControlPanel.Motion;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Nav
{
    /// <summary>
    /// The one "look here" glow of the nav rework, the pure half of WPF 7.1.5
    /// Controls/NavRail/NavGlow.cs: a rounded ring plus a soft halo in the section accent, drawn
    /// over the target. Full = sheen in, 2 s hold, fade; Reduced = static hold; Off = nothing.
    /// A one-shot cue, not an ambient loop: the performance tier does not gate it. The glow must
    /// leave with its target (a hidden target drops the glow at once).
    /// </summary>
    public static class NavGlowRules
    {
        public const int SheenMs = 600;
        public const int HoldMs = 2000;
        public const int FadeMs = 400;

        /// <summary>Total length at a motion level (0 = no glow).</summary>
        public static int TotalMs(MotionLevel level) => level switch
        {
            MotionLevel.Off => 0,
            MotionLevel.Reduced => HoldMs,
            _ => SheenMs + HoldMs + FadeMs,
        };

        /// <summary>The held opacity at Full (after the sheen) and at Reduced.</summary>
        public const double FullHoldOpacity = 0.8, ReducedHoldOpacity = 0.85;

        /// <summary>
        /// The ring's opacity track, starting from 0. Full: up to 1.0 by half the sheen, settle to
        /// 0.8 by its end (both WPF default SineEase = sine out), hold, linear fade to 0. Reduced:
        /// 0.85 at once, held 2 s, gone 1 ms later. Off: empty (no glow).
        /// </summary>
        public static Keyframe[] OpacityTrack(MotionLevel level) => level switch
        {
            MotionLevel.Off => Array.Empty<Keyframe>(),
            MotionLevel.Reduced => new[]
            {
                new Keyframe(0, ReducedHoldOpacity, EaseKind.Discrete),
                new Keyframe(HoldMs, ReducedHoldOpacity, EaseKind.Discrete),
                new Keyframe(HoldMs + 1, 0, EaseKind.Discrete),
            },
            _ => new[]
            {
                new Keyframe(SheenMs / 2.0, 1.0, EaseKind.SineOut),
                new Keyframe(SheenMs, FullHoldOpacity, EaseKind.SineOut),
                new Keyframe(SheenMs + HoldMs, FullHoldOpacity, EaseKind.Discrete),
                new Keyframe(SheenMs + HoldMs + FadeMs, 0, EaseKind.Linear),
            },
        };

        /// <summary>The ring's opacity <paramref name="ms"/> after it starts.</summary>
        public static double OpacityAt(MotionLevel level, double ms) => Keyframes.Sample(OpacityTrack(level), 0, ms);

        // ---- geometry (GlowAdorner.OnRender) ----------------------------------------------------

        /// <summary>The ring sits this far outside the target on every side.</summary>
        public const double RingOutsetPx = 3;
        /// <summary>The ring's stroke, and its corner radius cap (radius = min(14, ringHeight / 2)).</summary>
        public const double RingThickness = 2.5, MaxRadius = 14;
        /// <summary>The halo: four 3 px strokes, each 3 px further out than the last (first at +3),
        /// alphas 90, 70, 50, 30 of the accent.</summary>
        public const int HaloCount = 4;
        public const double HaloThickness = 3, HaloStepPx = 3;
        /// <summary>The ring's inner fill: the accent at 0x26.</summary>
        public const byte FillAlpha = 0x26;

        /// <summary>The halo stroke alpha for ring <paramref name="i"/> (0 innermost).</summary>
        public static byte HaloAlpha(int i) => (byte)(90 - i * 20);

        /// <summary>How far halo <paramref name="i"/> is inflated past the ring rect (and added to its radius).</summary>
        public static double HaloInflate(int i) => HaloStepPx + i * HaloStepPx;

        /// <summary>The ring's corner radius for a target of this height.</summary>
        public static double RingRadius(double targetHeight) => Math.Min(MaxRadius, (targetHeight + 2 * RingOutsetPx) / 2);
    }
}
