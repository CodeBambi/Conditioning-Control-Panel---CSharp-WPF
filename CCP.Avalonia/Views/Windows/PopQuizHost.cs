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
        /// <summary>WPF InteractionQueue.IsBusy: another fullscreen interaction holds the slot. On this head
        /// those are a mandatory video and the bubble count game (the lock card has its own check above).</summary>
        public bool IsInteractionBusy => Dispatcher.UIThread.Invoke(OtherInteractionUp);

        /// <summary>Test seam for the two probes.</summary>
        internal static Func<bool> OtherInteractionUp = () => CoreEngine.Video?.IsPlaying == true || BubbleCountWindow.IsAnyOpen();

        private DispatcherTimer? _busyWatch;

        public bool Defer(Action replay)
        {
            _deferred = () => Dispatcher.UIThread.Post(replay);
            // A lock card replays on AllClosed. A video or a bubble count has no such signal here, so the
            // queue is watched: the quiz takes its turn within a second of the slot coming free.
            Dispatcher.UIThread.Post(() =>
            {
                if (_busyWatch != null) return;
                _busyWatch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _busyWatch.Tick += (_, _) => BusyWatchTick();
                _busyWatch.Start();
            });
            return true;
        }

        internal void BusyWatchTick()
        {
            if (_deferred == null) { _busyWatch?.Stop(); _busyWatch = null; return; }
            if (OtherInteractionUp() || LockCardWindow.IsAnyOpen()) return;
            _busyWatch?.Stop();
            _busyWatch = null;
            var r = _deferred; _deferred = null; r?.Invoke();
        }

        internal bool HasDeferred => _deferred != null;

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
                if (!isTest) global::ConditioningControlPanel.Services.SeasonFeatureTracker.TrackFeature(global::ConditioningControlPanel.Models.SeasonFeatureKeys.PopQuiz);   // WPF PopQuizService:260
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
            LockCardWindow.DropHeld();   // first: the last quiz closing would replay a held card
            PopQuizWindow.ForceCloseAll();
        });
    }
}
