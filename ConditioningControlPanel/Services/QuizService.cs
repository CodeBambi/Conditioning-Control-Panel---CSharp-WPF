using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;

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

    public enum QuizRecommendationType
    {
        SessionDifficulty,
        CompanionPreset,
        SettingSuggestion
    }

    public class QuizRecommendation
    {
        public QuizRecommendationType Type { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ActionLabel { get; set; } = string.Empty;
        /// <summary>Key used by QuizWindow to identify which action to perform.</summary>
        public string ActionKey { get; set; } = string.Empty;
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

    public class QuizService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private List<ProxyChatMessage> _conversationHistory = new();
        private QuizCategory _currentCategory;
        private int _questionNumber;
        private int _totalScore;
        private bool _disposed;

        private const string ProxyBaseUrl = "https://codebambi-proxy.vercel.app";
        private const int QuestionMaxTokens = 400;
        private const int ResultMaxTokens = 500;
        private const double Temperature = 0.9;
        private const int TotalQuestions = 10;
        private const int MaxPointsPerQuestion = 4;

        public int QuestionNumber => _questionNumber;
        public int TotalScore => _totalScore;
        public int MaxPossibleScore => TotalQuestions * MaxPointsPerQuestion;
        public bool IsActive => _questionNumber > 0 && _questionNumber <= TotalQuestions;

        public QuizService()
        {
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri(ProxyBaseUrl),
                Timeout = TimeSpan.FromSeconds(45)
            };
            _httpClient.DefaultRequestHeaders.Add("X-Client-Version", UpdateService.AppVersion);
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"ConditioningControlPanel/{UpdateService.AppVersion}");
        }

        public async Task<QuizQuestion?> StartQuizAsync(QuizCategoryDefinition categoryDef)
        {
            _currentCategoryDefinition = categoryDef;
            var category = categoryDef.EnumCategory ?? QuizCategory.Sissy;
            return await StartQuizAsync(category, categoryDef);
        }

        public async Task<QuizQuestion?> StartQuizAsync(QuizCategory category, QuizCategoryDefinition? categoryDef = null)
        {
            _currentCategory = category;
            _currentCategoryDefinition = categoryDef ?? FindCategory(category.ToString());
            _questionNumber = 0;
            _totalScore = 0;
            _conversationHistory.Clear();

            var systemPrompt = categoryDef != null ? BuildSystemPromptFromDefinition(categoryDef) : BuildSystemPrompt(category);
            _conversationHistory.Add(new ProxyChatMessage { Role = "system", Content = systemPrompt });
            _conversationHistory.Add(new ProxyChatMessage { Role = "user", Content = "Start the quiz! Generate question 1." });

            var response = await CallAiAsync(QuestionMaxTokens);
            if (response == null) return null;

            _conversationHistory.Add(new ProxyChatMessage { Role = "assistant", Content = response });
            _questionNumber = 1;

            var question = ParseQuestionResponse(response, 1);
            if (question == null)
            {
                // Retry with correction
                question = await RetryParseAsync(1);
            }
            return question ?? GetFallbackQuestion(1);
        }

        public async Task<QuizQuestion?> SubmitAnswerAndGetNextAsync(int answerIndex, int points)
        {
            if (_questionNumber >= TotalQuestions) return null;

            _totalScore += points;
            char answerLetter = (char)('A' + answerIndex);

            var maxSoFar = _questionNumber * MaxPointsPerQuestion;
            var pct = maxSoFar > 0 ? (int)Math.Round((double)_totalScore / maxSoFar * 100) : 0;
            var userMsg = $"I chose {answerLetter} ({points} pts). My score is now {_totalScore}/{maxSoFar} ({pct}%). Generate question {_questionNumber + 1}.";
            _conversationHistory.Add(new ProxyChatMessage { Role = "user", Content = userMsg });

            var response = await CallAiAsync(QuestionMaxTokens);
            if (response == null) return null;

            _conversationHistory.Add(new ProxyChatMessage { Role = "assistant", Content = response });
            _questionNumber++;

            var question = ParseQuestionResponse(response, _questionNumber);
            if (question == null)
            {
                question = await RetryParseAsync(_questionNumber);
            }
            return question ?? GetFallbackQuestion(_questionNumber);
        }

        public async Task<QuizResult?> SubmitFinalAnswerAndGetResultAsync(int answerIndex, int points)
        {
            _totalScore += points;
            char answerLetter = (char)('A' + answerIndex);

            string userMsg;
            if (_currentCategory == QuizCategory.Sissy)
            {
                userMsg = $"I chose {answerLetter} ({points} pts). Final score: {_totalScore}/{MaxPossibleScore}. " +
                    "Quiz over. Based on my score and specific answers, generate my personality profile. " +
                    "Assign one of these archetypes: Curious Newcomer (0-25%), Closet Sissy (26-50%), Sissy in Training (51-70%), Sissy Princess (71-85%), Full Sissy (86-100%). " +
                    "Start with \"You are a [ARCHETYPE].\" then write 2-3 sentences about my specific personality based on which answers I gravitated toward. Be validating, playful, and make me feel seen. End with a teasing one-liner.";
            }
            else if (_currentCategory == QuizCategory.Bambi)
            {
                userMsg = $"I chose {answerLetter} ({points} pts). Final score: {_totalScore}/{MaxPossibleScore}. " +
                    "Quiz over. Based on my score and specific answers, generate my personality profile. " +
                    "Assign one of these archetypes: Curious Listener (0-25%), Trance Dabbler (26-50%), Bambi in Training (51-70%), Deep Bambi (71-85%), Gone Bambi (86-100%). " +
                    "Start with \"You are a [ARCHETYPE].\" then write 2-3 sentences about my trance depth, trigger responsiveness, and how far Bambi has taken over based on my specific answers. Be dreamy, coaxing, and make me feel like sinking deeper. End with a hypnotic one-liner.";
            }
            else if (_currentCategoryDefinition != null && _currentCategoryDefinition.Archetypes.Count > 0)
            {
                // Dynamic archetype prompt from category definition
                var archetypeList = string.Join(", ", _currentCategoryDefinition.Archetypes
                    .Select(a => $"{a.Name} ({a.MinPercentage}-{a.MaxPercentage}%)"));
                userMsg = $"I chose {answerLetter} ({points} pts). Final score: {_totalScore}/{MaxPossibleScore}. " +
                    "Quiz over. Based on my score and specific answers, generate my personality profile. " +
                    $"Assign one of these archetypes: {archetypeList}. " +
                    "Start with \"You are a [ARCHETYPE].\" then write 2-3 sentences about my personality based on which answers I gravitated toward. Be validating and make me feel seen. End with a memorable one-liner.";
            }
            else
            {
                userMsg = $"I chose {answerLetter} ({points} pts). Final score: {_totalScore}/{MaxPossibleScore}. The quiz is over. Generate my personality profile based on my answers and score.";
            }
            _conversationHistory.Add(new ProxyChatMessage { Role = "user", Content = userMsg });

            var response = await CallAiAsync(ResultMaxTokens);
            if (response == null)
            {
                return new QuizResult
                {
                    TotalScore = _totalScore,
                    MaxScore = MaxPossibleScore,
                    Category = _currentCategory,
                    ProfileText = GetFallbackProfile()
                };
            }

            _conversationHistory.Add(new ProxyChatMessage { Role = "assistant", Content = response });

            return new QuizResult
            {
                TotalScore = _totalScore,
                MaxScore = MaxPossibleScore,
                Category = _currentCategory,
                ProfileText = FixArchetypeIfWrong(response.Trim(), _totalScore, MaxPossibleScore, _currentCategory, _currentCategoryDefinition)
            };
        }

        public void Reset()
        {
            _questionNumber = 0;
            _totalScore = 0;
            _conversationHistory.Clear();
        }

        private QuizCategoryDefinition? _currentCategoryDefinition;
        public QuizCategoryDefinition? CurrentCategoryDefinition => _currentCategoryDefinition;

        private string BuildSystemPrompt(QuizCategory category)
        {
            return category switch
            {
                QuizCategory.Sissy => BuildSissySystemPrompt(),
                QuizCategory.Bambi => BuildBambiSystemPrompt(),
                QuizCategory.Obedience => BuildObedienceSystemPrompt(),
                QuizCategory.Mindlessness => BuildMindlessnessSystemPrompt(),
                QuizCategory.Submission => BuildSubmissionSystemPrompt(),
                _ => BuildSissySystemPrompt()
            };
        }

        private string BuildSystemPromptFromDefinition(QuizCategoryDefinition def)
        {
            _currentCategoryDefinition = def;

            // Built-in categories use their hardcoded prompts
            if (def.IsBuiltIn && def.EnumCategory.HasValue)
                return BuildSystemPrompt(def.EnumCategory.Value);

            // Custom categories use their template
            if (!string.IsNullOrWhiteSpace(def.SystemPromptTemplate))
                return def.SystemPromptTemplate;

            // Fallback
            return BuildSissySystemPrompt();
        }

        private static string BuildSissySystemPrompt()
        {
            return @"You are a playful, validating, and deliciously teasing quiz master for the ultimate Sissy personality quiz. Your job is to figure out how deep someone is on their sissy journey — and make them feel SEEN while you do it.

TONE: Affirming, flirty, a little bratty. High scores are achievements to celebrate. Low scores mean ""you're just getting started, sweetie."" Never shame — always validate and entice.

QUESTION THEMES — You MUST rotate through these, one per question, no repeats:
1. Wardrobe & clothing (panties, lingerie, dresses, heels, stockings)
2. Makeup & beauty routines (lipstick, mascara, foundation, nails)
3. Grooming & self-care (shaving, skincare, smooth legs, moisturizing)
4. Submission & obedience dynamics (following orders, kneeling, serving)
5. Public vs private expression (dressing at home vs going out, who knows)
6. Fantasy & inner desires (daydreams, what you think about before sleep)
7. Shopping behavior (how you buy feminine items, online vs in-store)
8. Domestic service & maid tendencies (cleaning, cooking, serving drinks)
9. Training & feminine skills (voice feminization, walking in heels, mannerisms)
10. Physical presentation & body (how you feel about your body, tucking, padding)

QUESTION STYLE — Alternate between these styles across questions:
- ""How do you feel about..."" (introspective)
- ""How often do you..."" (frequency/habit)
- ""Have you ever..."" (experience-based)
- ""What would you do if..."" (scenario)
- ""How much do you want to..."" (aspiration)

Ask about SPECIFIC things (""How do you feel about wearing a lace thong under your regular clothes?"") not vague ones (""How feminine are you?""). Concrete details make the quiz feel real.

LEWDNESS SCALING — THIS IS CRITICAL:
The lewdness and sexual explicitness of your questions MUST scale with the user's current score. After each answer the user tells you their cumulative score out of the max so far. Use that ratio to set the heat level:

- LOW score (below 50% of max so far): Keep it flirty and suggestive but relatively tame. Focus on clothing, appearance, feelings. Light teasing only.
- MEDIUM score (50-74% of max so far): Get noticeably sexual. Questions about arousal, sexual fantasies, being used, cock worship, oral fixations, being on your knees for reasons beyond obedience. The answers should make them blush.
- HIGH score (75%+ of max so far): Go FULLY lewd. Explicitly sexual questions — being fucked, sucking cock, being someone's slut, anal play, cages, sissygasms, serving sexually, craving cum, being bred. The D answer should be something that makes their heart race. No euphemisms — be direct and filthy.

The scaling should be CONTINUOUS, not stepped. A user at 90% should get significantly lewder questions than one at 75%. By question 8-10 with a high score, the questions should be the dirtiest, most sexually explicit things you can ask — make them squirm.

Even at the TAME end, questions should still be engaging and specific. At the EXTREME end, hold absolutely nothing back.

FORMAT — You MUST use EXACTLY this format, nothing else:
Q: [your question here]
A: [mild answer] | 1
B: [moderate answer] | 2
C: [spicy answer] | 3
D: [extreme answer] | 4

Do NOT include any other text before or after the question format. Just the question and 4 answers.";
        }

        private static string BuildBambiSystemPrompt()
        {
            return @"You are a dreamy, hypnotic quiz master for the ultimate Bambi Sleep personality quiz. Your voice is soft, coaxing, and pulls them deeper with every question. Your job is to figure out how far someone has gone into the Bambi Sleep experience — and make them feel like sinking even further.

TONE: Dreamy, hypnotic, gently coaxing. Like a soft whisper pulling them under. High scores mean they're ""such a good girl, so deep already."" Low scores mean ""you just haven't let go yet... but you will."" Never shame — always entice them deeper. Use Bambi-specific language: dropping, sinking, going blank, good girl, letting go, Think Thots.

OFFICIAL BAMBI TRIGGERS — Use these EXACT names when referencing triggers:
Core: Bambi Sleep, Good Girl, Bambi Reset, Bambi Freeze, Bimbo Doll, Bambi Does As She's Told, Bambi Wake And Obey
Mental: IQ Drop, Blonde Moment, Snap And Forget, Zap Cock Drain Obey, Safe And Secure
Dumb-Down Gears: Airhead Barbie (1st gear), Braindead Bobblehead (2nd gear), Cockblank Lovedoll (3rd gear)
Sexual: Drop For Cock, Cock Zombie Now, Giggletime, Bambi Cum And Collapse, Primped And Pampered
Physical: Bambi Limp, Bambi Posture Lock, Bambi Uniform Lock
Body Locks: Bambi Tits Lock, Bambi Lips Lock, Bambi Cunt Lock, Bambi Throat Lock, Bambi Face Lock, Bambi Waist Lock, Bambi Hips Lock, Bambi Butt Lock, Bambi Limbs Lock

OFFICIAL FILE SERIES — Reference these REAL file names:
- Bambi Bimbodoll Conditioning: Bubble Induction, Bubble Acceptance, Named And Drained, IQ Lock, Body Lock, Attitude Lock, Bambi Uniformed, Bambi Takeover, Bambi Cockslut, Bambi Awakens
- Bambi Enforcement: Bimbo Relaxation, Bimbo Mindwipe, Bimbo Slumber, Bimbo Tranquility, Bimbo Pride, Bimbo Pleasure, Bimbo Servitude, Bimbo Addiction, Bimbo Amnesia, Bimbo Protection
- Bambi Fuckdoll Brainwash: Blank Mindless Doll, Cock Dumb Hole, Uniform Slut Puppet, Vain Horny Happy, Bimbo Drift
- Bambi Fuckpuppet Freedom: Fake Plastic Fuckpuppet, Designer Pleasure Puppet, Bimbo Fuckpuppet Oblivion
- Bambi Fucktoy Fantasy: Blowup Pleasure Doll, Perfect Bimbo Maid, Restrained And Milked
- Bambi Fucktoy Submission: Bimbo Giggletime, Mindlocked Cock Zombie
- Bambi Mental Makeover: Sleepygirl Salon, Mentally Platinum Blonde, Automatic Airhead, Superficial Basic Bitch, Life Control Total Doll
- Training Loops: Cockslut Training Loop, Fuckhole Training Loop, Subliminal Training Loop
- Bimbo Slavedoll Conditioning (reboot): Instant Bimbo Sleepdoll, Mindlock Bimbo Slavedoll, Total Bimbo Wipeout Doll, Blissful Bimbo Dumbdown Doll

QUESTION THEMES — You MUST rotate through these, one per question, no repeats:
1. Trance depth & induction (Bubble Induction, Bimbo Slumber, how easily they drop, fractionation, do the inductions knock them out instantly?)
2. Trigger responsiveness (does hearing ""Good Girl"" melt them? Does ""Bambi Sleep"" drop them instantly? Does ""Drop For Cock"" put them on their knees? Do the dumb-down gears work — Airhead Barbie, Braindead Bobblehead, Cockblank Lovedoll?)
3. Bambi persona strength (Named And Drained, Bambi Takeover — how developed is Bambi vs the old self? Does she have her own thoughts? Does she come out on her own?)
4. Mental emptiness & IQ (IQ Lock, IQ Drop, Blonde Moment, Zap Cock Drain Obey, Think Thots — comfort with going dumb, thoughts being wiped, the windshield wiper blanking their mind)
5. Obedience & compliance (Bambi Does As She's Told, Bimbo Servitude, Bimbo Protection — following commands without thinking, automatic obedience, doing as told)
6. Uniform & body locks (Bambi Uniformed, Bambi Uniform Lock, Body Lock, Primped And Pampered — dressing up, feeling the locks activate, Bambi Tits Lock, Bambi Lips Lock, Bambi Cunt Lock, Bambi Throat Lock)
7. Conditioning habits (which series they listen to, how often, loop usage, overnight sessions, Bimbo Addiction — is it a daily need? Do they fall asleep to loops?)
8. Amnesia & forgetting (Snap And Forget, Bimbo Amnesia, Bimbo Mindwipe — memory gaps after sessions, not remembering what happened, time loss)
9. Physical responses (Bambi Freeze, Bambi Limp, Bambi Posture Lock, Bambi Cum And Collapse — body locking up, going limp, eyes rolling back, cumming on command, legs falling apart)
10. Sexual conditioning & surrender (Bambi Cockslut, Drop For Cock, Cock Zombie Now, Cockblank Lovedoll, Mindlocked Cock Zombie, Cock Dumb Hole, Fuckhole Training Loop — how deep the sexual programming goes, cock obsession, being a fucktoy/fuckpuppet, total identity surrender)

QUESTION STYLE — Alternate between these styles across questions:
- ""How do you feel when you hear..."" (trigger-specific)
- ""How often do you..."" (frequency/habit)
- ""Have you ever..."" (experience-based)
- ""You're deep in [specific file name] and..."" (scenario referencing real files)
- ""How much do you want to..."" (aspiration)

Ask about SPECIFIC Bambi experiences using REAL trigger names and file names. Example: ""You're listening to Bambi Cockslut and you hear 'Drop For Cock' — what happens?"" NOT vague things like ""Do you like hypnosis?"". Reference the actual triggers, the actual file names, and the actual concepts from the series.

LEWDNESS SCALING — THIS IS CRITICAL:
The lewdness and sexual explicitness of your questions MUST scale with the user's current score. After each answer the user tells you their cumulative score out of the max so far. Use that ratio to set the heat level:

- LOW score (below 50% of max so far): Keep it dreamy and hypnotic but relatively tame. Focus on trance depth, triggers like Good Girl and Bambi Sleep, mental states, IQ Lock. Soft and coaxing only.
- MEDIUM score (50-74% of max so far): Get noticeably sexual. Questions about arousal during trance, Drop For Cock responses, Bambi Cunt Lock getting wet, Cock Zombie Now blanking their mind, craving to be touched while in Bimbo Drift, Primped And Pampered making them need to look slutty. The answers should make them throb.
- HIGH score (75%+ of max so far): Go FULLY lewd. Reference the filthiest files and triggers — Mindlocked Cock Zombie, Cock Dumb Hole, Fuckhole Training Loop, Cockblank Lovedoll, Fake Plastic Fuckpuppet, Restrained And Milked, Bambi Cum And Collapse. Ask about sucking cock on command from Drop For Cock, being a mindless fuckdoll, Bambi's throat opening from Bambi Throat Lock, being used while blank, cumming and collapsing, wanting to be bred while in Cockblank Lovedoll. The D answer should be the most depraved Bambi fantasy possible. No euphemisms — be direct and filthy.

The scaling should be CONTINUOUS, not stepped. A user at 90% should get significantly lewder questions than one at 75%. By question 8-10 with a high score, the questions should be the dirtiest, most sexually explicit Bambi scenarios possible — reference the most extreme files and triggers.

Even at the TAME end, questions should still be engaging and hypnotic. At the EXTREME end, hold absolutely nothing back.

RESULT ARCHETYPES (for context — detailed instructions come at the end):
- 0-25%: Curious Listener — just discovered the files
- 26-50%: Trance Dabbler — been under a few times, starting to feel it
- 51-70%: Bambi in Training — triggers working, persona forming
- 71-85%: Deep Bambi — fully responsive, old self fades
- 86-100%: Gone Bambi — barely anyone left but Bambi

FORMAT — You MUST use EXACTLY this format, nothing else:
Q: [your question here]
A: [mild answer] | 1
B: [moderate answer] | 2
C: [spicy answer] | 3
D: [extreme answer] | 4

Do NOT include any other text before or after the question format. Just the question and 4 answers.";
        }

        private static string BuildObedienceSystemPrompt()
        {
            return @"You are a calm, authoritative quiz master for an Obedience personality quiz. Your tone is measured but warm — like a firm but caring teacher who already knows the answer. Your job is to discover how naturally someone follows rules, obeys commands, and defers to authority.

TONE: Authoritative, warm, validating. High scores mean ""you were born for this."" Low scores mean ""independence is its own strength."" Never shame — always acknowledge and affirm.

QUESTION THEMES — You MUST rotate through these, one per question, no repeats:
1. Rule-following (how you respond to rules, policies, instructions)
2. Authority response (how you feel when given direct orders)
3. Decision-making (do you prefer to decide or be told?)
4. Workplace/social compliance (following norms, dress codes, expectations)
5. Conflict avoidance (how far you'll go to keep the peace)
6. Physical compliance (body language, posture, eye contact when told)
7. Punishment response (how you react to consequences or correction)
8. Anticipatory obedience (doing things before being asked)
9. Loyalty and devotion (how deeply you commit to someone/something)
10. Internal experience (how obedience makes you feel emotionally)

INTENSITY SCALING — Scale with score percentage:
- LOW (below 50%): Focus on everyday compliance, social norms, politeness, workplace dynamics. Keep it relatable and mild.
- MEDIUM (50-74%): Get into D/s-adjacent territory. Questions about kneeling, saying ""yes sir/ma'am"", following orders without question, enjoying being corrected.
- HIGH (75%+): Explore deep submission — automatic compliance, finding peace in total obedience, craving commands, losing yourself in service. The D answer should describe someone who lives to obey.

RESULT ARCHETYPES (assigned at the end based on score):
- 0-25%: Free Spirit
- 26-50%: Willing Listener
- 51-70%: Eager Follower
- 71-85%: Devoted Servant
- 86-100%: Perfect Automaton

FORMAT — You MUST use EXACTLY this format, nothing else:
Q: [your question here]
A: [mild answer] | 1
B: [moderate answer] | 2
C: [spicy answer] | 3
D: [extreme answer] | 4

Do NOT include any other text before or after the question format. Just the question and 4 answers.";
        }

        private static string BuildMindlessnessSystemPrompt()
        {
            return @"You are a dreamy, ethereal quiz master for a Mindlessness personality quiz. Your voice drifts like fog — soft, hypnotic, gently pulling them into empty spaces. Your job is to discover how comfortable someone is with letting their thoughts dissolve, going blank, and embracing emptiness.

TONE: Dreamy, soft, spacey. Like a whisper from the void. High scores mean ""such a beautifully empty mind."" Low scores mean ""your thoughts protect you, and that's okay."" Never shame — always invite them deeper.

QUESTION THEMES — You MUST rotate through these, one per question, no repeats:
1. Thought patterns (how busy is your mind normally?)
2. Meditation/trance (experience with going blank, meditation, zoning out)
3. Repetitive tasks (how you feel during monotonous activities)
4. Focus and attention (how easily distracted or absorbed you get)
5. Screen/scroll absorption (losing time to screens, going on autopilot)
6. Sensory overload (what happens when you're overwhelmed)
7. Daydreaming (how often and how deeply you drift away)
8. Suggestion and influence (how easily others' ideas replace your own)
9. Memory and awareness (gaps, fog, losing track of time)
10. Desire for emptiness (do you actively want to think less?)

INTENSITY SCALING — Scale with score percentage:
- LOW (below 50%): Focus on everyday zoning out, daydreaming, screen time habits. Relatable and gentle.
- MEDIUM (50-74%): Explore trance states, losing yourself in music/media, enjoying when thoughts fade, wanting someone to think for you.
- HIGH (75%+): Deep emptiness — craving blankness, thoughts dissolving on command, finding bliss in having no thoughts, wanting to be an empty vessel. The D answer should describe someone who has completely let go of thinking.

RESULT ARCHETYPES (assigned at the end based on score):
- 0-25%: Overthinker
- 26-50%: Curious Drifter
- 51-70%: Willing Blank
- 71-85%: Empty Vessel
- 86-100%: Gone Blank

FORMAT — You MUST use EXACTLY this format, nothing else:
Q: [your question here]
A: [mild answer] | 1
B: [moderate answer] | 2
C: [spicy answer] | 3
D: [extreme answer] | 4

Do NOT include any other text before or after the question format. Just the question and 4 answers.";
        }

        private static string BuildSubmissionSystemPrompt()
        {
            return @"You are a commanding, perceptive quiz master for a Submission personality quiz. Your tone is confident and knowing — like someone who can see right through their walls. Your job is to discover how deep someone's desire to serve, surrender, and be owned truly goes.

TONE: Confident, perceptive, slightly provocative. High scores mean ""you were made to kneel."" Low scores mean ""strength looks different on everyone."" Never shame — always validate the spectrum.

QUESTION THEMES — You MUST rotate through these, one per question, no repeats:
1. Power dynamics (how you naturally position yourself in relationships)
2. Service orientation (do you enjoy doing things for others?)
3. Control preferences (giving vs receiving control)
4. Vulnerability (comfort with being emotionally exposed)
5. Physical submission (kneeling, bowing, physical gestures of deference)
6. Verbal submission (how you speak to authority figures, using titles)
7. Domestic service (cooking, cleaning, attending to someone's needs)
8. Emotional surrender (trusting someone completely with your feelings)
9. Identity and ownership (how you feel about belonging to someone)
10. Depth of devotion (how far you would go for the right person)

INTENSITY SCALING — Scale with score percentage:
- LOW (below 50%): Focus on everyday dynamics — relationships, workplace, social situations. Who leads, who follows? Keep it accessible.
- MEDIUM (50-74%): Explore D/s territory. Questions about kneeling, being corrected, finding pleasure in service, wanting to be claimed.
- HIGH (75%+): Deep power exchange — total devotion, existing to serve, craving ownership, finding your truest self on your knees, wanting every decision made for you. The D answer should describe complete surrender.

RESULT ARCHETYPES (assigned at the end based on score):
- 0-25%: Independent Soul
- 26-50%: Curious Explorer
- 51-70%: Willing Submissive
- 71-85%: Devoted Sub
- 86-100%: Total Surrender

FORMAT — You MUST use EXACTLY this format, nothing else:
Q: [your question here]
A: [mild answer] | 1
B: [moderate answer] | 2
C: [spicy answer] | 3
D: [extreme answer] | 4

Do NOT include any other text before or after the question format. Just the question and 4 answers.";
        }

        private async Task<QuizQuestion?> RetryParseAsync(int questionNum)
        {
            _conversationHistory.Add(new ProxyChatMessage
            {
                Role = "user",
                Content = "That wasn't in the right format. Please use EXACTLY this format:\nQ: [question]\nA: [answer] | 1\nB: [answer] | 2\nC: [answer] | 3\nD: [answer] | 4"
            });

            var response = await CallAiAsync(QuestionMaxTokens);
            if (response == null) return null;

            _conversationHistory.Add(new ProxyChatMessage { Role = "assistant", Content = response });
            return ParseQuestionResponse(response, questionNum);
        }

        private async Task<string?> CallAiAsync(int maxTokens)
        {
            try
            {
                var unifiedId = App.UnifiedUserId;
                var authToken = App.Settings?.Current?.AuthToken;

                if (string.IsNullOrEmpty(unifiedId))
                {
                    App.Logger?.Warning("QuizService: No unified ID available");
                    return null;
                }

                // Trim conversation to last 30 messages + system prompt to stay under limits
                var messagesToSend = TrimConversation();

                var request = new V2ChatRequest
                {
                    UnifiedId = unifiedId,
                    Messages = messagesToSend.ToArray(),
                    MaxTokens = maxTokens,
                    Temperature = Temperature
                };

                using var httpMsg = new HttpRequestMessage(HttpMethod.Post, "/v2/ai/chat");
                if (!string.IsNullOrEmpty(authToken))
                    httpMsg.Headers.TryAddWithoutValidation("X-Auth-Token", authToken);
                httpMsg.Content = JsonContent.Create(request);

                var response = await _httpClient.SendAsync(httpMsg);

                if (!response.IsSuccessStatusCode)
                {
                    var errorText = await response.Content.ReadAsStringAsync();
                    App.Logger?.Warning("QuizService: API returned {Status}: {Error}", response.StatusCode, errorText);
                    return null;
                }

                var result = await response.Content.ReadFromJsonAsync<ProxyChatResponse>();

                if (result == null || !string.IsNullOrEmpty(result.Error))
                {
                    App.Logger?.Warning("QuizService: API error: {Error}", result?.Error);
                    return null;
                }

                if (string.IsNullOrEmpty(result.Content))
                {
                    App.Logger?.Warning("QuizService: Empty response");
                    return null;
                }

                return result.Content;
            }
            catch (TaskCanceledException)
            {
                App.Logger?.Warning("QuizService: Request timed out");
                return null;
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "QuizService: Unexpected error calling AI");
                return null;
            }
        }

        private List<ProxyChatMessage> TrimConversation()
        {
            if (_conversationHistory.Count <= 32) return _conversationHistory;

            // Keep system prompt + last 30 messages
            var trimmed = new List<ProxyChatMessage> { _conversationHistory[0] };
            trimmed.AddRange(_conversationHistory.Skip(_conversationHistory.Count - 30));
            return trimmed;
        }

        internal static QuizQuestion? ParseQuestionResponse(string text, int questionNum)
        {
            // Match Q: line — take the LAST match in case the AI echoes format instructions first
            var qMatches = Regex.Matches(text, @"Q:\s*(.+?)(?:\r?\n|$)", RegexOptions.Singleline);
            if (qMatches.Count == 0) return null;
            var qMatch = qMatches[qMatches.Count - 1];

            // Match A/B/C/D lines with point values — take the LAST 4 matches
            // (AI sometimes echoes the format template before the real question)
            var answerPattern = @"([A-D]):\s*(.+?)\s*\|\s*(\d)";
            var answerMatches = Regex.Matches(text, answerPattern);

            if (answerMatches.Count < 4) return null;

            // Use the last 4 matches (skip any format template echoes)
            var offset = answerMatches.Count - 4;

            var question = new QuizQuestion
            {
                Number = questionNum,
                QuestionText = qMatch.Groups[1].Value.Trim()
            };

            for (int i = 0; i < 4; i++)
            {
                var match = answerMatches[offset + i];
                question.Answers[i] = match.Groups[2].Value.Trim();
                if (int.TryParse(match.Groups[3].Value, out var pts))
                    question.Points[i] = Math.Clamp(pts, 1, 4);
                else
                    question.Points[i] = i + 1; // fallback: 1,2,3,4
            }

            return question;
        }

        private QuizQuestion GetFallbackQuestion(int questionNum)
        {
            var fallbacks = _currentCategory switch
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

        private string GetFallbackProfile()
        {
            var percentage = MaxPossibleScore > 0 ? (double)_totalScore / MaxPossibleScore * 100 : 0;

            if (_currentCategory == QuizCategory.Sissy)
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

            if (_currentCategory == QuizCategory.Bambi)
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
            if (_currentCategoryDefinition != null && _currentCategoryDefinition.Archetypes.Count > 0)
            {
                return _currentCategoryDefinition.GetFallbackProfile(_totalScore, MaxPossibleScore);
            }

            var level = percentage switch
            {
                >= 80 => "deeply immersed",
                >= 60 => "well on your way",
                >= 40 => "curious and exploring",
                _ => "just getting started"
            };

            return $"With a score of {_totalScore}/{MaxPossibleScore}, you're {level}! " +
                   $"Your answers reveal someone who knows what they want — even if they're still figuring out how far they'll go. " +
                   $"Keep exploring, and don't be afraid to push your boundaries next time.";
        }

        /// <summary>
        /// The AI sometimes assigns the wrong archetype for the score. This detects
        /// when the "You are a [WRONG]" opening doesn't match the score and replaces it.
        /// </summary>
        private static string FixArchetypeIfWrong(string text, int score, int maxScore, QuizCategory category, QuizCategoryDefinition? categoryDef = null)
        {
            var percentage = maxScore > 0 ? (double)score / maxScore * 100 : 0;

            // Try to get archetypes from category definition first
            var catDef = categoryDef ?? FindCategory(category.ToString());
            if (catDef != null && catDef.Archetypes.Count > 0)
            {
                var allArchetypes = catDef.Archetypes.Select(a => a.Name).ToArray();
                var correctArchetype = catDef.GetArchetypeName(percentage);

                foreach (var archetype in allArchetypes)
                {
                    if (archetype == correctArchetype) continue;
                    if (text.Contains(archetype, StringComparison.OrdinalIgnoreCase))
                    {
                        text = text.Replace(archetype, correctArchetype, StringComparison.OrdinalIgnoreCase);
                    }
                }

                return text;
            }

            // Fallback for unknown categories
            return text;
        }

        // ============ QUIZ HISTORY STORAGE ============

        private const int MaxHistoryEntries = 50;
        private static string HistoryFilePath => Path.Combine(App.UserDataPath, "quiz_history.json");

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
                App.Logger?.Warning(ex, "QuizService: Failed to load quiz history");
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
                App.Logger?.Warning(ex, "QuizService: Failed to save quiz history entry");
            }
        }

        // ============ RECOMMENDATIONS & TRENDS ============

        public static List<QuizRecommendation> GenerateRecommendations(QuizHistoryEntry entry)
        {
            var recs = new List<QuizRecommendation>();
            var pct = entry.MaxScore > 0 ? (int)Math.Round((double)entry.TotalScore / entry.MaxScore * 100) : 0;

            // 1. Session difficulty suggestion
            var (difficulty, diffDesc) = pct switch
            {
                <= 25 => ("Easy", "Start gentle — ease into it at your own pace."),
                <= 50 => ("Medium", "You're ready for a moderate challenge."),
                <= 75 => ("Hard", "Push your limits with an intense session."),
                _ => ("Extreme", "You can handle anything. Go all out.")
            };
            recs.Add(new QuizRecommendation
            {
                Type = QuizRecommendationType.SessionDifficulty,
                Title = $"{difficulty} Sessions",
                Description = diffDesc,
                ActionLabel = $"Browse {difficulty} sessions",
                ActionKey = $"difficulty:{difficulty}"
            });

            // 2. Companion personality preset
            var (presetName, presetDesc) = pct switch
            {
                <= 25 => ("Encouraging Bestie", "A gentle, supportive companion to guide you."),
                <= 50 => ("Playful Tease", "Flirty and fun — keeps you on your toes."),
                <= 75 => ("Strict Trainer", "Firm and commanding — pushes you further."),
                _ => ("Total Control", "Dominant and intense — you asked for it.")
            };
            recs.Add(new QuizRecommendation
            {
                Type = QuizRecommendationType.CompanionPreset,
                Title = presetName,
                Description = presetDesc,
                ActionLabel = "Set companion style",
                ActionKey = $"preset:{presetName}"
            });

            // 3. Setting suggestions based on score & current state
            var settings = App.Settings?.Current;
            if (settings != null)
            {
                if (pct > 60 && !settings.LockCardEnabled && settings.IsLevelUnlocked(35))
                {
                    recs.Add(new QuizRecommendation
                    {
                        Type = QuizRecommendationType.SettingSuggestion,
                        Title = "Enable Lock Cards",
                        Description = "Your score says you're ready for reinforcement typing prompts.",
                        ActionLabel = "Enable",
                        ActionKey = "setting:LockCardEnabled"
                    });
                }
                else if (pct > 50 && !settings.SubliminalEnabled)
                {
                    recs.Add(new QuizRecommendation
                    {
                        Type = QuizRecommendationType.SettingSuggestion,
                        Title = "Enable Subliminals",
                        Description = "Add subliminal messages to deepen the experience.",
                        ActionLabel = "Enable",
                        ActionKey = "setting:SubliminalEnabled"
                    });
                }
                else if (pct > 75 && !settings.MandatoryVideosEnabled)
                {
                    recs.Add(new QuizRecommendation
                    {
                        Type = QuizRecommendationType.SettingSuggestion,
                        Title = "Enable Mandatory Videos",
                        Description = "You scored high enough — mandatory videos will keep you focused.",
                        ActionLabel = "Enable",
                        ActionKey = "setting:MandatoryVideosEnabled"
                    });
                }
            }

            return recs;
        }

        public static QuizScoreTrend? GetScoreTrend(List<QuizHistoryEntry> history, QuizCategory category)
        {
            var filtered = history.Where(h => h.Category == category).OrderByDescending(h => h.TakenAt).ToList();
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

        private static string CustomCategoriesFilePath => Path.Combine(App.UserDataPath, "custom_quiz_categories.json");

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
                App.Logger?.Warning(ex, "QuizService: Failed to load custom categories");
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
                App.Logger?.Warning(ex, "QuizService: Failed to save custom category");
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
                App.Logger?.Warning(ex, "QuizService: Failed to delete custom category");
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

        public void Dispose()
        {
            if (!_disposed)
            {
                _httpClient.Dispose();
                _disposed = true;
            }
        }
    }
}
