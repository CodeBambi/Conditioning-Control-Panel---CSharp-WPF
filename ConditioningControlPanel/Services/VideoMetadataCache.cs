using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        private readonly ConcurrentDictionary<string, double> _byKey = new();
        private readonly LibVLC _libVlc;
        private readonly object _saveLock = new();
        private bool _dirty;

        public VideoMetadataCache(LibVLC libVlc)
        {
            _libVlc = libVlc;
            Load();
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
        /// null on parse failure. Safe to call repeatedly — only the first
        /// call per path actually parses.
        /// </summary>
        public async Task<double?> GetOrComputeDurationAsync(string path)
        {
            var key = ComputeKey(path);
            if (key == null) return null;
            if (_byKey.TryGetValue(key, out var cached)) return cached;

            try
            {
                using var media = new Media(_libVlc, path, FromType.FromPath);
                var result = await media.Parse(MediaParseOptions.ParseLocal, timeout: 5000);
                if (result == MediaParsedStatus.Done && media.Duration > 0)
                {
                    var seconds = media.Duration / 1000.0;
                    _byKey[key] = seconds;
                    _dirty = true;
                    return seconds;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("VideoMetadataCache: parse failed for {Path}: {Error}", path, ex.Message);
            }
            return null;
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
