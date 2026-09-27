using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.BackRoom;
using ConditioningControlPanel.Services.Prizes;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Buying a v2 prize from the options panel: the counter is read once and lazily, a buy is one
/// request with one idem key however many times the button is pressed, and every refusal turns into
/// a line the panel can show.
/// </summary>
public class V2PurchaseServiceTests
{
    private sealed record Call(string Station, string Op, string? Idem, JObject? Body);

    /// <summary>
    /// A fake relay. The call list is written from the service's own worker threads, so every touch
    /// is locked: an unlocked List.Add from two tasks drops entries and the miscount reads as a
    /// timing bug hours later.
    /// </summary>
    private sealed class FakeRelay : IBackRoomRelay
    {
        private readonly object _gate = new();
        private readonly List<Call> _calls = new();

        public BackRoomStationResult StateResult = StateBody();
        public BackRoomStationResult BuyResult =
            new(true, 200, null, new JObject { ["ok"] = true, ["sp"] = 70 });
        /// <summary>Set to hold a buy open, so a second press can be sent while the first is out.</summary>
        public TaskCompletionSource<bool>? HoldBuy;
        /// <summary>Set to hold a counter read open, so a press can land on top of one.</summary>
        public TaskCompletionSource<bool>? HoldState;

        public IReadOnlyList<Call> Calls { get { lock (_gate) return _calls.ToArray(); } }
        public int CountOf(string op) => Calls.Count(c => c.Op == op);

        public async Task<BackRoomStationResult> RelayAsync(string station, string op, string? idem,
            JObject? body, CancellationToken ct = default)
        {
            lock (_gate) _calls.Add(new Call(station, op, idem, body));
            if (op == "buy" && HoldBuy != null) await HoldBuy.Task.ConfigureAwait(false);
            if (op == "state" && HoldState != null) await HoldState.Task.ConfigureAwait(false);
            return op == "buy" ? BuyResult : StateResult;
        }

    }

    /// <summary>A GET counter/state body, 10.17.C shape.</summary>
    private static BackRoomStationResult StateBody(int catalogVersion = 2, int flashesPrice = 30, int sp = 100) =>
        new(true, 200, null, new JObject
        {
            ["ok"] = true,
            ["open"] = true,
            ["catalogVersion"] = catalogVersion,
            ["sp"] = sp,
            ["catalog"] = new JArray
            {
                new JObject { ["id"] = "flashes_v2", ["priceSp"] = flashesPrice, ["sale"] = "on" },
                new JObject { ["id"] = "bubbles_v2", ["priceSp"] = 30, ["sale"] = "on" },
                new JObject { ["id"] = "rt_bundle_1", ["priceSp"] = 1200, ["sale"] = "soon" },
            },
        });

    private static BackRoomStationResult Refuse(string reason, int status = 200) =>
        new(false, status, reason, new JObject { ["ok"] = false, ["reason"] = reason });

    private sealed class Harness
    {
        public readonly FakeRelay Relay = new();
        public readonly V2PurchaseService Service;
        public readonly HashSet<string> Owned = new(StringComparer.Ordinal);
        public string? Account = "u_one";
        public int Sp = 100;
        public int Changes;
        public readonly List<string> Bought = new();

        /// <summary>Every (account, fromBuy, sp) the service handed on, locked: two tasks write it.</summary>
        public readonly List<(string Account, bool FromBuy, int Sp)> Adopted = new();

        public Harness()
        {
            Service = new V2PurchaseService(Relay, () => Account, id => Owned.Contains(id), () => Sp,
                (account, fromBuy, sp) => { lock (Adopted) Adopted.Add((account, fromBuy, sp)); });
            Service.Changed += () => Interlocked.Increment(ref Changes);
            Service.Bought += id => { lock (Bought) Bought.Add(id); };
        }

        public (string Account, bool FromBuy, int Sp)[] AdoptedCalls { get { lock (Adopted) return Adopted.ToArray(); } }

        /// <summary>
        /// Press the button: buy at the price the row is SHOWING, which is what the control does
        /// after its confirm. A test that wants a mismatch calls BuyAsync itself.
        /// </summary>
        public Task<bool> Buy(string prizeId) => Service.BuyAsync(prizeId, Service.RowFor(prizeId).PriceSp);

        /// <summary>Read the counter and wait for it, the way the row's first appearance does.</summary>
        public async Task ReadCounterAsync()
        {
            Service.EnsureState();
            await WaitFor(() => Service.RowFor(Flashes).State != V2PurchaseRowState.Loading);
        }

        /// <summary>
        /// Wait for the service to reach a state. The budget is generous on purpose: this runs
        /// alongside the whole suite, where a Task.Run can sit in the pool for a while, and a short
        /// budget turns into a test that is only red on a loaded machine.
        /// </summary>
        public static async Task WaitFor(Func<bool> done)
        {
            for (var i = 0; i < 800 && !done(); i++) await Task.Delay(10);
            Assert.True(done(), "the service never settled");
        }
    }

    private const string Flashes = "flashes_v2";
    private const string Bubbles = "bubbles_v2";

    [Fact]
    public async Task TheCounterIsReadOnce_AndOnlyWhenARowAsks()
    {
        var h = new Harness();
        Assert.Empty(h.Relay.Calls);                      // nothing at construction: no startup traffic

        await h.ReadCounterAsync();
        Assert.Equal(1, h.Relay.CountOf("state"));
        Assert.Equal("counter", h.Relay.Calls[0].Station);
        Assert.Null(h.Relay.Calls[0].Idem);                // a GET carries none

        h.Service.EnsureState();
        h.Service.EnsureState();
        await Task.Delay(30);
        Assert.Equal(1, h.Relay.CountOf("state"));         // cached for the session
    }

    [Fact]
    public async Task SignedOut_NothingIsSentAtAll()
    {
        var h = new Harness { Account = null };
        h.Service.EnsureState();
        await Task.Delay(30);
        Assert.Empty(h.Relay.Calls);
        Assert.Equal(V2PurchaseRowState.SignIn, h.Service.RowFor(Flashes).State);
    }

    [Fact]
    public async Task ACounterReadForOneAccountIsNotKnowledgeAboutTheNext()
    {
        var h = new Harness();
        await h.ReadCounterAsync();
        Assert.Equal(V2PurchaseRowState.Offer, h.Service.RowFor(Flashes).State);

        // The Back Room door is per account (BACKROOM_TESTERS), so a cache from the last sign-in
        // must not answer for whoever signs in now.
        h.Account = "u_two";
        Assert.Equal(V2PurchaseRowState.Loading, h.Service.RowFor(Flashes).State);

        await h.ReadCounterAsync();
        Assert.Equal(2, h.Relay.CountOf("state"));
        Assert.Equal(V2PurchaseRowState.Offer, h.Service.RowFor(Flashes).State);
    }

    [Fact]
    public async Task ThePriceAndTheSaleComeFromTheCounter_NeverFromTheClient()
    {
        var h = new Harness();
        await h.ReadCounterAsync();

        var row = h.Service.RowFor(Flashes);
        Assert.Equal(V2PurchaseRowState.Offer, row.State);
        Assert.Equal(30, row.PriceSp);
        // A row the deploy is not selling reads unavailable rather than being offered at its price.
        Assert.Equal(V2PurchaseRowState.Unavailable, h.Service.RowFor("rt_bundle_1").State);
        // A row the counter never listed is unavailable too, not a zero-price giveaway.
        Assert.Equal(V2PurchaseRowState.Unavailable, h.Service.RowFor("not_on_the_shelf").State);
    }

    [Fact]
    public async Task ABuyIsOneRequest_CarryingThePrizeAndTheVersionTheCounterNamed()
    {
        var h = new Harness();
        await h.ReadCounterAsync();

        Assert.True(await h.Buy(Flashes));

        var buy = h.Relay.Calls.Single(c => c.Op == "buy");
        Assert.Equal("counter", buy.Station);
        Assert.Equal(Flashes, buy.Body?.Value<string>("prizeId"));
        Assert.Equal(2, buy.Body?.Value<int>("catalogVersion"));
        Assert.Matches("^[A-Za-z0-9_-]{16,64}$", buy.Idem!);
        Assert.Contains(Flashes, h.Bought);
    }

    [Fact]
    public async Task ASecondPressWhileTheFirstIsOutSendsNothing()
    {
        var h = new Harness();
        await h.ReadCounterAsync();
        h.Relay.HoldBuy = new TaskCompletionSource<bool>();

        var first = h.Buy(Flashes);
        await Harness.WaitFor(() => h.Service.RowFor(Flashes).State == V2PurchaseRowState.Busy);

        Assert.False(await h.Buy(Flashes));   // the row is already someone's
        h.Relay.HoldBuy!.SetResult(true);
        Assert.True(await first);

        Assert.Equal(1, h.Relay.CountOf("buy"));
    }

    [Fact]
    public async Task ARetryAfterAFailureReusesTheSameIdem_SoAReceiptCanReplayInsteadOfDebitingTwice()
    {
        var h = new Harness();
        await h.ReadCounterAsync();

        h.Relay.BuyResult = Refuse("offline", 0);
        Assert.False(await h.Buy(Flashes));
        Assert.Equal("v2_get_error_offline", h.Service.RowFor(Flashes).MessageKey);

        h.Relay.BuyResult = new BackRoomStationResult(true, 200, null, new JObject { ["ok"] = true });
        Assert.True(await h.Buy(Flashes));

        var idems = h.Relay.Calls.Where(c => c.Op == "buy").Select(c => c.Idem).Distinct().ToList();
        Assert.Single(idems);
    }

    [Fact]
    public async Task TwoPrizesNeverShareAnIdem()
    {
        var h = new Harness();
        await h.ReadCounterAsync();
        await h.Buy(Flashes);
        await h.Buy(Bubbles);

        var idems = h.Relay.Calls.Where(c => c.Op == "buy").Select(c => c.Idem).ToList();
        Assert.Equal(2, idems.Count);
        Assert.NotEqual(idems[0], idems[1]);
    }

    [Fact]
    public async Task ShortOfThePrice_TheServersWordBecomesTheRowsLine()
    {
        var h = new Harness { Sp = 4 };
        await h.ReadCounterAsync();
        Assert.Equal(V2PurchaseRowState.CannotAfford, h.Service.RowFor(Flashes).State);

        // The row will not let a press through, but the server is the one that decides: a stale
        // balance that looks affordable still gets the honest answer back.
        h.Sp = 100;
        h.Relay.BuyResult = Refuse("insufficient");
        Assert.False(await h.Buy(Flashes));
        Assert.Equal("v2_get_error_sp", h.Service.RowFor(Flashes).MessageKey);
    }

    [Fact]
    public async Task AShutDoorRefusesWithoutBlamingThePlayer()
    {
        var h = new Harness();
        await h.ReadCounterAsync();
        h.Relay.BuyResult = Refuse("closed", 403);

        Assert.False(await h.Buy(Flashes));
        Assert.Equal("v2_get_unavailable", h.Service.RowFor(Flashes).MessageKey);
    }

    [Fact]
    public async Task AnUndeployedRouteIsUnavailable_NotAnErrorThePlayerCanRetryForever()
    {
        var h = new Harness();
        await h.ReadCounterAsync();
        h.Relay.BuyResult = new BackRoomStationResult(false, 404, "bad_op", new JObject { ["ok"] = false });

        Assert.False(await h.Buy(Flashes));
        Assert.Equal("v2_get_unavailable", h.Service.RowFor(Flashes).MessageKey);
    }

    [Fact]
    public async Task ARelayThatThrowsIsStillAnAnswer_AndTheRowIsNotLeftBusy()
    {
        var h = new Harness();
        await h.ReadCounterAsync();
        h.Relay.HoldBuy = new TaskCompletionSource<bool>();
        h.Relay.HoldBuy.SetException(new InvalidOperationException("socket gone"));

        Assert.False(await h.Buy(Flashes));
        var row = h.Service.RowFor(Flashes);
        Assert.Equal(V2PurchaseRowState.Failed, row.State);
        Assert.Equal("v2_get_error_generic", row.MessageKey);
    }

    [Fact]
    public async Task ACounterThatNeverAnsweredLeavesTheRowLooking_AndABuyRefusesRatherThanGuessAVersion()
    {
        var h = new Harness();
        h.Relay.StateResult = Refuse("offline", 0);
        h.Service.EnsureState();
        await Task.Delay(40);

        Assert.Equal(V2PurchaseRowState.Loading, h.Service.RowFor(Flashes).State);
        // A confirmed price with no counter behind it: the re-read fails too, so nothing is sent.
        Assert.False(await h.Service.BuyAsync(Flashes, 30));
        Assert.Equal("v2_get_error_offline", h.Service.RowFor(Flashes).MessageKey);
        Assert.Equal(0, h.Relay.CountOf("buy"));   // no version, so nothing was sent
    }

    [Fact]
    public async Task ARowWithNoPriceCannotBeBoughtAtAll()
    {
        var h = new Harness();
        await h.ReadCounterAsync();

        // The rule already refuses the press; this is the service refusing the same thing, because
        // a confirm that says "Spend 0 Sparkle Points" must never reach the counter.
        Assert.False(await h.Service.BuyAsync(Flashes, 0));
        Assert.False(await h.Service.BuyAsync(Flashes, -5));
        Assert.Equal(0, h.Relay.CountOf("buy"));
    }

    [Fact]
    public async Task APriceThatMovedBetweenTheConfirmAndTheRequestRefusesInsteadOfCharging()
    {
        var h = new Harness();
        await h.ReadCounterAsync();
        Assert.Equal(30, h.Service.RowFor(Flashes).PriceSp);

        // The user confirmed 30. The counter is re-read (a reprice cleared the version) and now
        // says 90: the press is spent, not honoured at the new number.
        h.Relay.StateResult = StateBody(catalogVersion: 3, flashesPrice: 90);
        h.Service.Invalidate();
        Assert.False(await h.Service.BuyAsync(Flashes, 30));
        Assert.Equal(0, h.Relay.CountOf("buy"));
        Assert.Equal("v2_get_error_changed", h.Service.RowFor(Flashes).MessageKey);

        // The row now shows 90, and a fresh confirm at 90 goes out - with the NEW version.
        await Harness.WaitFor(() => h.Service.RowFor(Flashes).PriceSp == 90);
        Assert.True(await h.Buy(Flashes));
        var buy = h.Relay.Calls.Single(c => c.Op == "buy");
        Assert.Equal(3, buy.Body?.Value<int>("catalogVersion"));
    }

    [Fact]
    public async Task ARepricedCatalogueDropsTheVersionWithIt_SoTheRetryIsNotRefusedForever()
    {
        var h = new Harness();
        await h.ReadCounterAsync();
        h.Relay.BuyResult = Refuse("catalog_changed");
        // The counter the refusal's own re-read will find. It is set BEFORE the press that triggers
        // that read: setting it afterwards races the read and it can pick up the old body.
        h.Relay.StateResult = StateBody(catalogVersion: 7, flashesPrice: 45);

        Assert.False(await h.Buy(Flashes));
        Assert.Equal("v2_get_error_changed", h.Service.RowFor(Flashes).MessageKey);

        // Leaving the stale version behind made every later press re-send it and be refused again.
        // Wait on the APPLIED price, never on the relay's call count: the call is recorded before
        // the reply lands, so counting it proves nothing about what the service knows.
        h.Relay.BuyResult = new BackRoomStationResult(true, 200, null, new JObject { ["ok"] = true });
        await Harness.WaitFor(() => h.Service.RowFor(Flashes).PriceSp == 45);

        Assert.True(await h.Buy(Flashes));
        var buy = h.Relay.Calls.Last(c => c.Op == "buy");
        Assert.Equal(7, buy.Body?.Value<int>("catalogVersion"));
    }

    [Fact]
    public async Task APressLandingOnTopOfARefreshWaitsForIt_RatherThanClaimingTheCounterIsUnreachable()
    {
        var h = new Harness();
        await h.ReadCounterAsync();

        // Hold the refresh open, start it, then press. The press needs a version and there is a
        // read already out: it must join that read, not give up and say "offline".
        var hold = new TaskCompletionSource<bool>();
        h.Relay.HoldState = hold;
        h.Service.Invalidate();          // drops the version, so the press has to fetch
        h.Service.Refresh();
        await Harness.WaitFor(() => h.Relay.CountOf("state") == 2);

        var press = h.Service.BuyAsync(Flashes, 30);
        await Task.Delay(60);
        Assert.False(press.IsCompleted);             // waiting on the read, not refusing
        Assert.Equal(2, h.Relay.CountOf("state"));   // it joined that read, it did not start another

        hold.SetResult(true);
        Assert.True(await press);
        Assert.Equal(1, h.Relay.CountOf("buy"));
    }

    // ---- the wallet ---------------------------------------------------------------------

    [Fact]
    public async Task AStateReadHandsItsBalanceOnAsAREAD_AndABuyAsASETTLEMENT()
    {
        var h = new Harness();
        await h.ReadCounterAsync();
        var read = Assert.Single(h.AdoptedCalls);
        Assert.Equal(("u_one", false, 100), read);

        h.Relay.BuyResult = new BackRoomStationResult(true, 200, null,
            new JObject { ["ok"] = true, ["sp"] = 70 });
        await h.Buy(Flashes);

        var settlement = h.AdoptedCalls.First(c => c.FromBuy);
        Assert.Equal(("u_one", true, 70), settlement);
    }

    [Fact]
    public async Task ABalanceIsHandedOnUNDERTheAccountTheRequestWentOutFor()
    {
        var h = new Harness();
        await h.ReadCounterAsync();

        // Sign out and in as somebody else while the buy is out. Reading the account back after
        // the relay answers would name B, the app's own re-check would then compare B with B, and
        // A's receipt would lower B's wallet.
        h.Relay.HoldBuy = new TaskCompletionSource<bool>();
        var press = h.Buy(Flashes);
        await Harness.WaitFor(() => h.Relay.CountOf("buy") == 1);
        h.Account = "u_two";
        h.Relay.HoldBuy.SetResult(true);
        await press;

        var settlement = h.AdoptedCalls.Single(c => c.FromBuy);
        Assert.Equal("u_one", settlement.Account);   // never "u_two"
    }

    [Theory]
    [InlineData("insufficient")]
    [InlineData("catalog_changed")]
    [InlineData("busy")]
    [InlineData("unavailable")]
    [InlineData("closed")]
    [InlineData("owned")]
    public async Task ONLYADebitedReceiptCountsAsASettlement(string reason)
    {
        var h = new Harness();
        await h.ReadCounterAsync();

        // A refusal that carries an sp is a snapshot like any state read: it may be behind a local
        // level-up credit, so it may only RAISE. `owned` is a refusal too, and it only fires when
        // no receipt exists for this idem (backroom-counter.js settleBuy), so it is never our own
        // settlement - it carries no sp today either.
        h.Relay.BuyResult = new BackRoomStationResult(false, 200, reason,
            new JObject { ["ok"] = false, ["reason"] = reason, ["sp"] = 4 });
        await h.Buy(Flashes);

        Assert.DoesNotContain(h.AdoptedCalls, c => c.FromBuy);
        Assert.Contains(h.AdoptedCalls, c => c is { FromBuy: false, Sp: 4 });
    }

    [Fact]
    public async Task AReplayedReceiptIsStillASettlement()
    {
        var h = new Harness();
        await h.ReadCounterAsync();

        // The server sends the stored receipt RAW when an idem replays (out.replay -> sendRaw), so
        // a replay answers ok:true and is the same debited money the first attempt made.
        h.Relay.BuyResult = new BackRoomStationResult(true, 200, null,
            new JObject { ["ok"] = true, ["idem"] = "x", ["paidSp"] = 30, ["sp"] = 70 });
        Assert.True(await h.Buy(Flashes));

        Assert.Contains(h.AdoptedCalls, c => c is { FromBuy: true, Sp: 70 });
    }

    [Fact]
    public async Task ARefusedReadHandsNothingOn()
    {
        var h = new Harness();
        h.Relay.StateResult = Refuse("offline", 0);
        h.Service.EnsureState();
        await Task.Delay(40);
        Assert.Empty(h.AdoptedCalls);
    }

    [Fact]
    public async Task OwnedComesBackAsABuy_BecauseTheReceiptAlreadyExists()
    {
        var h = new Harness();
        await h.ReadCounterAsync();
        h.Relay.BuyResult = Refuse("owned");

        Assert.True(await h.Buy(Flashes));
        Assert.Contains(Flashes, h.Bought);
        Assert.Null(h.Service.RowFor(Flashes).MessageKey);
    }

    [Fact]
    public async Task AGoodBuyReReadsTheCounter_SoTheSaleFlagsAndTheBalanceCatchUp()
    {
        var h = new Harness();
        await h.ReadCounterAsync();
        Assert.Equal(1, h.Relay.CountOf("state"));

        await h.Buy(Flashes);
        await Harness.WaitFor(() => h.Relay.CountOf("state") == 2);
    }

    [Fact]
    public async Task OnceTheGrantLands_TheRowGetsOutOfTheWay()
    {
        var h = new Harness();
        await h.ReadCounterAsync();
        Assert.Equal(V2PurchaseRowState.Offer, h.Service.RowFor(Flashes).State);

        h.Owned.Add(Flashes);        // the relay applied the prizes block
        Assert.Equal(V2PurchaseRowState.Hidden, h.Service.RowFor(Flashes).State);
        Assert.Equal(V2PurchaseRowState.Offer, h.Service.RowFor(Bubbles).State);
    }

    [Fact]
    public async Task SigningOutDropsTheCachedCounter()
    {
        var h = new Harness();
        await h.ReadCounterAsync();

        h.Service.Invalidate();
        Assert.Equal(V2PurchaseRowState.Loading, h.Service.RowFor(Flashes).State);

        await h.ReadCounterAsync();
        Assert.Equal(2, h.Relay.CountOf("state"));
    }

    [Fact]
    public async Task ThePanelIsToldToRepaint_AtTheStartAndTheEndOfAPurchase()
    {
        var h = new Harness();
        await h.ReadCounterAsync();
        var before = h.Changes;

        await h.Buy(Flashes);
        Assert.True(h.Changes >= before + 2);
    }
}
