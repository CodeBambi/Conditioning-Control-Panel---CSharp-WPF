using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services
{
    public class SessionTextContent
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<string> SubliminalPhrases { get; set; } = new();
        public List<string> BouncingTextPhrases { get; set; } = new();
        public List<string> LockCardPhrases { get; set; } = new();
    }

    public static class QuizSessionGenerator
    {
        private static readonly Random _random = new();

        public static Session GenerateSession(int totalScore, int maxScore, string categoryId, string categoryName, SessionTextContent textContent)
        {
            var scorePercent = maxScore > 0 ? (double)totalScore / maxScore * 100 : 0;
            var difficulty = scorePercent switch
            {
                <= 25 => SessionDifficulty.Easy,
                <= 50 => SessionDifficulty.Medium,
                <= 75 => SessionDifficulty.Hard,
                _ => SessionDifficulty.Extreme
            };

            var settings = BuildSettings(difficulty, textContent);
            var phases = BuildPhases(difficulty);
            var icon = GetCategoryIcon(categoryId);

            var session = new Session
            {
                Id = Guid.NewGuid().ToString(),
                Name = !string.IsNullOrWhiteSpace(textContent.Name) ? textContent.Name : $"{categoryName} Session",
                Icon = icon,
                DurationMinutes = 60,
                IsAvailable = true,
                Difficulty = difficulty,
                BonusXP = Session.GetDifficultyXP(difficulty),
                Source = SessionSource.Custom,
                Description = !string.IsNullOrWhiteSpace(textContent.Description) ? textContent.Description : $"A personalized {difficulty} session generated from your {categoryName} quiz results.",
                Settings = settings,
                Phases = phases
            };

            return session;
        }

        private static int Randomize(int value)
        {
            var variance = value * 0.15;
            return Math.Max(1, (int)(value + (_random.NextDouble() * 2 - 1) * variance));
        }

        private static int Randomize(int value, int min)
        {
            return Math.Max(min, Randomize(value));
        }

        private static SessionSettings BuildSettings(SessionDifficulty difficulty, SessionTextContent textContent)
        {
            var s = new SessionSettings();

            switch (difficulty)
            {
                case SessionDifficulty.Easy:
                    // Flash
                    s.FlashEnabled = true;
                    s.FlashPerHour = Randomize(12);
                    s.FlashOpacity = Randomize(25, 10);
                    s.FlashHydra = false;
                    s.FlashClickable = true;
                    s.FlashAudioEnabled = true;
                    // Subliminal
                    s.SubliminalEnabled = true;
                    s.SubliminalPerMin = Randomize(2, 1);
                    s.SubliminalOpacity = Randomize(40, 20);
                    s.SubliminalFrames = 2;
                    // Bouncing text
                    s.BouncingTextEnabled = true;
                    s.BouncingTextSpeed = 2;
                    s.BouncingTextSize = 50;
                    s.BouncingTextOpacity = 80;
                    // Pink filter
                    s.PinkFilterEnabled = true;
                    s.PinkFilterStartMinute = 15;
                    s.PinkFilterStartOpacity = 0;
                    s.PinkFilterEndOpacity = 15;
                    // Spiral
                    s.SpiralEnabled = false;
                    // Lock card
                    s.LockCardEnabled = false;
                    // Mandatory videos
                    s.MandatoryVideosEnabled = false;
                    // Bubble count
                    s.BubbleCountEnabled = false;
                    // Mind wipe
                    s.MindWipeEnabled = true;
                    s.MindWipeBaseMultiplier = 1;
                    s.MindWipeVolume = 30;
                    // Brain drain
                    s.BrainDrainEnabled = false;
                    // Bubbles
                    s.BubblesEnabled = true;
                    s.BubblesFrequency = 4;
                    s.BubblesClickable = true;
                    break;

                case SessionDifficulty.Medium:
                    // Flash
                    s.FlashEnabled = true;
                    s.FlashPerHour = Randomize(38);
                    s.FlashOpacity = Randomize(48, 20);
                    s.FlashHydra = false;
                    s.FlashClickable = true;
                    s.FlashAudioEnabled = true;
                    // Subliminal
                    s.SubliminalEnabled = true;
                    s.SubliminalPerMin = Randomize(3, 1);
                    s.SubliminalOpacity = Randomize(55, 30);
                    s.SubliminalFrames = 2;
                    // Bouncing text
                    s.BouncingTextEnabled = true;
                    s.BouncingTextSpeed = 3;
                    s.BouncingTextSize = 75;
                    s.BouncingTextOpacity = 90;
                    // Pink filter
                    s.PinkFilterEnabled = true;
                    s.PinkFilterStartMinute = 10;
                    s.PinkFilterStartOpacity = 5;
                    s.PinkFilterEndOpacity = 30;
                    // Spiral
                    s.SpiralEnabled = true;
                    s.SpiralStartMinute = 20;
                    s.SpiralOpacity = 5;
                    s.SpiralOpacityEnd = 15;
                    // Lock card
                    s.LockCardEnabled = false;
                    // Mandatory videos
                    s.MandatoryVideosEnabled = false;
                    // Bubble count
                    s.BubbleCountEnabled = false;
                    // Mind wipe
                    s.MindWipeEnabled = true;
                    s.MindWipeBaseMultiplier = 2;
                    s.MindWipeVolume = 40;
                    // Brain drain
                    s.BrainDrainEnabled = false;
                    // Bubbles
                    s.BubblesEnabled = true;
                    s.BubblesFrequency = 5;
                    s.BubblesClickable = true;
                    break;

                case SessionDifficulty.Hard:
                    // Flash
                    s.FlashEnabled = true;
                    s.FlashPerHour = Randomize(180);
                    s.FlashOpacity = Randomize(70, 40);
                    s.FlashHydra = true;
                    s.FlashClickable = true;
                    s.FlashAudioEnabled = true;
                    // Subliminal
                    s.SubliminalEnabled = true;
                    s.SubliminalPerMin = Randomize(4, 2);
                    s.SubliminalOpacity = Randomize(70, 40);
                    s.SubliminalFrames = 3;
                    // Bouncing text
                    s.BouncingTextEnabled = true;
                    s.BouncingTextSpeed = 5;
                    s.BouncingTextSize = 100;
                    s.BouncingTextOpacity = 100;
                    // Pink filter
                    s.PinkFilterEnabled = true;
                    s.PinkFilterStartMinute = 5;
                    s.PinkFilterStartOpacity = 10;
                    s.PinkFilterEndOpacity = 45;
                    // Spiral
                    s.SpiralEnabled = true;
                    s.SpiralStartMinute = 10;
                    s.SpiralOpacity = 10;
                    s.SpiralOpacityEnd = 25;
                    // Lock card
                    s.LockCardEnabled = true;
                    s.LockCardStartMinute = 10;
                    s.LockCardFrequency = 2;
                    // Mandatory videos
                    s.MandatoryVideosEnabled = true;
                    s.MandatoryVideosStartMinute = 10;
                    s.VideosPerHour = 2;
                    // Bubble count
                    s.BubbleCountEnabled = true;
                    s.BubbleCountStartMinute = 15;
                    s.BubbleCountFrequency = 3;
                    // Mind wipe
                    s.MindWipeEnabled = true;
                    s.MindWipeBaseMultiplier = 3;
                    s.MindWipeVolume = 50;
                    // Brain drain
                    s.BrainDrainEnabled = false;
                    // Bubbles
                    s.BubblesEnabled = true;
                    s.BubblesFrequency = 6;
                    s.BubblesClickable = true;
                    break;

                case SessionDifficulty.Extreme:
                    // Flash
                    s.FlashEnabled = true;
                    s.FlashPerHour = Randomize(450);
                    s.FlashOpacity = Randomize(68, 40);
                    s.FlashHydra = true;
                    s.FlashClickable = true;
                    s.FlashAudioEnabled = true;
                    // Subliminal
                    s.SubliminalEnabled = true;
                    s.SubliminalPerMin = Randomize(5, 3);
                    s.SubliminalOpacity = Randomize(85, 50);
                    s.SubliminalFrames = 3;
                    // Bouncing text
                    s.BouncingTextEnabled = true;
                    s.BouncingTextSpeed = 7;
                    s.BouncingTextSize = 100;
                    s.BouncingTextOpacity = 100;
                    // Pink filter
                    s.PinkFilterEnabled = true;
                    s.PinkFilterStartMinute = 0;
                    s.PinkFilterStartOpacity = 15;
                    s.PinkFilterEndOpacity = 55;
                    // Spiral
                    s.SpiralEnabled = true;
                    s.SpiralStartMinute = 0;
                    s.SpiralOpacity = 10;
                    s.SpiralOpacityEnd = 35;
                    // Lock card
                    s.LockCardEnabled = true;
                    s.LockCardStartMinute = 5;
                    s.LockCardFrequency = 3;
                    // Mandatory videos
                    s.MandatoryVideosEnabled = true;
                    s.MandatoryVideosStartMinute = 5;
                    s.VideosPerHour = 3;
                    // Bubble count
                    s.BubbleCountEnabled = true;
                    s.BubbleCountStartMinute = 5;
                    s.BubbleCountFrequency = 3;
                    // Mind wipe
                    s.MindWipeEnabled = true;
                    s.MindWipeBaseMultiplier = 3;
                    s.MindWipeVolume = 60;
                    // Brain drain
                    s.BrainDrainEnabled = true;
                    s.BrainDrainStartMinute = 40;
                    s.BrainDrainStartIntensity = 5;
                    s.BrainDrainEndIntensity = 20;
                    // Bubbles
                    s.BubblesEnabled = true;
                    s.BubblesFrequency = 8;
                    s.BubblesClickable = true;
                    break;
            }

            // Apply text content
            if (textContent.SubliminalPhrases.Count > 0)
                s.SubliminalPhrases = new List<string>(textContent.SubliminalPhrases);
            if (textContent.BouncingTextPhrases.Count > 0)
                s.BouncingTextPhrases = new List<string>(textContent.BouncingTextPhrases);
            if (textContent.LockCardPhrases.Count > 0)
                s.LockCardPhrases = new List<string>(textContent.LockCardPhrases);

            return s;
        }

        private static List<SessionPhase> BuildPhases(SessionDifficulty difficulty)
        {
            return difficulty switch
            {
                SessionDifficulty.Easy => new List<SessionPhase>
                {
                    new() { StartMinute = 0, Name = "Settling In", Description = "Easing into it gently." },
                    new() { StartMinute = 20, Name = "Pink Glow", Description = "The pink filter warms up." },
                    new() { StartMinute = 50, Name = "Winding Down", Description = "Gentle cool-down." }
                },
                SessionDifficulty.Medium => new List<SessionPhase>
                {
                    new() { StartMinute = 0, Name = "Warm Up", Description = "Getting started." },
                    new() { StartMinute = 15, Name = "Building", Description = "Intensity is growing." },
                    new() { StartMinute = 35, Name = "Intensifying", Description = "Things are getting serious." },
                    new() { StartMinute = 50, Name = "Peak", Description = "Maximum for this difficulty." }
                },
                SessionDifficulty.Hard => new List<SessionPhase>
                {
                    new() { StartMinute = 0, Name = "Introduction", Description = "Brace yourself." },
                    new() { StartMinute = 10, Name = "Escalation", Description = "Rapid increase in stimuli." },
                    new() { StartMinute = 25, Name = "Deep", Description = "Full immersion." },
                    new() { StartMinute = 40, Name = "Overwhelming", Description = "Relentless conditioning." },
                    new() { StartMinute = 55, Name = "Final Push", Description = "Last stretch of intensity." }
                },
                SessionDifficulty.Extreme => new List<SessionPhase>
                {
                    new() { StartMinute = 0, Name = "No Mercy", Description = "Maximum from the start." },
                    new() { StartMinute = 10, Name = "Spiral Down", Description = "Deeper and deeper." },
                    new() { StartMinute = 20, Name = "Breaking Point", Description = "Resistance is futile." },
                    new() { StartMinute = 35, Name = "Total Override", Description = "Complete sensory overload." },
                    new() { StartMinute = 50, Name = "Maximum Overload", Description = "Everything at once." }
                },
                _ => new List<SessionPhase>
                {
                    new() { StartMinute = 0, Name = "Start", Description = "Session begins." }
                }
            };
        }

        private static string GetCategoryIcon(string categoryId)
        {
            return categoryId.ToLowerInvariant() switch
            {
                "sissy" => "\uD83C\uDF80",       // ribbon
                "bambi" => "\uD83E\uDDE0",       // brain
                "obedience" => "\uD83D\uDD12",   // lock
                "mindlessness" => "\uD83C\uDF00", // cyclone
                "submission" => "\uD83D\uDC96",   // sparkling heart
                _ => "\u2728"                      // sparkles
            };
        }

        public static SessionTextContent GetFallbackContent(string categoryId, double scorePercentage)
        {
            var content = new SessionTextContent();
            var catKey = categoryId.ToLowerInvariant();

            // Difficulty-based name prefix
            var diffPrefix = scorePercentage switch
            {
                <= 25 => "Gentle",
                <= 50 => "Guided",
                <= 75 => "Intense",
                _ => "Extreme"
            };

            switch (catKey)
            {
                case "sissy":
                    content.Name = $"{diffPrefix} Feminization";
                    content.Description = "A session focused on feminine transformation and acceptance.";
                    content.SubliminalPhrases = new List<string>
                    {
                        "Good girl", "So pretty", "Embrace your femininity", "You love being girly",
                        "Pink is your color", "Such a good girl", "Feminine and obedient",
                        "You are beautiful", "Accept who you are", "Soft and pretty"
                    };
                    content.BouncingTextPhrases = new List<string>
                    {
                        "GOOD GIRL", "SO PRETTY", "EMBRACE IT", "PINK", "FEMININE", "BEAUTIFUL"
                    };
                    content.LockCardPhrases = new List<string>
                    {
                        "I am a good girl", "Pink is my favorite color", "I love being pretty",
                        "I embrace my femininity", "Good girls obey", "I am soft and feminine",
                        "Being girly makes me happy", "I accept who I am"
                    };
                    break;

                case "bambi":
                    content.Name = $"{diffPrefix} Bambi Training";
                    content.Description = "A deep conditioning session for Bambi transformation.";
                    content.SubliminalPhrases = new List<string>
                    {
                        "Bambi Sleep", "Good girl Bambi", "Bambi loves to obey", "Empty and happy",
                        "Bambi drops deeper", "No thoughts", "Bambi is a good girl",
                        "Drop for Bambi", "Bambi takes over", "Deeper and deeper"
                    };
                    content.BouncingTextPhrases = new List<string>
                    {
                        "BAMBI SLEEP", "DROP DEEPER", "GOOD GIRL", "OBEY", "EMPTY", "NO THOUGHTS"
                    };
                    content.LockCardPhrases = new List<string>
                    {
                        "Bambi is a good girl", "I love to drop deeper", "Bambi obeys",
                        "Empty and happy", "Good girls drop", "Bambi takes over",
                        "No thoughts just Bambi", "I love being Bambi"
                    };
                    break;

                case "obedience":
                    content.Name = $"{diffPrefix} Obedience Training";
                    content.Description = "A session designed to reinforce obedience and compliance.";
                    content.SubliminalPhrases = new List<string>
                    {
                        "Obey", "Submit", "Good girls listen", "Follow instructions",
                        "Compliance is bliss", "Do as you're told", "Obedience is pleasure",
                        "Listen and obey", "You love to comply", "Surrender control"
                    };
                    content.BouncingTextPhrases = new List<string>
                    {
                        "OBEY", "SUBMIT", "COMPLY", "LISTEN", "FOLLOW", "SURRENDER"
                    };
                    content.LockCardPhrases = new List<string>
                    {
                        "I love to obey", "Good girls follow instructions", "Obedience is pleasure",
                        "I submit willingly", "Compliance makes me happy", "I do as I am told",
                        "Listening is easy", "I surrender control"
                    };
                    break;

                case "mindlessness":
                    content.Name = $"{diffPrefix} Mindless Bliss";
                    content.Description = "A session focused on emptying the mind and embracing bliss.";
                    content.SubliminalPhrases = new List<string>
                    {
                        "Empty mind", "No thoughts", "Blank and happy", "Don't think",
                        "Mindless bliss", "Let go", "Drift away", "So empty",
                        "Thinking is hard", "Just float"
                    };
                    content.BouncingTextPhrases = new List<string>
                    {
                        "EMPTY", "BLANK", "NO THOUGHTS", "DRIFT", "FLOAT", "MINDLESS"
                    };
                    content.LockCardPhrases = new List<string>
                    {
                        "My mind is empty", "Thinking is so hard", "I love being blank",
                        "No thoughts just bliss", "Empty heads are happy heads", "I drift away so easily",
                        "Mindless is happy", "I let my thoughts go"
                    };
                    break;

                case "submission":
                    content.Name = $"{diffPrefix} Submission";
                    content.Description = "A session to deepen submission and surrender.";
                    content.SubliminalPhrases = new List<string>
                    {
                        "Submit", "Surrender", "Give in", "You are owned",
                        "Good girls submit", "Let go of control", "Deeper submission",
                        "You belong", "Surrender completely", "Submit and feel bliss"
                    };
                    content.BouncingTextPhrases = new List<string>
                    {
                        "SUBMIT", "SURRENDER", "GIVE IN", "OWNED", "DEEPER", "BELONG"
                    };
                    content.LockCardPhrases = new List<string>
                    {
                        "I submit willingly", "Surrender feels so good", "I give in completely",
                        "I belong", "Good girls surrender", "I let go of control",
                        "Submission is bliss", "I am deeply submissive"
                    };
                    break;

                default:
                    content.Name = $"{diffPrefix} Conditioning";
                    content.Description = "A personalized conditioning session.";
                    content.SubliminalPhrases = new List<string>
                    {
                        "Good girl", "Obey", "Submit", "Let go", "Deeper",
                        "Surrender", "Empty mind", "So pretty", "Accept it", "Drift away"
                    };
                    content.BouncingTextPhrases = new List<string>
                    {
                        "OBEY", "SUBMIT", "DEEPER", "GOOD GIRL", "LET GO", "SURRENDER"
                    };
                    content.LockCardPhrases = new List<string>
                    {
                        "I am a good girl", "I love to obey", "Surrender feels good",
                        "I submit willingly", "Empty and happy", "Good girls listen",
                        "I let go of control", "Deeper and deeper"
                    };
                    break;
            }

            return content;
        }
    }
}
