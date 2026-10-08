// Nav rework (WPF 7.1.5) paint helpers for the Avalonia head: Core decides every colour and
// number (ConditioningControlPanel.Nav / .Depth, ARGB uint), this file only turns them into
// Avalonia brushes. No numbers live here.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Depth;
using MotionLevel = ConditioningControlPanel.Models.MotionLevel;

namespace ConditioningControlPanel.Avalonia.Controls.NavRail
{
    internal static class NavPaint
    {
        /// <summary>An ARGB uint as an Avalonia colour.</summary>
        public static Color C(uint argb) => Color.FromUInt32(argb);

        /// <summary>A solid immutable brush (safe to share across controls and threads).</summary>
        public static IBrush Solid(uint argb) => new ImmutableSolidColorBrush(C(argb));

        /// <summary>A vertical gradient (top to bottom), the WPF stops-as-tuples shape.</summary>
        public static IBrush Vertical(IEnumerable<(uint Color, double Offset)> stops) =>
            Linear(stops, new RelativePoint(0, 0, RelativeUnit.Relative), new RelativePoint(0, 1, RelativeUnit.Relative));

        /// <summary>A diagonal gradient, StartPoint 0,0 to 1,1 (the rail ring's bevel).</summary>
        public static IBrush Diagonal(IEnumerable<(uint Color, double Offset)> stops) =>
            Linear(stops, new RelativePoint(0, 0, RelativeUnit.Relative), new RelativePoint(1, 1, RelativeUnit.Relative));

        /// <summary>A horizontal gradient, left to right (the rail's shadow on the page).</summary>
        public static IBrush Horizontal(IEnumerable<(uint Color, double Offset)> stops) =>
            Linear(stops, new RelativePoint(0, 0, RelativeUnit.Relative), new RelativePoint(1, 0, RelativeUnit.Relative));

        public static IBrush Linear(IEnumerable<(uint Color, double Offset)> stops, RelativePoint start, RelativePoint end)
        {
            var g = new GradientStops();
            foreach (var (c, o) in stops) g.Add(new GradientStop(C(c), o));
            return new LinearGradientBrush { StartPoint = start, EndPoint = end, GradientStops = g }.ToImmutable();
        }

        /// <summary>A centred radial gradient (the coin's contact disc, the halo).</summary>
        public static IBrush Radial(IEnumerable<(uint Color, double Offset)> stops)
        {
            var g = new GradientStops();
            foreach (var (c, o) in stops) g.Add(new GradientStop(C(c), o));
            return new RadialGradientBrush { GradientStops = g }.ToImmutable();
        }

        private static readonly Dictionary<string, IBrush?> DepthCache = new(StringComparer.Ordinal);

        /// <summary>
        /// A brush of WPF 7.1.5 Depth.xaml by its key, built from Core's DepthPalette (the same table
        /// lane D2's Depth.axaml is generated from). Null for an unknown key. Cached: immutable.
        /// </summary>
        public static IBrush? Depth(string key)
        {
            lock (DepthCache)
            {
                if (DepthCache.TryGetValue(key, out var hit)) return hit;
                var b = DepthPalette.Find(key);
                IBrush? brush = null;
                if (b != null)
                {
                    var rel = (Func<double, double, RelativePoint>)((x, y) => new RelativePoint(x, y, RelativeUnit.Relative));
                    switch (b.Kind)
                    {
                        case DepthBrushKind.Solid:
                            brush = Solid(b.Color);
                            break;
                        case DepthBrushKind.Linear:
                            brush = Linear(b.Stops, rel(b.Start.X, b.Start.Y), rel(b.End.X, b.End.Y));
                            break;
                        case DepthBrushKind.Radial:
                            var g = new GradientStops();
                            foreach (var (c, o) in b.Stops) g.Add(new GradientStop(C(c), o));
                            brush = new RadialGradientBrush
                            {
                                Center = rel(b.Start.X, b.Start.Y),
                                GradientOrigin = rel(b.End.X, b.End.Y),
                                RadiusX = new RelativeScalar(b.RadiusX, RelativeUnit.Relative),
                                RadiusY = new RelativeScalar(b.RadiusY, RelativeUnit.Relative),
                                GradientStops = g,
                            }.ToImmutable();
                            break;
                    }
                }
                DepthCache[key] = brush;
                return brush;
            }
        }

        /// <summary>The player's motion level (the head's one reader, AmbientFxCanvas.Env).</summary>
        public static MotionLevel Level
        {
            get
            {
                try { return AmbientFxCanvas.Env.Level; }
                catch { return MotionLevel.Full; }
            }
        }
    }
}
