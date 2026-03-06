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
        Bambi
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

        public async Task<QuizQuestion?> StartQuizAsync(QuizCategory category)
        {
            _currentCategory = category;
            _questionNumber = 0;
            _totalScore = 0;
            _conversationHistory.Clear();

            var systemPrompt = BuildSystemPrompt(category);
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

            var userMsg = _currentCategory switch
            {
                QuizCategory.Sissy =>
                    $"I chose {answerLetter} ({points} pts). Final score: {_totalScore}/{MaxPossibleScore}. " +
                    "Quiz over. Based on my score and specific answers, generate my personality profile. " +
                    "Assign one of these archetypes: Curious Newcomer (0-25%), Closet Sissy (26-50%), Sissy in Training (51-70%), Sissy Princess (71-85%), Full Sissy (86-100%). " +
                    "Start with \"You are a [ARCHETYPE].\" then write 2-3 sentences about my specific personality based on which answers I gravitated toward. Be validating, playful, and make me feel seen. End with a teasing one-liner.",
                QuizCategory.Bambi =>
                    $"I chose {answerLetter} ({points} pts). Final score: {_totalScore}/{MaxPossibleScore}. " +
                    "Quiz over. Based on my score and specific answers, generate my personality profile. " +
                    "Assign one of these archetypes: Curious Listener (0-25%), Trance Dabbler (26-50%), Bambi in Training (51-70%), Deep Bambi (71-85%), Gone Bambi (86-100%). " +
                    "Start with \"You are a [ARCHETYPE].\" then write 2-3 sentences about my trance depth, trigger responsiveness, and how far Bambi has taken over based on my specific answers. Be dreamy, coaxing, and make me feel like sinking deeper. End with a hypnotic one-liner.",
                _ =>
                    $"I chose {answerLetter} ({points} pts). Final score: {_totalScore}/{MaxPossibleScore}. The quiz is over. Generate my personality profile based on my answers and score."
            };
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
                ProfileText = FixArchetypeIfWrong(response.Trim(), _totalScore, MaxPossibleScore, _currentCategory)
            };
        }

        public void Reset()
        {
            _questionNumber = 0;
            _totalScore = 0;
            _conversationHistory.Clear();
        }

        private string BuildSystemPrompt(QuizCategory category)
        {
            return category switch
            {
                QuizCategory.Sissy => BuildSissySystemPrompt(),
                QuizCategory.Bambi => BuildBambiSystemPrompt(),
                _ => BuildSissySystemPrompt()
            };
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
        private static string FixArchetypeIfWrong(string text, int score, int maxScore, QuizCategory category)
        {
            var percentage = maxScore > 0 ? (double)score / maxScore * 100 : 0;

            string[] allArchetypes;
            string correctArchetype;

            if (category == QuizCategory.Sissy)
            {
                allArchetypes = new[] { "Curious Newcomer", "Closet Sissy", "Sissy in Training", "Sissy Princess", "Full Sissy" };
                correctArchetype = percentage switch
                {
                    >= 86 => "Full Sissy",
                    >= 71 => "Sissy Princess",
                    >= 51 => "Sissy in Training",
                    >= 26 => "Closet Sissy",
                    _ => "Curious Newcomer"
                };
            }
            else if (category == QuizCategory.Bambi)
            {
                allArchetypes = new[] { "Curious Listener", "Trance Dabbler", "Bambi in Training", "Deep Bambi", "Gone Bambi" };
                correctArchetype = percentage switch
                {
                    >= 86 => "Gone Bambi",
                    >= 71 => "Deep Bambi",
                    >= 51 => "Bambi in Training",
                    >= 26 => "Trance Dabbler",
                    _ => "Curious Listener"
                };
            }
            else
            {
                return text;
            }

            // Check if the AI used a wrong archetype
            foreach (var archetype in allArchetypes)
            {
                if (archetype == correctArchetype) continue;
                if (text.Contains(archetype, StringComparison.OrdinalIgnoreCase))
                {
                    text = text.Replace(archetype, correctArchetype, StringComparison.OrdinalIgnoreCase);
                }
            }

            // Also fix percentage ranges if the AI included them (e.g. "(26-50%)" when score is 100%)
            foreach (var range in new[] { "(0-25%)", "(26-50%)", "(51-70%)", "(71-85%)", "(86-100%)" })
            {
                if (text.Contains(range))
                {
                    var correctRange = percentage switch
                    {
                        >= 86 => "(86-100%)",
                        >= 71 => "(71-85%)",
                        >= 51 => "(51-70%)",
                        >= 26 => "(26-50%)",
                        _ => "(0-25%)"
                    };
                    if (range != correctRange)
                        text = text.Replace(range, correctRange);
                }
            }

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
