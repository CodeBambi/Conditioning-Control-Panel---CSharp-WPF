using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Models
{
    /// <summary>
    /// A bundle of pre-configured <see cref="KeywordTrigger"/>s shipped as a
    /// one-click "preset pack" for the Awareness Engine (e.g. Puppy Pet,
    /// Chastity Watcher, Bimbo Reinforcement, Trance Induction).
    ///
    /// Built-in presets are loaded from <c>Resources/AwarenessPresets/*.json</c>
    /// on app launch and merged into the user's settings. User-installed presets
    /// store their triggers cloned into <see cref="AppSettings.KeywordTriggers"/>
    /// with an id prefix of <c>preset:&lt;presetId&gt;:&lt;origId&gt;</c> so an
    /// uninstall can find and remove them deterministically.
    /// </summary>
    public class KeywordTriggerPreset : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        /// <summary>Stable preset id (e.g. "builtin.puppy"). Forms the id prefix for cloned triggers.</summary>
        [JsonProperty("id")]
        public string Id { get; set; } = "";

        private string _name = "";
        [JsonProperty("name")]
        public string Name
        {
            get => _name;
            set { _name = value ?? ""; OnPropertyChanged(); }
        }

        /// <summary>Emoji or short string shown on the preset card.</summary>
        [JsonProperty("icon")]
        public string Icon { get; set; } = "";

        [JsonProperty("description")]
        public string Description { get; set; } = "";

        [JsonProperty("longDescription")]
        public string LongDescription { get; set; } = "";

        [JsonProperty("author")]
        public string Author { get; set; } = "Built-in";

        /// <summary>Schema version — a newer built-in version overwrites its Triggers in place on load.</summary>
        [JsonProperty("version")]
        public int Version { get; set; } = 1;

        /// <summary>True for the four shipped built-in presets. User-created presets set this false.</summary>
        [JsonProperty("isBuiltIn")]
        public bool IsBuiltIn { get; set; } = true;

        /// <summary>Metadata for the AI badge on the card. Dispatch still respects per-action RequireAiAvailable.</summary>
        [JsonProperty("requiresAi")]
        public bool RequiresAi { get; set; }

        /// <summary>
        /// Default AI prompt template used by AvatarCommentActions in this preset
        /// when the individual action doesn't supply its own.
        /// </summary>
        [JsonProperty("avatarPromptTemplate")]
        public string? AvatarPromptTemplate { get; set; }

        /// <summary>Phrase pool category names this preset ships canned fallback phrases under.</summary>
        [JsonProperty("phrasePools", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<string> PhrasePools { get; set; } = new();

        /// <summary>Canned phrases to inject into the companion phrase pool on install, keyed by category.</summary>
        [JsonProperty("cannedPhrases", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public Dictionary<string, List<string>> CannedPhrases { get; set; } = new();

        /// <summary>The actual trigger bundle. Cloned into AppSettings.KeywordTriggers on install.</summary>
        [JsonProperty("triggers", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<KeywordTrigger> Triggers { get; set; } = new();

        // --- Runtime / persisted user state ---

        private bool _masterEnabled;
        /// <summary>True if the user has this preset currently installed.</summary>
        [JsonProperty("masterEnabled")]
        public bool MasterEnabled
        {
            get => _masterEnabled;
            set { _masterEnabled = value; OnPropertyChanged(); }
        }
    }
}
