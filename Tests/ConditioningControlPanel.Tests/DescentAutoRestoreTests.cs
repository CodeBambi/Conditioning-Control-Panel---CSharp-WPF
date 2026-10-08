using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Descent;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// THE CEREMONY IS RETIRED (owner, 2026-10-06): "it passed enough time, we can remove the descent
/// choice and give the 10% boost to anyone that comes back".
///
/// <para>Pinned here: an offer applies the RESTORE ledger at once with no window; every migrated
/// account (restore, cycle, auto) gets the XP bonus and an unmigrated one gets 1.0; the sync body
/// says <c>descent_auto: true</c> so the server offers only to builds like this one; and the offer
/// path builds no fullscreen window of any kind.</para>
/// </summary>
public class DescentAutoRestoreTests
{
    private static DescentMigrationOffer AnOffer() =>
        new() { TotalXpEarned = 120_000, DevotionDays = 240, RestoreBasisXp = 150_000 };

    // ------------------------------------------------------------ the offer

    [Fact]
    public void AnOffer_AppliesTheRestoreAtOnce_WithNoWindow()
    {
        var s = new AppSettings { PlayerLevel = 40, PlayerXP = 10 };
        var service = new DescentMigrationService(() => s);

        service.OfferReceived(AnOffer());

        var expected = DescentMigration.Resolve(DescentMigrationChoices.Restore, AnOffer());
        Assert.Equal(DescentMigrationChoices.Restore, s.PendingDescentMigrationChoice);
        Assert.Equal(DescentEpochs.AccountDescent, s.DescentEpoch);
        Assert.Equal(expected.Level, s.PlayerLevel);
        Assert.Equal(expected.XpIntoLevel, s.PlayerXP, 6);
        Assert.Equal(40, s.DescentPreMigrationLevel);
        Assert.Equal(0, s.DescentCycle);                 // Cycle I stays the Cycle door's
        Assert.False(s.DescentMigrationOffered);
        Assert.False(service.IsCeremonyOpen);
    }

    [Fact]
    public void AnAlreadyMigratedAccount_IsNotReleveled()
    {
        var s = new AppSettings { PlayerLevel = 77, DescentMigrationCompleted = true };
        var service = new DescentMigrationService(() => s);

        service.OfferReceived(AnOffer());

        Assert.Equal(77, s.PlayerLevel);
        Assert.Null(s.PendingDescentMigrationChoice);
    }

    [Fact]
    public void AChoiceAlreadyPending_IsNotOverwritten()
    {
        var s = new AppSettings { PlayerLevel = 1, PendingDescentMigrationChoice = DescentMigrationChoices.Cycle };
        var service = new DescentMigrationService(() => s);

        service.OfferReceived(AnOffer());

        Assert.Equal(DescentMigrationChoices.Cycle, s.PendingDescentMigrationChoice);
        Assert.Equal(1, s.PlayerLevel);
    }

    // ------------------------------------------------------------ the bonus

    [Theory]
    [InlineData("restore")]
    [InlineData("cycle")]
    public void EveryDoor_WritesAndEarnsTheBonus(string choice)
    {
        var s = new AppSettings { PlayerLevel = 40 };
        var service = new DescentMigrationService(() => s);

        Assert.True(service.ApplyChoice(choice, AnOffer()));

        Assert.Equal(DescentMigration.CycleXpBonus, s.DescentCycleXpBonus);
        Assert.Equal(DescentMigration.CycleXpBonus, DescentMigration.XpBonusFor(s));
        Assert.Equal(choice == DescentMigrationChoices.Cycle ? 1 : 0, s.DescentCycle);
    }

    [Fact]
    public void TheAutoRestore_EarnsTheBonus()
    {
        var s = new AppSettings { PlayerLevel = 40 };
        var service = new DescentMigrationService(() => s);

        service.OfferReceived(AnOffer());

        Assert.Equal(DescentMigration.CycleXpBonus, DescentMigration.XpBonusFor(s));
    }

    [Fact]
    public void AnAckedMigrationFromAnotherDevice_EarnsTheBonus()
    {
        var restored = new AppSettings
        {
            DescentMigrationCompleted = true,
            DescentMigrationChoice = DescentMigrationChoices.Restore,
            DescentCycleXpBonus = 1.0,
        };
        Assert.Equal(DescentMigration.CycleXpBonus, DescentMigration.XpBonusFor(restored));
    }

    [Fact]
    public void AnUnmigratedAccount_GetsNoBonus_EvenWithAHandEditedField()
    {
        Assert.Equal(1.0, DescentMigration.XpBonusFor(null));
        Assert.Equal(1.0, DescentMigration.XpBonusFor(new AppSettings()));
        Assert.Equal(1.0, DescentMigration.XpBonusFor(new AppSettings { DescentCycleXpBonus = 50 }));
        Assert.Equal(1.0, DescentMigration.XpBonusFor(new AppSettings { PendingDescentMigrationChoice = "maybe" }));
        // A fresh post-Descent signup carries curve_epoch 1 and never came back from anything.
        Assert.Equal(1.0, DescentMigration.XpBonusFor(new AppSettings { DescentEpoch = DescentEpochs.AccountDescent }));
    }

    [Fact]
    public void AMigratedAccount_NeverEarnsMoreThanTheConstant()
    {
        var s = new AppSettings { DescentMigrationCompleted = true, DescentCycleXpBonus = 50 };
        Assert.Equal(DescentMigration.CycleXpBonus, DescentMigration.XpBonusFor(s));
    }

    // ------------------------------------------------------------ the wire

    /// <summary>
    /// <c>DescentAuto = true</c> (wire key <c>descent_auto</c>) rides the sync body beside the epoch, unconditional. Without it
    /// the server offers nothing, so this one line is the whole difference between "migrated
    /// silently" and "never migrated".
    /// </summary>
    [Fact]
    public void TheSyncBody_SaysDescentAuto_BesideTheEpoch()
    {
        var src = AppFile("Services", "Settings", "ProfileSyncService.cs");

        var epoch = src.IndexOf("DescentEpoch = DescentEpochs.ClientEpoch,", StringComparison.Ordinal);
        Assert.True(epoch >= 0, "descent_epoch is gone from the sync body - re-read the builder");
        var auto = src.IndexOf("DescentAuto = true,", epoch, StringComparison.Ordinal);
        Assert.True(auto > epoch && auto - epoch < 1200, "descent_auto must sit beside descent_epoch in the same body");
    }

    // ------------------------------------------------------------ no window

    /// <summary>
    /// NO FULLSCREEN SURFACE OPENS FROM A SYNC. The service that takes the offer constructs no
    /// window at all, and shows nothing.
    /// </summary>
    [Fact]
    public void TheOfferPath_ConstructsNoWindow()
    {
        var src = StripComments(AppFile("Services", "Descent", "DescentMigrationService.cs"));

        Assert.DoesNotMatch(new Regex(@"new\s+\w*Window\s*\("), src);
        Assert.DoesNotContain(".Show()", src, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowDialog", src, StringComparison.Ordinal);
        Assert.DoesNotContain("DescentCeremonyWindow", src, StringComparison.Ordinal);
    }

    /// <summary>The fuse director no longer lights the ignition off a ceremony close.</summary>
    [Fact]
    public void TheDirector_HasNoCeremonyCloseHook()
    {
        var src = StripComments(AppFile("Services", "Descent", "DescentShowDirector.cs"));
        Assert.DoesNotContain("CeremonyClosed", src, StringComparison.Ordinal);
        Assert.DoesNotContain("DescentShowKind.Ignition", src, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------ helpers

    private static string StripComments(string src) =>
        string.Join("\n", src.Split('\n').Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    /// <summary>Through SourceRoots, so a move into CCP.Core keeps this scan covering the file.</summary>
    private static string AppFile(params string[] parts) => SourceRoots.ReadProductFile(parts);
}
