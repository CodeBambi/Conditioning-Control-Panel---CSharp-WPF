using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Display names for the BUILT-IN settings presets and sessions, per mod, with a couple of
    /// name variants each (owner, 2026-09-25: "make them mod aware and use variations on the
    /// names"). Only the displayed name moves: preset and session ids, <c>Preset.Name</c>,
    /// <c>Session.Name</c> and <c>AppSettings.CurrentPresetName</c> are untouched, because
    /// settings, cloud sync and the Customise per-mod defaults key on them.
    ///
    /// The variant is picked by a stable hash of (install seed, slot, mod), so a name never
    /// changes between launches but two installs, or two mods, can read differently. The seed is
    /// <c>AppSettings.InstallDate</c>, written once. Never use string.GetHashCode here: it is
    /// randomised per process.
    ///
    /// User presets and custom / imported sessions are NEVER renamed. Mods without a table (any
    /// community mod) read the CCP Default names.
    ///
    /// The key lookup is PURE (no App); <see cref="DisplayName(Preset, string?)"/> and
    /// <see cref="DisplayName(Session, string?)"/> are the thin app-facing wrappers.
    /// </summary>
    public static class PresetNaming
    {
        public const int VariantsPerMod = 2;
        public const string DefaultModTag = "default";

        /// <summary>Built-in settings preset id -> name slot.</summary>
        private static readonly Dictionary<string, string> PresetSlots = new(StringComparer.Ordinal)
        {
            ["default-gentle"] = "gentle",
            ["default-basics"] = "basics",
            ["default-pink-cloud"] = "pink_cloud",
            ["default-deep"] = "deep",
            ["default-surrender"] = "surrender",
        };

        /// <summary>Built-in session ids; each id is its own slot.</summary>
        private static readonly string[] SessionSlots =
        {
            "morning_drift", "gamer_girl", "distant_doll", "good_girls_dont_cum",
            "deep_dive", "bambi_time", "random_drop",
        };

        /// <summary>Mod id -> the tag its name table lives under.</summary>
        private static readonly Dictionary<string, string> ModTags = new(StringComparer.Ordinal)
        {
            [BuiltInMods.CCPDefaultId] = DefaultModTag,
            [BuiltInMods.BambiSleepId] = "bambi",
            [BuiltInMods.SissyHypnoId] = "sissy",
            [BuiltInMods.LockedId] = "locked",
            [BuiltInMods.DronificationId] = "drone",
            [BuiltInMods.InfectionControlId] = "infection",
        };

        public static IReadOnlyCollection<string> BuiltInPresetIds => PresetSlots.Keys;
        public static IReadOnlyList<string> BuiltInSessionIds => SessionSlots;
        public static IReadOnlyCollection<string> NamedModIds => ModTags.Keys;

        /// <summary>The name table a mod reads: its own, or CCP Default's.</summary>
        public static string ModTag(string? modId) =>
            modId != null && ModTags.TryGetValue(modId, out var tag) ? tag : DefaultModTag;

        /// <summary>Loc key for a built-in preset's name, or null when the id is not built in.</summary>
        public static string? PresetKey(string? presetId, string? modId, string? seed) =>
            presetId != null && PresetSlots.TryGetValue(presetId, out var slot)
                ? KeyFor(slot, ModTag(modId), seed)
                : null;

        /// <summary>Loc key for a built-in session's name, or null when the id is not built in.</summary>
        public static string? SessionKey(string? sessionId, string? modId, string? seed) =>
            sessionId != null && Array.IndexOf(SessionSlots, sessionId) >= 0
                ? KeyFor(sessionId, ModTag(modId), seed)
                : null;

        /// <summary>Every key the tables can hand out, for the loc completeness test.</summary>
        public static IEnumerable<string> AllKeys()
        {
            var slots = PresetSlots.Values.Concat(SessionSlots);
            var tags = ModTags.Values.Distinct();
            foreach (var slot in slots)
                foreach (var tag in tags)
                    for (int v = 1; v <= VariantsPerMod; v++)
                        yield return Key(slot, tag, v);
        }

        /// <summary>The keys of one mod's table (CCP Default when the mod has none).</summary>
        public static IEnumerable<string> KeysForMod(string? modId)
        {
            var tag = ModTag(modId);
            foreach (var slot in PresetSlots.Values.Concat(SessionSlots))
                for (int v = 1; v <= VariantsPerMod; v++)
                    yield return Key(slot, tag, v);
        }

        internal static string KeyFor(string slot, string modTag, string? seed) =>
            Key(slot, modTag, PickVariant(slot, modTag, seed));

        internal static string Key(string slot, string modTag, int variant) =>
            $"preset_name_{slot}_{modTag}_{variant}";

        /// <summary>1-based variant, stable for a given seed, slot and mod.</summary>
        internal static int PickVariant(string slot, string modTag, string? seed) =>
            (int)(Fnv1a($"{seed ?? ""}|{slot}|{modTag}") % VariantsPerMod) + 1;

        private static uint Fnv1a(string s)
        {
            uint hash = 2166136261;
            foreach (var c in s)
            {
                hash ^= c;
                hash *= 16777619;
            }
            return hash;
        }

        // ---- app-facing ------------------------------------------------------------------

        private static string? ActiveModId => App.Mods?.ActiveModId;
        private static string? InstallSeed => App.Settings?.Current?.InstallDate;

        /// <summary>
        /// The name to SHOW for a settings preset. <paramref name="modId"/> defaults to the active
        /// mod; the Customise window passes the mod whose defaults it is editing.
        /// </summary>
        public static string DisplayName(Preset preset, string? modId = null)
        {
            if (preset == null) return "";
            if (preset.IsDefault)
            {
                var name = Lookup(PresetKey(preset.Id, modId ?? ActiveModId, InstallSeed));
                if (name != null) return name;
            }
            return App.Mods?.MakeModAware(preset.Name) ?? preset.Name;
        }

        /// <summary>The name to SHOW for a session. Only built-in sessions are renamed.</summary>
        public static string DisplayName(Session session, string? modId = null)
        {
            if (session == null) return "";
            if (session.Source == SessionSource.BuiltIn)
            {
                var name = Lookup(SessionKey(session.Id, modId ?? ActiveModId, InstallSeed));
                if (name != null) return name;
            }
            return App.Mods?.MakeModAware(session.Name) ?? session.Name;
        }

        /// <summary>Loc text for a key, or null when the key is null or has no string.</summary>
        private static string? Lookup(string? key)
        {
            if (key == null) return null;
            var text = Loc.Get(key);
            return string.IsNullOrEmpty(text) || text == key ? null : text;
        }
    }
}
