// PORTED from ConditioningControlPanel/Controls/NavRail/SectionTabStrip.xaml.cs (NavStripRules
// :69-:465, nav polish waves 2/3/7/9: 495b7f429, 5dfaf3949, d38c3ae3d, e5d5afb61, 6314122c3): the
// strip's paint. The math lives in Core NavStripPaint (0xAARRGGBB, shared with WPF); this is the
// Avalonia Color wrapper plus the brushes.

using System;
using Avalonia;
using Avalonia.Media;
using ConditioningControlPanel.Services.UI;
using P = ConditioningControlPanel.Services.UI.NavStripPaint;

namespace ConditioningControlPanel.Avalonia.Views.Controls
{
    internal static class NavStripRules
    {
        private static Color C(uint c) => Color.FromUInt32(c);
        private static uint U(Color c) => c.ToUInt32();

        public static Color Accent(string? section) => C(P.Accent(section));
        public static Color Ink(string? section) => C(P.Ink(section));
        public static Color Tint(string? section) => C(P.Tint(section));
        public static Color Rule(string? section) => C(P.Rule(section));
        public static Color Outline(string? section) => C(P.Outline(section));

        public static readonly Color DarkInk = C(P.DarkInk);
        public const double RestFillAlpha = P.RestFillAlpha, PlateLift = P.PlateLift;
        public const double RestOutlineAlpha = 0.95, BevelLight = 0.70, BevelShade = 0.75, OutlineFootOffset = 0.85;
        public const double RestFaceThickness = 1.5, ActiveFaceThickness = 2.0, HoverFillAlpha = 0.38;
        public const double TrackBorderAlpha = 0.40, TrackInsetAlpha = 0.70;
        public const double ActiveGlowBlur = 16, ActiveGlowOpacity = 0.75;
        public const double ActiveRingTopAlpha = 0.80, ActiveRingFootAlpha = 0.30;
        public const double PillHeight = 38, PillFontSize = 14.5, PillPadding = 16;
        public const double BadgeMaxWidth = 50, BadgePlateWidth = 52, BadgePlateHeight = 24, BadgeGap = 8, BadgePadExtra = 4;

        public static Color WithAlpha(Color c, double alpha) => C(P.WithAlpha(U(c), alpha));
        public static double Luminance(Color c) => P.Luminance(U(c));
        public static double Contrast(Color a, Color b) => P.Contrast(U(a), U(b));
        public static Color ActiveTextOn(Color fill) => C(P.ActiveTextOn(U(fill)));
        public static Color Mix(Color a, Color b, double t) => C(P.Mix(U(a), U(b), t));
        public static Color TrackFill(Color hue) => C(P.TrackFill(U(hue)));
        public static Color TabTint(string? section, int index, int count) => C(P.TabTint(section, index, count));
        public static Color RestTextOn(Color hue, Color tint) => C(P.RestTextOn(U(hue), U(tint)));
        public static Color ActiveRingColor(Color tint, double alpha) => C(P.ActiveRingColor(U(tint), alpha));
        public static byte EdgeGlowAlpha(Color hue) => P.EdgeGlowAlpha(U(hue));

        // ---- brushes (WPF :437-:464) ----
        private static LinearGradientBrush Vertical(params (Color C, double At)[] stops)
        {
            var b = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            };
            foreach (var (c, at) in stops) b.GradientStops.Add(new GradientStop(c, at));
            return b;
        }

        public static LinearGradientBrush PlateBrush(Color tint, double alpha) =>
            Vertical((WithAlpha(tint, alpha + PlateLift), 0), (WithAlpha(tint, alpha - PlateLift), 1));

        public static LinearGradientBrush OutlineBrush(Color tint) =>
            Vertical((WithAlpha(Mix(tint, Colors.White, BevelLight), RestOutlineAlpha), 0),
                     (WithAlpha(tint, RestOutlineAlpha), 0.35),
                     (WithAlpha(tint, RestOutlineAlpha), 0.65),
                     (WithAlpha(Mix(tint, DarkInk, BevelShade), RestOutlineAlpha), OutlineFootOffset));

        public static LinearGradientBrush ActiveRingBrush(Color tint) =>
            Vertical((ActiveRingColor(tint, ActiveRingTopAlpha), 0), (ActiveRingColor(tint, ActiveRingFootAlpha), 1));

        public static LinearGradientBrush TrackBorderBrush(Color hue) =>
            Vertical((WithAlpha(DarkInk, TrackInsetAlpha), 0), (WithAlpha(hue, TrackBorderAlpha), 0.45),
                     (WithAlpha(hue, TrackBorderAlpha), 1));
    }
}
