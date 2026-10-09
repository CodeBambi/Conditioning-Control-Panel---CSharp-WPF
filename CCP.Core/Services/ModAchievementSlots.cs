using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Builds the Mod Creator's achievement art slots from <see cref="Achievement.All"/>.
    ///
    /// Split out of <c>ModCreatorWindow</c> (both heads) so the derivation is testable without a
    /// Dispatcher, and derived rather than hand-listed because the hand-listed version rotted:
    /// it stopped at 29 of the registry's 69 achievements and still offered a slot for a badge
    /// (how_many.png) that no achievement claims any more.
    /// </summary>
    internal static class ModAchievementSlots
    {
        /// <summary>
        /// One slot per distinct badge file, in registry order.
        ///
        /// The key is the packed resource path (<c>achievements/&lt;ImageName&gt;</c>); the name is
        /// the label printed over the art slot. Two achievements can share one badge file
        /// (first_week_graduate reuses daily_maintenance.png) and every dictionary in the editor
        /// is keyed by resource key, so a duplicate would collide and throw. The shared file gets
        /// a single slot whose label names both achievements it feeds, so an author can see what
        /// their art will be used for.
        /// </summary>
        public static (string Key, string Name)[] Build()
        {
            var slots = new List<(string Key, string Name)>();
            // Ordinal-ignore-case because the key becomes a filename inside the .ccpmod, and two
            // ImageNames differing only in case would be one file on disk.
            var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var achievement in Achievement.All.Values)
            {
                if (string.IsNullOrWhiteSpace(achievement.ImageName)) continue;

                var key = $"achievements/{achievement.ImageName}";
                var name = SlotName(achievement);

                if (seen.TryGetValue(key, out var index))
                {
                    var existing = slots[index].Name;
                    if (existing.IndexOf(name, StringComparison.OrdinalIgnoreCase) < 0)
                        slots[index] = (slots[index].Key, $"{existing} / {name}");
                    continue;
                }

                seen[key] = slots.Count;
                slots.Add((key, name));
            }

            return slots.ToArray();
        }

        /// <summary>
        /// The achievement's localized name, falling back to its built-in English name when the
        /// localization key is missing - LocalizationManager echoes an unknown key straight back,
        /// which would print a raw <c>achievement_x_name</c> over the art slot (only 40 of the 69
        /// achievements currently carry a name key). Same rule the profile title uses in
        /// MainWindow.ProfileCosmetics.ResolveAchievementTitle. Mod-awareness is applied later by
        /// BuildImageSlotsSection, exactly as it is for every other slot list.
        /// </summary>
        private static string SlotName(Achievement achievement)
        {
            var localized = achievement.LocalizedName;
            return string.IsNullOrWhiteSpace(localized) || localized == $"achievement_{achievement.Id}_name"
                ? achievement.Name
                : localized;
        }
    }
}
