using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Serilog;

namespace ConditioningControlPanel.Localization
{
    /// <summary>
    /// Manages loading and switching of language files for UI localization.
    /// Language files are JSON dictionaries stored in Localization/Languages/.
    /// </summary>
    public class LocalizationManager : INotifyPropertyChanged
    {
        public static LocalizationManager Instance { get; } = new();

        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler? LanguageChanged;

        private Dictionary<string, string> _strings = new();
        private Dictionary<string, string> _fallbackStrings = new();
        private string _currentLanguage = "en";

        /// <summary>
        /// Available languages as (code, display name, short name) tuples.
        /// </summary>
        public static readonly (string Code, string DisplayName, string ShortName)[] AvailableLanguages =
        {
            ("en", "English", "EN"),
            ("zh-CN", "简体中文", "中文"),
            ("ja", "日本語", "JA"),
            ("ko", "한국어", "KO"),
            ("es", "Español", "ES"),
            ("pt-BR", "Português (BR)", "PT"),
            ("fr", "Français", "FR"),
            ("de", "Deutsch", "DE"),
            ("ru", "Русский", "RU"),
        };

        public string CurrentLanguage
        {
            get => _currentLanguage;
            private set
            {
                if (_currentLanguage != value)
                {
                    _currentLanguage = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguage)));
                }
            }
        }

        private LocalizationManager() { }

        /// <summary>
        /// Initialize with fallback (English) loaded first, then switch to the requested language.
        /// </summary>
        public void Initialize(string languageCode)
        {
            _fallbackStrings = LoadLanguageFile("en");
            SetLanguage(languageCode);
        }

        /// <summary>
        /// Switch to a different language. Falls back to English for missing keys.
        /// </summary>
        public void SetLanguage(string languageCode)
        {
            if (string.IsNullOrWhiteSpace(languageCode))
                languageCode = "en";

            if (languageCode == "en")
            {
                _strings = _fallbackStrings;
            }
            else
            {
                _strings = LoadLanguageFile(languageCode);
            }

            CurrentLanguage = languageCode;
            LanguageChanged?.Invoke(this, EventArgs.Empty);
            // Notify all bindings that use the indexer
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        }

        /// <summary>
        /// Get a localized string by key. Falls back to English, then to the key itself.
        /// </summary>
        public string this[string key] => Get(key);

        /// <summary>
        /// Get a localized string by key. Falls back to English, then to the key itself.
        /// </summary>
        public string Get(string key)
        {
            if (string.IsNullOrEmpty(key))
                return string.Empty;

            if (_strings.TryGetValue(key, out var value))
                return value;

            if (_fallbackStrings.TryGetValue(key, out var fallback))
                return fallback;

            // Return key as-is so untranslated strings are visible during development
            return key;
        }

        /// <summary>
        /// Get a localized string with format arguments.
        /// Usage: Loc.GetF("msg_level_up", level) where value is "You reached level {0}!"
        /// </summary>
        public string GetF(string key, params object[] args)
        {
            var template = Get(key);
            try
            {
                return string.Format(template, args);
            }
            catch (FormatException)
            {
                return template;
            }
        }

        private Dictionary<string, string> LoadLanguageFile(string languageCode)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);

            // Try loading from app directory first (for development / bundled)
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            var filePath = Path.Combine(appDir, "Localization", "Languages", $"{languageCode}.json");

            // Fallback: try user data directory (for user-added translations)
            if (!File.Exists(filePath))
            {
                var userDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ConditioningControlPanel", "Languages", $"{languageCode}.json");
                if (File.Exists(userDir))
                    filePath = userDir;
            }

            if (!File.Exists(filePath))
            {
                Log.Warning("Language file not found: {Path}", filePath);
                return result;
            }

            try
            {
                var json = File.ReadAllText(filePath);
                var parsed = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                if (parsed != null)
                    result = new Dictionary<string, string>(parsed, StringComparer.Ordinal);
                Log.Information("Loaded {Count} strings for language '{Lang}'", result.Count, languageCode);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load language file: {Path}", filePath);
            }

            return result;
        }
    }
}
