using System;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Super
{
    /// <summary>
    /// The feel of the gold v2 box and its quiet switch (juice round, 2026-10-01). Pure numbers:
    /// the controls only turn these into WPF animations. The owner found the switches too loud, so
    /// nothing here is louder at rest: every channel is a short tween around an event, or a rare
    /// ambient sheen, and all of it sizes down with motion.
    /// </summary>
    public static class SuperChromeJuice
    {
        /// <summary>Lit/dim and on/off colour + glow tween.</summary>
        public const int TweenMs = 220;

        /// <summary>The knob's thud starts as the slide arrives and lasts this long.</summary>
        public const int ThudDelayMs = 140;
        public const int ThudMs = 260;
        public const double ThudSquash = 0.08;   // along the slide at the peak
        public const double ThudBulge = 0.06;    // across it

        /// <summary>The lock glyph's pop on a refused click.</summary>
        public const int LockPopMs = 320;
        public const double LockPopPeak = 0.18;
        /// <summary>The ring's one gold blink on a refused click (a single swell, far under 3 Hz).</summary>
        public const int RingBlinkMs = 300;

        /// <summary>The supporter sign's sheen: one sweep every 8-13 s, 900 ms long, faint.</summary>
        public const int SheenMs = 900;
        public const double SheenMinDelay = 8;
        public const double SheenSpread = 5;
        public const double SheenPeak = 0.32;

        /// <summary>1 at Full, 0.5 Reduced, 0 Off: the amplitude every event channel is scaled by.</summary>
        public static double Amount(MotionLevel level) => level switch
        {
            MotionLevel.Off => 0,
            MotionLevel.Reduced => 0.5,
            _ => 1,
        };

        /// <summary>The thud as (time fraction, scale x, scale y) keys: flat along the slide,
        /// one small overshoot back, rest. Empty when motion is off.</summary>
        public static (double T, double X, double Y)[] ThudKeys(double amount)
        {
            if (amount <= 0) return Array.Empty<(double, double, double)>();
            double a = Math.Min(1, amount);
            return new[]
            {
                (0.0, 1.0, 1.0),
                (0.3, 1 - ThudSquash * a, 1 + ThudBulge * a),
                (0.65, 1 + 0.3 * ThudSquash * a, 1 - 0.3 * ThudBulge * a),
                (1.0, 1.0, 1.0),
            };
        }

        /// <summary>The lock glyph pop as (time fraction, scale) keys: up, a 4% dip, rest.</summary>
        public static (double T, double S)[] LockPopKeys(double amount)
        {
            if (amount <= 0) return Array.Empty<(double, double)>();
            double a = Math.Min(1, amount);
            return new[]
            {
                (0.0, 1.0),
                (0.3, 1 + LockPopPeak * a),
                (0.65, 1 - 0.04 * a),
                (1.0, 1.0),
            };
        }

        /// <summary>The sheen is ambient: only when ambient loops are allowed and motion is not off.</summary>
        public static bool SheenAllowed(bool ambientLoops, MotionLevel level)
            => ambientLoops && level != MotionLevel.Off;

        /// <summary>Seconds to the next sheen from a roll in [0,1): 8 to 13 s, never in step across boxes.</summary>
        public static double SheenDelay(double roll) => SheenMinDelay + SheenSpread * Math.Clamp(roll, 0, 1);

        /// <summary>Sheen band position across the sign, -0.4 (off left) to 1.4 (off right), eased in-out.</summary>
        public static double SheenOffset(double t)
        {
            t = Math.Clamp(t, 0, 1);
            double s = t * t * (3 - 2 * t);
            return -0.4 + 1.8 * s;
        }

        /// <summary>Tween duration for a lit/dim or on/off change: 220 ms, 120 ms fade when motion is off.</summary>
        public static int ChangeMs(MotionLevel level) => level == MotionLevel.Off ? 120 : TweenMs;
    }
}
