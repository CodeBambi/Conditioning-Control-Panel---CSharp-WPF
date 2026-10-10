using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Remote;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>Remote Control v2 on the Core relay (WPF Services/Remote/RemoteControlService.V2.cs): the subject's
/// More / Easy / Stop signal, the Easy factor, the controller's name and the last action label.</summary>
[Collection(SessionStatics.Name)]
public sealed class RemoteSignalTests
{
    private sealed class FakeRelay : HttpMessageHandler
    {
        public readonly List<(string Path, JObject Body)> Seen = new();
        public string Poll = "{}";
        public HttpStatusCode Emote = HttpStatusCode.OK;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var path = r.RequestUri!.AbsolutePath;
            var parsed = JObject.Parse(await r.Content!.ReadAsStringAsync(ct));
            lock (Seen) Seen.Add((path, parsed));
            return path switch
            {
                "/v2/remote/start" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"code\":\"ABC123\"}") },
                "/v2/remote/poll" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Poll) },
                "/v2/remote/emote" => new HttpResponseMessage(Emote) { Content = new StringContent("{}") },
                _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") },
            };
        }
    }

    [Fact]
    public async Task Signals_post_kind_signal_and_stop_runs_the_forced_stop_even_when_the_send_fails()
    {
        var f = new FakeRelay();
        var stops = new List<bool>();
        using var r = new RemoteRelay(() => "tok", () => "uid-1", "9.9.9", (_, _) => null, stops.Add, f) { AutoPoll = false };
        Assert.False(await r.SendSignalAsync("stop"));          // no session: nothing sent, nothing stopped
        Assert.Empty(stops);
        await r.StartAsync("full");
        try
        {
            Assert.False(await r.SendSignalAsync("panic_off")); // only the three kinds
            Assert.True(await r.SendSignalAsync(" More "));
            var body = f.Seen.Last(x => x.Path == "/v2/remote/emote").Body;
            Assert.Equal("more", (string?)body["text"]);
            Assert.Equal("signal", (string?)body["kind"]);
            Assert.Equal("uid-1", (string?)body["unified_id"]);
            Assert.Empty(stops);

            // No debounce: a Stop right after is never swallowed. It acts here first.
            f.Emote = HttpStatusCode.InternalServerError;
            Assert.False(await r.SendSignalAsync("stop"));
            Assert.Equal(new[] { true }, stops);
            Assert.True(r.IsActive);                              // the controller stays connected
        }
        finally { await r.StopAsync(); }
    }

    [Fact]
    public async Task Easy_halves_to_a_quarter_fades_the_showing_spiral_and_the_session_end_hands_it_back()
    {
        var s = CoreSettings.Current;
        var saved = (s.SpiralEnabled, s.SpiralOpacity, s.PinkFilterEnabled, s.PinkFilterOpacity);
        var f = new FakeRelay();
        using var r = new RemoteRelay(() => "tok", () => "uid-1", "9.9.9", (_, _) => null, _ => { }, f) { AutoPoll = false };
        var changes = 0;
        EventHandler onEasy = (_, _) => changes++;
        r.EasyChanged += onEasy;
        try
        {
            (s.SpiralEnabled, s.SpiralOpacity, s.PinkFilterEnabled, s.PinkFilterOpacity) = (true, 40, false, 30);
            await r.StartAsync("full");
            Assert.Equal(1.0, r.EasyFactor);
            await r.SendSignalAsync("easy");
            Assert.Equal(0.5, r.EasyFactor);
            Assert.Equal(20, s.SpiralOpacity);       // showing: faded
            Assert.Equal(30, s.PinkFilterOpacity);   // not showing, not controller-set: left alone
            await r.SendSignalAsync("easy");
            await r.SendSignalAsync("easy");         // floor
            Assert.Equal(RemoteEasy.Floor, r.EasyFactor);
            Assert.Equal(10, s.SpiralOpacity);
            Assert.Equal(2, changes);

            await r.StopAsync();
            Assert.Equal(1.0, r.EasyFactor);
            Assert.Equal(40, s.SpiralOpacity);
            Assert.Equal(3, changes);
        }
        finally
        {
            r.EasyChanged -= onEasy;
            RemoteCommands.ResetEasy();
            (s.SpiralEnabled, s.SpiralOpacity, s.PinkFilterEnabled, s.PinkFilterOpacity) = saved;
        }
    }

    [Fact]
    public async Task The_poll_carries_the_controllers_name_and_a_landed_command_is_the_last_action()
    {
        var f = new FakeRelay();
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        using var r = new RemoteRelay(() => "tok", () => "uid-1", "9.9.9", (a, _) => a == "show_spiral" ? "refused" : null, _ => { }, f)
        { AutoPoll = false, Now = () => now };
        await r.StartAsync("full");
        var moved = 0;
        r.LastActionChanged += (_, _) => moved++;

        f.Poll = "{\"controller_connected\":true,\"controller_name\":\" Mistress K \"}";
        await r.PollOnceAsync();
        Assert.Equal("Mistress K", r.ControllerName);
        Assert.Equal(now, r.ControllerConnectedSinceUtc);

        f.Poll = "{\"controller_connected\":true,\"controller_name\":\"<b>x</b>\",\"commands\":[{\"id\":\"1\",\"action\":\"haptic_pattern\"},{\"id\":\"2\",\"action\":\"show_spiral\"}]}";
        await r.PollOnceAsync();
        Assert.Null(r.ControllerName);                       // not a plain name: none
        Assert.Equal("Toy pattern", r.LastActionLabel);     // the refused one never becomes the last action
        Assert.Equal(1, moved);
        Assert.Equal("some new verb", RemoteActionLabels.For("some_new_verb"));

        f.Poll = "{\"controller_connected\":false}";
        await r.PollOnceAsync();
        Assert.Null(r.ControllerConnectedSinceUtc);

        await r.StopAsync();
        Assert.Null(r.LastActionLabel);
        Assert.Equal(2, moved);
    }
}
