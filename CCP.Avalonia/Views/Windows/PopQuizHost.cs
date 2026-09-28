using System;
using Avalonia.Threading;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// The Avalonia <see cref="IPopQuizHost"/>, after WPF <c>PopQuizService</c>
    /// (ConditioningControlPanel/Services/Quiz/PopQuizService.cs). Every member goes through
    /// Dispatcher.UIThread.Invoke, which runs inline on the UI thread, so it is safe from any caller.
    ///
    /// <para>ponytail: this head has no InteractionQueueService. The queue is one pending replay,
    /// run when the last lock card closes - the only defer the policy needs on a head where the
    /// lock card is the only other interaction. Grow it when a second interaction type ports.</para>
    /// </summary>
    public sealed class PopQuizHost : IPopQuizHost
    {
        public static PopQuizHost Instance { get; } = new();
        public PopQuizScheduler Scheduler { get; }
        private Action? _deferred;
        private readonly Random _random = new();

        private PopQuizHost()
        {
            Scheduler = new PopQuizScheduler(this);
            LockCardWindow.AllClosed += () => { var r = _deferred; _deferred = null; r?.Invoke(); };
        }

        public bool IsQuizOpen => Dispatcher.UIThread.Invoke(PopQuizWindow.IsAnyOpen);
        public bool IsLockCardOpen => Dispatcher.UIThread.Invoke(LockCardWindow.IsAnyOpen);
        public bool IsInteractionBusy => false;

        public bool Defer(Action replay)
        {
            _deferred = () => Dispatcher.UIThread.Post(replay);
            return true;
        }

        public void DropDeferred() => _deferred = null;

        public void Open(bool isTest) => Dispatcher.UIThread.Invoke(() =>
        {
            _deferred = null;
            try
            {
                var mods = App.Mods;
                var pool = PopQuizScheduler.ResolveQuestionPool(
                    mods?.GetQuizPraiseOverride(),
                    mods?.GetQuizObedienceQuestionOverride(),
                    mods?.GetQuizPraiseHeardQuestionOverride());
                var question = pool[_random.Next(pool.Length)];
                new PopQuizWindow(question, isTest).Show();
                // ponytail: WPF also calls SeasonRecapService.TrackFeature(PopQuiz); that service is head-side.
                Log.Information("Pop Quiz shown: {Question}", question.QuestionText);
            }
            catch (Exception ex)
            {
                Log.Error("Failed to show pop quiz: {Error}", ex.Message);
            }
        });

        public void CloseAll() => Dispatcher.UIThread.Invoke(() =>
        {
            _deferred = null;
            PopQuizWindow.ForceCloseAll();
        });
    }
}
