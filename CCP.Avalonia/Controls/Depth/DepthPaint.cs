using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using ConditioningControlPanel.Depth;

namespace ConditioningControlPanel.Avalonia.Controls.Depth
{
    /// <summary>
    /// The head's paint for Core <see cref="DepthPalette"/> / <see cref="DepthRules"/>: a depth brush
    /// as an Avalonia brush, and the section-hue shadow (ink pulled 28% toward the hue) that a
    /// painter uses in place of the neutral DepthDropBand / DepthFloatBand when it knows its section.
    /// Theme/Depth.axaml carries the same 28 keys as resources; code that needs one per hue builds
    /// it here so nobody hand-picks a shade. Brushes are immutable (shareable, no change tracking).
    /// </summary>
    public static class DepthPaint
    {
        public static Color ToColor(uint argb) =>
            Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

        /// <summary>One <see cref="DepthPalette"/> entry as a brush (a Solid stays a solid brush).</summary>
        public static IImmutableBrush ToBrush(DepthBrush b)
        {
            var stops = b.Stops.Select(s => new ImmutableGradientStop(s.Offset, ToColor(s.Color))).ToArray();
            static RelativePoint P(ConditioningControlPanel.Fx.Vec2 v) => new(v.X, v.Y, RelativeUnit.Relative);
            return b.Kind switch
            {
                DepthBrushKind.Linear => new ImmutableLinearGradientBrush(stops, 1, null, null,
                    GradientSpreadMethod.Pad, P(b.Start), P(b.End)),
                DepthBrushKind.Radial => new ImmutableRadialGradientBrush(stops, 1, null, null,
                    GradientSpreadMethod.Pad, P(b.Start), P(b.End),
                    new RelativeScalar(b.RadiusX, RelativeUnit.Relative), new RelativeScalar(b.RadiusY, RelativeUnit.Relative)),
                _ => new ImmutableSolidColorBrush(ToColor(b.Color)),
            };
        }

        /// <summary>A Depth.axaml key as a brush, or null when the palette has no such key.</summary>
        public static IImmutableBrush? Brush(string key) => DepthPalette.Find(key) is { } b ? ToBrush(b) : null;

        /// <summary>A drop band (top to bottom, shadow to clear) in the section's shadow ink at
        /// <paramref name="alpha"/> (ShadowAlpha for a raised drop, FloatAlpha for a card).</summary>
        public static IImmutableBrush ShadowBand(uint sectionHue, double alpha = DepthRules.ShadowAlpha)
        {
            var ink = ToColor(DepthRules.ShadowColor(sectionHue, alpha));
            return new ImmutableLinearGradientBrush(new[]
            {
                new ImmutableGradientStop(0, ink),
                new ImmutableGradientStop(1, Color.FromArgb(0, ink.R, ink.G, ink.B)),
            }, 1, null, null, GradientSpreadMethod.Pad,
                new RelativePoint(0, 0, RelativeUnit.Relative), new RelativePoint(0, 1, RelativeUnit.Relative));
        }
    }
}
