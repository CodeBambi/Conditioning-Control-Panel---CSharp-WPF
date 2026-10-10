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
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls.Friends;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.GoonGame;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Drawer parity with WPF 7.1.5 (wave 5, r9): open tables (hosting first, Join or a lock),
/// tier plates, page mode, the drawer's sounds and juice seams. Swaps process-wide seams (motion
/// level, sfx player), so it runs alone.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class FriendsDrawerRestTests : IDisposable
{
    // A pressed button plays its click through CoreAudio unless a test says otherwise: real sound on the
    // desk, and a live audio engine keeps the last shell of ShellMemoryTests alive. Silent by default here.
    public FriendsDrawerRestTests() => FriendsSfx.Player = (_, _, _) => { };
    public void Dispose() => FriendsSfx.Player = null;

    private const string State = """
        {"ok":true,"code":"CCP-ABCDE","me":{"activity":"panel","lock_day":null,"shared":true},
         "friends":[{"id":"u_on","name":"Mia","tier":2,"online":true,"activity":"panel","lock_day":null,"last_seen":null,"squelched":false},
                    {"id":"u_off","name":"Zed","tier":0,"online":false,"activity":"online","lock_day":null,"last_seen":"2026-09-01T00:00:00Z","squelched":false}],
         "incoming":[],"outgoing":[],"blocked":[]}
        """;

    private sealed class Wire : HttpMessageHandler
    {
        public string Body = State;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var op = r.RequestUri!.AbsolutePath.Split('/').Last();
            var body = op == "state" ? Body : op == "poll" ? """{"ok":true,"online":[],"inbox":[],"receipts":[]}""" : """{"ok":true}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    private static FriendsService Service(Wire? wire = null)
    {
        var api = new FriendsApi(new HttpClient(wire ?? new Wire()), () => ("u_me", "tok"), "http://127.0.0.1:9");
        bool shared = true;
        return new FriendsService(api, () => "u_me", () => true, null, () => shared, v => shared = v);
    }

    private static OpenTablesReply Hosting(string friendId) => new(false, true,
        new[] { new OpenTable("ABCD12", "Zed", 3, null, true, friendId, false, 30, true, 10) }, null);

    private static T? Tagged<T>(Control root, string tag) where T : Control =>
        root.GetLogicalDescendants().OfType<T>().FirstOrDefault(c => c.Tag as string == tag);

    private static async Task<FriendsDrawer> Drawer(FriendsService svc, bool asPage = false, Func<OpenTablesReply>? tables = null)
    {
        var d = new FriendsDrawer(svc, asPage) { RefreshTables = () => { }, Tables = tables ?? (() => OpenTablesReply.Empty) };
        await svc.RefreshAsync();
        d.Render();
        return d;
    }

    private static List<string> RowOrder(FriendsDrawer d) =>
        d.GetLogicalDescendants().OfType<Border>().Select(b => b.Tag as string).Where(t => t != null && t.StartsWith("friends-row:")).ToList()!;

    /// <summary>WPF FriendsDrawer.Tables: a friend hosting a table floats to the top (even offline),
    /// wears "hosting a table" and a Join that runs the join seam and folds the drawer.</summary>
    [Fact]
    public Task AHostingFriendFloatsUpWithAJoinButton() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var d = await Drawer(Service(), tables: () => Hosting("u_off"));
        d.CanJoinTables = () => true;
        d.Render();
        Assert.Equal(new[] { "friends-row:u_off", "friends-row:u_on" }, RowOrder(d).ToArray());
        Assert.NotNull(Tagged<TextBlock>(d, "friends-table-hosting"));
        string? joined = null;
        bool closed = false;
        d.JoinTable = code => joined = code;
        d.CloseRequested += () => closed = true;
        Tagged<Button>(d, "friends-table-join")!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("ABCD12", joined);
        Assert.True(closed);
    });

    /// <summary>Signed out: a lock, and its press never launches anything.</summary>
    [Fact]
    public Task SignedOutSeesALockThatNeverJoins() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var d = await Drawer(Service(), tables: () => Hosting("u_off"));
        d.CanJoinTables = () => false;
        d.Render();
        Assert.Null(Tagged<Button>(d, "friends-table-join"));
        bool joined = false;
        d.JoinTable = _ => joined = true;
        Tagged<Button>(d, "friends-table-locked")!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.False(joined);
    });

    /// <summary>No tables: the list keeps the online / offline order and has no pink rows.</summary>
    [Fact]
    public Task NoTablesNoPinkRows() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var d = await Drawer(Service());
        Assert.Equal(new[] { "friends-row:u_on", "friends-row:u_off" }, RowOrder(d).ToArray());
        Assert.Null(Tagged<TextBlock>(d, "friends-table-hosting"));
    });

    /// <summary>WPF FriendsLook.TierPlate: a Prime friend wears the tier 2 plate, a free one none; the
    /// header shows your own plate.</summary>
    [Fact]
    public Task TierPlatesOnRowsAndHeader() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var d = await Drawer(Service());
        var art = FriendsDrawer.TierArt;
        FriendsDrawer.TierArt = _ => new global::Avalonia.Media.DrawingImage(new global::Avalonia.Media.DrawingGroup());
        try
        {
            d.MeTier = () => 1;
            d.Render();
            var row = Tagged<Border>(d, "friends-row:u_on")!;
            Assert.NotNull(Tagged<Image>(row, "friends-tier-2"));
            var off = Tagged<Border>(d, "friends-row:u_off")!;
            Assert.Null(off.GetLogicalDescendants().OfType<Image>().FirstOrDefault(i => (i.Tag as string)?.StartsWith("friends-tier") == true));
            Assert.NotNull(Tagged<Image>(d, "friends-tier-1"));
        }
        finally { FriendsDrawer.TierArt = art; }
    });

    /// <summary>WPF asPage: the page fills its column (620 max), has no leash slot, and its empty state
    /// is one line and an Add button that opens the add box.</summary>
    [Fact]
    public Task PageModeFillsTheColumnAndLeavesTheLeash() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var wire = new Wire { Body = """{"ok":true,"code":"CCP-ABCDE","me":{"activity":"panel","lock_day":null,"shared":true},"friends":[],"incoming":[],"outgoing":[],"blocked":[]}""" };
        var d = await Drawer(Service(wire), asPage: true);
        Assert.True(d.AsPage);
        Assert.True(double.IsNaN(d.Width));
        Assert.Equal(FriendsDrawer.PageMaxWidth, d.MaxWidth);
        Assert.Null(d.LeashSlot.Parent);
        Assert.NotNull(Tagged<StackPanel>(d, "friends-page-empty"));
        Tagged<Button>(d, "friends-page-empty-add")!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.True(Tagged<TextBox>(d, "friends-code-box")!.IsEffectivelyVisible || d.GetLogicalDescendants().OfType<TextBox>().Any());

        var popup = await Drawer(Service(wire));
        Assert.Equal(FriendsDrawer.DrawerWidth, popup.Width);
        Assert.NotNull(popup.LeashSlot.Parent);
        Assert.NotNull(Tagged<TextBlock>(popup, "friends-empty"));
    });

    /// <summary>The Friends page hosts the drawer in page mode (WPF FriendsTabView).</summary>
    [Fact]
    public Task FriendsPageHostsThePageModeDrawer() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var page = new FriendsTabView(Service());
        Assert.True(page.Drawer.AsPage);
        return Task.CompletedTask;
    });

    /// <summary>WPF FriendsSfx: same files and scales; an Add that fails says so with the denied sound.</summary>
    [Fact]
    public Task SoundsUseTheWpfFiles() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var played = new List<string>();
        FriendsSfx.Player = (rel, _, _) => played.Add(rel);
        var vol = ConditioningControlPanel.CoreSettings.Current.MasterVolume;
        try
        {
            ConditioningControlPanel.CoreSettings.Current.MasterVolume = 50;
            await Task.Delay(200);   // one gate for every drawer sound: let an earlier test's sound clear the 130 ms floor
            FriendsSfx.DrawerOpen();
            await Task.Delay(150);
            FriendsSfx.Click();
            Assert.Equal(new[] { "chaos/cards_in.mp3", "chaos/ui_click.mp3" }, played.ToArray());
            ConditioningControlPanel.CoreSettings.Current.MasterVolume = 0;
            await Task.Delay(150);
            FriendsSfx.Denied();
            Assert.Equal(2, played.Count);   // master 0 is silence
            ConditioningControlPanel.CoreSettings.Current.MasterVolume = 50;
            var video = FriendsSfx.VideoPlaying;
            FriendsSfx.VideoPlaying = () => true;
            try { await Task.Delay(150); FriendsSfx.Sent(); Assert.Equal(2, played.Count); }   // the mandatory video owns the room
            finally { FriendsSfx.VideoPlaying = video; }
        }
        finally { FriendsSfx.Player = null; ConditioningControlPanel.CoreSettings.Current.MasterVolume = vol; }
    });

    /// <summary>WPF FriendsBlockList: per account, newest first, capped; Remove forgets one.</summary>
    [Fact]
    public void BlockListIsPerAccountAndCapped()
    {
        var store = new List<BlockedEntry>();
        var list = new FriendsBlockList(() => store, s => { store = new List<BlockedEntry>(s); });
        list.Add("a", "u1", "One", DateTimeOffset.UtcNow.AddMinutes(-1));
        list.Add("a", "u2", " Two ", DateTimeOffset.UtcNow);
        list.Add("b", "u3", "Three", DateTimeOffset.UtcNow);
        Assert.Equal(new[] { "u2", "u1" }, list.For("a").Select(e => e.Id).ToArray());
        Assert.Equal("Two", list.For("a")[0].Name);
        Assert.Empty(list.For(null));
        list.Remove("a", "u2");
        Assert.Equal(new[] { "u1" }, list.For("a").Select(e => e.Id).ToArray());
        for (int i = 0; i < FriendsBlockList.Cap + 5; i++) list.Add("c", "x" + i, "X", DateTimeOffset.UtcNow.AddSeconds(i));
        Assert.True(store.Count <= FriendsBlockList.Cap);
    }

    /// <summary>The Blocked list: the server's list when it sends one, else this PC's memory (an old server).</summary>
    [Fact]
    public Task BlockedRowsPreferTheServerList() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var (shared, account) = (FriendsBlockList.Shared, FriendsBlockList.Account);
        try
        {
            var store = new List<BlockedEntry>();
            FriendsBlockList.Shared = new FriendsBlockList(() => store, s => store = new List<BlockedEntry>(s));
            FriendsBlockList.Account = () => "u_me";
            FriendsBlockList.Shared.Add("u_me", "u_local", "Local", DateTimeOffset.UtcNow);
            var old = new Wire { Body = State.Replace(",\"blocked\":[]", "") };
            var d = await Drawer(Service(old));
            Assert.Equal(new[] { "u_local" }, d.BlockedRows().Select(b => b.Id).ToArray());
            var d2 = await Drawer(Service(new Wire { Body = State.Replace("\"blocked\":[]", "\"blocked\":[{\"id\":\"u_srv\",\"name\":\"Srv\"}]") }));
            Assert.Equal(new[] { "u_srv" }, d2.BlockedRows().Select(b => b.Id).ToArray());
        }
        finally { (FriendsBlockList.Shared, FriendsBlockList.Account) = (shared, account); }
    });

    /// <summary>WPF BuildRequestMenu: an incoming request carries Block, then Report by reason.</summary>
    [Fact]
    public Task AnIncomingRequestHasBlockAndReport() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var body = State.Replace("\"incoming\":[]", "\"incoming\":[{\"id\":\"u_req\",\"name\":\"Ann\",\"via\":null,\"at\":null}]");
        var d = await Drawer(Service(new Wire { Body = body }));
        var row = Tagged<Border>(d, "friends-request-in:u_req")!;
        var menu = Assert.IsType<ContextMenu>(row.ContextMenu);
        var tags = menu.ItemsSource!.Cast<Control>().Select(c => c.Tag as string).ToArray();
        Assert.Equal(new[] { "friends-menu-item:block", "friends-menu-item:report" }, tags);
    });

    /// <summary>WPF FriendsRailChip: your tier plate after the name; the online pill bumps when its count rises.</summary>
    [Fact]
    public Task RailChipWearsThePlateAndBumps() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var (art, level) = (FriendsDrawer.TierArt, FriendsDrawer.MotionLevelNow);
        FriendsDrawer.TierArt = _ => new global::Avalonia.Media.DrawingImage(new global::Avalonia.Media.DrawingGroup());
        FriendsDrawer.MotionLevelNow = () => MotionLevel.Full;
        try
        {
            var svc = Service();
            var chip = new FriendsRailChip(svc);
            chip.Drawer.MeTier = () => 2;
            chip.RefreshFace();
            Assert.NotNull(Tagged<Image>(chip, "friends-tier-2"));
            chip.UpdatePill();
            await svc.RefreshAsync();
            chip.UpdatePill();
            var pill = Tagged<Border>(chip, "friends-chip-online")!;
            Assert.Equal(1, chip.PillCount);
            Assert.IsType<global::Avalonia.Media.ScaleTransform>(pill.RenderTransform);
        }
        finally { (FriendsDrawer.TierArt, FriendsDrawer.MotionLevelNow) = (art, level); }
    });

    /// <summary>WPF Sparks / Shockwave: dots and rings land on the drawer's fx layer at Full (BoxShadow
    /// glow, never an Effect) and nothing at Off; a sent chip pops (scale) and sparks.</summary>
    [Fact]
    public Task SparksAndShockwaveDrawOnTheFxLayer() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var level = FriendsDrawer.MotionLevelNow;
        try
        {
            FriendsDrawer.MotionLevelNow = () => MotionLevel.Full;
            var d = await Drawer(Service());
            var fx = Tagged<Canvas>(d, "friends-fx")!;
            var row = Tagged<Border>(d, "friends-row:u_on")!;
            d.Sparks(row, FriendsDrawer.MintC, 5);
            Assert.Equal(5, fx.Children.Count(c => c.Tag as string == "friends-spark"));
            Assert.All(fx.Children, c => Assert.Null(c.Effect));
            d.Shockwave(row, FriendsDrawer.MintC);
            Assert.Equal(2, fx.Children.Count(c => c.Tag as string == "friends-shockwave"));
            d.Pop(row, FriendsDrawer.PinkC);
            Assert.IsType<global::Avalonia.Media.ScaleTransform>(row.RenderTransform);
            fx.Children.Clear();
            FriendsDrawer.MotionLevelNow = () => MotionLevel.Off;
            d.Sparks(row, FriendsDrawer.MintC, 5);
            d.Shockwave(row, FriendsDrawer.MintC);
            Assert.Empty(fx.Children);
        }
        finally { FriendsDrawer.MotionLevelNow = level; }
    });

    /// <summary>Juice: online dots carry the breathing tag, a row has the sheen host, and motion Off
    /// leaves the drawer still (no transform after the entrance).</summary>
    [Fact]
    public Task JuiceRespectsMotionOff() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var level = FriendsDrawer.MotionLevelNow;
        try
        {
            FriendsDrawer.MotionLevelNow = () => MotionLevel.Off;
            var d = await Drawer(Service());
            d.OnOpened();
            Assert.Null(d.RenderTransform);
            Assert.Equal(1, d.Opacity);
            var row = Tagged<Border>(d, "friends-row:u_on")!;
            Assert.IsAssignableFrom<Grid>(row.Child);
            Assert.Contains(d.GetLogicalDescendants().OfType<Border>(), e => e.Tag as string == "friends-dot-on" && e.Effect == null);
            Assert.Equal(1, d.BoxShadow.Count);   // the popup's drop shadow is a BoxShadow, never an Effect
            Assert.Null(d.Effect);
            d.OnClosed();

            FriendsDrawer.MotionLevelNow = () => MotionLevel.Full;
            var full = await Drawer(Service());
            full.OnOpened();
            Assert.IsType<global::Avalonia.Media.TransformGroup>(full.RenderTransform);
            full.OnClosed();
        }
        finally { FriendsDrawer.MotionLevelNow = level; }
    });
}
