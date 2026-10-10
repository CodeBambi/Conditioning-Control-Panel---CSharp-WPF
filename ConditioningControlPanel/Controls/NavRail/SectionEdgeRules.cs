using System;
using System.Windows.Media;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Controls.NavRail
{
    /// <summary>How the window edge line moves at a motion level.</summary>
    internal enum SectionEdgeMotion
    {
        /// <summary>Full: the lift travels round the frame (the spin runs).</summary>
        Spin,
        /// <summary>Reduced: the lift sits still at the top centre.</summary>
        Fixed,
        /// <summary>Off: no lift, the line is the plain hue.</summary>
        Solid,
    }

    /// <summary>
    /// The section edge (nav polish wave 9, 2026-10-06), pure so tests pin it without a window.
    /// The window's 3 px frame and the faint band inside it wear the SECTION hue
    /// (NavStripRules.Accent), not the mod accent; MainWindow.SectionEdge.cs is only the painter.
    /// </summary>
    internal static class SectionEdgeRules
    {
        /// <summary>The frame line's alpha for every section (about 90%).</summary>
        internal const byte LineAlpha = 0xE6;
        /// <summary>The line's travelling lift: the hue this far toward white, solid.</summary>
        internal const double LiftWhite = 0.35;
        /// <summary>The glow band's depth into the window, native px.</summary>
        internal const double GlowBand = 28;
        /// <summary>The glow's middle stop: offset, and its alpha as a share of the edge alpha.</summary>
        internal const double GlowMidOffset = 0.45, GlowMidShare = 0.42;
        /// <summary>One lap of the travelling lift, and the frame rate it asks for.</summary>
        internal const double SpinSeconds = 12;
        internal const int SpinFps = 24;
        /// <summary>One leg of the lap: the lift crosses one side in a quarter of the lap.</summary>
        internal const double LiftLegSeconds = SpinSeconds / 4;
        /// <summary>The lift strip's three stops: a soft band 20% wide around its middle.</summary>
        internal static readonly double[] LiftOffsets = { 0.4, 0.5, 0.6 };

        /// <summary>The glow's edge alpha balances perceived brightness on the dark page:
        /// 0.20 x sqrt(0.40 / luminance), clamped 0.14..0.26, as a byte. Light hues (Sage, Coral)
        /// take less, VioletBlue the most.</summary>
        internal static byte GlowAlpha(Color hue) => global::ConditioningControlPanel.Services.UI.NavStripPaint.EdgeGlowAlpha(NavStripRules.U(hue));

        /// <summary>The glow's middle stop alpha: round(0.42 x the edge alpha).</summary>
        internal static byte GlowMidAlpha(Color hue) => (byte)Math.Round(GlowAlpha(hue) * GlowMidShare);

        /// <summary>The glow band's three stops, edge to inside: A, round(0.42 A) at 0.45, 0.</summary>
        internal static Color[] GlowStops(Color hue) => new[]
        {
            NavRailRules.WithAlpha(hue, GlowAlpha(hue)),
            NavRailRules.WithAlpha(hue, GlowMidAlpha(hue)),
            NavRailRules.WithAlpha(hue, 0),
        };

        /// <summary>The static band's depth while the fog runs over it (polish 11): thinner, so the
        /// puffs carry the hue and the band only keeps the frame tied to the page.</summary>
        internal const double FogBand = 16;
        /// <summary>The static band's alpha under the fog, as a share of its own (no fog) alpha.</summary>
        internal const double FogBandShare = 0.7;

        /// <summary>The band's depth: the full 28 px with no fog (Off, low tiers), 16 under it.</summary>
        internal static double BandDepth(bool fogLive) => fogLive ? FogBand : GlowBand;

        /// <summary>The band's stops with or without the fog over it: the same shape, the edge
        /// alpha at <see cref="FogBandShare"/> when the fog runs.</summary>
        internal static Color[] GlowStops(Color hue, bool fogLive)
        {
            if (!fogLive) return GlowStops(hue);
            byte edge = (byte)Math.Round(GlowAlpha(hue) * FogBandShare);
            return new[]
            {
                NavRailRules.WithAlpha(hue, edge),
                NavRailRules.WithAlpha(hue, (byte)Math.Round(edge * GlowMidShare)),
                NavRailRules.WithAlpha(hue, 0),
            };
        }

        /// <summary>The fog's alpha gain for a hue: the band's balance (light hues take less,
        /// VioletBlue the most) relative to its 0.20 centre, so Sage and Sky read alike.</summary>
        internal static double FogGain(Color hue) => GlowAlpha(hue) / (0.20 * 255);

        /// <summary>The glow stops' offsets (pinned beside <see cref="GlowStops"/>).</summary>
        internal static readonly double[] GlowOffsets = { 0, GlowMidOffset, 1 };

        /// <summary>The frame line's three stops (offsets 0, 0.5, 1): the hue at 0xE6, three
        /// times. The line never carries the lift itself (see <see cref="LiftStops"/>), so it
        /// is the same at every motion level; the parameter stays so the painter reads one shape.</summary>
        internal static Color[] LineStops(Color hue, MotionLevel level)
        {
            var line = NavRailRules.WithAlpha(hue, LineAlpha);
            return new[] { line, line, line };
        }

        /// <summary>The lift colour: the hue this far toward white, solid.</summary>
        internal static Color LiftColor(Color hue) => NavStripRules.Mix(hue, Colors.White, LiftWhite);

        /// <summary>A lift strip's three stops (offsets 0.4, 0.5, 0.6): clear, the lift, clear.
        /// Off has no lift at all, so every stop is clear.</summary>
        internal static Color[] LiftStops(Color hue, MotionLevel level)
        {
            var lift = LiftColor(hue);
            var clear = NavRailRules.WithAlpha(lift, 0);
            return level == MotionLevel.Off
                ? new[] { clear, clear, clear }
                : new[] { clear, lift, clear };
        }

        /// <summary>Where a side's lift starts and ends its leg, in the brush's relative units:
        /// the lap runs clockwise, so top and right travel -1 -> 1 and bottom and left 1 -> -1.
        /// At either end the band sits off the strip.</summary>
        internal static (double From, double To) LiftTravel(EdgeSide side) => side switch
        {
            EdgeSide.Top => (-1, 1),
            EdgeSide.Right => (-1, 1),
            EdgeSide.Bottom => (1, -1),
            _ => (1, -1),
        };

        /// <summary>When a side's leg begins inside the lap, seconds: top 0, right 3, bottom 6, left 9.</summary>
        internal static double LiftLegStart(EdgeSide side) => (int)side * LiftLegSeconds;

        /// <summary>Where a side's lift rests when it does not travel: Reduced holds the top lift
        /// at the centre of the top edge, every other side parks its band off the strip.</summary>
        internal static double LiftRest(EdgeSide side, SectionEdgeMotion motion) =>
            motion == SectionEdgeMotion.Fixed && side == EdgeSide.Top ? 0 : LiftTravel(side).From;

        /// <summary>What the line does at a motion level. The spin also needs ambient loops
        /// allowed (a tier that refuses them gets the fixed lift, never a slower spin).</summary>
        internal static SectionEdgeMotion MotionFor(MotionLevel level, bool ambientLoopsAllowed) => level switch
        {
            MotionLevel.Off => SectionEdgeMotion.Solid,
            MotionLevel.Full when ambientLoopsAllowed => SectionEdgeMotion.Spin,
            _ => SectionEdgeMotion.Fixed,
        };

        /// <summary>The spin runs only while the window can be seen and is the one in use.</summary>
        internal static bool SpinShouldRun(SectionEdgeMotion motion, bool active, bool visible, bool minimized) =>
            motion == SectionEdgeMotion.Spin && active && visible && !minimized;
    }
}
