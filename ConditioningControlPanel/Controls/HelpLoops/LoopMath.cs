using System;
using System.Windows;

namespace ConditioningControlPanel.Controls.HelpLoops
{
    /// <summary>The mockup's timing helpers, same maths (seg, eo, eio, back, lerp, tri, path, schedule).</summary>
    public static class LoopMath
    {
        public static double Clamp(double v, double a = 0, double b = 1) => Math.Min(b, Math.Max(a, v));

        /// <summary>0 before a, 1 after b, linear between.</summary>
        public static double Seg(double t, double a, double b) => Clamp((t - a) / (b - a));

        /// <summary>Cubic ease out.</summary>
        public static double EaseOut(double x) => 1 - Math.Pow(1 - x, 3);

        /// <summary>Cubic ease in-out.</summary>
        public static double EaseInOut(double x) =>
            x < .5 ? 4 * x * x * x : 1 - Math.Pow(-2 * x + 2, 3) / 2;

        /// <summary>Ease out with a small overshoot.</summary>
        public static double Back(double x)
        {
            const double c1 = 1.70158, c3 = c1 + 1;
            return 1 + c3 * Math.Pow(x - 1, 3) + c1 * Math.Pow(x - 1, 2);
        }

        public static double Lerp(double a, double b, double k) => a + (b - a) * k;

        /// <summary>Triangle wave 0..1..0 with period 2.</summary>
        public static double Tri(double p)
        {
            var m = ((p % 2) + 2) % 2;
            return m < 1 ? m : 2 - m;
        }

        /// <summary>Position along timed waypoints (T, X, Y), eased in-out between them.</summary>
        public static Point Path((double T, double X, double Y)[] pts, double t)
        {
            if (t <= pts[0].T) return new Point(pts[0].X, pts[0].Y);
            for (int i = 1; i < pts.Length; i++)
            {
                if (t <= pts[i].T)
                {
                    var a = pts[i - 1];
                    var b = pts[i];
                    var k = EaseInOut(Seg(t, a.T, b.T));
                    return new Point(Lerp(a.X, b.X, k), Lerp(a.Y, b.Y, k));
                }
            }
            var l = pts[^1];
            return new Point(l.X, l.Y);
        }

        /// <summary>Value along timed keys (T, V), eased in-out between them.</summary>
        public static double Schedule((double T, double V)[] pts, double t)
        {
            if (t <= pts[0].T) return pts[0].V;
            for (int i = 1; i < pts.Length; i++)
            {
                if (t <= pts[i].T)
                {
                    var a = pts[i - 1];
                    var b = pts[i];
                    return Lerp(a.V, b.V, EaseInOut(Seg(t, a.T, b.T)));
                }
            }
            return pts[^1].V;
        }
    }
}
