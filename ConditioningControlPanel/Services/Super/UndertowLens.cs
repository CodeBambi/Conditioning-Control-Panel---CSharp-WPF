using System;

namespace ConditioningControlPanel.Services.Super
{
    /// <summary>
    /// Motion level as Undertow sees it. Mirrors <c>MotionLevel</c> without pulling the WPF-side
    /// enum into the pure maths; the layer maps one onto the other.
    /// </summary>
    public enum UndertowMotion { Full, Reduced, Off }

    /// <summary>One click ripple. A fixed pool in <see cref="UndertowState"/>, never allocated per click.</summary>
    public struct UndertowRipple
    {
        public bool Live;
        public double X, Y;
        public double Age;       // seconds since the click
        public double Crest;     // 0..1 strength of the brief specular crest (photosafe: spam clicks get less)
    }

    /// <summary>One droplet spark thrown up from a click. Fixed pool, cosmetic only.</summary>
    public struct UndertowDrop
    {
        public bool Live;
        public double X, Y, Vx, Vy;
        public double Age;
    }

    /// <summary>
    /// The live lens. Mutable, WPF-free, Skia-free: the layer owns one, steps it once per engine
    /// tick and draws from it. Everything in it is in the layer's world-space physical pixels.
    /// </summary>
    public sealed class UndertowState
    {
        public const int MaxRipples = 6;

        public double X, Y;          // smoothed lens centre
        public double Radius;        // current lens radius, 0 = the whole screen is blur
        public double Kick;          // widening still owed from clicks
        public bool Placed;          // false until the first step snaps the lens to the cursor
        public readonly UndertowRipple[] Ripples = new UndertowRipple[MaxRipples];

        // ---- juice (cosmetic only: never moves the logical Radius or the clear-area guarantee) ----
        public const int MaxDrops = 18;
        public double DrawRadius, DrawVel;   // the drawn lens springs after Radius: small overshoot, one settle
        public double Age;                   // seconds since placement (rim fade-in, breath phase)
        public double KnockAge = -1;         // seconds since the last click's hit-knock, < 0 = none
        public double SinceClick = 999;      // seconds since the last click (crest photosafe cap)
        public double Vx, Vy;                // smoothed lens velocity, px/s (rim stretch)
        public uint Seed;                    // droplet scatter, advances per click
        public readonly UndertowDrop[] Drops = new UndertowDrop[MaxDrops];

        /// <summary>True while anything on screen still moves: a lens, a pending kick or a ripple.</summary>
        public bool Animating
        {
            get
            {
                if (Radius > 0 || Kick > 0) return true;
                foreach (var r in Ripples) if (r.Live) return true;
                foreach (var d in Drops) if (d.Live) return true;
                return false;
            }
        }

        /// <summary>Panic, emergency exit, stop: forget the lens and every ripple.</summary>
        public void Reset()
        {
            X = Y = Radius = Kick = 0;
            DrawRadius = DrawVel = Age = Vx = Vy = 0;
            KnockAge = -1; SinceClick = 999;
            Placed = false;
            for (int i = 0; i < Ripples.Length; i++) Ripples[i] = default;
            for (int i = 0; i < Drops.Length; i++) Drops[i] = default;
        }
    }

    /// <summary>
    /// Undertow, the Super add-on for Brain Drain and melt (mockup <c>mkDrain</c>). A soft lens in
    /// the blur keeps the screen sharp under the cursor; it shrinks on its own until everything is
    /// blur, and a click widens it again with a small double ripple. Pure maths only, tested in
    /// UndertowLensTests; <c>BrainDrainLayer</c> draws.
    /// </summary>
    public static class UndertowLens
    {
        // ---- constants, copied from the mockup and the owner's spec ----
        public const double FollowRate = 6.0;             // smoothing 1 - exp(-6 dt)
        public const double ShrinkLinear = 0.016;         // x screen width per second (owner: slower to retract)
        public const double ShrinkProportional = 0.1;     // x radius per second
        public const double KickPerClick = 0.2;           // x screen width
        public const double KickApplyRate = 0.75;         // x screen width per second
        public const double MaxRadius = 0.5;              // x screen width (owner: wider in general)
        public const double StillRadius = 0.25;           // x screen width, MotionFx Off
        public const double MinRadius = 0.09;             // x screen width; the lens never closes below this, so a few cm around the cursor stay sharp
        public const double StartRadius = MaxRadius;      // neutral default: the lens opens wide, then sinks
        public const double MaxKick = MaxRadius;          // x screen width; owed widening never queues past one full lens
        public const double ChangeEpsilon = 0.25;         // px; movement below this is not worth a repaint

        // Click waves (owner: no drawn circle, real waves through the screen image). One front runs
        // out from the click with a trailing train of crests that fades behind it.
        public const double WaveLife = 1.8;               // seconds
        public const double WaveSpeed = 0.42;             // x screen width per second
        public const double WaveLength = 0.026;           // x screen width, crest to crest
        public const double WaveBandCrests = 4.0;         // how many crests trail the front
        public const double WaveAmp = 10.0;               // px of displacement at the front at birth (x dpi)
        public const int MaxWaves = UndertowState.MaxRipples;

        /// <summary>Radial mask stops: 1 at the centre, 0.95 at half radius, 0 at the rim (no hard circle).</summary>
        public static readonly float[] MaskPositions = { 0f, 0.5f, 1f };
        public static readonly float[] MaskAlpha = { 1f, 0.95f, 0f };

        /// <summary>Exponential follow factor for one step.</summary>
        public static double FollowFactor(double dt) => 1.0 - Math.Exp(-Math.Max(0, dt) * FollowRate);

        /// <summary>Radius after one step of the constant shrink. Reduced runs at half speed. Never below 0.</summary>
        public static double Shrink(double radius, double dt, double screenWidth, UndertowMotion motion)
        {
            if (motion == UndertowMotion.Off) return StillRadius * screenWidth;
            double speed = motion == UndertowMotion.Reduced ? 0.5 : 1.0;
            double r = radius - Math.Max(0, dt) * speed * (ShrinkLinear * screenWidth + ShrinkProportional * radius);
            return Math.Max(MinRadius * screenWidth, r);
        }

        /// <summary>Opening radius for a fresh run. Off sits at the still lens at once; otherwise
        /// the lens starts closed and <see cref="InitialKick"/> opens it, so it never pops in.</summary>
        public static double InitialRadius(double screenWidth, UndertowMotion motion) =>
            motion == UndertowMotion.Off ? StillRadius * screenWidth : MinRadius * screenWidth;

        /// <summary>Widening owed at the start of a run: the lens opens at the click rate (about half
        /// a second), then sinks. Off: none, the still lens is already there.</summary>
        public static double InitialKick(double screenWidth, UndertowMotion motion) =>
            motion == UndertowMotion.Off ? 0 : StartRadius * screenWidth;

        /// <summary>One engine tick: follow the cursor, shrink, pay out kick, age ripples. Returns
        /// true when anything drawn changed (lens moved or resized while open, a ripple alive or just
        /// gone), so the layer repaints only then.</summary>
        public static bool Step(UndertowState s, double dt, double cursorX, double cursorY,
                                double screenWidth, UndertowMotion motion)
        {
            if (dt < 0) dt = 0;
            double ox = s.X, oy = s.Y, oldR = s.Radius;
            bool wasPlaced = s.Placed;
            bool changed = false;
            if (!s.Placed)
            {
                s.X = cursorX; s.Y = cursorY; s.Placed = true;
                s.Radius = InitialRadius(screenWidth, motion);
                s.Kick = InitialKick(screenWidth, motion);
                s.DrawRadius = s.Radius; s.DrawVel = 0; s.Age = 0;
            }
            else if (motion == UndertowMotion.Off)
            {
                s.X = cursorX; s.Y = cursorY;   // still: no easing, the lens just sits under the cursor
            }
            else
            {
                double f = FollowFactor(dt);
                s.X += (cursorX - s.X) * f;
                s.Y += (cursorY - s.Y) * f;
            }

            if (motion == UndertowMotion.Off)
            {
                s.Radius = StillRadius * screenWidth;
                s.Kick = 0;
            }
            else
            {
                s.Radius = Shrink(s.Radius, dt, screenWidth, motion);
                double rate = KickApplyRate * screenWidth * (motion == UndertowMotion.Reduced ? 0.5 : 1.0);
                double add = Math.Min(s.Kick, dt * rate);
                s.Radius = Math.Min(MaxRadius * screenWidth, s.Radius + add);
                s.Kick = Math.Max(0, s.Kick - add);
            }

            double life = RippleLifeFor(motion);
            for (int i = 0; i < s.Ripples.Length; i++)
            {
                if (!s.Ripples[i].Live) continue;
                changed = true;   // alive this tick, or dying now: either way the frame differs
                s.Ripples[i].Age += dt;
                if (s.Ripples[i].Age >= life) s.Ripples[i].Live = false;
            }

            if (StepJuice(s, dt, ox, oy, wasPlaced, screenWidth, motion)) changed = true;

            if (!wasPlaced) return true;
            if (Math.Abs(s.Radius - oldR) > ChangeEpsilon) changed = true;
            else if ((s.Radius > 0 || oldR > 0) &&
                     (Math.Abs(s.X - ox) > ChangeEpsilon || Math.Abs(s.Y - oy) > ChangeEpsilon)) changed = true;
            return changed;
        }

        /// <summary>A click (never swallowed). Widens the lens and drops a ripple where it landed.
        /// Reduced: widen, no ripple. Off: no widen, one 120 ms fade ring instead of the ripple.</summary>
        public static void Click(UndertowState s, double x, double y, double screenWidth, UndertowMotion motion)
        {
            if (motion != UndertowMotion.Off)
                s.Kick = Math.Min(MaxKick * screenWidth, s.Kick + KickPerClick * screenWidth);
            if (motion == UndertowMotion.Off) return;   // still: no wave

            // Hit-knock and droplet sparks. The crest is photosafe: a click sooner than a third of a
            // second after the last one gets a proportionally fainter crest, so full-strength crests
            // never come faster than 3 a second however fast the player clicks.
            double crest = Math.Clamp(s.SinceClick * CrestPhotosafeHz, 0, 1);
            s.SinceClick = 0;
            s.KnockAge = 0;
            SpawnDrops(s, x, y, screenWidth, motion);

            int slot = 0;
            double oldest = -1;
            for (int i = 0; i < s.Ripples.Length; i++)
            {
                if (!s.Ripples[i].Live) { slot = i; oldest = double.MaxValue; break; }
                if (s.Ripples[i].Age > oldest) { oldest = s.Ripples[i].Age; slot = i; }
            }
            s.Ripples[slot] = new UndertowRipple { Live = true, X = x, Y = y, Age = 0, Crest = crest };
        }

        public static double RippleLifeFor(UndertowMotion motion) => WaveLife;

        /// <summary>
        /// One click wave at <paramref name="age"/>. Returns false when nothing is drawn (Off, or the
        /// wave is spent). <paramref name="front"/> is the radius the wave front has reached,
        /// <paramref name="amp"/> the peak displacement in px, <paramref name="wavelength"/> and
        /// <paramref name="band"/> the crest spacing and the trailing length. Reduced: half speed and
        /// half amplitude. Pixel amplitude scales with <paramref name="dpiScale"/> (the layer draws in
        /// physical pixels); the rest is a screen share.
        /// </summary>
        public static bool WaveAt(double age, double screenWidth, UndertowMotion motion, double dpiScale,
                                  out double front, out double amp, out double wavelength, out double band)
        {
            front = amp = wavelength = band = 0;
            if (motion == UndertowMotion.Off || age < 0 || age >= WaveLife) return false;
            double speed = motion == UndertowMotion.Reduced ? 0.5 : 1.0;
            double px = dpiScale > 0 ? dpiScale : 1.0;
            double u = age / WaveLife;
            front = age * WaveSpeed * speed * screenWidth;
            amp = WaveAmp * px * speed * (1 - u) * (1 - u);
            wavelength = WaveLength * screenWidth;
            band = WaveBandCrests * wavelength;
            return amp > 0.01;
        }

        // ================================================================================
        //  Juice. Cosmetic only: the logical Radius above keeps its meaning (shrink, kick, cap,
        //  minimum clear area); everything here only shapes how it is DRAWN.
        // ================================================================================

        // Drawn radius springs after the logical radius: about 5 percent overshoot on an opening, one settle.
        public const double SpringOmega = 9.0;            // rad/s
        public const double SpringZeta = 0.45;            // Full; Reduced is damped harder and half as fast
        public const double ReducedSpringZeta = 0.8;

        // Hit-knock: a quick outward radius pulse with one small settle (SHIVER, 250 ms).
        public const double KnockLife = 0.25;             // seconds, Reduced twice as long
        public const double KnockAmp = 0.012;             // x screen width, Reduced half

        // Rim glow: a soft tinted ring at the feather that breathes, brightens as the lens grows and on a hit.
        public const double RimBase = 0.16;
        public const double RimBreathAmp = 0.06;
        public const double RimBreathHz = 0.2;            // one breath every 5 s, far under the 3 Hz photosafe line
        public const double RimIntro = 0.35;              // seconds to fade in; Off fades in 120 ms
        public const double OffFade = 0.12;
        public const double RimKnockBoost = 0.18;
        public const double RimGrowBoost = 0.12;
        public const double RimGrowPerSpeed = 0.25;       // per (screen widths per second) of growth
        public const double RimMax = 0.42;

        // Rim stretch along cursor motion.
        public const double StretchPerSpeed = 0.06;       // per (screen widths per second)
        public const double MaxStretch = 0.1;
        public const double VelocityFollow = 12.0;

        // Specular crest on a fresh wave.
        public const double CrestPeak = 0.22;
        public const double CrestDecay = 0.18;            // seconds
        public const double CrestPhotosafeHz = 3.0;

        // Droplet sparks thrown up from the click point.
        public const int DropsFull = 6, DropsReduced = 3;
        public const double DropLife = 0.6;               // seconds
        public const double DropSpeedMin = 0.09, DropSpeedMax = 0.2;   // x screen width per second
        public const double DropGravity = 0.75;           // x screen width per second squared

        /// <summary>The knock's shape over u in 0..1: out to 1 by 40 percent, then one small settle back
        /// in (a quarter of the way) that ends at 0.</summary>
        public static double KnockShape(double u)
        {
            if (u <= 0 || u >= 1) return 0;
            return u < 0.4 ? Math.Sin(Math.PI * u / 0.4) : -0.25 * Math.Sin(Math.PI * (u - 0.4) / 0.6);
        }

        public static double KnockLifeFor(UndertowMotion motion) => motion == UndertowMotion.Reduced ? KnockLife * 2 : KnockLife;

        /// <summary>Radius pulse in px at the knock's age. Off: none. Reduced: half amplitude, half speed.</summary>
        public static double KnockPulse(double age, double screenWidth, UndertowMotion motion)
        {
            if (motion == UndertowMotion.Off || age < 0) return 0;
            double amp = KnockAmp * screenWidth * (motion == UndertowMotion.Reduced ? 0.5 : 1.0);
            return amp * KnockShape(age / KnockLifeFor(motion));
        }

        /// <summary>The radius the layer draws: the springy radius plus the knock, never under the
        /// minimum clear area while the lens is open. 0 when there is no lens.</summary>
        public static double DrawnRadius(UndertowState s, double screenWidth, UndertowMotion motion)
        {
            if (s.Radius <= 0) return 0;
            double r = motion == UndertowMotion.Off ? s.Radius : s.DrawRadius + KnockPulse(s.KnockAge, screenWidth, motion);
            return Math.Max(Math.Min(s.Radius, MinRadius * screenWidth), r);
        }

        /// <summary>How far the rim stretches along motion (0 = round). Off: never. Reduced: half.</summary>
        public static double Stretch(double vx, double vy, double screenWidth, UndertowMotion motion)
        {
            if (motion == UndertowMotion.Off || screenWidth <= 0) return 0;
            double scale = motion == UndertowMotion.Reduced ? 0.5 : 1.0;
            double speed = Math.Sqrt(vx * vx + vy * vy) / screenWidth;
            return Math.Min(MaxStretch, speed * StretchPerSpeed) * scale;
        }

        /// <summary>Rim glow alpha 0..<see cref="RimMax"/>: fade-in, breath, hit and growth. Off: a
        /// steady glow after a 120 ms fade. Reduced: half the breath and half the boosts.</summary>
        public static double RimAlpha(UndertowState s, double screenWidth, UndertowMotion motion)
        {
            if (s.Radius <= 0 || !s.Placed) return 0;
            if (motion == UndertowMotion.Off) return RimBase * Math.Clamp(s.Age / OffFade, 0, 1);
            double half = motion == UndertowMotion.Reduced ? 0.5 : 1.0;
            double t = Math.Clamp(s.Age / RimIntro, 0, 1);
            double intro = t * t * (3 - 2 * t);
            double breath = RimBreathAmp * half * Math.Sin(2 * Math.PI * RimBreathHz * half * s.Age);
            double knock = 0;
            if (s.KnockAge >= 0)
            {
                double u = s.KnockAge / KnockLifeFor(motion);
                if (u < 1) { double k = u < 0.15 ? u / 0.15 : (1 - u) / 0.85; knock = RimKnockBoost * half * k * k; }
            }
            double grow = s.DrawVel > 0 && screenWidth > 0
                ? Math.Min(RimGrowBoost, s.DrawVel / screenWidth * RimGrowPerSpeed) * half : 0;
            return Math.Clamp((RimBase + breath + knock + grow) * intro, 0, RimMax);
        }

        /// <summary>Specular crest strength for a wave: one fade from the click, no flicker. Off: none.</summary>
        public static double CrestAt(double age, double crest, UndertowMotion motion)
        {
            if (motion == UndertowMotion.Off || age < 0) return 0;
            double half = motion == UndertowMotion.Reduced ? 0.5 : 1.0;
            return CrestPeak * half * Math.Clamp(crest, 0, 1) * Math.Exp(-age / CrestDecay);
        }

        public static int DropsFor(UndertowMotion motion) => motion switch
        {
            UndertowMotion.Full => DropsFull,
            UndertowMotion.Reduced => DropsReduced,
            _ => 0,
        };

        private static void SpawnDrops(UndertowState s, double x, double y, double screenWidth, UndertowMotion motion)
        {
            int n = DropsFor(motion);
            double speedScale = motion == UndertowMotion.Reduced ? 0.5 : 1.0;
            for (int k = 0; k < n; k++)
            {
                int slot = 0;
                double oldest = -1;
                for (int i = 0; i < s.Drops.Length; i++)
                {
                    if (!s.Drops[i].Live) { slot = i; break; }
                    if (s.Drops[i].Age > oldest) { oldest = s.Drops[i].Age; slot = i; }
                }
                // a fan above the click: -150 to -30 degrees (screen y points down)
                double a = (-150 + 120 * ((k + Next01(ref s.Seed)) / n)) * Math.PI / 180;
                double v = (DropSpeedMin + (DropSpeedMax - DropSpeedMin) * Next01(ref s.Seed)) * screenWidth * speedScale;
                s.Drops[slot] = new UndertowDrop { Live = true, X = x, Y = y, Vx = Math.Cos(a) * v, Vy = Math.Sin(a) * v };
            }
        }

        private static double Next01(ref uint seed)
        {
            uint z = seed += 0x9E3779B9u;
            z ^= z >> 16; z *= 0x85EBCA6Bu; z ^= z >> 13; z *= 0xC2B2AE35u; z ^= z >> 16;
            return z / 4294967296.0;
        }

        /// <summary>Spring, knock, velocity, droplets. True when something drawn moved enough to repaint.
        /// The rim breath alone never asks for a repaint (a parked lens must not force full-screen
        /// redraws); it shows on the frames the capture pump already publishes.</summary>
        private static bool StepJuice(UndertowState s, double dt, double ox, double oy, bool wasPlaced,
                                      double screenWidth, UndertowMotion motion)
        {
            bool changed = false;
            s.Age += dt;
            s.SinceClick += dt;
            double oldStretch = Stretch(s.Vx, s.Vy, screenWidth, motion);
            double oldDraw = s.DrawRadius;

            if (motion == UndertowMotion.Off)
            {
                s.Vx = s.Vy = 0;
                s.DrawRadius = s.Radius; s.DrawVel = 0;
                s.KnockAge = -1;
            }
            else
            {
                if (wasPlaced && dt > 0)
                {
                    double f = 1 - Math.Exp(-dt * VelocityFollow);
                    s.Vx += ((s.X - ox) / dt - s.Vx) * f;
                    s.Vy += ((s.Y - oy) / dt - s.Vy) * f;
                    if (Math.Abs(s.Vx) < 1 && Math.Abs(s.Vy) < 1) s.Vx = s.Vy = 0;
                }

                double half = motion == UndertowMotion.Reduced ? 0.5 : 1.0;
                double w = SpringOmega * half;
                double z = motion == UndertowMotion.Reduced ? ReducedSpringZeta : SpringZeta;
                double left = dt;
                while (left > 0)
                {
                    double h = Math.Min(left, 1.0 / 240);
                    double acc = w * w * (s.Radius - s.DrawRadius) - 2 * z * w * s.DrawVel;
                    s.DrawVel += acc * h;
                    s.DrawRadius += s.DrawVel * h;
                    left -= h;
                }
                double floor = Math.Min(s.Radius, MinRadius * screenWidth);
                if (s.DrawRadius < floor) { s.DrawRadius = floor; if (s.DrawVel < 0) s.DrawVel = 0; }
                if (Math.Abs(s.Radius - s.DrawRadius) < 0.05 && Math.Abs(s.DrawVel) < 0.5)
                { s.DrawRadius = s.Radius; s.DrawVel = 0; }

                if (s.KnockAge >= 0)
                {
                    changed = true;
                    s.KnockAge += dt;
                    if (s.KnockAge >= KnockLifeFor(motion)) s.KnockAge = -1;
                }
            }

            double g = DropGravity * screenWidth * (motion == UndertowMotion.Reduced ? 0.5 : 1.0);
            for (int i = 0; i < s.Drops.Length; i++)
            {
                if (!s.Drops[i].Live) continue;
                changed = true;
                ref var d = ref s.Drops[i];
                if (motion == UndertowMotion.Off) { d.Live = false; continue; }   // panic-quiet: drops just end
                d.Age += dt;
                d.Vy += g * dt;
                d.X += d.Vx * dt;
                d.Y += d.Vy * dt;
                if (d.Age >= DropLife) d.Live = false;
            }

            if (Math.Abs(s.DrawRadius - oldDraw) > ChangeEpsilon) changed = true;
            if (Math.Abs(Stretch(s.Vx, s.Vy, screenWidth, motion) - oldStretch) > 0.0005) changed = true;
            return changed;
        }
    }
}
