using System;
using ConditioningControlPanel.Services.Leash;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>When the leash's sounds may play: the gap, the punishment window, the mandatory video,
/// one cut per cut, the tug throttle and the hold-to-cut tick.</summary>
public class LeashSfxRulesTests
{
    private static readonly DateTime T0 = new(2026, 9, 26, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Cues_inside_the_gap_do_not_stack()
    {
        var r = new LeashSfxRules();
        Assert.True(r.TryPlay(LeashCue.Gift, T0, false, false));
        Assert.False(r.TryPlay(LeashCue.Scold, T0.AddMilliseconds(60), false, false));
        Assert.True(r.TryPlay(LeashCue.Scold, T0.AddMilliseconds(140), false, false));
    }

    [Fact]
    public void Mandatory_video_silences_everything()
    {
        var r = new LeashSfxRules();
        foreach (LeashCue c in Enum.GetValues(typeof(LeashCue)))
            Assert.False(r.TryPlay(c, T0.AddSeconds((int)c * 10), false, mandatoryVideo: true));
    }

    [Theory]
    [InlineData(LeashCue.Denied, true)]
    [InlineData(LeashCue.Tick, true)]
    [InlineData(LeashCue.Cut, true)]
    [InlineData(LeashCue.Gift, false)]
    [InlineData(LeashCue.Scold, false)]
    [InlineData(LeashCue.Jingle, false)]
    [InlineData(LeashCue.Done, false)]
    public void Only_the_players_own_answers_play_over_the_punish_window(LeashCue cue, bool allowed)
    {
        Assert.Equal(allowed, LeashSfxRules.AllowedOverPunishWindow(cue));
        Assert.Equal(allowed, new LeashSfxRules().TryPlay(cue, T0, punishWindowOpen: true, mandatoryVideo: false));
    }

    [Fact]
    public void A_cut_sounds_once_when_both_sides_report_it()
    {
        var r = new LeashSfxRules();
        Assert.True(r.TryPlay(LeashCue.Cut, T0, false, false));
        Assert.False(r.TryPlay(LeashCue.Cut, T0.AddSeconds(1), false, false));
        Assert.True(r.TryPlay(LeashCue.Cut, T0.AddSeconds(5), false, false));
    }

    [Fact]
    public void Tugs_are_throttled_per_friend()
    {
        var t = new LeashTugThrottle();
        Assert.True(t.TryTug("a", T0));
        Assert.False(t.TryTug("a", T0.AddSeconds(4)));
        Assert.True(t.TryTug("b", T0.AddSeconds(4)));
        Assert.True(t.TryTug("a", T0.AddSeconds(10)));
    }

    [Fact]
    public void The_hold_ticks_once_per_whole_second_up_to_the_ask()
    {
        Assert.Null(LeashHoldTick.Due(TimeSpan.FromMilliseconds(500), 0));
        Assert.Equal(1, LeashHoldTick.Due(TimeSpan.FromMilliseconds(1030), 0));
        Assert.Null(LeashHoldTick.Due(TimeSpan.FromMilliseconds(1600), 1));
        Assert.Equal(2, LeashHoldTick.Due(TimeSpan.FromMilliseconds(2010), 1));
        Assert.Null(LeashHoldTick.Due(TimeSpan.FromSeconds(6), 5));
    }

    [Fact]
    public void The_hold_clock_reads_the_current_hold()
    {
        var h = new LeashHoldToCut();
        Assert.Null(h.HeldFor(T0));
        h.Down(T0, leashed: true);
        h.Down(T0.AddMilliseconds(900), leashed: true);
        Assert.Equal(TimeSpan.FromMilliseconds(900), h.HeldFor(T0.AddMilliseconds(900)));
        h.Up();
        Assert.Null(h.HeldFor(T0.AddSeconds(1)));
    }

    [Fact]
    public void The_meter_knows_a_cap_from_an_ending()
    {
        var m = new LeashWatchMeter { CapSeconds = 60 };
        Assert.False(m.CapReached);
    }
}
