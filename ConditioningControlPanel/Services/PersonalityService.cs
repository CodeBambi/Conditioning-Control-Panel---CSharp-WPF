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
        /// Gets all available presets (built-in + user-created).
        /// </summary>
        public List<PersonalityPreset> GetAllPresets()
        {
            var presets = new List<PersonalityPreset>();

            // Add built-in presets first
            presets.AddRange(PersonalityPresets.GetAllBuiltIn());

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

            // Try to find in built-in presets
            var builtIn = PersonalityPresets.GetBuiltInById(activeId);
            if (builtIn != null) return builtIn;

            // Try to find in user presets
            var userPreset = App.Settings?.Current?.UserPersonalityPresets?
                .FirstOrDefault(p => p.Id == activeId);
            if (userPreset != null) return userPreset;

            // Fallback to BambiSprite
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

            // Check premium requirement
            if (preset.RequiresPremium && App.Patreon?.HasPremiumAccess != true)
            {
                App.Logger?.Warning("PersonalityService: Premium required for preset: {Id}", presetId);
                return false;
            }

            // Update settings
            if (App.Settings?.Current != null)
            {
                App.Settings.Current.ActivePersonalityPresetId = presetId;
                App.Settings.Save();

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
            // Check built-in first
            var builtIn = PersonalityPresets.GetBuiltInById(presetId);
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
            return PersonalityPresets.BuiltInIds.Contains(presetId);
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
