using System;
using System.Windows.Media;
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
        /// <summary>The lift's rest angle when it does not travel. The brush runs left to right,
        /// so at 0 the lift band is vertical and meets the frame at the top (and bottom) centre.</summary>
        internal const double FixedAngle = 0;

        /// <summary>The glow's edge alpha balances perceived brightness on the dark page:
        /// 0.20 x sqrt(0.40 / luminance), clamped 0.14..0.26, as a byte. Light hues (Sage, Coral)
        /// take less, VioletBlue the most.</summary>
        internal static byte GlowAlpha(Color hue)
        {
            double lum = Math.Max(NavStripRules.Luminance(hue), 0.0001);
            double a = Math.Clamp(0.20 * Math.Sqrt(0.40 / lum), 0.14, 0.26);
            return (byte)Math.Round(a * 255);
        }

        /// <summary>The glow's middle stop alpha: round(0.42 x the edge alpha).</summary>
        internal static byte GlowMidAlpha(Color hue) => (byte)Math.Round(GlowAlpha(hue) * GlowMidShare);

        /// <summary>The glow band's three stops, edge to inside: A, round(0.42 A) at 0.45, 0.</summary>
        internal static Color[] GlowStops(Color hue) => new[]
        {
            NavRailRules.WithAlpha(hue, GlowAlpha(hue)),
            NavRailRules.WithAlpha(hue, GlowMidAlpha(hue)),
            NavRailRules.WithAlpha(hue, 0),
        };

        /// <summary>The glow stops' offsets (pinned beside <see cref="GlowStops"/>).</summary>
        internal static readonly double[] GlowOffsets = { 0, GlowMidOffset, 1 };

        /// <summary>The frame line's three stops (offsets 0, 0.5, 1): the hue at 0xE6, the lift
        /// (hue toward white, solid), the hue again. Off has no lift: all three are the hue.</summary>
        internal static Color[] LineStops(Color hue, MotionLevel level)
        {
            var line = NavRailRules.WithAlpha(hue, LineAlpha);
            var lift = level == MotionLevel.Off ? line : NavStripRules.Mix(hue, Colors.White, LiftWhite);
            return new[] { line, lift, line };
        }

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
