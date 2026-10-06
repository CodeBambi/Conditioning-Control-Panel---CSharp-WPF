using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.UI
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

        /// <summary>Raised after a count changed: (section key, new count).</summary>
        public static event Action<string, int>? Changed;

        public static int Get(string sectionKey)
        {
            lock (Gate) return Counts.TryGetValue(sectionKey, out var n) ? n : 0;
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
            }
            Changed?.Invoke(sectionKey, count);
        }

        /// <summary>Tests only: forget every count.</summary>
        internal static void ResetForTests()
        {
            lock (Gate) Counts.Clear();
        }
    }
}
