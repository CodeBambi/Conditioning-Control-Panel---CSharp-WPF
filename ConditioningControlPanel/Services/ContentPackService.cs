using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
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

        // Server-controlled packs manifest (allows enable/disable without app update)
        private const string PacksManifestUrl = "https://codebambi-proxy.vercel.app/packs/manifest";

        // Proxy server URL for authenticated downloads
        private const string ProxyBaseUrl = "https://codebambi-proxy.vercel.app";

        // Built-in packs definition (shown even if manifest fetch fails)
        // Download URLs point to private CDN - update these when hosting is configured
        private static readonly List<ContentPack> BuiltInPacks = new()
        {
            new ContentPack
            {
                Id = "basic-bimbo-starter",
                Name = "Basic Bimbo Starter Pack",
                Description = "Essential images and videos to begin your bimbo journey. A curated collection perfect for newcomers!",
                Author = "CodeBambi",
                Version = "1.0.0",
                ImageCount = 113,
                VideoCount = 7,
                SizeBytes = 2_397_264_867, // 2.23 GB
                DownloadUrl = "https://ccp-packs.b-cdn.net/Basic%20Bimbo%20Starter%20Pack.zip",
                PreviewImageUrl = "",
                PatreonUrl = "",
                UpgradeUrl = ""
            },
            new ContentPack
            {
                Id = "enhanced-bimbodoll-video",
                Name = "Enhanced Bimbodoll Video Pack",
                Description = "Premium video collection for experienced users. High-quality hypno videos and exclusive content.",
                Author = "CodeBambi",
                Version = "1.0.0",
                ImageCount = 0,
                VideoCount = 27,
                SizeBytes = 4_392_954_093, // 4.09 GB
                DownloadUrl = "https://ccp-packs.b-cdn.net/Enhanced%20Bimbodoll%20video%20pack.zip",
                PreviewImageUrl = "",
                PatreonUrl = "https://patreon.com/CodeBambi",
                UpgradeUrl = ""
            }
        };

        public event EventHandler<ContentPack>? PackDownloadStarted;
        public event EventHandler<ContentPack>? PackDownloadCompleted;
        public event EventHandler<(ContentPack Pack, int Progress)>? PackDownloadProgress;
        public event EventHandler<(ContentPack Pack, string Status)>? PackInstallStatus;
        public event EventHandler<ContentPack>? PackInstallFailed;
        public event EventHandler<string>? AuthenticationRequired;
        public event EventHandler<(ContentPack Pack, string Message, DateTime ResetTime)>? RateLimitExceeded;

        public ContentPackService()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromMinutes(30); // Allow long downloads for large packs

            // Use hidden folder in user's chosen assets folder (where they want heavy files)
            _packsFolder = Path.Combine(App.EffectiveAssetsPath, ".packs");
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
        /// Requires Patreon authentication.
        /// </summary>
        public async Task InstallPackAsync(ContentPack pack, IProgress<int>? progress = null)
        {
            if (string.IsNullOrEmpty(pack.Id))
            {
                throw new InvalidOperationException("Pack has no ID");
            }

            // Check if user is authenticated with Patreon
            if (App.Patreon == null || !App.Patreon.IsAuthenticated)
            {
                AuthenticationRequired?.Invoke(this, "Please log in with Patreon to download content packs.");
                throw new UnauthorizedAccessException("Patreon authentication required to download packs");
            }

            var accessToken = App.Patreon.GetAccessToken();
            if (string.IsNullOrEmpty(accessToken))
            {
                AuthenticationRequired?.Invoke(this, "Your Patreon session has expired. Please log in again.");
                throw new UnauthorizedAccessException("Patreon access token not available");
            }

            // Get signed download URL from proxy server
            string downloadUrl;
            try
            {
                downloadUrl = await GetSignedDownloadUrlAsync(pack.Id, accessToken);
            }
            catch (PackRateLimitException ex)
            {
                RateLimitExceeded?.Invoke(this, (pack, ex.Message, ex.ResetTime));
                throw;
            }
            catch (UnauthorizedAccessException)
            {
                AuthenticationRequired?.Invoke(this, "Your Patreon session has expired. Please log in again.");
                throw;
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

                // Download ZIP file from signed URL
                using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
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

                // Ensure we show 100% briefly before switching to extracting
                pack.DownloadProgress = 100;
                PackDownloadProgress?.Invoke(this, (pack, 100));

                // Small delay so user sees 100% before status change
                await Task.Delay(200);

                // Update status - extracting (this hides progress bar)
                PackInstallStatus?.Invoke(this, (pack, "Extracting..."));

                // Extract to temp folder first (run on background thread to not block UI)
                var tempExtractPath = Path.Combine(_packsFolder, $".{packGuid}_extract");
                await Task.Run(() => ZipFile.ExtractToDirectory(tempZipPath, tempExtractPath));

                // Create encrypted pack structure
                Directory.CreateDirectory(packFolder);
                var contentFolder = Path.Combine(packFolder, "content");
                Directory.CreateDirectory(contentFolder);

                // Count total files for progress
                var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" };
                var videoExtensions = new[] { ".mp4", ".webm", ".mkv", ".avi", ".mov", ".wmv" };

                // Find images and videos folders (may be nested if ZIP has root folder)
                var imagesPath = FindSubfolder(tempExtractPath, "images");
                var videosPath = FindSubfolder(tempExtractPath, "videos");

                App.Logger?.Debug("Pack extract paths - images: {Images}, videos: {Videos}",
                    imagesPath ?? "not found", videosPath ?? "not found");

                var imageFiles = imagesPath != null && Directory.Exists(imagesPath)
                    ? Directory.GetFiles(imagesPath, "*", SearchOption.AllDirectories)
                        .Where(f => imageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant())).ToList()
                    : new List<string>();
                var videoFiles = videosPath != null && Directory.Exists(videosPath)
                    ? Directory.GetFiles(videosPath, "*", SearchOption.AllDirectories)
                        .Where(f => videoExtensions.Contains(Path.GetExtension(f).ToLowerInvariant())).ToList()
                    : new List<string>();

                App.Logger?.Debug("Pack files found - images: {ImageCount}, videos: {VideoCount}",
                    imageFiles.Count, videoFiles.Count);
                var totalFiles = imageFiles.Count + videoFiles.Count;
                var processedFiles = 0;

                // Process and encrypt files, building manifest
                var manifest = new InstalledPackManifest
                {
                    PackId = pack.Id,
                    PackGuid = packGuid,
                    PackName = pack.Name,
                    InstalledDate = DateTime.UtcNow,
                    Files = new List<PackFileEntry>()
                };

                // Update status - encrypting
                PackInstallStatus?.Invoke(this, (pack, $"Encrypting 0/{totalFiles}..."));

                // Process images
                if (imageFiles.Count > 0)
                {
                    await ProcessAndEncryptFilesWithProgressAsync(imageFiles, contentFolder, "image", manifest,
                        (current) => {
                            processedFiles = current;
                            PackInstallStatus?.Invoke(this, (pack, $"Encrypting {processedFiles}/{totalFiles}..."));
                        });
                }

                // Process videos
                if (videoFiles.Count > 0)
                {
                    var imageCount = imageFiles.Count;
                    await ProcessAndEncryptFilesWithProgressAsync(videoFiles, contentFolder, "video", manifest,
                        (current) => {
                            processedFiles = imageCount + current;
                            PackInstallStatus?.Invoke(this, (pack, $"Encrypting {processedFiles}/{totalFiles}..."));
                        });
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
        /// Gets a signed download URL from the proxy server.
        /// Requires Patreon authentication.
        /// </summary>
        private async Task<string> GetSignedDownloadUrlAsync(string packId, string accessToken)
        {
            var requestUrl = $"{ProxyBaseUrl}/pack/download-url";
            var requestBody = new { packId };
            var jsonContent = new StringContent(
                JsonConvert.SerializeObject(requestBody),
                Encoding.UTF8,
                "application/json");

            using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Content = jsonContent;

            using var response = await _httpClient.SendAsync(request);

            var responseJson = await response.Content.ReadAsStringAsync();

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                App.Logger?.Warning("Pack download auth failed: {Response}", responseJson);
                throw new UnauthorizedAccessException("Patreon authentication failed");
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var errorResponse = JsonConvert.DeserializeObject<PackDownloadErrorResponse>(responseJson);
                var resetTime = DateTime.TryParse(errorResponse?.ResetTime, out var parsed)
                    ? parsed
                    : DateTime.UtcNow.AddHours(24);
                App.Logger?.Warning("Pack download rate limited: {Message}", errorResponse?.Message);
                throw new PackRateLimitException(
                    errorResponse?.Message ?? "Download limit exceeded. Try again later.",
                    resetTime);
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorResponse = JsonConvert.DeserializeObject<PackDownloadErrorResponse>(responseJson);
                App.Logger?.Warning("Pack download failed: {Status} - {Message}", response.StatusCode, errorResponse?.Message);
                throw new Exception(errorResponse?.Message ?? $"Failed to get download URL: {response.StatusCode}");
            }

            var successResponse = JsonConvert.DeserializeObject<PackDownloadUrlResponse>(responseJson);
            if (string.IsNullOrEmpty(successResponse?.DownloadUrl))
            {
                throw new Exception("Server returned empty download URL");
            }

            App.Logger?.Information("Got signed download URL for pack: {PackId}, remaining downloads: {Remaining}",
                packId, successResponse.RateLimit?.Remaining ?? -1);

            return successResponse.DownloadUrl;
        }

        /// <summary>
        /// Gets the download status for all packs (rate limits).
        /// </summary>
        public async Task<Dictionary<string, PackDownloadStatus>?> GetPackDownloadStatusAsync()
        {
            var status = await GetFullPackStatusAsync();
            return status?.Packs;
        }

        /// <summary>
        /// Gets the full pack status including bandwidth from the server.
        /// </summary>
        public async Task<PackStatusResponse?> GetFullPackStatusAsync()
        {
            if (App.Patreon == null || !App.Patreon.IsAuthenticated)
            {
                return null;
            }

            var accessToken = App.Patreon.GetAccessToken();
            if (string.IsNullOrEmpty(accessToken))
            {
                return null;
            }

            try
            {
                var requestUrl = $"{ProxyBaseUrl}/pack/status";
                using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                using var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var responseJson = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<PackStatusResponse>(responseJson);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to get pack status: {Error}", ex.Message);
                return null;
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
        /// Processes a list of files, encrypts them, and reports progress.
        /// </summary>
        private async Task ProcessAndEncryptFilesWithProgressAsync(List<string> files, string destFolder,
            string fileType, InstalledPackManifest manifest, Action<int> onProgress)
        {
            var processed = 0;
            foreach (var file in files)
            {
                var originalName = Path.GetFileName(file);
                var obfuscatedName = PackEncryptionService.GenerateObfuscatedFilename() + ".enc";
                var destPath = Path.Combine(destFolder, obfuscatedName);

                // Encrypt file on background thread
                await Task.Run(() => PackEncryptionService.EncryptFile(file, destPath));

                // Add to manifest
                manifest.Files.Add(new PackFileEntry
                {
                    OriginalName = originalName,
                    ObfuscatedName = obfuscatedName,
                    FileType = fileType,
                    Extension = Path.GetExtension(file).ToLowerInvariant()
                });

                processed++;
                onProgress?.Invoke(processed);
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

        /// <summary>
        /// Gets all video files from all active packs.
        /// Returns tuples of (packId, PackFileEntry) for each video.
        /// </summary>
        public List<(string PackId, PackFileEntry File)> GetAllActivePackVideos()
        {
            var result = new List<(string, PackFileEntry)>();
            foreach (var packId in GetActivePackIds())
            {
                var videos = GetPackFiles(packId, "video");
                foreach (var video in videos)
                {
                    result.Add((packId, video));
                }
            }
            return result;
        }

        /// <summary>
        /// Gets all image files from all active packs.
        /// Returns tuples of (packId, PackFileEntry) for each image.
        /// </summary>
        public List<(string PackId, PackFileEntry File)> GetAllActivePackImages()
        {
            var result = new List<(string, PackFileEntry)>();
            foreach (var packId in GetActivePackIds())
            {
                var images = GetPackFiles(packId, "image");
                foreach (var image in images)
                {
                    result.Add((packId, image));
                }
            }
            return result;
        }

        /// <summary>
        /// Gets cached preview images for a pack's rotating thumbnail.
        /// Returns up to 10 random images from the installed pack.
        /// Caches selection in .preview-cache.json for consistency.
        /// </summary>
        public List<BitmapImage> GetPackPreviewImages(string packId, int count = 10, int width = 240, int height = 100)
        {
            var result = new List<BitmapImage>();

            if (!IsPackInstalled(packId))
                return result;

            try
            {
                var guidMap = App.Settings.Current.PackGuidMap;
                if (guidMap == null || !guidMap.TryGetValue(packId, out var guid))
                    return result;

                var packFolder = Path.Combine(_packsFolder, guid);
                var cacheFile = Path.Combine(packFolder, ".preview-cache.json");

                List<string>? cachedNames = null;

                // Try to load cached selection
                if (File.Exists(cacheFile))
                {
                    try
                    {
                        var cacheJson = File.ReadAllText(cacheFile);
                        cachedNames = JsonConvert.DeserializeObject<List<string>>(cacheJson);
                    }
                    catch
                    {
                        // Cache corrupted, will regenerate
                    }
                }

                // Get all image files from pack
                var allImages = GetPackFiles(packId, "image");
                if (allImages.Count == 0)
                    return result;

                List<PackFileEntry> selectedFiles = new();
                bool needsNewSelection = true;

                if (cachedNames != null && cachedNames.Count > 0)
                {
                    // Use cached selection (find entries by obfuscated name)
                    selectedFiles = allImages
                        .Where(f => cachedNames.Contains(f.ObfuscatedName))
                        .ToList();

                    // If cache has valid entries, use them
                    if (selectedFiles.Count > 0)
                        needsNewSelection = false;
                }

                if (needsNewSelection)
                {
                    // Select random images
                    var random = new Random();
                    selectedFiles = allImages
                        .OrderBy(_ => random.Next())
                        .Take(count)
                        .ToList();

                    // Cache the selection
                    try
                    {
                        var namesToCache = selectedFiles.Select(f => f.ObfuscatedName).ToList();
                        File.WriteAllText(cacheFile, JsonConvert.SerializeObject(namesToCache));
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Debug("Failed to cache preview selection: {Error}", ex.Message);
                    }
                }

                // Load and decrypt images as thumbnails
                foreach (var file in selectedFiles)
                {
                    try
                    {
                        var bitmap = GetPackFileThumbnail(packId, file, width, height);
                        if (bitmap != null)
                        {
                            result.Add(bitmap);
                        }
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Debug("Failed to load preview image {Name}: {Error}", file.OriginalName, ex.Message);
                    }
                }

                App.Logger?.Debug("Loaded {Count} preview images for pack {PackId}", result.Count, packId);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to get preview images for pack: {PackId}", packId);
            }

            return result;
        }

        /// <summary>
        /// Clears the cached preview image selection for a pack.
        /// Call this after pack update to refresh previews.
        /// </summary>
        public void ClearPreviewCache(string packId)
        {
            try
            {
                var guidMap = App.Settings.Current.PackGuidMap;
                if (guidMap == null || !guidMap.TryGetValue(packId, out var guid))
                    return;

                var cacheFile = Path.Combine(_packsFolder, guid, ".preview-cache.json");
                if (File.Exists(cacheFile))
                {
                    File.Delete(cacheFile);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to clear preview cache: {Error}", ex.Message);
            }
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

        /// <summary>
        /// Finds a subfolder by name anywhere in the directory tree.
        /// Handles ZIP files that have a root folder containing the images/videos folders.
        /// </summary>
        private static string? FindSubfolder(string rootPath, string folderName)
        {
            // First check direct child
            var directPath = Path.Combine(rootPath, folderName);
            if (Directory.Exists(directPath))
                return directPath;

            // Search recursively (handles ZIPs with root folder like "PackName/videos/")
            try
            {
                var dirs = Directory.GetDirectories(rootPath, folderName, SearchOption.AllDirectories);
                return dirs.FirstOrDefault();
            }
            catch
            {
                return null;
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

    /// <summary>
    /// Response from POST /pack/download-url endpoint.
    /// </summary>
    internal class PackDownloadUrlResponse
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("downloadUrl")]
        public string? DownloadUrl { get; set; }

        [JsonProperty("packId")]
        public string? PackId { get; set; }

        [JsonProperty("packName")]
        public string? PackName { get; set; }

        [JsonProperty("sizeBytes")]
        public long SizeBytes { get; set; }

        [JsonProperty("expiresIn")]
        public int ExpiresIn { get; set; }

        [JsonProperty("rateLimit")]
        public PackRateLimitInfo? RateLimit { get; set; }

        [JsonProperty("bandwidth")]
        public BandwidthStatus? Bandwidth { get; set; }
    }

    /// <summary>
    /// Rate limit info from server response.
    /// </summary>
    internal class PackRateLimitInfo
    {
        [JsonProperty("remaining")]
        public int Remaining { get; set; }

        [JsonProperty("limit")]
        public int Limit { get; set; }

        [JsonProperty("resetTime")]
        public string? ResetTime { get; set; }
    }

    /// <summary>
    /// Error response from pack download endpoints.
    /// </summary>
    internal class PackDownloadErrorResponse
    {
        [JsonProperty("error")]
        public string? Error { get; set; }

        [JsonProperty("message")]
        public string? Message { get; set; }

        [JsonProperty("resetTime")]
        public string? ResetTime { get; set; }

        [JsonProperty("remaining")]
        public int Remaining { get; set; }
    }

    /// <summary>
    /// Response from GET /pack/status endpoint.
    /// </summary>
    public class PackStatusResponse
    {
        [JsonProperty("userId")]
        public string? UserId { get; set; }

        [JsonProperty("packs")]
        public Dictionary<string, PackDownloadStatus>? Packs { get; set; }

        [JsonProperty("dailyLimit")]
        public int DailyLimit { get; set; }

        [JsonProperty("bandwidth")]
        public BandwidthStatus? Bandwidth { get; set; }
    }

    /// <summary>
    /// Bandwidth usage status.
    /// </summary>
    public class BandwidthStatus
    {
        [JsonProperty("usedBytes")]
        public long UsedBytes { get; set; }

        [JsonProperty("limitBytes")]
        public long LimitBytes { get; set; }

        [JsonProperty("remainingBytes")]
        public long RemainingBytes { get; set; }

        [JsonProperty("usedGB")]
        public string? UsedGB { get; set; }

        [JsonProperty("limitGB")]
        public double LimitGB { get; set; }

        [JsonProperty("isPatreon")]
        public bool IsPatreon { get; set; }
    }

    /// <summary>
    /// Download status for a single pack.
    /// </summary>
    public class PackDownloadStatus
    {
        [JsonProperty("name")]
        public string? Name { get; set; }

        [JsonProperty("sizeBytes")]
        public long SizeBytes { get; set; }

        [JsonProperty("canDownload")]
        public bool CanDownload { get; set; }

        [JsonProperty("downloadsRemaining")]
        public int DownloadsRemaining { get; set; }

        [JsonProperty("downloadsUsed")]
        public int DownloadsUsed { get; set; }

        [JsonProperty("resetTime")]
        public string? ResetTime { get; set; }
    }

    /// <summary>
    /// Exception thrown when pack download rate limit is exceeded.
    /// </summary>
    public class PackRateLimitException : Exception
    {
        public DateTime ResetTime { get; }

        public PackRateLimitException(string message, DateTime resetTime)
            : base(message)
        {
            ResetTime = resetTime;
        }
    }
}
