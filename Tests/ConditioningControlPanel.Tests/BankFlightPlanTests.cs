using System;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// THE BANK's flight arithmetic. The tokens themselves can only be judged by eye, but the numbers
/// they ride can drift silently: a stagger that creeps past 80ms turns a spill into a queue, a
/// duration outside 500-650 breaks the "caught" feel, and a token count that keeps climbing turns
/// a session claim into confetti. The House Book's bands are asserted here so the spec is a test
/// rather than a comment, and determinism is asserted because a plan that varies per call cannot
/// be reasoned about at all.
/// </summary>
public class BankFlightPlanTests
{
    [Theory]
    // Each pair is (xpSum, tokens) on both sides of every step in the table. The band widened from
    // 3-7 to 4-10 when THE BANK stopped firing for ambient XP: a flight that only happens on a
    // completion is allowed to be a fuller spill.
    [InlineData(0, 4)]
    [InlineData(14.99, 4)]
    [InlineData(15, 6)]
    [InlineData(49.99, 6)]
    [InlineData(50, 7)]
    [InlineData(119.99, 7)]
    [InlineData(120, 8)]
    [InlineData(299.99, 8)]
    [InlineData(300, 10)]
    [InlineData(50_000, 10)]
    public void TokenCount_StepsExactlyOnTheTablesEdges(double xpSum, int expected)
        => Assert.Equal(expected, BankFlightPlan.TokenCount(xpSum));

    [Fact]
    public void TokenCount_GarbageXpIsTheSmallestPot_NotAThrow()
    {
        // This sits downstream of a live XP figure. A NaN multiplier upstream must cost the moment
        // its size, never the award its completion.
        Assert.Equal(BankFlightPlan.MinTokens, BankFlightPlan.TokenCount(double.NaN));
        Assert.Equal(BankFlightPlan.MinTokens, BankFlightPlan.TokenCount(-500));
    }

    [Fact]
    public void TokenCount_NeverExceedsTheCap()
    {
        // "The FEELING scales, not the particle count."
        Assert.Equal(BankFlightPlan.MaxTokens, BankFlightPlan.TokenCount(double.MaxValue));
        Assert.Equal(BankFlightPlan.MaxTokens, BankFlightPlan.TokenCount(double.PositiveInfinity));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    public void EveryPlan_SitsInsideTheHouseBooksBands(int count)
    {
        // Swept over many seeds: a band violation that only shows up on one seed in fifty is
        // exactly the kind of thing a single-seed test would ship.
        for (int seed = 0; seed < 250; seed++)
        {
            var plan = BankFlightPlan.Plan(count, seed);
            Assert.Equal(count, plan.Length);
            Assert.Equal(0, plan[0].DelayMs);

            for (int i = 0; i < plan.Length; i++)
            {
                Assert.InRange(plan[i].DurationMs, BankFlightPlan.DurationMinMs, BankFlightPlan.DurationMaxMs);
                Assert.InRange(Math.Abs(plan[i].ArcBow), BankFlightPlan.ArcBowMin, BankFlightPlan.ArcBowMax);
                if (i > 0)
                {
                    Assert.InRange(plan[i].DelayMs - plan[i - 1].DelayMs,
                                   BankFlightPlan.StaggerMinMs, BankFlightPlan.StaggerMaxMs);
                }
            }
        }
    }

    [Fact]
    public void BowSignsAlternate_SoTheFlightFansInsteadOfStreaming()
    {
        for (int seed = 0; seed < 50; seed++)
        {
            var plan = BankFlightPlan.Plan(BankFlightPlan.MaxTokens, seed);
            for (int i = 1; i < plan.Length; i++)
                Assert.True(Math.Sign(plan[i].ArcBow) != Math.Sign(plan[i - 1].ArcBow),
                            $"seed {seed}: tokens {i - 1} and {i} bow the same way");
        }
    }

    [Fact]
    public void Plan_ClampsToTheCap_AndRefusesNothing()
    {
        Assert.Equal(BankFlightPlan.MaxTokens, BankFlightPlan.Plan(50, 1).Length);
        Assert.Empty(BankFlightPlan.Plan(0, 1));
        Assert.Empty(BankFlightPlan.Plan(-3, 1));
    }

    [Fact]
    public void SameSeed_SameFlight()
    {
        var a = BankFlightPlan.Plan(6, 4242);
        var b = BankFlightPlan.Plan(6, 4242);
        Assert.Equal(a, b);   // record struct equality: every field, every token
    }

    [Fact]
    public void DifferentSeeds_DifferentFlights()
    {
        // Not a distribution claim - just that the seed is actually wired to the draws, which a
        // stray `new Random()` would quietly break while leaving every band test green.
        var a = BankFlightPlan.Plan(7, 1);
        var b = BankFlightPlan.Plan(7, 2);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Envelope_IsTheLastLANDING_NotTheLastLaunch()
    {
        // The 150ms duration spread beats the 60-80ms stagger, so the final token in the array is
        // not always the final one to arrive. A watchdog timed off the tail would fire early.
        var plan = new[]
        {
            new BankFlightPlan.Token(0, 650, 0.12),
            new BankFlightPlan.Token(70, 500, -0.12),
        };
        Assert.Equal(650, BankFlightPlan.EnvelopeMs(plan), 3);
    }

    [Fact]
    public void Envelope_IsTheMaxOfDelayPlusDuration()
    {
        for (int seed = 0; seed < 100; seed++)
        {
            var plan = BankFlightPlan.Plan(7, seed);
            double expected = 0;
            foreach (var t in plan) expected = Math.Max(expected, t.DelayMs + t.DurationMs);
            Assert.Equal(expected, BankFlightPlan.EnvelopeMs(plan), 6);
        }
    }

    [Fact]
    public void Envelope_OfNothingIsZero()
    {
        Assert.Equal(0, BankFlightPlan.EnvelopeMs(Array.Empty<BankFlightPlan.Token>()));
        Assert.Equal(0, BankFlightPlan.EnvelopeMs(null!));
    }

    [Fact]
    public void WholeFlight_FitsInsideTheShellsWatchdog()
    {
        // The shell arms a 6s failsafe around a flight. The longest legal plan has to land well
        // inside it, or the failsafe becomes the normal path and the counter snaps every time.
        var worst = BankFlightPlan.MaxTokens * BankFlightPlan.StaggerMaxMs + BankFlightPlan.DurationMaxMs;
        Assert.True(worst < 6000, $"worst-case envelope {worst}ms is not comfortably inside the 6s watchdog");
    }
}
