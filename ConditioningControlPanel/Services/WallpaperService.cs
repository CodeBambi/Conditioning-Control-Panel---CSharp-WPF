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
    /// from the user's assets/wallpapers folder. Restores the original on deactivate/dispose.
    /// </summary>
    public class WallpaperService : IDisposable
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int SystemParametersInfo(int uAction, int uParam, string? lpvParam, int fuWinIni);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
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

        /// <summary>
        /// Save the current wallpaper, scan the pool, and set a random image.
        /// Returns false if no images were found.
        /// </summary>
        public bool Activate()
        {
            lock (_lock)
            {
                if (_isActive) return true;

                try
                {
                    // Save current wallpaper path
                    var sb = new StringBuilder(260);
                    SystemParametersInfo(SPI_GETDESKWALLPAPER, sb.Capacity, sb, 0);
                    _originalWallpaperPath = sb.ToString();

                    // Scan wallpapers folder
                    var wallpapersDir = Path.Combine(App.EffectiveAssetsPath, "wallpapers");
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

                    // Pick and set a random wallpaper
                    var image = _imagePool[_random.Next(_imagePool.Count)];
                    if (!SetWallpaper(image)) return false;

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
                    if (_originalWallpaperPath != null)
                    {
                        SetWallpaper(_originalWallpaperPath);
                        App.Logger?.Information("[Wallpaper] Restored original wallpaper");
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

                    string image;
                    if (_imagePool.Count == 1)
                    {
                        image = _imagePool[0];
                    }
                    else
                    {
                        // Pick a different image than current
                        do
                        {
                            image = _imagePool[_random.Next(_imagePool.Count)];
                        } while (image == _currentImagePath);
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

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Deactivate();
        }
    }
}
