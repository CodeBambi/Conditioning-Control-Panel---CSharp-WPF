using System;

namespace ConditioningControlPanel.Services.UI
{
    /// <summary>
    /// The section strip's colour math (nav polish waves 2/3/7/9), shared by both heads: the one
    /// hue table (<see cref="NavStripTable.AccentRgb"/>), the section ink family, per-tab tints,
    /// the WCAG contrast lifts and the section edge's glow alpha. Colours are 0xAARRGGBB; each
    /// head wraps these in its own Color type (WPF NavStripRules / SectionEdgeRules, Avalonia
    /// NavStripRules). Moved from WPF SectionTabStrip.xaml.cs:69-465 with the numbers unchanged.
    /// </summary>
    public static class NavStripPaint
    {
        public const uint TextLight = 0xFFF0F0F5, DarkInk = 0xFF15121F, PageGround = 0xFF1A1230, White = 0xFFFFFFFF;
        public const double InkLift = 0.35, TintPull = 0.15;
        public const byte RuleAlpha = 0x59, OutlineAlpha = 0x40;
        public const double RestTextAlpha = 0.95, RestFillAlpha = 0.24, PlateLift = 0.10;
        public const double TrackInkAlpha = 0.40, TrackFillAlpha = 0.10;
        public const double ActiveGlyphTint = 0.30, ActiveRingWhite = 0.75;
        public const double TabHueStep = 9;

        public static byte A(uint c) => (byte)(c >> 24);
        public static byte R(uint c) => (byte)(c >> 16);
        public static byte G(uint c) => (byte)(c >> 8);
        public static byte B(uint c) => (byte)c;
        public static uint Argb(byte a, byte r, byte g, byte b) => (uint)a << 24 | (uint)r << 16 | (uint)g << 8 | b;
        public static uint Rgb(byte r, byte g, byte b) => Argb(0xFF, r, g, b);

        /// <summary>The section's opaque hue.</summary>
        public static uint Accent(string? section) => 0xFF000000 | NavStripTable.AccentRgb(section);

        public static uint WithAlpha(uint c, double alpha) =>
            Argb((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), R(c), G(c), B(c));
        public static uint WithAlphaByte(uint c, byte a) => Argb(a, R(c), G(c), B(c));

        // ---- section ink (WPF :84-:111) ----
        public static uint Ink(string? section) => Mix(Accent(section), TextLight, InkLift);
        public static uint Tint(string? section) => Mix(TextLight, Accent(section), TintPull);
        public static uint Rule(string? section) => WithAlphaByte(Accent(section), RuleAlpha);
        public static uint Outline(string? section) => WithAlphaByte(Accent(section), OutlineAlpha);

        /// <summary>WCAG relative luminance of an opaque colour.</summary>
        public static double Luminance(uint c)
        {
            static double Lin(byte v)
            {
                double s = v / 255.0;
                return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Lin(R(c)) + 0.7152 * Lin(G(c)) + 0.0722 * Lin(B(c));
        }

        /// <summary>WCAG contrast ratio between two opaque colours (1..21).</summary>
        public static double Contrast(uint a, uint b)
        {
            double la = Luminance(a), lb = Luminance(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }

        /// <summary>Dark ink or white on a solid fill, whichever reads better.</summary>
        public static uint ActiveTextOn(uint fill) => Contrast(DarkInk, fill) >= Contrast(White, fill) ? DarkInk : White;

        /// <summary><paramref name="top"/> (with its alpha) over an opaque <paramref name="under"/>.</summary>
        public static uint Over(uint top, uint under)
        {
            double a = A(top) / 255.0;
            byte M(byte t, byte u) => (byte)Math.Round(t * a + u * (1 - a));
            return Rgb(M(R(top), R(under)), M(G(top), G(under)), M(B(top), B(under)));
        }

        /// <summary>Porter-Duff "over" for two colours that may both be translucent.</summary>
        public static uint Composite(uint top, uint under)
        {
            double at = A(top) / 255.0, au = A(under) / 255.0;
            double a = at + au * (1 - at);
            if (a <= 0) return 0;
            byte M(byte t, byte u) => (byte)Math.Round((t * at + u * au * (1 - at)) / a);
            return Argb((byte)Math.Round(a * 255), M(R(top), R(under)), M(G(top), G(under)), M(B(top), B(under)));
        }

        /// <summary>A straight mix of two colours (t = 0 gives a, 1 gives b), opaque.</summary>
        public static uint Mix(uint a, uint b, double t)
        {
            t = Math.Clamp(t, 0, 1);
            byte M(byte x, byte y) => (byte)Math.Round(x + (y - x) * t);
            return Rgb(M(R(a), R(b)), M(G(a), G(b)), M(B(a), B(b)));
        }

        /// <summary>The tray's fill: the hue at 10% over deep ink at 40%.</summary>
        public static uint TrackFill(uint hue) => Composite(WithAlpha(hue, TrackFillAlpha), WithAlpha(DarkInk, TrackInkAlpha));

        // ---- HSL ----
        public static (double H, double S, double L) ToHsl(uint c)
        {
            double r = R(c) / 255.0, g = G(c) / 255.0, b = B(c) / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            double l = (max + min) / 2, d = max - min;
            if (d < 1e-9) return (0, 0, l);
            double sat = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            double h = max == r ? (g - b) / d + (g < b ? 6 : 0)
                     : max == g ? (b - r) / d + 2
                     : (r - g) / d + 4;
            return (h * 60, sat, l);
        }

        public static uint FromHsl(double h, double sat, double l)
        {
            h = ((h % 360) + 360) % 360 / 360.0;
            if (sat <= 0) { var v = (byte)Math.Round(l * 255); return Rgb(v, v, v); }
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
            byte By(double x) => (byte)Math.Round(Math.Clamp(x, 0, 1) * 255);
            return Rgb(By(Ch(p, q, h + 1 / 3.0)), By(Ch(p, q, h)), By(Ch(p, q, h - 1 / 3.0)));
        }

        public static uint RotateHue(uint c, double degrees)
        {
            var (h, sat, l) = ToHsl(c);
            return FromHsl(h + degrees, sat, l);
        }

        /// <summary>A tab's own near-hue: the section hue turned by (index - middle) x 9 degrees.</summary>
        public static uint TabTint(string? section, int index, int count)
        {
            var hue = Accent(section);
            if (count <= 1) return hue;
            return RotateHue(hue, (index - (count - 1) / 2.0) * TabHueStep);
        }

        /// <summary>The worst ground a rest pill's text sits on: page, wash (14%), tray, the plate's lit top.</summary>
        public static uint RestGround(uint hue, uint tint) =>
            Over(WithAlpha(tint, RestFillAlpha + PlateLift), Over(TrackFill(hue), Over(WithAlpha(hue, 0.14), PageGround)));

        public static uint RestTextOn(uint hue, uint tint) => Lift(hue, RestGround(hue, tint));
        public static uint RestGlyphOn(uint hue, uint tint) => Lift(tint, RestGround(hue, tint));

        /// <summary>The active glyph: the label's ink mixed toward the tint, never under 3:1 on the hue.</summary>
        public static uint ActiveGlyphOn(uint hue, uint tint)
        {
            var ink = ActiveTextOn(hue);
            for (double t = ActiveGlyphTint; t > 0; t -= 0.05)
            {
                var c = Mix(ink, tint, t);
                if (Contrast(c, hue) >= 3.0) return c;
            }
            return ink;
        }

        public static uint ActiveRingColor(uint tint, double alpha) => WithAlpha(Mix(tint, White, ActiveRingWhite), alpha);

        /// <summary>Lifts <paramref name="start"/> toward white in 8% steps until it reads at 4.5:1 on the ground.</summary>
        public static uint Lift(uint start, uint ground)
        {
            var c = start;
            for (int i = 0; i < 20 && Contrast(Over(WithAlpha(c, RestTextAlpha), ground), ground) < 4.5; i++)
                c = Rgb((byte)Math.Round(R(c) + (255 - R(c)) * 0.08),
                        (byte)Math.Round(G(c) + (255 - G(c)) * 0.08),
                        (byte)Math.Round(B(c) + (255 - B(c)) * 0.08));
            return WithAlpha(c, RestTextAlpha);
        }

        /// <summary>The section edge's glow alpha (WPF SectionEdgeRules.GlowAlpha): 0.20 x
        /// sqrt(0.40 / luminance), clamped 0.14..0.26, as a byte.</summary>
        public static byte EdgeGlowAlpha(uint hue)
        {
            double lum = Math.Max(Luminance(hue), 0.0001);
            return (byte)Math.Round(Math.Clamp(0.20 * Math.Sqrt(0.40 / lum), 0.14, 0.26) * 255);
        }
    }
}
