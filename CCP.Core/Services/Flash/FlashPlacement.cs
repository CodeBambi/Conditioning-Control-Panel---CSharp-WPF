using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Flash
{
    /// <summary>
    /// Where a flash image goes and how big it is - the pure half of <c>FlashService</c>
    /// (CalculateGeometry / PickSpawnPoint / SpawnBounds / the #770 avoid-center bands / the
    /// anti-overlap test), moved here unchanged so the WPF head and the Avalonia head place
    /// flashes with one copy of the math. All inputs are in ONE monitor's DIPs, as in WPF.
    /// </summary>
    public static class FlashPlacement
    {
        /// <summary>Keep targets away from screen edges so they're fully visible and clickable.</summary>
        public const int SpawnEdgePadding = 50;

        /// <summary>Smallest exclusion box the adaptive shrink will fall back to, in percent.</summary>
        public const int MinExclusionPercent = 5;

        /// <summary>Largest exclusion box the setting allows, in percent (matches the AppSettings clamp).</summary>
        public const int MaxExclusionPercent = 60;

        /// <summary>
        /// How much of the padded spawn area must remain legal before the requested exclusion box is
        /// accepted. Below this the placement is technically legal but visually degenerate — at the
        /// default 25% a monitor-aspect image at ImageScale=100 leaves 1.4%, i.e. two ~8px-wide
        /// slivers, so every flash lands in the same two columns.
        /// </summary>
        public const double MinLegalAreaFraction = 0.10;

        /// <summary>File extensions the flash pool draws from (FlashService.RefreshImageLists).</summary>
        public static readonly string[] ImageExtensions =
            { ".png", ".jpg", ".jpeg", ".jpe", ".jfif", ".gif", ".webp", ".bmp", ".tif", ".tiff", ".heic", ".avif", ".ico" };

        /// <summary>The Fade slider is a percentage where 100% = a one second ramp, so the default
        /// 40% lands on 0.4 s.</summary>
        public const double FadeSecondsPerPercent = 0.01;

        /// <summary>
        /// Display size of an <paramref name="origWidth"/>x<paramref name="origHeight"/> image on a
        /// monitor: fit inside 40% of the monitor (matching the original Python), times the user's
        /// ImageScale (<paramref name="scale"/> 0.5-2.5), never below 50.
        /// </summary>
        public static (int Width, int Height) FitSize(int origWidth, int origHeight, int monW, int monH, double scale)
        {
            var ratio = Math.Min(monW * 0.4 / origWidth, monH * 0.4 / origHeight) * scale;
            return (Math.Max(50, (int)(origWidth * ratio)), Math.Max(50, (int)(origHeight * ratio)));
        }

        /// <summary>
        /// Top-left spawn point for a <paramref name="w"/>x<paramref name="h"/> image on the monitor
        /// at (<paramref name="monX"/>,<paramref name="monY"/>), honouring the #770 exclusion box when
        /// <paramref name="avoidCenter"/> is on. <paramref name="avoidFellBack"/> is true when the box
        /// was on but nothing legal remained even at the floor, so the pick is unconstrained - the
        /// caller logs that, since with the feature on it puts flashes back on the crosshair.
        /// </summary>
        public static (int X, int Y) PickSpawnPoint(int monX, int monY, int monW, int monH, int w, int h,
            bool avoidCenter, int pct, Random random, out bool avoidFellBack)
        {
            avoidFellBack = false;
            if (avoidCenter)
            {
                if (TryPickAvoidCenterPointAdaptive(monW, monH, w, h, pct, random, out int lx, out int ly, out _))
                    return (monX + lx, monY + ly);
                avoidFellBack = true;
            }

            var (minX, minY, maxX, maxY) = SpawnBounds(monW, monH, w, h);
            return (monX + random.Next(minX, maxX), monY + random.Next(minY, maxY));
        }

        /// <summary>
        /// True when a new rect would cover more than 30% of its own area with any of
        /// <paramref name="others"/> - the anti-overlap test the spawn retries against (10 attempts).
        /// </summary>
        public static bool IsOverlapping(int x, int y, int w, int h, IEnumerable<(int X, int Y, int W, int H)> others)
        {
            foreach (var o in others)
            {
                var dx = Math.Min(x + w, o.X + o.W) - Math.Max(x, o.X);
                var dy = Math.Min(y + h, o.Y + o.H) - Math.Max(y, o.Y);
                if (dx >= 0 && dy >= 0 && dx * dy > w * h * 0.3) return true;
            }
            return false;
        }

        /// <summary>
        /// The unconstrained legal range for a top-left spawn point, in monitor-local DIPs.
        /// Ranges are half-open on the max end, matching <see cref="Random.Next(int,int)"/>.
        /// </summary>
        public static (int MinX, int MinY, int MaxX, int MaxY) SpawnBounds(int monW, int monH, int w, int h)
        {
            var minX = SpawnEdgePadding;
            var minY = SpawnEdgePadding;
            var maxX = Math.Max(minX + 1, monW - w - SpawnEdgePadding);
            var maxY = Math.Max(minY + 1, monH - h - SpawnEdgePadding);
            return (minX, minY, maxX, maxY);
        }

        /// <summary>
        /// #770 — band remap (NOT rejection sampling). Builds the centered exclusion square
        /// (<paramref name="pct"/>% of the SHORTER monitor edge, per-monitor) and splits the legal
        /// area into 4 DISJOINT bands where a <paramref name="w"/>x<paramref name="h"/> image fits
        /// without touching it: left / right / above / below. A band is picked weighted by its area
        /// and the point is then uniform inside it, so the result is uniform over the whole legal
        /// region in a single roll — no retry loop, no worst-case starvation.
        /// </summary>
        /// <returns>false when the total legal area is 0 (image too large); caller falls back.</returns>
        public static bool TryPickAvoidCenterPoint(
            int monW, int monH, int w, int h, int pct, Random random, out int x, out int y)
        {
            x = y = 0;

            var b = new AvoidCenterBands(monW, monH, w, h, pct);
            long total = b.TotalArea;
            if (total <= 0) return false;

            // Weighted band pick, then uniform inside the chosen band.
            long roll = (long)(random.NextDouble() * total);
            if (roll >= total) roll = total - 1; // guard the 1.0 edge
            Span<long> areas = stackalloc long[4] { b.LeftArea, b.RightArea, b.AboveArea, b.BelowArea };
            int band = 0;
            for (; band < 3; band++)
            {
                if (roll < areas[band]) break;
                roll -= areas[band];
            }

            switch (band)
            {
                case 0: x = random.Next(b.MinX, b.LeftMaxX); y = random.Next(b.MinY, b.MaxY); break;
                case 1: x = random.Next(b.RightMinX, b.MaxX); y = random.Next(b.MinY, b.MaxY); break;
                case 2: x = random.Next(b.StripMinX, b.StripMaxX); y = random.Next(b.MinY, b.AboveMaxY); break;
                default: x = random.Next(b.StripMinX, b.StripMaxX); y = random.Next(b.BelowMinY, b.MaxY); break;
            }
            return true;
        }

        /// <summary>
        /// #770 follow-up — the exclusion box has to DEGRADE, not vanish. A flash at the default
        /// ImageScale is 40% of the monitor's width, so on 1920x1080 an ordinary 768x432 flash has no
        /// legal band at all at 30% (the feature silently became a no-op and flashes went back to the
        /// crosshair) and only 1.4% of the spawn area at the default 25%. This shrinks the effective
        /// percentage 5 points at a time, down to <see cref="MinExclusionPercent"/>, until at least
        /// <see cref="MinLegalAreaFraction"/> of the spawn area is legal, and picks with THAT box.
        /// Legal area is monotonic in the percentage (a smaller square is a subset of a bigger one),
        /// so the first percentage that clears the bar is also the largest one that does — the user's
        /// setting is honoured as far as the image size allows.
        /// </summary>
        /// <param name="effectivePct">The percentage actually used (&lt;= the requested one).</param>
        /// <returns>false only when even the floor leaves nothing (image bigger than the spawn area).</returns>
        public static bool TryPickAvoidCenterPointAdaptive(
            int monW, int monH, int w, int h, int pct, Random random,
            out int x, out int y, out int effectivePct)
        {
            x = y = 0;
            effectivePct = Math.Clamp(pct, MinExclusionPercent, MaxExclusionPercent);

            while (true)
            {
                var bands = new AvoidCenterBands(monW, monH, w, h, effectivePct);
                if (bands.LegalFraction >= MinLegalAreaFraction || effectivePct <= MinExclusionPercent)
                {
                    if (bands.TotalArea <= 0) return false;   // at the floor and still nowhere to go
                    return TryPickAvoidCenterPoint(monW, monH, w, h, effectivePct, random, out x, out y);
                }
                effectivePct = Math.Max(MinExclusionPercent, effectivePct - 5);
            }
        }

        /// <summary>
        /// Legal band area as a fraction (0..1) of the unconstrained padded spawn area. Exposed for
        /// the tests that pin the adaptive shrink's "at least 10% of the screen stays usable" bar.
        /// </summary>
        public static double LegalAreaFraction(int monW, int monH, int w, int h, int pct)
            => new AvoidCenterBands(monW, monH, w, h, pct).LegalFraction;

        /// <summary>
        /// The 4 disjoint legal bands around the #770 exclusion square plus their areas. One place so
        /// the pick and the adaptive shrink can never measure different regions.
        /// </summary>
        private readonly struct AvoidCenterBands
        {
            public readonly int MinX, MinY, MaxX, MaxY;
            public readonly int LeftMaxX, RightMinX, StripMinX, StripMaxX, AboveMaxY, BelowMinY;
            public readonly long LeftArea, RightArea, AboveArea, BelowArea;

            public AvoidCenterBands(int monW, int monH, int w, int h, int pct)
            {
                (MinX, MinY, MaxX, MaxY) = SpawnBounds(monW, monH, w, h);

                // Centered exclusion square, sized off the shorter edge so it stays square on ultrawides.
                double side = Math.Clamp(pct, MinExclusionPercent, MaxExclusionPercent) / 100.0 * Math.Min(monW, monH);
                int exLeft = (int)Math.Round((monW - side) / 2.0);
                int exTop = (int)Math.Round((monH - side) / 2.0);
                int exRight = exLeft + (int)Math.Round(side);
                int exBottom = exTop + (int)Math.Round(side);

                // An image at local (x,y) misses the box iff it is fully left (x + w <= exLeft),
                // fully right (x >= exRight), fully above (y + h <= exTop) or fully below (y >= exBottom).
                // Left/right take the FULL y range; above/below take only the x-strip left over between
                // them, which keeps the 4 bands disjoint so area weighting stays a true uniform.
                LeftMaxX = Math.Min(MaxX, exLeft - w + 1);   // exclusive
                RightMinX = Math.Max(MinX, exRight);
                StripMinX = Math.Max(MinX, LeftMaxX);
                StripMaxX = Math.Min(MaxX, RightMinX);       // exclusive
                AboveMaxY = Math.Min(MaxY, exTop - h + 1);   // exclusive
                BelowMinY = Math.Max(MinY, exBottom);

                LeftArea = Area(MinX, LeftMaxX, MinY, MaxY);
                RightArea = Area(RightMinX, MaxX, MinY, MaxY);
                AboveArea = Area(StripMinX, StripMaxX, MinY, AboveMaxY);
                BelowArea = Area(StripMinX, StripMaxX, BelowMinY, MaxY);
            }

            public long TotalArea => LeftArea + RightArea + AboveArea + BelowArea;

            /// <summary>Unconstrained padded spawn area, the denominator for <see cref="LegalFraction"/>.</summary>
            public long SpawnArea => (long)Math.Max(0, MaxX - MinX) * Math.Max(0, MaxY - MinY);

            public double LegalFraction => SpawnArea <= 0 ? 0.0 : (double)TotalArea / SpawnArea;

            private static long Area(int x0, int x1, int y0, int y1)
                => (long)Math.Max(0, x1 - x0) * Math.Max(0, y1 - y0);
        }
    }
}
