using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using ConditioningControlPanel.Services.Descent;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// THE RECEIPT (v6.9.1) — the permanent record of a one-night, irreversible choice.
///
/// <para>The bug these pin down: v6.9.0 shipped the Cycle bonus live in the ledger and invisible
/// on every screen. Five subjects on launch night made a choice that could not be undone and then
/// had nothing to look at, and a sixth would not choose at all because he could not see what the
/// door did. The fix is a chip, a suffix on the XP readout, and a sentence in the ceremony's
/// close — and all three of them read the SAME multiplier the ledger applies, which is the
/// property that must never rot.</para>
///
/// <para>Pure on purpose, like <see cref="DescentMigration.Resolve"/>: what receipt is owed and
/// what number it prints are decisions, not rendering.</para>
/// </summary>
public class DescentReceiptTests
{
    // ------------------------------------------------------ which receipt is owed

    [Fact]
    public void CompletedCycle_OwesTheCycleReceipt()
    {
        Assert.Equal(DescentReceiptKind.Cycle,
                     DescentReceipt.Resolve(true, DescentMigrationChoices.Cycle));
    }

    [Fact]
    public void CompletedRestore_OwesTheRestoreReceipt()
    {
        Assert.Equal(DescentReceiptKind.Restore,
                     DescentReceipt.Resolve(true, DescentMigrationChoices.Restore));
    }

    /// <summary>
    /// A fresh install, which is every account that never met the ceremony. Nothing is drawn, and
    /// nothing about the card measures differently than it did before this feature existed.
    /// </summary>
    [Fact]
    public void NeverMigrated_OwesNothing()
    {
        Assert.Equal(DescentReceiptKind.None, DescentReceipt.Resolve(false, null));
    }

    /// <summary>
    /// THE IN-FLIGHT CASE, and the reason completion outranks the choice. A choice applied
    /// locally but not yet acked lives in PendingDescentMigrationChoice; a receipt for it would be
    /// a promise the server has not made, and the ceremony re-offers after a crash precisely
    /// because that state is not final.
    /// </summary>
    [Theory]
    [InlineData(DescentMigrationChoices.Cycle)]
    [InlineData(DescentMigrationChoices.Restore)]
    public void ChosenButNotAcked_OwesNothing(string choice)
    {
        Assert.Equal(DescentReceiptKind.None, DescentReceipt.Resolve(false, choice));
    }

    /// <summary>A hand-edited settings file does not get to invent a third door.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("CYCLE")]
    [InlineData("descend")]
    public void AnUnrecognisedChoice_OwesNothing(string? choice)
    {
        Assert.Equal(DescentReceiptKind.None, DescentReceipt.Resolve(true, choice));
    }

    // ------------------------------------------------------ the number it prints

    /// <summary>
    /// THE CHIP AND THE CONSTANT ARE THE SAME NUMBER. CycleXpBonus is explicitly tunable
    /// (CONTRACTS §3 records that 1.10 is unblessed), so the receipt has to be derived from it
    /// rather than typed next to it — this is the assertion that catches a retune the copy forgot.
    /// </summary>
    [Fact]
    public void TheBlessedConstant_PrintsAsTen()
    {
        Assert.Equal("10", DescentReceipt.BonusPercentText(DescentMigration.CycleXpBonus));
    }

    [Fact]
    public void AFractionalBonus_KeepsOneDecimal()
    {
        var previous = System.Threading.Thread.CurrentThread.CurrentCulture;
        try
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            Assert.Equal("7.5", DescentReceipt.BonusPercentText(1.075));
            Assert.Equal("5", DescentReceipt.BonusPercentText(1.05));
        }
        finally
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    /// <summary>
    /// Junk floors at zero rather than printing a negative bonus. ActiveCycleXpBonus already
    /// clamps what the ledger applies; this is the display half of the same guard, and it exists
    /// because the chip must never be able to advertise a number the XP maths is not paying.
    /// </summary>
    [Theory]
    [InlineData(1.0)]
    [InlineData(0.5)]
    [InlineData(-4.0)]
    [InlineData(double.NaN)]
    public void ANonBonus_PrintsZero(double multiplier)
    {
        Assert.Equal("0", DescentReceipt.BonusPercentText(multiplier));
    }

    // ------------------------------------------------------ the XP readout suffix

    [Fact]
    public void OnlyACycleWithARealBonus_EarnsTheXpSuffix()
    {
        Assert.True(DescentReceipt.ShowsXpMultiplier(DescentReceiptKind.Cycle, 1.10));

        // Restore has no multiplier to advertise, and neither has a card nobody migrated.
        Assert.False(DescentReceipt.ShowsXpMultiplier(DescentReceiptKind.Restore, 1.10));
        Assert.False(DescentReceipt.ShowsXpMultiplier(DescentReceiptKind.None, 1.10));

        // A Cycle whose bonus went missing still gets the chip (the choice happened) but must not
        // put "(+0%)" on a number it is not moving.
        Assert.False(DescentReceipt.ShowsXpMultiplier(DescentReceiptKind.Cycle, 1.0));
        Assert.False(DescentReceipt.ShowsXpMultiplier(DescentReceiptKind.Cycle, double.NaN));
    }

    // ------------------------------------------------------ the ceremony's close

    /// <summary>
    /// THE SENTENCE THE WHOLE FIX HANGS ON. The close has to state the bonus and then say where
    /// it lives, because "it is on your card" is only useful if the card actually wears it.
    /// </summary>
    [Fact]
    public void TheCycleCloseStatesTheBonusAndNamesTheCard()
    {
        var body = DescentCeremonyCopy.DoneBody(DescentMigrationChoices.Cycle, 1);

        Assert.Contains("+10% XP", body, StringComparison.Ordinal);
        Assert.Contains("permanently", body, StringComparison.Ordinal);
        Assert.Contains("Profile card", body, StringComparison.Ordinal);
    }

    /// <summary>Restore has no bonus, so its close points at the record without inventing one.</summary>
    [Fact]
    public void TheRestoreCloseNamesTheCardAndPromisesNoBonus()
    {
        var body = DescentCeremonyCopy.DoneBody(DescentMigrationChoices.Restore, 117);

        Assert.Contains("Profile card", body, StringComparison.Ordinal);
        Assert.DoesNotContain("% XP", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The close's percent is derived, not typed. Same guard as
    /// <see cref="TheBlessedConstant_PrintsAsTen"/>, one layer up: the ceremony and the chip have
    /// to agree, and they can only agree by both reading the constant.
    /// </summary>
    [Fact]
    public void TheCloseAndTheChipQuoteTheSameNumber()
    {
        Assert.Contains(
            "+" + DescentReceipt.BonusPercentText(DescentMigration.CycleXpBonus) + "%",
            DescentCeremonyCopy.DoneReceiptLine(DescentMigrationChoices.Cycle),
            StringComparison.Ordinal);
    }

    // ------------------------------------------------------ the surface

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static string AppFile(params string[] parts)
        => File.ReadAllText(Path.Combine(RepoRoot(), Path.Combine("ConditioningControlPanel", Path.Combine(parts))));

    /// <summary>The element's own opening tag, so a Collapsed elsewhere cannot satisfy the scrape.</summary>
    private static string OpeningTag(string xaml, string name)
    {
        var start = xaml.IndexOf("x:Name=\"" + name + "\"", StringComparison.Ordinal);
        Assert.True(start >= 0, name + " is gone from the XAML - re-read the file before fixing this scrape");
        var end = xaml.IndexOf('>', start);
        Assert.True(end > start, name + "'s tag never closes - re-read the file, then fix the scrape");
        return xaml.Substring(start, end - start);
    }

    /// <summary>
    /// ZERO FOOTPRINT ON A CARD THAT IS OWED NOTHING — the same property the spiral plate and the
    /// vat bay carry. Flip the default and every un-migrated account, plus every stranger's card,
    /// grows an empty pill.
    /// </summary>
    [Fact]
    public void TheReceiptPillShipsCollapsed()
    {
        Assert.Contains("Visibility=\"Collapsed\"",
                        OpeningTag(AppFile("Views", "Tabs", "DiscordTabView.xaml"), "ProfileDescentReceipt"),
                        StringComparison.Ordinal);
    }

    /// <summary>
    /// The painter derives its percent from the multiplier the ledger applies. A literal here
    /// would survive a retune of CycleXpBonus and quietly start lying.
    /// </summary>
    [Fact]
    public void ThePainterReadsTheAppliedMultiplier()
    {
        var src = AppFile("MainWindow", "MainWindow.ProfileCard.cs");

        Assert.Contains("DescentMigration.ActiveCycleXpBonus", src, StringComparison.Ordinal);
        Assert.Contains("DescentReceipt.BonusPercentText", src, StringComparison.Ordinal);
    }

    // ------------------------------------------------------ localization

    private static readonly string[] Languages =
        { "en", "de", "es", "fr", "ja", "ko", "pt-BR", "ru", "zh-CN" };

    private static readonly string[] ReceiptKeys =
    {
        "profile_xp_progress_boosted",
        "profile_cycle_receipt_cycle",
        "profile_cycle_receipt_restore",
        "profile_cycle_receipt_tip_cycle",
        "profile_cycle_receipt_tip_restore",
    };

    /// <summary>
    /// All nine files, parsed STRICTLY (System.Text.Json, not Newtonsoft's leniency) — the exact
    /// check that caught the raw-newline breakage of 2026-07-29. A missing key here is a chip
    /// that renders its own key name at somebody.
    /// </summary>
    [Fact]
    public void EveryLanguageCarriesTheReceiptKeys()
    {
        foreach (var lang in Languages)
        {
            var path = Path.Combine(RepoRoot(), "ConditioningControlPanel", "Localization", "Languages", lang + ".json");
            using var doc = JsonDocument.Parse(File.ReadAllText(path));

            foreach (var key in ReceiptKeys)
            {
                Assert.True(doc.RootElement.TryGetProperty(key, out var value),
                            lang + ".json is missing " + key);
                Assert.False(string.IsNullOrWhiteSpace(value.GetString()),
                             lang + ".json has " + key + " blank");
            }
        }
    }

    /// <summary>
    /// Every placeholder survives translation. Loc.GetF is a plain string.Format with no
    /// diagnostics: a translator who drops "{0}" turns the live percent into silence, and the chip
    /// would read "Cycled: +% XP" with nothing anywhere to say why.
    /// </summary>
    [Fact]
    public void EveryLanguageKeepsThePlaceholders()
    {
        foreach (var lang in Languages)
        {
            var path = Path.Combine(RepoRoot(), "ConditioningControlPanel", "Localization", "Languages", lang + ".json");
            using var doc = JsonDocument.Parse(File.ReadAllText(path));

            Assert.Contains("{0}", doc.RootElement.GetProperty("profile_cycle_receipt_cycle").GetString()!, StringComparison.Ordinal);
            Assert.Contains("{0}", doc.RootElement.GetProperty("profile_cycle_receipt_tip_cycle").GetString()!, StringComparison.Ordinal);

            var boosted = doc.RootElement.GetProperty("profile_xp_progress_boosted").GetString()!;
            Assert.Contains("{0}", boosted, StringComparison.Ordinal);
            Assert.Contains("{1}", boosted, StringComparison.Ordinal);
            Assert.Contains("{2}", boosted, StringComparison.Ordinal);
        }
    }
}
