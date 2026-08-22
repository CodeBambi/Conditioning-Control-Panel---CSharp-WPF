using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Models
{
    /// <summary>
    /// Represents a community-created AI personality prompt that can be shared and downloaded.
    /// </summary>
    public class CommunityPrompt : INotifyPropertyChanged
    {
        private string _id = "";
        private string _name = "";
        private string _author = "";
        private string _description = "";
        private string _version = "1.0";
        private DateTime _createdAt = DateTime.UtcNow;
        private DateTime _updatedAt = DateTime.UtcNow;
        private int _downloads = 0;
        private int _likes = 0;
        private bool _isInstalled;
        private bool _isActive;
        private string[]? _tags;

        /// <summary>
        /// Unique identifier for this prompt (guid or slug).
        /// </summary>
        public string Id
        {
            get => _id;
            set { _id = value ?? ""; OnPropertyChanged(); }
        }

        /// <summary>
        /// Display name of the prompt.
        /// </summary>
        public string Name
        {
            get => _name;
            set { _name = value ?? ""; OnPropertyChanged(); }
        }

        /// <summary>
        /// Creator's display name or username.
        /// </summary>
        public string Author
        {
            get => _author;
            set { _author = value ?? ""; OnPropertyChanged(); }
        }

        /// <summary>
        /// Short description of what this prompt personality is like.
        /// </summary>
        public string Description
        {
            get => _description;
            set { _description = value ?? ""; OnPropertyChanged(); }
        }

        /// <summary>
        /// Version string for this prompt (allows updates).
        /// </summary>
        public string Version
        {
            get => _version;
            set { _version = value ?? "1.0"; OnPropertyChanged(); }
        }

        /// <summary>
        /// When this prompt was first created/uploaded.
        /// </summary>
        public DateTime CreatedAt
        {
            get => _createdAt;
            set { _createdAt = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// When this prompt was last updated.
        /// </summary>
        public DateTime UpdatedAt
        {
            get => _updatedAt;
            set { _updatedAt = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Number of times this prompt has been downloaded.
        /// </summary>
        public int Downloads
        {
            get => _downloads;
            set { _downloads = Math.Max(0, value); OnPropertyChanged(); }
        }

        /// <summary>
        /// Number of likes/upvotes for this prompt.
        /// </summary>
        public int Likes
        {
            get => _likes;
            set { _likes = Math.Max(0, value); OnPropertyChanged(); }
        }

        /// <summary>
        /// Tags for categorization (e.g., "strict", "playful", "dominant").
        /// </summary>
        public string[]? Tags
        {
            get => _tags;
            set { _tags = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// The actual prompt settings (personality, reactions, etc.).
        /// </summary>
        public CompanionPromptSettings PromptSettings { get; set; } = new();

        /// <summary>
        /// Whether this prompt is installed locally (for UI binding).
        /// Not serialized - set at runtime.
        /// </summary>
        [JsonIgnore]
        public bool IsInstalled
        {
            get => _isInstalled;
            set { _isInstalled = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Whether this prompt is currently active (for UI binding).
        /// Not serialized - set at runtime.
        /// </summary>
        [JsonIgnore]
        public bool IsActive
        {
            get => _isActive;
            set { _isActive = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Creates a new prompt from the current companion settings.
        /// </summary>
        public static CommunityPrompt FromCurrentSettings(string name, string author, string description)
        {
            var currentSettings = App.Settings?.Current?.CompanionPrompt ?? CompanionPromptSettings.GetDefaults();

            return new CommunityPrompt
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = name,
                Author = author,
                Description = description,
                Version = "1.0",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                PromptSettings = new CompanionPromptSettings
                {
                    UseCustomPrompt = true,
                    Personality = currentSettings.Personality,
                    ExplicitReaction = currentSettings.ExplicitReaction,
                    SlutModePersonality = currentSettings.SlutModePersonality,
                    KnowledgeBase = currentSettings.KnowledgeBase,
                    ContextReactions = currentSettings.ContextReactions,
                    OutputRules = currentSettings.OutputRules,
                    CustomDomains = new(currentSettings.CustomDomains)
                }
            };
        }

        /// <summary>
        /// Creates a manifest entry (without full prompt settings) for listing.
        /// </summary>
        public CommunityPromptManifestEntry ToManifestEntry()
        {
            return new CommunityPromptManifestEntry
            {
                Id = Id,
                Name = Name,
                Author = Author,
                Description = Description,
                Version = Version,
                CreatedAt = CreatedAt,
                Downloads = Downloads,
                Likes = Likes,
                Tags = Tags
            };
        }

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }

    /// <summary>
    /// Lightweight manifest entry for listing prompts without full settings.
    /// Used for the browse/discovery UI before downloading full prompt.
    /// </summary>
    public class CommunityPromptManifestEntry
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Author { get; set; } = "";
        public string Description { get; set; } = "";
        public string Version { get; set; } = "1.0";
        public DateTime CreatedAt { get; set; }
        public int Downloads { get; set; }
        public int Likes { get; set; }
        public string[]? Tags { get; set; }
    }

    /// <summary>
    /// Server response for prompts manifest endpoint.
    /// </summary>
    public class CommunityPromptsManifest
    {
        public CommunityPromptManifestEntry[] Prompts { get; set; } = Array.Empty<CommunityPromptManifestEntry>();
        public DateTime LastUpdated { get; set; }
    }
}
