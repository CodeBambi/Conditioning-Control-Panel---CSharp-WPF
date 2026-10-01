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
        /// <summary>Trail ring buffer: recent centres, newest at <see cref="TrailHead"/>.</summary>
        public readonly double[] TrailX = new double[AfterglowField.TrailLen];
        public readonly double[] TrailY = new double[AfterglowField.TrailLen];
        public int TrailCount, TrailHead;
        internal double TrailClock, PathAcc;
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
        public static double Life(MotionLevel level, bool photosafe)
        {
            if (photosafe) return SafeInS + SafeHoldS + SafeOutS;
            if (level == MotionLevel.Off) return OffFadeS;
            return FadeInS + HoldS + FadeOutS;
        }

        /// <summary>Alpha envelope 0..1. Off = a single 120 ms fade; photosafe = slow in, 0.5 s out.</summary>
        public static double Alpha(double age, MotionLevel level, bool photosafe)
        {
            if (age < 0) return 0;
            double inS, holdS, outS;
            if (photosafe) { inS = SafeInS; holdS = SafeHoldS; outS = SafeOutS; }
            else if (level == MotionLevel.Off) return Math.Clamp(1 - age / OffFadeS, 0, 1);
            else { inS = FadeInS; holdS = HoldS; outS = FadeOutS; }
            if (age < inS) return age / inS;
            if (age < inS + holdS) return 1;
            return Math.Clamp(1 - (age - inS - holdS) / outS, 0, 1);
        }

        /// <summary>Echo k (0 = newest) alpha, before the pop's own alpha.</summary>
        public static double TrailAlpha(int k) => k < 0 || k >= TrailLen ? 0 : 0.5 * (1 - (k + 1.0) / (TrailLen + 1));

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
        public AfterglowPop Spawn(string text, double x, double y, double scale, MotionLevel level, bool photosafe, Func<double> rnd)
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
            double life = Life(level, photosafe);
            double drag = Math.Exp(-Drag * dt);

            for (int i = Pops.Count - 1; i >= 0; i--)
            {
                var p = Pops[i];
                if (Now - p.Born >= life) { Pops.RemoveAt(i); continue; }
                if (k == 0) continue;   // Off: no movement, no trail, no sparks

                double dx = p.Vx * dt, dy = p.Vy * dt;
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
