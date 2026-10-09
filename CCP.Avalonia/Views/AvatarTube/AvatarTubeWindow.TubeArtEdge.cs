// The dock seam for a mod's own tube art. WPF docks every tube on the built-in glass's measured
// padding (353), so a mod tube.png that paints further right (Infection Control's pipes) crosses
// into the main window. Here the mod's art is measured once per tube change and the seam moves
// out by the overhang; the built-in art and narrower mod art keep the WPF seam exactly.

using System;
using System.IO;
using Avalonia.Platform;
using ConditioningControlPanel.AvatarTubeLayout;
using Serilog;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube
{
    public partial class AvatarTubeWindow
    {
        /// <summary>Window design units the mod's tube art reaches past the built-in glass.</summary>
        private double _tubeArtOverhang;

        private static (double Edge, double Aspect)? _builtInTubeEdge;

        /// <summary>Re-measure after the attached glass changes; re-dock when the seam moved.</summary>
        private void RefreshTubeArtOverhang()
        {
            double next = TubeArtOverhang();
            if (Math.Abs(next - _tubeArtOverhang) < 0.5) return;
            _tubeArtOverhang = next;
            Log.Information("AvatarTube: mod tube art reaches {Units:0} units past the built-in glass", next);
            if (_isAttached) UpdatePosition();
        }

        /// <summary>0 for the built-in tube; else the measured overhang of the mod's tube.png.</summary>
        private static double TubeArtOverhang()
        {
            try
            {
                var path = CoreModArt.OverridePath("tube.png");
                if (path == null || !File.Exists(path)) return 0;
                _builtInTubeEdge ??= MeasureBuiltIn();
                if (_builtInTubeEdge is not { } b) return 0;
                using var file = File.OpenRead(path);
                if (OpaqueRightEdge(file) is not { } m) return 0;
                return TubeWindowMath.ArtOverhangUnits(m.Edge, m.Aspect, b.Edge, b.Aspect);
            }
            catch (Exception ex)
            {
                Log.Debug("AvatarTube: tube art measure failed: {E}", ex.Message);
                return 0;
            }
        }

        private static (double, double)? MeasureBuiltIn()
        {
            var uri = new Uri("avares://CCP.Avalonia/Resources/tube.png");
            if (!AssetLoader.Exists(uri)) return null;
            using var s = AssetLoader.Open(uri);
            return OpaqueRightEdge(s);
        }

        /// <summary>The rightmost column holding a visible pixel (alpha above 16), as a fraction of
        /// the width, plus the picture's aspect. Null when it will not decode or is fully clear.</summary>
        internal static (double Edge, double Aspect)? OpaqueRightEdge(Stream png)
        {
            using var bmp = SKBitmap.Decode(png);
            if (bmp == null || bmp.Width <= 0 || bmp.Height <= 0) return null;
            int w = bmp.Width, h = bmp.Height;
            int step = Math.Max(1, h / 512);
            for (int x = w - 1; x >= 0; x--)
                for (int y = 0; y < h; y += step)
                    if (bmp.GetPixel(x, y).Alpha > 16)
                        return ((x + 1) / (double)w, w / (double)h);
            return null;
        }
    }
}
