using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace ConditioningControlPanel.Controls.Billboard
{
    /// <summary>
    /// A CSS <c>cubic-bezier(x1, y1, x2, y2)</c> timing curve for WPF animations. The house "thud"
    /// is cubic-bezier(.2,1.5,.4,1): it overshoots past the end and settles back, which no built-in
    /// WPF easing does with that shape. <see cref="Ease"/> is pure and pinned by tests.
    /// </summary>
    public sealed class CubicBezierEase : EasingFunctionBase
    {
        public double X1 { get; set; }
        public double Y1 { get; set; }
        public double X2 { get; set; }
        public double Y2 { get; set; }

        public CubicBezierEase() : this(0.25, 0.1, 0.25, 1.0) { }

        public CubicBezierEase(double x1, double y1, double x2, double y2)
        {
            X1 = x1; Y1 = y1; X2 = x2; Y2 = y2;
            EasingMode = EasingMode.EaseIn; // the curve is the whole shape; WPF must not mirror it
        }

        /// <summary>The house thud, cubic-bezier(.2,1.5,.4,1).</summary>
        public static CubicBezierEase Thud()
        {
            var c = Services.DashboardBillboard.ThudCurve;
            var e = new CubicBezierEase(c.X1, c.Y1, c.X2, c.Y2);
            e.Freeze();
            return e;
        }

        protected override double EaseInCore(double normalizedTime) => Ease(normalizedTime, X1, Y1, X2, Y2);

        protected override Freezable CreateInstanceCore() => new CubicBezierEase(X1, Y1, X2, Y2);

        /// <summary>The curve's y at time x (0..1), solved the way browsers do it: Newton, then bisection.</summary>
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
