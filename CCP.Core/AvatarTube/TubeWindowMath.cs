using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.AvatarTubeLayout
{
    /// <summary>
    /// The tube's size and detached placement, lifted from WPF's CalculateScaleFactor,
    /// ClampAvatarPosition and RestoreSavedPlacement (AvatarTubeWindow.Windowing.cs:632-677,
    /// 2566-2660) so every head shares them. Positions are physical px; saved values are DIPs.
    /// </summary>
    public static class TubeWindowMath
    {
        /// <summary>Art scale for a work area in DIPs: 85% of its height, 30% of its width, 0.4..1.</summary>
        public static double FitScale(double workW, double workH, double designW = 780, double designH = 1020)
            => Math.Max(0.4, Math.Min(1.0, Math.Min(workH * 0.85 / designH, workW * 0.3 / designW)));

        /// <summary>
        /// How far (window design units, 0..maxUnits) a mod's tube art paints past the built-in
        /// glass's right edge. Each picture is drawn Uniform, centred, in the gridW x gridH design
        /// grid, which the window then fits by <paramref name="k"/>. Edges are the rightmost opaque
        /// column as a fraction of the picture's width. Never negative: a narrower mod tube keeps the
        /// built-in seam (the dock never pulls the art away from main).
        /// </summary>
        public static double ArtOverhangUnits(double modEdge, double modAspect, double builtInEdge, double builtInAspect,
            double gridW = 780, double gridH = 1080, double k = 1020.0 / 1080, double maxUnits = 120)
        {
            double Pos(double edge, double aspect)
            {
                double w = Math.Min(gridW, gridH * Math.Max(0.01, aspect));
                return (gridW - w) / 2 + Math.Clamp(edge, 0, 1) * w;
            }
            return Math.Clamp((Pos(modEdge, modAspect) - Pos(builtInEdge, builtInAspect)) * k, 0, maxUnits);
        }

        /// <summary>Keep at least half the detached tube on the work area; only the bottom edge bounds Top.</summary>
        public static (int X, int Y) ClampDetached(double left, double top, double w, double h, Box work)
        {
            double x = Math.Clamp(left, work.X - w / 2, Math.Max(work.X - w / 2, work.Right - w / 2));
            double y = Math.Min(top, work.Bottom - h / 2);
            return ((int)Math.Round(x), (int)Math.Round(y));
        }

        /// <summary>Saved DIPs back to px: the screen whose own scaling maps the DIPs inside its
        /// bounds wins (mixed-DPI desks), else <paramref name="fallbackScaling"/> and the first screen.</summary>
        public static (int X, int Y) RestoreDetached(double dipLeft, double dipTop, double w, double h,
            IReadOnlyList<(Box Bounds, double Scaling, Box Work)> screens, double fallbackScaling)
        {
            foreach (var sc in screens)
            {
                double x = dipLeft * sc.Scaling, y = dipTop * sc.Scaling;
                if (sc.Bounds.Contains(x, y)) return ClampDetached(x, y, w, h, sc.Work);
            }
            double fx = dipLeft * fallbackScaling, fy = dipTop * fallbackScaling;
            return screens.Count > 0 ? ClampDetached(fx, fy, w, h, screens[0].Work)
                                     : ((int)Math.Round(fx), (int)Math.Round(fy));
        }
    }
}
