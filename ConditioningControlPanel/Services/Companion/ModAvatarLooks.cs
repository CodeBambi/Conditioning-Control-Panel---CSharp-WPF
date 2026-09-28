using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services.Companion
{
    /// <summary>
    /// The companion LOOK (avatar set, or portrait skin) remembered per mod id, so a mod switch
    /// and back puts the look the user picked in that mod back on. Stored in
    /// <c>AppSettings.ModAvatarSet</c>, never in the manifest. Only a real switch to a set is
    /// recorded (the Customise picker, the tube arrows, a level-up or companion switch); opening
    /// the picker and pressing Use with the look untouched records nothing, because nothing
    /// switched. A mod-change fallback is not a pick and is never recorded.
    /// PURE: callers pass the map and the pickable sets in.
    /// </summary>
    public static class ModAvatarLooks
    {
        /// <summary>The look stored for this mod, or null when there is none, it is not a
        /// positive set number, or the mod cannot show it right now.</summary>
        public static int? StoredFor(IReadOnlyDictionary<string, int>? map, string? modId, IEnumerable<int>? pickable)
        {
            if (map == null || string.IsNullOrWhiteSpace(modId)) return null;
            if (!map.TryGetValue(modId!, out var set) || set < 1) return null;
            return pickable != null && pickable.Contains(set) ? set : null;
        }

        /// <summary>
        /// The look to show after switching to <paramref name="modId"/>: the look stored for that
        /// mod, then the last look picked anywhere (<paramref name="globalPick"/>, which a mod
        /// without a picker leaves alone), then the look already showing, then the first pickable
        /// one. With nothing pickable the look already showing stays.
        /// </summary>
        public static int ForModSwitch(IReadOnlyDictionary<string, int>? map, string? modId,
            int globalPick, int current, IReadOnlyList<int>? pickable)
        {
            if (pickable == null || pickable.Count == 0) return current;
            if (StoredFor(map, modId, pickable) is { } stored) return stored;
            if (pickable.Contains(globalPick)) return globalPick;
            if (pickable.Contains(current)) return current;
            return pickable[0];
        }

        /// <summary>Records the look now showing in <paramref name="modId"/>. Ignores a blank
        /// mod id and a set number below 1.</summary>
        public static void Store(IDictionary<string, int> map, string? modId, int set)
        {
            if (map == null || string.IsNullOrWhiteSpace(modId) || set < 1) return;
            map[modId!] = set;
        }
    }
}
