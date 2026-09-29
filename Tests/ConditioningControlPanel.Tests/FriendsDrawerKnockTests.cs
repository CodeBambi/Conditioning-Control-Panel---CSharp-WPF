using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Wave 1 invites in the drawer (lane F1): the trail under a friend's name walks with the
/// receipts, an incoming request row reports seen only while the drawer is open, and the
/// server's blocked list is the truth when it sends one. Shares the drawer suite's fake.
/// </summary>
public partial class FriendsDrawerTests
{
    private const string TrailId = "00112233445566ff";

    [Fact]
    public void Every_knock_and_trail_key_is_in_all_nine_languages()
    {
        var keys = new List<string>
        {
            "friends_land_not_now", "friends_land_answer_later", "friends_trail_tip", "friends_trail_tip_invite",
            "friends_blocked_none",
        };
        foreach (var kind in new[] { SendKind.Poke, SendKind.Invite, SendKind.Watch })
            foreach (var state in new[] { ReceiptState.Sent, ReceiptState.Arrived, ReceiptState.Seen, ReceiptState.Joined,
                         ReceiptState.Declined, ReceiptState.Expired })
            {
                var (k, s) = FriendsDrawerRules.TrailKeys(new SentTrail("u", kind, null, null, state, Now));
                keys.Add(k);
                keys.Add(s);
            }
        var dir = System.IO.Path.Combine(RepoRoot(), "ConditioningControlPanel", "Localization", "Languages");
        foreach (var lang in new[] { "en", "de", "es", "fr", "ja", "ko", "pt-BR", "ru", "zh-CN" })
        {
            var json = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(dir, lang + ".json")));
            foreach (var k in keys.Distinct())
                Assert.True(json[k] != null, $"{lang}.json is missing {k}");
        }
    }

    [Fact]
    public void The_trail_under_a_name_walks_with_the_receipts()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var svc = new FakeFriends(Sample());
            var d = NewDrawer(svc);
            d.IsOpen = true;
            d.Listen();
            Assert.Null(d.TrailTextFor("sam"));

            svc.MoveTrail(new SentTrail("sam", SendKind.Invite, TrailId, "backroom", ReceiptState.Sent, Now));
            Assert.NotNull(Find(d.RowFor("sam")!, "friends-trail:sent"));
            Assert.StartsWith(Loc.Get("friends_trail_invite"), d.TrailTextFor("sam"));

            svc.MoveTrail(new SentTrail("sam", SendKind.Invite, TrailId, "backroom", ReceiptState.Seen, Now));
            Assert.NotNull(Find(d.RowFor("sam")!, "friends-trail:seen"));
            Assert.EndsWith(Loc.Get("friends_trail_seen"), d.TrailTextFor("sam"));

            svc.MoveTrail(new SentTrail("sam", SendKind.Invite, TrailId, "backroom", ReceiptState.Expired, Now));
            Assert.EndsWith(Loc.Get("friends_trail_expired"), d.TrailTextFor("sam"));
            // Nobody else wears it.
            Assert.Null(d.TrailTextFor("kit"));
        });
    }

    [Fact]
    public void A_folded_drawer_does_not_repaint_for_a_trail()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var svc = new FakeFriends(Sample());
            var d = NewDrawer(svc);
            d.IsOpen = false;
            d.Listen();
            svc.MoveTrail(new SentTrail("sam", SendKind.Poke, TrailId, "hi", ReceiptState.Seen, Now));
            Assert.Null(d.TrailTextFor("sam"));   // drawn on the next open, not while folded
            d.Render();
            Assert.NotNull(d.TrailTextFor("sam"));
        });
    }

    [Fact]
    public void An_incoming_request_is_seen_only_while_the_drawer_is_open()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            // Its own requester and time, so no other test's memory of "seen" gets in the way.
            var who = "u_knock_" + Guid.NewGuid().ToString("N")[..8];
            var snap = Sample() with { Incoming = new[] { new FriendRequest(who, "Vee", null, null, Now) } };
            var svc = new FakeFriends(snap);
            var d = NewDrawer(svc);
            Assert.DoesNotContain(svc.Reports, r => r.RequestFrom == who);

            d.IsOpen = true;
            d.Render();
            d.Render();
            var seen = svc.Reports.Where(r => r.RequestFrom == who).ToList();
            Assert.Single(seen);   // once, however often it repaints
            Assert.Equal(ReceiptState.Seen, seen[0].State);
            // An outgoing request is mine: nothing to report.
            Assert.DoesNotContain(svc.Reports, r => r.RequestFrom == "ash");
        });
    }

    [Fact]
    public void The_servers_blocked_list_is_the_truth_when_it_sends_one()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var svc = new FakeFriends(Sample() with { Blocked = new[] { new BlockedFriend("u_x", "Xan") } });
            var d = NewDrawer(svc);
            Blocked.Add(new BlockedEntry("me", "u_local", "Local", Now));   // this PC's old list is not read
            d.ToggleBlocked();
            Assert.NotNull(d.RowFor("blocked:u_x"));
            Assert.Null(d.RowFor("blocked:u_local"));

            // A block while the server lists them is not written to this PC.
            d.ConfirmAsync("kit", "Kit", "block").GetAwaiter().GetResult();
            Assert.Equal(new[] { "u_local" }, Blocked.Select(b => b.Id));

            svc.Push(Sample() with { Blocked = Array.Empty<BlockedFriend>() });
            d.Render();
            Assert.Null(d.RowFor("blocked:u_x"));
            var empty = Find(d, "friends-blocked-empty") as System.Windows.Controls.TextBlock;
            Assert.Equal(Loc.Get("friends_blocked_none"), empty?.Text);
        });
    }

    [Fact]
    public void An_old_server_keeps_the_per_pc_list()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var svc = new FakeFriends(Sample());   // Blocked null: the server did not say
            var d = NewDrawer(svc);
            d.ConfirmAsync("kit", "Kit", "block").GetAwaiter().GetResult();
            Assert.Equal("kit", Blocked.Single().Id);
            d.ToggleBlocked();
            Assert.NotNull(d.RowFor("blocked:kit"));
        });
    }
}
