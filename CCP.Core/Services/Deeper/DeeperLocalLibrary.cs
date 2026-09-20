using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models.Deeper;

namespace ConditioningControlPanel.Services.Deeper
{
    /// <summary>
    /// Read-only index for local <c>.ccpenh.json</c> files. It deliberately does not create the
    /// folder, seed demos, update recents, or watch the filesystem; those are head-owned lifecycle
    /// decisions. Loading and validation stay on the shared Core seams.
    /// </summary>
    public static class DeeperLocalLibrary
    {
        public const string FileSuffix = ".ccpenh.json";

        public enum MediaTypeFilter { All, Video, Audio }
        public enum SortMode { Recent, Name, Creator }

        public sealed record FilterCriteria(
            string Search,
            MediaTypeFilter MediaType = MediaTypeFilter.All,
            bool Haptics = false,
            bool Webcam = false,
            SortMode Sort = SortMode.Recent,
            bool Descending = true);

        public sealed class Entry
        {
            public string FilePath { get; init; } = "";
            public string Name { get; init; } = "";
            public string Creator { get; init; } = "";
            public string MediaType { get; init; } = "";
            public string MediaSource { get; init; } = "";
            public DateTime LastModified { get; init; }
            public IReadOnlyList<string> AutoTags { get; init; } = Array.Empty<string>();
            public double DurationSeconds { get; init; }
        }

        public sealed class ScanResult
        {
            internal ScanResult(string folderPath, IReadOnlyList<Entry> entries, int skippedCount, bool hasError)
            {
                FolderPath = folderPath;
                Entries = entries;
                SkippedCount = skippedCount;
                HasError = hasError;
            }

            public string FolderPath { get; }
            public IReadOnlyList<Entry> Entries { get; }
            public int SkippedCount { get; }
            public bool HasError { get; }
        }

        public static string DefaultFolder => Path.Combine(CorePaths.UserData, "enhancements");

        public static ScanResult Scan(string? folder = null)
        {
            var path = folder ?? DefaultFolder;
            try
            {
                path = Path.GetFullPath(path);
            }
            catch
            {
                return new ScanResult(path, Array.Empty<Entry>(), 0, hasError: true);
            }

            if (!Directory.Exists(path))
                return new ScanResult(path, Array.Empty<Entry>(), 0, hasError: File.Exists(path));

            var entries = new List<Entry>();
            var skipped = 0;
            try
            {
                foreach (var candidate in Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly))
                {
                    if (!candidate.EndsWith(FileSuffix, StringComparison.OrdinalIgnoreCase)) continue;
                    if (TryRead(candidate, out var entry)) entries.Add(entry!);
                    else skipped++;
                }
            }
            catch
            {
                return new ScanResult(path, entries, skipped, hasError: true);
            }

            entries.Sort(static (a, b) =>
            {
                var recent = b.LastModified.CompareTo(a.LastModified);
                return recent != 0
                    ? recent
                    : StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name);
            });
            return new ScanResult(path, entries, skipped, hasError: false);
        }

        public static bool Matches(Entry? entry, FilterCriteria criteria)
        {
            if (entry is null) return false;
            var search = (criteria.Search ?? "").Trim();
            return (search.Length == 0
                    || Contains(entry.Name, search)
                    || Contains(entry.Creator, search)
                    || entry.AutoTags.Any(tag => Contains(tag, search)))
                && MatchesMediaType(entry, criteria.MediaType)
                && (!criteria.Haptics || HasTag(entry, EnhancementAutoTagger.TagHaptics))
                && (!criteria.Webcam || HasTag(entry, EnhancementAutoTagger.TagWebcam));
        }

        public static IReadOnlyList<Entry> Filter(IEnumerable<Entry>? entries, FilterCriteria criteria)
        {
            var source = (entries ?? Array.Empty<Entry>()).Where(entry => Matches(entry, criteria));
            return criteria.Sort switch
            {
                SortMode.Name => Order(source, entry => entry.Name, StringComparer.OrdinalIgnoreCase, criteria.Descending),
                SortMode.Creator => Order(source, entry => entry.Creator, StringComparer.OrdinalIgnoreCase, criteria.Descending),
                _ => Order(source, entry => entry.LastModified, Comparer<DateTime>.Default, criteria.Descending),
            };
        }

        private static bool MatchesMediaType(Entry entry, MediaTypeFilter filter) => filter switch
        {
            MediaTypeFilter.Video => string.Equals(entry.MediaType, MediaTypes.Video, StringComparison.OrdinalIgnoreCase),
            MediaTypeFilter.Audio => string.Equals(entry.MediaType, MediaTypes.Audio, StringComparison.OrdinalIgnoreCase),
            _ => true,
        };

        private static bool HasTag(Entry entry, string tag)
            => entry.AutoTags.Any(value => string.Equals(value, tag, StringComparison.OrdinalIgnoreCase));

        private static bool Contains(string? value, string search)
            => !string.IsNullOrEmpty(value) && value.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;

        private static IReadOnlyList<Entry> Order<TKey>(IEnumerable<Entry> source, Func<Entry, TKey> key,
            IComparer<TKey> comparer, bool descending)
        {
            var ordered = descending ? source.OrderByDescending(key, comparer) : source.OrderBy(key, comparer);
            return ordered.ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static bool TryRead(string path, out Entry? entry)
        {
            entry = null;
            try
            {
                var enhancement = EnhancementSerializer.LoadFromFile(path);
                if (EnhancementValidator.Validate(enhancement)
                    .Any(error => error.Severity == ValidationSeverity.Error))
                    return false;

                var metadata = enhancement.Metadata;
                var name = metadata?.Name?.Trim();
                if (string.IsNullOrEmpty(name))
                {
                    var fileName = Path.GetFileName(path);
                    name = fileName.EndsWith(FileSuffix, StringComparison.OrdinalIgnoreCase)
                        ? fileName[..^FileSuffix.Length]
                        : Path.GetFileNameWithoutExtension(fileName);
                }

                var duration = metadata?.MediaDurationSeconds ?? 0;
                if (!double.IsFinite(duration) || duration < 0) duration = 0;
                entry = new Entry
                {
                    FilePath = Path.GetFullPath(path),
                    Name = name,
                    Creator = metadata?.Creator?.Trim() ?? "",
                    MediaType = enhancement.MediaType ?? "",
                    MediaSource = enhancement.MediaSource ?? "",
                    LastModified = File.GetLastWriteTime(path),
                    AutoTags = (metadata?.AutoTags ?? new List<string>()).ToArray(),
                    DurationSeconds = duration,
                };
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
