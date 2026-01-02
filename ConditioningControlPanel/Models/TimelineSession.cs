using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Models
{
    /// <summary>
    /// Represents a session being edited in the timeline editor
    /// </summary>
    public class TimelineSession
    {
        /// <summary>
        /// Unique identifier for the session
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Display name of the session
        /// </summary>
        public string Name { get; set; } = "New Session";

        /// <summary>
        /// Emoji icon for the session
        /// </summary>
        public string Icon { get; set; } = "✨";

        /// <summary>
        /// Vibe description / flavor text
        /// </summary>
        public string Description { get; set; } = "";

        /// <summary>
        /// Total duration in minutes
        /// </summary>
        public int DurationMinutes { get; set; } = 30;

        /// <summary>
        /// All timeline events (start/stop for features)
        /// </summary>
        public List<TimelineEvent> Events { get; set; } = new();

        /// <summary>
        /// Session-specific subliminal phrases (empty = use global pool)
        /// </summary>
        public List<string> SubliminalPhrases { get; set; } = new();

        /// <summary>
        /// Session-specific bouncing text phrases (empty = use global pool)
        /// </summary>
        public List<string> BouncingTextPhrases { get; set; } = new();

        /// <summary>
        /// Check if a feature is active in this session
        /// </summary>
        public bool HasFeature(string featureId)
        {
            return Events.Any(e => e.FeatureId == featureId && e.EventType == TimelineEventType.Start);
        }

        /// <summary>
        /// Get all start events for a feature
        /// </summary>
        public List<TimelineEvent> GetStartEvents(string featureId)
        {
            return Events.Where(e => e.FeatureId == featureId && e.EventType == TimelineEventType.Start).ToList();
        }

        /// <summary>
        /// Get the paired stop event for a start event
        /// </summary>
        public TimelineEvent? GetPairedStopEvent(TimelineEvent startEvent)
        {
            if (startEvent.EventType != TimelineEventType.Start || string.IsNullOrEmpty(startEvent.PairedEventId))
                return null;
            return Events.FirstOrDefault(e => e.Id == startEvent.PairedEventId);
        }

        /// <summary>
        /// Get maximum opacity/intensity setting for a feature
        /// </summary>
        public int GetMaxValue(string featureId, string settingKey)
        {
            var startEvents = GetStartEvents(featureId);
            if (!startEvents.Any()) return 0;

            int maxValue = 0;
            foreach (var evt in startEvents)
            {
                var value = evt.GetSetting<int>(settingKey, 0);
                if (value > maxValue) maxValue = value;

                // Also check end value for ramping
                if (evt.EndValue.HasValue && evt.EndValue.Value > maxValue)
                    maxValue = evt.EndValue.Value;
            }
            return maxValue;
        }

        /// <summary>
        /// Calculate XP reward for this session, rounded to nearest 50
        /// </summary>
        public int CalculateXP()
        {
            // Base XP: 10 per minute
            int baseXP = DurationMinutes * 10;

            // Feature bonus based on each start event
            int featureBonus = 0;
            foreach (var evt in Events.Where(e => e.EventType == TimelineEventType.Start))
            {
                var definition = FeatureDefinition.GetById(evt.FeatureId);
                if (definition != null)
                {
                    featureBonus += definition.XPBonus;
                }
            }

            int totalXP = baseXP + featureBonus;

            // Round to nearest 50
            return (int)(Math.Round(totalXP / 50.0) * 50);
        }

        /// <summary>
        /// Calculate difficulty based on feature intensity and count
        /// </summary>
        public SessionDifficulty CalculateDifficulty()
        {
            int score = 0;

            // Duration factor: +1 per 15 minutes
            score += DurationMinutes / 15;

            // Count distinct active features
            var activeFeatures = Events
                .Where(e => e.EventType == TimelineEventType.Start)
                .Select(e => e.FeatureId)
                .Distinct()
                .Count();
            score += activeFeatures;

            // Heavy features add more weight
            foreach (var evt in Events.Where(e => e.EventType == TimelineEventType.Start))
            {
                var definition = FeatureDefinition.GetById(evt.FeatureId);
                if (definition != null)
                {
                    score += definition.DifficultyWeight;
                }
            }

            // High intensity settings add more difficulty
            if (HasFeature("spiral") && GetMaxValue("spiral", "opacity") > 20) score += 1;
            if (HasFeature("flash") && GetMaxValue("flash", "opacity") > 60) score += 1;
            if (HasFeature("brain_drain") && GetMaxValue("brain_drain", "intensity") > 10) score += 1;

            return score switch
            {
                <= 4 => SessionDifficulty.Easy,
                <= 8 => SessionDifficulty.Medium,
                <= 12 => SessionDifficulty.Hard,
                _ => SessionDifficulty.Extreme
            };
        }

        /// <summary>
        /// Get difficulty display text with stars/skull
        /// </summary>
        public string GetDifficultyText()
        {
            return CalculateDifficulty() switch
            {
                SessionDifficulty.Easy => "⭐ Easy",
                SessionDifficulty.Medium => "⭐⭐ Medium",
                SessionDifficulty.Hard => "⭐⭐⭐ Hard",
                SessionDifficulty.Extreme => "💀 Extreme",
                _ => "⭐ Easy"
            };
        }

        /// <summary>
        /// Get difficulty color for UI
        /// </summary>
        public string GetDifficultyColor()
        {
            return CalculateDifficulty() switch
            {
                SessionDifficulty.Easy => "#90EE90",     // Light green
                SessionDifficulty.Medium => "#FFD700",   // Gold
                SessionDifficulty.Hard => "#FFA500",     // Orange
                SessionDifficulty.Extreme => "#FF6347",  // Tomato red
                _ => "#90EE90"
            };
        }

        /// <summary>
        /// Add a start event to the timeline
        /// </summary>
        public TimelineEvent AddStartEvent(string featureId, int minute, Dictionary<string, object>? settings = null)
        {
            var evt = new TimelineEvent
            {
                FeatureId = featureId,
                Minute = minute,
                EventType = TimelineEventType.Start,
                Settings = settings ?? new Dictionary<string, object>()
            };

            // Apply default settings from feature definition
            var definition = FeatureDefinition.GetById(featureId);
            if (definition != null)
            {
                foreach (var settingDef in definition.Settings)
                {
                    if (!evt.Settings.ContainsKey(settingDef.Key) && settingDef.Default != null)
                    {
                        evt.Settings[settingDef.Key] = settingDef.Default;
                    }
                }
            }

            Events.Add(evt);
            return evt;
        }

        /// <summary>
        /// Add a stop event paired to a start event
        /// </summary>
        public TimelineEvent AddStopEvent(TimelineEvent startEvent, int minute)
        {
            var evt = new TimelineEvent
            {
                FeatureId = startEvent.FeatureId,
                Minute = minute,
                EventType = TimelineEventType.Stop,
                PairedEventId = startEvent.Id
            };

            startEvent.PairedEventId = evt.Id;
            Events.Add(evt);
            return evt;
        }

        /// <summary>
        /// Remove an event and its paired event
        /// </summary>
        public void RemoveEvent(TimelineEvent evt)
        {
            if (!string.IsNullOrEmpty(evt.PairedEventId))
            {
                var paired = Events.FirstOrDefault(e => e.Id == evt.PairedEventId);
                if (paired != null)
                {
                    Events.Remove(paired);
                }
            }

            // Also remove any events that reference this one
            var referencing = Events.Where(e => e.PairedEventId == evt.Id).ToList();
            foreach (var r in referencing)
            {
                r.PairedEventId = null;
            }

            Events.Remove(evt);
        }

        /// <summary>
        /// Convert to SessionSettings for session playback
        /// </summary>
        public SessionSettings ToSessionSettings()
        {
            var settings = new SessionSettings();

            // Process each feature type
            ProcessFlashSettings(settings);
            ProcessSubliminalSettings(settings);
            ProcessAudioWhispersSettings(settings);
            ProcessBouncingTextSettings(settings);
            ProcessPinkFilterSettings(settings);
            ProcessSpiralSettings(settings);
            ProcessBrainDrainSettings(settings);
            ProcessBubblesSettings(settings);
            ProcessMandatoryVideosSettings(settings);
            ProcessLockCardsSettings(settings);
            ProcessBubbleCountSettings(settings);
            ProcessMindWipeSettings(settings);
            ProcessCornerGifSettings(settings);

            return settings;
        }

        private void ProcessFlashSettings(SessionSettings settings)
        {
            var startEvents = GetStartEvents("flash");
            if (!startEvents.Any())
            {
                settings.FlashEnabled = false;
                return;
            }

            settings.FlashEnabled = true;
            var evt = startEvents.First();
            settings.FlashPerHour = evt.GetSetting<int>("perHour", 30);
            settings.FlashImages = evt.GetSetting<int>("imagesCount", 2);
            settings.FlashOpacity = evt.StartValue ?? evt.GetSetting<int>("opacity", 50);
            settings.FlashOpacityEnd = evt.EndValue ?? settings.FlashOpacity;
            settings.FlashScale = evt.GetSetting<int>("scale", 100);
            settings.FlashClickable = evt.GetSetting<bool>("clickable", true);
            settings.FlashAudioEnabled = evt.GetSetting<bool>("audioEnabled", false);
        }

        private void ProcessSubliminalSettings(SessionSettings settings)
        {
            var startEvents = GetStartEvents("subliminal");
            if (!startEvents.Any())
            {
                settings.SubliminalEnabled = false;
                return;
            }

            settings.SubliminalEnabled = true;
            var evt = startEvents.First();
            settings.SubliminalPerMin = evt.GetSetting<int>("perMin", 5);
            settings.SubliminalFrames = evt.GetSetting<int>("frames", 2);
            settings.SubliminalOpacity = evt.GetSetting<int>("opacity", 70);
            settings.SubliminalPhrases = new List<string>(SubliminalPhrases);
        }

        private void ProcessAudioWhispersSettings(SessionSettings settings)
        {
            var startEvents = GetStartEvents("audio_whispers");
            if (!startEvents.Any())
            {
                settings.AudioWhispersEnabled = false;
                return;
            }

            settings.AudioWhispersEnabled = true;
            var evt = startEvents.First();
            settings.WhisperVolume = evt.GetSetting<int>("volume", 50);
            settings.AudioDuckLevel = evt.GetSetting<int>("duckLevel", 50);
        }

        private void ProcessBouncingTextSettings(SessionSettings settings)
        {
            var startEvents = GetStartEvents("bouncing_text");
            if (!startEvents.Any())
            {
                settings.BouncingTextEnabled = false;
                return;
            }

            settings.BouncingTextEnabled = true;
            var evt = startEvents.First();
            settings.BouncingTextSpeed = evt.GetSetting<int>("speed", 5);
            settings.BouncingTextSize = evt.GetSetting<int>("size", 100);
            settings.BouncingTextOpacity = evt.GetSetting<int>("opacity", 80);
            settings.BouncingTextPhrases = new List<string>(BouncingTextPhrases);
        }

        private void ProcessPinkFilterSettings(SessionSettings settings)
        {
            var startEvents = GetStartEvents("pink_filter");
            if (!startEvents.Any())
            {
                settings.PinkFilterEnabled = false;
                return;
            }

            settings.PinkFilterEnabled = true;
            var evt = startEvents.First();
            settings.PinkFilterStartMinute = evt.Minute;
            settings.PinkFilterStartOpacity = evt.StartValue ?? evt.GetSetting<int>("opacity", 20);
            settings.PinkFilterEndOpacity = evt.EndValue ?? settings.PinkFilterStartOpacity;
        }

        private void ProcessSpiralSettings(SessionSettings settings)
        {
            var startEvents = GetStartEvents("spiral");
            if (!startEvents.Any())
            {
                settings.SpiralEnabled = false;
                return;
            }

            settings.SpiralEnabled = true;
            var evt = startEvents.First();
            settings.SpiralStartMinute = evt.Minute;
            settings.SpiralOpacity = evt.StartValue ?? evt.GetSetting<int>("opacity", 15);
            settings.SpiralOpacityEnd = evt.EndValue ?? settings.SpiralOpacity;
        }

        private void ProcessBrainDrainSettings(SessionSettings settings)
        {
            var startEvents = GetStartEvents("brain_drain");
            if (!startEvents.Any())
            {
                settings.BrainDrainEnabled = false;
                return;
            }

            settings.BrainDrainEnabled = true;
            var evt = startEvents.First();
            settings.BrainDrainStartMinute = evt.Minute;
            settings.BrainDrainStartIntensity = evt.StartValue ?? evt.GetSetting<int>("intensity", 5);
            settings.BrainDrainEndIntensity = evt.EndValue ?? settings.BrainDrainStartIntensity;
        }

        private void ProcessBubblesSettings(SessionSettings settings)
        {
            var startEvents = GetStartEvents("bubbles");
            if (!startEvents.Any())
            {
                settings.BubblesEnabled = false;
                return;
            }

            settings.BubblesEnabled = true;
            var evt = startEvents.First();
            var mode = evt.GetSetting<string>("mode", "Continuous");
            settings.BubblesIntermittent = mode == "Intermittent";
            settings.BubblesClickable = evt.GetSetting<bool>("clickable", true);
            settings.BubblesStartMinute = evt.Minute;
            settings.BubblesFrequency = evt.GetSetting<int>("frequency", 5);
            settings.BubblesBurstCount = evt.GetSetting<int>("burstCount", 5);
            settings.BubblesPerBurst = evt.GetSetting<int>("perBurst", 3);
        }

        private void ProcessMandatoryVideosSettings(SessionSettings settings)
        {
            var startEvents = GetStartEvents("mandatory_videos");
            if (!startEvents.Any())
            {
                settings.MandatoryVideosEnabled = false;
                return;
            }

            settings.MandatoryVideosEnabled = true;
            var evt = startEvents.First();
            settings.VideosPerHour = evt.GetSetting<int>("perHour", 2);
        }

        private void ProcessLockCardsSettings(SessionSettings settings)
        {
            var startEvents = GetStartEvents("lock_cards");
            if (!startEvents.Any())
            {
                settings.LockCardEnabled = false;
                return;
            }

            settings.LockCardEnabled = true;
            var evt = startEvents.First();
            settings.LockCardFrequency = evt.GetSetting<int>("perHour", 2);
        }

        private void ProcessBubbleCountSettings(SessionSettings settings)
        {
            var startEvents = GetStartEvents("bubble_count");
            if (!startEvents.Any())
            {
                settings.BubbleCountEnabled = false;
                return;
            }

            settings.BubbleCountEnabled = true;
            var evt = startEvents.First();
            settings.BubbleCountFrequency = evt.GetSetting<int>("perHour", 2);
        }

        private void ProcessMindWipeSettings(SessionSettings settings)
        {
            var startEvents = GetStartEvents("mind_wipe");
            if (!startEvents.Any())
            {
                settings.MindWipeEnabled = false;
                return;
            }

            settings.MindWipeEnabled = true;
            var evt = startEvents.First();
            settings.MindWipeBaseMultiplier = evt.GetSetting<int>("multiplier", 1);
            settings.MindWipeVolume = evt.GetSetting<int>("volume", 50);
        }

        private void ProcessCornerGifSettings(SessionSettings settings)
        {
            var startEvents = GetStartEvents("corner_gif");
            if (!startEvents.Any())
            {
                settings.CornerGifEnabled = false;
                return;
            }

            settings.CornerGifEnabled = true;
            var evt = startEvents.First();
            settings.CornerGifPath = evt.GetSetting<string>("filePath", "");
            settings.CornerGifOpacity = evt.GetSetting<int>("opacity", 20);
            settings.CornerGifSize = evt.GetSetting<int>("size", 300);

            var position = evt.GetSetting<string>("position", "Bottom Left");
            settings.CornerGifPosition = position switch
            {
                "Top Left" => CornerPosition.TopLeft,
                "Top Right" => CornerPosition.TopRight,
                "Bottom Right" => CornerPosition.BottomRight,
                _ => CornerPosition.BottomLeft
            };
        }

        /// <summary>
        /// Create a TimelineSession from existing Session data
        /// </summary>
        public static TimelineSession FromSession(Session session)
        {
            var timeline = new TimelineSession
            {
                Id = session.Id,
                Name = session.Name,
                Icon = session.Icon,
                Description = session.Description,
                DurationMinutes = session.DurationMinutes,
                SubliminalPhrases = new List<string>(session.Settings.SubliminalPhrases),
                BouncingTextPhrases = new List<string>(session.Settings.BouncingTextPhrases)
            };

            var settings = session.Settings;

            // Flash Images
            if (settings.FlashEnabled)
            {
                var evt = timeline.AddStartEvent("flash", 0);
                evt.SetSetting("perHour", settings.FlashPerHour);
                evt.SetSetting("imagesCount", settings.FlashImages);
                evt.SetSetting("opacity", settings.FlashOpacity);
                evt.SetSetting("scale", settings.FlashScale);
                evt.SetSetting("clickable", settings.FlashClickable);
                evt.SetSetting("audioEnabled", settings.FlashAudioEnabled);
                if (settings.FlashOpacity != settings.FlashOpacityEnd)
                {
                    evt.StartValue = settings.FlashOpacity;
                    evt.EndValue = settings.FlashOpacityEnd;
                }
                timeline.AddStopEvent(evt, session.DurationMinutes);
            }

            // Subliminals
            if (settings.SubliminalEnabled)
            {
                var evt = timeline.AddStartEvent("subliminal", 0);
                evt.SetSetting("perMin", settings.SubliminalPerMin);
                evt.SetSetting("frames", settings.SubliminalFrames);
                evt.SetSetting("opacity", settings.SubliminalOpacity);
                timeline.AddStopEvent(evt, session.DurationMinutes);
            }

            // Audio Whispers
            if (settings.AudioWhispersEnabled)
            {
                var evt = timeline.AddStartEvent("audio_whispers", 0);
                evt.SetSetting("volume", settings.WhisperVolume);
                evt.SetSetting("duckLevel", settings.AudioDuckLevel);
                timeline.AddStopEvent(evt, session.DurationMinutes);
            }

            // Bouncing Text
            if (settings.BouncingTextEnabled)
            {
                var evt = timeline.AddStartEvent("bouncing_text", 0);
                evt.SetSetting("speed", settings.BouncingTextSpeed);
                evt.SetSetting("size", settings.BouncingTextSize);
                evt.SetSetting("opacity", settings.BouncingTextOpacity);
                timeline.AddStopEvent(evt, session.DurationMinutes);
            }

            // Pink Filter
            if (settings.PinkFilterEnabled)
            {
                var evt = timeline.AddStartEvent("pink_filter", settings.PinkFilterStartMinute);
                evt.SetSetting("opacity", settings.PinkFilterStartOpacity);
                if (settings.PinkFilterStartOpacity != settings.PinkFilterEndOpacity)
                {
                    evt.StartValue = settings.PinkFilterStartOpacity;
                    evt.EndValue = settings.PinkFilterEndOpacity;
                }
                timeline.AddStopEvent(evt, session.DurationMinutes);
            }

            // Spiral
            if (settings.SpiralEnabled)
            {
                var evt = timeline.AddStartEvent("spiral", settings.SpiralStartMinute);
                evt.SetSetting("opacity", settings.SpiralOpacity);
                if (settings.SpiralOpacity != settings.SpiralOpacityEnd)
                {
                    evt.StartValue = settings.SpiralOpacity;
                    evt.EndValue = settings.SpiralOpacityEnd;
                }
                timeline.AddStopEvent(evt, session.DurationMinutes);
            }

            // Brain Drain
            if (settings.BrainDrainEnabled)
            {
                var evt = timeline.AddStartEvent("brain_drain", settings.BrainDrainStartMinute);
                evt.SetSetting("intensity", settings.BrainDrainStartIntensity);
                if (settings.BrainDrainStartIntensity != settings.BrainDrainEndIntensity)
                {
                    evt.StartValue = settings.BrainDrainStartIntensity;
                    evt.EndValue = settings.BrainDrainEndIntensity;
                }
                timeline.AddStopEvent(evt, session.DurationMinutes);
            }

            // Bubbles
            if (settings.BubblesEnabled)
            {
                var evt = timeline.AddStartEvent("bubbles", settings.BubblesStartMinute);
                evt.SetSetting("mode", settings.BubblesIntermittent ? "Intermittent" : "Continuous");
                evt.SetSetting("clickable", settings.BubblesClickable);
                evt.SetSetting("frequency", settings.BubblesFrequency);
                evt.SetSetting("burstCount", settings.BubblesBurstCount);
                evt.SetSetting("perBurst", settings.BubblesPerBurst);
                timeline.AddStopEvent(evt, session.DurationMinutes);
            }

            // Mandatory Videos
            if (settings.MandatoryVideosEnabled)
            {
                var evt = timeline.AddStartEvent("mandatory_videos", 0);
                evt.SetSetting("perHour", settings.VideosPerHour ?? 2);
                timeline.AddStopEvent(evt, session.DurationMinutes);
            }

            // Lock Cards
            if (settings.LockCardEnabled)
            {
                var evt = timeline.AddStartEvent("lock_cards", 0);
                evt.SetSetting("perHour", settings.LockCardFrequency ?? 2);
                timeline.AddStopEvent(evt, session.DurationMinutes);
            }

            // Bubble Count
            if (settings.BubbleCountEnabled)
            {
                var evt = timeline.AddStartEvent("bubble_count", 0);
                evt.SetSetting("perHour", settings.BubbleCountFrequency ?? 2);
                timeline.AddStopEvent(evt, session.DurationMinutes);
            }

            // Mind Wipe
            if (settings.MindWipeEnabled)
            {
                var evt = timeline.AddStartEvent("mind_wipe", 0);
                evt.SetSetting("multiplier", settings.MindWipeBaseMultiplier);
                evt.SetSetting("volume", settings.MindWipeVolume);
                timeline.AddStopEvent(evt, session.DurationMinutes);
            }

            // Corner GIF
            if (settings.CornerGifEnabled)
            {
                var evt = timeline.AddStartEvent("corner_gif", 0);
                evt.SetSetting("filePath", settings.CornerGifPath);
                evt.SetSetting("opacity", settings.CornerGifOpacity);
                evt.SetSetting("size", settings.CornerGifSize);
                evt.SetSetting("position", settings.CornerGifPosition switch
                {
                    CornerPosition.TopLeft => "Top Left",
                    CornerPosition.TopRight => "Top Right",
                    CornerPosition.BottomRight => "Bottom Right",
                    _ => "Bottom Left"
                });
                timeline.AddStopEvent(evt, session.DurationMinutes);
            }

            return timeline;
        }

        /// <summary>
        /// Convert to a full Session object
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
                IsAvailable = true,
                Difficulty = CalculateDifficulty(),
                BonusXP = CalculateXP(),
                HasCornerGifOption = HasFeature("corner_gif"),
                Settings = ToSessionSettings()
            };
        }
    }
}
