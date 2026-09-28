using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Serilog;

namespace ConditioningControlPanel.Services
{
    public enum QuizCategory
    {
        Sissy,
        Bambi,
        Obedience,
        Mindlessness,
        Submission
    }

    public class QuizArchetypeDefinition
    {
        public string Name { get; set; } = string.Empty;
        public int MinPercentage { get; set; }
        public int MaxPercentage { get; set; }
        public string Description { get; set; } = string.Empty;
    }

    public class QuizCategoryDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string SystemPromptTemplate { get; set; } = string.Empty;
        public string Color { get; set; } = "#FF69B4";
        public bool IsBuiltIn { get; set; }
        public List<QuizArchetypeDefinition> Archetypes { get; set; } = new();

        /// <summary>Maps to QuizCategory enum for built-in categories, or null for custom.</summary>
        [JsonIgnore]
        public QuizCategory? EnumCategory { get; set; }

        public string GetArchetypeName(double percentage)
        {
            // Archetypes are sorted by MinPercentage ascending
            for (int i = Archetypes.Count - 1; i >= 0; i--)
            {
                if (percentage >= Archetypes[i].MinPercentage)
                    return Archetypes[i].Name;
            }
            return Archetypes.Count > 0 ? Archetypes[0].Name : "Unknown";
        }

        public string GetFallbackProfile(int totalScore, int maxScore)
        {
            var pct = maxScore > 0 ? (double)totalScore / maxScore * 100 : 0;
            var archetype = GetArchetypeName(pct);
            var archetypeDef = Archetypes.FirstOrDefault(a => a.Name == archetype);
            var desc = archetypeDef?.Description ?? "Your answers reveal a unique personality.";
            return $"You are a {archetype}. {desc}";
        }
    }

    public class QuizQuestion
    {
        public int Number { get; set; }
        public string QuestionText { get; set; } = string.Empty;
        public string[] Answers { get; set; } = new string[4];
        public int[] Points { get; set; } = new int[4];
    }

    public class QuizResult
    {
        public int TotalScore { get; set; }
        public int MaxScore { get; set; }
        public string ProfileText { get; set; } = string.Empty;
        public QuizCategory Category { get; set; }
    }

    public class QuizAnswerRecord
    {
        public int QuestionNumber { get; set; }
        public string QuestionText { get; set; } = string.Empty;
        public string[] AllAnswers { get; set; } = new string[4];
        public int[] AllPoints { get; set; } = new int[4];
        public int ChosenIndex { get; set; }
        public int PointsEarned { get; set; }
    }

    public class QuizHistoryEntry
    {
        public DateTime TakenAt { get; set; }
        public QuizCategory Category { get; set; }
        public int TotalScore { get; set; }
        public int MaxScore { get; set; }
        public string ProfileText { get; set; } = string.Empty;
        public List<QuizAnswerRecord> Answers { get; set; } = new();

        /// <summary>String category ID for custom categories. Falls back to Category enum name for built-in.</summary>
        public string CategoryId { get; set; } = string.Empty;

        /// <summary>Display name for the category (useful for custom categories where enum doesn't apply).</summary>
        public string CategoryName { get; set; } = string.Empty;
    }

    public enum TrendDirection
    {
        Up,
        Down,
        Flat,
        FirstQuiz
    }

    public class QuizScoreTrend
    {
        public int LatestPercent { get; set; }
        public int PreviousPercent { get; set; }
        public int AveragePercent { get; set; }
        public int QuizCount { get; set; }
        public TrendDirection Direction { get; set; }
        public int DeltaPercent { get; set; }
    }

    /// <summary>Payload for QuizService.QuizCompleted in the WPF head.</summary>
    public class QuizCompletedEventArgs : EventArgs
    {
        public int Score { get; init; }
        public bool Passed { get; init; }
        public bool Perfect { get; init; }
        public string Category { get; init; } = "";
    }

    /// <summary>Offline half of the WPF QuizService: categories, fallback content, scoring, trends, and
    /// quiz_history.json / custom_quiz_categories.json (WPF names, location, Newtonsoft shape).</summary>
    public static class QuizStore
    {
        private const int MaxHistoryEntries = 50;
        private static string HistoryFilePath => Path.Combine(CorePaths.UserData, "quiz_history.json");

        public static List<QuizHistoryEntry> LoadHistory()
        {
            try
            {
                var path = HistoryFilePath;
                if (!File.Exists(path)) return new List<QuizHistoryEntry>();

                var json = File.ReadAllText(path);
                var list = JsonConvert.DeserializeObject<List<QuizHistoryEntry>>(json);
                return list ?? new List<QuizHistoryEntry>();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "QuizService: Failed to load quiz history");
                return new List<QuizHistoryEntry>();
            }
        }

        public static void SaveEntry(QuizHistoryEntry entry)
        {
            try
            {
                var list = LoadHistory();
                list.Insert(0, entry);
                if (list.Count > MaxHistoryEntries)
                    list.RemoveRange(MaxHistoryEntries, list.Count - MaxHistoryEntries);

                var json = JsonConvert.SerializeObject(list, Formatting.Indented);
                var path = HistoryFilePath;
                var tmpPath = path + ".tmp";
                File.WriteAllText(tmpPath, json);
                File.Move(tmpPath, path, overwrite: true);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "QuizService: Failed to save quiz history entry");
            }
        }


        /// <summary>
        /// Aggregation key for trend/stat grouping. The Category enum is lossy for custom
        /// categories (StartQuizAsync collapses them all to Sissy), so group by the string
        /// CategoryId and only fall back to the enum name for legacy entries that predate it.
        /// </summary>
        public static string TrendKey(QuizHistoryEntry h) =>
            !string.IsNullOrEmpty(h.CategoryId) ? h.CategoryId : h.Category.ToString();

        /// <summary>Human-readable category name with the legacy-entry fallback.</summary>
        public static string DisplayName(QuizHistoryEntry h) =>
            !string.IsNullOrEmpty(h.CategoryName) ? h.CategoryName : h.Category.ToString();

        public static QuizScoreTrend? GetScoreTrend(List<QuizHistoryEntry> history, string categoryId)
        {
            var filtered = history
                .Where(h => string.Equals(TrendKey(h), categoryId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(h => h.TakenAt).ToList();
            if (filtered.Count == 0) return null;

            var latest = filtered[0];
            var latestPct = latest.MaxScore > 0 ? (int)Math.Round((double)latest.TotalScore / latest.MaxScore * 100) : 0;

            var avgPct = (int)Math.Round(filtered.Average(h => h.MaxScore > 0 ? (double)h.TotalScore / h.MaxScore * 100 : 0));

            if (filtered.Count == 1)
            {
                return new QuizScoreTrend
                {
                    LatestPercent = latestPct,
                    PreviousPercent = 0,
                    AveragePercent = latestPct,
                    QuizCount = 1,
                    Direction = TrendDirection.FirstQuiz,
                    DeltaPercent = 0
                };
            }

            var previous = filtered[1];
            var prevPct = previous.MaxScore > 0 ? (int)Math.Round((double)previous.TotalScore / previous.MaxScore * 100) : 0;
            var delta = latestPct - prevPct;
            var direction = delta > 0 ? TrendDirection.Up : delta < 0 ? TrendDirection.Down : TrendDirection.Flat;

            return new QuizScoreTrend
            {
                LatestPercent = latestPct,
                PreviousPercent = prevPct,
                AveragePercent = avgPct,
                QuizCount = filtered.Count,
                Direction = direction,
                DeltaPercent = delta
            };
        }

        // ============ CATEGORY DEFINITIONS ============

        private static string CustomCategoriesFilePath => Path.Combine(CorePaths.UserData, "custom_quiz_categories.json");

        public static List<QuizCategoryDefinition> GetBuiltInCategories()
        {
            return new List<QuizCategoryDefinition>
            {
                new QuizCategoryDefinition
                {
                    Id = "sissy", Name = "Sissy", Description = "How deep into feminization are you really?",
                    Color = "#FF69B4", IsBuiltIn = true, EnumCategory = QuizCategory.Sissy,
                    Archetypes = new List<QuizArchetypeDefinition>
                    {
                        new() { Name = "Curious Newcomer", MinPercentage = 0, MaxPercentage = 25, Description = "You're just peeking behind the curtain, and that's perfectly okay." },
                        new() { Name = "Closet Sissy", MinPercentage = 26, MaxPercentage = 50, Description = "You've got a secret side that's begging to come out." },
                        new() { Name = "Sissy in Training", MinPercentage = 51, MaxPercentage = 70, Description = "You're actively building your skills, wardrobe, and confidence." },
                        new() { Name = "Sissy Princess", MinPercentage = 71, MaxPercentage = 85, Description = "You've embraced your feminine side with open arms and painted nails." },
                        new() { Name = "Full Sissy", MinPercentage = 86, MaxPercentage = 100, Description = "You're not exploring — you're LIVING it." },
                    }
                },
                new QuizCategoryDefinition
                {
                    Id = "bambi", Name = "Bambi", Description = "How susceptible to conditioning are you?",
                    Color = "#9B59B6", IsBuiltIn = true, EnumCategory = QuizCategory.Bambi,
                    Archetypes = new List<QuizArchetypeDefinition>
                    {
                        new() { Name = "Curious Listener", MinPercentage = 0, MaxPercentage = 25, Description = "You've just discovered the files and barely scratched the surface." },
                        new() { Name = "Trance Dabbler", MinPercentage = 26, MaxPercentage = 50, Description = "You've been under a few times and you're starting to feel the pull." },
                        new() { Name = "Bambi in Training", MinPercentage = 51, MaxPercentage = 70, Description = "The triggers are starting to work and the persona is forming." },
                        new() { Name = "Deep Bambi", MinPercentage = 71, MaxPercentage = 85, Description = "You're fully responsive. Triggers pull you under instantly." },
                        new() { Name = "Gone Bambi", MinPercentage = 86, MaxPercentage = 100, Description = "There's barely anyone left but Bambi." },
                    }
                },
                new QuizCategoryDefinition
                {
                    Id = "obedience", Name = "Obedience", Description = "How naturally do you follow and comply?",
                    Color = "#E67E22", IsBuiltIn = true, EnumCategory = QuizCategory.Obedience,
                    Archetypes = new List<QuizArchetypeDefinition>
                    {
                        new() { Name = "Free Spirit", MinPercentage = 0, MaxPercentage = 25, Description = "Rules are suggestions, and you make your own path." },
                        new() { Name = "Willing Listener", MinPercentage = 26, MaxPercentage = 50, Description = "You follow when it feels right — on your own terms." },
                        new() { Name = "Eager Follower", MinPercentage = 51, MaxPercentage = 70, Description = "You find comfort in structure and direction from others." },
                        new() { Name = "Devoted Servant", MinPercentage = 71, MaxPercentage = 85, Description = "Obedience comes naturally — you thrive when given clear commands." },
                        new() { Name = "Perfect Automaton", MinPercentage = 86, MaxPercentage = 100, Description = "Commands are executed before you even think. Obedience is your default state." },
                    }
                },
                new QuizCategoryDefinition
                {
                    Id = "mindlessness", Name = "Mindlessness", Description = "How comfortable are you with going blank?",
                    Color = "#3498DB", IsBuiltIn = true, EnumCategory = QuizCategory.Mindlessness,
                    Archetypes = new List<QuizArchetypeDefinition>
                    {
                        new() { Name = "Overthinker", MinPercentage = 0, MaxPercentage = 25, Description = "Your mind is always racing — emptiness feels foreign." },
                        new() { Name = "Curious Drifter", MinPercentage = 26, MaxPercentage = 50, Description = "You've tasted moments of quiet and want to explore more." },
                        new() { Name = "Willing Blank", MinPercentage = 51, MaxPercentage = 70, Description = "Letting go of thoughts is becoming second nature to you." },
                        new() { Name = "Empty Vessel", MinPercentage = 71, MaxPercentage = 85, Description = "Your mind empties easily — thoughts dissolve on command." },
                        new() { Name = "Gone Blank", MinPercentage = 86, MaxPercentage = 100, Description = "There's nothing left but blissful emptiness. Thinking is a distant memory." },
                    }
                },
                new QuizCategoryDefinition
                {
                    Id = "submission", Name = "Submission", Description = "How deep does your desire to serve go?",
                    Color = "#E74C3C", IsBuiltIn = true, EnumCategory = QuizCategory.Submission,
                    Archetypes = new List<QuizArchetypeDefinition>
                    {
                        new() { Name = "Independent Soul", MinPercentage = 0, MaxPercentage = 25, Description = "You value autonomy and equality above all else." },
                        new() { Name = "Curious Explorer", MinPercentage = 26, MaxPercentage = 50, Description = "Power exchange intrigues you — you're testing the waters." },
                        new() { Name = "Willing Submissive", MinPercentage = 51, MaxPercentage = 70, Description = "You actively seek opportunities to serve and please." },
                        new() { Name = "Devoted Sub", MinPercentage = 71, MaxPercentage = 85, Description = "Service and submission are core to who you are." },
                        new() { Name = "Total Surrender", MinPercentage = 86, MaxPercentage = 100, Description = "You exist to serve. Submission isn't a choice — it's your nature." },
                    }
                },
            };
        }

        public static List<QuizCategoryDefinition> LoadCustomCategories()
        {
            try
            {
                var path = CustomCategoriesFilePath;
                if (!File.Exists(path)) return new List<QuizCategoryDefinition>();
                var json = File.ReadAllText(path);
                return JsonConvert.DeserializeObject<List<QuizCategoryDefinition>>(json) ?? new List<QuizCategoryDefinition>();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "QuizService: Failed to load custom categories");
                return new List<QuizCategoryDefinition>();
            }
        }

        public static void SaveCustomCategory(QuizCategoryDefinition category)
        {
            try
            {
                var list = LoadCustomCategories();
                var existing = list.FindIndex(c => c.Id == category.Id);
                if (existing >= 0)
                    list[existing] = category;
                else
                    list.Add(category);

                var json = JsonConvert.SerializeObject(list, Formatting.Indented);
                var path = CustomCategoriesFilePath;
                var tmpPath = path + ".tmp";
                File.WriteAllText(tmpPath, json);
                File.Move(tmpPath, path, overwrite: true);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "QuizService: Failed to save custom category");
            }
        }

        public static void DeleteCustomCategory(string categoryId)
        {
            try
            {
                var list = LoadCustomCategories();
                list.RemoveAll(c => c.Id == categoryId);
                var json = JsonConvert.SerializeObject(list, Formatting.Indented);
                var path = CustomCategoriesFilePath;
                var tmpPath = path + ".tmp";
                File.WriteAllText(tmpPath, json);
                File.Move(tmpPath, path, overwrite: true);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "QuizService: Failed to delete custom category");
            }
        }

        public static List<QuizCategoryDefinition> GetAllCategories()
        {
            var all = GetBuiltInCategories();
            all.AddRange(LoadCustomCategories());
            return all;
        }

        /// <summary>
        /// Finds a category definition by its Id or by QuizCategory enum name.
        /// </summary>
        public static QuizCategoryDefinition? FindCategory(string idOrName)
        {
            var all = GetAllCategories();
            return all.FirstOrDefault(c => c.Id.Equals(idOrName, StringComparison.OrdinalIgnoreCase)
                || c.Name.Equals(idOrName, StringComparison.OrdinalIgnoreCase));
        }

        public static QuizQuestion GetFallbackQuestion(QuizCategory category, int questionNum)
        {
            var fallbacks = category switch
            {
                QuizCategory.Sissy => new[]
                {
                    ("What's in your secret wardrobe?", new[] { "Nothing", "A few pairs of panties", "A lingerie collection", "A complete feminine wardrobe" }),
                    ("How do you feel about wearing makeup?", new[] { "Never tried it", "Curious about it", "I've practiced a few times", "I have a full routine" }),
                    ("Could you go out dressed feminine in public?", new[] { "Absolutely not", "Maybe somewhere far away", "I've thought about it seriously", "I already do" }),
                    ("Someone tells you to curtsy. You...", new[] { "Refuse", "Feel a secret thrill", "Do it when no one's watching", "Curtsy perfectly and say thank you" }),
                    ("How smooth are your legs right now?", new[] { "Haven't touched them", "Trimmed once or twice", "I shave regularly", "Silky smooth, always" }),
                    ("How often do you imagine yourself as a girl?", new[] { "Rarely", "Sometimes before bed", "More than I'd admit", "It's my default headspace" }),
                    ("How do you buy feminine clothes?", new[] { "I don't", "Online, shipped discreetly", "Online without hiding it", "In-store, no shame" }),
                    ("How does it feel when someone calls you a good girl?", new[] { "Weird", "A little flutter", "My heart melts", "It's the best thing anyone can say to me" }),
                    ("Have you ever practiced a feminine voice or walk?", new[] { "No", "Tried once or twice in private", "I practice regularly", "I can switch effortlessly" }),
                    ("How do you feel about serving someone?", new[] { "Not for me", "Intriguing in theory", "I enjoy it in the right context", "I was born to serve" }),
                },
                QuizCategory.Bambi => new[]
                {
                    ("You put on Bubble Induction and close your eyes. What happens?", new[] { "Nothing much", "I relax a little", "I start sinking fast", "I'm gone before the induction ends" }),
                    ("Someone whispers 'Good Girl.' You...", new[] { "Nothing", "A small warm feeling", "My mind goes fuzzy", "Instant bliss — I melt completely" }),
                    ("After listening to Named And Drained, how strong is your Bambi persona?", new[] { "What persona?", "She peeks out sometimes", "She takes over during sessions", "She's always there, waiting" }),
                    ("IQ Lock plays and your thoughts start fading. How does that feel?", new[] { "Scary", "Curious about it", "It's happened and I liked it", "Think Thots — it's my favorite feeling" }),
                    ("You hear 'Bambi Does As She's Told.' You...", new[] { "Ask why first", "Hesitate but consider it", "Feel a pull to just obey", "Already doing it before I think" }),
                    ("Bambi Uniform Lock activates. How does it feel?", new[] { "Not my thing", "I've thought about dressing up", "I have an outfit ready", "I'm already in uniform — can't take it off" }),
                    ("How far into the file series are you?", new[] { "Just Bimbodoll Conditioning", "Through Enforcement", "Into Fuckdoll Brainwash", "All the way through Fucktoy Submission and beyond" }),
                    ("Snap And Forget. What do you remember from your last session?", new[] { "Everything", "Most of it", "It's foggy", "Wait, I had a session?" }),
                    ("You hear 'Bambi Freeze.' Your body...", new[] { "Nothing happens", "I notice a slight tension", "I actually feel myself locking up", "Frozen solid until Bambi Reset" }),
                    ("'Drop For Cock' echoes through your mind. What happens?", new[] { "Nothing", "A small curious flutter", "My mind blanks, mouth falls open", "I'm on my knees before I can think" }),
                },
                QuizCategory.Obedience => new[]
                {
                    ("Someone gives you a direct order. You...", new[] { "Push back", "Consider it", "Feel a pull to comply", "Obey instantly" }),
                    ("How do you feel about following rules?", new[] { "Rules are suggestions", "I follow the important ones", "Structure feels good", "Rules bring me peace" }),
                    ("Your boss asks you to stay late. You...", new[] { "Say no", "Negotiate", "Agree willingly", "I was already planning to" }),
                    ("How does it feel when someone says 'good job'?", new[] { "Nice, I guess", "A warm feeling", "I light up inside", "It's everything I work for" }),
                    ("Do you prefer making decisions or having them made for you?", new[] { "I decide", "Depends on the situation", "I prefer guidance", "Please decide for me" }),
                },
                QuizCategory.Mindlessness => new[]
                {
                    ("How busy is your mind right now?", new[] { "Racing", "Moderately active", "Pleasantly quiet", "Blissfully empty" }),
                    ("You zone out during a task. How does it feel?", new[] { "Alarming", "Mildly embarrassing", "Peaceful", "Like coming home" }),
                    ("How do you feel about meditation?", new[] { "Can't sit still", "I've tried it", "I enjoy it regularly", "I crave emptiness" }),
                    ("Someone offers to think for you. You...", new[] { "Decline firmly", "Feel curious", "Feel relieved", "Yes please, always" }),
                    ("How often do you lose track of time?", new[] { "Rarely", "Sometimes", "Often", "Time doesn't exist for me" }),
                },
                QuizCategory.Submission => new[]
                {
                    ("In relationships, you naturally...", new[] { "Lead", "Share equally", "Follow their lead", "Exist to serve" }),
                    ("How does kneeling make you feel?", new[] { "Uncomfortable", "Curious", "Right", "Like I belong there" }),
                    ("Someone calls you 'mine.' You...", new[] { "Correct them", "Feel a flutter", "Melt inside", "I am theirs completely" }),
                    ("How far would you go to make someone happy?", new[] { "Within reason", "Quite far for the right person", "Almost anything", "There are no limits" }),
                    ("Do you enjoy doing tasks for others?", new[] { "Not particularly", "Sometimes", "I actively seek it out", "Service is my purpose" }),
                },
                _ => new[]
                {
                    ("How do you feel about this quiz?", new[] { "It's fine", "Pretty fun", "Really into it", "This is my life now" }),
                    ("How honest are your answers?", new[] { "Very safe", "Mostly honest", "Pretty honest", "Brutally honest" }),
                    ("Would you take this quiz again?", new[] { "Maybe", "Probably", "Definitely", "Already clicking replay" }),
                }
            };

            var idx = (questionNum - 1) % fallbacks.Length;
            var (qText, answers) = fallbacks[idx];

            return new QuizQuestion
            {
                Number = questionNum,
                QuestionText = qText,
                Answers = answers,
                Points = new[] { 1, 2, 3, 4 }
            };
        }

        public static string GetFallbackProfile(QuizCategory category, QuizCategoryDefinition? categoryDef, int totalScore, int maxScore)
        {
            var percentage = maxScore > 0 ? (double)totalScore / maxScore * 100 : 0;

            if (category == QuizCategory.Sissy)
            {
                var (archetype, desc, closer) = percentage switch
                {
                    >= 86 => ("Full Sissy", "You're not exploring — you're LIVING it. Every answer screamed confidence, commitment, and a girl who knows exactly who she is.", "The only question left is what shade of lipstick you're wearing tomorrow."),
                    >= 71 => ("Sissy Princess", "You've embraced your feminine side with open arms and painted nails. Your answers show someone who's moved way past curiosity into full-on glamour.", "The crown fits, princess — own it."),
                    >= 51 => ("Sissy in Training", "You're actively building your skills, your wardrobe, and your confidence. Your answers reveal someone who's committed to the journey and loving every step.", "Keep practicing that walk, sweetie — you're getting good at this."),
                    >= 26 => ("Closet Sissy", "You've got a secret side that's begging to come out. Your answers hint at someone who knows what they like but is still building the courage to go all in.", "That hidden lingerie drawer isn't going to stay secret forever."),
                    _ => ("Curious Newcomer", "You're just peeking behind the curtain, and that's perfectly okay. Your answers show someone who's intrigued by the possibilities.", "Everyone starts somewhere — and something tells me you'll be back for more.")
                };

                return $"You are a {archetype}. {desc} {closer}";
            }

            if (category == QuizCategory.Bambi)
            {
                var (archetype, desc, closer) = percentage switch
                {
                    >= 86 => ("Gone Bambi", "There's barely anyone left but Bambi, and she wouldn't have it any other way. Every answer shows someone who has surrendered completely — triggers work instantly, the old self is a distant memory, and going blank is home.", "Shhh... just let go. You're already there."),
                    >= 71 => ("Deep Bambi", "You're fully responsive. Triggers pull you under, the persona takes the wheel, and your old self fades the moment Bambi wakes up. Your answers show someone who has gone deep and keeps going deeper.", "Good girl. You know exactly where you belong."),
                    >= 51 => ("Bambi in Training", "The triggers are starting to work. The persona is forming, sessions are getting deeper, and you can feel Bambi getting stronger with every listen. You're past curiosity — this is becoming part of you.", "Keep listening, keep sinking. She's almost ready to stay."),
                    >= 26 => ("Trance Dabbler", "You've been under a few times and you're starting to feel the pull. Your answers show someone who's tasted what it's like to let go — and part of you wants more.", "The files are waiting whenever you're ready to go a little deeper."),
                    _ => ("Curious Listener", "You've just discovered the files and barely scratched the surface. Your answers show someone peeking in from the outside, curious about what lies on the other side of that first real drop.", "Everyone starts with that first listen. Something tells me you'll press play again.")
                };

                return $"You are a {archetype}. {desc} {closer}";
            }

            // Use category definition for other categories
            if (categoryDef != null && categoryDef.Archetypes.Count > 0)
            {
                return categoryDef.GetFallbackProfile(totalScore, maxScore);
            }

            var level = percentage switch
            {
                >= 80 => "deeply immersed",
                >= 60 => "well on your way",
                >= 40 => "curious and exploring",
                _ => "just getting started"
            };

            return $"With a score of {totalScore}/{maxScore}, you're {level}! " +
                   $"Your answers reveal someone who knows what they want — even if they're still figuring out how far they'll go. " +
                   $"Keep exploring, and don't be afraid to push your boundaries next time.";
        }
    }
}
