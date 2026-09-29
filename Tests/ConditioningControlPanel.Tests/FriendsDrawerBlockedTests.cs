using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Remove and Block ask first; the Blocked list remembers this PC's blocks per account and
/// offers Unblock, forgetting one only when the server agreed. Shares the drawer suite's fake.
/// </summary>
public partial class FriendsDrawerTests
{
    /// <summary>The in-memory block list the drawer writes to, per test.</summary>
    private static readonly List<BlockedEntry> Blocked = new();

    static partial void ResetDrawerExtras()
    {
        Blocked.Clear();
        FriendsBlockList.Shared = new FriendsBlockList(() => Blocked.ToList(), list => { Blocked.Clear(); Blocked.AddRange(list); });
        FriendsBlockList.Account = () => "me";
    }

    [Fact]
    public void Every_blocked_list_key_is_in_all_nine_languages()
    {
        var keys = new[]
        {
            "friends_confirm_remove", "friends_confirm_remove_sub", "friends_confirm_block", "friends_confirm_block_sub",
            "friends_confirm_yes_remove", "friends_confirm_keep", "friends_unblocked_done", "friends_section_blocked",
            "friends_blocked_title", "friends_blocked_empty", "friends_unblock",
        };
        var dir = SourceRoots.LanguagesDirectory;
        foreach (var lang in new[] { "en", "de", "es", "fr", "ja", "ko", "pt-BR", "ru", "zh-CN" })
        {
            var json = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(dir, lang + ".json")));
            foreach (var k in keys)
                Assert.True(json[k] != null, $"{lang}.json is missing {k}");
        }
    }

    [Fact]
    public void Remove_asks_first_and_only_the_second_press_removes()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var svc = new FakeFriends(Sample());
            var d = NewDrawer(svc);
            var sam = Sample().Friends.First(f => f.Id == "sam");
            d.RunMenuAsync(sam, "remove").GetAwaiter().GetResult();
            Assert.Empty(svc.Acts);
            Assert.Equal(("sam", "remove"), d.PendingConfirm);
            Assert.Equal("sam", d.OpenFriendId);
            Assert.NotNull(Find(d.RowFor("sam")!, "friends-confirm:remove"));

            var r = d.ConfirmAsync("sam", "Sam", "remove").GetAwaiter().GetResult();
            Assert.Equal(ActResult.Done, r);
            Assert.Equal(new[] { "remove:sam" }, svc.Acts);
            Assert.Null(d.PendingConfirm);
            Assert.Contains(Loc.GetF("friends_removed_done", "Sam"), Outside);
            Assert.Empty(Blocked);   // a remove is not a block
        });
    }

    [Fact]
    public void Keep_backs_out_of_the_question()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var svc = new FakeFriends(Sample());
            var d = NewDrawer(svc);
            d.RunMenuAsync(Sample().Friends.First(f => f.Id == "kit"), "block").GetAwaiter().GetResult();
            var keep = (System.Windows.Controls.Button)Find(d.RowFor("kit")!, "friends-confirm-keep")!;
            keep.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.Null(d.PendingConfirm);
            Assert.Empty(svc.Acts);
        });
    }

    [Fact]
    public void A_block_is_remembered_and_unblock_forgets_it_only_when_the_server_agrees()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var svc = new FakeFriends(Sample());
            var d = NewDrawer(svc);
            d.ConfirmAsync("kit", "Kit", "block").GetAwaiter().GetResult();
            Assert.Equal(new[] { "block:kit" }, svc.Acts);
            Assert.Equal("kit", Blocked.Single().Id);

            d.ToggleBlocked();
            Assert.True(d.ShowingBlocked);
            Assert.Contains("friends_section_blocked", d.SectionKeys);
            Assert.NotNull(d.RowFor("blocked:kit"));

            svc.NextAct = ActResult.TryLater;
            d.UnblockAsync(Blocked.Single()).GetAwaiter().GetResult();
            Assert.Single(Blocked);
            Assert.Equal(Loc.Get("friends_result_try_later"), d.ResultTextFor("blocked:kit"));

            svc.NextAct = ActResult.Done;
            d.UnblockAsync(Blocked.Single()).GetAwaiter().GetResult();
            Assert.Empty(Blocked);
            Assert.Null(d.RowFor("blocked:kit"));
            Assert.NotNull(Find(d, "friends-blocked-empty"));
        });
    }

    [Fact]
    public void A_failed_block_remembers_nothing()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var svc = new FakeFriends(Sample()) { NextAct = ActResult.TryLater };
            var d = NewDrawer(svc);
            d.ConfirmAsync("kit", "Kit", "block").GetAwaiter().GetResult();
            Assert.Empty(Blocked);
            Assert.Equal(Loc.Get("friends_result_try_later"), d.ResultTextFor("kit"));
        });
    }

    [Fact]
    public void A_requester_can_be_blocked_from_the_row()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var svc = new FakeFriends(Sample());
            var d = NewDrawer(svc);
            d.BlockRequesterAsync(Sample().Incoming.Single()).GetAwaiter().GetResult();
            Assert.Equal(new[] { "block:dee" }, svc.Acts);
            Assert.Equal("Dee", Blocked.Single().Name);
        });
    }

    [Fact]
    public void The_block_list_is_kept_per_account_newest_first_without_repeats()
    {
        var store = new List<BlockedEntry>();
        var list = new FriendsBlockList(() => store.ToList(), l => { store.Clear(); store.AddRange(l); });
        var t = new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);
        list.Add("me", "a", "Ann", t);
        list.Add("me", "b", "Bo", t.AddMinutes(1));
        list.Add("other", "c", "Cy", t);
        list.Add("me", "a", "Ann B", t.AddMinutes(2));

        Assert.Equal(new[] { "a", "b" }, list.For("me").Select(e => e.Id));
        Assert.Equal("Ann B", list.For("me")[0].Name);
        Assert.Equal(new[] { "c" }, list.For("other").Select(e => e.Id));
        Assert.Empty(list.For(null));

        list.Remove("me", "a");
        Assert.Equal(new[] { "b" }, list.For("me").Select(e => e.Id));
        Assert.Single(list.For("other"));
    }

    [Fact]
    public void The_block_list_never_grows_past_its_cap_and_survives_a_bad_load()
    {
        var store = new List<BlockedEntry>();
        var list = new FriendsBlockList(() => store.ToList(), l => { store.Clear(); store.AddRange(l); });
        var t = new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);
        for (int i = 0; i < FriendsBlockList.Cap + 5; i++) list.Add("me", "u" + i, "n", t.AddSeconds(i));
        Assert.Equal(FriendsBlockList.Cap, store.Count);
        Assert.DoesNotContain(store, e => e.Id == "u0");

        var broken = new FriendsBlockList(() => throw new InvalidOperationException("disk"), _ => { });
        Assert.Empty(broken.For("me"));
    }
}
