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

    // Default max time before auto-recovery when duration is unknown (5 minutes)
    private const int DefaultMaxInteractionMinutes = 5;

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
    private static void DispatchTrigger(Action trigger)
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

    /// <summary>
    /// Starts a timer that auto-recovers from stuck interactions
    /// </summary>
    private void StartStuckDetectionTimer(TimeSpan? timeout = null)
    {
        try
        {
            var interval = timeout ?? TimeSpan.FromMinutes(DefaultMaxInteractionMinutes);
            DispatcherHelper.RunOnUISync(() =>
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

    private void StopStuckDetectionTimer()
    {
        try
        {
            DispatcherHelper.RunOnUISync(() =>
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

    private void OnStuckDetectionTimerTick(object? sender, EventArgs e)
    {
        _stuckDetectionTimer?.Stop();

        InteractionType stuckType;
        lock (_lock)
        {
            if (!CurrentInteraction.HasValue)
            {
                return; // Not stuck anymore
            }

            stuckType = CurrentInteraction.Value;
            var activeDuration = DateTime.Now - _interactionStartTime;
            App.Logger?.Warning("InteractionQueue: STUCK INTERACTION DETECTED! {Type} has been active for {Duration:F1} minutes. Auto-recovering...",
                stuckType, activeDuration.TotalMinutes);
        }

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
            // Backstop: if the cleanup path didn't release the slot itself (or threw),
            // free it now that no teardown is running. Idempotent and type-guarded.
            CompleteIfCurrent(stuckType);
        }
    }
}
