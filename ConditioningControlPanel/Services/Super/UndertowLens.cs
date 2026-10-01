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

        /// <summary>True while anything on screen still moves: a lens, a pending kick or a ripple.</summary>
        public bool Animating
        {
            get
            {
                if (Radius > 0 || Kick > 0) return true;
                foreach (var r in Ripples) if (r.Live) return true;
                return false;
            }
        }

        /// <summary>Panic, emergency exit, stop: forget the lens and every ripple.</summary>
        public void Reset()
        {
            X = Y = Radius = Kick = 0;
            Placed = false;
            for (int i = 0; i < Ripples.Length; i++) Ripples[i] = default;
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

            int slot = 0;
            double oldest = -1;
            for (int i = 0; i < s.Ripples.Length; i++)
            {
                if (!s.Ripples[i].Live) { slot = i; oldest = double.MaxValue; break; }
                if (s.Ripples[i].Age > oldest) { oldest = s.Ripples[i].Age; slot = i; }
            }
            s.Ripples[slot] = new UndertowRipple { Live = true, X = x, Y = y, Age = 0 };
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
    }
}
