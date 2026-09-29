using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The WPF half of <see cref="WardrobeCatalog"/> (CCP.Core): decodes the registry's art into
    /// frozen ImageSources. Same contract - a missing PNG or unknown id is null, never a throw,
    /// and every lookup (failures included) is cached.
    /// </summary>
    public static class WardrobeArt
    {
        /// <summary>
        /// Decode cap for wardrobe art. The 512 canvases are only ever drawn at 148px (the hero's
        /// 104/0.70 decoration) or smaller, so decoding them at full size would hold ~1MB per item
        /// - 60MB once someone browses the whole picker. 384 stays crisp at 200% DPI and costs a
        /// third of that.
        /// </summary>
        private const int ArtDecodePixels = 384;

        private static readonly object _gate = new();
        private static readonly Dictionary<string, ImageSource?> _imageCache = new(StringComparer.Ordinal);

        /// <summary>
        /// The 512x512 art for an id, or null when the id is unknown or its PNG is missing or
        /// unreadable. Callers render nothing in that case - never a placeholder, never a throw.
        /// </summary>
        public static ImageSource? GetImage(string? id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;

            lock (_gate)
            {
                if (_imageCache.TryGetValue(id!, out var cached)) return cached;

                ImageSource? built = null;
                try
                {
                    var path = WardrobeCatalog.ArtPath(id);
                    if (path != null && System.IO.File.Exists(path))
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(path, UriKind.Absolute);
                        // OnLoad so the file handle is released immediately: the art stage may
                        // still be rewriting these PNGs while the app is open.
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                        bitmap.DecodePixelWidth = ArtDecodePixels;
                        bitmap.EndInit();
                        if (bitmap.CanFreeze) bitmap.Freeze();
                        built = bitmap;
                    }
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("WardrobeCatalog: art for {Id} failed to load: {E}", id, ex.Message);
                    built = null;
                }

                _imageCache[id!] = built;
                return built;
            }
        }

        /// <summary>True when this id has art we can actually paint (decodes it).</summary>
        public static bool HasArt(string? id) => GetImage(id) != null;

        /// <summary>
        /// Drops the cached registry and art. Only exists so a mid-session asset drop (the art
        /// stage writing PNGs under a running app) can be picked up without a restart.
        /// </summary>
        public static void Invalidate()
        {
            lock (_gate) _imageCache.Clear();
            WardrobeCatalog.Invalidate();

            // The silhouette masks are frozen ImageBrushes built off this cache; a stale one would
            // keep stencilling the OLD art after a mid-session asset drop.
            try { Helpers.Silhouette.InvalidateCache(); }
            catch (Exception ex) { App.Logger?.Debug("WardrobeCatalog: silhouette cache flush failed: {E}", ex.Message); }
        }
    }
}
