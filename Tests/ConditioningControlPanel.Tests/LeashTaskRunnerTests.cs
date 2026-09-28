using System;
using System.Collections.Generic;
using ConditioningControlPanel.Services.Leash;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The gate's task runner over a fake app: each kind starts the right feature, counts
/// the right thing, and says Completed exactly once. Since 2026-09-28 it also never sits in a
/// "running" limbo: a panic parks it, a stopped activity or a video that will not play sends it
/// back to idle with the task still pending, and the progress carries over to the next Start.</summary>
public class LeashTaskRunnerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 26, 10, 0, 0, TimeSpan.Zero);
    private static readonly LeashPerson Vex = new("u_vex", "Vex", null);

    private sealed class FakeHost : ILeashTaskHost
    {
        public int LockCardsCompleted { get; set; }
        public bool LockCardOpen { get; set; }
        public int CardsShown;
        public bool CardsOpen = true;
        public bool ShowLockCard()
        {
            CardsShown++;
            if (!CardsOpen) return false;
            LockCardOpen = true;
            return true;
        }

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
        public int BubbleStarts;
        public bool BubblesRunning { get; set; }
        public bool StartBubbles()
        {
            BubbleStarts++;
            BubblesRunning = true;
            return BubblesWereOff;
        }
        public void StopBubbles() { BubbleStops++; BubblesRunning = false; }
        public void Pop() => BubblePopped?.Invoke();

        public bool CanOpen = true;
        public bool Caged = true;
        public List<LeashWatch> Opened = new();
        public LeashWatchSample? Sample;
        public int Ended;
        public bool WindowUp;
        public event Action<LeashWatch>? WatchFinished;
        public event Action<LeashWatch>? WatchFailed;
        public bool OpenWatch(LeashWatch watch) { Opened.Add(watch); WindowUp = CanOpen; return CanOpen; }
        public LeashWatchSample? SampleWatch(LeashWatch watch) => Sample;
        public bool WatchOpen => WindowUp;
        public bool WatchCaged => Caged;
        public void EndWatch() { Ended++; WindowUp = false; }
        public void Finish(LeashWatch w) => WatchFinished?.Invoke(w);
        public void Fail(LeashWatch w) => WatchFailed?.Invoke(w);
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
        public List<LeashTaskStopped> Stopped = new();

        public Rig()
        {
            Runner = new LeashTaskRunner(Host, () => Now);
            Runner.Progress += (p, d, t) => Progress.Add((p, d, t));
            Runner.Completed += Completed.Add;
            Runner.AssignmentWatched += Watched.Add;
            Runner.Stopped += Stopped.Add;
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
        Assert.Empty(r.Stopped);
    }

    [Fact]
    public void Panic_parks_lines_and_no_card_comes_back_by_itself()
    {
        var r = new Rig();
        r.Host.LockCardsCompleted = 10;
        r.Runner.Start(Pun("p1", PunishKind.Lines, 5));
        r.Host.LockCardsCompleted = 12;
        r.Host.LockCardOpen = false;
        r.Advance(1);
        var shown = r.Host.CardsShown;

        // Panic: the ladder dismisses the card, the runner parks.
        r.Runner.Park();
        r.Host.LockCardOpen = false;
        Assert.False(r.Runner.IsRunning);
        for (var i = 0; i < 60; i++) r.Advance(1);
        Assert.Equal(shown, r.Host.CardsShown);
        Assert.Empty(r.Stopped);
        Assert.Empty(r.Completed);

        // Back through the gate later: the two cards already done still count.
        r.Host.LockCardsCompleted = 30;
        Assert.True(r.Runner.Start(Pun("p1", PunishKind.Lines, 5)));
        Assert.Equal(("p1", 2, 5), r.Progress[^1]);
    }

    [Fact]
    public void Cancel_forgets_the_progress()
    {
        var r = new Rig();
        r.Runner.Start(Pun("p1", PunishKind.Bubbles, 10));
        for (var i = 0; i < 4; i++) r.Host.Pop();
        r.Runner.Cancel();
        r.Runner.Start(Pun("p1", PunishKind.Bubbles, 10));
        Assert.Equal(("p1", 0, 10), r.Progress[^1]);
    }

    [Fact]
    public void Lines_stop_when_the_next_card_will_not_open()
    {
        var r = new Rig();
        Assert.True(r.Runner.Start(Pun("p1", PunishKind.Lines, 3)));
        r.Host.LockCardsCompleted++;
        r.Host.LockCardOpen = false;
        r.Host.CardsOpen = false;
        r.Advance(LeashTaskRunner.LockCardRetry.TotalSeconds);
        Assert.False(r.Runner.IsRunning);
        Assert.Equal(new LeashTaskStopped("p1", false, LeashTaskStop.CouldNotContinue), Assert.Single(r.Stopped));
        Assert.Empty(r.Completed);
        Assert.Equal(("p1", 1, 3), r.Progress[^1]);
    }

    [Fact]
    public void Lines_that_cannot_open_a_first_card_do_not_start()
    {
        var r = new Rig();
        r.Host.CardsOpen = false;
        Assert.False(r.Runner.Start(Pun("p1", PunishKind.Lines, 3)));
        Assert.False(r.Runner.IsRunning);
    }

    [Fact]
    public void Pink_session_stopped_by_the_player_sends_the_gate_back_and_keeps_the_minutes()
    {
        var r = new Rig();
        Assert.True(r.Runner.Start(Pun("p2", PunishKind.Pink, 10)));
        Assert.Equal((PunishKind.Pink, 10), r.Host.Sessions[0]);
        for (var i = 0; i < 300; i++) r.Advance(1);
        Assert.Equal(("p2", 5, 10), r.Progress[^1]);

        // They stopped the session: no limbo, the runner goes idle and says so.
        r.Host.SessionRunning = false;
        r.Advance(1);
        Assert.False(r.Runner.IsRunning);
        Assert.Equal(new LeashTaskStopped("p2", false, LeashTaskStop.ActivityStopped), Assert.Single(r.Stopped));
        Assert.Empty(r.Completed);

        // Start again from the gate: a new session, and the five minutes carry over.
        Assert.True(r.Runner.Start(Pun("p2", PunishKind.Pink, 10)));
        Assert.Equal(2, r.Host.Sessions.Count);
        Assert.Equal(("p2", 5, 10), r.Progress[^1]);
        for (var i = 0; i < 290; i++) r.Advance(1);
        Assert.Equal(new[] { "p2" }, r.Completed);
    }

    [Fact]
    public void A_session_that_never_shows_up_stops_after_the_grace()
    {
        var r = new Rig();
        r.Host.SessionStarts = true;
        Assert.True(r.Runner.Start(Pun("p2", PunishKind.Detention, 10)));
        r.Host.SessionRunning = false;
        r.Advance(LeashTaskRunner.StartGrace.TotalSeconds - 2);
        Assert.True(r.Runner.IsRunning);
        r.Advance(3);
        Assert.False(r.Runner.IsRunning);
        Assert.Equal(LeashTaskStop.ActivityStopped, Assert.Single(r.Stopped).Reason);
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
    public void Bubbles_switched_off_stop_the_task_and_keep_the_pops()
    {
        var r = new Rig();
        r.Runner.Start(Pun("p4", PunishKind.Bubbles, 20));
        for (var i = 0; i < 7; i++) r.Host.Pop();
        r.Advance(1);
        r.Host.BubblesRunning = false;
        r.Advance(1);
        Assert.False(r.Runner.IsRunning);
        Assert.Equal(LeashTaskStop.ActivityStopped, Assert.Single(r.Stopped).Reason);
        Assert.True(r.Runner.Start(Pun("p4", PunishKind.Bubbles, 20)));
        Assert.Equal(("p4", 7, 20), r.Progress[^1]);
        Assert.Equal(2, r.Host.BubbleStarts);
    }

    [Fact]
    public void Panic_parks_bubbles_and_stops_the_ones_the_task_started()
    {
        var r = new Rig();
        r.Runner.Start(Pun("p4", PunishKind.Bubbles, 20));
        r.Runner.Park();
        Assert.Equal(1, r.Host.BubbleStops);
        Assert.False(r.Runner.IsRunning);
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
        Assert.Empty(r.Stopped);
    }

    [Fact]
    public void A_video_that_never_plays_is_given_up_after_the_deadline()
    {
        var r = new Rig();
        var w = new LeashWatch("ht", "404", null);
        Assert.True(r.Runner.Start(Pun("p5", PunishKind.Video, 1, w)));
        r.Host.Sample = new LeashWatchSample(0, double.NaN, true);
        for (var i = 0; i < LeashPlayability.Deadline.TotalSeconds - 1; i++) r.Advance(1);
        Assert.True(r.Runner.IsRunning);
        r.Advance(1);
        Assert.False(r.Runner.IsRunning);
        Assert.Equal(new LeashTaskStopped("p5", false, LeashTaskStop.Unplayable), Assert.Single(r.Stopped));
        Assert.Equal(1, r.Host.Ended);
        Assert.Empty(r.Completed);
    }

    [Fact]
    public void A_playing_video_is_never_given_up()
    {
        var r = new Rig();
        var w = new LeashWatch("ht", "ok", null);
        r.Runner.Start(Pun("p5", PunishKind.Video, 1, w));
        r.Host.Sample = new LeashWatchSample(3, 600, true);
        r.Advance(1);
        r.Host.Sample = new LeashWatchSample(3, 600, true); // stalled after it played
        for (var i = 0; i < 60; i++) r.Advance(1);
        Assert.True(r.Runner.IsRunning);
        r.Host.Fail(w);
        Assert.True(r.Runner.IsRunning);
        Assert.Empty(r.Stopped);
    }

    [Fact]
    public void The_deeper_player_is_not_timed_out()
    {
        var r = new Rig();
        r.Host.Caged = false;
        var w = new LeashWatch("catalogue", "abc", null);
        r.Runner.Start(Pun("p6", PunishKind.Video, 1, w));
        for (var i = 0; i < 120; i++) r.Advance(1);
        Assert.True(r.Runner.IsRunning);
    }

    [Fact]
    public void A_page_that_fails_stops_at_once()
    {
        var r = new Rig();
        var w = new LeashWatch("ht", "404", null);
        r.Runner.Start(Pun("p5", PunishKind.Video, 1, w));
        r.Host.Fail(new LeashWatch("ht", "other", null));
        Assert.True(r.Runner.IsRunning);
        r.Host.Fail(w);
        Assert.False(r.Runner.IsRunning);
        Assert.Equal(LeashTaskStop.Unplayable, Assert.Single(r.Stopped).Reason);
    }

    [Fact]
    public void A_closed_video_window_stops_the_task()
    {
        var r = new Rig();
        var w = new LeashWatch("ht", "1", null);
        r.Runner.Start(Pun("p5", PunishKind.Video, 1, w));
        r.Host.WindowUp = false;
        r.Advance(1);
        Assert.False(r.Runner.IsRunning);
        Assert.Equal(LeashTaskStop.ActivityStopped, Assert.Single(r.Stopped).Reason);
    }

    [Fact]
    public void Panic_closes_the_video_window_and_keeps_the_watched_part()
    {
        var r = new Rig();
        var w = new LeashWatch("ht", "1", null);
        r.Runner.Start(Pun("p5", PunishKind.Video, 1, w));
        for (var t = 1; t <= 50; t++) { r.Host.Sample = new LeashWatchSample(t, 100, true); r.Advance(1); }
        var before = r.Progress[^1].Item2;
        r.Runner.Park();
        Assert.Equal(1, r.Host.Ended);
        Assert.False(r.Host.WindowUp);
        Assert.True(r.Runner.Start(Pun("p5", PunishKind.Video, 1, w)));
        Assert.Equal(before, r.Progress[^1].Item2);
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
        Assert.Equal("a1", r.Runner.RunningAid);
        Assert.False(r.Runner.Start(Pun("p1", PunishKind.Lines, 3)));
        r.Host.Finish(w);
        Assert.Equal(new[] { "a1" }, r.Watched);
        Assert.Empty(r.Completed);
        Assert.False(r.Runner.IsRunning);
    }

    [Fact]
    public void A_closed_assignment_window_stops_the_watch_and_says_it_was_the_assignment()
    {
        var r = new Rig();
        var w = new LeashWatch("ht", "9", null);
        var a = new Assignment("a1", AssignKind.Video, 1, w, "20260926", AssignStatus.Open, T0);
        Assert.True(r.Runner.StartAssignmentWatch(a));
        r.Host.WindowUp = false;
        r.Advance(1);
        Assert.Equal(new LeashTaskStopped("a1", true, LeashTaskStop.ActivityStopped), Assert.Single(r.Stopped));
        Assert.Null(r.Runner.RunningAid);
        Assert.Empty(r.Watched);
    }

    [Fact]
    public void Only_open_video_assignments_can_be_watched()
    {
        var r = new Rig();
        var w = new LeashWatch("ht", "9", null);
        Assert.False(r.Runner.StartAssignmentWatch(new Assignment("a1", AssignKind.Minutes, 30, null, "d", AssignStatus.Open, T0)));
        Assert.False(r.Runner.StartAssignmentWatch(new Assignment("a2", AssignKind.Video, 1, w, "d", AssignStatus.Done, T0)));
    }

    [Fact]
    public void Playability_needs_a_length_and_a_position()
    {
        Assert.True(LeashPlayability.Playing(new LeashWatchSample(0.4, 300, false)));
        Assert.False(LeashPlayability.Playing(new LeashWatchSample(0, 300, true)));
        Assert.False(LeashPlayability.Playing(new LeashWatchSample(5, double.NaN, true)));
        Assert.False(LeashPlayability.Playing(new LeashWatchSample(5, 0, true)));
        Assert.False(LeashPlayability.GiveUp(T0, T0.AddSeconds(19), everPlayed: false));
        Assert.True(LeashPlayability.GiveUp(T0, T0.AddSeconds(20), everPlayed: false));
        Assert.False(LeashPlayability.GiveUp(T0, T0.AddMinutes(5), everPlayed: true));
    }
}
