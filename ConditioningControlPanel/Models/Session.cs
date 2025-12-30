using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Models
{
    public enum SessionDifficulty
    {
        Easy,
        Medium,
        Hard,
        Extreme
    }
    
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
        public SessionDifficulty Difficulty { get; set; } = SessionDifficulty.Easy;
        public int BonusXP { get; set; } = 50;
        
        // Spoiler-free description (shown by default)
        public string Description { get; set; } = "";
        
        // Special options
        public bool HasCornerGifOption { get; set; } = false;
        public string CornerGifDescription { get; set; } = "";
        
        // Detailed settings (hidden until revealed)
        public SessionSettings Settings { get; set; } = new();
        public List<SessionPhase> Phases { get; set; } = new();
        
        /// <summary>
        /// Gets XP bonus based on difficulty
        /// </summary>
        public static int GetDifficultyXP(SessionDifficulty difficulty)
        {
            return difficulty switch
            {
                SessionDifficulty.Easy => 50,
                SessionDifficulty.Medium => 100,
                SessionDifficulty.Hard => 200,
                SessionDifficulty.Extreme => 500,
                _ => 50
            };
        }
        
        /// <summary>
        /// Gets difficulty display text
        /// </summary>
        public string GetDifficultyText()
        {
            return Difficulty switch
            {
                SessionDifficulty.Easy => "⭐ Easy",
                SessionDifficulty.Medium => "⭐⭐ Medium",
                SessionDifficulty.Hard => "⭐⭐⭐ Hard",
                SessionDifficulty.Extreme => "💀 Extreme",
                _ => "⭐ Easy"
            };
        }
        
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
            Difficulty = SessionDifficulty.Easy,
            BonusXP = 50,
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
                FlashOpacityEnd = 30,
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
                AudioDuckLevel = 40, // 40% ducking for morning session
                
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
                BubblesClickable = true,
                BubblesBurstCount = 4,
                BubblesPerBurst = 5,
                BubblesGapMin = 5,
                BubblesGapMax = 8,
                
                // Disabled features
                MandatoryVideosEnabled = false,
                SpiralEnabled = false,
                LockCardEnabled = false,
                BubbleCountEnabled = false,
                MiniGameEnabled = false,
                
                // Mind Wipe (Easy = base 1, escalates every 5 min)
                MindWipeEnabled = true,
                MindWipeBaseMultiplier = 1,
                MindWipeVolume = 40
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
        /// Gets the Gamer Girl session - conditioning while gaming
        /// </summary>
        public static Session GamerGirl => new()
        {
            Id = "gamer_girl",
            Name = "Gamer Girl",
            Icon = "🎮",
            DurationMinutes = 45,
            IsAvailable = true,
            Difficulty = SessionDifficulty.Medium,
            BonusXP = 100,
            HasCornerGifOption = true,
            CornerGifDescription = "Optional: Place a subtle GIF in a screen corner (great for covering minimaps!)",
            Description = @"Time to play, Gamer Girl...

This session was designed for your gaming sessions. Keep playing, keep focusing on your game. You won't even notice what's happening in the background... at first.

The conditioning works while you play. Subtle at first, then slowly building. By the time you notice, you'll already be drifting into that familiar pink haze.

Just play your game. Let everything else happen on its own.

⚠ Set your game to Borderless Windowed mode for the full experience!

💗 Good luck, Gamer Girl...",
            
            Settings = new SessionSettings
            {
                // Flash Images - very subtle, small, infrequent
                FlashEnabled = true,
                FlashPerHour = 4, // Only ~4 per hour (1 every 15 min)
                FlashImages = 1, // Single image at a time
                FlashOpacity = 20, // Very transparent
                FlashOpacityEnd = 35, // Slight ramp
                FlashClickable = false, // Ghost mode - click through
                FlashAudioEnabled = false,
                FlashSmallSize = true, // New: smaller images for gaming
                
                // Subliminals
                SubliminalEnabled = true,
                SubliminalPerMin = 2,
                SubliminalFrames = 2,
                SubliminalOpacity = 45,
                
                // Audio Whispers - barely audible, under game audio
                AudioWhispersEnabled = true,
                WhisperVolume = 12,
                AudioDuckLevel = 55, // 55% ducking for gaming session
                
                // Bouncing Text
                BouncingTextEnabled = true,
                BouncingTextSpeed = 3,
                BouncingTextPhrases = new List<string> { "Gamer Girl", "Good Girl", "Focus...", "💗" },
                
                // Pink Filter (delayed start at 15min)
                PinkFilterEnabled = true,
                PinkFilterStartMinute = 15,
                PinkFilterStartOpacity = 0,
                PinkFilterEndOpacity = 20,
                
                // Spiral (delayed start at 20min)
                SpiralEnabled = true,
                SpiralStartMinute = 20,
                SpiralOpacity = 1,
                SpiralOpacityEnd = 10,
                
                // Bubbles - floating only, no clicking required
                BubblesEnabled = true,
                BubblesIntermittent = true,
                BubblesClickable = false, // Float and auto-disappear
                BubblesBurstCount = 6,
                BubblesPerBurst = 5,
                BubblesGapMin = 6,
                BubblesGapMax = 10,
                
                // Corner GIF option (user configurable)
                CornerGifEnabled = false,
                CornerGifOpacity = 18,
                
                // Disabled features - no interruptions while gaming
                MandatoryVideosEnabled = false,
                LockCardEnabled = false,
                BubbleCountEnabled = false,
                MiniGameEnabled = false,
                
                // Mind Wipe (Medium = base 2, escalates every 5 min)
                MindWipeEnabled = true,
                MindWipeBaseMultiplier = 2,
                MindWipeVolume = 45
            },
            
            Phases = new List<SessionPhase>
            {
                new() { StartMinute = 0, Name = "Game Start", Description = "Subtle flashes and subliminals begin" },
                new() { StartMinute = 15, Name = "Pink Tint", Description = "Pink filter starts creeping in" },
                new() { StartMinute = 20, Name = "Spiral Added", Description = "Gentle spiral overlay joins the mix" },
                new() { StartMinute = 30, Name = "Building", Description = "Effects gradually intensifying" },
                new() { StartMinute = 45, Name = "GG!", Description = "Good Game, Good Girl!" }
            }
        };

        /// <summary>
        /// Gets all sessions including placeholders
        /// </summary>
        public static List<Session> GetAllSessions()
        {
            return new List<Session>
            {
                MorningDrift,
                GamerGirl,
                new Session
                {
                    Id = "deep_dive",
                    Name = "Deep Dive",
                    Icon = "🌙",
                    DurationMinutes = 60,
                    IsAvailable = false,
                    Difficulty = SessionDifficulty.Hard,
                    BonusXP = 200,
                    Description = "A longer, more immersive experience for when you have time to truly let go..."
                },
                new Session
                {
                    Id = "bambi_time",
                    Name = "Bambi Time",
                    Icon = "💗",
                    DurationMinutes = 45,
                    IsAvailable = false,
                    Difficulty = SessionDifficulty.Extreme,
                    BonusXP = 500,
                    Description = "Full Bambi mode. Everything turned up. Complete surrender."
                },
                new Session
                {
                    Id = "random_drop",
                    Name = "Random Drop",
                    Icon = "🎲",
                    DurationMinutes = 20,
                    IsAvailable = false,
                    Difficulty = SessionDifficulty.Medium,
                    BonusXP = 100,
                    Description = "You won't know what's coming. That's the point. Let go of control completely."
                }
            };
        }
        
        /// <summary>
        /// Gets the spoiler details as formatted text
        /// </summary>
        public string GetSpoilerFlash()
        {
            if (!Settings.FlashEnabled) return "Disabled";
            var opacity = Settings.FlashOpacity == Settings.FlashOpacityEnd 
                ? $"{Settings.FlashOpacity}%" 
                : $"{Settings.FlashOpacity}%→{Settings.FlashOpacityEnd}%";
            return $"~{Settings.FlashPerHour}/hour, {Settings.FlashImages} images, {opacity} opacity, " +
                   $"{(Settings.FlashClickable ? "clickable" : "ghost/click-through")}, {(Settings.FlashAudioEnabled ? "with audio" : "silent")}";
        }
        
        public string GetSpoilerSubliminal()
        {
            if (!Settings.SubliminalEnabled) return "Disabled";
            return $"{Settings.SubliminalPerMin}/min, {Settings.SubliminalFrames} frames, {Settings.SubliminalOpacity}% opacity";
        }
        
        public string GetSpoilerAudio()
        {
            if (!Settings.AudioWhispersEnabled) return "Whispers disabled";
            return $"Whispers at {Settings.WhisperVolume}%";
        }
        
        public string GetSpoilerOverlays()
        {
            var parts = new List<string>();
            if (Settings.PinkFilterEnabled)
            {
                parts.Add($"Pink: starts {Settings.PinkFilterStartMinute}min, {Settings.PinkFilterStartOpacity}%→{Settings.PinkFilterEndOpacity}%");
            }
            if (Settings.SpiralEnabled)
            {
                var spiralStart = Settings.SpiralStartMinute > 0 ? $"starts {Settings.SpiralStartMinute}min, " : "";
                var spiralOpacity = Settings.SpiralOpacity == Settings.SpiralOpacityEnd
                    ? $"{Settings.SpiralOpacity}%"
                    : $"{Settings.SpiralOpacity}%→{Settings.SpiralOpacityEnd}%";
                parts.Add($"Spiral: {spiralStart}{spiralOpacity}");
            }
            if (parts.Count == 0) return "None";
            return string.Join("\n", parts);
        }
        
        public string GetSpoilerExtras()
        {
            var parts = new List<string>();
            if (Settings.BouncingTextEnabled)
            {
                var speed = Settings.BouncingTextSpeed <= 3 ? "slow" : Settings.BouncingTextSpeed <= 6 ? "medium" : "fast";
                parts.Add($"Bouncing text ({speed}): \"{string.Join("\", \"", Settings.BouncingTextPhrases)}\"");
            }
            if (Settings.BubblesEnabled)
            {
                var clickInfo = Settings.BubblesClickable ? "pop to dismiss" : "float through";
                parts.Add($"Bubbles: {Settings.BubblesBurstCount} bursts of {Settings.BubblesPerBurst}, {clickInfo}");
            }
            if (Settings.CornerGifEnabled || HasCornerGifOption)
            {
                parts.Add($"Corner GIF: {(Settings.CornerGifEnabled ? "enabled" : "optional")} at {Settings.CornerGifOpacity}% opacity");
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
        public int FlashOpacityEnd { get; set; } = 100; // For ramping
        public bool FlashClickable { get; set; } = true;
        public bool FlashAudioEnabled { get; set; } = true;
        public bool FlashSmallSize { get; set; } = false; // Smaller images for gaming
        
        // Subliminals
        public bool SubliminalEnabled { get; set; }
        public int SubliminalPerMin { get; set; } = 5;
        public int SubliminalFrames { get; set; } = 2;
        public int SubliminalOpacity { get; set; } = 80;
        
        // Audio
        public bool AudioWhispersEnabled { get; set; }
        public int WhisperVolume { get; set; } = 50;
        public int AudioDuckLevel { get; set; } = 100; // 0-100%, how much to duck other audio
        
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
        public int SpiralStartMinute { get; set; } = 0;
        public int SpiralOpacity { get; set; } = 15;
        public int SpiralOpacityEnd { get; set; } = 15; // For ramping
        
        // Bubbles
        public bool BubblesEnabled { get; set; }
        public bool BubblesIntermittent { get; set; }
        public bool BubblesClickable { get; set; } = true;
        public int BubblesBurstCount { get; set; } = 5; // Total bursts in session
        public int BubblesPerBurst { get; set; } = 5; // Bubbles per burst
        public int BubblesGapMin { get; set; } = 5;
        public int BubblesGapMax { get; set; } = 8;
        
        // Corner GIF (for Gamer Girl session)
        public bool CornerGifEnabled { get; set; }
        public int CornerGifOpacity { get; set; } = 20;
        public string CornerGifPath { get; set; } = "";
        public CornerPosition CornerGifPosition { get; set; } = CornerPosition.BottomLeft;
        
        // Disabled features
        public bool MandatoryVideosEnabled { get; set; }
        public bool LockCardEnabled { get; set; }
        public bool BubbleCountEnabled { get; set; }
        public bool MiniGameEnabled { get; set; }
        
        // Mind Wipe (escalating audio during sessions)
        public bool MindWipeEnabled { get; set; }
        public int MindWipeBaseMultiplier { get; set; } = 1; // Starting frequency multiplier (Easy=1, Medium=2, Hard=3)
        public int MindWipeVolume { get; set; } = 50; // Volume for this session
    }
    
    public enum CornerPosition
    {
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
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
