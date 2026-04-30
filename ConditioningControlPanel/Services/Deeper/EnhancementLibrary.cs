using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    public class EnhancementLibrary
    {
        public const string FileSuffix = ".ccpenh.json";
        private const int MaxRecentFiles = 10;

        public string LibraryFolder { get; }

        public event EventHandler? LibraryChanged;

        public EnhancementLibrary()
        {
            LibraryFolder = Path.Combine(App.UserDataPath, "enhancements");
            try { Directory.CreateDirectory(LibraryFolder); }
            catch (Exception ex) { App.Logger?.Warning(ex, "EnhancementLibrary: could not create library folder {Path}", LibraryFolder); }
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
            var json = File.ReadAllText(path);
            var enhancement = EnhancementSerializer.Load(json);
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
            return new Enhancement
            {
                MediaType = mediaType,
                MediaSource = mediaSource,
                Metadata = new EnhancementMetadata { Name = "Untitled" }
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
                var json = File.ReadAllText(path);
                var enhancement = EnhancementSerializer.Load(json);
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
