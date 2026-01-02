using System.Collections.Generic;

namespace ConditioningControlPanel.Models
{
    /// <summary>
    /// Category grouping for features in the editor
    /// </summary>
    public enum FeatureCategory
    {
        Audio,
        Video,
        Overlays,
        Interactive,
        Extras
    }

    /// <summary>
    /// Type of setting control
    /// </summary>
    public enum SettingType
    {
        Slider,
        Toggle,
        Dropdown,
        TextList,
        FilePicker
    }

    /// <summary>
    /// Definition of a single setting within a feature
    /// </summary>
    public class FeatureSettingDefinition
    {
        public string Name { get; set; } = "";
        public string Key { get; set; } = "";
        public SettingType Type { get; set; }
        public double Min { get; set; }
        public double Max { get; set; }
        public object? Default { get; set; }
        public string[]? Options { get; set; }      // For dropdowns
        public bool SupportsRamp { get; set; }      // Can this setting ramp between start/end?
    }

    /// <summary>
    /// Definition of a feature available in the session editor
    /// </summary>
    public class FeatureDefinition
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Icon { get; set; } = "";              // Emoji icon
        public FeatureCategory Category { get; set; }
        public bool SupportsRamping { get; set; }           // Can values ramp over time?
        public int XPBonus { get; set; }                    // XP contribution for calculation
        public int DifficultyWeight { get; set; }           // Weight for difficulty calculation
        public List<FeatureSettingDefinition> Settings { get; set; } = new();

        /// <summary>
        /// Get all available features for the session editor
        /// </summary>
        public static List<FeatureDefinition> GetAllFeatures() => new()
        {
            // ═══════════════════════════════════════════════════════════════
            // AUDIO
            // ═══════════════════════════════════════════════════════════════
            new()
            {
                Id = "audio_whispers",
                Name = "Audio Whispers",
                Icon = "🔊",
                Category = FeatureCategory.Audio,
                SupportsRamping = false,
                XPBonus = 20,
                DifficultyWeight = 0,
                Settings = new()
                {
                    new() { Name = "Volume", Key = "volume", Type = SettingType.Slider, Min = 0, Max = 100, Default = 50 },
                    new() { Name = "Duck Level", Key = "duckLevel", Type = SettingType.Slider, Min = 0, Max = 100, Default = 50 }
                }
            },
            new()
            {
                Id = "mind_wipe",
                Name = "Mind Wipe",
                Icon = "🧠",
                Category = FeatureCategory.Audio,
                SupportsRamping = false,
                XPBonus = 50,
                DifficultyWeight = 1,
                Settings = new()
                {
                    new() { Name = "Multiplier", Key = "multiplier", Type = SettingType.Slider, Min = 1, Max = 5, Default = 1 },
                    new() { Name = "Volume", Key = "volume", Type = SettingType.Slider, Min = 0, Max = 100, Default = 50 },
                    new() { Name = "Loop in Background", Key = "loopBackground", Type = SettingType.Toggle, Default = false }
                }
            },

            // ═══════════════════════════════════════════════════════════════
            // VIDEO
            // ═══════════════════════════════════════════════════════════════
            new()
            {
                Id = "flash",
                Name = "Flash Images",
                Icon = "⚡",
                Category = FeatureCategory.Video,
                SupportsRamping = true,
                XPBonus = 50,
                DifficultyWeight = 1,
                Settings = new()
                {
                    new() { Name = "Per Hour", Key = "perHour", Type = SettingType.Slider, Min = 1, Max = 600, Default = 30 },
                    new() { Name = "Opacity %", Key = "opacity", Type = SettingType.Slider, Min = 10, Max = 100, Default = 50, SupportsRamp = true },
                    new() { Name = "Images Count", Key = "imagesCount", Type = SettingType.Slider, Min = 1, Max = 5, Default = 2 },
                    new() { Name = "Scale %", Key = "scale", Type = SettingType.Slider, Min = 50, Max = 200, Default = 100 },
                    new() { Name = "Clickable", Key = "clickable", Type = SettingType.Toggle, Default = true },
                    new() { Name = "Audio Enabled", Key = "audioEnabled", Type = SettingType.Toggle, Default = false }
                }
            },
            new()
            {
                Id = "mandatory_videos",
                Name = "Mandatory Videos",
                Icon = "🎬",
                Category = FeatureCategory.Video,
                SupportsRamping = false,
                XPBonus = 100,
                DifficultyWeight = 2,
                Settings = new()
                {
                    new() { Name = "Per Hour", Key = "perHour", Type = SettingType.Slider, Min = 1, Max = 10, Default = 2 }
                }
            },
            new()
            {
                Id = "subliminal",
                Name = "Subliminal Text",
                Icon = "💭",
                Category = FeatureCategory.Video,
                SupportsRamping = false,
                XPBonus = 30,
                DifficultyWeight = 0,
                Settings = new()
                {
                    new() { Name = "Per Minute", Key = "perMin", Type = SettingType.Slider, Min = 1, Max = 20, Default = 5 },
                    new() { Name = "Frames", Key = "frames", Type = SettingType.Slider, Min = 1, Max = 10, Default = 2 },
                    new() { Name = "Opacity %", Key = "opacity", Type = SettingType.Slider, Min = 10, Max = 100, Default = 70 }
                }
            },
            new()
            {
                Id = "bouncing_text",
                Name = "Bouncing Text",
                Icon = "📝",
                Category = FeatureCategory.Video,
                SupportsRamping = false,
                XPBonus = 20,
                DifficultyWeight = 0,
                Settings = new()
                {
                    new() { Name = "Speed", Key = "speed", Type = SettingType.Slider, Min = 1, Max = 10, Default = 5 },
                    new() { Name = "Size %", Key = "size", Type = SettingType.Slider, Min = 50, Max = 200, Default = 100 },
                    new() { Name = "Opacity %", Key = "opacity", Type = SettingType.Slider, Min = 10, Max = 100, Default = 80 }
                }
            },

            // ═══════════════════════════════════════════════════════════════
            // OVERLAYS
            // ═══════════════════════════════════════════════════════════════
            new()
            {
                Id = "pink_filter",
                Name = "Pink Filter",
                Icon = "💗",
                Category = FeatureCategory.Overlays,
                SupportsRamping = true,
                XPBonus = 40,
                DifficultyWeight = 0,
                Settings = new()
                {
                    new() { Name = "Opacity %", Key = "opacity", Type = SettingType.Slider, Min = 5, Max = 80, Default = 20, SupportsRamp = true }
                }
            },
            new()
            {
                Id = "spiral",
                Name = "Spiral Overlay",
                Icon = "🌀",
                Category = FeatureCategory.Overlays,
                SupportsRamping = true,
                XPBonus = 50,
                DifficultyWeight = 1,
                Settings = new()
                {
                    new() { Name = "Opacity %", Key = "opacity", Type = SettingType.Slider, Min = 5, Max = 50, Default = 15, SupportsRamp = true }
                }
            },
            new()
            {
                Id = "brain_drain",
                Name = "Brain Drain",
                Icon = "😵",
                Category = FeatureCategory.Overlays,
                SupportsRamping = true,
                XPBonus = 80,
                DifficultyWeight = 2,
                Settings = new()
                {
                    new() { Name = "Intensity %", Key = "intensity", Type = SettingType.Slider, Min = 1, Max = 20, Default = 5, SupportsRamp = true }
                }
            },

            // ═══════════════════════════════════════════════════════════════
            // INTERACTIVE
            // ═══════════════════════════════════════════════════════════════
            new()
            {
                Id = "bubbles",
                Name = "Bubbles",
                Icon = "🫧",
                Category = FeatureCategory.Interactive,
                SupportsRamping = false,
                XPBonus = 30,
                DifficultyWeight = 0,
                Settings = new()
                {
                    new() { Name = "Mode", Key = "mode", Type = SettingType.Dropdown, Options = new[] { "Continuous", "Intermittent" }, Default = "Continuous" },
                    new() { Name = "Clickable", Key = "clickable", Type = SettingType.Toggle, Default = true },
                    new() { Name = "Frequency", Key = "frequency", Type = SettingType.Slider, Min = 1, Max = 20, Default = 5 },
                    new() { Name = "Burst Count", Key = "burstCount", Type = SettingType.Slider, Min = 1, Max = 10, Default = 5 },
                    new() { Name = "Per Burst", Key = "perBurst", Type = SettingType.Slider, Min = 1, Max = 5, Default = 3 }
                }
            },
            new()
            {
                Id = "lock_cards",
                Name = "Lock Cards",
                Icon = "🔒",
                Category = FeatureCategory.Interactive,
                SupportsRamping = false,
                XPBonus = 60,
                DifficultyWeight = 1,
                Settings = new()
                {
                    new() { Name = "Per Hour", Key = "perHour", Type = SettingType.Slider, Min = 1, Max = 10, Default = 2 }
                }
            },
            new()
            {
                Id = "bubble_count",
                Name = "Bubble Count Game",
                Icon = "🔢",
                Category = FeatureCategory.Interactive,
                SupportsRamping = false,
                XPBonus = 40,
                DifficultyWeight = 0,
                Settings = new()
                {
                    new() { Name = "Per Hour", Key = "perHour", Type = SettingType.Slider, Min = 1, Max = 10, Default = 2 }
                }
            },

            // ═══════════════════════════════════════════════════════════════
            // EXTRAS
            // ═══════════════════════════════════════════════════════════════
            new()
            {
                Id = "corner_gif",
                Name = "Corner GIF",
                Icon = "🖼️",
                Category = FeatureCategory.Extras,
                SupportsRamping = false,
                XPBonus = 10,
                DifficultyWeight = 0,
                Settings = new()
                {
                    new() { Name = "File", Key = "filePath", Type = SettingType.FilePicker, Default = "" },
                    new() { Name = "Opacity %", Key = "opacity", Type = SettingType.Slider, Min = 5, Max = 100, Default = 20 },
                    new() { Name = "Position", Key = "position", Type = SettingType.Dropdown, Options = new[] { "Top Left", "Top Right", "Bottom Left", "Bottom Right" }, Default = "Bottom Left" },
                    new() { Name = "Size (px)", Key = "size", Type = SettingType.Slider, Min = 100, Max = 500, Default = 300 }
                }
            }
        };

        /// <summary>
        /// Get a feature by ID
        /// </summary>
        public static FeatureDefinition? GetById(string id)
        {
            return GetAllFeatures().Find(f => f.Id == id);
        }

        /// <summary>
        /// Get all features in a category
        /// </summary>
        public static List<FeatureDefinition> GetByCategory(FeatureCategory category)
        {
            return GetAllFeatures().FindAll(f => f.Category == category);
        }
    }
}
