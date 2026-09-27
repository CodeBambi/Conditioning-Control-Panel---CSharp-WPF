using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Chaster;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The flashing "+0:30": colour carries the sign, the jackpot wins, the motion table is the whole
/// difference between the three motion levels, and a burst of prices is one figure.
/// </summary>
public class BookedFlashPlanTests
{
    private static BookedFlashPlan.Plan Plan(string eventId, int seconds, MotionLevel motion = MotionLevel.Full)
    {
        var plan = BookedFlashPlan.For(eventId, seconds, motion);
        Assert.NotNull(plan);
        return plan!.Value;
    }

    // ---- colour -----------------------------------------------------------

    [Fact]
    public void TimeAdded_IsRed()
    {
        Assert.Equal(BookedFlashPlan.AddColour, Plan("typo", 30).Colour);
    }

    [Fact]
    public void TimeGivenBack_IsMint()
    {
        Assert.Equal(BookedFlashPlan.CreditColour, Plan("session", -600).Colour);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(-400)]
    public void TheJackpotIsGold_WhicheverWayItMoved(int seconds)
    {
        Assert.Equal(BookedFlashPlan.JackpotColour, Plan(CircesTab.JackpotEventId, seconds).Colour);
    }

    [Fact]
    public void AnUnknownEventStillReadsBySign()
    {
        Assert.Equal(BookedFlashPlan.AddColour, Plan("", 15).Colour);
        Assert.Equal(BookedFlashPlan.CreditColour, Plan(null!, -15).Colour);
    }

    // ---- text -------------------------------------------------------------

    [Fact]
    public void TheTextIsTheTabsOwnFigure_Signed()
    {
        Assert.Equal("+0:30", Plan("typo", 30).Text);
        Assert.Equal("-10:00", Plan("session", -600).Text);
        // Past 100 minutes the tab's own formatter switches to h:mm:ss, and the figure follows it.
        Assert.Equal("+2:00:00", Plan("misses", 7200).Text);
    }

    // ---- nothing to show --------------------------------------------------

    [Fact]
    public void ZeroSecondsHasNoFigure()
    {
        Assert.Null(BookedFlashPlan.For("typo", 0, MotionLevel.Full));
        Assert.Null(BookedFlashPlan.For(CircesTab.JackpotEventId, 0, MotionLevel.Off));
    }

    // ---- the motion table -------------------------------------------------

    [Fact]
    public void Full_TravelsAndBlinks()
    {
        var plan = Plan("typo", 30, MotionLevel.Full);
        Assert.Equal(48, plan.TravelPx);
        Assert.Equal(900, plan.DurationMs);
        Assert.True(plan.Flash);
    }

    [Fact]
    public void Reduced_IsHalfTheTravel_AndNeverBlinks()
    {
        var plan = Plan("typo", 30, MotionLevel.Reduced);
        Assert.Equal(24, plan.TravelPx);
        Assert.Equal(600, plan.DurationMs);
        Assert.False(plan.Flash);
    }

    [Fact]
    public void Off_StillShowsTheNumber_ButNeverMovesIt()
    {
        var plan = Plan("typo", 30, MotionLevel.Off);
        Assert.Equal(0, plan.TravelPx);
        Assert.Equal(700, plan.DurationMs);
        Assert.False(plan.Flash);
        Assert.Equal("+0:30", plan.Text);
    }

    [Fact]
    public void TheMotionLevelNeverChangesWhatTheFigureSays()
    {
        foreach (var motion in new[] { MotionLevel.Full, MotionLevel.Reduced, MotionLevel.Off })
        {
            var plan = Plan(CircesTab.JackpotEventId, -120, motion);
            Assert.Equal("-2:00", plan.Text);
            Assert.Equal(BookedFlashPlan.JackpotColour, plan.Colour);
        }
    }

    // ---- coalescing -------------------------------------------------------

    [Fact]
    public void TheFirstBookingOpensAFigure()
    {
        var (figure, isNew) = BookedFlashPlan.Merge(null, 30, "typo", 1_000);
        Assert.True(isNew);
        Assert.Equal(30, figure.Seconds);
        Assert.Equal("typo", figure.EventId);
        Assert.Equal(1_000, figure.StartedAtMs);
    }

    [Fact]
    public void BookingsInsideTheWindowSumIntoOneFigure()
    {
        var (a, _) = BookedFlashPlan.Merge(null, 30, "typo", 1_000);
        var (b, newB) = BookedFlashPlan.Merge(a, 15, "typo", 1_100);
        var (c, newC) = BookedFlashPlan.Merge(b, 15, "lockcard", 1_399);

        Assert.False(newB);
        Assert.False(newC);
        Assert.Equal(60, c.Seconds);
        Assert.Equal("+1:00", Plan(c.EventId, c.Seconds).Text);
    }

    [Fact]
    public void TheWindowIsAnchoredAtTheFirstBooking_NotSlidForward()
    {
        // A drip of prices 300ms apart must not hold one figure on screen forever.
        var (a, _) = BookedFlashPlan.Merge(null, 30, "typo", 1_000);
        var (b, newB) = BookedFlashPlan.Merge(a, 30, "typo", 1_300);
        var (c, newC) = BookedFlashPlan.Merge(b, 30, "typo", 1_500);

        Assert.False(newB);
        Assert.True(newC);
        Assert.Equal(30, c.Seconds);
        Assert.Equal(1_500, c.StartedAtMs);
    }

    [Fact]
    public void ABookingOnTheWindowsEdgeStartsANewFigure()
    {
        var (a, _) = BookedFlashPlan.Merge(null, 30, "typo", 1_000);
        var (b, isNew) = BookedFlashPlan.Merge(a, 30, "typo", 1_000 + BookedFlashPlan.CoalesceMs);

        Assert.True(isNew);
        Assert.Equal(30, b.Seconds);
    }

    [Fact]
    public void TheJackpotWinsAMergedIdentity()
    {
        var (a, _) = BookedFlashPlan.Merge(null, 30, "typo", 1_000);
        var (b, _) = BookedFlashPlan.Merge(a, -400, CircesTab.JackpotEventId, 1_050);
        Assert.Equal(CircesTab.JackpotEventId, b.EventId);
        Assert.Equal(BookedFlashPlan.JackpotColour, Plan(b.EventId, b.Seconds).Colour);

        // And the other way round: an ordinary price landing beside a wipe does not take it back.
        var (c, _) = BookedFlashPlan.Merge(b, 15, "typo", 1_100);
        Assert.Equal(CircesTab.JackpotEventId, c.EventId);
    }

    [Fact]
    public void AMergeThatNetsToZeroHasNothingToShow()
    {
        var (a, _) = BookedFlashPlan.Merge(null, 30, "typo", 1_000);
        var (b, _) = BookedFlashPlan.Merge(a, -30, "session", 1_100);

        Assert.Equal(0, b.Seconds);
        Assert.Null(BookedFlashPlan.For(b, MotionLevel.Full));
    }

    [Fact]
    public void AMergeCanFlipTheColour()
    {
        var (a, _) = BookedFlashPlan.Merge(null, 30, "typo", 1_000);
        Assert.Equal(BookedFlashPlan.AddColour, Plan(a.EventId, a.Seconds).Colour);

        var (b, _) = BookedFlashPlan.Merge(a, -600, "session", 1_100);
        var plan = BookedFlashPlan.For(b, MotionLevel.Full);
        Assert.NotNull(plan);
        Assert.Equal(BookedFlashPlan.CreditColour, plan!.Value.Colour);
        Assert.Equal("-9:30", plan.Value.Text);
    }
}
