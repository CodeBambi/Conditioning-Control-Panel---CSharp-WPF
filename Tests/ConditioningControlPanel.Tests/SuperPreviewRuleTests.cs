using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Super;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The weekly free Super try: Monday 00:00 UTC weeks, one effect per week off the SuperEffect wheel
/// (the week of 2026-10-05 is Vortex, the owner's launch pick), one 10 s try per week, and the gate
/// truth table SuperAccess reads.
/// </summary>
public class SuperPreviewRuleTests
{
    private static DateTimeOffset Utc(int y, int mo, int d, int h = 0, int mi = 0, int s = 0)
        => new(y, mo, d, h, mi, s, TimeSpan.Zero);

    [Fact]
    public void Launch_week_is_vortex()
    {
        Assert.Equal(SuperEffect.Vortex, SuperPreviewRule.EffectThisWeek(Utc(2026, 10, 5)));
        Assert.Equal(SuperEffect.Vortex, SuperPreviewRule.EffectThisWeek(Utc(2026, 10, 11, 23, 59, 59)));
    }

    [Fact]
    public void The_wheel_walks_the_enum_in_order_and_wraps_after_eight()
    {
        int w = SuperPreviewRule.WeekIndex(Utc(2026, 10, 5));
        var all = Enum.GetValues<SuperEffect>();
        for (int i = 0; i < 16; i++)
            Assert.Equal(all[((int)SuperEffect.Vortex + i) % all.Length], SuperPreviewRule.EffectForWeek(w + i));
        Assert.Equal(SuperEffect.Creep, SuperPreviewRule.EffectThisWeek(Utc(2026, 10, 12)));
        Assert.Equal(SuperEffect.Afterglow, SuperPreviewRule.EffectThisWeek(Utc(2026, 9, 28)));
    }

    [Fact]
    public void Week_starts_monday_midnight_utc()
    {
        int w = SuperPreviewRule.WeekIndex(Utc(2026, 10, 5));
        Assert.Equal(w - 1, SuperPreviewRule.WeekIndex(Utc(2026, 10, 4, 23, 59, 59)));
        Assert.Equal(w, SuperPreviewRule.WeekIndex(Utc(2026, 10, 5, 0, 0, 0)));
        Assert.Equal(w, SuperPreviewRule.WeekIndex(Utc(2026, 10, 11, 23, 59, 59)));
        Assert.Equal(w + 1, SuperPreviewRule.WeekIndex(Utc(2026, 10, 12)));
        Assert.Equal(DayOfWeek.Monday, SuperPreviewRule.WeekStart(w).DayOfWeek);
        Assert.Equal(Utc(2026, 10, 5), SuperPreviewRule.WeekStart(w));
        Assert.Equal(Utc(2026, 10, 12), SuperPreviewRule.NextSwap(Utc(2026, 10, 7, 13, 0)));
    }

    [Fact]
    public void Local_offsets_and_dst_do_not_move_the_week()
    {
        // Monday 01:30 in Rome during summer time is Sunday 23:30 UTC: still the old week.
        var romeSummer = new DateTimeOffset(2026, 10, 5, 1, 30, 0, TimeSpan.FromHours(2));
        Assert.Equal(SuperPreviewRule.WeekIndex(Utc(2026, 10, 4, 23, 30)), SuperPreviewRule.WeekIndex(romeSummer));
        // The EU clock change (Sunday 2026-10-25) shifts nothing: the week still turns at Monday 00:00 UTC.
        int w = SuperPreviewRule.WeekIndex(Utc(2026, 10, 26));
        Assert.Equal(w - 1, SuperPreviewRule.WeekIndex(new DateTimeOffset(2026, 10, 25, 23, 59, 0, TimeSpan.Zero)));
        Assert.Equal(w, SuperPreviewRule.WeekIndex(new DateTimeOffset(2026, 10, 26, 1, 0, 0, TimeSpan.FromHours(1))));
        Assert.Equal(TimeSpan.FromDays(7), SuperPreviewRule.WeekStart(w + 1) - SuperPreviewRule.WeekStart(w));
    }

    [Fact]
    public void Weeks_before_the_epoch_floor_and_wrap_positive()
    {
        Assert.Equal(-1, SuperPreviewRule.WeekIndex(Utc(1970, 1, 4, 23, 59, 59)));
        Assert.True(Enum.IsDefined(SuperPreviewRule.EffectForWeek(-1)));
        Assert.True(Enum.IsDefined(SuperPreviewRule.EffectForWeek(-12345)));
    }

    [Fact]
    public void One_try_a_week_of_this_weeks_effect_only()
    {
        var now = Utc(2026, 10, 7, 12, 0);
        int week = SuperPreviewRule.WeekIndex(now);

        Assert.True(SuperPreviewRule.CanTry(SuperEffect.Vortex, SuperPreviewRule.NeverUsed, now));
        Assert.True(SuperPreviewRule.CanTry(SuperEffect.Vortex, week - 1, now));
        Assert.False(SuperPreviewRule.CanTry(SuperEffect.Vortex, week, now));
        Assert.False(SuperPreviewRule.CanTry(SuperEffect.Creep, SuperPreviewRule.NeverUsed, now));

        Assert.True(SuperPreviewRule.UsedThisWeek(week, now));
        Assert.False(SuperPreviewRule.UsedThisWeek(week - 1, now));
        // Back Monday: the spent week does not block next week's effect.
        Assert.True(SuperPreviewRule.CanTry(SuperEffect.Creep, week, Utc(2026, 10, 12, 0, 0, 1)));
    }

    [Theory]
    // hasTier, switchedOn, trying,                 unlocked, on
    [InlineData(false, false, null,                  false, false)]
    [InlineData(false, true,  null,                  false, false)] // a lapse leaves the name inert
    [InlineData(true,  false, null,                  true,  false)]
    [InlineData(true,  true,  null,                  true,  true)]
    [InlineData(false, false, SuperEffect.Vortex,    true,  true)]  // the try is on by definition
    [InlineData(false, true,  SuperEffect.Vortex,    true,  true)]
    [InlineData(false, true,  SuperEffect.Creep,     false, false)] // someone else's try opens nothing
    [InlineData(true,  false, SuperEffect.Creep,     true,  false)]
    public void Gate_truth_table(bool hasTier, bool switchedOn, SuperEffect? trying, bool unlocked, bool on)
    {
        Assert.Equal(unlocked, SuperPreviewRule.IsUnlocked(hasTier, SuperEffect.Vortex, trying));
        Assert.Equal(on, SuperPreviewRule.IsOn(hasTier, switchedOn, SuperEffect.Vortex, trying));
    }

    [Fact]
    public void Seconds_left_counts_down_and_clamps()
    {
        var t0 = Utc(2026, 10, 7, 12, 0);
        Assert.Equal(10, SuperPreviewRule.SecondsLeft(t0, t0));
        Assert.Equal(6.5, SuperPreviewRule.SecondsLeft(t0, t0.AddSeconds(3.5)), 3);
        Assert.Equal(0, SuperPreviewRule.SecondsLeft(t0, t0.AddSeconds(42)));
        Assert.Equal(10, SuperPreviewRule.SecondsLeft(t0, t0.AddSeconds(-5)));
    }

    [Fact]
    public void Panic_and_the_emergency_exit_end_the_try()
    {
        // Ending with nothing running is a no-op, never a throw.
        SuperPreview.End(panic: true);
        Assert.Null(SuperPreview.Trying);

        var root = Directory.GetParent(FindLanguages())!.Parent!.FullName;
        var main = File.ReadAllText(Path.Combine(root, "MainWindow", "MainWindow.xaml.cs"));
        int panic = main.IndexOf("private void HandlePanicKeyPress()", StringComparison.Ordinal);
        int end = main.IndexOf("SuperPreview.End(panic: true)", panic, StringComparison.Ordinal);
        int firstReturn = main.IndexOf("if (TryRacePauseOnEscape()) return;", panic, StringComparison.Ordinal);
        int lockCard = main.IndexOf("LockCardWindow.IsAnyOpen()", panic, StringComparison.Ordinal);
        Assert.True(end > panic && end < lockCard, "panic must end the Super try before any rung can return");
        Assert.True(firstReturn < end, "only the race pause (not a panic) may come first");

        var exit = File.ReadAllText(Path.Combine(root, "Services", "EmergencyExit", "EmergencyExitHostService.cs"));
        Assert.Contains("SuperPreview.End(panic: true)", exit);
    }

    private static string FindLanguages()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null)
        {
            var p = Path.Combine(d.FullName, "ConditioningControlPanel", "Localization", "Languages");
            if (Directory.Exists(p)) return p;
            d = d.Parent;
        }
        throw new DirectoryNotFoundException("Localization/Languages");
    }
}
