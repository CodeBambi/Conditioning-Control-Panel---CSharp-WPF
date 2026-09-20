using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services.Deeper
{
    /// <summary>
    /// Pure filter / sort / pill-count logic for the Deeper library list, kept
    /// off MainWindow so it can be unit tested. Every pill count answers "how
    /// many rows would show if THIS pill were on, with every other active filter
    /// left as it is", so the type pills and the Haptics/Webcam pills can never
    /// disagree with the list they describe.
    /// </summary>
    public static class EnhancementLibraryFilter
    {
        public enum MediaTypeFilter { All, Video, Audio }
        public enum SortMode { Recent, Name, Creator, Duration }

        public sealed record Criteria(string Search, MediaTypeFilter MediaType, bool Haptics, bool Webcam);

        public readonly record struct PillCounts(int All, int Video, int Audio, int Haptics, int Webcam);

        public static bool MatchesSearch(EnhancementLibraryEntry? e, string? needle)
        {
            if (string.IsNullOrEmpty(needle)) return true;
            if (e == null) return false;
            if (Contains(e.Name, needle)) return true;
            if (Contains(e.Creator, needle)) return true;
            if (e.AutoTags != null)
                foreach (var tag in e.AutoTags) if (Contains(tag, needle)) return true;
            return false;
            static bool Contains(string? hay, string n) =>
                !string.IsNullOrEmpty(hay) && hay.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool MatchesMediaType(EnhancementLibraryEntry e, MediaTypeFilter filter) => filter switch
        {
            MediaTypeFilter.Video => string.Equals(e.MediaType, Models.Deeper.MediaTypes.Video, StringComparison.OrdinalIgnoreCase),
            MediaTypeFilter.Audio => string.Equals(e.MediaType, Models.Deeper.MediaTypes.Audio, StringComparison.OrdinalIgnoreCase),
            _ => true,
        };

        public static bool HasTag(EnhancementLibraryEntry e, string tag)
            => e.AutoTags != null && e.AutoTags.Contains(tag);

        public static bool Matches(EnhancementLibraryEntry e, Criteria c)
        {
            if (e == null) return false;
            var needle = (c.Search ?? "").Trim();
            return MatchesSearch(e, needle)
                && MatchesMediaType(e, c.MediaType)
                && (!c.Haptics || HasTag(e, EnhancementAutoTagger.TagHaptics))
                && (!c.Webcam || HasTag(e, EnhancementAutoTagger.TagWebcam));
        }

        public static PillCounts CountPills(IEnumerable<EnhancementLibraryEntry> all, Criteria c)
        {
            var list = all as IList<EnhancementLibraryEntry> ?? all.ToList();
            int Count(Criteria k) => list.Count(e => Matches(e, k));
            return new PillCounts(
                All:     Count(c with { MediaType = MediaTypeFilter.All }),
                Video:   Count(c with { MediaType = MediaTypeFilter.Video }),
                Audio:   Count(c with { MediaType = MediaTypeFilter.Audio }),
                Haptics: Count(c with { Haptics = true }),
                Webcam:  Count(c with { Webcam = true }));
        }

        /// <summary>Recent and Duration read best newest/longest first; Name and Creator A-Z.</summary>
        public static bool DefaultDescending(SortMode mode) => mode is SortMode.Recent or SortMode.Duration;

        public static IEnumerable<EnhancementLibraryEntry> Sort(IEnumerable<EnhancementLibraryEntry> src, SortMode mode, bool descending)
        {
            IOrderedEnumerable<EnhancementLibraryEntry> ordered = mode switch
            {
                SortMode.Name     => Order(src, e => e.Name ?? "", StringComparer.OrdinalIgnoreCase, descending),
                SortMode.Creator  => Order(src, e => e.Creator ?? "", StringComparer.OrdinalIgnoreCase, descending),
                SortMode.Duration => Order(src, e => e.DurationSeconds, Comparer<double>.Default, descending),
                _                 => Order(src, e => e.LastModified, Comparer<DateTime>.Default, descending),
            };
            // Stable secondary key so equal primaries (unknown durations, same
            // minute) keep a predictable order.
            return ordered.ThenBy(e => e.Name ?? "", StringComparer.OrdinalIgnoreCase);
        }

        private static IOrderedEnumerable<EnhancementLibraryEntry> Order<TKey>(
            IEnumerable<EnhancementLibraryEntry> src, Func<EnhancementLibraryEntry, TKey> key, IComparer<TKey> cmp, bool descending)
            => descending ? src.OrderByDescending(key, cmp) : src.OrderBy(key, cmp);
    }
}
