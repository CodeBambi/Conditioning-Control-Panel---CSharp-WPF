using System;
using System.Collections.Generic;
using System.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

// PopQuizScheduler against WPF PopQuizService (pre-move ShowPopQuiz/Timer_Tick): rate, #763 defer/drop, no stacking.
[Collection(SessionStatics.Name)]
public sealed class PopQuizSchedulerTests
{
    internal sealed class FakeClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch;
        private readonly List<FakeTimer> _timers = new();
        public override DateTimeOffset GetUtcNow() => _now;

        public override ITimer CreateTimer(TimerCallback cb, object? state, TimeSpan due, TimeSpan period)
        {
            var t = new FakeTimer(this, cb, state);
            _timers.Add(t);
            t.Change(due, period);
            return t;
        }

        public void Advance(TimeSpan by)
        {
            var end = _now + by;
            while (true)
            {
                FakeTimer? next = null;
                foreach (var t in _timers)
                    if (t.Due is { } d && d <= end && (next == null || d < next.Due)) next = t;
                if (next == null) break;
                _now = next.Due!.Value;
                next.Due = null;
                next.Fire();
            }
            _now = end;
        }

        private sealed class FakeTimer(FakeClock clock, TimerCallback cb, object? state) : ITimer
        {
            public DateTimeOffset? Due;
            public void Fire() => cb(state);
            public bool Change(TimeSpan due, TimeSpan period)
            {
                Due = due == Timeout.InfiniteTimeSpan ? null : clock._now + due;
                return true;
            }
            public void Dispose() => Due = null;
            public System.Threading.Tasks.ValueTask DisposeAsync() { Dispose(); return default; }
        }
    }

    private sealed class Host : IPopQuizHost
    {
        public bool QuizOpen, LockCard, Busy, HasQueue = true;
        public int Opens, Drops, Closes;
        public readonly List<Action> Deferred = new();
        public bool IsQuizOpen => QuizOpen;
        public bool IsLockCardOpen => LockCard;
        public bool IsInteractionBusy => Busy;
        public bool Defer(Action replay) { if (HasQueue) Deferred.Add(replay); return HasQueue; }
        public void DropDeferred() => Drops++;
        public void Open(bool isTest) => Opens++;
        public void CloseAll() => Closes++;
    }

    private static void With(bool enabled, int perHour, Action body)
    {
        var s = CoreSettings.Current;
        var (e, f) = (s.PopQuizEnabled, s.PopQuizFrequency);
        s.PopQuizEnabled = enabled; s.PopQuizFrequency = perHour;
        try { body(); } finally { s.PopQuizEnabled = e; s.PopQuizFrequency = f; }
    }

    [Fact]
    public void Rate_follows_per_hour_with_30_percent_jitter() => With(true, 6, () =>
    {
        var clock = new FakeClock(); var host = new Host();
        using var q = new PopQuizScheduler(host, clock);
        q.Start();
        clock.Advance(TimeSpan.FromMinutes(6.99));
        Assert.Equal(0, host.Opens);                   // earliest is 10min * 0.7
        clock.Advance(TimeSpan.FromMinutes(6.02));
        Assert.Equal(1, host.Opens);                   // latest is 10min * 1.3
        clock.Advance(TimeSpan.FromMinutes(120));
        Assert.InRange(host.Opens, 10, 19);            // 133.01min total / [7,13]min gaps
        q.Stop();
        Assert.Equal(1, host.Closes);
        var before = host.Opens;
        clock.Advance(TimeSpan.FromHours(5));
        Assert.Equal(before, host.Opens);
    });

    [Fact]
    public void Disabled_arms_nothing_and_a_mid_run_disable_shows_nothing() => With(false, 100, () =>
    {
        var clock = new FakeClock(); var host = new Host();
        var q = new PopQuizScheduler(host, clock);
        q.Start();
        Assert.False(q.IsRunning);
        clock.Advance(TimeSpan.FromHours(2));
        Assert.Equal(0, host.Opens);

        CoreSettings.Current.PopQuizEnabled = true;
        q.Start();
        CoreSettings.Current.PopQuizEnabled = false;
        clock.Advance(TimeSpan.FromHours(2));
        Assert.Equal(0, host.Opens);
        Assert.True(q.IsRunning);                      // WPF kept ticking, just showed nothing
        q.Stop();
    });

    [Fact]
    public void One_open_at_a_time()
    {
        var host = new Host { QuizOpen = true };
        new PopQuizScheduler(host, new FakeClock()).Show();
        Assert.Equal((0, 0), (host.Opens, host.Deferred.Count));
    }

    [Fact]
    public void Lock_card_defers_once_then_drops_on_replay()
    {
        var host = new Host { LockCard = true };
        new PopQuizScheduler(host, new FakeClock()).Show();
        Assert.Single(host.Deferred);
        Assert.Equal(0, host.Opens);

        host.Deferred[0]();                            // replay, card still up
        Assert.Single(host.Deferred);                  // not re-deferred
        Assert.Equal((0, 1), (host.Opens, host.Drops));
    }

    [Fact]
    public void Deferred_replay_opens_once_the_card_is_gone_and_busy_queue_waits()
    {
        var host = new Host { LockCard = true };
        var q = new PopQuizScheduler(host, new FakeClock());
        q.Show();
        host.LockCard = false;
        host.Deferred[0]();
        Assert.Equal((1, 0), (host.Opens, host.Drops));

        host.Busy = true;
        q.Show();
        Assert.Equal(2, host.Deferred.Count);
        Assert.Equal(1, host.Opens);

        host.HasQueue = false; host.LockCard = true;   // no queue behind a card: dropped, not shown
        q.Show();
        Assert.Equal(1, host.Opens);
    }
}
