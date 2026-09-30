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
        Assert.Equal("the panic key stays on", (string?)status["reason"]);
    }

    [Fact]
    public void Lockdown_refuses_every_verb_that_touches_what_it_pinned()
    {
        foreach (var a in new[] { "enable_strict_lock", "disable_strict_lock", "enable_panic", "stop_session", "pause_session", "trigger_panic" })
        {
            Assert.Null(RemoteCommandGate.Screen(a, lockdownActive: false));
            Assert.Equal("not during Lockdown", RemoteCommandGate.Screen(a, lockdownActive: true));
        }
        Assert.Null(RemoteCommandGate.Screen("start_flash", lockdownActive: true));
        Assert.NotNull(RemoteCommandGate.Screen("disable_panic", lockdownActive: false));
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
}
