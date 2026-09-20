using System;

namespace ConditioningControlPanel.Avalonia.Views.Deeper
{
    /// <summary>
    /// Platform-neutral timeline geometry shared by the editor renderers and rubber-band hit-test.
    /// Keeping the lane arithmetic here prevents selection from drifting when the visual bands move.
    /// </summary>
    internal static class DeeperEditorGeometry
    {
        internal enum TimelineLane { Regions, Effects, Haptics }

        internal static (double top, double height) LaneBand(TimelineLane lane, double canvasHeight)
        {
            if (canvasHeight <= 0) return (0, 0);
            var laneHeight = canvasHeight / 3.0;
            var top = lane switch
            {
                TimelineLane.Regions => 0,
                TimelineLane.Effects => laneHeight,
                TimelineLane.Haptics => 2 * laneHeight,
                _ => 0
            };
            return (top, laneHeight);
        }

        internal const double LaneInset = 2.0;

        internal static (double top, double height) LaneBandInset(TimelineLane lane, double canvasHeight)
        {
            var (top, height) = LaneBand(lane, canvasHeight);
            return (top + LaneInset, Math.Max(0, height - 2 * LaneInset));
        }

        internal static bool RangesOverlap(double a1, double a2, double b1, double b2)
            => a1 < b2 && a2 > b1;

        /// <summary>True when a rubber-band spanning [yMin, yMax] touches a lane.</summary>
        internal static bool BandHitsLane(TimelineLane lane, double yMin, double yMax, double canvasHeight)
        {
            var (top, height) = LaneBand(lane, canvasHeight);
            return height > 0 && RangesOverlap(yMin, yMax, top, top + height);
        }
    }
}
