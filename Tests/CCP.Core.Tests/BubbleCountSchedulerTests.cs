using System;
using System.Collections.Generic;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using Xunit;
using FakeClock = CCP.Core.Tests.PopQuizSchedulerTests.FakeClock;

namespace CCP.Core.Tests;

// BubbleCountScheduler against WPF BubbleCountService: schedule rate, 800 ms lead-in, completion XP
// (duration-scaled, 3-minute cooldown), strict retry with mercy at 3, and panic via CoreEngine.Stop.
[Collection(SessionStatics.Name)]
public sealed class BubbleCountSchedulerTests
{
    private sealed class Host : IBubbleCountHost
    {
        public readonly List<(string Path, int Difficulty, bool Strict, Action<bool> Done)> Shown = new();
        public readonly List<(string Text, Action Then)> Messages = new();
        public int Closes;
        public double LastVideoDurationSeconds { get; set; } = 60;
        public void Show(string path, int difficulty, bool strict, Action<bool> onComplete) => Shown.Add((path, difficulty, strict, onComplete));
        public void ShowMessage(string text, int ms, Action then) => Messages.Add((text, then));
        public void CloseAll() => Closes++;
    }

    private static void With(bool enabled, bool strict, bool mercy, Action body)
    {
        var s = CoreSettings.Current;
        var (e, f, st, m, d) = (s.BubbleCountEnabled, s.BubbleCountFrequency, s.BubbleCountStrictLock, s.MercySystemEnabled, s.BubbleCountDifficulty);
        (s.BubbleCountEnabled, s.BubbleCountFrequency, s.BubbleCountStrictLock, s.MercySystemEnabled, s.BubbleCountDifficulty) = (enabled, 6, strict, mercy, 2);
        var xp = new List<(double, string)>();
        var prevXp = CoreProgression.AddXPProvider;
        CoreProgression.AddXPProvider = (a, src) => xp.Add((a, src));
        try { body(); }
        finally
        {
            CoreProgression.AddXPProvider = prevXp;
            (s.BubbleCountEnabled, s.BubbleCountFrequency, s.BubbleCountStrictLock, s.MercySystemEnabled, s.BubbleCountDifficulty) = (e, f, st, m, d);
        }
    }

    private static readonly string[] Clips = { "/v/a.mp4", "/v/b.mkv" };

    [Fact]
    public void Rules_match_wpf()
    {
        Assert.Equal(360, BubbleCountScheduler.NextIntervalSeconds(100, 0.5));    // clamped to 10/h
        Assert.Equal(2880, BubbleCountScheduler.NextIntervalSeconds(1, 0));        // 3600 * 0.8
        Assert.Equal(100, BubbleCountScheduler.ScaleXpByDuration(100, 90));
        Assert.Equal(50, BubbleCountScheduler.ScaleXpByDuration(100, 30));
        Assert.Equal(10, BubbleCountScheduler.ScaleXpByDuration(100, 1));          // 10% floor
        Assert.Equal(5, BubbleCountScheduler.TargetBubbles(BubbleCountScheduler.Difficulty.Medium, 30, 0.5));
        Assert.Equal(16, BubbleCountScheduler.TargetBubbles(BubbleCountScheduler.Difficulty.Hard, 60, 0.5));
        Assert.Equal(3, BubbleCountScheduler.TargetBubbles(BubbleCountScheduler.Difficulty.Easy, 5, 0));  // min 3
    }

    [Fact]
    public void Schedule_lead_in_and_xp_cooldown() => With(true, false, true, () =>
    {
        var clock = new FakeClock(); var host = new Host { LastVideoDurationSeconds = 30 };
        var b = new BubbleCountScheduler(host, clock, () => Clips);
        var xp = new List<double>();
        CoreProgression.AddXPProvider = (a, _) => xp.Add(a);
        var held = 0;
        CoreBubbles.PauseAction = () => held++;
        CoreBubbles.ResumeAction = () => held--;
        b.Start();
        clock.Advance(TimeSpan.FromSeconds(479));              // 6/h: 480-720 s
        Assert.False(b.IsBusy);
        clock.Advance(TimeSpan.FromSeconds(242));
        Assert.True(b.IsBusy);
        clock.Advance(TimeSpan.FromSeconds(0.8));              // lead-in
        var game = Assert.Single(host.Shown);
        Assert.Equal(2, game.Difficulty);
        Assert.Equal(1, held);                                 // WPF PauseAndClear: ambient bubbles held
        game.Done(true);
        Assert.False(b.IsBusy);
        Assert.Equal(0, held);                                 // and resumed
        Assert.Equal(new[] { 50.0 }, xp);                      // 100 scaled by a 30 s clip

        b.Trigger(forceTest: true);
        clock.Advance(TimeSpan.FromSeconds(1));
        host.Shown[1].Done(true);
        Assert.Single(xp);                                     // inside the 3-minute cooldown
        CoreBubbles.PauseAction = CoreBubbles.ResumeAction = null;
    });

    [Fact]
    public void Strict_failure_retries_then_mercy_after_three() => With(true, true, true, () =>
    {
        var clock = new FakeClock(); var host = new Host();
        var b = new BubbleCountScheduler(host, clock, () => Clips);
        b.Trigger(forceTest: true);
        clock.Advance(TimeSpan.FromSeconds(1));
        for (int i = 0; i < 2; i++)
        {
            host.Shown[^1].Done(false);
            Assert.Equal("WRONG!\nWATCH AGAIN", host.Messages[^1].Text);
            host.Messages[^1].Then();
            Assert.True(host.Shown[^1].Strict);                // the rewatch is always strict
        }
        Assert.Equal(3, host.Shown.Count);
        host.Shown[^1].Done(false);
        Assert.Equal("BAMBI GETS MERCY", host.Messages[^1].Text);
        Assert.True(b.IsBusy);
        host.Messages[^1].Then();
        Assert.False(b.IsBusy);
        Assert.Equal(3, host.Shown.Count);
    });

    [Fact]
    public void Non_strict_failure_ends_and_disabled_drops_scheduled_games() => With(false, false, true, () =>
    {
        var clock = new FakeClock(); var host = new Host();
        var b = new BubbleCountScheduler(host, clock, () => Clips);
        b.Start();                                             // disabled: never runs
        Assert.False(b.IsRunning);
        b.Trigger(forceTest: true);                            // the Test button ignores the flag
        clock.Advance(TimeSpan.FromSeconds(1));
        host.Shown[0].Done(false);
        Assert.False(b.IsBusy);
        Assert.Empty(host.Messages);
    });

    [Fact]
    public void Panic_engine_stop_closes_the_game_and_a_late_answer_is_ignored() => With(true, true, true, () =>
    {
        var clock = new FakeClock(); var host = new Host();
        var b = new BubbleCountScheduler(host, clock, () => Clips);
        var prev = CoreEngine.BubbleCount;
        CoreEngine.BubbleCount = b;
        try
        {
            b.Trigger(forceTest: true);
            clock.Advance(TimeSpan.FromSeconds(1));
            CoreEngine.Stop();
            Assert.Equal(1, host.Closes);
            Assert.False(b.IsBusy);
            host.Shown[0].Done(false);                         // the closing window's own callback
            Assert.Empty(host.Messages);                       // no strict retry after panic

            b.Trigger(forceTest: true);                        // panic during the lead-in
            CoreEngine.Stop();
            clock.Advance(TimeSpan.FromSeconds(5));
            Assert.Single(host.Shown);
        }
        finally { CoreEngine.BubbleCount = prev; }
    });
}
