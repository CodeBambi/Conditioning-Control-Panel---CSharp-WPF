using System;
using System.Windows;
using System.Windows.Media;
using ConditioningControlPanel.Controls.Depth;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Depth for the HUD (nav polish wave 10, lane B). The XAML carries the recipe (the band is a
    /// recess, the XP track a groove with a tube in it, the chip and stat pills raised coins, the
    /// START row raised planks pressed through <see cref="Controls.HudPlank"/>); this file only
    /// re-tints the shadows from code, because a shadow takes the colour of what casts it:
    ///  * <see cref="PaintHudChipDepth"/>: the LVL chip's drop band, from the chip's own colour
    ///    (called by PaintLevelChip, so mod accent and Lockdown both reach it).
    ///  * <see cref="PaintDepthHud"/>: the recess and the groove, from the live section hue. Not
    ///    called here; the integrator wires it from PaintSectionWash. Static paint, no clocks.
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>A vertical drop band: the shadow colour at the contact edge, gone at the foot.</summary>
        internal static LinearGradientBrush HudDropBand(Color shadow) =>
            HudFade(shadow, new Point(0, 0), new Point(0, 1));

        /// <summary>A shadow fading from <paramref name="start"/> to <paramref name="end"/>, frozen.</summary>
        internal static LinearGradientBrush HudFade(Color shadow, Point start, Point end)
        {
            var b = new LinearGradientBrush { StartPoint = start, EndPoint = end };
            b.GradientStops.Add(new GradientStop(shadow, 0));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0, shadow.R, shadow.G, shadow.B), 1));
            b.Freeze();
            return b;
        }

        /// <summary>The LVL chip casts a shadow in its own colour (pulled from ink by ShadowHuePull).</summary>
        internal void PaintHudChipDepth(Color chip)
        {
            try
            {
                if (LevelChipDrop == null) return;
                LevelChipDrop.Background = HudDropBand(DepthRules.ShadowColor(chip));
            }
            catch (Exception ex) { App.Logger?.Debug("PaintHudChipDepth: {E}", ex.Message); }
        }

        /// <summary>The HUD's recess and the XP groove take the section hue in their inner shade
        /// (WellTopAlpha on top edges, WellLeftAlpha on the groove's left lip).</summary>
        internal void PaintDepthHud(Color hue)
        {
            try
            {
                var top = DepthRules.ShadowColor(hue, DepthRules.WellTopAlpha);
                var left = DepthRules.ShadowColor(hue, DepthRules.WellLeftAlpha);
                if (HudBandWellTop != null) HudBandWellTop.Fill = HudFade(top, new Point(0, 0), new Point(0, 1));
                if (XPGrooveTop != null) XPGrooveTop.Background = HudFade(top, new Point(0, 0), new Point(0, 1));
                if (XPGrooveLeft != null) XPGrooveLeft.Background = HudFade(left, new Point(0, 0), new Point(1, 0));
                PaintBannerDepth(hue);
            }
            catch (Exception ex) { App.Logger?.Debug("PaintDepthHud: {E}", ex.Message); }
        }

        /// <summary>Polish wave 13: the marquee drum's shade in the section hue. Dark at the top
        /// (WellTopAlpha, the lamp is above), clear across the middle where the text reads, and a
        /// weaker turn at the foot (WellLeftAlpha) so the face reads as a cylinder.</summary>
        internal static LinearGradientBrush BannerDrumBrush(Color hue)
        {
            var top = DepthRules.ShadowColor(hue, DepthRules.WellTopAlpha);
            var foot = DepthRules.ShadowColor(hue, DepthRules.WellLeftAlpha);
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            b.GradientStops.Add(new GradientStop(top, 0));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0, top.R, top.G, top.B), 0.34));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0, foot.R, foot.G, foot.B), 0.70));
            b.GradientStops.Add(new GradientStop(foot, 1));
            b.Freeze();
            return b;
        }

        private void PaintBannerDepth(Color hue)
        {
            if (BannerDrumShade != null) BannerDrumShade.Background = BannerDrumBrush(hue);
            if (BannerDrumLip != null) BannerDrumLip.Background = LipBrush(DepthRules.ShadowColor(hue, DepthRules.WellLeftAlpha));
        }

        /// <summary>The well's left band, 10 px wide whatever the banner's width (absolute mapping).</summary>
        private static LinearGradientBrush LipBrush(Color shadow)
        {
            var b = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(10, 0),
                MappingMode = BrushMappingMode.Absolute,
            };
            b.GradientStops.Add(new GradientStop(shadow, 0));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0, shadow.R, shadow.G, shadow.B), 1));
            b.Freeze();
            return b;
        }

        // ============================== the tube's bead (polish wave 11) ==============================

        /// <summary>The shortest fill that shows the bead. It hangs half its 8 px past the fill's
        /// edge, so below this it would sit past the track start; the meniscus hides at the same
        /// width (XpMeniscusMinFillPx).</summary>
        internal const double XpBeadMinFillPx = 5.0;

        /// <summary>Whether a fill <paramref name="fillWidth"/> px wide carries the bead.</summary>
        internal static bool XpBeadVisible(double fillWidth) =>
            !double.IsNaN(fillWidth) && fillWidth >= XpBeadMinFillPx;

        /// <summary>Shows or hides <paramref name="bead"/> for a fill <paramref name="fillWidth"/> px wide.
        /// Hidden, not Collapsed: the bead keeps no layout of its own to give back.</summary>
        internal static void ApplyXpBeadRule(UIElement? bead, double fillWidth)
        {
            if (bead == null) return;
            var want = XpBeadVisible(fillWidth) ? Visibility.Visible : Visibility.Hidden;
            if (bead.Visibility != want) bead.Visibility = want;
        }

        /// <summary>The fill's width is tweened (MotionFx.BarFill) or set outright; either way its
        /// size changes, and the bead follows the rule. Only touches the bead when the answer flips.</summary>
        private void XPBar_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            try { ApplyXpBeadRule(XPTubeBead, e.NewSize.Width); }
            catch (Exception ex) { App.Logger?.Debug("XPBar_SizeChanged: {E}", ex.Message); }
        }
    }
}
