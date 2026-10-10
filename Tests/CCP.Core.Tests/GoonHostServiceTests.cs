using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.GoonGame;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The Goon Game host's windowless half (WPF GoonHostService): the proxy forwards only the
/// duel's own routes, patrons host and every signed-in account joins, a join code or a host ask for a
/// window that is not up rides the next init once, and the flavour pick opts in for one session.</summary>
[Collection(SessionStatics.Name)]
public sealed class GoonHostServiceTests : IDisposable
{
    private readonly Func<SettingsService?>? _provider = CoreSettings.ServiceProvider;
    private readonly Func<bool>? _premium = CoreAccount.HasPremiumAccessProvider;
    private readonly Func<bool>? _open = GoonHostService.OpenWindowProvider;
    private readonly string? _unified = CoreAccount.UnifiedUserId;
    private readonly SettingsService _service = new();
    private readonly List<JObject> _frames = new();

    public GoonHostServiceTests()
    {
        CoreSettings.ServiceProvider = () => _service;
        GoonHostService.DetachWindow();
        GoonHostService.OpenWindowProvider = null;
    }

    public void Dispose()
    {
        GoonHostService.DetachWindow();
        GoonHostService.OpenWindowProvider = _open;
        CoreSettings.ServiceProvider = _provider;
        CoreAccount.HasPremiumAccessProvider = _premium;
        CoreAccount.UnifiedUserId = _unified;
    }

    private sealed class Recorder : HttpMessageHandler
    {
        public readonly List<HttpRequestMessage> Seen = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Seen.Add(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new StringContent("{\"ok\":true}") });
        }
    }

    [Theory]
    [InlineData("/v2/goon/invite", true)]
    [InlineData("/v2/goon/relay/poll", true)]
    [InlineData("/v2/backroom/slot/tape", false)]
    [InlineData("/v2/goon", false)]
    [InlineData("/v2/goon/../auth/discord", false)]
    [InlineData("https://evil.example/v2/goon/x", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_the_duels_own_routes_are_proxied(string? path, bool allowed)
        => Assert.Equal(allowed, GoonHostService.IsAllowedPath(path));

    [Fact]
    public async Task A_path_off_the_whitelist_never_reaches_the_network()
    {
        var rec = new Recorder();
        using var http = new HttpMessageInvoker(rec);
        var (status, body) = await GoonHostService.NetPostAsync("/admin/merge-accounts", "{}", http);
        Assert.Equal(0, status);
        Assert.Equal("forbidden_path", body);
        Assert.Empty(rec.Seen);
    }

    [Fact]
    public async Task An_allowed_path_is_posted_to_the_proxy_with_the_client_version()
    {
        var rec = new Recorder();
        using var http = new HttpMessageInvoker(rec);
        var (status, body) = await GoonHostService.NetPostAsync("/v2/goon/open", "{\"unified_id\":\"u\"}", http);
        Assert.Equal(202, status);
        Assert.Equal("{\"ok\":true}", body);
        var req = Assert.Single(rec.Seen);
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal(GoonHostService.ProxyBaseUrl + "/v2/goon/open", req.RequestUri!.ToString());
        Assert.True(req.Headers.Contains("X-Client-Version"));
    }

    [Fact]
    public void The_frame_body_is_sent_as_the_page_wrote_it()
    {
        Assert.Equal("{\"a\":1}", GoonHostService.NetPostBody(JObject.Parse("{\"body\":\"{\\\"a\\\":1}\"}")));
        Assert.Equal("{\"a\":1}", GoonHostService.NetPostBody(JObject.Parse("{\"body\":{\"a\":1}}")));
        Assert.Equal("", GoonHostService.NetPostBody(new JObject()));
    }

    [Fact]
    public void Patrons_host_and_every_signed_in_account_joins()
    {
        CoreAccount.HasPremiumAccessProvider = () => false;
        CoreAccount.UnifiedUserId = "u_free";
        var caps = GoonHostService.BuildInit(false)["caps"]!;
        Assert.False((bool)caps["canHost"]!);
        Assert.False((bool)caps["mediaTransfer"]!);   // own-media sending is a patron perk
        Assert.True((bool)caps["canJoin"]!);

        CoreAccount.HasPremiumAccessProvider = () => true;
        caps = GoonHostService.BuildInit(false)["caps"]!;
        Assert.True((bool)caps["canHost"]!);
        Assert.True((bool)caps["mediaTransfer"]!);

        CoreAccount.UnifiedUserId = null;
        Assert.False((bool)GoonHostService.BuildInit(false)["caps"]!["canJoin"]!);
    }

    [Fact]
    public void Init_carries_the_wire_the_page_reads()
    {
        var init = GoonHostService.BuildInit(fullscreen: true);
        Assert.Equal("init", (string?)init["type"]);
        Assert.Equal(1, (int)init["protocol"]!);
        Assert.Equal(GoonHostService.ProxyBaseUrl, (string?)init["net"]!["serverBase"]);
        Assert.True((bool)init["net"]!["viaHost"]!);
        Assert.True((bool)init["fullscreen"]!);
        Assert.False((bool)init["caps"]!["haptics"]!);
        Assert.Equal("", (string?)init["joinCode"]);
        Assert.False((bool)init["autoHost"]!);
        Assert.Equal(new ConsentSheetMsg().LiveDurationSec, (int)init["consent"]!["liveDurationSec"]!);
        Assert.Equal("unlinked", (string?)init["discord"]!["avatarState"]);
        Assert.Equal("", (string?)init["media"]!["flavour"]);   // no pick yet this session
    }

    [Fact]
    public void A_join_code_for_a_closed_window_rides_the_next_init_once()
    {
        int opened = 0;
        GoonHostService.OpenWindowProvider = () => { opened++; return true; };
        GoonHostService.Launch("ab-cd 12");
        Assert.Equal(1, opened);
        Assert.Equal("ABCD12", (string?)GoonHostService.BuildInit(false)["joinCode"]);
        Assert.Equal("", (string?)GoonHostService.BuildInit(false)["joinCode"]);   // a reload does not rejoin
    }

    [Fact]
    public void A_failed_launch_carries_nothing_to_a_later_open()
    {
        GoonHostService.OpenWindowProvider = () => false;
        GoonHostService.Launch("ABCD12");
        GoonHostService.LaunchToHost();
        var init = GoonHostService.BuildInit(false);
        Assert.Equal("", (string?)init["joinCode"]);
        Assert.False((bool)init["autoHost"]!);
    }

    [Fact]
    public void A_live_window_gets_frames_instead_of_a_relaunch()
    {
        GoonHostService.OpenWindowProvider = () => true;
        GoonHostService.AttachWindow(f => _frames.Add(JObject.FromObject(f)));
        GoonHostService.Launch("wxyz");
        GoonHostService.Launch("not a code!");   // refused by the grammar: no frame
        GoonHostService.LaunchToHost();
        Assert.Equal(new[] { "join-code", "host-now" }, _frames.Select(f => (string?)f["type"]));
        Assert.Equal("WXYZ", (string?)_frames[0]["code"]);
    }

    [Fact]
    public void A_host_ask_for_a_closed_window_opens_on_the_host_screen()
    {
        GoonHostService.OpenWindowProvider = () => true;
        GoonHostService.LaunchToHost();
        Assert.True((bool)GoonHostService.BuildInit(false)["autoHost"]!);
        Assert.False((bool)GoonHostService.BuildInit(false)["autoHost"]!);
    }

    [Fact]
    public async Task The_invite_waits_for_the_pages_room_code_or_its_busy_answer()
    {
        GoonHostService.OpenWindowProvider = () => true;
        GoonHostService.AttachWindow(_ => { });
        var ask = GoonHostService.OpenRoomForInviteAsync(TimeSpan.FromSeconds(5));
        GoonHostService.OnRoomCodeFrame(JObject.Parse("{\"code\":\"room-42\"}"));
        Assert.Equal(("ROOM42", false), await ask);
        Assert.Equal("ROOM42", GoonHostService.RoomCode);

        GoonHostService.OnRoomCodeFrame(JObject.Parse("{\"code\":\"\"}"));   // somebody sat down
        Assert.Null(GoonHostService.RoomCode);
        var busy = GoonHostService.OpenRoomForInviteAsync(TimeSpan.FromSeconds(5));
        GoonHostService.OnHostBusyFrame();
        Assert.Equal(((string?)null, true), await busy);

        GoonHostService.OnRoomCodeFrame(JObject.Parse("{\"code\":\"ROOM42\"}"));
        GoonHostService.DetachWindow();   // the window closed: the room went with it
        Assert.Null(GoonHostService.RoomCode);
    }

    [Fact]
    public void A_flavour_pick_opts_in_for_this_session_only_and_niches_are_cleaned()
    {
        var subs = GoonHostService.OnMediaFlavour(JObject.Parse(
            "{\"flavour\":\"pink\",\"subs\":[\"Good_one\",\"https://evil.example/x\",\"a\",5],\"online\":true}"));
        Assert.Equal(new[] { "Good_one" }, subs);
        Assert.True(GoonHostService.SessionOptIn);
        Assert.Equal("Good_one", _service.Current.GoonMediaSubs);
        Assert.Equal("pink", (string?)JObject.FromObject(GoonHostService.BuildMediaBlock())["flavour"]);

        GoonHostService.DetachWindow();   // a fresh window waits for the flavour card
        Assert.False(GoonHostService.SessionOptIn);
        var block = JObject.FromObject(GoonHostService.BuildMediaBlock());
        Assert.Equal("", (string?)block["flavour"]);
        Assert.Equal("pink", (string?)block["last"]);   // remembered as a preselection only

        GoonHostService.OnMediaFlavour(JObject.Parse("{\"flavour\":\"pink\",\"subs\":[\"Good_one\"],\"online\":false}"));
        Assert.False(GoonHostService.SessionOptIn);   // the switch off is never an opt-in
    }

    [Fact]
    public void Discord_prefs_are_stored_and_echoed()
    {
        var echo = GoonHostService.OnDiscordPrefs(
            JObject.Parse("{\"shareDm\":true,\"richPresence\":true,\"seenSharePrompt\":true}"), out var shared, out var rpOff);
        Assert.True(shared);
        Assert.False(rpOff);
        Assert.Equal("discord", (string?)echo["type"]);
        Assert.True((bool)echo["dmShared"]!);
        Assert.True((bool)echo["richPresence"]!);
        Assert.Null(echo["lastOpponent"]);   // the echo never carries it

        GoonHostService.OnDiscordPrefs(JObject.Parse("{\"richPresence\":false}"), out shared, out rpOff);
        Assert.False(shared);
        Assert.True(rpOff);
    }
}
