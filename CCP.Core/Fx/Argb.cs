using System;
using System.Globalization;

namespace ConditioningControlPanel.Fx
{
    /// <summary>
    /// Colour as a neutral ARGB <see cref="uint"/> (0xAARRGGBB), so the parity rules (DepthRules,
    /// NavStripRules, SectionEdgeRules, PremiumSparkRules) live in Core with no WPF or Avalonia
    /// Color type. A head converts once at the paint call (WPF Color.FromArgb, Avalonia
    /// Color.FromUInt32). Byte maths and rounding match the WPF 7.1.5 originals exactly.
    /// </summary>
    public static class Argb
    {
        public const uint White = 0xFFFFFFFF;
        public const uint Black = 0xFF000000;
        /// <summary>WPF Colors.Transparent is white at zero alpha (#00FFFFFF).</summary>
        public const uint Transparent = 0x00FFFFFF;

        public static uint FromArgb(byte a, byte r, byte g, byte b) =>
            ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | b;

        public static uint FromRgb(byte r, byte g, byte b) => FromArgb(0xFF, r, g, b);

        public static byte A(uint c) => (byte)(c >> 24);
        public static byte R(uint c) => (byte)(c >> 16);
        public static byte G(uint c) => (byte)(c >> 8);
        public static byte B(uint c) => (byte)c;

        /// <summary>The colour with its alpha replaced by a byte.</summary>
        public static uint WithAlpha(uint c, byte a) => (c & 0x00FFFFFF) | ((uint)a << 24);

        /// <summary>The colour at an alpha 0..1 (clamped, rounded to the nearest byte).</summary>
        public static uint WithAlpha(uint c, double alpha) =>
            WithAlpha(c, (byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255));

        /// <summary>The colour with its alpha forced to 0xFF.</summary>
        public static uint Opaque(uint c) => c | 0xFF000000;

        /// <summary>"#AARRGGBB", for test messages and logs.</summary>
        public static string ToHex(uint c) => "#" + c.ToString("X8", CultureInfo.InvariantCulture);

        /// <summary>Parses "#RRGGBB" or "#AARRGGBB" (with or without '#').</summary>
        public static uint Parse(string hex)
        {
            var h = hex.TrimStart('#');
            if (h.Length == 6) h = "FF" + h;
            if (h.Length != 8) throw new FormatException("not a colour: " + hex);
            return uint.Parse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }
    }
}
