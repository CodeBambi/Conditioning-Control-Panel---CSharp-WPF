using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ConditioningControlPanel.Controls.Leash;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.Leash;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>Seen receipts on the leash (CONTRACT "Receipts"): the holder's log of what it sent and
/// how far each item got, the wire, and the service's two halves on the friends channel.</summary>
public class LeashReceiptsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 29, 10, 0, 0, TimeSpan.Zero);
    private const string Id1 = "0123456789abcdef";
    private const string Id2 = "fedcba9876543210";
    private const string Id3 = "00000000deadbeef";
    private static readonly LeashPerson Pet = new("u_pet", "Pet", null);

    private static LeashSendResult Sent(string? id = null) => new(LeashSendStatus.Sent, null, id);

    private static Punishment Pun(string pid, PunishKind kind, int size, DateTimeOffset? at = null) =>
        new(pid, kind, size, null, FakeLeashService.Vex, at ?? T0, (at ?? T0).AddHours(72));

    private static HeldLeash Held(IEnumerable<Punishment>? pending = null, Assignment? assignment = null) => new(
        Pet, true, LeashIntensity.Strict, T0.AddDays(-3), 3, null, null, Array.Empty<WeekDay>(),
        (pending ?? Array.Empty<Punishment>()).ToList(), assignment, 0);

    private static LeashReceipt Rc(string id, LeashItemKind kind, LeashStep state, DateTimeOffset at, string to = "u_pet") =>
        new(id, kind, to, "Pet", state, at);

    private static LeashEvent Ev(LeashEventKind kind, DateTimeOffset at, PunishRef? given = null, Assignment? a = null,
        bool? accepted = null, string? reason = null) =>
        new("e" + at.Ticks, kind, Pet, at, Assignment: a, Accepted: accepted) { Given = given, Reason = reason };

    private static string Id(string k) => k;

    // ---- the log -----------------------------------------------------------------------

    [Fact]
    public void An_old_server_never_turns_the_steps_on()
    {
        var log = new LeashSentLog();
        Assert.True(log.NoteSent(LeashItemKind.Tug, "u_pet", Sent(), T0));
        Assert.True(log.NoteSent(LeashItemKind.Punish, "u_pet", Sent(), T0.AddSeconds(1), punish: PunishKind.Lines, size: 5));
        log.Bind(new[] { Held(new[] { Pun(Id1, PunishKind.Lines, 5) }) });
        log.ApplyEvent(Ev(LeashEventKind.PunishDone, T0.AddMinutes(5), new PunishRef(Id1, PunishKind.Lines, 5)));
        log.ApplyEvent(Ev(LeashEventKind.Answered, T0.AddMinutes(6), accepted: true));

        Assert.False(log.Supported);
        Assert.Equal(LeashStep.Done, log.For("u_pet", T0.AddMinutes(7)).Single(i => i.Kind == LeashItemKind.Punish).Step);
        Assert.Null(LeashUiRules.OfferStep(log.Supported, log.For("u_pet", T0.AddMinutes(7))));
    }

    [Fact]
    public void An_id_in_the_reply_turns_the_steps_on_and_receipts_only_move_forward()
    {
        var log = new LeashSentLog();
        Assert.True(log.NoteSent(LeashItemKind.Tug, "u_pet", Sent(Id1), T0));
        Assert.True(log.Supported);
        Assert.True(log.Apply(Rc(Id1, LeashItemKind.Tug, LeashStep.Seen, T0.AddSeconds(40))));
        Assert.False(log.Apply(Rc(Id1, LeashItemKind.Tug, LeashStep.Arrived, T0.AddSeconds(50))));

        var it = log.For("u_pet", T0.AddMinutes(1)).Single();
        Assert.Equal(LeashStep.Seen, it.Step);
        Assert.Equal(T0.AddSeconds(40), it.SeenAt);
        Assert.Equal(T0.AddSeconds(40), it.ArrivedAt);
        Assert.Equal(new[] { true, true, true }, LeashUiRules.Lit(it));
        // The same id twice is one row.
        Assert.False(log.NoteSent(LeashItemKind.Tug, "u_pet", Sent(Id1), T0.AddSeconds(2)));
        Assert.Single(log.For("u_pet", T0.AddMinutes(1)));
    }

    [Fact]
    public void A_punishment_learns_its_pid_from_the_snapshot_then_arrives_and_is_done()
    {
        var log = new LeashSentLog();
        log.NoteSent(LeashItemKind.Punish, "u_pet", new LeashSendResult(LeashSendStatus.Queued), T0, punish: PunishKind.Video, size: 30);
        Assert.Null(log.For("u_pet", T0).Single().Id);

        // The server lowered the video to their cap: its size wins.
        Assert.True(log.Bind(new[] { Held(new[] { Pun(Id1, PunishKind.Video, 20) }) }));
        Assert.False(log.Bind(new[] { Held(new[] { Pun(Id1, PunishKind.Video, 20) }) }));
        var it = log.For("u_pet", T0).Single();
        Assert.Equal(Id1, it.Id);
        Assert.Equal(20, it.Size);

        Assert.True(log.Apply(Rc(Id1, LeashItemKind.Punish, LeashStep.Arrived, T0.AddMinutes(1))));
        Assert.True(log.Supported);
        Assert.Equal(new[] { true, true, false, false }, LeashUiRules.Lit(log.For("u_pet", T0).Single()));
        Assert.True(log.ApplyEvent(Ev(LeashEventKind.PunishDone, T0.AddMinutes(30), new PunishRef(Id1, PunishKind.Video, 20))));

        it = log.For("u_pet", T0.AddHours(1)).Single();
        Assert.Equal(LeashStep.Done, it.Step);
        Assert.Equal(new[] { true, true, true, true }, LeashUiRules.Lit(it));
        // A late seen fills in its time; the ending stays.
        log.Apply(Rc(Id1, LeashItemKind.Punish, LeashStep.Seen, T0.AddMinutes(2)));
        it = log.For("u_pet", T0.AddHours(1)).Single();
        Assert.Equal(LeashStep.Done, it.Step);
        Assert.Equal(T0.AddMinutes(2), it.SeenAt);
    }

    [Fact]
    public void Queued_punishments_bind_by_kind_oldest_first()
    {
        var log = new LeashSentLog();
        log.NoteSent(LeashItemKind.Punish, "u_pet", Sent(), T0, punish: PunishKind.Lines, size: 5);
        log.NoteSent(LeashItemKind.Punish, "u_pet", Sent(), T0.AddSeconds(1), punish: PunishKind.Pink, size: 10);
        log.NoteSent(LeashItemKind.Punish, "u_pet", Sent(), T0.AddSeconds(2), punish: PunishKind.Lines, size: 3);
        log.Bind(new[]
        {
            Held(new[] { Pun(Id3, PunishKind.Lines, 3, T0.AddSeconds(2)), Pun(Id1, PunishKind.Lines, 5, T0), Pun(Id2, PunishKind.Pink, 10, T0.AddSeconds(1)) }),
        });
        var items = log.For("u_pet", T0.AddMinutes(1));
        Assert.Equal(Id1, items.Single(i => i.Punish == PunishKind.Lines && i.Size == 5).Id);
        Assert.Equal(Id2, items.Single(i => i.Punish == PunishKind.Pink).Id);
        Assert.Equal(Id3, items.Single(i => i.Punish == PunishKind.Lines && i.Size == 3).Id);
    }

    [Fact]
    public void A_skipped_punishment_ends_with_its_reason_and_proves_receipts()
    {
        var log = new LeashSentLog();
        log.NoteSent(LeashItemKind.Punish, "u_pet", new LeashSendResult(LeashSendStatus.Queued), T0, punish: PunishKind.Video, size: 20);
        log.Bind(new[] { Held(new[] { Pun(Id1, PunishKind.Video, 20) }) });
        Assert.False(log.Supported);

        Assert.True(log.ApplyEvent(Ev(LeashEventKind.PunishSkipped, T0.AddMinutes(3), new PunishRef(Id1, PunishKind.Video, 20), reason: "unplayable")));
        Assert.True(log.Supported);
        var it = log.For("u_pet", T0.AddMinutes(5)).Single();
        Assert.Equal(LeashStep.Skipped, it.Step);
        Assert.Equal("unplayable", it.Reason);
        Assert.True(it.WasSeen);
        Assert.Equal(0, LeashUiRules.Tone(it.Step));
        Assert.EndsWith("\nleash_skip_unplayable", LeashUiRules.Timeline(it, T0.AddMinutes(5), Id));
        // An ending never changes.
        Assert.False(log.ApplyEvent(Ev(LeashEventKind.PunishDone, T0.AddMinutes(9), new PunishRef(Id1, PunishKind.Video, 20))));
    }

    [Fact]
    public void A_refused_send_is_a_row_that_never_left_and_a_fault_is_nothing()
    {
        var log = new LeashSentLog();
        Assert.False(log.NoteSent(LeashItemKind.Reward, "u_pet", new LeashSendResult(LeashSendStatus.Failed), T0, reward: RewardKind.Pardon));
        Assert.False(log.NoteSent(LeashItemKind.Reward, "u_pet", new LeashSendResult(LeashSendStatus.Off), T0, reward: RewardKind.Pardon));
        Assert.False(log.NoteSent(LeashItemKind.Offer, "u_pet", new LeashSendResult(LeashSendStatus.Already), T0));
        Assert.False(log.NoteSent(LeashItemKind.Offer, "", Sent(Id1), T0));
        Assert.Empty(log.For("u_pet", T0));

        Assert.True(log.NoteSent(LeashItemKind.Reward, "u_pet", new LeashSendResult(LeashSendStatus.Full, null, Id1), T0, reward: RewardKind.Pardon));
        var it = log.For("u_pet", T0).Single();
        Assert.Equal(LeashStep.Refused, it.Step);
        Assert.Null(it.Id);
        Assert.Equal(LeashSendStatus.Full, it.Refusal);
        Assert.False(log.Supported);
        Assert.All(LeashUiRules.Lit(it), on => Assert.False(on));
        Assert.Equal(-1, LeashUiRules.Tone(it.Step));
        Assert.Equal("leash_step_refused: leash_res_full", LeashUiRules.Timeline(it, T0, Id));
    }

    [Fact]
    public void A_chaster_punishment_takes_its_id_from_its_first_receipt()
    {
        var log = new LeashSentLog();
        log.NoteSent(LeashItemKind.Punish, "u_pet", Sent(), T0, punish: PunishKind.Chaster, size: 900);
        log.NoteSent(LeashItemKind.Punish, "u_pet", Sent(), T0.AddSeconds(1), punish: PunishKind.Lines, size: 5);
        // Chaster time is booked at once and never sits in the queue: the snapshot never names it.
        log.Bind(new[] { Held() });
        Assert.True(log.Apply(Rc(Id1, LeashItemKind.Punish, LeashStep.Arrived, T0.AddMinutes(1))));

        var items = log.For("u_pet", T0.AddMinutes(2));
        Assert.Equal(2, items.Count);
        Assert.Equal(Id1, items.Single(i => i.Punish == PunishKind.Chaster).Id);
        Assert.Null(items.Single(i => i.Punish == PunishKind.Lines).Id);
        Assert.Equal("leash_pun_chaster +15:00", LeashUiRules.ItemText(items.Single(i => i.Punish == PunishKind.Chaster), Id));
    }

    [Fact]
    public void A_receipt_for_something_this_client_never_sent_is_a_row_of_its_own()
    {
        var log = new LeashSentLog();
        log.NoteSent(LeashItemKind.Tug, "u_pet", Sent(Id1), T0);
        Assert.True(log.Apply(Rc(Id2, LeashItemKind.Reward, LeashStep.Seen, T0.AddMinutes(1))));
        Assert.True(log.Apply(Rc(Id3, LeashItemKind.Punish, LeashStep.Arrived, T0.AddMinutes(2))));

        var items = log.For("u_pet", T0.AddMinutes(3));
        Assert.Equal(3, items.Count);
        var reward = items.Single(i => i.Id == Id2);
        Assert.Equal(LeashItemKind.Reward, reward.Kind);
        Assert.Equal(LeashStep.Seen, reward.Step);
        Assert.Equal("leash_btn_reward", LeashUiRules.ItemText(reward, Id));
        Assert.Equal("leash_item_punish", LeashUiRules.ItemText(items.Single(i => i.Id == Id3), Id));
        // Someone else's receipt never lands on this friend's card; a malformed one lands nowhere.
        Assert.True(log.Apply(Rc("aaaaaaaaaaaaaaaa", LeashItemKind.Tug, LeashStep.Arrived, T0, to: "u_other")));
        Assert.False(log.Apply(Rc("NOT-AN-ID", LeashItemKind.Tug, LeashStep.Arrived, T0)));
        Assert.Equal(3, log.For("u_pet", T0.AddMinutes(3)).Count);
    }

    [Fact]
    public void A_task_binds_its_aid_and_ends_done_or_missed()
    {
        var log = new LeashSentLog();
        log.NoteSent(LeashItemKind.Assign, "u_pet", Sent(), T0, assign: AssignKind.Minutes, size: 30);
        var a = new Assignment(Id1, AssignKind.Minutes, 30, null, "20260929", AssignStatus.Open, T0);
        Assert.True(log.Bind(new[] { Held(assignment: a) }));
        Assert.True(log.Apply(Rc(Id1, LeashItemKind.Assign, LeashStep.Seen, T0.AddMinutes(2))));
        Assert.True(log.ApplyEvent(Ev(LeashEventKind.AssignMissed, T0.AddHours(14), a: a)));

        var it = log.For("u_pet", T0.AddHours(15)).Single();
        Assert.Equal(LeashStep.Missed, it.Step);
        Assert.Equal(new[] { true, true, true, true }, LeashUiRules.Lit(it));
        Assert.Equal(-1, LeashUiRules.Tone(it.Step));
        var words = new Dictionary<string, string> { ["leash_item_task"] = "Task: {0}", ["leash_assign_minutes_n"] = "{0} min" };
        Assert.Equal("Task: 30 min", LeashUiRules.ItemText(it, k => words.TryGetValue(k, out var v) ? v : k));

        // A task sent from elsewhere and done still gets its row.
        var b = new Assignment(Id2, AssignKind.Quests, 2, null, "20260930", AssignStatus.Done, T0.AddDays(1));
        Assert.True(log.ApplyEvent(Ev(LeashEventKind.AssignDone, T0.AddDays(1), a: b)));
        Assert.Equal(LeashStep.Done, log.For("u_pet", T0.AddDays(1)).Single(i => i.Id == Id2).Step);
    }

    [Fact]
    public void An_answered_offer_ends_accepted_or_declined()
    {
        var log = new LeashSentLog();
        log.NoteSent(LeashItemKind.Offer, "u_pet", Sent(Id1), T0);
        log.Apply(Rc(Id1, LeashItemKind.Offer, LeashStep.Arrived, T0.AddSeconds(30)));
        Assert.Equal(LeashStep.Arrived, LeashUiRules.OfferStep(log.Supported, log.For("u_pet", T0.AddMinutes(1))));
        Assert.True(log.ApplyEvent(Ev(LeashEventKind.Answered, T0.AddMinutes(1), accepted: true)));
        Assert.Equal(LeashStep.Accepted, log.LatestOffer("u_pet")!.Step);
        Assert.Null(LeashUiRules.OfferStep(log.Supported, log.For("u_pet", T0.AddMinutes(2))));

        // The next offer after a cut is its own row; a decline ends it.
        log.NoteSent(LeashItemKind.Offer, "u_pet", Sent(Id2), T0.AddDays(1));
        Assert.True(log.ApplyEvent(Ev(LeashEventKind.Answered, T0.AddDays(1).AddMinutes(1), accepted: false)));
        Assert.Equal(Id2, log.LatestOffer("u_pet")!.Id);
        Assert.Equal(LeashStep.Declined, log.LatestOffer("u_pet")!.Step);
        Assert.Equal(LeashStep.Accepted, log.For("u_pet", T0.AddDays(1)).Single(i => i.Id == Id1).Step);
    }

    [Fact]
    public void Each_friend_keeps_the_newest_twelve_and_nothing_past_four_days()
    {
        var log = new LeashSentLog();
        for (int i = 0; i < LeashSentLog.PerFriend + 3; i++) log.NoteSent(LeashItemKind.Tug, "u_pet", Sent(), T0.AddMinutes(i));
        log.NoteSent(LeashItemKind.Tug, "u_other", Sent(), T0);

        var mine = log.For("u_pet", T0.AddHours(1));
        Assert.Equal(LeashSentLog.PerFriend, mine.Count);
        Assert.Equal(T0.AddMinutes(LeashSentLog.PerFriend + 2), mine[0].SentAt);
        Assert.Single(log.For("u_other", T0.AddHours(1)));
        Assert.Empty(log.For("u_pet", T0.AddDays(5)));
        log.Clear();
        Assert.Empty(log.For("u_other", T0));
    }

    // ---- the wire ----------------------------------------------------------------------

    private static JObject P(string id, string name) => new() { ["id"] = id, ["name"] = name, ["avatar"] = null };

    [Fact]
    public void The_wire_reads_offer_ids_skip_events_and_leash_receipts_only()
    {
        var at = T0.ToString("o");
        Assert.Equal(Id1, LeashParse.Offer(new JObject { ["from"] = P("u_vex", "Vex"), ["at"] = at, ["id"] = Id1 })!.Id);
        Assert.Null(LeashParse.Offer(new JObject { ["from"] = P("u_vex", "Vex"), ["at"] = at, ["id"] = "NOT-HEX-AT-ALL!!" })!.Id);
        Assert.Null(LeashParse.Offer(new JObject { ["from"] = P("u_vex", "Vex"), ["at"] = at })!.Id);

        var skip = LeashParse.Event(new JObject
        {
            ["id"] = Id2, ["kind"] = "punish_skipped", ["from"] = P("u_pet", "Pet"), ["at"] = at,
            ["punishment"] = new JObject { ["pid"] = Id1, ["kind"] = "video", ["size"] = 20 }, ["reason"] = "unplayable",
        });
        Assert.Equal(LeashEventKind.PunishSkipped, skip!.Kind);
        Assert.Equal(new PunishRef(Id1, PunishKind.Video, 20), skip.Given);
        Assert.Equal("unplayable", skip.Reason);
        Assert.Equal("leash_evt_punish_skipped", LeashUiRules.EventKey(skip));
        // Without the punishment there is nothing to tell the holder.
        Assert.Null(LeashParse.Event(new JObject { ["id"] = Id2, ["kind"] = "punish_skipped", ["from"] = P("u_pet", "Pet"), ["at"] = at }));

        var done = LeashParse.Event(new JObject
        {
            ["id"] = Id3, ["kind"] = "punish_done", ["from"] = P("u_pet", "Pet"), ["at"] = at,
            ["punishment"] = new JObject { ["pid"] = Id1, ["kind"] = "lines", ["size"] = 5 },
        });
        Assert.Equal(Id1, done!.Given!.Pid);
        Assert.Null(LeashParse.Event(new JObject { ["id"] = Id3, ["kind"] = "punish_done", ["from"] = P("u_pet", "Pet"), ["at"] = at })!.Given);

        // As the friends wire reads it: dates stay strings (FriendsApi's DateParseHandling.None).
        var list = LeashParse.Receipts(Newtonsoft.Json.JsonConvert.DeserializeObject<JArray>($$"""
        [
          { "id": "{{Id1}}", "kind": "leash_punish", "to": "u_pet", "to_name": "Pet", "state": "arrived", "at": "2026-09-29T10:00:00Z", "ref": "{{Id1}}" },
          { "id": "{{Id2}}", "kind": "leash_tug", "to": "u_pet", "state": "seen", "at": "2026-09-29T10:01:00Z" },
          { "id": "{{Id3}}", "kind": "poke", "to": "u_pet", "state": "seen" },
          { "id": "{{Id3}}", "kind": "leash_offer", "to": "u_pet", "state": "joined" },
          { "id": "BAD", "kind": "leash_tug", "to": "u_pet", "state": "seen" },
          { "id": "{{Id3}}", "kind": "leash_tug", "state": "seen" },
          "junk"
        ]
        """, new Newtonsoft.Json.JsonSerializerSettings { DateParseHandling = Newtonsoft.Json.DateParseHandling.None }));
        Assert.Equal(new[] { LeashItemKind.Punish, LeashItemKind.Tug }, list.Select(r => r.Kind));
        Assert.Equal(new[] { LeashStep.Arrived, LeashStep.Seen }, list.Select(r => r.State));
        Assert.Equal(Id1, list[0].Ref);
        Assert.Equal(new DateTimeOffset(2026, 9, 29, 10, 1, 0, TimeSpan.Zero), list[1].At);
        Assert.Empty(LeashParse.Receipts(null));
    }

    // ---- the service -------------------------------------------------------------------

    private sealed class Wire : ILeashApi
    {
        public readonly Queue<JObject?> Replies = new();
        public readonly List<string> Ops = new();

        public Task<JObject?> CallAsync(string op, JObject body, CancellationToken ct = default)
        {
            Ops.Add(op);
            return Task.FromResult(Replies.Count > 0 ? Replies.Dequeue() : new JObject { ["ok"] = true, ["status"] = "sent" });
        }
    }

    private static (LeashService Svc, Wire Wire, List<string> Seen) Service(Func<string?>? account = null)
    {
        var wire = new Wire();
        var seen = new List<string>();
        // A clock that ticks a second per read, so "newest first" has an order to keep.
        var t = T0;
        var svc = new LeashService(wire, account ?? (() => "u_me"), () => t = t.AddSeconds(1), cutSafety: () => { }, reportSeen: seen.Add);
        return (svc, wire, seen);
    }

    private static JObject HeldJson(JArray? pending = null) => new()
    {
        ["who"] = P("u_pet", "Pet"), ["online"] = true, ["intensity"] = "strict", ["since"] = T0.AddDays(-3).ToString("o"), ["day"] = 3,
        ["dnd_until"] = null, ["report"] = null, ["week"] = new JArray(), ["pending"] = pending ?? new JArray(), ["assignment"] = null,
        ["punished_today"] = 1,
    };

    private static JObject BlockJson(JObject held, JArray? events = null) => new()
    {
        ["me"] = null, ["holding"] = new JArray { held }, ["offers"] = new JArray(), ["events"] = events ?? new JArray(),
    };

    [Fact]
    public async Task Send_replies_with_ids_turn_the_steps_on_and_old_replies_do_not()
    {
        var (svc, wire, _) = Service();
        var changes = 0;
        svc.ReceiptsChanged += () => changes++;

        Assert.Equal(LeashSendStatus.Sent, (await svc.TugAsync("u_pet")).Status);
        Assert.False(svc.ReceiptsSupported);

        wire.Replies.Enqueue(new JObject { ["ok"] = true, ["status"] = "sent", ["offer_id"] = Id1 });
        Assert.Equal(Id1, (await svc.OfferAsync("u_juno")).ItemId);
        Assert.True(svc.ReceiptsSupported);
        Assert.Equal(Id1, svc.OfferTo("u_juno")!.Id);

        wire.Replies.Enqueue(new JObject { ["ok"] = true, ["status"] = "sent", ["item_id"] = Id2 });
        await svc.RewardAsync("u_pet", RewardKind.Sticker, "star");
        var newest = svc.SentTo("u_pet").First();
        Assert.Equal(Id2, newest.Id);
        Assert.Equal("star", newest.Token);
        Assert.Equal(2, svc.SentTo("u_pet").Count);
        Assert.Equal(3, changes);

        // A refusal carries no id, whatever the reply says; a grammar refusal never reaches the wire or the log.
        wire.Replies.Enqueue(new JObject { ["ok"] = false, ["reason"] = "full", ["item_id"] = Id3 });
        Assert.Null((await svc.RewardAsync("u_pet", RewardKind.Pardon)).ItemId);
        Assert.Equal(LeashStep.Refused, svc.SentTo("u_pet").First().Step);
        Assert.Equal(LeashSendStatus.Refused, (await svc.RewardAsync("u_pet", RewardKind.Sticker, "not-a-sticker")).Status);
        Assert.Equal(3, svc.SentTo("u_pet").Count);
        Assert.Equal(new[] { "tug", "offer", "reward", "reward" }, wire.Ops);
    }

    [Fact]
    public void Seen_is_reported_once_per_item_and_only_for_real_ids()
    {
        string? account = "u_me";
        var (svc, _, seen) = Service(() => account);
        svc.NoteShown(Id1);
        svc.NoteShown(Id1);
        svc.NoteShown("p1");
        svc.NoteShown(null);
        svc.NoteShown(Id1.ToUpperInvariant());
        Assert.Equal(new[] { Id1 }, seen);

        // Signed out nothing is reported; another account starts fresh.
        account = null;
        svc.NoteShown(Id2);
        account = "u_other";
        svc.NoteShown(Id1);
        Assert.Equal(new[] { Id1, Id1 }, seen);
    }

    [Fact]
    public void The_friends_channel_carries_seen_out_and_leash_receipts_in()
    {
        var (svc, _, _) = Service();
        var reports = new List<ReceiptReport>();
        Action<IReadOnlyList<SenderReceipt>>? handler = null;
        svc.Attach(reports.Add, h => handler = h);

        svc.NoteShown(Id1);
        Assert.Equal(new[] { ReceiptReport.Item(Id1, ReceiptState.Seen) }, reports);
        Assert.NotNull(handler);

        var changes = 0;
        svc.ReceiptsChanged += () => changes++;
        handler!(new[]
        {
            new SenderReceipt(Id2, ReceiptKind.LeashTug, "u_pet", "Pet", ReceiptState.Seen, new DateTime(2026, 9, 29, 10, 5, 0, DateTimeKind.Utc), null),
            new SenderReceipt(Id3, ReceiptKind.Poke, "u_pet", "Pet", ReceiptState.Seen, DateTime.UtcNow, null),
            new SenderReceipt(null, ReceiptKind.LeashTug, "u_pet", "Pet", ReceiptState.Seen, DateTime.UtcNow, null),
            new SenderReceipt(Id1, ReceiptKind.LeashOffer, "u_pet", "Pet", ReceiptState.Joined, DateTime.UtcNow, null),
        });
        Assert.True(svc.ReceiptsSupported);
        Assert.Equal(1, changes);
        var it = svc.SentTo("u_pet").Single();
        Assert.Equal(Id2, it.Id);
        Assert.Equal(LeashItemKind.Tug, it.Kind);
        Assert.Equal(LeashStep.Seen, it.Step);
        Assert.Equal(new DateTimeOffset(2026, 9, 29, 10, 5, 0, TimeSpan.Zero), it.SeenAt);
    }

    [Fact]
    public void A_poll_reply_with_an_empty_receipts_array_still_proves_the_server_speaks_them()
    {
        var (svc, _, _) = Service();
        svc.ApplyReceiptsJson(null);
        Assert.False(svc.ReceiptsSupported);
        svc.ApplyReceiptsJson(new JArray());
        Assert.True(svc.ReceiptsSupported);
    }

    [Fact]
    public async Task A_skipped_punishment_reaches_the_holder_log_and_the_event_stream()
    {
        var (svc, wire, _) = Service();
        wire.Replies.Enqueue(new JObject { ["ok"] = true, ["status"] = "queued" });
        await svc.PunishAsync("u_pet", PunishKind.Video, 20, new LeashWatch("ht", "123456", "Deep pink"));

        var pun = new JObject
        {
            ["pid"] = Id1, ["kind"] = "video", ["size"] = 20, ["from"] = P("u_me", "Me"), ["at"] = T0.ToString("o"),
            ["expires_at"] = T0.AddHours(72).ToString("o"), ["watch"] = new JObject { ["kind"] = "ht", ["id"] = "123456", ["title"] = "Deep pink" },
        };
        svc.ApplyBlock(BlockJson(HeldJson(new JArray { pun })));
        Assert.Equal(Id1, svc.SentTo("u_pet").Single().Id);
        Assert.False(svc.ReceiptsSupported);

        var events = new List<LeashEvent>();
        var changes = 0;
        svc.EventArrived += events.Add;
        svc.ReceiptsChanged += () => changes++;
        var skip = new JObject
        {
            ["id"] = Id2, ["kind"] = "punish_skipped", ["from"] = P("u_pet", "Pet"), ["at"] = T0.AddMinutes(4).ToString("o"),
            ["punishment"] = new JObject { ["pid"] = Id1, ["kind"] = "video", ["size"] = 20 }, ["reason"] = "unplayable",
        };
        svc.ApplyBlock(BlockJson(HeldJson(), new JArray { skip }));
        svc.ApplyBlock(BlockJson(HeldJson(), new JArray { skip }));

        Assert.Equal(LeashEventKind.PunishSkipped, events.Single().Kind);
        Assert.Equal(1, changes);
        var it = svc.SentTo("u_pet").Single();
        Assert.Equal(LeashStep.Skipped, it.Step);
        Assert.Equal("unplayable", it.Reason);
        Assert.True(svc.ReceiptsSupported);
    }

    // ---- what the leashed side does with an event ------------------------------------

    [Fact]
    public void Only_what_was_on_screen_is_reported_seen()
    {
        // Not a receipt item: toast as always, report nothing.
        Assert.Equal((true, false, false), LeashUiRules.EventNotice(LeashEventKind.Answered, false, false));
        Assert.Equal((true, false, false), LeashUiRules.EventNotice(LeashEventKind.PunishSkipped, true, true));
        // The panel is up: the toast shows it.
        foreach (var k in new[] { LeashEventKind.Punish, LeashEventKind.Assign, LeashEventKind.Reward, LeashEventKind.Tug })
            Assert.Equal((true, true, false), LeashUiRules.EventNotice(k, true, true));
        // Only the launcher is up: a tug wobbles it, the rest wait for the panel.
        Assert.Equal((false, true, false), LeashUiRules.EventNotice(LeashEventKind.Tug, false, true));
        Assert.Equal((false, false, true), LeashUiRules.EventNotice(LeashEventKind.Punish, false, true));
        // Nothing is up: everything waits.
        Assert.Equal((false, false, true), LeashUiRules.EventNotice(LeashEventKind.Tug, false, false));
        Assert.Equal((false, false, true), LeashUiRules.EventNotice(LeashEventKind.Reward, false, false));
    }

    [Fact]
    public void Item_words_and_times_read_plainly()
    {
        Assert.Equal("leash_pun_lines 5", LeashUiRules.ItemText(Item(LeashItemKind.Punish) with { Punish = PunishKind.Lines, Size = 5 }, Id));
        Assert.Equal("leash_item_task_any", LeashUiRules.ItemText(Item(LeashItemKind.Assign), Id));
        Assert.Equal("leash_rew_sticker: leash_rew_sticker_star", LeashUiRules.ItemText(Item(LeashItemKind.Reward) with { Reward = RewardKind.Sticker, Token = "star" }, Id));
        Assert.Equal("leash_rew_praise: leash_praise_good", LeashUiRules.ItemText(Item(LeashItemKind.Reward) with { Reward = RewardKind.Praise, Token = "good" }, Id));
        Assert.Equal("leash_rew_credit -15:00", LeashUiRules.ItemText(Item(LeashItemKind.Reward) with { Reward = RewardKind.Credit, Size = 900 }, Id));
        Assert.Equal("leash_rew_pardon", LeashUiRules.ItemText(Item(LeashItemKind.Reward) with { Reward = RewardKind.Pardon }, Id));
        Assert.Equal("leash_btn_tug", LeashUiRules.ItemText(Item(LeashItemKind.Tug), Id));
        Assert.Equal("leash_item_offer", LeashUiRules.ItemText(Item(LeashItemKind.Offer), Id));
        Assert.Equal(3, LeashUiRules.Dots(LeashItemKind.Tug));
        Assert.Equal(4, LeashUiRules.Dots(LeashItemKind.Offer));

        var now = new DateTimeOffset(new DateTime(2026, 9, 29, 15, 0, 0, DateTimeKind.Local));
        Assert.Equal("14:55", LeashUiRules.StepTime(now.AddMinutes(-5), now));
        Assert.Equal("28.09 15:00", LeashUiRules.StepTime(now.AddDays(-1), now));
        var done = Item(LeashItemKind.Punish, now.AddMinutes(-10)) with
        {
            Step = LeashStep.Done, StepAt = now.AddMinutes(-1), ArrivedAt = now.AddMinutes(-9), SeenAt = now.AddMinutes(-8),
        };
        Assert.Equal("leash_step_sent 14:50  ·  leash_step_arrived 14:51  ·  leash_step_seen 14:52  ·  leash_step_done 14:59",
            LeashUiRules.Timeline(done, now, Id));
    }

    private static LeashSentItem Item(LeashItemKind kind, DateTimeOffset? at = null) =>
        new("k", kind, "u_pet", null, LeashStep.Sent, at ?? T0, at ?? T0);
}

/// <summary>The cards that draw receipts, and the surfaces that report <c>seen</c>.</summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class LeashReceiptsUiTests
{
    private static FrameworkElement? Find(DependencyObject root, Func<string, bool> tag)
    {
        if (root is FrameworkElement fe && fe.Tag is string s && tag(s)) return fe;
        foreach (var c in LogicalTreeHelper.GetChildren(root))
            if (c is DependencyObject d && Find(d, tag) is { } hit) return hit;
        return null;
    }

    private static List<string> Tags(DependencyObject root, string prefix)
    {
        var list = new List<string>();
        void Walk(DependencyObject d)
        {
            if (d is FrameworkElement fe && fe.Tag is string s && s.StartsWith(prefix, StringComparison.Ordinal)) list.Add(s);
            foreach (var c in LogicalTreeHelper.GetChildren(d)) if (c is DependencyObject x) Walk(x);
        }
        Walk(root);
        return list;
    }

    private static void Run(Action body) => WpfRenderHarness.OnStaThread(() =>
    {
        LeashFx.ForceStill = true;
        try { body(); } finally { LeashFx.ForceStill = false; }
    });

    [Fact]
    public void Holder_card_draws_no_steps_for_a_server_without_receipts()
    {
        Run(() =>
        {
            var svc = FakeLeashService.Sample();
            svc.Sent[FakeLeashService.Mika.Id] = FakeLeashService.SampleSent();
            var card = new LeashHolderCard(svc.Snapshot.Holding[0], () => svc);
            Assert.Null(Find(card, t => t == "leash-sent"));
            Assert.Empty(Tags(card, "leash-step-dot:"));
        });
    }

    [Fact]
    public void Holder_card_lists_what_was_sent_and_how_far_it_got()
    {
        Run(() =>
        {
            var svc = FakeLeashService.Sample().WithSampleReceipts();
            var section = new LeashDrawerSection(() => svc);
            var card = section.HolderCards.Single();
            Assert.NotNull(Find(card, t => t == "leash-sent"));
            Assert.Equal(
                new[] { "leash-sent-row:tug:seen", "leash-sent-row:punish:arrived", "leash-sent-row:punish:skipped", "leash-sent-row:reward:refused" },
                Tags(card, "leash-sent-row:"));
            Assert.Equal(
                new[] { "on", "on", "on", "on", "on", "off", "off", "on", "on", "on", "on", "off", "off", "off" },
                Tags(card, "leash-step-dot:").Select(t => t.Split(':')[2]));

            // A receipt lands: the card repaints in place.
            var sent = svc.Sent[FakeLeashService.Mika.Id];
            sent[1] = sent[1] with { Step = LeashStep.Seen, SeenAt = DateTimeOffset.UtcNow };
            svc.RaiseReceipts();
            Assert.Contains("leash-sent-row:punish:seen", Tags(section.HolderCards.Single(), "leash-sent-row:"));
        });
    }

    [Fact]
    public void The_gate_reports_a_punishment_seen_once_when_it_is_presented()
    {
        Run(() =>
        {
            var svc = FakeLeashService.Sample();
            LeashLocator.Service = () => svc;
            try
            {
                var gate = new LeashGateCard();
                gate.Present(FakeLeashService.SamplePunishment(), pardons: 0);
                gate.Present(FakeLeashService.SamplePunishment(), pardons: 0);
                Assert.Equal(1, svc.Calls.Count(c => c == "seen:p1"));
            }
            finally { LeashLocator.Service = () => null; }
        });
    }

    [Fact]
    public void The_drawer_reports_the_offer_and_the_task_it_shows()
    {
        Run(() =>
        {
            var svc = FakeLeashService.Sample();
            var section = new LeashDrawerSection(() => svc);
            // Not on screen yet (no window): nothing reported.
            Assert.DoesNotContain(svc.Calls, c => c.StartsWith("seen:", StringComparison.Ordinal));
            section.NoteOnScreen();
            Assert.Contains("seen:" + FakeLeashService.SampleOfferId, svc.Calls);
            Assert.Contains("seen:a1", svc.Calls);
        });
    }
}
