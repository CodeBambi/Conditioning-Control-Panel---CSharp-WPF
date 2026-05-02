using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Captures every video played and image flashed during a session, persists
    /// the result to disk (capped at MaxRetainedLogs files), and raises LogReady
    /// when a session ends so the post-session dialog can render it.
    ///
    /// Subscribed only while a session is active; subscriptions are released in
    /// EndSession even when no media was logged.
    /// </summary>
    public class SessionLogService : IDisposable
    {
        public const int MaxRetainedLogs = 20;

        // Sessions shorter than this with no media are not persisted - prevents
        // accidental starts/stops from cluttering the log folder.
        private static readonly TimeSpan PersistenceMinDuration = TimeSpan.FromSeconds(30);

        private readonly object _lock = new();
        private SessionLog? _activeLog;
        private DateTime _sessionStart;
        private bool _isSubscribed;

        public event EventHandler<SessionLogReadyEventArgs>? LogReady;

        public string LogsFolder { get; }

        public SessionLogService()
        {
            LogsFolder = Path.Combine(App.UserDataPath, "session_logs");
            try { Directory.CreateDirectory(LogsFolder); }
            catch (Exception ex) { App.Logger?.Warning(ex, "SessionLogService: failed to create logs folder"); }
        }

        /// <summary>
        /// Begin tracking media for a session. Called from SessionEngine.StartSessionAsync.
        /// </summary>
        public void BeginSession(Session session)
        {
            if (session == null) return;

            lock (_lock)
            {
                if (_activeLog != null)
                {
                    App.Logger?.Warning("SessionLogService.BeginSession called while a log was already active; discarding previous log");
                    UnsubscribeUnlocked();
                }

                _sessionStart = DateTime.Now;
                _activeLog = new SessionLog
                {
                    SessionId = session.Id,
                    SessionName = session.Name,
                    SessionIcon = session.Icon,
                    SessionDifficulty = session.Difficulty,
                    StartedAt = _sessionStart,
                };

                SubscribeUnlocked();
            }
        }

        /// <summary>
        /// Finalize the active session log: stop subscribing, populate completion
        /// fields, persist to disk (with prune), and raise LogReady. Safe to call
        /// even if BeginSession was never called - it becomes a no-op.
        /// </summary>
        public void EndSession(bool completed, TimeSpan duration, int xpEarned)
        {
            SessionLog? log;
            lock (_lock)
            {
                if (_activeLog == null) return;

                UnsubscribeUnlocked();

                log = _activeLog;
                log.EndedAt = DateTime.Now;
                log.Duration = duration;
                log.Completed = completed;
                log.XPEarned = xpEarned;
                _activeLog = null;
            }

            // Skip persistence for trivially short sessions with no media (accidental starts).
            bool persist = log.Media.Count > 0 || duration >= PersistenceMinDuration;
            if (persist)
            {
                TryPersist(log);
                TryPrune();
            }

            try { LogReady?.Invoke(this, new SessionLogReadyEventArgs(log)); }
            catch (Exception ex) { App.Logger?.Error(ex, "SessionLogService: LogReady handler threw"); }
        }

        /// <summary>
        /// Load the most recent persisted logs (up to MaxRetainedLogs), newest first.
        /// Corrupt files are skipped, not thrown.
        /// </summary>
        public List<SessionLog> LoadRecentLogs()
        {
            var result = new List<SessionLog>();
            if (!Directory.Exists(LogsFolder)) return result;

            string[] files;
            try { files = Directory.GetFiles(LogsFolder, "*.json"); }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "SessionLogService: failed to enumerate logs folder");
                return result;
            }

            Array.Sort(files);
            Array.Reverse(files);

            foreach (var file in files.Take(MaxRetainedLogs))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var log = JsonConvert.DeserializeObject<SessionLog>(json);
                    if (log != null) result.Add(log);
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "SessionLogService: failed to read log file {File}", file);
                }
            }
            return result;
        }

        public void Dispose()
        {
            lock (_lock)
            {
                UnsubscribeUnlocked();
                _activeLog = null;
            }
        }

        private void SubscribeUnlocked()
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
                App.Logger?.Warning(ex, "SessionLogService: subscribe failed");
            }
        }

        private void UnsubscribeUnlocked()
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

                lock (_lock)
                {
                    if (_activeLog == null) return;
                    var now = DateTime.Now;
                    var offset = now - _sessionStart;
                    foreach (var path in paths)
                    {
                        if (string.IsNullOrEmpty(path)) continue;
                        _activeLog.Media.Add(new MediaLogEntry
                        {
                            Timestamp = now,
                            SessionTime = offset,
                            Type = MediaType.Image,
                            FilePath = path,
                            DisplayName = SafeFileName(path),
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("SessionLogService.OnFlashDisplayed failed: {Error}", ex.Message);
            }
        }

        private void OnVideoStarted(object? sender, EventArgs e)
        {
            try
            {
                var path = App.Video?.LastVideoPath;
                if (string.IsNullOrEmpty(path)) return;

                lock (_lock)
                {
                    if (_activeLog == null) return;
                    var now = DateTime.Now;
                    _activeLog.Media.Add(new MediaLogEntry
                    {
                        Timestamp = now,
                        SessionTime = now - _sessionStart,
                        Type = MediaType.Video,
                        FilePath = path,
                        DisplayName = SafeFileName(path),
                    });
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("SessionLogService.OnVideoStarted failed: {Error}", ex.Message);
            }
        }

        private static string SafeFileName(string path)
        {
            try { return Path.GetFileName(path) ?? ""; }
            catch { return ""; }
        }

        private void TryPersist(SessionLog log)
        {
            try
            {
                var fileName = $"{log.StartedAt:yyyyMMdd_HHmmss}_{SanitizeId(log.SessionId)}.json";
                var path = Path.Combine(LogsFolder, fileName);
                var json = JsonConvert.SerializeObject(log, Formatting.Indented);
                File.WriteAllText(path, json);
                App.Logger?.Debug("SessionLogService: persisted {File} ({Count} media entries)", fileName, log.Media.Count);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "SessionLogService: failed to persist session log");
            }
        }

        private void TryPrune()
        {
            try
            {
                if (!Directory.Exists(LogsFolder)) return;
                var files = Directory.GetFiles(LogsFolder, "*.json");
                if (files.Length <= MaxRetainedLogs) return;

                Array.Sort(files);
                Array.Reverse(files);
                for (int i = MaxRetainedLogs; i < files.Length; i++)
                {
                    try { File.Delete(files[i]); }
                    catch (Exception ex) { App.Logger?.Debug("SessionLogService: prune delete failed for {File}: {Error}", files[i], ex.Message); }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "SessionLogService: prune failed");
            }
        }

        private static string SanitizeId(string id)
        {
            if (string.IsNullOrEmpty(id)) return "session";
            var invalid = Path.GetInvalidFileNameChars();
            var chars = id.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (Array.IndexOf(invalid, chars[i]) >= 0) chars[i] = '_';
            }
            return new string(chars);
        }
    }

    public class SessionLogReadyEventArgs : EventArgs
    {
        public SessionLog Log { get; }
        public SessionLogReadyEventArgs(SessionLog log) { Log = log; }
    }
}
