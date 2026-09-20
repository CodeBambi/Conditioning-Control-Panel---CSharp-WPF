using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Views.Deeper
{
    // Pure, Window-free helpers for the Deeper editor timeline so the lane
    // math, colour picking and time formatting can be unit-tested without
    // spinning up WPF. The editor partials import this with `using static`.
    internal static class DeeperEditorGeometry
    {
        // The timeline canvas is split into three equal horizontal lanes, top to
        // bottom: Regions, Effects, Haptics. Rules are NOT a lane - they render as
        // full-height pins layered across all three. This is the single source of
        // truth for lane geometry; every Build*/hit-test path resolves its Y band
        // through LaneBand().
        internal enum TimelineLane { Regions, Effects, Haptics }

        internal static (double top, double height) LaneBand(TimelineLane lane, double canvasHeight)
        {
            if (canvasHeight <= 0) return (0, 0);
            double laneH = canvasHeight / 3.0;
            double top = lane switch
            {
                TimelineLane.Regions => 0,
                TimelineLane.Effects => laneH,
                TimelineLane.Haptics => 2 * laneH,
                _ => 0
            };
            return (top, laneH);
        }

        // Inset band rect for a lane (a few px of breathing room above/below so
        // adjacent lanes read as distinct). Used by region/haptic/effect-segment bands.
        internal const double LaneInset = 2.0;
        internal static (double top, double height) LaneBandInset(TimelineLane lane, double canvasHeight)
        {
            var (top, height) = LaneBand(lane, canvasHeight);
            return (top + LaneInset, Math.Max(0, height - 2 * LaneInset));
        }

        /// <summary>Transport readout: m:ss.f (h:mm:ss.f past an hour), tenths floored
        /// so the label never shows a second the playhead has not reached.</summary>
        internal static string FormatTransportTime(double seconds)
        {
            if (double.IsNaN(seconds) || seconds < 0) seconds = 0;
            long tenths = (long)Math.Floor(seconds * 10 + 1e-6);
            long s = tenths / 10, t = tenths % 10;
            long h = s / 3600, m = (s % 3600) / 60, sec = s % 60;
            return h > 0 ? $"{h}:{m:D2}:{sec:D2}.{t}" : $"{m}:{sec:D2}.{t}";
        }

        /// <summary>Hover readout over the ruler / canvas: m:ss.ff.</summary>
        internal static string FormatHoverTime(double seconds)
        {
            if (double.IsNaN(seconds) || seconds < 0) seconds = 0;
            long hund = (long)Math.Floor(seconds * 100 + 1e-6);
            long s = hund / 100, c = hund % 100;
            long h = s / 3600, m = (s % 3600) / 60, sec = s % 60;
            return h > 0 ? $"{h}:{m:D2}:{sec:D2}.{c:D2}" : $"{m}:{sec:D2}.{c:D2}";
        }

        internal static bool RangesOverlap(double a1, double a2, double b1, double b2) => a1 < b2 && a2 > b1;

        /// <summary>True when a rubber-band spanning [yMin, yMax] touches the given lane.</summary>
        internal static bool BandHitsLane(TimelineLane lane, double yMin, double yMax, double canvasHeight)
        {
            var (top, height) = LaneBand(lane, canvasHeight);
            if (height <= 0) return false;
            return RangesOverlap(yMin, yMax, top, top + height);
        }

        /// <summary>
        /// Picks the palette entry used by the fewest existing items (first wins on
        /// ties) so neighbours stop repeating after deletes shift the count.
        /// </summary>
        internal static string LeastUsedPaletteColor(IReadOnlyList<string> palette, IEnumerable<string?> used)
        {
            if (palette.Count == 0) return "#7B5CFF";
            var counts = new int[palette.Count];
            foreach (var u in used)
            {
                if (string.IsNullOrEmpty(u)) continue;
                for (int i = 0; i < palette.Count; i++)
                {
                    if (string.Equals(palette[i], u, StringComparison.OrdinalIgnoreCase)) { counts[i]++; break; }
                }
            }
            int best = 0;
            for (int i = 1; i < palette.Count; i++)
                if (counts[i] < counts[best]) best = i;
            return palette[best];
        }
    }
}
