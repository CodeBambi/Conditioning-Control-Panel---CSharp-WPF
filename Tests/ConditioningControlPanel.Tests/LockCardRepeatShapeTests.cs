using System.Collections.Generic;
using System.Linq;
using Xunit;
using static ConditioningControlPanel.Services.LockCardService;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// v6.9.4 lock-card QoL, from two Discord asks: a repeat count that can be rolled instead of
/// pinned, and a card sized by how much TYPING it costs instead of by a count. Both land in one
/// pure resolver, so the precedence between them and the defaults that must not move are covered
/// here rather than in a window that cannot be realized headlessly.
/// </summary>
public class LockCardRepeatShapeTests
{
    // Today's shipping defaults: both switches off, repeats 3.
    private static int Default(double roll, string phrase = "GOOD GIRLS OBEY")
        => ResolveRepeats(-1, phrase, false, 1, 3, false, 120, 20, roll);

    // ── the default must not move ───────────────────────────────────────────

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(0.9999999)]
    public void DefaultsAreTheFlatCount_WhateverTheRoll(double roll)
        => Assert.Equal(3, Default(roll));

    [Fact]
    public void ExplicitRepeatsWinOverEveryMode()
    {
        // A Goon round, MantraLockScreenCommand and the AI all pass an exact count. Neither new
        // mode may quietly rewrite it, or a scripted round stops being scripted.
        Assert.Equal(7, ResolveRepeats(7, "SHORT", true, 1, 10, true, 600, 200, 0.5));
        // Zero keeps its pre-existing meaning too: >= 0 is "the caller decided".
        Assert.Equal(0, ResolveRepeats(0, "SHORT", true, 1, 10, true, 600, 200, 0.5));
    }

    // ── random repeats (Kuhaku) ─────────────────────────────────────────────

    [Fact]
    public void RandomRepeats_CoversTheWholeRangeAndNeverLeavesIt()
    {
        var seen = new HashSet<int>();
        for (var i = 0; i < 1000; i++)
        {
            var n = ResolveRepeats(-1, "GOOD GIRLS OBEY", true, 2, 5, false, 120, 20, i / 1000.0);
            Assert.InRange(n, 2, 5);
            seen.Add(n);
        }
        Assert.Equal(new[] { 2, 3, 4, 5 }, seen.OrderBy(x => x).ToArray());
    }

    [Theory]
    [InlineData(1.0)]      // a roll of exactly 1.0 overshoots every arithmetic path by one
    [InlineData(1.5)]
    [InlineData(double.NaN)]
    public void RandomRepeats_SurvivesAnOutOfContractRoll(double roll)
        => Assert.InRange(ResolveRepeats(-1, "GOOD GIRLS OBEY", true, 2, 5, false, 120, 20, roll), 2, 5);

    [Fact]
    public void RandomRepeats_TolerateAnInvertedRange()
    {
        // Nothing forces the two sliders to stay ordered, so a floor above the ceiling must sort
        // itself out rather than throw out of Random.Next or return a negative count.
        for (var i = 0; i < 100; i++)
            Assert.InRange(ResolveRepeats(-1, "PHRASE", true, 8, 3, false, 120, 20, i / 100.0), 3, 8);
    }

    [Fact]
    public void RandomRepeats_WithAOneWideRangeIsJustThatNumber()
        => Assert.Equal(4, ResolveRepeats(-1, "PHRASE", true, 4, 4, false, 120, 20, 0.9999999));

    // ── match by length (Wobberjockey) ──────────────────────────────────────

    [Fact]
    public void ByLength_EqualisesTheWorkAcrossPhraseLengths()
    {
        const string shortPhrase = "DROP FOR ME";                                   // 11 chars
        const string longPhrase = "I AM EMPTY AND OBEDIENT AND I LOVE BEING PROGRAMMED"; // 51 chars

        // Zero variance so the budget is exactly the target and the comparison is about length.
        var shortReps = ResolveRepeats(-1, shortPhrase, false, 1, 3, true, 120, 0, 0.5);
        var longReps = ResolveRepeats(-1, longPhrase, false, 1, 3, true, 120, 0, 0.5);

        Assert.Equal(11, shortReps);   // ceil(120/11)
        Assert.Equal(3, longReps);     // ceil(120/51)

        // The point of the feature: both cards now cost about the same keystrokes, where the flat
        // count of 3 would have made the short phrase a fifth of the work.
        Assert.True(shortReps * shortPhrase.Length >= 120);
        Assert.True(longReps * longPhrase.Length >= 120);
        Assert.True(shortReps * shortPhrase.Length - longReps * longPhrase.Length < longPhrase.Length);
    }

    [Fact]
    public void ByLength_BeatsRandomRepeats()
    {
        // Both switches on: length owns the card, because it already produces a varying count.
        // If this ever flips, UpdateRepeatRowVisibility in the settings card must flip with it.
        var n = ResolveRepeats(-1, "DROP FOR ME", true, 1, 2, true, 120, 0, 0.5);
        Assert.Equal(11, n);
    }

    [Theory]
    [InlineData(0.0, 100)]        // target - variance
    [InlineData(0.5, 120)]        // dead centre
    [InlineData(0.9999999, 140)]  // target + variance
    public void ByLength_VarianceSpansExactlyPlusMinus(double roll, int expectedBudget)
    {
        // A 1-character phrase makes the repeat count read back the rolled budget directly.
        var n = ResolveRepeats(-1, "X", false, 1, 3, true, 120, 20, roll);
        Assert.Equal(System.Math.Min(expectedBudget, TargetLengthRepeatCap), n);
    }

    [Fact]
    public void ByLength_IsCappedSoAShortPhraseIsNotAWall()
        => Assert.Equal(TargetLengthRepeatCap, ResolveRepeats(-1, "X", false, 1, 3, true, 600, 0, 0.5));

    [Fact]
    public void ByLength_AlwaysAsksForAtLeastOne()
    {
        // A budget below one phrase, and a variance wide enough to roll the budget negative.
        Assert.Equal(1, ResolveRepeats(-1, "I AM EMPTY AND OBEDIENT", false, 1, 3, true, 20, 0, 0.5));
        Assert.Equal(1, ResolveRepeats(-1, "I AM EMPTY AND OBEDIENT", false, 1, 3, true, 20, 200, 0.0));
    }

    [Fact]
    public void ByLength_MeasuresWhatTheUserActuallyTypes()
    {
        // The normaliser expands a typographic ellipsis to three keystrokes and collapses a double
        // space to one, so the budget must be divided by the NORMALISED length. Raw Length would
        // put these two phrases on different repeat counts even though typing them is identical.
        var curly = ResolveRepeats(-1, "DROP…  NOW", false, 1, 3, true, 120, 0, 0.5);
        var plain = ResolveRepeats(-1, "DROP... NOW", false, 1, 3, true, 120, 0, 0.5);
        Assert.Equal(plain, curly);
        Assert.Equal(11, curly); // ceil(120/11), "DROP... NOW"
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ByLength_FallsBackWhenThereIsNothingToMeasure(string? phrase)
    {
        // Dividing by a zero-length phrase is the one way this resolver could throw. It must fall
        // through to the count modes instead, because the card is still going to be shown.
        Assert.Equal(3, ResolveRepeats(-1, phrase, false, 1, 3, true, 120, 20, 0.5));
        Assert.InRange(ResolveRepeats(-1, phrase, true, 2, 5, true, 120, 20, 0.5), 2, 5);
    }
}
