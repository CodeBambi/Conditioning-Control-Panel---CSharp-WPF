using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Super
{
    /// <summary>One word popped near the cursor. Lives a split second.</summary>
    public sealed class AfterglowPop
    {
        public string Text = "";
        /// <summary>Centre of the word now, world (virtual-desktop) px.</summary>
        public double X, Y, Vx, Vy;
        /// <summary>Field clock (s) at birth.</summary>
        public double Born;
        /// <summary>Physical px per DIP on the screen it was born on. Every distance scales off it.</summary>
        public double Scale = 1;
        /// <summary>Glow colour: 0 = the player's colour 1, 1 = colour 2 (AfterglowColorA / B).</summary>
        public int Hue;
        /// <summary>Slight tilt in radians the word is drawn at (burst words). 0 for a single pop.</summary>
        public double Tilt;
        /// <summary>Trail ring buffer: recent centres, newest at <see cref="TrailHead"/>.</summary>
        public readonly double[] TrailX = new double[AfterglowField.TrailLen];
        public readonly double[] TrailY = new double[AfterglowField.TrailLen];
        public int TrailCount, TrailHead;
        internal double TrailClock, PathAcc;
        /// <summary>Juice: where the cursor was against the word at birth (it arrives from there), px.</summary>
        public double FromDx, FromDy;
        /// <summary>Juice: unit direction the word drifts off along as it fades (its momentum, or up at rest).</summary>
        public double ExitUx, ExitUy = -1;
        internal double ExitDone;
        /// <summary>The layer's draw cache (measured runs). Never read by the maths.</summary>
        public object? Payload;

        /// <summary>The k-th echo back along the path (0 = the most recent one).</summary>
        public void TrailAt(int k, out double x, out double y)
        {
            int i = ((TrailHead - k) % AfterglowField.TrailLen + AfterglowField.TrailLen) % AfterglowField.TrailLen;
            x = TrailX[i]; y = TrailY[i];
        }

        internal void Record(double x, double y)
        {
            TrailHead = (TrailHead + 1) % AfterglowField.TrailLen;
            TrailX[TrailHead] = x; TrailY[TrailHead] = y;
            if (TrailCount < AfterglowField.TrailLen) TrailCount++;
        }
    }

    /// <summary>A spark. Struct, pooled: no allocation per frame.</summary>
    public struct AfterglowParticle
    {
        public double X, Y, Vx, Vy, T, Life, Size;
        public int Hue;
    }

    /// <summary>
    /// Super Afterglow, the maths. While subliminals run, a random word from the active pool pops up
    /// near the mouse for a split second, keeps a little of the mouse's momentum, leaves a short trail
    /// of echoes and sheds sparks. Distances are DIP and multiplied by the screen's scale.
    ///
    /// WPF-free, Skia-free, App-free. The layer draws what this leaves in <see cref="Pops"/> and
    /// <see cref="Particles"/>; the driver feeds it the cursor and decides when to spawn.
    /// </summary>
    public sealed class AfterglowField
    {
        /// <summary>How long words stay, as a multiple of the shipped life (the Duration slider). 1 = as shipped.</summary>
        public double DurationFactor = 1.0;

        // Lifecycle (owner: "for just a split second").
        public const double FadeInS = 0.06, HoldS = 0.14, FadeOutS = 0.22;      // 0.42 s total
        public const double OffFadeS = 0.12;                                     // MotionFx Off
        public const double SafeInS = 0.12, SafeHoldS = 0.1, SafeOutS = 0.5;     // photosafe: soft, no snap
        public const double IntervalMinS = 1.5, IntervalMaxS = 4.5;
        public const int MaxAlive = 2;
        public const double FontDip = 40;
        // Placement: a random spot this far from the cursor, clamped on screen.
        public const double OffsetMinDip = 50, OffsetMaxDip = 160;
        // Momentum: smoothed cursor velocity, a share of it handed to the word, then drag.
        public const double VelocityTauS = 0.08;
        public const double MaxCursorSpeedPx = 6000;
        public const double TeleportGapS = 0.25;
        public const double Inherit = 0.6;
        public const double Drag = 5;                   // v *= exp(-5 dt): about 0.15 s half-life
        public const double JitterDip = 30;             // +-15 DIP/s nudge so a still mouse is not dead still
        // Trail.
        public const int TrailLen = 6;
        public const double TrailStepS = 0.025;
        // Sparks.
        public const int BirthBurst = 10;
        public const double BurstSpeedMinDip = 40, BurstSpeedSpanDip = 90;
        public const double SparkEveryDip = 14;         // one spark per 14 DIP travelled
        public const int MaxPathSparksPerStep = 4;
        public const double SparkInherit = 0.35, SparkSpreadDip = 60;
        public const double SparkLifeMin = 0.3, SparkLifeSpan = 0.3;
        public const double SparkDrag = 3;
        public const int MaxParticles = 160;
        // Juice (owner juice round 2026-10-01): feel only. Never moves life, count, size or placement.
        public const double ArriveS = 0.18;             // THUD arrival, shortened with a short Duration
        public const double ArriveFrom = 0.6;           // scale at birth
        public const double ArriveBack = 2.2;           // back-ease: one swing, about 6% over size
        public const double FromCursorShare = 0.35;     // starts this share of the way back toward the cursor
        public const double ExitShrink = 0.12;          // scale lost over the fade-out, ease-in
        public const double ExitDriftDip = 28;          // drifted along the momentum over the fade-out, ease-in
        public const double ExitRestSpeedDip = 20;      // slower than this the word rises instead
        public const double HaloS = 0.22, HaloPeak = 0.28, HaloRise = 0.15;
        public const double ChromaS = 0.14, ChromaDip = 4, ChromaPeak = 0.45;
        public const double SparkGravityDip = 240;      // DIP/s^2, sparks fall a little
        public const double StreakS = 0.02, StreakMaxDip = 10;
        public const double TrailShrinkStep = 0.05;

        /// <summary>Pops alive at once; the option box raises it with the burst size.</summary>
        public int Capacity = MaxAlive;

        public readonly List<AfterglowPop> Pops = new();
        public readonly AfterglowParticle[] Particles = new AfterglowParticle[MaxParticles];
        public int ParticleCount;

        private double _cvx, _cvy, _lastX, _lastY;
        private bool _hasLast;

        /// <summary>Seconds since the field started; advanced only by <see cref="Step"/>.</summary>
        public double Now { get; private set; }

        public bool IsEmpty => Pops.Count == 0 && ParticleCount == 0;

        public double CursorVx => _cvx;
        public double CursorVy => _cvy;

        /// <summary>Half speed and distance at Reduced, none at Off (CONTRACT rule 3).</summary>
        public static double MotionScale(MotionLevel level) =>
            level == MotionLevel.Off ? 0 : level == MotionLevel.Reduced ? 0.5 : 1.0;

        /// <summary>Seconds until the next pop, 1.5..4.5. <paramref name="r01"/> in [0,1).</summary>
        public static double NextInterval(double r01) => IntervalMinS + Math.Clamp(r01, 0, 1) * (IntervalMaxS - IntervalMinS);

        /// <summary>Total life of a pop under these settings.</summary>
        public static double Life(MotionLevel level, bool photosafe, double durationFactor = 1.0)
        {
            if (level == MotionLevel.Off) return OffFadeS;                 // Off stays one 120 ms fade
            double f = durationFactor > 0 ? durationFactor : 1.0;
            if (photosafe) return (SafeInS + SafeHoldS + SafeOutS) * f;
            return (FadeInS + HoldS + FadeOutS) * f;
        }

        /// <summary>Alpha envelope 0..1. Off = a single 120 ms fade; photosafe = slow in, 0.5 s out.</summary>
        public static double Alpha(double age, MotionLevel level, bool photosafe, double durationFactor = 1.0)
        {
            if (age < 0) return 0;
            double inS, holdS, outS;
            double f = durationFactor > 0 ? durationFactor : 1.0;
            if (level == MotionLevel.Off) return Math.Clamp(1 - age / OffFadeS, 0, 1);
            if (photosafe) { inS = SafeInS * f; holdS = SafeHoldS * f; outS = SafeOutS * f; }
            else { inS = FadeInS * f; holdS = HoldS * f; outS = FadeOutS * f; }
            if (age < inS) return age / inS;
            if (age < inS + holdS) return 1;
            return Math.Clamp(1 - (age - inS - holdS) / outS, 0, 1);
        }

        /// <summary>Echo k (0 = newest) alpha, before the pop's own alpha.</summary>
        public static double TrailAlpha(int k) => k < 0 || k >= TrailLen ? 0 : 0.5 * (1 - Smooth((k + 1.0) / (TrailLen + 1)));

        /// <summary>Echo k (0 = newest) size against the word: each one a little smaller.</summary>
        public static double TrailScale(int k) => Math.Max(0.6, 1 - TrailShrinkStep * (Math.Max(0, k) + 1));

        private static double Smooth(double x) { x = Math.Clamp(x, 0, 1); return x * x * (3 - 2 * x); }
        private static double EaseOutCubic(double u) { u = 1 - Math.Clamp(u, 0, 1); return 1 - u * u * u; }

        /// <summary>Back-ease out: 0 to 1 with one swing past 1 (about 15% at s 2.2), then home.</summary>
        public static double BackOut(double u, double s)
        {
            double v = Math.Clamp(u, 0, 1) - 1;
            return 1 + (s + 1) * v * v * v + s * v * v;
        }

        private static double Amp(MotionLevel level) => level == MotionLevel.Reduced ? 0.5 : 1;

        /// <summary>Arrival time: <see cref="ArriveS"/>, shortened with a Duration under the default so it fits the life.</summary>
        public static double ArriveTime(double durationFactor) => ArriveS * Math.Min(1, durationFactor > 0 ? durationFactor : 1);

        /// <summary>Arrival curve 0..1: THUD (one swing over) at Full, half the swing at Reduced, plain ease-out photosafe.</summary>
        public static double Arrive(double age, MotionLevel level, bool photosafe, double durationFactor = 1.0)
        {
            double u = age / ArriveTime(durationFactor);
            if (photosafe) return EaseOutCubic(u);
            double back = BackOut(u, ArriveBack);
            return level == MotionLevel.Reduced ? (back + EaseOutCubic(u)) / 2 : back;
        }

        /// <summary>Fade-out progress 0..1 (0 before the out phase). Off has no out phase of its own.</summary>
        public static double OutProgress(double age, MotionLevel level, bool photosafe, double durationFactor = 1.0)
        {
            if (level == MotionLevel.Off) return 0;
            double f = durationFactor > 0 ? durationFactor : 1.0;
            double start = (photosafe ? SafeInS + SafeHoldS : FadeInS + HoldS) * f;
            double len = (photosafe ? SafeOutS : FadeOutS) * f;
            return Math.Clamp((age - start) / len, 0, 1);
        }

        /// <summary>
        /// Word size against its set size: grows in from 0.6 with one small overshoot (about 6% at Full),
        /// then shrinks a little as it leaves (ease-in). Reduced = half of both; Off = 1 always.
        /// </summary>
        public static double PopScale(double age, MotionLevel level, bool photosafe, double durationFactor = 1.0)
        {
            if (level == MotionLevel.Off) return 1;
            double amp = Amp(level);
            double s = 1 - (1 - ArriveFrom) * amp * (1 - Arrive(Math.Max(0, age), level, photosafe, durationFactor));
            double o = OutProgress(age, level, photosafe, durationFactor);
            return s * (1 - ExitShrink * amp * o * o);
        }

        /// <summary>Share of the birth offset back toward the cursor the word still sits at: 0.35 at birth, 0 once arrived.</summary>
        public static double FromCursor(double age, MotionLevel level, bool photosafe, double durationFactor = 1.0)
        {
            if (level == MotionLevel.Off) return 0;
            return FromCursorShare * Amp(level) * (1 - EaseOutCubic(Math.Max(0, age) / ArriveTime(durationFactor)));
        }

        /// <summary>Burst word tilt against its target: swings in with the arrival. Off = straight (the layer zeroes it).</summary>
        public static double TiltSwing(double age, MotionLevel level, bool photosafe, double durationFactor = 1.0)
            => level == MotionLevel.Off ? 1 : Arrive(Math.Max(0, age), level, photosafe, durationFactor);

        /// <summary>Soft tinted halo behind the word at the pop instant: quick rise, ease-out. None at Off or photosafe.</summary>
        public static double HaloAlpha(double age, MotionLevel level, bool photosafe)
        {
            if (level == MotionLevel.Off || photosafe || age < 0 || age >= HaloS) return 0;
            double u = age / HaloS;
            double a = u < HaloRise ? u / HaloRise : Math.Pow(1 - (u - HaloRise) / (1 - HaloRise), 2);
            return HaloPeak * Amp(level) * a;
        }

        /// <summary>Halo size against the word: opens from 0.7 to 1.3 over its life.</summary>
        public static double HaloSpread(double age) => 0.7 + 0.6 * EaseOutCubic(age / HaloS);

        /// <summary>Chromatic split at the pop instant, px per DIP of scale: the two glow colours pull apart and close. None at Off or photosafe.</summary>
        public static double ChromaOffset(double age, MotionLevel level, bool photosafe)
        {
            if (level == MotionLevel.Off || photosafe || age < 0 || age >= ChromaS) return 0;
            double r = 1 - age / ChromaS;
            return ChromaDip * Amp(level) * r * r;
        }

        /// <summary>Alpha of the two split copies, on top of the word's own alpha.</summary>
        public static double ChromaAlpha(double age, MotionLevel level, bool photosafe)
            => ChromaOffset(age, level, photosafe) <= 0 ? 0 : ChromaPeak * Amp(level) * (1 - age / ChromaS);

        /// <summary>Spark heat 1 at birth (hot, near white) to 0 at death (the glow colour), ease-out.</summary>
        public static double SparkHeat(in AfterglowParticle p)
        {
            double x = p.Life <= 0 ? 1 : Math.Clamp(1 - p.T / p.Life, 0, 1);
            return x * x;
        }

        /// <summary>
        /// Where a pop goes: <paramref name="r1"/> picks the angle, <paramref name="r2"/> the distance
        /// (50..160 DIP, half at Reduced), clamped so the word (half size <paramref name="halfW"/> x
        /// <paramref name="halfH"/>) stays inside the screen. A screen too small to fit it centres it.
        /// </summary>
        public static void SpawnPoint(double cx, double cy, double scale, double left, double top, double right, double bottom,
            double halfW, double halfH, MotionLevel level, double r1, double r2, out double x, out double y)
        {
            double k = level == MotionLevel.Reduced ? 0.5 : 1;
            double ang = r1 * Math.PI * 2;
            double dist = (OffsetMinDip + Math.Clamp(r2, 0, 1) * (OffsetMaxDip - OffsetMinDip)) * scale * k;
            x = ClampSpan(cx + Math.Cos(ang) * dist, left + halfW, right - halfW);
            y = ClampSpan(cy + Math.Sin(ang) * dist, top + halfH, bottom - halfH);
        }

        private static double ClampSpan(double v, double lo, double hi) => lo > hi ? (lo + hi) / 2 : Math.Clamp(v, lo, hi);

        /// <summary>
        /// Feed one cursor sample. Velocity is smoothed (tau 80 ms) so a jerk does not fling the word;
        /// a gap over 0.25 s (or the first sample) restarts from rest.
        /// </summary>
        public void SampleCursor(double dt, double x, double y)
        {
            if (!_hasLast || dt <= 0 || dt > TeleportGapS)
            {
                if (!_hasLast || dt > TeleportGapS) { _cvx = 0; _cvy = 0; }
                _lastX = x; _lastY = y; _hasLast = true;
                return;
            }
            double ix = (x - _lastX) / dt, iy = (y - _lastY) / dt;
            double sp = Math.Sqrt(ix * ix + iy * iy);
            if (sp > MaxCursorSpeedPx) { ix *= MaxCursorSpeedPx / sp; iy *= MaxCursorSpeedPx / sp; }
            double a = 1 - Math.Exp(-dt / VelocityTauS);
            _cvx += (ix - _cvx) * a;
            _cvy += (iy - _cvy) * a;
            _lastX = x; _lastY = y;
        }

        public void ResetCursor() { _hasLast = false; _cvx = 0; _cvy = 0; }

        /// <summary>
        /// Pop a word at (<paramref name="x"/>, <paramref name="y"/>). Over <see cref="Capacity"/>, the
        /// oldest goes. It takes 60% of the smoothed cursor velocity (30% at Reduced, none at Off or
        /// photosafe for the jitter) and, at Full and Reduced without photosafe, a small birth burst.
        /// </summary>
        public AfterglowPop Spawn(string text, double x, double y, double scale, MotionLevel level, bool photosafe, Func<double> rnd,
            double fromX = double.NaN, double fromY = double.NaN)
        {
            while (Pops.Count >= Math.Max(1, Capacity)) Pops.RemoveAt(0);
            double k = MotionScale(level);
            scale = scale <= 0 ? 1 : scale;
            var p = new AfterglowPop
            {
                Text = text, X = x, Y = y, Born = Now, Scale = scale,
                Vx = _cvx * Inherit * k, Vy = _cvy * Inherit * k,
                Hue = rnd() < 0.5 ? 0 : 1
            };
            if (k > 0 && !photosafe)
            {
                p.Vx += (rnd() - 0.5) * JitterDip * scale * k;
                p.Vy += (rnd() - 0.5) * JitterDip * scale * k;
            }
            p.Record(x, y);
            if (!double.IsNaN(fromX) && !double.IsNaN(fromY)) { p.FromDx = fromX - x; p.FromDy = fromY - y; }
            double vs = Math.Sqrt(p.Vx * p.Vx + p.Vy * p.Vy);
            if (vs > ExitRestSpeedDip * scale) { p.ExitUx = p.Vx / vs; p.ExitUy = p.Vy / vs; }
            Pops.Add(p);

            int burst = photosafe || k == 0 ? 0 : (int)(BirthBurst * k);
            for (int i = 0; i < burst; i++)
            {
                double ang = rnd() * Math.PI * 2;
                double sp = (BurstSpeedMinDip + rnd() * BurstSpeedSpanDip) * scale * k;
                Emit(x, y, Math.Cos(ang) * sp + p.Vx * SparkInherit, Math.Sin(ang) * sp + p.Vy * SparkInherit,
                    SparkLifeMin + rnd() * SparkLifeSpan, scale, (p.Hue + i) % 2);
            }
            return p;
        }

        /// <summary>Panic, stop, switch off: everything goes, at once.</summary>
        public void Clear()
        {
            Pops.Clear();
            ParticleCount = 0;
        }

        /// <summary>Advance by <paramref name="dt"/> seconds. <paramref name="rnd"/> returns [0,1).</summary>
        public void Step(double dt, MotionLevel level, bool photosafe, Func<double> rnd)
        {
            if (dt < 0) dt = 0;
            Now += dt;
            double k = MotionScale(level);
            double life = Life(level, photosafe, DurationFactor);
            double drag = Math.Exp(-Drag * dt);

            for (int i = Pops.Count - 1; i >= 0; i--)
            {
                var p = Pops[i];
                if (Now - p.Born >= life) { Pops.RemoveAt(i); continue; }
                if (k == 0) continue;   // Off: no movement, no trail, no sparks

                double dx = p.Vx * dt, dy = p.Vy * dt;
                // Exit: the word leaves along its momentum (or rises), easing in over the fade-out.
                double o = OutProgress(Now - p.Born, level, photosafe, DurationFactor);
                double drift = ExitDriftDip * p.Scale * k * o * o, step = drift - p.ExitDone;
                p.ExitDone = drift;
                dx += p.ExitUx * step; dy += p.ExitUy * step;
                p.X += dx; p.Y += dy;
                p.Vx *= drag; p.Vy *= drag;

                p.TrailClock += dt;
                while (p.TrailClock >= TrailStepS)
                {
                    p.TrailClock -= TrailStepS;
                    p.Record(p.X, p.Y);
                }

                p.PathAcc += Math.Sqrt(dx * dx + dy * dy);
                double every = SparkEveryDip * p.Scale / k;   // Reduced: half as many
                int n = 0;
                while (p.PathAcc >= every && n < MaxPathSparksPerStep)
                {
                    p.PathAcc -= every; n++;
                    double sx = (rnd() - 0.5) * SparkSpreadDip * p.Scale * k;
                    double sy = (rnd() - 0.5) * SparkSpreadDip * p.Scale * k;
                    Emit(p.X, p.Y, p.Vx * SparkInherit + sx, p.Vy * SparkInherit + sy,
                        SparkLifeMin + rnd() * SparkLifeSpan, p.Scale, p.Hue);
                }
                if (p.PathAcc >= every) p.PathAcc = 0;   // a capped burst does not bank sparks
            }

            // Particles: drift, drag, age; compact in place (no allocation).
            int w = 0;
            double pd = Math.Exp(-SparkDrag * dt);
            for (int i = 0; i < ParticleCount; i++)
            {
                var q = Particles[i];
                q.T += dt;
                if (q.T >= q.Life) continue;
                q.Vy += SparkGravityDip * q.Size * k * dt;   // a little weight: sparks arc down
                q.X += q.Vx * dt; q.Y += q.Vy * dt;
                q.Vx *= pd; q.Vy *= pd;
                Particles[w++] = q;
            }
            ParticleCount = w;
        }

        private void Emit(double x, double y, double vx, double vy, double life, double scale, int hue)
        {
            if (ParticleCount >= MaxParticles)
            {
                Array.Copy(Particles, 1, Particles, 0, MaxParticles - 1);   // the oldest go first
                ParticleCount = MaxParticles - 1;
            }
            Particles[ParticleCount++] = new AfterglowParticle { X = x, Y = y, Vx = vx, Vy = vy, Life = life, Size = scale, Hue = hue };
        }

        /// <summary>Particle alpha, 1 at birth to 0 at end of life.</summary>
        public static double ParticleAlpha(in AfterglowParticle p) => Math.Max(0, 1 - p.T / p.Life);
    }
}
