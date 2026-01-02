using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ConditioningControlPanel.Models
{
    /// <summary>
    /// Tracks where a session came from
    /// </summary>
    public enum SessionSource
    {
        BuiltIn,    // Shipped with the app in Assets/Sessions
        Custom,     // User-created and saved locally
        Imported    // Dropped in via drag & drop
    }

    /// <summary>
    /// Serializable session definition for .session.json files.
    /// This is the file format for exporting/importing sessions.
    /// </summary>
    public class SessionDefinition
    {
        // === Metadata ===
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Icon { get; set; } = "🎯";

        /// <summary>
        /// Short vibe description shown on card
        /// </summary>
        public string VibeSummary { get; set; } = "";

        /// <summary>
        /// Main description/flavor text
        /// </summary>
        public string Description { get; set; } = "";

        /// <summary>
        /// Image path for the session card
        /// </summary>
        public string ImagePath { get; set; } = "";

        // === Progression ===
        public int DurationMinutes { get; set; } = 30;
        public SessionDifficulty Difficulty { get; set; } = SessionDifficulty.Easy;
        public int BonusXP { get; set; } = 400;
        public bool IsAvailable { get; set; } = true;

        // === Special Options ===
        public bool HasCornerGifOption { get; set; } = false;
        public string CornerGifDescription { get; set; } = "";

        // === Settings ===
        public SessionSettings Settings { get; set; } = new();

        // === Timeline Phases ===
        public List<SessionPhase> Phases { get; set; } = new();

        // === Source Tracking (not serialized to file) ===
        [JsonIgnore]
        public SessionSource Source { get; set; } = SessionSource.BuiltIn;

        [JsonIgnore]
        public string SourceFilePath { get; set; } = "";

        /// <summary>
        /// Convert to runtime Session object
        /// </summary>
        public Session ToSession()
        {
            return new Session
            {
                Id = Id,
                Name = Name,
                Icon = Icon,
                Description = Description,
                DurationMinutes = DurationMinutes,
                Difficulty = Difficulty,
                BonusXP = BonusXP,
                IsAvailable = IsAvailable,
                HasCornerGifOption = HasCornerGifOption,
                CornerGifDescription = CornerGifDescription,
                Settings = Settings,
                Phases = Phases,
                Source = Source,
                SourceFilePath = SourceFilePath
            };
        }

        /// <summary>
        /// Create SessionDefinition from a Session object
        /// </summary>
        public static SessionDefinition FromSession(Session session)
        {
            return new SessionDefinition
            {
                Id = session.Id,
                Name = session.Name,
                Icon = session.Icon,
                Description = session.Description,
                DurationMinutes = session.DurationMinutes,
                Difficulty = session.Difficulty,
                BonusXP = session.BonusXP,
                IsAvailable = session.IsAvailable,
                HasCornerGifOption = session.HasCornerGifOption,
                CornerGifDescription = session.CornerGifDescription,
                Settings = session.Settings,
                Phases = session.Phases,
                Source = session.Source,
                SourceFilePath = session.SourceFilePath
            };
        }
    }
}
