using System;
using System.Collections.Generic;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using Xunit;
using FakeClock = CCP.Core.Tests.PopQuizSchedulerTests.FakeClock;

namespace CCP.Core.Tests;

// MandatoryVideoScheduler against WPF VideoService: ScheduleNext rate, 1.3 s pre-roll, Cleanup
// re-arms / ForceCleanup does not, watch credit, strict keys, and the CoreEngine start/stop.
[Collection(SessionStatics.Name)]
public sealed class MandatoryVideoSchedulerTests
{
    private sealed class Host : IMandatoryVideoHost
    {
        public readonly List<(string Path, bool Strict)> Shown = new();
        public int Closes;
        public double Watched;
        public void Show(string path, bool strict) => Shown.Add((path, strict));
        public double CloseAll() { Closes++; return Watched; }
    }

    private static void With(int perHour, bool strict, Action body)
    {
        var s = CoreSettings.Current;
        var (e, p, st, f) = (s.MandatoryVideosEnabled, s.VideosPerHour, s.StrictLockEnabled, s.FlashEnabled);
        (s.MandatoryVideosEnabled, s.VideosPerHour, s.StrictLockEnabled, s.FlashEnabled) = (true, perHour, strict, false);
        try { body(); }
        finally { (s.MandatoryVideosEnabled, s.VideosPerHour, s.StrictLockEnabled, s.FlashEnabled) = (e, p, st, f); }
    }

    private static readonly string[] Clips = { "/v/a.mp4", "/v/b.mkv" };

    [Fact]
    public void Rate_follows_videos_per_hour_with_the_pre_roll_and_End_rearms() => With(6, false, () =>
    {
        var clock = new FakeClock(); var host = new Host();
        var v = new MandatoryVideoScheduler(host, clock, () => Clips);
        v.Start();
        clock.Advance(TimeSpan.FromSeconds(479));          // earliest: 600 s * 0.8
        Assert.False(v.IsPlaying);
        var waited = 479;
        while (!v.IsPlaying) { clock.Advance(TimeSpan.FromSeconds(1)); waited++; }
        Assert.InRange(waited, 480, 721);                   // latest: 600 s * 1.2
        Assert.Empty(host.Shown);                           // still in the 1.3 s pre-roll
        clock.Advance(TimeSpan.FromSeconds(1.3));
        Assert.Single(host.Shown);
        Assert.Contains(host.Shown[0].Path, Clips);
        Assert.False(host.Shown[0].Strict);

        clock.Advance(TimeSpan.FromHours(1));               // nothing is scheduled while one plays
        Assert.Single(host.Shown);
        v.End();                                            // natural end / Esc: next one is scheduled
        Assert.Equal(1, host.Closes);
        clock.Advance(TimeSpan.FromSeconds(722));
        Assert.Equal(2, host.Shown.Count);
        Assert.NotEqual(host.Shown[0].Path, host.Shown[1].Path);   // a shuffled queue, not a re-draw

        v.Stop();                                           // engine stop / panic: closes, no more
        Assert.Equal(2, host.Closes);
        clock.Advance(TimeSpan.FromHours(5));
        Assert.Equal(2, host.Shown.Count);
    });

    [Fact]
    public void ForceCleanup_does_not_rearm_and_cancels_a_pending_pre_roll() => With(20, true, () =>
    {
        var clock = new FakeClock(); var host = new Host();
        var v = new MandatoryVideoScheduler(host, clock, () => Clips);
        Assert.True(v.Trigger());                           // the Test button: no schedule running
        Assert.True(v.IsStrict);                            // StrictLockEnabled at trigger time
        v.ForceCleanup();
        clock.Advance(TimeSpan.FromHours(2));
        Assert.Empty(host.Shown);                           // the pre-roll died with its run
        Assert.False(v.IsPlaying);

        v.Start();                                          // 20/h: 144-216 s
        clock.Advance(TimeSpan.FromSeconds(217.3));
        Assert.Single(host.Shown);
        v.ForceCleanup();                                   // in-window panic key
        clock.Advance(TimeSpan.FromHours(2));
        Assert.Single(host.Shown);                          // WPF ForceCleanup never re-arms
        v.Stop();
    });

    [Fact]
    public void Empty_library_starts_nothing_and_keeps_the_schedule_alive() => With(60, false, () =>
    {
        var clock = new FakeClock(); var host = new Host();
        var v = new MandatoryVideoScheduler(host, clock, () => Array.Empty<string>());
        v.Start();
        Assert.False(v.Trigger());
        clock.Advance(TimeSpan.FromHours(1));
        Assert.Empty(host.Shown);
        Assert.True(v.IsRunning);
        v.Stop();
    });

    [Fact]
    public void Watch_credit_goes_to_progression_from_one_second()
    {
        var credited = new List<double>();
        CoreProgression.TrackVideoWatchedProvider = credited.Add;
        try
        {
            var host = new Host { Watched = 42.5 };
            var v = new MandatoryVideoScheduler(host, new FakeClock(), () => Clips);
            v.Trigger(); v.End();
            host.Watched = 0.5;
            v.Trigger(); v.ForceCleanup();
            Assert.Equal(new[] { 42.5 }, credited);
        }
        finally { CoreProgression.TrackVideoWatchedProvider = null; }
    }

    [Theory]
    [InlineData(true, "F12", false, VideoKeyAction.Swallow)]     // the panic key
    [InlineData(true, "F4", true, VideoKeyAction.Swallow)]       // Alt+F4
    [InlineData(true, "System", false, VideoKeyAction.Swallow)]
    [InlineData(true, "Escape", false, VideoKeyAction.None)]
    [InlineData(false, "Escape", false, VideoKeyAction.Dismiss)]
    [InlineData(false, "F12", false, VideoKeyAction.ForceStop)]
    [InlineData(false, "A", false, VideoKeyAction.None)]
    public void Strict_keys(bool strict, string key, bool alt, VideoKeyAction expected) =>
        Assert.Equal(expected, MandatoryVideoScheduler.KeyAction(strict, key, alt, panicEnabled: true, panicKey: "F12"));

    [Fact]
    public void Pure_rules_match_WPF()
    {
        Assert.Equal(60, MandatoryVideoScheduler.NextIntervalSeconds(100, 0));   // floored at 60 s
        Assert.Equal(3600 * 1.2, MandatoryVideoScheduler.NextIntervalSeconds(0, 1), 6);   // perHour < 1 is 1
        Assert.False(MandatoryVideoScheduler.ShouldFillSecondaryMonitors(1, true));
        Assert.True(MandatoryVideoScheduler.ShouldFillSecondaryMonitors(2, false));
        Assert.False(MandatoryVideoScheduler.ShouldFillSecondaryMonitors(3, false));
        Assert.Equal(25, MandatoryVideoScheduler.EffectiveVolume(50, 50));
        Assert.True(MandatoryVideoScheduler.IsSupportedVideoExtension("x/Clip.MP4"));
        Assert.False(MandatoryVideoScheduler.IsSupportedVideoExtension("x/clip.gif"));
    }

    [Fact]
    public void Engine_starts_and_stops_it_by_the_saved_flag() => With(6, false, () =>
    {
        var s = CoreSettings.Current;
        var host = new Host();
        CoreEngine.Video = new MandatoryVideoScheduler(host, new FakeClock(), () => Clips);
        try
        {
            CoreEngine.Start();
            Assert.True(CoreEngine.Video.IsRunning);
            CoreEngine.Video.Trigger();
            s.MandatoryVideosEnabled = false;
            CoreEngine.Reconcile();                         // WPF ReconcileRunningServices
            Assert.False(CoreEngine.Video.IsRunning);
            Assert.Equal(1, host.Closes);
            s.MandatoryVideosEnabled = true;
            CoreEngine.ApplyLive("video", true);
            Assert.True(CoreEngine.Video.IsRunning);
            CoreEngine.Stop();
            Assert.False(CoreEngine.Video.IsRunning);
        }
        finally { CoreEngine.Stop(); CoreEngine.Video = null; }
    });
}
