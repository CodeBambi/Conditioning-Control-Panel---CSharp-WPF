using System;
using System.Globalization;
using System.Windows.Media;

namespace ConditioningControlPanel.Controls.HelpLoops
{
    /// <summary>
    /// Colours of the help loops. Accent follows the active mod theme (<c>PinkBrush</c> on the host);
    /// everything else is fixed so the fake desktop reads the same under every mod.
    /// </summary>
    public sealed class LoopPalette
    {
        public static readonly Color DefaultAccent = Css("#ff6fb5");

        public LoopPalette(Brush? accent = null)
        {
            var a = accent is SolidColorBrush s ? s.Color : DefaultAccent;
            AccentColor = a;
            Accent = Freeze(new SolidColorBrush(a));
        }

        public Color AccentColor { get; }

        public Brush Ground { get; } = Solid("#110d1c");
        public Brush Panel { get; } = Solid("#1b1530");
        public Brush Panel2 { get; } = Solid("#241c3d");
        public Brush Border { get; } = Solid("#342a55");
        public Brush Window { get; } = Solid("#221a38");
        public Brush WindowBorder { get; } = Solid("#3b3060");
        public Brush WindowBar { get; } = Solid("#2d2349");
        public Brush LineA { get; } = Solid("#4a3d73");
        public Brush LineB { get; } = Solid("#5b4b8c");
        public Brush Track { get; } = Solid("#3b3060");
        public Brush Glass { get; } = Solid("#0d0a18d9");
        public Brush Text { get; } = Solid("#f3ecff");
        public Brush Dim { get; } = Solid("#a497c4");
        public Brush Ink { get; } = Solid("#1a1030");
        public Brush Accent { get; }
        public Brush Lilac { get; } = Solid("#b99cff");
        public Brush Mint { get; } = Solid("#5fffd0");
        public Brush Gold { get; } = Solid("#ffcf6b");
        public Brush Red { get; } = Solid("#ff5a6e");
        public Brush White { get; } = Solid("#ffffff");

        /// <summary>A frozen solid brush from a CSS colour (#rgb, #rrggbb or #rrggbbaa).</summary>
        public static Brush Solid(string css) => Freeze(new SolidColorBrush(Css(css)));

        /// <summary>A frozen solid brush with the colour's alpha multiplied by <paramref name="alpha"/>.</summary>
        public static Brush Solid(Color c, double alpha) =>
            Freeze(new SolidColorBrush(Color.FromArgb((byte)Math.Round(c.A * LoopMath.Clamp(alpha)), c.R, c.G, c.B)));

        /// <summary>
        /// Parses a CSS hex colour. CSS puts alpha LAST (#rrggbbaa), WPF puts it first, so the
        /// mockup's values must come through here, never straight into ColorConverter.
        /// </summary>
        public static Color Css(string css)
        {
            var h = css.TrimStart('#');
            if (h.Length == 3 || h.Length == 4)
            {
                var e = "";
                foreach (var ch in h) e += new string(ch, 2);
                h = e;
            }
            byte P(int i) => byte.Parse(h.Substring(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return h.Length == 8
                ? Color.FromArgb(P(6), P(0), P(2), P(4))
                : Color.FromRgb(P(0), P(2), P(4));
        }

        public static T Freeze<T>(T f) where T : System.Windows.Freezable
        {
            if (f.CanFreeze) f.Freeze();
            return f;
        }
    }
}
