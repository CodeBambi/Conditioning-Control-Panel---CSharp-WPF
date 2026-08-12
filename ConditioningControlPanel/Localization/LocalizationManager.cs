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
            EnsureFallbackLoaded();
            SetLanguage(languageCode);
        }

        /// <summary>
        /// Load English into the fallback slot if it is not there yet.
        /// </summary>
        private void EnsureFallbackLoaded()
        {
            if (_fallbackStrings.Count > 0)
                return;

            _fallbackStrings = LoadLanguageFile("en");

            if (_fallbackStrings.Count == 0)
            {
                // This is the one failure the app cannot paper over: with no English to
                // fall back to, every Get() returns the raw key, so the UI reads
                // "btn_start_flashes" instead of "Start Flashes". Ugly but diagnosable -
                // and loud in the log, because the old behaviour was a single Error line
                // that nobody would connect to a UI full of identifier text.
                Log.Fatal("English language file failed to load - all UI strings will render as raw keys");
            }
        }

        /// <summary>
        /// Switch to a different language. Falls back to English for missing keys.
        /// </summary>
        public void SetLanguage(string languageCode)
        {
            if (string.IsNullOrWhiteSpace(languageCode))
                languageCode = "en";

            EnsureFallbackLoaded();

            if (languageCode == "en")
            {
                _strings = _fallbackStrings;
            }
            else
            {
                var loaded = LoadLanguageFile(languageCode);
                if (loaded.Count == 0)
                {
                    // A missing or corrupt translation must not leave the UI stringless.
                    // Get() already falls back per key, but pointing _strings straight at
                    // English makes a dead language behave exactly like English instead of
                    // relying on that second lookup, and keeps the logged count honest.
                    Log.Warning("Language '{Lang}' loaded no strings - falling back to English", languageCode);
                    loaded = _fallbackStrings;
                }
                _strings = loaded;
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
        ///
        /// Descent vocabulary tokens (<c>{petname}</c>, <c>{collective}</c>) are substituted on the
        /// way out - see <see cref="VocabTokens"/>. That happens AFTER the English fallback on
        /// purpose: a key that only exists in en.json must still come back with the active mod's
        /// words in it, not with raw tokens. No shipped string contains a token yet, so today this
        /// costs one character scan per lookup.
        /// </summary>
        public string Get(string key)
        {
            if (string.IsNullOrEmpty(key))
                return string.Empty;

            if (_strings.TryGetValue(key, out var value))
                return VocabTokens.Apply(value);

            if (_fallbackStrings.TryGetValue(key, out var fallback))
                return VocabTokens.Apply(fallback);

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
                var userDir = Path.Combine(App.UserDataPath, "Languages", $"{languageCode}.json");
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

            // An empty dictionary is this method's "could not load" signal - SetLanguage
            // keys the English fallback off Count == 0, so do not return partial results.
            return result;
        }
    }
}
