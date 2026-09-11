using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The list rules behind the dashboard's FAVORITES + RECENT rail (owner decision, Discord
    /// "New UI Feedback", 2026-09-11). Pure so the caps, the dedupe and the ordering are unit
    /// tested without a window; <c>MainWindow.FavoritesRail.cs</c> owns the chips.
    ///
    /// <para>A destination is a Ctrl+K palette row id (<see cref="SettingsPaletteEntry.Id"/>):
    /// <c>door.*</c>, <c>tab.*</c>, <c>launch.*</c>, <c>card.*</c> and <c>part.*</c>. The palette is already the
    /// app's one registry of "places you can go", with a localised label, a glyph and a ShowTab
    /// key per row, so the rail borrows it rather than keeping a second list that would drift.
    /// Settings sections and individual controls are not destinations: pinning "master volume"
    /// to the home page is not what a favorites rail is for.</para>
    /// </summary>
    public static class FavoritesRailRule
    {
        /// <summary>Chips the 92px column holds before it has to scroll.</summary>
        public const int FavoritesCap = 8;

        /// <summary>The last five places opened, most recent first.</summary>
        public const int RecentCap = 5;

        // "part." rows (2026-09-11) name a zone of a page - the Deeper player/editor halves, the
        // Companion page's AI Effects, Workshop and Engine Room - and are pinnable like a tab.
        private static readonly string[] DestinationPrefixes = { "door.", "tab.", "launch.", "card.", "part." };

        /// <summary>
        /// ShowTab keys that never enter RECENT. "settings" is the dashboard itself - the page the
        /// rail is on - and "progression" / "lab" are its permanent aliases.
        /// </summary>
        private static readonly HashSet<string> NeverRecent =
            new(StringComparer.OrdinalIgnoreCase) { "settings", "progression", "lab", "patreon" };

        public static bool IsDestination(string? id) =>
            !string.IsNullOrWhiteSpace(id) &&
            DestinationPrefixes.Any(p => id.StartsWith(p, StringComparison.Ordinal));

        /// <summary>
        /// The palette row a ShowTab key lands on, or null when the key is not a destination
        /// (an alias, the dashboard, a key with no row). Prefers the tab row; a key that only
        /// has a door row (justdrop) resolves to that door.
        /// </summary>
        public static string? DestinationIdForTab(string? tabKey, IEnumerable<SettingsPaletteEntry> entries)
        {
            if (string.IsNullOrWhiteSpace(tabKey) || NeverRecent.Contains(tabKey)) return null;
            var key = tabKey.ToLowerInvariant();
            SettingsPaletteEntry? door = null;
            foreach (var e in entries)
            {
                if (!string.Equals(e.TabKey, key, StringComparison.Ordinal)) continue;
                if (e.Id.StartsWith("tab.", StringComparison.Ordinal)) return e.Id;
                if (door == null && e.Id.StartsWith("door.", StringComparison.Ordinal)) door = e;
            }
            return door?.Id;
        }

        /// <summary>Pins a destination at the end of the list. False when it is already
        /// pinned, not a destination, or the list is full.</summary>
        public static bool TryPin(IList<string> favorites, string? id)
        {
            if (!IsDestination(id) || favorites == null) return false;
            if (favorites.Contains(id!, StringComparer.Ordinal)) return false;
            if (favorites.Count >= FavoritesCap) return false;
            favorites.Add(id!);
            return true;
        }

        public static bool Unpin(IList<string> favorites, string? id)
        {
            if (favorites == null || id == null) return false;
            var i = IndexOf(favorites, id);
            if (i < 0) return false;
            favorites.RemoveAt(i);
            return true;
        }

        public static bool IsPinned(IEnumerable<string>? favorites, string? id) =>
            id != null && favorites != null && favorites.Contains(id, StringComparer.Ordinal);

        public static bool IsFull(ICollection<string>? favorites) =>
            favorites != null && favorites.Count >= FavoritesCap;

        /// <summary>
        /// Records an open: the id moves to the front, its older copy goes, and the tail is
        /// trimmed to <see cref="RecentCap"/>. False when nothing changed (already at the front,
        /// or not a destination).
        /// </summary>
        public static bool NoteOpened(IList<string> recent, string? id)
        {
            if (!IsDestination(id) || recent == null) return false;
            if (recent.Count > 0 && string.Equals(recent[0], id, StringComparison.Ordinal)) return false;
            var i = IndexOf(recent, id!);
            if (i >= 0) recent.RemoveAt(i);
            recent.Insert(0, id!);
            while (recent.Count > RecentCap) recent.RemoveAt(recent.Count - 1);
            return true;
        }

        /// <summary>
        /// What RECENT shows: the stored list minus anything already pinned (a chip in both
        /// sections is a stutter) and minus any id the registry no longer knows. Order kept.
        /// </summary>
        public static List<string> RecentForDisplay(IEnumerable<string>? recent, IEnumerable<string>? favorites,
                                                    Func<string, bool> known)
        {
            var pinned = new HashSet<string>(favorites ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            return (recent ?? Enumerable.Empty<string>())
                .Where(id => IsDestination(id) && !pinned.Contains(id) && known(id))
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        private static int IndexOf(IList<string> list, string id)
        {
            for (int i = 0; i < list.Count; i++)
                if (string.Equals(list[i], id, StringComparison.Ordinal)) return i;
            return -1;
        }
    }
}
