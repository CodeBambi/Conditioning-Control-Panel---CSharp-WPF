using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Chaster;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The Chaster wire: tokens come from our proxy, lock calls go straight to Chaster with the
/// player's token, add is the only verb, and an outage never reads as a dead link.
/// </summary>
public class ChasterClientTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        public readonly List<(HttpMethod Method, Uri Url, string? Bearer, string? Body)> Seen = new();
        public Func<HttpRequestMessage, HttpResponseMessage> Answer = _ => Json(200, "{}");

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var body = r.Content == null ? null : await r.Content.ReadAsStringAsync(ct);
            Seen.Add((r.Method, r.RequestUri!, r.Headers.Authorization?.Parameter, body));
            return Answer(r);
        }
    }

    private static HttpResponseMessage Json(int status, string body) =>
        new((HttpStatusCode)status) { Content = new StringContent(body) };

    private const string Tokens =
        "{\"access_token\":\"AT\",\"refresh_token\":\"RT\",\"expires_in\":300,\"refresh_expires_in\":0,\"scope\":\"profile locks offline_access\"}";

    [Fact]
    public async Task Tokens_come_from_the_proxy_and_carry_no_bearer()
    {
        var h = new FakeHandler { Answer = _ => Json(200, Tokens) };
        using var client = new ChasterClient(h);
        var state = ChasterClient.NewState();

        var got = await client.ExchangeAsync(state);
        await client.RefreshAsync("RT");

        Assert.True(got.Ok);
        Assert.Equal("AT", got.Value!.AccessToken);
        Assert.Equal(0, got.Value.RefreshExpiresIn);
        Assert.All(h.Seen, s => Assert.Equal("codebambi-proxy.vercel.app", s.Url.Host));
        Assert.All(h.Seen, s => Assert.Null(s.Bearer));
        Assert.Equal(state, JObject.Parse(h.Seen[0].Body!)["state"]!.Value<string>());
        Assert.Equal("RT", JObject.Parse(h.Seen[1].Body!)["refresh_token"]!.Value<string>());
    }

    [Fact]
    public void A_state_is_the_32_hex_the_broker_accepts()
    {
        var state = ChasterClient.NewState();

        Assert.Matches("^[A-F0-9]{32}$", state);
        Assert.NotEqual(state, ChasterClient.NewState());
        Assert.EndsWith("/chaster/authorize?state=" + state, ChasterClient.AuthorizeUrl(state));
    }

    [Fact]
    public async Task A_token_reply_with_no_token_in_it_is_not_a_link()
    {
        using var client = new ChasterClient(new FakeHandler { Answer = _ => Json(200, "{\"ok\":true}") });

        Assert.Equal(ChasterStatus.Unavailable, (await client.RefreshAsync("RT")).Status);
    }

    [Fact]
    public async Task Locks_are_read_straight_from_chaster_and_only_the_wearers_own()
    {
        var h = new FakeHandler
        {
            Answer = _ => Json(200, "[" +
                "{\"_id\":\"aaa111\",\"title\":\"mine\",\"role\":\"wearer\",\"endDate\":\"2026-10-01T10:00:00.000Z\",\"isFrozen\":true,\"unknownField\":{\"x\":1}}," +
                "{\"_id\":\"bbb222\",\"role\":\"keyholder\"}," +
                "{\"_id\":\"ccc333\",\"role\":\"wearer\",\"displayRemainingTime\":false,\"endDate\":\"2026-10-01T10:00:00.000Z\"}," +
                "{\"role\":\"wearer\"}]"),
        };
        using var client = new ChasterClient(h);

        var locks = await client.GetLocksAsync("AT");

        Assert.Equal(new[] { "aaa111", "ccc333" }, locks.Value!.Select(l => l.Id));
        Assert.True(locks.Value![0].IsFrozen);
        Assert.False(locks.Value![0].TimerHidden);
        Assert.True(locks.Value![1].TimerHidden);
        var seen = Assert.Single(h.Seen);
        Assert.Equal("https://api.chaster.app/locks?status=active", seen.Url.ToString());
        Assert.Equal("AT", seen.Bearer);
    }

    [Fact]
    public async Task Adding_time_posts_seconds_to_the_lock()
    {
        var h = new FakeHandler { Answer = _ => new HttpResponseMessage(HttpStatusCode.NoContent) };
        using var client = new ChasterClient(h);

        var result = await client.AddTimeAsync("AT", "aaa111", 750);

        Assert.True(result.Ok);
        var seen = Assert.Single(h.Seen);
        Assert.Equal(HttpMethod.Post, seen.Method);
        Assert.Equal("https://api.chaster.app/locks/aaa111/update-time", seen.Url.ToString());
        Assert.Equal(750, JObject.Parse(seen.Body!)["duration"]!.Value<int>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    [InlineData(ChasterClient.MaxAddSeconds + 1)]
    public async Task Add_is_the_only_verb_and_never_more_than_the_days_cap(int seconds)
    {
        var h = new FakeHandler();
        using var client = new ChasterClient(h);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.AddTimeAsync("AT", "aaa111", seconds));
        Assert.Empty(h.Seen);
    }

    [Theory]
    [InlineData("")]
    [InlineData("../auth/profile")]
    [InlineData("abc?x=1")]
    public async Task A_lock_id_that_is_not_an_id_never_reaches_the_url(string lockId)
    {
        var h = new FakeHandler();
        using var client = new ChasterClient(h);

        await Assert.ThrowsAsync<ArgumentException>(() => client.AddTimeAsync("AT", lockId, 60));
        Assert.Empty(h.Seen);
    }

    [Theory]
    [InlineData(204, ChasterStatus.Ok)]
    [InlineData(401, ChasterStatus.LinkExpired)]
    [InlineData(410, ChasterStatus.LinkExpired)]
    [InlineData(403, ChasterStatus.Refused)]
    [InlineData(404, ChasterStatus.NotFound)]
    [InlineData(429, ChasterStatus.RateLimited)]
    [InlineData(500, ChasterStatus.Unavailable)]
    [InlineData(502, ChasterStatus.Unavailable)]
    public void Only_a_401_or_a_spent_state_reads_as_a_dead_link(int code, ChasterStatus expected)
    {
        Assert.Equal(expected, ChasterClient.Map((HttpStatusCode)code));
    }

    [Fact]
    public async Task A_network_failure_or_a_broken_body_reads_as_unavailable()
    {
        using var down = new ChasterClient(new FakeHandler { Answer = _ => throw new HttpRequestException("no route") });
        using var garbled = new ChasterClient(new FakeHandler { Answer = _ => Json(200, "<html>") });

        Assert.Equal(ChasterStatus.Unavailable, (await down.GetLocksAsync("AT")).Status);
        Assert.Equal(ChasterStatus.Unavailable, (await garbled.GetLocksAsync("AT")).Status);
    }

    [Fact]
    public async Task A_call_nobody_answered_is_told_apart_from_an_outage()
    {
        using var client = new ChasterClient(new FakeHandler { Answer = _ => throw new TaskCanceledException("timeout") });

        Assert.Equal(ChasterStatus.TimedOut, (await client.AddTimeAsync("AT", "aaa111", 60)).Status);
    }

    [Fact]
    public async Task Revoke_never_throws()
    {
        using var client = new ChasterClient(new FakeHandler { Answer = _ => throw new HttpRequestException("no route") });

        await client.RevokeAsync("RT");
    }

    [Theory]
    [InlineData(300, false)]
    [InlineData(61, false)]
    [InlineData(59, true)]
    [InlineData(-10, true)]
    public void A_token_is_refreshed_with_under_a_minute_left(int secondsLeft, bool expected)
    {
        var now = new DateTime(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc);

        Assert.Equal(expected, ChasterClient.NeedsRefresh(now.AddSeconds(secondsLeft), now));
    }
}
