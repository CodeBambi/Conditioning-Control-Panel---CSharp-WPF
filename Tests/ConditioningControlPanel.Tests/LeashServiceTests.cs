using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.Leash;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The leash service over a fake wire: the block, the events, the tab, the cut, the ops.</summary>
public class LeashServiceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 26, 10, 0, 0, TimeSpan.Zero);

    private sealed class FakeApi : ILeashApi
    {
        public List<(string Op, JObject Body)> Calls = new();
        public Dictionary<string, Queue<JObject?>> Answers = new();
        public JObject? Default = new() { ["ok"] = true };

        public void Answer(string op, JObject? reply)
        {
            if (!Answers.TryGetValue(op, out var q)) Answers[op] = q = new Queue<JObject?>();
            q.Enqueue(reply);
        }

        public Task<JObject?> CallAsync(string op, JObject body, CancellationToken ct = default)
        {
            Calls.Add((op, body));
            if (Answers.TryGetValue(op, out var q) && q.Count > 0) return Task.FromResult(q.Dequeue());
            return Task.FromResult(Default);
        }

        public int Count(string op) => Calls.Count(c => c.Op == op);
    }

    private sealed class FakeTab : ILeashTab
    {
        public List<int> Punished = new();
        public List<int> Credited = new();
        public int BookPunish(int seconds) { Punished.Add(seconds); return seconds; }
        public int BookCredit(int seconds) { Credited.Add(seconds); return -seconds; }
    }

    private sealed class Store : ILeashCutStore
    {
        public string? V;
        public string? Read() => V;
        public void Write(string? accountId) => V = accountId;
    }

    private sealed class Rig
    {
        public FakeApi Api = new();
        public FakeTab Tab = new();
        public Store Store = new();
        public string? Account = "u_me";
        public int Kicks;
        public int Safety;
        public LeashDayInputs? Inputs = new LeashDayInputs(new DateTime(2026, 9, 26, 12, 0, 0), 20, 1, 3, 4, false, null, false, null);
        public LeashService Svc = null!;

        public Rig Build()
        {
            Svc = new LeashService(Api, () => Account, () => T0, () => Inputs, Tab, () => Kicks++, Store, () => Safety++);
            return this;
        }
    }

    private static JObject P(string id, string name) => new() { ["id"] = id, ["name"] = name, ["avatar"] = null };

    private static JObject Pun(string pid, string kind, int size, DateTimeOffset at) => new()
    {
        ["pid"] = pid, ["kind"] = kind, ["size"] = size, ["from"] = P("u_vex", "Vex"),
        ["at"] = at.ToString("o"), ["expires_at"] = at.AddHours(72).ToString("o"),
    };

    private static JObject Me(string intensity = "strict", JArray? pending = null, JObject? assignment = null) => new()
    {
        ["holder"] = P("u_vex", "Vex"), ["intensity"] = intensity, ["since"] = T0.AddDays(-2).ToString("o"), ["day"] = 3,
        ["dnd_until"] = null, ["remote_mode"] = "ask", ["pending"] = pending ?? new JArray(),
        ["assignment"] = assignment, ["pardons"] = 1, ["stickers"] = new JArray(),
    };

    private static JObject Block(JObject? me = null, JArray? events = null, JArray? holding = null) => new()
    {
        ["me"] = me, ["holding"] = holding ?? new JArray(), ["offers"] = new JArray(), ["events"] = events ?? new JArray(),
    };

    private static JObject Ev(string id, string kind, JObject? extra = null)
    {
        var o = new JObject { ["id"] = id, ["kind"] = kind, ["from"] = P("u_vex", "Vex"), ["at"] = T0.ToString("o") };
        if (extra != null) foreach (var p in extra.Properties()) o[p.Name] = p.Value;
        return o;
    }

    [Fact]
    public void Block_parses_and_events_arrive_once_in_order()
    {
        var r = new Rig().Build();
        var snaps = 0;
        var events = new List<LeashEvent>();
        r.Svc.SnapshotChanged += _ => snaps++;
        r.Svc.EventArrived += events.Add;
        var evs = new JArray { Ev("e2", "tug"), Ev("e1", "reward", new JObject { ["reward"] = "sticker", ["sticker"] = "star" }) };
        evs[0]!["at"] = T0.AddSeconds(5).ToString("o");
        var block = Block(Me(pending: new JArray { Pun("p1", "lines", 3, T0.AddHours(-1)) }), evs);

        r.Svc.ApplyBlock(block);
        r.Svc.ApplyBlock(block);

        Assert.Equal(1, snaps);
        Assert.Equal(new[] { "e1", "e2" }, events.Select(e => e.Id));
        Assert.Equal("star", events[0].StickerOrPoke);
        Assert.True(r.Svc.Active);
        Assert.Equal("Vex", r.Svc.Snapshot.Me!.Holder.Name);
        Assert.Equal(3, r.Svc.Snapshot.Me.Day);
        Assert.Equal("p1", r.Svc.GateDue!.Pid);

        r.Svc.ApplyBlock(null);
        Assert.False(r.Svc.Active);
        Assert.Null(r.Svc.GateDue);
    }

    [Fact]
    public void Report_rides_only_while_leashed()
    {
        var r = new Rig().Build();
        Assert.Null(r.Svc.BuildReportJson());
        r.Svc.ApplyBlock(Block(Me(assignment: new JObject
        {
            ["aid"] = "a1", ["kind"] = "minutes", ["size"] = 15, ["day"] = "20260926", ["status"] = "open", ["at"] = T0.ToString("o"),
        })));
        var rep = r.Svc.BuildReportJson();
        Assert.NotNull(rep);
        Assert.Equal("20260926", (string?)rep!["day"]);
        Assert.Equal(20, (int)rep["minutes"]!);
        Assert.True((bool)rep["assign_done"]!);
        Assert.False((bool)rep["chaster_linked"]!);
    }

    [Fact]
    public void Chaster_punishment_books_once_at_strict_and_completes_itself()
    {
        var r = new Rig().Build();
        r.Svc.ApplyBlock(Block(Me("strict")));
        var ev = new JArray { Ev("e1", "punish", new JObject { ["punishment"] = Pun("pc", "chaster", 1800, T0) }) };
        r.Svc.ApplyBlock(Block(Me("strict"), ev));
        r.Svc.ApplyBlock(Block(Me("strict"), ev));
        Assert.Equal(new[] { 1800 }, r.Tab.Punished);
        Assert.Equal(1, r.Api.Count("complete"));
        Assert.Equal("pc", (string?)r.Api.Calls.Single(c => c.Op == "complete").Body["pid"]);
    }

    [Fact]
    public void Chaster_punishment_below_strict_books_nothing_but_still_completes()
    {
        var r = new Rig().Build();
        r.Svc.ApplyBlock(Block(Me("standard")));
        r.Svc.ApplyBlock(Block(Me("standard"), new JArray { Ev("e1", "punish", new JObject { ["punishment"] = Pun("pc", "chaster", 900, T0) }) }));
        Assert.Empty(r.Tab.Punished);
        Assert.Equal(1, r.Api.Count("complete"));
    }

    [Fact]
    public void Credit_books_only_from_my_own_holder()
    {
        var r = new Rig().Build();
        r.Svc.ApplyBlock(Block(Me()));
        var mine = Ev("e1", "reward", new JObject { ["reward"] = "credit", ["size"] = 900 });
        var stranger = Ev("e2", "reward", new JObject { ["reward"] = "credit", ["size"] = 1800 });
        stranger["from"] = P("u_other", "Other");
        r.Svc.ApplyBlock(Block(Me(), new JArray { mine, stranger }));
        Assert.Equal(new[] { 900 }, r.Tab.Credited);
    }

    [Fact]
    public async Task Cut_is_local_first_and_retried_until_the_server_has_it()
    {
        var r = new Rig().Build();
        var flips = new List<bool>();
        r.Svc.LeashedChanged += flips.Add;
        r.Svc.ApplyBlock(Block(Me()));
        r.Api.Answer("cut", null);                         // offline

        await r.Svc.CutAsync();

        Assert.Equal(1, r.Safety);
        Assert.Null(r.Svc.Snapshot.Me);
        Assert.Equal(new[] { true, false }, flips);
        Assert.True(r.Svc.CutPending);
        Assert.Equal("u_me", r.Store.V);
        Assert.Null(r.Svc.BuildReportJson());

        // The server still says leashed: stays cut here, and the cut goes again.
        r.Svc.ApplyBlock(Block(Me()));
        Assert.Null(r.Svc.Snapshot.Me);
        Assert.Equal(2, r.Api.Count("cut"));
        Assert.False(r.Svc.CutPending);

        // A restart with the flag still stored remembers it.
        var r2 = new Rig { Store = new Store { V = "u_me" } }.Build();
        Assert.True(r2.Svc.CutPending);
        r2.Svc.ApplyBlock(Block(Me()));
        Assert.Null(r2.Svc.Snapshot.Me);
        r2.Svc.ApplyBlock(Block());
        Assert.False(r2.Svc.CutPending);
        Assert.Null(r2.Store.V);
    }

    [Fact]
    public async Task Cut_works_signed_in_with_nothing_to_cut()
    {
        var r = new Rig().Build();
        await r.Svc.CutAsync();
        Assert.Equal(1, r.Safety);
        Assert.Equal(1, r.Api.Count("cut"));
        Assert.False(r.Svc.CutPending);
    }

    [Fact]
    public async Task Complete_hides_the_gate_and_is_resent_until_it_lands()
    {
        var r = new Rig().Build();
        var pending = new JArray { Pun("p1", "lines", 3, T0.AddHours(-2)), Pun("p2", "pink", 10, T0.AddHours(-1)) };
        r.Svc.ApplyBlock(Block(Me(pending: pending)));
        r.Api.Answer("complete", null);
        await r.Svc.CompleteAsync("p1");
        Assert.Equal("p2", r.Svc.GateDue!.Pid);
        Assert.DoesNotContain(r.Svc.Snapshot.Me!.Pending, p => p.Pid == "p1");

        r.Svc.ApplyBlock(Block(Me(pending: pending)));
        Assert.Equal(2, r.Api.Count("complete"));
        Assert.Equal("p2", r.Svc.GateDue!.Pid);

        r.Svc.ApplyBlock(Block(Me(pending: new JArray { pending[1]!.DeepClone() })));
        Assert.Equal(2, r.Api.Count("complete"));
    }

    [Fact]
    public async Task Holder_ops_word_every_reply_and_never_send_what_the_grammar_refuses()
    {
        var r = new Rig().Build();
        var held = new JObject
        {
            ["who"] = P("u_pet", "Pet"), ["online"] = true, ["intensity"] = "soft", ["since"] = T0.ToString("o"), ["day"] = 1,
            ["dnd_until"] = null, ["report"] = null, ["week"] = new JArray(), ["pending"] = new JArray(), ["assignment"] = null,
            ["punished_today"] = 0,
        };
        r.Svc.ApplyBlock(Block(holding: new JArray { held }));
        Assert.True(r.Svc.Active);

        Assert.Equal(LeashSendStatus.NotAllowed, (await r.Svc.PunishAsync("u_pet", PunishKind.Bubbles, 50)).Status);
        Assert.Equal(LeashSendStatus.Refused, (await r.Svc.PunishAsync("u_pet", PunishKind.Lines, 4)).Status);
        Assert.Equal(LeashSendStatus.Refused, (await r.Svc.RewardAsync("u_pet", RewardKind.Sticker, "anything")).Status);
        Assert.Empty(r.Api.Calls);

        r.Api.Answer("punish", new JObject { ["ok"] = true, ["status"] = "queued" });
        Assert.Equal(LeashSendStatus.Queued, (await r.Svc.PunishAsync("u_pet", PunishKind.Lines, 5)).Status);
        var body = r.Api.Calls.Last().Body;
        Assert.Equal("lines", (string?)body["kind"]);
        Assert.Equal(5, (int)body["size"]!);

        r.Api.Answer("tug", new JObject { ["ok"] = true, ["status"] = "dnd", ["dnd_until"] = T0.AddHours(1).ToString("o") });
        var tug = await r.Svc.TugAsync("u_pet");
        Assert.Equal(LeashSendStatus.Dnd, tug.Status);
        Assert.Equal(T0.AddHours(1), tug.DndUntil);

        r.Api.Answer("offer", new JObject { ["ok"] = false, ["reason"] = "bad_input" });
        Assert.Equal(LeashSendStatus.Refused, (await r.Svc.OfferAsync("u_x")).Status);
        r.Api.Answer("offer", null);
        Assert.Equal(LeashSendStatus.Failed, (await r.Svc.OfferAsync("u_x")).Status);

        r.Api.Answer("assign", new JObject { ["ok"] = false, ["reason"] = "off" });
        Assert.Equal(LeashSendStatus.Off, (await r.Svc.AssignAsync("u_pet", AssignKind.Minutes, 15)).Status);
        Assert.False(r.Svc.Available);
    }

    [Fact]
    public async Task Settings_apply_the_returned_me_and_dnd_today_carries_its_end()
    {
        var r = new Rig().Build();
        r.Svc.ApplyBlock(Block(Me("strict")));
        r.Api.Answer("settings", new JObject { ["ok"] = true, ["me"] = Me("soft") });
        await r.Svc.SetIntensityAsync(LeashIntensity.Soft);
        Assert.Equal(LeashIntensity.Soft, r.Svc.Snapshot.Me!.Intensity);
        Assert.Equal("soft", (string?)r.Api.Calls.Last().Body["intensity"]);

        await r.Svc.SetDndAsync(LeashDnd.Today);
        var body = r.Api.Calls.Last().Body;
        Assert.Equal("today", (string?)body["dnd"]);
        Assert.NotNull(body["dnd_until"]);
        await r.Svc.SetDndAsync(LeashDnd.OneHour);
        Assert.Null(r.Api.Calls.Last().Body["dnd_until"]);
    }

    [Fact]
    public async Task Answer_sends_intensity_only_on_accept()
    {
        var r = new Rig().Build();
        r.Api.Answer("answer", new JObject { ["ok"] = true, ["status"] = "on" });
        Assert.True(await r.Svc.AnswerAsync("u_vex", true, LeashIntensity.Standard));
        Assert.Equal("standard", (string?)r.Api.Calls.Last().Body["intensity"]);
        await r.Svc.AnswerAsync("u_vex", false, LeashIntensity.Strict);
        Assert.Null(r.Api.Calls.Last().Body["intensity"]);
        Assert.False((bool)r.Api.Calls.Last().Body["accept"]!);
    }

    [Fact]
    public void A_new_account_forgets_everything()
    {
        var r = new Rig().Build();
        r.Svc.ApplyBlock(Block(Me()));
        r.Account = "u_other";
        r.Svc.ApplyBlock(null);
        Assert.Null(r.Svc.Snapshot.Me);
        r.Account = null;
        Assert.False(r.Svc.Available);
        Assert.Null(r.Svc.BuildReportJson());
    }

    [Fact]
    public void Offers_and_held_leashes_parse_and_bad_items_are_dropped()
    {
        var block = Block(null, new JArray
        {
            Ev("x1", "nonsense"),
            Ev("x2", "reward", new JObject { ["reward"] = "praise", ["poke"] = "type anything" }),
            Ev("x3", "answered", new JObject { ["accepted"] = true }),
        });
        block["offers"] = new JArray { new JObject { ["from"] = P("u_vex", "Vex"), ["at"] = T0.ToString("o"), ["expires_at"] = T0.AddDays(7).ToString("o") } };
        var (snap, events) = LeashParse.Block(block);
        Assert.Single(snap.Offers);
        Assert.Equal(new[] { "x3" }, events.Select(e => e.Id));
        Assert.True(events[0].Accepted);
        Assert.False(snap.Active);
    }
}

/// <summary>The friends poll carries the leash both ways, and runs at 20 s while the leash is on.</summary>
public class LeashPiggybackTests
{
    private sealed class Handler : HttpMessageHandler
    {
        public string? LastBody;
        public string Reply = "{}";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastBody = request.Content == null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Reply, Encoding.UTF8, "application/json") };
        }
    }

    [Fact]
    public async Task Poll_sends_the_report_and_reads_the_block()
    {
        var h = new Handler { Reply = "{\"ok\":true,\"online\":[],\"inbox\":[],\"leash\":{\"me\":null,\"holding\":[],\"offers\":[],\"events\":[]}}" };
        var api = new FriendsApi(new HttpClient(h), () => ("u_me", "tok"), "http://127.0.0.1:1");
        var report = new JObject { ["day"] = "20260926" };
        var r = await api.PollAsync(null, null, false, report);
        Assert.NotNull(r!.Leash);
        Assert.Equal("20260926", (string?)JObject.Parse(h.LastBody!)["leash_report"]!["day"]);

        h.Reply = "{\"ok\":true,\"online\":[],\"inbox\":[]}";
        r = await api.PollAsync(null, null, false);
        Assert.Null(r!.Leash);
        Assert.Null(JObject.Parse(h.LastBody!)["leash_report"]);
    }

    private sealed class PlainApi : IFriendsApi
    {
        public Task<FriendsSnapshot?> StateAsync(CancellationToken ct = default) => Task.FromResult<FriendsSnapshot?>(FriendsSnapshot.Empty);
        public Task<FriendsPollReply?> PollAsync(PresenceActivity? a, int? l, bool s, CancellationToken ct = default) =>
            Task.FromResult<FriendsPollReply?>(new FriendsPollReply(Array.Empty<string>(), Array.Empty<InboxItem>(), new JObject { ["me"] = null }));
        public Task<AddResult> RequestAsync(string code, CancellationToken ct = default) => Task.FromResult(AddResult.Sent);
        public Task<SendResult> SendAsync(string to, SendKind kind, string? poke, string? destination, string? code, WatchRef? watch,
            CancellationToken ct = default) => Task.FromResult(SendResult.Sent);
        public Task<bool> ActAsync(string op, string id, JObject? extra = null, CancellationToken ct = default) => Task.FromResult(true);
    }

    [Fact]
    public async Task Service_hands_over_the_block_and_speeds_up_while_active()
    {
        var svc = new FriendsService(new PlainApi(), () => "u_me", () => false, () => DateTimeOffset.UtcNow, () => false, _ => { });
        JObject? got = null;
        var arrived = 0;
        svc.LeashBlockArrived += b => { got = b; arrived++; };
        var asked = 0;
        svc.LeashReportProvider = () => { asked++; return null; };
        await svc.TickAsync();
        Assert.Equal(1, arrived);
        Assert.NotNull(got);
        Assert.Equal(1, asked);

        Assert.Equal(FriendsPollRule.SlowSeconds, svc.NextIntervalSeconds());
        var active = true;
        svc.LeashActive = () => active;
        Assert.Equal(LeashPollRule.FastSeconds, svc.NextIntervalSeconds());
        active = false;
        Assert.Equal(FriendsPollRule.SlowSeconds, svc.NextIntervalSeconds());
    }
}
