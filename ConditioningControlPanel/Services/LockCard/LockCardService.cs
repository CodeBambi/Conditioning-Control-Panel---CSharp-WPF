using System;
using System.Collections.Generic;
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
    /// The Windows head's half of the lock card: putting one on screen, and negotiating with the
    /// interaction queue and a visible pop quiz for the right to do so.
    ///
    /// <para>The schedule is no longer here. <see cref="LockCardScheduler"/> in Core owns the timer,
    /// the first-card offset, the ±30% spacing and the no-repeat phrase rotation - all of it
    /// arithmetic over <c>AppSettings</c> and a clock, so all of it portable. This class keeps its
    /// public shape (<see cref="Start"/>, <see cref="Stop"/>, <see cref="IsRunning"/>,
    /// <see cref="ShowLockCard"/>, <see cref="TestLockCard"/>) so no caller moved, and forwards the
    /// scheduling half to the shared <see cref="LockCardScheduler.Instance"/> - shared because every
    /// ad-hoc card here (voice command, Deeper, the dashboard Test button, remote trigger) must draw
    /// from the same rotation window the scheduled cards do.</para>
    ///
    /// <para>What deliberately did NOT move: <see cref="ResolveBlockedCardAction"/> and
    /// <see cref="BlockedCardAction"/>. They are pure, but they describe a race between two WPF
    /// window classes and this head's interaction queue, and moving them would have edited two test
    /// files this layer does not own for no behaviour gained.</para>
    /// </summary>
    public class LockCardService : IDisposable
    {
        private bool _isDisposed;

        /// <summary>The schedule, in Core. Shared, so ad-hoc cards share its phrase rotation.</summary>
        private static LockCardScheduler Scheduler => LockCardScheduler.Instance;

        public bool IsRunning => Scheduler.IsRunning;

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

            // EMI Desk (MOMENTS `lockCardSolved`). Fired HERE and not through the bark bridge on
            // purpose: BarkService only raises its LockCardCompleted trigger on the pool-bark
            // fallback path (FireLockCardPoolBark), so on the coin-flip's AI branch — every user
            // with the avatar and AI chat on — the trigger never happens at all and she would have
            // been silent for exactly the people who use the app most. This is the one place a
            // completed card is announced from, so it is the one place that cannot miss.
            //
            // {n} = tries, which is the mistakes plus the one that landed. A clean card has no
            // number worth saying ("1 tries"), so the ctx is omitted and the single line in the
            // pool that asks for {n} is skipped by the engine — the other seven still play.
            try
            {
                App.EmiDesk?.Fire("lockCardSolved",
                    mistakes > 0 ? (object?)new { n = mistakes + 1 } : null);
            }
            catch { /* a desk that throws never gets to break a lock card */ }
        }

        /// <param name="windowMinutes">
        /// #736: how long the caller expects to keep the service running — a session's remaining
        /// minutes. When supplied, the first card is guaranteed to land inside that window with
        /// room to complete it. Null (dashboard use) means open-ended.
        /// </param>
        public void Start(double? windowMinutes = null) => Scheduler.Start(windowMinutes);

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

            Scheduler.Stop();
        }

        /// <summary>#736: delay before the FIRST lock card of a run, in minutes. The rule moved to
        /// <see cref="LockCardScheduler.ComputeFirstCardDelayMinutes"/>; this forwards so
        /// LockCardScheduleTests keeps testing the one implementation.</summary>
        internal static double ComputeFirstCardDelayMinutes(int perHour, double? windowMinutes, double roll)
            => LockCardScheduler.ComputeFirstCardDelayMinutes(perHour, windowMinutes, roll);

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
                    List<string> enabledPhrases = LockCardScheduler.EnabledPhrases();

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
                    // path bypasses rotation entirely — it isn't a draw from the enabled pool. The draw
                    // happens HERE, past every gate above, so a deferred or dropped card never consumes a
                    // rotation slot — and on the UI thread, which is what keeps the scheduler's rotation
                    // state single-threaded.
                    var phrase = customPhrase ?? Scheduler.PickPhrase(enabledPhrases)!;
                    var repeats = customRepeats >= 0 ? customRepeats : settings.LockCardRepeats;
                    var strict = customStrict || settings.LockCardStrict;
                    var voice = settings.LockCardVoiceMode;

                    // Show on all monitors with synced input
                    LockCardWindow.ShowOnAllMonitors(phrase, repeats, strict, isTest, voice);

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

            // Teardown: nothing is left to solve the card against, so it must not outlive the service.
            Stop(dismissOpenCards: true);
        }
    }
}
