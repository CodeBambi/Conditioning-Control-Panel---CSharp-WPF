using System;
using System.Collections.Generic;
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

/// <summary>The Goon Game's Discord sharing (WPF GoonHostService, contract 4): the opponent's card
/// reaches the page as a name, a picture and a BOOLEAN, never their Discord id; the id opens a DM only
/// through the "peer" / "last" enum; the last-opponent record is the host's own, from the card it fetched.</summary>
[Collection(SessionStatics.Name)]
public sealed class GoonPeerCardTests : IDisposable
{
    private readonly Func<SettingsService?>? _provider = CoreSettings.ServiceProvider;
    private readonly Func<(bool, string?, string?)>? _discord = GoonAvatarCache.OwnDiscordProvider;
    private readonly SettingsService _service = new();

    public GoonPeerCardTests()
    {
        CoreSettings.ServiceProvider = () => _service;
        GoonAvatarCache.OwnDiscordProvider = null;
        GoonHostService.ResetPeerCardState();
        GoonHostService.OnLastOpponentClear();
    }

    public void Dispose()
    {
        GoonHostService.ResetPeerCardState();
        GoonHostService.OnLastOpponentClear();
        GoonAvatarCache.OwnDiscordProvider = _discord;
        CoreSettings.ServiceProvider = _provider;
    }

    private sealed class Server : HttpMessageHandler
    {
        public HttpStatusCode Status = HttpStatusCode.OK;
        public string Body = "{}";
        public readonly List<(string Url, string Body)> Seen = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Seen.Add((request.RequestUri!.ToString(), await request.Content!.ReadAsStringAsync(ct)));
            return new HttpResponseMessage(Status) { Content = new StringContent(Body) };
        }
    }

    private static string Png()
    {
        var b = new byte[32];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(b, 0);
        return "data:image/png;base64," + Convert.ToBase64String(b);
    }

    [Fact]
    public async Task The_card_carries_a_name_a_picture_and_a_boolean_never_the_id()
    {
        var server = new Server { Body = new JObject { ["name"] = " Velvet ", ["avatar"] = Png(), ["avatar_reason"] = "ok", ["dm_id"] = "123456789012345678", ["ver"] = "7.1.5" }.ToString() };
        using var http = new HttpMessageInvoker(server);
        var card = await GoonHostService.PeerCardAsync(JObject.Parse("{\"code\":\"ABCD\",\"token\":\"t\",\"role\":\"guest\"}"), http);

        Assert.NotNull(card);
        Assert.Equal("peer-card", (string?)card!["type"]);
        Assert.Equal("Velvet", (string?)card["name"]);
        Assert.StartsWith("data:image/png;base64,", (string?)card["avatarDataUri"]);
        Assert.Equal("ok", (string?)card["reason"]);
        Assert.True((bool)card["dm"]!);
        Assert.DoesNotContain("123456789012345678", card.ToString());
        var (url, body) = Assert.Single(server.Seen);
        Assert.Equal(GoonHostService.ProxyBaseUrl + "/v2/goon/peercard", url);
        Assert.Equal("ABCD", (string?)JObject.Parse(body)["code"]);

        // The id opens a DM only through the enum; anything else is nothing to open.
        Assert.Equal("https://discord.com/users/123456789012345678", GoonHostService.DiscordDmUrl("peer"));
        Assert.Null(GoonHostService.DiscordDmUrl("123456789012345678"));
        Assert.Null(GoonHostService.DiscordDmUrl("last"));   // no match finished yet

        // match-result: the host writes the record from the card it fetched.
        GoonHostService.WriteLastOpponentRecord();
        var last = GoonHostService.BuildDiscordBlock(includeLastOpponent: true)["lastOpponent"]!;
        Assert.Equal("Velvet", (string?)last["name"]);
        Assert.True((bool)last["dm"]!);
        Assert.StartsWith("data:image/png;base64,", (string?)last["avatarDataUri"]);
        Assert.DoesNotContain("123456789012345678", last.ToString());
        Assert.Equal("https://discord.com/users/123456789012345678", GoonHostService.DiscordDmUrl("last"));

        GoonHostService.OnLastOpponentClear();
        Assert.Equal(JTokenType.Null, GoonHostService.BuildDiscordBlock(true)["lastOpponent"]!.Type);
        Assert.Null(GoonHostService.DiscordDmUrl("last"));
    }

    [Fact]
    public async Task A_refusal_is_an_error_card_and_is_not_retried()
    {
        var server = new Server { Status = HttpStatusCode.Forbidden };
        using var http = new HttpMessageInvoker(server);
        var card = await GoonHostService.PeerCardAsync(JObject.Parse("{\"code\":\"ABCD\"}"), http);
        Assert.Equal("error", (string?)card!["reason"]);
        Assert.Equal("", (string?)card["name"]);
        Assert.False((bool)card["dm"]!);
        Assert.Single(server.Seen);   // 403 is an answer, not a transport fault
        GoonHostService.WriteLastOpponentRecord();
        Assert.Equal(JTokenType.Null, GoonHostService.BuildDiscordBlock(true)["lastOpponent"]!.Type);
    }

    [Fact]
    public async Task A_dm_id_that_is_not_a_snowflake_never_becomes_a_url()
    {
        var server = new Server { Body = "{\"name\":\"X\",\"dm_id\":\"12; rm -rf\"}" };
        using var http = new HttpMessageInvoker(server);
        var card = await GoonHostService.PeerCardAsync(new JObject(), http);
        Assert.False((bool)card!["dm"]!);
        Assert.Null(GoonHostService.DiscordDmUrl("peer"));
    }

    [Theory]
    [InlineData("lobby", true, "lobby")]
    [InlineData("live", true, "live")]
    [InlineData("off", true, "off")]
    [InlineData("In a duel with Sam", true, null)]   // fixed strings only
    [InlineData("live", false, null)]                // the flag off drops the frame
    public void Rich_presence_takes_only_the_enum_and_only_with_the_flag_on(string s, bool flag, string? expected)
    {
        _service.Current.GoonRichPresence = flag;
        Assert.Equal(expected, GoonHostService.RichPresenceState(new JObject { ["s"] = s }));
    }

    [Fact]
    public void The_own_avatar_state_reads_unlinked_off_or_shared()
    {
        Assert.Equal("unlinked", (string?)GoonHostService.BuildDiscordBlock(false)["avatarState"]);
        GoonAvatarCache.OwnDiscordProvider = () => (true, "hash", null);
        _service.Current.GoonShareAvatar = false;
        Assert.Equal("off", (string?)GoonHostService.BuildDiscordBlock(false)["avatarState"]);
        _service.Current.GoonShareAvatar = true;
        Assert.Equal("shared", (string?)GoonHostService.BuildDiscordBlock(false)["avatarState"]);
    }
}
