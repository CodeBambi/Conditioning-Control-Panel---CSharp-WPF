using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Super;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The ways round the one-try-a-week rule found in review: winding the clock forward then back,
/// a cloud restore carrying an older week, and an end timer that fires late.
/// </summary>
public class SuperPreviewHardeningTests
{
    private static DateTimeOffset Utc(int y, int mo, int d, int h = 0)
        => new(y, mo, d, h, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_try_stamped_in_a_later_week_is_not_handed_out_again_when_the_clock_goes_back()
    {
        var real = Utc(2026, 10, 6);                         // Vortex week
        int week = SuperPreviewRule.WeekIndex(real);
        int forward = week + 3;                              // try taken with the clock wound on

        Assert.True(SuperPreviewRule.UsedThisWeek(forward, real));
        Assert.False(SuperPreviewRule.CanTry(SuperPreviewRule.EffectThisWeek(real), forward, real));
        // Real time catching up past the forward week opens the next try as normal.
        var later = SuperPreviewRule.WeekStart(forward + 1).AddHours(1);
        Assert.True(SuperPreviewRule.CanTry(SuperPreviewRule.EffectThisWeek(later), forward, later));
    }

    [Fact]
    public void A_try_from_an_earlier_week_still_allows_this_weeks()
    {
        var now = Utc(2026, 10, 6);
        int week = SuperPreviewRule.WeekIndex(now);
        Assert.True(SuperPreviewRule.CanTry(SuperEffect.Vortex, week - 5, now));
        Assert.False(SuperPreviewRule.UsedThisWeek(week - 5, now));
    }

    [Fact]
    public void The_backstop_ends_a_try_a_second_after_its_length_even_without_the_timer()
    {
        Assert.True(SuperPreviewRule.TryStillRunning(0));
        Assert.True(SuperPreviewRule.TryStillRunning(SuperPreviewRule.TrySeconds + 0.5));
        Assert.False(SuperPreviewRule.TryStillRunning(SuperPreviewRule.TrySeconds + SuperPreviewRule.TryGraceSeconds));
        Assert.False(SuperPreviewRule.TryStillRunning(3600));
        Assert.False(SuperPreviewRule.TryStillRunning(-1));
    }

    [Fact]
    public void Seconds_left_counts_down_on_the_monotonic_clock_and_clamps()
    {
        Assert.Equal(SuperPreviewRule.TrySeconds, SuperPreviewRule.SecondsLeftAfter(0));
        Assert.Equal(4, SuperPreviewRule.SecondsLeftAfter(6));
        Assert.Equal(0, SuperPreviewRule.SecondsLeftAfter(99));
        Assert.Equal(SuperPreviewRule.TrySeconds, SuperPreviewRule.SecondsLeftAfter(-3));
    }

    [Fact]
    public void The_spent_week_never_leaves_this_machine_and_a_restore_keeps_it()
    {
        Assert.True(ProfileSyncService.IsExcludedFromBackup(nameof(AppSettings.SuperPreviewUsedWeek)));

        var current = new AppSettings { SuperPreviewUsedWeek = 2961 };
        var restored = new AppSettings { SuperPreviewUsedWeek = SuperPreviewRule.NeverUsed };
        ProfileSyncService.PreserveLocalOnlyFields(current, restored);
        Assert.Equal(2961, restored.SuperPreviewUsedWeek);
    }

    [Fact]
    public void A_restored_switch_list_does_not_unlock_a_free_account()
    {
        // A cloud profile from a paid month can bring SuperEffectsOn back on a free account:
        // the gate still says off for every effect but the one being tried.
        foreach (var e in Enum.GetValues<SuperEffect>())
        {
            Assert.False(SuperPreviewRule.IsOn(hasTier: false, switchedOn: true, e, trying: null));
            Assert.Equal(e == SuperEffect.Vortex,
                SuperPreviewRule.IsOn(hasTier: false, switchedOn: true, e, trying: SuperEffect.Vortex));
        }
    }
}
