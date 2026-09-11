using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LibVLCSharp.Shared;
using NAudio.Wave;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Services.Deeper
{
    /// <summary>
    /// Answers "how long is this media?" for the Deeper library list without
    /// opening every file on every visit. Three sources, cheapest first:
    /// the enhancement file itself (the editor writes metadata.media_duration
    /// on save), this cache (a small JSON map next to the waveform cache, keyed
    /// by path + size + last-write so a re-encoded file re-probes), and finally
    /// a background probe of a local file. Remote sources are never probed.
    /// </summary>
    public static class MediaDurationCache
    {
        private const int MaxEntries = 4000;
        private const int VlcParseTimeoutMs = 5000;

        private static readonly object Gate = new();
        private static Dictionary<string, double>? _store;

        public static string StorePath => Path.Combine(AudioWaveformCache.CacheFolder, "media-durations.json");

        /// <summary>
        /// The mini timeline readout format: m:ss under an hour, h:mm:ss from
        /// an hour up. Negative and NaN read as 0:00; callers that want an
        /// empty string for "unknown" check for a positive value first.
        /// </summary>
        public static string Format(double seconds)
        {
            if (seconds < 0 || double.IsNaN(seconds) || double.IsInfinity(seconds)) seconds = 0;
            var ts = TimeSpan.FromSeconds(seconds);
            return ts.TotalHours >= 1
                ? $"{(int)ts.TotalHours}:{ts.Minutes:00}:{ts.Seconds:00}"
                : $"{ts.Minutes}:{ts.Seconds:00}";
        }

        /// <summary>
        /// True when the source is a local file that exists on disk. URLs
        /// (hypnotube, bambicloud, anything http) and missing files are not
        /// probeable; those rows only get a duration the enhancement file carries.
        /// </summary>
        public static bool IsProbeable(string? mediaSource)
        {
            if (string.IsNullOrWhiteSpace(mediaSource)) return false;
            if (mediaSource.Contains("://", StringComparison.Ordinal)) return false;
            if (mediaSource.EndsWith("*", StringComparison.Ordinal)) return false;
            try { return Path.IsPathRooted(mediaSource) && File.Exists(mediaSource); }
            catch { return false; }
        }

        /// <summary>Cached duration for a local file, if the file is unchanged since it was probed.</summary>
        public static bool TryGetCached(string? mediaPath, out double seconds)
        {
            seconds = 0;
            if (!IsProbeable(mediaPath)) return false;
            try
            {
                var key = KeyFor(mediaPath!);
                lock (Gate)
                {
                    var store = LoadStoreLocked();
                    return store.TryGetValue(key, out seconds) && seconds > 0;
                }
            }
            catch (Exception ex)
            {
                Diag.Swallowed(ex);
                return false;
            }
        }

        /// <summary>Records a known duration for a local file and flushes to disk off the caller's thread.</summary>
        public static void Remember(string? mediaPath, double seconds)
        {
            if (seconds <= 0 || double.IsNaN(seconds) || !IsProbeable(mediaPath)) return;
            try
            {
                var key = KeyFor(mediaPath!);
                lock (Gate)
                {
                    var store = LoadStoreLocked();
                    if (store.TryGetValue(key, out var existing) && Math.Abs(existing - seconds) < 0.5) return;
                    store[key] = Math.Round(seconds, 1);
                    if (store.Count > MaxEntries)
                    {
                        foreach (var stale in store.Keys.Take(store.Count - MaxEntries).ToList())
                            store.Remove(stale);
                    }
                }
                _ = Task.Run(Flush);
            }
            catch (Exception ex) { Diag.Swallowed(ex); }
        }

        /// <summary>
        /// Reads the duration of a local file on a worker thread. MediaFoundation
        /// first (header read, no decode, same reader the waveform cache uses),
        /// then a LibVLC parse-only pass for containers MediaFoundation cannot
        /// open. Null when neither source knows.
        /// </summary>
        public static async Task<double?> ProbeAsync(string mediaPath)
        {
            if (!IsProbeable(mediaPath)) return null;
            var viaMf = await Task.Run(() => ProbeMediaFoundation(mediaPath)).ConfigureAwait(false);
            if (viaMf is > 0) return viaMf;
            return await ProbeLibVlcAsync(mediaPath).ConfigureAwait(false);
        }

        private static double? ProbeMediaFoundation(string path)
        {
            try
            {
                using var reader = new MediaFoundationReader(path);
                var seconds = reader.TotalTime.TotalSeconds;
                return seconds > 0 ? seconds : null;
            }
            catch (Exception ex)
            {
                Diag.Swallowed(ex);
                return null;
            }
        }

        private static async Task<double?> ProbeLibVlcAsync(string path)
        {
            try
            {
                var vlc = VideoService.SharedLibVLC;
                if (vlc == null) return null;
                using var media = new Media(vlc, path, FromType.FromPath);
                var status = await media.Parse(MediaParseOptions.ParseLocal, VlcParseTimeoutMs).ConfigureAwait(false);
                if (status != MediaParsedStatus.Done) return null;
                var ms = media.Duration;
                return ms > 0 ? ms / 1000.0 : null;
            }
            catch (Exception ex)
            {
                Diag.Swallowed(ex);
                return null;
            }
        }

        // -- store ----------------------------------------------------------

        internal static string KeyFor(string mediaPath)
        {
            var info = new FileInfo(mediaPath);
            return KeyFor(mediaPath, info.Length, info.LastWriteTimeUtc.Ticks);
        }

        internal static string KeyFor(string mediaPath, long length, long lastWriteUtcTicks)
            => string.Create(CultureInfo.InvariantCulture, $"{mediaPath.ToLowerInvariant()}|{length}|{lastWriteUtcTicks}");

        private static Dictionary<string, double> LoadStoreLocked()
        {
            if (_store != null) return _store;
            _store = new Dictionary<string, double>(StringComparer.Ordinal);
            try
            {
                if (File.Exists(StorePath))
                    _store = ParseStore(File.ReadAllText(StorePath));
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("MediaDurationCache: store read failed: {Error}", ex.Message);
            }
            return _store;
        }

        internal static Dictionary<string, double> ParseStore(string json)
        {
            var parsed = JsonConvert.DeserializeObject<Dictionary<string, double>>(json);
            var store = new Dictionary<string, double>(StringComparer.Ordinal);
            if (parsed == null) return store;
            foreach (var kv in parsed)
                if (!string.IsNullOrEmpty(kv.Key) && kv.Value > 0) store[kv.Key] = kv.Value;
            return store;
        }

        private static void Flush()
        {
            try
            {
                string json;
                lock (Gate)
                {
                    if (_store == null) return;
                    json = JsonConvert.SerializeObject(_store);
                }
                Directory.CreateDirectory(AudioWaveformCache.CacheFolder);
                File.WriteAllText(StorePath, json);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("MediaDurationCache: store write failed: {Error}", ex.Message);
            }
        }
    }
}
