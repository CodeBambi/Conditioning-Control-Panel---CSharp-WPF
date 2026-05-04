using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Threading;
using ConditioningControlPanel.Models.Deeper;

namespace ConditioningControlPanel.Services.Deeper
{
    public class EnhancementLibraryEntry
    {
        public string FilePath { get; set; } = "";
        public string Name { get; set; } = "";
        public string Creator { get; set; } = "";
        public string MediaType { get; set; } = "";
        public string MediaSource { get; set; } = "";
        public DateTime LastModified { get; set; }
    }

    /// <summary>
    /// Manages on-disk enhancement files and the recent-files list.
    /// All ops are silent on failure — the library never throws past the boundary.
    /// </summary>
    public class EnhancementLibrary : IDisposable
    {
        public const string FileSuffix = ".ccpenh.json";
        private const int MaxRecentFiles = 10;
        // FileSystemWatcher commonly fires multiple events for one logical
        // change (Office-style atomic save = delete + create + change in
        // <100 ms). Coalesce by waiting this long for events to settle.
        private const int DebounceMs = 300;

        public string LibraryFolder { get; }

        public event EventHandler? LibraryChanged;

        private FileSystemWatcher? _watcher;
        private DispatcherTimer? _debounceTimer;
        private bool _disposed;

        public EnhancementLibrary()
        {
            LibraryFolder = Path.Combine(App.UserDataPath, "enhancements");
            try { Directory.CreateDirectory(LibraryFolder); }
            catch (Exception ex) { App.Logger?.Warning(ex, "EnhancementLibrary: could not create library folder {Path}", LibraryFolder); }
            SeedBundledDemos();
            StartWatching();
        }

        // Copy bundled .ccpenh.json files from Resources/DeeperDemos into the
        // user's library on first launch. Gated by AppSettings.HasSeededDeeperDemos
        // so the user can delete a demo without it returning. We do NOT
        // overwrite an existing file with the same name — if the user already
        // has a "welcome.ccpenh.json", their copy wins (they may have edited it).
        private void SeedBundledDemos()
        {
            try
            {
                if (App.Settings?.Current?.HasSeededDeeperDemos == true) return;

                var demoSource = Path.Combine(AppContext.BaseDirectory, "Resources", "DeeperDemos");
                if (!Directory.Exists(demoSource))
                {
                    App.Logger?.Debug("EnhancementLibrary: no bundled demos folder at {Path}", demoSource);
                    return;
                }

                var sources = Directory.GetFiles(demoSource, "*" + FileSuffix, SearchOption.TopDirectoryOnly);
                int copied = 0;
                foreach (var src in sources)
                {
                    var name = Path.GetFileName(src);
                    var dst = Path.Combine(LibraryFolder, name);
                    if (File.Exists(dst)) continue; // respect user-edited copy
                    try { File.Copy(src, dst, overwrite: false); copied++; }
                    catch (Exception ex) { App.Logger?.Debug("EnhancementLibrary: failed to copy demo {Name}: {Error}", name, ex.Message); }
                }

                if (App.Settings?.Current is { } s)
                {
                    s.HasSeededDeeperDemos = true;
                    App.Settings?.Save();
                }
                if (copied > 0)
                    App.Logger?.Information("EnhancementLibrary: seeded {Count} demo enhancement(s) into {Path}", copied, LibraryFolder);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "EnhancementLibrary: SeedBundledDemos failed");
            }
        }

        // -- Hot-reload via FileSystemWatcher ---------------------------------

        private void StartWatching()
        {
            try
            {
                if (!Directory.Exists(LibraryFolder)) return;

                _watcher = new FileSystemWatcher(LibraryFolder, "*" + FileSuffix)
                {
                    NotifyFilter = NotifyFilters.FileName
                                 | NotifyFilters.LastWrite
                                 | NotifyFilters.Size,
                    IncludeSubdirectories = false,
                    EnableRaisingEvents = true
                };
                _watcher.Created += OnFsEvent;
                _watcher.Deleted += OnFsEvent;
                _watcher.Changed += OnFsEvent;
                _watcher.Renamed += OnFsRenamed;
                _watcher.Error += OnFsError;
                App.Logger?.Information("EnhancementLibrary: watching {Path}", LibraryFolder);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "EnhancementLibrary: could not start watcher");
                _watcher = null;
            }
        }

        private void OnFsEvent(object sender, FileSystemEventArgs e) => ScheduleChangeNotification();
        private void OnFsRenamed(object sender, RenamedEventArgs e) => ScheduleChangeNotification();
        private void OnFsError(object sender, ErrorEventArgs e)
        {
            App.Logger?.Warning(e.GetException(), "EnhancementLibrary: watcher error");
            // Tear down + try to restart so a transient FS error (e.g. drive
            // disconnect on a network share) doesn't permanently kill hot-reload.
            try
            {
                _watcher?.Dispose();
                _watcher = null;
                StartWatching();
            }
            catch { }
        }

        private void ScheduleChangeNotification()
        {
            // Debounce on the WPF dispatcher so subscribers receive the
            // event on the UI thread (UI code can rebuild lists directly).
            try
            {
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted)
                {
                    LibraryChanged?.Invoke(this, EventArgs.Empty);
                    return;
                }
                dispatcher.BeginInvoke(() =>
                {
                    if (_disposed) return;
                    if (_debounceTimer == null)
                    {
                        _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(DebounceMs) };
                        _debounceTimer.Tick += (_, _) =>
                        {
                            _debounceTimer?.Stop();
                            try { LibraryChanged?.Invoke(this, EventArgs.Empty); }
                            catch (Exception ex) { App.Logger?.Debug("LibraryChanged subscriber error: {Error}", ex.Message); }
                        };
                    }
                    _debounceTimer.Stop();
                    _debounceTimer.Start();
                });
            }
            catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                if (_watcher != null)
                {
                    _watcher.EnableRaisingEvents = false;
                    _watcher.Created -= OnFsEvent;
                    _watcher.Deleted -= OnFsEvent;
                    _watcher.Changed -= OnFsEvent;
                    _watcher.Renamed -= OnFsRenamed;
                    _watcher.Error -= OnFsError;
                    _watcher.Dispose();
                }
            }
            catch { }
            try { _debounceTimer?.Stop(); } catch { }
            _watcher = null;
            _debounceTimer = null;
        }

        public List<string> RecentFiles
        {
            get => App.Settings?.Current?.DeeperRecentFiles
                       .Where(File.Exists)
                       .ToList()
                   ?? new List<string>();
        }

        public string LastDirectory
        {
            get
            {
                var d = App.Settings?.Current?.DeeperLastDirectory;
                if (!string.IsNullOrEmpty(d) && Directory.Exists(d)) return d;
                return LibraryFolder;
            }
        }

        /// <summary>
        /// Finds the best library entry whose media_source matches
        /// <paramref name="audioOrUrl"/>. Match order: exact path, same-name
        /// (basename), substring of media_source pattern (with trailing *
        /// stripped). Returns null if nothing matches; ignores enhancements
        /// of the wrong media_type when supplied.
        /// </summary>
        public EnhancementLibraryEntry? FindMatch(string audioOrUrl, string? mediaTypeFilter = null)
        {
            if (string.IsNullOrEmpty(audioOrUrl)) return null;
            try
            {
                var entries = ScanLibrary();
                var baseName = Path.GetFileNameWithoutExtension(audioOrUrl);

                EnhancementLibraryEntry? best = null;
                foreach (var entry in entries)
                {
                    if (mediaTypeFilter != null && entry.MediaType != mediaTypeFilter) continue;
                    var pattern = entry.MediaSource ?? "";

                    // Exact / suffix match wins immediately.
                    if (string.Equals(pattern, audioOrUrl, StringComparison.OrdinalIgnoreCase))
                        return entry;

                    // Basename of pattern matches basename of audioOrUrl.
                    var patternBase = Path.GetFileNameWithoutExtension(pattern.TrimEnd('*'));
                    if (!string.IsNullOrEmpty(patternBase)
                        && string.Equals(patternBase, baseName, StringComparison.OrdinalIgnoreCase))
                    {
                        if (best == null) best = entry;
                    }

                    // Substring (lowest priority).
                    var pTrimmed = pattern.TrimEnd('*');
                    if (best == null
                        && pTrimmed.Length >= 4 // avoid "*" or single chars matching everything
                        && audioOrUrl.Contains(pTrimmed, StringComparison.OrdinalIgnoreCase))
                    {
                        best = entry;
                    }
                }
                return best;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("EnhancementLibrary.FindMatch error: {Error}", ex.Message);
                return null;
            }
        }

        public List<EnhancementLibraryEntry> ScanLibrary()
        {
            var results = new List<EnhancementLibraryEntry>();
            try
            {
                if (!Directory.Exists(LibraryFolder)) return results;
                var files = Directory.GetFiles(LibraryFolder, "*" + FileSuffix, SearchOption.TopDirectoryOnly);
                foreach (var path in files)
                {
                    var entry = TryReadMetadata(path);
                    if (entry != null) results.Add(entry);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "EnhancementLibrary: scan failed");
            }
            return results
                .OrderByDescending(e => e.LastModified)
                .ToList();
        }

        public Enhancement Open(string path)
        {
            var enhancement = EnhancementSerializer.LoadFromFile(path);
            TouchRecent(path);
            return enhancement;
        }

        public void Save(Enhancement enhancement, string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var json = EnhancementSerializer.Save(enhancement);
            File.WriteAllText(path, json);
            TouchRecent(path);
            RememberDirectory(Path.GetDirectoryName(path));
            LibraryChanged?.Invoke(this, EventArgs.Empty);
        }

        public Enhancement CreateBlank(string mediaType, string mediaSource)
        {
            // Leave Name empty so the editor's HT auto-fill can populate it
            // from og:title. UpdateTitle() falls back to the localized
            // "Untitled" string when Name is empty, so the window header
            // still reads correctly until the user (or auto-fill) sets one.
            return new Enhancement
            {
                MediaType = mediaType,
                MediaSource = mediaSource,
                Metadata = new EnhancementMetadata()
            };
        }

        public string SuggestedFileName(Enhancement enhancement)
        {
            var name = enhancement.Metadata?.Name;
            if (string.IsNullOrWhiteSpace(name)) name = "Untitled";
            // Sanitize for filesystem
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name + FileSuffix;
        }

        private void TouchRecent(string path)
        {
            if (App.Settings?.Current is not { } settings) return;
            try
            {
                var canonical = Path.GetFullPath(path);
                var list = settings.DeeperRecentFiles?.Where(p => !string.Equals(p, canonical, StringComparison.OrdinalIgnoreCase)).ToList()
                           ?? new List<string>();
                list.Insert(0, canonical);
                if (list.Count > MaxRecentFiles) list = list.Take(MaxRecentFiles).ToList();
                settings.DeeperRecentFiles = list;
                App.Settings?.Save();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "EnhancementLibrary: TouchRecent failed");
            }
        }

        private void RememberDirectory(string? dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
            if (App.Settings?.Current is not { } settings) return;
            try
            {
                settings.DeeperLastDirectory = dir;
                App.Settings?.Save();
            }
            catch { /* best-effort */ }
        }

        private EnhancementLibraryEntry? TryReadMetadata(string path)
        {
            try
            {
                var enhancement = EnhancementSerializer.LoadFromFile(path);
                return new EnhancementLibraryEntry
                {
                    FilePath = path,
                    Name = enhancement.Metadata?.Name ?? Path.GetFileNameWithoutExtension(path),
                    Creator = enhancement.Metadata?.Creator ?? "",
                    MediaType = enhancement.MediaType,
                    MediaSource = enhancement.MediaSource,
                    LastModified = File.GetLastWriteTime(path)
                };
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("EnhancementLibrary: skipping unreadable file {Path}: {Error}", path, ex.Message);
                return null;
            }
        }
    }
}
