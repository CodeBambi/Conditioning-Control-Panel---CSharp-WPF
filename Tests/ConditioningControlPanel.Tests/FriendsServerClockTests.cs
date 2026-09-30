using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.FriendsWindows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Friends;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// An invite's <c>at</c> / <c>expires_at</c> and every receipt's time are the SERVER's clock. The
/// friends side routes, counts down and ages them with <see cref="ServerClock.UtcNow"/> (the proxy's
/// clock, learned from its Date header), never the PC clock: a PC running fast must not drop a
/// fresh invite, shorten its knock countdown, or hide the sender's trail.
/// </summary>
[Collection("ServerClockStatics")]
public class FriendsServerClockTests
{
    private const string Id1 = "0123456789abcdef";

    /// <summary>Teaches <see cref="ServerClock"/> that this PC runs <paramref name="fast"/> ahead of the server.</summary>
    private static DateTimeOffset PcFast(TimeSpan fast)
    {
        ServerClock.ResetForTests();
        var local = DateTimeOffset.UtcNow;
        ServerClock.Observe(local - fast, local);
        return local - fast;   // the server's now
    }

    [Fact]
    public async Task The_sent_trail_survives_its_first_receipt_on_a_fast_pc_clock()
    {
        try
        {
            var serverNow = PcFast(TimeSpan.FromMinutes(31));
            var api = new TrailApi { NextItemId = Id1 };
            using var svc = new FriendsService(api, () => "u_me", () => true, now: null, () => false, _ => { });
            await svc.TickAsync();

            Assert.Equal(SendResult.Sent, await svc.InviteAsync("u_ann", InviteDestination.BackRoom, null));
            Assert.NotNull(svc.LastSentTo("u_ann"));   // "sent" shows

            api.Replies.Enqueue(new FriendsPollReply(Array.Empty<string>(), Array.Empty<InboxItem>(), null, new[]
            {
                new SenderReceipt(Id1, "invite", "u_ann", "Ann", ReceiptState.Arrived, serverNow.UtcDateTime.AddSeconds(1), null),
            }));
            await svc.TickAsync();

            var trail = svc.LastSentTo("u_ann");
            Assert.True(trail != null, "the trail vanished when 'arrived' landed a second after the send (PC clock 31 min fast)");
            Assert.Equal(ReceiptState.Arrived, trail!.State);
        }
        finally { ServerClock.ResetForTests(); }
    }

    [Fact]
    public void The_knock_countdown_of_a_fresh_invite_reads_about_five_minutes_on_a_fast_pc_clock()
    {
        Assert.True(PackUriBootstrap.Failure == null, PackUriBootstrap.Failure);
        try
        {
            var serverNow = PcFast(TimeSpan.FromMinutes(2));
            var sent = serverNow.AddSeconds(-5);
            var invite = new InboxItem(Id1, SendKind.Invite, "u_ann", "Ann", null, null,
                InviteDestination.BackRoom, null, null, sent, sent.AddSeconds(InviteDestination.LifetimeSeconds));
            string? shown = null;
            WpfRenderHarness.OnStaThread(() =>
            {
                var ctor = typeof(KnockCard).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)[0];
                Action<InboxItem, KnockOutcome> done = (_, _) => { };
                var card = (Window)ctor.Invoke(new object?[] { new Window(), invite, "invites you", "Join", "Not now", "Answer later", done, null });
                var countdown = (TextBlock)typeof(KnockCard).GetField("_countdown", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(card)!;
                shown = countdown.Text;
                card.Content = null;
            });

            var parts = shown!.Split(':');
            var left = int.Parse(parts[0]) * 60 + int.Parse(parts[1]);
            Assert.True(left >= 280, $"countdown shows {shown} for an invite sent 5 s ago (PC clock 2 min fast)");
        }
        finally { ServerClock.ResetForTests(); }
    }

    [Fact]
    public void A_fresh_invite_lands_on_a_fast_pc_clock()
    {
        try
        {
            var serverNow = PcFast(TimeSpan.FromMinutes(6));
            var invite = new InboxItem(Id1, SendKind.Invite, "u_ann", "Ann", null, null,
                InviteDestination.BackRoom, null, null, serverNow.AddSeconds(-20), serverNow.AddSeconds(-20 + InviteDestination.LifetimeSeconds));
            var world = new LandingWorld(false, false, false, PanelVisible: true, LauncherVisible: false, GameHostActive: false);
            Assert.Equal(LandingRoute.Present, LandingRules.Decide(invite, world, ServerClock.UtcNow));
        }
        finally { ServerClock.ResetForTests(); }
    }

    /// <summary>The landing is a static class over App and windows, so its clock is held here by
    /// its source: the router, the expiry checks and the knock card all read the server's clock.</summary>
    [Fact]
    public void The_landing_and_the_knock_card_never_read_the_pc_clock_for_server_times()
    {
        var landing = ReadSource("ConditioningControlPanel", "Services", "Friends", "FriendsLanding.cs");
        Assert.Contains("new FriendsLandingRouter(current, ReadWorld, () => ServerClock.UtcNow, TheSink)", landing);
        Assert.DoesNotContain("IsExpired(DateTimeOffset.UtcNow)", landing);
        var knockDone = Between(landing, "private static void OnKnockDone(", "// ----");
        Assert.DoesNotContain("DateTimeOffset.UtcNow", knockDone);

        var card = ReadSource("ConditioningControlPanel", "Windows", "Friends", "KnockCard.cs");
        Assert.DoesNotContain("DateTimeOffset.UtcNow", card);

        var service = ReadSource("ConditioningControlPanel", "Services", "Friends", "FriendsService.cs");
        Assert.Contains("_now = now ?? (() => ServerClock.UtcNow);", service);
    }

    // ---------------------------------------------------------------- helpers

    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!.FullName;
        }
    }

    private static string ReadSource(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot }.Concat(parts).ToArray()));

    private static string Between(string source, string start, string end)
    {
        int from = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"'{start}' not found");
        int to = source.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        Assert.True(to > from, $"'{end}' not found after '{start}'");
        return source.Substring(from, to - from);
    }

    /// <summary>Answers a send with an item id and each poll with the next queued reply.</summary>
    private sealed class TrailApi : IFriendsApi
    {
        public string? NextItemId;
        public Queue<FriendsPollReply> Replies = new();

        public Task<FriendsSnapshot?> StateAsync(CancellationToken ct = default) => Task.FromResult<FriendsSnapshot?>(FriendsSnapshot.Empty);

        public Task<FriendsPollReply?> PollAsync(PresenceActivity? activity, int? lockDay, bool shared, CancellationToken ct = default)
            => PollAsync(activity, lockDay, shared, null, null, ct);

        public Task<FriendsPollReply?> PollAsync(PresenceActivity? activity, int? lockDay, bool shared, JObject? leashReport,
            JArray? receipts, CancellationToken ct = default)
            => Task.FromResult<FriendsPollReply?>(Replies.Count > 0 ? Replies.Dequeue()
                : new FriendsPollReply(Array.Empty<string>(), Array.Empty<InboxItem>()));

        public Task<AddResult> RequestAsync(string code, CancellationToken ct = default) => Task.FromResult(AddResult.Sent);

        public Task<SendResult> SendAsync(string to, SendKind kind, string? poke, string? destination, string? code, WatchRef? watch,
            CancellationToken ct = default) => Task.FromResult(SendResult.Sent);

        public Task<SendOutcome> SendForItemAsync(string to, SendKind kind, string? poke, string? destination, string? code,
            WatchRef? watch, CancellationToken ct = default)
            => Task.FromResult(new SendOutcome(SendResult.Sent, NextItemId));

        public Task<bool> ActAsync(string op, string id, JObject? extra = null, CancellationToken ct = default) => Task.FromResult(true);
    }
}
