using System;
using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Serilog;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Resolves resource images with mod override support.
    /// Checks the active mod's resources/ folder first, falls back to embedded pack:// URI.
    /// </summary>
    public static class ModResourceResolver
    {
        private static readonly ILogger? _log = App.Logger;
        private static readonly ConcurrentDictionary<string, ImageSource?> _cache = new();

        /// <summary>
        /// Resolve a resource image path. If the active mod has an override, returns
        /// a BitmapImage from the mod's file. Otherwise returns from embedded resources.
        /// </summary>
        /// <param name="resourcePath">
        /// Relative path within Resources/, e.g. "achievements/lv_10.png" or "logo.png".
        /// </param>
        /// <returns>An ImageSource, or null if neither mod override nor embedded resource exists.</returns>
        public static ImageSource? ResolveImage(string resourcePath)
        {
            if (string.IsNullOrEmpty(resourcePath)) return null;

            // Normalize path separators
            resourcePath = resourcePath.Replace('\\', '/');

            // Check cache first
            var cacheKey = $"{App.Mods?.ActiveModId}:{resourcePath}";
            if (_cache.TryGetValue(cacheKey, out var cached))
                return cached;

            ImageSource? result = null;

            // Check active mod's resources folder
            var modPath = App.Mods?.ActiveMod?.InstalledPath;
            if (modPath != null)
            {
                var overridePath = Path.Combine(modPath, "resources", resourcePath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(overridePath))
                {
                    try
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(overridePath, UriKind.Absolute);
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();
                        bitmap.Freeze();
                        result = bitmap;
                    }
                    catch (Exception ex)
                    {
                        _log?.Warning(ex, "Failed to load mod resource override: {Path}", overridePath);
                    }
                }
            }

            // Fallback to embedded resource
            if (result == null)
            {
                try
                {
                    var packUri = new Uri($"pack://application:,,,/Resources/{resourcePath}", UriKind.Absolute);
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = packUri;
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    result = bitmap;
                }
                catch
                {
                    // Resource doesn't exist in embedded resources either
                    result = null;
                }
            }

            _cache[cacheKey] = result;
            return result;
        }

        /// <summary>
        /// Resolve a resource path to a URI string. Returns a file:// URI for mod overrides
        /// or a pack:// URI for embedded resources.
        /// </summary>
        public static string ResolveUri(string resourcePath)
        {
            if (string.IsNullOrEmpty(resourcePath))
                return $"pack://application:,,,/Resources/{resourcePath}";

            resourcePath = resourcePath.Replace('\\', '/');

            // Check active mod's resources folder
            var modPath = App.Mods?.ActiveMod?.InstalledPath;
            if (modPath != null)
            {
                var overridePath = Path.Combine(modPath, "resources", resourcePath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(overridePath))
                {
                    return new Uri(overridePath, UriKind.Absolute).AbsoluteUri;
                }
            }

            return $"pack://application:,,,/Resources/{resourcePath}";
        }

        /// <summary>
        /// Check whether the active mod has an override for a given resource.
        /// </summary>
        public static bool HasModOverride(string resourcePath)
        {
            var modPath = App.Mods?.ActiveMod?.InstalledPath;
            if (modPath == null) return false;

            resourcePath = resourcePath.Replace('\\', '/');
            var overridePath = Path.Combine(modPath, "resources", resourcePath.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(overridePath);
        }

        /// <summary>
        /// Clear the image cache (call when mod switches).
        /// </summary>
        public static void ClearCache()
        {
            _cache.Clear();
        }
    }
}
