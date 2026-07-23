using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// App-lifetime "media recap" log: records every flash image displayed and every
    /// video played, regardless of whether a session is active. Mirrors the subscription
    /// pattern of <see cref="SessionLogService"/> but is global (subscribed once at
    /// startup) and keeps a single rolling, disk-persisted history capped at
    /// <see cref="MaxEntries"/> so it never grows unbounded.
    ///
    /// The history window (opened from the Assets tab) reads a snapshot via
    /// <see cref="GetSnapshot"/> and listens to <see cref="EntryAdded"/> for live updates.
    /// Only lightweight metadata (path/name/type/time) is stored - thumbnails are decoded
    /// lazily by the UI, never here.
    /// </summary>
    public class MediaHistoryService : IDisposable
    {
        /// <summary>Hard cap on retained entries. Oldest are dropped first (ring buffer).</summary>
        public const int MaxEntries = 500;

        // Coalesce identical consecutive hits (e.g. hydra re-spawns, vout self-heal retries
        // re-fire the same file) so one visible media = one entry.
        private static readonly TimeSpan DedupWindow = TimeSpan.FromSeconds(2);

        // Debounce disk writes so a flash burst doesn't thrash the file.
        private static readonly TimeSpan SaveDebounce = TimeSpan.FromSeconds(3);

        private readonly object _lock = new();
        private readonly List<MediaLogEntry> _entries = new();   // oldest first
        private readonly string _filePath;
        private readonly Timer _saveTimer;
        private bool _saveScheduled;
        private bool _isSubscribed;
        private bool _disposed;

        /// <summary>Raised (on the thread that logged the media) when a new entry is appended.</summary>
        public event EventHandler<MediaLogEntry>? EntryAdded;

        /// <summary>Raised when the history is cleared.</summary>
        public event EventHandler? Cleared;

        public MediaHistoryService()
        {
            _filePath = Path.Combine(App.UserDataPath, "media_history.json");
            _saveTimer = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);

            Load();
            Subscribe();
        }

        /// <summary>Newest-first copy of the current history. Safe to iterate off the UI thread.</summary>
        public List<MediaLogEntry> GetSnapshot()
        {
            lock (_lock)
            {
                var copy = new List<MediaLogEntry>(_entries);
                copy.Reverse();
                return copy;
            }
        }

        public int Count
        {
            get { lock (_lock) { return _entries.Count; } }
        }

        public void Clear()
        {
            lock (_lock) { _entries.Clear(); }
            try { Cleared?.Invoke(this, EventArgs.Empty); }
            catch (Exception ex) { App.Logger?.Debug("MediaHistoryService: Cleared handler threw: {Error}", ex.Message); }
            ScheduleSave();
        }

        private void Subscribe()
        {
            if (_isSubscribed) return;
            try
            {
                if (App.Flash != null) App.Flash.FlashDisplayed += OnFlashDisplayed;
                if (App.Video != null) App.Video.VideoStarted += OnVideoStarted;
                _isSubscribed = true;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "MediaHistoryService: subscribe failed");
            }
        }

        private void Unsubscribe()
        {
            if (!_isSubscribed) return;
            try { if (App.Flash != null) App.Flash.FlashDisplayed -= OnFlashDisplayed; } catch { }
            try { if (App.Video != null) App.Video.VideoStarted -= OnVideoStarted; } catch { }
            _isSubscribed = false;
        }

        private void OnFlashDisplayed(object? sender, EventArgs e)
        {
            try
            {
                var paths = App.Flash?.LastDisplayedImagePaths;
                if (paths == null || paths.Count == 0) return;
                foreach (var path in paths)
                    Add(MediaType.Image, path);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("MediaHistoryService.OnFlashDisplayed failed: {Error}", ex.Message);
            }
        }

        private void OnVideoStarted(object? sender, EventArgs e)
        {
            try
            {
                Add(MediaType.Video, App.Video?.LastVideoPath);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("MediaHistoryService.OnVideoStarted failed: {Error}", ex.Message);
            }
        }

        private void Add(MediaType type, string? path)
        {
            if (string.IsNullOrEmpty(path)) return;

            MediaLogEntry entry;
            lock (_lock)
            {
                var now = DateTime.Now;

                // Skip an immediate repeat of the same file (retry/re-spawn noise).
                if (_entries.Count > 0)
                {
                    var last = _entries[_entries.Count - 1];
                    if (last.Type == type && string.Equals(last.FilePath, path, StringComparison.OrdinalIgnoreCase)
                        && now - last.Timestamp < DedupWindow)
                    {
                        return;
                    }
                }

                entry = new MediaLogEntry
                {
                    Timestamp = now,
                    Type = type,
                    FilePath = path,
                    DisplayName = SafeFileName(path),
                };
                _entries.Add(entry);

                // Ring-buffer trim.
                int overflow = _entries.Count - MaxEntries;
                if (overflow > 0) _entries.RemoveRange(0, overflow);
            }

            try { EntryAdded?.Invoke(this, entry); }
            catch (Exception ex) { App.Logger?.Debug("MediaHistoryService: EntryAdded handler threw: {Error}", ex.Message); }

            ScheduleSave();
        }

        private static string SafeFileName(string path)
        {
            try { return Path.GetFileName(path) ?? ""; }
            catch { return ""; }
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_filePath)) return;
                var json = File.ReadAllText(_filePath);
                var loaded = JsonConvert.DeserializeObject<List<MediaLogEntry>>(json);
                if (loaded == null) return;

                lock (_lock)
                {
                    _entries.Clear();
                    // Stored newest-first; keep internal list oldest-first.
                    loaded.Reverse();
                    if (loaded.Count > MaxEntries)
                        loaded.RemoveRange(0, loaded.Count - MaxEntries);
                    _entries.AddRange(loaded.Where(m => m != null && !string.IsNullOrEmpty(m.FilePath)));
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "MediaHistoryService: failed to load history");
            }
        }

        private void ScheduleSave()
        {
            if (_disposed) return;
            lock (_lock)
            {
                if (_saveScheduled) return;
                _saveScheduled = true;
            }
            try { _saveTimer.Change(SaveDebounce, Timeout.InfiniteTimeSpan); } catch { }
        }

        private void Flush()
        {
            List<MediaLogEntry> snapshot;
            lock (_lock)
            {
                _saveScheduled = false;
                snapshot = new List<MediaLogEntry>(_entries);
                snapshot.Reverse(); // persist newest-first for readability
            }
            try
            {
                var tmp = _filePath + ".tmp";
                var json = JsonConvert.SerializeObject(snapshot, Formatting.Indented);
                File.WriteAllText(tmp, json);
                // Atomic-ish replace so a crash mid-write can't corrupt the history.
                if (File.Exists(_filePath)) File.Replace(tmp, _filePath, null);
                else File.Move(tmp, _filePath);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "MediaHistoryService: failed to persist history");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Unsubscribe();
            try { _saveTimer.Dispose(); } catch { }
            Flush(); // best-effort final write on shutdown
        }
    }
}
