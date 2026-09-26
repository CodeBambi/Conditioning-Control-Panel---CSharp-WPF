using System;
using System.Linq;
using ConditioningControlPanel.Services.Leash;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>Hold the panic key to cut, the video time cap and its wire (owner, 2026-09-26).</summary>
public class LeashPunishWindowRulesTests
{
    private static readonly DateTime T0 = new(2026, 9, 26, 10, 0, 0, DateTimeKind.Utc);

    // ---- hold to cut --------------------------------------------------------------

    [Fact]
    public void A_held_key_is_one_press_and_asks_once_at_five_seconds()
    {
        var h = new LeashHoldToCut();
        var first = h.Down(T0, leashed: true);
        Assert.False(first.Repeat);
        Assert.False(first.Due);

        int asks = 0;
        for (var ms = 33; ms <= 7000; ms += 33)
        {
            var step = h.Down(T0.AddMilliseconds(ms), leashed: true);
            Assert.True(step.Repeat);
            if (step.Due) { asks++; Assert.True(ms >= 5000); }
        }
        Assert.Equal(1, asks);
    }

    [Fact]
    public void Letting_go_starts_the_clock_again()
    {
        var h = new LeashHoldToCut();
        h.Down(T0, true);
        h.Down(T0.AddSeconds(4), true);
        h.Up();
        var again = h.Down(T0.AddSeconds(4.1), true);
        Assert.False(again.Repeat);
        double dueAt = -1;
        for (var ms = 4133; ms <= 10000; ms += 33)
            if (h.Down(T0.AddMilliseconds(ms), true).Due) dueAt = ms / 1000.0;
        Assert.InRange(dueAt, 9.1, 9.2);
    }

    [Fact]
    public void A_lost_key_up_never_swallows_the_next_real_press()
    {
        var h = new LeashHoldToCut();
        h.Down(T0, true);
        // No key-up arrives; the next down is well past any repeat delay.
        var next = h.Down(T0.AddSeconds(3), true);
        Assert.False(next.Repeat);
    }

    [Fact]
    public void Not_leashed_means_every_down_is_a_press_and_nothing_is_asked()
    {
        var h = new LeashHoldToCut();
        for (var ms = 0; ms <= 8000; ms += 33)
        {
            var step = h.Down(T0.AddMilliseconds(ms), leashed: false);
            Assert.False(step.Repeat);
            Assert.False(step.Due);
        }
        Assert.False(h.Held);
    }

    [Theory]
    [InlineData("Escape", "Esc")]
    [InlineData(null, "Esc")]
    [InlineData("F8", "F8")]
    public void The_key_reads_short(string? key, string label) => Assert.Equal(label, LeashHoldToCut.KeyLabel(key));

    // ---- the cap --------------------------------------------------------------------

    [Theory]
    [InlineData(1, LeashVideoCap.Band.Blue)]
    [InlineData(30, LeashVideoCap.Band.Blue)]
    [InlineData(31, LeashVideoCap.Band.Yellow)]
    [InlineData(60, LeashVideoCap.Band.Yellow)]
    [InlineData(61, LeashVideoCap.Band.Red)]
    [InlineData(90, LeashVideoCap.Band.Red)]
    public void Bands_are_blue_yellow_red(int minutes, LeashVideoCap.Band band) => Assert.Equal(band, LeashVideoCap.BandOf(minutes));

    [Fact]
    public void The_cap_is_the_lower_of_the_pick_and_the_maximum()
    {
        Assert.Equal(20, LeashVideoCap.Effective(45, 20));
        Assert.Equal(45, LeashVideoCap.Effective(45, 90));
        Assert.Equal(90, LeashVideoCap.Effective(500, 500));
        Assert.Equal(1, LeashVideoCap.Effective(0, 30));
    }

    [Fact]
    public void A_video_punishment_takes_any_minute_from_1_to_90()
    {
        var w = new LeashWatch("ht", "123456", null);
        Assert.True(LeashGrammar.ValidPunish(PunishKind.Video, 1, w));
        Assert.True(LeashGrammar.ValidPunish(PunishKind.Video, 47, w));
        Assert.True(LeashGrammar.ValidPunish(PunishKind.Video, 90, w));
        Assert.False(LeashGrammar.ValidPunish(PunishKind.Video, 0, w));
        Assert.False(LeashGrammar.ValidPunish(PunishKind.Video, 91, w));
        Assert.False(LeashGrammar.ValidPunish(PunishKind.Video, 30, null));
        Assert.False(LeashGrammar.ValidPunish(PunishKind.Lines, 4, null));
    }

    [Fact]
    public void The_meter_is_done_at_the_cap_even_mid_video()
    {
        var m = new LeashWatchMeter { CapSeconds = 60 };
        for (int t = 0; t <= 59; t++) m.Sample(t, 1800, visible: true);
        Assert.False(m.IsComplete);
        m.Sample(60, 1800, true);
        m.Sample(61, 1800, true);
        Assert.True(m.IsComplete);
        Assert.Equal(100, m.Percent);
    }

    [Fact]
    public void Hidden_time_does_not_count_toward_the_cap()
    {
        var m = new LeashWatchMeter { CapSeconds = 30 };
        for (int t = 0; t <= 100; t++) m.Sample(t, 1800, visible: false);
        Assert.False(m.IsComplete);
    }

    [Fact]
    public void A_short_video_still_ends_before_the_cap()
    {
        var m = new LeashWatchMeter { CapSeconds = 90 * 60 };
        for (int t = 0; t <= 120; t++) m.Sample(t, 120, true);
        Assert.True(m.IsComplete);
    }

    // ---- wire ------------------------------------------------------------------------

    [Fact]
    public void Video_max_parses_on_both_sides_and_defaults_to_30()
    {
        var p = new JObject { ["id"] = "u_vex", ["name"] = "Vex" };
        var me = LeashParse.Me(new JObject { ["holder"] = p, ["intensity"] = "standard", ["video_max"] = 45 });
        Assert.Equal(45, me!.VideoMax);
        var bad = LeashParse.Me(new JObject { ["holder"] = p, ["intensity"] = "standard", ["video_max"] = 400 });
        Assert.Equal(30, bad!.VideoMax);
        var held = LeashParse.Held(new JObject { ["who"] = p, ["intensity"] = "strict", ["video_max"] = 12 });
        Assert.Equal(12, held!.VideoMax);
    }

    [Fact]
    public void Punish_done_reaches_the_holder_as_its_own_event()
    {
        var ev = LeashParse.Event(new JObject
        {
            ["id"] = "e1",
            ["kind"] = "punish_done",
            ["from"] = new JObject { ["id"] = "u_kit", ["name"] = "Kit" },
            ["at"] = "2026-09-26T10:00:00.000Z",
            ["punishment"] = new JObject { ["pid"] = "p1", ["kind"] = "video", ["size"] = 30 },
        });
        Assert.NotNull(ev);
        Assert.Equal(LeashEventKind.PunishDone, ev!.Kind);
        Assert.Equal("Kit", ev.From.Name);
    }
}
