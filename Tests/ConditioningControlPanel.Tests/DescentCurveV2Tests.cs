using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Descent;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// CURVE v2 — THE RECURVE, pinned to the sim (CONTRACTS-0812 §3, planning/descent-sim4.py
/// <c>cost_hybrid</c>).
///
/// These numbers are not "what the code happens to produce". They are the numbers the SERVER
/// implements independently, and §2.5 has the server re-derive a migrating client's level and
/// clamp the client's claim to within one level of its own answer. A one-XP drift here is a
/// one-level disagreement at a boundary and a clamped ceremony for a real person, so the whole
/// table is nailed down rather than spot-checked.
///
/// Three properties matter more than any individual figure:
///   1. L1-40 is BYTE-IDENTICAL to v1. The honeymoon is protected on purpose; a subject who has
///      not yet passed 40 must feel literally nothing change.
///   2. The relevel is monotonic and idempotent — more lifetime XP never buys a lower level, and
///      deriving twice gives the same answer. That is what makes a ceremony that crashes
///      mid-flight safe to re-run.
///   3. Nothing on curve v2 is reachable without an explicit epoch argument. There is no way to
///      get a v2 number by accident.
/// </summary>
public class DescentCurveV2Tests
{
    private const int V1 = ProgressionService.CurveEpochLegacy;
    private const int V2 = ProgressionService.CurveEpochDescent;

    // ---------------------------------------------------------------- the table

    /// <summary>Every segment boundary of the sim's cost_hybrid, plus a point inside each.</summary>
    [Theory]
    [InlineData(1, 800)]        // segment 1 (== v1)
    [InlineData(2, 822)]
    [InlineData(10, 994)]
    [InlineData(25, 1316)]
    [InlineData(40, 1639)]      // last honeymoon level
    [InlineData(41, 1703)]      // segment 2 starts: 1639 -> 4200 over 40 levels
    [InlineData(50, 2279)]
    [InlineData(60, 2920)]
    [InlineData(80, 4200)]
    [InlineData(81, 4460)]      // segment 3: +260/level
    [InlineData(100, 9400)]
    [InlineData(101, 9840)]     // segment 4: +440/level
    [InlineData(125, 20400)]
    [InlineData(126, 21600)]    // segment 5: +1200/level
    [InlineData(150, 50400)]
    [InlineData(151, 52164)]    // segment 6: x1.035 compounding
    [InlineData(175, 119108)]
    [InlineData(200, 281480)]
    public void XpForLevelV2_MatchesTheSim(int level, double expected)
    {
        Assert.Equal(expected, ProgressionService.XpForLevelV2(level));
    }

    /// <summary>
    /// THE HONEYMOON IS PROTECTED. Levels 1-40 must cost exactly what they have always cost —
    /// this is the single most user-visible promise the recurve makes.
    /// </summary>
    [Fact]
    public void Levels1To40_AreIdenticalOnBothCurves()
    {
        for (int level = 1; level <= 40; level++)
        {
            Assert.Equal(ProgressionService.XpForLevelV1(level), ProgressionService.XpForLevelV2(level));
        }
    }

    /// <summary>...and 41 onwards must not be, or the recurve did nothing.</summary>
    [Fact]
    public void Level41Onwards_IsStrictlyDearerOnV2()
    {
        for (int level = 41; level <= 250; level++)
        {
            Assert.True(ProgressionService.XpForLevelV2(level) > ProgressionService.XpForLevelV1(level),
                $"Level {level} should cost more on curve v2");
        }
    }

    /// <summary>A curve that ever gets cheaper as it deepens is a curve with a farming exploit in it.</summary>
    [Fact]
    public void BothCurves_AreMonotonicallyIncreasing()
    {
        for (int level = 2; level <= 300; level++)
        {
            Assert.True(ProgressionService.XpForLevelV2(level) >= ProgressionService.XpForLevelV2(level - 1));
            Assert.True(ProgressionService.XpForLevelV1(level) >= ProgressionService.XpForLevelV1(level - 1));
        }
    }

    // ------------------------------------------------------------ the dispatcher

    [Fact]
    public void GetXPForLevel_DispatchesOnEpoch()
    {
        Assert.Equal(ProgressionService.XpForLevelV1(100), ProgressionService.GetXPForLevel(100, V1));
        Assert.Equal(ProgressionService.XpForLevelV2(100), ProgressionService.GetXPForLevel(100, V2));

        // Anything that is not the Descent epoch is legacy. A garbage epoch must not silently
        // recurve somebody.
        Assert.Equal(ProgressionService.XpForLevelV1(100), ProgressionService.GetXPForLevel(100, -7));
    }

    // -------------------------------------------------------------- cumulatives

    /// <summary>
    /// Cumulative totals at the levels the ceremony's relevel actually lands on. Same source, and
    /// the same figures the server sums to clamp with.
    /// </summary>
    [Theory]
    [InlineData(10, 7976, 7976)]
    [InlineData(25, 25140, 25140)]
    [InlineData(40, 47147, 47147)]        // still identical
    [InlineData(50, 64507, 66417)]        // and here the curves part
    [InlineData(100, 193750, 296047)]
    [InlineData(150, 515750, 1533047)]
    [InlineData(200, 1643720, 8135340)]
    public void CumulativeXpToReachLevel_MatchesTheSim(int level, double v1Total, double v2Total)
    {
        Assert.Equal(v1Total, ProgressionService.CumulativeXpToReachLevel(level, V1));
        Assert.Equal(v2Total, ProgressionService.CumulativeXpToReachLevel(level, V2));
    }

    // ------------------------------------------------------------- the relevel

    /// <summary>
    /// THE RELEVEL, at the levels people are actually standing at. A veteran at v1 L150 comes out
    /// of the ceremony at v2 L117 — a 33-level drop, which is exactly the number the ceremony has
    /// to say out loud before anyone agrees to it (design doc §13: "both migration options
    /// re-derive level explicitly, so the relevel is part of the ceremony instead of a silent
    /// shift").
    /// </summary>
    [Theory]
    [InlineData(10, 10)]
    [InlineData(25, 25)]
    [InlineData(40, 40)]    // honeymoon: nobody below 41 moves at all
    [InlineData(50, 49)]
    [InlineData(60, 57)]
    [InlineData(75, 68)]
    [InlineData(100, 86)]
    [InlineData(125, 102)]
    [InlineData(150, 117)]
    [InlineData(175, 133)]
    [InlineData(200, 152)]
    public void Restore_RelevelsALegacyStandingOntoCurveV2(int legacyLevel, int expectedV2Level)
    {
        var lifetime = ProgressionService.CumulativeXpToReachLevel(legacyLevel, V1);
        var (level, _) = ProgressionService.DeriveLevelFromLifetimeXp(lifetime, V2);
        Assert.Equal(expectedV2Level, level);
    }

    /// <summary>Derive is the exact inverse of cumulate: land ON a boundary, stand at that level with 0 into it.</summary>
    [Fact]
    public void DeriveLevelFromLifetimeXp_IsTheInverseOfCumulative()
    {
        for (int level = 1; level <= 200; level++)
        {
            var lifetime = ProgressionService.CumulativeXpToReachLevel(level, V2);
            var (derived, into) = ProgressionService.DeriveLevelFromLifetimeXp(lifetime, V2);
            Assert.Equal(level, derived);
            Assert.Equal(0, into);
        }
    }

    /// <summary>One XP short of a boundary is still the level below it, with the whole level banked.</summary>
    [Fact]
    public void DeriveLevelFromLifetimeXp_IsExactAtTheBoundaryMinusOne()
    {
        var lifetime = ProgressionService.CumulativeXpToReachLevel(100, V2) - 1;
        var (level, into) = ProgressionService.DeriveLevelFromLifetimeXp(lifetime, V2);

        Assert.Equal(99, level);
        Assert.Equal(ProgressionService.XpForLevelV2(99) - 1, into);
    }

    /// <summary>More lifetime XP can never buy a lower level. Sanity that outranks any single figure.</summary>
    [Fact]
    public void DeriveLevelFromLifetimeXp_IsMonotonic()
    {
        int last = 0;
        for (double xp = 0; xp < 3_000_000; xp += 7_919)   // prime step, so no boundary is favoured
        {
            var (level, _) = ProgressionService.DeriveLevelFromLifetimeXp(xp, V2);
            Assert.True(level >= last, $"level went backwards at {xp} XP");
            last = level;
        }
    }

    /// <summary>Garbage in, Level 1 out — never a hang, never a negative level.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-999999)]
    [InlineData(double.NaN)]
    public void DeriveLevelFromLifetimeXp_FloorsAtLevelOne(double lifetime)
    {
        var (level, into) = ProgressionService.DeriveLevelFromLifetimeXp(lifetime, V2);
        Assert.Equal(1, level);
        Assert.Equal(0, into);
    }

    /// <summary>
    /// A lifetime figure no human could earn must terminate at the cap rather than spinning. The
    /// derive loop is the one place a corrupt server figure could hang the ceremony.
    /// </summary>
    [Fact]
    public void DeriveLevelFromLifetimeXp_TerminatesAtTheCap()
    {
        var (level, _) = ProgressionService.DeriveLevelFromLifetimeXp(double.MaxValue, V2);
        Assert.Equal(ProgressionService.MaxDerivableLevel, level);
    }

    // ------------------------------------------------------------ quest scaling

    /// <summary>
    /// Quest scaling moves with the curve: +4%/level before the ceremony, +1.2%/level after. The
    /// audit that produced this number found 74% of a casual's year coming from quest claims,
    /// with "log in, claim, quit" out-earning playing.
    /// </summary>
    [Theory]
    [InlineData(1, 1.04, 1.012)]
    [InlineData(50, 3.0, 1.6)]
    [InlineData(100, 5.0, 2.2)]
    [InlineData(150, 7.0, 2.8)]
    public void QuestLevelScale_MovesWithTheCurve(int level, double v1Scale, double v2Scale)
    {
        Assert.Equal(v1Scale, ProgressionService.QuestLevelScale(level, V1), 6);
        Assert.Equal(v2Scale, ProgressionService.QuestLevelScale(level, V2), 6);
    }

    /// <summary>The §3 prose figure: a 600-XP quest at L100 pays 1,320 post-migration.</summary>
    [Fact]
    public void QuestBase600_PaysTheDesignDocFigure()
    {
        Assert.Equal(1320, 600 * ProgressionService.QuestLevelScale(100, V2), 6);
    }

    // ------------------------------------------------------ the epoch constants

    /// <summary>
    /// The build's wire epoch and the account's curve epoch are the same integer and NOT the same
    /// idea. This pins them apart: if someone ever "simplifies" one into the other, every user of
    /// the new build gets recurved on install instead of at their ceremony.
    /// </summary>
    [Fact]
    public void EpochConstants_AreDistinctConcepts()
    {
        Assert.Equal(1, DescentEpochs.ClientEpoch);          // sent on every sync, always
        Assert.Equal(0, DescentEpochs.AccountLegacy);        // where every account starts
        Assert.Equal(1, DescentEpochs.AccountDescent);       // where the ceremony leaves it
        Assert.Equal(DescentEpochs.AccountLegacy, ProgressionService.CurveEpochLegacy);
        Assert.Equal(DescentEpochs.AccountDescent, ProgressionService.CurveEpochDescent);
    }
}
