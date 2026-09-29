using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Services.Friends;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Wave 1 invites (lane F1): the sender's trail (sent, arrived, seen, answered), the send reply's
/// item id, the once-only seen reports, the server's blocked list and the one-time move of this
/// PC's old list onto it. Pure rules and the service over a fake wire; no window is built.
/// </summary>
public class FriendsKnockTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);
    private const string Id1 = "0123456789abcdef";
    private const string Id2 = "fedcba9876543210";

    private static SenderReceipt Rc(string? id, string kind, string to, string state, int secondsIn = 5)
        => new(id, kind, to, "Sam", state, T0.UtcDateTime.AddSeconds(secondsIn), null);

    // ---------------------------------------------------------------- the sent book

    [Fact]
    public void A_trail_walks_forward_and_never_back()
    {
        var book = new FriendsSentBook();
        book.Note("u_sam", SendKind.Invite, Id1, "backroom", T0);
        Assert.Equal(ReceiptState.Sent, book.Latest("u_sam", T0)!.State);

        Assert.True(book.Apply(new[] { Rc(Id1, "invite", "u_sam", "arrived"), Rc(Id1, "invite", "u_sam", "seen", 9) }));
        var t = book.Latest("u_sam", T0.AddSeconds(10))!;
        Assert.Equal(ReceiptState.Seen, t.State);
        Assert.Equal(3, t.Lit);
        Assert.Equal(4, t.Steps);
        Assert.False(t.Done);

        // A late arrived after seen moves nothing.
        Assert.False(book.Apply(new[] { Rc(Id1, "invite", "u_sam", "arrived", 12) }));
        Assert.True(book.Apply(new[] { Rc(Id1, "invite", "u_sam", "declined", 20) }));
        t = book.Latest("u_sam", T0.AddSeconds(30))!;
        Assert.Equal(ReceiptState.Declined, t.State);
        Assert.True(t.Done);
        Assert.Equal(4, t.Lit);
    }

    [Fact]
    public void Only_the_latest_item_to_a_friend_is_followed()
    {
        var book = new FriendsSentBook();
        book.Note("u_sam", SendKind.Poke, Id1, "hi", T0);
        book.Note("u_sam", SendKind.Watch, Id2, null, T0.AddSeconds(3));
        // A receipt for the older poke says nothing about the newer watch.
        Assert.False(book.Apply(new[] { Rc(Id1, "poke", "u_sam", "seen") }));
        Assert.Equal(SendKind.Watch, book.Latest("u_sam", T0)!.Kind);
        Assert.True(book.Apply(new[] { Rc(Id2, "watch", "u_sam", "seen") }));
        Assert.True(book.Latest("u_sam", T0)!.Done);
    }

    [Fact]
    public void Requests_leash_kinds_and_impossible_answers_are_ignored()
    {
        var book = new FriendsSentBook();
        book.Note("u_sam", SendKind.Poke, Id1, "hi", T0);
        Assert.False(book.Apply(new[]
        {
            Rc(null, "request", "u_sam", "accepted"),
            Rc(Id1, "leash_tug", "u_sam", "seen"),
            Rc(Id1, "poke", "u_sam", "joined"),   // a poke cannot be joined
            Rc(Id1, "poke", "u_kit", "seen"),     // not who it went to
        }));
        Assert.Equal(ReceiptState.Sent, book.Latest("u_sam", T0)!.State);
        Assert.False(book.Apply(null));
    }

    [Fact]
    public void A_trail_with_no_item_id_stays_at_sent_and_fades_after_its_window()
    {
        var book = new FriendsSentBook();
        book.Note("u_sam", SendKind.Invite, null, "goon", T0);   // an old server gave no id
        Assert.False(book.Apply(new[] { Rc(Id1, "invite", "u_sam", "seen") }));
        Assert.NotNull(book.Latest("u_sam", T0.Add(FriendsSentBook.ShowFor)));
        Assert.Null(book.Latest("u_sam", T0.Add(FriendsSentBook.ShowFor).AddSeconds(1)));
        book.Clear();
        Assert.Null(book.Latest("u_sam", T0));
    }

    [Fact]
    public void An_expired_invite_reads_no_answer_in_the_quiet_tone()
    {
        var t = new SentTrail("u_sam", SendKind.Invite, Id1, null, ReceiptState.Expired, T0);
        Assert.Equal(("friends_trail_invite", "friends_trail_expired"), FriendsDrawerRules.TrailKeys(t));
        Assert.Equal(FriendsDrawerRules.TrailTone.Quiet, FriendsDrawerRules.ToneOf(t));
        var joined = t with { State = ReceiptState.Joined };
        Assert.Equal(FriendsDrawerRules.TrailTone.Yes, FriendsDrawerRules.ToneOf(joined));
        Assert.Equal(("friends_trail_poke", "friends_trail_arrived"),
            FriendsDrawerRules.TrailKeys(t with { Kind = SendKind.Poke, State = ReceiptState.Arrived }));
    }

    // ---------------------------------------------------------------- the service

    [Fact]
    public async Task A_send_starts_a_trail_that_the_polls_receipts_walk_on()
    {
        var api = new ReceiptApi { NextItemId = Id1 };
        var svc = new FriendsService(api, () => "u_me", () => true, () => T0, () => false, _ => { });
        await svc.TickAsync();   // the account is known from the first tick on
        int moved = 0;
        svc.SentTrailsChanged += () => moved++;

        Assert.Equal(SendResult.Sent, await svc.InviteAsync("u_sam", InviteDestination.BackRoom, null));
        Assert.Equal(1, moved);
        var t = svc.LastSentTo("u_sam")!;
        Assert.Equal((SendKind.Invite, Id1, ReceiptState.Sent, "backroom"), (t.Kind, t.ItemId, t.State, t.Detail));

        api.Replies.Enqueue(new FriendsPollReply(Array.Empty<string>(), Array.Empty<InboxItem>(), null,
            new[] { Rc(Id1, "invite", "u_sam", "arrived"), Rc(Id1, "invite", "u_sam", "joined", 30) }));
        await svc.TickAsync();
        Assert.Equal(ReceiptState.Joined, svc.LastSentTo("u_sam")!.State);
        Assert.Equal(2, moved);
    }

    [Fact]
    public async Task A_refused_send_starts_no_trail_and_a_new_account_forgets_them()
    {
        string? account = "u_me";
        var api = new ReceiptApi { NextItemId = Id1, NextResult = SendResult.NotFriends };
        var svc = new FriendsService(api, () => account, () => true, () => T0, () => false, _ => { });
        await svc.TickAsync();
        await svc.PokeAsync("u_sam", "hi");
        Assert.Null(svc.LastSentTo("u_sam"));

        api.NextResult = SendResult.Sent;
        api.NextItemId = Id2;
        await svc.PokeAsync("u_kit", "wp");
        Assert.NotNull(svc.LastSentTo("u_kit"));
        await svc.TickAsync();
        Assert.NotNull(svc.LastSentTo("u_kit"));   // the same account keeps it across polls

        account = "u_other";
        await svc.TickAsync();
        Assert.Null(svc.LastSentTo("u_kit"));
    }

    [Fact]
    public async Task Seen_reports_ride_the_next_poll()
    {
        var api = new ReceiptApi();
        var svc = new FriendsService(api, () => "u_me", () => true, () => T0, () => false, _ => { });
        await svc.TickAsync();
        Assert.Null(api.Bodies.Last());   // nothing to report, nothing sent
        var seen = new FriendsSeen();
        Assert.True(seen.Seen(svc, Id1));
        Assert.True(seen.Report(svc, ReceiptReport.Item(Id1, ReceiptState.Declined)));
        await svc.TickAsync();
        var body = api.Bodies.Last()!;
        Assert.Single(body);   // folded: the furthest state wins
        Assert.Equal(("declined", Id1), ((string?)body[0]!["state"], (string?)body[0]!["id"]));
        await svc.TickAsync();
        Assert.Null(api.Bodies.Last());   // sent once, then forgotten
    }

    // ---------------------------------------------------------------- the wire

    [Fact]
    public void The_send_reply_keeps_its_item_id_only_on_a_real_sent()
    {
        var sent = FriendsApi.SendOutcomeFromReply(JObject.Parse($$"""{ "ok": true, "status": "sent", "item_id": "{{Id1}}" }"""));
        Assert.Equal(new SendOutcome(SendResult.Sent, Id1), sent);
        Assert.Equal(new SendOutcome(SendResult.Sent, null),
            FriendsApi.SendOutcomeFromReply(JObject.Parse("""{ "ok": true, "status": "sent" }""")));
        Assert.Equal(new SendOutcome(SendResult.Sent, null),
            FriendsApi.SendOutcomeFromReply(JObject.Parse("""{ "ok": true, "status": "sent", "item_id": "NOT-AN-ID" }""")));
        Assert.Equal(new SendOutcome(SendResult.Refused, null),
            FriendsApi.SendOutcomeFromReply(JObject.Parse($$"""{ "ok": false, "reason": "refused", "item_id": "{{Id1}}" }""")));
        Assert.Equal(new SendOutcome(SendResult.TryLater, null), FriendsApi.SendOutcomeFromReply(null));
    }

    [Fact]
    public void The_state_reads_the_servers_blocked_list_and_tells_absent_from_empty()
    {
        var withList = FriendsApi.ParseState(JObject.Parse("""
            { "ok": true, "code": "CCP-ABCDE", "friends": [], "incoming": [], "outgoing": [],
              "blocked": [ { "id": "u_a", "name": "Ann" }, { "id": "u_b" }, { "id": "u_a", "name": "Again" }, { "name": "no id" } ] }
            """));
        Assert.Equal(new[] { new BlockedFriend("u_a", "Ann"), new BlockedFriend("u_b", "?") }, withList.Blocked);

        var old = FriendsApi.ParseState(JObject.Parse("""{ "ok": true, "code": "CCP-ABCDE", "friends": [] }"""));
        Assert.Null(old.Blocked);

        var none = FriendsApi.ParseState(JObject.Parse("""{ "ok": true, "code": "CCP-ABCDE", "blocked": [] }"""));
        Assert.NotNull(none.Blocked);
        Assert.Empty(none.Blocked!);

        // A snapshot that only differs in its blocked list is a change the drawer must see.
        Assert.False(FriendsSnapshotEquality.Same(old, old with { Blocked = new[] { new BlockedFriend("u_a", "Ann") } }));
        Assert.True(FriendsSnapshotEquality.Same(withList, withList with { Blocked = withList.Blocked!.ToList() }));
    }

    // ---------------------------------------------------------------- seen, once

    [Fact]
    public void Seen_goes_once_per_thing_and_state()
    {
        var svc = new RecordingService();
        var seen = new FriendsSeen();
        Assert.True(seen.Seen(svc, Id1));
        Assert.False(seen.Seen(svc, Id1));
        Assert.True(seen.Report(svc, ReceiptReport.Item(Id1, ReceiptState.Joined)));
        Assert.False(seen.Seen(svc, "short"));
        Assert.False(seen.Seen(null, Id2));

        var req = new FriendRequest("u_dee", "Dee", null, null, T0);
        Assert.True(seen.RequestSeen(svc, req));
        Assert.False(seen.RequestSeen(svc, req));
        // The same person asking again later is a new request.
        Assert.True(seen.RequestSeen(svc, req with { At = T0.AddDays(1) }));

        Assert.Equal(new[] { "i:" + Id1 + "=seen", "i:" + Id1 + "=joined", "r:u_dee=seen", "r:u_dee=seen" },
            svc.Reports.Select(r => r.Key + "=" + r.State));
        seen.Clear();
        Assert.True(seen.Seen(svc, Id1));
    }

    // ---------------------------------------------------------------- the blocked move

    [Fact]
    public async Task The_old_block_list_moves_to_the_server_once()
    {
        var store = new List<BlockedEntry>
        {
            new("me", "u_a", "Ann", T0),
            new("me", "u_b", "Bo", T0),
            new("me", "u_c", "Cy", T0),
            new("other", "u_z", "Zed", T0),
        };
        var list = new FriendsBlockList(() => store.ToList(), l => { store.Clear(); store.AddRange(l); });
        var svc = new RecordingService { BlockAnswers = { ["u_b"] = ActResult.Done, ["u_c"] = ActResult.TryLater } };

        // u_a is on the server already; u_b is sent and lands; u_c hits a fault and waits.
        var done = await FriendsBlockList.MigrateAsync(svc, list, "me", new[] { new BlockedFriend("u_a", "Ann") });
        Assert.False(done);
        Assert.Equal(new[] { "u_b", "u_c" }, svc.Blocks.OrderBy(x => x));
        Assert.Equal(new[] { "u_c" }, list.For("me").Select(e => e.Id));
        Assert.Single(list.For("other"));   // another account's list is never touched

        svc.BlockAnswers["u_c"] = ActResult.Done;
        Assert.True(await FriendsBlockList.MigrateAsync(svc, list, "me", new[] { new BlockedFriend("u_a", "Ann") }));
        Assert.Empty(list.For("me"));
    }

    [Fact]
    public async Task A_block_the_server_will_never_take_is_dropped_not_retried()
    {
        var store = new List<BlockedEntry> { new("me", "u_gone", "Gone", T0) };
        var list = new FriendsBlockList(() => store.ToList(), l => { store.Clear(); store.AddRange(l); });
        var svc = new RecordingService { BlockAnswers = { ["u_gone"] = ActResult.NotFound } };
        Assert.True(await FriendsBlockList.MigrateAsync(svc, list, "me", Array.Empty<BlockedFriend>()));
        Assert.Empty(store);
    }

    // ---------------------------------------------------------------- fakes

    /// <summary>A wire that answers sends with an item id and polls with queued receipts, and keeps
    /// every poll's receipts body.</summary>
    private sealed class ReceiptApi : IFriendsApi
    {
        public string? NextItemId;
        public SendResult NextResult = SendResult.Sent;
        public Queue<FriendsPollReply> Replies = new();
        public List<JArray?> Bodies = new();

        public Task<FriendsSnapshot?> StateAsync(CancellationToken ct = default) => Task.FromResult<FriendsSnapshot?>(FriendsSnapshot.Empty);

        public Task<FriendsPollReply?> PollAsync(PresenceActivity? activity, int? lockDay, bool shared, CancellationToken ct = default)
            => PollAsync(activity, lockDay, shared, null, null, ct);

        public Task<FriendsPollReply?> PollAsync(PresenceActivity? activity, int? lockDay, bool shared, JObject? leashReport,
            JArray? receipts, CancellationToken ct = default)
        {
            Bodies.Add(receipts);
            return Task.FromResult<FriendsPollReply?>(Replies.Count > 0 ? Replies.Dequeue()
                : new FriendsPollReply(Array.Empty<string>(), Array.Empty<InboxItem>()));
        }

        public Task<AddResult> RequestAsync(string code, CancellationToken ct = default) => Task.FromResult(AddResult.Sent);

        public Task<SendResult> SendAsync(string to, SendKind kind, string? poke, string? destination, string? code, WatchRef? watch,
            CancellationToken ct = default) => Task.FromResult(NextResult);

        public Task<SendOutcome> SendForItemAsync(string to, SendKind kind, string? poke, string? destination, string? code,
            WatchRef? watch, CancellationToken ct = default)
            => Task.FromResult(new SendOutcome(NextResult, NextResult == SendResult.Sent ? NextItemId : null));

        public Task<bool> ActAsync(string op, string id, JObject? extra = null, CancellationToken ct = default) => Task.FromResult(true);
    }

    /// <summary>Records receipts and blocks; everything else answers done.</summary>
    private sealed class RecordingService : IFriendsService
    {
        public List<ReceiptReport> Reports { get; } = new();
        public List<string> Blocks { get; } = new();
        public Dictionary<string, ActResult> BlockAnswers { get; } = new();

        public void ReportReceipt(ReceiptReport report) => Reports.Add(report);

        public bool Available => true;
        public FriendsSnapshot Snapshot => FriendsSnapshot.Empty;
        public bool PresenceShared { get; set; }
        public event Action<FriendsSnapshot>? SnapshotChanged { add { } remove { } }
        public event Action<InboxItem>? Delivered { add { } remove { } }
        public event Action<SendKind, Friend>? Sent { add { } remove { } }
        public event Action<FriendRequest>? RequestArrived { add { } remove { } }
        public event Action<string>? RequestGone { add { } remove { } }
        public Task RefreshAsync() => Task.CompletedTask;
        public Task<SendResult> PokeAsync(string friendId, string pokeId) => Task.FromResult(SendResult.Sent);
        public Task<SendResult> InviteAsync(string friendId, string destination, string? code) => Task.FromResult(SendResult.Sent);
        public Task<SendResult> SendWatchAsync(string friendId, WatchRef watch) => Task.FromResult(SendResult.Sent);
        public Task<AddResult> AddByCodeAsync(string code) => Task.FromResult(AddResult.Sent);
        public Task<ActResult> AcceptAsync(string requesterId) => Task.FromResult(ActResult.Done);
        public Task<ActResult> DeclineAsync(string requesterId) => Task.FromResult(ActResult.Done);
        public Task<ActResult> CancelRequestAsync(string targetId) => Task.FromResult(ActResult.Done);
        public Task<ActResult> RemoveAsync(string friendId) => Task.FromResult(ActResult.Done);
        public Task<ActResult> BlockAsync(string friendId)
        {
            Blocks.Add(friendId);
            return Task.FromResult(BlockAnswers.TryGetValue(friendId, out var r) ? r : ActResult.Done);
        }
        public Task<ActResult> UnblockAsync(string friendId) => Task.FromResult(ActResult.Done);
        public Task<ActResult> SetSquelchAsync(string friendId, bool on) => Task.FromResult(ActResult.Done);
        public Task<ActResult> ReportAsync(string friendId, string reason) => Task.FromResult(ActResult.Done);
        public void SetActivity(PresenceActivity activity) { }
        public void SetDrawerOpen(bool open) { }
    }
}
