using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Descent;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// THE TWO DOORS, as arithmetic (CONTRACTS-0812 §2.5, design doc §6).
///
/// <see cref="DescentMigration.Resolve"/> is deliberately pure — no App, no settings, no clock —
/// because the crash-before-ack ordering the handshake depends on is only safe if re-running a
/// choice against an unchanged offer produces an unchanged answer. These tests are that claim,
/// written down.
/// </summary>
public class DescentMigrationChoiceTests
{
    private static DescentMigrationOffer Offer(double lifetimeXp, int devotionDays = 120) =>
        new() { TotalXpEarned = lifetimeXp, DevotionDays = devotionDays };

    // ------------------------------------------------------ "Take it all back"

    /// <summary>
    /// A v1 Level 150 veteran, re-measured. The drop is real and large, which is precisely why it
    /// happens inside a ceremony that states it rather than in a silent patch.
    /// </summary>
    [Fact]
    public void Restore_RederivesLevelFromTheServersLifetimeFigure()
    {
        var lifetime = ProgressionService.CumulativeXpToReachLevel(150, ProgressionService.CurveEpochLegacy);

        var result = DescentMigration.Resolve(DescentMigrationChoices.Restore, Offer(lifetime));

        Assert.Equal(117, result.Level);
        Assert.Equal(lifetime, result.LifetimeXp);
        Assert.Equal(lifetime, result.LedgerXp);   // lifetime is what rides the wire's xp field
    }

    /// <summary>
    /// The relevel is exact, not approximate: level plus progress-into-level must add back up to
    /// the lifetime figure the server handed us. If it did not, the ledger we submit would
    /// disagree with the ledger the server derives and the clamp would bite.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(799)]
    [InlineData(47_147)]
    [InlineData(193_750)]
    [InlineData(515_750)]
    [InlineData(8_135_340)]
    public void Restore_LevelPlusProgressReconstructsLifetimeXp(double lifetime)
    {
        var result = DescentMigration.Resolve(DescentMigrationChoices.Restore, Offer(lifetime));

        var floor = ProgressionService.CumulativeXpToReachLevel(result.Level, ProgressionService.CurveEpochDescent);
        Assert.Equal(lifetime, floor + result.XpIntoLevel);
    }

    /// <summary>Nobody in the honeymoon moves. A Level 30 subject walks out a Level 30 subject.</summary>
    [Fact]
    public void Restore_LeavesTheHoneymoonUntouched()
    {
        var lifetime = ProgressionService.CumulativeXpToReachLevel(30, ProgressionService.CurveEpochLegacy);
        var result = DescentMigration.Resolve(DescentMigrationChoices.Restore, Offer(lifetime));

        Assert.Equal(30, result.Level);
    }

    // --------------------------------------------------------- "Descend again"

    /// <summary>
    /// Cycle is Level 1 / 0 XP — and lifetime XP survives it intact. §2.5 exempts
    /// total_xp_earned from the one sanctioned downward write, and §6 says a Cycle "wipes nothing
    /// else". A Cycle that ate lifetime XP would also quietly eat the user's Chapter standing.
    /// </summary>
    [Fact]
    public void Cycle_IsLevelOneAndKeepsLifetimeXp()
    {
        var lifetime = ProgressionService.CumulativeXpToReachLevel(150, ProgressionService.CurveEpochLegacy);

        var result = DescentMigration.Resolve(DescentMigrationChoices.Cycle, Offer(lifetime));

        Assert.Equal(1, result.Level);
        Assert.Equal(0, result.XpIntoLevel);
        Assert.Equal(lifetime, result.LifetimeXp);
    }

    // ------------------------------------------------------------- idempotence

    /// <summary>
    /// THE ORDERING GUARANTEE. Crash after applying a choice but before the server acks, and the
    /// ceremony re-offers against the same untouched lifetime figure. Re-running must land in the
    /// same place, or the "nothing is lost in any ordering" claim in the handshake is false.
    /// </summary>
    [Theory]
    [InlineData(DescentMigrationChoices.Restore)]
    [InlineData(DescentMigrationChoices.Cycle)]
    public void Resolve_IsIdempotent(string choice)
    {
        var offer = Offer(515_750);

        var first = DescentMigration.Resolve(choice, offer);
        var second = DescentMigration.Resolve(choice, offer);

        Assert.Equal(first, second);
    }

    /// <summary>
    /// And switching horses mid-crash is safe too: a user who applied Cycle, crashed, and picks
    /// Restore on the re-offer still gets the level their untouched lifetime XP buys. The anchor
    /// is the server's lifetime figure, which no client action moves.
    /// </summary>
    [Fact]
    public void Resolve_ARestoreAfterAnUnackedCycleStillRestoresFully()
    {
        var offer = Offer(ProgressionService.CumulativeXpToReachLevel(150, ProgressionService.CurveEpochLegacy));

        DescentMigration.Resolve(DescentMigrationChoices.Cycle, offer);
        var restore = DescentMigration.Resolve(DescentMigrationChoices.Restore, offer);

        Assert.Equal(117, restore.Level);
    }

    // -------------------------------------------------- the veteran credit basis

    /// <summary>
    /// THE VETERAN CREDIT (owner ruling 2026-08-16). The server prices Option A on
    /// <c>restore_basis_xp</c> — lifetime plus 300 XP per witnessed devotion day — and the offer
    /// carries the computed figure so this client never re-derives the credit with a constant
    /// that could go stale. When the basis is present, it is the relevel's ONLY input.
    /// </summary>
    [Fact]
    public void Restore_PricesTheServersBasisWhenItIsPresent()
    {
        var lifetime = ProgressionService.CumulativeXpToReachLevel(30, ProgressionService.CurveEpochLegacy);
        var basis = ProgressionService.CumulativeXpToReachLevel(50, ProgressionService.CurveEpochDescent);
        var offer = new DescentMigrationOffer
            { TotalXpEarned = lifetime, DevotionDays = 180, RestoreBasisXp = basis };

        var result = DescentMigration.Resolve(DescentMigrationChoices.Restore, offer);

        Assert.Equal(50, result.Level);
    }

    /// <summary>
    /// The ledger xp written home is the BASIS, not the raw lifetime. The next sync's level/XP
    /// consistency check re-derives level from xp under curve v2 — a level bought by the credit
    /// but an xp without it would be demoted right back one sync later.
    /// </summary>
    [Fact]
    public void Restore_TheLedgerCarriesTheBasisSoTheNextSyncAgreesWithIt()
    {
        var offer = new DescentMigrationOffer
            { TotalXpEarned = 16_343, DevotionDays = 180, RestoreBasisXp = 70_343 };

        var result = DescentMigration.Resolve(DescentMigrationChoices.Restore, offer);

        Assert.Equal(70_343, result.LedgerXp);
        var floor = ProgressionService.CumulativeXpToReachLevel(result.Level, ProgressionService.CurveEpochDescent);
        Assert.Equal(70_343, floor + result.XpIntoLevel);
    }

    /// <summary>An older server sends no basis (0), and the relevel is the pre-credit arithmetic
    /// exactly — derived from the lifetime figure alone.</summary>
    [Fact]
    public void Restore_FallsBackToLifetimeWhenTheServerSentNoBasis()
    {
        var lifetime = ProgressionService.CumulativeXpToReachLevel(30, ProgressionService.CurveEpochLegacy);

        var withBasisZero = DescentMigration.Resolve(DescentMigrationChoices.Restore,
            new DescentMigrationOffer { TotalXpEarned = lifetime, RestoreBasisXp = 0 });

        Assert.Equal(30, withBasisZero.Level);
        Assert.Equal(lifetime, withBasisZero.LedgerXp);
    }

    /// <summary>The credit never rides the Cycle door: level 1, xp 0, lifetime carried through
    /// RAW — a Cycle's keepsake records what was witnessed, not what a restore would have paid.</summary>
    [Fact]
    public void Cycle_IgnoresTheBasisEntirely()
    {
        var result = DescentMigration.Resolve(DescentMigrationChoices.Cycle,
            new DescentMigrationOffer { TotalXpEarned = 16_343, RestoreBasisXp = 70_343 });

        Assert.Equal(1, result.Level);
        Assert.Equal(16_343, result.LifetimeXp);
    }

    /// <summary>A junk basis (NaN, negative) degrades to the lifetime fallback, never to a throw
    /// and never to level 0.</summary>
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(-500)]
    public void Restore_TreatsAJunkBasisAsAbsent(double basis)
    {
        var lifetime = ProgressionService.CumulativeXpToReachLevel(30, ProgressionService.CurveEpochLegacy);
        var result = DescentMigration.Resolve(DescentMigrationChoices.Restore,
            new DescentMigrationOffer { TotalXpEarned = lifetime, RestoreBasisXp = basis });

        Assert.Equal(30, result.Level);
    }

    // ------------------------------------------------------------ hostile input

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void Resolve_TreatsImpossibleLifetimeFiguresAsZero(double lifetime)
    {
        var result = DescentMigration.Resolve(DescentMigrationChoices.Restore, Offer(lifetime));

        Assert.Equal(1, result.Level);
        Assert.Equal(0, result.LifetimeXp);
    }

    /// <summary>
    /// Anything that is not one of the two exact wire strings must resolve as a restore, never as
    /// a cycle. Restore is the conservative branch — it keeps the standing — so an unknown choice
    /// falling through it cannot cost anybody a level. (The submit path rejects invalid choices
    /// outright; this is the second line.)
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("CYCLE")]
    [InlineData("Cycle")]
    [InlineData("reset")]
    public void Resolve_UnknownChoicesFallToTheConservativeBranch(string choice)
    {
        Assert.False(DescentMigrationChoices.IsValid(choice));

        var lifetime = ProgressionService.CumulativeXpToReachLevel(100, ProgressionService.CurveEpochLegacy);
        var result = DescentMigration.Resolve(choice, Offer(lifetime));

        Assert.Equal(86, result.Level);   // the restore answer, not level 1
    }

    [Fact]
    public void Choices_AreExactLowercaseWireStrings()
    {
        Assert.Equal("restore", DescentMigrationChoices.Restore);
        Assert.Equal("cycle", DescentMigrationChoices.Cycle);
        Assert.True(DescentMigrationChoices.IsValid("restore"));
        Assert.True(DescentMigrationChoices.IsValid("cycle"));
        Assert.False(DescentMigrationChoices.IsValid(null));
    }

    // ------------------------------------------------------------- the tunable

    /// <summary>
    /// The Cycle XP bonus is UNBLESSED (CONTRACTS §3: the owner has signed off on "there is a
    /// lasting bonus", not on 1.10). This test does not defend the number — it defends the shape:
    /// a bonus, above 1.0, sane, and reflected verbatim in the ceremony copy so tuning the
    /// constant tunes what the user is promised.
    /// </summary>
    [Fact]
    public void CycleXpBonus_IsABonusAndTheCopyQuotesIt()
    {
        Assert.True(DescentMigration.CycleXpBonus > 1.0);
        Assert.True(DescentMigration.CycleXpBonus <= 1.5);

        var expectedPct = $"{(DescentMigration.CycleXpBonus - 1.0) * 100:0.#}%";
        Assert.Contains(expectedPct, DescentCeremonyCopy.CycleBonusLine());
    }

    // ------------------------------------------------------- the ceremony's mouth

    /// <summary>
    /// CONTRACTS §0.6: no offers, no popups, no upsell anywhere near this flow. Cheap to assert,
    /// and the failure mode it guards against — someone dropping a "go Tier 2 to keep your level"
    /// line onto the most emotionally loaded screen in the app — is expensive.
    /// </summary>
    [Theory]
    [InlineData("patreon")]
    [InlineData("subscribe")]
    [InlineData("upgrade")]
    [InlineData("tier")]
    [InlineData("$")]
    [InlineData("purchase")]
    [InlineData("premium")]
    [InlineData("discount")]
    [InlineData("limited time")]
    public void CeremonyCopy_ContainsNoOffer(string forbidden)
    {
        var everything = string.Join("\n",
            DescentCeremonyCopy.IntroHeadline,
            DescentCeremonyCopy.IntroBody,
            DescentCeremonyCopy.IntroContinue,
            DescentCeremonyCopy.IntroStanding(150, 515750, 300),
            DescentCeremonyCopy.ChoiceHeadline,
            DescentCeremonyCopy.RestoreTitle,
            DescentCeremonyCopy.RestoreKicker,
            DescentCeremonyCopy.RestoreBody(150, 117),
            DescentCeremonyCopy.RestoreDelta(150, 117),
            DescentCeremonyCopy.CycleTitle,
            DescentCeremonyCopy.CycleKicker,
            DescentCeremonyCopy.CycleBody(),
            DescentCeremonyCopy.CycleBonusLine(),
            DescentCeremonyCopy.BothDoorsFooter,
            DescentCeremonyCopy.ConfirmHeadline,
            DescentCeremonyCopy.ConfirmBody(DescentMigrationChoices.Restore),
            DescentCeremonyCopy.ConfirmBody(DescentMigrationChoices.Cycle),
            DescentCeremonyCopy.ConfirmYes(DescentMigrationChoices.Restore),
            DescentCeremonyCopy.ConfirmYes(DescentMigrationChoices.Cycle),
            DescentCeremonyCopy.ConfirmBack,
            DescentCeremonyCopy.DoneHeadline,
            DescentCeremonyCopy.DoneBody(DescentMigrationChoices.Restore, 117),
            DescentCeremonyCopy.DoneBody(DescentMigrationChoices.Cycle, 1),
            DescentCeremonyCopy.DoneClose,
            DescentCeremonyCopy.Later,
            DescentCeremonyCopy.LaterHint,
            DescentCeremonyCopy.CompanionIntro,
            DescentCeremonyCopy.CompanionDone(DescentMigrationChoices.Restore),
            DescentCeremonyCopy.CompanionDone(DescentMigrationChoices.Cycle));

        Assert.DoesNotContain(forbidden, everything, System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The confirm step must say the one-way part in words, not imply it (§4).</summary>
    [Theory]
    [InlineData(DescentMigrationChoices.Restore)]
    [InlineData(DescentMigrationChoices.Cycle)]
    public void ConfirmCopy_StatesThatThereIsNoUndo(string choice)
    {
        Assert.Contains("no undo", DescentCeremonyCopy.ConfirmBody(choice), System.StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(1, "I")]
    [InlineData(7, "VII")]
    [InlineData(9, "IX")]
    [InlineData(42, "42")]
    public void RomanNumeral_CoversTheLadderAndFallsBackPastIt(int n, string expected)
    {
        Assert.Equal(expected, DescentCeremonyCopy.RomanNumeral(n));
    }
}
