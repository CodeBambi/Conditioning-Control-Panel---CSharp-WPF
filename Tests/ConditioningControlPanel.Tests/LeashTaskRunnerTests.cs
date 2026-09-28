using System;
using System.Collections.Generic;
using ConditioningControlPanel.Services.Leash;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The gate's task runner over a fake app: each kind starts the right feature, counts
/// the right thing, and says Completed exactly once.</summary>
public class LeashTaskRunnerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 26, 10, 0, 0, TimeSpan.Zero);
    private static readonly LeashPerson Vex = new("u_vex", "Vex", null);

    private sealed class FakeHost : ILeashTaskHost
    {
        public int LockCardsCompleted { get; set; }
        public bool LockCardOpen { get; set; }
        public int CardsShown;
        public bool ShowLockCard() { CardsShown++; LockCardOpen = true; return true; }

        public bool SessionRunning { get; set; }
        public List<(PunishKind, int)> Sessions = new();
        public bool SessionStarts = true;
        public bool StartSession(PunishKind kind, int minutes)
        {
            Sessions.Add((kind, minutes));
            if (SessionStarts) SessionRunning = true;
            return SessionStarts;
        }

        public event Action? BubblePopped;
        public bool BubblesWereOff = true;
        public int BubbleStops;
        public bool StartBubbles() => BubblesWereOff;
        public void StopBubbles() => BubbleStops++;
        public void Pop() => BubblePopped?.Invoke();

        public bool CanOpen = true;
        public List<LeashWatch> Opened = new();
        public LeashWatchSample? Sample;
        public int Ended;
        public event Action<LeashWatch>? WatchFinished;
        public bool OpenWatch(LeashWatch watch) { Opened.Add(watch); return CanOpen; }
        public LeashWatchSample? SampleWatch(LeashWatch watch) => Sample;
        public void EndWatch() => Ended++;
        public void Finish(LeashWatch w) => WatchFinished?.Invoke(w);
    }

    private static Punishment Pun(string pid, PunishKind kind, int size, LeashWatch? w = null) =>
        new(pid, kind, size, w, Vex, T0, T0.AddHours(72));

    private sealed class Rig
    {
        public FakeHost Host = new();
        public DateTimeOffset Now = T0;
        public LeashTaskRunner Runner;
        public List<(string, int, int)> Progress = new();
        public List<string> Completed = new();
        public List<string> Watched = new();

        public Rig()
        {
            Runner = new LeashTaskRunner(Host, () => Now);
            Runner.Progress += (p, d, t) => Progress.Add((p, d, t));
            Runner.Completed += Completed.Add;
            Runner.AssignmentWatched += Watched.Add;
        }

        public void Advance(double seconds)
        {
            Now = Now.AddSeconds(seconds);
            Runner.Tick();
        }
    }

    [Fact]
    public void Lines_put_cards_up_until_n_are_done()
    {
        var r = new Rig();
        r.Host.LockCardsCompleted = 40;
        Assert.True(r.Runner.Start(Pun("p1", PunishKind.Lines, 3)));
        Assert.Equal("p1", r.Runner.RunningPid);
        Assert.Equal(1, r.Host.CardsShown);
        Assert.Equal(("p1", 0, 3), r.Progress[^1]);

        for (var i = 1; i <= 3; i++)
        {
            r.Host.LockCardsCompleted++;
            r.Host.LockCardOpen = false;
            r.Advance(1);
            if (i < 3)
            {
                r.Advance(LeashTaskRunner.LockCardRetry.TotalSeconds);
                Assert.Equal(i + 1, r.Host.CardsShown);
            }
        }
        Assert.Equal(new[] { "p1" }, r.Completed);
        Assert.False(r.Runner.IsRunning);
        r.Advance(10);
        Assert.Single(r.Completed);
    }

    [Fact]
    public void Pink_session_counts_only_minutes_while_the_session_runs()
    {
        var r = new Rig();
        Assert.True(r.Runner.Start(Pun("p2", PunishKind.Pink, 10)));
        Assert.Equal((PunishKind.Pink, 10), r.Host.Sessions[0]);
        for (var i = 0; i < 300; i++) r.Advance(1);
        Assert.Equal(("p2", 5, 10), r.Progress[^1]);

        // They stopped the session: nothing counts, the task waits, and Start resumes it.
        r.Host.SessionRunning = false;
        for (var i = 0; i < 120; i++) r.Advance(1);
        Assert.Equal(("p2", 5, 10), r.Progress[^1]);
        Assert.Empty(r.Completed);
        Assert.True(r.Runner.Start(Pun("p2", PunishKind.Pink, 10)));
        Assert.Equal(2, r.Host.Sessions.Count);

        for (var i = 0; i < 290; i++) r.Advance(1);
        Assert.Equal(new[] { "p2" }, r.Completed);
    }

    [Fact]
    public void Detention_session_is_its_own_kind()
    {
        var r = new Rig();
        Assert.True(r.Runner.Start(Pun("p3", PunishKind.Detention, 20)));
        Assert.Equal((PunishKind.Detention, 20), r.Host.Sessions[0]);
    }

    [Fact]
    public void A_session_that_will_not_start_is_not_a_task()
    {
        var r = new Rig();
        r.Host.SessionStarts = false;
        Assert.False(r.Runner.Start(Pun("p3", PunishKind.Detention, 10)));
        Assert.False(r.Runner.IsRunning);
    }

    [Fact]
    public void Bubbles_count_pops_and_stop_the_bubbles_the_task_started()
    {
        var r = new Rig();
        Assert.True(r.Runner.Start(Pun("p4", PunishKind.Bubbles, 50)));
        for (var i = 0; i < 49; i++) r.Host.Pop();
        Assert.Empty(r.Completed);
        r.Host.Pop();
        Assert.Equal(new[] { "p4" }, r.Completed);
        Assert.Equal(1, r.Host.BubbleStops);
        r.Host.Pop();
        Assert.Single(r.Completed);
    }

    [Fact]
    public void Bubbles_already_on_are_left_on()
    {
        var r = new Rig();
        r.Host.BubblesWereOff = false;
        r.Runner.Start(Pun("p4", PunishKind.Bubbles, 50));
        r.Runner.Cancel();
        Assert.Equal(0, r.Host.BubbleStops);
    }

    [Fact]
    public void Video_completes_on_a_real_watch()
    {
        var r = new Rig();
        var w = new LeashWatch("ht", "123", null);
        Assert.True(r.Runner.Start(Pun("p5", PunishKind.Video, 1, w)));
        Assert.Equal(w, r.Host.Opened[0]);
        for (var t = 0; t <= 90; t++)
        {
            r.Host.Sample = new LeashWatchSample(t, 100, true);
            r.Advance(1);
        }
        Assert.Equal(new[] { "p5" }, r.Completed);
        Assert.Equal(("p5", 100, 100), r.Progress[^1]);
        Assert.True(r.Host.Ended >= 1);
    }

    [Fact]
    public void Catalogue_video_completes_on_its_own_finish()
    {
        var r = new Rig();
        var w = new LeashWatch("catalogue", "abc", null);
        r.Runner.Start(Pun("p6", PunishKind.Video, 1, w));
        r.Host.Finish(new LeashWatch("catalogue", "other", null));
        Assert.Empty(r.Completed);
        r.Host.Finish(w);
        Assert.Equal(new[] { "p6" }, r.Completed);
    }

    [Fact]
    public void A_video_that_cannot_open_is_not_a_task()
    {
        var r = new Rig();
        r.Host.CanOpen = false;
        Assert.False(r.Runner.Start(Pun("p7", PunishKind.Video, 1, new LeashWatch("catalogue", "abc", null))));
        Assert.False(r.Runner.IsRunning);
    }

    [Fact]
    public void Chaster_never_runs_and_one_task_at_a_time()
    {
        var r = new Rig();
        Assert.False(r.Runner.Start(Pun("pc", PunishKind.Chaster, 900)));
        Assert.True(r.Runner.Start(Pun("p1", PunishKind.Lines, 3)));
        Assert.False(r.Runner.Start(Pun("p2", PunishKind.Lines, 5)));
        r.Runner.Cancel();
        Assert.False(r.Runner.IsRunning);
        Assert.True(r.Runner.Start(Pun("p2", PunishKind.Lines, 5)));
    }

    [Fact]
    public void Assignment_watch_reports_the_aid_and_never_completes_a_punishment()
    {
        var r = new Rig();
        var w = new LeashWatch("catalogue", "abc", null);
        var a = new Assignment("a1", AssignKind.Video, 1, w, "20260926", AssignStatus.Open, T0);
        Assert.True(r.Runner.StartAssignmentWatch(a));
        Assert.False(r.Runner.Start(Pun("p1", PunishKind.Lines, 3)));
        r.Host.Finish(w);
        Assert.Equal(new[] { "a1" }, r.Watched);
        Assert.Empty(r.Completed);
        Assert.False(r.Runner.IsRunning);
    }
}
