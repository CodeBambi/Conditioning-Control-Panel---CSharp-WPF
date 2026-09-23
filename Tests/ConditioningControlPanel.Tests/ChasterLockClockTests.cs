using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Services.Chaster;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The rail chip's clock: two fields at most, and one state table the padlock, the tint and the
/// digits are all painted from, so they can never disagree.
/// </summary>
public class ChasterLockClockTests
{
    private static readonly DateTime Now = new(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc);

    private static LockSnapshot Snap(TimeSpan left, bool frozen = false, bool hidden = false, string? title = "Test lock") =>
        new("lock1", title, Now + left, frozen, hidden, IsTestLock: true, FetchedAtUtc: Now);

    [Theory]
    [InlineData(0, 0, 0, "0m")]              // at the end
    [InlineData(0, 0, 40, "1m")]             // under a minute is still a minute, never "none"
    [InlineData(0, 0, 90, "1m")]
    [InlineData(0, 12, 0, "12m")]
    [InlineData(0, 59, 59, "59m")]
    [InlineData(4, 12, 0, "4h 12m")]
    [InlineData(23, 59, 0, "23h 59m")]
    public void The_countdown_reads_short(int hours, int minutes, int seconds, string expected) =>
        Assert.Equal(expected, LockClockText.Countdown(new TimeSpan(0, hours, minutes, seconds)));

    [Fact]
    public void A_day_and_over_reads_in_days_and_hours()
    {
        Assert.Equal("1d 0h", LockClockText.Countdown(TimeSpan.FromDays(1)));
        Assert.Equal("12d 4h", LockClockText.Countdown(new TimeSpan(12, 4, 30, 0)));
        Assert.Equal("99d 23h", LockClockText.Countdown(new TimeSpan(99, 23, 0, 0)));
    }

    [Fact]
    public void Past_ninety_nine_days_the_hours_are_dropped_so_the_rail_never_wraps()
    {
        Assert.Equal("100d", LockClockText.Countdown(new TimeSpan(100, 5, 0, 0)));
        Assert.Equal("365d", LockClockText.Countdown(TimeSpan.FromDays(365.5)));
    }

    [Fact]
    public void A_negative_or_zero_span_reads_zero_rather_than_a_minus()
    {
        Assert.Equal("0m", LockClockText.Countdown(TimeSpan.Zero));
        Assert.Equal("0m", LockClockText.Countdown(TimeSpan.FromHours(-3)));
    }

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(1, "0:01")]
    [InlineData(65, "1:05")]
    [InlineData(600, "10:00")]
    public void The_hold_is_the_one_readout_with_seconds(int seconds, string expected) =>
        Assert.Equal(expected, LockClockText.HoldClock(TimeSpan.FromSeconds(seconds)));

    // ============================== the state table ==============================

    [Fact]
    public void Unlinked_shows_a_padlock_and_no_digits()
    {
        var clock = LockClockText.State(LockLookup.Unlinked, null, linked: false, TimeSpan.Zero, Now);

        Assert.Equal(LockClockState.Unlinked, clock.State);
        Assert.Equal("", clock.Text);
    }

    [Fact]
    public void A_token_store_that_still_has_a_grant_but_a_lookup_that_does_not_reads_unlinked()
    {
        // Either half saying "unlinked" is enough: the chip must never claim a lock it cannot ask about.
        Assert.Equal(LockClockState.Unlinked,
            LockClockText.State(LockLookup.Unlinked, Snap(TimeSpan.FromDays(2)), linked: true, TimeSpan.Zero, Now).State);
        Assert.Equal(LockClockState.Unlinked,
            LockClockText.State(LockLookup.Chosen, Snap(TimeSpan.FromDays(2)), linked: false, TimeSpan.Zero, Now).State);
    }

    [Fact]
    public void No_active_lock_reads_no_lock()
    {
        var clock = LockClockText.State(LockLookup.None, null, linked: true, TimeSpan.Zero, Now);

        Assert.Equal(LockClockState.NoLock, clock.State);
        Assert.Equal("", clock.Text);
    }

    [Fact]
    public void Several_locks_and_none_picked_reads_as_no_lock_because_there_is_no_clock_to_show()
    {
        var clock = LockClockText.State(LockLookup.Ambiguous, null, linked: true, TimeSpan.Zero, Now);

        Assert.Equal(LockClockState.NoLock, clock.State);
        Assert.Equal("", clock.Text);
    }

    [Fact]
    public void A_chosen_lock_counts_down()
    {
        var clock = LockClockText.State(LockLookup.Chosen, Snap(new TimeSpan(2, 6, 0, 0)), linked: true, TimeSpan.Zero, Now);

        Assert.Equal(LockClockState.Locked, clock.State);
        Assert.Equal("2d 6h", clock.Text);
    }

    [Fact]
    public void A_frozen_lock_reads_frozen_and_keeps_the_time_it_had_at_the_fetch()
    {
        var snap = Snap(TimeSpan.FromHours(5), frozen: true);

        var clock = LockClockText.State(LockLookup.Chosen, snap, linked: true, TimeSpan.Zero, Now.AddHours(3));

        Assert.Equal(LockClockState.Frozen, clock.State);
        Assert.Equal("5h 0m", clock.Text);   // three hours passed and the frozen clock did not move
    }

    [Fact]
    public void A_hidden_timer_is_a_question_mark_and_never_a_guess()
    {
        var clock = LockClockText.State(LockLookup.Chosen, Snap(TimeSpan.FromDays(3), hidden: true), linked: true, TimeSpan.Zero, Now);

        Assert.Equal(LockClockState.Hidden, clock.State);
        Assert.Equal("?", clock.Text);
    }

    [Fact]
    public void Hidden_wins_over_frozen_because_there_is_nothing_to_freeze_on_screen()
    {
        var snap = Snap(TimeSpan.FromDays(3), frozen: true, hidden: true);

        Assert.Equal(LockClockState.Hidden, LockClockText.State(LockLookup.Chosen, snap, linked: true, TimeSpan.Zero, Now).State);
    }

    [Fact]
    public void Away_keeps_the_last_number_and_says_so_with_its_own_state()
    {
        var clock = LockClockText.State(LockLookup.Away, Snap(TimeSpan.FromHours(30)), linked: true, TimeSpan.Zero, Now);

        Assert.Equal(LockClockState.Away, clock.State);
        Assert.Equal("1d 6h", clock.Text);
    }

    [Fact]
    public void Away_with_nothing_ever_fetched_shows_no_digits()
    {
        var clock = LockClockText.State(LockLookup.Away, null, linked: true, TimeSpan.Zero, Now);

        Assert.Equal(LockClockState.Away, clock.State);
        Assert.Equal("", clock.Text);
    }

    [Fact]
    public void A_safety_hold_wins_over_every_other_state_including_the_lock_itself()
    {
        var snap = Snap(TimeSpan.FromDays(4));

        var locked = LockClockText.State(LockLookup.Chosen, snap, linked: true, TimeSpan.FromSeconds(325), Now);
        var unlinked = LockClockText.State(LockLookup.Unlinked, null, linked: false, TimeSpan.FromSeconds(325), Now);

        Assert.Equal(LockClockState.Held, locked.State);
        Assert.Equal("5:25", locked.Text);
        // Even with no account: the hold is the app promising nothing is being added right now.
        Assert.Equal(LockClockState.Held, unlinked.State);
    }

    [Fact]
    public void A_hold_that_has_run_out_gives_the_lock_back()
    {
        var clock = LockClockText.State(LockLookup.Chosen, Snap(TimeSpan.FromHours(2)), linked: true, TimeSpan.Zero, Now);

        Assert.Equal(LockClockState.Locked, clock.State);
    }

    [Fact]
    public void A_lock_with_no_end_date_shows_the_question_mark_rather_than_a_guess()
    {
        // Remaining() is null for a hidden timer and for a lock with no end date alike, and the
        // chip has one honest answer for "we do not know how long is left".
        var open = new LockSnapshot("lock1", "Open ended", null, IsFrozen: false, TimerHidden: false,
            IsTestLock: true, FetchedAtUtc: Now);

        var clock = LockClockText.State(LockLookup.Chosen, open, linked: true, TimeSpan.Zero, Now);

        Assert.Equal(LockClockState.Locked, clock.State);
        Assert.Equal("?", clock.Text);
    }

    [Fact]
    public void A_lock_already_past_its_end_date_reads_zero_rather_than_counting_up()
    {
        var clock = LockClockText.State(LockLookup.Chosen, Snap(TimeSpan.FromHours(-2)), linked: true, TimeSpan.Zero, Now);

        Assert.Equal(LockClockState.Locked, clock.State);
        Assert.Equal("0m", clock.Text);
    }
}

/// <summary>
/// The rail chip as it actually builds. Two things a pure test cannot see: a padlock geometry
/// that does not parse, and the clock quietly losing the tag that keeps it out of the rail's
/// label fade.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class ChasterRailChipRenderTests
{
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var deeper in Descendants(child)) yield return deeper;
        }
    }

    private static void OnChip(Action<ChasterRailChip> body) => WpfRenderHarness.OnStaThread(() =>
    {
        var chip = new ChasterRailChip();
        var host = new Grid { Width = 56, Height = 120 };
        host.Children.Add(chip);
        host.Measure(new Size(56, 120));
        host.Arrange(new Rect(new Point(0, 0), new Size(56, 120)));
        host.UpdateLayout();
        body(chip);
    });

    [Fact]
    public void The_chip_builds_with_no_service_behind_it() => OnChip(chip =>
    {
        Assert.NotNull(chip.ToolTip);
        Assert.True(chip.ActualHeight > 0);
    });

    [Fact]
    public void The_clock_is_tagged_so_the_rail_label_fade_leaves_it_alone() => OnChip(chip =>
    {
        // CacheNavRailParts (MainWindow.NavRail.cs) fades every rail TextBlock that is not tagged
        // "navrailstatic", and the countdown has to survive the rail shutting - it is the reason
        // the chip is here. The pending badge's figure is the deliberate opposite and has no tag.
        var texts = Descendants(chip).OfType<TextBlock>().ToList();
        Assert.Contains(texts, t => (t.Tag as string) == "navrailstatic");
        Assert.Contains(texts, t => t.Tag == null);
    });

    [Fact]
    public void The_chip_fits_the_collapsed_rail() => OnChip(chip =>
    {
        // 56px is NavRailCollapsedWidth. A chip wider than that pushes the shut rail open.
        Assert.True(chip.ActualWidth <= 56, $"the chip measured {chip.ActualWidth}px wide");
    });

    [Fact]
    public void A_pulse_on_a_chip_nobody_wired_is_harmless() =>
        OnChip(chip => chip.Pulse(Color.FromRgb(0xFF, 0x6B, 0x8A)));
}
