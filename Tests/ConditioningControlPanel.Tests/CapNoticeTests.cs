using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Chaster;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The "day is full" tag on a red bubble pop the tab had no room for (owner, 2026-10-03).</summary>
public class CapNoticeTests
{
    private static readonly TabBooking DayFull = new(0, TabRefusal.DailyCap);
    private static readonly TabBooking TabFull = new(0, TabRefusal.Backlog);

    [Fact]
    public void A_player_pop_refused_by_the_day_limit_shows_the_tag()
    {
        Assert.True(CapNotice.Shows(NatashasFavourite.EventId, 300, DayFull, unprompted: false));
        Assert.True(CapNotice.Shows(NatashasFavourite.EventId, 300, TabFull, unprompted: false));
    }

    [Theory]
    [InlineData(TabRefusal.Nothing)]
    [InlineData(TabRefusal.Paused)]
    [InlineData(TabRefusal.SafetyExit)]
    [InlineData(TabRefusal.Remote)]
    [InlineData(TabRefusal.RowCap)]
    [InlineData(TabRefusal.Floor)]
    public void Any_other_refusal_stays_silent(TabRefusal refusal) =>
        Assert.False(CapNotice.Shows(NatashasFavourite.EventId, 300, new TabBooking(0, refusal), unprompted: false));

    [Fact]
    public void A_booking_that_landed_shows_the_figure_not_the_tag()
    {
        // Clamped by the day limit but something still landed: the "+2:00" floats, not the tag.
        Assert.False(CapNotice.Shows(NatashasFavourite.EventId, 300, new TabBooking(120, TabRefusal.DailyCap), unprompted: false));
    }

    [Fact]
    public void An_unprompted_red_flash_stays_silent() =>
        Assert.False(CapNotice.Shows(NatashasFavourite.EventId, 300, DayFull, unprompted: true));

    [Fact]
    public void A_resist_credit_and_other_rows_stay_silent()
    {
        Assert.False(CapNotice.Shows(NatashasFavourite.HeldEventId, -60, DayFull, unprompted: false));
        Assert.False(CapNotice.Shows(NatashasFavourite.EventId, -60, DayFull, unprompted: false));
        Assert.False(CapNotice.Shows("quest_fail", 300, DayFull, unprompted: false));
        Assert.False(CapNotice.Shows(null, 300, DayFull, unprompted: false));
    }

    [Fact]
    public void A_burst_of_pops_shows_one_tag_every_two_seconds()
    {
        Assert.False(CapNotice.Throttled(null, 5_000));
        Assert.True(CapNotice.Throttled(5_000, 5_000 + CapNotice.ThrottleMs - 1));
        Assert.False(CapNotice.Throttled(5_000, 5_000 + CapNotice.ThrottleMs));
    }

    [Fact]
    public void The_text_names_which_limit_is_full()
    {
        Assert.Equal("chaster_pop_day_full", CapNotice.TextKey(TabRefusal.DailyCap));
        Assert.Equal("chaster_pop_tab_full", CapNotice.TextKey(TabRefusal.Backlog));
    }

    [Fact]
    public void Motion_off_only_fades()
    {
        var off = CapNotice.Pop(MotionLevel.Off);
        Assert.Equal(0, off.RiseDip);
        Assert.Equal(0, off.ScaleInMs);
        Assert.Equal(0, off.Particles);
        Assert.True(off.FadeMs > 0);
        Assert.Equal(0, CapNotice.Look("day is full", MotionLevel.Off).TravelPx);
        Assert.False(CapNotice.Look("day is full", MotionLevel.Full).Flash);
        Assert.Null(CapNotice.Look("day is full", MotionLevel.Full).Source);
    }
}
