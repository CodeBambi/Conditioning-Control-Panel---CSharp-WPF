using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Fx
{
    /// <summary>What one banner beat change does: pop the incoming line, flash the ring, run the
    /// sparkles (support beat only).</summary>
    public readonly record struct BannerBeatFx(bool Pop, bool Flash, bool Sparkles);

    /// <summary>One sparkle's flight along the support line, in sparkle-layer pixels.</summary>
    public readonly record struct SparkleFlight(double FromX, double ToX, double Y, double DelaySeconds,
                                                double Size, double Spin);

    /// <summary>
    /// PORTED from WPF 7.1.5 MainWindow/MainWindow.BannerFx.cs (BannerFxRules, nav polish wave 6
    /// + polish wave 13 drum). The header marquee is a SUNKEN drum: the beats roll one face per
    /// beat at Full motion only, the pill flashes and its halo breathes, and the support line gets
    /// a sparkle run. Numbers verbatim; the heads only draw.
    /// </summary>
    public static class BannerFxRules
    {
        public const double HostHeight = 31;
        public const double HostMaxWidth = 744;
        public const double BeatFontSize = 14;

        /// <summary>The incoming beat pops from this scale back to 1 (BackEase out, amplitude 0.6).</summary>
        public const double PopFrom = 0.96;
        public const int PopMs = 260;

        /// <summary>The ring flash: 0 -> 0.95 in 70 ms, back to 0 by FlashMs (quadratic out).</summary>
        public const int FlashMs = 400;
        public const int FlashPeakMs = 70;
        public const double FlashPeak = 0.95;

        /// <summary>The halo's breath (ambient loop, Full motion and window active only).</summary>
        public const double BreathMin = 0.25;
        public const double BreathMax = 0.6;
        public const double BreathSeconds = 3.2;
        public const double GlowRest = 0.35;

        /// <summary>The halo flare on a beat change: blur 12 -> 22 in 90 ms -> 12.</summary>
        public const double GlowBlur = 12;
        public const double GlowFlareBlur = 22;
        public const int GlowFlarePeakMs = 90;

        public const int SparkleCount = 7;
        public const double SparkleRunSeconds = 1.25;
        public const double SparkleStaggerSeconds = 0.09;
        public const double SparkleRepeatSeconds = 12;

        /// <summary>The drum roll: the old face rolls up and away, the new one rolls in from below.</summary>
        public const int RollMs = 500;
        public const double RollTravelPx = 13;
        public const double RollSquash = 0.3;
        public const double RollSettle = 0.3;

        /// <summary>The crossfade under the roll, and the rotation clock.</summary>
        public const int FadeMs = 500;
        public const double RotationSeconds = 4;

        /// <summary>The one-shot sheen over the pill: a pass, never more often than the gap.</summary>
        public const double SheenSeconds = 0.75;
        public const double SheenMinGapSeconds = 20;
        public const double SheenPeak = 0.22;

        /// <summary>The drum rolls only at Full motion; Reduced crossfades, Off swaps.</summary>
        public static bool Roll(MotionLevel level) => level == MotionLevel.Full;

        public static BannerBeatFx OnBeatChange(MotionLevel level, bool windowActive, bool isSupportBeat)
        {
            if (level == MotionLevel.Off || !windowActive) return default;
            return new BannerBeatFx(Pop: true, Flash: true, Sparkles: isSupportBeat);
        }

        public static bool RepeatSparkles(bool ambientAllowed, bool supportOnScreen) => ambientAllowed && supportOnScreen;

        public static bool Breathe(bool ambientAllowed) => ambientAllowed;

        /// <summary>The breath at time t (seconds): sine between BreathMin and BreathMax, half
        /// period BreathSeconds (auto-reversed).</summary>
        public static double BreathAt(double t) =>
            BreathMin + (BreathMax - BreathMin) * (0.5 - 0.5 * Math.Cos(Math.PI * t / BreathSeconds));

        /// <summary>The sheen gate: a forced pass always runs, otherwise not twice inside the gap.</summary>
        public static bool SheenDue(DateTime nowUtc, DateTime lastUtc, bool force) =>
            force || (nowUtc - lastUtc).TotalSeconds >= SheenMinGapSeconds;

        public static IReadOnlyList<SparkleFlight> PlanRun(double textLeft, double textWidth, double layerHeight,
                                                           int count, int seed)
        {
            var list = new List<SparkleFlight>();
            if (textWidth <= 4 || count <= 0) return list;
            var rng = new Random(seed);
            double mid = layerHeight / 2;
            for (int i = 0; i < count; i++)
            {
                double size = 11 + rng.NextDouble() * 7;              // 11-18 px sprites
                double lane = (rng.NextDouble() * 2 - 1) * Math.Min(7, layerHeight * 0.3);
                double from = textLeft - 6 + rng.NextDouble() * textWidth * 0.12;
                double to = textLeft + textWidth * (0.82 + rng.NextDouble() * 0.2);
                double spin = (rng.NextDouble() < 0.5 ? -1 : 1) * (90 + rng.NextDouble() * 120);
                list.Add(new SparkleFlight(from, to, mid + lane, i * SparkleStaggerSeconds, size, spin));
            }
            return list;
        }

        /// <summary>The four-point star the sparkle sprites draw, on a unit box.</summary>
        public const string SparkleStarPath =
            "M 0.5,0 C 0.55,0.4 0.6,0.45 1,0.5 C 0.6,0.55 0.55,0.6 0.5,1 "
            + "C 0.45,0.6 0.4,0.55 0,0.5 C 0.4,0.45 0.45,0.4 0.5,0 Z";

        /// <summary>A sparkle's twinkle scale at fraction u of its run (keyframes 0 / 1 / 0.5 / 1.1 / 0).</summary>
        public static double TwinkleAt(double u) => Keys(u, (0, 0), (0.15, 1), (0.4, 0.5), (0.65, 1.1), (1, 0));

        /// <summary>A sparkle's opacity at fraction u of its run (in by 12%, out after 80%).</summary>
        public static double AlphaAt(double u) => Keys(u, (0, 0), (0.12, 1), (0.8, 1), (1, 0));

        private static double Keys(double u, params (double at, double v)[] keys)
        {
            if (u <= keys[0].at) return keys[0].v;
            for (int i = 1; i < keys.Length; i++)
            {
                if (u <= keys[i].at)
                {
                    var (a0, v0) = keys[i - 1];
                    var (a1, v1) = keys[i];
                    var k = a1 > a0 ? (u - a0) / (a1 - a0) : 1;
                    return v0 + (v1 - v0) * k;
                }
            }
            return keys[^1].v;
        }
    }
}
