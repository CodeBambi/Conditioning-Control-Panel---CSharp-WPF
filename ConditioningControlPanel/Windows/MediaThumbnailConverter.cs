using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Turns a media file path into a small, static thumbnail for the media-history list.
    ///
    /// Memory discipline (this list can hold hundreds of entries):
    ///  - Only IMAGE entries decode here; videos return null (the row template shows a glyph).
    ///  - Images decode at <see cref="ThumbSize"/> px via DecodePixelWidth, so full-res files
    ///    never enter memory - just a tiny frozen bitmap (a GIF yields its first frame).
    ///  - Results are cached in a small bounded LRU so re-scrolling is instant without the
    ///    cache itself growing without limit.
    ///  - Combined with UI virtualization (recycling), only on-screen rows ever decode.
    /// The parameter selects the mode: "image" (default) or "video-check" (return null unless
    /// it's a video whose file is missing, letting the template show a broken-file glyph).
    /// </summary>
    public class MediaThumbnailConverter : IValueConverter
    {
        private const int ThumbSize = 96;      // decode target (px); displayed smaller
        private const int CacheCap = 128;      // bounded thumbnail cache

        private static readonly object _cacheLock = new();
        private static readonly Dictionary<string, BitmapImage?> _cache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly LinkedList<string> _lru = new();

        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not MediaLogEntry entry) return null;
            if (entry.Type != MediaType.Image) return null;      // videos: no thumbnail decode

            var path = entry.FilePath;
            if (string.IsNullOrEmpty(path)) return null;

            lock (_cacheLock)
            {
                if (_cache.TryGetValue(path, out var cached))
                {
                    Touch(path);
                    return cached;
                }
            }

            BitmapImage? thumb = null;
            try
            {
                if (File.Exists(path))
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;      // read fully, then release file handle
                    bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                    bmp.DecodePixelWidth = ThumbSize;               // decode small - the whole point
                    bmp.UriSource = new Uri(path, UriKind.Absolute);
                    bmp.EndInit();
                    bmp.Freeze();                                   // cross-thread + no per-use cost
                    thumb = bmp;
                }
            }
            catch
            {
                thumb = null; // unreadable/corrupt -> template shows the placeholder glyph
            }

            lock (_cacheLock)
            {
                if (!_cache.ContainsKey(path))
                {
                    _cache[path] = thumb;
                    _lru.AddLast(path);
                    while (_lru.Count > CacheCap)
                    {
                        var oldest = _lru.First!.Value;
                        _lru.RemoveFirst();
                        _cache.Remove(oldest);
                    }
                }
            }
            return thumb;
        }

        private static void Touch(string path)
        {
            var node = _lru.Find(path);
            if (node != null)
            {
                _lru.Remove(node);
                _lru.AddLast(node);
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
