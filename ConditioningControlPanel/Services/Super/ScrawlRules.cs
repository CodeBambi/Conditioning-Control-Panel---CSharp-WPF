using System;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Super
{
    /// <summary>Which wall a bouncing word hit. A two-axis hit reports the side wall, as the mockup does.</summary>
    public enum ScrawlWall { None, Left, Right, Top, Bottom }

    /// <summary>The bounce field in virtual-desktop DIP (the same box the bouncing text uses).</summary>
    public readonly record struct ScrawlBounds(double MinX, double MinY, double MaxX, double MaxY)
    {
        public double Width => MaxX - MinX;
        public double Height => MaxY - MinY;
    }

    /// <summary>Where a stamp sits: its centre in virtual DIP and its rotation in radians.</summary>
    public readonly record struct StampPose(double X, double Y, double Rotation);

    /// <summary>
    /// Super Scrawl, the pure part: every number and rule from the approved mockup (`mkMantra`,
    /// handoff-1001/super-effects-mockup.html). Words stamp the walls where they hit, a corner
    /// gets a big gold stamp, a click slams a word at the screen centre and knocks the others
    /// away. WPF-free and App-free; ScrawlScene draws what this decides. Lengths are in units of
    /// the bouncing text's font size (fs) so the feel carries from the mockup's 34 px to any size.
    /// </summary>
    public static class ScrawlRules
    {
        // Stamps
        public const double StampScale = 1.15;          // x fs
        public const double CornerStampScale = 1.7;     // x fs
        public const double SlamStampScale = 3.6;       // x fs
        public const double MockupFont = 34;            // the mockup's fs, for scaling stroke widths
        public const double StrokeWidth = 1.6;          // at MockupFont
        public const double SlamStrokeWidth = 3.5;      // at MockupFont
        public const double FillAlpha = 0.14;
        public const double StrokeAlpha = 0.9;
        public const double DotAlpha = 0.7;
        public const double StampLife = 14.0;           // s, fades linearly
        public const double SlamStampLife = 1.4;        // s
        public const int StampCap = 44;
        public const int InkDots = 7;
        public const int SlamDots = 16;
        public const double Jitter = 0.07;              // rad, either way
        public const double WallInset = 0.85;           // x fs, stamp centre from the wall
        public const double AlongMargin = 1.6;          // x fs, kept clear of the corners
        public const double CornerTolerance = 1.4;      // x fs, both axes, for a corner hit
        public const double CornerTilt = 0.35;          // rad

        // Press-in: scale 1 + amp, back to 1 at rate per second
        public const double PressAmp = 0.5, PressRate = 8.0;          // 1.5 -> 1 over 0.125 s
        public const double SlamPressAmp = 0.55, SlamPressRate = 5.0;

        // Flare when a slam lands
        public const double PulseLife = 0.5, PulseGain = 0.7;

        // Glow, waves, sparks, label
        public const double GlowLife = 0.6, GlowRadius = 2.4, GlowAlpha = 0.55;   // radius x fs
        public const double WaveLife = 0.7, WaveLag = 0.08, WaveReach = 0.7;      // reach x screen width
        public const double FloatLife = 1.3;
        public const int CornerSparks = 46, CornerWhiteSparks = 18;
        public const int SlamMintSparks = 34, SlamPinkSparks = 20;

        // Slam
        public const double SlamTravel = 0.2;           // s to the centre
        public const double SlamHold = 0.45;            // s before the word bounces on
        public const double SlamPeak = 3.2;             // word scale at the centre
        public const double SlamPull = 14.0;            // per second, the move to the centre
        public const double KnockImpulse = 170.0;       // px/s in the mockup
        public const double RelaxSpeed = 115.0;         // px/s in the mockup
        public const double RelaxRate = 1.6;            // per second
        public const double ShakeSlam = 1.0, ShakeCorner = 0.5;
        public const double ShakeDecay = 1.0 / 0.35;    // a full shake dies in 0.35 s
        public const double ShakePixels = 9.0;          // at MockupFont

        /// <summary>The wall a hit reports: the side wall when x bounced, else the top/bottom wall.</summary>
        public static ScrawlWall WallOf(bool left, bool right, bool top, bool bottom)
        {
            if (left) return ScrawlWall.Left;
            if (right) return ScrawlWall.Right;
            if (top) return ScrawlWall.Top;
            if (bottom) return ScrawlWall.Bottom;
            return ScrawlWall.None;
        }

        /// <summary>A corner hit: the word's box is within 1.4 fs of a wall on BOTH axes.</summary>
        public static bool IsCorner(double left, double top, double right, double bottom, ScrawlBounds b, double fs)
        {
            double tol = CornerTolerance * fs;
            double dx = Math.Min(left - b.MinX, b.MaxX - right);
            double dy = Math.Min(top - b.MinY, b.MaxY - bottom);
            return dx < tol && dy < tol;
        }

        /// <summary>
        /// A wall stamp: flush on the wall it hit, kept 1.6 fs clear of the corners, top and bottom
        /// reading left to right, left wall turned -90 degrees and right wall +90, plus a little
        /// jitter (<paramref name="jitterUnit"/> in -1..1 maps to +-0.07 rad).
        /// </summary>
        public static StampPose PlaceStamp(ScrawlWall wall, double cx, double cy, ScrawlBounds b, double fs, double jitterUnit)
        {
            double jit = Math.Clamp(jitterUnit, -1, 1) * Jitter;
            double inset = WallInset * fs, margin = AlongMargin * fs;
            double x = ClampAlong(cx, b.MinX + margin, b.MaxX - margin);
            double y = ClampAlong(cy, b.MinY + margin, b.MaxY - margin);
            return wall switch
            {
                ScrawlWall.Top => new StampPose(x, b.MinY + inset, jit),
                ScrawlWall.Bottom => new StampPose(x, b.MaxY - inset, jit),
                ScrawlWall.Left => new StampPose(b.MinX + inset, y, -Math.PI / 2 + jit),
                ScrawlWall.Right => new StampPose(b.MaxX - inset, y, Math.PI / 2 + jit),
                _ => new StampPose(cx, cy, jit),
            };
        }

        /// <summary>The big gold corner stamp, tucked into the corner nearest the word, tilted along the diagonal.</summary>
        public static StampPose PlaceCornerStamp(double cx, double cy, ScrawlBounds b, double fs)
        {
            bool leftHalf = cx < (b.MinX + b.MaxX) / 2, topHalf = cy < (b.MinY + b.MaxY) / 2;
            double x = leftHalf ? b.MinX + 1.7 * fs : b.MaxX - 1.7 * fs;
            double y = topHalf ? b.MinY + 1.4 * fs : b.MaxY - 1.4 * fs;
            return new StampPose(x, y, leftHalf == topHalf ? -CornerTilt : CornerTilt);
        }

        /// <summary>The corner the word is nearest, inset 0.9 fs: where the gold sparks burst from.</summary>
        public static (double X, double Y) CornerPoint(double cx, double cy, ScrawlBounds b, double fs)
        {
            double m = 0.9 * fs;
            return (cx < (b.MinX + b.MaxX) / 2 ? b.MinX + m : b.MaxX - m,
                    cy < (b.MinY + b.MaxY) / 2 ? b.MinY + m : b.MaxY - m);
        }

        /// <summary>Where the radial wall glow sits: on the wall line, at the hit.</summary>
        public static (double X, double Y) GlowPoint(ScrawlWall wall, double cx, double cy, ScrawlBounds b) => wall switch
        {
            ScrawlWall.Left => (b.MinX, cy),
            ScrawlWall.Right => (b.MaxX, cy),
            ScrawlWall.Top => (cx, b.MinY),
            ScrawlWall.Bottom => (cx, b.MaxY),
            _ => (cx, cy),
        };

        /// <summary>Ink per word, stable for the same text: 0 pink, 1 mint, 2 lilac.</summary>
        public static int InkFor(string? word)
        {
            unchecked
            {
                int h = 17;
                foreach (char c in word ?? "") h = h * 31 + char.ToUpperInvariant(c);
                return (int)((uint)h % 3u);
            }
        }

        /// <summary>A stamp's opacity: linear fade over its life, flared up to x1.7 for 0.5 s after a slam (capped at 1).</summary>
        public static double StampAlpha(double age, double life, double pulseAge)
        {
            double a = Math.Clamp(1 - age / life, 0, 1);
            if (pulseAge >= 0) a = Math.Min(1, a * (1 + PulseGain * Math.Max(0, 1 - pulseAge / PulseLife)));
            return a;
        }

        /// <summary>The press-in scale: 1.5 down to 1 (slam stamp 1.55 slower). Reduced halves it, Off has none.</summary>
        public static double PressScale(double age, bool slam, MotionLevel motion)
        {
            if (motion == MotionLevel.Off) return 1;
            double amp = slam ? SlamPressAmp : PressAmp, rate = slam ? SlamPressRate : PressRate;
            if (motion == MotionLevel.Reduced) amp *= 0.5;
            return 1 + amp * Math.Max(0, 1 - age * rate);
        }

        /// <summary>Smoothstep, the mockup's ease.</summary>
        public static double Ease(double x)
        {
            x = Math.Clamp(x, 0, 1);
            return x * x * (3 - 2 * x);
        }

        /// <summary>The slammed word's scale: swells to 3.2 over 0.2 s, holds to 0.45 s, then 1. Off never swells.</summary>
        public static double SlamWordScale(double t, MotionLevel motion)
        {
            if (motion == MotionLevel.Off || t < 0 || t >= SlamHold) return 1;
            double peak = motion == MotionLevel.Reduced ? 1 + (SlamPeak - 1) * 0.5 : SlamPeak;
            return t >= SlamTravel ? peak : 1 + (peak - 1) * Ease(t / SlamTravel);
        }

        /// <summary>
        /// The push a slam gives another word: straight away from the slam point, at the mockup's
        /// 170 px/s scaled to that word's own cruise speed (the mockup cruised at 115).
        /// </summary>
        public static (double Dvx, double Dvy) Knock(double ox, double oy, double sx, double sy, double cruise)
        {
            double dx = ox - sx, dy = oy - sy, d = Math.Sqrt(dx * dx + dy * dy);
            if (d < 1e-6) { dx = 1; dy = 0; d = 1; }
            double impulse = KnockImpulse / RelaxSpeed * cruise;
            return (dx / d * impulse, dy / d * impulse);
        }

        /// <summary>The factor a too-fast word's velocity is multiplied by this frame to relax back to its cruise speed.</summary>
        public static double RelaxFactor(double speed, double cruise, double dt)
        {
            if (speed <= cruise * 1.02 || speed <= 0) return 1;
            return 1 - (speed - cruise) / speed * Math.Min(1, dt * RelaxRate);
        }

        /// <summary>How much shake a motion level allows: Full 1, Reduced 0.5, Off none.</summary>
        public static double ShakeGain(MotionLevel motion) => motion switch
        {
            MotionLevel.Off => 0,
            MotionLevel.Reduced => 0.5,
            _ => 1,
        };

        /// <summary>The shake level after dt: decays linearly so a full shake lasts 0.35 s.</summary>
        public static double DecayShake(double shake, double dt) => Math.Max(0, shake - dt * ShakeDecay);

        /// <summary>
        /// Ink dot offsets around a stamp, in stamp space: <paramref name="count"/> dots at a
        /// distance of near..far fs, flattened vertically (the mockup's 0.55 / 0.6), radius rMin..rMax.
        /// </summary>
        public static void Dots(Random rng, int count, double fs, double near, double far, double flatten,
            double rMin, double rMax, Span<(double X, double Y, double R)> into)
        {
            for (int i = 0; i < count && i < into.Length; i++)
            {
                double a = rng.NextDouble() * Math.PI * 2, d = fs * (near + rng.NextDouble() * (far - near));
                into[i] = (Math.Cos(a) * d, Math.Sin(a) * d * flatten, rMin + rng.NextDouble() * (rMax - rMin));
            }
        }

        private static double ClampAlong(double v, double lo, double hi) => lo > hi ? (lo + hi) / 2 : Math.Clamp(v, lo, hi);
    }
}
