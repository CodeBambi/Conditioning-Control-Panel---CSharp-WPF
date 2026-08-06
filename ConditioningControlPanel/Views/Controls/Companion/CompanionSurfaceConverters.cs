using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    // =====================================================================================
    //  Converters used only by the two "living surfaces" (Z2 chat, Z3 diary). They are
    //  instanced inside those controls' own resource dictionaries rather than the shared
    //  theme, per the package rule: a control adds what only it needs to its own scope, and
    //  the assembler promotes anything that turns out to be shared later.
    // =====================================================================================

    /// <summary>
    /// Fact kind → its accent colour, for the wall's kind-coloured card edge and kind tag.
    ///
    /// <para>The design's readability trick: a card's kind is legible before a word of it is read.
    /// <c>boundary</c> keeps the steel-blue of the consent rail (the theme paints that one as a
    /// real border, so the rail Rectangle stays hidden on those cards) and every other kind gets
    /// its own hue from the house palette.</para>
    ///
    /// <para>Unknown kinds fall back to violet — a new memory kind shipping from the Brain must
    /// render as a normal card, never as a blank or a crash.</para>
    /// </summary>
    public sealed class CompanionFactKindBrushConverter : IValueConverter
    {
        /// <summary>Rail mode: the same hue at reduced alpha, so the edge whispers.</summary>
        public bool Soft { get; set; }

        private static readonly Dictionary<string, Color> Palette =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["boundary"] = Color.FromRgb(0x7F, 0xB2, 0xD9),   // steel — consent hygiene
                ["joke"] = Color.FromRgb(0xFF, 0x69, 0xB4),       // pink
                ["preference"] = Color.FromRgb(0xB4, 0x78, 0xFF), // purple
                ["goal"] = Color.FromRgb(0xFF, 0xD7, 0x00),       // gold
                ["moment"] = Color.FromRgb(0x93, 0x70, 0xDB),     // violet
                ["identity"] = Color.FromRgb(0x6E, 0xE7, 0xA7)    // live-green
            };

        private static readonly Color Fallback = Color.FromRgb(0xB4, 0x78, 0xFF);

        /// <summary>Cached brushes: one per kind per mode, frozen — item containers recycle.</summary>
        private static readonly Dictionary<string, SolidColorBrush> Cache =
            new(StringComparer.OrdinalIgnoreCase);

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            string kind = value as string ?? string.Empty;
            return BrushFor(kind, Soft);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => Binding.DoNothing;

        /// <summary>The accent brush for a kind key. Never null, never unfrozen.</summary>
        internal static SolidColorBrush BrushFor(string kindKey, bool soft)
        {
            string cacheKey = (soft ? "s:" : "f:") + kindKey;
            lock (Cache)
            {
                if (Cache.TryGetValue(cacheKey, out var cached)) return cached;

                Color c = Palette.TryGetValue(kindKey ?? string.Empty, out var hue) ? hue : Fallback;
                var brush = new SolidColorBrush(soft ? Color.FromArgb(0x99, c.R, c.G, c.B) : c);
                brush.Freeze();
                Cache[cacheKey] = brush;
                return brush;
            }
        }
    }
}
