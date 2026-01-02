using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Models
{
    /// <summary>
    /// Type of timeline event
    /// </summary>
    public enum TimelineEventType
    {
        Start,  // Feature begins (green icon)
        Stop    // Feature ends (red icon)
    }

    /// <summary>
    /// Represents a single event on the timeline (start or stop of a feature)
    /// </summary>
    public class TimelineEvent
    {
        /// <summary>
        /// Unique identifier for this event
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Feature ID (e.g., "flash", "spiral", "bubbles")
        /// </summary>
        public string FeatureId { get; set; } = "";

        /// <summary>
        /// Position on timeline in minutes (0 to duration)
        /// </summary>
        public int Minute { get; set; }

        /// <summary>
        /// Whether this is a start or stop event
        /// </summary>
        public TimelineEventType EventType { get; set; } = TimelineEventType.Start;

        /// <summary>
        /// Links start event to its corresponding stop event (and vice versa)
        /// </summary>
        public string? PairedEventId { get; set; }

        /// <summary>
        /// Feature-specific settings for this event
        /// </summary>
        public Dictionary<string, object> Settings { get; set; } = new();

        /// <summary>
        /// Start value for ramping features (e.g., opacity at start)
        /// </summary>
        public int? StartValue { get; set; }

        /// <summary>
        /// End value for ramping features (e.g., opacity at end)
        /// </summary>
        public int? EndValue { get; set; }

        /// <summary>
        /// Get a setting value with default fallback
        /// </summary>
        public T GetSetting<T>(string key, T defaultValue)
        {
            if (Settings.TryGetValue(key, out var value))
            {
                try
                {
                    if (value is T typedValue)
                        return typedValue;
                    return (T)Convert.ChangeType(value, typeof(T));
                }
                catch
                {
                    return defaultValue;
                }
            }
            return defaultValue;
        }

        /// <summary>
        /// Set a setting value
        /// </summary>
        public void SetSetting(string key, object value)
        {
            Settings[key] = value;
        }

        /// <summary>
        /// Create a copy of this event
        /// </summary>
        public TimelineEvent Clone()
        {
            return new TimelineEvent
            {
                Id = Guid.NewGuid().ToString(),
                FeatureId = FeatureId,
                Minute = Minute,
                EventType = EventType,
                PairedEventId = null,
                Settings = new Dictionary<string, object>(Settings),
                StartValue = StartValue,
                EndValue = EndValue
            };
        }
    }
}
