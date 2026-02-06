using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Services
{
    public class SettingsService
    {
        private readonly string _settingsPath;
        
        public AppSettings Current { get; private set; }

        public SettingsService()
        {
            // Store settings in user data folder (persists across updates)
            _settingsPath = Path.Combine(App.UserDataPath, "settings.json");

            // Migrate settings from old location if needed
            MigrateSettingsFromOldLocation();

            Current = Load();
        }

        /// <summary>
        /// Migrate settings from old install directory location to persistent user data folder.
        /// </summary>
        private void MigrateSettingsFromOldLocation()
        {
            try
            {
                var oldSettingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

                // If new location already has settings, don't overwrite
                if (File.Exists(_settingsPath)) return;

                // If old location has settings, copy them
                if (File.Exists(oldSettingsPath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
                    File.Copy(oldSettingsPath, _settingsPath);
                    App.Logger?.Information("Migrated settings from {Old} to {New}", oldSettingsPath, _settingsPath);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Failed to migrate settings: {Error}", ex.Message);
            }
        }

        private AppSettings Load()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    var json = File.ReadAllText(_settingsPath);

                    // Use explicit settings to ensure lists are REPLACED, not merged with defaults
                    var serializerSettings = new JsonSerializerSettings
                    {
                        ObjectCreationHandling = ObjectCreationHandling.Replace
                    };

                    var settings = JsonConvert.DeserializeObject<AppSettings>(json, serializerSettings);
                    if (settings != null)
                    {
                        App.Logger?.Information("Settings loaded from {Path} (Triggers: {TriggerCount})",
                            _settingsPath, settings.CustomTriggers?.Count ?? 0);
                        // NOTE: Don't validate level-locked features here - cloud sync hasn't happened yet.
                        // The cloud level may be higher than the local level, and we don't want to
                        // incorrectly disable features. Validation happens after cloud sync completes.

                        // Merge any new default subliminal triggers that were added in updates
                        MergeNewDefaultSubliminalTriggers(settings);

                        return settings;
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Could not load settings: {Error}", ex.Message);
            }

            App.Logger?.Information("Using default settings");
            return new AppSettings();
        }

        /// <summary>
        /// Merges any new default subliminal triggers that were added in updates.
        /// New triggers are added as disabled so users can opt-in.
        /// </summary>
        private void MergeNewDefaultSubliminalTriggers(AppSettings settings)
        {
            try
            {
                var defaults = ContentModeConfig.GetDefaultSubliminalPool(settings.ContentMode);
                var added = new List<string>();

                foreach (var trigger in defaults.Keys)
                {
                    if (!settings.SubliminalPool.ContainsKey(trigger))
                    {
                        // Add new triggers as enabled by default (matching default behavior)
                        settings.SubliminalPool[trigger] = true;
                        added.Add(trigger);
                    }
                }

                if (added.Count > 0)
                {
                    App.Logger?.Information("Added {Count} new default subliminal triggers: {Triggers}",
                        added.Count, string.Join(", ", added));
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Failed to merge new subliminal triggers: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Validates that level-locked features are disabled if user doesn't meet the level requirement.
        /// This fixes issues where old sessions or manual JSON edits enable features the user can't access.
        /// </summary>
        private void ValidateLevelLockedFeatures(AppSettings settings)
        {
            var anyFixed = false;

            // Brain Drain requires Level 70
            if (settings.BrainDrainEnabled && !settings.IsLevelUnlocked(70))
            {
                settings.BrainDrainEnabled = false;
                App.Logger?.Warning("Settings: Disabled BrainDrain (requires Level 70, user is {Level})", settings.PlayerLevel);
                anyFixed = true;
            }

            // Bouncing Text requires Level 60
            if (settings.BouncingTextEnabled && !settings.IsLevelUnlocked(60))
            {
                settings.BouncingTextEnabled = false;
                App.Logger?.Warning("Settings: Disabled BouncingText (requires Level 60, user is {Level})", settings.PlayerLevel);
                anyFixed = true;
            }

            // Bubble Count requires Level 50
            if (settings.BubbleCountEnabled && !settings.IsLevelUnlocked(50))
            {
                settings.BubbleCountEnabled = false;
                App.Logger?.Warning("Settings: Disabled BubbleCount (requires Level 50, user is {Level})", settings.PlayerLevel);
                anyFixed = true;
            }

            // Lock Card requires Level 35
            if (settings.LockCardEnabled && !settings.IsLevelUnlocked(35))
            {
                settings.LockCardEnabled = false;
                App.Logger?.Warning("Settings: Disabled LockCard (requires Level 35, user is {Level})", settings.PlayerLevel);
                anyFixed = true;
            }

            // Bubbles require Level 20
            if (settings.BubblesEnabled && !settings.IsLevelUnlocked(20))
            {
                settings.BubblesEnabled = false;
                App.Logger?.Warning("Settings: Disabled Bubbles (requires Level 20, user is {Level})", settings.PlayerLevel);
                anyFixed = true;
            }

            // Autonomy Mode is Patreon-only (no level requirement)
            // Patreon check is done at runtime, not here

            if (anyFixed)
            {
                App.Logger?.Information("Settings: Fixed level-locked features that were incorrectly enabled");
            }
        }

        public void Save()
        {
            try
            {
                App.Logger?.Information("Settings.Save: ActivePackIds BEFORE serialize: [{Ids}]",
                    string.Join(", ", Current.ActivePackIds ?? new List<string>()));

                var json = JsonConvert.SerializeObject(Current, Formatting.Indented);
                File.WriteAllText(_settingsPath, json);
                App.Logger?.Information("Settings saved to {Path} (Triggers: {TriggerCount}, ActivePacks: {PackCount})",
                    _settingsPath, Current.CustomTriggers?.Count ?? 0, Current.ActivePackIds?.Count ?? 0);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Could not save settings");
            }
        }

        public void Reset()
        {
            Current = new AppSettings();
            Save();
        }
    }
}