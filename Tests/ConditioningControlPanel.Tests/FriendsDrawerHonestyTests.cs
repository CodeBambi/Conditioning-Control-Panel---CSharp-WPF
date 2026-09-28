using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The drawer's honesty pass (2026-09-28): list changes are worded by what came back, Remove and
/// Block ask first, the Blocked list offers Unblock, request rows say Accept or Decline and how
/// long ago. Shares the drawer suite's fake and helpers.
/// </summary>
public partial class FriendsDrawerTests
{
    /// <summary>What the drawer said outside itself (its folded-drawer channel), per test.</summary>
    private static readonly List<string> Outside = new();

    /// <summary>Resets whatever the Blocked list keeps between tests (FriendsDrawerBlockedTests).</summary>
    static partial void ResetDrawerExtras();

    [Fact]
    public void Remove_is_worded_outside_once_the_row_is_gone()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var svc = new FakeFriends(Sample());
            var d = NewDrawer(svc);
            var r = d.RemoveOrBlockAsync("sam", "Sam", "remove").GetAwaiter().GetResult();
            Assert.Equal(ActResult.Done, r);
            Assert.Equal(new[] { "remove:sam" }, svc.Acts);
            Assert.Contains(Loc.GetF("friends_removed_done", "Sam"), Outside);

            svc.NextAct = ActResult.TryLater;
            d.RemoveOrBlockAsync("kit", "Kit", "block").GetAwaiter().GetResult();
            Assert.Equal(Loc.Get("friends_result_try_later"), d.ResultTextFor("kit"));
        });
    }

    [Fact]
    public void A_failed_accept_is_worded_in_the_request_row_and_never_cheered()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var svc = new FakeFriends(Sample()) { NextAct = ActResult.NotFound };
            var d = NewDrawer(svc);
            var dee = Sample().Incoming.Single();
            var r = d.AnswerRequestAsync(dee, "accept").GetAwaiter().GetResult();
            Assert.Equal(ActResult.NotFound, r);
            Assert.Equal(new[] { "accept:dee" }, svc.Acts);
            Assert.Equal(Loc.Get("friends_act_not_found"), d.ResultTextFor("in:dee"));
            Assert.NotNull(Find(d.RowFor("in:dee")!, "friends-result"));
        });
    }

    [Fact]
    public void Request_rows_say_accept_and_decline_and_how_long_ago()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var snap = Sample() with { Incoming = new[] { new FriendRequest("dee", "Dee", null, null, DateTimeOffset.UtcNow.AddMinutes(-15)) } };
            var d = NewDrawer(new FakeFriends(snap));
            var row = d.RowFor("in:dee")!;
            Assert.NotNull(Find(row, "friends-accept"));
            Assert.NotNull(Find(row, "friends-decline"));
            Assert.NotNull(Find(row, "friends-request-more"));
            var sub = (System.Windows.Controls.TextBlock)Find(row, "friends-request-sub")!;
            Assert.Contains(Loc.GetF("friends_seen_minutes", 15), sub.Text);
        });
    }

    [Fact]
    public void A_report_says_thanks_only_when_it_went_through()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var svc = new FakeFriends(Sample()) { NextAct = ActResult.TryLater };
            var d = NewDrawer(svc);
            d.ReportAsync("sam", "sam", "spam").GetAwaiter().GetResult();
            Assert.Equal(Loc.Get("friends_result_try_later"), d.ResultTextFor("sam"));

            svc.NextAct = ActResult.Done;
            d.ReportAsync("sam", "sam", "spam").GetAwaiter().GetResult();
            Assert.Equal(Loc.Get("friends_report_done"), d.ResultTextFor("sam"));
            Assert.Equal(new[] { "report_spam:sam", "report_spam:sam" }, svc.Acts);
        });
    }

    [Fact]
    public void Squelch_is_worded_by_what_came_back()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var svc = new FakeFriends(Sample()) { NextAct = ActResult.Refused };
            var d = NewDrawer(svc);
            d.RunMenuAsync(Sample().Friends.First(f => f.Id == "sam"), "squelch").GetAwaiter().GetResult();
            Assert.Equal(Loc.Get("friends_result_refused"), d.ResultTextFor("sam"));
            svc.NextAct = ActResult.Done;
            d.RunMenuAsync(Sample().Friends.First(f => f.Id == "sam"), "squelch").GetAwaiter().GetResult();
            Assert.Equal(Loc.Get("friends_squelch_done"), d.ResultTextFor("sam"));
        });
    }

    [Fact]
    public void A_send_from_a_folded_drawer_is_said_outside()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var svc = new FakeFriends(Sample()) { NextSend = SendResult.Offline };
            var d = NewDrawer(svc);
            d.IsOpen = true;
            d.PokeAsync("sam", "hi").GetAwaiter().GetResult();
            Assert.Empty(Outside);   // open: the row carries it

            d.IsOpen = false;
            d.PokeAsync("kit", "hi").GetAwaiter().GetResult();
            Assert.Equal(new[] { Loc.Get("friends_result_offline") }, Outside);
        });
    }

    [Fact]
    public void Remote_is_never_offered_or_sent()
    {
        Assert.DoesNotContain(FriendsDrawer.InviteTiles, t => t.Id == InviteDestination.Remote);
        Assert.Equal(InviteDestination.Sendable.Count, FriendsDrawer.InviteTiles.Length);
        WpfRenderHarness.OnStaThread(() =>
        {
            var svc = new FakeFriends(Sample());
            var d = NewDrawer(svc);
            Assert.Equal(SendResult.Refused, d.InviteAsync("sam", InviteDestination.Remote, "ABCD").GetAwaiter().GetResult());
            Assert.Empty(svc.Invites);
        });
    }

    // ---- pure ---------------------------------------------------------------------------

    [Fact]
    public void Request_age_reads_like_last_seen_and_skips_an_unknown_time()
    {
        var now = new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(("friends_seen_hours", (int?)3), FriendsDrawerRules.RequestAgo(now.AddHours(-3), now));
        Assert.Equal("friends_seen_now", FriendsDrawerRules.RequestAgo(now.AddMinutes(5), now).Key);   // a fast server clock
        Assert.Null(FriendsDrawerRules.RequestAgo(DateTimeOffset.MinValue, now).Key);
    }

}
