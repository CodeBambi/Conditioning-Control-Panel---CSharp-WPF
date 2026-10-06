using System;

namespace ConditioningControlPanel.Services.UI
{
    /// <summary>
    /// Pure rule for fitting the panel window into a monitor's work area.
    ///
    /// <para>The panel's whole UI is a <c>Viewbox Stretch="Fill"</c> over a fixed design canvas
    /// (MainWindow.xaml), so the window's ASPECT is the canvas's aspect: clamp the width and the
    /// height one at a time and a window that only overflows on one axis comes out squashed, every
    /// glyph and picture with it. Nav polish wave 7 (owner, 2026-10-06: "make the default size of
    /// the window 25% bigger") raised the default to 1954 x 1179 DIP, which overflows the HEIGHT
    /// of a 1080p desk long before the width, so the old per-axis clamp would have shipped a
    /// stretched panel to most players. A window that does not fit now shrinks on BOTH axes by the
    /// same factor, the binding axis landing exactly on the work area.</para>
    ///
    /// <para>Floors (MinWidth / MinHeight) are not this rule's business: on a work area so small
    /// that the fitted size falls under a floor (a 300% TV), WPF enforces the floor afterwards and
    /// the result is the old per-axis shape, which is the best the floors allow.</para>
    /// </summary>
    public static class WindowFitRule
    {
        /// <summary>The panel's default size in DIP (MainWindow.xaml), x1.25 of the 1563 x 943 it opened at before.</summary>
        public const double DefaultWidthDip = 1954;
        public const double DefaultHeightDip = 1179;

        /// <summary>
        /// The largest size with the same aspect as <paramref name="width"/> x <paramref name="height"/>
        /// that fits inside the area. A size that already fits comes back unchanged (never grown).
        /// Unknown or non-positive inputs come back unchanged too: a broken measurement must never
        /// collapse the window.
        /// </summary>
        public static (double Width, double Height) Fit(double width, double height, double areaWidth, double areaHeight)
        {
            if (!(width > 0) || !(height > 0) || double.IsInfinity(width) || double.IsInfinity(height))
                return (width, height);
            if (!(areaWidth > 0) || !(areaHeight > 0) || double.IsInfinity(areaWidth) || double.IsInfinity(areaHeight))
                return (width, height);
            if (width <= areaWidth && height <= areaHeight) return (width, height);

            var scale = Math.Min(areaWidth / width, areaHeight / height);
            // The binding axis lands exactly on the area (no rounding gap), the other one scales.
            var w = Math.Min(areaWidth, width * scale);
            var h = Math.Min(areaHeight, height * scale);
            return (w, h);
        }

        /// <summary>
        /// <see cref="Fit(double, double, double, double)"/> in whole physical pixels, for the
        /// SetWindowPos path. Rounds to the nearest pixel and never past the area.
        /// </summary>
        public static (int Width, int Height) FitPx(int width, int height, int areaWidth, int areaHeight)
        {
            if (width <= 0 || height <= 0 || areaWidth <= 0 || areaHeight <= 0) return (width, height);
            if (width <= areaWidth && height <= areaHeight) return (width, height);
            var (w, h) = Fit(width, height, areaWidth, areaHeight);
            return (Math.Max(1, Math.Min(areaWidth, (int)Math.Round(w))),
                    Math.Max(1, Math.Min(areaHeight, (int)Math.Round(h))));
        }
    }
}
