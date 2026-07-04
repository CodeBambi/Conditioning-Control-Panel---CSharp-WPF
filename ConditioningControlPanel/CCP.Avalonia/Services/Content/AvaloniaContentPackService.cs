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
using Avalonia.Media.Imaging;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Services.Auth;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services;
using ConditioningControlPanel.Core.Services.Content;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Avalonia.Services.Content;

/// <summary>
/// Avalonia implementation of <see cref="IContentPackService"/>.
/// Ports the legacy WPF ContentPackService to DI while preserving behavior.
/// </summary>
public sealed class AvaloniaContentPackService : IContentPackService
{
    private readonly HttpClient _httpClient;
    private readonly IAppEnvironment _appEnvironment;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<AvaloniaContentPackService>? _logger;
    private readonly AvaloniaPatreonProvider _patreonProvider;
    private readonly AvaloniaDiscordProvider _discordProvider;

    private string _packsFolder;
    private string _manifestCachePath;
    private string _mediaTempPath;
    private List<ContentPack> _availablePacks = new();
    private readonly Dictionary<string, InstalledPackManifest> _installedManifests = new();
    private bool _disposed;

    public string PacksFolder => _packsFolder;

    public IReadOnlyCollection<string> InstalledPacks => _installedManifests.Keys;

    public event EventHandler<ContentPack>? PackDownloadStarted;
    public event EventHandler<ContentPack>? PackDownloadCompleted;
    public event EventHandler<(ContentPack Pack, int Progress)>? PackDownloadProgress;
    public event EventHandler<(ContentPack Pack, string Status)>? PackInstallStatus;
    public event EventHandler<ContentPack>? PackInstallFailed;
    public event EventHandler<string>? AuthenticationRequired;
    public event EventHandler<(ContentPack Pack, string Message, DateTime ResetTime)>? RateLimitExceeded;

    public AvaloniaContentPackService(
        IAppEnvironment appEnvironment,
        ISettingsService settingsService,
        ILogger<AvaloniaContentPackService>? logger,
        AvaloniaPatreonProvider patreonProvider,
        AvaloniaDiscordProvider discordProvider)
    {
        _appEnvironment = appEnvironment ?? throw new ArgumentNullException(nameof(appEnvironment));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _logger = logger;
        _patreonProvider = patreonProvider ?? throw new ArgumentNullException(nameof(patreonProvider));
        _discordProvider = discordProvider ?? throw new ArgumentNullException(nameof(discordProvider));

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(AuthConstants.ProxyBaseUrl),
            Timeout = TimeSpan.FromMinutes(30)
        };
        _httpClient.DefaultRequestHeaders.Add("X-Client-Version", AuthConstants.ClientVersion);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"ConditioningControlPanel/{AuthConstants.ClientVersion}");

        _packsFolder = Path.Combine(_appEnvironment.EffectiveAssetsPath, ".packs");
        _manifestCachePath = Path.Combine(_packsFolder, ".manifest_cache.enc");
        _mediaTempPath = Path.Combine(_appEnvironment.UserDataPath, "media_tmp");

        EnsurePacksFolder();
        EnsureMediaTempPath();

        ScanAndRegisterOrphanedPacks();
        LoadInstalledManifests();
    }

    public void RefreshPacksPath()
    {
        var oldPacksFolder = _packsFolder;
        var newPacksFolder = Path.Combine(_appEnvironment.EffectiveAssetsPath, ".packs");

        if (string.Equals(oldPacksFolder, newPacksFolder, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _packsFolder = newPacksFolder;
        _manifestCachePath = Path.Combine(_packsFolder, ".manifest_cache.enc");

        EnsurePacksFolder();
        ScanAndRegisterOrphanedPacks();

        _installedManifests.Clear();
        LoadInstalledManifests();

        _logger?.LogInformation("ContentPackService: Packs path refreshed from {OldPath} to {NewPath}", oldPacksFolder, _packsFolder);
    }

    /// <summary>
    /// Create <see cref="_packsFolder"/> as a hidden directory, swallowing the failure modes
    /// that can occur on a user-chosen custom assets path (denied ACL, read-only/offline drive).
    /// Never throws: this runs from the DI-singleton constructor at startup, so a raw
    /// <see cref="Directory.CreateDirectory"/> here would hard-crash the app before any window
    /// shows. Callers must tolerate the folder being absent afterwards (scan/load paths already
    /// guard on <see cref="Directory.Exists"/>; downloads/installs fail-soft later). Mirrors WPF
    /// ContentPackService.TryCreateHiddenPacksFolder (crash reports #450/#451/#452).
    /// </summary>
    private void EnsurePacksFolder()
    {
        try
        {
            if (!Directory.Exists(_packsFolder))
            {
                var di = Directory.CreateDirectory(_packsFolder);
                di.Attributes |= FileAttributes.Hidden;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "ContentPackService: could not create packs folder {Path} - content packs disabled this session", _packsFolder);
        }
    }

    /// <summary>
    /// Create the decrypted-media scratch directory. Never throws for the same reason as
    /// <see cref="EnsurePacksFolder"/>: if the directory can't be made, pack media playback
    /// fails-soft later (the write path in CreateTempFileForPack is already try/caught).
    /// </summary>
    private void EnsureMediaTempPath()
    {
        try
        {
            if (!Directory.Exists(_mediaTempPath))
            {
                Directory.CreateDirectory(_mediaTempPath);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "ContentPackService: could not create media temp folder {Path} - pack media playback disabled this session", _mediaTempPath);
        }
    }

    /// <summary>
    /// Crash-recovery sweep of decrypted pack media (and haptic scratch videos) left behind by
    /// a session that terminated abnormally. Per-session cleanup handles the happy path; this
    /// covers crashes so decrypted plaintext adult content never lingers on disk. Targets the
    /// same directory the decryptor writes to (<c>UserDataPath/media_tmp</c>, see
    /// CreateTempFileForPack). Best-effort: swallows per-file and top-level failures so it can
    /// safely run once at startup. Mirrors WPF App.CleanupStaleTempFiles.
    /// </summary>
    public static void CleanupStaleTempFiles(string userDataPath)
    {
        if (string.IsNullOrEmpty(userDataPath))
            return;

        try
        {
            var tempDir = Path.Combine(userDataPath, "media_tmp");
            if (!Directory.Exists(tempDir))
                return;

            foreach (var file in Directory.GetFiles(tempDir, "ccp_temp_*"))
            {
                try { File.Delete(file); }
                catch { /* best-effort: file may be locked or already gone */ }
            }
            foreach (var file in Directory.GetFiles(tempDir, "haptic_video_*"))
            {
                try { File.Delete(file); }
                catch { /* best-effort */ }
            }
        }
        catch { /* never throw at startup */ }
    }

    private void ScanAndRegisterOrphanedPacks()
    {
        if (!Directory.Exists(_packsFolder))
            return;

        try
        {
            var registeredCount = 0;
            var settings = _settingsService.Current;

            foreach (var dir in Directory.GetDirectories(_packsFolder))
            {
                var guid = Path.GetFileName(dir);
                var manifestPath = Path.Combine(dir, ".manifest.enc");

                if (!File.Exists(manifestPath))
                    continue;

                try
                {
                    var json = PackEncryptionService.LoadEncryptedManifest(manifestPath);
                    var manifest = JsonConvert.DeserializeObject<InstalledPackManifest>(json);
                    var packId = manifest?.PackId;

                    if (string.IsNullOrEmpty(packId))
                        continue;

                    var isInSettings = settings.InstalledPackIds.Contains(packId);
                    var hasCorrectGuid = settings.PackGuidMap?.TryGetValue(packId, out var existingGuid) == true
                        && string.Equals(existingGuid, guid, StringComparison.OrdinalIgnoreCase);

                    if (isInSettings && hasCorrectGuid)
                        continue;

                    settings.InstalledPackIds ??= new List<string>();
                    settings.PackGuidMap ??= new Dictionary<string, string>();
                    settings.ActivePackIds ??= new List<string>();

                    if (!settings.InstalledPackIds.Contains(packId))
                    {
                        settings.InstalledPackIds.Add(packId);
                    }

                    settings.PackGuidMap[packId] = guid;

                    if (!settings.ActivePackIds.Contains(packId))
                    {
                        settings.ActivePackIds.Add(packId);
                    }

                    registeredCount++;
                    _logger?.LogInformation("Registered orphaned pack: {PackId} ({PackName}) -> {Guid}",
                        packId, manifest?.PackName ?? "Unknown", guid);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to read manifest for potential orphaned pack: {Path}", manifestPath);
                }
            }

            if (registeredCount > 0)
            {
                _settingsService.Save();
                _logger?.LogInformation("Registered {Count} orphaned pack(s) found in {Path}", registeredCount, _packsFolder);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to scan for orphaned packs in {Path}", _packsFolder);
        }
    }

    public async Task<List<ContentPack>> GetAvailablePacksAsync()
    {
        var settings = _settingsService.Current;

        if (settings.OfflineMode)
        {
            _logger?.LogDebug("Offline mode enabled, using built-in packs only");
            return GetBuiltInPacks();
        }

        try
        {
            var response = await _httpClient.GetStringAsync("/packs/manifest");
            var manifest = JsonConvert.DeserializeObject<PacksManifest>(response);

            if (manifest?.Packs?.Count > 0)
            {
                _availablePacks = manifest.Packs;
                _logger?.LogInformation("Fetched {Count} packs from remote manifest", _availablePacks.Count);
            }
            else
            {
                _availablePacks = new List<ContentPack>(BuiltInPacks);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug("Could not fetch remote packs manifest: {Error}, using built-in", ex.Message);
            _availablePacks = new List<ContentPack>(BuiltInPacks);
        }

        foreach (var pack in _availablePacks)
        {
            pack.IsDownloaded = IsPackInstalled(pack.Id);
            pack.IsActive = IsPackActive(pack.Id);
        }

        return _availablePacks;
    }

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

    public async Task InstallPackAsync(ContentPack pack, IProgress<int>? progress = null)
    {
        var settings = _settingsService.Current;

        if (settings.OfflineMode)
        {
            _logger?.LogInformation("Offline mode enabled, pack download blocked");
            PackInstallFailed?.Invoke(this, pack);
            throw new InvalidOperationException("Cannot download packs in offline mode");
        }

        if (string.IsNullOrEmpty(pack.Id))
        {
            throw new InvalidOperationException("Pack has no ID");
        }

        if (!_patreonProvider.IsAuthenticated)
        {
            AuthenticationRequired?.Invoke(this, "Please log in with Patreon to download content packs.\nA free Patreon account gives you 10 GB/month — no payment needed!");
            throw new UnauthorizedAccessException("Patreon authentication required to download packs");
        }

        var accessToken = _patreonProvider.GetAccessToken();
        if (string.IsNullOrEmpty(accessToken))
        {
            AuthenticationRequired?.Invoke(this, "Your Patreon session has expired. Please log in again.");
            throw new UnauthorizedAccessException("Patreon access token not available");
        }

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

        var packGuid = Guid.NewGuid().ToString("N");
        var packFolder = Path.Combine(_packsFolder, packGuid);
        var tempZipPath = Path.Combine(_packsFolder, $".{packGuid}_temp.zip");

        try
        {
            pack.IsDownloading = true;
            PackDownloadStarted?.Invoke(this, pack);
            _logger?.LogInformation("Starting download of pack: {Name}", pack.Name);

            var maxRetries = 10;
            var retryDelay = TimeSpan.FromSeconds(3);
            var totalBytes = pack.SizeBytes;
            var downloadComplete = false;

            for (int attempt = 1; attempt <= maxRetries && !downloadComplete; attempt++)
            {
                try
                {
                    long resumeFromByte = 0;
                    if (File.Exists(tempZipPath))
                    {
                        resumeFromByte = new FileInfo(tempZipPath).Length;
                        if (resumeFromByte > 0)
                        {
                            _logger?.LogInformation("Resuming download from byte {Byte} (attempt {Attempt})", resumeFromByte, attempt);
                        }
                    }

                    using var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
                    if (resumeFromByte > 0)
                    {
                        request.Headers.Range = new RangeHeaderValue(resumeFromByte, null);
                    }

                    using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

                    if (response.StatusCode != HttpStatusCode.OK && response.StatusCode != HttpStatusCode.PartialContent)
                    {
                        response.EnsureSuccessStatusCode();
                    }

                    if (response.Content.Headers.ContentRange?.Length != null)
                    {
                        totalBytes = response.Content.Headers.ContentRange.Length.Value;
                    }
                    else if (response.Content.Headers.ContentLength != null)
                    {
                        totalBytes = resumeFromByte + response.Content.Headers.ContentLength.Value;
                    }

                    var fileMode = resumeFromByte > 0 ? FileMode.Append : FileMode.Create;

                    using (var contentStream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(tempZipPath, fileMode, FileAccess.Write, FileShare.None))
                    {
                        var buffer = new byte[65536];
                        int bytesRead;
                        var downloadedThisSession = resumeFromByte;

                        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            downloadedThisSession += bytesRead;

                            if (totalBytes > 0)
                            {
                                var progressPercent = (int)(downloadedThisSession * 100 / totalBytes);
                                progress?.Report(progressPercent);
                                pack.DownloadProgress = progressPercent;
                                PackDownloadProgress?.Invoke(this, (pack, progressPercent));
                            }
                        }
                    }

                    var finalSize = new FileInfo(tempZipPath).Length;
                    if (totalBytes > 0 && finalSize < totalBytes * 0.99)
                    {
                        _logger?.LogWarning("Download incomplete: got {Got} of {Expected} bytes", finalSize, totalBytes);
                        throw new IOException($"Download incomplete: received {finalSize} of {totalBytes} bytes");
                    }

                    downloadComplete = true;
                    _logger?.LogInformation("Download completed successfully: {Bytes} bytes", finalSize);
                }
                catch (Exception ex) when (attempt < maxRetries && (ex is HttpRequestException || ex is TaskCanceledException || ex is IOException))
                {
                    var innerMsg = ex.InnerException?.Message ?? "none";
                    var currentSize = File.Exists(tempZipPath) ? new FileInfo(tempZipPath).Length : 0;
                    var pct = totalBytes > 0 ? (currentSize * 100 / totalBytes) : 0;

                    _logger?.LogWarning("Download attempt {Attempt}/{Max} failed at {Pct}% ({Bytes} bytes): {Error} (Inner: {Inner})",
                        attempt, maxRetries, pct, currentSize, ex.Message, innerMsg);

                    PackInstallStatus?.Invoke(this, (pack, $"Connection lost at {pct}%, resuming..."));
                    await Task.Delay(retryDelay);
                    retryDelay = TimeSpan.FromSeconds(Math.Min(retryDelay.TotalSeconds * 1.5, 30));
                }
            }

            var finalFileSize = File.Exists(tempZipPath) ? new FileInfo(tempZipPath).Length : 0;
            if (finalFileSize < 1000)
            {
                throw new IOException($"Download failed after {maxRetries} attempts. File is missing or incomplete.");
            }

            _logger?.LogDebug("Download complete ({Bytes} bytes), extracting and encrypting...", finalFileSize);

            pack.DownloadProgress = 100;
            PackDownloadProgress?.Invoke(this, (pack, 100));
            await Task.Delay(200);
            PackInstallStatus?.Invoke(this, (pack, "Extracting..."));

            var tempExtractPath = Path.Combine(_packsFolder, $".{packGuid}_extract");
            await Task.Run(() => ZipFile.ExtractToDirectory(tempZipPath, tempExtractPath));

            Directory.CreateDirectory(packFolder);
            var contentFolder = Path.Combine(packFolder, "content");
            Directory.CreateDirectory(contentFolder);

            var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" };
            var videoExtensions = new[] { ".mp4", ".webm", ".mkv", ".avi", ".mov", ".wmv" };

            var imagesPath = FindSubfolder(tempExtractPath, "images");
            var videosPath = FindSubfolder(tempExtractPath, "videos");

            _logger?.LogDebug("Pack extract paths - images: {Images}, videos: {Videos}",
                imagesPath ?? "not found", videosPath ?? "not found");

            var imageFiles = imagesPath != null && Directory.Exists(imagesPath)
                ? Directory.GetFiles(imagesPath, "*", SearchOption.AllDirectories)
                    .Where(f => imageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant())).ToList()
                : new List<string>();
            var videoFiles = videosPath != null && Directory.Exists(videosPath)
                ? Directory.GetFiles(videosPath, "*", SearchOption.AllDirectories)
                    .Where(f => videoExtensions.Contains(Path.GetExtension(f).ToLowerInvariant())).ToList()
                : new List<string>();

            _logger?.LogDebug("Pack files found - images: {ImageCount}, videos: {VideoCount}",
                imageFiles.Count, videoFiles.Count);

            var totalFiles = imageFiles.Count + videoFiles.Count;
            var processedFiles = 0;

            var manifest = new InstalledPackManifest
            {
                PackId = pack.Id,
                PackGuid = packGuid,
                PackName = pack.Name,
                InstalledDate = DateTime.UtcNow,
                Files = new List<PackFileEntry>()
            };

            PackInstallStatus?.Invoke(this, (pack, $"Encrypting 0/{totalFiles}..."));

            if (imageFiles.Count > 0)
            {
                await ProcessAndEncryptFilesWithProgressAsync(imageFiles, contentFolder, "image", manifest,
                    (current) => {
                        processedFiles = current;
                        PackInstallStatus?.Invoke(this, (pack, $"Encrypting {processedFiles}/{totalFiles}..."));
                    });
            }

            if (videoFiles.Count > 0)
            {
                var imageCount = imageFiles.Count;
                await ProcessAndEncryptFilesWithProgressAsync(videoFiles, contentFolder, "video", manifest,
                    (current) => {
                        processedFiles = imageCount + current;
                        PackInstallStatus?.Invoke(this, (pack, $"Encrypting {processedFiles}/{totalFiles}..."));
                    });
            }

            var manifestJson = JsonConvert.SerializeObject(manifest, Formatting.Indented);
            var manifestPath = Path.Combine(packFolder, ".manifest.enc");
            PackEncryptionService.SaveEncryptedManifest(manifestJson, manifestPath);

            File.Delete(tempZipPath);
            Directory.Delete(tempExtractPath, true);

            new DirectoryInfo(packFolder).Attributes |= FileAttributes.Hidden;

            if (!settings.InstalledPackIds.Contains(pack.Id))
            {
                settings.InstalledPackIds.Add(pack.Id);
            }

            settings.PackGuidMap ??= new Dictionary<string, string>();
            settings.PackGuidMap[pack.Id] = packGuid;
            _settingsService.Save();

            _installedManifests[pack.Id] = manifest;

            pack.IsDownloaded = true;
            pack.IsDownloading = false;
            pack.DownloadProgress = 100;

            _logger?.LogInformation("Pack installed successfully: {Name} ({FileCount} files encrypted)",
                pack.Name, manifest.Files.Count);
            PackDownloadCompleted?.Invoke(this, pack);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to install pack: {Name}", pack.Name);
            pack.IsDownloading = false;
            pack.DownloadProgress = 0;

            CleanupFailedInstall(tempZipPath, packFolder);

            PackInstallFailed?.Invoke(this, pack);
            throw;
        }
    }

    private async Task<string> GetSignedDownloadUrlAsync(string packId, string accessToken)
    {
        var requestUrl = "/pack/download-url";
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
            _logger?.LogWarning("Pack download auth failed: {Response}", responseJson);
            throw new UnauthorizedAccessException("Patreon authentication failed");
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var errorResponse = JsonConvert.DeserializeObject<PackDownloadErrorResponse>(responseJson);
            var resetTime = DateTime.TryParse(errorResponse?.ResetTime, out var parsed)
                ? parsed
                : DateTime.UtcNow.AddHours(24);
            _logger?.LogWarning("Pack download rate limited: {Message}", errorResponse?.Message);
            throw new PackRateLimitException(
                errorResponse?.Message ?? "Download limit exceeded. Try again later.",
                resetTime);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorResponse = JsonConvert.DeserializeObject<PackDownloadErrorResponse>(responseJson);
            _logger?.LogWarning("Pack download failed: {Status} - {Message}", response.StatusCode, errorResponse?.Message);
            throw new Exception(errorResponse?.Message ?? $"Failed to get download URL: {response.StatusCode}");
        }

        var successResponse = JsonConvert.DeserializeObject<PackDownloadUrlResponse>(responseJson);
        if (string.IsNullOrEmpty(successResponse?.DownloadUrl))
        {
            throw new Exception("Server returned empty download URL");
        }

        _logger?.LogInformation("Got signed download URL for pack: {PackId}, remaining downloads: {Remaining}",
            packId, successResponse.RateLimit?.Remaining ?? -1);

        return successResponse.DownloadUrl;
    }

    public async Task<string?> GetExternalPackDownloadUrlAsync(string packId)
    {
        var settings = _settingsService.Current;
        var unifiedId = settings.UnifiedId;
        var authToken = settings.AuthToken;

        if (string.IsNullOrEmpty(unifiedId) || string.IsNullOrEmpty(authToken))
        {
            AuthenticationRequired?.Invoke(this, "Please log in to download content packs.");
            return null;
        }

        try
        {
            var requestUrl = "/pack/download-url";
            var requestBody = new { packId, unified_id = unifiedId };
            var jsonContent = new StringContent(
                JsonConvert.SerializeObject(requestBody),
                Encoding.UTF8,
                "application/json");

            using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
            request.Headers.Add("X-Auth-Token", authToken);
            request.Content = jsonContent;

            using var response = await _httpClient.SendAsync(request);
            var responseJson = await response.Content.ReadAsStringAsync();

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                _logger?.LogWarning("External pack download auth failed for pack {PackId}", packId);
                AuthenticationRequired?.Invoke(this, "Your session has expired. Please log in again.");
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorResponse = JsonConvert.DeserializeObject<PackDownloadErrorResponse>(responseJson);
                _logger?.LogWarning("External pack download failed: {Status} - {Message}", response.StatusCode, errorResponse?.Message);
                throw new Exception(errorResponse?.Message ?? $"Failed to get download URL: {response.StatusCode}");
            }

            var successResponse = JsonConvert.DeserializeObject<PackDownloadUrlResponse>(responseJson);
            if (string.IsNullOrEmpty(successResponse?.DownloadUrl))
            {
                throw new Exception("Server returned empty download URL");
            }

            _logger?.LogInformation("Got external download URL for pack: {PackId}", packId);
            return successResponse.DownloadUrl;
        }
        catch (Exception ex) when (ex is not UnauthorizedAccessException)
        {
            _logger?.LogError(ex, "Failed to get external pack download URL for {PackId}", packId);
            throw;
        }
    }

    public async Task<Dictionary<string, PackDownloadStatus>?> GetPackDownloadStatusAsync()
    {
        var status = await GetFullPackStatusAsync();
        return status?.Packs;
    }

    public async Task<PackStatusResponse?> GetFullPackStatusAsync()
    {
        if (_patreonProvider.IsAuthenticated)
        {
            var accessToken = _patreonProvider.GetAccessToken();
            if (!string.IsNullOrEmpty(accessToken))
            {
                try
                {
                    var requestUrl = "/pack/status";
                    using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                    using var response = await _httpClient.SendAsync(request);
                    if (response.IsSuccessStatusCode)
                    {
                        var responseJson = await response.Content.ReadAsStringAsync();
                        return JsonConvert.DeserializeObject<PackStatusResponse>(responseJson);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug("Failed to get pack status via Patreon: {Error}", ex.Message);
                }
            }
        }

        if (_discordProvider.IsAuthenticated)
        {
            var discordToken = _discordProvider.GetAccessToken();
            if (!string.IsNullOrEmpty(discordToken))
            {
                try
                {
                    var requestUrl = "/discord/pack/status";
                    using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", discordToken);

                    using var response = await _httpClient.SendAsync(request);
                    if (response.IsSuccessStatusCode)
                    {
                        var responseJson = await response.Content.ReadAsStringAsync();
                        return JsonConvert.DeserializeObject<PackStatusResponse>(responseJson);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug("Failed to get pack status via Discord: {Error}", ex.Message);
                }
            }
        }

        return null;
    }

    private async Task ProcessAndEncryptFilesWithProgressAsync(List<string> files, string destFolder,
        string fileType, InstalledPackManifest manifest, Action<int> onProgress)
    {
        var processed = 0;
        foreach (var file in files)
        {
            var originalName = Path.GetFileName(file);
            var obfuscatedName = PackEncryptionService.GenerateObfuscatedFilename() + ".enc";
            var destPath = Path.Combine(destFolder, obfuscatedName);

            await Task.Run(() => PackEncryptionService.EncryptFile(file, destPath));

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

    public void ActivatePack(string packId)
    {
        var settings = _settingsService.Current;
        var wasAlreadyActive = settings.ActivePackIds.Contains(packId);
        if (!wasAlreadyActive)
        {
            settings.ActivePackIds.Add(packId);
            _settingsService.Save();
        }

        var pack = _availablePacks.FirstOrDefault(p => p.Id == packId);
        if (pack != null)
        {
            pack.IsActive = true;
        }

        _logger?.LogInformation("Pack activated: {Id}, WasAlreadyActive={WasAlreadyActive}, Total={Total}",
            packId, wasAlreadyActive, settings.ActivePackIds.Count);
    }

    public void DeactivatePack(string packId)
    {
        var settings = _settingsService.Current;
        var beforeCount = settings.ActivePackIds.Count;
        var wasRemoved = settings.ActivePackIds.Remove(packId);
        var afterCount = settings.ActivePackIds.Count;
        _settingsService.Save();

        var pack = _availablePacks.FirstOrDefault(p => p.Id == packId);
        if (pack != null)
        {
            pack.IsActive = false;
        }

        _logger?.LogInformation("Pack deactivated: {Id}, WasRemoved={WasRemoved}, Before={Before}, After={After}, Remaining={Remaining}",
            packId, wasRemoved, beforeCount, afterCount, string.Join(",", settings.ActivePackIds));
    }

    public void UninstallPack(string packId)
    {
        DeactivatePack(packId);

        var settings = _settingsService.Current;
        if (settings.PackGuidMap?.TryGetValue(packId, out var guid) == true)
        {
            var packFolder = Path.Combine(_packsFolder, guid);
            if (Directory.Exists(packFolder))
            {
                Directory.Delete(packFolder, true);
            }

            settings.PackGuidMap.Remove(packId);
        }

        settings.InstalledPackIds.Remove(packId);
        _settingsService.Save();

        _installedManifests.Remove(packId);

        var pack = _availablePacks.FirstOrDefault(p => p.Id == packId);
        if (pack != null)
        {
            pack.IsDownloaded = false;
        }

        _logger?.LogInformation("Pack uninstalled: {Id}", packId);
    }

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

    public MemoryStream? GetPackFileStream(string packId, PackFileEntry file)
    {
        try
        {
            var guidMap = _settingsService.Current.PackGuidMap;
            if (guidMap == null || !guidMap.TryGetValue(packId, out var guid))
                return null;

            var filePath = Path.Combine(_packsFolder, guid, "content", file.ObfuscatedName);
            if (!File.Exists(filePath)) return null;

            return PackEncryptionService.DecryptFileToStream(filePath);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to decrypt pack file: {Name}", file.OriginalName);
            return null;
        }
    }

    public string? GetPackFileTempPath(string packId, PackFileEntry file)
    {
        try
        {
            var guidMap = _settingsService.Current.PackGuidMap;
            if (guidMap == null || !guidMap.TryGetValue(packId, out var guid))
                return null;

            var encryptedPath = Path.Combine(_packsFolder, guid, "content", file.ObfuscatedName);
            if (!File.Exists(encryptedPath)) return null;

            EnsureMediaTempPath();
            var tempPath = Path.Combine(_mediaTempPath, $"ccp_temp_{Guid.NewGuid():N}{file.Extension}");
            var decrypted = PackEncryptionService.DecryptFile(encryptedPath);
            File.WriteAllBytes(tempPath, decrypted);

            return tempPath;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to create temp file for pack: {Name}", file.OriginalName);
            return null;
        }
    }

    public bool IsPackInstalled(string packId)
    {
        var settings = _settingsService.Current;
        var inSettings = settings.InstalledPackIds.Contains(packId);
        if (!inSettings) return false;

        if (settings.PackGuidMap == null || !settings.PackGuidMap.TryGetValue(packId, out var guid))
        {
            _logger?.LogDebug("Pack {PackId} is in settings but has no GUID mapping", packId);
            return false;
        }

        var manifestPath = Path.Combine(_packsFolder, guid, ".manifest.enc");
        var manifestExists = File.Exists(manifestPath);

        if (!manifestExists)
        {
            _logger?.LogDebug("Pack {PackId} (GUID {Guid}) is in settings but manifest not found at {Path}", packId, guid, manifestPath);
        }

        return manifestExists;
    }

    public bool IsPackActive(string packId)
    {
        var settings = _settingsService.Current;
        var isActive = settings.ActivePackIds.Contains(packId);
        return isActive && IsPackInstalled(packId);
    }

    public List<string> GetActivePackIds()
    {
        var settings = _settingsService.Current;
        var settingsIds = settings.ActivePackIds.ToList();
        var ids = settingsIds.Where(id => IsPackInstalled(id)).ToList();

        if (ids.Count != settingsIds.Count)
        {
            _logger?.LogInformation("ContentPackService.GetActivePackIds: {SettingsCount} in settings, {ActualCount} actually installed: [{Ids}]",
                settingsIds.Count, ids.Count, string.Join(", ", ids));
        }
        else
        {
            _logger?.LogDebug("ContentPackService.GetActivePackIds: Returning {Count} active packs: [{Ids}]",
                ids.Count, string.Join(", ", ids));
        }
        return ids;
    }

    public List<(string PackId, PackFileEntry File)> GetAllActivePackVideos()
    {
        var result = new List<(string, PackFileEntry)>();
        var disabledPaths = _settingsService.Current.DisabledAssetPaths;
        foreach (var packId in GetActivePackIds())
        {
            var videos = GetPackFiles(packId, "video");
            foreach (var video in videos)
            {
                if (disabledPaths.Contains($"pack:{packId}/{video.OriginalName}"))
                {
                    _logger?.LogDebug("ContentPacks: Skipping disabled pack video: pack:{PackId}/{Name}", packId, video.OriginalName);
                    continue;
                }
                result.Add((packId, video));
            }
        }
        return result;
    }

    public List<(string PackId, PackFileEntry File)> GetAllActivePackImages()
    {
        var result = new List<(string, PackFileEntry)>();
        var disabledPaths = _settingsService.Current.DisabledAssetPaths;
        foreach (var packId in GetActivePackIds())
        {
            var images = GetPackFiles(packId, "image");
            foreach (var image in images)
            {
                if (disabledPaths.Contains($"pack:{packId}/{image.OriginalName}"))
                {
                    _logger?.LogDebug("ContentPacks: Skipping disabled pack image: pack:{PackId}/{Name}", packId, image.OriginalName);
                    continue;
                }
                result.Add((packId, image));
            }
        }
        return result;
    }

    public List<object> GetPackPreviewImages(string packId, int count = 10, int width = 240, int height = 100)
    {
        var result = new List<object>();

        if (!IsPackInstalled(packId))
            return result;

        try
        {
            var guidMap = _settingsService.Current.PackGuidMap;
            if (guidMap == null || !guidMap.TryGetValue(packId, out var guid))
                return result;

            var packFolder = Path.Combine(_packsFolder, guid);
            var cacheFile = Path.Combine(packFolder, ".preview-cache.json");

            List<string>? cachedNames = null;
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

            var allImages = GetPackFiles(packId, "image");
            if (allImages.Count == 0)
                return result;

            List<PackFileEntry> selectedFiles = new();
            bool needsNewSelection = true;

            if (cachedNames != null && cachedNames.Count > 0)
            {
                selectedFiles = allImages
                    .Where(f => cachedNames.Contains(f.ObfuscatedName))
                    .ToList();

                if (selectedFiles.Count > 0)
                    needsNewSelection = false;
            }

            if (needsNewSelection)
            {
                selectedFiles = allImages
                    .OrderBy(_ => Random.Shared.Next())
                    .Take(count)
                    .ToList();

                try
                {
                    var namesToCache = selectedFiles.Select(f => f.ObfuscatedName).ToList();
                    File.WriteAllText(cacheFile, JsonConvert.SerializeObject(namesToCache));
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug("Failed to cache preview selection: {Error}", ex.Message);
                }
            }

            foreach (var file in selectedFiles)
            {
                try
                {
                    var bitmap = LoadPackFileBitmap(packId, file, width, height);
                    if (bitmap != null)
                    {
                        result.Add(bitmap);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug("Failed to load preview image {Name}: {Error}", file.OriginalName, ex.Message);
                }
            }

            _logger?.LogDebug("Loaded {Count} preview images for pack {PackId}", result.Count, packId);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to get preview images for pack: {PackId}", packId);
        }

        return result;
    }

    private Bitmap? LoadPackFileBitmap(string packId, PackFileEntry file, int width = 100, int height = 100)
    {
        try
        {
            var guidMap = _settingsService.Current.PackGuidMap;
            if (guidMap == null || !guidMap.TryGetValue(packId, out var guid))
                return null;

            var filePath = Path.Combine(_packsFolder, guid, "content", file.ObfuscatedName);
            if (!File.Exists(filePath)) return null;

            using var decryptedStream = PackEncryptionService.DecryptFileToStream(filePath);
            var bitmap = new Bitmap(decryptedStream);
            return bitmap;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug("Failed to get pack file thumbnail: {Error}", ex.Message);
            return null;
        }
    }

    public void ClearPreviewCache(string packId)
    {
        try
        {
            var guidMap = _settingsService.Current.PackGuidMap;
            if (guidMap?.TryGetValue(packId, out var guid) != true)
                return;

            var cacheFile = Path.Combine(_packsFolder, guid, ".preview-cache.json");
            if (File.Exists(cacheFile))
            {
                File.Delete(cacheFile);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug("Failed to clear preview cache: {Error}", ex.Message);
        }
    }

    private void LoadInstalledManifests()
    {
        foreach (var packId in _settingsService.Current.InstalledPackIds)
        {
            LoadPackManifest(packId);
        }
    }

    private void LoadPackManifest(string packId)
    {
        try
        {
            var guidMap = _settingsService.Current.PackGuidMap;
            if (guidMap?.TryGetValue(packId, out var guid) != true)
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
            _logger?.LogWarning(ex, "Failed to load manifest for pack: {Id}", packId);
        }
    }

    private static string? FindSubfolder(string rootPath, string folderName)
    {
        var directPath = Path.Combine(rootPath, folderName);
        if (Directory.Exists(directPath))
            return directPath;

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

            var tempFolders = Directory.GetDirectories(_packsFolder, ".*_extract");
            foreach (var folder in tempFolders)
            {
                Directory.Delete(folder, true);
            }
        }
        catch { }
    }

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
            SizeBytes = 2_397_264_867,
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
            SizeBytes = 4_392_954_093,
            DownloadUrl = "https://ccp-packs.b-cdn.net/Enhanced%20Bimbodoll%20video%20pack.zip",
            PreviewImageUrl = "",
            PatreonUrl = "https://patreon.com/CodeBambi",
            UpgradeUrl = ""
        }
    };

    public void Dispose()
    {
        if (!_disposed)
        {
            _httpClient?.Dispose();
            _disposed = true;
        }
    }
}
