using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services.Fyp.Online;

/// <summary>
/// The one place remote media bytes are fetched, held and handed to a consumer. Two shapes,
/// because the app's asset consumers split cleanly in two (planning/remote-media/PLAN.md §2):
///
///   * <see cref="OpenAsync"/> — a <see cref="Stream"/> over in-memory bytes, for the
///     Contract-2 surfaces (<c>BitmapImage.StreamSource</c>, SKCodec). No disk at all.
///   * <see cref="MaterializeAsync"/> — a real path on local disk, for the Contract-1
///     surfaces that can only take one (LibVLC <c>FromPath</c>, <c>SPI_SETDESKWALLPAPER</c>,
///     <c>System.Drawing.Image.FromFile</c>).
///
/// THE BRIGHT LINE (<see cref="IFeedSource"/>): every byte here travels provider → this
/// machine. Nothing routes through, is cached on, or is re-served by CC Labs infrastructure.
/// The transient temp files below are on the USER'S own disk, which the owner explicitly
/// cleared on 2026-08-12 (decision 7) — that line is about our servers, not their drive.
///
/// TEMP FILES DELIBERATELY MIRROR THE CONTENT-PACK PATTERN
/// (<c>ContentPackService.GetPackFileTempPath</c>): same directory (<c>App.GetMediaTempPath()</c>,
/// i.e. <c>{EffectiveAssetsPath}/.temp</c>, so temp lives on the same drive as the library),
/// same <c>ccp_temp_</c> filename prefix, same "track them and clean up above N" rule, same
/// startup sweep. The prefix matters twice over: <c>App.CleanupStaleTempFiles</c> globs
/// <c>ccp_temp_*</c>, so a crash that skips every path below is still recovered on the next
/// launch, while the extra <c>remote_</c> segment lets <see cref="CleanupStaleTempFiles"/>
/// sweep only ours. A second temp scheme would have been a second thing to leak.
///
/// NOTHING HERE THROWS. A dead CDN, a DNS failure, a full disk and a cancelled token all
/// come back as null, because every caller's fallback is "no media" and a flash pipeline
/// that faults on a bad hotspot is worse than one that shows a local image instead.
/// </summary>
internal static class RemoteMediaCache
{
    // ---- bounds --------------------------------------------------------------------
    //
    // These hold COMPRESSED source bytes, not decoded pixels — a 1080p still is ~200-600KB
    // here versus ~8MB once WIC has it, and FlashService's own decode cache (50 entries /
    // 200MB) is what actually holds the expensive form. This cache only has to bridge
    // "prefetched" to "decoded", so it is deliberately an order of magnitude cheaper.
    // Both caps are enforced: a feed of stills would blow the entry count long before the
    // byte count, and one stray 4K png would do the reverse.

    private const int MaxEntries = 64;
    private const long MaxBytes = 64L * 1024 * 1024;

    /// <summary>Anything larger is refused outright rather than evicting the whole cache to
    /// hold it. Comfortably above any still (<see cref="ScrolllerSource"/> caps the source
    /// pick at 1920px) and above a short feed clip.</summary>
    private const long MaxSingleEntryBytes = 24L * 1024 * 1024;

    /// <summary>Tracked temp files above which the oldest get swept, matching the
    /// content-pack convention (<c>FlashService.GetNextImages</c> cleans above 50).</summary>
    private const int MaxTrackedTempFiles = 50;

    /// <summary>Filename prefix. The <c>ccp_temp_</c> half is load-bearing — see the class
    /// remarks — and <c>remote_</c> is what makes our sweep ours alone.</summary>
    private const string TempPrefix = "ccp_temp_remote_";

    private const int CopyBufferBytes = 81920;

    // ---- state ---------------------------------------------------------------------

    private sealed class CacheEntry
    {
        public byte[] Bytes = Array.Empty<byte>();
        public long Stamp;          // LRU: last touched, monotonically increasing
    }

    private static readonly object Gate = new();
    private static readonly Dictionary<string, CacheEntry> Cache = new(StringComparer.Ordinal);

    /// <summary>Single-flight: one download per URL no matter how many callers ask. A burst
    /// of flashes drawing from the same prefetched batch would otherwise fetch the same
    /// image once per flash.</summary>
    private static readonly Dictionary<string, Task<byte[]?>> InFlight = new(StringComparer.Ordinal);

    private static readonly List<string> TempFiles = new();

    private static long _bytes;
    private static long _tick;

    private static readonly HttpClient Http = CreateClient();

    /// <summary>Same browser-ish headers <see cref="ScrolllerSource"/> uses for the API. The
    /// CDN hotlinks fine without a Referer, but sending what its own site sends is the
    /// cheapest possible insurance against that changing.</summary>
    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36");
        c.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://scrolller.com/");
        c.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "image/webp,image/jpeg,image/png,video/*,*/*;q=0.8");
        return c;
    }

    // ---- public surface ------------------------------------------------------------

    /// <summary>
    /// In-memory bytes for Contract-2 consumers (<c>BitmapImage.StreamSource</c>, SKCodec).
    /// Null when the URL is not usable remote media, the fetch failed, or the token fired.
    ///
    /// Each caller gets its OWN <see cref="MemoryStream"/> over the shared (immutable) byte
    /// array — a single shared stream would have concurrent decoders fighting over one
    /// Position. The caller owns and disposes what it gets back; the bytes stay cached.
    /// </summary>
    public static async Task<Stream?> OpenAsync(string url, CancellationToken ct)
    {
        var bytes = await GetBytesAsync(url, ct).ConfigureAwait(false);
        if (bytes == null) return null;
        try
        {
            // writable:false — nobody may mutate the array the cache is still handing out.
            return new MemoryStream(bytes, 0, bytes.Length, writable: false, publiclyVisible: false);
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("[remote media] stream open failed for {Url}: {E}", url, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// A real file on local disk for Contract-1 consumers (LibVLC <c>FromPath</c>, wallpaper).
    /// Null on any failure. The file carries the source's extension because several consumers
    /// sniff the codec from it.
    ///
    /// The caller SHOULD hand the path back to <see cref="ReleaseTempFile"/> when it is done;
    /// if it forgets (or crashes), the cleanup-above-N rule and the startup sweep both still
    /// catch the file. A fresh file per call, exactly like the content-pack path — sharing one
    /// file between two players is how you get a locked handle mid-playback.
    /// </summary>
    public static async Task<string?> MaterializeAsync(string url, CancellationToken ct)
    {
        var bytes = await GetBytesAsync(url, ct).ConfigureAwait(false);
        if (bytes == null) return null;

        string? path = null;
        try
        {
            var ext = ExtensionOf(url) ?? ".bin";
            path = Path.Combine(App.GetMediaTempPath(), $"{TempPrefix}{Guid.NewGuid():N}{ext}");
            await File.WriteAllBytesAsync(path, bytes, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // A half-written file is worse than none: a video player would open it and stall.
            TryDelete(path);
            App.Logger?.Warning("[remote media] temp materialize failed for {Url}: {E}", url, ex.Message);
            return null;
        }

        TrackTempFile(path);
        return path;
    }

    /// <summary>
    /// Consumer is done with a materialized file. Ignores null, unknown and non-ours paths, and
    /// never throws — a locked file (the player has not let go yet) is simply left for the
    /// startup sweep rather than retried on a timer.
    /// </summary>
    public static void ReleaseTempFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        // Only ever delete something we made. A caller that mixes a library path into its
        // release bookkeeping must not be able to delete the user's own file through us.
        if (!IsOurTempFile(path)) return;

        lock (Gate) TempFiles.Remove(path);
        TryDelete(path);
    }

    /// <summary>
    /// Startup sweep for temp files a crash left behind. Called from <c>App.OnStartup</c>
    /// beside <c>App.CleanupStaleTempFiles</c> — that one already globs <c>ccp_temp_*</c> and
    /// therefore covers ours, so this is belt to those braces AND the only sweep that can run
    /// again later in the session without touching content-pack temps.
    /// </summary>
    public static void CleanupStaleTempFiles()
    {
        int deleted = 0;
        try
        {
            var dir = App.GetMediaTempPath();
            if (!Directory.Exists(dir)) return;
            foreach (var file in Directory.GetFiles(dir, TempPrefix + "*"))
            {
                try { File.Delete(file); deleted++; }
                catch { /* still in use: App.CleanupStaleTempFiles gets it next launch */ }
            }
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("[remote media] stale temp sweep failed: {E}", ex.Message);
        }

        if (deleted > 0)
            App.Logger?.Information("[remote media] swept {Count} stale temp file(s)", deleted);
    }

    /// <summary>
    /// True when the bytes are already in memory, so a consumer on the UI thread can pick this
    /// URL knowing the decode will not wait on a network round trip. Prefetchers use this to
    /// decide what is safe to put in a pool.
    /// </summary>
    public static bool IsCached(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        lock (Gate) return Cache.ContainsKey(url);
    }

    /// <summary>
    /// Warm one URL without allocating a stream for it. Returns true when the bytes are
    /// resident afterwards. This is the call a prefetcher makes; every consumer path stays
    /// synchronous-feeling because of it.
    /// </summary>
    public static async Task<bool> PrefetchAsync(string url, CancellationToken ct)
        => await GetBytesAsync(url, ct).ConfigureAwait(false) != null;

    /// <summary>Drop every cached byte (memory pressure, session teardown). Temp files are
    /// untouched — someone may still be playing one.</summary>
    public static void ClearMemory()
    {
        lock (Gate)
        {
            Cache.Clear();
            _bytes = 0;
        }
    }

    // ---- fetch ---------------------------------------------------------------------

    /// <summary>Cache hit, join an in-flight download, or start one. Never throws.</summary>
    private static async Task<byte[]?> GetBytesAsync(string url, CancellationToken ct)
    {
        if (!IsUsable(url))
        {
            App.Logger?.Debug("[remote media] refused non-media url {Url}", url);
            return null;
        }

        try
        {
            var task = GetOrStart(url);
            // The shared download is NOT bound to this caller's token — a second consumer
            // may still want the bytes after the first gives up, and a cancelled prefetch
            // that poisoned the download for everyone else would be a nasty intermittent.
            // WaitAsync detaches THIS caller instead, leaving the fetch to finish and cache.
            return await task.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("[remote media] fetch of {Url} failed: {E}", url, ex.Message);
            return null;
        }
    }

    private static Task<byte[]?> GetOrStart(string url)
    {
        lock (Gate)
        {
            if (Cache.TryGetValue(url, out var hit))
            {
                hit.Stamp = ++_tick;
                return Task.FromResult<byte[]?>(hit.Bytes);
            }
            if (InFlight.TryGetValue(url, out var running)) return running;

            // Task.Run so the synchronous head of DownloadAsync never runs under Gate.
            var started = Task.Run(() => DownloadAsync(url));
            InFlight[url] = started;
            return started;
        }
    }

    private static async Task<byte[]?> DownloadAsync(string url)
    {
        try
        {
            // ResponseHeadersRead so an oversized body is refused on Content-Length rather
            // than after we have already buffered all of it.
            using var resp = await Http
                .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None)
                .ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                App.Logger?.Debug("[remote media] HTTP {Code} for {Url}", (int)resp.StatusCode, url);
                return null;
            }

            long declared = resp.Content.Headers.ContentLength ?? -1;
            if (declared > MaxSingleEntryBytes)
            {
                App.Logger?.Debug("[remote media] {Url} is {Size} bytes — over the per-item cap, skipped",
                    url, declared);
                return null;
            }

            var bytes = await ReadBoundedAsync(resp, url).ConfigureAwait(false);
            if (bytes == null || bytes.Length == 0) return null;

            Store(url, bytes);
            return bytes;
        }
        catch (Exception ex)
        {
            // Includes the HttpClient timeout (surfaces as TaskCanceledException) and every
            // transport failure. One log line at Debug: a flaky CDN must not spam the log.
            App.Logger?.Debug("[remote media] download of {Url} failed: {E}", url, ex.Message);
            return null;
        }
        finally
        {
            lock (Gate) InFlight.Remove(url);
        }
    }

    /// <summary>Body → bytes, abandoning anything that grows past the per-item cap mid-stream
    /// (a chunked response has no Content-Length to check up front).</summary>
    private static async Task<byte[]?> ReadBoundedAsync(HttpResponseMessage resp, string url)
    {
        using var src = await resp.Content.ReadAsStreamAsync(CancellationToken.None).ConfigureAwait(false);
        long declared = resp.Content.Headers.ContentLength ?? -1;
        // Right-sized up front when the server told us; grown on demand when it didn't.
        using var buffer = declared > 0 && declared <= MaxSingleEntryBytes
            ? new MemoryStream((int)declared)
            : new MemoryStream();

        var chunk = new byte[CopyBufferBytes];
        while (true)
        {
            int read = await src.ReadAsync(chunk.AsMemory(0, chunk.Length), CancellationToken.None).ConfigureAwait(false);
            if (read <= 0) break;
            if (buffer.Length + read > MaxSingleEntryBytes)
            {
                App.Logger?.Debug("[remote media] {Url} exceeded the per-item cap mid-stream, dropped", url);
                return null;
            }
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    /// <summary>Insert and evict least-recently-used until both caps hold.</summary>
    private static void Store(string url, byte[] bytes)
    {
        lock (Gate)
        {
            if (Cache.TryGetValue(url, out var existing))
            {
                // A racing duplicate download: release the old accounting before replacing,
                // or _bytes drifts upward forever and the cache silently shrinks to nothing.
                _bytes -= existing.Bytes.LongLength;
                Cache.Remove(url);
            }

            while (Cache.Count > 0 && (Cache.Count + 1 > MaxEntries || _bytes + bytes.LongLength > MaxBytes))
            {
                string? oldest = null;
                long oldestStamp = long.MaxValue;
                foreach (var kvp in Cache)
                {
                    if (kvp.Value.Stamp < oldestStamp) { oldestStamp = kvp.Value.Stamp; oldest = kvp.Key; }
                }
                if (oldest == null) break;
                _bytes -= Cache[oldest].Bytes.LongLength;
                Cache.Remove(oldest);
            }

            Cache[url] = new CacheEntry { Bytes = bytes, Stamp = ++_tick };
            _bytes += bytes.LongLength;
        }
    }

    // ---- temp bookkeeping ----------------------------------------------------------

    private static void TrackTempFile(string path)
    {
        List<string>? sweep = null;
        lock (Gate)
        {
            TempFiles.Add(path);
            int over = TempFiles.Count - MaxTrackedTempFiles;
            if (over > 0)
            {
                // Oldest-first, never the ones just handed out: those are the likeliest to be
                // open in a player right now. A delete that fails is dropped from tracking
                // anyway — retrying it every call would burn I/O on a file we cannot have, and
                // the ccp_temp_ prefix means the next launch sweeps it regardless.
                sweep = TempFiles.GetRange(0, over);
                TempFiles.RemoveRange(0, over);
            }
        }
        if (sweep == null) return;
        foreach (var stale in sweep) TryDelete(stale);
    }

    private static void TryDelete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { App.Logger?.Debug("[remote media] temp delete failed: {E}", ex.Message); }
    }

    private static bool IsOurTempFile(string path)
    {
        try { return Path.GetFileName(path).StartsWith(TempPrefix, StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    // ---- url validation ------------------------------------------------------------

    /// <summary>
    /// Gate every fetch through <see cref="RemoteMediaFormats"/> so this cache can never be
    /// turned into a general-purpose downloader by a caller that hands it an arbitrary URL.
    /// Absolute http(s) only, and only the containers the app can actually render.
    /// </summary>
    private static bool IsUsable(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
        return RemoteMediaFormats.IsRemoteImage(url) || RemoteMediaFormats.IsRemoteVideo(url);
    }

    /// <summary>The source extension, query/fragment stripped, or null. Consumers that sniff
    /// a codec from the filename need this to survive the trip through temp.</summary>
    private static string? ExtensionOf(string url)
    {
        try
        {
            int cut = url.IndexOfAny(new[] { '?', '#' });
            var clean = cut >= 0 ? url[..cut] : url;
            var ext = Path.GetExtension(clean);
            return string.IsNullOrEmpty(ext) ? null : ext.ToLowerInvariant();
        }
        catch { return null; }
    }
}
