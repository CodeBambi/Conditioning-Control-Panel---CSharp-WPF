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