using ConditioningControlPanel.Depth;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Nav
{
    /// <summary>
    /// PORTED from WPF 7.1.5 Controls/NavRail/SectionTabStrip.Fx.cs (NavStripFxRules), numbers
    /// unchanged. The pills' motion: hover lifts and grows the pill a touch and the glyph gives a
    /// little wiggle; choosing a pill rings it and throws a small spark burst; the active pill
    /// catches a sheen every 9 s. Full = all of it, Reduced = the lift only, Off = nothing.
    /// </summary>
    public static class NavStripFxRules
    {
        public const double HoverLiftPx = DepthRules.HoverLiftPx;
        public const double HoverScale = 1.035;
        public const int HoverInMs = 120;
        public const int HoverOutMs = 160;
        public const double WiggleDegrees = 8;
        public const int WiggleMs = 280;
        public const int SheenEveryMs = 9000;
        public const int SheenMs = 650;
        public const double SheenWidth = 40;
        public const double SheenOpacity = 0.22;
        public const int BurstSparks = 18;
        /// <summary>How far the spark layer spills past the track on every side.</summary>
        public const double SparkBleed = 12;

        /// <summary>The hover lift at a motion level (0 = none).</summary>
        public static double LiftFor(MotionLevel level) => level == MotionLevel.Off ? 0 : HoverLiftPx;

        /// <summary>The hover scale at a motion level (Reduced lifts only).</summary>
        public static double ScaleFor(MotionLevel level) => level == MotionLevel.Full ? HoverScale : 1.0;

        /// <summary>The glyph wiggles on Full only.</summary>
        public static bool Wiggles(MotionLevel level) => level == MotionLevel.Full;

        /// <summary>The idle sheen clock runs only on Full, on a tier that allows ambient motion,
        /// while the strip is on screen and the main window's chrome loops are allowed.</summary>
        public static bool SheenRuns(MotionLevel level, bool tierAllowsAmbient, bool visible, bool chromeAllows) =>
            level == MotionLevel.Full && tierAllowsAmbient && visible && chromeAllows;
    }
}
