using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using ConditioningControlPanel.Helpers;

namespace ConditioningControlPanel.Services
{
    public class LockCardCompletedEventArgs : EventArgs
    {
        public string Phrase { get; init; } = "";
        public int Mistakes { get; init; }
        public int Repeats { get; init; }
    }

    /// <summary>
    /// Service that manages Lock Card popups
    /// </summary>
    public class LockCardService : IDisposable
    {
        private DispatcherTimer? _timer;
        private Random _random = new();
        private bool _isRunning;
        private bool _isDisposed;
        private DateTime _lastShown = DateTime.MinValue;

        public bool IsRunning => _isRunning;

        /// <summary>
        /// Fires when the user finishes typing all repeats of a real (non-test) lock card.
        /// Subscribers like the avatar use this to trigger AI reactions.
        /// </summary>
        public event EventHandler<LockCardCompletedEventArgs>? LockCardCompleted;

        internal void NotifyCompleted(string phrase, int mistakes, int repeats)
        {
            LockCardCompleted?.Invoke(this, new LockCardCompletedEventArgs
            {
                Phrase = phrase,
                Mistakes = mistakes,
                Repeats = repeats
            });
        }

        public void Start()
        {
            if (_isRunning) return;
            
            var settings = App.Settings.Current;

            if (!settings.LockCardEnabled)
            {
                App.Logger?.Information("LockCardService: Disabled in settings");
                return;
            }
            
            _isRunning = true;
            
            // Calculate interval based on frequency (per hour)
            var perHour = settings.LockCardFrequency;
            var intervalMinutes = 60.0 / perHour;
            
            // Add some randomness (±30%)
            var minInterval = intervalMinutes * 0.7;
            var maxInterval = intervalMinutes * 1.3;
            
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(_random.NextDouble() * (maxInterval - minInterval) + minInterval)
            };
            _timer.Tick += Timer_Tick;
            _timer.Start();
            
            App.Logger?.Information("LockCardService started - approximately {PerHour}/hour", perHour);
        }

        public void Stop()
        {
            if (!_isRunning) return;
            _isRunning = false;
            
            _timer?.Stop();
            _timer = null;
            
            App.Logger?.Information("LockCardService stopped");
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            // Recalculate next interval with randomness
            var settings = App.Settings.Current;
            var perHour = settings.LockCardFrequency;
            var intervalMinutes = 60.0 / perHour;
            var minInterval = intervalMinutes * 0.7;
            var maxInterval = intervalMinutes * 1.3;
            
            if (_timer != null)
            {
                _timer.Interval = TimeSpan.FromMinutes(_random.NextDouble() * (maxInterval - minInterval) + minInterval);
            }
            
            // Check if enabled
            if (!settings.LockCardEnabled) return;
            
            // Show the lock card
            ShowLockCard();
        }

        public void ShowLockCard(string? customPhrase = null, int customRepeats = -1, bool customStrict = false, bool isTest = false, bool isDeferredReplay = false)
        {
            DispatcherHelper.RunOnUISync(() =>
            {
                // Prevent stacking multiple lock cards.
                // Gate on the visible set (IsAnyOpen), NOT Application.Current.Windows: since 6.2.10 the
                // window is keep-alive pooled (dismiss => Hide(), not Close()), so a hidden pooled instance
                // lingers in Application.Current.Windows forever and would block every card after the first.
                if (LockCardWindow.IsAnyOpen())
                {
                    // Short, log-safe snippet of the requested phrase for diagnostics.
                    var phraseSnippet = string.IsNullOrEmpty(customPhrase)
                        ? "(default/random)"
                        : (customPhrase.Length > 40 ? customPhrase.Substring(0, 40) + "..." : customPhrase);

                    // A lock card is already visible. Rather than silently dropping this request
                    // (the AI can emit cards faster than the user types one out, so later cards
                    // used to vanish), defer-and-replay: enqueue it on the interaction queue so it
                    // shows after the current card closes. The queue suppresses duplicate pending
                    // LockCard items, so at most one card is ever pending.
                    if (isDeferredReplay)
                    {
                        // Anti-loop guard: we already deferred once and are the dequeued replay,
                        // yet a card is STILL open (rare close/hide race). Do NOT re-enqueue — the
                        // queue's Complete()/dequeue cycle already set us as the active LockCard, so
                        // re-enqueuing would hold the slot with nothing on screen until the 5-min
                        // stuck backstop, and could bounce indefinitely. Give up after this single
                        // re-defer and release the slot so the queue keeps moving.
                        App.Logger?.Warning("LockCardService: Deferred lock card still blocked on replay (a card is open). Dropping after one re-defer. Phrase: {Phrase}", phraseSnippet);
                        App.InteractionQueue?.Complete(InteractionQueueService.InteractionType.LockCard);
                        return;
                    }

                    if (App.InteractionQueue != null)
                    {
                        App.Logger?.Warning("LockCardService: A lock card is already open. Deferring this one to the interaction queue. Phrase: {Phrase}", phraseSnippet);
                        App.InteractionQueue.TryStart(
                            InteractionQueueService.InteractionType.LockCard,
                            () => ShowLockCard(customPhrase, customRepeats, customStrict, isTest, isDeferredReplay: true),
                            queue: true);
                    }
                    else
                    {
                        App.Logger?.Warning("LockCardService: A lock card is already open and no interaction queue is available to defer to. Dropping. Phrase: {Phrase}", phraseSnippet);
                    }
                    return;
                }

                // Check if another fullscreen interaction is active (video, bubble count)
                // If so, queue this lock card for later
                // Note: If CurrentInteraction is already LockCard, the queue dequeued us — proceed normally
                var alreadyActive = App.InteractionQueue?.CurrentInteraction == InteractionQueueService.InteractionType.LockCard;
                if (!alreadyActive && App.InteractionQueue != null && !App.InteractionQueue.CanStart)
                {
                    App.InteractionQueue.TryStart(
                        InteractionQueueService.InteractionType.LockCard,
                        () => ShowLockCard(customPhrase, customRepeats, customStrict, isTest),
                        queue: true);
                    return;
                }

                try
                {
                    var settings = App.Settings.Current;

                    // Get enabled phrases
                    var enabledPhrases = settings.LockCardPhrases?
                        .Where(p => p.Value)
                        .Select(p => p.Key)
                        .ToList() ?? new List<string>();

                    if (enabledPhrases.Count == 0)
                    {
                        App.Logger?.Warning("LockCardService: No phrases enabled");
                        App.InteractionQueue?.Complete(InteractionQueueService.InteractionType.LockCard);
                        return;
                    }

                    // Notify queue we're starting (skip if queue already set us as active)
                    if (!alreadyActive)
                    {
                        App.InteractionQueue?.TryStart(
                            InteractionQueueService.InteractionType.LockCard,
                            () => { }, // Already executing
                            queue: false);
                    }

                    // Pick a random phrase (or use custom one if AI provided it)
                    var phrase = customPhrase ?? enabledPhrases[_random.Next(enabledPhrases.Count)];
                    var repeats = customRepeats >= 0 ? customRepeats : settings.LockCardRepeats;
                    var strict = customStrict || settings.LockCardStrict;
                    var voice = settings.LockCardVoiceMode;

                    // Show on all monitors with synced input
                    LockCardWindow.ShowOnAllMonitors(phrase, repeats, strict, isTest, voice);

                    _lastShown = DateTime.Now;

                    App.Logger?.Information("Lock Card shown on all monitors - Phrase: {Phrase}", phrase);
                }
                catch (Exception ex)
                {
                    App.Logger?.Error("Failed to show lock card: {Error}", ex.Message);
                    // If ShowOnAllMonitors threw mid-show a half-registered window can linger in
                    // LockCardWindow's visible set, leaving IsAnyOpen() permanently true and silently
                    // skipping every future lock card until the next stop. Force-close clears that set
                    // so the guard can't stay armed with zero visible cards.
                    try { LockCardWindow.ForceCloseAll(); } catch { }
                    App.InteractionQueue?.Complete(InteractionQueueService.InteractionType.LockCard);
                }
            });
        }

        /// <summary>
        /// Manually trigger a test lock card
        /// </summary>
        public void TestLockCard()
        {
            ShowLockCard(isTest: true);
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            
            Stop();
        }
    }
}
