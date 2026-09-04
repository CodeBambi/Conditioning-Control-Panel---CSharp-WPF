using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The portable half of the standalone corner-GIF overlays: everything the feature DECIDES.
    /// Which file a slot plays, where on screen it lands, how big and how transparent it draws,
    /// how far apart two slots realize. None of that needs a window, so none of it belongs in a
    /// head.
    ///
    /// <para>What is deliberately NOT here: the layered / click-through window itself, the GIF
    /// decode, the DPI read and the screen enumeration - all per-platform - and the ADMISSION rule
    /// ("may a standalone slot realize at all?"), which stays beside the session-side rule it must
    /// never disagree with, in the head's <c>CornerGifMedia</c>.</para>
    ///
    /// <para>Pure and static: no clock, no settings read, no I/O beyond a <c>File.Exists</c> probe
    /// on paths the caller hands in. Every value it returns is finite, or null.</para>
    /// </summary>
    public static class CornerGifPlanner
    {
        /// <summary>Hard cap on simultaneous overlays, independent of what settings.json holds -
        /// the config UI only ever writes two slots, but a hand-edited or migrated file must not be
        /// able to spawn an unbounded number of topmost layered windows.</summary>
        public const int MaxOverlays = 2;

        /// <summary>Longest-edge target when a slot carries no size of its own.</summary>
        public const int DefaultSize = 300;

        /// <summary>Diagonal inset (logical px) applied per slot that lands in an already-claimed
        /// corner. Two slots on identical pixels means two topmost animating windows fighting for
        /// z-order, and one of them simply looks gone.</summary>
        public const double SameCornerNudge = 40;

        /// <summary>Real-time gap between two slots' realizations (#958). A dispatcher pass alone
        /// was not enough: both slots still drained in the same pump, so the second layered surface
        /// was created while the first GIF's animator was already driving the render thread.</summary>
        public const int StaggerMs = 400;

        /// <summary>Where one slot's overlay goes, in logical pixels, and how opaque it draws.
        /// Opacity is 0..1, ready for a head's window property.</summary>
        public readonly struct Placement
        {
            public Placement(double left, double top, double width, double height, double opacity)
            {
                Left = left; Top = top; Width = width; Height = height; Opacity = opacity;
            }

            public double Left { get; }
            public double Top { get; }
            public double Width { get; }
            public double Height { get; }
            public double Opacity { get; }
        }

        /// <summary>
        /// Which file this slot should play, or null for "no file - draw your own default".
        ///
        /// <para>Resolution order, unchanged from the WPF original: the slot's explicit pick, then
        /// the Spiral Library's active selection (<c>AppSettings.SpiralPath</c>, the "pool"), then
        /// nothing. An enabled-but-unpicked slot therefore draws whatever spiral the app is already
        /// using rather than a separate file.</para>
        ///
        /// <para>Null does NOT mean "no art": it means the head resolves its own default, which on
        /// every head is the active mod's spiral (<see cref="CoreModArt.SpiralOverridePath"/>) and
        /// otherwise its shipped pre-scaled corner asset. That last step stays head-side because
        /// only a head knows where its own art lives, and on WPF it is a <c>pack://</c> URI that
        /// Core is forbidden to name.</para>
        /// </summary>
        public static string? ResolveSourcePath(CornerGifOverlaySetting? setting, string? poolSpiralPath)
        {
            if (Playable(setting?.GifPath)) return setting!.GifPath;
            if (Playable(poolSpiralPath)) return poolSpiralPath;
            return null;
        }

        private static bool Playable(string? path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            try { return File.Exists(path); }
            catch { return false; }
        }

        /// <summary>
        /// Scale the source to the slot's longest-edge size and pin it to the chosen corner of a
        /// screen <paramref name="screenWidth"/> x <paramref name="screenHeight"/> logical pixels.
        ///
        /// <para>Returns null rather than a degenerate rectangle. Bug #625: a 0x0 source makes the
        /// scale divide by zero, and handing WPF a NaN Width threw deep inside layout and took the
        /// whole app down on the startup restore path. Every caller must treat null as "skip this
        /// overlay and say why", never as "use a default size".</para>
        ///
        /// <para><paramref name="earlierSlotsInSameCorner"/> comes from
        /// <see cref="CountEarlierSlotsInCorner"/>: counted from SETTINGS rather than from live
        /// windows, so the offset is the same regardless of which slot realizes first.</para>
        /// </summary>
        public static Placement? Place(
            CornerGifOverlaySetting? setting,
            double sourceWidth, double sourceHeight,
            double screenWidth, double screenHeight,
            int earlierSlotsInSameCorner)
        {
            if (setting == null) return null;
            if (!Finite(sourceWidth) || !Finite(sourceHeight) || sourceWidth <= 0 || sourceHeight <= 0) return null;
            if (!Finite(screenWidth) || !Finite(screenHeight)) return null;

            double target = setting.Size > 0 ? setting.Size : DefaultSize;
            double scale = target / Math.Max(sourceWidth, sourceHeight);
            double width = sourceWidth * scale;
            double height = sourceHeight * scale;

            if (!Finite(scale) || !Finite(width) || !Finite(height) || width <= 0 || height <= 0) return null;

            double left = 0, top = 0;
            switch (setting.Position)
            {
                case CornerPosition.TopLeft: left = 0; top = 0; break;
                case CornerPosition.TopRight: left = screenWidth - width; top = 0; break;
                case CornerPosition.BottomLeft: left = 0; top = screenHeight - height; break;
                case CornerPosition.BottomRight: left = screenWidth - width; top = screenHeight - height; break;
            }

            if (earlierSlotsInSameCorner > 0)
            {
                double nudge = SameCornerNudge * earlierSlotsInSameCorner;
                left += setting.Position is CornerPosition.TopLeft or CornerPosition.BottomLeft ? nudge : -nudge;
                top += setting.Position is CornerPosition.TopLeft or CornerPosition.TopRight ? nudge : -nudge;
            }

            if (!Finite(left) || !Finite(top)) return null;

            return new Placement(left, top, width, height, Math.Clamp(setting.Opacity, 1, 100) / 100.0);
        }

        private static bool Finite(double d) => !double.IsNaN(d) && !double.IsInfinity(d);

        /// <summary>How many ENABLED slots before <paramref name="index"/> already claimed
        /// <paramref name="position"/>. Feeds the same-corner nudge in <see cref="Place"/>.</summary>
        public static int CountEarlierSlotsInCorner(
            IList<CornerGifOverlaySetting>? overlays, int index, CornerPosition position)
        {
            if (overlays == null) return 0;
            int count = 0;
            for (int i = 0; i < index && i < overlays.Count; i++)
            {
                var o = overlays[i];
                if (o != null && o.Enabled && o.Position == position) count++;
            }
            return count;
        }

        /// <summary>
        /// How long this slot must wait before it realizes, so no two slots create a layered
        /// surface back to back. <paramref name="cursor"/> is the caller's monotonic "earliest next
        /// realization" tick and is advanced by <see cref="StaggerMs"/>; pass 0 for a fresh queue.
        /// Answers 0 when the cursor is already in the past, which is the common single-slot case.
        /// </summary>
        public static long NextRealizeDelayMs(ref long cursor, long nowTick)
        {
            long at = Math.Max(nowTick, cursor);
            cursor = at + StaggerMs;
            return Math.Max(0, at - nowTick);
        }
    }
}
