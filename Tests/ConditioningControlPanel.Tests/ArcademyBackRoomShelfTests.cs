using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Services.Arcademy;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// THE BACK ROOM SHELF (wire contract v1, 2026-09-04). Thirteen rows that spend CHIPS, priced at
/// the cage and sold through the counter the Arcademy already has.
///
/// <para>Prices and stack sizes are the contract's, and they are pinned here rather than reviewed,
/// because the same table is regenerated into the server catalog and parsed again by the web sync:
/// a price that moves in one place and not the other two is the one bug down here that costs a
/// player money. Everything runs for real against <see cref="ArcademyEconomy"/>, which is free of
/// WPF on purpose; the lexicon half is a source tripwire because the table it checks is private.</para>
/// </summary>
public class ArcademyBackRoomShelfTests
{
    // =====================================================================================
    //  the contract table
    // =====================================================================================

    /// <summary>sku, cost, kind, stack, locked, wave. Contract section 6, verbatim.</summary>
    public static IEnumerable<object[]> Rows() => new[]
    {
        new object[] { "bk_scratcher", 50, "consumable", 20, false, 3 },
        new object[] { "bk_insurance", 100, "consumable", 5, false, 3 },
        new object[] { "bk_visor", 3000, "cosmetic", 0, false, 3 },
        new object[] { "bk_felt_teal", 6000, "cosmetic", 0, false, 3 },
        new object[] { "bk_frame_highroller", 10000, "cosmetic", 0, false, 3 },
        new object[] { "bk_your_word", 30000, "unlock", 0, false, 3 },
        new object[] { "bk_house_favorite", 0, "cosmetic", 0, true, 3 },
        new object[] { "bk_double_payday", 1000, "consumable", 5, false, 4 },
        new object[] { "bk_boon", 4000, "consumable", 3, false, 4 },
        new object[] { "bk_pitboss", 15000, "unlock", 0, false, 4 },
        new object[] { "bk_mantra", 20000, "unlock", 0, false, 4 },
        new object[] { "bk_dealers_cut", 50000, "unlock", 0, false, 4 },
        new object[] { "bk_vault_key", 250000, "unlock", 0, false, 4 },
    };

    private static IReadOnlyList<ArcademyEconomy.CatalogItem> ChipRows() =>
        ArcademyEconomy.Catalog.Where(c => c.Cur == ArcademyEconomy.CurChips).ToList();

    [Theory]
    [MemberData(nameof(Rows))]
    public void EveryChipRowIsPricedExactlyAsTheContractSaid(
        string sku, int cost, string kind, int stack, bool locked, int wave)
    {
        var row = ArcademyEconomy.Catalog.Single(c => c.Sku == sku);
        Assert.Equal(ArcademyEconomy.CurChips, row.Cur);
        Assert.Equal(cost, row.Cost);
        Assert.Equal(kind, row.Kind);
        Assert.Equal(stack, row.StackMax);
        Assert.Equal(locked, row.Locked);
        Assert.Equal(wave, row.Wave);
    }

    [Fact]
    public void TheChipShelfIsThirteenRowsAndNothingElseSpendsChips()
    {
        var expected = Rows().Select(r => (string)r[0]).ToList();
        Assert.Equal(expected.OrderBy(s => s, StringComparer.Ordinal),
            ChipRows().Select(c => c.Sku).OrderBy(s => s, StringComparer.Ordinal));
    }

    [Fact]
    public void ChipsAreTheirOwnPurseAndSpelledC()
    {
        // The identifier is read by name out of this file by two external parsers (the server's
        // arcademy-catalog.mjs and cclabs-web's sync-arcademy.mjs), and the letter is the wallet
        // field. Neither can move without both of those moving first.
        Assert.Equal("c", ArcademyEconomy.CurChips);
        Assert.NotEqual(ArcademyEconomy.CurTickets, ArcademyEconomy.CurChips);
        Assert.NotEqual(ArcademyEconomy.CurTokens, ArcademyEconomy.CurChips);
    }

    // =====================================================================================
    //  still behind the truck
    // =====================================================================================

    [Fact]
    public void NoChipRowIsOnTheWireTonight()
    {
        // The wave bump is a separate PR on purpose: it also releases the three ticket and token
        // rows already sitting at wave 3, so it is an owner call rather than a side effect of
        // adding a shelf.
        Assert.Equal(2, ArcademyEconomy.CurrentWave);
        Assert.All(ChipRows(), c => Assert.False(ArcademyEconomy.InStock(c)));

        var projected = ArcademyEconomy.CatalogJson().OfType<JObject>()
            .Where(r => (string?)r["cur"] == ArcademyEconomy.CurChips)
            .ToList();
        Assert.Empty(projected);
    }

    [Fact]
    public void TheCounterDoesNotKnowAChipRowYetAndChargesNothing()
    {
        var w = new JObject { ["t"] = 5000, ["k"] = 5, ["c"] = 100000 };
        foreach (var sku in Rows().Select(r => (string)r[0]))
        {
            var r = ArcademyEconomy.Buy(w, sku, "2026-09-04");
            Assert.False(r.Ok);
            // "unknown" rather than "locked": naming an unreleased prize in a refusal spoils it.
            Assert.Equal("unknown", r.Reason);
        }
        Assert.Equal(100000, (int?)w["c"]);
        Assert.Equal(5000, (int?)w["t"]);
        Assert.Equal(5, (int?)w["k"]);
    }

    [Fact]
    public void TheHouseFavoriteIsWonAndNeverSold()
    {
        var row = ArcademyEconomy.Catalog.Single(c => c.Sku == "bk_house_favorite");
        Assert.Equal(0, row.Cost);
        Assert.True(row.Locked);
    }

    // =====================================================================================
    //  the words on the card (source tripwire - NeutralLexicon is private)
    // =====================================================================================

    private static string HostSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return File.ReadAllText(Path.Combine(dir!.FullName, "ConditioningControlPanel",
            "Services", "Arcademy", "ArcademyHostService.cs"));
    }

    [Fact]
    public void EveryChipRowHasItsNameAndBlurbInTheLexicon()
    {
        var source = HostSource();
        var table = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(source, "\\[\"(prize_bk_[a-z_]+)\"\\] = \"([^\"]*)\""))
            table[m.Groups[1].Value] = m.Groups[2].Value;

        foreach (var row in ChipRows())
        {
            Assert.True(table.ContainsKey(row.NameKey), "lexicon is missing " + row.NameKey);
            Assert.True(table.ContainsKey(row.BlurbKey), "lexicon is missing " + row.BlurbKey);
        }
        Assert.Equal(ChipRows().Count * 2, table.Count);
    }

    [Fact]
    public void NoChipRowStringRunsPastTheCardOrCarriesALineBreak()
    {
        var source = HostSource();
        foreach (Match m in Regex.Matches(source, "\\[\"(prize_bk_[a-z_]+)\"\\] = \"([^\"]*)\""))
        {
            var key = m.Groups[1].Value;
            var value = m.Groups[2].Value;
            Assert.True(value.Length < 96, key + " runs to " + value.Length + " chars");
            Assert.DoesNotContain("\\n", value, StringComparison.Ordinal);
        }
        // House rule: no em-dash or en-dash anywhere a player can read it.
        foreach (var row in ChipRows())
        {
            var words = row.NameEn + " " + row.BlurbEn;
            Assert.False(words.Contains('\u2014'), row.Sku + " carries an em-dash");
            Assert.False(words.Contains('\u2013'), row.Sku + " carries an en-dash");
        }
    }
}
