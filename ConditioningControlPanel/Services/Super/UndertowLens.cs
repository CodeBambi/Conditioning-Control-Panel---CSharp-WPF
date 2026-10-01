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
        public const double ShrinkLinear = 0.045;         // x screen width per second
        public const double ShrinkProportional = 0.3;     // x radius per second
        public const double KickPerClick = 0.15;          // x screen width
        public const double KickApplyRate = 0.75;         // x screen width per second
        public const double MaxRadius = 0.36;             // x screen width
        public const double StillRadius = 0.18;           // x screen width, MotionFx Off
        public const double StartRadius = MaxRadius;      // neutral default: the lens opens wide, then sinks

        public const double RippleLife = 0.8;             // seconds
        public const double RippleRingGap = 0.1;          // second ring starts this much later
        public const double RippleGrow = 0.7;             // seconds for one ring to reach full size
        public const double RippleReach = 0.18;           // x screen width
        public const double RippleBase = 6.0;             // px, ring radius at birth
        public const double RippleAlpha = 0.65;
        public const double OffFadeSeconds = 0.12;        // MotionFx Off: the click is one 120 ms fade

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
            return Math.Max(0, r);
        }

        /// <summary>Opening radius for a fresh run.</summary>
        public static double InitialRadius(double screenWidth, UndertowMotion motion) =>
            (motion == UndertowMotion.Off ? StillRadius : StartRadius) * screenWidth;

        /// <summary>One engine tick: follow the cursor, shrink, pay out kick, age ripples.</summary>
        public static void Step(UndertowState s, double dt, double cursorX, double cursorY,
                                double screenWidth, UndertowMotion motion)
        {
            if (dt < 0) dt = 0;
            if (!s.Placed)
            {
                s.X = cursorX; s.Y = cursorY; s.Placed = true;
                s.Radius = InitialRadius(screenWidth, motion);
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
                s.Ripples[i].Age += dt;
                if (s.Ripples[i].Age >= life) s.Ripples[i].Live = false;
            }
        }

        /// <summary>A click (never swallowed). Widens the lens and drops a ripple where it landed.
        /// Reduced: widen, no ripple. Off: no widen, one 120 ms fade ring instead of the ripple.</summary>
        public static void Click(UndertowState s, double x, double y, double screenWidth, UndertowMotion motion)
        {
            if (motion != UndertowMotion.Off) s.Kick += KickPerClick * screenWidth;
            if (motion == UndertowMotion.Reduced) return;

            int slot = 0;
            double oldest = -1;
            for (int i = 0; i < s.Ripples.Length; i++)
            {
                if (!s.Ripples[i].Live) { slot = i; oldest = double.MaxValue; break; }
                if (s.Ripples[i].Age > oldest) { oldest = s.Ripples[i].Age; slot = i; }
            }
            s.Ripples[slot] = new UndertowRipple { Live = true, X = x, Y = y, Age = 0 };
        }

        public static double RippleLifeFor(UndertowMotion motion) =>
            motion == UndertowMotion.Off ? OffFadeSeconds : RippleLife;

        /// <summary>
        /// Ring <paramref name="ring"/> (0 or 1) of a ripple at <paramref name="age"/>. Returns false
        /// when that ring is not drawn this frame. Off: ring 0 only, fixed at the still lens radius,
        /// fading over 120 ms; it never grows.
        /// </summary>
        public static bool RingAt(double age, int ring, double screenWidth, UndertowMotion motion,
                                  out double radius, out double alpha, out double strokeWidth)
        {
            radius = alpha = strokeWidth = 0;
            if (motion == UndertowMotion.Reduced) return false;
            if (motion == UndertowMotion.Off)
            {
                if (ring != 0 || age < 0 || age >= OffFadeSeconds) return false;
                radius = StillRadius * screenWidth;
                alpha = RippleAlpha * (1.0 - age / OffFadeSeconds);
                strokeWidth = 3;
                return alpha > 0;
            }
            double u = Math.Clamp((age - ring * RippleRingGap) / RippleGrow, 0, 1);
            if (u <= 0 || age >= RippleLife) return false;
            radius = u * RippleReach * screenWidth + RippleBase;
            alpha = RippleAlpha * (1.0 - u);
            strokeWidth = 3 - ring * 1.5;
            return alpha > 0;
        }
    }
}
