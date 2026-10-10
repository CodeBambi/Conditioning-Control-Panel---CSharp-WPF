using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Fx;

namespace ConditioningControlPanel.Depth
{
    /// <summary>What kind of paint a depth brush is.</summary>
    public enum DepthBrushKind { Solid, Linear, Radial }

    /// <summary>
    /// One depth brush as data. Linear: <see cref="Start"/> to <see cref="End"/> in relative units.
    /// Radial: <see cref="Start"/> is the Center, <see cref="End"/> the GradientOrigin, plus the radii.
    /// Solid: one stop at offset 0. Colours are ARGB <see cref="uint"/>.
    /// </summary>
    public sealed record DepthBrush(string Key, DepthBrushKind Kind, Vec2 Start, Vec2 End,
        double RadiusX, double RadiusY, IReadOnlyList<(uint Color, double Offset)> Stops)
    {
        /// <summary>The solid colour (the first stop).</summary>
        public uint Color => Stops[0].Color;
    }

    /// <summary>
    /// WPF 7.1.5 Resources/Theme/Depth.xaml as data, every key and stop verbatim (generated from the
    /// XAML), so the Avalonia head builds the same brushes (a Depth.axaml or code) from one table.
    /// Shadows here are the NEUTRAL ink; a painter that knows its section hue uses
    /// <see cref="DepthRules.ShadowColor(uint)"/> instead. No Effect anywhere: bevels are 1 px
    /// gradient borders, shadows are gradient rectangles / ellipses, wells are inner bands.
    /// </summary>
    public static class DepthPalette
    {
        private static DepthBrush Solid(string key, uint c) =>
            new(key, DepthBrushKind.Solid, new Vec2(0, 0), new Vec2(0, 1), 0, 0, new[] { (c, 0.0) });

        private static DepthBrush Linear(string key, Vec2 start, Vec2 end, params (uint Color, double Offset)[] stops) =>
            new(key, DepthBrushKind.Linear, start, end, 0, 0, stops);

        private static DepthBrush Radial(string key, Vec2 center, Vec2 origin, double rx, double ry, params (uint Color, double Offset)[] stops) =>
            new(key, DepthBrushKind.Radial, center, origin, rx, ry, stops);

        /// <summary>Every brush and colour of Depth.xaml, in file order.</summary>
        public static readonly IReadOnlyList<DepthBrush> All = new[]
        {
            Solid("DepthHighlight", 0x42FFFFFF),
            Solid("DepthShade", 0x80000000),
            Solid("DepthHighlightBrush", 0x42FFFFFF),
            Solid("DepthShadeBrush", 0x80000000),
            Linear("DepthRaisedBevel", new Vec2(0, 0), new Vec2(0, 1), (0x42FFFFFF, 0.0), (0x10FFFFFF, 0.5), (0x80000000, 1.0)),
            Linear("DepthRaisedSheen", new Vec2(0, 0), new Vec2(0, 1), (0x17FFFFFF, 0.00), (0x00FFFFFF, 0.55)),
            Linear("DepthPlankSheen", new Vec2(0, 0), new Vec2(0, 1), (0x47FFFFFF, 0.00), (0x00FFFFFF, 0.50), (0x2E000000, 1.00)),
            Linear("DepthPlankBevel", new Vec2(0, 0), new Vec2(0, 1), (0x80FFFFFF, 0.0), (0x14FFFFFF, 0.5), (0xA6000000, 1.0)),
            Linear("DepthPressedBevel", new Vec2(0, 0), new Vec2(0, 1), (0x99000000, 0.0), (0x26000000, 0.5), (0x12FFFFFF, 1.0)),
            Linear("DepthPressedShade", new Vec2(0, 0), new Vec2(0, 1), (0x2E000000, 0.00), (0x00000000, 0.60)),
            Linear("DepthDropBand", new Vec2(0, 0), new Vec2(0, 1), (0x9E06020F, 0.0), (0x0006020F, 1.0)),
            Radial("DepthDropDisc", new Vec2(0.5, 0.5), new Vec2(0.5, 0.5), 0.5, 0.5, (0x9E06020F, 0.0), (0x5C06020F, 0.6), (0x0006020F, 1.0)),
            Linear("DepthWellTop", new Vec2(0, 0), new Vec2(0, 1), (0xA6000000, 0.0), (0x00000000, 1.0)),
            Linear("DepthWellLeft", new Vec2(0, 0), new Vec2(1, 0), (0x59000000, 0.0), (0x00000000, 1.0)),
            Solid("DepthWellFoot", 0x12FFFFFF),
            Solid("DepthWellFloorBrush", 0xFF120D20),
            Linear("DepthFloatRim", new Vec2(0, 0), new Vec2(0, 1), (0x1AFFFFFF, 0.0), (0x33000000, 1.0)),
            Linear("DepthFloatBand", new Vec2(0, 0), new Vec2(0, 1), (0x6606020F, 0.0), (0x0006020F, 1.0)),
            Linear("DepthLedgeUp", new Vec2(0, 1), new Vec2(0, 0), (0xB3000000, 0.0), (0x00000000, 1.0)),
            Linear("DepthRailShadow", new Vec2(0, 0), new Vec2(1, 0), (0xBF000000, 0.0), (0x00000000, 1.0)),
            Linear("DepthTubeGloss", new Vec2(0, 0), new Vec2(0, 1), (0x8CFFFFFF, 0.00), (0x1FFFFFFF, 0.38), (0x00000000, 0.55), (0x47000000, 1.00)),
            Radial("DepthTubeBead", new Vec2(0.5, 0.5), new Vec2(0.44, 0.38), 0.5, 0.5, (0xFFFFFFFF, 0.00), (0x80FFFFFF, 0.55), (0x00FFFFFF, 1.00)),
            Radial("DepthCoinDish", new Vec2(0.5, 0.42), new Vec2(0.5, 0.42), 0.6, 0.6, (0x00000000, 0.00), (0x1A000000, 0.70), (0x66000000, 1.00)),
            Linear("DepthCoinRim", new Vec2(0, 0), new Vec2(0, 1), (0x4DFFFFFF, 0.0), (0x0AFFFFFF, 0.5), (0x99000000, 1.0)),
            Linear("DepthCoinSocket", new Vec2(0, 0), new Vec2(0, 1), (0x66000000, 0.00), (0x26000000, 0.35), (0x00000000, 0.70)),
            Linear("DepthCoinInnerShadow", new Vec2(0, 0), new Vec2(1, 1), (0xA6000000, 0.0), (0x4D000000, 0.55), (0x26000000, 1.0)),
            Linear("DepthCoinOuterRim", new Vec2(0, 0), new Vec2(1, 1), (0x40000000, 0.0), (0xD9000000, 1.0)),
            Radial("DepthCoinSpecular", new Vec2(0.12, 0.12), new Vec2(0.12, 0.12), 0.62, 0.62, (0xC8FFFFFF, 0.00), (0x59FFFFFF, 0.45), (0x00FFFFFF, 1.00)),
        };

        private static readonly Dictionary<string, DepthBrush> ByKey = All.ToDictionary(b => b.Key, StringComparer.Ordinal);

        /// <summary>The brush by its Depth.xaml key, or null.</summary>
        public static DepthBrush? Find(string key) => ByKey.TryGetValue(key, out var b) ? b : null;
    }
}
