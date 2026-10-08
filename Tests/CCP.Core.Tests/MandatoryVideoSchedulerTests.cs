using System;
using System.Collections.Generic;
using System.Linq;
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
        public readonly List<AttentionVerdict> Messages = new();
        public Action? Then;
        public void ShowMessage(AttentionVerdict verdict, int ms, Action then) { Messages.Add(verdict); Then = then; }
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

    [Fact]   // WPF VideoStarted (VideoService.cs:3370): once the clip is on screen, not at the trigger
    public void VideoStarted_fires_when_the_clip_is_shown() => With(20, false, () =>
    {
        var clock = new FakeClock(); var host = new Host(); var started = 0; string? announced = null;
        var v = new MandatoryVideoScheduler(host, clock, () => Clips);
        v.VideoStarted += () => { started++; announced = v.LastVideoPath; };
        Assert.True(v.Trigger());
        Assert.Equal(0, started);                           // still in the pre-roll
        clock.Advance(TimeSpan.FromSeconds(1.3));
        Assert.Single(host.Shown);
        Assert.Equal(1, started);
        Assert.Equal(host.Shown[0].Path, announced);        // what the media log records (WPF LastVideoPath)
        v.ForceCleanup();
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
    public void Attention_rules_match_WPF()
    {
        var r = new Random(1);
        var t = MandatoryVideoScheduler.AttentionSpawnTimes(8, 30, r);
        Assert.Equal(8, t.Count);
        Assert.True(t[0] >= 3);
        for (var i = 1; i < t.Count; i++) Assert.True(t[i] - t[i - 1] >= 3 - 1e-9, "3 s apart");
        Assert.Equal(3, MandatoryVideoScheduler.AttentionTargetCount(3, false, r));
        Assert.Equal(1, MandatoryVideoScheduler.AttentionTargetCount(0, false, r));
        Assert.InRange(MandatoryVideoScheduler.AttentionTargetCount(5, true, r), 1, 5);
        Assert.Equal(AttentionVerdict.None, MandatoryVideoScheduler.Evaluate(true, 0, 0, 0.5));
        Assert.Equal(AttentionVerdict.None, MandatoryVideoScheduler.Evaluate(false, 3, 0, 0.5));
        Assert.Equal(AttentionVerdict.Fail, MandatoryVideoScheduler.Evaluate(true, 3, 2, 0.5));
        Assert.Equal(AttentionVerdict.Pass, MandatoryVideoScheduler.Evaluate(true, 3, 3, 0.5));
        Assert.Equal(AttentionVerdict.Troll, MandatoryVideoScheduler.Evaluate(true, 3, 3, 0.05));
        Assert.Equal(250, MandatoryVideoScheduler.AttentionPassXp(0));
        Assert.Equal(350, MandatoryVideoScheduler.AttentionPassXp(2));
        Assert.Equal("GOOD\nGIRL", MandatoryVideoScheduler.FormatTriggerText("GOOD GIRL"));
        Assert.Equal("A B\nC D E", MandatoryVideoScheduler.FormatTriggerText("A B C D E"));
        Assert.True(MandatoryVideoScheduler.NeedsBlurFill(4 / 3.0, 16 / 9.0));
        Assert.False(MandatoryVideoScheduler.NeedsBlurFill(16 / 9.0, 16 / 9.0));
    }

    [Fact]
    public void A_missed_check_replays_a_fresh_strict_clip_until_mercy_and_a_catch_pays() => With(60, true, () =>
    {
        var s = CoreSettings.Current;
        var (a, m) = (s.AttentionChecksEnabled, s.MercySystemEnabled);
        (s.AttentionChecksEnabled, s.MercySystemEnabled) = (true, true);
        var xp = new List<double>(); var checks = new List<bool>();
        CoreProgression.AddXPProvider = (x, _) => xp.Add(x);
        CoreProgression.TrackAttentionCheckProvider = checks.Add;
        try
        {
            var clock = new FakeClock(); var host = new Host();
            var v = new MandatoryVideoScheduler(host, clock, () => Clips);
            var bubbles = new List<string>();   // WPF: bubbles held from the first clip until the run ends
            CoreBubbles.PauseAction = () => bubbles.Add("pause");
            CoreBubbles.ResumeAction = () => bubbles.Add("resume");
            v.Trigger(); clock.Advance(MandatoryVideoScheduler.PreRoll);
            Assert.Equal(new[] { "pause" }, bubbles);
            for (var replay = 1; replay <= 2; replay++)
            {
                v.NoteSpawn(); v.NoteSpawn(); v.NoteHit();   // 1 of 2 caught
                v.Ended();
                Assert.False(v.IsPlaying);
                Assert.Equal(AttentionVerdict.Fail, host.Messages[^1]);
                Assert.Equal(replay, v.Penalties);
                host.Then!();                                // the 2 s message ends: a fresh clip, same strictness
                clock.Advance(MandatoryVideoScheduler.PreRoll);
                Assert.Equal(replay + 1, host.Shown.Count);
                Assert.True(host.Shown[^1].Strict);
                Assert.Equal(0, v.AttentionSpawned);
                Assert.DoesNotContain("resume", bubbles);    // not through the verdict or the replay
            }
            v.NoteSpawn(); v.Ended();                        // third miss: mercy, no replay
            Assert.Equal(AttentionVerdict.Mercy, host.Messages[^1]);
            host.Then!();
            Assert.Equal("resume", bubbles[^1]);
            clock.Advance(TimeSpan.FromSeconds(5));
            Assert.Equal(3, host.Shown.Count);
            Assert.Equal(0, v.Penalties);
            Assert.Equal(new[] { false, false, false }, checks);

            xp.Clear(); checks.Clear();
            for (var i = 0; i < 50 && host.Messages.Count == 3; i++)   // a catch pays, 1 in 10 is trolled
            {
                v.Trigger(); clock.Advance(MandatoryVideoScheduler.PreRoll);
                v.NoteSpawn(); v.NoteHit(); v.Ended();
            }
            Assert.Contains(250.0, xp);
            Assert.Contains(15.0, xp);
            Assert.All(checks, Assert.True);

            v.Trigger(); clock.Advance(MandatoryVideoScheduler.PreRoll);   // a stop during the message cancels the replay
            v.NoteSpawn(); v.Ended();
            var shown = host.Shown.Count;
            v.Stop(); host.Then!();
            clock.Advance(TimeSpan.FromHours(1));
            Assert.Equal(shown, host.Shown.Count);
        }
        finally
        {
            (s.AttentionChecksEnabled, s.MercySystemEnabled) = (a, m);
            CoreProgression.AddXPProvider = null;
            CoreProgression.TrackAttentionCheckProvider = null;
            CoreBubbles.PauseAction = CoreBubbles.ResumeAction = null;
        }
    });

    /// <summary>#1145: the Mercy picker moves the threshold (here 2 misses).</summary>
    [Fact]
    public void Mercy_comes_after_the_picked_number_of_misses() => With(60, true, () =>
    {
        var s = CoreSettings.Current;
        var (a, m, n) = (s.AttentionChecksEnabled, s.MercySystemEnabled, s.MercyAfterFails);
        (s.AttentionChecksEnabled, s.MercySystemEnabled, s.MercyAfterFails) = (true, true, 2);
        try
        {
            var clock = new FakeClock(); var host = new Host();
            var v = new MandatoryVideoScheduler(host, clock, () => Clips);
            v.Trigger(); clock.Advance(MandatoryVideoScheduler.PreRoll);
            v.NoteSpawn(); v.Ended();
            Assert.Equal(AttentionVerdict.Fail, host.Messages[^1]);
            host.Then!(); clock.Advance(MandatoryVideoScheduler.PreRoll);
            v.NoteSpawn(); v.Ended();                        // second miss: mercy
            Assert.Equal(AttentionVerdict.Mercy, host.Messages[^1]);
        }
        finally
        {
            (s.AttentionChecksEnabled, s.MercySystemEnabled, s.MercyAfterFails) = (a, m, n);
            CoreBubbles.PauseAction = CoreBubbles.ResumeAction = null;
        }
    });

    [Fact]
    public void A_new_clip_during_the_verdict_message_cancels_the_pending_replay() => With(60, false, () =>
    {
        var s = CoreSettings.Current;
        var (a, m) = (s.AttentionChecksEnabled, s.MercySystemEnabled);
        (s.AttentionChecksEnabled, s.MercySystemEnabled) = (true, true);
        try
        {
            var clock = new FakeClock(); var host = new Host();
            var v = new MandatoryVideoScheduler(host, clock, () => Clips);
            v.Trigger(); clock.Advance(MandatoryVideoScheduler.PreRoll);
            v.NoteSpawn(); v.Ended();                        // missed: the 2 s verdict message is up
            Assert.True(v.Trigger());                        // the Test button in that gap
            clock.Advance(MandatoryVideoScheduler.PreRoll);
            Assert.Equal(2, host.Shown.Count);
            host.Then!();                                    // the message ends: its replay is stale
            clock.Advance(TimeSpan.FromSeconds(5));
            Assert.Equal(2, host.Shown.Count);
            Assert.True(v.IsPlaying);
            Assert.Equal(1, v.Penalties);                    // a stale replay would have run AfterEnd (reset to 0)
        }
        finally { (s.AttentionChecksEnabled, s.MercySystemEnabled) = (a, m); }
    });

    [Fact]
    public void No_videos_is_raised_once_per_launch()
    {
        var v = new MandatoryVideoScheduler(new Host(), new FakeClock(), () => Array.Empty<string>());
        var n = 0;
        v.NoVideos += () => n++;
        v.Trigger(); v.Trigger();
        Assert.Equal(1, n);
    }

    [Fact]
    public void Length_filter_picks_inside_the_range_and_counts_what_it_emptied()
    {
        // WPF RefillVideoQueues' duration filter + #1352 funnel counts; a clip with no cached length is kept.
        var s = CoreSettings.Current;
        var (min, max) = (s.VideoMinDurationSeconds, s.VideoMaxDurationSeconds);
        var lengths = new Dictionary<string, double> { ["/v/short.mp4"] = 20, ["/v/mid.mp4"] = 90, ["/v/long.mp4"] = 400 };
        var files = new[] { "/v/short.mp4", "/v/mid.mp4", "/v/long.mp4", "/v/unparsed.mp4" };
        try
        {
            (s.VideoMinDurationSeconds, s.VideoMaxDurationSeconds) = (52, 170);
            var host = new Host();
            var v = new MandatoryVideoScheduler(host, new FakeClock(), () => files)
                { DurationOf = p => lengths.TryGetValue(p, out var d) ? d : null };
            var picks = new HashSet<string> { v.PickNext()!, v.PickNext()! };
            Assert.Equal(new HashSet<string> { "/v/mid.mp4", "/v/unparsed.mp4" }, picks);
            Assert.Equal((4, 2), (v.LastFunnelEnabled, v.LastFunnelDuration));

            // The filter keeps none of the enabled clips: no pick, and the dialog can blame the filter.
            lengths["/v/unparsed.mp4"] = 30;
            (s.VideoMinDurationSeconds, s.VideoMaxDurationSeconds) = (500, 0);
            var w = new MandatoryVideoScheduler(host, new FakeClock(), () => files) { DurationOf = v.DurationOf };
            var raised = 0;
            w.NoVideos += () => raised++;
            Assert.False(w.Trigger());
            Assert.Equal(1, raised);
            Assert.True(NoVideosReason.LengthFilterEmptied(w.LastFunnelEnabled, w.LastFunnelDuration));
            Assert.Equal("8m 20s - ∞", NoVideosReason.FormatRange(s.VideoMinDurationSeconds, s.VideoMaxDurationSeconds));

            (s.VideoMinDurationSeconds, s.VideoMaxDurationSeconds) = (0, 0);   // unset keeps everything
            Assert.Equal(4, MandatoryVideoScheduler.KeepByLength(files, 0, 0, v.DurationOf).Count);
        }
        finally { (s.VideoMinDurationSeconds, s.VideoMaxDurationSeconds) = (min, max); }
    }

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

    // #875: no live global panic listener -> strict must not trap; panic key and Esc force-stop.
    [Theory]
    [InlineData("F12", VideoKeyAction.ForceStop)]
    [InlineData("Escape", VideoKeyAction.ForceStop)]
    [InlineData("F4", VideoKeyAction.None)]
    public void Strict_without_a_live_panic_listener_falls_open(string key, VideoKeyAction expected) =>
        Assert.Equal(expected, MandatoryVideoScheduler.KeyAction(true, key, false, panicEnabled: true, panicKey: "F12", panicListenerLive: false));

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

    // WPF #735: one grace pause per clip, only while it plays; the same keystroke's second handler
    // is swallowed for 200 ms; the countdown rounds up.
    [Fact]
    public void GracePauseRules()
    {
        Assert.Equal(GraceDecision.Pause, MandatoryVideoScheduler.EvaluateGrace(true, false, false, false, double.MaxValue));
        Assert.Equal(GraceDecision.ConsumedDedup, MandatoryVideoScheduler.EvaluateGrace(true, false, true, true, 150));
        Assert.Equal(GraceDecision.FallThrough, MandatoryVideoScheduler.EvaluateGrace(true, false, true, false, 5000));   // press 2
        Assert.Equal(GraceDecision.FallThrough, MandatoryVideoScheduler.EvaluateGrace(true, false, false, true, 5000));   // spent
        Assert.Equal(GraceDecision.FallThrough, MandatoryVideoScheduler.EvaluateGrace(false, false, false, false, double.MaxValue));
        Assert.Equal(60, MandatoryVideoScheduler.GraceSecondsRemaining(0));
        Assert.Equal(59, MandatoryVideoScheduler.GraceSecondsRemaining(1.2));
        Assert.Equal(0, MandatoryVideoScheduler.GraceSecondsRemaining(61));

        // Which keys try it (WPF SetupStrictHandlers). Strict: Esc only, with a non-Esc panic key and
        // a live listener - the fall-open force-stop keeps Esc when nothing global can stop the clip.
        Assert.Equal(false, MandatoryVideoScheduler.GraceKey(true, "Escape", true, "F12"));
        Assert.Null(MandatoryVideoScheduler.GraceKey(true, "Escape", true, "F12", panicListenerLive: false));
        Assert.Null(MandatoryVideoScheduler.GraceKey(true, "Escape", true, "Escape"));
        Assert.Null(MandatoryVideoScheduler.GraceKey(true, "F12", true, "F12"));
        Assert.Equal(false, MandatoryVideoScheduler.GraceKey(false, "Escape", true, "F12"));
        Assert.Equal(true, MandatoryVideoScheduler.GraceKey(false, "Escape", true, "Escape"));
        Assert.Equal(true, MandatoryVideoScheduler.GraceKey(false, "F12", true, "F12"));
        Assert.Null(MandatoryVideoScheduler.GraceKey(false, "F12", false, "F12"));
    }

    // WPF vout watchdog (8 s), mid-play vout loss (5 s) - both replay once - and the safety timer
    // (+5 s), 600 s fallback and max-length cap on the clock from the first frame.
    [Fact]
    public void ClipGuards()
    {
        string? G(double show, double played, bool framed, double len, double sinceFrame, int max, bool video = true)
            => MandatoryVideoScheduler.Guard(show, played, framed, video, len, sinceFrame, max);
        Assert.Null(G(7.9, 0, false, 0, 0, 0));
        Assert.Equal("no frame", G(8, 0, false, 0, 0, 0));
        Assert.Null(G(30, 30, false, 0, 0, 0, video: false));            // audio-only plays out
        Assert.Null(G(40, 34.9, true, 30, 0, 0));                        // counts from the first frame
        Assert.Equal("overran", G(40, 35, true, 30, 0, 0));
        Assert.Null(G(700, 599, true, 0, 0, 0));
        Assert.Equal("overran", G(600, 600, true, 0, 0, 0));
        Assert.Equal("max length", G(25, 20, true, 30, 0, 20));
        Assert.Null(G(25, 19.9, true, 30, 0, 20));
        Assert.Equal("output lost", G(10, 10, true, 30, 5, 0));
        Assert.Null(G(10, 10, true, 30, 4.9, 0));
        Assert.True(MandatoryVideoScheduler.GuardHeals("no frame", false));
        Assert.True(MandatoryVideoScheduler.GuardHeals("output lost", false));
        Assert.False(MandatoryVideoScheduler.GuardHeals("output lost", true));
        Assert.False(MandatoryVideoScheduler.GuardHeals("max length", false));
    }

    // WPF CompanionService.OnAttentionCheckFailed: only the Trainer's perk costs 25 XP, floored at 0.
    [Fact]
    public void TrainerPerkPaysForAFailedCheck()
    {
        var s = new ConditioningControlPanel.Models.AppSettings();
        var trainer = ConditioningControlPanel.Models.CompanionDefinition.AllCompanions
            .First(c => c.BonusType == ConditioningControlPanel.Models.CompanionBonusType.StrictModeBonus);
        s.ActiveCompanionId = (int)trainer.Id;
        s.ActiveCompanionProgress.CurrentXP = 30;
        Assert.True(ConditioningControlPanel.Services.Companion.CompanionPerks.ApplyAttentionFailPenalty(s));
        Assert.Equal(5, s.ActiveCompanionProgress.CurrentXP);
        Assert.True(ConditioningControlPanel.Services.Companion.CompanionPerks.ApplyAttentionFailPenalty(s));
        Assert.Equal(0, s.ActiveCompanionProgress.CurrentXP);
        var other = ConditioningControlPanel.Models.CompanionDefinition.AllCompanions
            .First(c => c.BonusType != ConditioningControlPanel.Models.CompanionBonusType.StrictModeBonus);
        s.ActiveCompanionId = (int)other.Id;
        s.ActiveCompanionProgress.CurrentXP = 30;
        Assert.False(ConditioningControlPanel.Services.Companion.CompanionPerks.ApplyAttentionFailPenalty(s));
        Assert.Equal(30, s.ActiveCompanionProgress.CurrentXP);
    }

    /// <summary>WPF VideoService.cs:5729 through CoreHaptics: a caught target pulses the toy.</summary>
    [Fact]
    public void Haptics_follow_the_target_hit_and_the_clip() => With(6, false, () =>
    {
        var haptics = new HapticService(new ConditioningControlPanel.Models.HapticSettings());
        var msgs = new List<string>();
        haptics.HapticTriggered += (_, m) => msgs.Add(m);
        var old = CoreHaptics.Service;
        CoreHaptics.Service = haptics;
        try
        {
            var clock = new FakeClock(); var host = new Host();
            var v = new MandatoryVideoScheduler(host, clock, () => Clips);
            v.NoteSpawn(); v.NoteHit();
            Assert.Contains(msgs, m => m.StartsWith("Target Hit"));
        }
        finally { CoreHaptics.Service = old; haptics.Dispose(); }
    });

    /// <summary>The AI's named clip (WPF PlaySpecificVideo(path, strict: false)) plays that clip, not a pick,
    /// and not strict even when Strict Lock is on.</summary>
    [Fact]
    public void TriggerWithAPathPlaysThatClipUnstrict() => With(6, strict: true, () =>
    {
        var host = new Host();
        var clock = new FakeClock();
        var v = new MandatoryVideoScheduler(host, clock, () => Clips);
        Assert.True(v.Trigger(strictOverride: false, path: "/ai/named.mp4"));
        clock.Advance(TimeSpan.FromSeconds(1.3));
        Assert.Equal(new[] { ("/ai/named.mp4", false) }, host.Shown);
        Assert.False(v.Trigger(path: "/ai/other.mp4"));      // one already playing
    });
}
