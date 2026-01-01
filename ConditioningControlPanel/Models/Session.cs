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
                SessionDifficulty.Easy => 400,
                SessionDifficulty.Medium => 800,
                SessionDifficulty.Hard => 1200,
                SessionDifficulty.Extreme => 2000,
                _ => 400
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
            BonusXP = 400,
            Description = @"Let the morning carry you gently into that soft, floaty space...

This session is designed for your morning routine - while you work, browse, or prepare for the day. No interruptions, no demands. Just gentle whispers and soft reminders that help good girls drift into that comfortable, familiar headspace.

Perfect for multitasking. Perfect for letting go without even trying.

You don't need to do anything special. Just... let it happen. 💗",
            
            Settings = new SessionSettings
            {
                // Flash Images
                FlashEnabled = true,
                FlashPerHour = 12,
                FlashPerHourEnd = 12, // No frequency ramping
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
                
                // Bubbles (ramping)
                BubblesEnabled = true,
                BubblesIntermittent = false,
                BubblesClickable = true,
                BubblesStartMinute = 5,
                BubblesFrequency = 1,
                
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
            BonusXP = 800,
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
                FlashPerHourEnd = 4, // No frequency ramping
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
        /// The Distant Doll - Passive couch session for watching videos
        /// Duration: 45 minutes, Difficulty: Easy
        /// </summary>
        public static Session DistantDoll { get; } = new Session
        {
            Id = "distant_doll",
            Name = "The Distant Doll",
            Icon = "🛋️",
            DurationMinutes = 45,
            IsAvailable = true,
            Difficulty = SessionDifficulty.Easy,
            BonusXP = 400,
            Description = "No need to get up, sweetheart. Stay comfortable, soft, and empty. Everything is designed to be viewed from a distance while your mind drifts away. Perfect for turning your relaxation time into a passive reprogramming session.",
            
            Settings = new SessionSettings
            {
                // Flash Images - Relaxed pace, large format for distance viewing
                FlashEnabled = true,
                FlashPerHour = 30,
                FlashPerHourEnd = 30, // No frequency ramping
                FlashImages = 3,
                FlashOpacity = 35,
                FlashOpacityEnd = 35,
                FlashScale = 150, // Large for distance viewing
                FlashClickable = false, // Ghost mode
                FlashAudioEnabled = false, // Silent flashes
                
                // Subliminals - Low frequency
                SubliminalEnabled = true,
                SubliminalPerMin = 2,
                SubliminalFrames = 2,
                SubliminalOpacity = 60,
                
                // Audio Whispers - Low volume background
                AudioWhispersEnabled = true,
                WhisperVolume = 15,
                AudioDuckLevel = 0, // NO audio ducking - video volume stays normal
                
                // Pink Filter - Delayed start, ramps to 35%
                PinkFilterEnabled = true,
                PinkFilterStartMinute = 7, // Random ±3 will make it 5-10
                PinkFilterStartOpacity = 5,
                PinkFilterEndOpacity = 35,
                
                // Spiral - Delayed start, subtle
                SpiralEnabled = true,
                SpiralStartMinute = 12, // Random ±3 will make it 10-15
                SpiralOpacity = 5,
                SpiralOpacityEnd = 15,
                
                // Bubbles - Rare visual-only bursts
                BubblesEnabled = true,
                BubblesIntermittent = true,
                BubblesClickable = false, // Visual only
                BubblesBurstCount = 5, // ~5 times in 45 min
                BubblesPerBurst = 3,
                BubblesGapMin = 7,
                BubblesGapMax = 12,
                
                // Mind Wipe - Low volume, escalating
                MindWipeEnabled = true,
                MindWipeBaseMultiplier = 1, // Easy start
                MindWipeVolume = 12,
                
                // All interactions DISABLED
                MandatoryVideosEnabled = false,
                LockCardEnabled = false,
                BubbleCountEnabled = false,
                MiniGameEnabled = false,
                BouncingTextEnabled = false
            },
            
            Phases = new List<SessionPhase>
            {
                new() { StartMinute = 0, Name = "Settling In", Description = "Get comfortable on the couch..." },
                new() { StartMinute = 7, Name = "Pink Creeping", Description = "The pink fog begins to form" },
                new() { StartMinute = 12, Name = "Spiral Dreams", Description = "Gentle spirals join the view" },
                new() { StartMinute = 25, Name = "Deep Drift", Description = "Mind wipes increasing, drifting deeper" },
                new() { StartMinute = 40, Name = "Empty Doll", Description = "Completely passive, completely pretty" },
                new() { StartMinute = 45, Name = "Complete", Description = "Such a good doll 💗" }
            }
        };

        /// <summary>
        /// Good Girls Don't Cum - Denial/edging session with heavy conditioning
        /// Duration: 60 minutes, Difficulty: Hard
        /// </summary>
        public static Session GoodGirlsDontCum { get; } = new Session
        {
            Id = "good_girls_dont_cum",
            Name = "Good Girls Don't Cum",
            Icon = "🔒",
            DurationMinutes = 60,
            IsAvailable = true,
            Difficulty = SessionDifficulty.Hard,
            BonusXP = 1200,
            Description = "A challenging denial and edging session designed to test your obedience and focus. Expect frequent conditioning visuals, audio whispers, and interactive elements designed to reinforce your training. Your only purpose is to sit prettily and let the pink fog consume you. And remember not to touch that clitty, Good Girls Don't Cum.",
            
            Settings = new SessionSettings
            {
                // Flash Images - Starts slow, RAMPS UP to block view
                FlashEnabled = true,
                FlashPerHour = 180, // 3 bursts/min start
                FlashPerHourEnd = 600, // 10 bursts/min end
                FlashImages = 3,
                FlashOpacity = 35,
                FlashOpacityEnd = 90, // Blocks view at end
                FlashScale = 100,
                FlashClickable = false, // Ghost mode
                FlashAudioEnabled = false, // Silent - denial is quiet
                
                // Subliminals - Frequent
                SubliminalEnabled = true,
                SubliminalPerMin = 4,
                SubliminalFrames = 3,
                SubliminalOpacity = 70,
                
                // Audio Whispers
                AudioWhispersEnabled = true,
                WhisperVolume = 15,
                AudioDuckLevel = 50, // 50% ducking - video gets quieter
                
                // Pink Filter - Heavy, from start
                PinkFilterEnabled = true,
                PinkFilterStartMinute = 0, // Immediate
                PinkFilterStartOpacity = 10,
                PinkFilterEndOpacity = 50, // Very heavy pink
                
                // Spiral - Delayed, ramps high
                SpiralEnabled = true,
                SpiralStartMinute = 5,
                SpiralOpacity = 5,
                SpiralOpacityEnd = 30, // Strong spiral
                
                // Bouncing Text - Denial phrases
                BouncingTextEnabled = true,
                BouncingTextSpeed = 4,
                BouncingTextPhrases = new List<string> 
                { 
                    "BIMBO DOLL", 
                    "GOOD GIRLS DONT CUM", 
                    "LISTEN AND OBEY", 
                    "BAMBI CLIT LOCK", 
                    "GOOD GIRL",
                    "NO TOUCHING",
                    "DENIED 💗"
                },
                
                // Mind Wipe - Starts at 2/min, escalates
                MindWipeEnabled = true,
                MindWipeBaseMultiplier = 2, // Medium start (2 per 5min block)
                MindWipeVolume = 25,

                // Bubbles (ramping)
                BubblesEnabled = true,
                BubblesIntermittent = false,
                BubblesClickable = true,
                BubblesStartMinute = 5,
                BubblesFrequency = 1,
                
                // Interactive Events
                MandatoryVideosEnabled = true,
                VideosPerHour = 2,
                LockCardEnabled = true,
                LockCardFrequency = 2,
                BubbleCountEnabled = true,
                BubbleCountFrequency = 2
            },
            
            Phases = new List<SessionPhase>
            {
                new() { StartMinute = 0, Name = "Denial Begins", Description = "Hands off. Eyes forward. Mind empty." },
                new() { StartMinute = 5, Name = "Spiral Starts", Description = "The spiral draws you in deeper..." },
                new() { StartMinute = 15, Name = "Building", Description = "Flash images increasing, pink deepening" },
                new() { StartMinute = 30, Name = "Half Way", Description = "You're doing so well. Don't touch." },
                new() { StartMinute = 45, Name = "Overwhelming", Description = "Images blocking view, mind melting" },
                new() { StartMinute = 55, Name = "Final Push", Description = "Maximum intensity. Stay denied." },
                new() { StartMinute = 60, Name = "Complete", Description = "Good girl. You didn't cum. 🔒" }
            }
        };

        /// <summary>
        /// Gets all sessions including placeholders
        /// </summary>
        /// </summary>
        public static List<Session> GetAllSessions()
        {
            return new List<Session>
            {
                MorningDrift,
                GamerGirl,
                DistantDoll,
                GoodGirlsDontCum,
                new Session
                {
                    Id = "deep_dive",
                    Name = "Deep Dive",
                    Icon = "🌙",
                    DurationMinutes = 60,
                    IsAvailable = false,
                    Difficulty = SessionDifficulty.Hard,
                    BonusXP = 1200,
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
                    BonusXP = 2000,
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
                    BonusXP = 800,
                    Description = "You won't know what's coming. That's the point. Let go of control completely."
                }
            };
        }
        
        /// <summary>
        /// Gets the spoiler details as formatted text
        /// </summary>
        public string GetSpoilerFlash()
        {
            if (!Settings.FlashEnabled) return "■ Flashes: Disabled";
            var frequency = Settings.FlashPerHour == Settings.FlashPerHourEnd 
                ? $"~{Settings.FlashPerHour}/hr" 
                : $"~{Settings.FlashPerHour}→{Settings.FlashPerHourEnd}/hr";
            var opacity = Settings.FlashOpacity == Settings.FlashOpacityEnd 
                ? $"{Settings.FlashOpacity}%" 
                : $"{Settings.FlashOpacity}% → {Settings.FlashOpacityEnd}%";
            var scale = Settings.FlashScale == 100 ? "" : $" ({Settings.FlashScale}% size)";
            var audio = Settings.FlashAudioEnabled ? " audio-linked" : " silent";
            var clickable = Settings.FlashClickable ? "" : " ghost";
            return $"■ Flashes: {frequency}, {Settings.FlashImages} images, {opacity}{scale}{audio}{clickable}";
        }
        
        public string GetSpoilerSubliminal()
        {
            if (!Settings.SubliminalEnabled) return "■ Text Subliminals: Disabled";
            return $"■ Text Subliminals: {Settings.SubliminalPerMin}/min, {Settings.SubliminalFrames} frames, {Settings.SubliminalOpacity}% opacity. Uses global phrase pool.";
        }
        
        public string GetSpoilerAudio()
        {
            if (!Settings.AudioWhispersEnabled) return "■ Audio Whispers: Disabled";
            return $"■ Audio Whispers: {Settings.WhisperVolume}% volume. Uses global audio pool.";
        }
        
        public string GetSpoilerOverlays()
        {
            var parts = new List<string>();
            if (Settings.PinkFilterEnabled)
            {
                var ramp = Settings.PinkFilterStartOpacity == Settings.PinkFilterEndOpacity
                    ? $"{Settings.PinkFilterStartOpacity}%"
                    : $"{Settings.PinkFilterStartOpacity}% → {Settings.PinkFilterEndOpacity}%";
                var timing = Settings.PinkFilterStartMinute > 0 ? $" (starts at ~{Settings.PinkFilterStartMinute} min)" : "";
                parts.Add($"■ Pink Filter: {ramp}{timing}");
            }
            if (Settings.SpiralEnabled)
            {
                var ramp = Settings.SpiralOpacity == Settings.SpiralOpacityEnd
                    ? $"{Settings.SpiralOpacity}%"
                    : $"{Settings.SpiralOpacity}% → {Settings.SpiralOpacityEnd}%";
                var timing = Settings.SpiralStartMinute > 0 ? $" (starts at ~{Settings.SpiralStartMinute} min)" : "";
                parts.Add($"■ Spiral Overlay: {ramp}{timing}");
            }
            if (HasCornerGifOption)
            {
                var status = Settings.CornerGifEnabled ? "Enabled" : "Optional";
                parts.Add($"■ Corner GIF: {status} at {Settings.CornerGifOpacity}% opacity");
            }
            if (parts.Count == 0) return "■ Overlays: None";
            return string.Join("\n", parts);
        }
        
        public string GetSpoilerInteractive()
        {
            var parts = new List<string>();
            if (Settings.BouncingTextEnabled)
            {
                var speed = Settings.BouncingTextSpeed <= 3 ? "slow" : Settings.BouncingTextSpeed <= 6 ? "medium" : "fast";
                var phrases = Settings.BouncingTextPhrases.Any() 
                    ? $"Using phrases: \"{string.Join("\", \"", Settings.BouncingTextPhrases)}\"" 
                    : "Uses global phrase pool";
                parts.Add($"■ Bouncing Text: {speed} speed. {phrases}.");
            }

            if (Settings.BubblesEnabled)
            {
                string bubbleDesc;
                if (Settings.BubblesIntermittent)
                {
                    var clickInfo = Settings.BubblesClickable ? "pop to dismiss" : "float-through";
                    bubbleDesc = $"Intermittent bursts (~{Settings.BubblesBurstCount} total), {clickInfo}.";
                }
                else
                {
                    var freq = Settings.BubblesFrequency;
                    var timing = Settings.BubblesStartMinute > 0 ? $"starts at {Settings.BubblesStartMinute} min, ramps up from {freq}/min" : $"~{freq}/min";
                    bubbleDesc = $"Continuous at {timing}.";
                }
                parts.Add($"■ Bubbles: {bubbleDesc}");
            }
    
            if (Settings.MandatoryVideosEnabled)
            {
                parts.Add($"■ Mandatory Videos: ~{Settings.VideosPerHour}/hour. Uses global video pool.");
            }
    
            if (Settings.LockCardEnabled)
            {
                parts.Add($"■ Lock Cards: ~{Settings.LockCardFrequency}/hour. Uses global phrase pool.");
            }

            if (Settings.BubbleCountEnabled)
            {
                parts.Add($"■ Bubble Count Game: ~{Settings.BubbleCountFrequency}/hour.");
            }

            if (parts.Count == 0) return "■ Interactive Events: None";
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
        public int FlashPerHourEnd { get; set; } = 10; // For frequency ramping
        public int FlashImages { get; set; } = 2;
        public int FlashOpacity { get; set; } = 100;
        public int FlashOpacityEnd { get; set; } = 100; // For ramping
        public int FlashScale { get; set; } = 100; // Image scale percentage
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
        public int BubblesFrequency { get; set; } = 5;
        public bool BubblesIntermittent { get; set; }
        public bool BubblesClickable { get; set; } = true;
        public int BubblesBurstCount { get; set; } = 5; // Total bursts in session
        public int BubblesPerBurst { get; set; } = 5; // Bubbles per burst
        public int BubblesGapMin { get; set; } = 5;
        public int BubblesGapMax { get; set; } = 8;
        public int BubblesStartMinute { get; set; } = 0;
        
        // Corner GIF (for Gamer Girl session)
        public bool CornerGifEnabled { get; set; }
        public int CornerGifOpacity { get; set; } = 20;
        public string CornerGifPath { get; set; } = "";
        public CornerPosition CornerGifPosition { get; set; } = CornerPosition.BottomLeft;
        
        // Interactive Features
        public bool MandatoryVideosEnabled { get; set; }
        public int? VideosPerHour { get; set; }
        public bool LockCardEnabled { get; set; }
        public int? LockCardFrequency { get; set; }
        public bool BubbleCountEnabled { get; set; }
        public int? BubbleCountFrequency { get; set; }
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
