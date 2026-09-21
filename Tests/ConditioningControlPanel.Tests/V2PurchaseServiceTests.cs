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

        public BackRoomStationResult StateResult = Ok(new JObject
        {
            ["ok"] = true,
            ["open"] = true,
            ["catalogVersion"] = 2,
            ["sp"] = 100,
            ["catalog"] = new JArray
            {
                new JObject { ["id"] = "flashes_v2", ["priceSp"] = 30, ["sale"] = "on" },
                new JObject { ["id"] = "bubbles_v2", ["priceSp"] = 30, ["sale"] = "on" },
                new JObject { ["id"] = "rt_bundle_1", ["priceSp"] = 1200, ["sale"] = "soon" },
            },
        });
        public BackRoomStationResult BuyResult = Ok(new JObject { ["ok"] = true, ["sp"] = 70 });
        /// <summary>Set to hold a buy open, so a second press can be sent while the first is out.</summary>
        public TaskCompletionSource<bool>? HoldBuy;

        public IReadOnlyList<Call> Calls { get { lock (_gate) return _calls.ToArray(); } }
        public int CountOf(string op) => Calls.Count(c => c.Op == op);

        public async Task<BackRoomStationResult> RelayAsync(string station, string op, string? idem,
            JObject? body, CancellationToken ct = default)
        {
            lock (_gate) _calls.Add(new Call(station, op, idem, body));
            if (op == "buy" && HoldBuy != null) await HoldBuy.Task.ConfigureAwait(false);
            return op == "buy" ? BuyResult : StateResult;
        }

        private static BackRoomStationResult Ok(JObject body) => new(true, 200, null, body);
    }

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

        public Harness()
        {
            Service = new V2PurchaseService(Relay, () => Account, id => Owned.Contains(id), () => Sp);
            Service.Changed += () => Interlocked.Increment(ref Changes);
            Service.Bought += id => { lock (Bought) Bought.Add(id); };
        }

        /// <summary>Read the counter and wait for it, the way the row's first appearance does.</summary>
        public async Task ReadCounterAsync()
        {
            Service.EnsureState();
            await WaitFor(() => Service.RowFor(Flashes).State != V2PurchaseRowState.Loading);
        }

        public static async Task WaitFor(Func<bool> done)
        {
            for (var i = 0; i < 400 && !done(); i++) await Task.Delay(5);
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

        Assert.True(await h.Service.BuyAsync(Flashes));

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

        var first = h.Service.BuyAsync(Flashes);
        await Harness.WaitFor(() => h.Service.RowFor(Flashes).State == V2PurchaseRowState.Busy);

        Assert.False(await h.Service.BuyAsync(Flashes));   // the row is already someone's
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
        Assert.False(await h.Service.BuyAsync(Flashes));
        Assert.Equal("v2_get_error_offline", h.Service.RowFor(Flashes).MessageKey);

        h.Relay.BuyResult = new BackRoomStationResult(true, 200, null, new JObject { ["ok"] = true });
        Assert.True(await h.Service.BuyAsync(Flashes));

        var idems = h.Relay.Calls.Where(c => c.Op == "buy").Select(c => c.Idem).Distinct().ToList();
        Assert.Single(idems);
    }

    [Fact]
    public async Task TwoPrizesNeverShareAnIdem()
    {
        var h = new Harness();
        await h.ReadCounterAsync();
        await h.Service.BuyAsync(Flashes);
        await h.Service.BuyAsync(Bubbles);

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
        Assert.False(await h.Service.BuyAsync(Flashes));
        Assert.Equal("v2_get_error_sp", h.Service.RowFor(Flashes).MessageKey);
    }

    [Fact]
    public async Task AShutDoorRefusesWithoutBlamingThePlayer()
    {
        var h = new Harness();
        await h.ReadCounterAsync();
        h.Relay.BuyResult = Refuse("closed", 403);

        Assert.False(await h.Service.BuyAsync(Flashes));
        Assert.Equal("v2_get_unavailable", h.Service.RowFor(Flashes).MessageKey);
    }

    [Fact]
    public async Task AnUndeployedRouteIsUnavailable_NotAnErrorThePlayerCanRetryForever()
    {
        var h = new Harness();
        await h.ReadCounterAsync();
        h.Relay.BuyResult = new BackRoomStationResult(false, 404, "bad_op", new JObject { ["ok"] = false });

        Assert.False(await h.Service.BuyAsync(Flashes));
        Assert.Equal("v2_get_unavailable", h.Service.RowFor(Flashes).MessageKey);
    }

    [Fact]
    public async Task ARelayThatThrowsIsStillAnAnswer_AndTheRowIsNotLeftBusy()
    {
        var h = new Harness();
        await h.ReadCounterAsync();
        h.Relay.HoldBuy = new TaskCompletionSource<bool>();
        h.Relay.HoldBuy.SetException(new InvalidOperationException("socket gone"));

        Assert.False(await h.Service.BuyAsync(Flashes));
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
        Assert.False(await h.Service.BuyAsync(Flashes));
        Assert.Equal("v2_get_error_offline", h.Service.RowFor(Flashes).MessageKey);
        Assert.Equal(0, h.Relay.CountOf("buy"));   // no version, so nothing was sent
    }

    [Fact]
    public async Task OwnedComesBackAsABuy_BecauseTheReceiptAlreadyExists()
    {
        var h = new Harness();
        await h.ReadCounterAsync();
        h.Relay.BuyResult = Refuse("owned");

        Assert.True(await h.Service.BuyAsync(Flashes));
        Assert.Contains(Flashes, h.Bought);
        Assert.Null(h.Service.RowFor(Flashes).MessageKey);
    }

    [Fact]
    public async Task AGoodBuyReReadsTheCounter_SoTheSaleFlagsAndTheBalanceCatchUp()
    {
        var h = new Harness();
        await h.ReadCounterAsync();
        Assert.Equal(1, h.Relay.CountOf("state"));

        await h.Service.BuyAsync(Flashes);
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

        await h.Service.BuyAsync(Flashes);
        Assert.True(h.Changes >= before + 2);
    }
}
