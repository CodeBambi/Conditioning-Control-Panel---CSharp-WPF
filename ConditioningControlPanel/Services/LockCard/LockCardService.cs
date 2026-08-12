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

        // Per-session no-repeat rotation (mirrors BarkService's _recentlySpoken idiom, but in-memory
        // only — lock-card rotation deliberately does NOT persist to disk). Avoids replaying any of
        // the last few phrases so a pure random draw can't repeat the same phrase back-to-back.
        private readonly Queue<string> _recentPhrases = new();
        private readonly HashSet<string> _recentPhrasesSet = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>How many distinct just-shown phrases to avoid replaying.</summary>
        private const int RecentPhrasesMemory = 3;

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

        /// <param name="windowMinutes">
        /// #736: how long the caller expects to keep the service running — a session's remaining
        /// minutes. When supplied, the first card is guaranteed to land inside that window with
        /// room to complete it. Null (dashboard use) means open-ended.
        /// </param>
        public void Start(double? windowMinutes = null)
        {
            if (_isRunning) return;

            var settings = App.Settings.Current;

            if (!settings.LockCardEnabled)
            {
                App.Logger?.Information("LockCardService: Disabled in settings");
                return;
            }

            _isRunning = true;

            var perHour = Math.Max(1, settings.LockCardFrequency);
            var firstDelay = ComputeFirstCardDelayMinutes(perHour, windowMinutes, _random.NextDouble());

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(firstDelay)
            };
            _timer.Tick += Timer_Tick;
            _timer.Start();

            App.Logger?.Information(
                "LockCardService started - approximately {PerHour}/hour, first card in {First:F1}min (window {Window})",
                perHour, firstDelay, windowMinutes is > 0 ? $"{windowMinutes.Value:F1}min" : "open-ended");
        }

        /// <summary>
        /// Stop the scheduler. <paramref name="dismissOpenCards"/> additionally tears down a card that
        /// is already on screen.
        /// </summary>
        /// <param name="dismissOpenCards">
        /// #875: dismiss the visible card FIRST, before the not-running early-return. A card can be on
        /// screen with the scheduler stopped — every ad-hoc surface shows one that way (voice command,
        /// the dashboard Test button, Deeper, MantraLockScreenCommand, remote trigger_lock_card). The
        /// panic key reaches an ad-hoc card only through MainWindow.StopAdHocEffects -> this Stop(), so
        /// the early-return made panic a silent no-op and left the user staring at a card nothing could
        /// close.
        ///
        /// It stays opt-in because most Stop() callers are not an escape at all: pausing a session,
        /// un-ticking the lock-card feature, applying a preset. Dismissing there would walk straight
        /// through strict mode and forfeit the XP of a card the user was mid-way through typing — a
        /// feature toggle must never be a back door out of a lock. Only panic and genuine
        /// kill-everything paths pass true. ForceCloseAll is idempotent, so true with no card up is a
        /// no-op.
        /// </param>
        public void Stop(bool dismissOpenCards = false)
        {
            if (dismissOpenCards)
            {
                try { LockCardWindow.ForceCloseAll(); } catch { }
            }

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
            var perHour = Math.Max(1, settings.LockCardFrequency);
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

        /// <summary>#736: delay before the FIRST lock card of a run, in minutes. Pure so the
        /// reachability guarantee is unit-testable without a dispatcher.
        ///
        /// The first card is an OFFSET into the opening interval, not a whole inter-arrival gap.
        /// Scheduling it at 60/freq ±30% (as before) put the earliest possible card at 1/hour at
        /// minute 42, so a 30-minute session could never produce one — which hard-blocked every
        /// program day whose task required a lock card. Subsequent cards keep the ±30% spacing in
        /// <see cref="Timer_Tick"/>.
        ///
        /// When <paramref name="windowMinutes"/> is supplied the card is additionally clamped to
        /// land inside it, leaving the tail free so the user can actually complete the card.
        /// </summary>
        /// <param name="perHour">Cards per hour; values below 1 are treated as 1.</param>
        /// <param name="windowMinutes">Minutes the service will keep running, or null for open-ended.</param>
        /// <param name="roll">A uniform random sample in [0,1).</param>
        internal static double ComputeFirstCardDelayMinutes(int perHour, double? windowMinutes, double roll)
        {
            var intervalMinutes = 60.0 / Math.Max(1, perHour);

            var maxFirst = intervalMinutes;
            if (windowMinutes is > 0)
                maxFirst = Math.Min(maxFirst, windowMinutes.Value * 0.8);

            return roll * maxFirst;
        }

        /// <summary>Decision for what to do when <see cref="ShowLockCard"/> finds another fullscreen
        /// interaction already visible — a lock card (#676) or a pop quiz (#763). Pure so the
        /// defer-and-replay policy — including the one-re-defer cap that stops a close/hide race from
        /// bouncing forever — is unit-testable without any WPF windows.</summary>
        internal enum BlockedCardAction
        {
            /// <summary>No card open; show this one now.</summary>
            Proceed,
            /// <summary>A card is open; enqueue this request to replay after it closes.</summary>
            Defer,
            /// <summary>We are the dequeued replay and a card is STILL open: give up (one re-defer max)
            /// and release the interaction slot so the queue keeps moving.</summary>
            DropAfterReDefer,
            /// <summary>A card is open but there is no interaction queue to defer to; drop.</summary>
            DropNoQueue
        }

        /// <summary>#676: AI cards can arrive faster than the user types one out. Rather than silently
        /// dropping a request that lands while a card is open, defer-and-replay it through the interaction
        /// queue — but cap at a single re-defer so a rare close/hide race can't loop.</summary>
        /// <param name="cardAlreadyOpen">Any blocking fullscreen interaction is on screen: another lock
        /// card, or (#763) a pop quiz — both are ownerless HWND_TOPMOST covers and must never share the
        /// screen.</param>
        internal static BlockedCardAction ResolveBlockedCardAction(bool cardAlreadyOpen, bool isDeferredReplay, bool hasInteractionQueue)
        {
            if (!cardAlreadyOpen) return BlockedCardAction.Proceed;
            if (isDeferredReplay) return BlockedCardAction.DropAfterReDefer; // one re-defer cap
            if (hasInteractionQueue) return BlockedCardAction.Defer;
            return BlockedCardAction.DropNoQueue;
        }

        public void ShowLockCard(string? customPhrase = null, int customRepeats = -1, bool customStrict = false, bool isTest = false, bool isDeferredReplay = false)
        {
            DispatcherHelper.RunOnUISync(() =>
            {
                // Prevent stacking multiple lock cards.
                // Gate on the visible set (IsAnyOpen), NOT Application.Current.Windows: since 6.2.10 the
                // window is keep-alive pooled (dismiss => Hide(), not Close()), so a hidden pooled instance
                // lingers in Application.Current.Windows forever and would block every card after the first.
                // #763: a visible pop quiz blocks us just as hard — the interaction queue is meant to keep
                // the two apart, but it released the slot with a card still up and both covers stacked.
                var cardOpen = LockCardWindow.IsAnyOpen();
                var quizOpen = PopQuizWindow.IsAnyOpen();
                var blockedAction = ResolveBlockedCardAction(cardOpen || quizOpen, isDeferredReplay, App.InteractionQueue != null);
                if (blockedAction != BlockedCardAction.Proceed)
                {
                    var blocker = cardOpen ? "a lock card" : "a pop quiz";
                    // Short, log-safe snippet of the requested phrase for diagnostics.
                    var phraseSnippet = string.IsNullOrEmpty(customPhrase)
                        ? "(default/random)"
                        : (customPhrase.Length > 40 ? customPhrase.Substring(0, 40) + "..." : customPhrase);

                    switch (blockedAction)
                    {
                        case BlockedCardAction.DropAfterReDefer:
                            // We already deferred once and are the dequeued replay, yet a card is STILL
                            // open. Do NOT re-enqueue — the queue's Complete()/dequeue cycle already set us
                            // as the active LockCard, so re-enqueuing would hold the slot with nothing on
                            // screen until the 5-min stuck backstop, and could bounce indefinitely. Give up
                            // after this single re-defer and release the slot so the queue keeps moving.
                            App.Logger?.Warning("LockCardService: Deferred lock card still blocked on replay ({Blocker} is open). Dropping after one re-defer. Phrase: {Phrase}", blocker, phraseSnippet);
                            // CompleteIfCurrent, not Complete: if the card blocking us is the one
                            // holding the slot, a type-blind Complete would release SOMEONE ELSE's
                            // live claim and dequeue the next interaction over their open window.
                            App.InteractionQueue?.CompleteIfCurrent(InteractionQueueService.InteractionType.LockCard);
                            break;

                        case BlockedCardAction.Defer:
                            App.Logger?.Warning("LockCardService: {Blocker} is already open. Deferring this lock card to the interaction queue. Phrase: {Phrase}", blocker, phraseSnippet);
                            App.InteractionQueue?.TryStart(
                                InteractionQueueService.InteractionType.LockCard,
                                () => ShowLockCard(customPhrase, customRepeats, customStrict, isTest, isDeferredReplay: true),
                                queue: true);
                            break;

                        case BlockedCardAction.DropNoQueue:
                            App.Logger?.Warning("LockCardService: {Blocker} is already open and no interaction queue is available to defer to. Dropping. Phrase: {Phrase}", blocker, phraseSnippet);
                            break;
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
                        () => ShowLockCard(customPhrase, customRepeats, customStrict, isTest, isDeferredReplay: true),
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

                    // Pick a random phrase (or use custom one if AI provided it). The custom (AI-supplied)
                    // path bypasses rotation entirely — it isn't a draw from the enabled pool.
                    var phrase = customPhrase ?? PickPhrase(enabledPhrases);
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
        /// Pick a phrase at random while avoiding the last few shown, so the same phrase can't
        /// repeat back-to-back. Filters the enabled pool against the recent set (rather than
        /// re-rolling in a loop); if that empties the pool — or only one phrase is enabled — we
        /// skip rotation and draw from the full list so we can never loop forever or go silent.
        /// </summary>
        private string PickPhrase(List<string> enabledPhrases)
        {
            var candidates = enabledPhrases;
            if (enabledPhrases.Count > 1)
            {
                var fresh = enabledPhrases.Where(p => !_recentPhrasesSet.Contains(p)).ToList();
                if (fresh.Count > 0) candidates = fresh;
            }

            var phrase = candidates[_random.Next(candidates.Count)];

            // Remember it, then trim the window to at most (pool - 1) so there's always at least one
            // fresh candidate next time, capped at RecentPhrasesMemory. Skip tracking a lone phrase.
            if (enabledPhrases.Count > 1 && _recentPhrasesSet.Add(phrase))
            {
                _recentPhrases.Enqueue(phrase);
                int cap = Math.Min(enabledPhrases.Count - 1, RecentPhrasesMemory);
                while (_recentPhrases.Count > cap)
                    _recentPhrasesSet.Remove(_recentPhrases.Dequeue());
            }

            return phrase;
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

            // Teardown: nothing is left to solve the card against, so it must not outlive the service.
            Stop(dismissOpenCards: true);
        }
    }
}
