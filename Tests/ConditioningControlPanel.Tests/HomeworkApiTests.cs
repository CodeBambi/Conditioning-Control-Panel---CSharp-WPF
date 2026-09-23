using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Homework;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The homework routes fail quiet (any trouble is idle, never a card), carry the Back Room's token
/// door, and leaving takes effect locally even when the server cannot be reached.
/// </summary>
public class HomeworkApiTests
{
    private const string Today =
        "{\"ok\":true,\"enabled\":true,\"optedIn\":true,\"discordLinked\":false,\"done\":false," +
        "\"current\":{\"day\":\"2026-09-23\",\"url\":\"https://hypnotube.com/video/deep-sleep-42.html\",\"title\":\"Deep Sleep\"}}";

    private sealed class FakeHandler : HttpMessageHandler
    {
        public readonly List<(HttpMethod Method, string Url, string? Token, string? Body)> Seen = new();
        public Func<int, HttpResponseMessage> Answer = _ => Json(200, Today);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var body = r.Content == null ? null : await r.Content.ReadAsStringAsync(ct);
            Seen.Add((r.Method, r.RequestUri!.ToString(), r.Headers.TryGetValues("X-Auth-Token", out var v) ? v.First() : null, body));
            return Answer(Seen.Count);
        }
    }

    private static HttpResponseMessage Json(int status, string body) =>
        new((HttpStatusCode)status) { Content = new StringContent(body) };

    private static (HomeworkApi Api, FakeHandler Fake) Make(string? account = "u_1")
    {
        var fake = new FakeHandler();
        return (new HomeworkApi(new HttpClient(fake), () => account == null ? null : (account, "tok")), fake);
    }

    [Fact]
    public void ParseReadsTheWholePayload()
    {
        var r = HomeworkApi.Parse(Today);
        Assert.True(r.Ok);
        var t = r.Today!;
        Assert.True(t.Enabled && t.OptedIn && !t.DiscordLinked && !t.Done && t.Due);
        Assert.Equal(new HomeworkCurrent("2026-09-23", "https://hypnotube.com/video/deep-sleep-42.html", "Deep Sleep"), t.Current);
    }

    [Fact]
    public void DisabledIsIdle_WhateverElseItSays() =>
        Assert.Equal(HomeworkToday.Idle, HomeworkApi.Parse("{\"ok\":true,\"enabled\":false,\"optedIn\":true}").Today);

    [Theory]
    [InlineData("<html>gateway</html>")]
    [InlineData("{\"reason\":\"closed\"}")]
    [InlineData("")]
    public void AnythingUnreadableIsUnreachable(string text) => Assert.Same(HomeworkReply.Unreachable, HomeworkApi.Parse(text));

    [Fact]
    public void ARefusalKeepsItsReasonAndItsTodayPayload()
    {
        var r = HomeworkApi.Parse(Today.Replace("\"ok\":true", "\"ok\":false,\"reason\":\"not_watched\""));
        Assert.False(r.Ok);
        Assert.False(r.Transient);
        Assert.Equal("not_watched", r.Reason);
        Assert.True(r.Today!.Due);
        Assert.True(HomeworkApi.Parse("{\"ok\":false,\"reason\":\"busy\"}").Transient);
    }

    [Theory]
    [InlineData("{\"day\":\"2026-09-23\",\"url\":\"http://hypnotube.com/video/a.html\",\"title\":\"A\"}")]
    [InlineData("{\"day\":\"2026-09-23\",\"url\":\"https://hypnotube.com/video/a.html\"}")]
    [InlineData("{\"url\":\"https://hypnotube.com/video/a.html\",\"title\":\"A\"}")]
    public void AMalformedPickIsNoHomework(string current)
    {
        var t = HomeworkApi.Parse($"{{\"ok\":true,\"enabled\":true,\"optedIn\":true,\"done\":true,\"current\":{current}}}").Today!;
        Assert.Null(t.Current);
        Assert.False(t.Done);
        Assert.False(t.Due);
    }

    [Fact]
    public async Task RequestsCarryTheTokenDoorAndTheAccount()
    {
        var (api, fake) = Make();
        await api.TodayAsync();
        await api.WatchedAsync("2026-09-23", "https://hypnotube.com/video/deep-sleep-42.html", 581.26, 600);

        Assert.Equal(HttpMethod.Get, fake.Seen[0].Method);
        Assert.EndsWith("/v2/homework/today?unified_id=u_1", fake.Seen[0].Url);
        Assert.Equal("tok", fake.Seen[0].Token);

        Assert.Equal(HttpMethod.Post, fake.Seen[1].Method);
        Assert.EndsWith("/v2/homework/watched", fake.Seen[1].Url);
        var body = JObject.Parse(fake.Seen[1].Body!);
        Assert.Equal("u_1", (string?)body["unified_id"]);
        Assert.Equal("2026-09-23", (string?)body["day"]);
        Assert.Equal(581.3, (double)body["watchedSeconds"]!, 3);
        Assert.Equal(600, (double)body["durationSeconds"]!, 3);
    }

    [Fact]
    public async Task NoAccountSendsNothing()
    {
        var (api, fake) = Make(account: null);
        Assert.Same(HomeworkReply.Unreachable, await api.TodayAsync());
        Assert.Empty(fake.Seen);
    }

    [Fact]
    public async Task AServerErrorIsUnreachable()
    {
        var (api, fake) = Make();
        fake.Answer = _ => Json(409, "{\"ok\":false,\"reason\":\"merged\"}");
        Assert.Same(HomeworkReply.Unreachable, await api.SetOptInAsync(true));
    }

    [Fact]
    public async Task LeavingLetsGoAtOnce_EvenWhenTheServerIsDown_AndIsRetriedOnRefresh()
    {
        var (api, fake) = Make();
        var svc = new HomeworkService(api, () => "u_1", a => a()) { RetryDelay = TimeSpan.Zero };
        await svc.RefreshAsync();
        Assert.True(svc.IsDue);

        fake.Answer = _ => Json(503, "down");
        Assert.True(await svc.SetOptInAsync(false));
        Assert.False(svc.IsDue);
        Assert.False(svc.Current.OptedIn);

        // Still down, and the server still says opted in: the local leave holds.
        await svc.RefreshAsync();
        Assert.False(svc.IsDue);

        fake.Answer = _ => Json(200, Today.Replace("\"optedIn\":true", "\"optedIn\":false"));
        await svc.RefreshAsync();
        var optins = fake.Seen.Where(s => s.Url.EndsWith("/optin")).Select(s => JObject.Parse(s.Body!)).ToList();
        Assert.True(optins.Count >= 2);
        Assert.All(optins, o => Assert.False((bool)o["on"]!));
        Assert.False(svc.IsDue);
    }

    [Fact]
    public async Task ASignOutOrAnotherAccountReadsIdleAtOnce()
    {
        var account = "u_1";
        var fake = new FakeHandler();
        var svc = new HomeworkService(new HomeworkApi(new HttpClient(fake), () => (account, "tok")), () => account, a => a());
        await svc.RefreshAsync();
        Assert.True(svc.IsDue);
        account = "u_2";
        Assert.False(svc.IsDue);
    }

    [Fact]
    public async Task AFailedHandInIsKeptAndSentOnTheNextRefresh()
    {
        var (api, fake) = Make();
        var svc = new HomeworkService(api, () => "u_1", a => a()) { RetryDelay = TimeSpan.Zero };
        var handedIn = 0;
        svc.HandedIn += () => handedIn++;
        var hw = new HomeworkCurrent("2026-09-23", "https://hypnotube.com/video/deep-sleep-42.html", "Deep Sleep");

        await svc.RefreshAsync();
        fake.Answer = _ => Json(502, "bad gateway");
        Assert.False(await svc.HandInAsync(hw, 590, 600));
        Assert.False(svc.IsDue);   // watched is watched, even before the server hears it

        fake.Answer = _ => Json(200, Today.Replace("\"done\":false", "\"done\":true"));
        await svc.RefreshAsync();
        Assert.Equal(1, handedIn);
        Assert.Contains(fake.Seen, s => s.Url.EndsWith("/watched") && s.Body!.Contains("\"watchedSeconds\":590"));
        Assert.False(svc.IsDue);
    }

    [Fact]
    public async Task ARefusedHandInIsDropped_NotRetried()
    {
        var (api, fake) = Make();
        var svc = new HomeworkService(api, () => "u_1", a => a()) { RetryDelay = TimeSpan.Zero };
        var hw = new HomeworkCurrent("2026-09-23", "https://hypnotube.com/video/deep-sleep-42.html", "Deep Sleep");
        fake.Answer = _ => Json(200, Today.Replace("\"ok\":true", "\"ok\":false,\"reason\":\"wrong_video\""));
        Assert.False(await svc.HandInAsync(hw, 590, 600));
        fake.Answer = _ => Json(200, Today);
        await svc.RefreshAsync();
        Assert.Single(fake.Seen, s => s.Url.EndsWith("/watched"));
    }

    [Fact]
    public async Task ABusyLeaveIsAskedAgain()
    {
        var (api, fake) = Make();
        var svc = new HomeworkService(api, () => "u_1", a => a()) { RetryDelay = TimeSpan.Zero };
        fake.Answer = n => n == 1
            ? Json(200, "{\"ok\":false,\"reason\":\"busy\"}")
            : Json(200, Today.Replace("\"optedIn\":true", "\"optedIn\":false"));
        Assert.True(await svc.SetOptInAsync(false));
        Assert.Equal(2, fake.Seen.Count);
        await svc.RefreshAsync();
        Assert.DoesNotContain(fake.Seen.Skip(2), s => s.Url.EndsWith("/optin"));
    }
}
