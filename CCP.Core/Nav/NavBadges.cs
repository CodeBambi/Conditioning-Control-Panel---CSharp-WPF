using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Nav
{
    /// <summary>
    /// Nav rework seam (2026-10-06): counts shown as badges on the section rail ("3 open" on
    /// Social). Producers call <see cref="Set"/> from any thread; the rail subscribes to
    /// <see cref="Changed"/> and marshals to the UI thread itself. A count of 0 clears the badge
    /// (a badge that never clears trains people to ignore every badge). Pure, no WPF, so both the
    /// producer (SOCIAL lane) and the consumer (RAIL lane) compile without the other.
    /// </summary>
    public static class NavBadges
    {
        private static readonly object Gate = new();
        private static readonly Dictionary<string, int> Counts = new(StringComparer.Ordinal);
        private static readonly Dictionary<string, int> SeenCounts = new(StringComparer.Ordinal);

        /// <summary>Raised after a count or its seen state changed: (section key, count).</summary>
        public static event Action<string, int>? Changed;

        public static int Get(string sectionKey)
        {
            lock (Gate) return Counts.TryGetValue(sectionKey, out var n) ? n : 0;
        }

        /// <summary>The count the player last saw on this section (0 = never, or it cleared since).</summary>
        public static int Seen(string sectionKey)
        {
            lock (Gate) return SeenCounts.TryGetValue(sectionKey, out var n) ? n : 0;
        }

        /// <summary>
        /// 7.1.5 (tier-2: a red "7" read as seven unread messages): a count is FRESH only when it
        /// rose above what the player last saw. A seen count stays up, dimmed, and is never a
        /// notification. Pure.
        /// </summary>
        public static bool IsFresh(int count, int seen) => count > 0 && count > seen;

        /// <summary>The seen mark after the count moves: a badge that clears forgets what was seen,
        /// so the next table to open brightens it again. Pure.</summary>
        public static int SeenAfterCount(int seen, int count) => count <= 0 ? 0 : seen;

        /// <summary>True when this section's badge should be bright.</summary>
        public static bool IsFresh(string sectionKey)
        {
            lock (Gate)
            {
                Counts.TryGetValue(sectionKey, out var n);
                SeenCounts.TryGetValue(sectionKey, out var s);
                return IsFresh(n, s);
            }
        }

        public static void Set(string sectionKey, int count)
        {
            if (string.IsNullOrEmpty(sectionKey)) return;
            if (count < 0) count = 0;
            lock (Gate)
            {
                Counts.TryGetValue(sectionKey, out var old);
                if (old == count) return;
                Counts[sectionKey] = count;
                SeenCounts.TryGetValue(sectionKey, out var seen);
                SeenCounts[sectionKey] = SeenAfterCount(seen, count);
            }
            Changed?.Invoke(sectionKey, count);
        }

        /// <summary>The player looked (visited the section, opened the Lobby drop): the current
        /// count is seen and the badge dims. Raises <see cref="Changed"/> only when that moved.</summary>
        public static void MarkSeen(string sectionKey)
        {
            if (string.IsNullOrEmpty(sectionKey)) return;
            int count;
            lock (Gate)
            {
                Counts.TryGetValue(sectionKey, out count);
                SeenCounts.TryGetValue(sectionKey, out var seen);
                if (seen == count) return;
                SeenCounts[sectionKey] = count;
            }
            Changed?.Invoke(sectionKey, count);
        }

        /// <summary>Tests only: forget every count.</summary>
        internal static void ResetForTests()
        {
            lock (Gate) { Counts.Clear(); SeenCounts.Clear(); }
        }
    }
}
