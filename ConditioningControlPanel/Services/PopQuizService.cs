using System;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Service that shows pop-up reinforcement quiz questions during sessions.
    /// Follows the same scheduling pattern as LockCardService.
    /// All answers are "correct" — pure positive reinforcement.
    /// </summary>
    public class PopQuizService : IDisposable
    {
        private DispatcherTimer? _timer;
        private readonly Random _random = new();
        private bool _isRunning;
        private bool _isDisposed;

        public bool IsRunning => _isRunning;

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
            new PopQuizQuestion("What do good girls do?",
                new[] { "Obey", "Listen", "Follow", "All of the above" },
                new[] { "Good girl.", "Such good ears.", "One step at a time.", "Perfect answer." }),
            new PopQuizQuestion("How deep can you go?",
                new[] { "Deeper than I thought", "There's no bottom", "Deep enough", "I'm still finding out" },
                new[] { "You haven't seen anything yet.", "That's the spirit.", "Deeper is always better.", "And the journey continues..." }),
            new PopQuizQuestion("What's the best thing about letting go?",
                new[] { "The relief", "The pleasure", "The simplicity", "Everything" },
                new[] { "Relief washes over you.", "Pleasure follows surrender.", "Simple feels so good.", "Yes. Everything." }),
            new PopQuizQuestion("When I hear 'good girl,' I feel...",
                new[] { "Warm inside", "A little flutter", "Pure bliss", "Like melting" },
                new[] { "Good girl.", "That flutter means it's working.", "Bliss is your reward.", "Melt for me." }),
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
                new[] { "Obey", "Drop", "Yes", "Good girl" },
                new[] { "Obey.", "Drop.", "Yes.", "Good girl." }),
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

        public void Start()
        {
            if (_isRunning) return;

            var settings = App.Settings?.Current;
            if (settings == null) return;

            if (!settings.PopQuizEnabled) return;

            _isRunning = true;

            var perHour = settings.PopQuizFrequency;
            var intervalMinutes = 60.0 / perHour;

            // Add randomness (±30%)
            var minInterval = intervalMinutes * 0.7;
            var maxInterval = intervalMinutes * 1.3;

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(_random.NextDouble() * (maxInterval - minInterval) + minInterval)
            };
            _timer.Tick += Timer_Tick;
            _timer.Start();

            App.Logger?.Information("PopQuizService started — approximately {PerHour}/hour", perHour);
        }

        public void Stop()
        {
            if (!_isRunning) return;
            _isRunning = false;

            _timer?.Stop();
            _timer = null;

            // Close any open quiz windows
            CloseAllQuizWindows();

            App.Logger?.Information("PopQuizService stopped");
        }

        private static void CloseAllQuizWindows()
        {
            try
            {
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    foreach (var win in Application.Current.Windows.OfType<PopQuizWindow>().ToList())
                        win.Close();
                });
            }
            catch { }
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            // Recalculate next interval with randomness
            var settings = App.Settings?.Current;
            if (settings == null) return;

            var perHour = settings.PopQuizFrequency;
            var intervalMinutes = 60.0 / perHour;
            var minInterval = intervalMinutes * 0.7;
            var maxInterval = intervalMinutes * 1.3;

            if (_timer != null)
            {
                _timer.Interval = TimeSpan.FromMinutes(_random.NextDouble() * (maxInterval - minInterval) + minInterval);
            }

            if (!settings.PopQuizEnabled) return;

            ShowPopQuiz();
        }

        public void ShowPopQuiz(bool isTest = false)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                // Prevent stacking
                if (Application.Current.Windows.OfType<PopQuizWindow>().Any())
                {
                    App.Logger?.Debug("PopQuizService: A pop quiz is already open. Skipping.");
                    return;
                }

                // Check interaction queue
                var alreadyActive = App.InteractionQueue?.CurrentInteraction == InteractionQueueService.InteractionType.PopQuiz;
                if (!alreadyActive && App.InteractionQueue != null && !App.InteractionQueue.CanStart)
                {
                    App.InteractionQueue.TryStart(
                        InteractionQueueService.InteractionType.PopQuiz,
                        () => ShowPopQuiz(),
                        queue: true);
                    return;
                }

                try
                {
                    // Notify queue we're starting
                    if (!alreadyActive)
                    {
                        App.InteractionQueue?.TryStart(
                            InteractionQueueService.InteractionType.PopQuiz,
                            () => { },
                            queue: false);
                    }

                    // Pick a random question
                    var question = QuestionPool[_random.Next(QuestionPool.Length)];
                    var window = new PopQuizWindow(question, isTest);
                    var mainWin = Application.Current.MainWindow;
                    if (mainWin != null && mainWin.IsVisible)
                        window.Owner = mainWin;
                    window.Show();
                    window.Activate();

                    App.Logger?.Information("Pop Quiz shown: {Question}", question.QuestionText);
                }
                catch (Exception ex)
                {
                    App.Logger?.Error("Failed to show pop quiz: {Error}", ex.Message);
                    App.InteractionQueue?.Complete(InteractionQueueService.InteractionType.PopQuiz);
                }
            });
        }

        public void TestPopQuiz()
        {
            ShowPopQuiz(isTest: true);
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            Stop();
        }
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
