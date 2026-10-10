// PORTED from ConditioningControlPanel/Services/Video/WallpaperService.cs (7.1.5): the rules. The desktop
// itself is behind IWallpaperBackend (Windows SystemParametersInfo; GNOME / KDE settings on Linux).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Serilog;

namespace ConditioningControlPanel.Services
{
    /// <summary>The desktop's own wallpaper setting. <see cref="Read"/> answers a file that exists, or null
    /// when the desktop cannot say (solid colour, slideshow, an unknown desktop): then nothing is changed.</summary>
    public interface IWallpaperBackend
    {
        string? Read();
        bool Set(string path);
    }

    /// <summary>
    /// Temporarily overrides the desktop wallpaper with random images from the user's wallpapers folder
    /// (settings.WallpaperSourceFolder, or assets/wallpapers) and restores the original on deactivate.
    /// The original is written to settings BEFORE the desktop changes (WallpaperOriginalPath, saved at
    /// once), so a session that dies without restoring puts it back on the next launch (#692).
    /// not ported: the remote (online) wallpaper pool; this head shuffles the local folder only.
    /// </summary>
    public sealed class WallpaperService : IDisposable
    {
        /// <summary>WPF SupportedExtensions.</summary>
        public static readonly string[] SupportedExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff" };

        private readonly IWallpaperBackend _backend;
        private readonly Random _random;
        private readonly object _lock = new();
        private string? _originalWallpaperPath;
        private string? _currentImagePath;
        private bool _isActive;
        private List<string> _imagePool = new();
        private bool _disposed;

        public bool IsActive { get { lock (_lock) return _isActive; } }
        public string? CurrentFilename { get { lock (_lock) return _currentImagePath == null ? null : Path.GetFileName(_currentImagePath); } }

        public WallpaperService(IWallpaperBackend backend, Random? random = null)
        {
            _backend = backend;
            _random = random ?? new Random();
            // A hard kill never reaches Deactivate(), so our wallpaper is still on the desktop next launch,
            // and a fresh Activate() would capture THAT as "the original" (#692). Put it back first.
            try { RestoreStaleOriginal(); }
            catch (Exception ex) { Log.Error(ex, "[Wallpaper] Startup restore of the previous session's wallpaper failed"); }
        }

        /// <summary>The folder she pulls from: the user's pick when it exists, else assets/wallpapers.</summary>
        public static string SourceFolder()
        {
            var custom = CoreSettings.Current?.WallpaperSourceFolder;
            return !string.IsNullOrWhiteSpace(custom) && Directory.Exists(custom)
                ? custom
                : Path.Combine(CorePaths.EffectiveAssets, "wallpapers");
        }

        /// <summary>Save the current wallpaper, scan the pool, set a random image. False when there are no
        /// images or the current wallpaper could not be captured (then the desktop is not touched).</summary>
        public bool Activate()
        {
            lock (_lock)
            {
                if (_isActive) return true;
                try
                {
                    var original = _backend.Read();
                    if (original == null)
                    {
                        Log.Warning("[Wallpaper] Current desktop wallpaper is unreadable - skipping so the desktop stays restorable");
                        return false;
                    }

                    var dir = SourceFolder();
                    _imagePool = Directory.Exists(dir)
                        ? Directory.GetFiles(dir).Where(f => SupportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant())).ToList()
                        : new List<string>();
                    if (_imagePool.Count == 0)
                    {
                        Log.Warning("[Wallpaper] No usable images - local folder {Dir} is empty or absent", dir);
                        return false;
                    }

                    // Breadcrumb BEFORE the desktop changes: a kill between the set and the persist would
                    // strand the user's wallpaper for good (#692). Write first, undo if the set fails.
                    var image = _imagePool[_random.Next(_imagePool.Count)];
                    PersistOriginal(original);
                    if (!_backend.Set(image))
                    {
                        PersistOriginal(null);
                        return false;
                    }

                    _originalWallpaperPath = original;
                    _currentImagePath = image;
                    _isActive = true;
                    Log.Information("[Wallpaper] Activated with {File} (pool: {Count})", Path.GetFileName(image), _imagePool.Count);
                    return true;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[Wallpaper] Failed to activate");
                    return false;
                }
            }
        }

        /// <summary>Restore the original wallpaper.</summary>
        public void Deactivate()
        {
            lock (_lock)
            {
                if (!_isActive) return;
                try
                {
                    var original = _originalWallpaperPath;
                    if (string.IsNullOrWhiteSpace(original) || !File.Exists(original))
                    {
                        // Never hand the desktop an empty or missing path: it clears to a flat colour (#692).
                        Log.Warning("[Wallpaper] No usable original to restore ({Path}) - leaving the desktop as-is", original ?? "<null>");
                    }
                    else if (_backend.Set(original))
                    {
                        Log.Information("[Wallpaper] Restored original wallpaper");
                        PersistOriginal(null);   // restored for real: drop the crash-recovery breadcrumb
                    }
                    else
                    {
                        // Keep the persisted path so the next launch gets another go at it.
                        Log.Warning("[Wallpaper] The desktop refused to restore {Path}", original);
                    }
                }
                catch (Exception ex) { Log.Error(ex, "[Wallpaper] Failed to restore original wallpaper"); }
                finally
                {
                    _isActive = false;
                    _currentImagePath = null;
                }
            }
        }

        /// <summary>If active, pick a new random image (different from the current one when possible);
        /// if not active, activate.</summary>
        public bool Shuffle()
        {
            lock (_lock)
            {
                if (!_isActive) return Activate();
                try
                {
                    if (_imagePool.Count == 0) return false;
                    var image = _imagePool[_random.Next(_imagePool.Count)];
                    if (_imagePool.Count > 1)
                        for (var attempt = 0; attempt < 8 && image == _currentImagePath; attempt++)
                            image = _imagePool[_random.Next(_imagePool.Count)];
                    if (!_backend.Set(image)) return false;
                    _currentImagePath = image;
                    Log.Debug("[Wallpaper] Shuffled to {File}", Path.GetFileName(image));
                    return true;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[Wallpaper] Failed to shuffle");
                    return false;
                }
            }
        }

        /// <summary>Remember (or forget) the captured original so a session that dies without Deactivate()
        /// can put it back next launch. SaveImmediate, never the debounced save: a kill inside the debounce
        /// leaves no breadcrumb at all.</summary>
        private static void PersistOriginal(string? path)
        {
            try
            {
                var s = CoreSettings.Current;
                if (s == null) return;
                var value = string.IsNullOrWhiteSpace(path) ? "" : path!;
                if (s.WallpaperOriginalPath == value) return;
                s.WallpaperOriginalPath = value;
                CoreSettings.SaveImmediate();
            }
            catch (Exception ex) { Log.Warning(ex, "[Wallpaper] Could not persist the original wallpaper path"); }
        }

        /// <summary>Put back a wallpaper a previous session captured but never restored (crash / kill).
        /// Clears the breadcrumb either way once it has had its go.</summary>
        private void RestoreStaleOriginal()
        {
            var s = CoreSettings.Current;
            var stale = s?.WallpaperOriginalPath;
            if (s == null || string.IsNullOrWhiteSpace(stale)) return;
            if (File.Exists(stale) && _backend.Set(stale))
                Log.Information("[Wallpaper] Previous session never restored - put {File} back", Path.GetFileName(stale));
            else
                Log.Warning("[Wallpaper] Stale original {Path} could not be restored (gone or refused)", stale);
            s.WallpaperOriginalPath = "";
            CoreSettings.SaveImmediate();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Deactivate();
        }
    }
}
