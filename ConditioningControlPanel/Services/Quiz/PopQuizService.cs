using System;
using System.Linq;
using System.Windows;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Service that shows pop-up reinforcement quiz questions during sessions.
    /// All answers are "correct" — pure positive reinforcement. Scheduling, rate and the
    /// defer/drop policy live in Core's <see cref="PopQuizScheduler"/>; this is the WPF host.
    /// </summary>
    public class PopQuizService : IPopQuizHost, IDisposable
    {
        private readonly PopQuizScheduler _scheduler;
        private readonly Random _random = new();

        public PopQuizService() => _scheduler = new PopQuizScheduler(this);

        public bool IsRunning => _scheduler.IsRunning;

        public void Start() => _scheduler.Start();

        public void Stop() => _scheduler.Stop();

        public void ShowPopQuiz(bool isTest = false, bool isDeferredReplay = false) =>
            DispatcherHelper.RunOnUISync(() => _scheduler.Show(isTest, isDeferredReplay));

        public void TestPopQuiz() => ShowPopQuiz(isTest: true);

        public void Dispose() => _scheduler.Dispose();

        bool IPopQuizHost.IsQuizOpen => Application.Current.Windows.OfType<PopQuizWindow>().Any();

        // #763: both this window and a lock card are ownerless HWND_TOPMOST covers.
        bool IPopQuizHost.IsLockCardOpen => LockCardWindow.IsAnyOpen();

        bool IPopQuizHost.IsInteractionBusy =>
            App.InteractionQueue != null
            && App.InteractionQueue.CurrentInteraction != InteractionQueueService.InteractionType.PopQuiz
            && !App.InteractionQueue.CanStart;

        bool IPopQuizHost.Defer(Action replay)
        {
            if (App.InteractionQueue == null) return false;
            App.InteractionQueue.TryStart(
                InteractionQueueService.InteractionType.PopQuiz,
                () => DispatcherHelper.RunOnUISync(replay),
                queue: true);
            return true;
        }

        // Slot-guarded: the replay is dispatched asynchronously, so a panic/ForceReset and a fresh
        // claim can land in between - an unconditional Complete would clear whatever is current (#462).
        void IPopQuizHost.DropDeferred() =>
            App.InteractionQueue?.CompleteIfCurrent(InteractionQueueService.InteractionType.PopQuiz);

        void IPopQuizHost.Open(bool isTest)
        {
            try
            {
                if (App.InteractionQueue?.CurrentInteraction != InteractionQueueService.InteractionType.PopQuiz)
                {
                    App.InteractionQueue?.TryStart(
                        InteractionQueueService.InteractionType.PopQuiz,
                        () => { },
                        queue: false);
                }

                // Pick a random question from the pool as the ACTIVE MOD sees it.
                var pool = PopQuizScheduler.ResolveQuestionPool(
                    App.Mods?.GetQuizPraiseOverride(),
                    App.Mods?.GetQuizObedienceQuestionOverride(),
                    App.Mods?.GetQuizPraiseHeardQuestionOverride());
                var question = pool[_random.Next(pool.Length)];
                var window = new PopQuizWindow(question, isTest);
                // Don't set Owner — WPF ties owned window z-order to owner,
                // which fights with our Win32 HWND_TOPMOST positioning
                window.Show();
                if (!isTest) SeasonRecapService.TrackFeature(SeasonFeatureKeys.PopQuiz);

                App.Logger?.Information("Pop Quiz shown: {Question}", question.QuestionText);
            }
            catch (Exception ex)
            {
                App.Logger?.Error("Failed to show pop quiz: {Error}", ex.Message);
                App.InteractionQueue?.Complete(InteractionQueueService.InteractionType.PopQuiz);
            }
        }

        void IPopQuizHost.CloseAll()
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
    }
}
