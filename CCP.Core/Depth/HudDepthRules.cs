using System;
using ConditioningControlPanel.Fx;

namespace ConditioningControlPanel.Depth
{
    /// <summary>
    /// PORTED from WPF 7.1.5 MainWindow/MainWindow.HudDepth.cs + the XP-row numbers of
    /// MainWindow.HeroFx.cs (nav polish waves 4, 10 and 11). The HUD is a recess (the band's top
    /// takes the well shade), the XP track a sunken groove with a tube in it, the LVL chip and the
    /// stat pills raised coins. Every shade is ink pulled toward the section hue by
    /// <see cref="DepthRules.ShadowColor(uint, double)"/>; the heads only paint these colours.
    /// </summary>
    public static class HudDepthRules
    {
        // ---- the tube's bead (polish wave 11) ----------------------------------------------------

        /// <summary>The shortest fill that shows the bead (it hangs half its 8 px past the tip).</summary>
        public const double XpBeadMinFillPx = 5.0;

        public static bool XpBeadVisible(double fillWidth) =>
            !double.IsNaN(fillWidth) && fillWidth >= XpBeadMinFillPx;

        // ---- the meniscus (Velvet Kit 2, 10 px since polish wave 11) -----------------------------

        public const double XpMeniscusPx = 10.0;
        public const int XpMeniscusSlideMs = 600;
        public const double XpMeniscusPulseSeconds = 2.0;
        public const double XpMeniscusMinOpacity = 0.35;
        public const double XpMeniscusMaxOpacity = 0.95;
        public const double XpMeniscusRestOpacity = 0.55;
        public const double XpMeniscusMinFillPx = 5.0;

        /// <summary>The meniscus X for a fill <paramref name="fillWidth"/> wide: centred on the tip.</summary>
        public static double XpMeniscusX(double fillWidth, double dot = XpMeniscusPx) =>
            Math.Max(0, fillWidth - dot / 2);

        /// <summary>Its resting opacity: hidden with no fill to sit on, still when ambient is off.</summary>
        public static double XpMeniscusOpacity(double fillWidth, bool ambientAllowed) =>
            fillWidth < XpMeniscusMinFillPx ? 0 : ambientAllowed ? XpMeniscusMaxOpacity : XpMeniscusRestOpacity;

        // ---- the LVL chip pop (Velvet Kit 2) -----------------------------------------------------

        public const int LevelChipPopMs = 600;
        public const double LevelChipPopScale = 1.35;
        public const double LevelChipPopDegrees = -4.0;

        /// <summary>The XP fill tween (MotionFx.BarFill): 0.6 s quadratic out.</summary>
        public const int XpFillMs = 600;

        /// <summary>The slow gloss along the fill, one 6 s pass on the ambient clock.</summary>
        public const double XpSheenSeconds = 6.0;

        // ---- shades ------------------------------------------------------------------------------

        /// <summary>The recess and the groove's top edge: the well's inner shade in the hue.</summary>
        public static uint WellTop(uint hue) => DepthRules.ShadowColor(hue, DepthRules.WellTopAlpha);

        /// <summary>The groove's left lip and the drum's foot turn.</summary>
        public static uint WellLeft(uint hue) => DepthRules.ShadowColor(hue, DepthRules.WellLeftAlpha);

        /// <summary>The LVL chip's drop band: the chip casts a shadow in its own colour.</summary>
        public static uint ChipDrop(uint chip) => DepthRules.ShadowColor(chip);

        /// <summary>The marquee drum's shade as (colour, offset) stops, top to foot: dark at the
        /// top (the lamp is above), clear across the middle where the text reads, a weaker turn at
        /// the foot so the face reads as a cylinder.</summary>
        public static (uint argb, double offset)[] DrumStops(uint hue)
        {
            var top = WellTop(hue);
            var foot = WellLeft(hue);
            return new[]
            {
                (top, 0.0),
                (Argb.WithAlpha(top, (byte)0), 0.34),
                (Argb.WithAlpha(foot, (byte)0), 0.70),
                (foot, 1.0),
            };
        }

        /// <summary>The drum's left lip, an absolute 10 px band whatever the banner's width.</summary>
        public const double DrumLipPx = 10.0;
    }
}
