using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Per-mod default presets, picked in the Customise window: a settings preset and an asset
    /// preset to apply each time that mod is switched to. Stored in AppSettings keyed by mod id,
    /// never in the manifest. A mod's suggestedSettingsPreset / suggestedAssetPreset only
    /// PRE-SELECTS the dropdown; nothing applies until the user's choice is stored, and an
    /// explicit dropdown choice (or pressing "Use this mod" with it showing) is the consent.
    /// PURE: callers pass the maps and presets in.
    /// </summary>
    public static class ModPresetDefaults
    {
        /// <summary>Stored value meaning "Keep current", chosen on purpose.</summary>
        public const string KeepCurrent = "";

        /// <summary>A mod's suggested settings preset, matched by id first, then by name
        /// (case-insensitive). Null when the mod names none or the name matches nothing.</summary>
        public static Preset? ResolveSettings(IEnumerable<Preset>? presets, string? key) =>
            Resolve(presets, key, p => p.Id, p => p.Name);

        /// <summary>A mod's suggested asset preset, same matching as <see cref="ResolveSettings"/>.</summary>
        public static AssetPreset? ResolveAssets(IEnumerable<AssetPreset>? presets, string? key) =>
            Resolve(presets, key, p => p.Id, p => p.Name);

        /// <summary>True when the user has made a choice for this mod, "Keep current" included.</summary>
        public static bool HasChoice(IReadOnlyDictionary<string, string>? map, string? modId) =>
            map != null && !string.IsNullOrWhiteSpace(modId) && map.ContainsKey(modId);

        /// <summary>
        /// The preset id to apply when <paramref name="modId"/> is activated: the user's stored
        /// choice only. Null for "Keep current", for no choice, and for a blank id. A suggestion the
        /// user never confirmed is NOT applied.
        /// </summary>
        public static string? ForActivation(IReadOnlyDictionary<string, string>? map, string? modId)
        {
            if (!HasChoice(map, modId)) return null;
            var id = map![modId!];
            return string.IsNullOrWhiteSpace(id) ? null : id;
        }

        /// <summary>
        /// What the dropdown shows: the stored choice when there is one ("" = Keep current),
        /// otherwise the mod's resolved suggestion, otherwise Keep current.
        /// </summary>
        public static string Initial(IReadOnlyDictionary<string, string>? map, string? modId, string? suggestedId)
        {
            if (HasChoice(map, modId)) return map![modId!] ?? KeepCurrent;
            return suggestedId ?? KeepCurrent;
        }

        /// <summary>Records the user's choice. Null or blank stores "Keep current".</summary>
        public static void Store(IDictionary<string, string> map, string? modId, string? presetId)
        {
            if (string.IsNullOrWhiteSpace(modId)) return;
            map[modId!] = string.IsNullOrWhiteSpace(presetId) ? KeepCurrent : presetId!;
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
