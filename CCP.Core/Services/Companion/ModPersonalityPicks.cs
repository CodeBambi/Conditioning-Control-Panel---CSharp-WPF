using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Companion
{
    /// <summary>
    /// The companion PERSONALITY remembered per mod id, the twin of <see cref="ModAvatarLooks"/>:
    /// a mod switch and back puts the personality the user picked in that mod back on. Stored in
    /// <c>AppSettings.ModPersonalityPreset</c>, never in the manifest. Only an explicit pick is
    /// recorded (<c>PersonalityService.SetActivePreset</c>, which the Customise picker, the
    /// companion room chips, the chat command and the studio all go through); a mod-change
    /// fallback and pressing Use with the mod's suggested personality untouched record nothing.
    /// PURE: callers pass the map and the validity check in.
    /// </summary>
    public static class ModPersonalityPicks
    {
        /// <summary>The personality stored for this mod, or null when there is none, it is blank,
        /// or the mod cannot offer it right now (<paramref name="isAvailable"/> says no).</summary>
        public static string? StoredFor(IReadOnlyDictionary<string, string>? map, string? modId, Func<string, bool>? isAvailable)
        {
            if (map == null || string.IsNullOrWhiteSpace(modId)) return null;
            if (!map.TryGetValue(modId!, out var id) || string.IsNullOrWhiteSpace(id)) return null;
            if (isAvailable != null && !isAvailable(id)) return null;
            return id;
        }

        /// <summary>
        /// The personality to switch to after a mod switch, or null to leave the current one
        /// alone (nothing stored, the stored one is gone, or it is already the active one).
        /// </summary>
        public static string? ForModSwitch(IReadOnlyDictionary<string, string>? map, string? modId,
            string? currentId, Func<string, bool>? isAvailable)
        {
            var stored = StoredFor(map, modId, isAvailable);
            if (stored == null) return null;
            return string.Equals(stored, currentId, StringComparison.Ordinal) ? null : stored;
        }

        /// <summary>Records an explicit pick in <paramref name="modId"/>. Ignores a blank mod id
        /// or preset id.</summary>
        public static void Store(IDictionary<string, string>? map, string? modId, string? presetId)
        {
            if (map == null || string.IsNullOrWhiteSpace(modId) || string.IsNullOrWhiteSpace(presetId)) return;
            map[modId!] = presetId!;
        }
    }
}
