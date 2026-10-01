using System;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Super
{
    /// <summary>
    /// "Super only" for the base looks that sit on a compositor layer (the pink tint under Creep,
    /// the spiral under Vortex). Pure: OverlayService decides, the layer fades a veil over its own
    /// alpha. The base feature keeps running underneath (its toggle stays the master, its state
    /// stays "showing"), so the Super add-on that rides it never notices and comes back with it.
    /// </summary>
    public static class SuperStepAside
    {
        /// <summary>Fade out or back in at Full motion.</summary>
        public const double FadeSeconds = 0.45;
        /// <summary>Motion Off: the house 120 ms fade, never a one-frame cut.</summary>
        public const double OffFadeSeconds = 0.12;

        /// <summary>
        /// Should the base layer hide: Super really replaces it (<see cref="SuperAccess.ReplacesBase"/>)
        /// and nobody else is holding the overlay. A timed bubble pop, a voice "go pink", a remote
        /// command or a Deeper band put the same layer up on purpose; those keep showing.
        /// </summary>
        public static bool Hides(bool replacesBase, bool otherOwnerHolds) => replacesBase && !otherOwnerHolds;

        /// <summary>How long the veil takes end to end. Reduced = half speed.</summary>
        public static double FadeSecondsFor(MotionLevel level) => level switch
        {
            MotionLevel.Off => OffFadeSeconds,
            MotionLevel.Reduced => FadeSeconds * 2,
            _ => FadeSeconds,
        };

        /// <summary>One tick of the veil (1 = base fully drawn, 0 = stepped aside). Linear in time; draw through <see cref="Ease"/>.</summary>
        public static double Step(double veil, bool hidden, double dtSeconds, double fadeSeconds)
        {
            double target = hidden ? 0 : 1;
            veil = Math.Clamp(veil, 0, 1);
            if (dtSeconds <= 0) return veil;
            double move = fadeSeconds <= 0 ? 1 : dtSeconds / fadeSeconds;
            return veil < target ? Math.Min(target, veil + move) : Math.Max(target, veil - move);
        }

        /// <summary>Smoothstep: no snap at either end of the fade.</summary>
        public static double Ease(double veil)
        {
            veil = Math.Clamp(veil, 0, 1);
            return veil * veil * (3 - 2 * veil);
        }
    }
}
