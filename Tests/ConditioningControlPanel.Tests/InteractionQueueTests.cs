using System;
using ConditioningControlPanel.Services;
using Xunit;
using IType = ConditioningControlPanel.Services.InteractionQueueService.InteractionType;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// #676 — AI lock cards were silently dropped when one was already on screen. The fix defers the
/// later request onto the InteractionQueue and replays it when the current holder completes, while
/// capping each interaction type at ONE pending item (a second request of the same type is dropped,
/// not double-queued). These tests exercise the queue's start / defer / replay / duplicate-suppression
/// contract headlessly.
///
/// Headless seam: the dequeued replay is normally marshalled onto the WPF Dispatcher, which no-ops
/// with no Application present, so this class swaps <see cref="InteractionQueueService.TriggerScheduler"/>
/// for a synchronous one for the duration of the test and restores it afterwards.
/// </summary>
public class InteractionQueueTests : IDisposable
{
    private readonly Action<Action> _originalScheduler;

    public InteractionQueueTests()
    {
        _originalScheduler = InteractionQueueService.TriggerScheduler;
        // Run dequeued replays inline instead of via the (absent) Dispatcher.
        InteractionQueueService.TriggerScheduler = a => a();
    }

    public void Dispose() => InteractionQueueService.TriggerScheduler = _originalScheduler;

    [Fact]
    public void Idle_StartsImmediately_AndTakesTheSlot()
    {
        var q = new InteractionQueueService();
        bool ran = false;

        bool started = q.TryStart(IType.Video, () => ran = true);

        Assert.True(started);
        Assert.True(ran, "the trigger runs synchronously when the queue is idle");
        Assert.True(q.IsBusy);
        Assert.False(q.CanStart);
        Assert.Equal(IType.Video, q.CurrentInteraction);
    }

    [Fact]
    public void Complete_FreesTheSlot_WhenNothingQueued()
    {
        var q = new InteractionQueueService();
        q.TryStart(IType.Video, () => { });

        q.Complete(IType.Video);

        Assert.False(q.IsBusy);
        Assert.True(q.CanStart);
        Assert.Null(q.CurrentInteraction);
    }

    [Fact]
    public void Busy_WithQueue_QueuesAndReplaysOnComplete()
    {
        var q = new InteractionQueueService();
        q.TryStart(IType.Video, () => { }); // current holder

        int lockRuns = 0;
        bool queued = q.TryStart(IType.LockCard, () => lockRuns++, queue: true);

        Assert.False(queued);           // did not start immediately
        Assert.Equal(1, q.QueuedCount);
        Assert.Equal(0, lockRuns);      // not run yet — the video still holds the slot
        Assert.Equal(IType.Video, q.CurrentInteraction);

        q.Complete(IType.Video);        // dequeues + replays the lock card

        Assert.Equal(1, lockRuns);
        Assert.Equal(IType.LockCard, q.CurrentInteraction);
        Assert.Equal(0, q.QueuedCount);
    }

    [Fact]
    public void DuplicateSuppression_OnePendingPerType_LaterTriggerDropped()
    {
        var q = new InteractionQueueService();
        q.TryStart(IType.Video, () => { }); // holder

        int firstRuns = 0, secondRuns = 0;
        bool first = q.TryStart(IType.LockCard, () => firstRuns++, queue: true);
        bool second = q.TryStart(IType.LockCard, () => secondRuns++, queue: true); // duplicate kind

        Assert.False(first);
        Assert.False(second);
        Assert.Equal(1, q.QueuedCount); // NOT 2 — the second was suppressed

        q.Complete(IType.Video);

        Assert.Equal(1, firstRuns);     // the first pending item replayed
        Assert.Equal(0, secondRuns);    // the duplicate was dropped, never replayed
    }

    [Fact]
    public void DistinctTypes_AreNotSuppressed()
    {
        var q = new InteractionQueueService();
        q.TryStart(IType.Video, () => { });

        q.TryStart(IType.LockCard, () => { }, queue: true);
        q.TryStart(IType.BubbleCount, () => { }, queue: true);

        Assert.Equal(2, q.QueuedCount); // different kinds each get a slot
    }

    [Fact]
    public void Busy_WithoutQueue_DiscardsInsteadOfQueuing()
    {
        var q = new InteractionQueueService();
        q.TryStart(IType.Video, () => { });

        int runs = 0;
        bool started = q.TryStart(IType.LockCard, () => runs++, queue: false);

        Assert.False(started);
        Assert.Equal(0, q.QueuedCount);

        q.Complete(IType.Video);

        Assert.Equal(0, runs);          // nothing was queued, so nothing replays
        Assert.Null(q.CurrentInteraction);
    }

    [Fact]
    public void CompleteIfCurrent_OnlyReleasesTheMatchingType()
    {
        var q = new InteractionQueueService();
        q.TryStart(IType.Video, () => { });

        Assert.False(q.CompleteIfCurrent(IType.LockCard)); // wrong type: no-op
        Assert.True(q.IsBusy);
        Assert.Equal(IType.Video, q.CurrentInteraction);

        Assert.True(q.CompleteIfCurrent(IType.Video));     // matching type: releases
        Assert.False(q.IsBusy);
        Assert.Null(q.CurrentInteraction);
    }

    [Fact]
    public void ChainedReplays_DequeueOneAtATime()
    {
        var q = new InteractionQueueService();
        q.TryStart(IType.Video, () => { });

        int lockRuns = 0, bubbleRuns = 0;
        q.TryStart(IType.LockCard, () => lockRuns++, queue: true);
        q.TryStart(IType.BubbleCount, () => bubbleRuns++, queue: true);

        q.Complete(IType.Video);          // -> LockCard becomes current, runs
        Assert.Equal(1, lockRuns);
        Assert.Equal(0, bubbleRuns);
        Assert.Equal(IType.LockCard, q.CurrentInteraction);
        Assert.Equal(1, q.QueuedCount);

        q.Complete(IType.LockCard);       // -> BubbleCount becomes current, runs
        Assert.Equal(1, bubbleRuns);
        Assert.Equal(IType.BubbleCount, q.CurrentInteraction);
        Assert.Equal(0, q.QueuedCount);
    }
}
