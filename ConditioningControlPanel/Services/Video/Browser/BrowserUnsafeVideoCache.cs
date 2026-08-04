using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Services.Video.Browser
{
    /// <summary>
    /// The set of video files the browser engine has proven it cannot play. A file lands here when
    /// the player page reports a media error for it, or never reports <c>playing</c> within the
    /// no-playing window; from then on <see cref="BrowserVideoGate"/> routes it to LibVLC forever,
    /// so the user never pays the fallback cost twice for the same clip.
    ///
    /// NOT populated by <c>ProcessFailed</c>: a dead browser process says nothing about the file.
    ///
    /// Keyed by full path + file size so a re-encode of the same filename gets a fresh chance.
    /// Persisted to <c>%LOCALAPPDATA%\ConditioningControlPanel\browser_unsafe_videos.json</c>;
    /// a corrupt or unreadable file degrades to an empty cache rather than blocking playback.
    /// </summary>
    internal static class BrowserUnsafeVideoCache
    {
        /// <summary>Bound the file. Oldest entries are dropped wholesale once it is hit - the
        /// cost of forgetting is one extra fallback, which is cheaper than an unbounded file.</summary>
        private const int MaxEntries = 2000;
        private const int SaveDebounceMs = 3000;

        private static readonly object _lock = new();
        private static readonly HashSet<string> _keys = new(StringComparer.OrdinalIgnoreCase);
        private static readonly List<string> _order = new();   // insertion order, for the cap
        private static bool _loaded;
        private static Timer? _saveTimer;

        private static string FilePath => Path.Combine(App.UserDataPath, "browser_unsafe_videos.json");

        /// <summary>True when this exact file has already failed in the browser engine.</summary>
        public static bool Contains(string? path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            var key = KeyFor(path);
            if (key == null) return false;
            lock (_lock)
            {
                EnsureLoaded();
                return _keys.Contains(key);
            }
        }

        /// <summary>Record a file as browser-undecodable. Idempotent; never throws.</summary>
        public static void Add(string? path, string reason)
        {
            if (string.IsNullOrEmpty(path)) return;
            var key = KeyFor(path);
            if (key == null) return;
            lock (_lock)
            {
                EnsureLoaded();
                if (!_keys.Add(key)) return;
                _order.Add(key);
                while (_order.Count > MaxEntries)
                {
                    _keys.Remove(_order[0]);
                    _order.RemoveAt(0);
                }
                ScheduleSave();
            }
            App.Logger?.Information("BrowserVideo: {File} marked browser-unsafe ({Reason}) - it will use LibVLC from now on",
                Path.GetFileName(path), reason);
        }

        private static string? KeyFor(string path)
        {
            try
            {
                var full = Path.GetFullPath(path);
                long size = 0;
                try { size = new FileInfo(full).Length; }
                catch { /* missing/locked: key on the path alone */ }
                return full + "|" + size;
            }
            catch { return null; }
        }

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                if (!File.Exists(FilePath)) return;
                var list = JsonConvert.DeserializeObject<List<string>>(File.ReadAllText(FilePath));
                if (list == null) return;
                foreach (var k in list)
                {
                    if (string.IsNullOrEmpty(k)) continue;
                    if (_keys.Add(k)) _order.Add(k);
                }
            }
            catch (Exception ex)
            {
                // Corrupt file = empty cache. Everything simply gets one more browser attempt.
                _keys.Clear();
                _order.Clear();
                App.Logger?.Warning("BrowserVideo: unsafe-video cache load failed ({E}) - starting empty", ex.Message);
            }
        }

        private static void ScheduleSave()
        {
            _saveTimer ??= new Timer(_ => Save(), null, Timeout.Infinite, Timeout.Infinite);
            try { _saveTimer.Change(SaveDebounceMs, Timeout.Infinite); }
            catch (Exception ex) { App.Logger?.Debug("BrowserVideo: unsafe-cache save schedule failed: {E}", ex.Message); }
        }

        private static void Save()
        {
            try
            {
                string json;
                lock (_lock) { json = JsonConvert.SerializeObject(_order); }
                Directory.CreateDirectory(App.UserDataPath);
                File.WriteAllText(FilePath, json);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("BrowserVideo: unsafe-video cache save failed: {E}", ex.Message);
            }
        }
    }
}
