using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The friends rail chip and drawer over the real Core FriendsService, its wire faked in
/// process (never a server), and the service's timer seam.</summary>
public sealed class FriendsDrawerTests
{
    private const string State = """
        {"ok":true,"code":"CCP-ABCDE","me":{"activity":"panel","lock_day":null,"shared":false},
         "friends":[{"id":"u_on","name":"Mia","tier":0,"online":true,"activity":"session","lock_day":3,"last_seen":null,"squelched":false},
                    {"id":"u_off","name":"Zed","tier":0,"online":false,"activity":"online","lock_day":null,"last_seen":"2026-09-01T00:00:00Z","squelched":false}],
         "incoming":[{"id":"u_req","name":"Ann","via":null,"at":"2026-09-01T00:00:00Z"}],"outgoing":[],"blocked":[{"id":"u_bad","name":"Bob"}]}
        """;

    private sealed class Wire : HttpMessageHandler
    {
        public readonly List<string> Ops = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var op = r.RequestUri!.AbsolutePath.Split('/').Last();
            lock (Ops) Ops.Add(op);
            var body = op == "state" ? State : op == "poll" ? """{"ok":true,"online":[],"inbox":[],"receipts":[]}""" : """{"ok":true}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    /// <summary>Ticks only when the test says so; <see cref="Restarted"/> completes on the first Start after a tick.</summary>
    private sealed class FakeTimer : IUiTimer
    {
        public TimeSpan Interval { get; set; }
        public event EventHandler? Tick;
        public bool Running;
        public readonly TaskCompletionSource Restarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _ticked;
        public void Start() { Running = true; if (_ticked) Restarted.TrySetResult(); }
        public void Stop() => Running = false;
        public void OnUiThread(Action action) => action();
        public void Fire() { _ticked = true; Tick?.Invoke(this, EventArgs.Empty); }
    }

    private static (FriendsService Svc, Wire Wire) Service(string? account = "u_me")
    {
        var wire = new Wire();
        var shared = false;
        var api = new FriendsApi(new HttpClient(wire), () => account == null ? null : (account, "tok"), "http://127.0.0.1:9");
        return (new FriendsService(api, () => account, () => true, null, () => shared, v => shared = v), wire);
    }
    private static T? Tagged<T>(Control root, string tag) where T : Control =>
        root.GetLogicalDescendants().OfType<T>().FirstOrDefault(c => c.Tag as string == tag);

    [Fact]
    public async Task TheCoreServicePollsOnTheHeadsTimer()
    {
        var (svc, wire) = Service();
        var timer = new FakeTimer();
        svc.Start(timer);
        Assert.True(timer.Running);
        Assert.Empty(wire.Ops);
        timer.Fire();
        await timer.Restarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(new[] { "poll", "state" }, wire.Ops);
        // The state has Mia online and the app is in front: the fast cadence (FriendsPollRule).
        Assert.Equal(TimeSpan.FromSeconds(FriendsPollRule.FastSeconds), timer.Interval);
    }

    [Theory]
    [InlineData("/tmp/sandbox", null, null)]
    [InlineData("/tmp/sandbox", "https://codebambi-proxy.vercel.app", null)]
    [InlineData("/tmp/sandbox", "http://example.com:3001", null)]
    [InlineData("/tmp/sandbox", "http://127.0.0.1:3001/", "http://127.0.0.1:3001")]
    [InlineData(null, null, "https://codebambi-proxy.vercel.app")]
    public void ASandboxReachesOnlyALoopbackFriendsServer(string? userData, string? url, string? expected)
    {
        Assert.Equal(expected, FriendsHead.BaseUrl(userData, url));
        Assert.Equal(expected != null, FriendsHead.Create(userData, url) != null);
    }

    [Fact]
    public Task TheDrawerListsFriendsRequestsAndBlockedAndTheChipCountsOnline() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var (svc, _) = Service();
        var chip = new FriendsRailChip(svc);
        await svc.RefreshAsync();
        Assert.Equal(1, chip.PillCount);
        var d = chip.Drawer;
        d.Render();
        foreach (var tag in new[] { "friends-row:u_on", "friends-row:u_off", "friends-request-in:u_req" }) Assert.NotNull(Tagged<Border>(d, tag));
        Assert.Equal("CCP-ABCDE", Tagged<TextBlock>(d, "friends-my-code")!.Text);
        Assert.Null(Tagged<Border>(d, "friends-blocked:u_bad"));
        d.ToggleBlocked();
        Assert.NotNull(Tagged<Border>(d, "friends-blocked:u_bad"));
    });

    [Fact]
    public Task BlockAsksFirstAndAcceptAndUnblockGoToTheWire() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var (svc, wire) = Service();
        var d = new FriendsDrawer(svc);
        await svc.RefreshAsync();
        var mia = svc.Snapshot.Friends.First(f => f.Id == "u_on");
        await d.RunMenuAsync(mia, "block");
        Assert.DoesNotContain("block", wire.Ops);
        Assert.NotNull(Tagged<Border>(d, "friends-confirm:block"));
        Assert.Equal(ActResult.Done, await d.ConfirmAsync(mia.Id, mia.Name, "block"));
        Assert.Contains("block", wire.Ops);
        Assert.Equal(ActResult.Done, await d.AnswerRequestAsync(svc.Snapshot.Incoming[0], "accept"));
        Assert.Contains("accept", wire.Ops);
        Assert.Equal(ActResult.Done, await d.UnblockAsync(new BlockedFriend("u_bad", "Bob")));
        Assert.Contains("unblock", wire.Ops);
    });

    [Fact]
    public Task RemoveAsksFirstAndReportGoesToTheWire() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var (svc, wire) = Service();
        var d = new FriendsDrawer(svc);
        await svc.RefreshAsync();
        var zed = svc.Snapshot.Friends.First(f => f.Id == "u_off");
        await d.RunMenuAsync(zed, "remove");
        Assert.DoesNotContain("remove", wire.Ops);
        Assert.NotNull(Tagged<Border>(d, "friends-confirm:remove"));
        Assert.Equal(ActResult.Done, await d.ConfirmAsync(zed.Id, zed.Name, "remove"));
        Assert.Contains("remove", wire.Ops);
        Assert.Equal(ActResult.Done, await d.ReportAsync("u_on", "spam"));
        Assert.Contains("report", wire.Ops);
        Assert.Equal(Loc.Get("friends_report_done"), Tagged<TextBlock>(d, "friends-result")!.Text);
    });

    [Fact]
    public Task ThePresenceSwitchFlipsSharingAndAnswersTheAsk() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var (svc, _) = Service();
        var marked = 0;
        var before = PresenceAsk.MarkAsked;
        PresenceAsk.MarkAsked = () => marked++;
        try
        {
            var d = new FriendsDrawer(svc);
            await svc.RefreshAsync();
            d.TogglePresence();
            Assert.True(svc.PresenceShared);
            Assert.Equal(1, marked);
        }
        finally { PresenceAsk.MarkAsked = before; }
    });

    [Fact]
    public Task SignedOutShowsTheSignInButtonAndNoCode() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var d = new FriendsDrawer(Service(account: null).Svc);
        Assert.NotNull(Tagged<Button>(d, "friends-sign-in"));
        Assert.Null(Tagged<TextBlock>(d, "friends-my-code"));
        return Task.CompletedTask;
    });
}
