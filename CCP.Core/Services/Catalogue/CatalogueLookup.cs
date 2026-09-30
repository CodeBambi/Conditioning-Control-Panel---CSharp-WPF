using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// W3 Piece 1 — looks up community-published enhancements for the HT video
    /// the user has navigated to in the embedded browser, and downloads the
    /// chosen .ccpenh.json bundle into the user's local Library.
    ///
    /// Two endpoints, both unauthenticated (same public surface the web
    /// catalogue uses):
    ///   GET /api/enhancements/by-ht-url?url=&lt;urlencoded&gt; — discovery
    ///   GET &lt;FileUrl&gt;                                  — bundle download
    ///       (the proxy URL the server hands back per entry; the catalogue
    ///       runs the download server-side to force Content-Disposition:
    ///       attachment and to bump view_count).
    ///
    /// Caching: none. Lookups are cheap, results can change (new uploads /
    /// removals), and freshness beats efficiency. Revisit only if telemetry
    /// shows lookup latency hurting UX.
    ///
    /// Cancellation: the navigation hook holds a single in-flight CTS and
    /// cancels it on each new HT navigation so a slow lookup doesn't surface
    /// a stale toast after the user has moved on.
    /// </summary>
    /// <remarks>Core since catalogue U2; WPF's <c>CatalogueLookupService</c> is a thin subclass.</remarks>
    public class CatalogueLookup
    {
        /// <summary>Where lookups go, or null for "no network": CatalogueClient.ResolveBaseUrl, the Core
        /// LoopbackUrl rule (loopback CCP_CATALOGUE_BASE_URL only; a CCP_USERDATA_DIR sandbox without one
        /// never reaches the real server).</summary>
        private readonly string? CclabsBaseUrl;

        // One client per process, as in WPF; a test handler gets its own.
        private static HttpClient? _shared;
        private readonly HttpClient _http;
        private readonly Func<string?> _libraryFolder;
        private readonly Func<Func<bool>, Task<bool>>? _onUiThread;

        /// <param name="libraryFolder">The enhancement library folder (WPF: App.EnhancementLibrary.LibraryFolder).</param>
        /// <param name="appVersion">Sent as X-Client-Version and in the User-Agent.</param>
        /// <param name="onUiThread">Runs the opener on the head's UI thread; null runs it inline.</param>
        /// <param name="handler">Test seam; null is the real network.</param>
        public CatalogueLookup(Func<string?> libraryFolder, string appVersion,
            Func<Func<bool>, Task<bool>>? onUiThread = null, HttpMessageHandler? handler = null)
            : this(libraryFolder, appVersion, onUiThread, handler,
                Environment.GetEnvironmentVariable("CCP_CATALOGUE_BASE_URL"),
                !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CCP_USERDATA_DIR")),
                trustHandler: true) { }

        /// <param name="trustHandler">A test handler with no explicit environment keeps the real base URL (it
        /// never leaves the process), as CatalogueClient does; the sandbox tests pass false.</param>
        internal CatalogueLookup(Func<string?> libraryFolder, string appVersion,
            Func<Func<bool>, Task<bool>>? onUiThread, HttpMessageHandler? handler,
            string? overrideUrl, bool sandboxed, bool trustHandler = false)
        {
            (_libraryFolder, _onUiThread) = (libraryFolder, onUiThread);
            CclabsBaseUrl = handler != null && trustHandler ? CatalogueClient.RealBaseUrl
                : CatalogueClient.ResolveBaseUrl(overrideUrl, sandboxed);
            _http = handler != null ? BuildHttpClient(new HttpClient(handler), appVersion)
                : _shared ??= BuildHttpClient(new HttpClient(), appVersion);
        }

        private static HttpClient BuildHttpClient(HttpClient http, string appVersion)
        {
            http.Timeout = TimeSpan.FromSeconds(30);
            http.DefaultRequestHeaders.Add("X-Client-Version", appVersion);
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"ConditioningControlPanel/{appVersion}");
            return http;
        }

        /// <summary>
        /// Look up enhancements published against a given HT URL.
        ///
        /// Performs the eligibility check FIRST — if the URL is not an HT
        /// video page (or isn't parseable), returns InvalidUrl without making
        /// the API call. Lets the caller (the WebView2 navigation hook) blindly
        /// fire on every NavigationCompleted without an upstream filter.
        /// </summary>
        public async Task<LookupResult> LookupForUrlAsync(string url, CancellationToken ct)
        {
            if (!Helpers.HtUrlHelper.IsEligibleHtUrl(url))
            {
                return new LookupResult.InvalidUrl();
            }

            // Never log the full URL — HT URLs can carry tracking params we
            // don't want in app logs that ship in bug reports. Video ID is the
            // identifying handle we care about.
            var videoId = Helpers.HtUrlHelper.TryExtractHtVideoId(url) ?? "?";

            if (CclabsBaseUrl == null)
            {
                Log.Warning("[CatalogueLookupService] Sandboxed without a loopback CCP_CATALOGUE_BASE_URL: no network");
                return new LookupResult.NetworkError();
            }

            try
            {
                Log.Information("[CatalogueLookupService] Looking up video_id={VideoId}", videoId);

                var requestUri = $"{CclabsBaseUrl}/api/enhancements/by-ht-url?url={Uri.EscapeDataString(url)}";
                using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
                using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    Log.Warning("[CatalogueLookupService] Lookup failed status={Status} video_id={VideoId}",
                        (int)response.StatusCode, videoId);
                    return new LookupResult.NetworkError();
                }

                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var parsed = JObject.Parse(body);
                // API contract: response shape is { "enhancements": [...] }. See cclabs-web/src/app/api/enhancements/by-ht-url/route.ts for canonical source.
                var entriesJson = parsed["enhancements"] as JArray ?? new JArray();

                var entries = new List<CatalogueEntry>();
                foreach (var item in entriesJson)
                {
                    try
                    {
                        var tagsArr = item["tags"] as JArray;
                        var tags = new List<string>();
                        if (tagsArr != null)
                        {
                            foreach (var t in tagsArr)
                            {
                                var s = t?.ToString();
                                if (!string.IsNullOrWhiteSpace(s)) tags.Add(s);
                            }
                        }

                        entries.Add(new CatalogueEntry(
                            Id: item["id"]?.ToString() ?? "",
                            Title: item["title"]?.ToString() ?? "Untitled",
                            Description: item["description"]?.ToString() ?? "",
                            CreatorName: item["creator_name"]?.ToString() ?? "",
                            RemixerName: item["remixer_name"]?.ToString(),
                            Tags: tags,
                            License: item["license"]?.ToString(),
                            ViewCount: item["view_count"]?.ToObject<int?>() ?? 0,
                            HtUrl: item["ht_url"]?.ToString() ?? url,
                            // JSON key: thumbnail_url (full absolute URL). C# field name
                            // ThumbnailPath is legacy from when the server returned a relative
                            // storage path. Don't rename without also updating CataloguePickerDialog.xaml.
                            ThumbnailPath: item["thumbnail_url"]?.ToString(),
                            FileUrl: item["file_url"]?.ToString() ?? ""
                        ));
                    }
                    catch (Exception itemEx)
                    {
                        Log.Debug("[CatalogueLookupService] Skipping malformed entry: {Error}", itemEx.Message);
                    }
                }

                if (entries.Count == 0)
                {
                    Log.Information("[CatalogueLookupService] No enhancements for video_id={VideoId}", videoId);
                    return new LookupResult.None();
                }

                Log.Information("[CatalogueLookupService] Found {Count} enhancements for video_id={VideoId}",
                    entries.Count, videoId);
                return new LookupResult.Success(entries);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // User-initiated cancel (new navigation) — bubble up to the caller.
                throw;
            }
            catch (OperationCanceledException)
            {
                // HttpClient timeout surfaces as TaskCanceledException (subclass
                // of OperationCanceledException) when the token did NOT cancel
                // — treat as a network error so the caller stays silent.
                Log.Information("[CatalogueLookupService] Timed out for video_id={VideoId}", videoId);
                return new LookupResult.NetworkError();
            }
            catch (HttpRequestException ex)
            {
                Log.Information("[CatalogueLookupService] Network error: {Msg}", ex.Message);
                return new LookupResult.NetworkError();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[CatalogueLookupService] Lookup threw unexpectedly");
                return new LookupResult.NetworkError();
            }
        }

        /// <summary>
        /// Download a bundle, validate it's parseable JSON, save into the user's
        /// EnhancementLibrary with a collision-suffixed filename, and open it in
        /// the Deeper player UI. Returns a result variant that the toast layer
        /// maps to an info or error notification.
        ///
        /// The library's FileSystemWatcher fires LibraryChanged once the file
        /// hits disk, so the Library tab refreshes automatically — no explicit
        /// refresh call needed.
        /// </summary>
        public async Task<DownloadResult> DownloadAndOpenAsync(CatalogueEntry entry, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(entry.FileUrl))
            {
                Log.Warning("[CatalogueLookupService] Entry {Id} has empty FileUrl", entry.Id);
                return new DownloadResult.NetworkError();
            }
            // The server hands back an absolute FileUrl, so the base-URL rule has to hold here too: no
            // network in a bare sandbox, and under a loopback override the bundle must be loopback as well.
            if (CclabsBaseUrl == null
                || (CclabsBaseUrl != CatalogueClient.RealBaseUrl && !LoopbackUrl.IsHonoured(entry.FileUrl, out _)))
            {
                Log.Warning("[CatalogueLookupService] Download refused outside the catalogue base-URL rule entry={Id}", entry.Id);
                return new DownloadResult.NetworkError();
            }

            // ---- Download ------------------------------------------------------
            string body;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, entry.FileUrl);
                using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    Log.Warning("[CatalogueLookupService] Download failed status={Status} entry={Id}",
                        (int)response.StatusCode, entry.Id);
                    return new DownloadResult.NetworkError();
                }
                body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                // HttpClient timeout — see lookup branch above for the rationale.
                Log.Information("[CatalogueLookupService] Download timed out for entry={Id}", entry.Id);
                return new DownloadResult.NetworkError();
            }
            catch (HttpRequestException ex)
            {
                Log.Information("[CatalogueLookupService] Download network error: {Msg}", ex.Message);
                return new DownloadResult.NetworkError();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[CatalogueLookupService] Download threw");
                return new DownloadResult.NetworkError();
            }

            // ---- Validate it's parseable JSON ---------------------------------
            // Loose-parse only — the engine does the heavy schema validation
            // when the file is opened. We just want to fail early if the
            // response was an HTML error page or otherwise not JSON.
            try
            {
                _ = JToken.Parse(body);
            }
            catch (JsonReaderException)
            {
                Log.Warning("[CatalogueLookupService] Downloaded file is not valid JSON entry={Id}", entry.Id);
                return new DownloadResult.InvalidFile();
            }

            // ---- Save to library folder with collision suffix ------------------
            string finalPath;
            try
            {
                var libFolder = _libraryFolder();
                if (string.IsNullOrEmpty(libFolder))
                {
                    Log.Warning("[CatalogueLookupService] Library folder not initialized");
                    return new DownloadResult.SaveError();
                }
                Directory.CreateDirectory(libFolder);

                var baseName = SanitizeForFilename(entry.Title);
                if (string.IsNullOrWhiteSpace(baseName)) baseName = entry.Id;

                finalPath = ResolveCollisionFreePath(libFolder, baseName);
                await File.WriteAllTextAsync(finalPath, body, ct).ConfigureAwait(false);
                Log.Information("[CatalogueLookupService] Saved enhancement to {Path}", finalPath);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[CatalogueLookupService] Save failed entry={Id}", entry.Id);
                return new DownloadResult.SaveError();
            }

            // ---- Open in Deeper player ----------------------------------------
            // Must marshal to the UI thread — DownloadAndOpenAsync may be
            // awaited from the WebView2 navigation context. The opener is
            // supplied by MainWindow (so it can reuse OpenDeeperFile / show
            // load-failure dialogs against itself as Owner). If no opener has
            // been registered yet (rare: download finished before MainWindow
            // loaded), we still report Success — the user can open it from the
            // Library tab.
            var opener = _opener;
            if (opener == null)
            {
                Log.Information("[CatalogueLookupService] No opener registered; saved but not opened");
                return new DownloadResult.Success(Path.GetFileName(finalPath));
            }

            try
            {
                // The head marshals onto its UI thread (WPF: the Dispatcher; with no
                // UI thread left it reports true, i.e. Success, file on disk).
                var ok = _onUiThread != null ? await _onUiThread(() => opener(finalPath)) : opener(finalPath);
                if (!ok)
                {
                    return new DownloadResult.OpenError(Path.GetFileName(finalPath));
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[CatalogueLookupService] Opener threw entry={Id}", entry.Id);
                return new DownloadResult.OpenError(Path.GetFileName(finalPath));
            }

            return new DownloadResult.Success(Path.GetFileName(finalPath));
        }

        // ---- Opener injection -------------------------------------------------
        // MainWindow registers OpenDeeperFile on startup so the service can
        // open files into the Deeper player without taking a reference to
        // MainWindow itself. Kept Func-based so future callers (e.g. tests or
        // a different host window) can swap it.
        private Func<string, bool>? _opener;

        public void SetOpener(Func<string, bool> opener) => _opener = opener;

        // ---- Filename helpers -------------------------------------------------

        private static readonly Regex InvalidFsChars = new(@"[<>:""/\\|?*\x00-\x1f]", RegexOptions.Compiled);

        // Windows reserved device names. Treat as empty → fall back to UUID.
        private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        };

        // Sanitize a creator-supplied title into a base filename (no extension).
        // Returns empty string if the result would be unsafe (reserved name,
        // empty after stripping, etc.) so the caller can fall back to UUID.
        private static string SanitizeForFilename(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            var s = InvalidFsChars.Replace(input, "");
            s = Regex.Replace(s, @"\s+", " ").Trim();
            s = s.Trim('.');
            if (s.Length > 80) s = s.Substring(0, 80).Trim();
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            if (ReservedNames.Contains(s)) return string.Empty;
            return s;
        }

        // Resolve a non-colliding full path: "<base>.ccpenh.json", then
        // "<base> (2).ccpenh.json", "(3)", … until we find a free slot.
        // Caps the suffix at 999 to avoid runaway loops — past that we give up
        // and let the caller report SaveError.
        private static string ResolveCollisionFreePath(string folder, string baseName)
        {
            var ext = Services.Deeper.DeeperLocalLibrary.FileSuffix; // ".ccpenh.json"
            var first = Path.Combine(folder, baseName + ext);
            if (!File.Exists(first)) return first;

            for (int i = 2; i <= 999; i++)
            {
                var candidate = Path.Combine(folder, $"{baseName} ({i}){ext}");
                if (!File.Exists(candidate)) return candidate;
            }
            // Astronomically unlikely; if we get here something is wrong with
            // the user's filesystem, but we'd rather throw than overwrite.
            throw new IOException($"Could not find a free filename for '{baseName}{ext}' in {folder}");
        }
    }

    /// <summary>
    /// Discriminated union for catalogue lookup outcomes. Pattern-match on the
    /// concrete subtype in caller code.
    /// </summary>
    public abstract record LookupResult
    {
        public sealed record Success(List<CatalogueEntry> Entries) : LookupResult;
        public sealed record None : LookupResult;
        public sealed record NetworkError : LookupResult;
        public sealed record InvalidUrl : LookupResult;
    }

    /// <summary>
    /// One catalogue entry as returned by /api/enhancements/by-ht-url. All
    /// fields are server-truth; the client doesn't enrich or transform
    /// (except for graceful defaults when fields are missing).
    /// </summary>
    public record CatalogueEntry(
        string Id,
        string Title,
        string Description,
        string CreatorName,
        string? RemixerName,
        List<string> Tags,
        string? License,
        int ViewCount,
        string HtUrl,
        string? ThumbnailPath,
        string FileUrl
    );

    public abstract record DownloadResult
    {
        public sealed record Success(string LocalFilename) : DownloadResult;
        public sealed record NetworkError : DownloadResult;
        public sealed record InvalidFile : DownloadResult;
        public sealed record SaveError : DownloadResult;
        public sealed record OpenError(string LocalFilename) : DownloadResult;
    }
}
