using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// A mod may RECOMMEND a settings preset and an asset preset. These are suggestions, never
    /// silent overwrites: the Customise window asks once, the first time the mod is activated,
    /// and never again on later switches. PURE: callers pass in the presets and the asked set.
    /// </summary>
    public static class ModSuggestions
    {
        /// <summary>The recommended settings preset, matched by id first, then by name
        /// (case-insensitive). Null when the mod names none or the name matches nothing.</summary>
        public static Preset? ResolveSettings(IEnumerable<Preset>? presets, string? key) =>
            Resolve(presets, key, p => p.Id, p => p.Name);

        /// <summary>The recommended asset preset, same matching as <see cref="ResolveSettings"/>.</summary>
        public static AssetPreset? ResolveAssets(IEnumerable<AssetPreset>? presets, string? key) =>
            Resolve(presets, key, p => p.Id, p => p.Name);

        /// <summary>
        /// True when activating <paramref name="modId"/> should ask "use the recommended setup?":
        /// the mod has at least one suggestion that resolves, and this mod has never been asked
        /// about before. A "Keep mine" answer counts as asked, exactly like a yes.
        /// </summary>
        public static bool ShouldAsk(string? modId, bool hasResolvedSuggestion, ICollection<string>? asked)
        {
            if (string.IsNullOrWhiteSpace(modId) || !hasResolvedSuggestion) return false;
            return asked == null || !asked.Contains(modId);
        }

        /// <summary>Records that <paramref name="modId"/> was asked. Returns true when this is new.</summary>
        public static bool MarkAsked(string? modId, ICollection<string> asked)
        {
            if (string.IsNullOrWhiteSpace(modId) || asked.Contains(modId)) return false;
            asked.Add(modId);
            return true;
        }

        private static T? Resolve<T>(IEnumerable<T>? items, string? key, Func<T, string?> id, Func<T, string?> name)
            where T : class
        {
            if (items == null || string.IsNullOrWhiteSpace(key)) return null;
            var k = key.Trim();
            var list = items.Where(x => x != null).ToList();
            return list.FirstOrDefault(x => string.Equals(id(x), k, StringComparison.Ordinal))
                ?? list.FirstOrDefault(x => string.Equals(name(x), k, StringComparison.OrdinalIgnoreCase));
        }
    }
}
