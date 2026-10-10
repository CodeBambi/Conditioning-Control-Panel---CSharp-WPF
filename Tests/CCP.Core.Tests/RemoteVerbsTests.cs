using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The Remote Control verbs that need a head (WPF RemoteControlService.ExecuteCommand: pink filter,
/// spiral, opacity, Melt, lock card, Takeover, the session verbs) against a fake head. The rule under test:
/// what a controller puts up comes down on its stop, session end, its leaving and panic, and a controller
/// never gets Strict Lock or the panic key out of any of these verbs.</summary>
[Collection(SessionStatics.Name)]
public sealed class RemoteVerbsTests
{
    private sealed class FakeHead : RemoteCommands.IRemoteHead
    {
        public bool CanShow = true, RemoteRun;
        public string? HazeRefusal;
        public readonly List<string> Calls = new();
        public bool RefreshOverlay(string which)
        {
            Calls.Add("refresh:" + which);
            var s = CoreSettings.Current;
            var on = which == "pink" ? s.PinkFilterEnabled : s.SpiralEnabled;
            return CanShow && on && RemoteCommands.OverlayHold;
        }
        public string? RefreshBrainDrain() { Calls.Add("refresh:haze"); return HazeRefusal; }
        public string? ShowLockCard() { Calls.Add("card"); return null; }
        public void CloseCards() => Calls.Add("close-cards");
        public string? SetAutonomy(bool on) { Calls.Add("autonomy:" + on); return null; }
        public void CancelAutonomyPulses(bool restart) => Calls.Add("pulses:" + restart);
        public string? Session(string verb, JObject? p) { Calls.Add(verb); return null; }
        public bool SessionIsRemoteStarted => RemoteRun;
    }

    private sealed class Saved : IDisposable
    {
        private readonly (bool, bool, bool, int, int, bool, bool, bool) _v;
        private readonly RemoteCommands.IRemoteHead? _head = RemoteCommands.Head;
        public Saved()
        {
            var s = CoreSettings.Current;
            _v = (s.PinkFilterEnabled, s.SpiralEnabled, s.BrainDrainEnabled, s.PinkFilterOpacity, s.SpiralOpacity,
                s.StrictLockEnabled, s.PanicKeyEnabled, s.StopEffectsOnRemoteDisconnect);
            (s.PinkFilterEnabled, s.SpiralEnabled, s.BrainDrainEnabled, s.StrictLockEnabled, s.PanicKeyEnabled, s.StopEffectsOnRemoteDisconnect)
                = (false, false, false, false, true, false);
        }
        public void Dispose()
        {
            RemoteCommands.StopEffects(force: false);   // clears the hold and the "controller switched it on" marks
            RemoteCommands.Head = _head;
            var s = CoreSettings.Current;
            (s.PinkFilterEnabled, s.SpiralEnabled, s.BrainDrainEnabled, s.PinkFilterOpacity, s.SpiralOpacity,
                s.StrictLockEnabled, s.PanicKeyEnabled, s.StopEffectsOnRemoteDisconnect) = _v;
        }
    }

    [Fact]
    public void Without_a_head_the_window_verbs_refuse_and_say_so()
    {
        using var _ = new Saved();
        RemoteCommands.Head = null;
        foreach (var verb in new[] { "show_pink_filter", "show_spiral", "set_pink_opacity", "start_brain_drain",
                     "trigger_lock_card", "start_autonomy", "start_session", "stop_session" })
            Assert.Equal(RemoteCommands.NotOnThisBuild, RemoteCommands.Execute(verb, new JObject()));
        // Still refused on every head, each for its reason (see RemoteCommands).
        foreach (var verb in new[] { "play_hypnotube", "trigger_wallpaper", "stop_wallpaper" })
            Assert.Equal(RemoteCommands.NotOnThisBuild, RemoteCommands.Execute(verb, null));
        Assert.False(RemoteCommands.OverlayHold);
    }

    [Fact]
    public void Pink_filter_and_spiral_show_without_the_engine_and_come_down_on_the_controllers_stop()
    {
        using var _ = new Saved();
        var s = CoreSettings.Current;
        RemoteCommands.Head = new FakeHead();
        Assert.Null(RemoteCommands.Execute("show_pink_filter", null));
        Assert.True(s.PinkFilterEnabled && RemoteCommands.OverlayHold);
        Assert.Null(RemoteCommands.Execute("show_spiral", null));
        Assert.True(s.SpiralEnabled);
        Assert.Null(RemoteCommands.Execute("stop_pink_filter", null));
        Assert.Null(RemoteCommands.Execute("stop_spiral", null));
        Assert.False(s.PinkFilterEnabled || s.SpiralEnabled);
    }

    [Fact]
    public void Opacity_is_clamped_as_WPF_pink_50_spiral_100_default_25()
    {
        using var _ = new Saved();
        var s = CoreSettings.Current;
        RemoteCommands.Head = new FakeHead();
        Assert.Null(RemoteCommands.Execute("set_pink_opacity", new JObject { ["value"] = 90 }));
        Assert.Equal(50, s.PinkFilterOpacity);
        Assert.Null(RemoteCommands.Execute("set_spiral_opacity", new JObject { ["value"] = 400 }));
        Assert.Equal(100, s.SpiralOpacity);
        Assert.Null(RemoteCommands.Execute("set_spiral_opacity", new JObject()));
        Assert.Equal(25, s.SpiralOpacity);
        Assert.Equal("bad params", RemoteCommands.Execute("set_pink_opacity", new JObject { ["value"] = "lots" }));
    }

    [Fact]
    public void An_overlay_that_cannot_reach_the_screen_is_refused_and_the_settings_stay()
    {
        using var _ = new Saved();
        var s = CoreSettings.Current;
        RemoteCommands.Head = new FakeHead { CanShow = false };
        Assert.Equal(RemoteCommands.NoOverlayHere, RemoteCommands.Execute("show_pink_filter", null));
        Assert.False(s.PinkFilterEnabled);
        Assert.False(RemoteCommands.OverlayHold);
        RemoteCommands.Head = new FakeHead { HazeRefusal = "no screen haze on this system" };
        Assert.Equal("no screen haze on this system", RemoteCommands.Execute("start_brain_drain", null));
        Assert.False(s.BrainDrainEnabled);
        Assert.False(RemoteCommands.OverlayHold);
    }

    [Fact]
    public void Session_end_takes_everything_down_and_hands_Melt_back()
    {
        using var _ = new Saved();
        var s = CoreSettings.Current;
        var head = new FakeHead();
        RemoteCommands.Head = head;
        Assert.Null(RemoteCommands.Execute("show_pink_filter", null));
        Assert.Null(RemoteCommands.Execute("show_spiral", null));
        Assert.Null(RemoteCommands.Execute("start_brain_drain", null));
        Assert.True(s.BrainDrainEnabled);
        head.Calls.Clear();
        RemoteCommands.StopEffects(force: false);
        Assert.False(s.PinkFilterEnabled || s.SpiralEnabled || s.BrainDrainEnabled || RemoteCommands.OverlayHold);
        Assert.Contains("refresh:pink", head.Calls);
        Assert.Contains("refresh:spiral", head.Calls);
        Assert.Contains("refresh:haze", head.Calls);
        Assert.Contains("close-cards", head.Calls);
        Assert.Contains("pulses:False", head.Calls);
        Assert.DoesNotContain("stop_session", head.Calls);   // the subject's own run is not the controller's to end (#878)
    }

    [Fact]
    public void Session_end_stops_a_run_the_controller_started_and_panic_stops_any_run()
    {
        using var _ = new Saved();
        var head = new FakeHead { RemoteRun = true };
        RemoteCommands.Head = head;
        RemoteCommands.StopEffects(force: false);
        Assert.Contains("stop_session", head.Calls);
        head.RemoteRun = false;
        head.Calls.Clear();
        Assert.Null(RemoteCommands.Execute("trigger_panic", null));
        Assert.Contains("stop_session", head.Calls);
        Assert.Contains("pulses:True", head.Calls);
    }

    [Fact]
    public void Panic_drops_the_hold_at_once()
    {
        using var _ = new Saved();
        var head = new FakeHead();
        RemoteCommands.Head = head;
        Assert.Null(RemoteCommands.Execute("show_spiral", null));
        head.Calls.Clear();
        RemoteCommands.PanicDropOverlays();
        Assert.False(RemoteCommands.OverlayHold);
        Assert.Contains("refresh:spiral", head.Calls);
    }

    [Fact]
    public void The_controller_leaving_takes_its_overlays_down_and_keeps_the_subjects_own_setting()
    {
        using var _ = new Saved();
        var s = CoreSettings.Current;
        RemoteCommands.Head = new FakeHead();
        s.SpiralEnabled = true;                                    // the subject's own: armed for their engine
        Assert.Null(RemoteCommands.Execute("show_pink_filter", null));
        Assert.Null(RemoteCommands.Execute("show_spiral", null));
        Assert.Null(RemoteCommands.Execute("start_brain_drain", null));
        RemoteCommands.ControllerLeft();
        Assert.False(RemoteCommands.OverlayHold);
        Assert.False(s.PinkFilterEnabled);
        Assert.False(s.BrainDrainEnabled);
        Assert.True(s.SpiralEnabled);
    }

    [Fact]
    public void The_session_and_card_verbs_reach_the_head_and_never_touch_strict_lock_or_the_panic_key()
    {
        using var _ = new Saved();
        var s = CoreSettings.Current;
        var head = new FakeHead();
        RemoteCommands.Head = head;
        foreach (var verb in new[] { "start_session", "pause_session", "resume_session", "stop_session", "trigger_lock_card" })
            Assert.Null(RemoteCommands.Execute(verb, new JObject { ["strict_lock"] = true }));
        Assert.Null(RemoteCommands.Execute("start_autonomy", null));
        Assert.Null(RemoteCommands.Execute("stop_autonomy", null));
        Assert.Equal(new[] { "start_session", "pause_session", "resume_session", "stop_session", "card", "autonomy:True", "autonomy:False" }, head.Calls);
        Assert.False(s.StrictLockEnabled);   // start_session's strict_lock flag is not honoured on this head
        Assert.True(s.PanicKeyEnabled);
    }

    private sealed class Relay : HttpMessageHandler
    {
        public string Poll = "{}";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var body = r.RequestUri!.AbsolutePath switch { "/v2/remote/start" => "{\"code\":\"ABC123\"}", "/v2/remote/poll" => Poll, _ => "{}" };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    [Fact]
    public async Task Through_the_relay_a_leave_and_a_session_end_both_bring_the_spiral_down()
    {
        using var _ = new Saved();
        var s = CoreSettings.Current;
        RemoteCommands.Head = new FakeHead();
        var f = new Relay();
        using var r = new RemoteRelay(() => "tok", () => "uid-1", "9.9.9", RemoteCommands.Execute, RemoteCommands.StopEffects, f) { AutoPoll = false };
        await r.StartAsync("light");
        f.Poll = "{\"controller_connected\":true,\"commands\":[{\"id\":\"1\",\"action\":\"show_spiral\"}]}";
        await r.PollOnceAsync();
        Assert.True(s.SpiralEnabled && RemoteCommands.OverlayHold);
        f.Poll = "{\"controller_connected\":false}";
        await r.PollOnceAsync();
        Assert.False(s.SpiralEnabled || RemoteCommands.OverlayHold);   // StopEffectsOnRemoteDisconnect is off: it still comes down

        f.Poll = "{\"controller_connected\":true,\"commands\":[{\"id\":\"2\",\"action\":\"show_pink_filter\"}]}";
        await r.PollOnceAsync();
        Assert.True(s.PinkFilterEnabled);
        await r.StopAsync();                                           // End Session / the toggle
        Assert.False(s.PinkFilterEnabled || RemoteCommands.OverlayHold);
    }
}
