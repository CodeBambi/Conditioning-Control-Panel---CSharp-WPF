using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Which mods may use which shared, themed audio (owner pivot 2026-09-25: CCP Default is a plain,
    /// gender-neutral mod with classic triggers only). Pure, so the rules are unit tested in one place
    /// and every caller asks the same question.
    ///
    /// <para><b>Shared whisper clips</b> (<c>Resources/sub_audio</c>): all 21 are Bambi-voiced. Built-in
    /// mods have no <c>InstalledPath</c>, so Bambi Sleep and Sissy Hypno get their trigger audio from
    /// this bundled folder and nowhere else; it stays in the box for them. CCP Default (and an unknown
    /// mod id, which at boot means the default) never falls back to it, so a CCP Default player who
    /// types GOOD GIRL gets text only.</para>
    ///
    /// <para><b>Baseline flash voice</b> (the <c>audio-base</c> pack, <c>Resources/sounds/flashes_audio</c>):
    /// only Bambi Sleep plays it (<see cref="Companion.CompanionContentResolver.OwnsBaselineVoiceLines"/>),
    /// so only Bambi Sleep fetches it.</para>
    /// </summary>
    public static class ModAudioPolicy
    {
        /// <summary>True when the mod may fall back to the bundled <c>Resources/sub_audio</c> clips.</summary>
        public static bool UsesSharedSubAudio(string? modId)
        {
            if (string.IsNullOrWhiteSpace(modId)) return false;
            return !string.Equals(modId, BuiltInMods.CCPDefaultId, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>True when the mod plays the baseline flash voice, so the <c>audio-base</c> pack
        /// is worth downloading for it. An unknown id fetches nothing: the settings default is CCP
        /// Default and a real session lands on a concrete id.</summary>
        public static bool UsesBaselineVoicePack(string? modId)
            => string.Equals(modId, BuiltInMods.BambiSleepId, StringComparison.OrdinalIgnoreCase);

        /// <summary>True when speech bubbles must not play the canned giggle1-8 SFX. Bambi Sleep has
        /// real voiceline barks (the giggle read as cheap next to them); CCP Default is gender-neutral
        /// and the giggle is feminine-coded.</summary>
        public static bool SuppressesGiggleSfx(string? modId)
        {
            if (string.IsNullOrWhiteSpace(modId)) return false;
            if (string.Equals(modId, BuiltInMods.CCPDefaultId, StringComparison.OrdinalIgnoreCase)) return true;
            return modId.Contains("bambi", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The themed built-in awareness presets and the mods they belong to. A preset not
        /// listed here (trance, any custom preset) shows under every mod.</summary>
        private static readonly Dictionary<string, string[]> PresetHomeMods = new(StringComparer.OrdinalIgnoreCase)
        {
            ["builtin.bimbo"] = new[] { BuiltInMods.BambiSleepId, BuiltInMods.SissyHypnoId },
            // No pet-play mod exists; its "good boy / good girl" lines sit closest to the two
            // feminising mods. Owner call if a different home is wanted.
            ["builtin.puppy"] = new[] { BuiltInMods.BambiSleepId, BuiltInMods.SissyHypnoId },
            ["builtin.chastity"] = new[] { BuiltInMods.LockedId },
        };

        /// <summary>
        /// True when the awareness preset card should show under the active mod. A themed preset shows
        /// only under its own mods, except that one the player already has ON always stays visible
        /// (hiding it would leave its triggers running with no card to switch them off), and a
        /// user-installed third-party mod keeps the whole library as before.
        /// </summary>
        public static bool AwarenessPresetVisible(string? presetId, string? modId, bool installed, bool modIsBuiltIn)
        {
            if (string.IsNullOrEmpty(presetId)) return true;
            if (!PresetHomeMods.TryGetValue(presetId, out var homes)) return true;
            if (installed) return true;
            if (!string.IsNullOrWhiteSpace(modId) && !modIsBuiltIn) return true;
            if (string.IsNullOrWhiteSpace(modId)) return false;
            return Array.Exists(homes, h => string.Equals(h, modId, StringComparison.OrdinalIgnoreCase));
        }
    }
}
