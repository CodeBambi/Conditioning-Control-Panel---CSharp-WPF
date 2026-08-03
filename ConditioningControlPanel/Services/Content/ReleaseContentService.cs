using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services
{
    /// <summary>Coarse state of the release-content subsystem, for UI polling.</summary>
    public enum ReleaseContentState
    {
        /// <summary>Nothing in flight.</summary>
        Idle,
        /// <summary>Running as a full/dev layout (bundled audio present on disk) — packs are never fetched.</summary>
        FullInstall,
        /// <summary>Fetching content-manifest.json.</summary>
        CheckingManifest,
        /// <summary>Downloading a pack zip.</summary>
        Downloading,
        /// <summary>Verifying + extracting + merging a downloaded pack.</summary>
        Installing,
        /// <summary>Manifest could not be reached this session (offline / assets not published yet).</summary>
        Unavailable
    }

    /// <summary>Progress for a single pack download (0-100).</summary>
    public class PackProgressEventArgs : EventArgs
    {
        public string PackId { get; init; } = "";
        public double Percent { get; init; }
        public long BytesReceived { get; init; }
        public long TotalBytes { get; init; }
    }

    /// <summary>
    /// Downloads and installs the version-stable content packs that were pulled OUT of the installer
    /// (baseline avatar audio, web audio, per-mod media) and hosted as assets on the
    /// <c>vX.Y.0</c> GitHub release of each minor cycle.
    ///
    /// Design points (see <c>docs/CONTENT_PACKS_PLAN.md</c> §2, §3, §9-B):
    /// <list type="bullet">
    /// <item>The download URL is DERIVED from <see cref="UpdateService.AppVersion"/> — no GitHub API
    /// call, so no rate limits and no token.</item>
    /// <item>Packs are plain zips. This service deliberately does NOT reuse
    /// <see cref="ContentPackService"/>'s encryption (machine-bound AES + obfuscated filenames would
    /// break every direct-file-path consumer) — only its transport patterns: ranged/resumable
    /// downloads, retry with backoff, progress events.</item>
    /// <item>Everything lands under <c>%LOCALAPPDATA%\ConditioningControlPanel\content\</c>, mirroring
    /// the install-dir layout. The install dir is not writable under Program Files.</item>
    /// <item>Every public entry swallows its exceptions. Missing content degrades gracefully all over
    /// the app already; a failed fetch must never block startup or bubble into a caller.</item>
    /// </list>
    /// </summary>
    public class ReleaseContentService : IDisposable
    {
        // ---- Pack catalog (ids only — display strings belong to the UI wave's loc keys) ----

        public const string PackAudioBase = "audio-base";
        public const string PackAudioWeb = "audio-web";
        public const string PackModBambi = "mod-bambi";
        public const string PackModSissy = "mod-sissy";
        public const string PackModLocked = "mod-locked";
        public const string PackModDrone = "mod-drone";

        /// <summary>Audio group: baseline voice (auto) + web-engine audio (lazy).</summary>
        public static readonly IReadOnlyList<string> AudioPackIds = new[] { PackAudioBase, PackAudioWeb };

        /// <summary>Mod group: one pack per built-in mod, each carrying its own DTRH persona barks.</summary>
        public static readonly IReadOnlyList<string> ModPackIds = new[] { PackModBambi, PackModSissy, PackModLocked, PackModDrone };

        /// <summary>All six known pack ids, audio group first.</summary>
        public static readonly IReadOnlyList<string> AllPackIds =
            AudioPackIds.Concat(ModPackIds).ToArray();

        private const string ReleaseBaseUrlFormat =
            "https://github.com/CodeBambi/Conditioning-Control-Panel---CSharp-WPF/releases/download/{0}/";
        private const string ManifestFileName = "content-manifest.json";

        /// <summary>Relative path (under the app's base dir) whose presence means "full install — nothing to fetch".</summary>
        private static readonly string LegacyAudioProbe = Path.Combine("Resources", "sounds", "flashes_audio");

        private const int MaxDownloadAttempts = 10;

        private readonly HttpClient _httpClient;
        private readonly ConcurrentDictionary<string, Task<bool>> _inFlight = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _catalogLock = new();
        private List<ContentPackInfo> _catalog = new();
        private string? _resolvedBaseUrl;
        private bool _manifestUnavailableThisSession;
        private bool _disposed;

        // ---- State the UI can poll ----

        /// <summary>Coarse subsystem state. Poll-friendly; also raised via <see cref="StateChanged"/>.</summary>
        public ReleaseContentState State { get; private set; } = ReleaseContentState.Idle;

        /// <summary>Pack id currently downloading/installing, or null.</summary>
        public string? CurrentPackId { get; private set; }

        /// <summary>Progress (0-100) of <see cref="CurrentPackId"/>.</summary>
        public double CurrentPercent { get; private set; }

        /// <summary>Last successfully fetched manifest (empty until <see cref="FetchManifestAsync"/> succeeds).</summary>
        public IReadOnlyList<ContentPackInfo> Catalog
        {
            get { lock (_catalogLock) return _catalog.ToList(); }
        }

        /// <summary>Root of downloaded content: <c>%LOCALAPPDATA%\ConditioningControlPanel\content</c>.</summary>
        public static string ContentRoot => ContentLocator.ContentRoot;

        /// <summary>Scratch area for .partial zips and extract dirs (a hidden sibling under <see cref="ContentRoot"/>).</summary>
        private static string DownloadsRoot => Path.Combine(ContentRoot, ".downloads");

        /// <summary>Cycle tag this build fetches from, e.g. <c>v6.6.0</c> for app version 6.6.3.</summary>
        public string CycleTag { get; }

        /// <summary>Release asset base URL for <see cref="CycleTag"/> (trailing slash included).</summary>
        public string BaseUrl => string.Format(ReleaseBaseUrlFormat, CycleTag);

        /// <summary>
        /// True when the bundled (pre-modularization) audio is still present next to the exe — a dev
        /// build or a full/legacy install. Nothing is ever downloaded in that case: local files win the
        /// path probe anyway, so fetching would only waste the user's bandwidth.
        /// </summary>
        public bool IsFullInstall { get; }

        public event EventHandler<PackProgressEventArgs>? PackProgressChanged;

        /// <summary>Raised (pack id) after a pack is verified, extracted, merged and stamped.</summary>
        public event EventHandler<string>? PackInstalled;

        public event EventHandler<ReleaseContentState>? StateChanged;

        public ReleaseContentService()
        {
            _httpClient = new HttpClient
            {
                // Packs run to ~380 MB; the ranged-resume loop handles drops, but give a slow line room.
                Timeout = TimeSpan.FromMinutes(30)
            };
            try
            {
                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"ConditioningControlPanel/{UpdateService.AppVersion}");
            }
            catch { /* header parsing is best-effort */ }

            CycleTag = DeriveCycleTag(UpdateService.AppVersion);
            IsFullInstall = DetectFullInstall();

            if (IsFullInstall)
            {
                State = ReleaseContentState.FullInstall;
            }

            App.Logger?.Information(
                "ReleaseContentService: cycle {Cycle}, contentRoot {Root}, fullInstall {Full}",
                CycleTag, ContentRoot, IsFullInstall);
        }

        // ---------------------------------------------------------------- version / layout

        /// <summary>
        /// "6.6.3" -> "v6.6.0". Falls back to the raw version prefixed with 'v' if it will not parse
        /// (a broken tag 404s and is handled by the manifest fallback, so this never throws).
        /// </summary>
        internal static string DeriveCycleTag(string appVersion)
        {
            try
            {
                var parts = (appVersion ?? "").Split('.');
                if (parts.Length >= 2
                    && int.TryParse(parts[0], out var major)
                    && int.TryParse(parts[1], out var minor))
                {
                    return $"v{major}.{minor}.0";
                }
            }
            catch { }
            return $"v{appVersion}";
        }

        /// <summary>
        /// Cycle tag one minor behind, or null at X.0.0 (we do not guess the previous major's last
        /// minor — that would be a shot in the dark that always 404s).
        /// </summary>
        internal static string? DerivePreviousCycleTag(string cycleTag)
        {
            try
            {
                var trimmed = (cycleTag ?? "").TrimStart('v', 'V');
                var parts = trimmed.Split('.');
                if (parts.Length >= 2
                    && int.TryParse(parts[0], out var major)
                    && int.TryParse(parts[1], out var minor)
                    && minor > 0)
                {
                    return $"v{major}.{minor - 1}.0";
                }
            }
            catch { }
            return null;
        }

        private static bool DetectFullInstall()
        {
            try
            {
                var probe = Path.Combine(AppContext.BaseDirectory, LegacyAudioProbe);
                if (!Directory.Exists(probe)) return false;
                // An empty leftover folder is not a full install — Inno leaves dirs behind.
                return Directory.EnumerateFiles(probe, "*", SearchOption.AllDirectories).Any();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("ReleaseContentService: full-install probe failed: {Error}", ex.Message);
                return false;
            }
        }

        private void SetState(ReleaseContentState state, string? packId = null, double percent = 0)
        {
            State = state;
            CurrentPackId = packId;
            CurrentPercent = percent;
            try { StateChanged?.Invoke(this, state); } catch { }
        }

        // ---------------------------------------------------------------- manifest

        /// <summary>
        /// Fetches <c>content-manifest.json</c> for this cycle. On 404 (the X.Y.0 assets were never
        /// published, e.g. during a cycle's first patch) retries the previous minor once, then gives up
        /// QUIETLY: returns null, logs, and lets the next launch try again. Never throws.
        /// </summary>
        public async Task<IReadOnlyList<ContentPackInfo>?> FetchManifestAsync(CancellationToken ct = default)
        {
            try
            {
                if (App.Settings?.Current?.OfflineMode == true)
                {
                    App.Logger?.Information("ReleaseContentService: offline mode — skipping manifest fetch");
                    SetState(ReleaseContentState.Unavailable);
                    return null;
                }

                SetState(ReleaseContentState.CheckingManifest);

                var tags = new List<string> { CycleTag };
                var previous = DerivePreviousCycleTag(CycleTag);
                if (!string.IsNullOrEmpty(previous)) tags.Add(previous!);

                foreach (var tag in tags)
                {
                    var baseUrl = string.Format(ReleaseBaseUrlFormat, tag);
                    var url = baseUrl + ManifestFileName;
                    try
                    {
                        using var response = await _httpClient.GetAsync(url, ct).ConfigureAwait(false);
                        if (response.StatusCode == HttpStatusCode.NotFound)
                        {
                            App.Logger?.Information(
                                "ReleaseContentService: no content manifest on {Tag} (404) — trying older cycle if any", tag);
                            continue;
                        }
                        if (!response.IsSuccessStatusCode)
                        {
                            App.Logger?.Warning("ReleaseContentService: manifest fetch for {Tag} returned {Status}",
                                tag, response.StatusCode);
                            continue;
                        }

                        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                        var packs = ParseManifest(json);
                        if (packs == null || packs.Count == 0)
                        {
                            App.Logger?.Warning("ReleaseContentService: manifest for {Tag} parsed to zero packs", tag);
                            continue;
                        }

                        lock (_catalogLock) _catalog = packs;
                        _resolvedBaseUrl = baseUrl;
                        _manifestUnavailableThisSession = false;
                        SetState(ReleaseContentState.Idle);
                        App.Logger?.Information("ReleaseContentService: manifest OK from {Tag} ({Count} packs)",
                            tag, packs.Count);
                        return packs;
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        SetState(ReleaseContentState.Idle);
                        return null;
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Warning("ReleaseContentService: manifest fetch for {Tag} failed: {Error}", tag, ex.Message);
                    }
                }

                _manifestUnavailableThisSession = true;
                SetState(ReleaseContentState.Unavailable);
                App.Logger?.Information("ReleaseContentService: no content manifest available this session — retrying next launch");
                return null;
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "ReleaseContentService: FetchManifestAsync failed unexpectedly");
                SetState(ReleaseContentState.Unavailable);
                return null;
            }
        }

        /// <summary>Accepts both the envelope form ({ packs: [...] }) and a bare array.</summary>
        private static List<ContentPackInfo>? ParseManifest(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                var token = JToken.Parse(json);
                if (token is JArray array)
                    return array.ToObject<List<ContentPackInfo>>();

                var envelope = token.ToObject<ReleaseContentManifest>();
                return envelope?.Packs;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("ReleaseContentService: could not parse content manifest: {Error}", ex.Message);
                return null;
            }
        }

        /// <summary>Manifest entry for a pack id, or null when the manifest has not been fetched yet.</summary>
        public ContentPackInfo? GetPackInfo(string packId)
        {
            if (string.IsNullOrWhiteSpace(packId)) return null;
            lock (_catalogLock)
                return _catalog.FirstOrDefault(p => string.Equals(p.Id, packId, StringComparison.OrdinalIgnoreCase));
        }

        // ---------------------------------------------------------------- installed state

        private static Dictionary<string, InstalledPackStamp> Stamps
        {
            get
            {
                var map = App.Settings?.Current?.InstalledContentPacks;
                if (map == null) return new Dictionary<string, InstalledPackStamp>();
                return map;
            }
        }

        /// <summary>Persisted install stamp for a pack, or null.</summary>
        public InstalledPackStamp? GetStamp(string packId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(packId)) return null;
                return Stamps.TryGetValue(packId, out var stamp) ? stamp : null;
            }
            catch { return null; }
        }

        /// <summary>
        /// True when the pack has an install stamp (and, when we know its target folder, that folder
        /// still exists on disk — a user who nuked <c>content\</c> should re-download).
        /// </summary>
        public bool IsInstalled(string packId)
        {
            try
            {
                var stamp = GetStamp(packId);
                if (stamp == null) return false;

                var info = GetPackInfo(packId);
                if (info == null) return true; // manifest not fetched — trust the stamp

                var target = ResolveTargetDirectory(info);
                if (target == null) return true;
                return Directory.Exists(target);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("ReleaseContentService: IsInstalled({Pack}) failed: {Error}", packId, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// True when the manifest advertises different bytes than what is stamped locally. Always false
        /// when the manifest has not been fetched (we never claim staleness we cannot prove).
        /// </summary>
        public bool NeedsUpdate(string packId)
        {
            try
            {
                var info = GetPackInfo(packId);
                if (info == null) return false;

                var stamp = GetStamp(packId);
                if (stamp == null) return false; // not installed at all — that is IsInstalled's job

                if (stamp.ContentVersion != info.ContentVersion) return true;
                if (!string.IsNullOrEmpty(info.Sha256)
                    && !string.Equals(stamp.Sha256, info.Sha256, StringComparison.OrdinalIgnoreCase))
                    return true;

                return false;
            }
            catch { return false; }
        }

        // ---------------------------------------------------------------- download / install

        /// <summary>
        /// Idempotent entry point for lazily-needed packs (e.g. <c>audio-web</c> before the first DTRH
        /// or Intake launch). Returns immediately when the pack is already installed and current, joins
        /// an in-flight download when one exists, and fetches the manifest first if needed.
        /// Never throws.
        /// </summary>
        public Task<bool> RequestPackAsync(string packId, IProgress<double>? progress = null, CancellationToken ct = default)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(packId)) return Task.FromResult(false);
                if (IsFullInstall) return Task.FromResult(true);
                if (IsInstalled(packId) && !NeedsUpdate(packId)) return Task.FromResult(true);

                return _inFlight.GetOrAdd(packId, id => RunRequestAsync(id, progress, ct));
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "ReleaseContentService: RequestPackAsync({Pack}) failed to start", packId);
                return Task.FromResult(false);
            }
        }

        private async Task<bool> RunRequestAsync(string packId, IProgress<double>? progress, CancellationToken ct)
        {
            try
            {
                if (GetPackInfo(packId) == null)
                {
                    await FetchManifestAsync(ct).ConfigureAwait(false);
                }
                if (GetPackInfo(packId) == null)
                {
                    App.Logger?.Information("ReleaseContentService: pack {Pack} not in manifest — nothing to fetch", packId);
                    return false;
                }
                if (IsInstalled(packId) && !NeedsUpdate(packId)) return true;

                return await DownloadPackAsync(packId, progress, ct).ConfigureAwait(false);
            }
            finally
            {
                _inFlight.TryRemove(packId, out _);
            }
        }

        /// <summary>
        /// Downloads, verifies and installs a single pack. Resumable across app restarts (the
        /// <c>.partial</c> file survives), sha256-verified (mismatch = delete and one clean retry),
        /// extracted to a temp sibling and then merged into <c>content\{targetRoot}</c>.
        /// Returns true on success. Never throws — a false return plus a log line is the whole contract.
        /// </summary>
        public async Task<bool> DownloadPackAsync(string packId, IProgress<double>? progress = null, CancellationToken ct = default)
        {
            var info = GetPackInfo(packId);
            if (info == null)
            {
                App.Logger?.Warning("ReleaseContentService: DownloadPackAsync({Pack}) — no manifest entry", packId);
                return false;
            }
            if (string.IsNullOrWhiteSpace(info.File))
            {
                App.Logger?.Warning("ReleaseContentService: manifest entry {Pack} has no file name", packId);
                return false;
            }
            if (App.Settings?.Current?.OfflineMode == true)
            {
                App.Logger?.Information("ReleaseContentService: offline mode — pack {Pack} download skipped", packId);
                return false;
            }

            var baseUrl = _resolvedBaseUrl ?? BaseUrl;
            var url = baseUrl + Uri.EscapeDataString(info.File);
            var partialPath = Path.Combine(DownloadsRoot, info.File + ".partial");
            var extractPath = Path.Combine(DownloadsRoot, info.Id + ".extract");

            try
            {
                if (!TryCreateDirectory(DownloadsRoot)) return false;

                // Pass 1: normal (possibly resumed) download. Pass 2 only runs on a sha256 mismatch,
                // and starts from zero bytes — a corrupt partial cannot be repaired by resuming.
                for (var pass = 1; pass <= 2; pass++)
                {
                    SetState(ReleaseContentState.Downloading, packId, 0);

                    var ok = await DownloadToPartialAsync(info, url, partialPath, progress, ct).ConfigureAwait(false);
                    if (!ok) return false;

                    if (!string.IsNullOrEmpty(info.Sha256))
                    {
                        SetState(ReleaseContentState.Installing, packId, 100);
                        var actual = await ComputeSha256Async(partialPath, ct).ConfigureAwait(false);
                        if (!string.Equals(actual, info.Sha256, StringComparison.OrdinalIgnoreCase))
                        {
                            App.Logger?.Warning(
                                "ReleaseContentService: sha256 mismatch for {Pack} (expected {Expected}, got {Actual}) — pass {Pass}",
                                packId, info.Sha256, actual, pass);
                            TryDeleteFile(partialPath);
                            if (pass == 2)
                            {
                                App.Logger?.Error("ReleaseContentService: pack {Pack} failed verification twice — giving up", packId);
                                SetState(ReleaseContentState.Idle);
                                return false;
                            }
                            continue; // one clean re-download
                        }
                    }

                    break;
                }

                // ---- install: extract to a temp sibling, then merge into content\{targetRoot} ----
                SetState(ReleaseContentState.Installing, packId, 100);

                TryDeleteDirectory(extractPath);
                await Task.Run(() => ZipFile.ExtractToDirectory(partialPath, extractPath, overwriteFiles: true), ct)
                    .ConfigureAwait(false);

                var target = ResolveTargetDirectory(info);
                if (target == null)
                {
                    App.Logger?.Error("ReleaseContentService: pack {Pack} has an unsafe targetRoot '{Root}' — refusing to install",
                        packId, info.TargetRoot);
                    TryDeleteDirectory(extractPath);
                    TryDeleteFile(partialPath);
                    SetState(ReleaseContentState.Idle);
                    return false;
                }

                await Task.Run(() => MergeDirectory(extractPath, target), ct).ConfigureAwait(false);

                TryDeleteDirectory(extractPath);
                TryDeleteFile(partialPath);

                RecordInstalled(info);

                App.Logger?.Information("ReleaseContentService: installed pack {Pack} (v{Version}) into {Target}",
                    packId, info.ContentVersion, target);

                SetState(ReleaseContentState.Idle);
                try { PackInstalled?.Invoke(this, packId); } catch { }
                return true;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Leave the .partial in place — the next attempt resumes from it.
                App.Logger?.Information("ReleaseContentService: pack {Pack} download cancelled", packId);
                SetState(ReleaseContentState.Idle);
                return false;
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "ReleaseContentService: failed to install pack {Pack}", packId);
                TryDeleteDirectory(extractPath);
                SetState(ReleaseContentState.Idle);
                return false;
            }
        }

        /// <summary>
        /// Ranged/resumable download loop with backoff, modelled on ContentPackService's transport:
        /// the <c>.partial</c> file is never deleted on a transient failure, so a killed app (or a
        /// dropped connection) resumes where it left off. GitHub redirects release-asset URLs to
        /// objects.githubusercontent.com, which honours Range on the redirected request.
        /// </summary>
        private async Task<bool> DownloadToPartialAsync(
            ContentPackInfo info, string url, string partialPath, IProgress<double>? progress, CancellationToken ct)
        {
            var totalBytes = info.SizeBytes;
            var retryDelay = TimeSpan.FromSeconds(3);

            for (var attempt = 1; attempt <= MaxDownloadAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    long resumeFrom = 0;
                    if (File.Exists(partialPath))
                    {
                        resumeFrom = new FileInfo(partialPath).Length;
                        if (totalBytes > 0 && resumeFrom > totalBytes)
                        {
                            // Longer than advertised — the asset changed under us. Start clean.
                            App.Logger?.Warning("ReleaseContentService: partial for {Pack} is larger than the manifest size — restarting",
                                info.Id);
                            TryDeleteFile(partialPath);
                            resumeFrom = 0;
                        }
                        else if (totalBytes > 0 && resumeFrom == totalBytes)
                        {
                            // Already complete from a previous session; let the caller verify it.
                            ReportProgress(info.Id, 100, resumeFrom, totalBytes, progress);
                            return true;
                        }
                        else if (resumeFrom > 0)
                        {
                            App.Logger?.Information("ReleaseContentService: resuming {Pack} from byte {Byte} (attempt {Attempt})",
                                info.Id, resumeFrom, attempt);
                        }
                    }

                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    if (resumeFrom > 0)
                    {
                        request.Headers.Range = new RangeHeaderValue(resumeFrom, null);
                    }

                    using var response = await _httpClient
                        .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                        .ConfigureAwait(false);

                    if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
                    {
                        // The server thinks we already hold the whole file. Verify upstream.
                        App.Logger?.Information("ReleaseContentService: server reports {Pack} range complete", info.Id);
                        return true;
                    }
                    if (response.StatusCode == HttpStatusCode.NotFound)
                    {
                        App.Logger?.Warning("ReleaseContentService: pack asset 404 for {Pack} at {Url}", info.Id, url);
                        return false;
                    }
                    if (response.StatusCode != HttpStatusCode.OK && response.StatusCode != HttpStatusCode.PartialContent)
                    {
                        response.EnsureSuccessStatusCode();
                    }

                    // A 200 to a ranged request means the server ignored Range — rewrite from zero.
                    var serverHonouredRange = response.StatusCode == HttpStatusCode.PartialContent;
                    if (resumeFrom > 0 && !serverHonouredRange)
                    {
                        App.Logger?.Information("ReleaseContentService: server ignored Range for {Pack} — restarting download", info.Id);
                        resumeFrom = 0;
                    }

                    var contentRangeLength = response.Content.Headers.ContentRange?.Length;
                    var contentLength = response.Content.Headers.ContentLength;
                    if (contentRangeLength.HasValue)
                        totalBytes = contentRangeLength.Value;
                    else if (contentLength.HasValue)
                        totalBytes = resumeFrom + contentLength.Value;

                    var fileMode = resumeFrom > 0 ? FileMode.Append : FileMode.Create;
                    var received = resumeFrom;

                    using (var contentStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
                    using (var fileStream = new FileStream(partialPath, fileMode, FileAccess.Write, FileShare.None, 65536, useAsync: true))
                    {
                        var buffer = new byte[65536];
                        int read;
                        var lastReported = -1.0;
                        while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, read, ct).ConfigureAwait(false);
                            received += read;

                            if (totalBytes > 0)
                            {
                                var pct = Math.Min(100.0, received * 100.0 / totalBytes);
                                // Throttle to whole-percent steps — these events can reach the UI thread.
                                if (pct - lastReported >= 1.0 || pct >= 100.0)
                                {
                                    lastReported = pct;
                                    ReportProgress(info.Id, pct, received, totalBytes, progress);
                                }
                            }
                        }
                    }

                    var finalSize = new FileInfo(partialPath).Length;
                    if (totalBytes > 0 && finalSize < totalBytes)
                    {
                        throw new IOException($"Download incomplete: received {finalSize} of {totalBytes} bytes");
                    }

                    ReportProgress(info.Id, 100, finalSize, totalBytes > 0 ? totalBytes : finalSize, progress);
                    App.Logger?.Information("ReleaseContentService: downloaded {Pack} ({Bytes} bytes)", info.Id, finalSize);
                    return true;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (attempt < MaxDownloadAttempts
                                           && (ex is HttpRequestException || ex is TaskCanceledException || ex is IOException))
                {
                    var currentSize = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;
                    App.Logger?.Warning(
                        "ReleaseContentService: {Pack} attempt {Attempt}/{Max} failed at {Bytes} bytes: {Error}",
                        info.Id, attempt, MaxDownloadAttempts, currentSize, ex.Message);

                    // Keep the partial — the next attempt resumes from it.
                    await Task.Delay(retryDelay, ct).ConfigureAwait(false);
                    retryDelay = TimeSpan.FromSeconds(Math.Min(retryDelay.TotalSeconds * 1.5, 30));
                }
                catch (Exception ex)
                {
                    App.Logger?.Error(ex, "ReleaseContentService: {Pack} download failed permanently", info.Id);
                    return false;
                }
            }

            App.Logger?.Warning("ReleaseContentService: {Pack} exhausted {Max} download attempts", info.Id, MaxDownloadAttempts);
            return false;
        }

        private void ReportProgress(string packId, double percent, long received, long total, IProgress<double>? progress)
        {
            CurrentPackId = packId;
            CurrentPercent = percent;
            try { progress?.Report(percent); } catch { }
            try
            {
                PackProgressChanged?.Invoke(this, new PackProgressEventArgs
                {
                    PackId = packId,
                    Percent = percent,
                    BytesReceived = received,
                    TotalBytes = total
                });
            }
            catch { }
        }

        private static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
        {
            return await Task.Run(() =>
            {
                using var sha = SHA256.Create();
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
                var hash = sha.ComputeHash(stream);
                return Convert.ToHexString(hash).ToLowerInvariant();
            }, ct).ConfigureAwait(false);
        }

        // ---------------------------------------------------------------- filesystem helpers

        /// <summary>
        /// Absolute destination for a pack's payload, or null when <c>targetRoot</c> tries to escape
        /// the content root (defence against a tampered manifest).
        /// </summary>
        private static string? ResolveTargetDirectory(ContentPackInfo info)
        {
            try
            {
                var root = Path.GetFullPath(ContentRoot);
                if (string.IsNullOrWhiteSpace(info.TargetRoot)) return root;

                var rel = info.TargetRoot.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
                var full = Path.GetFullPath(Path.Combine(root, rel));

                var rootWithSep = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
                if (!full.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
                return full;
            }
            catch { return null; }
        }

        /// <summary>
        /// Moves <paramref name="source"/> onto <paramref name="target"/>. A plain
        /// <see cref="Directory.Move"/> when the target does not exist (both live under LOCALAPPDATA,
        /// so it is a same-volume rename); otherwise a recursive file-by-file overwrite so an update
        /// replaces changed files without orphaning the rest.
        /// </summary>
        private static void MergeDirectory(string source, string target)
        {
            if (!Directory.Exists(target))
            {
                var parent = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                Directory.Move(source, target);
                return;
            }

            foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(source, dir);
                Directory.CreateDirectory(Path.Combine(target, rel));
            }

            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(source, file);
                var dest = Path.Combine(target, rel);
                var destDir = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);
                File.Copy(file, dest, overwrite: true);
            }
        }

        private void RecordInstalled(ContentPackInfo info)
        {
            try
            {
                var settings = App.Settings?.Current;
                if (settings == null) return;

                settings.InstalledContentPacks[info.Id] = new InstalledPackStamp
                {
                    ContentVersion = info.ContentVersion,
                    Sha256 = info.Sha256 ?? ""
                };
                App.Settings?.Save();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "ReleaseContentService: could not persist install stamp for {Pack}", info.Id);
            }
        }

        private static bool TryCreateDirectory(string path)
        {
            try
            {
                Directory.CreateDirectory(path);
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "ReleaseContentService: could not create {Path}", path);
                return false;
            }
        }

        private static void TryDeleteFile(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        private static void TryDeleteDirectory(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { }
        }

        // ---------------------------------------------------------------- startup

        /// <summary>
        /// Fire-and-forget at startup: makes sure the baseline avatar audio (<c>audio-base</c>) is on
        /// disk. No-ops on a full/dev layout, in offline mode, or when the pack is already stamped —
        /// so the common case costs zero network. <c>audio-web</c> and the mod packs stay lazy
        /// (<see cref="RequestPackAsync"/>). Never throws, never blocks startup.
        /// </summary>
        public async Task EnsureBaselineAsync(CancellationToken ct = default)
        {
            try
            {
                if (IsFullInstall)
                {
                    App.Logger?.Information("ReleaseContentService: full install detected (bundled audio present) — no packs needed");
                    return;
                }
                if (App.Settings?.Current?.OfflineMode == true)
                {
                    App.Logger?.Information("ReleaseContentService: offline mode — baseline check skipped");
                    return;
                }
                if (GetStamp(PackAudioBase) != null && IsInstalled(PackAudioBase))
                {
                    App.Logger?.Debug("ReleaseContentService: baseline audio already installed");
                    return;
                }

                App.Logger?.Information("ReleaseContentService: baseline audio missing — fetching manifest");
                var manifest = await FetchManifestAsync(ct).ConfigureAwait(false);
                if (manifest == null) return;

                await RequestPackAsync(PackAudioBase, null, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "ReleaseContentService: EnsureBaselineAsync failed — content stays absent this session");
            }
        }

        /// <summary>True when a manifest fetch was attempted and failed this session.</summary>
        public bool ManifestUnavailable => _manifestUnavailableThisSession;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _httpClient.Dispose(); } catch { }
        }
    }
}
