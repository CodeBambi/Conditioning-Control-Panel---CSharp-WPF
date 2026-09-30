using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Friends;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The friends feed store ("What happened"): newest first, one line per key, the newest 200, per
/// account, read and unread, and the file it survives a restart in. Plus the pure rules the drawer,
/// the chips and the tray draw with. No WPF here; the drawer and the chip are in
/// FriendsDrawerFeedTests.
/// </summary>
public class FriendsFeedTests
{
    private static readonly DateTime T0 = new(2026, 9, 29, 10, 0, 0, DateTimeKind.Utc);

    private static FriendEvent Ev(string key, double minutes, FriendEventKind kind = FriendEventKind.PokeReceived,
        string friend = "sam", string? name = "Sam", string? dest = null, string? detail = "hi")
        => new(kind, friend, name, T0.AddMinutes(minutes), key, dest, detail);

    /// <summary>An in-memory disk: one list per account.</summary>
    private sealed class Disk
    {
        public readonly Dictionary<string, List<FeedEntry>> Files = new();
        public int Saves;
        public IReadOnlyList<FeedEntry> Load(string a) => Files.TryGetValue(a, out var l) ? l.ToList() : new List<FeedEntry>();
        public void Save(string a, IReadOnlyList<FeedEntry> l) { Files[a] = l.ToList(); Saves++; }
    }

    private static FriendsFeed NewFeed(Disk disk, Func<string?> account) => new(account, disk.Load, disk.Save);

    // ---- the store ------------------------------------------------------------------------

    [Fact]
    public void Lines_are_newest_first_whatever_order_they_arrive_in()
    {
        var feed = NewFeed(new Disk(), () => "me");
        Assert.True(feed.Add(Ev("b", 5)));
        Assert.True(feed.Add(Ev("a", 1)));
        Assert.True(feed.Add(Ev("c", 9)));
        Assert.Equal(new[] { "c", "b", "a" }, feed.Lines.Select(l => l.Key));
    }

    [Fact]
    public void A_line_as_new_as_another_goes_above_it()
    {
        var feed = NewFeed(new Disk(), () => "me");
        feed.Add(Ev("first", 3));
        feed.Add(Ev("second", 3));
        Assert.Equal(new[] { "second", "first" }, feed.Lines.Select(l => l.Key));
    }

    [Fact]
    public void A_line_lands_once_per_key()
    {
        var disk = new Disk();
        var feed = NewFeed(disk, () => "me");
        var changed = 0;
        feed.Changed += () => changed++;

        Assert.True(feed.Add(Ev("in:1", 1)));
        Assert.False(feed.Add(Ev("in:1", 1)));
        Assert.False(feed.Add(Ev("in:1", 7, detail: "wp")));
        Assert.Single(feed.Lines);
        Assert.Equal(1, changed);
        Assert.Equal(1, disk.Saves);
    }

    [Fact]
    public void Keeps_the_newest_200()
    {
        var feed = NewFeed(new Disk(), () => "me");
        for (var i = 0; i < FriendsFeed.Cap + 5; i++) feed.Add(Ev("k" + i, i));
        var lines = feed.Lines;
        Assert.Equal(FriendsFeed.Cap, lines.Count);
        Assert.Equal("k204", lines[0].Key);
        Assert.Equal("k5", lines[^1].Key);
        Assert.DoesNotContain(lines, l => l.Key == "k4");

        // Older than everything in a full feed: not kept, nothing moves.
        Assert.False(feed.Add(Ev("ancient", -100)));
        Assert.Equal(FriendsFeed.Cap, feed.Lines.Count);
        Assert.Equal("k5", feed.Lines[^1].Key);
    }

    [Fact]
    public void Unread_counts_until_marked_read()
    {
        var feed = NewFeed(new Disk(), () => "me");
        var changed = 0;
        feed.Add(Ev("a", 1));
        feed.Add(Ev("b", 2));
        feed.Add(Ev("c", 3));
        feed.Changed += () => changed++;
        Assert.Equal(3, feed.Unread);

        Assert.Equal(1, feed.MarkRead(new[] { "b", "nope" }));
        Assert.Equal(2, feed.Unread);
        Assert.True(feed.Lines.Single(l => l.Key == "b").Read);
        Assert.Equal(1, changed);

        // Reading it twice changes nothing and says nothing.
        Assert.Equal(0, feed.MarkRead(new[] { "b" }));
        Assert.Equal(0, feed.MarkRead(Array.Empty<string>()));
        Assert.Equal(0, feed.MarkRead(null));
        Assert.Equal(1, changed);

        Assert.Equal(2, feed.MarkAllRead());
        Assert.Equal(0, feed.Unread);
        Assert.Equal(2, changed);
        Assert.Equal(0, feed.MarkAllRead());

        // A new line is unread again.
        feed.Add(Ev("d", 4));
        Assert.Equal(1, feed.Unread);
    }

    [Fact]
    public void Signed_out_keeps_nothing()
    {
        var disk = new Disk();
        var feed = NewFeed(disk, () => null);
        Assert.False(feed.Add(Ev("a", 1)));
        Assert.Empty(feed.Lines);
        Assert.Equal(0, feed.Unread);
        Assert.Equal(0, feed.MarkAllRead());
        Assert.Equal(0, disk.Saves);
    }

    [Fact]
    public void Each_account_sees_only_its_own_lines()
    {
        var disk = new Disk();
        string? account = "u_a";
        var feed = NewFeed(disk, () => account);
        feed.Add(Ev("a1", 1));
        feed.Add(Ev("a2", 2));
        Assert.Equal(2, feed.Unread);

        account = "u_b";
        Assert.Empty(feed.Lines);
        Assert.Equal(0, feed.Unread);
        feed.Add(Ev("b1", 3));
        Assert.Equal(new[] { "b1" }, feed.Lines.Select(l => l.Key));

        account = null;
        Assert.Empty(feed.Lines);

        account = "u_a";
        Assert.Equal(new[] { "a2", "a1" }, feed.Lines.Select(l => l.Key));
        Assert.Equal(new[] { "a2", "a1" }, disk.Files["u_a"].Select(l => l.Key));
        Assert.Equal(new[] { "b1" }, disk.Files["u_b"].Select(l => l.Key));

        // The same key is a different line for a different account.
        account = "u_b";
        Assert.True(feed.Add(Ev("a1", 1)));
    }

    [Fact]
    public void A_broken_disk_reads_empty_and_never_throws()
    {
        var feed = new FriendsFeed(() => "me", _ => throw new IOException("locked"), (_, _) => throw new IOException("full"));
        Assert.Empty(feed.Lines);
        Assert.True(feed.Add(Ev("a", 1)));
        Assert.Equal(1, feed.Unread);
        Assert.Equal(1, feed.MarkAllRead());
    }

    [Fact]
    public void Loaded_lines_are_cleaned_sorted_and_capped()
    {
        var disk = new Disk();
        var stored = new List<FeedEntry>
        {
            new(Ev("old", 1), true),
            new(Ev("new", 9), false),
            new(Ev("new", 8), true),                  // a duplicate key: the newer one wins
            new(Ev("", 5), false),                    // no key
            new(Ev("remote", 6, FriendEventKind.InviteReceived, dest: InviteDestination.Remote), false),
        };
        for (var i = 0; i < FriendsFeed.Cap; i++) stored.Add(new FeedEntry(Ev("filler" + i, -1000 - i), true));
        disk.Files["me"] = stored;

        var lines = NewFeed(disk, () => "me").Lines;
        Assert.Equal(FriendsFeed.Cap, lines.Count);
        Assert.Equal(new[] { "new", "old" }, lines.Take(2).Select(l => l.Key));
        Assert.False(lines[0].Read);
        Assert.DoesNotContain(lines, l => l.Key == "" || l.Key == "remote");
    }

    // ---- the file -------------------------------------------------------------------------

    [Fact]
    public void A_restart_brings_the_lines_back_as_they_were()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-feed-test-" + Guid.NewGuid().ToString("N"));
        var folder = FriendsFeedFile.Folder;
        try
        {
            FriendsFeedFile.Folder = () => dir;
            var first = new FriendsFeed(() => "u_me", FriendsFeedFile.Load, FriendsFeedFile.Save);
            first.Add(Ev("in:0000000000000001", 1));
            first.Add(Ev("in:0000000000000002", 2, FriendEventKind.InviteReceived, "kit", "Kit", InviteDestination.Goon, null));
            first.Add(Ev("rc:0000000000000003:seen", 3, FriendEventKind.ItemSeen, "noor", null, null, ReceiptKind.Watch));
            first.Add(Ev("req-in:dee:1790000000", 4, FriendEventKind.RequestReceived, "dee", "Dee \"the\" one", null, null));
            first.MarkRead(new[] { "in:0000000000000001" });

            var again = new FriendsFeed(() => "u_me", FriendsFeedFile.Load, FriendsFeedFile.Save);
            Assert.Equal(first.Lines, again.Lines);
            Assert.Equal(3, again.Unread);
            var seen = again.Lines.Single(l => l.Key.StartsWith("rc:"));
            Assert.Null(seen.Event.FriendName);
            Assert.Equal(ReceiptKind.Watch, seen.Event.Detail);
            Assert.Equal(DateTimeKind.Utc, seen.Event.AtUtc.Kind);

            // Another account on the same PC reads nothing of it.
            var other = new FriendsFeed(() => "u_other", FriendsFeedFile.Load, FriendsFeedFile.Save);
            Assert.Empty(other.Lines);
            Assert.NotEqual(FriendsFeedFile.PathFor("u_me"), FriendsFeedFile.PathFor("u_other"));
            Assert.DoesNotContain("u_me", Path.GetFileName(FriendsFeedFile.PathFor("u_me")));
            Assert.False(File.Exists(FriendsFeedFile.PathFor("u_me") + ".tmp"));
        }
        finally
        {
            FriendsFeedFile.Folder = folder;
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void A_file_for_another_account_or_a_broken_one_reads_empty()
    {
        var lines = new[] { new FeedEntry(Ev("a", 1), false) };
        var json = FriendsFeedFile.Serialize("u_a", lines);
        Assert.Single(FriendsFeedFile.Parse(json, "u_a"));
        Assert.Empty(FriendsFeedFile.Parse(json, "u_b"));
        Assert.Empty(FriendsFeedFile.Parse("{ not json", "u_a"));
        Assert.Empty(FriendsFeedFile.Parse("", "u_a"));
        Assert.Empty(FriendsFeedFile.Parse(null, "u_a"));
        Assert.Empty(FriendsFeedFile.Parse("[1,2,3]", "u_a"));
    }

    [Fact]
    public void A_kind_this_build_does_not_know_is_dropped_on_load()
    {
        var json = "{\"v\":1,\"account\":\"u_a\",\"lines\":["
            + "{\"kind\":\"PokeReceived\",\"friend\":\"sam\",\"at\":\"2026-09-29T10:00:00Z\",\"key\":\"k1\",\"read\":true},"
            + "{\"kind\":\"SomethingNew\",\"friend\":\"sam\",\"at\":\"2026-09-29T10:00:00Z\",\"key\":\"k2\"},"
            + "{\"kind\":\"3\",\"friend\":\"sam\",\"at\":\"2026-09-29T10:00:00Z\",\"key\":\"k3\"},"
            + "{\"kind\":\"PokeReceived\",\"friend\":\"sam\",\"at\":\"not a time\",\"key\":\"k4\"},"
            + "{\"kind\":\"PokeReceived\",\"at\":\"2026-09-29T10:00:00Z\",\"key\":\"k5\"}"
            + "]}";
        var lines = FriendsFeedFile.Parse(json, "u_a");
        var only = Assert.Single(lines);
        Assert.Equal("k1", only.Key);
        Assert.True(only.Read);
        Assert.Equal(T0, only.Event.AtUtc);
    }

    // ---- fed by the real service ----------------------------------------------------------

    [Fact]
    public void The_service_feeds_it_inbox_items_and_receipts()
    {
        var now = new DateTimeOffset(T0);
        var svc = new FriendsService(new FriendsApi(), () => "u_me", () => false, () => now, () => false, _ => { });
        var feed = NewFeed(new Disk(), () => "u_me");
        feed.Attach(svc);

        svc.Deliver(new[]
        {
            new InboxItem("0000000000000001", SendKind.Poke, "sam", "Sam", null, "tut", null, null, null, now, now.AddHours(24)),
            new InboxItem("0000000000000002", SendKind.Invite, "kit", "Kit", null, null, InviteDestination.BackRoom, null, null,
                now.AddSeconds(5), now.AddMinutes(5)),
        });
        // The same inbox item again (a second poll carrying it) lands once.
        svc.Deliver(new[] { new InboxItem("0000000000000001", SendKind.Poke, "sam", "Sam", null, "tut", null, null, null, now, now.AddHours(24)) });

        var seen = FriendsService.FeedLine(new SenderReceipt("0000000000000009", ReceiptKind.Poke, "noor", "Noor",
            ReceiptState.Seen, T0.AddMinutes(1), null));
        Assert.NotNull(seen);
        feed.Add(seen);
        feed.Add(FriendsService.FeedLine(new SenderReceipt("0000000000000009", ReceiptKind.Poke, "noor", "Noor",
            ReceiptState.Seen, T0.AddMinutes(1), null)));

        var lines = feed.Lines;
        Assert.Equal(3, lines.Count);
        Assert.Equal(FriendEventKind.ItemSeen, lines[0].Event.Kind);
        Assert.Equal(FriendEventKind.InviteReceived, lines[1].Event.Kind);
        Assert.Equal(InviteDestination.BackRoom, lines[1].Event.Destination);
        Assert.Equal(FriendEventKind.PokeReceived, lines[2].Event.Kind);
        Assert.Equal("tut", lines[2].Event.Detail);
        Assert.Equal(3, feed.Unread);

        feed.Detach();
        svc.Deliver(new[] { new InboxItem("0000000000000003", SendKind.Poke, "sam", "Sam", null, "hi", null, null, null, now, now.AddHours(24)) });
        Assert.Equal(3, feed.Lines.Count);
    }

    // ---- rules ----------------------------------------------------------------------------

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(2, 0, 2)]
    [InlineData(10, 0, 3)]
    [InlineData(10, 5, 5)]
    [InlineData(20, 12, 8)]
    [InlineData(4, 7, 4)]
    public void Folded_it_shows_a_few_and_every_new_line_up_to_eight(int total, int fresh, int shown)
        => Assert.Equal(shown, FriendsFeedRules.FoldCount(total, fresh));

    [Theory]
    [InlineData(0, "")]
    [InlineData(-3, "")]
    [InlineData(1, "1")]
    [InlineData(9, "9")]
    [InlineData(10, "9+")]
    [InlineData(250, "9+")]
    public void The_badge_says_the_number_then_nine_plus(int unread, string text)
        => Assert.Equal(text, FriendsFeedRules.BadgeText(unread));

    [Fact]
    public void Ago_is_short()
    {
        Assert.Equal(("friends_notice_now", (int?)null), FriendsFeedRules.Ago(T0, T0.AddSeconds(59)));
        Assert.Equal(("friends_notice_now", (int?)null), FriendsFeedRules.Ago(T0.AddMinutes(1), T0));   // a server clock ahead
        Assert.Equal(("friends_notice_minutes", (int?)12), FriendsFeedRules.Ago(T0, T0.AddMinutes(12.5)));
        Assert.Equal(("friends_notice_hours", (int?)3), FriendsFeedRules.Ago(T0, T0.AddHours(3.9)));
        Assert.Equal(("friends_feed_days", (int?)2), FriendsFeedRules.Ago(T0, T0.AddHours(49)));
    }

    [Fact]
    public void The_tray_adds_a_line_only_while_something_is_new()
    {
        Assert.Equal("CCP", FriendsFeedRules.TrayText("CCP", 0, "0 new"));
        Assert.Equal("CCP\n3 new", FriendsFeedRules.TrayText("CCP", 3, "3 new"));
        var longText = FriendsFeedRules.TrayText(new string('x', 120), 4, "4 new from friends");
        Assert.Equal(FriendsFeedRules.TrayTextMax, longText.Length);
        Assert.StartsWith(new string('x', 120), longText);
    }

    [Fact]
    public void Remote_invites_and_keyless_lines_are_never_kept()
    {
        Assert.False(FriendsFeedRules.Keep(null));
        Assert.False(FriendsFeedRules.Keep(Ev("", 1)));
        Assert.False(FriendsFeedRules.Keep(Ev("k", 1, friend: "")));
        Assert.False(FriendsFeedRules.Keep(Ev("k", 1, (FriendEventKind)99)));
        Assert.False(FriendsFeedRules.Keep(Ev("k", 1, FriendEventKind.InviteReceived, dest: InviteDestination.Remote)));
        Assert.True(FriendsFeedRules.Keep(Ev("k", 1, FriendEventKind.InviteReceived, dest: InviteDestination.Chess)));
    }

    [Fact]
    public void Every_kind_reads_as_its_own_line()
    {
        FriendsFeedRules.Line D(FriendEventKind k, string? dest = null, string? detail = null)
            => FriendsFeedRules.Describe(Ev("k", 1, k, dest: dest, detail: detail));

        Assert.Equal(new FriendsFeedRules.Line("friends_feed_poke", "friends_poke_tut", true), D(FriendEventKind.PokeReceived, detail: "tut"));
        Assert.Equal("friends_feed_poke_plain", D(FriendEventKind.PokeReceived, detail: "made-up").Key);
        Assert.Equal(new FriendsFeedRules.Line("friends_feed_invite", "friends_invite_goon", true), D(FriendEventKind.InviteReceived, InviteDestination.Goon));
        Assert.Equal("friends_feed_invite_plain", D(FriendEventKind.InviteReceived, "somewhere").Key);
        Assert.Equal(new FriendsFeedRules.Line("friends_feed_watch", "Pink", false), D(FriendEventKind.WatchReceived, detail: " Pink "));
        Assert.Equal("friends_feed_watch_plain", D(FriendEventKind.WatchReceived, detail: null).Key);
        Assert.Equal("friends_feed_request", D(FriendEventKind.RequestReceived).Key);
        Assert.Equal("friends_feed_accepted", D(FriendEventKind.RequestAccepted).Key);
        Assert.Equal("friends_feed_seen_poke", D(FriendEventKind.ItemSeen, detail: ReceiptKind.Poke).Key);
        Assert.Equal("friends_feed_seen_invite", D(FriendEventKind.ItemSeen, detail: ReceiptKind.Invite).Key);
        Assert.Equal("friends_feed_seen_watch", D(FriendEventKind.ItemSeen, detail: ReceiptKind.Watch).Key);
        Assert.Equal("friends_feed_seen_request", D(FriendEventKind.ItemSeen, detail: ReceiptKind.Request).Key);
        Assert.Equal("friends_feed_seen", D(FriendEventKind.ItemSeen, detail: "leash_tug").Key);
        Assert.Equal("friends_feed_joined", D(FriendEventKind.InviteAnswered, detail: ReceiptState.Joined).Key);
        Assert.Equal("friends_feed_declined", D(FriendEventKind.InviteAnswered, detail: ReceiptState.Declined).Key);
        Assert.Equal("friends_feed_no_answer", D(FriendEventKind.InviteUnanswered).Key);

        foreach (var k in Enum.GetValues<FriendEventKind>())
            Assert.Contains(FriendsFeedRules.Describe(Ev("k", 1, k)).Key, FriendsFeedRules.AllKeys);
    }

    [Fact]
    public void A_line_without_a_name_borrows_the_friend_list()
    {
        var friends = new[] { new Friend("noor", "Noor", null, 0, false, FriendPresence.None, false) };
        Assert.Equal("Sam", FriendsFeedRules.NameFor(Ev("k", 1, name: " Sam "), friends));
        Assert.Equal("Noor", FriendsFeedRules.NameFor(Ev("k", 1, friend: "noor", name: null), friends));
        Assert.Null(FriendsFeedRules.NameFor(Ev("k", 1, friend: "ghost", name: null), friends));
        Assert.Null(FriendsFeedRules.NameFor(Ev("k", 1, friend: "ghost", name: ""), null));
    }

    [Fact]
    public void Every_feed_key_is_in_all_nine_languages()
    {
        var keys = FriendsFeedRules.AllKeys.Concat(new[] { "friends_notice_now", "friends_notice_minutes", "friends_notice_hours" })
            .Concat(PokeSet.All.Select(p => "friends_poke_" + p))
            .Concat(InviteDestination.All.Select(d => "friends_invite_" + d))
            .ToList();
        var dir = Path.Combine(RepoRoot(), "ConditioningControlPanel", "Localization", "Languages");
        foreach (var lang in new[] { "en", "de", "es", "fr", "ja", "ko", "pt-BR", "ru", "zh-CN" })
        {
            var json = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(dir, lang + ".json")));
            foreach (var k in keys)
                Assert.True(json[k] != null, $"{lang}.json is missing {k}");
            foreach (var k in FriendsFeedRules.AllKeys)
            {
                var v = (string)json[k]!;
                Assert.DoesNotContain(char.ConvertFromUtf32(0x2014), v);
                Assert.DoesNotContain("!", v);
            }
        }
    }

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "ConditioningControlPanel", "Localization")))
            d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
