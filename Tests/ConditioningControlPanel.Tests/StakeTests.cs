using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Chaster;
using ConditioningControlPanel.Services.Stakes;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>PvP stakes: the table, the wallet rule on stake replies, and booking a lost time stake once.</summary>
public class StakeTests
{
    private const string Me = "u_me";

    private sealed class FakeApi : IStakeApi
    {
        public string? Who = Me;
        public List<(string Op, JObject Body)> Calls = new();
        public Queue<JObject?> Replies = new();
        public JObject? Default = new() { ["ok"] = true };

        public string? Account() => Who;

        public Task<JObject?> CallAsync(string op, JObject body, CancellationToken ct = default)
        {
            Calls.Add((op, body));
            return Task.FromResult(Replies.Count > 0 ? Replies.Dequeue() : Default);
        }
    }

    private sealed class Rig
    {
        public readonly List<(string Row, int Seconds)> Booked = new();
        public readonly List<(string Account, bool Receipt, int Sp)> Adopted = new();
        public string? SignedIn = Me;
        public int Refuse = -1;       // when set, the tab applies only this much
        public StakeSettlement Settlement;

        public Rig()
        {
            Settlement = new StakeSettlement(
                (row, s) => { Booked.Add((row, s)); return Refuse >= 0 ? Refuse : s; },
                (a, r, sp) => Adopted.Add((a, r, sp)),
                () => SignedIn);
        }
    }

    private static readonly Func<TimeSpan, CancellationToken, Task> NoWait = (_, _) => Task.CompletedTask;

    private static JObject Settled(string result, int spDelta, int timeS, int sp) => new()
    {
        ["ok"] = true,
        ["you"] = new JObject { ["kind"] = "time", ["amount"] = 1800 },
        ["them"] = null,
        ["locked"] = true,
        ["settled"] = new JObject { ["result"] = result, ["sp_delta"] = spDelta, ["time_s"] = timeS, ["sp"] = sp },
    };

    private static JObject Unsettled(int sp) => new() { ["ok"] = true, ["locked"] = true, ["settled"] = null, ["sp"] = sp };

    // ---------------------------------------------------------------- the table

    [Fact]
    public void The_table_offers_off_two_times_and_three_sparkle_sizes()
    {
        Assert.Equal(new[] { 900, 1800 }, StakeRules.TimeOptions);
        Assert.Equal(new[] { 5, 10, 25 }, StakeRules.SpOptions);
        Assert.True(StakeRules.IsValid(StakeKind.None, 0));
        Assert.True(StakeRules.IsValid(StakeKind.Time, 1800));
        Assert.False(StakeRules.IsValid(StakeKind.Time, 3600));
        Assert.False(StakeRules.IsValid(StakeKind.Time, 5));
        Assert.True(StakeRules.IsValid(StakeKind.Sp, 25));
        Assert.False(StakeRules.IsValid(StakeKind.Sp, 26));
        Assert.False(StakeRules.IsValid(StakeKind.Sp, -5));
    }

    [Fact]
    public void Normalise_reads_the_wire_and_refuses_what_is_off_the_table()
    {
        Assert.Equal(Stake.None, StakeRules.Normalise("none", 999));
        Assert.Equal(Stake.None, StakeRules.Normalise(null, 0));
        Assert.Equal(Stake.None, StakeRules.Normalise("banana", 5));
        Assert.Equal(new Stake(StakeKind.Time, 900), StakeRules.Normalise("TIME", 900));
        Assert.Equal(new Stake(StakeKind.Sp, 10), StakeRules.Normalise("sp", 10));
        Assert.Null(StakeRules.Normalise("sp", 11));
        Assert.Null(StakeRules.Normalise("time", 7200));
        Assert.Equal("sp", StakeRules.KindWire(StakeKind.Sp));
        Assert.Equal("time", StakeRules.KindWire(StakeKind.Time));
        Assert.Equal("none", StakeRules.KindWire(StakeKind.None));
    }

    [Fact]
    public void A_won_time_stake_pays_one_sparkle_per_three_minutes()
    {
        Assert.Equal(5, StakeRules.TimePrizeSp(900));
        Assert.Equal(10, StakeRules.TimePrizeSp(1800));
        Assert.Equal(10, StakeRules.TimePrizeSp(99999));
        Assert.Equal(0, StakeRules.TimePrizeSp(0));
        Assert.Equal(0, StakeRules.TimePrizeSp(-900));
    }

    [Fact]
    public void Time_is_offered_only_while_the_tab_could_book_it()
    {
        Assert.True(StakeRules.TimeAllowed(true, true, false, TimeSpan.Zero));
        Assert.False(StakeRules.TimeAllowed(false, true, false, TimeSpan.Zero));
        Assert.False(StakeRules.TimeAllowed(true, false, false, TimeSpan.Zero));
        Assert.False(StakeRules.TimeAllowed(true, true, true, TimeSpan.Zero));
        Assert.False(StakeRules.TimeAllowed(true, true, false, TimeSpan.FromMinutes(3)));
    }

    [Fact]
    public void The_loss_row_is_opt_in_free_and_never_a_way_out()
    {
        var row = TabPrices.Find(StakeRules.LossRowId);
        Assert.NotNull(row);
        Assert.Equal(TabPriceGate.Free, row!.Gate);
        Assert.True(row.Seconds > 0);
        Assert.Equal(0, TabPrices.Resolve(StakeRules.LossRowId, new HashSet<string>()));
        Assert.DoesNotContain(StakeRules.LossRowId, TabPrices.NeverPriced);
        Assert.Contains("panic", TabPrices.NeverPriced);
        Assert.Contains("emergency_exit", TabPrices.NeverPriced);
    }

    // ---------------------------------------------------------------- reading a reply

    [Fact]
    public void A_settled_reply_reads_and_only_a_loss_carries_time()
    {
        var lost = StakeSettlement.ReadSettled(Settled("lost", 0, 1800, 40))!;
        Assert.True(lost.Lost);
        Assert.Equal(1800, lost.TimeSeconds);
        Assert.Equal(40, lost.Sp);

        // A server that says time on a win, or more than the ceiling, is not believed.
        Assert.Equal(0, StakeSettlement.ReadSettled(Settled("won", 10, 1800, 50))!.TimeSeconds);
        Assert.Equal(StakeRules.MaxTimeSeconds, StakeSettlement.ReadSettled(Settled("lost", 0, 99999, 40))!.TimeSeconds);
        // Anything that is not won or lost is a void.
        Assert.True(StakeSettlement.ReadSettled(Settled("draw", 0, 0, 40))!.Void);

        Assert.Null(StakeSettlement.ReadSettled(Unsettled(40)));
        Assert.Null(StakeSettlement.ReadSettled(new JObject { ["ok"] = false, ["reason"] = "disabled" }));
        Assert.Null(StakeSettlement.ReadSettled(null));
    }

    [Fact]
    public void Only_an_offer_taken_or_a_settled_state_is_a_receipt()
    {
        Assert.True(StakeSettlement.IsReceipt("offer", new JObject { ["ok"] = true, ["sp"] = 10 }));
        Assert.False(StakeSettlement.IsReceipt("offer", new JObject { ["ok"] = false, ["reason"] = "insufficient_sp", ["sp"] = 3 }));
        Assert.True(StakeSettlement.IsReceipt("state", Settled("won", 10, 0, 50)));
        Assert.False(StakeSettlement.IsReceipt("state", Unsettled(50)));
        Assert.False(StakeSettlement.IsReceipt("limits", new JObject { ["ok"] = true, ["sp"] = 50 }));
    }

    // ---------------------------------------------------------------- the wallet rule

    [Fact]
    public void Balances_go_to_the_wallet_for_the_account_that_asked_marked_receipt_or_snapshot()
    {
        var rig = new Rig();
        rig.Settlement.AdoptBalance("limits", Me, new JObject { ["ok"] = true, ["sp"] = 70 });
        rig.Settlement.AdoptBalance("offer", Me, new JObject { ["ok"] = true, ["stake"] = new JObject(), ["sp"] = 60 });
        rig.Settlement.AdoptBalance("offer", Me, new JObject { ["ok"] = false, ["reason"] = "daily_cap", ["sp"] = 60 });
        // No account, no balance, a string balance: nothing.
        rig.Settlement.AdoptBalance("offer", null, new JObject { ["ok"] = true, ["sp"] = 60 });
        rig.Settlement.AdoptBalance("offer", Me, new JObject { ["ok"] = true });
        rig.Settlement.AdoptBalance("offer", Me, new JObject { ["ok"] = true, ["sp"] = "60" });

        Assert.Equal(new[] { (Me, false, 70), (Me, true, 60), (Me, false, 60) }, rig.Adopted);
    }

    [Fact]
    public void The_app_wallet_write_lowers_only_on_a_receipt_for_the_same_account()
    {
        // StakeSettlement hands (account, receipt, sp) to V2WalletAdoption.Decide; pin the two cases it must get right.
        Assert.True(ConditioningControlPanel.Services.Prizes.V2WalletAdoption.Decide(true, fromBuy: true, 40, 50, out var next));
        Assert.Equal(40, next);
        Assert.False(ConditioningControlPanel.Services.Prizes.V2WalletAdoption.Decide(true, fromBuy: false, 40, 50, out _));
        Assert.False(ConditioningControlPanel.Services.Prizes.V2WalletAdoption.Decide(false, fromBuy: true, 40, 50, out _));
    }

    // ---------------------------------------------------------------- settlement

    [Fact]
    public void A_lost_time_stake_books_once_on_the_loss_row()
    {
        var rig = new Rig();
        var first = rig.Settlement.Settle("pbp", "m1", Me, Settled("lost", 0, 1800, 40));
        var again = rig.Settlement.Settle("pbp", "m1", Me, Settled("lost", 0, 1800, 40));

        Assert.True(first.First);
        Assert.Equal(1800, first.BookedSeconds);
        Assert.False(again.First);
        Assert.Equal(0, again.BookedSeconds);
        Assert.Equal(new[] { (StakeRules.LossRowId, 1800) }, rig.Booked);
        // The balance is a receipt both times; the wallet rule makes the second a no-op.
        Assert.All(rig.Adopted, a => Assert.True(a.Receipt));
    }

    [Fact]
    public void Wins_voids_and_sparkle_losses_book_no_time()
    {
        var rig = new Rig();
        rig.Settlement.Settle("pbp", "a", Me, Settled("won", 10, 0, 60));
        rig.Settlement.Settle("pbp", "b", Me, Settled("void", 0, 0, 60));
        rig.Settlement.Settle("pbp", "c", Me, Settled("lost", -10, 0, 50));
        Assert.Empty(rig.Booked);
        Assert.Equal(3, rig.Adopted.Count);
    }

    [Fact]
    public void Nothing_books_for_an_account_that_is_no_longer_signed_in()
    {
        var rig = new Rig { SignedIn = "u_other" };
        var o = rig.Settlement.Settle("pbp", "m1", Me, Settled("lost", 0, 900, 40));
        Assert.Equal(0, o.BookedSeconds);
        Assert.Empty(rig.Booked);
    }

    [Fact]
    public void What_the_tab_refuses_is_not_owed()
    {
        var rig = new Rig { Refuse = 0 };
        var o = rig.Settlement.Settle("goon", "ABCD", Me, Settled("lost", 0, 1800, 40));
        Assert.Equal(0, o.BookedSeconds);
        Assert.True(o.First);
    }

    [Fact]
    public void An_unsettled_reply_does_nothing()
    {
        var rig = new Rig();
        var o = rig.Settlement.Settle("pbp", "m1", Me, Unsettled(40));
        Assert.Null(o.Result);
        Assert.Empty(rig.Booked);
        Assert.Empty(rig.Adopted);
    }

    [Fact]
    public async Task The_watch_polls_until_settled_then_books()
    {
        var rig = new Rig();
        var api = new FakeApi();
        api.Replies.Enqueue(null);
        api.Replies.Enqueue(Unsettled(40));
        api.Replies.Enqueue(Settled("lost", 0, 900, 40));

        var (reply, outcome) = await rig.Settlement.WatchAsync(api, "pbp", "m9", NoWait);

        Assert.NotNull(reply);
        Assert.Equal(900, outcome.BookedSeconds);
        Assert.Equal(3, api.Calls.Count);
        Assert.All(api.Calls, c => Assert.Equal("state", c.Op));
        Assert.Equal("m9", (string?)api.Calls[0].Body["match"]);
        Assert.Equal("pbp", (string?)api.Calls[0].Body["game"]);
    }

    [Fact]
    public async Task The_watch_gives_up_as_a_void_and_books_nothing()
    {
        var rig = new Rig();
        var api = new FakeApi { Default = Unsettled(40) };

        var (_, outcome) = await rig.Settlement.WatchAsync(api, "pbp", "m9", NoWait);

        Assert.Null(outcome.Result);
        Assert.Equal(StakeRules.SettlePollDelays.Count, api.Calls.Count);
        Assert.Empty(rig.Booked);
    }

    [Fact]
    public async Task The_watch_stops_on_a_worded_refusal_and_without_an_account()
    {
        var rig = new Rig();
        var api = new FakeApi { Default = new JObject { ["ok"] = false, ["reason"] = "disabled" } };
        await rig.Settlement.WatchAsync(api, "pbp", "m9", NoWait);
        Assert.Single(api.Calls);

        var nobody = new FakeApi { Who = null };
        await rig.Settlement.WatchAsync(nobody, "pbp", "m9", NoWait);
        Assert.Empty(nobody.Calls);
    }

    // ---------------------------------------------------------------- the bridge

    private sealed class BridgeRig
    {
        public readonly List<JObject> Posted = new();
        public readonly FakeApi Api = new();
        public readonly Rig Rig = new();
        public bool TimeOk = true;
        public int RowSwitched;
        public StakeBridge Bridge;

        public BridgeRig()
        {
            Bridge = new StakeBridge("pbp", Posted.Add, Api, Rig.Settlement, () => TimeOk, () => RowSwitched++,
                () => new JObject { ["title"] = "Stake" });
        }
    }

    [Fact]
    public async Task Limits_carry_whether_time_is_on_offer_and_the_labels()
    {
        var b = new BridgeRig();
        b.Api.Default = new JObject { ["ok"] = true, ["enabled"] = true, ["sp"] = 12 };
        b.TimeOk = false;
        await b.Bridge.Handle(new JObject { ["type"] = "stake-limits" });

        var o = Assert.Single(b.Posted);
        Assert.Equal("stake", (string?)o["type"]);
        Assert.Equal("limits", (string?)o["op"]);
        Assert.False((bool)o["time_ok"]!);
        Assert.Equal("Stake", (string?)o["labels"]!["title"]);
        Assert.Equal((Me, false, 12), Assert.Single(b.Rig.Adopted));
    }

    [Fact]
    public async Task A_time_offer_switches_the_loss_row_on_and_sends_the_stake()
    {
        var b = new BridgeRig();
        b.Api.Default = new JObject { ["ok"] = true, ["stake"] = new JObject { ["kind"] = "time", ["amount"] = 900 }, ["sp"] = 12 };
        await b.Bridge.Handle(new JObject { ["type"] = "stake-offer", ["match"] = "m1", ["kind"] = "time", ["amount"] = 900 });

        Assert.Equal(1, b.RowSwitched);
        var call = Assert.Single(b.Api.Calls);
        Assert.Equal("offer", call.Op);
        Assert.Equal("pbp", (string?)call.Body["game"]);
        Assert.Equal("time", (string?)call.Body["kind"]);
        Assert.Equal(900, (int)call.Body["amount"]!);
        Assert.False(string.IsNullOrEmpty((string?)call.Body["idem"]));
        Assert.Equal("offer", (string?)b.Posted.Single()["op"]);
    }

    [Fact]
    public async Task Offers_off_the_table_or_time_without_chaster_never_reach_the_server()
    {
        var b = new BridgeRig { TimeOk = false };
        await b.Bridge.Handle(new JObject { ["type"] = "stake-offer", ["match"] = "m1", ["kind"] = "time", ["amount"] = 900 });
        await b.Bridge.Handle(new JObject { ["type"] = "stake-offer", ["match"] = "m1", ["kind"] = "sp", ["amount"] = 7 });
        await b.Bridge.Handle(new JObject { ["type"] = "stake-offer", ["match"] = "has space", ["kind"] = "sp", ["amount"] = 5 });

        Assert.Empty(b.Api.Calls);
        Assert.Equal(0, b.RowSwitched);
        Assert.Equal(new[] { "no_chaster", "bad_amount", "no_match" }, b.Posted.Select(p => (string?)p["reason"]));
    }

    [Fact]
    public async Task A_state_that_has_settled_books_and_says_how_much_landed()
    {
        var b = new BridgeRig();
        b.Api.Default = Settled("lost", 0, 1800, 40);
        await b.Bridge.Handle(new JObject { ["type"] = "stake-state", ["match"] = "m1" });
        await b.Bridge.Handle(new JObject { ["type"] = "stake-state", ["match"] = "m1" });

        Assert.Single(b.Rig.Booked);
        Assert.Equal(1800, (int)b.Posted[0]["booked_s"]!);
        Assert.Equal(0, (int)b.Posted[1]["booked_s"]!);
    }

    [Fact]
    public async Task No_reply_reads_as_offline_or_signin()
    {
        var b = new BridgeRig();
        b.Api.Default = null;
        await b.Bridge.Handle(new JObject { ["type"] = "stake-state", ["match"] = "m1" });
        b.Api.Who = null;
        await b.Bridge.Handle(new JObject { ["type"] = "stake-limits" });
        Assert.Equal(new[] { "offline", "signin" }, b.Posted.Select(p => (string?)p["reason"]));
    }
}
