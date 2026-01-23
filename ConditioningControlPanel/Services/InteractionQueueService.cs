using System;
using System.Collections.Generic;
using System.Windows.Threading;

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
        LockCard
    }

    private readonly Queue<(InteractionType Type, Action Trigger)> _queue = new();
    private readonly object _lock = new();
    private DispatcherTimer? _stuckDetectionTimer;
    private DateTime _interactionStartTime;

    // Maximum time an interaction can be active before auto-recovery (5 minutes)
    private const int MaxInteractionDurationMinutes = 5;

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
                        App.Logger?.Debug("InteractionQueue: {Type} already queued, skipping duplicate", type);
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

                // Use dispatcher to avoid stack overflow from nested calls
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(next.Trigger);
            }
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
    /// Starts a timer that auto-recovers from stuck interactions after MaxInteractionDurationMinutes
    /// </summary>
    private void StartStuckDetectionTimer()
    {
        try
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                StopStuckDetectionTimer();

                _stuckDetectionTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMinutes(MaxInteractionDurationMinutes)
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
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
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

        lock (_lock)
        {
            if (!CurrentInteraction.HasValue)
            {
                return; // Not stuck anymore
            }

            var activeDuration = DateTime.Now - _interactionStartTime;
            App.Logger?.Warning("InteractionQueue: STUCK INTERACTION DETECTED! {Type} has been active for {Duration:F1} minutes. Auto-recovering...",
                CurrentInteraction, activeDuration.TotalMinutes);

            // Force reset to recover
            CurrentInteraction = null;

            // Trigger next queued interaction if any
            if (_queue.Count > 0)
            {
                var next = _queue.Dequeue();
                CurrentInteraction = next.Type;
                _interactionStartTime = DateTime.Now;
                StartStuckDetectionTimer();
                App.Logger?.Information("InteractionQueue: Auto-recovery starting queued {Type} (remaining: {Count})",
                    next.Type, _queue.Count);

                System.Windows.Application.Current?.Dispatcher.BeginInvoke(next.Trigger);
            }
            else
            {
                App.Logger?.Information("InteractionQueue: Auto-recovery complete, queue is now clear");
            }
        }
    }
}
