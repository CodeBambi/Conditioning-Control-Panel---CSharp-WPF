using System;
using System.Globalization;
using ConditioningControlPanel.Services.Vault;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The unlock card's first-month sale: the server switch is read strictly and fails closed, the
/// sale covers only the tiers it names and only until it ends, yearly never has one, and the
/// first-month price rounds to the cent.
/// </summary>
public class VaultSaleTests
{
    private static readonly DateTime Now = new(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_live_sale_reads_whole()
    {
        var sale = VaultSale.Parse("{\"active\":true,\"percent\":50,\"ends_at\":\"2026-10-10T22:00:00Z\",\"tiers\":[\"basic\",\"prime\"]}");
        Assert.NotNull(sale);
        Assert.Equal(50, sale!.Percent);
        Assert.Equal(new DateTime(2026, 10, 10, 22, 0, 0, DateTimeKind.Utc), sale.EndsAtUtc);
        Assert.Equal(DateTimeKind.Utc, sale.EndsAtUtc!.Value.Kind);
        Assert.True(VaultSale.AppliesTo(sale, 1, Now));
        Assert.True(VaultSale.AppliesTo(sale, 2, Now));
    }

    [Fact]
    public void An_offset_stamp_lands_in_utc()
    {
        var sale = VaultSale.Parse("{\"active\":true,\"percent\":20,\"ends_at\":\"2026-10-10T23:00:00+02:00\",\"tiers\":[\"prime\"]}");
        Assert.Equal(new DateTime(2026, 10, 10, 21, 0, 0, DateTimeKind.Utc), sale!.EndsAtUtc);
    }

    [Fact]
    public void No_end_date_is_an_open_sale()
    {
        var sale = VaultSale.Parse("{\"active\":true,\"percent\":30,\"ends_at\":null,\"tiers\":[\"basic\"]}");
        Assert.NotNull(sale);
        Assert.Null(sale!.EndsAtUtc);
        Assert.Null(VaultSale.EndsText(sale));
        Assert.True(VaultSale.AppliesTo(sale, 1, Now.AddYears(5)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"active\":false,\"percent\":50,\"ends_at\":null,\"tiers\":[\"basic\"]}")]
    [InlineData("{\"active\":\"true\",\"percent\":50,\"ends_at\":null,\"tiers\":[\"basic\"]}")]
    [InlineData("{\"percent\":50,\"ends_at\":null,\"tiers\":[\"basic\"]}")]
    [InlineData("{\"active\":true,\"percent\":4,\"ends_at\":null,\"tiers\":[\"basic\"]}")]
    [InlineData("{\"active\":true,\"percent\":91,\"ends_at\":null,\"tiers\":[\"basic\"]}")]
    [InlineData("{\"active\":true,\"percent\":50.5,\"ends_at\":null,\"tiers\":[\"basic\"]}")]
    [InlineData("{\"active\":true,\"percent\":\"50\",\"ends_at\":null,\"tiers\":[\"basic\"]}")]
    [InlineData("{\"active\":true,\"percent\":50,\"ends_at\":\"soon\",\"tiers\":[\"basic\"]}")]
    [InlineData("{\"active\":true,\"percent\":50,\"ends_at\":null,\"tiers\":[]}")]
    [InlineData("{\"active\":true,\"percent\":50,\"ends_at\":null,\"tiers\":[\"vault\",\"lab\"]}")]
    [InlineData("{\"active\":true,\"percent\":50,\"ends_at\":null}")]
    public void Anything_odd_is_no_sale(string? body) => Assert.Null(VaultSale.Parse(body));

    [Fact]
    public void Percent_bounds_are_inclusive()
    {
        Assert.Equal(5, VaultSale.Parse("{\"active\":true,\"percent\":5,\"ends_at\":null,\"tiers\":[\"basic\"]}")!.Percent);
        Assert.Equal(90, VaultSale.Parse("{\"active\":true,\"percent\":90,\"ends_at\":null,\"tiers\":[\"basic\"]}")!.Percent);
    }

    [Fact]
    public void Only_the_named_tiers_are_on_sale()
    {
        var sale = VaultSale.Parse("{\"active\":true,\"percent\":50,\"ends_at\":null,\"tiers\":[\"Prime\",\"other\"]}");
        Assert.False(VaultSale.AppliesTo(sale, 1, Now));
        Assert.True(VaultSale.AppliesTo(sale, 2, Now));
    }

    [Fact]
    public void An_ended_sale_and_yearly_billing_show_normal_prices()
    {
        var sale = VaultSale.Parse("{\"active\":true,\"percent\":50,\"ends_at\":\"2026-10-02T12:00:00Z\",\"tiers\":[\"basic\"]}");
        Assert.True(VaultSale.AppliesTo(sale, 1, Now.AddSeconds(-1)));
        Assert.False(VaultSale.AppliesTo(sale, 1, Now));
        Assert.False(VaultSale.AppliesTo(sale, 1, Now.AddSeconds(-1), yearly: true));
        Assert.False(VaultSale.AppliesTo(null, 1, Now));
    }

    [Theory]
    [InlineData(600, 50, 300)]
    [InlineData(750, 50, 375)]
    [InlineData(1000, 33, 670)]
    [InlineData(1250, 15, 1063)]  // 1062.5 rounds away from zero
    [InlineData(750, 90, 75)]
    public void First_month_rounds_to_the_cent(int monthly, int percent, int expected)
        => Assert.Equal(expected, VaultSale.FirstMonthCents(monthly, percent));

    [Fact]
    public void First_month_reads_in_card_money()
    {
        var basic = VaultOffer.PriceFor(1, PriceCurrency.Usd);
        Assert.Equal("$3.75", VaultOffer.Money(VaultSale.FirstMonthCents(basic.MonthlyCents, 50), PriceCurrency.Usd));
        var prime = VaultOffer.PriceFor(2, PriceCurrency.Eur);
        Assert.Equal("€5", VaultOffer.Money(VaultSale.FirstMonthCents(prime.MonthlyCents, 50), PriceCurrency.Eur));
    }

    [Fact]
    public void The_end_date_prints_in_local_time()
    {
        var sale = VaultSale.Parse("{\"active\":true,\"percent\":50,\"ends_at\":\"2026-10-10T23:30:00Z\",\"tiers\":[\"basic\"]}")!;
        var rome = TimeZoneInfo.CreateCustomTimeZone("plus2", TimeSpan.FromHours(2), "plus2", "plus2");
        Assert.Equal("Sun 11 Oct", VaultSale.EndsText(sale, rome, CultureInfo.InvariantCulture));
    }
}
