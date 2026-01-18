using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Manages downloading, installing, and activating encrypted content packs.
    /// Packs are stored in a hidden folder with encrypted files to deter piracy.
    /// </summary>
    public class ContentPackService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _packsFolder;
        private readonly string _manifestCachePath;
        private List<ContentPack> _availablePacks = new();
        private Dictionary<string, InstalledPackManifest> _installedManifests = new();
        private bool _disposed;

        // GitHub releases URL for pack downloads
        private const string PacksManifestUrl = "https://raw.githubusercontent.com/CodeBambi/ccp-packs/main/manifest.json";

        // Built-in packs definition (shown even if manifest fetch fails)
        private static readonly List<ContentPack> BuiltInPacks = new()
        {
            new ContentPack
            {
                Id = "basic-bimbo-starter",
                Name = "Basic Bimbo Starter Pack",
                Description = "Essential images and videos to begin your journey. Perfect for newcomers!",
                Author = "CodeBambi",
                Version = "1.0.0",
                ImageCount = 50,
                VideoCount = 5,
                SizeBytes = 350_000_000, // ~350 MB
                DownloadUrl = "https://github.com/CodeBambi/ccp-packs/releases/download/v1.0/basic-bimbo-starter.zip",
                PreviewImageUrl = "https://raw.githubusercontent.com/CodeBambi/ccp-packs/main/previews/basic-bimbo-starter.jpg",
                PatreonUrl = "",
                UpgradeUrl = ""
            },
            new ContentPack
            {
                Id = "enhanced-bimbodoll-expansion",
                Name = "Enhanced Bimbodoll Expansion",
                Description = "Advanced content for experienced users. Includes premium videos and exclusive images.",
                Author = "CodeBambi",
                Version = "1.0.0",
                ImageCount = 150,
                VideoCount = 15,
                SizeBytes = 500_000_000, // ~500 MB
                DownloadUrl = "https://github.com/CodeBambi/ccp-packs/releases/download/v1.0/enhanced-bimbodoll-expansion.zip",
                PreviewImageUrl = "https://raw.githubusercontent.com/CodeBambi/ccp-packs/main/previews/enhanced-bimbodoll-expansion.jpg",
                PatreonUrl = "https://patreon.com/CodeBambi",
                UpgradeUrl = ""
            }
        };

        public event EventHandler<ContentPack>? PackDownloadStarted;
        public event EventHandler<ContentPack>? PackDownloadCompleted;
        public event EventHandler<(ContentPack Pack, int Progress)>? PackDownloadProgress;
        public event EventHandler<ContentPack>? PackInstallFailed;

        public ContentPackService()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromMinutes(30); // Allow long downloads for large packs

            // Use hidden folder with dot prefix
            _packsFolder = Path.Combine(App.UserDataPath, ".packs");
            _manifestCachePath = Path.Combine(_packsFolder, ".manifest_cache.enc");

            // Create hidden directory
            if (!Directory.Exists(_packsFolder))
            {
                var di = Directory.CreateDirectory(_packsFolder);
                di.Attributes |= FileAttributes.Hidden;
            }

            // Load installed pack manifests
            LoadInstalledManifests();
        }

        /// <summary>
        /// Gets the list of available packs (from remote or built-in).
        /// </summary>
        public async Task<List<ContentPack>> GetAvailablePacksAsync()
        {
            try
            {
                // Try to fetch remote manifest
                var response = await _httpClient.GetStringAsync(PacksManifestUrl);
                var manifest = JsonConvert.DeserializeObject<PacksManifest>(response);

                if (manifest?.Packs?.Count > 0)
                {
                    _availablePacks = manifest.Packs;
                    App.Logger?.Information("Fetched {Count} packs from remote manifest", _availablePacks.Count);
                }
                else
                {
                    _availablePacks = new List<ContentPack>(BuiltInPacks);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Could not fetch remote packs manifest: {Error}, using built-in", ex.Message);
                _availablePacks = new List<ContentPack>(BuiltInPacks);
            }

            // Update installed/active status
            foreach (var pack in _availablePacks)
            {
                pack.IsDownloaded = IsPackInstalled(pack.Id);
                pack.IsActive = IsPackActive(pack.Id);
            }

            return _availablePacks;
        }

        /// <summary>
        /// Gets built-in packs without network request.
        /// </summary>
        public List<ContentPack> GetBuiltInPacks()
        {
            var packs = new List<ContentPack>(BuiltInPacks);
            foreach (var pack in packs)
            {
                pack.IsDownloaded = IsPackInstalled(pack.Id);
                pack.IsActive = IsPackActive(pack.Id);
            }
            return packs;
        }

        /// <summary>
        /// Downloads, encrypts, and installs a content pack.
        /// </summary>
        public async Task InstallPackAsync(ContentPack pack, IProgress<int>? progress = null)
        {
            if (string.IsNullOrEmpty(pack.DownloadUrl))
            {
                throw new InvalidOperationException("Pack has no download URL");
            }

            // Generate unique folder name (GUID for obfuscation)
            var packGuid = Guid.NewGuid().ToString("N");
            var packFolder = Path.Combine(_packsFolder, packGuid);
            var tempZipPath = Path.Combine(_packsFolder, $".{packGuid}_temp.zip");

            try
            {
                pack.IsDownloading = true;
                PackDownloadStarted?.Invoke(this, pack);
                App.Logger?.Information("Starting download of pack: {Name}", pack.Name);

                // Download ZIP file
                using var response = await _httpClient.GetAsync(pack.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? pack.SizeBytes;
                var downloadedBytes = 0L;

                using (var contentStream = await response.Content.ReadAsStreamAsync())
                using (var fileStream = new FileStream(tempZipPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[81920]; // 80KB buffer for faster downloads
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

                App.Logger?.Debug("Download complete ({Bytes} bytes), extracting and encrypting...", downloadedBytes);

                // Extract to temp folder first
                var tempExtractPath = Path.Combine(_packsFolder, $".{packGuid}_extract");
                ZipFile.ExtractToDirectory(tempZipPath, tempExtractPath);

                // Create encrypted pack structure
                Directory.CreateDirectory(packFolder);
                var contentFolder = Path.Combine(packFolder, "content");
                Directory.CreateDirectory(contentFolder);

                // Process and encrypt files, building manifest
                var manifest = new InstalledPackManifest
                {
                    PackId = pack.Id,
                    PackGuid = packGuid,
                    PackName = pack.Name,
                    InstalledDate = DateTime.UtcNow,
                    Files = new List<PackFileEntry>()
                };

                // Process images
                var imagesPath = Path.Combine(tempExtractPath, "images");
                if (Directory.Exists(imagesPath))
                {
                    await ProcessAndEncryptFilesAsync(imagesPath, contentFolder, "image", manifest);
                }

                // Process videos
                var videosPath = Path.Combine(tempExtractPath, "videos");
                if (Directory.Exists(videosPath))
                {
                    await ProcessAndEncryptFilesAsync(videosPath, contentFolder, "video", manifest);
                }

                // Save encrypted manifest
                var manifestJson = JsonConvert.SerializeObject(manifest, Formatting.Indented);
                var manifestPath = Path.Combine(packFolder, ".manifest.enc");
                PackEncryptionService.SaveEncryptedManifest(manifestJson, manifestPath);

                // Clean up temp files
                File.Delete(tempZipPath);
                Directory.Delete(tempExtractPath, true);

                // Hide the pack folder
                new DirectoryInfo(packFolder).Attributes |= FileAttributes.Hidden;

                // Update settings
                if (!App.Settings.Current.InstalledPackIds.Contains(pack.Id))
                {
                    App.Settings.Current.InstalledPackIds.Add(pack.Id);
                }

                // Store GUID mapping
                App.Settings.Current.PackGuidMap ??= new Dictionary<string, string>();
                App.Settings.Current.PackGuidMap[pack.Id] = packGuid;
                App.Settings.Save();

                // Cache manifest
                _installedManifests[pack.Id] = manifest;

                pack.IsDownloaded = true;
                pack.IsDownloading = false;
                pack.DownloadProgress = 100;

                App.Logger?.Information("Pack installed successfully: {Name} ({FileCount} files encrypted)",
                    pack.Name, manifest.Files.Count);
                PackDownloadCompleted?.Invoke(this, pack);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to install pack: {Name}", pack.Name);
                pack.IsDownloading = false;
                pack.DownloadProgress = 0;

                // Clean up on failure
                CleanupFailedInstall(tempZipPath, packFolder);

                PackInstallFailed?.Invoke(this, pack);
                throw;
            }
        }

        /// <summary>
        /// Processes files from a folder, encrypts them, and adds to manifest.
        /// </summary>
        private async Task ProcessAndEncryptFilesAsync(string sourceFolder, string destFolder,
            string fileType, InstalledPackManifest manifest)
        {
            var extensions = fileType == "image"
                ? new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" }
                : new[] { ".mp4", ".webm", ".mkv", ".avi", ".mov", ".wmv" };

            var files = Directory.GetFiles(sourceFolder)
                .Where(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .ToList();

            foreach (var file in files)
            {
                var originalName = Path.GetFileName(file);
                var obfuscatedName = PackEncryptionService.GenerateObfuscatedFilename() + ".enc";
                var destPath = Path.Combine(destFolder, obfuscatedName);

                // Encrypt file
                await Task.Run(() => PackEncryptionService.EncryptFile(file, destPath));

                // Add to manifest
                manifest.Files.Add(new PackFileEntry
                {
                    OriginalName = originalName,
                    ObfuscatedName = obfuscatedName,
                    FileType = fileType,
                    Extension = Path.GetExtension(file).ToLowerInvariant()
                });
            }
        }

        /// <summary>
        /// Activates a pack (adds to active list so files appear in TreeView).
        /// </summary>
        public void ActivatePack(string packId)
        {
            if (!App.Settings.Current.ActivePackIds.Contains(packId))
            {
                App.Settings.Current.ActivePackIds.Add(packId);
                App.Settings.Save();
            }

            var pack = _availablePacks.FirstOrDefault(p => p.Id == packId);
            if (pack != null)
            {
                pack.IsActive = true;
            }

            App.Logger?.Information("Pack activated: {Id}", packId);
        }

        /// <summary>
        /// Deactivates a pack (removes from active list).
        /// </summary>
        public void DeactivatePack(string packId)
        {
            App.Settings.Current.ActivePackIds.Remove(packId);
            App.Settings.Save();

            var pack = _availablePacks.FirstOrDefault(p => p.Id == packId);
            if (pack != null)
            {
                pack.IsActive = false;
            }

            App.Logger?.Information("Pack deactivated: {Id}", packId);
        }

        /// <summary>
        /// Completely removes an installed pack.
        /// </summary>
        public void UninstallPack(string packId)
        {
            // Deactivate first
            DeactivatePack(packId);

            // Get GUID and delete folder
            if (App.Settings.Current.PackGuidMap?.TryGetValue(packId, out var guid) == true)
            {
                var packFolder = Path.Combine(_packsFolder, guid);
                if (Directory.Exists(packFolder))
                {
                    Directory.Delete(packFolder, true);
                }

                App.Settings.Current.PackGuidMap.Remove(packId);
            }

            App.Settings.Current.InstalledPackIds.Remove(packId);
            App.Settings.Save();

            _installedManifests.Remove(packId);

            var pack = _availablePacks.FirstOrDefault(p => p.Id == packId);
            if (pack != null)
            {
                pack.IsDownloaded = false;
            }

            App.Logger?.Information("Pack uninstalled: {Id}", packId);
        }

        /// <summary>
        /// Gets files from an installed pack for display in TreeView.
        /// </summary>
        public List<PackFileEntry> GetPackFiles(string packId, string? fileType = null)
        {
            if (!_installedManifests.TryGetValue(packId, out var manifest))
            {
                LoadPackManifest(packId);
                _installedManifests.TryGetValue(packId, out manifest);
            }

            if (manifest == null) return new List<PackFileEntry>();

            var files = manifest.Files.AsEnumerable();
            if (!string.IsNullOrEmpty(fileType))
            {
                files = files.Where(f => f.FileType == fileType);
            }

            return files.ToList();
        }

        /// <summary>
        /// Decrypts and returns a thumbnail for a pack file.
        /// </summary>
        public BitmapImage? GetPackFileThumbnail(string packId, PackFileEntry file, int width = 100, int height = 100)
        {
            try
            {
                var guidMap = App.Settings.Current.PackGuidMap;
                if (guidMap == null || !guidMap.TryGetValue(packId, out var guid))
                    return null;

                var filePath = Path.Combine(_packsFolder, guid, "content", file.ObfuscatedName);
                if (!File.Exists(filePath)) return null;

                using var decryptedStream = PackEncryptionService.DecryptFileToStream(filePath);

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = width;
                bitmap.DecodePixelHeight = height;
                bitmap.StreamSource = decryptedStream;
                bitmap.EndInit();
                bitmap.Freeze();

                return bitmap;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to get pack file thumbnail: {Error}", ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Decrypts a pack file to a memory stream (for playback/display).
        /// </summary>
        public MemoryStream? GetPackFileStream(string packId, PackFileEntry file)
        {
            try
            {
                var guidMap = App.Settings.Current.PackGuidMap;
                if (guidMap == null || !guidMap.TryGetValue(packId, out var guid))
                    return null;

                var filePath = Path.Combine(_packsFolder, guid, "content", file.ObfuscatedName);
                if (!File.Exists(filePath)) return null;

                return PackEncryptionService.DecryptFileToStream(filePath);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to decrypt pack file: {Name}", file.OriginalName);
                return null;
            }
        }

        /// <summary>
        /// Gets a decrypted file path for temporary use (creates temp file).
        /// Used for video playback which requires file path.
        /// </summary>
        public string? GetPackFileTempPath(string packId, PackFileEntry file)
        {
            try
            {
                var guidMap = App.Settings.Current.PackGuidMap;
                if (guidMap == null || !guidMap.TryGetValue(packId, out var guid))
                    return null;

                var encryptedPath = Path.Combine(_packsFolder, guid, "content", file.ObfuscatedName);
                if (!File.Exists(encryptedPath)) return null;

                // Create temp file with correct extension (for codec detection)
                var tempPath = Path.Combine(Path.GetTempPath(), $"ccp_temp_{Guid.NewGuid():N}{file.Extension}");
                var decrypted = PackEncryptionService.DecryptFile(encryptedPath);
                File.WriteAllBytes(tempPath, decrypted);

                return tempPath;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to create temp file for pack: {Name}", file.OriginalName);
                return null;
            }
        }

        /// <summary>
        /// Checks if a pack is installed.
        /// </summary>
        public bool IsPackInstalled(string packId)
        {
            return App.Settings?.Current?.InstalledPackIds?.Contains(packId) ?? false;
        }

        /// <summary>
        /// Checks if a pack is active.
        /// </summary>
        public bool IsPackActive(string packId)
        {
            return App.Settings?.Current?.ActivePackIds?.Contains(packId) ?? false;
        }

        /// <summary>
        /// Gets all active packs.
        /// </summary>
        public List<string> GetActivePackIds()
        {
            return App.Settings?.Current?.ActivePackIds?.ToList() ?? new List<string>();
        }

        private void LoadInstalledManifests()
        {
            foreach (var packId in App.Settings?.Current?.InstalledPackIds ?? new List<string>())
            {
                LoadPackManifest(packId);
            }
        }

        private void LoadPackManifest(string packId)
        {
            try
            {
                var guidMap = App.Settings.Current.PackGuidMap;
                if (guidMap == null || !guidMap.TryGetValue(packId, out var guid))
                    return;

                var manifestPath = Path.Combine(_packsFolder, guid, ".manifest.enc");
                if (!File.Exists(manifestPath)) return;

                var json = PackEncryptionService.LoadEncryptedManifest(manifestPath);
                var manifest = JsonConvert.DeserializeObject<InstalledPackManifest>(json);

                if (manifest != null)
                {
                    _installedManifests[packId] = manifest;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to load manifest for pack: {Id}", packId);
            }
        }

        private void CleanupFailedInstall(string tempZipPath, string packFolder)
        {
            try
            {
                if (File.Exists(tempZipPath))
                    File.Delete(tempZipPath);

                if (Directory.Exists(packFolder))
                    Directory.Delete(packFolder, true);

                // Clean up any temp extract folders
                var tempFolders = Directory.GetDirectories(_packsFolder, ".*_extract");
                foreach (var folder in tempFolders)
                {
                    Directory.Delete(folder, true);
                }
            }
            catch { }
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
    /// Remote packs manifest structure.
    /// </summary>
    internal class PacksManifest
    {
        [JsonProperty("version")]
        public string Version { get; set; } = "1.0";

        [JsonProperty("packs")]
        public List<ContentPack> Packs { get; set; } = new();
    }

    /// <summary>
    /// Manifest for an installed pack (stored encrypted locally).
    /// </summary>
    public class InstalledPackManifest
    {
        public string PackId { get; set; } = "";
        public string PackGuid { get; set; } = "";
        public string PackName { get; set; } = "";
        public DateTime InstalledDate { get; set; }
        public List<PackFileEntry> Files { get; set; } = new();
    }

    /// <summary>
    /// Entry for a file in an installed pack.
    /// </summary>
    public class PackFileEntry
    {
        public string OriginalName { get; set; } = "";
        public string ObfuscatedName { get; set; } = "";
        public string FileType { get; set; } = ""; // "image" or "video"
        public string Extension { get; set; } = "";
    }
}
