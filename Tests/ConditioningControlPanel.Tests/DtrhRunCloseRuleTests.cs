using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Banking a DtRH descent when the window dies under it (Beppu, 2026-09-20: "the run counter
/// doesn't seem keep track when I exit that way"). An endless descent has no clock, so leaving
/// IS how it ends - and every leaving except the frame's X now books on the page. This rule is
/// the one that decides whether the teardown has to book the run itself.
/// </summary>
public class DtrhRunCloseRuleTests
{
    [Fact]
    public void ADescentStillFallingIsBankedFromTheLastSnapshot()
        => Assert.True(DtrhRunCloseRule.ShouldBookOnClose(runActive: true, haveSnapshot: true));

    [Fact]
    public void ACleanExitIsNotPaidTwice()
    {
        // The page books first and the host clears runActive on run-ended, so the close that
        // follows an Escape hold or a recap must find nothing left to bank.
        Assert.False(DtrhRunCloseRule.ShouldBookOnClose(runActive: false, haveSnapshot: true));
    }

    [Fact]
    public void ClosingFromTheHubBanksNothing()
        => Assert.False(DtrhRunCloseRule.ShouldBookOnClose(runActive: false, haveSnapshot: false));

    [Fact]
    public void ARunTooYoungToHaveReportedAnythingIsNotInvented()
    {
        // The first progress ping is ten seconds in. Before it there are no figures to pay,
        // and guessing a score would be worse than losing a ten-second descent.
        Assert.False(DtrhRunCloseRule.ShouldBookOnClose(runActive: true, haveSnapshot: false));
    }

    [Fact]
    public void ADescentIsSpentOnce_TheSecondBookingPaysNothing()
    {
        // The host now has TWO producers - the page's run-ended and the teardown's synthetic one -
        // and a run-ended already queued on the dispatcher can be pumped after the teardown has
        // banked. OnRunEnded clears the flag as it takes the claim, so this is the second call
        // arriving: it must pay nothing rather than doubling the Sparks and the run counter.
        bool runActive = true;

        Assert.True(DtrhRunCloseRule.ShouldPayBooking(runActive));
        runActive = false;                                   // what OnRunEnded does as it claims
        Assert.False(DtrhRunCloseRule.ShouldPayBooking(runActive));
        Assert.False(DtrhRunCloseRule.ShouldBookOnClose(runActive, haveSnapshot: true));
    }
}
