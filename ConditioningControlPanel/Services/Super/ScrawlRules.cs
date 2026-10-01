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
        public const double ShakeDecay = 3.5;           // per second, the mockup's: a full shake dies in about 0.29 s
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

        // Leftover stamps (owner: afterglow and a little melt). A glow that breathes after landing, a
        // one-off ghost echo, then a slow sag with drips in the second half of the stamp's life.
        public const double MeltStart = 0.4;            // share of the life before the melt begins
        public const double MeltSag = 0.32;             // x fs, how far the stamp sinks at full melt
        public const double MeltStretch = 0.55;         // extra height at full melt
        public const double EchoLife = 1.1, EchoGrow = 0.14, EchoAlpha = 0.35;
        public const int MaxDrips = 4;
        public const double DripDelaySpread = 0.25;     // share of the life the drips start over
        public const double DripReach = 1.3;            // x fs a drip travels over its life

        /// <summary>Leftover motion factor: Off 0 (still), Reduced half, Full 1.</summary>
        public static double LeftoverMotion(MotionLevel motion) =>
            motion == MotionLevel.Off ? 0 : motion == MotionLevel.Reduced ? 0.5 : 1;

        /// <summary>Halo strength 0..1: brightest at landing, settles to a slow breath. Off holds a steady 0.35.</summary>
        public static double AfterglowStrength(double age, double life, MotionLevel motion, double phase = 0)
        {
            if (motion == MotionLevel.Off) return 0.35;
            double settle = 0.3 + 0.7 * Math.Exp(-age * 1.1);
            double breath = 1 + 0.18 * LeftoverMotion(motion) * Math.Sin(age * 2.2 + phase);
            return Math.Clamp(settle * breath, 0, 1);
        }

        /// <summary>The ghost echo right after landing: grows by 14 percent and fades over 1.1 s. False when none.</summary>
        public static bool EchoAt(double age, MotionLevel motion, out double scale, out double alpha)
        {
            scale = 1; alpha = 0;
            if (motion == MotionLevel.Off || age < 0 || age >= EchoLife) return false;
            double u = age / EchoLife;
            scale = 1 + EchoGrow * LeftoverMotion(motion) * Ease(u);
            alpha = EchoAlpha * (1 - u);
            return true;
        }

        /// <summary>How melted a stamp is, 0..1: nothing until 40 percent of its life, then a smooth sag.</summary>
        public static double MeltAmount(double age, double life, MotionLevel motion)
        {
            if (motion == MotionLevel.Off || life <= 0) return 0;
            return Ease((age / life - MeltStart) / (1 - MeltStart));
        }

        /// <summary>Drip travel 0..1 for one drip (delay is 0..1 of DripDelaySpread); 0 until it starts.</summary>
        public static double DripProgress(double age, double life, double delay01, MotionLevel motion)
        {
            if (motion == MotionLevel.Off || life <= 0) return 0;
            double start = (MeltStart + delay01 * DripDelaySpread) * life;
            return Math.Clamp((age - start) / Math.Max(0.001, life - start), 0, 1);
        }

        // Juice round: the stamp lands with a squash and one springy settle, bounces throw sparks in
        // the word's ink, a landed stamp splats a few ink drops, leftovers breathe and fade ease-in.
        public const double LandSquash = 0.07;          // flatten along the wall normal at contact
        public const double LandWiden = 0.6;            // across = 1 + squash x this (area roughly kept)
        public const double SlamSquashGain = 1.15;
        public const double SettleLife = 0.32, SettleDamp = 9.0, SettleFreq = 26.0;
        public const int BounceSparks = 6, SplatSparks = 7;
        public const double BounceSparkSpeed = 150, BounceSparkLife = 0.32, BounceSparkCone = 0.9;  // cone: half-angle, rad
        public const double SplatSpeed = 95, SplatLife = 0.42, SplatFlatten = 0.45, SplatGravity = 260;
        public const double GlowIn = 0.06;              // s for the wall glow to come up
        public const double BreathAmp = 0.018, BreathHz = 0.28, BreathDelay = 0.4, BreathRamp = 0.8;
        public const double DripWobble = 0.06, DripWobbleHz = 1.7;   // x fs, sideways sway of a falling drip
        public const double LabelIn = 0.34, LabelFrom = 0.6, LabelAlphaIn = 0.08;

        /// <summary>When the press-in meets the wall, in seconds after the hit.</summary>
        public static double ContactTime(bool slam) => 1 / (slam ? SlamPressRate : PressRate);

        /// <summary>
        /// The landing punch in stamp space: an ease-in drop from the press-in scale to the wall
        /// (the stamp accelerates into it), then at contact a flatten along the wall normal with a
        /// widen across it that springs back once. Along = the wall-normal axis, Across = the text axis.
        /// Reduced halves every amplitude; Off is still.
        /// </summary>
        public static (double Along, double Across) LandPunch(double age, bool slam, MotionLevel motion)
        {
            if (motion == MotionLevel.Off || age < 0) return (1, 1);
            double m = LeftoverMotion(motion), dur = ContactTime(slam);
            if (age < dur)
            {
                double u = age / dur, s = 1 + (slam ? SlamPressAmp : PressAmp) * m * (1 - u * u);
                return (s, s);
            }
            double v = age - dur;
            if (v >= SettleLife) return (1, 1);
            double k = Math.Exp(-SettleDamp * v) * Math.Cos(SettleFreq * v) * (1 - v / SettleLife);
            double sq = LandSquash * m * (slam ? SlamSquashGain : 1);
            return (1 - sq * k, 1 + sq * LandWiden * k);
        }

        /// <summary>A leftover stamp's opacity: an ease-in fade (holds, then lets go), flared after a slam like <see cref="StampAlpha"/>.</summary>
        public static double LeftoverAlpha(double age, double life, double pulseAge)
        {
            double u = Math.Clamp(age / life, 0, 1), a = 1 - u * u;
            if (pulseAge >= 0) a = Math.Min(1, a * (1 + PulseGain * Math.Max(0, 1 - pulseAge / PulseLife)));
            return a;
        }

        /// <summary>The wall glow's opacity: up over 60 ms (ease-out), then falls off fast-first. Never one frame on.</summary>
        public static double GlowAlphaAt(double age)
        {
            if (age < 0 || age >= GlowLife) return 0;
            if (age < GlowIn) { double r = 1 - age / GlowIn; return 1 - r * r; }
            double f = 1 - (age - GlowIn) / (GlowLife - GlowIn);
            return f * f;
        }

        /// <summary>Spark count for a motion level: Off none, Reduced half (rounded up), Full all.</summary>
        public static int SparkCount(int n, MotionLevel motion) =>
            motion == MotionLevel.Off ? 0 : motion == MotionLevel.Reduced ? (n + 1) / 2 : n;

        /// <summary>The direction a bounce spark flies: into the field off the wall it hit, inside a cone (<paramref name="unit"/> -1..1).</summary>
        public static double BounceSparkAngle(ScrawlWall wall, double unit)
        {
            double n = wall switch
            {
                ScrawlWall.Left => 0,
                ScrawlWall.Right => Math.PI,
                ScrawlWall.Top => Math.PI / 2,     // screen y grows down
                _ => -Math.PI / 2,
            };
            return n + Math.Clamp(unit, -1, 1) * BounceSparkCone;
        }

        /// <summary>Idle breath of a leftover stamp (uniform scale), phased per stamp, eased in once the landing has settled. Off is still.</summary>
        public static double IdleBreath(double age, double phase, MotionLevel motion)
        {
            if (motion == MotionLevel.Off) return 1;
            double ramp = Ease((age - BreathDelay) / BreathRamp);
            return 1 + BreathAmp * LeftoverMotion(motion) * ramp * Math.Sin(2 * Math.PI * BreathHz * age + phase);
        }

        /// <summary>A falling drip's sideways sway in fs: grows in over the first quarter of the fall. Off is still.</summary>
        public static double DripSway(double age, double phase, double t, MotionLevel motion)
        {
            if (motion == MotionLevel.Off || t <= 0) return 0;
            return DripWobble * LeftoverMotion(motion) * Math.Min(1, t * 4) * Math.Sin(2 * Math.PI * DripWobbleHz * age + phase);
        }

        /// <summary>The CORNER label's entrance: from 0.6 to 1 with a small back-ease overshoot (about 4 percent). Reduced starts at 0.8, Off at 1.</summary>
        public static double LabelScale(double age, MotionLevel motion)
        {
            if (motion == MotionLevel.Off || age >= LabelIn) return 1;
            double from = motion == MotionLevel.Reduced ? 1 - (1 - LabelFrom) / 2 : LabelFrom;
            double u = Math.Clamp(age / LabelIn, 0, 1) - 1;
            const double c1 = 1.70158, c3 = c1 + 1;
            return from + (1 - from) * (1 + c3 * u * u * u + c1 * u * u);
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
        public static (double Dvx, double Dvy) Knock(double ox, double oy, double sx, double sy, double cruise,
            MotionLevel motion = MotionLevel.Full)
        {
            double dx = ox - sx, dy = oy - sy, d = Math.Sqrt(dx * dx + dy * dy);
            if (d < 1e-6) { dx = 1; dy = 0; d = 1; }
            double impulse = KnockImpulse / RelaxSpeed * cruise * ShakeGain(motion);
            return (dx / d * impulse, dy / d * impulse);
        }

        /// <summary>The factor a too-fast word's velocity is multiplied by this frame to relax back to its cruise speed.</summary>
        public static double RelaxFactor(double speed, double cruise, double dt)
        {
            if (speed <= cruise * 1.02 || speed <= 0) return 1;
            return 1 - (speed - cruise) / speed * Math.Min(1, dt * RelaxRate);
        }

        /// <summary>How much shake (and knock) a motion level allows: Full 1, Reduced 0.5, Off none. Photosafe never shakes.</summary>
        public static double ShakeGain(MotionLevel motion, bool photosafe = false) => photosafe ? 0 : motion switch
        {
            MotionLevel.Off => 0,
            MotionLevel.Reduced => 0.5,
            _ => 1,
        };

        /// <summary>
        /// May a left press on a word be swallowed and turned into a slam? Only when it lands on a
        /// word AND the window that would get the click is not CCP's own input surface. A click on
        /// any CCP window (the panel, Lockdown, a lock card, a leash window, an attention check, the
        /// panic button) always goes through, so a slam can never block an exit. Click-through
        /// overlays (the words themselves, the compositor layers) are not input surfaces.
        /// </summary>
        public static bool MaySwallow(bool onWord, bool targetIsOurs, bool targetClickThrough)
            => onWord && !(targetIsOurs && !targetClickThrough);

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
