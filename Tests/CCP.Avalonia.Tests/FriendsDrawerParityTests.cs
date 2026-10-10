using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.Friends.Feed;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Drawer parity with WPF 7.1.5 (wave 4, r7): the row menu, "What happened", the once-only
/// presence ask, the bell and the leash slot. Swaps process-wide seams, so it runs alone.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class FriendsDrawerParityTests
{
    private const string State = """
        {"ok":true,"code":"CCP-ABCDE","me":{"activity":"panel","lock_day":null,"shared":false},
         "friends":[{"id":"u_on","name":"Mia","tier":0,"online":true,"activity":"session","lock_day":3,"last_seen":null,"squelched":false}],
         "incoming":[],"outgoing":[],"blocked":[]}
        """;

    private sealed class Wire : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var op = r.RequestUri!.AbsolutePath.Split('/').Last();
            var body = op == "state" ? State : op == "poll" ? """{"ok":true,"online":[],"inbox":[],"receipts":[]}""" : """{"ok":true}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    private static FriendsService Service(bool shared = false)
    {
        var api = new FriendsApi(new HttpClient(new Wire()), () => ("u_me", "tok"), "http://127.0.0.1:9");
        return new FriendsService(api, () => "u_me", () => true, null, () => shared, v => shared = v);
    }

    private static T? Tagged<T>(Control root, string tag) where T : Control =>
        root.GetLogicalDescendants().OfType<T>().FirstOrDefault(c => c.Tag as string == tag);

    private static void Click(Control root, string tag) =>
        Tagged<Button>(root, tag)!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

    private static async Task<FriendsDrawer> Drawer(FriendsService svc)
    {
        var d = new FriendsDrawer(svc);
        await svc.RefreshAsync();
        d.Render();
        return d;
    }

    /// <summary>Owner report 2026-10-09: the first line was empty (an explicit null Foreground draws no text).
    /// WPF BuildMenu: Squelch, a line, Remove, Block, Report (four reasons), every line worded, the menu dark.</summary>
    [Fact]
    public Task TheRowMenuMatchesWpfAndEveryLineIsWorded() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var d = await Drawer(Service());
        var row = Tagged<Border>(d, "friends-row:u_on")!;
        var menu = Assert.IsType<ContextMenu>(row.ContextMenu);
        var items = menu.ItemsSource!.Cast<Control>().ToList();
        Assert.Equal(new[] { "friends-menu-item:squelch", "friends-menu-separator", "friends-menu-item:remove", "friends-menu-item:block", "friends-menu-item:report" },
            items.Select(i => i.Tag as string).ToArray());
        foreach (var mi in items.OfType<MenuItem>())
        {
            var header = Assert.IsType<TextBlock>(mi.Header);
            Assert.False(string.IsNullOrWhiteSpace(header.Text));
            Assert.NotNull(mi.Foreground);
            Assert.NotNull(header.Foreground);
        }
        var report = items.OfType<MenuItem>().Single(m => (string)m.Tag! == "friends-menu-item:report");
        Assert.Equal(ReportReason.All.Count, report.ItemsSource!.Cast<object>().Count());
        Assert.NotNull(menu.Background);
        Assert.Equal(PlacementMode.Pointer, menu.Placement);
        // WPF BuildFriendRow: a friend in a lock wears the day chip.
        Assert.NotNull(Tagged<Border>(d, "friends-lock"));
    });

    /// <summary>WPF FriendsDrawer.Feed: lines newest first under the leash, read once shown in an open drawer.</summary>
    [Fact]
    public Task WhatHappenedShowsTheFeedAndReadsIt() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var feed = new FriendsFeed(() => "u_me");
        var t0 = DateTime.UtcNow.AddMinutes(-10);
        feed.Add(new FriendEvent(FriendEventKind.PokeReceived, "u_on", "Mia", t0, "k1", null, "hi"));
        feed.Add(new FriendEvent(FriendEventKind.PokeReceived, "u_on", "Mia", t0.AddMinutes(1), "k2", null, "hi"));
        var before = FriendsDrawer.FeedSource;
        var asked = PresenceAsk.Asked;
        try
        {
            FriendsDrawer.FeedSource = () => feed;
            PresenceAsk.Asked = () => true;
            var svc = Service();
            var d = new FriendsDrawer(svc);
            await svc.RefreshAsync();
            d.OnOpened();
            d.Render();
            Assert.True(d.FeedSection.IsVisible);
            Assert.Equal(new[] { "k2", "k1" }, d.FeedLineKeys);
            Assert.Equal(0, feed.Unread);
            // Still dotted until the drawer folds.
            Assert.NotNull(Tagged<global::Avalonia.Controls.Shapes.Ellipse>(d, "friends-feed-new"));
            // ...and the dot glows (WPF FriendsLook.Glow(Pink, 6, 0.9)): a BoxShadow halo, never an Effect
            var halo = Tagged<Border>(d, "friends-feed-new-glow");
            Assert.NotNull(halo);
            Assert.Equal(6, halo!.BoxShadow[0].Blur);
            Assert.Equal((byte)Math.Round(255 * 0.9), halo.BoxShadow[0].Color.A);
            Assert.Null(Tagged<global::Avalonia.Controls.Shapes.Ellipse>(d, "friends-feed-new")!.Effect);
            d.OnClosed();
        }
        finally { FriendsDrawer.FeedSource = before; PresenceAsk.Asked = asked; }
    });

    /// <summary>WPF FriendsRailChip: the pink badge counts unread feed lines and drops when they are read.</summary>
    [Fact]
    public Task TheRailChipBadgeCountsUnreadFeedLines() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var feed = new FriendsFeed(() => "u_me");
        var svc = Service();
        await svc.RefreshAsync();
        var chip = new FriendsRailChip(svc, feed);
        Assert.Equal(0, chip.UnreadBadge);
        // WPF FriendsLook.Glow(Pink / Mint, 8, 0.7) on the two counts, as BoxShadows on the pills themselves
        foreach (var tag in new[] { "friends-chip-unread", "friends-chip-online" })
        {
            var pill = chip.GetLogicalDescendants().OfType<Border>().Single(x => (x.Tag as string) == tag);
            Assert.Equal(8, pill.BoxShadow[0].Blur);
            Assert.Equal((byte)Math.Round(255 * 0.7), pill.BoxShadow[0].Color.A);
            Assert.Null(pill.Effect);
        }
        feed.Add(new FriendEvent(FriendEventKind.PokeReceived, "u_on", "Mia", DateTime.UtcNow, "b1", null, "hi"));
        feed.Add(new FriendEvent(FriendEventKind.PokeReceived, "u_on", "Mia", DateTime.UtcNow, "b2", null, "hi"));
        Assert.Equal(2, chip.UnreadBadge);
        feed.MarkAllRead();
        Assert.Equal(0, chip.UnreadBadge);
    });

    /// <summary>WPF RenderList: the leash slot is pinned first, then the feed, then the people.</summary>
    [Fact]
    public Task TheLeashSlotSitsOnTopOfTheList() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var d = await Drawer(Service());
        var slot = Tagged<StackPanel>(d, "friends-leash-slot");
        Assert.Same(d.LeashSlot, slot);
        var list = Assert.IsAssignableFrom<Panel>(slot!.Parent);
        Assert.Same(slot, list.Children[0]);
        Assert.Equal("friends-feed", list.Children[1].Tag);
        d.Render();
        Assert.Same(slot, list.Children[0]);
    });

    /// <summary>WPF RenderAsk: hidden and never asked shows the strip; "Not now" answers it for good.</summary>
    [Fact]
    public Task ThePresenceAskShowsOnceAndNotNowAnswersIt() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var (asked, mark) = (PresenceAsk.Asked, PresenceAsk.MarkAsked);
        var flag = false;
        try
        {
            PresenceAsk.Asked = () => flag;
            PresenceAsk.MarkAsked = () => flag = true;
            var d = await Drawer(Service(shared: false));
            Assert.NotNull(Tagged<Border>(d, "friends-presence-ask"));
            Click(d, "friends-presence-no");
            Assert.True(flag);
            Assert.Null(Tagged<Border>(d, "friends-presence-ask"));
        }
        finally { PresenceAsk.Asked = asked; PresenceAsk.MarkAsked = mark; }
    });

    /// <summary>WPF RenderFoot: Settings, the bell (corner notices), Blocked; Add friend on the right.</summary>
    [Fact]
    public Task TheFootHasTheBellAndItFlipsTheNotices() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var (on, toggle) = (FriendsDrawer.NoticesOn, FriendsDrawer.ToggleNotices);
        var state = true;
        try
        {
            FriendsDrawer.NoticesOn = () => state;
            FriendsDrawer.ToggleNotices = () => state = !state;
            var d = await Drawer(Service());
            foreach (var tag in new[] { "friends-settings", "friends-notices", "friends-blocked-toggle", "friends-add-open" })
                Assert.NotNull(Tagged<Button>(d, tag));
            var bell = Tagged<Button>(d, "friends-notices")!;
            Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("friends_notices_on"), ToolTip.GetTip(bell));
            Click(d, "friends-notices");
            Assert.False(state);
            Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("friends_notices_off"), ToolTip.GetTip(bell));
        }
        finally { FriendsDrawer.NoticesOn = on; FriendsDrawer.ToggleNotices = toggle; }
    });
}
