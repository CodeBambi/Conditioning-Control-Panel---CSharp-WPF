using System;
using ConditioningControlPanel.Services.Chaster;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>Security pass 2 (2026-09-24): a raise of a limit waits a day, the escape row is
/// capped per day, the "Ends" line agrees with the live clock, and the tag says what goes today.</summary>
public class ChasterGuardrailsTests
{
    private static readonly DateTime Now = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Lowering_a_limit_applies_at_once()
    {
        var s = LimitChange.Request(new LimitSetting(180, 0, null), 60, Now);
        Assert.Equal(new LimitSetting(60, 0, null), s);
        Assert.Equal(60, LimitChange.Effective(s, Now));
    }

    [Fact]
    public void Raising_a_limit_waits_a_day()
    {
        var s = LimitChange.Request(new LimitSetting(180, 0, null), 300, Now);
        Assert.Equal(180, s.Minutes);
        Assert.Equal(300, s.PendingMinutes);
        Assert.Equal(Now.AddHours(24), s.PendingAtUtc);
        Assert.Equal(180, LimitChange.Effective(s, Now.AddHours(23.9)));
        Assert.Equal(300, LimitChange.Effective(s, Now.AddHours(24)));
        Assert.Equal(new LimitSetting(300, 0, null), LimitChange.Settle(s, Now.AddHours(25)));
    }

    [Fact]
    public void Lowering_again_cancels_a_waiting_raise()
    {
        var raised = LimitChange.Request(new LimitSetting(180, 0, null), 300, Now);
        var back = LimitChange.Request(raised, 120, Now.AddHours(2));
        Assert.Equal(new LimitSetting(120, 0, null), back);
        // Back to exactly what is in force also drops it.
        Assert.Equal(new LimitSetting(180, 0, null), LimitChange.Request(raised, 180, Now.AddHours(2)));
    }

    [Fact]
    public void A_smaller_raise_keeps_its_landing_time_and_a_bigger_one_starts_the_day_again()
    {
        var raised = LimitChange.Request(new LimitSetting(180, 0, null), 300, Now);
        var smaller = LimitChange.Request(raised, 240, Now.AddHours(5));
        Assert.Equal(new LimitSetting(180, 240, Now.AddHours(24)), smaller);
        var bigger = LimitChange.Request(smaller, 400, Now.AddHours(6));
        Assert.Equal(new LimitSetting(180, 400, Now.AddHours(30)), bigger);
        Assert.Equal(bigger, LimitChange.Request(bigger, 400, Now.AddHours(7)));
    }

    [Fact]
    public void A_raise_that_landed_is_the_base_for_the_next_request()
    {
        var raised = LimitChange.Request(new LimitSetting(180, 0, null), 300, Now);
        var later = LimitChange.Request(raised, 240, Now.AddDays(2));
        Assert.Equal(new LimitSetting(240, 0, null), later);
    }

    [Fact]
    public void The_escape_row_counts_its_uses_per_local_day()
    {
        var state = new TabState();
        var day = new DateTime(2026, 9, 24, 10, 0, 0);
        for (var i = 0; i < 3; i++)
        {
            Assert.True(CircesTab.UseLeft(state, "escape", day));
            CircesTab.NoteUse(state, "escape", day);
        }
        Assert.False(CircesTab.UseLeft(state, "escape", day));
        Assert.True(CircesTab.UseLeft(state, "escape", day.AddDays(1)));
        // Rows without a cap are never counted or refused.
        CircesTab.NoteUse(state, "typo", day);
        Assert.Equal(0, CircesTab.UsesToday(state, "typo", day));
        Assert.True(CircesTab.UseLeft(state, "typo", day));
        Assert.Equal(3, TabPrices.DailyMaxUses("escape"));
    }

    [Fact]
    public void The_ends_line_counts_to_the_same_end_as_the_clock()
    {
        var end = Now.AddDays(2);
        var snap = new LockSnapshot("l", "t", end, false, false, false, Now);
        Assert.Equal(end.AddSeconds(600), LiveLockClock.EndsAt(snap, 600, Now));
        Assert.Equal(Now + LiveLockClock.Remaining(snap, 600, Now)!.Value, LiveLockClock.EndsAt(snap, 600, Now));
        // Credit never shortens it.
        Assert.Equal(end, LiveLockClock.EndsAt(snap, -600, Now));
        // A lock at its end stays at its own end.
        Assert.Equal(end, LiveLockClock.EndsAt(snap, 600, end.AddMinutes(1)));
        Assert.Null(LiveLockClock.EndsAt(snap with { TimerHidden = true }, 600, Now));
    }

    [Theory]
    [InlineData(600, 600, false, true, "chaster_tag_lands")]
    [InlineData(600, 0, false, true, "chaster_tag_tomorrow")]
    [InlineData(600, 200, false, true, "chaster_tag_split")]
    [InlineData(600, 600, true, true, "chaster_tag_paused")]
    [InlineData(600, 600, false, false, "chaster_tag_nolock")]
    [InlineData(-60, 0, false, true, "chaster_tag_credit_lands")]
    [InlineData(0, 0, false, true, "chaster_tag_lands")]
    public void The_tag_says_what_goes_today(int balance, int today, bool paused, bool picked, string key)
    {
        Assert.Equal(key, TabPageText.Tag(balance, today, paused, picked).Key);
    }

    [Fact]
    public void A_paused_chip_greys_out_but_keeps_the_digits()
    {
        var snap = new LockSnapshot("l", "t", Now.AddHours(3), false, false, false, Now);
        var clock = LockClockText.State(LockLookup.Chosen, snap, linked: true, TimeSpan.Zero, Now, paused: true);
        Assert.Equal(LockClockState.Paused, clock.State);
        Assert.Equal("3h 0m", clock.Text);
        // The safety hold still wins.
        Assert.Equal(LockClockState.Held,
            LockClockText.State(LockLookup.Chosen, snap, true, TimeSpan.FromMinutes(5), Now, paused: true).State);
        Assert.Equal(LockClockState.Unlinked,
            LockClockText.State(LockLookup.Unlinked, null, false, TimeSpan.Zero, Now, paused: true).State);
    }
}
