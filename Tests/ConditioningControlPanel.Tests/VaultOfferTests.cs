using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Services.Vault;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The vault gate card's decisions: which feature a padlock belongs to, what the tiers cost, the
/// supporter line (never a made-up number), and when an invite week's last-day card is owed.
/// </summary>
public class VaultOfferTests
{
    private static readonly DateTime Now = new(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);
    private static readonly string[] Languages = { "en", "de", "es", "fr", "ja", "ko", "pt-BR", "ru", "zh-CN" };

    [Theory]
    [InlineData(1, PriceCurrency.Eur, "€6", "€0.20", "€60", "€5")]
    [InlineData(2, PriceCurrency.Eur, "€10", "€0.33", "€100", "€8.33")]
    [InlineData(1, PriceCurrency.Usd, "$7.50", "$0.25", "$75", "$6.25")]
    [InlineData(2, PriceCurrency.Usd, "$12.50", "$0.42", "$125", "$10.42")]
    [InlineData(0, PriceCurrency.Usd, "$7.50", "$0.25", "$75", "$6.25")]
    public void ThePatreonPriceList(int tier, PriceCurrency currency, string monthly, string perDay, string yearly, string yearlyPerMonth)
    {
        var price = VaultOffer.PriceFor(tier, currency);
        Assert.Equal(monthly, VaultOffer.Money(price.MonthlyCents, currency));
        Assert.Equal(perDay, VaultOffer.PerDay(price));
        Assert.Equal(yearly, VaultOffer.Money(price.YearlyCents, currency));
        Assert.Equal(yearlyPerMonth, VaultOffer.YearlyPerMonth(price));
    }

    [Theory]
    [InlineData(1, PriceCurrency.Eur)]
    [InlineData(2, PriceCurrency.Eur)]
    [InlineData(1, PriceCurrency.Usd)]
    [InlineData(2, PriceCurrency.Usd)]
    public void YearlyIsTwoMonthsFree(int tier, PriceCurrency currency)
    {
        var price = VaultOffer.PriceFor(tier, currency);
        Assert.Equal(price.MonthlyCents * 10, price.YearlyCents);
    }

    [Theory]
    [InlineData("EUR", PriceCurrency.Eur)]
    [InlineData("eur", PriceCurrency.Eur)]
    [InlineData("USD", PriceCurrency.Usd)]
    [InlineData("GBP", PriceCurrency.Usd)]
    [InlineData(null, PriceCurrency.Usd)]
    public void EurosOnlyWhereTheRegionPaysInEuros(string? iso, PriceCurrency expected)
        => Assert.Equal(expected, VaultOffer.CurrencyFor(iso));

    [Theory]
    [InlineData("LockdownTabView", "lockdown")]
    [InlineData("BambiTakeoverTabView", "bambitakeover")]
    [InlineData("HapticsTabView", "haptics")]
    [InlineData("AwarenessTabView", "awareness")]
    [InlineData("RemoteControlTabView", "remotecontrol")]
    [InlineData("SheListeningTabView", "shelistening")]
    [InlineData("BlinkTrainerTabView", "blinktrainer")]
    public void EveryGatedViewNamesARealFeature(string view, string key)
    {
        Assert.Equal(key, VaultOffer.FeatureKeyForView(view));
        Assert.NotNull(VaultOffer.Feature(key));
    }

    [Theory]
    [InlineData("GradedIntakeTabView", true)]
    [InlineData("LockdownTabView", false)]
    [InlineData(null, false)]
    public void GradedIntakeKeepsTheAccountRoute(string? view, bool expected)
        => Assert.Equal(expected, VaultOffer.KeepsAccountRoute(view));

    [Theory]
    [InlineData(null)]
    [InlineData("SettingsTabView")]
    public void AnUnknownViewGetsTheGenericCard(string? view) => Assert.Null(VaultOffer.FeatureKeyForView(view));

    [Theory]
    [InlineData(null, null)]
    [InlineData(0, null)]
    [InlineData(99, null)]
    [InlineData(100, "100")]
    [InlineData(1234, "1,200")]
    [InlineData(1299, "1,200")]
    [InlineData(25080, "25,000")]
    public void SupporterFloorRoundsDownAndHidesSmallNumbers(int? count, string? expected)
        => Assert.Equal(expected, VaultOffer.SupporterFloor(count));

    [Theory]
    [InlineData("{\"count\":1234}", 1234)]
    [InlineData("{\"count\":\"1234\"}", null)]
    [InlineData("{\"count\":-1}", null)]
    [InlineData("{}", null)]
    [InlineData("<html>", null)]
    [InlineData("", null)]
    public void ParseSupporterCount(string body, int? expected)
        => Assert.Equal(expected, VaultOffer.ParseSupporterCount(body));

    [Fact]
    public void EndingIsOwedInsideTheWindowOnce()
    {
        var end = Now.AddHours(20);
        var key = VaultOffer.InviteEndingOwed(end, Now, inviteWeekOnly: true, new List<string>());
        Assert.Equal("invite-ending:202610030800", key);
        Assert.Null(VaultOffer.InviteEndingOwed(end, Now, true, new List<string> { key! }));
    }

    [Fact]
    public void EndingIsNotOwedTooEarlyAfterTheEndOrToAPayingAccount()
    {
        Assert.Null(VaultOffer.InviteEndingOwed(Now.AddDays(3), Now, true, null));
        Assert.Null(VaultOffer.InviteEndingOwed(Now.AddMinutes(-1), Now, true, null));
        Assert.Null(VaultOffer.InviteEndingOwed(Now.AddHours(5), Now, inviteWeekOnly: false, null));
        Assert.Null(VaultOffer.InviteEndingOwed(null, Now, true, null));
    }

    [Fact]
    public void ALaterWeekIsOwedItsOwnCard()
    {
        var first = VaultOffer.InviteEndingOwed(Now.AddHours(10), Now, true, null);
        var second = VaultOffer.InviteEndingOwed(Now.AddDays(40).AddHours(10), Now.AddDays(40), true, new List<string> { first! });
        Assert.NotNull(second);
        Assert.NotEqual(first, second);
    }

    // ---------------------------------------------------------------- words

    private static string AppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return Path.Combine(dir!.FullName, "ConditioningControlPanel");
    }

    private static string Placeholders(string s)
        => string.Join(",", Regex.Matches(s, @"\{\d\}").Select(m => m.Value).OrderBy(v => v));

    [Fact]
    public void EveryKeyTheCardsReadIsInEveryLanguageWithTheSamePlaceholders()
    {
        var src = File.ReadAllText(Path.Combine(AppDir(), "Dialogs", "VaultGateDialog.cs"))
                  + File.ReadAllText(Path.Combine(AppDir(), "MainWindow", "MainWindow.Patreon.cs"));
        var keys = Regex.Matches(src, "\"(vaultgate_[a-z_0-9]+)\"").Select(m => m.Groups[1].Value).Distinct().ToList();
        Assert.Contains("vaultgate_open_vault", keys);
        Assert.Contains("vaultgate_ending_inbox", keys);

        var en = JObject.Parse(File.ReadAllText(Path.Combine(SourceRoots.LanguagesDirectory, "en.json")));
        foreach (var lang in Languages)
        {
            var json = JObject.Parse(File.ReadAllText(Path.Combine(SourceRoots.LanguagesDirectory, lang + ".json")));
            foreach (var key in keys)
            {
                var value = json.Value<string>(key);
                Assert.False(string.IsNullOrWhiteSpace(value), $"{lang}.json is missing {key}");
                Assert.Equal(Placeholders(en.Value<string>(key)!), Placeholders(value!));
            }
        }
    }

    [Fact]
    public void EveryPadlockButtonGoesThroughTheGateCard()
    {
        var patreon = File.ReadAllText(Path.Combine(AppDir(), "MainWindow", "MainWindow.Patreon.cs"));
        var at = patreon.IndexOf("internal void BtnGateUnlock_Click", StringComparison.Ordinal);
        Assert.True(at > 0);
        var body = patreon.Substring(at, 900);
        Assert.Contains("ShowVaultGate(", body);
        Assert.Contains("TierGate.ReconnectIsTheAnswer()", body);
        Assert.Contains("StartPatreonReconnectFromGate()", body);
    }
}
