using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Temporarily overrides the Windows desktop wallpaper with random images
    /// from the user's chosen wallpapers folder (settings.WallpaperSourceFolder, or the
    /// default assets/wallpapers folder). Restores the original on deactivate/dispose.
    /// </summary>
    public class WallpaperService : IDisposable
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int SystemParametersInfo(int uAction, int uParam, string? lpvParam, int fuWinIni);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int SystemParametersInfo(int uAction, int uParam, StringBuilder lpvParam, int fuWinIni);

        private const int SPI_SETDESKWALLPAPER = 0x0014;
        private const int SPI_GETDESKWALLPAPER = 0x0073;
        private const int SPIF_UPDATEINIFILE = 0x01;
        private const int SPIF_SENDCHANGE = 0x02;

        private static readonly string[] SupportedExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff" };

        private string? _originalWallpaperPath;
        private string? _currentImagePath;
        private bool _isActive;
        private List<string> _imagePool = new();
        private readonly Random _random = new();
        private readonly object _lock = new();
        private bool _disposed;

        public bool IsActive
        {
            get { lock (_lock) return _isActive; }
        }

        public string? CurrentFilename
        {
            get { lock (_lock) return _currentImagePath != null ? Path.GetFileName(_currentImagePath) : null; }
        }

        public WallpaperService()
        {
            // A hard kill (render wedge, task-kill, the TerminateProcess tail in App.OnExit) never
            // reaches Deactivate(), so our wallpaper is still on the desktop next launch — and a
            // fresh Activate() would capture THAT as "the original", stranding the user's real
            // wallpaper forever (#692). If the last session left one behind, put it back first.
            try
            {
                RestoreStaleOriginal();
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "[Wallpaper] Startup restore of the previous session's wallpaper failed");
            }
        }

        /// <summary>
        /// Save the current wallpaper, scan the pool, and set a random image.
        /// Returns false if no images were found or the current wallpaper couldn't be captured.
        /// </summary>
        public bool Activate()
        {
            lock (_lock)
            {
                if (_isActive) return true;

                try
                {
                    // Save current wallpaper path. If Windows can't hand back a usable one we do
                    // NOT touch the desktop at all — a skipped effect is far cheaper than a
                    // wallpaper the user can never get back (#692).
                    var original = ReadCurrentWallpaper();
                    if (original == null)
                    {
                        App.Logger?.Warning("[Wallpaper] Current desktop wallpaper is unreadable (solid colour, slideshow, theme or Spotlight?) — skipping so the desktop stays restorable");
                        return false;
                    }

                    // Scan wallpapers folder — user-chosen folder if set, else the default
                    // assets/wallpapers folder under the effective assets path.
                    var custom = App.Settings?.Current?.WallpaperSourceFolder;
                    var wallpapersDir = !string.IsNullOrWhiteSpace(custom) && Directory.Exists(custom)
                        ? custom
                        : Path.Combine(App.EffectiveAssetsPath, "wallpapers");
                    if (!Directory.Exists(wallpapersDir))
                    {
                        App.Logger?.Warning("[Wallpaper] Wallpapers directory not found: {Dir}", wallpapersDir);
                        return false;
                    }

                    _imagePool = Directory.GetFiles(wallpapersDir)
                        .Where(f => SupportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                        .ToList();

                    if (_imagePool.Count == 0)
                    {
                        App.Logger?.Warning("[Wallpaper] No supported images found in {Dir}", wallpapersDir);
                        return false;
                    }

                    // Pick a random wallpaper. Breadcrumb goes down BEFORE the desktop changes:
                    // between SetWallpaper and the persist there is a window where the user's
                    // wallpaper is already gone but nothing on disk remembers it, and a kill in
                    // there strands them permanently (#692). Write first, undo if the set fails.
                    var image = _imagePool[_random.Next(_imagePool.Count)];
                    PersistOriginal(original);
                    if (!SetWallpaper(image))
                    {
                        PersistOriginal(null);
                        return false;
                    }

                    _originalWallpaperPath = original;
                    _currentImagePath = image;
                    _isActive = true;
                    App.Logger?.Information("[Wallpaper] Activated with {File} (pool: {Count} images)", Path.GetFileName(image), _imagePool.Count);
                    return true;
                }
                catch (Exception ex)
                {
                    App.Logger?.Error(ex, "[Wallpaper] Failed to activate");
                    return false;
                }
            }
        }

        /// <summary>
        /// Restore the original wallpaper.
        /// </summary>
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
                        // Never hand SPI_SETDESKWALLPAPER an empty/missing path: Windows clears the
                        // desktop to a flat colour and writes that to the registry (#692).
                        App.Logger?.Warning("[Wallpaper] No usable original to restore ({Path}) — leaving the desktop as-is", original ?? "<null>");
                    }
                    else if (SetWallpaper(original))
                    {
                        App.Logger?.Information("[Wallpaper] Restored original wallpaper");
                        // Restored for real — drop the crash-recovery breadcrumb.
                        PersistOriginal(null);
                    }
                    else
                    {
                        // Keep the persisted path so the next launch gets another go at it.
                        App.Logger?.Warning("[Wallpaper] Windows refused to restore {Path} (win32 {Err})", original, Marshal.GetLastWin32Error());
                    }
                }
                catch (Exception ex)
                {
                    App.Logger?.Error(ex, "[Wallpaper] Failed to restore original wallpaper");
                }
                finally
                {
                    _isActive = false;
                    _currentImagePath = null;
                }
            }
        }

        /// <summary>
        /// If active, pick a new random image (different from current if possible).
        /// If not active, activate.
        /// </summary>
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
                    {
                        // Try to pick a different image than current. Bounded: GetFiles can't return
                        // duplicates today, but an unbounded spin here is one duplicate entry away
                        // from wedging the UI thread.
                        for (var attempt = 0; attempt < 8 && image == _currentImagePath; attempt++)
                            image = _imagePool[_random.Next(_imagePool.Count)];
                    }

                    if (!SetWallpaper(image)) return false;

                    _currentImagePath = image;
                    App.Logger?.Debug("[Wallpaper] Shuffled to {File}", Path.GetFileName(image));
                    return true;
                }
                catch (Exception ex)
                {
                    App.Logger?.Error(ex, "[Wallpaper] Failed to shuffle");
                    return false;
                }
            }
        }

        private static bool SetWallpaper(string path)
        {
            var result = SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, path, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
            return result != 0;
        }

        /// <summary>
        /// Read the desktop wallpaper path, or null if Windows didn't hand back a usable one.
        /// The buffer is deliberately well past MAX_PATH — OneDrive-redirected Pictures folders
        /// routinely produce longer paths and the API truncates silently instead of failing.
        /// An empty result is normal for solid-colour, slideshow, theme and Spotlight desktops.
        /// </summary>
        private static string? ReadCurrentWallpaper()
        {
            var sb = new StringBuilder(4096);
            if (SystemParametersInfo(SPI_GETDESKWALLPAPER, sb.Capacity, sb, 0) == 0)
            {
                App.Logger?.Warning("[Wallpaper] SPI_GETDESKWALLPAPER failed (win32 {Err})", Marshal.GetLastWin32Error());
                return null;
            }

            var path = sb.ToString();
            if (string.IsNullOrWhiteSpace(path)) return null;
            if (!File.Exists(path))
            {
                App.Logger?.Warning("[Wallpaper] Reported wallpaper does not exist on disk: {Path}", path);
                return null;
            }
            return path;
        }

        /// <summary>
        /// Remember (or forget) the captured original in settings so a session that dies without
        /// reaching Deactivate() can put the user's wallpaper back on the next launch.
        /// Never persists an empty path — that's the value that causes #692 in the first place.
        /// </summary>
        private static void PersistOriginal(string? path)
        {
            try
            {
                var s = App.Settings?.Current;
                if (s == null) return;

                var value = string.IsNullOrWhiteSpace(path) ? "" : path!;
                if (s.WallpaperOriginalPath == value) return;

                s.WallpaperOriginalPath = value;
                // SaveImmediate, NOT Save(): the debounced path waits 500ms for quiet, and a kill
                // inside that window leaves no breadcrumb at all — the next launch then captures
                // OUR wallpaper as "the original" and the user's is stranded for good. That is #692
                // again, just through a narrower door. This writes maybe twice a session; the
                // debounce buys nothing here and costs the whole self-heal.
                App.Settings?.SaveImmediate();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[Wallpaper] Could not persist the original wallpaper path");
            }
        }

        /// <summary>
        /// Put back a wallpaper a previous session captured but never restored (crash / kill).
        /// Clears the breadcrumb either way once we've had our go at it.
        /// </summary>
        private static void RestoreStaleOriginal()
        {
            var s = App.Settings?.Current;
            var stale = s?.WallpaperOriginalPath;
            if (s == null || string.IsNullOrWhiteSpace(stale)) return;

            if (File.Exists(stale) && SetWallpaper(stale))
                App.Logger?.Information("[Wallpaper] Previous session never restored — put {File} back", Path.GetFileName(stale));
            else
                App.Logger?.Warning("[Wallpaper] Stale original {Path} could not be restored (gone or refused)", stale);

            s.WallpaperOriginalPath = "";
            App.Settings?.Save();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Deactivate();
        }
    }
}
