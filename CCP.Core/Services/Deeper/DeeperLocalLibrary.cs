using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel.Models.Deeper;

namespace ConditioningControlPanel.Services.Deeper
{
    /// <summary>
    /// Read-only index for local <c>.ccpenh.json</c> files. It deliberately does not create the
    /// folder, seed demos, update recents, or watch the filesystem; those are head-owned lifecycle
    /// decisions. Loading is shared in Core; validation belongs to the future open/play path and
    /// never gates a parseable file out of the listing.
    /// </summary>
    public static class DeeperLocalLibrary
    {
        public const string FileSuffix = ".ccpenh.json";

        public sealed class ScanResult
        {
            internal ScanResult(string folderPath, IReadOnlyList<EnhancementLibraryEntry> entries, int skippedCount, bool hasError)
            {
                FolderPath = folderPath;
                Entries = entries;
                SkippedCount = skippedCount;
                HasError = hasError;
            }

            public string FolderPath { get; }
            public IReadOnlyList<EnhancementLibraryEntry> Entries { get; }
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
                return new ScanResult(path, Array.Empty<EnhancementLibraryEntry>(), 0, hasError: true);
            }

            if (!Directory.Exists(path))
                return new ScanResult(path, Array.Empty<EnhancementLibraryEntry>(), 0, hasError: File.Exists(path));

            var entries = new List<EnhancementLibraryEntry>();
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

        private static bool TryRead(string path, out EnhancementLibraryEntry? entry)
        {
            entry = null;
            try
            {
                var enhancement = EnhancementSerializer.LoadFromFile(path);
                var metadata = enhancement.Metadata;
                var duration = metadata?.MediaDurationSeconds ?? 0;
                if (!double.IsFinite(duration) || duration < 0) duration = 0;

                entry = new EnhancementLibraryEntry
                {
                    FilePath = Path.GetFullPath(path),
                    // Keep the WPF expression: a null metadata/name falls back through
                    // GetFileNameWithoutExtension (only ".json" is removed); an authored empty
                    // name remains empty.
                    Name = metadata?.Name ?? Path.GetFileNameWithoutExtension(path),
                    Creator = metadata?.Creator ?? "",
                    MediaType = enhancement.MediaType ?? "",
                    MediaSource = enhancement.MediaSource ?? "",
                    LastModified = File.GetLastWriteTime(path),
                    AutoTags = metadata?.AutoTags ?? new List<string>(),
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
