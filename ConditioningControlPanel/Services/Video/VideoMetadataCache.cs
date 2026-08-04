using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LibVLCSharp.Shared;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// On-disk cache of per-video duration in seconds, keyed by path+size+mtime
    /// (fallback to path+size when mtime can't be read — network drives, sync
    /// clients, etc). Avoids re-parsing every video at refill time, and falls
    /// open so cache misses or LibVLC parse failures never block playback or
    /// filtering — callers treat missing entries as "include the video, queue
    /// a background fetch."
    /// </summary>
    public class VideoMetadataCache
    {
        private static string CacheFilePath => Path.Combine(App.UserDataPath, "video_metadata.json");

        private const int ParseTimeoutMs = 5000;
        /// <summary>
        /// Delay between a parse finishing and releasing its Media. LibVLC raises the
        /// parsed event from its own worker thread and can resume us inline on it while
        /// still inside that media's event manager — releasing there frees the critical
        /// section under the sender, which is the native 0xC0000005 behind #750-#753.
        /// </summary>
        private const int DisposeGraceMs = 1500;
        private const int ReaperPeriodMs = 1000;
        private const int SaveDebounceMs = 3000;
        /// <summary>How many grace extensions a media gets before we leak it instead of freeing a live one.</summary>
        private const int MaxDisposeDeferrals = 20;

        private readonly ConcurrentDictionary<string, double> _byKey = new();
        /// <summary>Keys LibVLC couldn't give us a duration for. In-memory only, so a restart retries.</summary>
        private readonly ConcurrentDictionary<string, byte> _unparseable = new();
        private readonly ConcurrentDictionary<string, byte> _inFlight = new();
        private readonly SemaphoreSlim _parseGate = new(1, 1);
        private readonly ConcurrentQueue<CondemnedMedia> _condemned = new();
        private readonly object _reaperLock = new();
        private readonly object _saveTimerLock = new();
        private Timer? _reaper;
        private Timer? _saveTimer;
        private int _reaping;
        private readonly LibVLC _libVlc;
        private readonly object _saveLock = new();
        private bool _dirty;

        public VideoMetadataCache(LibVLC libVlc)
        {
            _libVlc = libVlc;
            Load();
        }

        private sealed class CondemnedMedia
        {
            public CondemnedMedia(Media media, DateTime notBeforeUtc)
            {
                Media = media;
                NotBeforeUtc = notBeforeUtc;
            }

            public Media Media { get; }
            public DateTime NotBeforeUtc { get; set; }
            public int Deferrals { get; set; }
        }

        /// <summary>
        /// Returns the cached duration in seconds, or null if unknown. Never
        /// throws; cache-key failures degrade to "unknown" rather than block.
        /// </summary>
        public double? TryGetDuration(string path)
        {
            var key = ComputeKey(path);
            if (key != null && _byKey.TryGetValue(key, out var seconds)) return seconds;
            return null;
        }

        /// <summary>
        /// Fetches the duration, parsing via LibVLC if not cached. Returns
        /// null on parse failure, and also when a parse for this path is already
        /// queued — callers treat that exactly like a cache miss. Parses are
        /// serialized, so this can take a while; nothing waits on the result.
        /// </summary>
        public async Task<double?> GetOrComputeDurationAsync(string path)
        {
            var key = ComputeKey(path);
            if (key == null) return null;
            if (_byKey.TryGetValue(key, out var cached)) return cached;
            if (_unparseable.ContainsKey(key)) return null;
            // A refill fires one of these per uncached video (24+ on a cold cache) and
            // does it again on the next refill. Collapse repeats so the queue behind the
            // gate can't grow past one entry per distinct video.
            if (!_inFlight.TryAdd(key, 0)) return null;

            try
            {
                // One parse at a time: every concurrent preparse is another live media
                // whose event manager we might free out from under it (#750-#753), for
                // metadata that only affects the *next* refill.
                await _parseGate.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (_byKey.TryGetValue(key, out cached)) return cached;
                    return await ParseDurationAsync(path, key).ConfigureAwait(false);
                }
                finally
                {
                    _parseGate.Release();
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("VideoMetadataCache: parse failed for {Path}: {Error}", path, ex.Message);
                return null;
            }
            finally
            {
                _inFlight.TryRemove(key, out _);
                ReapCondemned();
            }
        }

        /// <summary>
        /// Record a duration learned WITHOUT a LibVLC parse — the browser video engine's page
        /// <c>meta</c> message. Every browser session therefore warms the selection-time duration
        /// filter for free. Ignores non-positive durations (the page reports 0 for "unknowable")
        /// and never throws.
        /// </summary>
        public void StoreDuration(string path, double seconds)
        {
            if (seconds <= 0) return;
            try
            {
                var key = ComputeKey(path);
                if (key == null) return;
                if (_byKey.TryGetValue(key, out var existing) && Math.Abs(existing - seconds) < 0.5) return;
                _byKey[key] = seconds;
                _unparseable.TryRemove(key, out _);
                _dirty = true;
                ScheduleSave();
            }
            catch (Exception ex) { App.Logger?.Debug("VideoMetadataCache: StoreDuration failed for {Path}: {Error}", path, ex.Message); }
        }

        private async Task<double?> ParseDurationAsync(string path, string key)
        {
            Media? media = null;
            try
            {
                media = new Media(_libVlc, path, FromType.FromPath);
                var result = await media.Parse(MediaParseOptions.ParseLocal, timeout: ParseTimeoutMs)
                    .ConfigureAwait(false);

                // LibVLCSharp completes the parse task from the LibVLC event thread and can
                // resume us inline on it. Get off that thread before touching the media
                // again — see DisposeGraceMs.
                await Task.Yield();

                if (result == MediaParsedStatus.Done && media.Duration > 0)
                {
                    var seconds = media.Duration / 1000.0;
                    _byKey[key] = seconds;
                    _dirty = true;
                    ScheduleSave();
                    return seconds;
                }
                _unparseable[key] = 0;
            }
            catch (Exception ex)
            {
                _unparseable[key] = 0;
                App.Logger?.Debug("VideoMetadataCache: parse failed for {Path}: {Error}", path, ex.Message);
            }
            finally
            {
                if (media != null) Condemn(media);
            }
            return null;
        }

        /// <summary>
        /// Hands a finished-with media to the reaper instead of disposing it here. Never
        /// dispose a Media on the heels of its own parse: the preparser may still be
        /// inside its event manager, and the release frees that memory under it.
        /// </summary>
        private void Condemn(Media media)
        {
            try
            {
                var status = media.ParsedStatus;
                // Anything short of a finished parse may still be running - ask it to stop.
                // ParseStop is asynchronous too; the grace below is what actually waits.
                if (status != MediaParsedStatus.Done && status != MediaParsedStatus.Failed)
                    media.ParseStop();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("VideoMetadataCache: ParseStop failed: {Error}", ex.Message);
            }

            _condemned.Enqueue(new CondemnedMedia(media, DateTime.UtcNow.AddMilliseconds(DisposeGraceMs)));
            EnsureReaper();
        }

        /// <summary>
        /// Disposes condemned media whose grace has elapsed and whose parse has settled.
        /// Anything still unsettled gets another grace period, and after
        /// <see cref="MaxDisposeDeferrals"/> of those we drop the reference without
        /// disposing — a leaked native media is survivable, freeing a live one is not.
        /// (LibVLCSharp's Media has no finalizer, so dropping it really does leak-not-free.)
        /// </summary>
        private void ReapCondemned()
        {
            if (_condemned.IsEmpty) return;
            if (Interlocked.CompareExchange(ref _reaping, 1, 0) != 0) return;
            try
            {
                var now = DateTime.UtcNow;
                var carry = new List<CondemnedMedia>();
                var pass = _condemned.Count;
                for (var i = 0; i < pass && _condemned.TryDequeue(out var item); i++)
                {
                    if (item.NotBeforeUtc > now)
                    {
                        carry.Add(item);
                        continue;
                    }
                    if (!IsParseSettled(item.Media))
                    {
                        if (++item.Deferrals >= MaxDisposeDeferrals)
                        {
                            App.Logger?.Debug("VideoMetadataCache: leaking a media whose parse never settled");
                            continue;
                        }
                        item.NotBeforeUtc = now.AddMilliseconds(DisposeGraceMs);
                        carry.Add(item);
                        continue;
                    }
                    try { item.Media.Dispose(); }
                    catch (Exception ex) { App.Logger?.Debug("VideoMetadataCache: media dispose failed: {Error}", ex.Message); }
                }
                foreach (var c in carry) _condemned.Enqueue(c);
            }
            finally
            {
                Interlocked.Exchange(ref _reaping, 0);
            }

            if (_condemned.IsEmpty)
            {
                lock (_reaperLock)
                {
                    if (_condemned.IsEmpty)
                    {
                        _reaper?.Dispose();
                        _reaper = null;
                    }
                }
            }
        }

        /// <summary>True once LibVLC has reported a terminal parse status for this media.</summary>
        private static bool IsParseSettled(Media media)
        {
            try
            {
                var status = media.ParsedStatus;
                return status == MediaParsedStatus.Done
                    || status == MediaParsedStatus.Failed
                    || status == MediaParsedStatus.Timeout
                    || status == MediaParsedStatus.Skipped;
            }
            catch
            {
                return false;
            }
        }

        private void EnsureReaper()
        {
            lock (_reaperLock)
            {
                _reaper ??= new Timer(_ =>
                {
                    try { ReapCondemned(); }
                    catch (Exception ex) { App.Logger?.Debug("VideoMetadataCache: reaper failed: {Error}", ex.Message); }
                }, null, ReaperPeriodMs, ReaperPeriodMs);
            }
        }

        /// <summary>Debounced write-behind so a warmed cache actually survives to the next launch.</summary>
        private void ScheduleSave()
        {
            lock (_saveTimerLock)
            {
                if (_saveTimer == null)
                {
                    _saveTimer = new Timer(_ =>
                    {
                        try { Save(); }
                        catch (Exception ex) { App.Logger?.Debug("VideoMetadataCache: deferred save failed: {Error}", ex.Message); }
                    }, null, SaveDebounceMs, Timeout.Infinite);
                }
                else
                {
                    _saveTimer.Change(SaveDebounceMs, Timeout.Infinite);
                }
            }
        }

        /// <summary>
        /// Best-effort prewarm. Runs sequentially on a background task; misses
        /// are silent. Saves the cache at the end.
        /// </summary>
        public async Task PrewarmAsync(IEnumerable<string> paths)
        {
            foreach (var p in paths)
            {
                try { await GetOrComputeDurationAsync(p); }
                catch { /* best-effort */ }
            }
            Save();
        }

        public void Save()
        {
            if (!_dirty) return;
            lock (_saveLock)
            {
                if (!_dirty) return;
                try
                {
                    Directory.CreateDirectory(App.UserDataPath);
                    var snapshot = _byKey.ToDictionary(kv => kv.Key, kv => kv.Value);
                    var json = JsonConvert.SerializeObject(snapshot, Formatting.Indented);
                    File.WriteAllText(CacheFilePath, json);
                    _dirty = false;
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "VideoMetadataCache: save failed");
                }
            }
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(CacheFilePath)) return;
                var json = File.ReadAllText(CacheFilePath);
                var loaded = JsonConvert.DeserializeObject<Dictionary<string, double>>(json);
                if (loaded == null) return;
                foreach (var kv in loaded) _byKey[kv.Key] = kv.Value;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "VideoMetadataCache: load failed");
            }
        }

        /// <summary>
        /// Composite key: path|size|mtime. Falls back to path|size when the
        /// file timestamps can't be read (sync clients, network shares). Returns
        /// null if the path doesn't refer to an existing file.
        /// </summary>
        private static string? ComputeKey(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var info = new FileInfo(path);
                var size = info.Length;
                try
                {
                    var mtime = info.LastWriteTimeUtc.Ticks;
                    return $"{path}|{size}|{mtime}";
                }
                catch
                {
                    return $"{path}|{size}";
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
