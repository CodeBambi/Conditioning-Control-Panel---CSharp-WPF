using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Service for managing AI companion personality presets.
    /// Handles built-in presets, user presets, and personality switching.
    /// </summary>
    public class PersonalityService
    {
        /// <summary>
        /// Fired when the active personality changes.
        /// </summary>
        public event EventHandler<PersonalityPreset>? PersonalityChanged;

        /// <summary>
        /// The built-in preset set for the current context: the active mod's personalities
        /// (converted from its ModManifest) when it defines any, otherwise the stock presets.
        /// This is what makes a themed mod like Locked ship its own AI personalities instead
        /// of the default Bambi ones. When no mod personalities exist, behavior is unchanged.
        /// </summary>
        private static List<PersonalityPreset> GetBuiltInPresetsForActiveMod()
        {
            var modPresets = GetActiveModPersonalities();
            return (modPresets != null && modPresets.Count > 0)
                ? modPresets
                : PersonalityPresets.GetAllBuiltIn();
        }

        /// <summary>
        /// Converts the active mod's ModManifest.Personalities into PersonalityPresets, or
        /// returns null when the active mod defines none. The prompt text is carried in each
        /// personality's PromptSettings dictionary (key "Personality", plus any optional
        /// CompanionPromptSettings sections the mod chooses to set).
        /// </summary>
        private static List<PersonalityPreset>? GetActiveModPersonalities()
        {
            var manifest = App.Mods?.ActiveMod?.Manifest;
            var defs = manifest?.Personalities;
            if (defs == null || defs.Count == 0) return null;

            var result = new List<PersonalityPreset>();
            foreach (var d in defs)
            {
                if (string.IsNullOrWhiteSpace(d.Id) || string.IsNullOrWhiteSpace(d.Name)) continue;

                var cps = new CompanionPromptSettings { UseCustomPrompt = true };
                if (d.PromptSettings != null)
                {
                    if (d.PromptSettings.TryGetValue("Personality", out var p)) cps.Personality = p;
                    if (d.PromptSettings.TryGetValue("ExplicitReaction", out var er)) cps.ExplicitReaction = er;
                    if (d.PromptSettings.TryGetValue("SlutModePersonality", out var sm)) cps.SlutModePersonality = sm;
                    if (d.PromptSettings.TryGetValue("KnowledgeBase", out var kb)) cps.KnowledgeBase = kb;
                    if (d.PromptSettings.TryGetValue("ContextReactions", out var cr)) cps.ContextReactions = cr;
                    if (d.PromptSettings.TryGetValue("OutputRules", out var or)) cps.OutputRules = or;
                }

                result.Add(new PersonalityPreset
                {
                    Id = d.Id,
                    Name = d.Name,
                    Description = d.Description ?? "",
                    IsBuiltIn = true,
                    RequiresPremium = false,
                    PromptSettings = cps
                });
            }
            return result.Count > 0 ? result : null;
        }

        /// <summary>
        /// Gets all available presets (built-in/mod + user-created).
        /// </summary>
        public List<PersonalityPreset> GetAllPresets()
        {
            var presets = new List<PersonalityPreset>();

            // Built-in presets first (mod-supplied if the active mod defines personalities)
            presets.AddRange(GetBuiltInPresetsForActiveMod());

            // Add user-created presets
            var userPresets = App.Settings?.Current?.UserPersonalityPresets;
            if (userPresets != null)
            {
                presets.AddRange(userPresets);
            }

            return presets;
        }

        /// <summary>
        /// Gets the currently active personality preset.
        /// </summary>
        public PersonalityPreset GetActivePreset()
        {
            var activeId = App.Settings?.Current?.ActivePersonalityPresetId ?? PersonalityPresets.BambiSpriteId;

            // Try the active context's built-in set (mod personalities if the active mod
            // defines any, else stock presets).
            var builtInSet = GetBuiltInPresetsForActiveMod();
            var builtIn = builtInSet.FirstOrDefault(p => p.Id == activeId);
            if (builtIn != null) return builtIn;

            // Try to find in user presets
            var userPreset = App.Settings?.Current?.UserPersonalityPresets?
                .FirstOrDefault(p => p.Id == activeId);
            if (userPreset != null) return userPreset;

            // The stored preset id isn't valid for the active mod (e.g. the default
            // "bambisprite" while a themed mod is active). Fall back to the active set's
            // first entry — the mod's intended default personality — so the AI speaks in
            // the mod's voice instead of the stock Bambi prompt.
            if (builtInSet.Count > 0) return builtInSet[0];

            return PersonalityPresets.GetBambiSprite();
        }

        /// <summary>
        /// Switches to a preset by ID.
        /// </summary>
        /// <param name="presetId">The preset ID to switch to.</param>
        /// <returns>True if successful, false if preset not found or access denied.</returns>
        public bool SetActivePreset(string presetId)
        {
            // Check if preset exists
            var preset = GetPresetById(presetId);
            if (preset == null)
            {
                App.Logger?.Warning("PersonalityService: Preset not found: {Id}", presetId);
                return false;
            }

            // Update settings
            if (App.Settings?.Current != null)
            {
                App.Settings.Current.ActivePersonalityPresetId = presetId;

                // Picking a preset is the user saying "this one, now" — so it has to win over any
                // community/asset/hand-edited prompt still holding the single wire slot, otherwise
                // the chip row and the quick menu confirm a switch that never reached the model.
                var cleared = CommunityPromptService.ClearCustomPromptOverride(App.Settings.Current);

                // ...and it has to win over the chat HISTORY. Her own recent replies are the
                // strongest few-shot signal a small model has; with a restored ~100-turn session
                // in the previous voice, the persona paragraph changed but the voice never did
                // (owner repro, 2026-08-07). Fencing here (deliberately also on a re-select of
                // the SAME preset — clicking it again re-asserts "this voice, now") drops
                // assistant-authored turns older than this moment from the wire window only:
                // the persisted session and the visible bubbles keep the full history.
                App.Settings.Current.PersonaVoiceFenceUtc = DateTime.UtcNow;

                App.Settings.Save();

                if (cleared)
                    App.Logger?.Information("PersonalityService: Preset {Id} took over from the active custom prompt", presetId);
                App.Logger?.Information("PersonalityService: Switched to preset: {Name} ({Id})", preset.Name, presetId);
                PersonalityChanged?.Invoke(this, preset);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Gets a preset by ID from built-in or user presets.
        /// </summary>
        public PersonalityPreset? GetPresetById(string presetId)
        {
            // Check the active context's built-in set first (mod-supplied or stock)
            var builtIn = GetBuiltInPresetsForActiveMod().FirstOrDefault(p => p.Id == presetId);
            if (builtIn != null) return builtIn;

            // Check user presets
            return App.Settings?.Current?.UserPersonalityPresets?
                .FirstOrDefault(p => p.Id == presetId);
        }

        /// <summary>
        /// Creates a user copy of an existing preset.
        /// </summary>
        public PersonalityPreset CreateUserCopy(string sourcePresetId, string newName)
        {
            var source = GetPresetById(sourcePresetId);
            if (source == null)
            {
                source = PersonalityPresets.GetBambiSprite();
            }

            var copy = source.Clone();
            copy.Name = newName;
            copy.Description = $"Copy of {source.Name}";

            // Add to user presets
            App.Settings?.Current?.UserPersonalityPresets?.Add(copy);
            App.Settings?.Save();

            App.Logger?.Information("PersonalityService: Created user copy: {Name} from {Source}", newName, source.Name);
            return copy;
        }

        /// <summary>
        /// Saves changes to a user preset.
        /// </summary>
        public void SaveUserPreset(PersonalityPreset preset)
        {
            if (preset.IsBuiltIn)
            {
                App.Logger?.Warning("PersonalityService: Cannot save built-in preset");
                return;
            }

            preset.ModifiedAt = DateTime.Now;
            App.Settings?.Save();
            App.Logger?.Information("PersonalityService: Saved user preset: {Name}", preset.Name);
        }

        /// <summary>
        /// Deletes a user preset. Built-in presets cannot be deleted.
        /// </summary>
        public bool DeletePreset(string presetId)
        {
            if (IsBuiltIn(presetId))
            {
                App.Logger?.Warning("PersonalityService: Cannot delete built-in preset: {Id}", presetId);
                return false;
            }

            var presets = App.Settings?.Current?.UserPersonalityPresets;
            var preset = presets?.FirstOrDefault(p => p.Id == presetId);
            if (preset != null)
            {
                presets!.Remove(preset);

                // If this was the active preset, switch to default
                if (App.Settings?.Current?.ActivePersonalityPresetId == presetId)
                {
                    App.Settings.Current.ActivePersonalityPresetId = PersonalityPresets.BambiSpriteId;
                }

                App.Settings?.Save();
                App.Logger?.Information("PersonalityService: Deleted user preset: {Name}", preset.Name);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Checks if a preset ID is built-in (non-deletable).
        /// </summary>
        public bool IsBuiltIn(string presetId)
        {
            if (PersonalityPresets.BuiltInIds.Contains(presetId)) return true;
            // Mod-supplied personalities are also built-in (non-deletable, non-editable).
            var modPresets = GetActiveModPersonalities();
            return modPresets?.Any(p => p.Id == presetId) == true;
        }

        /// <summary>
        /// Migrates from legacy SlutModeEnabled setting to new preset system.
        /// Should be called once during app startup.
        /// </summary>
        public void MigrateFromLegacy(AppSettings? settings)
        {
            if (settings == null) return;

            // Only migrate if not already using new system (default is BambiSprite)
            // If they've already changed to something else, they're using new system
            if (settings.ActivePersonalityPresetId != PersonalityPresets.BambiSpriteId)
            {
                return; // Already migrated or using new system
            }

            // Check if SlutModeEnabled was true
            if (settings.SlutModeEnabled)
            {
                settings.ActivePersonalityPresetId = PersonalityPresets.SlutModeId;
                App.Logger?.Information("PersonalityService: Migrated SlutModeEnabled=true to Slut Mode preset");
            }

            // Check if custom prompts were active
            if (settings.CompanionPrompt?.UseCustomPrompt == true)
            {
                // Create a user preset from the custom settings
                var customPreset = new PersonalityPreset
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = "My Custom Personality",
                    Description = "Migrated from custom prompt settings",
                    IsBuiltIn = false,
                    RequiresPremium = false,
                    PromptSettings = settings.CompanionPrompt.Clone(),
                    CreatedAt = DateTime.Now,
                    ModifiedAt = DateTime.Now
                };

                settings.UserPersonalityPresets.Add(customPreset);
                settings.ActivePersonalityPresetId = customPreset.Id;

                // Clear the UseCustomPrompt flag
                settings.CompanionPrompt.UseCustomPrompt = false;

                App.Logger?.Information("PersonalityService: Migrated custom prompt to user preset: {Name}", customPreset.Name);
            }

            App.Settings?.Save();
        }
    }
}
