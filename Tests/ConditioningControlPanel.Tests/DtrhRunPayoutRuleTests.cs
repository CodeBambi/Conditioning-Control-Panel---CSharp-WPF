using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// What a descent that was LEFT is worth. Both halves exist because banking every exit (so an
/// endless run finally counts) opened a door that had never been open: <c>AwardRunRewards</c>
/// scales its predictable Spark floor off the run's CONFIGURED length, and an endless descent is
/// dealt 720 s.
/// </summary>
public class DtrhRunPayoutRuleTests
{
    private const double EndlessConfigured = 720.0;   // ENDLESS_BASE_SEC, chaosRun.js

    [Fact]
    public void ACompletedRunIsPaidForTheRunItWasDealt()
    {
        var payout = DtrhRunPayoutRule.For(abandoned: false, configuredDurationSec: 180, elapsedSec: 180);

        Assert.Equal(180, payout.PaidDurationSec);
        Assert.True(payout.CountsAsRun);
    }

    [Fact]
    public void ACompletedRunIsNotDockedForEndingEarly()
    {
        // The surfacing wash, the relapse, the final Landing: a run can reach its own ending with
        // elapsed short of the clock, and that is still a whole descent.
        var payout = DtrhRunPayoutRule.For(abandoned: false, configuredDurationSec: 180, elapsedSec: 171);

        Assert.Equal(180, payout.PaidDurationSec);
        Assert.True(payout.CountsAsRun);
    }

    [Fact]
    public void TheExploit_HoldingEscapeOneSecondIntoAnEndlessRunMintsNothing()
    {
        // The whole point. Paid for one second, not for the 720 the descent was configured with,
        // and it is not a run - so no RunsCompleted, no first-fall bonus, repeatable or not.
        var payout = DtrhRunPayoutRule.For(abandoned: true, EndlessConfigured, elapsedSec: 1.2);

        Assert.Equal(1.2, payout.PaidDurationSec);
        Assert.False(payout.CountsAsRun);
    }

    [Fact]
    public void ABounceOffTheTutorialIsNotADescent()
    {
        // RunsCompleted == 0 is the key to ChaosHappyPath's one-shot scripted first fall. A new
        // player who backs out at five seconds must not burn it.
        Assert.False(DtrhRunPayoutRule.For(abandoned: true, EndlessConfigured, elapsedSec: 5).CountsAsRun);
    }

    [Theory]
    [InlineData(5.0, false)]
    [InlineData(59.0, false)]
    [InlineData(60.0, true)]
    [InlineData(600.0, true)]
    public void AnAbandonedRunCountsOnlyPastTheMinute(double elapsedSec, bool counts)
    {
        var payout = DtrhRunPayoutRule.For(abandoned: true, EndlessConfigured, elapsedSec);

        Assert.Equal(counts, payout.CountsAsRun);
        Assert.Equal(elapsedSec, payout.PaidDurationSec);   // paid for what was fallen, either way
    }

    [Theory]
    [InlineData(5.0)]
    [InlineData(600.0)]
    public void ACompletedRunOfTheSameLengthAlwaysCounts(double elapsedSec)
        => Assert.True(DtrhRunPayoutRule.For(abandoned: false, EndlessConfigured, elapsedSec).CountsAsRun);

    [Fact]
    public void AnAbandonedRunIsNeverPaidForMoreThanItWasDealt()
    {
        // elapsedSec arrives from the page and, on the teardown path, from a snapshot that may be
        // seconds stale. It is clamped, so a bad number cannot pay more than a whole descent.
        var payout = DtrhRunPayoutRule.For(abandoned: true, configuredDurationSec: 180, elapsedSec: 9_000);

        Assert.Equal(180, payout.PaidDurationSec);
        Assert.True(payout.CountsAsRun);
    }

    [Fact]
    public void NegativesAreFloored()
    {
        var payout = DtrhRunPayoutRule.For(abandoned: true, configuredDurationSec: 180, elapsedSec: -5);

        Assert.Equal(0, payout.PaidDurationSec);
        Assert.False(payout.CountsAsRun);
    }

    [Fact]
    public void ThePageCannotClaimMoreSecondsThanTheHostSatThrough()
    {
        // Every other figure the payout rests on comes from the page, so the one figure worth
        // Sparks is bounded by the only clock the host owns: the wall time since run-started.
        // Eight seconds in the room, six hundred claimed, paid on thirteen and not a descent.
        var payout = DtrhRunPayoutRule.For(abandoned: true, EndlessConfigured, elapsedSec: 600,
            hostElapsedSec: 8);

        Assert.Equal(8 + DtrhRunPayoutRule.HostClockSlackSec, payout.PaidDurationSec);
        Assert.False(payout.CountsAsRun);
    }

    [Fact]
    public void AnHonestPageIsBelievedWhenItReportsLessThanTheHostSaw()
    {
        // The page's clock pauses for a covering video and an in-world freeze; the wall clock does
        // not. Under the host's figure is the normal case and must not be topped up to it.
        var payout = DtrhRunPayoutRule.For(abandoned: true, EndlessConfigured, elapsedSec: 90,
            hostElapsedSec: 140);

        Assert.Equal(90, payout.PaidDurationSec);
        Assert.True(payout.CountsAsRun);
    }

    [Fact]
    public void WithNoHostClockThePagesWordIsAllThereIs()
    {
        // No run-started was seen, so there is nothing to bound it with. The minute floor is still
        // the backstop.
        var payout = DtrhRunPayoutRule.For(abandoned: true, EndlessConfigured, elapsedSec: 600,
            hostElapsedSec: null);

        Assert.Equal(600, payout.PaidDurationSec);
        Assert.True(payout.CountsAsRun);
    }

    [Fact]
    public void ANegativeHostClockIsFlooredRatherThanTrusted()
    {
        var payout = DtrhRunPayoutRule.For(abandoned: true, EndlessConfigured, elapsedSec: 600,
            hostElapsedSec: -30);

        Assert.Equal(DtrhRunPayoutRule.HostClockSlackSec, payout.PaidDurationSec);
        Assert.False(payout.CountsAsRun);
    }
}
