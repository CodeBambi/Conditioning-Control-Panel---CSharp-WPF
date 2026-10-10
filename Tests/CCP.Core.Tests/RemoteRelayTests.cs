using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>Core RemoteRelay against a fake relay: no network. Rules from WPF RemoteControlService.</summary>
[Collection(SessionStatics.Name)]
public sealed class RemoteRelayTests
{
    private sealed class FakeRelay : HttpMessageHandler
    {
        public readonly List<(string Path, JObject Body)> Seen = new();
        public Func<string, HttpResponseMessage> Answer = path => path switch
        {
            "/v2/remote/start" => Json(HttpStatusCode.OK, "{\"code\":\"ABC123\"}"),
            _ => Json(HttpStatusCode.OK, "{}"),
        };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Seen.Add((r.RequestUri!.AbsolutePath, JObject.Parse(await r.Content!.ReadAsStringAsync(ct))));
            return Answer(r.RequestUri.AbsolutePath);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode s, string body) => new(s) { Content = new StringContent(body) };

    private readonly List<string> _ran = new();
    private readonly List<bool> _stops = new();

    private RemoteRelay Relay(FakeRelay f) => new(() => "tok", () => "uid-1", "9.9.9",
        (a, _) => { _ran.Add(a); return null; }, force => _stops.Add(force), f) { AutoPoll = false };

    private static void Poll(FakeRelay f, string json) =>
        f.Answer = path => path == "/v2/remote/poll" ? Json(HttpStatusCode.OK, json) : Json(HttpStatusCode.OK, "{}");

    [Fact]
    public void A_sandbox_never_reaches_the_real_relay()
    {
        Assert.Null(RemoteRelay.ResolveBaseUrl(null, sandboxed: true));
        Assert.Null(RemoteRelay.ResolveBaseUrl("https://codebambi-proxy.vercel.app", sandboxed: true));
        Assert.Equal("http://127.0.0.1:9999", RemoteRelay.ResolveBaseUrl("http://127.0.0.1:9999/", sandboxed: true));
        Assert.Equal("https://codebambi-proxy.vercel.app", RemoteRelay.ResolveBaseUrl(null, sandboxed: false));
    }

    [Fact]
    public async Task Start_pairs_with_a_pin_and_the_url_carries_it_in_the_fragment()
    {
        var f = new FakeRelay();
        using var r = Relay(f);
        Assert.Equal("ABC123", await r.StartAsync("light"));
        var body = f.Seen.Single().Body;
        Assert.Equal("uid-1", (string?)body["unified_id"]);
        Assert.Equal("light", (string?)body["tier"]);
        Assert.Matches("^[0-9]{4}$", (string?)body["connect_pin"]);
        Assert.Equal(r.ConnectPin, (string?)body["connect_pin"]);
        Assert.Equal($"https://cclabs.app/remote/#code=ABC123&pin={r.ConnectPin}", RemoteRelay.PairingUrl("ABC123", r.ConnectPin));
    }

    [Fact]
    public async Task Commands_run_but_panic_can_never_be_switched_off_and_the_controller_is_told()
    {
        var f = new FakeRelay();
        using var r = Relay(f);
        await r.StartAsync("full");
        var received = new List<string>();
        r.CommandReceived += (_, a) => received.Add(a);
        Poll(f, "{\"controller_connected\":true,\"commands\":[{\"id\":\"1\",\"action\":\"start_flash\"},{\"id\":\"2\",\"action\":\"disable_panic\"}]}");
        await r.PollOnceAsync();

        Assert.True(r.ControllerConnected);
        Assert.Equal(new[] { "start_flash" }, _ran);
        Assert.Equal(new[] { "start_flash" }, received);
        var status = f.Seen.Last(s => s.Path == "/v2/remote/status").Body["last_executed"]!;
        Assert.Equal("disable_panic", (string?)status["action"]);
        Assert.Equal("fail", (string?)status["status"]);
        Assert.Equal("the panic key stays on", (string?)status["reason"]);   // WPF 719ed9ca5
    }

    [Fact]
    public void Lockdown_refuses_only_enable_strict_lock()
    {
        Assert.Null(RemoteCommandGate.Screen("enable_strict_lock", lockdownActive: false));
        Assert.Equal("not during Lockdown", RemoteCommandGate.Screen("enable_strict_lock", lockdownActive: true));
        foreach (var a in new[] { "disable_strict_lock", "enable_panic", "stop_session", "pause_session", "trigger_panic", "start_flash" })
            Assert.Null(RemoteCommandGate.Screen(a, lockdownActive: true));
        Assert.NotNull(RemoteCommandGate.Screen("disable_panic", lockdownActive: false));
        Assert.NotNull(RemoteCommandGate.Screen("disable_panic", lockdownActive: true));
    }

    [Fact]
    public void Restraint_reducing_verbs_run_under_Lockdown_keep_its_timer_and_it_still_restores_on_end()
    {
        var s = CoreSettings.Current;
        var saved = (s.StrictLockEnabled, s.PanicKeyEnabled, s.LockdownForceStrictLock, s.LockdownDisablePanicKey);
        var prev = LockdownService.Current;
        var ld = LockdownService.Current = new LockdownService();
        try
        {
            (s.StrictLockEnabled, s.PanicKeyEnabled, s.LockdownForceStrictLock, s.LockdownDisablePanicKey) = (false, true, true, true);
            ld.Activate(TimeSpan.FromMinutes(30));
            Assert.True(s.StrictLockEnabled);
            Assert.False(s.PanicKeyEnabled);
            Assert.Null(RemoteCommands.Execute("trigger_panic", null));
            Assert.True(s.PanicKeyEnabled);       // WPF StopAllRemoteEffects, Lockdown or not
            Assert.False(s.StrictLockEnabled);
            (s.StrictLockEnabled, s.PanicKeyEnabled) = (true, false);
            Assert.Null(RemoteCommands.Execute("enable_panic", null));
            Assert.Null(RemoteCommands.Execute("disable_strict_lock", null));
            Assert.True(s.PanicKeyEnabled);
            Assert.False(s.StrictLockEnabled);
            Assert.True(ld.IsActive);             // the timer was not ended
            Assert.Equal(0, ld.RestartCount);     // nor restarted
            s.PanicKeyEnabled = false;            // whatever the controller left behind...
            ld.Deactivate();
            Assert.True(s.PanicKeyEnabled);       // ...Lockdown restores the pre-lockdown values
            Assert.False(s.StrictLockEnabled);
        }
        finally
        {
            ld.Deactivate();
            ld.Dispose();
            LockdownService.Current = prev;
            (s.StrictLockEnabled, s.PanicKeyEnabled, s.LockdownForceStrictLock, s.LockdownDisablePanicKey) = saved;
        }
    }

    [Fact]
    public async Task Rate_limit_backs_off_and_an_expired_session_ends_and_stops_remote_effects()
    {
        var f = new FakeRelay();
        using var r = Relay(f);
        await r.StartAsync("light");
        f.Answer = _ => Json((HttpStatusCode)429, "{}");
        await r.PollOnceAsync();
        await r.PollOnceAsync();
        Assert.Equal(20, r.PollInterval);

        var ended = 0;
        r.SessionEnded += (_, _) => ended++;
        f.Answer = _ => Json(HttpStatusCode.NotFound, "{}");
        await r.PollOnceAsync();
        Assert.False(r.IsActive);
        Assert.Null(r.SessionCode);
        Assert.Equal(1, ended);
        Assert.Equal(new[] { false }, _stops);   // the subject's own run survives (#878)
    }

    [Fact]
    public async Task An_idle_controller_is_dropped_after_two_minutes_and_does_not_bounce_back_while_idle()
    {
        var f = new FakeRelay();
        using var r = Relay(f);
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        r.Now = () => now;
        await r.StartAsync("light");
        Poll(f, "{\"controller_connected\":true,\"controller_idle\":true}");
        await r.PollOnceAsync();
        Assert.True(r.ControllerConnected);
        now = now.AddSeconds(119);
        await r.PollOnceAsync();
        Assert.True(r.ControllerConnected);
        now = now.AddSeconds(2);
        await r.PollOnceAsync();
        Assert.False(r.ControllerConnected);
        await r.PollOnceAsync();
        Assert.False(r.ControllerConnected);
        Poll(f, "{\"controller_connected\":true,\"controller_idle\":false}");
        await r.PollOnceAsync();
        Assert.True(r.ControllerConnected);
    }

    [Fact]
    public void Panic_stop_restores_the_panic_key_and_drops_strict_lock()
    {
        var s = CoreSettings.Current;
        var (strict, panic) = (s.StrictLockEnabled, s.PanicKeyEnabled);
        try
        {
            (s.StrictLockEnabled, s.PanicKeyEnabled) = (true, false);
            Assert.Null(RemoteCommands.Execute("trigger_panic", null));
            Assert.False(s.StrictLockEnabled);
            Assert.True(s.PanicKeyEnabled);
            Assert.Equal(RemoteCommands.NotOnThisBuild, RemoteCommands.Execute("play_hypnotube", null));
        }
        finally { (s.StrictLockEnabled, s.PanicKeyEnabled) = (strict, panic); }
    }
    // main 71cfc4185 (ccp-bugs #1340): the controller leaving hands back the panic key and the strict
    // lock IT switched on; a strict lock the subject set stays.
    [Fact]
    public async Task Controller_leave_releases_the_controllers_strict_lock_and_the_panic_key_only()
    {
        var s = CoreSettings.Current;
        var saved = (s.StrictLockEnabled, s.PanicKeyEnabled, s.StopEffectsOnRemoteDisconnect);
        try
        {
            (s.StrictLockEnabled, s.PanicKeyEnabled, s.StopEffectsOnRemoteDisconnect) = (false, false, false);
            var f = new FakeRelay();
            using var r = new RemoteRelay(() => "tok", () => "uid-1", "9.9.9", RemoteCommands.Execute, _ => { }, f) { AutoPoll = false };
            await r.StartAsync("full");
            Poll(f, "{\"controller_connected\":true,\"commands\":[{\"id\":\"1\",\"action\":\"enable_strict_lock\"}]}");
            await r.PollOnceAsync();
            Assert.True(s.StrictLockEnabled);
            Poll(f, "{\"controller_connected\":false}");
            await r.PollOnceAsync();
            Assert.False(s.StrictLockEnabled);
            Assert.True(s.PanicKeyEnabled);

            s.StrictLockEnabled = true;   // the subject's own
            Poll(f, "{\"controller_connected\":true,\"commands\":[{\"id\":\"2\",\"action\":\"enable_strict_lock\"}]}");
            await r.PollOnceAsync();
            Poll(f, "{\"controller_connected\":false}");
            await r.PollOnceAsync();
            Assert.True(s.StrictLockEnabled);
        }
        finally { (s.StrictLockEnabled, s.PanicKeyEnabled, s.StopEffectsOnRemoteDisconnect) = saved; }
    }

    // main 7b22ece8c (ccp-bugs #1065): no toy -> refused, never a silent "ok".
    [Fact]
    public void Trigger_haptic_with_no_toy_is_refused()
    {
        var prev = CoreHaptics.Service;
        try
        {
            CoreHaptics.Service = null;
            Assert.Equal("no_device", RemoteCommands.Execute("trigger_haptic", null));
        }
        finally { CoreHaptics.Service = prev; }
    }

    private const string Loop = "{\"levels\":[50,80],\"step_ms\":100,\"loop\":true}";

    /// <summary>A stepped-clock driver in place of the static one (P08), restored after (P02).</summary>
    private static void WithSteppedHaptics(Action<PopQuizSchedulerTests.FakeClock, List<int>> body)
    {
        var prev = RemoteCommands.RemoteHaptics;
        var clock = new PopQuizSchedulerTests.FakeClock();
        var sent = new List<int>();
        RemoteCommands.RemoteHaptics = new ConditioningControlPanel.Services.Remote.CoreRemoteHapticDriver(() => 1.0, clock)
        { Sink = steps => { sent.Add(steps.Count); return null; } };
        try { body(clock, sent); }
        finally { RemoteCommands.RemoteHaptics.Stop(); RemoteCommands.RemoteHaptics = prev; }
    }

    private static string Cmd(string id, string action, string p = "{}") =>
        "{\"controller_connected\":true,\"commands\":[{\"id\":\"" + id + "\",\"action\":\"" + action + "\",\"params\":" + p + "}]}";

    // main 719ed9ca5: a looping pattern keeps playing until haptic_stop, a leaving controller, or panic
    // ends it; after panic the loop never comes back (HapticMixer.PanicStop only mutes 400 ms).
    [Fact]
    public void Remote_haptics_play_and_every_stop_path_ends_them() => WithSteppedHaptics((clock, sent) =>
    {
        var f = new FakeRelay();
        using var r = new RemoteRelay(() => "tok", () => "uid-1", "9.9.9", RemoteCommands.Execute, _ => { }, f) { AutoPoll = false };
        r.StartAsync("full").Wait();
        var d = RemoteCommands.RemoteHaptics;
        void Playing(string id) { Poll(f, Cmd(id, "haptic_pattern", Loop)); r.PollOnceAsync().Wait(); Assert.True(d.IsPlaying); clock.Advance(TimeSpan.FromSeconds(2)); Assert.True(sent.Count > 1); }
        void Silent() { Assert.False(d.IsPlaying); var n = sent.Count; clock.Advance(TimeSpan.FromSeconds(5)); Assert.Equal(n, sent.Count); }

        Playing("1");
        Poll(f, Cmd("2", "haptic_stop")); r.PollOnceAsync().Wait();
        Silent();

        Playing("3");
        Poll(f, "{\"controller_connected\":false}"); r.PollOnceAsync().Wait();
        Silent();

        Playing("4");
        RemoteCommands.StopHaptics();   // every panic path on both heads
        Silent();

        Assert.Equal("no pattern", RemoteCommands.Execute("haptic_pattern", new JObject()));
    });

    // Safety review P1/P2: a command fetched before a panic or a leave never runs after it, however
    // late its UI dispatch lands; a late enable_strict_lock that did land before the leave is released.
    [Fact]
    public void A_command_in_flight_never_outlives_a_panic_or_a_leave() => WithSteppedHaptics((clock, sent) =>
    {
        var s = CoreSettings.Current;
        var saved = (s.StrictLockEnabled, s.PanicKeyEnabled, CoreDispatch.InvokeProvider);
        Func<object?>? parked = null;
        try
        {
            (s.StrictLockEnabled, s.PanicKeyEnabled) = (false, true);
            var f = new FakeRelay();
            using var r = new RemoteRelay(() => "tok", () => "uid-1", "9.9.9", RemoteCommands.Execute, _ => { }, f) { AutoPoll = false };
            r.StartAsync("full").Wait();
            Poll(f, "{\"controller_connected\":true}"); r.PollOnceAsync().Wait();
            // The UI thread is busy: the first dispatch times out. Avalonia cancels it then; here it is parked and
            // run by hand, to prove the generation check refuses it even if it did land late. The rest run at once.
            CoreDispatch.InvokeProvider = (fn, _) => { if (parked == null) { parked = fn; return (false, null); } return (true, fn()); };

            Poll(f, Cmd("1", "haptic_level", "{\"level\":60,\"ms\":3000}")); r.PollOnceAsync().Wait();
            RemoteCommands.StopHaptics();
            Assert.Equal("stopped by panic", parked!());
            Assert.False(RemoteCommands.RemoteHaptics.IsPlaying);

            parked = null;
            Poll(f, Cmd("2", "haptic_pattern", Loop)); r.PollOnceAsync().Wait();
            Poll(f, "{\"controller_connected\":false}"); r.PollOnceAsync().Wait();
            Assert.Equal("the controller left", parked!());
            Assert.False(RemoteCommands.RemoteHaptics.IsPlaying);

            Poll(f, "{\"controller_connected\":true}"); r.PollOnceAsync().Wait();
            parked = null;
            Poll(f, Cmd("3", "enable_strict_lock")); r.PollOnceAsync().Wait();
            Assert.Null(parked!());          // lands late, still before the leave
            Assert.True(s.StrictLockEnabled);
            Poll(f, "{\"controller_connected\":false}"); r.PollOnceAsync().Wait();
            Assert.False(s.StrictLockEnabled);
        }
        finally { (s.StrictLockEnabled, s.PanicKeyEnabled, CoreDispatch.InvokeProvider) = saved; }
    });

    // Safety review P2: Avalonia cancels an Invoke that times out, so after a UI stall the leave cleanup is
    // posted instead: the loop stops, the controller's strict lock goes, the panic key comes back.
    [Fact]
    public void A_leave_during_a_ui_stall_still_releases_everything() => WithSteppedHaptics((clock, sent) =>
    {
        var s = CoreSettings.Current;
        var saved = (s.StrictLockEnabled, s.PanicKeyEnabled, CoreDispatch.InvokeProvider, CoreDispatch.PostProvider);
        try
        {
            (s.StrictLockEnabled, s.PanicKeyEnabled) = (false, true);
            var f = new FakeRelay();
            using var r = new RemoteRelay(() => "tok", () => "uid-1", "9.9.9", RemoteCommands.Execute, _ => { }, f) { AutoPoll = false };
            r.StartAsync("full").Wait();
            Poll(f, "{\"controller_connected\":true}"); r.PollOnceAsync().Wait();
            Poll(f, Cmd("1", "enable_strict_lock")); r.PollOnceAsync().Wait();
            Poll(f, Cmd("2", "haptic_pattern", Loop)); r.PollOnceAsync().Wait();
            s.PanicKeyEnabled = false;
            Assert.True(s.StrictLockEnabled && RemoteCommands.RemoteHaptics.IsPlaying);

            var posted = new List<Action>();
            CoreDispatch.InvokeProvider = (_, _) => (false, null);   // the UI is stalled: Avalonia cancels the call
            CoreDispatch.PostProvider = posted.Add;
            Poll(f, "{\"controller_connected\":false}"); r.PollOnceAsync().Wait();
            Assert.True(RemoteCommands.RemoteHaptics.IsPlaying);       // nothing ran on the stalled UI yet
            foreach (var a in posted.ToArray()) a();                  // the UI drains
            Assert.False(RemoteCommands.RemoteHaptics.IsPlaying);
            Assert.False(s.StrictLockEnabled);
            Assert.True(s.PanicKeyEnabled);
        }
        finally { (s.StrictLockEnabled, s.PanicKeyEnabled, CoreDispatch.InvokeProvider, CoreDispatch.PostProvider) = saved; }
    });

    // Audit #1937: ending the session stops the controller's effects on the UI thread (they close
    // windows), and still stops them, posted, when a stalled UI cancels the Invoke.
    [Fact]
    public async Task Ending_the_session_stops_effects_on_the_ui_thread()
    {
        var saved = (CoreDispatch.InvokeProvider, CoreDispatch.PostProvider);
        try
        {
            var onUi = false;
            var stoppedOnUi = new List<bool>();
            CoreDispatch.InvokeProvider = (fn, _) => { onUi = true; try { return (true, fn()); } finally { onUi = false; } };
            var f = new FakeRelay();
            using (var r = new RemoteRelay(() => "tok", () => "uid-1", "9.9.9", (_, _) => null, _ => stoppedOnUi.Add(onUi), f) { AutoPoll = false })
            {
                await r.StartAsync("full");
                await r.StopAsync();
            }
            Assert.Equal(new[] { true }, stoppedOnUi);

            var posted = new List<Action>();
            CoreDispatch.InvokeProvider = (_, _) => (false, null);
            CoreDispatch.PostProvider = posted.Add;
            stoppedOnUi.Clear();
            using (var r = new RemoteRelay(() => "tok", () => "uid-1", "9.9.9", (_, _) => null, _ => stoppedOnUi.Add(onUi), f) { AutoPoll = false })
            {
                await r.StartAsync("full");
                await r.StopAsync();
                Assert.Empty(stoppedOnUi);
                foreach (var a in posted.ToArray()) a();
                Assert.Single(stoppedOnUi);
            }
        }
        finally { (CoreDispatch.InvokeProvider, CoreDispatch.PostProvider) = saved; }
    }

    // main d39969827: 1 s polls while a connected controller is busy; a 429 still backs off from 5 s.
    [Fact]
    public async Task Hot_cadence_while_the_controller_is_busy()
    {
        var f = new FakeRelay();
        using var r = Relay(f);
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        r.Now = () => now;
        await r.StartAsync("light");
        Poll(f, "{\"controller_connected\":true,\"commands\":[{\"id\":\"1\",\"action\":\"start_flash\"}]}");
        await r.PollOnceAsync();
        Assert.Equal(RemoteRelay.HotPollSeconds, r.PollInterval);
        f.Answer = _ => Json((HttpStatusCode)429, "{}");
        await r.PollOnceAsync();
        Assert.Equal(10, r.PollInterval);
        Poll(f, "{\"controller_connected\":true}");
        now = now.AddSeconds(61);
        await r.PollOnceAsync();
        Assert.Equal(RemoteRelay.PollIntervalSeconds, r.PollInterval);
    }

    [Fact]
    public async Task Emotes_post_trimmed_debounce_double_clicks_and_report_the_rate_limit()
    {
        var f = new FakeRelay();
        using var r = Relay(f);
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        r.Now = () => now;
        Assert.Equal((false, "session not active", (int?)null), await r.SendEmoteAsync("hi", "", "custom"));
        await r.StartAsync("light");

        Assert.True((await r.SendEmoteAsync("  " + new string('x', 70) + " ", "123456789", "preset")).ok);
        var body = f.Seen.Last(s => s.Path == "/v2/remote/emote").Body;
        Assert.Equal(60, body["text"]!.ToString().Length);
        Assert.Equal("12345678", body["icon"]!.ToString());
        Assert.Equal("uid-1", body["unified_id"]!.ToString());

        now = now.AddMilliseconds(100);   // a double click: swallowed, nothing posted
        var posts = f.Seen.Count;
        Assert.Equal("debounced", (await r.SendEmoteAsync("again", "", "custom")).error);
        Assert.Equal(posts, f.Seen.Count);

        now = now.AddMilliseconds(RemoteRelay.EmoteDebounceMs);
        f.Answer = _ => Json((HttpStatusCode)429, "{\"retry_after_seconds\":7}");
        Assert.Equal((false, "rate_limited", (int?)7), await r.SendEmoteAsync("again", "", "custom"));
    }
}
