using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The friends drawer against a fake service: which rows land in which section and in what
/// order, the expanded card's four actions, how a send and an add are worded, and the chip's
/// online pill. The drawer is realized on the shared STA thread; nothing touches the network.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public partial class FriendsDrawerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static Friend F(string id, string name, bool online, int minutesAgo = 0, int? lockDay = null,
        PresenceActivity act = PresenceActivity.Panel, bool squelched = false)
        => new(id, name, null, 0, online,
            new FriendPresence(online ? act : PresenceActivity.Offline, lockDay, Now.AddMinutes(-minutesAgo)), squelched);

    private static FriendsSnapshot Sample() => new(
        new[]
        {
            F("sam", "Sam", true, act: PresenceActivity.BackRoom, lockDay: 6),
            F("kit", "Kit", true, act: PresenceActivity.GoonHosting),
            F("noor", "Noor", false, minutesAgo: 60 * 72),
            F("robin", "Robin", false, minutesAgo: 120, lockDay: 12),
        },
        new[] { new FriendRequest("dee", "Dee", null, "Sam", Now) },
        new[] { new FriendRequest("ash", "Ash", null, null, Now) },
        "CCP-7K2Q9",
        FriendPresence.None);

    private static FriendsDrawer NewDrawer(FakeFriends svc)
    {
        PresenceAsk.Asked = () => true;
        PresenceAsk.MarkAsked = () => { };
        var d = new FriendsDrawer(svc) { MeName = () => "cb", MeTier = () => 0 };
        d.Render();
        return d;
    }

    // ---- pure rules --------------------------------------------------------------------

    [Theory]
    [InlineData("ccp-7k2q9", "7K2Q9")]
    [InlineData("7k2q9xyz", "7K2Q9")]
    [InlineData(" o0i1ab ", "AB")]
    [InlineData(null, "")]
    public void Code_is_uppercased_and_filtered_to_the_alphabet(string? typed, string expected)
        => Assert.Equal(expected, FriendsDrawerRules.NormaliseCode(typed));

    [Fact]
    public void Full_code_needs_five_characters()
    {
        Assert.Null(FriendsDrawerRules.FullCode("7K2Q"));
        Assert.Equal("CCP-7K2Q9", FriendsDrawerRules.FullCode("7k2q9"));
    }

    [Fact]
    public void Card_actions_are_in_the_owners_order()
        => Assert.Equal(new[] { "invite", "poke", "watch", "more" }, FriendsDrawerRules.CardActions);

    [Fact]
    public void A_squelched_send_reads_like_any_other_send()
    {
        Assert.Equal(FriendsDrawerRules.SendResultKey(SendResult.Sent), FriendsDrawerRules.SendResultKey(SendResult.Squelched));
        Assert.Equal("friends_result_too_fast", FriendsDrawerRules.SendResultKey(SendResult.TooFast));
    }

    [Fact]
    public void Last_seen_buckets()
    {
        Assert.Equal("friends_seen_now", FriendsDrawerRules.SeenKey(Now.AddSeconds(-30), Now).Key);
        Assert.Equal(("friends_seen_minutes", (int?)15), FriendsDrawerRules.SeenKey(Now.AddMinutes(-15), Now));
        Assert.Equal(("friends_seen_hours", (int?)3), FriendsDrawerRules.SeenKey(Now.AddHours(-3), Now));
        Assert.Equal("friends_seen_yesterday", FriendsDrawerRules.SeenKey(Now.AddHours(-30), Now).Key);
        Assert.Equal(("friends_seen_days", (int?)4), FriendsDrawerRules.SeenKey(Now.AddDays(-4), Now));
        Assert.Equal("friends_seen_long", FriendsDrawerRules.SeenKey(DateTimeOffset.MinValue, Now).Key);
    }

    [Fact]
    public void Came_online_lists_only_the_new_arrivals()
    {
        var before = Sample();
        var after = before with { Friends = before.Friends.Select(f => f.Id == "robin" ? f with { Online = true } : f).ToList() };
        Assert.Equal(new[] { "robin" }, FriendsDrawerRules.CameOnline(before, after));
    }

    [Fact]
    public void Every_drawer_key_is_in_all_nine_languages()
    {
        var keys = new List<string>();
        foreach (var id in PokeSet.All) keys.Add("friends_poke_" + id);
        foreach (var id in InviteDestination.All) keys.Add("friends_invite_" + id);
        foreach (var id in WatchRef.Flavours) keys.Add("friends_flavour_" + id);
        foreach (var a in Enum.GetValues<PresenceActivity>()) keys.Add(FriendsDrawerRules.ActivityKey(a));
        foreach (var r in Enum.GetValues<SendResult>()) keys.Add(FriendsDrawerRules.SendResultKey(r));
        foreach (var r in Enum.GetValues<AddResult>()) keys.Add(FriendsDrawerRules.AddResultKey(r));
        foreach (var r in ReportReason.All) keys.Add("friends_menu_report_" + r);

        var dir = System.IO.Path.Combine(RepoRoot(), "ConditioningControlPanel", "Localization", "Languages");
        foreach (var lang in new[] { "en", "de", "es", "fr", "ja", "ko", "pt-BR", "ru", "zh-CN" })
        {
            var json = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(dir, lang + ".json")));
            foreach (var k in keys)
                Assert.True(json[k] != null, $"{lang}.json is missing {k}");
        }
    }

    private static string RepoRoot()
    {
        var d = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !System.IO.Directory.Exists(System.IO.Path.Combine(d.FullName, "ConditioningControlPanel", "Localization")))
            d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    // ---- the drawer --------------------------------------------------------------------

    [Fact]
    public void Rows_land_in_their_sections_online_first()
    {
        Assert.True(PackUriBootstrap.Failure == null, PackUriBootstrap.Failure);
        WpfRenderHarness.OnStaThread(() =>
        {
            var d = NewDrawer(new FakeFriends(Sample()));
            Assert.Equal(new[] { "friends_section_online", "friends_section_offline", "friends_section_requests" }, d.SectionKeys);
            // Online by name, offline by who was here last, then incoming before outgoing.
            Assert.Equal(new[] { "kit", "sam", "robin", "noor", "in:dee", "out:ash" }, d.RowIds);

            var sam = d.RowFor("sam")!;
            Assert.NotNull(Find(sam, "friends-lock"));
            Assert.NotNull(Find(sam, "friends-dot-on"));
            Assert.NotNull(Find(d.RowFor("robin")!, "friends-dot-off"));
            Assert.NotNull(Find(d.RowFor("in:dee")!, "friends-accept"));
            Assert.NotNull(Find(d.RowFor("out:ash")!, "friends-cancel"));

            Layout(d);
        });
    }

    [Fact]
    public void Signed_out_draws_no_rows()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var d = NewDrawer(new FakeFriends(Sample()) { IsAvailable = false });
            Assert.Empty(d.RowIds);
            Assert.Empty(d.SectionKeys);
        });
    }

    [Fact]
    public void Expanded_card_shows_the_four_actions_in_order()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var d = NewDrawer(new FakeFriends(Sample()));
            d.Toggle("sam");
            Assert.Equal("sam", d.OpenFriendId);
            var card = Find(d.RowFor("sam")!, "friends-card");
            Assert.NotNull(card);
            var actions = Tags(card!).Where(t => t.StartsWith("friends-action:")).Select(t => t.Substring(15)).ToList();
            Assert.Equal(new[] { "invite", "poke", "watch", "more" }, actions);

            d.Toggle("sam");
            Assert.Null(d.OpenFriendId);
            Assert.Null(Find(d.RowFor("sam")!, "friends-card"));
        });
    }

    [Fact]
    public void Poke_picker_offers_the_shipped_set()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var d = NewDrawer(new FakeFriends(Sample()));
            d.OpenPickerFor("sam", "poke");
            var pokes = Tags(d.RowFor("sam")!).Where(t => t.StartsWith("friends-poke:")).Select(t => t.Substring(13)).ToList();
            Assert.Equal(PokeSet.Shipped, pokes);
        });
    }

    [Fact]
    public void Invite_tiles_without_a_live_code_are_disabled()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var goon = InviteCodes.GoonCode;
            var remote = InviteCodes.RemoteCode;
            var canHost = InviteCodes.CanHostGoon;
            try
            {
                InviteCodes.GoonCode = () => null;
                InviteCodes.RemoteCode = () => null;
                InviteCodes.CanHostGoon = () => true;
                var d = NewDrawer(new FakeFriends(Sample()));
                d.OpenPickerFor("sam", "invite");
                var row = d.RowFor("sam")!;
                // No room yet, but a host can open one from the tile itself.
                Assert.True(((Button)Find(row, "friends-invite:goon")!).IsEnabled);
                Assert.False(((Button)Find(row, "friends-invite:remote")!).IsEnabled);
                Assert.True(((Button)Find(row, "friends-invite:backroom")!).IsEnabled);
                Assert.True(((Button)Find(row, "friends-invite:ramp")!).IsEnabled);

                // An account that cannot host gets the tile disabled with the reason.
                InviteCodes.CanHostGoon = () => false;
                var d2 = NewDrawer(new FakeFriends(Sample()));
                d2.OpenPickerFor("sam", "invite");
                var tile = (Button)Find(d2.RowFor("sam")!, "friends-invite:goon")!;
                Assert.False(tile.IsEnabled);
                Assert.Equal(Loc.Get("friends_invite_goon_prime"), tile.ToolTip);
            }
            finally
            {
                InviteCodes.GoonCode = goon;
                InviteCodes.RemoteCode = remote;
                InviteCodes.CanHostGoon = canHost;
            }
        });
    }

    [Fact]
    public void Goon_invite_opens_a_room_then_sends_its_code()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var goon = InviteCodes.GoonCode;
            var canHost = InviteCodes.CanHostGoon;
            var open = InviteCodes.OpenGoonRoom;
            try
            {
                int opens = 0;
                InviteCodes.GoonCode = () => null;
                InviteCodes.CanHostGoon = () => true;
                InviteCodes.OpenGoonRoom = _ => { opens++; return Task.FromResult<(string?, bool)>(("7QK4RM", false)); };
                var svc = new FakeFriends(Sample()) { NextSend = SendResult.Sent };
                var d = NewDrawer(svc);
                var r = d.InviteToGoonAsync("sam").GetAwaiter().GetResult();
                Assert.Equal(SendResult.Sent, r);
                Assert.Equal(1, opens);
                Assert.Equal(("sam", "goon", "7QK4RM"), svc.Invites.Single());

                // A room already waiting is sent as is, no second room.
                InviteCodes.GoonCode = () => "ABC123";
                d.InviteToGoonAsync("kit").GetAwaiter().GetResult();
                Assert.Equal(1, opens);
                Assert.Equal(("kit", "goon", "ABC123"), svc.Invites.Last());
            }
            finally
            {
                InviteCodes.GoonCode = goon;
                InviteCodes.CanHostGoon = canHost;
                InviteCodes.OpenGoonRoom = open;
            }
        });
    }

    [Fact]
    public void Goon_invite_mid_match_or_failed_open_sends_nothing_and_says_why()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var goon = InviteCodes.GoonCode;
            var canHost = InviteCodes.CanHostGoon;
            var open = InviteCodes.OpenGoonRoom;
            try
            {
                InviteCodes.GoonCode = () => null;
                InviteCodes.CanHostGoon = () => true;
                InviteCodes.OpenGoonRoom = _ => Task.FromResult<(string?, bool)>((null, true));
                var svc = new FakeFriends(Sample());
                var d = NewDrawer(svc);
                Assert.Null(d.InviteToGoonAsync("sam").GetAwaiter().GetResult());
                Assert.Equal(Loc.Get("friends_invite_goon_busy"), d.ResultTextFor("sam"));

                InviteCodes.OpenGoonRoom = _ => Task.FromResult<(string?, bool)>((null, false));
                Assert.Null(d.InviteToGoonAsync("sam").GetAwaiter().GetResult());
                Assert.Equal(Loc.Get("friends_invite_goon_failed"), d.ResultTextFor("sam"));

                InviteCodes.CanHostGoon = () => false;
                Assert.Null(d.InviteToGoonAsync("sam").GetAwaiter().GetResult());
                Assert.Equal(Loc.Get("friends_invite_goon_prime"), d.ResultTextFor("sam"));
                Assert.Empty(svc.Invites);
            }
            finally
            {
                InviteCodes.GoonCode = goon;
                InviteCodes.CanHostGoon = canHost;
                InviteCodes.OpenGoonRoom = open;
            }
        });
    }

    [Fact]
    public void A_send_is_worded_in_the_row()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var svc = new FakeFriends(Sample()) { NextSend = SendResult.TooFast };
            var d = NewDrawer(svc);
            var r = d.PokeAsync("sam", "hi").GetAwaiter().GetResult();
            Assert.Equal(SendResult.TooFast, r);
            Assert.Equal(("sam", "hi"), svc.Pokes.Single());
            Assert.Equal(Loc.Get("friends_result_too_fast"), d.ResultTextFor("sam"));
            Assert.NotNull(Find(d.RowFor("sam")!, "friends-result"));

            svc.NextSend = SendResult.Sent;
            d.SendWatchAsync("kit", new WatchRef(WatchKind.Flavour, "pink", "Pink")).GetAwaiter().GetResult();
            Assert.Equal(Loc.Get("friends_result_sent"), d.ResultTextFor("kit"));

            // A watch that breaks the grammar never reaches the wire.
            Assert.Equal(SendResult.Refused, d.SendWatchAsync("kit", new WatchRef(WatchKind.Ht, "12ab", null)).GetAwaiter().GetResult());
            Assert.Single(svc.Watches);
        });
    }

    [Fact]
    public void Add_by_code_sends_the_whole_code_and_words_the_answer()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var svc = new FakeFriends(Sample()) { NextAdd = AddResult.NotFound };
            var d = NewDrawer(svc);
            var r = d.AddByCodeAsync("ccp-7k2q9").GetAwaiter().GetResult();
            Assert.Equal(AddResult.NotFound, r);
            Assert.Equal("CCP-7K2Q9", svc.Adds.Single());
            Assert.Equal(Loc.Get("friends_add_not_found"), d.AddResultText);
        });
    }

    [Fact]
    public void Chip_pill_counts_friends_online()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            PresenceAsk.Asked = () => true;
            var svc = new FakeFriends(Sample());
            var chip = new FriendsRailChip(svc);
            chip.Rebind();
            Assert.Equal(2, chip.PillCount);

            var none = Sample() with { Friends = Sample().Friends.Select(f => f with { Online = false }).ToList() };
            svc.Push(none);
            Assert.Equal(0, chip.PillCount);

            svc.Push(Sample());
            Assert.Equal(2, chip.PillCount);
        });
    }

    // ---- helpers -----------------------------------------------------------------------

    private static void Layout(FrameworkElement e)
    {
        e.Measure(new Size(FriendsDrawer.DrawerWidth, FriendsDrawer.DrawerMaxHeight));
        e.Arrange(new Rect(0, 0, FriendsDrawer.DrawerWidth, FriendsDrawer.DrawerMaxHeight));
        e.UpdateLayout();
    }

    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root))
            if (child is DependencyObject d)
                foreach (var x in Walk(d)) yield return x;
    }

    private static IEnumerable<string> Tags(DependencyObject root)
        => Walk(root).OfType<FrameworkElement>().Select(f => f.Tag as string).Where(t => t != null)!;

    private static FrameworkElement? Find(DependencyObject root, string tag)
        => Walk(root).OfType<FrameworkElement>().FirstOrDefault(f => f.Tag as string == tag);

    private sealed class FakeFriends : IFriendsService
    {
        private FriendsSnapshot _snap;

        public FakeFriends(FriendsSnapshot snap) { _snap = snap; }

        public bool IsAvailable { get; set; } = true;
        public SendResult NextSend { get; set; } = SendResult.Sent;
        public AddResult NextAdd { get; set; } = AddResult.Sent;
        public List<(string, string)> Pokes { get; } = new();
        public List<WatchRef> Watches { get; } = new();
        public List<string> Adds { get; } = new();

        public void Push(FriendsSnapshot s) { _snap = s; SnapshotChanged?.Invoke(s); }

        public bool Available => IsAvailable;
        public FriendsSnapshot Snapshot => _snap;
        public bool PresenceShared { get; set; }
        public event Action<FriendsSnapshot>? SnapshotChanged;
        public event Action<InboxItem>? Delivered { add { } remove { } }
        public event Action<SendKind, Friend>? Sent { add { } remove { } }
        public event Action<FriendRequest>? RequestArrived { add { } remove { } }
        public event Action<string>? RequestGone { add { } remove { } }
        public Task RefreshAsync() => Task.CompletedTask;
        public Task<SendResult> PokeAsync(string friendId, string pokeId) { Pokes.Add((friendId, pokeId)); return Task.FromResult(NextSend); }
        public List<(string, string, string?)> Invites { get; } = new();
        public Task<SendResult> InviteAsync(string friendId, string destination, string? code)
        {
            Invites.Add((friendId, destination, code));
            return Task.FromResult(NextSend);
        }
        public Task<SendResult> SendWatchAsync(string friendId, WatchRef watch) { Watches.Add(watch); return Task.FromResult(NextSend); }
        public Task<AddResult> AddByCodeAsync(string code) { Adds.Add(code); return Task.FromResult(NextAdd); }
        public Task AcceptAsync(string requesterId) => Task.CompletedTask;
        public Task DeclineAsync(string requesterId) => Task.CompletedTask;
        public Task CancelRequestAsync(string targetId) => Task.CompletedTask;
        public Task RemoveAsync(string friendId) => Task.CompletedTask;
        public Task BlockAsync(string friendId) => Task.CompletedTask;
        public Task UnblockAsync(string friendId) => Task.CompletedTask;
        public Task SetSquelchAsync(string friendId, bool on) => Task.CompletedTask;
        public Task ReportAsync(string friendId, string reason) => Task.CompletedTask;
        public void SetActivity(PresenceActivity activity) { }
        public void SetDrawerOpen(bool open) { }
    }
}
