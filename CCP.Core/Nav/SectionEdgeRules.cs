using System;
using ConditioningControlPanel.Fx;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Nav
{
    /// <summary>How the window edge line moves at a motion level.</summary>
    public enum SectionEdgeMotion
    {
        /// <summary>Full: the lift travels round the frame (the spin runs).</summary>
        Spin,
        /// <summary>Reduced: the lift sits still at the top centre.</summary>
        Fixed,
        /// <summary>Off: no lift, the line is the plain hue.</summary>
        Solid,
    }

    /// <summary>
    /// The section edge (nav polish wave 9, fog band polish 11), ported from WPF 7.1.5
    /// Controls/NavRail/SectionEdgeRules.cs with colours as ARGB <see cref="uint"/>. The window's
    /// 3 px frame and the faint band inside it wear the SECTION hue (NavStripRules.Accent), never
    /// the mod accent.
    /// </summary>
    public static class SectionEdgeRules
    {
        /// <summary>The frame line's alpha for every section (about 90%).</summary>
        public const byte LineAlpha = 0xE6;
        /// <summary>The line's travelling lift: the hue this far toward white, solid.</summary>
        public const double LiftWhite = 0.35;
        /// <summary>The glow band's depth into the window, native px.</summary>
        public const double GlowBand = 28;
        /// <summary>The glow's middle stop: offset, and its alpha as a share of the edge alpha.</summary>
        public const double GlowMidOffset = 0.45, GlowMidShare = 0.42;
        /// <summary>One lap of the travelling lift, and the frame rate it asks for.</summary>
        public const double SpinSeconds = 12;
        public const int SpinFps = 24;
        /// <summary>One leg of the lap: the lift crosses one side in a quarter of the lap.</summary>
        public const double LiftLegSeconds = SpinSeconds / 4;
        /// <summary>The lift strip's three stops: a soft band 20% wide around its middle.</summary>
        public static readonly double[] LiftOffsets = { 0.4, 0.5, 0.6 };
        /// <summary>The frame (MainWindow.xaml GlassWindowEdge): 3 px border, 8 px corner radius.</summary>
        public const double LineThickness = 3, CornerRadius = 8;
        /// <summary>The travelling lift rides four separate 3 px strips (SectionEdgeLift), inset 8 px
        /// from the corners, so a tick dirties a strip and never the full window.</summary>
        public const double LiftStripPx = 3, LiftStripInset = 8;

        /// <summary>The glow's edge alpha: 0.20 x sqrt(0.40 / luminance), clamped 0.14..0.26, as a byte.</summary>
        public static byte GlowAlpha(uint hue)
        {
            double lum = Math.Max(NavStripRules.Luminance(hue), 0.0001);
            double a = Math.Clamp(0.20 * Math.Sqrt(0.40 / lum), 0.14, 0.26);
            return (byte)Math.Round(a * 255);
        }

        /// <summary>The glow's middle stop alpha: round(0.42 x the edge alpha).</summary>
        public static byte GlowMidAlpha(uint hue) => (byte)Math.Round(GlowAlpha(hue) * GlowMidShare);

        /// <summary>The glow band's three stops, edge to inside: A, round(0.42 A) at 0.45, 0.</summary>
        public static uint[] GlowStops(uint hue) => new[]
        {
            Argb.WithAlpha(hue, GlowAlpha(hue)),
            Argb.WithAlpha(hue, GlowMidAlpha(hue)),
            Argb.WithAlpha(hue, (byte)0),
        };

        /// <summary>The static band's depth while the fog runs over it (polish 11).</summary>
        public const double FogBand = 16;
        /// <summary>The static band's alpha under the fog, as a share of its own (no fog) alpha.</summary>
        public const double FogBandShare = 0.7;

        /// <summary>The band's depth: the full 28 px with no fog, 16 under it.</summary>
        public static double BandDepth(bool fogLive) => fogLive ? FogBand : GlowBand;

        /// <summary>The band's stops with or without the fog over it.</summary>
        public static uint[] GlowStops(uint hue, bool fogLive)
        {
            if (!fogLive) return GlowStops(hue);
            byte edge = (byte)Math.Round(GlowAlpha(hue) * FogBandShare);
            return new[]
            {
                Argb.WithAlpha(hue, edge),
                Argb.WithAlpha(hue, (byte)Math.Round(edge * GlowMidShare)),
                Argb.WithAlpha(hue, (byte)0),
            };
        }

        /// <summary>The fog's alpha gain for a hue: the band's balance relative to its 0.20 centre.</summary>
        public static double FogGain(uint hue) => GlowAlpha(hue) / (0.20 * 255);

        /// <summary>The glow stops' offsets.</summary>
        public static readonly double[] GlowOffsets = { 0, GlowMidOffset, 1 };

        /// <summary>The frame line's three stops (offsets 0, 0.5, 1): the hue at 0xE6, three times.</summary>
        public static uint[] LineStops(uint hue, MotionLevel level)
        {
            var line = Argb.WithAlpha(hue, LineAlpha);
            return new[] { line, line, line };
        }

        /// <summary>The lift colour: the hue this far toward white, solid.</summary>
        public static uint LiftColor(uint hue) => NavStripRules.Mix(hue, Argb.White, LiftWhite);

        /// <summary>A lift strip's three stops (offsets 0.4, 0.5, 0.6): clear, the lift, clear.
        /// Off has no lift at all.</summary>
        public static uint[] LiftStops(uint hue, MotionLevel level)
        {
            var lift = LiftColor(hue);
            var clear = Argb.WithAlpha(lift, (byte)0);
            return level == MotionLevel.Off
                ? new[] { clear, clear, clear }
                : new[] { clear, lift, clear };
        }

        /// <summary>Where a side's lift starts and ends its leg (clockwise lap).</summary>
        public static (double From, double To) LiftTravel(EdgeSide side) => side switch
        {
            EdgeSide.Top => (-1, 1),
            EdgeSide.Right => (-1, 1),
            EdgeSide.Bottom => (1, -1),
            _ => (1, -1),
        };

        /// <summary>When a side's leg begins inside the lap, seconds: top 0, right 3, bottom 6, left 9.</summary>
        public static double LiftLegStart(EdgeSide side) => (int)side * LiftLegSeconds;

        /// <summary>Where a side's lift rests when it does not travel.</summary>
        public static double LiftRest(EdgeSide side, SectionEdgeMotion motion) =>
            motion == SectionEdgeMotion.Fixed && side == EdgeSide.Top ? 0 : LiftTravel(side).From;

        /// <summary>What the line does at a motion level (the spin also needs ambient loops).</summary>
        public static SectionEdgeMotion MotionFor(MotionLevel level, bool ambientLoopsAllowed) => level switch
        {
            MotionLevel.Off => SectionEdgeMotion.Solid,
            MotionLevel.Full when ambientLoopsAllowed => SectionEdgeMotion.Spin,
            _ => SectionEdgeMotion.Fixed,
        };

        /// <summary>The spin runs only while the window can be seen and is the one in use.</summary>
        public static bool SpinShouldRun(SectionEdgeMotion motion, bool active, bool visible, bool minimized) =>
            motion == SectionEdgeMotion.Spin && active && visible && !minimized;
    }
}
