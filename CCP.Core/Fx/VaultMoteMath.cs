using System;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Fx
{
    /// <summary>One place the Premium page's motes rise from: a card or a group sign, in the
    /// canvas's own coordinates. <paramref name="Diamond"/> = cyan Prime diamonds, otherwise round
    /// gold glitter. Colour is ARGB.</summary>
    public readonly record struct VaultZone(RectD Bounds, uint Color, bool Diamond);

    /// <summary>
    /// The numbers of the VaultMotes layer (polish 12 round 2, owner: "add more particles and flair
    /// to the Premium section"), ported from WPF 7.1.5 Controls/AmbientFxCanvas.Vault.cs. Full is
    /// the rich room; Reduced a few slow motes; Off none.
    /// </summary>
    public static class VaultMoteMath
    {
        /// <summary>Hard ceiling on motes alive at once, before the tier's own budget.</summary>
        public static int Cap(MotionLevel level) => level switch
        {
            MotionLevel.Full => 64,
            MotionLevel.Reduced => 14,
            _ => 0,
        };

        /// <summary>New motes a second while the page is short of its cap.</summary>
        public static double SpawnPerSecond(MotionLevel level) => level switch
        {
            MotionLevel.Full => 26,
            MotionLevel.Reduced => 4,
            _ => 0,
        };

        /// <summary>Rise speed multiplier: Reduced drifts at half pace.</summary>
        public static double SpeedScale(MotionLevel level) => level == MotionLevel.Full ? 1.0 : 0.5;

        /// <summary>Share of the motes that rise from a card or sign (the rest drift anywhere).</summary>
        public const double ZoneShare = 0.78;

        /// <summary>How far outside a zone's edge a mote may be born.</summary>
        public const double RingOut = 12;

        /// <summary>How far inside a zone's edge a mote may be born.</summary>
        public const double RingIn = 4;

        public const double LifeMin = 1.6, LifeMax = 3.4;

        /// <summary>A birth point on the ring around <paramref name="zone"/>: pick a side weighted by
        /// its length, a spot along it, and a depth across the edge. u1..u3 are 0..1 randoms.</summary>
        public static Vec2 SpawnOnRing(RectD zone, double u1, double u2, double u3)
        {
            double w = Math.Max(1, zone.Width), h = Math.Max(1, zone.Height);
            double across = -RingIn + u3 * (RingIn + RingOut);   // negative = inside
            double t = u1 * (2 * w + 2 * h);
            if (t < w) return new Vec2(zone.Left + u2 * w, zone.Top - across);
            t -= w;
            if (t < w) return new Vec2(zone.Left + u2 * w, zone.Bottom + across);
            t -= w;
            if (t < h) return new Vec2(zone.Left - across, zone.Top + u2 * h);
            return new Vec2(zone.Right + across, zone.Top + u2 * h);
        }

        /// <summary>The motes the page keeps alive: the level's cap, never above twice the tier's
        /// particle budget.</summary>
        public static int Target(MotionLevel level, int liveBudget) =>
            Math.Max(0, Math.Min(Cap(level), liveBudget * 2));
    }
}
