using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Chaster;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Who is linked: GET /auth/profile gives the username and the picture for the account strip.
/// Only those two fields are read, and the picture only ever comes from Chaster's own hosts.
/// </summary>
public class ChasterProfileTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        public readonly List<(Uri Url, string? Bearer)> Seen = new();
        public Func<HttpRequestMessage, HttpResponseMessage> Answer = _ => new HttpResponseMessage(HttpStatusCode.OK);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Seen.Add((r.RequestUri!, r.Headers.Authorization?.Parameter));
            var res = Answer(r);
            res.RequestMessage ??= r;
            return Task.FromResult(res);
        }
    }

    private const string Profile =
        "{\"_id\":\"u1\",\"username\":\"lockedlou\",\"avatarUrl\":\"https://api.chaster.app/users/avatar/u1.jpg\","
        + "\"email\":\"x@example.com\",\"birthDate\":\"1990-01-01\",\"isPremium\":false,\"settings\":{}}";

    [Fact]
    public void Parse_reads_the_username_and_a_chaster_avatar()
    {
        var p = ChasterClient.ParseProfile(Profile);
        Assert.NotNull(p);
        Assert.Equal("lockedlou", p!.Username);
        Assert.Equal("https://api.chaster.app/users/avatar/u1.jpg", p.Avatar!.AbsoluteUri);
    }

    [Fact]
    public void Parse_without_an_avatar_keeps_the_name()
    {
        var p = ChasterClient.ParseProfile("{\"username\":\"  lou  \",\"avatarUrl\":null}");
        Assert.Equal("lou", p!.Username);
        Assert.Null(p.Avatar);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"username\":\"\"}")]
    [InlineData("{\"username\":42}")]
    [InlineData("{\"username\":{\"a\":1}}")]
    public void Parse_without_a_username_is_nothing(string? json) =>
        Assert.Null(ChasterClient.ParseProfile(json));

    [Theory]
    [InlineData("https://api.chaster.app/users/avatar/u1.jpg", true)]
    [InlineData("https://chaster.app/a.png", true)]
    [InlineData("https://cdn.chaster.app/a.png", true)]
    [InlineData("http://api.chaster.app/a.png", false)]
    [InlineData("https://evil.example/a.png", false)]
    [InlineData("https://chaster.app.evil.example/a.png", false)]
    [InlineData("https://notchaster.app/a.png", false)]
    [InlineData("https://api.chaster.app:8443/a.png", false)]
    [InlineData("https://user:pw@api.chaster.app/a.png", false)]
    [InlineData("file:///C:/a.png", false)]
    [InlineData("/users/avatar/u1.jpg", false)]
    public void Avatar_only_over_https_from_chaster_hosts(string url, bool ok) =>
        Assert.Equal(ok, ChasterClient.SafeAvatarUri(url) != null);

    [Fact]
    public void A_foreign_avatar_is_dropped_but_the_name_stays()
    {
        var p = ChasterClient.ParseProfile("{\"username\":\"lou\",\"avatarUrl\":\"https://tracker.example/p.gif\"}");
        Assert.Equal("lou", p!.Username);
        Assert.Null(p.Avatar);
    }

    [Fact]
    public async Task Profile_is_asked_of_chaster_with_the_players_token()
    {
        var handler = new FakeHandler { Answer = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Profile) } };
        using var client = new ChasterClient(handler);
        var result = await client.GetProfileAsync("AT");
        Assert.True(result.Ok);
        Assert.Equal("lockedlou", result.Value!.Username);
        var (url, bearer) = Assert.Single(handler.Seen);
        Assert.Equal("https://api.chaster.app/auth/profile", url.AbsoluteUri);
        Assert.Equal("AT", bearer);
    }

    [Theory]
    [InlineData(401, ChasterStatus.Unauthorized)]
    [InlineData(503, ChasterStatus.Unavailable)]
    public async Task A_failed_profile_maps_like_every_other_call(int code, ChasterStatus expected)
    {
        var handler = new FakeHandler { Answer = _ => new HttpResponseMessage((HttpStatusCode)code) };
        using var client = new ChasterClient(handler);
        Assert.Equal(expected, (await client.GetProfileAsync("AT")).Status);
    }

    [Fact]
    public async Task A_200_with_no_username_reads_as_unavailable()
    {
        var handler = new FakeHandler { Answer = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") } };
        using var client = new ChasterClient(handler);
        Assert.Equal(ChasterStatus.Unavailable, (await client.GetProfileAsync("AT")).Status);
    }

    [Fact]
    public async Task Avatar_bytes_are_fetched_without_the_token_and_capped()
    {
        var small = new byte[] { 1, 2, 3 };
        var handler = new FakeHandler
        {
            Answer = r => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(r.RequestUri!.AbsolutePath.Contains("big") ? new byte[ChasterClient.MaxAvatarBytes + 1] : small),
            },
        };
        using var client = new ChasterClient(handler);
        Assert.Equal(small, await client.GetAvatarBytesAsync(new Uri("https://api.chaster.app/users/avatar/u1.jpg")));
        Assert.Null(handler.Seen[0].Bearer);
        Assert.Null(await client.GetAvatarBytesAsync(new Uri("https://api.chaster.app/users/avatar/big.jpg")));
        Assert.Null(await client.GetAvatarBytesAsync(new Uri("https://evil.example/a.jpg")));
        Assert.Equal(2, handler.Seen.Count); // the foreign one never left the machine
    }
}
