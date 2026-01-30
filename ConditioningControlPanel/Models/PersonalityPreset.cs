using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Models
{
    /// <summary>
    /// Represents an AI companion personality preset.
    /// Can be a built-in preset (non-deletable) or a user-created preset.
    /// </summary>
    public class PersonalityPreset : INotifyPropertyChanged
    {
        private string _id = "";
        private string _name = "";
        private string _description = "";
        private bool _isBuiltIn;
        private CompanionPromptSettings? _promptSettings;
        private DateTime _createdAt = DateTime.Now;
        private DateTime _modifiedAt = DateTime.Now;

        /// <summary>
        /// Unique identifier for this preset (e.g., "bambisprite", "slutmode", or GUID for user presets).
        /// </summary>
        [JsonProperty("id")]
        public string Id
        {
            get => _id;
            set { _id = value ?? ""; OnPropertyChanged(); }
        }

        /// <summary>
        /// Display name shown in the UI (e.g., "BambiSprite", "Slut Mode").
        /// </summary>
        [JsonProperty("name")]
        public string Name
        {
            get => _name;
            set { _name = value ?? ""; OnPropertyChanged(); }
        }

        /// <summary>
        /// Short description of the personality (e.g., "Bubbly, cheeky bad influence bestie").
        /// </summary>
        [JsonProperty("description")]
        public string Description
        {
            get => _description;
            set { _description = value ?? ""; OnPropertyChanged(); }
        }

        /// <summary>
        /// True for the 6 built-in presets that cannot be deleted.
        /// False for user-created presets.
        /// </summary>
        [JsonProperty("isBuiltIn")]
        public bool IsBuiltIn
        {
            get => _isBuiltIn;
            set { _isBuiltIn = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Whether this preset requires Patreon premium access.
        /// </summary>
        [JsonProperty("requiresPremium")]
        public bool RequiresPremium { get; set; }

        /// <summary>
        /// The prompt settings for this personality.
        /// </summary>
        [JsonProperty("promptSettings")]
        public CompanionPromptSettings? PromptSettings
        {
            get => _promptSettings;
            set { _promptSettings = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// When this preset was created.
        /// </summary>
        [JsonProperty("createdAt")]
        public DateTime CreatedAt
        {
            get => _createdAt;
            set { _createdAt = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// When this preset was last modified.
        /// </summary>
        [JsonProperty("modifiedAt")]
        public DateTime ModifiedAt
        {
            get => _modifiedAt;
            set { _modifiedAt = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Creates a deep copy of this preset with a new ID.
        /// </summary>
        public PersonalityPreset Clone(string? newId = null)
        {
            return new PersonalityPreset
            {
                Id = newId ?? Guid.NewGuid().ToString("N"),
                Name = Name,
                Description = Description,
                IsBuiltIn = false, // Clones are never built-in
                RequiresPremium = false, // User copies don't require premium
                PromptSettings = PromptSettings?.Clone(),
                CreatedAt = DateTime.Now,
                ModifiedAt = DateTime.Now
            };
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
