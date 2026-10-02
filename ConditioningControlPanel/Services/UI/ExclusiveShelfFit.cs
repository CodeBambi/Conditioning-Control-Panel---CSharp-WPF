using System;

namespace ConditioningControlPanel.Services.UI
{
    /// <summary>
    /// The Premium tab's collection grid: as many columns as fit, cards stretched to fill the row,
    /// so a wide window gets a fourth (or fifth) column instead of an empty strip on the right.
    /// </summary>
    public static class ExclusiveShelfFit
    {
        public const double BaseWidth = 336;
        public const double BaseHeight = 200;
        public const double MinWidth = 320;
        public const double Gap = 16;

        /// <summary>Columns, card width and card height for a shelf this wide.</summary>
        public static (int Columns, double Width, double Height) For(double available)
        {
            if (double.IsNaN(available) || available <= 0) return (1, BaseWidth, BaseHeight);
            var cols = Math.Max(1, (int)Math.Floor((available + Gap) / (MinWidth + Gap)));
            // Each card carries Gap on its right; one pixel of slack keeps WrapPanel rounding
            // from pushing the last card onto the next row.
            var width = Math.Floor(available / cols) - Gap - 1;
            if (cols == 1) width = Math.Min(width, BaseWidth * 1.5);
            width = Math.Max(240, width);
            var height = Math.Max(BaseHeight, Math.Round(width * BaseHeight / BaseWidth));
            return (cols, width, height);
        }
    }
}
