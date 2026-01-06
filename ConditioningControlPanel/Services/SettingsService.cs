using System;
using System.IO;
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
            _settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
            Current = Load();
        }

        private AppSettings Load()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    var json = File.ReadAllText(_settingsPath);
                    var settings = JsonConvert.DeserializeObject<AppSettings>(json);
                    if (settings != null)
                    {
                        App.Logger?.Information("Settings loaded from {Path}", _settingsPath);
                        ValidateLevelLockedFeatures(settings);
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
        /// Validates that level-locked features are disabled if user doesn't meet the level requirement.
        /// This fixes issues where old sessions or manual JSON edits enable features the user can't access.
        /// </summary>
        private void ValidateLevelLockedFeatures(AppSettings settings)
        {
            var level = settings.PlayerLevel;
            var anyFixed = false;

            // Brain Drain requires Level 70
            if (settings.BrainDrainEnabled && level < 70)
            {
                settings.BrainDrainEnabled = false;
                App.Logger?.Warning("Settings: Disabled BrainDrain (requires Level 70, user is {Level})", level);
                anyFixed = true;
            }

            // Bouncing Text requires Level 60
            if (settings.BouncingTextEnabled && level < 60)
            {
                settings.BouncingTextEnabled = false;
                App.Logger?.Warning("Settings: Disabled BouncingText (requires Level 60, user is {Level})", level);
                anyFixed = true;
            }

            // Bubble Count requires Level 50
            if (settings.BubbleCountEnabled && level < 50)
            {
                settings.BubbleCountEnabled = false;
                App.Logger?.Warning("Settings: Disabled BubbleCount (requires Level 50, user is {Level})", level);
                anyFixed = true;
            }

            // Lock Card requires Level 35
            if (settings.LockCardEnabled && level < 35)
            {
                settings.LockCardEnabled = false;
                App.Logger?.Warning("Settings: Disabled LockCard (requires Level 35, user is {Level})", level);
                anyFixed = true;
            }

            // Bubbles require Level 20
            if (settings.BubblesEnabled && level < 20)
            {
                settings.BubblesEnabled = false;
                App.Logger?.Warning("Settings: Disabled Bubbles (requires Level 20, user is {Level})", level);
                anyFixed = true;
            }

            if (anyFixed)
            {
                App.Logger?.Information("Settings: Fixed level-locked features that were incorrectly enabled");
            }
        }

        public void Save()
        {
            try
            {
                var json = JsonConvert.SerializeObject(Current, Formatting.Indented);
                File.WriteAllText(_settingsPath, json);
                App.Logger?.Information("Settings saved to {Path}", _settingsPath);
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