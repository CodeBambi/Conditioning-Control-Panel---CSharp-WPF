using System;
using System.Collections.Generic;
using System.Windows.Threading;
using ConditioningControlPanel.Helpers;

namespace ConditioningControlPanel.Services;

/// <summary>
/// Coordinates fullscreen interactions (videos, bubble counts, lock cards) to prevent overlap.
/// Each service checks CanStart before triggering, and queued items play when the current one finishes.
/// </summary>
public class InteractionQueueService
{
    public enum InteractionType
    {
        Video,
        BubbleCount,
        LockCard,
        PopQuiz,
        /// <summary>Takeover "Hypnotube" video playing fullscreen in the embedded browser.
        /// Deliberately a distinct type from <see cref="Video"/>: native teardown paths
        /// (panic, ForceCleanup, session switch) call CompleteIfCurrent(Video) and must
        /// never be able to release a web video's claim.</summary>
        WebVideo
    }

    private readonly Queue<(InteractionType Type, Action Trigger)> _queue = new();
    private readonly object _lock = new();
    private DispatcherTimer? _stuckDetectionTimer;
    private DateTime _interactionStartTime;
    // Bumped on every claim of the slot, so a teardown path can tell "the claim I tripped on" from
    // "a new claim of the same type that the teardown itself dequeued".
    private long _claimId;

    // Default max time before auto-recovery when duration is unknown (5 minutes)
    private const int DefaultMaxInteractionMinutes = 5;

    // User-paced interactions (lock cards, pop quizzes) have no duration of their own — the 5-minute
    // backstop is a video-shaped timeout applied to a typing/speaking-shaped interaction. While their
    // window is still on screen the tick renews instead of recovering, up to a hard ceiling after which
    // the UI is force-closed BEFORE the slot is released (#763).
    private const int UserPacedRenewMinutes = 5;
    private const int UserPacedMaxInteractionMinutes = 30;
    // Last-ditch cap on holding the slot for a window that reports itself open but won't go away:
    // an un-closable overlay is bad, a permanently wedged queue (no further interactions all session)
    // is worse.
    private const int HeldSlotAbandonMinutes = 60;

    /// <summary>
    /// Currently active interaction type, or null if none
    /// </summary>
    public InteractionType? CurrentInteraction { get; private set; }

    /// <summary>
    /// Whether any fullscreen interaction is currently active
    /// </summary>
    public bool IsBusy => CurrentInteraction.HasValue;

    /// <summary>
    /// Check if a new interaction can start immediately
    /// </summary>
    public bool CanStart => !IsBusy;

    /// <summary>
    /// Number of queued interactions waiting
    /// </summary>
    public int QueuedCount
    {
        get
        {
            lock (_lock)
            {
                return _queue.Count;
            }
        }
    }

    /// <summary>
    /// Try to start an interaction. Returns true if started immediately, false if queued.
    /// </summary>
    /// <param name="type">Type of interaction</param>
    /// <param name="triggerAction">Action to execute when it's this interaction's turn</param>
    /// <param name="queue">If true and busy, queue for later. If false and busy, discard.</param>
    /// <returns>True if started immediately</returns>
    public bool TryStart(InteractionType type, Action triggerAction, bool queue = true)
    {
        lock (_lock)
        {
            if (!IsBusy)
            {
                CurrentInteraction = type;
                _interactionStartTime = DateTime.Now;
                _claimId++;
                StartStuckDetectionTimer();
                App.Logger?.Information("InteractionQueue: Starting {Type}", type);
                triggerAction();
                return true;
            }

            // Log how long current interaction has been active (helps diagnose stuck queue)
            var activeDuration = DateTime.Now - _interactionStartTime;
            App.Logger?.Debug("InteractionQueue: {Type} blocked by {Current} (active for {Duration:F1}s, queue: {Count})",
                type, CurrentInteraction, activeDuration.TotalSeconds, _queue.Count);

            if (queue)
            {
                // Don't queue duplicates of the same type
                foreach (var item in _queue)
                {
                    if (item.Type == type)
                    {
                        App.Logger?.Information("InteractionQueue: {Type} already has a pending item queued - suppressing duplicate; this later trigger is dropped (queue caps this type at one pending)", type);
                        return false;
                    }
                }

                _queue.Enqueue((type, triggerAction));
                App.Logger?.Information("InteractionQueue: Queued {Type} (queue size: {Count})", type, _queue.Count);
            }
            else
            {
                App.Logger?.Debug("InteractionQueue: Discarded {Type} (busy with {Current})", type, CurrentInteraction);
            }

            return false;
        }
    }

    /// <summary>
    /// Mark the current interaction as complete and trigger the next queued one
    /// </summary>
    public void Complete(InteractionType type)
    {
        lock (_lock)
        {
            CompleteLocked(type);
        }
    }

    /// <summary>
    /// Release the slot ONLY if <paramref name="type"/> is the interaction currently active.
    /// Safe to call from abnormal teardown paths (panic key, ForceCleanup, session switch) that
    /// might run after a different interaction has already taken over — it will never clear the
    /// wrong one. No-op if the queue is idle or a different type is active. Returns true if it
    /// released the slot. The check-and-release is atomic under the queue lock (no TOCTOU with a
    /// concurrent dequeue).
    /// </summary>
    public bool CompleteIfCurrent(InteractionType type)
    {
        lock (_lock)
        {
            if (CurrentInteraction != type) return false;
            CompleteLocked(type);
            return true;
        }
    }

    /// <summary>
    /// Release the slot only if it is still the exact claim identified by <paramref name="claimId"/>.
    /// A type-only check is not enough on the stuck-recovery path: the force-cleanup it runs may already
    /// have released the slot AND dequeued a new interaction of the same type, whose window has not been
    /// created yet (the trigger is dispatched asynchronously) — a type-only release would drop that fresh
    /// claim on the floor.
    /// </summary>
    private void CompleteIfClaim(InteractionType type, long claimId)
    {
        lock (_lock)
        {
            if (CurrentInteraction != type || _claimId != claimId) return;
            CompleteLocked(type);
        }
    }

    /// <summary>Completion logic; caller MUST hold <see cref="_lock"/>.</summary>
    private void CompleteLocked(InteractionType type)
    {
        {
            StopStuckDetectionTimer();

            if (CurrentInteraction != type)
            {
                // Type mismatch - this could indicate a bug, but we should still try to recover
                // If CurrentInteraction is null, the queue is already clear
                if (!CurrentInteraction.HasValue)
                {
                    App.Logger?.Debug("InteractionQueue: Complete({Type}) called but queue already clear", type);
                    return;
                }

                // Log warning but continue to clear if this helps unstick the queue
                var activeDuration = DateTime.Now - _interactionStartTime;
                App.Logger?.Warning("InteractionQueue: Complete called for {Type} but current is {Current} (active {Duration:F1}s). Clearing anyway to prevent stuck state.",
                    type, CurrentInteraction, activeDuration.TotalSeconds);
            }

            App.Logger?.Information("InteractionQueue: Completed {Type}", type);
            CurrentInteraction = null;

            // Trigger next queued interaction
            if (_queue.Count > 0)
            {
                var next = _queue.Dequeue();
                CurrentInteraction = next.Type;
                _interactionStartTime = DateTime.Now;
                _claimId++;
                StartStuckDetectionTimer();
                App.Logger?.Information("InteractionQueue: Starting queued {Type} (remaining: {Count})",
                    next.Type, _queue.Count);

                DispatchTrigger(next.Trigger);
            }
        }
    }

    /// <summary>
    /// Queue a dequeued trigger asynchronously — NEVER inline. Complete() holds _lock and is
    /// called from window close handlers (LockCardWindow.OnClosing, PopQuizWindow.OnClosed);
    /// DispatcherHelper.RunOnUI runs inline when already on the UI thread, which would execute
    /// a fullscreen trigger (video/lock card) inside the lock and re-entrantly inside Close().
    /// </summary>
    private static void DispatchTrigger(Action trigger) => TriggerScheduler(trigger);

    /// <summary>Test seam: how a dequeued queued trigger is scheduled. Defaults to the
    /// Dispatcher-marshalled path (<see cref="DefaultDispatchTrigger"/>). Overridable so headless
    /// tests can run the dequeued replay synchronously without a WPF Application/Dispatcher (with no
    /// dispatcher the default path silently no-ops, which would hide the queue-replay behavior).</summary>
    internal static Action<Action> TriggerScheduler = DefaultDispatchTrigger;

    private static void DefaultDispatchTrigger(Action trigger)
    {
        try
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) return;
            dispatcher.BeginInvoke(trigger);
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("InteractionQueue: trigger dispatch failed: {Error}", ex.Message);
        }
    }

    /// <summary>
    /// Force clear the current interaction (e.g., panic button)
    /// </summary>
    public void ForceReset()
    {
        lock (_lock)
        {
            if (CurrentInteraction.HasValue)
            {
                App.Logger?.Information("InteractionQueue: Force reset from {Type}", CurrentInteraction);
            }
            CurrentInteraction = null;
            _queue.Clear();
        }
    }

    /// <summary>
    /// Clear all queued interactions without affecting current
    /// </summary>
    public void ClearQueue()
    {
        lock (_lock)
        {
            var count = _queue.Count;
            _queue.Clear();
            if (count > 0)
            {
                App.Logger?.Information("InteractionQueue: Cleared {Count} queued items", count);
            }
        }
    }

    /// <summary>
    /// Extends the stuck detection timeout to accommodate a known interaction duration.
    /// Call this when the actual duration becomes known (e.g., video duration from VLC).
    /// </summary>
    /// <param name="durationSeconds">The expected duration in seconds</param>
    /// <param name="onlyIf">When set, extend only if this type currently holds the slot.
    /// Callers reporting a duration for THEIR interaction must pass their type — a
    /// type-blind extension can stretch an unrelated (possibly genuinely stuck)
    /// interaction's recovery window to the reported duration.</param>
    public void ExtendTimeout(double durationSeconds, InteractionType? onlyIf = null)
    {
        lock (_lock)
        {
            if (!CurrentInteraction.HasValue) return;
            if (onlyIf.HasValue && CurrentInteraction != onlyIf)
            {
                App.Logger?.Debug("InteractionQueue: ExtendTimeout for {OnlyIf} skipped - current is {Current}",
                    onlyIf, CurrentInteraction);
                return;
            }

            // Restart the timer with: expected duration + 30s buffer, minimum 5 minutes
            var timeoutMinutes = Math.Max(DefaultMaxInteractionMinutes, (durationSeconds + 30) / 60.0);
            StartStuckDetectionTimer(TimeSpan.FromMinutes(timeoutMinutes));
            App.Logger?.Debug("InteractionQueue: Extended stuck timeout to {Duration:F1} min for {Type}",
                timeoutMinutes, CurrentInteraction);
        }
    }

    // ------------------------------------------------------------------------------------
    // LOCK/DISPATCHER INVERSION (freeze cluster #775/#777/#779/#780)
    //
    // Every caller of the two methods below holds _lock: TryStart (:94), CompleteLocked (:178,
    // :206), RenewStuckTimeout (:380), ExtendTimeout (:298). They used to marshal with
    // RunOnUISync, i.e. a BLOCKING cross-thread Dispatcher.Invoke — so a background caller held
    // _lock while waiting on the UI thread, and any UI-thread TryStart/Complete/QueuedCount
    // waiting on _lock deadlocked against it. (Video teardown calls Complete() on the UI thread
    // on every single video end; haptics/OCR/remote-control paths call TryStart off-thread.)
    // Only DispatcherHelper's 5-second SyncInvokeTimeout broke it, i.e. a 5-second whole-app
    // freeze per occurrence, silently, with the UI work then abandoned.
    //
    // RunOnUI (BeginInvoke) removes the inversion outright: no result is needed here, the
    // dispatcher preserves FIFO order at a given priority so a start-then-stop pair still lands
    // in order, and this is a FIVE-MINUTE stuck detector — arming it a dispatcher turn later
    // cannot matter. On the UI thread RunOnUI still runs inline, exactly as before.
    // ------------------------------------------------------------------------------------

    /// <summary>
    /// Starts a timer that auto-recovers from stuck interactions. Safe to call under <see cref="_lock"/>:
    /// never blocks on the UI thread (see the note above).
    /// </summary>
    private void StartStuckDetectionTimer(TimeSpan? timeout = null)
    {
        try
        {
            var interval = timeout ?? TimeSpan.FromMinutes(DefaultMaxInteractionMinutes);
            DispatcherHelper.RunOnUI(() =>
            {
                StopStuckDetectionTimer();

                _stuckDetectionTimer = new DispatcherTimer
                {
                    Interval = interval
                };
                _stuckDetectionTimer.Tick += OnStuckDetectionTimerTick;
                _stuckDetectionTimer.Start();
            });
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("Failed to start stuck detection timer: {Error}", ex.Message);
        }
    }

    /// <summary>Disarms the stuck detector. Safe to call under <see cref="_lock"/> (see the note above).</summary>
    private void StopStuckDetectionTimer()
    {
        try
        {
            DispatcherHelper.RunOnUI(() =>
            {
                _stuckDetectionTimer?.Stop();
                _stuckDetectionTimer = null;
            });
        }
        catch
        {
            // Ignore errors during shutdown
        }
    }

    /// <summary>Interactions paced by the user rather than by content: they end when a human types,
    /// speaks or clicks, so elapsed time alone says nothing about whether they are stuck.</summary>
    private static bool IsUserPaced(InteractionType type)
        => type is InteractionType.LockCard or InteractionType.PopQuiz;

    /// <summary>Test seam: how the queue asks whether an interaction's fullscreen UI is still on screen.
    /// Overridable so headless tests can drive the stuck-timer paths without realizing WPF windows
    /// (the default path needs a real visible-window set).</summary>
    internal static Func<InteractionType, bool> UiStillOnScreenProbe = DefaultUiStillOnScreen;

    private static bool DefaultUiStillOnScreen(InteractionType type)
    {
        try
        {
            return type switch
            {
                InteractionType.LockCard => LockCardWindow.IsAnyOpen(),
                InteractionType.PopQuiz => PopQuizWindow.IsAnyOpen(),
                _ => false
            };
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Re-arm the stuck timer for a claim that turned out not to be stuck. Claim-guarded so a
    /// slot that changed hands while we were tearing down keeps its own (already armed) timer.</summary>
    private void RenewStuckTimeout(InteractionType type, long claimId, TimeSpan interval)
    {
        lock (_lock)
        {
            if (CurrentInteraction != type || _claimId != claimId) return;
            StartStuckDetectionTimer(interval);
        }
    }

    private void OnStuckDetectionTimerTick(object? sender, EventArgs e)
    {
        // Stop the timer that actually fired, not whatever the field points at now: if the slot changed
        // hands between this timer firing and the handler running, the field is already the NEW claim's
        // timer, and stopping it would leave that claim with no stuck detection at all (the renew and
        // "not stuck anymore" paths below return without re-arming it).
        ((sender as DispatcherTimer) ?? _stuckDetectionTimer)?.Stop();

        InteractionType stuckType;
        long claimId;
        TimeSpan activeDuration;
        lock (_lock)
        {
            if (!CurrentInteraction.HasValue)
            {
                return; // Not stuck anymore
            }

            stuckType = CurrentInteraction.Value;
            claimId = _claimId;
            activeDuration = DateTime.Now - _interactionStartTime;
        }

        // A lock card or pop quiz that is still on screen is waiting on a human, not stuck — nobody
        // calls ExtendTimeout for them, so the 5-minute default used to "recover" a perfectly healthy
        // card. Renew instead, up to the hard ceiling; someone who walked away still gets the screen
        // cleared, just never while their card is up (#763).
        if (IsUserPaced(stuckType) && activeDuration.TotalMinutes < UserPacedMaxInteractionMinutes
            && UiStillOnScreenProbe(stuckType))
        {
            App.Logger?.Debug("InteractionQueue: {Type} still on screen after {Duration:F1} min - user-paced, renewing stuck timeout",
                stuckType, activeDuration.TotalMinutes);
            RenewStuckTimeout(stuckType, claimId, TimeSpan.FromMinutes(UserPacedRenewMinutes));
            return;
        }

        App.Logger?.Warning("InteractionQueue: STUCK INTERACTION DETECTED! {Type} has been active for {Duration:F1} minutes. Auto-recovering...",
            stuckType, activeDuration.TotalMinutes);

        // Tear down OUTSIDE the lock, then release. Ordering matters twice over:
        // - Not under _lock: the cleanups call back into CompleteIfCurrent; running them
        //   inside the (re-entrant) lock used to dequeue the next item and then the old
        //   code below cleared the slot and dequeued a SECOND item (double-dispatch).
        // - Release AFTER teardown returns, never before: the old code cleared the slot
        //   and dispatched the next queued interaction first, but ForceCleanup/CloseAll
        //   pumps messages for seconds — the next interaction's trigger executed INSIDE
        //   that pump, creating fullscreen windows while LibVLC players were mid-detach
        //   (the multi-monitor freeze path). The cleanups' own CompleteIfCurrent (plus
        //   the backstop in finally) dequeues only once teardown is done.
        // This tick runs on the UI thread (DispatcherTimer), so the cleanups can run
        // inline here now that the lock is released.
        try
        {
            switch (stuckType)
            {
                case InteractionType.Video:
                    App.Video?.ForceCleanup();               // ends with CompleteIfCurrent(Video)
                    break;
                case InteractionType.BubbleCount:
                    App.BubbleCount?.ForceCleanup();
                    break;
                case InteractionType.LockCard:
                    LockCardWindow.ForceCloseAll();          // releases the slot itself when it is ours
                    break;
                case InteractionType.PopQuiz:
                    PopQuizWindow.ForceCloseAll();           // OnClosed releases the slot
                    break;
                case InteractionType.WebVideo:
                    App.BrowserMedia?.ForceEndCore("queue-stuck-recovery"); // sync teardown, then releases
                    break;
            }
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("InteractionQueue: Failed to force-cleanup stuck {Type}: {Error}", stuckType, ex.Message);
        }
        finally
        {
            // The slot is the ONLY thing keeping two fullscreen HWND_TOPMOST overlays apart, so it must
            // never be released while its window is still up — releasing under a live lock card is how a
            // card and a pop quiz ended up stacked (#763). If teardown could not clear the UI, keep
            // holding the claim and re-arm instead of handing the screen to the next interaction.
            if (UiStillOnScreenProbe(stuckType) && activeDuration.TotalMinutes < HeldSlotAbandonMinutes)
            {
                App.Logger?.Warning("InteractionQueue: {Type} UI still on screen after force-cleanup - holding the slot instead of releasing it under a live overlay",
                    stuckType);
                RenewStuckTimeout(stuckType, claimId, TimeSpan.FromMinutes(DefaultMaxInteractionMinutes));
            }
            else
            {
                // Backstop: if the cleanup path didn't release the slot itself (or threw),
                // free it now that no teardown is running. Idempotent and claim-guarded.
                CompleteIfClaim(stuckType, claimId);
            }
        }
    }
}
