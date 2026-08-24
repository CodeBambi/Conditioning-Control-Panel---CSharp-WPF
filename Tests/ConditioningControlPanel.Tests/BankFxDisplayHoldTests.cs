using System;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// THE BANK's counter arithmetic - the half of the shell choreography that can lie without anybody
/// noticing. The tokens, the arcs and the pop can only be judged by eye; what cannot is whether the
/// number the tokens deliver is the number the ledger holds. House Law I says the ledger never
/// lies and only its presentation is staged, so every rounding, clamp and drift decision in
/// <see cref="BankCounterScript"/> is asserted here rather than trusted.
/// </summary>
public class BankFxDisplayHoldTests
{
    // ---- StartValue: what a flight counts up FROM ----

    [Fact]
    public void StartValue_IsTheLastShownNumberWhenTheLevelHasNotMoved()
        => Assert.Equal(412.5, BankCounterScript.StartValue(412.5, 7, 7), 6);

    [Fact]
    public void StartValue_IsZeroAcrossALevelChange()
    {
        // The readout wraps at a level-up. Counting up from the old level's number would make the
        // first token look like a correction of a wrong figure.
        Assert.Equal(0, BankCounterScript.StartValue(980, 7, 8), 6);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-5)]
    public void StartValue_IsZeroWhenThereIsNoHonestPreviousValue(double lastShown)
        => Assert.Equal(0, BankCounterScript.StartValue(lastShown, 3, 3), 6);

    // ---- Target: what the LAST token must deliver ----

    [Fact]
    public void Target_IsTheStartPlusThePot()
        => Assert.Equal(160, BankCounterScript.Target(100, 60, 500), 6);

    [Fact]
    public void Target_NeverExceedsTheLedger()
    {
        // The first award of a pot is displayed before the pot exists (XPChanged runs ahead of
        // XPAwarded), so a flight can be handed a pot it has already been partly credited with.
        // Showing start+pot there would put more XP on screen than the player owns.
        Assert.Equal(120, BankCounterScript.Target(100, 60, 120), 6);
    }

    [Fact]
    public void Target_IgnoresXpThatArrivedMidFlight()
    {
        // The ledger has run on to 900; this flight is only carrying 60 and the rest belongs to the
        // pot already collecting behind it.
        Assert.Equal(160, BankCounterScript.Target(100, 60, 900), 6);
    }

    [Fact]
    public void Target_NeverRunsBackwards()
    {
        // A level-up spends XP, so truth can sit below the counter. The flight simply does not
        // move and the release to truth owns the correction.
        Assert.Equal(100, BankCounterScript.Target(100, 60, 20), 6);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(0)]
    [InlineData(-30)]
    public void Target_IsTheStartForAPotThatIsNotAPot(double pot)
        => Assert.Equal(100, BankCounterScript.Target(100, pot, 500), 6);

    [Fact]
    public void Target_IsTheStartWhenTheLedgerIsUnreadable()
        => Assert.Equal(100, BankCounterScript.Target(100, 60, double.NaN), 6);

    // ---- StepValue: the per-landing tick ----

    [Fact]
    public void StepValue_DividesThePotEvenlyAcrossTheLandings()
    {
        // 100 -> 160 over four tokens: a quarter of the pot per landing.
        Assert.Equal(115, BankCounterScript.StepValue(100, 160, 0, 4), 6);
        Assert.Equal(130, BankCounterScript.StepValue(100, 160, 1, 4), 6);
        Assert.Equal(145, BankCounterScript.StepValue(100, 160, 2, 4), 6);
    }

    [Fact]
    public void StepValue_LandsExactlyOnTheTarget()
    {
        // A third of a pot three times over is not the pot. The ledger is the authority, so the
        // last landing returns the target itself rather than the sum of its slices.
        double target = 100 + 1.0 / 3.0;
        Assert.Equal(target, BankCounterScript.StepValue(100, target, 2, 3));
    }

    [Fact]
    public void StepValue_IsMonotonicAndEndsOnTheTargetForEveryLegalTokenCount()
    {
        for (int count = BankFlightPlan.MinTokens; count <= BankFlightPlan.MaxTokens; count++)
        {
            double previous = 100;
            for (int i = 0; i < count; i++)
            {
                double value = BankCounterScript.StepValue(100, 273.7, i, count);
                Assert.True(value >= previous, $"count {count}, landing {i} went backwards");
                previous = value;
            }
            Assert.Equal(273.7, previous, 6);
        }
    }

    [Fact]
    public void StepValue_ClampsALandingOrdinalOutsideTheFlight()
    {
        // The canvas counts LANDINGS, not plan slots, and a force-landed predecessor can settle
        // more callbacks than this flight has tokens. Neither end may produce a stray number.
        Assert.Equal(115, BankCounterScript.StepValue(100, 160, -3, 4), 6);
        Assert.Equal(160, BankCounterScript.StepValue(100, 160, 9, 4), 6);
    }

    [Fact]
    public void StepValue_SnapsToTheTargetWhenThereIsNoFlightToDivide()
        => Assert.Equal(160, BankCounterScript.StepValue(100, 160, 0, 0), 6);

    [Fact]
    public void StepValue_SurvivesANonFiniteStart()
        => Assert.Equal(160, BankCounterScript.StepValue(double.NaN, 160, 1, 4), 6);

    // ---- StepMs: the tick has to be shorter than the gap between ticks ----

    [Fact]
    public void StepMs_FitsInsideTheShortestPossibleGapBetweenLandings()
    {
        double step = BankCounterScript.StepMs(isLast: false);
        Assert.True(step < BankFlightPlan.StaggerMinMs,
                    "a mid-flight step that outlives the stagger is replaced mid-tween and stutters");
        Assert.True(step > 0);
    }

    [Fact]
    public void StepMs_GivesTheLastLandingTheFullBeat()
    {
        Assert.Equal(BankCounterScript.MaxStepMs, BankCounterScript.StepMs(isLast: true), 6);
        Assert.True(BankCounterScript.MaxStepMs <= 90,
                    "House Book: a tick per landing, not an odometer that happens to be slow");
    }
}
