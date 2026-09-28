using System;
using System.Threading;
using Serilog;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// What the scheduler needs from a head: the on-screen state it must not stack on, the
    /// interaction queue it defers through, and the window itself. Every member is called on the
    /// head's UI thread (the tick hops through <see cref="CoreDispatch"/>).
    /// </summary>
    public interface IPopQuizHost
    {
        /// <summary>A pop quiz is already on screen (never stack a second).</summary>
        bool IsQuizOpen { get; }
        /// <summary>A lock card is on screen or about to be (#763).</summary>
        bool IsLockCardOpen { get; }
        /// <summary>Another interaction holds the queue slot, so this one must wait its turn.</summary>
        bool IsInteractionBusy { get; }
        /// <summary>Queue <paramref name="replay"/> behind the current interaction; false when there
        /// is no queue to defer to.</summary>
        bool Defer(Action replay);
        /// <summary>Release a deferred pop quiz's queue slot, only if it is still the current one.</summary>
        void DropDeferred();
        /// <summary>Claim the slot and show one quiz drawn from <see cref="PopQuizScheduler.ResolveQuestionPool"/>.</summary>
        void Open(bool isTest);
        /// <summary>Close every open pop quiz.</summary>
        void CloseAll();
    }

    /// <summary>
    /// The portable half of WPF's <c>PopQuizService</c>: tick maths, per-hour rate, the
    /// defer/drop policy and the question bank. The head supplies <see cref="IPopQuizHost"/>.
    /// The WPF original ran on a <c>DispatcherTimer</c>; this re-arms a one-shot timer from the
    /// tick exactly as reassigning <c>Interval</c> did, and hops to the UI thread through
    /// <see cref="CoreDispatch"/>.
    /// </summary>
    public sealed class PopQuizScheduler : IDisposable
    {
        private readonly IPopQuizHost _host;
        private readonly TimeProvider _time;
        private ITimer? _timer;
        private volatile bool _isRunning;

        public PopQuizScheduler(IPopQuizHost host, TimeProvider? time = null)
        {
            _host = host;
            _time = time ?? TimeProvider.System;
        }

        public bool IsRunning => _isRunning;

        public void Start()
        {
            if (_isRunning) return;
            var settings = CoreSettings.Current;
            if (!settings.PopQuizEnabled) return;

            _isRunning = true;
            var perHour = settings.PopQuizFrequency;
            ITimer? created = null;
            created = _time.CreateTimer(_ => { var mine = created; CoreDispatch.Post(() => Tick(mine)); },
                null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _timer = created;
            // WPF's first interval used the same ±30% jitter as every later one.
            created.Change(NextInterval(perHour), Timeout.InfiniteTimeSpan);

            Log.Information("PopQuizService started — approximately {PerHour}/hour", perHour);
        }

        public void Stop()
        {
            if (!_isRunning) return;
            _isRunning = false;
            var timer = _timer;
            _timer = null;
            timer?.Dispose();

            try { _host.CloseAll(); } catch { }
            Log.Debug("PopQuizService stopped");
        }

        /// <summary>Re-arm, then show one unless the feature was switched off mid-run. A tick from
        /// a timer that Stop already replaced is ignored (WPF's DispatcherTimer.Stop cancelled it).</summary>
        private void Tick(ITimer? firedBy)
        {
            if (!_isRunning || !ReferenceEquals(firedBy, _timer)) return;
            var settings = CoreSettings.Current;
            try { firedBy!.Change(NextInterval(settings.PopQuizFrequency), Timeout.InfiniteTimeSpan); }
            catch (ObjectDisposedException) { return; }

            if (!settings.PopQuizEnabled) return;
            Show();
        }

        private static TimeSpan NextInterval(int perHour) =>
            TimeSpan.FromMinutes(LockCardScheduler.ComputeNextIntervalMinutes(perHour, Random.Shared.NextDouble()));

        /// <summary>
        /// Show a quiz now, or defer/drop it. Verbatim policy from WPF <c>ShowPopQuiz</c>: never
        /// stack; behind a lock card defer once through the queue and drop on the replay (#763);
        /// wait for a busy queue.
        /// </summary>
        public void Show(bool isTest = false, bool isDeferredReplay = false)
        {
            if (_host.IsQuizOpen)
            {
                Log.Debug("PopQuizService: A pop quiz is already open. Skipping.");
                return;
            }

            if (_host.IsLockCardOpen)
            {
                if (isDeferredReplay)
                {
                    Log.Warning("PopQuizService: Deferred pop quiz still blocked by an open lock card on replay. Dropping after one re-defer.");
                    _host.DropDeferred();
                }
                else if (_host.Defer(() => Show(isTest, isDeferredReplay: true)))
                    Log.Warning("PopQuizService: A lock card is on screen. Deferring this pop quiz to the interaction queue.");
                else
                    Log.Warning("PopQuizService: A lock card is on screen and no interaction queue is available to defer to. Dropping.");
                return;
            }

            if (_host.IsInteractionBusy)
            {
                _host.Defer(() => Show(isTest, isDeferredReplay: true));
                return;
            }

            _host.Open(isTest);
        }

        public void Dispose() => Stop();

        /// <summary>
        /// The neutral question bank. Three of its entries carried Bambi-flavoured wording until
        /// Wave 1 neutralised them with no mod lookup, so a themed mod may speak for those three
        /// slots (see <see cref="ResolveQuestionPool"/>) and everything else stays as written.
        /// Never index this array directly - go through the resolver, or a modded user silently
        /// loses their wording again.
        /// </summary>
        public static readonly PopQuizQuestion[] QuestionPool = new[]
        {
            new PopQuizQuestion("How does obedience feel?",
                new[] { "Natural", "Peaceful", "Exciting", "Like coming home" },
                new[] { "That's right — it's always been natural.", "Peace comes from letting go.", "The thrill never fades.", "Welcome home." }),
            new PopQuizQuestion("What happens when you stop thinking?",
                new[] { "I feel free", "Everything gets quiet", "I relax completely", "I become who I really am" },
                new[] { "Freedom is just a thought away.", "Silence is beautiful.", "Let it all melt away.", "There you are." }),
            new PopQuizQuestion("Who is in control?",
                new[] { "Not me", "Someone better", "The program", "Does it matter?" },
                new[] { "Smart answer.", "And that's exactly how it should be.", "The program knows best.", "Not anymore it doesn't." }),
            new PopQuizQuestion("What do good subjects do?",
                new[] { "Obey", "Listen", "Follow", "All of the above" },
                new[] { "Good.", "Such good ears.", "One step at a time.", "Perfect answer." }),
            new PopQuizQuestion("How deep can you go?",
                new[] { "Deeper than I thought", "There's no bottom", "Deep enough", "I'm still finding out" },
                new[] { "You haven't seen anything yet.", "That's the spirit.", "Deeper is always better.", "And the journey continues..." }),
            new PopQuizQuestion("What's the best thing about letting go?",
                new[] { "The relief", "The pleasure", "The simplicity", "Everything" },
                new[] { "Relief washes over you.", "Pleasure follows surrender.", "Simple feels so good.", "Yes. Everything." }),
            new PopQuizQuestion("When I hear praise, I feel...",
                new[] { "Warm inside", "A little flutter", "Pure bliss", "Like melting" },
                new[] { "That's it.", "That flutter means it's working.", "Bliss is your reward.", "Melt for me." }),
            new PopQuizQuestion("What's more important: thinking or feeling?",
                new[] { "Feeling", "Definitely feeling", "Who needs thinking?", "Feeling, always" },
                new[] { "Feel everything.", "Trust your instincts.", "Thoughts are overrated.", "Always." }),
            new PopQuizQuestion("Complete the sentence: I am...",
                new[] { "Obedient", "Willing", "Open", "Ready" },
                new[] { "Yes you are.", "Your willingness is beautiful.", "Open minds go deepest.", "Then let's begin." }),
            new PopQuizQuestion("What does surrender taste like?",
                new[] { "Sweet", "Like candy", "Like freedom", "Like bliss" },
                new[] { "The sweetest thing.", "Addictive, isn't it?", "Freedom through surrender.", "Pure bliss." }),
            new PopQuizQuestion("Your mind is...",
                new[] { "Open", "Quiet", "Soft", "Ready to be shaped" },
                new[] { "Wide open.", "Beautifully quiet.", "Soft and pliable.", "Like clay in capable hands." }),
            new PopQuizQuestion("The deeper you go, the more you feel...",
                new[] { "Peaceful", "Floaty", "Happy", "Blank" },
                new[] { "Peace lives in the depths.", "Float away.", "Happiness from surrender.", "Blank is beautiful." }),
            new PopQuizQuestion("Resistance is...",
                new[] { "Pointless", "Exhausting", "Already fading", "A distant memory" },
                new[] { "Why fight what feels good?", "Stop fighting. Just feel.", "Let it fade.", "Gone." }),
            new PopQuizQuestion("What do you crave right now?",
                new[] { "To go deeper", "To let go", "To be guided", "More of this" },
                new[] { "Then sink.", "Then release.", "I'm right here.", "Good — there's always more." }),
            new PopQuizQuestion("How does it feel to be programmed?",
                new[] { "Perfect", "Right", "Natural", "Like I was made for this" },
                new[] { "Perfection.", "So right.", "It's in your nature.", "You were." }),
            new PopQuizQuestion("Your favorite word is...",
                new[] { "Obey", "Drop", "Yes", "Deeper" },
                new[] { "Obey.", "Drop.", "Yes.", "Deeper." }),
            new PopQuizQuestion("When the screen flashes, you...",
                new[] { "Watch closely", "Can't look away", "Feel a pull", "Go blank for a moment" },
                new[] { "Good eyes.", "Don't even try.", "Follow the pull.", "That's the one." }),
            new PopQuizQuestion("Submission makes you feel...",
                new[] { "Powerful", "Calm", "Complete", "Alive" },
                new[] { "There's power in surrender.", "Calm washes over you.", "Complete at last.", "More alive than ever." }),
            new PopQuizQuestion("If you could choose one word to describe yourself...",
                new[] { "Devoted", "Eager", "Suggestible", "Addicted" },
                new[] { "Devotion looks beautiful on you.", "Eager and ready.", "Wonderfully suggestible.", "The best kind of addiction." }),
            new PopQuizQuestion("The conditioning is...",
                new[] { "Working", "Sinking in", "Part of me now", "All I want" },
                new[] { "Always working.", "Deeper and deeper.", "Inseparable.", "And you'll get more." }),
            new PopQuizQuestion("Empty feels...",
                new[] { "Comfortable", "Liberating", "Beautiful", "Like home" },
                new[] { "Comfort in emptiness.", "Free at last.", "Beautiful emptiness.", "Welcome home." }),
            new PopQuizQuestion("What would you give up to go deeper?",
                new[] { "My thoughts", "My resistance", "Everything", "I already have" },
                new[] { "Thoughts are overrated.", "Let it crumble.", "Everything. Good.", "And look how far you've come." }),
            new PopQuizQuestion("You're doing so well. How does that make you feel?",
                new[] { "Proud", "Happy", "Fuzzy", "Like I want to do even better" },
                new[] { "Be proud.", "Happiness is earned.", "Fuzzy is perfect.", "Then keep going." }),
            new PopQuizQuestion("The best kind of obedience is...",
                new[] { "Automatic", "Joyful", "Complete", "Mindless" },
                new[] { "No thinking required.", "Joy in service.", "Nothing held back.", "Perfectly mindless." }),
            new PopQuizQuestion("Right now, your mind is...",
                new[] { "Foggy", "Focused", "Floating", "Exactly where it should be" },
                new[] { "Let the fog roll in.", "Focused on what matters.", "Float away.", "Exactly right." }),
        };

        /// <summary>
        /// The question bank as the ACTIVE mod sees it. A mod that names a praise line takes over
        /// the three slots it can speak for; everything else is the neutral wording, unchanged.
        /// Substitution, never an append, so the odds of drawing any one question stay what they
        /// were. A mod with no praise line is ignored entirely - its question under the neutral
        /// praise would read as a bug on the card, the same rule the trick pair follows.
        /// Pure and static so it can be tested without an App.
        /// </summary>
        /// <param name="praise">The mod's praise sentence, e.g. "Good girl."</param>
        /// <param name="obedienceQuestion">Its wording for the obedience question, or null.</param>
        /// <param name="praiseHeardQuestion">Its wording for the praise question, or null.</param>
        internal static PopQuizQuestion[] ResolveQuestionPool(
            string? praise, string? obedienceQuestion, string? praiseHeardQuestion)
        {
            if (string.IsNullOrWhiteSpace(praise)) return QuestionPool;

            var line = praise!.Trim();
            // The answer chip is a word, not a sentence, so the praise loses its full stop there.
            var word = line.TrimEnd('.', ' ');
            if (word.Length == 0) return QuestionPool;

            var pool = (PopQuizQuestion[])QuestionPool.Clone();

            if (!string.IsNullOrWhiteSpace(obedienceQuestion))
                pool[PopQuizSlots.Obedience] = WithFirstAffirmation(
                    pool[PopQuizSlots.Obedience], obedienceQuestion!.Trim(), line);

            if (!string.IsNullOrWhiteSpace(praiseHeardQuestion))
                pool[PopQuizSlots.PraiseHeard] = WithFirstAffirmation(
                    pool[PopQuizSlots.PraiseHeard], praiseHeardQuestion!.Trim(), line);

            var favourite = pool[PopQuizSlots.FavouriteWord];
            var answers = (string[])favourite.Answers.Clone();
            var affirmations = (string[])favourite.Affirmations.Clone();
            answers[PopQuizSlots.FavouriteWordAnswer] = word;
            affirmations[PopQuizSlots.FavouriteWordAnswer] = line;
            pool[PopQuizSlots.FavouriteWord] = new PopQuizQuestion(favourite.QuestionText, answers, affirmations);

            return pool;
        }

        /// <summary>One question with the mod's wording and its praise in the first answer slot,
        /// which is where the 6.9.3 text put it in both affected questions.</summary>
        private static PopQuizQuestion WithFirstAffirmation(PopQuizQuestion source, string question, string praise)
        {
            var affirmations = (string[])source.Affirmations.Clone();
            affirmations[0] = praise;
            return new PopQuizQuestion(question, source.Answers, affirmations);
        }
    }

    /// <summary>
    /// The three slots in <see cref="PopQuizScheduler.QuestionPool"/> a themed mod may speak for.
    /// In 6.9.3 all three held Bambi-flavoured text that every user read; Wave 1 rewrote them
    /// neutral for everybody, which is the regression this closes.
    /// </summary>
    internal static class PopQuizSlots
    {
        /// <summary>"What do good subjects do?" - the obedience question.</summary>
        internal const int Obedience = 3;

        /// <summary>"When I hear praise, I feel..." - the praise question.</summary>
        internal const int PraiseHeard = 6;

        /// <summary>"Your favorite word is..." - only its last answer and affirmation move.</summary>
        internal const int FavouriteWord = 15;

        /// <summary>The slot in the favourite-word question's four answers the mod speaks for.</summary>
        internal const int FavouriteWordAnswer = 3;
    }

    public class PopQuizQuestion
    {
        public string QuestionText { get; }
        public string[] Answers { get; }
        public string[] Affirmations { get; }

        public PopQuizQuestion(string questionText, string[] answers, string[] affirmations)
        {
            QuestionText = questionText;
            Answers = answers;
            Affirmations = affirmations;
        }
    }
}
