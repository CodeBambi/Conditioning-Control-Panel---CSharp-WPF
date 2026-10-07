using System;
using Avalonia;

namespace ConditioningControlPanel.Avalonia.Controls.HelpLoops
{
    /// <summary>The mockup's timing helpers, verbatim from WPF Controls/HelpLoops/LoopMath.cs
    /// (only <see cref="Point"/> is Avalonia's).</summary>
    public static class LoopMath
    {
        public static double Clamp(double v, double a = 0, double b = 1) => Math.Min(b, Math.Max(a, v));

        public static double Seg(double t, double a, double b) => Clamp((t - a) / (b - a));

        public static double EaseOut(double x) => 1 - Math.Pow(1 - x, 3);

        public static double EaseInOut(double x) =>
            x < .5 ? 4 * x * x * x : 1 - Math.Pow(-2 * x + 2, 3) / 2;

        public static double Back(double x)
        {
            const double c1 = 1.70158, c3 = c1 + 1;
            return 1 + c3 * Math.Pow(x - 1, 3) + c1 * Math.Pow(x - 1, 2);
        }

        public static double Lerp(double a, double b, double k) => a + (b - a) * k;

        public static Point Path((double T, double X, double Y)[] pts, double t)
        {
            if (t <= pts[0].T) return new Point(pts[0].X, pts[0].Y);
            for (int i = 1; i < pts.Length; i++)
            {
                if (t > pts[i].T) continue;
                var a = pts[i - 1];
                var b = pts[i];
                var k = EaseInOut(Seg(t, a.T, b.T));
                return new Point(Lerp(a.X, b.X, k), Lerp(a.Y, b.Y, k));
            }
            return new Point(pts[^1].X, pts[^1].Y);
        }

        public static double Schedule((double T, double V)[] pts, double t)
        {
            if (t <= pts[0].T) return pts[0].V;
            for (int i = 1; i < pts.Length; i++)
            {
                if (t > pts[i].T) continue;
                return Lerp(pts[i - 1].V, pts[i].V, EaseInOut(Seg(t, pts[i - 1].T, pts[i].T)));
            }
            return pts[^1].V;
        }
    }
}
