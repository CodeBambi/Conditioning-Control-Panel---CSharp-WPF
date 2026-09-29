using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// "What happened" in the drawer and the unread badge on the chip, against an in-memory feed:
/// newest first, folded to a few lines plus every new one, read once shown in an open drawer (the
/// dot stays until the drawer folds), "more" and "less", and no section at all while nothing
/// happened. Shares the drawer suite's fake and helpers.
/// </summary>
public partial class FriendsDrawerTests
{
    private static readonly DateTime FeedT0 = DateTime.UtcNow.AddHours(-1);

    private static FriendsFeed FeedWith(int count, bool read = false)
    {
        var feed = new FriendsFeed(() => "me");
        for (var i = 0; i < count; i++)
            feed.Add(new FriendEvent(FriendEventKind.PokeReceived, "sam", "Sam", FeedT0.AddMinutes(i), "k" + i, null, "hi"));
        if (read) feed.MarkAllRead();
        return feed;
    }

    /// <summary>Runs <paramref name="body"/> with the drawer drawing <paramref name="feed"/>.</summary>
    private static void WithFeed(FriendsFeed? feed, Action body)
    {
        var source = FriendsDrawer.FeedSource;
        try
        {
            FriendsDrawer.FeedSource = () => feed;
            body();
        }
        finally { FriendsDrawer.FeedSource = source; }
    }

    [Fact]
    public void Feed_is_drawn_newest_first_and_read_only_once_the_drawer_is_open()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var feed = FeedWith(5);
            WithFeed(feed, () =>
            {
                var d = NewDrawer(new FakeFriends(Sample()));
                // Built but folded: drawn, nothing read.
                Assert.Equal(new[] { "k4", "k3", "k2", "k1", "k0" }, d.FeedLineKeys);
                Assert.Equal(5, feed.Unread);
                // The feed has no place in the friend sections.
                Assert.Equal(new[] { "friends_section_online", "friends_section_requests", "friends_section_offline" }, d.SectionKeys);

                d.IsOpen = true;
                d.Render();
                Assert.Equal(0, feed.Unread);
                Assert.Equal(5, Tags(d.FeedSection).Count(t => t == "friends-feed-new"));
                Assert.NotNull(Find(d.FeedSection, "friends-feed-count"));

                // Still open: the dots stay on a repaint, the lines stay put.
                d.Render();
                Assert.Equal(5, Tags(d.FeedSection).Count(t => t == "friends-feed-new"));
                Assert.Equal(5, d.FeedLineKeys.Count);

                // Folded and opened again: read, no dots, back to three lines and "2 more".
                d.FeedFolded();
                d.Render();
                Assert.Equal(new[] { "k4", "k3", "k2" }, d.FeedLineKeys);
                Assert.DoesNotContain("friends-feed-new", Tags(d.FeedSection));
                Assert.Null(Find(d.FeedSection, "friends-feed-count"));
                var more = (Button)Find(d.FeedSection, "friends-feed-more")!;
                Assert.Equal(Loc.GetF("friends_feed_more", 2), more.Content);
                Layout(d);
            });
        });
    }

    [Fact]
    public void Feed_shows_every_new_line_up_to_eight_and_more_reads_the_rest()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var feed = FeedWith(12);
            WithFeed(feed, () =>
            {
                var d = NewDrawer(new FakeFriends(Sample()));
                d.IsOpen = true;
                d.Render();
                Assert.Equal(FriendsFeedRules.FoldMax, d.FeedLineKeys.Count);
                Assert.Equal(4, feed.Unread);
                var more = (Button)Find(d.FeedSection, "friends-feed-more")!;
                Assert.Equal(Loc.GetF("friends_feed_more_new", 4, 4), more.Content);

                d.ShowMoreFeed();
                Assert.Equal(12, d.FeedLineKeys.Count);
                Assert.Equal(0, feed.Unread);
                Assert.Null(Find(d.FeedSection, "friends-feed-more"));
                Assert.NotNull(Find(d.FeedSection, "friends-feed-less"));

                d.ShowLessFeed();
                // Twelve lines are new this open, so the fold still holds eight.
                Assert.Equal(FriendsFeedRules.FoldMax, d.FeedLineKeys.Count);
            });
        });
    }

    [Fact]
    public void A_line_landing_in_the_open_drawer_shows_at_once_and_is_read()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var feed = FeedWith(2, read: true);
            WithFeed(feed, () =>
            {
                var d = NewDrawer(new FakeFriends(Sample()));
                d.IsOpen = true;
                d.Render();
                Assert.DoesNotContain("friends-feed-new", Tags(d.FeedSection));

                feed.Add(new FriendEvent(FriendEventKind.InviteUnanswered, "kit", "Kit", DateTime.UtcNow, "rc:x:expired"));
                Assert.Equal("rc:x:expired", d.FeedLineKeys.First());
                Assert.Equal(0, feed.Unread);
                Assert.Single(Tags(d.FeedSection), t => t == "friends-feed-new");

                // A folded drawer does not follow the feed and reads nothing.
                d.FeedFolded();
                d.IsOpen = false;
                feed.Add(new FriendEvent(FriendEventKind.RequestReceived, "dee", "Dee", DateTime.UtcNow, "req-in:dee:1"));
                Assert.Equal(1, feed.Unread);
                Assert.Equal("rc:x:expired", d.FeedLineKeys.First());
            });
        });
    }

    [Fact]
    public void A_line_about_a_friend_opens_their_card()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var feed = new FriendsFeed(() => "me");
            feed.Add(new FriendEvent(FriendEventKind.PokeReceived, "sam", "Sam", FeedT0, "p1", null, "hi"));
            feed.Add(new FriendEvent(FriendEventKind.RequestReceived, "dee", "Dee", FeedT0.AddMinutes(1), "r1"));
            WithFeed(feed, () =>
            {
                var d = NewDrawer(new FakeFriends(Sample()));
                var samLine = (Border)Find(d.FeedSection, "friends-feed-line:p1")!;
                var deeLine = (Border)Find(d.FeedSection, "friends-feed-line:r1")!;
                Assert.Equal(System.Windows.Input.Cursors.Hand, samLine.Cursor);
                Assert.Null(deeLine.Cursor);   // not on the list (a request): nothing to open

                d.OpenFromFeed("sam");
                Assert.Equal("sam", d.OpenFriendId);
                Assert.NotNull(Find(d.RowFor("sam")!, "friends-card"));
            });
        });
    }

    [Fact]
    public void An_empty_or_missing_feed_draws_no_section()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            WithFeed(new FriendsFeed(() => "me"), () =>
            {
                var d = NewDrawer(new FakeFriends(Sample()));
                Assert.Equal(Visibility.Collapsed, d.FeedSection.Visibility);
                Assert.Empty(d.FeedLineKeys);
            });
            WithFeed(null, () =>
            {
                var d = NewDrawer(new FakeFriends(Sample()));
                Assert.Equal(Visibility.Collapsed, d.FeedSection.Visibility);
                Assert.Equal(new[] { "kit", "sam", "in:dee", "out:ash", "robin", "noor" }, d.RowIds);
            });
        });
    }

    [Fact]
    public void A_feed_line_reads_as_a_sentence_with_the_name()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var friends = Sample().Friends;
            string Say(FriendEvent e) => FriendsDrawer.FillLine(new TextBlock(), e, friends, true);

            var poke = Say(new FriendEvent(FriendEventKind.PokeReceived, "sam", "Sam", FeedT0, "a", null, "tut"));
            Assert.Equal(Loc.Get("friends_feed_poke").Replace("{0}", "Sam").Replace("{1}", Loc.Get("friends_poke_tut")), poke);

            // No name on the line: the friend list's name, else "someone".
            var seen = Say(new FriendEvent(FriendEventKind.ItemSeen, "noor", null, FeedT0, "b", null, ReceiptKind.Invite));
            Assert.Equal(Loc.Get("friends_feed_seen_invite").Replace("{0}", "Noor"), seen);
            var ghost = Say(new FriendEvent(FriendEventKind.InviteUnanswered, "ghost", null, FeedT0, "c"));
            Assert.Equal(Loc.Get("friends_feed_no_answer").Replace("{0}", Loc.Get("friends_feed_someone")), ghost);

            // A title holding a placeholder stays a title.
            var watch = Say(new FriendEvent(FriendEventKind.WatchReceived, "sam", "Sam", FeedT0, "d", null, "{0} tricks"));
            Assert.Equal(Loc.Get("friends_feed_watch").Replace("{0}", "Sam").Replace("{1}", "{0} tricks"), watch);
        });
    }

    [Fact]
    public void Chip_badge_counts_unread_feed_lines()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            PresenceAsk.Asked = () => true;
            var feed = FeedWith(3);
            var svc = new FakeFriends(Sample());
            var chip = new FriendsRailChip(svc, feed);
            chip.Rebind();
            Assert.Equal(3, chip.UnreadBadge);
            Assert.Equal(2, chip.PillCount);
            Assert.Contains(Loc.GetF("friends_feed_new", 3), chip.ToolTip as string);

            feed.Add(new FriendEvent(FriendEventKind.RequestReceived, "dee", "Dee", DateTime.UtcNow, "req-in:dee:1"));
            Assert.Equal(4, chip.UnreadBadge);

            for (var i = 0; i < 8; i++)
                feed.Add(new FriendEvent(FriendEventKind.PokeReceived, "kit", "Kit", DateTime.UtcNow, "more" + i, null, "hi"));
            Assert.Equal(10, chip.UnreadBadge);   // "9+"

            feed.MarkAllRead();
            Assert.Equal(0, chip.UnreadBadge);
            Assert.Equal(Loc.Get("friends_chip_tooltip"), chip.ToolTip as string);

            // Signed out: no badge, whatever the feed holds.
            feed.Add(new FriendEvent(FriendEventKind.RequestReceived, "ash", "Ash", DateTime.UtcNow, "req-in:ash:1"));
            Assert.Equal(1, chip.UnreadBadge);
            svc.IsAvailable = false;
            svc.Push(Sample());
            Assert.Equal(0, chip.UnreadBadge);
        });
    }
}
