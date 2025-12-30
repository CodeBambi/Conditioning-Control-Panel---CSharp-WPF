using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Models
{
    /// <summary>
    /// Defines a timed conditioning session with specific settings
    /// </summary>
    public class Session
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Icon { get; set; } = "🎯";
        public int DurationMinutes { get; set; } = 30;
        public bool IsAvailable { get; set; } = false;
        
        // Spoiler-free description (shown by default)
        public string Description { get; set; } = "";
        
        // Detailed settings (hidden until revealed)
        public SessionSettings Settings { get; set; } = new();
        public List<SessionPhase> Phases { get; set; } = new();
        
        /// <summary>
        /// Gets the Morning Drift session - gentle passive conditioning
        /// </summary>
        public static Session MorningDrift => new()
        {
            Id = "morning_drift",
            Name = "Morning Drift",
            Icon = "🌅",
            DurationMinutes = 30,
            IsAvailable = true,
            Description = @"Let the morning carry you gently into that soft, floaty space...

This session is designed for your morning routine - while you work, browse, or prepare for the day. No interruptions, no demands. Just gentle whispers and soft reminders that help good girls drift into that comfortable, familiar headspace.

Perfect for multitasking. Perfect for letting go without even trying.

You don't need to do anything special. Just... let it happen. 💗",
            
            Settings = new SessionSettings
            {
                // Flash Images
                FlashEnabled = true,
                FlashPerHour = 12,
                FlashImages = 2,
                FlashOpacity = 30,
                FlashClickable = true,
                FlashAudioEnabled = false,
                
                // Subliminals
                SubliminalEnabled = true,
                SubliminalPerMin = 2,
                SubliminalFrames = 3,
                SubliminalOpacity = 45,
                
                // Audio Whispers
                AudioWhispersEnabled = true,
                WhisperVolume = 12,
                
                // Bouncing Text
                BouncingTextEnabled = true,
                BouncingTextSpeed = 2,
                BouncingTextPhrases = new List<string> { "Good Girl", "GOOD GIRL", "good girl 💗" },
                
                // Pink Filter (delayed start, gradual)
                PinkFilterEnabled = true,
                PinkFilterStartMinute = 10,
                PinkFilterStartOpacity = 0,
                PinkFilterEndOpacity = 15,
                
                // Bubbles (intermittent)
                BubblesEnabled = true,
                BubblesIntermittent = true,
                BubblesBurstDurationMin = 2,
                BubblesBurstDurationMax = 3,
                BubblesGapMin = 5,
                BubblesGapMax = 8,
                BubblesPerMin = 3,
                
                // Disabled features
                MandatoryVideosEnabled = false,
                SpiralEnabled = false,
                LockCardEnabled = false,
                BubbleCountEnabled = false,
                MiniGameEnabled = false
            },
            
            Phases = new List<SessionPhase>
            {
                new() { StartMinute = 0, Name = "Settling In", Description = "Gentle start with bouncing text and soft subliminals" },
                new() { StartMinute = 10, Name = "Pink Awakening", Description = "Pink filter begins its gradual embrace" },
                new() { StartMinute = 15, Name = "Drifting", Description = "Random bubble bursts may appear" },
                new() { StartMinute = 25, Name = "Deep Pink", Description = "Pink filter nearing full intensity" },
                new() { StartMinute = 30, Name = "Complete", Description = "Session ends with congratulations" }
            }
        };
        
        /// <summary>
        /// Gets placeholder sessions (coming soon)
        /// </summary>
        public static List<Session> GetAllSessions()
        {
            return new List<Session>
            {
                MorningDrift,
                new Session
                {
                    Id = "deep_dive",
                    Name = "Deep Dive",
                    Icon = "🌙",
                    DurationMinutes = 60,
                    IsAvailable = false,
                    Description = "A longer, more immersive experience for when you have time to truly let go..."
                },
                new Session
                {
                    Id = "bambi_time",
                    Name = "Bambi Time",
                    Icon = "💗",
                    DurationMinutes = 45,
                    IsAvailable = false,
                    Description = "Full Bambi mode. Everything turned up. Complete surrender."
                },
                new Session
                {
                    Id = "random_drop",
                    Name = "Random Drop",
                    Icon = "🎲",
                    DurationMinutes = 20,
                    IsAvailable = false,
                    Description = "You won't know what's coming. That's the point. Let go of control completely."
                },
                new Session
                {
                    Id = "edge_walk",
                    Name = "Edge Walk",
                    Icon = "📈",
                    DurationMinutes = 40,
                    IsAvailable = false,
                    Description = "Starts soft, ends intense. Can you handle the climb?"
                }
            };
        }
        
        /// <summary>
        /// Gets the spoiler details as formatted text
        /// </summary>
        public string GetSpoilerFlash()
        {
            if (!Settings.FlashEnabled) return "Disabled";
            return $"~{Settings.FlashPerHour}/hour, {Settings.FlashImages} images, {Settings.FlashOpacity}% opacity, " +
                   $"{(Settings.FlashClickable ? "clickable" : "locked")}, {(Settings.FlashAudioEnabled ? "with audio" : "silent")}";
        }
        
        public string GetSpoilerSubliminal()
        {
            if (!Settings.SubliminalEnabled) return "Disabled";
            return $"{Settings.SubliminalPerMin}/min, {Settings.SubliminalFrames} frames, {Settings.SubliminalOpacity}% opacity";
        }
        
        public string GetSpoilerAudio()
        {
            if (!Settings.AudioWhispersEnabled) return "Whispers disabled";
            return $"Whispers at {Settings.WhisperVolume}% (barely audible)";
        }
        
        public string GetSpoilerOverlays()
        {
            var parts = new List<string>();
            if (Settings.PinkFilterEnabled)
                parts.Add($"Pink filter: starts at {Settings.PinkFilterStartMinute}min, ramps {Settings.PinkFilterStartOpacity}%→{Settings.PinkFilterEndOpacity}%");
            if (Settings.SpiralEnabled)
                parts.Add("Spiral enabled");
            if (parts.Count == 0) return "None";
            return string.Join("\n", parts);
        }
        
        public string GetSpoilerExtras()
        {
            var parts = new List<string>();
            if (Settings.BouncingTextEnabled)
                parts.Add($"Bouncing text (slow): \"{string.Join("\", \"", Settings.BouncingTextPhrases)}\"");
            if (Settings.BubblesEnabled)
            {
                if (Settings.BubblesIntermittent)
                    parts.Add($"Bubbles: random {Settings.BubblesBurstDurationMin}-{Settings.BubblesBurstDurationMax}min bursts, {Settings.BubblesGapMin}-{Settings.BubblesGapMax}min gaps");
                else
                    parts.Add($"Bubbles: {Settings.BubblesPerMin}/min constant");
            }
            if (parts.Count == 0) return "None";
            return string.Join("\n", parts);
        }
        
        public string GetSpoilerTimeline()
        {
            var lines = new List<string>();
            foreach (var phase in Phases)
            {
                lines.Add($"{phase.StartMinute:D2}:00 - {phase.Name}");
            }
            return string.Join("\n", lines);
        }
    }
    
    /// <summary>
    /// Settings for a session
    /// </summary>
    public class SessionSettings
    {
        // Flash Images
        public bool FlashEnabled { get; set; }
        public int FlashPerHour { get; set; } = 10;
        public int FlashImages { get; set; } = 2;
        public int FlashOpacity { get; set; } = 100;
        public bool FlashClickable { get; set; } = true;
        public bool FlashAudioEnabled { get; set; } = true;
        
        // Subliminals
        public bool SubliminalEnabled { get; set; }
        public int SubliminalPerMin { get; set; } = 5;
        public int SubliminalFrames { get; set; } = 2;
        public int SubliminalOpacity { get; set; } = 80;
        
        // Audio
        public bool AudioWhispersEnabled { get; set; }
        public int WhisperVolume { get; set; } = 50;
        
        // Bouncing Text
        public bool BouncingTextEnabled { get; set; }
        public int BouncingTextSpeed { get; set; } = 5;
        public List<string> BouncingTextPhrases { get; set; } = new();
        
        // Pink Filter
        public bool PinkFilterEnabled { get; set; }
        public int PinkFilterStartMinute { get; set; } = 0;
        public int PinkFilterStartOpacity { get; set; } = 10;
        public int PinkFilterEndOpacity { get; set; } = 10;
        
        // Spiral
        public bool SpiralEnabled { get; set; }
        public int SpiralOpacity { get; set; } = 15;
        
        // Bubbles
        public bool BubblesEnabled { get; set; }
        public bool BubblesIntermittent { get; set; }
        public int BubblesBurstDurationMin { get; set; } = 2;
        public int BubblesBurstDurationMax { get; set; } = 3;
        public int BubblesGapMin { get; set; } = 5;
        public int BubblesGapMax { get; set; } = 8;
        public int BubblesPerMin { get; set; } = 5;
        
        // Disabled features
        public bool MandatoryVideosEnabled { get; set; }
        public bool LockCardEnabled { get; set; }
        public bool BubbleCountEnabled { get; set; }
        public bool MiniGameEnabled { get; set; }
    }
    
    /// <summary>
    /// A phase within a session timeline
    /// </summary>
    public class SessionPhase
    {
        public int StartMinute { get; set; }
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
    }
}
