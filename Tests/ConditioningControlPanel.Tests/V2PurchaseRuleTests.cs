using ConditioningControlPanel.Services.Prizes;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The standalone "Get it" row (owner decision, 2026-09-19). One rule table, two panels, so the
/// Flashes box and the Bubble Pop box cannot disagree about the same purchase.
/// </summary>
public class V2PurchaseRuleTests
{
    private const int Price = 30;

    private static V2PurchaseRow Row(bool owned = false, bool signedIn = true, bool known = true,
        bool open = true, bool onSale = true, int price = Price, int sp = 100, bool busy = false,
        string? failure = null)
        => V2PurchaseRule.Decide(owned, signedIn, known, open, onSale, price, sp, busy, failure);

    [Fact]
    public void Owned_HidesTheRow_SoTheRealOptionsHaveTheBox()
    {
        var row = Row(owned: true);
        Assert.Equal(V2PurchaseRowState.Hidden, row.State);
        Assert.False(row.ShowsBuyButton);
        Assert.False(row.ShowsPrice);
    }

    [Fact]
    public void Owned_BeatsEverythingElse_IncludingABuyStillInFlight()
    {
        // The reply that granted the prize is the one that clears the flag, so this ordering is
        // what stops the row flashing "Working" over options that are already up.
        Assert.Equal(V2PurchaseRowState.Hidden, Row(owned: true, busy: true, failure: "v2_get_error_sp").State);
    }

    [Fact]
    public void SignedOut_OffersTheWayIn_AndNamesNoPrice()
    {
        var row = Row(signedIn: false, known: false, price: 0);
        Assert.Equal(V2PurchaseRowState.SignIn, row.State);
        Assert.Equal(0, row.PriceSp);
        Assert.False(row.ShowsPrice);
    }

    [Fact]
    public void SignedOut_WinsOverAStaleFailureFromTheLastAccount()
        => Assert.Equal(V2PurchaseRowState.SignIn, Row(signedIn: false, failure: "v2_get_error_sp").State);

    [Fact]
    public void InFlight_ShowsBusyWithTheButtonDead()
    {
        var row = Row(busy: true);
        Assert.Equal(V2PurchaseRowState.Busy, row.State);
        Assert.True(row.ShowsBuyButton);
        Assert.False(row.BuyEnabled);
        Assert.True(row.ShowsPrice);      // the price does not vanish mid-purchase
    }

    [Fact]
    public void AFailureIsSaidOnce_AndTheButtonStaysPressableForARetry()
    {
        var row = Row(failure: "v2_get_error_offline");
        Assert.Equal(V2PurchaseRowState.Failed, row.State);
        Assert.Equal("v2_get_error_offline", row.MessageKey);
        Assert.True(row.BuyEnabled);
    }

    [Fact]
    public void BeforeTheCounterAnswers_TheRowOnlySaysItIsLooking()
    {
        var row = Row(known: false, price: 0);
        Assert.Equal(V2PurchaseRowState.Loading, row.State);
        Assert.False(row.ShowsBuyButton);
    }

    [Theory]
    [InlineData(false, true, Price)]    // door shut
    [InlineData(true, false, Price)]    // row not on sale on this deploy
    [InlineData(true, true, 0)]         // catalog answered but named no price
    public void AShutDoorOrAnUnsoldRowSaysTheOneHonestThing(bool open, bool onSale, int price)
    {
        var row = Row(open: open, onSale: onSale, price: price);
        Assert.Equal(V2PurchaseRowState.Unavailable, row.State);
        Assert.False(row.ShowsBuyButton);
        Assert.False(row.ShowsPrice);
    }

    [Fact]
    public void ShortOfThePrice_SaysHowShort_AndCannotBePressed()
    {
        var row = Row(sp: 12);
        Assert.Equal(V2PurchaseRowState.CannotAfford, row.State);
        Assert.Equal(18, row.ShortBy);
        Assert.True(row.ShowsPrice);
        Assert.False(row.BuyEnabled);
    }

    [Fact]
    public void ExactlyThePrice_Buys()
    {
        var row = Row(sp: Price);
        Assert.Equal(V2PurchaseRowState.Offer, row.State);
        Assert.Equal(Price, row.PriceSp);
        Assert.Equal(0, row.ShortBy);
        Assert.True(row.BuyEnabled);
    }

    [Fact]
    public void ThePriceIsNeverGuessed_ItIsWhateverTheCounterSaid()
    {
        // 30 SP today, but BACKROOM_COUNTER_PRICES can move it without a client release.
        Assert.Equal(7, Row(price: 7, sp: 7).PriceSp);
        Assert.Equal(V2PurchaseRowState.CannotAfford, Row(price: 900, sp: 7).State);
        Assert.Equal(893, Row(price: 900, sp: 7).ShortBy);
    }

    // ---- refusals to copy ---------------------------------------------------------------

    [Fact]
    public void ASuccessSaysNothing() => Assert.Null(V2PurchaseRule.FailureKeyFor(true, null));

    [Fact]
    public void AlreadyOwnedIsNotAFailure_TheReceiptExistsAndTheGrantsCameWithIt()
        => Assert.Null(V2PurchaseRule.FailureKeyFor(false, "owned"));

    [Theory]
    [InlineData("insufficient", "v2_get_error_sp")]
    [InlineData("closed", "v2_get_unavailable")]
    [InlineData("unavailable", "v2_get_unavailable")]
    [InlineData("bad_op", "v2_get_unavailable")]
    [InlineData("busy", "v2_get_error_busy")]
    [InlineData("too_fast", "v2_get_error_busy")]
    [InlineData("offline", "v2_get_error_offline")]
    [InlineData("timeout", "v2_get_error_offline")]
    [InlineData("catalog_changed", "v2_get_error_changed")]
    public void EveryWordedRefusalHasALineOfItsOwn(string reason, string key)
        => Assert.Equal(key, V2PurchaseRule.FailureKeyFor(false, reason));

    [Theory]
    [InlineData("bad_input")]
    [InlineData("idem_mismatch")]
    [InlineData("discord_required")]
    [InlineData("something_the_server_grew_later")]
    [InlineData(null)]
    [InlineData("")]
    public void AWireWordThePlayerCannotActOnFallsBackToThePlainLine(string? reason)
        => Assert.Equal("v2_get_error_generic", V2PurchaseRule.FailureKeyFor(false, reason));

    // ---- ownership ----------------------------------------------------------------------

    [Fact]
    public void EitherFlashMotionStyleCountsAsOwningTheFlashesRow()
    {
        Assert.True(V2PurchaseRule.OwnsPrize(V2PurchaseRule.FlashesPrizeId, true, false, false, false));
        Assert.True(V2PurchaseRule.OwnsPrize(V2PurchaseRule.FlashesPrizeId, false, true, false, false));
        Assert.False(V2PurchaseRule.OwnsPrize(V2PurchaseRule.FlashesPrizeId, false, false, true, true));
    }

    [Fact]
    public void EitherBubbleMotionCountsAsOwningTheBubblesRow()
    {
        Assert.True(V2PurchaseRule.OwnsPrize(V2PurchaseRule.BubblesPrizeId, false, false, true, false));
        Assert.True(V2PurchaseRule.OwnsPrize(V2PurchaseRule.BubblesPrizeId, false, false, false, true));
        Assert.False(V2PurchaseRule.OwnsPrize(V2PurchaseRule.BubblesPrizeId, true, true, false, false));
    }

    [Fact]
    public void AnUnknownPrizeIsNeverOwned()
        => Assert.False(V2PurchaseRule.OwnsPrize("high_roller", true, true, true, true));

    [Fact]
    public void TheIdemKeyFitsTheServersShape()
    {
        var idem = V2PurchaseRule.NewIdem();
        Assert.Matches("^[A-Za-z0-9_-]{16,64}$", idem);
        Assert.NotEqual(idem, V2PurchaseRule.NewIdem());
    }
}
