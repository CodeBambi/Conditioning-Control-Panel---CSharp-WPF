using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Manages downloading, installing, and activating community content packs.
    /// </summary>
    public class ContentPackService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _packsFolder;
        private readonly string _manifestCachePath;
        private List<ContentPack> _cachedPacks = new();
        private bool _disposed;

        // Remote packs manifest URL - can be configured
        private const string DefaultManifestUrl = "https://raw.githubusercontent.com/ConditioningControlPanel/packs/main/manifest.json";

        public event EventHandler<ContentPack>? PackDownloadStarted;
        public event EventHandler<ContentPack>? PackDownloadCompleted;
        public event EventHandler<(ContentPack Pack, int Progress)>? PackDownloadProgress;

        public ContentPackService()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromMinutes(10); // Allow long downloads
            _packsFolder = Path.Combine(App.UserDataPath, "packs");
            _manifestCachePath = Path.Combine(_packsFolder, "manifest_cache.json");
            Directory.CreateDirectory(_packsFolder);
        }

        /// <summary>
        /// Fetches available content packs from the remote manifest.
        /// Returns cached data if fetch fails.
        /// </summary>
        public async Task<List<ContentPack>> FetchAvailablePacksAsync()
        {
            try
            {
                var manifestUrl = App.Settings?.Current?.BambiCloudUrl ?? DefaultManifestUrl;
                // Use a specific endpoint for packs if cloud URL is set
                if (!manifestUrl.Contains("manifest.json"))
                {
                    manifestUrl = manifestUrl.TrimEnd('/') + "/packs/manifest.json";
                }

                App.Logger?.Debug("Fetching content packs manifest from {Url}", manifestUrl);

                var response = await _httpClient.GetStringAsync(manifestUrl);
                var manifest = JsonConvert.DeserializeObject<PacksManifest>(response);

                if (manifest?.Packs != null)
                {
                    _cachedPacks = manifest.Packs;

                    // Update download/active status from settings
                    foreach (var pack in _cachedPacks)
                    {
                        pack.IsDownloaded = App.Settings.Current.InstalledPackIds.Contains(pack.Id);
                        pack.IsActive = App.Settings.Current.ActivePackIds.Contains(pack.Id);
                        pack.LocalPath = Path.Combine(_packsFolder, pack.Id);
                    }

                    // Cache the manifest locally
                    await File.WriteAllTextAsync(_manifestCachePath, response);

                    App.Logger?.Information("Fetched {Count} content packs", _cachedPacks.Count);
                    return _cachedPacks;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to fetch content packs manifest, using cache");

                // Try to load from cache
                if (File.Exists(_manifestCachePath))
                {
                    try
                    {
                        var cached = await File.ReadAllTextAsync(_manifestCachePath);
                        var manifest = JsonConvert.DeserializeObject<PacksManifest>(cached);
                        if (manifest?.Packs != null)
                        {
                            _cachedPacks = manifest.Packs;
                            foreach (var pack in _cachedPacks)
                            {
                                pack.IsDownloaded = App.Settings.Current.InstalledPackIds.Contains(pack.Id);
                                pack.IsActive = App.Settings.Current.ActivePackIds.Contains(pack.Id);
                                pack.LocalPath = Path.Combine(_packsFolder, pack.Id);
                            }
                            return _cachedPacks;
                        }
                    }
                    catch
                    {
                        // Ignore cache errors
                    }
                }
            }

            return _cachedPacks;
        }

        /// <summary>
        /// Downloads and extracts a content pack.
        /// </summary>
        public async Task DownloadPackAsync(ContentPack pack, IProgress<int>? progress = null)
        {
            if (string.IsNullOrEmpty(pack.DownloadUrl))
            {
                throw new InvalidOperationException("Pack has no download URL");
            }

            var packFolder = Path.Combine(_packsFolder, pack.Id);
            var tempZipPath = Path.Combine(_packsFolder, $"{pack.Id}_temp.zip");

            try
            {
                PackDownloadStarted?.Invoke(this, pack);
                App.Logger?.Information("Starting download of pack: {Name}", pack.Name);

                // Download with progress
                using var response = await _httpClient.GetAsync(pack.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1;
                var downloadedBytes = 0L;

                using (var contentStream = await response.Content.ReadAsStreamAsync())
                using (var fileStream = new FileStream(tempZipPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[8192];
                    int bytesRead;

                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead);
                        downloadedBytes += bytesRead;

                        if (totalBytes > 0)
                        {
                            var progressPercent = (int)(downloadedBytes * 100 / totalBytes);
                            progress?.Report(progressPercent);
                            pack.DownloadProgress = progressPercent;
                            PackDownloadProgress?.Invoke(this, (pack, progressPercent));
                        }
                    }
                }

                App.Logger?.Debug("Download complete, extracting to {Folder}", packFolder);

                // Extract the ZIP
                if (Directory.Exists(packFolder))
                {
                    Directory.Delete(packFolder, true);
                }

                ZipFile.ExtractToDirectory(tempZipPath, packFolder);

                // Clean up temp file
                File.Delete(tempZipPath);

                // Update settings
                if (!App.Settings.Current.InstalledPackIds.Contains(pack.Id))
                {
                    App.Settings.Current.InstalledPackIds.Add(pack.Id);
                }

                pack.IsDownloaded = true;
                pack.LocalPath = packFolder;

                App.Logger?.Information("Pack installed successfully: {Name}", pack.Name);
                PackDownloadCompleted?.Invoke(this, pack);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to download pack: {Name}", pack.Name);

                // Clean up on failure
                if (File.Exists(tempZipPath))
                {
                    try { File.Delete(tempZipPath); } catch { }
                }

                throw;
            }
        }

        /// <summary>
        /// Activates a pack by copying its assets to the main assets folder.
        /// </summary>
        public void ActivatePack(ContentPack pack)
        {
            if (!pack.IsDownloaded || string.IsNullOrEmpty(pack.LocalPath))
            {
                throw new InvalidOperationException("Pack is not downloaded");
            }

            var assetsPath = App.EffectiveAssetsPath;
            var packImagesPath = Path.Combine(pack.LocalPath, "images");
            var packVideosPath = Path.Combine(pack.LocalPath, "videos");

            // Copy images
            if (Directory.Exists(packImagesPath))
            {
                var destImagesPath = Path.Combine(assetsPath, "images", $"pack_{pack.Id}");
                CopyDirectory(packImagesPath, destImagesPath);
            }

            // Copy videos
            if (Directory.Exists(packVideosPath))
            {
                var destVideosPath = Path.Combine(assetsPath, "videos", $"pack_{pack.Id}");
                CopyDirectory(packVideosPath, destVideosPath);
            }

            // Update settings
            if (!App.Settings.Current.ActivePackIds.Contains(pack.Id))
            {
                App.Settings.Current.ActivePackIds.Add(pack.Id);
            }

            pack.IsActive = true;
            App.Logger?.Information("Pack activated: {Name}", pack.Name);
        }

        /// <summary>
        /// Deactivates a pack by removing its assets from the main assets folder.
        /// </summary>
        public void DeactivatePack(ContentPack pack)
        {
            var assetsPath = App.EffectiveAssetsPath;
            var packImagesPath = Path.Combine(assetsPath, "images", $"pack_{pack.Id}");
            var packVideosPath = Path.Combine(assetsPath, "videos", $"pack_{pack.Id}");

            // Remove images
            if (Directory.Exists(packImagesPath))
            {
                Directory.Delete(packImagesPath, true);
            }

            // Remove videos
            if (Directory.Exists(packVideosPath))
            {
                Directory.Delete(packVideosPath, true);
            }

            // Update settings
            App.Settings.Current.ActivePackIds.Remove(pack.Id);

            pack.IsActive = false;
            App.Logger?.Information("Pack deactivated: {Name}", pack.Name);
        }

        /// <summary>
        /// Deletes a downloaded pack completely.
        /// </summary>
        public void DeletePack(ContentPack pack)
        {
            // First deactivate
            if (pack.IsActive)
            {
                DeactivatePack(pack);
            }

            // Delete the pack folder
            if (!string.IsNullOrEmpty(pack.LocalPath) && Directory.Exists(pack.LocalPath))
            {
                Directory.Delete(pack.LocalPath, true);
            }

            // Update settings
            App.Settings.Current.InstalledPackIds.Remove(pack.Id);

            pack.IsDownloaded = false;
            pack.LocalPath = "";
            App.Logger?.Information("Pack deleted: {Name}", pack.Name);
        }

        /// <summary>
        /// Gets list of installed packs.
        /// </summary>
        public List<ContentPack> GetInstalledPacks()
        {
            return _cachedPacks.Where(p => p.IsDownloaded).ToList();
        }

        /// <summary>
        /// Checks if a pack is installed.
        /// </summary>
        public bool IsPackInstalled(string packId)
        {
            return App.Settings.Current.InstalledPackIds.Contains(packId);
        }

        /// <summary>
        /// Checks if a pack is active.
        /// </summary>
        public bool IsPackActive(string packId)
        {
            return App.Settings.Current.ActivePackIds.Contains(packId);
        }

        private static void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var destFile = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, destFile, true);
            }

            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                var destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
                CopyDirectory(dir, destSubDir);
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _httpClient?.Dispose();
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// JSON structure for the packs manifest.
    /// </summary>
    internal class PacksManifest
    {
        [JsonProperty("version")]
        public string Version { get; set; } = "1.0";

        [JsonProperty("packs")]
        public List<ContentPack> Packs { get; set; } = new();
    }
}
