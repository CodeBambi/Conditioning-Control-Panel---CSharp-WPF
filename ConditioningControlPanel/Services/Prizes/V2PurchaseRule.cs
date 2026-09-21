using System;

namespace ConditioningControlPanel.Services.Prizes;

/// <summary>What the "Get it" row inside a v2 box is showing.</summary>
public enum V2PurchaseRowState
{
    /// <summary>Nothing to sell: the account already owns the prize, so the real options are up.</summary>
    Hidden,
    /// <summary>No account, so no Sparkle Points and no counter. The row offers the way in.</summary>
    SignIn,
    /// <summary>The counter has not answered yet.</summary>
    Loading,
    /// <summary>Buyable: price known, sale on, balance covers it.</summary>
    Offer,
    /// <summary>Buyable in principle, but the balance is short.</summary>
    CannotAfford,
    /// <summary>The door is shut or the row is not on sale on this deploy.</summary>
    Unavailable,
    /// <summary>A buy is in flight.</summary>
    Busy,
    /// <summary>The last attempt did not go through; the row says why and offers a retry.</summary>
    Failed,
}

/// <summary>One row's whole appearance. <see cref="ShortBy"/> is 0 outside <see cref="V2PurchaseRowState.CannotAfford"/>.</summary>
public readonly record struct V2PurchaseRow(V2PurchaseRowState State, int PriceSp, int ShortBy, string? MessageKey)
{
    /// <summary>The price is only worth drawing once the counter has told us what it is.</summary>
    public bool ShowsPrice => PriceSp > 0
        && State is V2PurchaseRowState.Offer or V2PurchaseRowState.CannotAfford
            or V2PurchaseRowState.Busy or V2PurchaseRowState.Failed;

    /// <summary>The "Get it" button is up, and pressable only when a press would actually buy.</summary>
    public bool ShowsBuyButton => State is V2PurchaseRowState.Offer or V2PurchaseRowState.CannotAfford
        or V2PurchaseRowState.Busy or V2PurchaseRowState.Failed;

    /// <summary>Pressable: a press starts a purchase.</summary>
    public bool BuyEnabled => State is V2PurchaseRowState.Offer or V2PurchaseRowState.Failed;
}

/// <summary>
/// THE ONE PLACE that decides what the standalone "Get it" row shows, so the Flashes panel and the
/// Bubble Pop panel cannot drift apart about the same purchase.
///
/// <para>Owner decision, 2026-09-19: the v2 effects are obtainable without ever walking into the
/// Back Room, because not everybody wants to play it. The row buys the SAME counter prize at the
/// SAME price through the same route; nothing here knows a number, because the server is the
/// authority on both the price and whether the row is for sale at all (10.17.B).</para>
///
/// <para>Ownership hides the row rather than dressing it: once the prize lands, the box already has
/// the real options to show and a bought row is clutter. Nothing auto-enables (owner rule): owning
/// reveals the dials, the user picks them.</para>
///
/// <para>Nothing here touches WPF or the network.</para>
/// </summary>
public static class V2PurchaseRule
{
    /// <summary>The counter prize behind the Flashes v2 box (fx.flash.drift_bounce + fx.flash.pendulum).</summary>
    public const string FlashesPrizeId = "flashes_v2";
    /// <summary>The counter prize behind the Bubbles v2 box (fx.bubble.rain + fx.bubble.spiral_in).</summary>
    public const string BubblesPrizeId = "bubbles_v2";

    /// <summary>
    /// Does the account already hold what this row sells? Pure, for the tests.
    ///
    /// <para>Jackpot Remix is deliberately NOT counted for the Flashes row: it is its own counter
    /// prize at its own price, so owning it must not hide the offer for the motion styles.</para>
    /// </summary>
    public static bool OwnsPrize(string? prizeId, bool driftBounce, bool pendulum, bool rain, bool spiralIn)
        => prizeId switch
        {
            FlashesPrizeId => driftBounce || pendulum,
            BubblesPrizeId => rain || spiralIn,
            _ => false,
        };

    /// <summary>The live read, off <see cref="PrizeGrants"/> and never a setting.</summary>
    public static bool OwnsPrize(string? prizeId) => OwnsPrize(prizeId,
        PrizeGrants.IsGranted(PrizeGrants.FlashDriftBounce),
        PrizeGrants.IsGranted(PrizeGrants.FlashPendulum),
        PrizeGrants.IsGranted(PrizeGrants.BubbleRain),
        PrizeGrants.IsGranted(PrizeGrants.BubbleSpiralIn));

    /// <summary>
    /// The row for one prize.
    /// </summary>
    /// <param name="owned">The account holds the grants already (PrizeGrants, never a setting).</param>
    /// <param name="signedIn">A unified id and an auth token are on hand, and the app is not offline.</param>
    /// <param name="stateKnown">counter/state has answered at least once this session.</param>
    /// <param name="doorOpen">The Back Room door is open for this account.</param>
    /// <param name="onSale">This row's sale is `on` (env BACKROOM_COUNTER_ON), not `soon`.</param>
    /// <param name="priceSp">The server's price for this row. 0 until the counter says.</param>
    /// <param name="sp">The balance the wallet is showing.</param>
    /// <param name="busy">A buy for this row is in flight.</param>
    /// <param name="failureKey">Lexicon key for the last refusal, or null.</param>
    public static V2PurchaseRow Decide(bool owned, bool signedIn, bool stateKnown, bool doorOpen,
        bool onSale, int priceSp, int sp, bool busy, string? failureKey)
    {
        // Owned wins over everything, including an in-flight buy: the reply that granted it is the
        // one that cleared the flag, and a row for a prize in hand has nothing left to say.
        if (owned) return new V2PurchaseRow(V2PurchaseRowState.Hidden, 0, 0, null);
        if (!signedIn) return new V2PurchaseRow(V2PurchaseRowState.SignIn, 0, 0, null);
        if (busy) return new V2PurchaseRow(V2PurchaseRowState.Busy, priceSp, 0, null);
        if (!string.IsNullOrEmpty(failureKey))
            return new V2PurchaseRow(V2PurchaseRowState.Failed, priceSp, 0, failureKey);
        if (!stateKnown) return new V2PurchaseRow(V2PurchaseRowState.Loading, 0, 0, null);
        // A shut door and a row that is not for sale read the same to a player, and neither is
        // something waiting will fix, so say the one honest thing and offer nothing.
        if (!doorOpen || !onSale || priceSp <= 0)
            return new V2PurchaseRow(V2PurchaseRowState.Unavailable, 0, 0, null);
        if (sp < priceSp) return new V2PurchaseRow(V2PurchaseRowState.CannotAfford, priceSp, priceSp - sp, null);
        return new V2PurchaseRow(V2PurchaseRowState.Offer, priceSp, 0, null);
    }

    /// <summary>
    /// A refusal turned into a line in the app's voice. The server's reason is never shown raw: half
    /// of them are wire words (bad_op, idem_mismatch) that mean nothing to a player, and the other
    /// half read as an accusation. Answers null when there is nothing to say.
    ///
    /// <para><c>owned</c> is not a failure. It means the receipt already exists (a replay, or a buy
    /// that landed from another device), and the reply carries the prizes block that unlocks it.</para>
    /// </summary>
    public static string? FailureKeyFor(bool ok, string? reason) => ok ? null : reason switch
    {
        null or "" => "v2_get_error_generic",
        "owned" => null,
        "insufficient" => "v2_get_error_sp",
        "closed" or "unavailable" or "bad_op" => "v2_get_unavailable",
        "busy" or "too_fast" => "v2_get_error_busy",
        "offline" or "timeout" => "v2_get_error_offline",
        "catalog_changed" => "v2_get_error_changed",
        _ => "v2_get_error_generic",
    };

    /// <summary>
    /// A fresh idempotency key. The server's shape is <c>[A-Za-z0-9_-]{16,64}</c>; a 32 character
    /// hex guid sits inside it.
    /// </summary>
    public static string NewIdem() => Guid.NewGuid().ToString("N");
}
