using System;
using System.Windows.Media;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Small colour maths for "the mod picked the accent, now make the text on it readable".
    ///
    /// <para>A mod's <c>theme.accentColor</c> is author-supplied and can be anything: Infection
    /// Control's is <c>#2855F0</c>, a deep blue that scored about 1.4:1 as speech text on the
    /// tube's old fixed purple bubble - beebee reported it as simply unreadable. Nothing here
    /// second-guesses the author's hue; it only decides which text colour to put ON it.</para>
    ///
    /// <para>Everything is WCAG relative luminance, so the answers match what a contrast checker
    /// says rather than a hand-tuned guess per mod.</para>
    /// </summary>
    internal static class AccentContrast
    {
        /// <summary>WCAG AA for large/bold text is 3.0; the tube's bubble text is 20px bold, but
        /// aim at the stricter 4.5 so smaller labels sharing this helper are covered too.</summary>
        internal const double TargetRatio = 4.5;

        private static double Channel(byte c)
        {
            var v = c / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        internal static double Luminance(Color c) =>
            0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);

        /// <summary>WCAG contrast ratio, 1.0 (identical) to 21.0 (black on white).</summary>
        internal static double Ratio(Color a, Color b)
        {
            var la = Luminance(a);
            var lb = Luminance(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }

        /// <summary>Black on light backgrounds, white on dark ones - whichever actually wins.</summary>
        internal static Color BestForeground(Color background) =>
            Ratio(Colors.Black, background) >= Ratio(Colors.White, background) ? Colors.Black : Colors.White;

        /// <summary>Linear mix, <paramref name="t"/> = 0 keeps <paramref name="a"/>. Alpha from a.</summary>
        internal static Color Blend(Color a, Color b, double t)
        {
            t = Math.Clamp(t, 0, 1);
            static byte Mix(byte x, byte y, double f) => (byte)Math.Round(x + (y - x) * f);
            return Color.FromArgb(a.A, Mix(a.R, b.R, t), Mix(a.G, b.G, t), Mix(a.B, b.B, t));
        }

        /// <summary>
        /// Text colour that keeps as much of <paramref name="accent"/> as it can while still
        /// clearing <see cref="TargetRatio"/> against <paramref name="background"/>. Walks the
        /// accent toward black or white (whichever contrasts better) in tenths and stops at the
        /// first step that passes, so a readable mod keeps its own colour untouched and an
        /// unreadable one lands tinted rather than flat white.
        /// </summary>
        internal static Color ReadableOn(Color accent, Color background)
        {
            var pole = BestForeground(background);
            for (var t = 0.0; t <= 1.0001; t += 0.1)
            {
                var candidate = Blend(accent, pole, t);
                if (Ratio(candidate, background) >= TargetRatio) return candidate;
            }
            return pole;
        }

        /// <summary>Parses #RGB/#RRGGBB/#AARRGGBB, falling back to <paramref name="fallback"/>.</summary>
        internal static Color Parse(string? hex, Color fallback)
        {
            if (string.IsNullOrWhiteSpace(hex)) return fallback;
            try { return (Color)ColorConverter.ConvertFromString(hex); }
            catch { return fallback; }
        }
    }
}
