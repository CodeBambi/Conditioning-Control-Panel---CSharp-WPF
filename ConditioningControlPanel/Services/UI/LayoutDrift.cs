using System;

namespace ConditioningControlPanel.Services.UI
{
    /// <summary>
    /// Pure rule for "WPF's layout no longer covers the window". After a mixed-DPI move (the panel
    /// dragged between a 100% and a 125% monitor while it was being minimised, re-fitted and moved
    /// by the tube's make-room), the HWND can end up bigger than the size WPF laid the root out at.
    /// WPF paints nothing past its own layout, so the rest of the window shows as bare black inside
    /// the Windows border (owner report on 6.11.5, 2026-09-29). A real resize makes WPF catch up;
    /// this decides when to force one. See MainWindow.WorkAreaFit.cs.
    /// </summary>
    public static class LayoutDrift
    {
        /// <summary>Rounding slack in physical pixels: fractional DIP sizes land a pixel either side.</summary>
        public const int TolerancePx = 3;

        /// <summary>At most this many forced resizes inside <see cref="BurstWindowMs"/>.</summary>
        public const int MaxNudgesPerBurst = 2;
        public const int BurstWindowMs = 3000;

        /// <summary>
        /// True when the root's laid-out size, scaled to device pixels, misses the client area by
        /// more than <see cref="TolerancePx"/> on either axis. Sizes that are not yet known (0, NaN,
        /// a zero scale) never count as drift.
        /// </summary>
        public static bool Drifted(int clientWidthPx, int clientHeightPx,
                                   double layoutWidthDip, double layoutHeightDip,
                                   double scaleX, double scaleY)
        {
            if (clientWidthPx <= 0 || clientHeightPx <= 0) return false;
            if (!(layoutWidthDip > 0) || !(layoutHeightDip > 0)) return false;
            if (!(scaleX > 0) || !(scaleY > 0)) return false;
            if (double.IsInfinity(layoutWidthDip) || double.IsInfinity(layoutHeightDip)) return false;

            var laidW = layoutWidthDip * scaleX;
            var laidH = layoutHeightDip * scaleY;
            return Math.Abs(laidW - clientWidthPx) > TolerancePx
                || Math.Abs(laidH - clientHeightPx) > TolerancePx;
        }
    }
}
