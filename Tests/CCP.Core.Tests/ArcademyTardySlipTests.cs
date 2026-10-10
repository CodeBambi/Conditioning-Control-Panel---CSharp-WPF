using System.Linq;
using ConditioningControlPanel.Services.Arcademy;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// THE TARDY SLIP (owner ruling 2026-09-04). The Prize Counter's one consumable, repriced to the
/// middle rung of the Locker's three outfits, capped at two, and — this is the new part — SPENT IN
/// A STACK: one slip per missed night, so two of them carry a streak over two consecutive missed
/// nights and a third missed night breaks it exactly as it always did.
///
/// <para>Everything here runs for real. <see cref="ArcademyEconomy"/> is deliberately free of WPF
/// and of <c>App</c>, so the whole till is exercisable without a window — which is the reason the
/// money was put in that file in the first place. The attendance path itself
/// (<c>ArcademyMetaStore.RecordAttendance</c>) needs a user-data folder and a dispatcher, so its
/// half is covered by the gap arithmetic below plus a source tripwire.</para>
/// </summary>
public class ArcademyTardySlipTests
{
    private static CatalogItemView Slip()
    {
        var row = ArcademyEconomy.Catalog.Single(c => c.Sku == ArcademyEconomy.SkuLateSlip);
        return new CatalogItemView(row.Cur, row.Cost, row.Kind, row.NameEn, row.StackMax);
    }

    private readonly record struct CatalogItemView(
        string Cur, int Cost, string Kind, string NameEn, int StackMax);

    private static JObject Purse(int tickets) => new() { ["t"] = tickets, ["k"] = 0 };

    // =====================================================================================
    //  the shelf
    // =====================================================================================

    [Fact]
    public void SlipIsPricedAtTheMiddleLockerOutfit()
    {
        // The Locker hangs three outfits on tickets; the owner's ruling is "a mid-tier outfit",
        // which is the MIDDLE of those three and nothing else. If a fourth outfit ever ships this
        // test is the thing that notices the middle moved.
        var outfits = ArcademyEconomy.Catalog
            .Where(c => c.Sku.StartsWith("emi_") && c.Cur == ArcademyEconomy.CurTickets)
            .Select(c => c.Cost)
            .OrderBy(c => c)
            .ToList();

        Assert.Equal(3, outfits.Count);
        var middle = outfits[outfits.Count / 2];

        Assert.Equal(middle, Slip().Cost);
        Assert.Equal(ArcademyEconomy.CurTickets, Slip().Cur);
    }

    [Fact]
    public void SlipIsAConsumableCappedAtTwoAndNamedForThePlayer()
    {
        Assert.Equal("consumable", Slip().Kind);
        Assert.Equal(2, ArcademyEconomy.SlipStackMax);
        Assert.Equal(ArcademyEconomy.SlipStackMax, Slip().StackMax);
        Assert.Equal("Tardy Slip", Slip().NameEn);
    }

    [Fact]
    public void TheWireIdNeverMoves()
    {
        // The rebrand was words only. This id is a KEY: it is in every player's `inv`, in the
        // server catalog, in the mobile port and in EMI's `shop.bought:late_slip` pool, and
        // renaming it would silently orphan every slip anybody has already bought.
        Assert.Equal("late_slip", ArcademyEconomy.SkuLateSlip);
    }

    [Fact]
    public void TheShelfProjectsTheCapAsMax()
    {
        var row = ArcademyEconomy.CatalogJson()
            .OfType<JObject>()
            .Single(r => (string?)r["sku"] == ArcademyEconomy.SkuLateSlip);
        Assert.Equal(ArcademyEconomy.SlipStackMax, (int?)row["max"]);
    }

    // =====================================================================================
    //  the counter
    // =====================================================================================

    [Fact]
    public void TwoSlipsFitAndTheThirdIsRefusedFull()
    {
        var w = Purse(Slip().Cost * 3);

        Assert.True(ArcademyEconomy.Buy(w, ArcademyEconomy.SkuLateSlip, "2026-09-04").Ok);
        Assert.True(ArcademyEconomy.Buy(w, ArcademyEconomy.SkuLateSlip, "2026-09-04").Ok);
        Assert.Equal(2, ArcademyEconomy.Held(w, ArcademyEconomy.SkuLateSlip));

        var third = ArcademyEconomy.Buy(w, ArcademyEconomy.SkuLateSlip, "2026-09-04");
        Assert.False(third.Ok);
        Assert.Equal("full", third.Reason);

        // A refusal never leaves the wallet half-spent.
        Assert.Equal(Slip().Cost, (int?)w["t"]);
    }

    [Fact]
    public void AnEmptyPocketIsRefusedPoorAndCostsNothing()
    {
        var w = Purse(Slip().Cost - 1);
        var r = ArcademyEconomy.Buy(w, ArcademyEconomy.SkuLateSlip, "2026-09-04");
        Assert.False(r.Ok);
        Assert.Equal("poor", r.Reason);
        Assert.Equal(Slip().Cost - 1, (int?)w["t"]);
        Assert.Equal(0, ArcademyEconomy.Held(w, ArcademyEconomy.SkuLateSlip));
    }

    // =====================================================================================
    //  the spend
    // =====================================================================================

    [Fact]
    public void OneNightSpendsOneSlip()
    {
        var w = Purse(Slip().Cost * 2);
        ArcademyEconomy.Buy(w, ArcademyEconomy.SkuLateSlip, "2026-09-01");
        ArcademyEconomy.Buy(w, ArcademyEconomy.SkuLateSlip, "2026-09-01");

        Assert.Equal(1, ArcademyEconomy.ConsumeLateSlips(w, 1));
        Assert.Equal(1, ArcademyEconomy.Held(w, ArcademyEconomy.SkuLateSlip));
    }

    [Fact]
    public void TwoNightsSpendBothSlipsTogether()
    {
        var w = Purse(Slip().Cost * 2);
        ArcademyEconomy.Buy(w, ArcademyEconomy.SkuLateSlip, "2026-09-01");
        ArcademyEconomy.Buy(w, ArcademyEconomy.SkuLateSlip, "2026-09-01");

        Assert.Equal(2, ArcademyEconomy.ConsumeLateSlips(w, 2));
        Assert.Equal(0, ArcademyEconomy.Held(w, ArcademyEconomy.SkuLateSlip));
        // The row goes away entirely rather than sitting at zero, so `inv` stays a bag of what
        // is actually on the player.
        Assert.False(((JObject)w["inv"]!).ContainsKey(ArcademyEconomy.SkuLateSlip));
    }

    [Fact]
    public void HalfACoverIsNotACoverAndSpendsNothing()
    {
        var w = Purse(Slip().Cost);
        ArcademyEconomy.Buy(w, ArcademyEconomy.SkuLateSlip, "2026-09-01");

        // Two nights missed, one slip in the bag: the streak breaks and the slip is KEPT. Burning
        // it for a streak that broke anyway is the worst trade the counter could make for them.
        Assert.Equal(0, ArcademyEconomy.ConsumeLateSlips(w, 2));
        Assert.Equal(1, ArcademyEconomy.Held(w, ArcademyEconomy.SkuLateSlip));
    }

    [Fact]
    public void NothingWiderThanTheStackIsEverCoverable()
    {
        var w = Purse(Slip().Cost * 2);
        ArcademyEconomy.Buy(w, ArcademyEconomy.SkuLateSlip, "2026-09-01");
        ArcademyEconomy.Buy(w, ArcademyEconomy.SkuLateSlip, "2026-09-01");

        Assert.Equal(0, ArcademyEconomy.ConsumeLateSlips(w, ArcademyEconomy.SlipStackMax + 1));
        Assert.Equal(0, ArcademyEconomy.ConsumeLateSlips(w, 0));
        Assert.Equal(0, ArcademyEconomy.ConsumeLateSlips(w, -1));
        Assert.Equal(2, ArcademyEconomy.Held(w, ArcademyEconomy.SkuLateSlip));
    }

    [Fact]
    public void AnEmptyBagSpendsNothingAndDoesNotThrow()
    {
        var w = Purse(0);
        Assert.Equal(0, ArcademyEconomy.ConsumeLateSlips(w, 1));
        Assert.Equal(0, ArcademyEconomy.ConsumeLateSlips(w, 2));
    }

    [Fact]
    public void AGrandfatheredThreeStackStillSpends()
    {
        // Slips bought when the cap was three are still in the bag. The counter will not sell a
        // fourth (or a third), but the attendance path must go on spending what is there.
        var w = new JObject
        {
            ["t"] = 0,
            ["inv"] = new JObject { [ArcademyEconomy.SkuLateSlip] = new JObject { ["n"] = 3 } },
        };
        Assert.Equal(2, ArcademyEconomy.ConsumeLateSlips(w, 2));
        Assert.Equal(1, ArcademyEconomy.Held(w, ArcademyEconomy.SkuLateSlip));

        var refused = ArcademyEconomy.Buy(Grandfathered(), ArcademyEconomy.SkuLateSlip, "2026-09-04");
        Assert.False(refused.Ok);
        Assert.Equal("full", refused.Reason);
    }

    private static JObject Grandfathered() => new()
    {
        ["t"] = 10000,
        ["inv"] = new JObject { [ArcademyEconomy.SkuLateSlip] = new JObject { ["n"] = 3 } },
    };

    // =====================================================================================
    //  the gap arithmetic the attendance path reads
    // =====================================================================================

    [Theory]
    [InlineData("2026-09-01", "2026-09-02", 0)]   // no night missed - the streak just climbs
    [InlineData("2026-09-01", "2026-09-03", 1)]   // one night, one slip
    [InlineData("2026-09-01", "2026-09-04", 2)]   // two nights, both slips
    [InlineData("2026-09-01", "2026-09-05", 3)]   // three nights - past the stack, it breaks
    public void MissedNightsAreTheGapMinusOne(string last, string today, int expected)
    {
        var gap = ArcademyEconomy.DayGap(last, today);
        var missed = gap > 1 ? gap - 1 : 0;
        Assert.Equal(expected, missed);
        Assert.Equal(expected > 0 && expected <= ArcademyEconomy.SlipStackMax,
            missed > 0 && missed <= ArcademyEconomy.SlipStackMax);
    }

    [Fact]
    public void AnUnreadableLastDateCoversNothing()
    {
        Assert.Equal(-1, ArcademyEconomy.DayGap(null, "2026-09-04"));
        Assert.Equal(-1, ArcademyEconomy.DayGap("not-a-date", "2026-09-04"));
    }
}
