using System;
using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Serilog;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Resolves resource images with EVENT SKIN and mod override support.
    /// Chain: active event skin → active mod's resources/ → embedded pack:// URI.
    ///
    /// THE EVENT SKIN LINK (The Descent §14, "themed bubble skins") is the whole reason
    /// this file is the right place for it: bubble.png, tube.png, tube2.png and logo.png
    /// all already resolve through here, so ONE probe at the head of the chain lights
    /// every bubble renderer (the ambient/Chaos field in BubbleService, its Skia
    /// compositor twin, the Bubble Count minigame, the avatar tube's random bubble and
    /// the Intake page's data-URI sprite) plus the tube and the logo at once. Adding a
    /// skin id to any of those renderers individually would have been five divergent
    /// implementations of the same lookup.
    ///
    /// DORMANT IN THIS BUILD: nothing arms LiveEventService, so <see cref="EventSkinId"/>
    /// is always null, the probe is skipped entirely, and every resolve is byte-for-byte
    /// the mod-then-embedded answer it was before. A skin id with no art on disk is also
    /// a no-op per file — the probe is File.Exists, so an event that ships only a bubble
    /// leaves the tube and logo exactly as the mod drew them.
    /// </summary>
    public static class ModResourceResolver
    {
        private static readonly ILogger? _log = App.Logger;
        private static readonly ConcurrentDictionary<string, ImageSource?> _cache = new();

        /// <summary>Folder under UserDataPath holding downloaded event skins, one dir per skin id.</summary>
        private const string EventSkinRoot = "event_skins";

        /// <summary>The active event's skin id, or null. Null = the whole event link is skipped.</summary>
        private static string? EventSkinId => App.LiveEvent?.SkinId;

        /// <summary>
        /// Absolute path this resource would have inside the active event skin, or null
        /// when there is no event skin (or the id is not a safe single path segment — an
        /// id is server-supplied, so it gets the same traversal treatment as the path).
        /// Does NOT test existence; callers do.
        /// </summary>
        private static string? EventSkinPath(string resourcePath)
        {
            var id = EventSkinId;
            if (string.IsNullOrEmpty(id)) return null;
            if (id!.Contains("..") || id.Contains('/') || id.Contains('\\') || Path.IsPathRooted(id)) return null;

            return Path.Combine(App.UserDataPath, EventSkinRoot, id,
                resourcePath.Replace('/', Path.DirectorySeparatorChar));
        }

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

            // Reject path traversal attempts
            if (resourcePath.Contains("..") || Path.IsPathRooted(resourcePath)) return null;

            // Normalize path separators
            resourcePath = resourcePath.Replace('\\', '/');

            // Check cache first. THE EVENT SKIN ID IS PART OF THE KEY, not just the mod
            // id: an event starting or ending does not change the active mod, so without
            // it the first bubble sprite resolved would be the one every later resolve got.
            var cacheKey = $"{EventSkinId}:{App.Mods?.ActiveModId}:{resourcePath}";
            if (_cache.TryGetValue(cacheKey, out var cached))
                return cached;

            ImageSource? result = null;

            // Event skin first — it outranks the mod for the duration of the event.
            var eventPath = EventSkinPath(resourcePath);
            if (eventPath != null && File.Exists(eventPath))
            {
                result = LoadFrozen(eventPath, "event skin");
            }

            // Check active mod's resources folder
            var modPath = App.Mods?.ActiveMod?.InstalledPath;
            if (result == null && modPath != null)
            {
                var overridePath = Path.Combine(modPath, "resources", resourcePath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(overridePath))
                {
                    result = LoadFrozen(overridePath, "mod resource override");
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
        /// Load a file into a frozen BitmapImage, or null on any failure. Shared by the
        /// event-skin and mod-override branches so a broken file in either degrades the
        /// same way — one warning, then fall through to the next link in the chain.
        /// </summary>
        private static ImageSource? LoadFrozen(string path, string what)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch (Exception ex)
            {
                _log?.Warning(ex, "Failed to load {What}: {Path}", what, path);
                return null;
            }
        }

        /// <summary>
        /// Resolve a resource path to a URI string. Returns a file:// URI for mod overrides
        /// or a pack:// URI for embedded resources.
        /// </summary>
        public static string ResolveUri(string resourcePath)
        {
            if (string.IsNullOrEmpty(resourcePath))
                return $"pack://application:,,,/Resources/{resourcePath}";

            // Reject path traversal attempts
            if (resourcePath.Contains("..") || Path.IsPathRooted(resourcePath))
                return $"pack://application:,,,/Resources/{resourcePath}";

            resourcePath = resourcePath.Replace('\\', '/');

            // Event skin first, same precedence as ResolveImage — the two must agree or a
            // surface that asks for a URI (the Intake data-URI sprite, spiral.gif) would
            // draw the mod's art while its neighbour draws the event's.
            var eventPath = EventSkinPath(resourcePath);
            if (eventPath != null && File.Exists(eventPath))
            {
                return new Uri(eventPath, UriKind.Absolute).AbsoluteUri;
            }

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
        /// Check whether anything above the embedded resources overrides this path — the
        /// active event skin, or failing that the active mod.
        ///
        /// The event counts here deliberately. Callers use this to ask "did an author
        /// draw this art", and the tube pair is the reason: a skin that ships tube.png
        /// but not tube2.png must trigger the same attached-layout fallback a mod does
        /// (ModOverridesAttachedTubeOnly), or the avatar floats outside the event's
        /// chamber instead of the mod's.
        /// </summary>
        public static bool HasModOverride(string resourcePath)
        {
            resourcePath = resourcePath.Replace('\\', '/');

            var eventPath = EventSkinPath(resourcePath);
            if (eventPath != null && File.Exists(eventPath)) return true;

            var modPath = App.Mods?.ActiveMod?.InstalledPath;
            if (modPath == null) return false;

            var overridePath = Path.Combine(modPath, "resources", resourcePath.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(overridePath);
        }

        /// <summary>
        /// Resolve the built-in spiral GIF to a URI string. Mods ship the override either in
        /// resources/spirals/ (what the mod template scaffolds) or at resources/ root - probe
        /// both so a mod using either layout is honoured by every spiral call site. The
        /// embedded fallback is always the root Resources/spiral.gif.
        /// </summary>
        public static string ResolveSpiralUri()
        {
            return HasModOverride("spirals/spiral.gif")
                ? ResolveUri("spirals/spiral.gif")
                : ResolveUri("spiral.gif");
        }

        /// <summary>
        /// Resolve a sound file path. If the active mod has an override in resources/sounds/,
        /// returns the mod's file path. Otherwise returns the embedded Resources/sounds/ path.
        /// </summary>
        /// <param name="soundRelativePath">
        /// Relative path within sounds/, e.g. "giggle5.mp3" or "bubbles/Pop.mp3".
        /// </param>
        public static string ResolveAudioPath(string soundRelativePath)
        {
            if (string.IsNullOrEmpty(soundRelativePath))
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "sounds", soundRelativePath);

            // Reject path traversal attempts
            if (soundRelativePath.Contains("..") || Path.IsPathRooted(soundRelativePath))
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "sounds", soundRelativePath);

            soundRelativePath = soundRelativePath.Replace('\\', '/');

            var modPath = App.Mods?.ActiveMod?.InstalledPath;
            if (modPath != null)
            {
                var modSoundsDir = Path.Combine(modPath, "resources", "sounds");
                var overridePath = Path.Combine(modSoundsDir, soundRelativePath.Replace('/', Path.DirectorySeparatorChar));

                // Check exact match first
                if (File.Exists(overridePath))
                    return overridePath;

                // Check alternate extensions (.wav <-> .mp3) so mods can use either format
                var altExt = Path.GetExtension(overridePath).ToLowerInvariant() == ".mp3" ? ".wav" : ".mp3";
                var altPath = Path.ChangeExtension(overridePath, altExt);
                if (File.Exists(altPath))
                    return altPath;
            }

            // Shared sounds: bundled install dir first, downloaded content pack second. A miss in both
            // comes back as the install-dir path so callers' "file missing" handling is unchanged.
            return ContentLocator.Resolve(Path.Combine(
                "Resources", "sounds", soundRelativePath.Replace('/', Path.DirectorySeparatorChar)));
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
