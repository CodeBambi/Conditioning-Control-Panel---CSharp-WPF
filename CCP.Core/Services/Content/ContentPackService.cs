using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;
using Serilog;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The NETWORK half of WPF 7.1.5 Services/Content/ContentPackService.cs on Core: the server pack
    /// manifest (with the built-in list as fallback), the Patreon-gated signed-URL install with HTTP
    /// Range resume and the extract -> per-file encrypt step, the local-zip install, the external
    /// (V2 auth) download link, pack status, and the rotating preview picks
    /// (<c>.preview-cache.json</c>). The installed half (discovery, activation, decrypt) is
    /// <see cref="ContentPackStore"/>; an install writes the same layout and then rescans it.
    ///
    /// <para>Every server call goes through <see cref="BaseUrl"/>: null (a CCP_USERDATA_DIR sandbox
    /// with no loopback override, or offline) sends nothing. Tests hand in an HttpClient over a fake
    /// handler.</para>
    /// </summary>
    public sealed class ContentPackService
    {
        public static volatile ContentPackService? Current;

        /// <summary>The proxy base the head seeds (FriendsHead.BaseUrl rule). Unseeded: null, nothing sent.</summary>
        public static Func<string?> DefaultBaseUrl { get; set; } = () => null;

        public const string PreviewCacheFileName = ".preview-cache.json";

        private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" };
        private static readonly string[] VideoExtensions = { ".mp4", ".webm", ".mkv", ".avi", ".mov", ".wmv" };

        /// <summary>WPF BuiltInPacks: shown when the manifest cannot be fetched.</summary>
        private static IEnumerable<ContentPack> NewBuiltInPacks()
        {
            yield return new ContentPack
            {
                Id = "basic-bimbo-starter",
                Name = "Basic Bimbo Starter Pack",
                Description = "Essential images and videos to begin your bimbo journey. A curated collection perfect for newcomers!",
                Author = "CodeBambi", Version = "1.0.0", ImageCount = 113, VideoCount = 7,
                SizeBytes = 2_397_264_867,
                DownloadUrl = "https://ccp-packs.b-cdn.net/Basic%20Bimbo%20Starter%20Pack.zip",
            };
            yield return new ContentPack
            {
                Id = "enhanced-bimbodoll-video",
                Name = "Enhanced Bimbodoll Video Pack",
                Description = "Premium video collection for experienced users. High-quality hypno videos and exclusive content.",
                Author = "CodeBambi", Version = "1.0.0", ImageCount = 0, VideoCount = 27,
                SizeBytes = 4_392_954_093,
                DownloadUrl = "https://ccp-packs.b-cdn.net/Enhanced%20Bimbodoll%20video%20pack.zip",
                PatreonUrl = "https://patreon.com/CodeBambi",
            };
        }

        private static readonly HttpClient SharedHttp = NewHttp();

        private static HttpClient NewHttp()
        {
            var h = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            try
            {
                h.DefaultRequestHeaders.Add("X-Client-Version", CoreReleaseContent.AppVersion);
                h.DefaultRequestHeaders.UserAgent.ParseAdd($"ConditioningControlPanel/{CoreReleaseContent.AppVersion}");
            }
            catch { }
            return h;
        }

        private readonly ContentPackStore _store;
        private readonly HttpClient _http;
        private readonly Func<string?> _baseUrl;
        private readonly Func<AppSettings> _settings;
        private readonly Action _save;
        private readonly Func<string?> _patreonToken;
        private readonly Func<(string UnifiedId, string Token)?> _identity;
        private readonly Func<string> _previewCacheRoot;
        private readonly Func<TimeSpan, Task> _delay;

        public event EventHandler<ContentPack>? PackDownloadStarted;
        public event EventHandler<ContentPack>? PackDownloadCompleted;
        public event EventHandler<(ContentPack Pack, int Progress)>? PackDownloadProgress;
        public event EventHandler<(ContentPack Pack, string Status)>? PackInstallStatus;
        public event EventHandler<ContentPack>? PackInstallFailed;
        public event EventHandler<string>? AuthenticationRequired;
        public event EventHandler<(ContentPack Pack, string Message, DateTime ResetTime)>? RateLimitExceeded;

        public ContentPackService(
            ContentPackStore store,
            HttpClient? http = null,
            Func<string?>? baseUrl = null,
            Func<AppSettings>? settings = null,
            Action? save = null,
            Func<string?>? patreonToken = null,
            Func<(string UnifiedId, string Token)?>? identity = null,
            Func<string>? previewCacheRoot = null,
            Func<TimeSpan, Task>? delay = null)
        {
            _store = store;
            _http = http ?? SharedHttp;
            _baseUrl = baseUrl ?? DefaultBaseUrl;
            _settings = settings ?? (() => CoreSettings.Current);
            _save = save ?? (() => CoreSettings.Save());
            _patreonToken = patreonToken ?? (() => CoreAccount.PatreonAccessToken);
            _identity = identity ?? DefaultIdentity;
            _previewCacheRoot = previewCacheRoot ?? (() => Path.Combine(CorePaths.UserData, "pack-previews"));
            _delay = delay ?? (t => Task.Delay(t));
        }

        private static (string UnifiedId, string Token)? DefaultIdentity()
        {
            try
            {
                var s = CoreSettings.Current;
                return string.IsNullOrWhiteSpace(s.UnifiedId) || string.IsNullOrWhiteSpace(s.AuthToken) ? null : (s.UnifiedId!, s.AuthToken!);
            }
            catch { return null; }
        }

        private bool Offline
        {
            get { try { return _settings().OfflineMode; } catch { return false; } }
        }

        /// <summary>The proxy base, or null when nothing may be sent (offline, sandbox, unseeded).</summary>
        private string? BaseUrl
        {
            get
            {
                if (Offline) return null;
                try { return _baseUrl()?.TrimEnd('/'); } catch { return null; }
            }
        }

        // =========================================================================================
        //  catalogue
        // =========================================================================================

        /// <summary>WPF GetBuiltInPacks: the two built-in packs with live installed/active state.</summary>
        public List<ContentPack> GetBuiltInPacks() => Stamp(NewBuiltInPacks().ToList());

        /// <summary>WPF GetAvailablePacksAsync: the server manifest (<c>GET /packs/manifest</c>), the
        /// built-in list when offline, unreachable or empty. Never throws.</summary>
        public async Task<List<ContentPack>> GetAvailablePacksAsync(CancellationToken ct = default)
        {
            var url = BaseUrl;
            if (url == null) return GetBuiltInPacks();
            try
            {
                var json = await _http.GetStringAsync($"{url}/packs/manifest", ct).ConfigureAwait(false);
                var manifest = JsonConvert.DeserializeObject<PacksManifest>(json);
                if (manifest?.Packs?.Count > 0)
                {
                    Log.Information("Fetched {Count} packs from remote manifest", manifest.Packs.Count);
                    return Stamp(manifest.Packs);
                }
            }
            catch (Exception ex) { Log.Debug("Could not fetch remote packs manifest: {Error}, using built-in", ex.Message); }
            return GetBuiltInPacks();
        }

        private List<ContentPack> Stamp(List<ContentPack> packs)
        {
            foreach (var p in packs)
            {
                p.IsDownloaded = _store.IsPackInstalled(p.Id);
                p.IsActive = _store.IsPackActive(p.Id);
            }
            return packs;
        }

        // =========================================================================================
        //  install
        // =========================================================================================

        /// <summary>
        /// WPF InstallPackAsync: Patreon token -> <c>POST /pack/download-url</c> (Bearer) -> resumable
        /// download (Range, 10 tries, 3 s growing by 1.5x to 30 s) -> extract -> per-file encrypt ->
        /// encrypted manifest -> settings + rescan. Throws <see cref="UnauthorizedAccessException"/>
        /// (and raises <see cref="AuthenticationRequired"/>) or <see cref="PackRateLimitException"/>
        /// (and raises <see cref="RateLimitExceeded"/>).
        /// </summary>
        public async Task InstallPackAsync(ContentPack pack, IProgress<int>? progress = null, CancellationToken ct = default)
        {
            var url = BaseUrl;
            if (url == null)
            {
                Log.Information("Pack download blocked: offline or no server");
                PackInstallFailed?.Invoke(this, pack);
                throw new InvalidOperationException("Cannot download packs in offline mode");
            }
            if (string.IsNullOrEmpty(pack.Id)) throw new InvalidOperationException("Pack has no ID");

            var accessToken = _patreonToken();
            if (string.IsNullOrEmpty(accessToken))
            {
                AuthenticationRequired?.Invoke(this, "Please log in with Patreon to download content packs.\nA free Patreon account gives you 10 GB/month - no payment needed.");
                throw new UnauthorizedAccessException("Patreon authentication required to download packs");
            }

            string downloadUrl;
            try { downloadUrl = await GetSignedDownloadUrlAsync(url, pack.Id, accessToken, ct).ConfigureAwait(false); }
            catch (PackRateLimitException ex) { RateLimitExceeded?.Invoke(this, (pack, ex.Message, ex.ResetTime)); throw; }
            catch (UnauthorizedAccessException) { AuthenticationRequired?.Invoke(this, "Your Patreon session has expired. Please log in again."); throw; }

            var packsFolder = EnsurePacksFolder();
            var packGuid = Guid.NewGuid().ToString("N");
            var packFolder = Path.Combine(packsFolder, packGuid);
            var tempZipPath = Path.Combine(packsFolder, $".{packGuid}_temp.zip");
            try
            {
                pack.IsDownloading = true;
                PackDownloadStarted?.Invoke(this, pack);
                await DownloadResumableAsync(pack, downloadUrl, tempZipPath, progress, ct).ConfigureAwait(false);

                pack.DownloadProgress = 100;
                PackDownloadProgress?.Invoke(this, (pack, 100));
                PackInstallStatus?.Invoke(this, (pack, "Extracting..."));

                await InstallExtractedAsync(pack, tempZipPath, packsFolder, packGuid,
                    s => PackInstallStatus?.Invoke(this, (pack, s)), requireContent: false).ConfigureAwait(false);
                TryDelete(tempZipPath);

                pack.IsDownloaded = true;
                pack.IsDownloading = false;
                pack.DownloadProgress = 100;
                PackDownloadCompleted?.Invoke(this, pack);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to install pack: {Name}", pack.Name);
                pack.IsDownloading = false;
                pack.DownloadProgress = 0;
                TryDelete(tempZipPath);
                try { if (Directory.Exists(packFolder)) Directory.Delete(packFolder, true); } catch { }
                PackInstallFailed?.Invoke(this, pack);
                throw;
            }
        }

        private async Task DownloadResumableAsync(ContentPack pack, string downloadUrl, string tempZipPath,
            IProgress<int>? progress, CancellationToken ct)
        {
            const int maxRetries = 10;
            var retryDelay = TimeSpan.FromSeconds(3);
            var totalBytes = pack.SizeBytes;
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    long resumeFrom = File.Exists(tempZipPath) ? new FileInfo(tempZipPath).Length : 0;
                    using var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
                    if (resumeFrom > 0) request.Headers.Range = new RangeHeaderValue(resumeFrom, null);
                    using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                    if (response.StatusCode != HttpStatusCode.OK && response.StatusCode != HttpStatusCode.PartialContent)
                        response.EnsureSuccessStatusCode();
                    // A server that ignores Range answers 200 with the whole body: start over.
                    if (response.StatusCode == HttpStatusCode.OK) resumeFrom = 0;

                    if (response.Content.Headers.ContentRange?.Length is long full) totalBytes = full;
                    else if (response.Content.Headers.ContentLength is long len) totalBytes = resumeFrom + len;

                    await using (var body = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
                    await using (var file = new FileStream(tempZipPath, resumeFrom > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        var buffer = new byte[65536];
                        int read;
                        var done = resumeFrom;
                        while ((read = await body.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
                        {
                            await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                            done += read;
                            if (totalBytes > 0)
                            {
                                var pct = (int)(done * 100 / totalBytes);
                                progress?.Report(pct);
                                pack.DownloadProgress = pct;
                                PackDownloadProgress?.Invoke(this, (pack, pct));
                            }
                        }
                    }

                    var size = new FileInfo(tempZipPath).Length;
                    if (totalBytes > 0 && size < totalBytes * 0.99)
                        throw new IOException($"Download incomplete: received {size} of {totalBytes} bytes");
                    return;
                }
                catch (Exception ex) when (attempt < maxRetries && !ct.IsCancellationRequested
                    && (ex is HttpRequestException || ex is TaskCanceledException || ex is IOException))
                {
                    var current = File.Exists(tempZipPath) ? new FileInfo(tempZipPath).Length : 0;
                    var pct = totalBytes > 0 ? current * 100 / totalBytes : 0;
                    Log.Warning("Download attempt {Attempt}/{Max} failed at {Pct}%: {Error}", attempt, maxRetries, pct, ex.Message);
                    PackInstallStatus?.Invoke(this, (pack, $"Connection lost at {pct}%, resuming..."));
                    await _delay(retryDelay).ConfigureAwait(false);
                    retryDelay = TimeSpan.FromSeconds(Math.Min(retryDelay.TotalSeconds * 1.5, 30));
                }
            }
        }

        /// <summary>
        /// WPF InstallPackFromLocalZipAsync: same extract -> encrypt -> manifest pipeline, no download
        /// and no Patreon gate. The source zip is the user's file and is never deleted.
        /// </summary>
        public async Task InstallPackFromLocalZipAsync(ContentPack pack, string zipPath, Action<string>? status = null)
        {
            if (!File.Exists(zipPath)) throw new FileNotFoundException("Pack zip not found", zipPath);
            if (_store.IsPackInstalled(pack.Id)) throw new InvalidOperationException($"Pack already installed: {pack.Name}");
            var packsFolder = EnsurePacksFolder();
            var packGuid = Guid.NewGuid().ToString("N");
            try
            {
                await InstallExtractedAsync(pack, zipPath, packsFolder, packGuid, status, requireContent: true).ConfigureAwait(false);
            }
            catch
            {
                try { var d = Path.Combine(packsFolder, packGuid); if (Directory.Exists(d)) Directory.Delete(d, true); } catch { }
                throw;
            }
            pack.IsDownloaded = true;
            PackDownloadCompleted?.Invoke(this, pack);
        }

        private async Task InstallExtractedAsync(ContentPack pack, string zipPath, string packsFolder, string packGuid,
            Action<string>? status, bool requireContent)
        {
            var packFolder = Path.Combine(packsFolder, packGuid);
            var extractPath = Path.Combine(packsFolder, $".{packGuid}_extract");
            try
            {
                status?.Invoke("Extracting...");
                await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, extractPath)).ConfigureAwait(false);

                var contentFolder = Path.Combine(packFolder, ContentPackStore.ContentFolderName);
                Directory.CreateDirectory(contentFolder);

                var images = FilesUnder(FindSubfolder(extractPath, "images"), ImageExtensions);
                var videos = FilesUnder(FindSubfolder(extractPath, "videos"), VideoExtensions);
                if (requireContent && images.Count == 0 && videos.Count == 0)
                    throw new InvalidDataException("Zip has no images/ or videos/ content - not a content pack.");

                var total = images.Count + videos.Count;
                var manifest = new InstalledPackManifest
                {
                    PackId = pack.Id, PackGuid = packGuid, PackName = pack.Name, InstalledDate = DateTime.UtcNow,
                };
                status?.Invoke($"Encrypting 0/{total}...");
                var done = 0;
                foreach (var (list, type) in new[] { (images, ContentPackStore.ImageType), (videos, ContentPackStore.VideoType) })
                {
                    foreach (var file in list)
                    {
                        var obf = PackEncryptionService.GenerateObfuscatedFilename() + ".enc";
                        await Task.Run(() => PackEncryptionService.EncryptFile(file, Path.Combine(contentFolder, obf))).ConfigureAwait(false);
                        manifest.Files.Add(new PackFileEntry
                        {
                            OriginalName = Path.GetFileName(file), ObfuscatedName = obf, FileType = type,
                            Extension = Path.GetExtension(file).ToLowerInvariant(),
                        });
                        status?.Invoke($"Encrypting {++done}/{total}...");
                    }
                }

                PackEncryptionService.SaveEncryptedManifest(JsonConvert.SerializeObject(manifest, Formatting.Indented),
                    Path.Combine(packFolder, ContentPackStore.ManifestFileName));
                try { new DirectoryInfo(packFolder).Attributes |= FileAttributes.Hidden; } catch { }

                var s = _settings();
                s.InstalledPackIds ??= new List<string>();
                if (!s.InstalledPackIds.Contains(pack.Id)) s.InstalledPackIds.Add(pack.Id);
                s.PackGuidMap ??= new Dictionary<string, string>();
                s.PackGuidMap[pack.Id] = packGuid;
                try { _save(); } catch (Exception ex) { Log.Warning(ex, "ContentPacks: settings save failed"); }
                _store.Rescan();
                Log.Information("Pack installed: {Name} ({Count} files encrypted)", pack.Name, manifest.Files.Count);
                ConditioningControlPanel.Services.EmiDesk.EmiDeskBus.Fire("contentPackInstalled", new { target = pack.Name?.ToLowerInvariant() });   // WPF ContentPackService.cs:588
            }
            finally
            {
                try { if (Directory.Exists(extractPath)) Directory.Delete(extractPath, true); } catch { }
            }
        }

        private static List<string> FilesUnder(string? dir, string[] exts) =>
            dir != null && Directory.Exists(dir)
                ? Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
                    .Where(f => exts.Contains(Path.GetExtension(f).ToLowerInvariant())).OrderBy(f => f, StringComparer.Ordinal).ToList()
                : new List<string>();

        /// <summary>WPF FindSubfolder: a direct child first, else anywhere (zips with a root folder).</summary>
        internal static string? FindSubfolder(string root, string name)
        {
            var direct = Path.Combine(root, name);
            if (Directory.Exists(direct)) return direct;
            try { return Directory.GetDirectories(root, name, SearchOption.AllDirectories).FirstOrDefault(); }
            catch { return null; }
        }

        private string EnsurePacksFolder()
        {
            var folder = _store.PacksFolder;
            if (string.IsNullOrEmpty(folder)) throw new IOException("Content packs folder is unavailable");
            if (!Directory.Exists(folder))
            {
                var di = Directory.CreateDirectory(folder);
                try { di.Attributes |= FileAttributes.Hidden; } catch { }
            }
            return folder;
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        // =========================================================================================
        //  server calls
        // =========================================================================================

        private async Task<string> GetSignedDownloadUrlAsync(string baseUrl, string packId, string accessToken, CancellationToken ct)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/pack/download-url")
            {
                Content = new StringContent(JsonConvert.SerializeObject(new { packId }), Encoding.UTF8, "application/json"),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new UnauthorizedAccessException("Patreon authentication failed");
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var err = TryRead<PackDownloadErrorResponse>(json);
                var reset = DateTime.TryParse(err?.ResetTime, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed)
                    ? parsed : DateTime.UtcNow.AddHours(24);
                throw new PackRateLimitException(err?.Message ?? "Download limit exceeded. Try again later.", reset);
            }
            if (!response.IsSuccessStatusCode)
                throw new Exception(TryRead<PackDownloadErrorResponse>(json)?.Message ?? $"Failed to get download URL: {response.StatusCode}");

            var ok = TryRead<PackDownloadUrlResponse>(json);
            if (string.IsNullOrEmpty(ok?.DownloadUrl)) throw new Exception("Server returned empty download URL");
            Log.Information("Got signed download URL for pack: {PackId}, remaining downloads: {Remaining}", packId, ok.RateLimit?.Remaining ?? -1);
            return ok.DownloadUrl;
        }

        /// <summary>
        /// WPF GetExternalPackDownloadUrlAsync: an external pack's link for any signed-in account
        /// (V2: <c>X-Auth-Token</c> + <c>unified_id</c>). Null (and <see cref="AuthenticationRequired"/>)
        /// when signed out or refused 401; throws on any other refusal.
        /// </summary>
        public async Task<string?> GetExternalPackDownloadUrlAsync(string packId, CancellationToken ct = default)
        {
            (string UnifiedId, string Token)? id;
            try { id = _identity(); } catch { id = null; }
            if (id == null)
            {
                AuthenticationRequired?.Invoke(this, "Please log in to download content packs.");
                return null;
            }
            var url = BaseUrl;
            if (url == null) return null;

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{url}/pack/download-url")
            {
                Content = new StringContent(JsonConvert.SerializeObject(new { packId, unified_id = id.Value.UnifiedId }), Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("X-Auth-Token", id.Value.Token);
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            // ponytail: no Contract D (MergedAccountRecovery) hook on this head yet; a merged account reads as 401/refusal.
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                AuthenticationRequired?.Invoke(this, "Your session has expired. Please log in again.");
                return null;
            }
            if (!response.IsSuccessStatusCode)
                throw new Exception(TryRead<PackDownloadErrorResponse>(json)?.Message ?? $"Failed to get download URL: {response.StatusCode}");
            var ok = TryRead<PackDownloadUrlResponse>(json);
            if (string.IsNullOrEmpty(ok?.DownloadUrl)) throw new Exception("Server returned empty download URL");
            return ok.DownloadUrl;
        }

        /// <summary>WPF GetFullPackStatusAsync: <c>GET /pack/status</c> with the Patreon token. The
        /// Discord fallback (<c>/discord/pack/status</c>) waits for a Discord token seam on this head.
        /// Null when signed out or unreachable. Never throws.</summary>
        public async Task<PackStatusResponse?> GetFullPackStatusAsync(CancellationToken ct = default)
        {
            var url = BaseUrl;
            var token = _patreonToken();
            if (url == null || string.IsNullOrEmpty(token)) return null;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{url}/pack/status");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                return TryRead<PackStatusResponse>(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            }
            catch (Exception ex) { Log.Debug("Failed to get pack status: {Error}", ex.Message); return null; }
        }

        public async Task<Dictionary<string, PackDownloadStatus>?> GetPackDownloadStatusAsync(CancellationToken ct = default) =>
            (await GetFullPackStatusAsync(ct).ConfigureAwait(false))?.Packs;

        private static T? TryRead<T>(string json) where T : class
        {
            try { return JsonConvert.DeserializeObject<T>(json); } catch { return null; }
        }

        // =========================================================================================
        //  previews
        // =========================================================================================

        /// <summary>
        /// WPF GetPackPreviewImages without the decode: up to <paramref name="count"/> random image
        /// files of an installed pack, the pick kept in <c>&lt;pack&gt;/.preview-cache.json</c> (obfuscated
        /// names) so the card shows the same faces every visit, decrypted to bytes for the head.
        /// </summary>
        public List<byte[]> GetPackPreviewBytes(string packId, int count = 10)
        {
            var result = new List<byte[]>();
            var folder = _store.GetPackFolder(packId);
            if (folder == null || !_store.IsPackInstalled(packId)) return result;
            var all = _store.GetPackFiles(packId, ContentPackStore.ImageType);
            if (all.Count == 0) return result;

            var cacheFile = Path.Combine(folder, PreviewCacheFileName);
            List<PackFileEntry> picked = new();
            try
            {
                if (File.Exists(cacheFile) && JsonConvert.DeserializeObject<List<string>>(File.ReadAllText(cacheFile)) is { Count: > 0 } names)
                    picked = all.Where(f => names.Contains(f.ObfuscatedName)).ToList();
            }
            catch { }
            if (picked.Count == 0)
            {
                picked = all.OrderBy(_ => Random.Shared.Next()).Take(count).ToList();
                try { File.WriteAllText(cacheFile, JsonConvert.SerializeObject(picked.Select(f => f.ObfuscatedName).ToList())); }
                catch (Exception ex) { Log.Debug("Failed to cache preview selection: {Error}", ex.Message); }
            }
            foreach (var f in picked)
            {
                try { using var ms = _store.GetPackFileStream(packId, f); if (ms != null) result.Add(ms.ToArray()); }
                catch (Exception ex) { Log.Debug("Failed to load preview image {Name}: {Error}", f.OriginalName, ex.Message); }
            }
            return result;
        }

        /// <summary>WPF ClearPreviewCache.</summary>
        public void ClearPreviewCache(string packId)
        {
            try
            {
                if (_store.GetPackFolder(packId) is { } folder)
                {
                    var f = Path.Combine(folder, PreviewCacheFileName);
                    if (File.Exists(f)) File.Delete(f);
                }
            }
            catch (Exception ex) { Log.Debug("Failed to clear preview cache: {Error}", ex.Message); }
        }

        /// <summary>
        /// WPF LoadPreviewImagesFromUrlsAsync without the decode: a not-installed pack's server
        /// preview URLs, each cached under <c>pack-previews/&lt;id&gt;/&lt;stem&gt;.&lt;sniffed ext&gt;</c>.
        /// A url that fails is skipped. Only https urls are fetched.
        /// </summary>
        public async Task<List<byte[]>> GetPreviewBytesFromUrlsAsync(string packId, IEnumerable<string> urls, CancellationToken ct = default)
        {
            var result = new List<byte[]>();
            string dir;
            try
            {
                var safeId = string.Concat(packId.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
                dir = Path.Combine(_previewCacheRoot(), safeId);
                Directory.CreateDirectory(dir);
            }
            catch (Exception ex) { Log.Debug("Pack preview cache unavailable: {Error}", ex.Message); return result; }

            foreach (var url in urls)
            {
                try
                {
                    var stem = PreviewFileStem(url);
                    var cached = Directory.GetFiles(dir, stem + ".*");
                    if (cached.Length > 0) { result.Add(await File.ReadAllBytesAsync(cached[0], ct).ConfigureAwait(false)); continue; }
                    if (Offline || !Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps) continue;
                    using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    budget.CancelAfter(TimeSpan.FromSeconds(10));
                    using var response = await _http.GetAsync(u, budget.Token).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();
                    var bytes = await response.Content.ReadAsByteArrayAsync(budget.Token).ConfigureAwait(false);
                    var ext = Helpers.MediaTypeSniffer.ResolveExtension(response.Content.Headers.ContentType?.ToString(), bytes, url,
                        Helpers.MediaTypeSniffer.DefaultImageExtension, "PackPreviewCache");
                    await File.WriteAllBytesAsync(Path.Combine(dir, stem + ext), bytes, ct).ConfigureAwait(false);
                    result.Add(bytes);
                }
                catch (Exception ex) { Log.Debug("Failed to load preview image from {Host}: {Error}", Logging.UrlLog.Host(url), ex.Message); }
            }
            return result;
        }

        /// <summary>WPF GetPackPreviewFileStem: the url's last segment without its extension, folded safe, 64 chars max.</summary>
        internal static string PreviewFileStem(string url)
        {
            try
            {
                var stem = Path.GetFileNameWithoutExtension(new Uri(url).LocalPath);
                foreach (var c in Path.GetInvalidFileNameChars()) stem = stem.Replace(c, '_');
                stem = stem.Trim();
                if (stem.Length > 64) stem = stem[..64];
                return stem.Length > 0 ? stem : "preview";
            }
            catch { return "preview"; }
        }
    }

    public class PackDownloadUrlResponse
    {
        [JsonProperty("success")] public bool Success { get; set; }
        [JsonProperty("downloadUrl")] public string? DownloadUrl { get; set; }
        [JsonProperty("packId")] public string? PackId { get; set; }
        [JsonProperty("packName")] public string? PackName { get; set; }
        [JsonProperty("sizeBytes")] public long SizeBytes { get; set; }
        [JsonProperty("expiresIn")] public int ExpiresIn { get; set; }
        [JsonProperty("rateLimit")] public PackRateLimitInfo? RateLimit { get; set; }
    }

    public class PackRateLimitInfo
    {
        [JsonProperty("remaining")] public int Remaining { get; set; }
        [JsonProperty("limit")] public int Limit { get; set; }
        [JsonProperty("resetTime")] public string? ResetTime { get; set; }
    }

    public class PackDownloadErrorResponse
    {
        [JsonProperty("error")] public string? Error { get; set; }
        [JsonProperty("message")] public string? Message { get; set; }
        [JsonProperty("resetTime")] public string? ResetTime { get; set; }
        [JsonProperty("remaining")] public int Remaining { get; set; }
    }

    public class PackStatusResponse
    {
        [JsonProperty("userId")] public string? UserId { get; set; }
        [JsonProperty("packs")] public Dictionary<string, PackDownloadStatus>? Packs { get; set; }
        [JsonProperty("dailyLimit")] public int DailyLimit { get; set; }
    }

    public class PackDownloadStatus
    {
        [JsonProperty("name")] public string? Name { get; set; }
        [JsonProperty("sizeBytes")] public long SizeBytes { get; set; }
        [JsonProperty("canDownload")] public bool CanDownload { get; set; }
        [JsonProperty("downloadsRemaining")] public int DownloadsRemaining { get; set; }
        [JsonProperty("downloadsUsed")] public int DownloadsUsed { get; set; }
        [JsonProperty("resetTime")] public string? ResetTime { get; set; }
    }

    public class PackRateLimitException : Exception
    {
        public DateTime ResetTime { get; }
        public PackRateLimitException(string message, DateTime resetTime) : base(message) => ResetTime = resetTime;
    }
}
