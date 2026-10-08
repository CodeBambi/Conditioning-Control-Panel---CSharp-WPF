using System;

namespace ConditioningControlPanel.Fx
{
    /// <summary>
    /// The section edge's fog (nav polish wave 11, 2026-10-07): soft round puffs in the section
    /// hue that drift along a window-edge strip and breathe gently inward and outward, each fading
    /// in, living a few seconds and fading out. Two layers per strip give the depth: a slow layer
    /// of big faint puffs and a quicker layer of small brighter ones. Pure numbers and steps, no
    /// WPF and no Skia, so tests pin the budget, the band and the envelope without a canvas.
    ///
    /// <para>Units: every distance is in the strip's own DIPs (the strips sit outside the Viewbox,
    /// so a DIP is a native window pixel at 100%). <c>along</c> runs clockwise along the side like
    /// <see cref="EdgeDriftMath"/>; <c>depth</c> is the puff centre's distance in from the window's
    /// outer edge (it may sit a few px outside, so the puff reads as light leaking in).</para>
    /// </summary>
    public static class EdgeFogMath
    {
        /// <summary>Strip thickness the fog is authored for: the deepest puff edge stays inside it.</summary>
        public const double StripPx = 56;

        /// <summary>Big slow layer: size across the edge (px), along speed (px/s), peak alpha, life (s).</summary>
        public const double BigSizeMinPx = 34, BigSizeMaxPx = 60;
        public const double BigSpeedMinPx = 4, BigSpeedMaxPx = 9;
        public const double BigAlphaMin = 0.12, BigAlphaMax = 0.22;
        public const double BigLifeMin = 5.0, BigLifeMax = 9.0;

        /// <summary>Small quick layer: diameter, along speed (px/s), peak alpha, life (s).</summary>
        public const double SmallSizeMinPx = 18, SmallSizeMaxPx = 32;
        public const double SmallSpeedMinPx = 10, SmallSpeedMaxPx = 20;
        public const double SmallAlphaMin = 0.14, SmallAlphaMax = 0.22;
        public const double SmallLifeMin = 3.0, SmallLifeMax = 5.5;

        /// <summary>Puffs are soft ellipses drawn out ALONG the edge (a band of fog, not a row of
        /// dots): big ones 4 to 7 times longer than deep, so five of them nearly close a long
        /// side and the band breathes as they overlap and part; small ones 1.4 to 2.4 times.</summary>
        public const double BigStretchMin = 4.0, BigStretchMax = 7.0;
        public const double SmallStretchMin = 1.4, SmallStretchMax = 2.4;

        /// <summary>Inward / outward breathing: amplitude (px) and rate (radians per second).</summary>
        public const double BreathePxMin = 3, BreathePxMax = 8;
        public const double BreatheRateMin = 0.5, BreatheRateMax = 1.1;

        /// <summary>How far outside the window a puff centre may sit (negative depth).</summary>
        public const double DepthOutPx = 6;
        /// <summary>The shallowest a puff centre rests, as a share of its size: most of every puff
        /// stays in the window, and the breathing carries it toward the frame and back.</summary>
        public const double MinDepthShare = 0.22;
        /// <summary>A puff's inner edge keeps this much clear of the strip's inner edge.</summary>
        public const double InnerClearPx = 2;

        /// <summary>Share of each life spent fading in, and the same share fading out.</summary>
        public const double FadeShare = 0.35;
        /// <summary>Share of puffs that drift anticlockwise, so the fog shifts instead of marching.</summary>
        public const double CounterShare = 0.3;
        /// <summary>Share of spawns placed in the corner zones (the first and last tenth of a side).</summary>
        public const double CornerShare = 0.35, CornerZone = 0.10;

        /// <summary>Full counts per strip: a long side (top, bottom) and a short side (left, right).
        /// Four strips hold 2 x (5 + 5) + 2 x (4 + 3) = 34 puffs, beside the 24 embers (58).</summary>
        public const int BigLong = 5, SmallLong = 5, BigShort = 4, SmallShort = 3;
        /// <summary>Reduced motion: half the puffs at half the speed.</summary>
        public const double ReducedCount = 0.5, ReducedSpeed = 0.5;
        /// <summary>The share of the full counts a canvas keeps when its live budget is under the
        /// Quality tier's 60 (Balanced, or the governor halving it after hitches).</summary>
        public const double LeanShare = 0.6;
        /// <summary>The live budget at and above which the full counts hold.</summary>
        public const int FullBudget = 60;
        /// <summary>Seconds between spawns while a layer is under its target.</summary>
        public const double SpawnEverySeconds = 0.35;

        /// <summary>Dust (owner, 2026-10-07: "more particles, granular, like little dust"): tiny
        /// specks drifting through the fog, brighter than the puffs and lighter than the hue, each
        /// twinkling as it lives. Diameter, along speed (px/s), peak alpha, life (s).</summary>
        public const double DustSizeMinPx = 1.2, DustSizeMaxPx = 3.0;
        public const double DustSpeedMinPx = 5, DustSpeedMaxPx = 16;
        public const double DustAlphaMin = 0.55, DustAlphaMax = 1.0;
        public const double DustLifeMin = 2.5, DustLifeMax = 6.5;
        /// <summary>Sideways wander across the strip: amplitude (px) and rate (radians per second).</summary>
        public const double DustWanderPxMin = 1.5, DustWanderPxMax = 5;
        public const double DustWanderRateMin = 0.6, DustWanderRateMax = 1.8;
        /// <summary>Twinkle: the alpha swings this share around its envelope, at this rate (rad/s).</summary>
        public const double DustTwinkleShare = 0.45, DustTwinkleRateMin = 2.0, DustTwinkleRateMax = 6.0;
        /// <summary>Depth bias toward the frame: depth = strip x u^power (higher = more by the edge).</summary>
        public const double DustDepthPower = 2.6;
        /// <summary>The deepest band dust rests in (px from the frame): the owner wants it hugging the edge.</summary>
        public const double DustDepthSpanPx = 22;
        /// <summary>Specks wear the rail ring's vivid section colour (NavRailRules.Vivid), lifted only
        /// this far toward white so they still glint over the fog.</summary>
        public const double DustLift = 0.12;
        /// <summary>Full dust counts per strip (long side, short side): 2 x 90 + 2 x 56 = 292 specks.</summary>
        public const int DustLong = 90, DustShort = 56;
        /// <summary>Seconds between dust spawns while under target.</summary>
        public const double DustSpawnEverySeconds = 0.04;

        /// <summary>Dust alpha: peak x envelope x twinkle x gain, capped at 0.95.</summary>
        public static double DustAlpha(double peak, double age, double life, double twinkle, double gain)
        {
            double tw = 1.0 - DustTwinkleShare * 0.5 * (1.0 - Math.Sin(twinkle));
            return Math.Min(0.95, Math.Max(0, peak) * Envelope(age, life) * tw * Math.Clamp(gain, 0, 1.5));
        }

        /// <summary>A speck's resting depth: biased toward the frame, always inside the strip.</summary>
        public static double DustDepth(double u, double wanderPx) =>
            1 + wanderPx + Math.Pow(Math.Clamp(u, 0, 1), DustDepthPower) * Math.Min(DustDepthSpanPx, Math.Max(0, StripPx - 2 - 2 * wanderPx - 1));

        /// <summary>Puffs one layer may hold. Zero budget = zero puffs; under 60 the lean share;
        /// Reduced halves it (never below one while anything is allowed).</summary>
        public static int Target(int fullCount, int liveBudget, bool reduced)
        {
            if (fullCount <= 0 || liveBudget <= 0) return 0;
            double n = fullCount * (liveBudget >= FullBudget ? 1.0 : LeanShare);
            if (reduced) n *= ReducedCount;
            return Math.Max(1, (int)Math.Round(n, MidpointRounding.ToEven));
        }

        /// <summary>The full count for one layer on one side.</summary>
        public static int FullCount(bool big, bool longSide) =>
            big ? (longSide ? BigLong : BigShort) : (longSide ? SmallLong : SmallShort);

        /// <summary>Fade in over the first 35% of the life, hold, fade out over the last 35%
        /// (smoothstep on both ends, so a puff never pops). 0 outside the life.</summary>
        public static double Envelope(double age, double life)
        {
            if (life <= 0 || age <= 0 || age >= life) return 0;
            double t = age / life;
            double edge = Math.Min(t, 1.0 - t) / FadeShare;
            if (edge >= 1) return 1;
            return edge * edge * (3 - 2 * edge);
        }

        /// <summary>A puff's alpha: its peak x the envelope x the strip's gain, capped at the
        /// small layer's ceiling so no hue ever pushes a puff past 0.22.</summary>
        public static double Alpha(double peak, double age, double life, double gain) =>
            Math.Min(SmallAlphaMax, Math.Max(0, peak) * Envelope(age, life) * Math.Clamp(gain, 0, 1.5));

        /// <summary>The deepest a puff centre may sit before its inner edge leaves the strip.</summary>
        public static double MaxDepth(double sizePx, double breathePx) =>
            StripPx - sizePx / 2 - breathePx - InnerClearPx;

        /// <summary>The breathing centre depth at a phase.</summary>
        public static double Depth(double baseDepth, double breathePx, double phase) =>
            baseDepth + breathePx * Math.Sin(phase);

        /// <summary>Along distance after <paramref name="dt"/> at a signed speed.</summary>
        public static double Advance(double along, double speedPx, double dt) =>
            along + speedPx * Math.Max(0.0, dt);

        /// <summary>A puff is spent when its life is over or it has drifted a whole puff past
        /// either end of its side (<paramref name="spanPx"/> = its length along the edge).</summary>
        public static bool IsSpent(double age, double life, double along, double length, double spanPx) =>
            age >= life || along < -spanPx || along > length + spanPx;

        /// <summary>Element px of a puff on a strip of the given side, from its along and depth
        /// (the strip is <paramref name="w"/> by <paramref name="h"/> DIPs).</summary>
        public static (double X, double Y) Position(EdgeSide side, double along, double depth, double w, double h) => side switch
        {
            EdgeSide.Top => (along, depth),
            EdgeSide.Right => (w - depth, along),
            EdgeSide.Bottom => (w - along, h - depth),
            _ => (depth, h - along),
        };

        /// <summary>The side's length along the edge, from the strip's size.</summary>
        public static double Length(EdgeSide side, double w, double h) =>
            side is EdgeSide.Top or EdgeSide.Bottom ? w : h;

        /// <summary>Where a new puff starts: <paramref name="corner"/> &lt; CornerShare puts it in a
        /// corner zone (end chosen by <paramref name="u"/>), otherwise anywhere along the side.</summary>
        public static double SpawnAlong(double u, double corner, double length)
        {
            if (corner < CornerShare)
            {
                double zone = CornerZone * length;
                return u < 0.5 ? (u * 2) * zone : length - ((u - 0.5) * 2) * zone;
            }
            return u * length;
        }
    }
}
