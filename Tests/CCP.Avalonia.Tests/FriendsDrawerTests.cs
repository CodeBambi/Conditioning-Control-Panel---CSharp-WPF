using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Models;
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
        /// <summary>The bodies of every "send" (poke / invite / watch), in order.</summary>
        public readonly List<string> Sends = new();
        /// <summary>When set, a "send" waits for it: the answer is still out while the test folds the drawer.</summary>
        public TaskCompletionSource? HoldSends;
        public string StateBody = State;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var op = r.RequestUri!.AbsolutePath.Split('/').Last();
            lock (Ops) Ops.Add(op);
            if (op == "send") { Sends.Add(await r.Content!.ReadAsStringAsync(ct)); if (HoldSends != null) await HoldSends.Task; }
            var body = op == "state" ? StateBody : op == "poll" ? """{"ok":true,"online":[],"inbox":[],"receipts":[]}"""
                : op == "send" ? """{"ok":true,"status":"sent"}""" : """{"ok":true}""";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        }
    }

    /// <summary>Lets a click's async handler finish: dispatcher turns, no clock.</summary>
    private static async Task Until(Func<bool> done)
    {
        for (var i = 0; i < 500 && !done(); i++)
            await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, global::Avalonia.Threading.DispatcherPriority.Background);
    }

    private static void Click(Control root, string tag) =>
        Tagged<Button>(root, tag)!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

    /// <summary>WPF FriendsDrawer.BuildCard + Pickers: the card's three actions open their picker inline, and a
    /// poke chip, a flavour chip and the HT box each send through the service and word the answer in the row.</summary>
    [Fact]
    public Task TheCardsPickersSendAPokeAWatchAndAnInvite() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var (svc, wire) = Service();
        var d = new FriendsRailChip(svc).Drawer;
        await svc.RefreshAsync();
        d.Toggle("u_on");
        foreach (var act in new[] { "invite", "poke", "watch" }) Assert.NotNull(Tagged<Button>(d, "friends-action:" + act));
        Assert.Null(d.OpenPicker);

        Click(d, "friends-action:poke");
        Assert.Equal("poke", d.OpenPicker);
        Assert.Equal(PokeSet.Shipped.Count, Tagged<WrapPanel>(d, "friends-picker:poke")!.Children.Count);
        Click(d, "friends-poke:" + PokeSet.Shipped[0]);
        await Until(() => Tagged<TextBlock>(d, "friends-result") != null);
        Assert.Equal(Loc.Get(FriendsDrawerRules.SendResultKey(SendResult.Sent)), Tagged<TextBlock>(d, "friends-result")!.Text);
        // A second poke inside the cooldown is refused by the service (PokeSet.CooldownSeconds) and never sent.
        Assert.Equal(SendResult.TooFast, await d.PokeAsync("u_on", PokeSet.Shipped[1]));
        Assert.Single(wire.Sends);
        d.Render();
        Assert.Contains("\"kind\":\"poke\"", wire.Sends[0]);
        Assert.Contains("\"poke\":\"" + PokeSet.Shipped[0] + "\"", wire.Sends[0]);
        Assert.Equal(Loc.Get(FriendsDrawerRules.SendResultKey(SendResult.TooFast)), Tagged<TextBlock>(d, "friends-result")!.Text);

        Click(d, "friends-action:watch");
        Assert.Equal("watch", d.OpenPicker);
        Click(d, "friends-flavour:trance");
        await Until(() => wire.Sends.Count == 2 && Tagged<TextBlock>(d, "friends-result")?.Text == Loc.Get(FriendsDrawerRules.SendResultKey(SendResult.Sent)));
        Click(d, "friends-watch-tab:ht");
        var box = Tagged<TextBox>(d, "friends-ht-box")!;
        Assert.False(Tagged<Button>(d, "friends-ht-send")!.IsEnabled);
        box.Text = "ht-1234!";
        Assert.Equal("1234", box.Text);   // digits only, as typed (FriendsDrawerRules.NormaliseHtId)
        Assert.True(Tagged<Button>(d, "friends-ht-send")!.IsEnabled);
        Click(d, "friends-ht-send");
        Assert.Equal(SendResult.Sent, await d.SendWatchAsync("u_on", new WatchRef(WatchKind.Flavour, "pink", "Pink")));
        Assert.Contains(wire.Sends, s => s.Contains("\"kind\":\"flavour\"") && s.Contains("\"id\":\"trance\""));
        Assert.Contains(wire.Sends, s => s.Contains("\"kind\":\"ht\"") && s.Contains("\"id\":\"" + FriendsDrawerRules.NormaliseHtId("ht-1234!") + "\""));

        // The Goon room is not hosted on this head: that tile is shut and says why. The chess board is (g1).
        Click(d, "friends-action:invite");
        Assert.Equal("invite", d.OpenPicker);
        Assert.False(Tagged<Button>(d, "friends-invite:" + InviteDestination.Goon)!.IsEnabled);
        Assert.True(Tagged<Button>(d, "friends-invite:" + InviteDestination.Chess)!.IsEnabled);
        Click(d, "friends-invite:" + InviteDestination.BackRoom);
        Assert.Equal(SendResult.Sent, await d.InviteAsync("u_on", InviteDestination.Ramp, null));
        Assert.Contains(wire.Sends, s => s.Contains("\"kind\":\"invite\"") && s.Contains("\"destination\":\"backroom\""));

        // Pressing the lit action again folds its picker; folding the card forgets it.
        Click(d, "friends-action:invite");
        Assert.Null(d.OpenPicker);
        d.OpenPickerFor("u_on", "poke");
        d.Toggle("u_on");
        Assert.Null(d.OpenPicker);
    });

    /// <summary>WPF ShowResult: a send answered after the drawer folded says so outside, over the rail chip's
    /// window (the drawer itself sits in a popup root, then detached). Driven from the rail chip's click.</summary>
    [Fact]
    public Task ASendAnsweredAfterTheDrawerFoldsFliesOverTheChipsWindow() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (global::Avalonia.Application.Current is null)
            global::Avalonia.AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new global::Avalonia.Headless.AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var (svc, wire) = Service();
        var chip = new FriendsRailChip(svc) { Width = 200, HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Bottom };
        var host = new Window { Width = 1000, Height = 700, Content = chip };
        try
        {
            host.Show();
            await svc.RefreshAsync();
            var at = chip.TranslatePoint(new Point(24, 24), host)!.Value;
            global::Avalonia.Headless.HeadlessWindowExtensions.MouseDown(host, at, MouseButton.Left);
            global::Avalonia.Headless.HeadlessWindowExtensions.MouseUp(host, at, MouseButton.Left);
            Assert.True(chip.IsOpen);
            var d = chip.Drawer;
            d.Toggle("u_on");
            Click(d, "friends-action:poke");
            wire.HoldSends = new TaskCompletionSource();
            Click(d, "friends-poke:" + PokeSet.Shipped[0]);
            await Until(() => wire.Sends.Count == 1);
            // Esc folds the picker, then the drawer (WPF OnKey).
            foreach (var _ in new[] { 1, 2 }) d.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            Assert.False(chip.IsOpen);
            wire.HoldSends.SetResult();
            await Until(() => host.OwnedWindows.OfType<FloatingWord>().Any());
            var word = Assert.Single(host.OwnedWindows.OfType<FloatingWord>());
            Assert.Equal(Loc.Get(FriendsDrawerRules.SendResultKey(SendResult.Sent)), word.GetLogicalDescendants().OfType<TextBlock>().Single().Text);

            // Folded, the drawer no longer redraws on every snapshot (P07).
            var row = Tagged<Border>(d, "friends-row:u_on");
            wire.StateBody = State.Replace("\"Zed\"", "\"Zoe\"");
            await svc.RefreshAsync();
            Assert.Equal("Zoe", svc.Snapshot.Friends.Single(f => f.Id == "u_off").Name);
            Assert.Same(row, Tagged<Border>(d, "friends-row:u_on"));
        }
        finally { foreach (var w in host.OwnedWindows.ToArray()) w.Close(); host.Close(); }
    });

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

    /// <summary>audit #1976: a click inside the drawer routes through the Popup to the chip; it must
    /// not toggle the drawer shut (WPF's HWND popup never reached the chip at all).</summary>
    [Fact]
    public Task AClickInsideTheDrawerKeepsItOpen() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var (svc, _) = Service();
        var chip = new FriendsRailChip(svc);
        await svc.RefreshAsync();
        var w = new Window { Width = 400, Height = 600, Content = new StackPanel { VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Bottom, Children = { chip } } };
        w.Show();
        try
        {
            Click(w, chip);
            Assert.True(chip.IsOpen);
            var code = Tagged<TextBlock>(chip.Drawer, "friends-my-code")!;
            Click(TopLevel.GetTopLevel(code)!, code);
            Assert.True(chip.IsOpen);
            Click(w, chip);
            Assert.False(chip.IsOpen);
        }
        finally { w.Close(); }
    });

    private static void Click(TopLevel top, Control c)
    {
        var p = c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), top)!.Value;
        top.MouseDown(p, global::Avalonia.Input.MouseButton.Left);
        top.MouseUp(p, global::Avalonia.Input.MouseButton.Left);
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

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

    /// <summary>WPF FriendsDrawer.Honesty TellOutside(always) -> FriendsLanding.Say -> FloatingWord.Throw:
    /// the removed row is gone, so the word flies over the window, owned and unfocusable, and closes itself.</summary>
    [Fact]
    public Task RemovingAFriendThrowsAFloatingWordOverTheWindowThatClosesItself() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (global::Avalonia.Application.Current is null)
            global::Avalonia.AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new global::Avalonia.Headless.AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var (svc, _) = Service();
        var d = new FriendsDrawer(svc);
        var host = new Window { Width = 1000, Height = 700, Content = d };
        try
        {
            host.Show();
            await svc.RefreshAsync();
            var zed = svc.Snapshot.Friends.First(f => f.Id == "u_off");
            Assert.Equal(ActResult.Done, await d.ConfirmAsync(zed.Id, zed.Name, "remove"));

            var word = Assert.Single(host.OwnedWindows.OfType<FloatingWord>());
            Assert.Equal(Loc.GetF("friends_removed_done", "Zed"), word.GetLogicalDescendants().OfType<TextBlock>().Single().Text);
            Assert.True(word.Topmost);
            Assert.False(word.ShowActivated);
            Assert.False(word.IsHitTestVisible);
            word.Step(800);
            Assert.Contains(word, host.OwnedWindows);
            word.Step(FloatingWord.TotalMs);
            Assert.True(word.IsDone);
            Assert.DoesNotContain(word, host.OwnedWindows);
        }
        finally { foreach (var w in host.OwnedWindows.ToArray()) w.Close(); host.Close(); }
    });

    /// <summary>WPF FloatingWord's keyframes at the stepped times they name (FloatingWord.cs:67-103).</summary>
    [Fact]
    public void TheFloatingWordPoseFollowsWpfsKeyframes()
    {
        const double th = 40;
        var start = FloatingWord.Pose(0, MotionLevel.Full, th);
        Assert.Equal((0.6, -6.0, 10.0, 0.0), (Round(start.Scale), Round(start.Angle), Round(start.Y), Round(start.Opacity)));
        var pop = FloatingWord.Pose(240, MotionLevel.Full, th);   // 15%
        Assert.Equal((1.15, 2.0, 0.0, 1.0), (Round(pop.Scale), Round(pop.Angle), Round(pop.Y), Round(pop.Opacity)));
        Assert.True(FloatingWord.Pose(100, MotionLevel.Full, th).Scale > 1.15 - 0.55 * 0.4167);   // BackEase overshoots linear
        Assert.Equal(1.0, FloatingWord.Pose(960, MotionLevel.Full, th).Opacity);                    // held to 60%
        var end = FloatingWord.Pose(1600, MotionLevel.Full, th);
        Assert.Equal((1.0, 0.0, -64.0, 0.0), (Round(end.Scale), Round(end.Angle), Round(end.Y), Round(end.Opacity)));
        Assert.Equal(-32.0, Round(FloatingWord.Pose(1600, MotionLevel.Reduced, th).Y));            // half the travel
        Assert.Equal((1.0, 0.0, 0.0, 1.0), FloatingWord.Pose(1100, MotionLevel.Off, th));            // still
        Assert.Equal(0.5, Round(FloatingWord.Pose(1300, MotionLevel.Off, th).Opacity));
        Assert.Equal((0.0, 0.0, 0.0), FloatingWord.Spark(180, 0, 100, 600));                         // waits 180 ms
        Assert.Equal(100.0, Round(FloatingWord.Spark(780 - 0.0001, 0, 100, 600).X));
        static double Round(double v) => Math.Round(v, 3) + 0.0;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task TheAddBoxOffersSubscribersTheInviteLink(bool subscriber) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var (svc, _) = Service();
        var d = new FriendsRailChip(svc).Drawer;
        await svc.RefreshAsync();
        d.OffersInviteLink = () => subscriber;
        d.Render();
        var asked = 0;
        d.InvitesRequested += () => asked++;
        Tagged<Button>(d, "friends-add-open")!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(subscriber, Tagged<WrapPanel>(d, "friends-invite-line")!.IsVisible);
        Tagged<Button>(d, "friends-invite-link")!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, asked);
    });
}
