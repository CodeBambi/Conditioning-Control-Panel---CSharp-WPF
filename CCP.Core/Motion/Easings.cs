using System;

namespace ConditioningControlPanel.Motion
{
    /// <summary>
    /// The easing vocabulary of WPF 7.1.5 as pure functions of normalised time t in 0..1, so a head
    /// without WPF's easing classes (Avalonia) draws the same curves. Formulas are WPF's own
    /// (EasingFunctionBase mirrors EaseIn into EaseOut / EaseInOut exactly as here) plus the house
    /// curves from docs/JUICE-PLAYBOOK.md: THUD cubic-bezier(.2,1.5,.4,1), the damped spring, and
    /// the Breakout jelly / squash / push-in shapes.
    ///
    /// <para>WPF defaults worth remembering: QuadraticEase, CubicEase and SineEase default to
    /// EaseOut, so a bare <c>new SineEase()</c> in the WPF source is <see cref="SineOut"/>.</para>
    /// </summary>
    public static class Easings
    {
        // ---- the WPF mirroring rule ------------------------------------------------------------

        private static double Out(Func<double, double> easeIn, double t) => 1.0 - easeIn(1.0 - t);

        private static double InOut(Func<double, double> easeIn, double t) =>
            t < 0.5 ? easeIn(t * 2.0) * 0.5 : (1.0 - easeIn((1.0 - t) * 2.0)) * 0.5 + 0.5;

        public static double Linear(double t) => t;

        // ---- QuadraticEase ---------------------------------------------------------------------
        public static double QuadIn(double t) => t * t;
        public static double QuadOut(double t) => Out(QuadIn, t);
        public static double QuadInOut(double t) => InOut(QuadIn, t);

        // ---- CubicEase -------------------------------------------------------------------------
        public static double CubicIn(double t) => t * t * t;
        public static double CubicOut(double t) => Out(CubicIn, t);
        public static double CubicInOut(double t) => InOut(CubicIn, t);

        // ---- SineEase (WPF EaseInCore: 1 - sin(pi/2 (1 - t))) -----------------------------------
        public static double SineIn(double t) => 1.0 - Math.Sin(Math.PI * 0.5 * (1.0 - t));
        public static double SineOut(double t) => Out(SineIn, t);
        public static double SineInOut(double t) => InOut(SineIn, t);

        // ---- BackEase (WPF: t^3 - t * amplitude * sin(pi t)) -----------------------------------
        public static double BackIn(double t, double amplitude = 1.0) =>
            Math.Pow(t, 3.0) - t * Math.Max(0.0, amplitude) * Math.Sin(Math.PI * t);
        public static double BackOut(double t, double amplitude = 1.0) => 1.0 - BackIn(1.0 - t, amplitude);

        /// <summary>The Leash explainer's "thud": WPF BackEase { Amplitude = 0.55, EaseOut }.</summary>
        public const double LeashThudAmplitude = 0.55;

        // ---- house curves (JUICE-PLAYBOOK section 2) -------------------------------------------

        /// <summary>The house THUD: cubic-bezier(.2,1.5,.4,1). Overshoots past the end and settles
        /// back. DashboardBillboard.ThudCurve in 7.1.5.</summary>
        public static readonly (double X1, double Y1, double X2, double Y2) ThudCurve = (0.2, 1.5, 0.4, 1.0);

        /// <summary>The THUD curve at time t.</summary>
        public static double Thud(double t) => CubicBezier.Ease(t, ThudCurve.X1, ThudCurve.Y1, ThudCurve.X2, ThudCurve.Y2);

        /// <summary>Smoothstep t*t*(3-2t): holds that release, camera moves, parallax returns.</summary>
        public static double Smoothstep(double t)
        {
            t = Math.Clamp(t, 0, 1);
            return t * t * (3 - 2 * t);
        }

        /// <summary>The damped spring exp(-k u) * cos(w u): squash, jelly, wobble, anything that rings.</summary>
        public static double DampedSpring(double u, double k, double w) => Math.Exp(-k * u) * Math.Cos(w * u);

        /// <summary>Breakout feedback.js jellyScale(t): a ringing scale pair, t = 0..1 since the hit.</summary>
        public static (double Along, double Across) Jelly(double t)
        {
            double u = 1 - Math.Max(0, Math.Min(1, t));
            double k = u >= 1 ? 0 : Math.Exp(-4.2 * u) * Math.Cos(u * Math.PI * 5) * (1 - u);
            return (1 - .2 * k, 1 + .16 * k);
        }

        /// <summary>Breakout feedback.js squashScale(t): flat at once, round by 40%, one springy overshoot.</summary>
        public static (double Along, double Across) Squash(double t)
        {
            double u = 1 - Math.Max(0, Math.Min(1, t));
            double k = u < .4 ? 1 - u / .4 : -.3 * Math.Sin((u - .4) / .6 * Math.PI);
            return (1 - .38 * k, 1 + .24 * k);
        }

        /// <summary>Breakout feedback.js PUSH_IN_S: the push-in zoom's whole length, seconds.</summary>
        public const double PushInSeconds = 0.85;

        /// <summary>Breakout feedback.js pushInZoom(t): up to 1.06 in 120 ms, held, eased back by 0.85 s.</summary>
        public static double PushIn(double seconds)
        {
            if (!(seconds >= 0) || seconds >= PushInSeconds) return 1;
            double rise = 1 - Math.Pow(1 - Math.Min(1, seconds / .12), 3);
            double fall = Math.Max(0, (seconds - .35) / (PushInSeconds - .35));
            return 1 + .06 * rise * (1 - fall * fall * (3 - 2 * fall));
        }

        /// <summary>Applies an <see cref="EaseKind"/> at t.</summary>
        public static double Apply(EaseKind kind, double t) => kind switch
        {
            EaseKind.QuadIn => QuadIn(t),
            EaseKind.QuadOut => QuadOut(t),
            EaseKind.QuadInOut => QuadInOut(t),
            EaseKind.CubicIn => CubicIn(t),
            EaseKind.CubicOut => CubicOut(t),
            EaseKind.CubicInOut => CubicInOut(t),
            EaseKind.SineIn => SineIn(t),
            EaseKind.SineOut => SineOut(t),
            EaseKind.SineInOut => SineInOut(t),
            EaseKind.Thud => Thud(t),
            EaseKind.Smoothstep => Smoothstep(t),
            _ => t,
        };
    }

    /// <summary>The named curves a keyframe can ride. Discrete holds the previous value until the
    /// keyframe's time (WPF DiscreteDoubleKeyFrame).</summary>
    public enum EaseKind
    {
        Linear,
        Discrete,
        QuadIn,
        QuadOut,
        QuadInOut,
        CubicIn,
        CubicOut,
        CubicInOut,
        SineIn,
        SineOut,
        SineInOut,
        Thud,
        Smoothstep,
    }

    /// <summary>
    /// A CSS cubic-bezier(x1, y1, x2, y2) timing curve, solved the way browsers do it: Newton, then
    /// bisection. Verbatim from the 7.1.5 Controls/Billboard/CubicBezierEase.Ease.
    /// </summary>
    public static class CubicBezier
    {
        /// <summary>The curve's y at time x (0..1).</summary>
        public static double Ease(double x, double x1, double y1, double x2, double y2)
        {
            if (x <= 0) return 0;
            if (x >= 1) return 1;
            double t = Solve(x, x1, x2);
            return Bezier(t, y1, y2);
        }

        private static double Bezier(double t, double p1, double p2)
        {
            double u = 1 - t;
            return 3 * u * u * t * p1 + 3 * u * t * t * p2 + t * t * t;
        }

        private static double Slope(double t, double p1, double p2)
        {
            double u = 1 - t;
            return 3 * u * u * p1 + 6 * u * t * (p2 - p1) + 3 * t * t * (1 - p2);
        }

        private static double Solve(double x, double x1, double x2)
        {
            double t = x;
            for (int i = 0; i < 8; i++)
            {
                double err = Bezier(t, x1, x2) - x;
                if (Math.Abs(err) < 1e-6) return t;
                double d = Slope(t, x1, x2);
                if (Math.Abs(d) < 1e-6) break;
                t -= err / d;
            }
            double lo = 0, hi = 1;
            t = x;
            for (int i = 0; i < 40; i++)
            {
                double v = Bezier(t, x1, x2);
                if (Math.Abs(v - x) < 1e-7) break;
                if (v < x) lo = t; else hi = t;
                t = (lo + hi) / 2;
            }
            return t;
        }
    }
}
