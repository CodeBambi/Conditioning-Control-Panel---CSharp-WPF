using System;
using System.Globalization;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace ConditioningControlPanel.Avalonia.Controls.HelpLoops
{
    /// <summary>The mockup's colours (WPF LoopPalette.cs). Immutable brushes stand in for frozen ones;
    /// colours are written CSS-style (#rrggbbaa), as in the mockup and the WPF scenes.</summary>
    public sealed class LoopPalette
    {
        public static readonly Color DefaultAccent = Css("#ff6fb5");

        public LoopPalette(IBrush? accent = null)
        {
            AccentColor = accent is ISolidColorBrush s ? s.Color : DefaultAccent;
            Accent = new ImmutableSolidColorBrush(AccentColor);
        }

        public Color AccentColor { get; }

        public IBrush Window { get; } = Solid("#221a38");
        public IBrush WindowBorder { get; } = Solid("#3b3060");
        public IBrush WindowBar { get; } = Solid("#2d2349");
        public IBrush LineA { get; } = Solid("#4a3d73");
        public IBrush LineB { get; } = Solid("#5b4b8c");
        public IBrush Track { get; } = Solid("#3b3060");
        public IBrush Glass { get; } = Solid("#0d0a18d9");
        public IBrush Border { get; } = Solid("#342a55");
        public IBrush Text { get; } = Solid("#f3ecff");
        public IBrush Dim { get; } = Solid("#a497c4");
        public IBrush Accent { get; }
        public IBrush Mint { get; } = Solid("#5fffd0");
        public IBrush White { get; } = Solid("#ffffff");

        public static IBrush Solid(string css) => new ImmutableSolidColorBrush(Css(css));

        public static Color Css(string css)
        {
            var h = css.TrimStart('#');
            if (h.Length is 3 or 4)
            {
                var e = "";
                foreach (var ch in h) e += new string(ch, 2);
                h = e;
            }
            byte P(int i) => byte.Parse(h.Substring(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return h.Length == 8 ? Color.FromArgb(P(6), P(0), P(2), P(4)) : Color.FromRgb(P(0), P(2), P(4));
        }
    }
}
