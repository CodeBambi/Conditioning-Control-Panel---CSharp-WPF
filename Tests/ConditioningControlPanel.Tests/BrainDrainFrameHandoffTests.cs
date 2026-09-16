using System;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Compositor;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// #777 — the brain-drain desktop capture moved off the UI thread, and <see cref="LatestFrameSlot{T}"/>
/// is the seam it moved across. The freeze being fixed was a full-screen StretchBlt off the live
/// desktop DC running ON the dispatcher; the fix is only worth anything if the new hand-off cannot
/// re-introduce a wait, a leak, or a double free. Those three properties are what is asserted here
/// (the GDI capture itself needs a real desktop and is verified in play-test).
/// </summary>
public class BrainDrainFrameHandoffTests
{
    private sealed class Frame : IDisposable
    {
        public readonly int Id;
        public int DisposeCount;
        public bool Disposed => DisposeCount > 0;
        public Frame(int id) => Id = id;
        public void Dispose() => Interlocked.Increment(ref DisposeCount);
    }

    // ---- latest-wins ----

    [Fact]
    public void ConsumerGetsTheNewestFrame_AndTheSkippedOneIsFreed()
    {
        var slot = new LatestFrameSlot<Frame>();
        var first = new Frame(1);
        var second = new Frame(2);

        slot.Publish(first);
        slot.Publish(second);      // consumer never came for #1

        var taken = slot.TryTake(2);

        Assert.Same(second, taken);
        Assert.True(first.Disposed);      // skipped frames are freed by the producer, not leaked
        Assert.False(second.Disposed);
    }

    [Fact]
    public void FallingBehindSkipsFrames_ItNeverQueuesThem()
    {
        // A capture thread running while the UI is busy: 50 frames produced, one consumed. Exactly
        // one frame is live at any moment, and the other 49 are freed - if this queued instead, a
        // 30fps full-screen capture would balloon memory during any UI stall.
        var slot = new LatestFrameSlot<Frame>();
        var frames = new Frame[50];
        for (int i = 0; i < frames.Length; i++)
        {
            frames[i] = new Frame(i);
            slot.Publish(frames[i]);
        }

        var taken = slot.TryTake(2);

        Assert.Same(frames[^1], taken);
        for (int i = 0; i < frames.Length - 1; i++)
            Assert.True(frames[i].Disposed, $"frame {i} leaked");
    }

    // ---- ownership transfers exactly once ----

    [Fact]
    public void ATakenFrameIsNeverDisposedByTheProducer()
    {
        var slot = new LatestFrameSlot<Frame>();
        var taken = new Frame(1);
        slot.Publish(taken);
        Assert.Same(taken, slot.TryTake(2));

        slot.Publish(new Frame(2));   // producer moves on
        slot.Clear();                 // ...then tears the slot down

        Assert.False(taken.Disposed); // the consumer owns it; double-freeing it would be a native AV
    }

    [Fact]
    public void NothingNewMeansNull_SoTheConsumerRedrawsWhatItHas()
    {
        var slot = new LatestFrameSlot<Frame>();
        slot.Publish(new Frame(1));

        Assert.NotNull(slot.TryTake(2));
        Assert.Null(slot.TryTake(2));   // a capture cadence slower than the render tick
        Assert.Null(slot.TryTake(2));
    }

    [Fact]
    public void TeardownFreesAnUntakenFrame()
    {
        var slot = new LatestFrameSlot<Frame>();
        var orphan = new Frame(1);
        slot.Publish(orphan);

        slot.Clear();

        Assert.True(orphan.Disposed);
        Assert.Equal(1, orphan.DisposeCount);
        Assert.Null(slot.TryTake(2));
    }

    // ---- the consumer never waits (the whole point of #777) ----

    [Fact]
    public void AContendedSlotMakesTheConsumerSkip_NotBlock()
    {
        var slot = new LatestFrameSlot<Frame>();
        var held = new Frame(1);
        slot.Publish(held);

        // These are liveness watchdogs, not a wall-clock performance budget. Stopwatch elapsed time
        // includes time the test thread is descheduled under a busy Windows CI test host (#1326).
        const int WatchdogMs = 5000;
        using var holding = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        using var consumerFinished = new ManualResetEventSlim(false);
        Frame? taken = null;
        Exception? consumerError = null;

        var hog = new Thread(() =>
        {
            lock (slot.Gate)      // stand in for a producer mid-swap
            {
                holding.Set();
                release.Wait();
            }
        })
        {
            IsBackground = true,
            Name = "BrainDrain-test-holder",
        };
        var consumer = new Thread(() =>
        {
            try { taken = slot.TryTake(2); }
            catch (Exception ex) { consumerError = ex; }
            finally { consumerFinished.Set(); }
        })
        {
            IsBackground = true,
            Name = "BrainDrain-test-consumer",
        };

        var holdingAcquired = false;
        var completedWhileHeld = false;
        var holderStopped = false;
        var consumerStopped = true;
        try
        {
            hog.Start();
            holdingAcquired = holding.Wait(WatchdogMs);
            if (holdingAcquired)
            {
                consumer.Start();
                completedWhileHeld = consumerFinished.Wait(WatchdogMs);
            }
        }
        finally
        {
            // A blocking implementation is released only after the finite watchdog has observed
            // that the consumer did not finish. Joining here lets the negative mutation fail cleanly
            // instead of turning a test failure into a permanently hung test host.
            release.Set();
            holderStopped = hog.Join(WatchdogMs);
            consumerStopped = !consumer.IsAlive || consumer.Join(WatchdogMs);
        }

        if (consumerStopped)
        {
            taken?.Dispose();
            slot.Clear();
        }

        Assert.True(holdingAcquired, "the contention thread did not acquire the slot gate");
        Assert.True(holderStopped, "the contention thread did not release the slot gate");
        Assert.True(consumerStopped, "the consumer did not exit after the contention gate was released");
        Assert.True(completedWhileHeld,
            "TryTake did not complete while the slot gate was held - a blocking hand-off would " +
            "freeze the UI thread through the #777 seam.");
        Assert.Null(consumerError);
        Assert.Null(taken);   // skipped this frame rather than waiting on the capture thread
        Assert.True(held.Disposed);
        Assert.Equal(1, held.DisposeCount);
    }

    // ---- concurrent hammering: no double free, no leak ----

    [Fact]
    public void ProducerAndConsumerRacing_LeaveEveryFrameFreedExactlyOnce()
    {
        var slot = new LatestFrameSlot<Frame>();
        const int Count = 5000;
        var produced = new Frame[Count];
        Frame? lastTaken = null;

        var producer = Task.Run(() =>
        {
            for (int i = 0; i < Count; i++)
            {
                produced[i] = new Frame(i);
                slot.Publish(produced[i]);
            }
        });

        var consumer = Task.Run(() =>
        {
            while (!producer.IsCompleted)
            {
                var f = slot.TryTake(2);
                if (f == null) continue;
                lastTaken?.Dispose();   // the consumer frees the frame it is replacing
                lastTaken = f;
            }
            var tail = slot.TryTake(2);
            if (tail != null) { lastTaken?.Dispose(); lastTaken = tail; }
        });

        Task.WaitAll(new[] { producer, consumer }, 30000);
        slot.Clear();
        lastTaken?.Dispose();

        foreach (var f in produced)
        {
            Assert.Equal(1, f.DisposeCount);   // exactly once: 0 = leak, 2 = use-after-free
        }
    }
}
