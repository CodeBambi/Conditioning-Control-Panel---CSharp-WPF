using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Skia;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Games.BackRoom;
using ConditioningControlPanel.Models;
using Newtonsoft.Json.Linq;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;
using CoreIntensity = ConditioningControlPanel.Services.BackRoom.BackRoomFxIntensity;

namespace CCP.Avalonia.Tests;

/// <summary>Back Room host parity (hunt2 HB3, HB4, HB7, HB8, HB9, HB10): the room's Options reach the
/// settings, init and the settings frame carry the WPF projection, the word speaks recorded clips only,
/// the toy limiter holds, a slot line maps to its tab row, and an fx primitive with no overlay on this
/// head is acked skipped instead of claimed.</summary>
public sealed class BackRoomWireTests
{
    private static BackRoomBridge.RoomOption Opt(string key, object value)
        => BackRoomBridge.ReadRoomOption(new JObject { ["type"] = "room-option", ["key"] = key, ["value"] = JToken.FromObject(value) })!;

    [Fact]
    public void ApplyRoomOption_WritesEachKnownOption()
    {
        var s = new AppSettings();
        BackRoomWire.ApplyRoomOption(s, Opt("tunnel", true));
        BackRoomWire.ApplyRoomOption(s, Opt("melt", true));
        BackRoomWire.ApplyRoomOption(s, Opt("invertLook", true));
        BackRoomWire.ApplyRoomOption(s, Opt("welcomeSeen", true));
        BackRoomWire.ApplyRoomOption(s, Opt("intensity", "full"));
        BackRoomWire.ApplyRoomOption(s, Opt("mediaSource", "local"));
        BackRoomWire.ApplyRoomOption(s, Opt("subVolume", 40));
        BackRoomWire.ApplyRoomOption(s, Opt("sfxVolume", 50));
        BackRoomWire.ApplyRoomOption(s, Opt("musicVolume", 60));
        Assert.True(s.BackRoomTunnel);
        Assert.True(s.BackRoomMelt);
        Assert.True(s.BackRoomInvertLook);
        Assert.True(s.BackRoomWelcomeSeen);
        Assert.Equal(CoreIntensity.Full, s.BackRoomFxIntensity);
        Assert.Equal("local", s.BackRoomMediaSource);
        Assert.Equal((40, 50, 60), (s.BackRoomSubVolume, s.BackRoomSfxVolume, s.BackRoomMusicVolume));
    }

    [Fact]
    public void ApplyRoomOption_BreakoutKeepsItsOwnThreeLevels()
    {
        var s = new AppSettings();
        int room = s.BackRoomSfxVolume;
        BackRoomWire.ApplyRoomOption(s, Opt("sfxVolume", 7), breakout: true);
        Assert.Equal(7, s.BreakoutSfxVolume);
        Assert.Equal(room, s.BackRoomSfxVolume);
    }

    [Fact]
    public void Niches_OnePerPress_CaseInsensitive_Capped_OffKeepsItsPlace()
    {
        var s = new AppSettings { BackRoomMediaSubs = new List<string>(), BackRoomMediaSubsOff = new List<string>() };
        BackRoomWire.ApplyRoomOption(s, Opt("mediaSubAdd", "EroticHypnosis"));
        BackRoomWire.ApplyRoomOption(s, Opt("mediaSubAdd", "erotichypnosis"));
        Assert.Single(s.BackRoomMediaSubs);
        BackRoomWire.ApplyRoomOption(s, Opt("mediaSubToggle", "EROTICHYPNOSIS"));
        Assert.Single(s.BackRoomMediaSubsOff);
        Assert.Single(s.BackRoomMediaSubs);
        BackRoomWire.ApplyRoomOption(s, Opt("mediaSubToggle", "not_in_list"));
        Assert.Single(s.BackRoomMediaSubsOff);
        BackRoomWire.ApplyRoomOption(s, Opt("mediaSubRemove", "erotichypnosis"));
        Assert.Empty(s.BackRoomMediaSubs);
        Assert.Empty(s.BackRoomMediaSubsOff);
        for (int i = 0; i < AppSettings.BackRoomMediaSubCap + 3; i++) BackRoomWire.ApplyRoomOption(s, Opt("mediaSubAdd", "niche" + i));
        Assert.Equal(AppSettings.BackRoomMediaSubCap, s.BackRoomMediaSubs.Count);
    }

    [Fact]
    public void Gates_AreAlwaysOn_WhateverThePanelToggles()
    {
        var s = new AppSettings { FlashEnabled = false, SubliminalEnabled = false, BrainDrainEnabled = false };
        var g = JObject.FromObject(BackRoomWire.GatesWire(s));
        Assert.True((bool)g["flash"]!);
        Assert.True((bool)g["subliminal"]!);
        Assert.True((bool)g["spiral"]!);
        Assert.True((bool)g["brainDrain"]!);
        Assert.DoesNotContain(nameof(AppSettings.FlashEnabled), BackRoomWire.SettingsFrameProperties);
    }

    [Fact]
    public void Intensity_IsCalmBelowFullMotion_AndTheChoiceStillShows()
    {
        var s = new AppSettings { BackRoomFxIntensity = CoreIntensity.Full };
        Assert.Equal("full", BackRoomWire.IntensityWire(s, MotionLevel.Full));
        Assert.Equal("calm", BackRoomWire.IntensityWire(s, MotionLevel.Reduced));
        Assert.Equal("full", BackRoomWire.IntensityChoiceWire(s));
    }

    [Fact]
    public void Media_OnlineWithoutConsent_ReadsLocal()
    {
        var s = new AppSettings { BackRoomMediaSource = "online" };
        var m = JObject.FromObject(BackRoomWire.MediaWire(s));
        if (!s.HasRemoteMediaConsent) Assert.Equal("local", (string?)m["effective"]);
        Assert.Equal("online", (string?)m["source"]);
        Assert.Equal(AppSettings.BackRoomMediaSubCap, (int)m["cap"]!);
    }

    [Fact]
    public void Audio_IsZeroToOne_AndBreakoutReadsItsOwn()
    {
        var s = new AppSettings { BackRoomSubVolume = 50, BreakoutSubVolume = 20 };
        Assert.Equal(0.5, (double)JObject.FromObject(BackRoomWire.AudioWire(s))["sub"]!, 3);
        Assert.Equal(0.2, (double)JObject.FromObject(BackRoomWire.AudioWire(s, breakout: true))["sub"]!, 3);
    }

    [Theory]
    [InlineData("melt", "melt")]
    [InlineData("emi3", "jackpot")]
    [InlineData("cherry", null)]
    [InlineData(null, null)]
    public void SlotLine_MapsOnlyMeltAndTheJackpot(string? line, string? row)
        => Assert.Equal(row, BackRoomWire.SlotLineRow(line));

    [Fact]
    public void Voice_PlayerClipThenPreset_ElseSilent()
    {
        var played = new List<string>();
        int stops = 0;
        var voice = new BackRoomVoice(t => t == "Mine" ? "mine.mp3" : null, k => k == "let go" ? "let-go.mp3" : null,
            p => { played.Add(p); return 420; }, () => stops++);
        Assert.Equal(new BackRoomVoiceAck("clip", 420), voice.Speak("Mine", false, 1));
        Assert.Equal(new BackRoomVoiceAck("preset", 420), voice.Speak("Let Go!", false, 1));
        // No recording: the word stays silent. Nothing synthetic ever speaks it.
        Assert.Equal(new BackRoomVoiceAck("none", 0), voice.Speak("Unrecorded phrase", false, 1));
        Assert.Equal(new[] { "mine.mp3", "let-go.mp3" }, played);
        voice.Stop();
        Assert.Equal(1, stops);
    }

    [Fact]
    public void Voice_AMutedRoomStillOwnsTheWord()
    {
        var voice = new BackRoomVoice(_ => "a.mp3", _ => null, _ => -1, () => { });
        Assert.Equal(new BackRoomVoiceAck("clip", 0), voice.Speak("word", false, 0));
    }

    [Fact]
    public void HapticLimiter_HoldsTheGapAndTheWindow()
    {
        var l = new BackRoomHapticDirector.Limiter();
        Assert.True(l.Accept(0));
        Assert.False(l.Accept(50));
        int ok = 1;
        for (long t = 100; t < 1000; t += 100) if (l.Accept(t)) ok++;
        Assert.Equal(BackRoomHapticDirector.Limiter.MaxPerWindow, ok);
        Assert.True(l.Accept(1100));
        Assert.Equal(3, BackRoomHapticDirector.PriorityFor(0.9));
        Assert.Equal(BackRoomBridge.HapticMaxMs, BackRoomHapticDirector.DurationFor(1200, buttplug: true));
    }

    [Fact]
    public void WashEnvelope_RisesThenFalls_AndEnds()
    {
        Assert.Equal(0, BackRoomFxHead.WashEnvelope(-1));
        Assert.Equal(0.5, BackRoomFxHead.WashEnvelope(BackRoomFxHead.WashRiseMs / 2.0), 3);
        Assert.True(BackRoomFxHead.WashEnvelope(200) > BackRoomFxHead.WashEnvelope(600));
        Assert.Equal(0, BackRoomFxHead.WashEnvelope(BackRoomFxPlan.WashMs));
    }

    // ---- the dispatcher with this head's supported set ----

    private sealed class Recorder : IBackRoomFxSink
    {
        public readonly List<string> Calls = new();
        public void FlashBurst(int amount, double opacity, int gapMs) => Calls.Add("flash");
        public void GifRain(int count, int durationMs, double opacity) => Calls.Add("rain");
        public void GlitchWash(int durationMs, double opacity) => Calls.Add("glitch");
        public void Subliminal(string text, double opacity) => Calls.Add("sub");
        public void BrainDrain(int durationMs, double level, bool melt) => Calls.Add(melt ? "melt" : "haze");
        public void GifFull(BackRoomGif gif, int durationMs, double opacity, Action shown) => Calls.Add("gif-full");
        public void Wash(FxRgb color, double peak, BackRoomGif? picture, Action shown) => Calls.Add("wash");
        public bool GifFrom(BackRoomGif gif, FxCssRect? from, int durationMs, double scale, double dim, Action shown) { Calls.Add("gif-from"); return true; }
        public void SpiralLoom(string gifPath, int durationMs, double alpha, bool hold, bool slow) => Calls.Add("spiral");
        public void ReleaseSpiralLoom() => Calls.Add("spiral-release");
        public void ReleaseBrainDrain() => Calls.Add("drain-release");
        public void Tunnel(double level) => Calls.Add("tunnel");
        public void CancelTunnel() => Calls.Add("tunnel-cancel");
        public void StopAll() => Calls.Add("stop");
    }

    private sealed class ManualScheduler : IFxScheduler
    {
        private readonly List<(long At, Action Run, Handle H)> _due = new();
        public long NowMs { get; private set; }
        public IDisposable After(int delayMs, Action action)
        {
            var h = new Handle();
            _due.Add((NowMs + delayMs, action, h));
            return h;
        }
        public void Advance(int ms)
        {
            long until = NowMs + ms;
            while (true)
            {
                var next = _due.Where(d => d.At <= until).OrderBy(d => d.At).FirstOrDefault();
                if (next.Run == null) break;
                _due.Remove(next);
                NowMs = Math.Max(NowMs, next.At);
                if (!next.H.Cancelled) next.Run();
            }
            NowMs = until;
        }
        private sealed class Handle : IDisposable { public bool Cancelled; public void Dispose() => Cancelled = true; }
    }

    private static BackRoomMediaDeal Deal() => new NullBackRoomMedia(null).Deal("slot", 1);

    private static (BackRoomFx Fx, Recorder Sink, ManualScheduler Clock) Dispatcher(BackRoomFxIntensity intensity = BackRoomFxIntensity.Normal)
    {
        var sink = new Recorder();
        var clock = new ManualScheduler();
        var fx = new BackRoomFx(sink, clock, () => new FxEnvironment(MotionLevel.Full, intensity, null), new Random(3),
            supports: p => BackRoomFxHead.Supported.Contains(p));
        return (fx, sink, clock);
    }

    [Fact]
    public void Fx_ASupportedPrimitivePlays_AndIsAckedFired()
    {
        var (fx, sink, clock) = Dispatcher();
        var ack = fx.Fire("fx.sub_single", "slot", new[] { "sub0" }, Deal());
        Assert.Contains("sub-single", ack.Fired);
        clock.Advance(5000);
        Assert.Contains("sub", sink.Calls);
    }

    [Fact]
    public void Fx_TheStorm_ReachesTheSinkWhole_NothingAckedSkippedUnknown()
    {
        var (fx, sink, clock) = Dispatcher();
        // The storm is a flash burst, the glitch wash and the gif rain: all three have an overlay on this head (k14: the cascade).
        var ack = fx.Fire("fx.gif_storm", "slot", new[] { "gif0" }, Deal());
        clock.Advance(20000);
        Assert.Contains("flash-burst", ack.Fired);
        Assert.Contains("gif-rain", ack.Fired);
        Assert.Contains("flash", sink.Calls);
        Assert.Contains("rain", sink.Calls);
        Assert.Contains("glitch", sink.Calls);
        Assert.DoesNotContain(ack.Skipped, s => s.Why == BackRoomFxSkipReason.Unknown);
    }

    [Fact]
    public void Fx_CancelAll_DropsWaitingOnsets_AndStopsTheSink()
    {
        var (fx, sink, clock) = Dispatcher();
        fx.Fire("fx.sub_cascade", "slot", new[] { "sub0", "sub1", "sub2" }, Deal());
        fx.CancelAll();
        Assert.Equal(0, fx.PendingCount);
        int before = sink.Calls.Count(c => c == "sub");
        clock.Advance(20000);
        Assert.Equal(before, sink.Calls.Count(c => c == "sub"));
        Assert.Contains("stop", sink.Calls);
    }

    [Fact]
    public void Fx_UnknownId_IsSkippedUnknown()
    {
        var (fx, _, _) = Dispatcher();
        var ack = fx.Fire("fx.nope", "slot", Array.Empty<string>(), Deal());
        Assert.Empty(ack.Fired);
        Assert.Contains(ack.Skipped, s => s.Why == BackRoomFxSkipReason.Unknown);
    }
}

/// <summary>The open room: init carries the WPF projection, a setting change pushes one full settings
/// frame, a room-option lands in the settings, and the race pauses under a mandatory video (HB11).</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class BackRoomHostWindowTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static (GameWindow Window, List<JObject> Posted) OpenRoom()
    {
        EnsureApp();
        GameWindow.RoomVoiceFactory = _ => NullBackRoomVoice.Instance;
        var w = new GameWindow(GameWindow.Games["backroom"]);
        var posted = new List<JObject>();
        w.Posted += json => posted.Add(JObject.Parse(json));
        w.Show();
        return (w, posted);
    }

    [Fact]
    public async Task Init_CarriesGatesMediaAudioChoiceLexAndOwnedTracks()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var (w, posted) = OpenRoom();
            try
            {
                w.HandleMessage("{\"type\":\"ready\"}");
                var init = posted.Single(p => (string?)p["type"] == "init");
                Assert.True((bool)init["gates"]!["flash"]!);
                Assert.NotNull(init["media"]!["effective"]);
                Assert.InRange((double)init["audio"]!["music"]!, 0, 1);
                Assert.Contains((string?)init["intensityChoice"], new[] { "calm", "normal", "full" });
                var lex = (JObject)init["lex"]!;
                Assert.NotEmpty(lex.Properties());
                Assert.All(lex.Properties(), p => Assert.StartsWith("br_", p.Name));
                Assert.Equal(RaceWindow.OwnedTracks().Length, ((JArray)init["racingTracks"]!).Count);
            }
            finally { w.Close(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task ASettingChange_PushesOneFullSettingsFrame_AndARoomOptionLandsInTheSettings()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var s = CoreSettings.Current;
            bool invert = s.BackRoomInvertLook, tunnel = s.BackRoomTunnel;
            var (w, posted) = OpenRoom();
            try
            {
                w.HandleMessage("{\"type\":\"ready\"}");
                s.BackRoomInvertLook = !invert;
                var frame = posted.Last(p => (string?)p["type"] == "settings");
                Assert.Equal(!invert, (bool)frame["invertLook"]!);
                Assert.NotNull(frame["gates"]);
                Assert.NotNull(frame["media"]);
                Assert.NotNull(frame["audio"]);

                int before = posted.Count(p => (string?)p["type"] == "settings");
                w.HandleMessage("{\"type\":\"room-option\",\"key\":\"tunnel\",\"value\":" + (!tunnel ? "true" : "false") + "}");
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(!tunnel, s.BackRoomTunnel);
                Assert.True(posted.Count(p => (string?)p["type"] == "settings") > before);
                Assert.Equal(!tunnel, (bool)posted.Last(p => (string?)p["type"] == "settings")["gates"]!["tunnel"]!);
            }
            finally
            {
                w.Close();
                s.BackRoomInvertLook = invert;
                s.BackRoomTunnel = tunnel;
                CoreSettings.SaveImmediate();
                GameWindow.RoomVoiceFactory = breakout => new BackRoomVoice(breakout);
            }
            return Task.CompletedTask;
        });
    }

    private sealed class CountingFx : IBackRoomFx
    {
        public int Cancels;
        public readonly List<string> Released = new();
        public BackRoomFxAck Fire(string fxId, string station, IReadOnlyList<string> symbolKeys, BackRoomMediaDeal deal) =>
            new(Array.Empty<string>(), Array.Empty<BackRoomFxSkip>());
        public void ReleaseStation(string station) => Released.Add(station);
        public void CancelAll() => Cancels++;
    }

    /// <summary>IB6: the room and a Breakout window share one fx head. Closing one never blanks the
    /// other's host or cancels the other's effects; the last one out (and so a panic) stops everything.</summary>
    [Fact]
    public async Task TwoRoomWindows_ClosingOneLeavesTheOthersEffectsAndHost()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var inner = new CountingFx();
            var (room, breakout) = (new global::Avalonia.Controls.Border(), new global::Avalonia.Controls.Border());
            var roomFx = BackRoomFxHead.Attach(room, inner);
            var breakoutFx = BackRoomFxHead.Attach(breakout, inner);
            try
            {
                Assert.Same(breakout, BackRoomFxHead.Host);            // the newest window hosts
                roomFx.Fire("fx.x", "slot", Array.Empty<string>(), null!);
                breakoutFx.Fire("fx.x", "breakout", Array.Empty<string>(), null!);

                breakoutFx.CancelAll();                                  // Breakout closes first
                BackRoomFxHead.Detach(breakout);
                Assert.Equal(0, inner.Cancels);                         // the room's effects go on
                Assert.Equal(new[] { "breakout" }, inner.Released);     // only its own holds drop
                Assert.Same(room, BackRoomFxHead.Host);                 // and the room still has a host

                roomFx.CancelAll();                                      // the last one out stops everything
                BackRoomFxHead.Detach(room);
                Assert.Equal(1, inner.Cancels);
                Assert.Null(BackRoomFxHead.Host);
            }
            finally { BackRoomFxHead.Detach(room); BackRoomFxHead.Detach(breakout); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task AClosedRoom_HearsNoMoreSettings()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var s = CoreSettings.Current;
            bool melt = s.BackRoomMelt;
            var (w, posted) = OpenRoom();
            w.HandleMessage("{\"type\":\"ready\"}");
            w.Close();
            int before = posted.Count;
            try
            {
                s.BackRoomMelt = !melt;
                Assert.Equal(before, posted.Count);
                Assert.Null(BackRoomFxHead.Host);
            }
            finally { s.BackRoomMelt = melt; CoreSettings.SaveImmediate(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task TheRace_PausesUnderAMandatoryVideo_AndResumes()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var w = new GameWindow(RaceWindow.Spec);
            w.RaceOwnedTracks = () => new[] { 0 };
            w.RaceCanLaunch = () => true;
            var posted = new List<JObject>();
            w.Posted += json => posted.Add(JObject.Parse(json));
            w.StartRace();
            w.Show();
            try
            {
                w.OnRaceVideoStartedForTest();
                var on = posted.Last(p => (string?)p["type"] == "pause");
                Assert.True((bool)on["on"]!);
                w.OnRaceVideoEndedForTest();
                Assert.False((bool)posted.Last(p => (string?)p["type"] == "pause")["on"]!);
            }
            finally { w.Close(); }
            int after = posted.Count;
            w.OnRaceVideoStartedForTest();
            Assert.Equal(after, posted.Count);
            return Task.CompletedTask;
        });
    }
}
