using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Migrations
{
    /// <summary>
    /// One-shot rewrite of the Circe's Lock text a user's settings.json saved BEFORE the
    /// gender-neutral pass. The manifest change alone reaches almost nobody who already ran Circe,
    /// because the old defaults were copied into places ModService never re-derives:
    ///
    /// <list type="bullet">
    ///   <item>the per-mod pool backups. "GOOD BOY" is no longer any built-in's default, so the
    ///         cross-mod prune now reads it as user-added and keeps it forever, and
    ///         CustomTriggers / BouncingTextPool have no top-up to bring "GOOD PET" in;</item>
    ///   <item>Phrase Manager toggles keyed on a voice-line FILENAME or a text-only bark's TEXT, both
    ///         of which changed, so a line the user switched off would come back on;</item>
    ///   <item>a custom prompt holding a pasted copy of one of the six old Circe prompts;</item>
    ///   <item>session.json, whose old "good boy" replies are the strongest few-shot a small model
    ///         sees until the persona fence moves.</item>
    /// </list>
    ///
    /// <para>Rules. Only an EXACT, case-sensitive match of an old default (see
    /// CirceNeutralMigration.OldStrings.cs) is rewritten, so anything the user typed or edited is
    /// left alone, as is anything they explicitly added by hand (UserAddedSubliminals /
    /// UserAddedCustomTriggers). An entry keeps its position and its enabled flag. If the new text is
    /// already there too, one entry survives at the earlier position and is enabled only if BOTH
    /// were: a user who switched either copy off keeps it off. Toggle ids are ADDED for the new line
    /// and the old id is kept, because a user whose content pack has not refreshed still has the old
    /// file on disk, and it must stay off too.</para>
    ///
    /// <para>Runs from SettingsService.Load, after MigrateFromContentModeToMod (so ActiveModId is
    /// final) and before ModService.Initialize applies any pool, and from SettingsService.RestoreFrom
    /// before a restored cloud backup is handed to ModService. Latches
    /// <see cref="AppSettings.CirceNeutralTextMigrated"/> whether or not anything matched.</para>
    /// </summary>
    internal static partial class CirceNeutralMigration
    {
        /// <summary>What one run changed. <see cref="ToString"/> is the single log line.</summary>
        internal sealed class Result
        {
            public int PoolEntries { get; set; }
            public int ToggleIds { get; set; }
            public int RemovedDefaults { get; set; }
            public int Prompts { get; set; }
            public bool FencedHistory { get; set; }

            public bool ChangedAnything =>
                PoolEntries > 0 || ToggleIds > 0 || RemovedDefaults > 0 || Prompts > 0 || FencedHistory;

            public override string ToString() =>
                $"pool entries {PoolEntries}, toggle ids {ToggleIds}, removed-default marks {RemovedDefaults}, " +
                $"prompts {Prompts}, history fenced {(FencedHistory ? "yes" : "no")}";
        }

        /// <summary>
        /// Applies the migration once. Returns null when it had already run (nothing is touched), or
        /// the counts of what this run changed. Pure over <paramref name="settings"/>: no App
        /// statics, no disk, so it is unit tested directly.
        /// </summary>
        /// <param name="nowUtc">Clock for the persona fence; tests pass a fixed value.</param>
        internal static Result? Apply(AppSettings settings, DateTime? nowUtc = null)
        {
            if (settings == null || settings.CirceNeutralTextMigrated) return null;

            var result = new Result();
            var lockedActive = string.Equals(settings.ActiveModId, BuiltInMods.LockedId, StringComparison.Ordinal);
            var changes = 0;

            // ---- Pools ----------------------------------------------------------------------
            // The builtin-locked backup is Circe's by definition. The ACTIVE pools are only Circe's
            // while Circe is the active mod; under any other mod a "GOOD BOY" there is the user's own
            // (the cross-mod prune has removed Circe's defaults from it on every launch since 6.x),
            // or a third-party mod's default, and neither is ours to rename.
            var lockedId = BuiltInMods.LockedId;

            if (settings.SubliminalPoolByMod?.TryGetValue(lockedId, out var subBackup) == true && subBackup != null)
                settings.SubliminalPoolByMod[lockedId] = RenameKeys(subBackup, PoolPhraseMap, settings.UserAddedSubliminals, ref changes);
            if (settings.LockCardPhrasesByMod?.TryGetValue(lockedId, out var lockBackup) == true && lockBackup != null)
                settings.LockCardPhrasesByMod[lockedId] = RenameKeys(lockBackup, LockCardPhraseMap, null, ref changes);
            if (settings.BouncingTextPoolByMod?.TryGetValue(lockedId, out var bounceBackup) == true && bounceBackup != null)
                settings.BouncingTextPoolByMod[lockedId] = RenameKeys(bounceBackup, PoolPhraseMap, null, ref changes);
            if (settings.CustomTriggersByMod?.TryGetValue(lockedId, out var triggerBackup) == true && triggerBackup != null)
                settings.CustomTriggersByMod[lockedId] = RenameItems(triggerBackup, PoolPhraseMap, settings.UserAddedCustomTriggers, ref changes);

            if (lockedActive)
            {
                // Assigning raises INPC, but ModService is not subscribed yet at load time, and the
                // setters only replace a null with an empty pool.
                settings.SubliminalPool = RenameKeys(settings.SubliminalPool, PoolPhraseMap, settings.UserAddedSubliminals, ref changes);
                settings.LockCardPhrases = RenameKeys(settings.LockCardPhrases, LockCardPhraseMap, null, ref changes);
                settings.BouncingTextPool = RenameKeys(settings.BouncingTextPool, PoolPhraseMap, null, ref changes);
                settings.CustomTriggers = RenameItems(settings.CustomTriggers, PoolPhraseMap, settings.UserAddedCustomTriggers, ref changes);
            }
            result.PoolEntries = changes;

            // A user who deleted the old default "GOOD BOY" must not have "GOOD PET" topped up in its
            // place by RestorePoolsFromSettings on the next boot. The old mark stays: harmless, and
            // still correct if a third-party mod ships the old phrase.
            var removed = settings.RemovedDefaultSubliminals;
            if (removed != null)
                foreach (var (oldText, newText) in PoolPhraseMap)
                    if (removed.Contains(oldText) && removed.Add(newText))
                        result.RemovedDefaults++;

            // ---- Phrase Manager toggles -----------------------------------------------------
            // The live sets, and the same sets frozen into saved Phrase Manager presets, which
            // would otherwise put the stale ids back the next time a preset is applied.
            result.ToggleIds += CarryRenamedIds(settings.DisabledPhraseIds);
            result.ToggleIds += CarryRenamedIds(settings.RemovedPhraseIds);
            result.ToggleIds += CarryRenamedKeys(settings.PhraseAudioOverrides);
            if (settings.PhrasePresets != null)
                foreach (var preset in settings.PhrasePresets)
                {
                    if (preset == null) continue;
                    result.ToggleIds += CarryRenamedIds(preset.DisabledPhraseIds);
                    result.ToggleIds += CarryRenamedIds(preset.RemovedPhraseIds);
                    result.ToggleIds += CarryRenamedKeys(preset.PhraseAudioOverrides);
                }

            // ---- Prompts --------------------------------------------------------------------
            // Picking a preset stores only its id, so the manifest text reaches those users on its
            // own. What does not is a custom prompt that is a pasted copy: replace it only while it is
            // byte-identical to an old prompt, never an edited one.
            var newPrompts = NewPromptsById();
            if (TryReplacePrompt(settings.CompanionPrompt, newPrompts)) result.Prompts++;
            if (settings.UserPersonalityPresets != null)
                foreach (var preset in settings.UserPersonalityPresets)
                    if (TryReplacePrompt(preset?.PromptSettings, newPrompts)) result.Prompts++;

            // ---- Chat history ---------------------------------------------------------------
            // session.json is left intact (it is the user's conversation), but her old-voice replies
            // are fenced off the wire exactly as a persona pick would fence them. Only for someone
            // for whom Circe is actually talking: the active mod, or a replaced custom prompt.
            if (lockedActive || result.Prompts > 0)
            {
                var now = nowUtc ?? DateTime.UtcNow;
                if (settings.PersonaVoiceFenceUtc is not DateTime fence || fence < now)
                {
                    settings.PersonaVoiceFenceUtc = now;
                    result.FencedHistory = true;
                }
            }

            settings.CirceNeutralTextMigrated = true;
            return result;
        }

        /// <summary>
        /// Renames the mapped keys of a pool, keeping order and each entry's flag. Returns the
        /// SAME instance when nothing matches, so an untouched pool is not reallocated.
        /// </summary>
        internal static Dictionary<string, bool> RenameKeys(
            Dictionary<string, bool>? pool, IReadOnlyDictionary<string, string> map,
            ISet<string>? userOwned, ref int changes)
        {
            if (pool == null) return new Dictionary<string, bool>();
            if (!pool.Keys.Any(k => ShouldRename(k, map, userOwned, out _))) return pool;

            var result = new Dictionary<string, bool>(pool.Comparer);
            foreach (var (key, enabled) in pool)
            {
                var target = key;
                if (ShouldRename(key, map, userOwned, out var renamed))
                {
                    target = renamed;
                    changes++;
                }

                // Both the old and the new text present: one survives, at the earlier position, and
                // it is off if either was off.
                result[target] = result.TryGetValue(target, out var already) ? already && enabled : enabled;
            }
            return result;
        }

        /// <summary>
        /// The list form (CustomTriggers, where presence is the flag). A renamed entry whose new text
        /// is already in the list is dropped rather than duplicated; duplicates the user made
        /// themselves are not ours to collapse.
        /// </summary>
        internal static List<string> RenameItems(
            List<string>? list, IReadOnlyDictionary<string, string> map,
            ISet<string>? userOwned, ref int changes)
        {
            if (list == null) return new List<string>();
            if (!list.Any(t => ShouldRename(t, map, userOwned, out _))) return list;

            var result = new List<string>(list.Count);
            foreach (var item in list)
            {
                if (!ShouldRename(item, map, userOwned, out var renamed))
                {
                    result.Add(item);
                    continue;
                }

                changes++;
                if (!list.Contains(renamed, StringComparer.Ordinal) && !result.Contains(renamed, StringComparer.Ordinal))
                    result.Add(renamed);
            }
            return result;
        }

        private static bool ShouldRename(
            string? key, IReadOnlyDictionary<string, string> map, ISet<string>? userOwned, out string renamed)
        {
            renamed = "";
            if (key == null || !map.TryGetValue(key, out var target)) return false;
            if (userOwned != null && userOwned.Contains(key)) return false;
            renamed = target;
            return true;
        }

        /// <summary>Every (old id, new id) pair whose line changed text or filename.</summary>
        internal static IEnumerable<(string OldId, string NewId)> RenamedPhraseIds()
        {
            foreach (var (oldStem, newStem) in VoiceLineStemMap)
                yield return (VoiceLineIdFor(oldStem), VoiceLineIdFor(newStem));
            foreach (var (ruleId, oldText, newText) in TextOnlyBarks)
                yield return (TextBarkIdFor(ruleId, oldText), TextBarkIdFor(ruleId, newText));
        }

        /// <summary>
        /// Adds the new id beside each old id present. The old id is kept (see the class remarks),
        /// so a toggle can only stay as strict as it was. Returns how many ids were added.
        /// </summary>
        internal static int CarryRenamedIds(HashSet<string>? ids)
        {
            if (ids == null || ids.Count == 0) return 0;
            var added = 0;
            foreach (var (oldId, newId) in RenamedPhraseIds())
                if (ids.Contains(oldId) && ids.Add(newId))
                    added++;
            return added;
        }

        /// <summary>The dictionary form (PhraseAudioOverrides): copies the value, never overwrites.</summary>
        internal static int CarryRenamedKeys(Dictionary<string, string>? map)
        {
            if (map == null || map.Count == 0) return 0;
            var added = 0;
            foreach (var (oldId, newId) in RenamedPhraseIds())
                if (map.TryGetValue(oldId, out var value) && map.TryAdd(newId, value))
                    added++;
            return added;
        }

        /// <summary>Same shape as <see cref="CompanionPhraseIds.VoiceLineId"/>, from a stem.</summary>
        internal static string VoiceLineIdFor(string stem) =>
            CompanionPhraseIds.VoiceLineCategory + ":" + stem;

        /// <summary>Same shape as <see cref="CompanionPhraseIds.BarkLineId"/> for a variant with no audio.</summary>
        internal static string TextBarkIdFor(string ruleId, string text) =>
            CompanionPhraseIds.BarkLineId(ruleId, text, audio: null);

        /// <summary>The CURRENT Circe prompts, from the manifest, keyed by preset id.</summary>
        private static Dictionary<string, string> NewPromptsById()
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var p in BuiltInMods.Locked.Personalities ?? new List<ModPersonality>())
                if (p?.PromptSettings != null && p.PromptSettings.TryGetValue("Personality", out var text) && !string.IsNullOrEmpty(text))
                    map[p.Id] = text;
            return map;
        }

        private static bool TryReplacePrompt(CompanionPromptSettings? prompt, IReadOnlyDictionary<string, string> newPrompts)
        {
            if (prompt == null) return false;
            var changed = false;

            if (TryMatchOldPrompt(prompt.Personality, newPrompts, out var personality))
            {
                prompt.Personality = personality;
                changed = true;
            }
            if (TryMatchOldPrompt(prompt.SlutModePersonality, newPrompts, out var slut))
            {
                prompt.SlutModePersonality = slut;
                changed = true;
            }
            return changed;
        }

        private static bool TryMatchOldPrompt(string? text, IReadOnlyDictionary<string, string> newPrompts, out string replacement)
        {
            replacement = "";
            if (string.IsNullOrEmpty(text)) return false;
            foreach (var (id, oldText) in OldPersonalityPrompts)
            {
                if (!string.Equals(text, oldText, StringComparison.Ordinal)) continue;
                if (!newPrompts.TryGetValue(id, out var newText)) return false;
                replacement = newText;
                return true;
            }
            return false;
        }
    }
}
