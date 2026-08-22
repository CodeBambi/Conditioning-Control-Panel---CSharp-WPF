using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Models
{
    public enum MediaType
    {
        Video,
        Image
    }

    public class MediaLogEntry
    {
        [JsonProperty("timestamp")]
        public DateTime Timestamp { get; set; }

        [JsonProperty("session_time_seconds")]
        public double SessionTimeSeconds { get; set; }

        [JsonProperty("type")]
        public MediaType Type { get; set; }

        [JsonProperty("file_path")]
        public string FilePath { get; set; } = "";

        [JsonProperty("display_name")]
        public string DisplayName { get; set; } = "";

        [JsonIgnore]
        public TimeSpan SessionTime
        {
            get => TimeSpan.FromSeconds(SessionTimeSeconds);
            set => SessionTimeSeconds = value.TotalSeconds;
        }
    }

    public class SessionLog
    {
        [JsonProperty("session_id")]
        public string SessionId { get; set; } = "";

        [JsonProperty("session_name")]
        public string SessionName { get; set; } = "";

        [JsonProperty("session_icon")]
        public string SessionIcon { get; set; } = "";

        [JsonProperty("session_difficulty")]
        public SessionDifficulty SessionDifficulty { get; set; } = SessionDifficulty.Easy;

        [JsonProperty("started_at")]
        public DateTime StartedAt { get; set; }

        [JsonProperty("ended_at")]
        public DateTime EndedAt { get; set; }

        [JsonProperty("duration_seconds")]
        public double DurationSeconds { get; set; }

        [JsonProperty("completed")]
        public bool Completed { get; set; }

        [JsonProperty("xp_earned")]
        public int XPEarned { get; set; }

        [JsonProperty("media")]
        public List<MediaLogEntry> Media { get; set; } = new();

        [JsonIgnore]
        public TimeSpan Duration
        {
            get => TimeSpan.FromSeconds(DurationSeconds);
            set => DurationSeconds = value.TotalSeconds;
        }
    }
}
