// PORTED from ConditioningControlPanel/Controls/NavRail/SectionTabStrip.xaml.cs (NavStripRules
// :69-:465, nav polish waves 2/3/7/9: 495b7f429, 5dfaf3949, d38c3ae3d, e5d5afb61, 6314122c3): the
// strip's paint - the one hue table (Core NavStripTable.AccentRgb), the section ink family, the
// per-tab tints, the contrast lifts and the plate/outline/ring/tray brushes. Same numbers, Avalonia
// Color; the WPF copy works on System.Windows.Media.Color so it cannot move to Core unchanged.

using System;
using Avalonia;
using Avalonia.Media;
using ConditioningControlPanel.Services.UI;

namespace ConditioningControlPanel.Avalonia.Views.Controls
{
    internal static class NavStripRules
    {
        public static Color Accent(string? section) => FromRgb(NavStripTable.AccentRgb(section));
        private static Color FromRgb(uint rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

        // ---- section ink (WPF :84-:111, 4f22d0478) ----
        public static readonly Color TextLight = Color.FromRgb(0xF0, 0xF0, 0xF5);
        public const double InkLift = 0.35;
        public const double TintPull = 0.15;
        public const byte RuleAlpha = 0x59, OutlineAlpha = 0x40;
        public static Color Ink(string? section) => Mix(Accent(section), TextLight, InkLift);
        public static Color Tint(string? section) => Mix(TextLight, Accent(section), TintPull);
        public static Color Rule(string? section) => WithAlphaByte(Accent(section), RuleAlpha);
        public static Color Outline(string? section) => WithAlphaByte(Accent(section), OutlineAlpha);
        private static Color WithAlphaByte(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

        // ---- pill paint (WPF :131-:200) ----
        public static readonly Color DarkInk = Color.FromRgb(0x15, 0x12, 0x1F);
        public const double RestTextAlpha = 0.95;
        public const double RestFillAlpha = 0.24;
        public const double PlateLift = 0.10;
        public const double RestOutlineAlpha = 0.95;
        public const double BevelLight = 0.70;
        public const double BevelShade = 0.75;
        public const double OutlineFootOffset = 0.85;
        public const double RestFaceThickness = 1.5;
        public const double ActiveFaceThickness = 2.0;
        public const double HoverFillAlpha = 0.38;
        public const double TrackInkAlpha = 0.40;
        public const double TrackFillAlpha = 0.10;
        public const double TrackBorderAlpha = 0.40;
        public const double TrackInsetAlpha = 0.70;
        public const double ActiveGlowBlur = 16;
        public const double ActiveGlowOpacity = 0.75;
        public const double ActiveRingTopAlpha = 0.80;
        public const double ActiveRingFootAlpha = 0.30;
        public const double ActiveRingWhite = 0.75;
        public const double PillHeight = 38;
        public const double PillFontSize = 14.5;
        public const double PillPadding = 16;
        public const double BadgeMaxWidth = 50;
        public const double BadgePlateWidth = 52;
        public const double BadgePlateHeight = 24;
        public const double BadgeGap = 8;
        public const double BadgePadExtra = 4;
        public const double TabHueStep = 9;
        public static readonly Color PageGround = Color.FromRgb(0x1A, 0x12, 0x30);

        public static Color WithAlpha(Color c, double alpha) =>
            Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), c.R, c.G, c.B);

        public static double Luminance(Color c)
        {
            static double Lin(byte v)
            {
                double s = v / 255.0;
                return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
        }

        public static double Contrast(Color a, Color b)
        {
            double la = Luminance(a), lb = Luminance(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }

        /// <summary>The active pill's text on its solid hue: dark ink or white, whichever reads better.</summary>
        public static Color ActiveTextOn(Color fill) =>
            Contrast(DarkInk, fill) >= Contrast(Colors.White, fill) ? DarkInk : Colors.White;

        public static Color Over(Color top, Color under)
        {
            double a = top.A / 255.0;
            byte M(byte t, byte u) => (byte)Math.Round(t * a + u * (1 - a));
            return Color.FromRgb(M(top.R, under.R), M(top.G, under.G), M(top.B, under.B));
        }

        public static Color Composite(Color top, Color under)
        {
            double at = top.A / 255.0, au = under.A / 255.0;
            double a = at + au * (1 - at);
            if (a <= 0) return Color.FromArgb(0, 0, 0, 0);
            byte M(byte t, byte u) => (byte)Math.Round((t * at + u * au * (1 - at)) / a);
            return Color.FromArgb((byte)Math.Round(a * 255), M(top.R, under.R), M(top.G, under.G), M(top.B, under.B));
        }

        public static Color Mix(Color a, Color b, double t)
        {
            t = Math.Clamp(t, 0, 1);
            byte M(byte x, byte y) => (byte)Math.Round(x + (y - x) * t);
            return Color.FromRgb(M(a.R, b.R), M(a.G, b.G), M(a.B, b.B));
        }

        /// <summary>The tray's fill: the hue at 10% over deep ink at 40%.</summary>
        public static Color TrackFill(Color hue) =>
            Composite(WithAlpha(hue, TrackFillAlpha), WithAlpha(DarkInk, TrackInkAlpha));

        // ---- HSL (WPF :325-:376) ----
        public static (double H, double S, double L) ToHsl(Color c)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            double l = (max + min) / 2, d = max - min;
            if (d < 1e-9) return (0, 0, l);
            double sat = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            double h = max == r ? (g - b) / d + (g < b ? 6 : 0)
                     : max == g ? (b - r) / d + 2
                     : (r - g) / d + 4;
            return (h * 60, sat, l);
        }

        public static Color FromHsl(double h, double sat, double l)
        {
            h = ((h % 360) + 360) % 360 / 360.0;
            if (sat <= 0) { var v = (byte)Math.Round(l * 255); return Color.FromRgb(v, v, v); }
            double q = l < 0.5 ? l * (1 + sat) : l + sat - l * sat, p = 2 * l - q;
            static double Ch(double p, double q, double t)
            {
                if (t < 0) t += 1;
                if (t > 1) t -= 1;
                if (t < 1 / 6.0) return p + (q - p) * 6 * t;
                if (t < 0.5) return q;
                if (t < 2 / 3.0) return p + (q - p) * (2 / 3.0 - t) * 6;
                return p;
            }
            byte B(double x) => (byte)Math.Round(Math.Clamp(x, 0, 1) * 255);
            return Color.FromRgb(B(Ch(p, q, h + 1 / 3.0)), B(Ch(p, q, h)), B(Ch(p, q, h - 1 / 3.0)));
        }

        public static Color RotateHue(Color c, double degrees)
        {
            var (h, sat, l) = ToHsl(c);
            return FromHsl(h + degrees, sat, l);
        }

        /// <summary>A tab's own near-hue: the section hue turned by (index - middle) x 9 degrees. WPF :378.</summary>
        public static Color TabTint(string? section, int index, int count)
        {
            var hue = Accent(section);
            if (count <= 1) return hue;
            return RotateHue(hue, (index - (count - 1) / 2.0) * TabHueStep);
        }

        /// <summary>The worst ground a rest pill's text sits on: page, wash (14%), tray, plate's lit top. WPF :391.</summary>
        public static Color RestGround(Color hue, Color tint) =>
            Over(WithAlpha(tint, RestFillAlpha + PlateLift),
                 Over(TrackFill(hue), Over(WithAlpha(hue, 0.14), PageGround)));

        public static Color RestTextOn(Color hue, Color tint) => Lift(hue, RestGround(hue, tint));
        public static Color RestGlyphOn(Color hue, Color tint) => Lift(tint, RestGround(hue, tint));

        public static Color ActiveRingColor(Color tint, double alpha) => WithAlpha(Mix(tint, Colors.White, ActiveRingWhite), alpha);

        private static Color Lift(Color start, Color ground)
        {
            var c = start;
            for (int i = 0; i < 20 && Contrast(Over(WithAlpha(c, RestTextAlpha), ground), ground) < 4.5; i++)
                c = Color.FromRgb((byte)Math.Round(c.R + (255 - c.R) * 0.08),
                                  (byte)Math.Round(c.G + (255 - c.G) * 0.08),
                                  (byte)Math.Round(c.B + (255 - c.B) * 0.08));
            return WithAlpha(c, RestTextAlpha);
        }

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
