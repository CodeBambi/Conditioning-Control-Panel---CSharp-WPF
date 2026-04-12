using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Install/uninstall logic for Awareness Engine keyword trigger presets.
    /// Registered on <see cref="App.KeywordPresets"/>.
    ///
    /// Presets themselves are persisted in <see cref="AppSettings.KeywordTriggerPresets"/>;
    /// installing a preset clones its bundled triggers into
    /// <see cref="AppSettings.KeywordTriggers"/> with a <c>preset:&lt;presetId&gt;:&lt;origId&gt;</c>
    /// id prefix so uninstall can find and remove them deterministically.
    /// </summary>
    public class KeywordTriggerPresetService
    {
        private const string TriggerIdPrefix = "preset:";

        /// <summary>
        /// Event raised whenever an install/uninstall completes, so UIs can refresh
        /// card state, pulse feed counters, etc.
        /// </summary>
        public event EventHandler? PresetsChanged;

        /// <summary>Presets suitable for display in the Awareness tab card grid.</summary>
        public IReadOnlyList<KeywordTriggerPreset> VisiblePresets
        {
            get
            {
                var list = App.Settings?.Current?.KeywordTriggerPresets;
                if (list == null || list.Count == 0) return Array.Empty<KeywordTriggerPreset>();
                return list.ToList();
            }
        }

        public KeywordTriggerPreset? GetPreset(string presetId)
        {
            if (string.IsNullOrEmpty(presetId)) return null;
            return App.Settings?.Current?.KeywordTriggerPresets
                .FirstOrDefault(p => p.Id == presetId);
        }

        public bool IsInstalled(string presetId)
            => GetPreset(presetId)?.MasterEnabled == true;

        /// <summary>
        /// Installs a preset by cloning its triggers into the user's KeywordTriggers list,
        /// injecting its canned phrase pools, and flipping MasterEnabled.
        ///
        /// If AI is currently unavailable, AvatarCommentActions on the cloned triggers
        /// are disabled (action.Enabled = false). They are re-enabled automatically by
        /// <see cref="RefreshAiGating"/> when AI becomes available later.
        /// </summary>
        public bool InstallPreset(string presetId)
        {
            var settings = App.Settings?.Current;
            var preset = GetPreset(presetId);
            if (settings == null || preset == null) return false;

            if (preset.MasterEnabled)
            {
                App.Logger?.Debug("InstallPreset: {Id} already installed", presetId);
                return true;
            }

            var prefix = TriggerIdPrefix + presetId + ":";

            // Remove any stale clones from previous installs (defensive).
            settings.KeywordTriggers.RemoveAll(t => t.Id?.StartsWith(prefix, StringComparison.Ordinal) == true);

            var aiAvailable = App.Ai?.IsAvailable == true;

            foreach (var source in preset.Triggers)
            {
                if (source == null) continue;
                var clone = source.Clone();

                // Deterministic id for uninstall lookups.
                clone.Id = prefix + (string.IsNullOrEmpty(source.Id) ? Guid.NewGuid().ToString("N")[..8] : source.Id);
                clone.LastTriggeredAt = DateTime.MinValue;

                if (clone.Actions != null && clone.Actions.Count > 0)
                {
                    foreach (var action in clone.Actions.OfType<AvatarCommentAction>())
                    {
                        if (action.RequireAiAvailable && !aiAvailable)
                            action.Enabled = false;
                    }
                }
                else
                {
                    KeywordTriggerService.RebuildActionsFromFlatFields(clone);
                }

                settings.KeywordTriggers.Add(clone);
            }

            InstallCannedPhrases(preset);

            preset.MasterEnabled = true;
            App.Settings?.Save();
            App.Logger?.Information("InstallPreset: {Id} installed ({Count} triggers)",
                presetId, preset.Triggers?.Count ?? 0);

            PresetsChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        /// <summary>
        /// Uninstalls a preset — removes every cloned trigger matching the preset's id prefix,
        /// strips its injected canned phrases, and clears MasterEnabled.
        /// </summary>
        public bool UninstallPreset(string presetId)
        {
            var settings = App.Settings?.Current;
            var preset = GetPreset(presetId);
            if (settings == null || preset == null) return false;

            var prefix = TriggerIdPrefix + presetId + ":";
            int removed = settings.KeywordTriggers.RemoveAll(t =>
                t.Id?.StartsWith(prefix, StringComparison.Ordinal) == true);

            RemoveCannedPhrases(preset);

            preset.MasterEnabled = false;
            App.Settings?.Save();
            App.Logger?.Information("UninstallPreset: {Id} uninstalled ({Count} triggers removed)",
                presetId, removed);

            PresetsChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        /// <summary>
        /// Clones a preset's triggers into the user's custom triggers list with fresh ids
        /// and no preset linkage — gives power users a starting point to tweak freely.
        /// </summary>
        public int CloneToCustom(string presetId)
        {
            var settings = App.Settings?.Current;
            var preset = GetPreset(presetId);
            if (settings == null || preset == null) return 0;

            int added = 0;
            foreach (var source in preset.Triggers)
            {
                if (source == null) continue;
                var clone = source.Clone();
                clone.Id = Guid.NewGuid().ToString("N")[..8];
                clone.LastTriggeredAt = DateTime.MinValue;
                if (clone.Actions == null || clone.Actions.Count == 0)
                    KeywordTriggerService.RebuildActionsFromFlatFields(clone);

                settings.KeywordTriggers.Add(clone);
                added++;
            }

            if (added > 0)
            {
                App.Settings?.Save();
                PresetsChanged?.Invoke(this, EventArgs.Empty);
            }
            return added;
        }

        /// <summary>
        /// Walks every installed preset's cloned triggers and enables/disables
        /// <see cref="AvatarCommentAction"/> instances based on current AI availability.
        /// Call this after AI state changes (login, logout, offline-mode toggle).
        /// </summary>
        public void RefreshAiGating()
        {
            var settings = App.Settings?.Current;
            if (settings == null) return;

            var aiAvailable = App.Ai?.IsAvailable == true;
            int touched = 0;

            foreach (var trigger in settings.KeywordTriggers)
            {
                if (trigger?.Id?.StartsWith(TriggerIdPrefix, StringComparison.Ordinal) != true) continue;
                if (trigger.Actions == null) continue;

                foreach (var action in trigger.Actions.OfType<AvatarCommentAction>())
                {
                    if (action.RequireAiAvailable)
                    {
                        var shouldEnable = aiAvailable || !string.IsNullOrEmpty(action.FallbackPhraseCategory);
                        if (action.Enabled != shouldEnable)
                        {
                            action.Enabled = shouldEnable;
                            touched++;
                        }
                    }
                }
            }

            if (touched > 0)
            {
                App.Settings?.Save();
                App.Logger?.Information("RefreshAiGating: adjusted {Count} avatar-comment actions (aiAvailable={Ai})",
                    touched, aiAvailable);
            }
        }

        // --- Canned phrase pool management ---

        private static string MakeCustomPhraseId(string presetId, string category, int index)
            => $"preset:{presetId}:phrase:{category}:{index}";

        private static void InstallCannedPhrases(KeywordTriggerPreset preset)
        {
            var settings = App.Settings?.Current;
            if (settings == null || preset.CannedPhrases == null || preset.CannedPhrases.Count == 0) return;

            // Defensive: strip any previously-injected phrases for this preset.
            RemoveCannedPhrases(preset);

            foreach (var kvp in preset.CannedPhrases)
            {
                var category = kvp.Key;
                var phrases = kvp.Value;
                if (string.IsNullOrEmpty(category) || phrases == null) continue;

                for (int i = 0; i < phrases.Count; i++)
                {
                    var text = phrases[i];
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    settings.CustomCompanionPhrases.Add(new CustomCompanionPhrase
                    {
                        Id = MakeCustomPhraseId(preset.Id, category, i),
                        Text = text,
                        Category = category,
                        Enabled = true
                    });
                }
            }
        }

        private static void RemoveCannedPhrases(KeywordTriggerPreset preset)
        {
            var settings = App.Settings?.Current;
            if (settings == null) return;

            var marker = $"preset:{preset.Id}:phrase:";
            settings.CustomCompanionPhrases.RemoveAll(p =>
                p.Id?.StartsWith(marker, StringComparison.Ordinal) == true);
        }
    }
}
