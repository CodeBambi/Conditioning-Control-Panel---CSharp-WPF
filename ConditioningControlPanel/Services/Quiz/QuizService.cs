using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.AIService;
using ConditioningControlPanel.Services.Moderation;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Services
{
    // TODO(v6 follow-up): make quiz content mod-aware. Quiz prompts, archetype names, and seed
    // questions throughout this file are Bambi-coded. Deferred from the v6 agnostic refactor —
    // a real content authoring pass, not a code change. Track in its own PR.

    public class QuizService : IDisposable
    {
        /// <summary>
        /// Raised once when a quiz run finalizes (from QuizWindow.ShowResult). Static
        /// because QuizService is a per-window instance with no app-wide handle; a
        /// single-purpose static event lets GamificationBridge subscribe for the
        /// lifetime of the app (mirrors the TutorialEventBus pattern).
        /// </summary>
        public static event EventHandler<QuizCompletedEventArgs>? QuizCompleted;

        /// <summary>Fire the QuizCompleted signal. Called by QuizWindow on finalization.</summary>
        public static void RaiseQuizCompleted(int score, bool passed, bool perfect, string category)
        {
            try { QuizCompleted?.Invoke(null, new QuizCompletedEventArgs { Score = score, Passed = passed, Perfect = perfect, Category = category }); }
            catch (Exception ex) { App.Logger?.Debug("QuizCompleted subscriber error: {Error}", ex.Message); }
        }

        /// <summary>
        /// Raised when a graded run is WALKED OUT OF rather than finished - today only the web
        /// intake can do that (its "are you sure? -> Yes" abort and its own exit affordance,
        /// both before a quiz-result has arrived). The classic quiz has no equivalent: it grades
        /// whatever you gave it. Same static shape and the same reason as <see cref="QuizCompleted"/>.
        /// </summary>
        public static event EventHandler? QuizAbandoned;

        /// <summary>Fire the QuizAbandoned signal. Called by IntakeHostService when a run is quit
        /// before it reports a result.</summary>
        public static void RaiseQuizAbandoned()
        {
            try { QuizAbandoned?.Invoke(null, EventArgs.Empty); }
            catch (Exception ex) { App.Logger?.Debug("QuizAbandoned subscriber error: {Error}", ex.Message); }
        }

        private readonly HttpClient _httpClient;
        private List<ProxyChatMessage> _conversationHistory = new();
        private QuizCategory _currentCategory;
        private int _questionNumber;
        private int _totalScore;
        private bool _disposed;

        // Distinct reference (not null, not interned) returned by CallAiAsync when OUTPUT
        // moderation discarded the model's response. null means a transport/availability
        // failure (network, daily limit, Ollama down) — that still surfaces the real
        // error. A moderation block instead lets question-generation callers serve a
        // deterministic canned question so the quiz keeps going, as documented in
        // CallAiAsync. Before this, a block returned null too and aborted the quiz with a
        // misleading "AI busy / daily limit" message (BUG-cloud-quiz HateSpeech, 2026-06).
        private static readonly string OutputBlocked = new(" quiz-output-blocked ".ToCharArray());

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
            // Safety Sandwich (Layer 2): wrap the assembled quiz system prompt with the
            // hardcoded Preamble/Floor before it's sent to the LLM. Custom category
            // SystemPromptTemplate strings (user-editable in QuizCategoryEditorWindow)
            // are still applied as-is between the Preamble and Floor — they just can't
            // bypass the safety rules. See SafetyComposer.cs.
            systemPrompt = SafetyComposer.Wrap(systemPrompt);
            _conversationHistory.Add(new ProxyChatMessage { Role = "system", Content = systemPrompt });
            _conversationHistory.Add(new ProxyChatMessage { Role = "user", Content = "Start the quiz! Generate question 1." });

            var response = await CallAiAsync(QuestionMaxTokens);
            if (ReferenceEquals(response, OutputBlocked))
            {
                // Output moderation discarded question 1 — keep the quiz alive with a
                // canned question instead of aborting with a misleading transport error.
                _questionNumber = 1;
                var fallback = GetFallbackQuestion(1);
                // Record the served question as the assistant turn so the transcript
                // stays well-formed (user→assistant→user) for the next request.
                _conversationHistory.Add(new ProxyChatMessage { Role = "assistant", Content = SerializeQuestionToWire(fallback) });
                return fallback;
            }
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
            if (ReferenceEquals(response, OutputBlocked))
            {
                // Output moderation discarded this question — advance and serve a canned
                // one so the run continues rather than dying mid-quiz.
                _questionNumber++;
                var fallback = GetFallbackQuestion(_questionNumber);
                // Record the served question as the assistant turn so the next request
                // doesn't go out with two consecutive user messages (history desync).
                _conversationHistory.Add(new ProxyChatMessage { Role = "assistant", Content = SerializeQuestionToWire(fallback) });
                return fallback;
            }
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
            // Custom categories (EnumCategory == null) must never fall into the hardcoded
            // Sissy/Bambi archetype branches — StartQuizAsync collapses their enum to Sissy
            // for internal bookkeeping, so gate the built-in branches on the definition being
            // an actual built-in. Otherwise a custom category emits "Sissy Princess" etc. (#501).
            bool isCustomCategory = _currentCategoryDefinition != null && _currentCategoryDefinition.EnumCategory == null;
            if (!isCustomCategory && _currentCategory == QuizCategory.Sissy)
            {
                userMsg = $"I chose {answerLetter} ({points} pts). Final score: {_totalScore}/{MaxPossibleScore}. " +
                    "Quiz over. Based on my score and specific answers, generate my personality profile. " +
                    "Assign one of these archetypes: Curious Newcomer (0-25%), Closet Sissy (26-50%), Sissy in Training (51-70%), Sissy Princess (71-85%), Full Sissy (86-100%). " +
                    "Start with \"You are a [ARCHETYPE].\" then write 2-3 sentences about my specific personality based on which answers I gravitated toward. Be validating, playful, and make me feel seen. End with a teasing one-liner.";
            }
            else if (!isCustomCategory && _currentCategory == QuizCategory.Bambi)
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
            if (response == null || ReferenceEquals(response, OutputBlocked))
            {
                // Transport failure OR an output-moderation block — either way fall back
                // to the deterministic profile (always correct for the score).
                return new QuizResult
                {
                    TotalScore = _totalScore,
                    MaxScore = MaxPossibleScore,
                    Category = _currentCategory,
                    ProfileText = GetFallbackProfile()
                };
            }

            _conversationHistory.Add(new ProxyChatMessage { Role = "assistant", Content = response });

            var rawProfile = response.Trim();
            var fixedProfile = FixArchetypeIfWrong(rawProfile, _totalScore, MaxPossibleScore, _currentCategory, _currentCategoryDefinition);

            // If the AI assigned the wrong archetype, the entire description text
            // is likely incoherent (describes the wrong personality). Use the
            // deterministic fallback which is always correct for the score.
            var profileText = (fixedProfile != rawProfile) ? GetFallbackProfile() : rawProfile;

            return new QuizResult
            {
                TotalScore = _totalScore,
                MaxScore = MaxPossibleScore,
                Category = _currentCategory,
                ProfileText = profileText
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
            // Sentinel (output blocked) or null (transport failure) → let the caller's
            // `?? GetFallbackQuestion(...)` serve a canned question.
            if (response == null || ReferenceEquals(response, OutputBlocked)) return null;

            _conversationHistory.Add(new ProxyChatMessage { Role = "assistant", Content = response });
            return ParseQuestionResponse(response, questionNum);
        }

        private async Task<string?> CallAiAsync(int maxTokens)
        {
            // Trim conversation to last 30 messages + system prompt to stay under limits.
            var messagesToSend = TrimConversation();

            // [AI-METER] — log-only sizing. The quiz bypasses IAiService entirely, so it has to
            // emit its own line or a whole quiz (11-12+ requests) is invisible to the meter.
            var meterStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var meterInputChars = messagesToSend.Sum(m => m.Content?.Length ?? 0);
            void Meter(string outcome, int outputChars = 0) =>
                AIService.AiMeter.Record(AIService.AiMeter.ProviderQuiz, AIService.AiMeter.PurposeQuiz,
                    meterInputChars, outputChars, meterStopwatch.ElapsedMilliseconds, outcome);

            // Route to whichever provider the user has selected for the companion. The
            // quiz used to be cloud-only, so switching to local Ollama left it stuck on
            // "AI busy / daily limit" even though chat worked (BUG-XQRK4USW5X / #334).
            bool useLocal = App.Settings?.Current?.CompanionPrompt?.UseLocalAi == true;
            string? content = useLocal
                ? await CallLocalAiAsync(messagesToSend)
                : await CallCloudAiAsync(messagesToSend, maxTokens);

            if (string.IsNullOrEmpty(content))
            {
                // Both transports collapse "call failed" and "empty reply" into null and log
                // the specific cause themselves, so the meter can only report `error` here.
                Meter(AIService.AiMeter.OutcomeError);
                return null;
            }

            // OUTPUT MODERATION (Layer 1) — applies to BOTH providers. Discard
            // AI-generated questions / result archetype text that trips the guard.
            // Returning null lets the existing fallback path (deterministic archetype +
            // canned question) take over so the quiz continues without leaking content.
            var guard = App.ModerationGuard;
            if (guard != null)
            {
                var modelHint = useLocal ? "local-quiz" : "cloud-quiz";
                var outputCheck = guard.CheckOutput(content);
                if (!outputCheck.Allow && outputCheck.Category.HasValue)
                {
                    App.ModerationLog?.Record(outputCheck.Category.Value, source: "output", modelHint: modelHint);
                    // Quiz questions are AI-generated OUTPUT, never user-typed input, so
                    // a hit is logged for the compliance record but does NOT escalate
                    // the user-facing Content Policy Notice. The batch is still
                    // discarded fail-closed (return null) so nothing prohibited leaks.
                    App.Logger?.Information("QuizService: output blocked by ModerationGuard (category={Cat})", outputCheck.Category);
                    Meter(AIService.AiMeter.OutcomeRefusedOutput, content.Length);
                    return OutputBlocked;
                }
                if (outputCheck.Allow && outputCheck.Category == ProhibitedCategory.ProfessionalAdvice)
                {
                    App.ModerationLog?.Record(ProhibitedCategory.ProfessionalAdvice, source: "output", modelHint: modelHint);
                }
            }

            Meter(AIService.AiMeter.OutcomeOk, content.Length);
            return content;
        }

        /// <summary>Cloud-proxy transport. Returns raw assistant text or null on failure.</summary>
        private async Task<string?> CallCloudAiAsync(List<ProxyChatMessage> messagesToSend, int maxTokens)
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
                    if (await MergedAccountRecovery.TryHandleAsync(response, errorText)) return null;   // contract D
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
                App.Logger?.Error(ex, "QuizService: Unexpected error calling cloud AI");
                return null;
            }
        }

        /// <summary>
        /// Local Ollama transport — mirrors the companion's local provider so the quiz
        /// honours the same Companion → AI host/model settings. Returns raw assistant
        /// text or null on failure (LocalAiService logs the specific cause).
        /// </summary>
        private async Task<string?> CallLocalAiAsync(List<ProxyChatMessage> messagesToSend)
        {
            var mapped = messagesToSend.Select(m => (role: m.Role ?? "user", content: m.Content ?? string.Empty));
            return await LocalAiService.GetRawChatCompletionAsync(mapped, Temperature);
        }

        private List<ProxyChatMessage> TrimConversation()
        {
            if (_conversationHistory.Count <= 32) return _conversationHistory;

            // Keep system prompt + last 30 messages
            var trimmed = new List<ProxyChatMessage> { _conversationHistory[0] };
            trimmed.AddRange(_conversationHistory.Skip(_conversationHistory.Count - 30));
            return trimmed;
        }

        /// <summary>
        /// Render a question back into the exact wire format the model emits
        /// (Q: / A-D: text | points), so a canned fallback served after an output
        /// block can be recorded as the assistant turn. Without it the blocked turn
        /// leaves the history with two consecutive user messages, which desyncs — and
        /// for strict providers can outright reject — every later question. Round-trips
        /// through <see cref="ParseQuestionResponse"/>.
        /// </summary>
        internal static string SerializeQuestionToWire(QuizQuestion q)
        {
            var lines = new List<string> { $"Q: {q.QuestionText}" };
            for (int i = 0; i < q.Answers.Length && i < 4; i++)
            {
                char letter = (char)('A' + i);
                int pts = (q.Points != null && i < q.Points.Length) ? q.Points[i] : i + 1;
                lines.Add($"{letter}: {q.Answers[i]} | {pts}");
            }
            return string.Join("\n", lines);
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

            // Reject if AI echoed the format template placeholders instead of real answers
            if (question.Answers.Any(a => a.StartsWith("[") && a.EndsWith("]")))
                return null;

            return question;
        }

        private QuizQuestion GetFallbackQuestion(int questionNum) => QuizStore.GetFallbackQuestion(_currentCategory, questionNum);

        private string GetFallbackProfile() =>
            QuizStore.GetFallbackProfile(_currentCategory, _currentCategoryDefinition, _totalScore, MaxPossibleScore);

        /// <summary>
        /// The AI sometimes assigns the wrong archetype for the score. This detects
        /// when the "You are a [WRONG]" opening doesn't match the score and replaces
        /// the archetype name. Any change signals the caller to use a fallback profile.
        /// Also fixes wrong percentage ranges like "(0-25%)" when archetype is correct.
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

                // Fix wrong percentage range in parentheses, e.g. "(0-25%)" → "(71-85%)"
                var correctDef = catDef.Archetypes.FirstOrDefault(a => a.Name == correctArchetype);
                if (correctDef != null)
                {
                    var correctRange = $"({correctDef.MinPercentage}-{correctDef.MaxPercentage}%)";
                    // Match any "(XX-YY%)" pattern near the archetype name
                    text = Regex.Replace(text, @"\(\d+-\d+%\)", correctRange);
                }

                return text;
            }

            // Fallback for unknown categories
            return text;
        }

        // ============ QUIZ HISTORY STORAGE ============

        public static List<QuizHistoryEntry> LoadHistory() => QuizStore.LoadHistory();
        public static void SaveEntry(QuizHistoryEntry entry) => QuizStore.SaveEntry(entry);

        // ============ SESSION CONTENT GENERATION ============

        public async Task<SessionTextContent?> GenerateSessionContentAsync()
        {
            try
            {
                var prompt = @"Based on this quiz, generate content for a personalized conditioning session. Use the exact format below with no extra text:

SESSION_NAME: [A creative 2-5 word session name]
SESSION_DESC: [A 1-sentence description of the session theme]
SUBLIMINAL_1: [short subliminal phrase]
SUBLIMINAL_2: [short subliminal phrase]
SUBLIMINAL_3: [short subliminal phrase]
SUBLIMINAL_4: [short subliminal phrase]
SUBLIMINAL_5: [short subliminal phrase]
SUBLIMINAL_6: [short subliminal phrase]
SUBLIMINAL_7: [short subliminal phrase]
SUBLIMINAL_8: [short subliminal phrase]
SUBLIMINAL_9: [short subliminal phrase]
SUBLIMINAL_10: [short subliminal phrase]
BOUNCING_1: [ALL CAPS bouncing text phrase]
BOUNCING_2: [ALL CAPS bouncing text phrase]
BOUNCING_3: [ALL CAPS bouncing text phrase]
BOUNCING_4: [ALL CAPS bouncing text phrase]
BOUNCING_5: [ALL CAPS bouncing text phrase]
BOUNCING_6: [ALL CAPS bouncing text phrase]
LOCKCARD_1: [typing reinforcement phrase]
LOCKCARD_2: [typing reinforcement phrase]
LOCKCARD_3: [typing reinforcement phrase]
LOCKCARD_4: [typing reinforcement phrase]
LOCKCARD_5: [typing reinforcement phrase]
LOCKCARD_6: [typing reinforcement phrase]
LOCKCARD_7: [typing reinforcement phrase]
LOCKCARD_8: [typing reinforcement phrase]

Make all phrases thematically consistent with the quiz category and the user's score. Subliminals should be 2-5 words. Bouncing text should be 1-3 words in ALL CAPS. Lock card phrases should be short affirmations (4-8 words).";

                _conversationHistory.Add(new ProxyChatMessage { Role = "user", Content = prompt });

                var response = await CallAiAsync(800);
                // Session content is optional; on transport failure or a moderation block
                // return null and let the caller fall back (no canned session text here).
                if (response == null || ReferenceEquals(response, OutputBlocked)) return null;

                _conversationHistory.Add(new ProxyChatMessage { Role = "assistant", Content = response });

                return ParseSessionContent(response);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "QuizService: Failed to generate session content");
                return null;
            }
        }

        private static SessionTextContent? ParseSessionContent(string text)
        {
            var content = new SessionTextContent();

            var nameMatch = Regex.Match(text, @"SESSION_NAME:\s*(.+?)(?:\r?\n|$)");
            if (nameMatch.Success) content.Name = nameMatch.Groups[1].Value.Trim();

            var descMatch = Regex.Match(text, @"SESSION_DESC:\s*(.+?)(?:\r?\n|$)");
            if (descMatch.Success) content.Description = descMatch.Groups[1].Value.Trim();

            for (int i = 1; i <= 10; i++)
            {
                var match = Regex.Match(text, $@"SUBLIMINAL_{i}:\s*(.+?)(?:\r?\n|$)");
                if (match.Success)
                {
                    var phrase = match.Groups[1].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(phrase) && !phrase.StartsWith("["))
                        content.SubliminalPhrases.Add(phrase);
                }
            }

            for (int i = 1; i <= 6; i++)
            {
                var match = Regex.Match(text, $@"BOUNCING_{i}:\s*(.+?)(?:\r?\n|$)");
                if (match.Success)
                {
                    var phrase = match.Groups[1].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(phrase) && !phrase.StartsWith("["))
                        content.BouncingTextPhrases.Add(phrase);
                }
            }

            for (int i = 1; i <= 8; i++)
            {
                var match = Regex.Match(text, $@"LOCKCARD_{i}:\s*(.+?)(?:\r?\n|$)");
                if (match.Success)
                {
                    var phrase = match.Groups[1].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(phrase) && !phrase.StartsWith("["))
                        content.LockCardPhrases.Add(phrase);
                }
            }

            // Require minimum viable content
            if (string.IsNullOrWhiteSpace(content.Name) || content.SubliminalPhrases.Count < 3)
                return null;

            return content;
        }

        // ponytail: offline half (trends, categories, history, fallback content) lives in Core's QuizStore; these keep the public API.
        public static string TrendKey(QuizHistoryEntry h) => QuizStore.TrendKey(h);
        public static string DisplayName(QuizHistoryEntry h) => QuizStore.DisplayName(h);
        public static QuizScoreTrend? GetScoreTrend(List<QuizHistoryEntry> history, string categoryId) => QuizStore.GetScoreTrend(history, categoryId);
        public static List<QuizCategoryDefinition> GetBuiltInCategories() => QuizStore.GetBuiltInCategories();
        public static List<QuizCategoryDefinition> LoadCustomCategories() => QuizStore.LoadCustomCategories();
        public static void SaveCustomCategory(QuizCategoryDefinition category) => QuizStore.SaveCustomCategory(category);
        public static void DeleteCustomCategory(string categoryId) => QuizStore.DeleteCustomCategory(categoryId);
        public static List<QuizCategoryDefinition> GetAllCategories() => QuizStore.GetAllCategories();
        public static QuizCategoryDefinition? FindCategory(string idOrName) => QuizStore.FindCategory(idOrName);

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
