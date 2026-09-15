using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Chaos;
using Newtonsoft.Json;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Bubbles v2, wave 2: the Magnet bubble. Roll gating, the attraction physics (turn rate, speed
/// cap, motion levels, the stale-cursor fallback), the early-window bargain and the persisted
/// default. All of it pure - no WPF, no App, no compositor.
/// </summary>
public class MagnetBubbleTests
{
    private static readonly List<string> DefaultIds =
        new() { "flash", "subliminal", "pink", "spiral", "glitch", "htlink", "video" };

    // ---- roll gating -------------------------------------------------------------------

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Unowned_or_switched_off_never_enters_the_pool(bool owned, bool on)
    {
        var pool = MagnetBubble.RollPool(DefaultIds, owned, on);
        Assert.DoesNotContain(MagnetBubble.VariantId, pool);
        Assert.Equal(DefaultIds, pool);
    }

    [Fact]
    public void Owned_and_on_adds_exactly_one_entry()
    {
        var pool = MagnetBubble.RollPool(DefaultIds, v2Owned: true, settingOn: true);
        Assert.Equal(DefaultIds.Count + 1, pool.Count);
        Assert.Single(pool, id => id == MagnetBubble.VariantId);
        // Every id the user picked is still in there, unweighted.
        foreach (var id in DefaultIds) Assert.Contains(id, pool);
    }

    [Fact]
    public void Roll_pool_never_double_adds()
    {
        var ids = new List<string> { "flash", MagnetBubble.VariantId };
        Assert.Equal(2, MagnetBubble.RollPool(ids, true, true).Count);
    }

    [Fact]
    public void Empty_and_null_inputs_are_safe()
    {
        var pool = MagnetBubble.RollPool(new List<string>(), true, true);
        Assert.Equal(new[] { MagnetBubble.VariantId }, pool);
        Assert.Empty(MagnetBubble.RollPool(null, false, true));
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void Eligibility_is_owned_and_on(bool owned, bool on, bool expected)
        => Assert.Equal(expected, MagnetBubble.IsEligible(owned, on));

    /// <summary>
    /// The two v2 bubbles chain through the SAME roll, each adding its own id once. This is the
    /// arrangement BubbleService.RollTriggerSpec uses, so a user who owns v2 and leaves both rows
    /// on rolls from the picked ids plus exactly two extras.
    /// </summary>
    [Fact]
    public void Chains_with_the_brain_drain_pool_without_stepping_on_it()
    {
        var pool = MagnetBubble.RollPool(
            BrainDrainBubble.RollPool(DefaultIds, true, true), true, true);

        Assert.Equal(DefaultIds.Count + 2, pool.Count);
        Assert.Single(pool, id => id == BrainDrainBubble.VariantId);
        Assert.Single(pool, id => id == MagnetBubble.VariantId);
        Assert.Equal(pool.Count, pool.Distinct().Count());
    }

    [Fact]
    public void Its_id_and_tint_are_its_own()
    {
        Assert.Equal("magnet_pull", MagnetBubble.VariantId);
        Assert.NotEqual(BrainDrainBubble.VariantId, MagnetBubble.VariantId);
        // Never the Brain Drain violet (0x6A22A8): the two have to be told apart at a glance.
        Assert.False(MagnetBubble.TintR == BrainDrainBubble.TintR
                     && MagnetBubble.TintG == BrainDrainBubble.TintG
                     && MagnetBubble.TintB == BrainDrainBubble.TintB);
        Assert.Equal(0x2F, MagnetBubble.TintR);
        Assert.Equal(0x6F, MagnetBubble.TintG);
        Assert.Equal(0xBF, MagnetBubble.TintB);
    }

    /// <summary>
    /// No sprite ships for it, so the body glyph is all the user has to tell it apart at a glance.
    /// It must not be one another bubble already wears - the chaos "spiral" variant's U+25CE was
    /// the original pick and collided exactly there.
    /// </summary>
    [Fact]
    public void Its_body_glyph_is_not_already_taken()
    {
        Assert.False(string.IsNullOrEmpty(MagnetBubble.Label));
        foreach (var v in ChaosBubbleVariants.All)
            Assert.NotEqual(v.Label, MagnetBubble.Label);
        Assert.NotEqual("◎", MagnetBubble.Label);   // the spiral variant
        Assert.NotEqual("◍", MagnetBubble.Label);   // the Brain Drain bubble
    }

    /// <summary>The floating pop word. Its pop reaches ShowChaosEffectLabel like any other, so a
    /// missing case would flash nothing where every neighbour flashes a token.</summary>
    [Fact]
    public void It_has_a_pop_word()
        => Assert.Equal("PULL", ChaosBubbleVariants.PopWordFor(MagnetBubble.VariantId));

    // ---- the early-pop bargain ---------------------------------------------------------

    [Fact]
    public void Early_window_is_the_documented_first_35_percent()
        => Assert.Equal(0.35, MagnetBubble.EarlyWindowFrac, 6);

    [Theory]
    [InlineData(0, 7000, true)]          // popped the instant it spawned
    [InlineData(2449, 7000, true)]       // last millisecond inside the window
    [InlineData(2450, 7000, false)]      // exactly 35% of 7000 - the boundary is NOT early
    [InlineData(2451, 7000, false)]
    [InlineData(6999, 7000, false)]
    public void Early_window_boundary(double ageMs, double lifeMs, bool expected)
        => Assert.Equal(expected, MagnetBubble.IsEarlyPop(ageMs, lifeMs));

    [Fact]
    public void A_bubble_with_no_life_has_no_early_window()
    {
        Assert.False(MagnetBubble.IsEarlyPop(0, 0));
        Assert.False(MagnetBubble.IsEarlyPop(0, -1));
    }

    [Theory]
    [InlineData(true, MotionLevel.Full, 2)]
    [InlineData(true, MotionLevel.Reduced, 2)]
    [InlineData(true, MotionLevel.Off, 1)]      // no attraction was offered, so no bonus
    [InlineData(false, MotionLevel.Full, 1)]
    [InlineData(false, MotionLevel.Reduced, 1)]
    [InlineData(false, MotionLevel.Off, 1)]
    public void Xp_multiplier(bool early, MotionLevel level, int expected)
        => Assert.Equal(expected, MagnetBubble.XpMultiplier(early, level));

    [Fact]
    public void Early_pays_exactly_double()
        => Assert.Equal(2, MagnetBubble.EarlyXpMultiplier);

    // ---- motion level ------------------------------------------------------------------

    [Fact]
    public void Reduced_is_half_strength_and_off_is_none()
    {
        Assert.Equal(1.0, MagnetBubble.StrengthFor(MotionLevel.Full), 6);
        Assert.Equal(0.5, MagnetBubble.StrengthFor(MotionLevel.Reduced), 6);
        Assert.Equal(0.0, MagnetBubble.StrengthFor(MotionLevel.Off), 6);
    }

    [Fact]
    public void Off_means_no_attraction_at_all()
    {
        // Rising, cursor hard to the right: a steering bubble would bend a long way.
        var v = new MagnetBubble.Velocity(0, -1);
        var after = MagnetBubble.Steer(v, 0, 0, 400, 0, MagnetBubble.StrengthFor(MotionLevel.Off), 1.0);
        Assert.Equal(v, after);   // byte-for-byte the same velocity: a plain float
    }

    // ---- attraction physics ------------------------------------------------------------

    private static double Heading(MagnetBubble.Velocity v) => Math.Atan2(v.Vy, v.Vx);

    /// <summary>|angle between this velocity and the line to the cursor|.</summary>
    private static double ErrorTo(MagnetBubble.Velocity v, double bx, double by, double cx, double cy)
        => Math.Abs(MagnetBubble.AngleDelta(Heading(v), Math.Atan2(cy - by, cx - bx)));

    [Fact]
    public void One_frame_turns_the_velocity_toward_the_cursor()
    {
        // Rising straight up at x=0; the cursor is out to the right on the same row.
        var v = new MagnetBubble.Velocity(0, -1);
        double before = ErrorTo(v, 0, 0, 400, 0);
        var after = MagnetBubble.Steer(v, 0, 0, 400, 0, 1.0, 1.0);

        Assert.True(ErrorTo(after, 0, 0, 400, 0) < before, "the heading should close on the cursor");
        Assert.True(after.Vx > 0, "and it should now be moving toward it, not away");
    }

    [Fact]
    public void It_homes_but_never_teleports()
    {
        // Sixty frames (two seconds) of full attraction from across the screen.
        double bx = 0, by = 0;
        const double cx = 900, cy = -400;
        var v = new MagnetBubble.Velocity(0, -1);
        for (int i = 0; i < 60; i++)
        {
            var next = MagnetBubble.Steer(v, bx, by, cx, cy, 1.0, 1.0);
            // No frame may move the bubble further than the speed cap allows: no snapping.
            double step = Math.Sqrt(next.Vx * next.Vx + next.Vy * next.Vy);
            Assert.True(step <= MagnetBubble.MaxSpeedDipPerFrame + 1e-9,
                        $"frame {i} moved {step:0.###} DIP, past the cap");
            v = next;
            bx += v.Vx; by += v.Vy;
        }
        // Two seconds of hunting should have it pointed at the cursor and closing.
        Assert.True(ErrorTo(v, bx, by, cx, cy) < 0.2, "should be aimed at the cursor by now");
        Assert.True(Math.Sqrt((cx - bx) * (cx - bx) + (cy - by) * (cy - by)) < 900,
                    "and closer than it started");
    }

    [Fact]
    public void The_turn_rate_is_capped_per_frame()
    {
        // Cursor directly behind: the shortest way round is half a turn, far past the cap.
        var v = new MagnetBubble.Velocity(2.0, 0);
        var after = MagnetBubble.Steer(v, 0, 0, -400, 0, 1.0, 1.0);
        double turned = Math.Abs(MagnetBubble.AngleDelta(Heading(v), Heading(after)));
        Assert.Equal(MagnetBubble.MaxTurnRadPerFrame, turned, 6);
    }

    [Fact]
    public void Speed_is_capped_however_long_it_chases()
    {
        var v = new MagnetBubble.Velocity(0, -1);
        for (int i = 0; i < 400; i++)
        {
            v = MagnetBubble.Steer(v, 0, 0, 5000, 0, 1.0, 1.0);
            Assert.True(v.Speed <= MagnetBubble.MaxSpeedDipPerFrame + 1e-9);
        }
        // It really does reach the ceiling, so the cap is doing work rather than never binding.
        Assert.Equal(MagnetBubble.MaxSpeedDipPerFrame, v.Speed, 6);
    }

    [Fact]
    public void Reduced_halves_the_turn_the_same_frame_would_have_made()
    {
        var v = new MagnetBubble.Velocity(0, -1);
        var full = MagnetBubble.Steer(v, 0, 0, 400, 0, MagnetBubble.StrengthFor(MotionLevel.Full), 1.0);
        var half = MagnetBubble.Steer(v, 0, 0, 400, 0, MagnetBubble.StrengthFor(MotionLevel.Reduced), 1.0);

        double turnedFull = Math.Abs(MagnetBubble.AngleDelta(Heading(v), Heading(full)));
        double turnedHalf = Math.Abs(MagnetBubble.AngleDelta(Heading(v), Heading(half)));
        Assert.Equal(turnedFull / 2.0, turnedHalf, 9);
        Assert.True(turnedHalf > 0, "Reduced still pulls, it just pulls half as hard");
    }

    [Fact]
    public void The_time_scale_scales_the_turn_too()
    {
        var v = new MagnetBubble.Velocity(0, -1);
        var full = MagnetBubble.Steer(v, 0, 0, 400, 0, 1.0, 1.0);
        var slow = MagnetBubble.Steer(v, 0, 0, 400, 0, 1.0, 0.5);
        Assert.Equal(Math.Abs(MagnetBubble.AngleDelta(Heading(v), Heading(full))) / 2.0,
                     Math.Abs(MagnetBubble.AngleDelta(Heading(v), Heading(slow))), 9);
    }

    [Fact]
    public void Inside_the_dead_zone_it_stops_steering_and_drifts_through()
    {
        var v = new MagnetBubble.Velocity(0, -1);
        // Cursor a hair inside the dead zone: no turn, no acceleration, no jitter.
        var after = MagnetBubble.Steer(v, 0, 0, MagnetBubble.DeadZoneDip - 1, 0, 1.0, 1.0);
        Assert.Equal(v, after);
    }

    [Fact]
    public void A_stalled_bubble_still_gets_a_heading()
    {
        var after = MagnetBubble.Steer(new MagnetBubble.Velocity(0, 0), 0, 0, 400, 0, 1.0, 1.0);
        Assert.True(after.Speed >= MagnetBubble.MinSpeedDipPerFrame);
        Assert.True(after.Vx > 0, "and it points at the cursor");
    }

    // ---- the cursor-stale fallback -----------------------------------------------------

    [Fact]
    public void Stale_cursor_window_is_two_seconds()
        => Assert.Equal(2.0, MagnetBubble.CursorStaleSeconds, 6);

    [Theory]
    [InlineData(true, 0.0, true)]
    [InlineData(true, 1.99, true)]
    [InlineData(true, 2.0, true)]        // exactly at the edge still counts
    [InlineData(true, 2.01, false)]      // parked longer than two seconds: back to a plain float
    [InlineData(true, 60.0, false)]
    [InlineData(false, 0.0, false)]      // off all monitors: nothing to steer at
    [InlineData(false, 0.5, false)]
    public void Cursor_usable(bool onScreen, double idleSec, bool expected)
        => Assert.Equal(expected, MagnetBubble.CursorUsable(onScreen, idleSec));

    // ---- the multi-monitor bound -------------------------------------------------------
    //
    // One bubble's own screen box, in DIPs: the left monitor of a pair, 0..1920 x 0..1080.

    private const double L = 0, R = 1920, T = 0, B = 1080;

    [Theory]
    [InlineData(960, 540, true)]         // middle of its own screen
    [InlineData(0, 0, true)]             // exactly the top-left corner
    [InlineData(1920, 1080, true)]       // exactly the bottom-right corner
    [InlineData(1919, 1079, true)]
    [InlineData(1921, 540, false)]       // one DIP onto the monitor to the right
    [InlineData(3000, 540, false)]       // squarely on monitor 2
    [InlineData(-1, 540, false)]         // a monitor to the LEFT of this one
    [InlineData(960, -1, false)]         // a monitor stacked above
    [InlineData(960, 1081, false)]       // a monitor stacked below
    public void Cursor_in_bounds(double cx, double cy, bool expected)
        => Assert.Equal(expected, MagnetBubble.CursorInBounds(cx, cy, L, R, T, B));

    /// <summary>
    /// The reason the bound exists: a perfectly live, freshly moved cursor sitting on a SECOND
    /// monitor must not steer a bubble that lives on the first. Without it the bubble beelines for
    /// the shared edge, crosses it, and is destroyed unpopped - every magnet, every time.
    /// </summary>
    [Fact]
    public void A_live_cursor_on_another_monitor_does_not_steer_this_bubble()
    {
        // Desktop-wide the sample is impeccable: on a monitor, moved this instant.
        Assert.True(MagnetBubble.CursorUsable(cursorOnScreen: true, secondsSinceCursorMoved: 0));
        // But it is on monitor 2, so this bubble may not chase it.
        Assert.False(MagnetBubble.CanSteer(true, 0, 3000, 540, L, R, T, B));
        // The same cursor back on its own screen does steer it.
        Assert.True(MagnetBubble.CanSteer(true, 0, 960, 540, L, R, T, B));
    }

    [Theory]
    [InlineData(true, 0.0, 960.0, 540.0, true)]
    [InlineData(true, 2.01, 960.0, 540.0, false)]    // stale, though it is in reach
    [InlineData(false, 0.0, 960.0, 540.0, false)]    // off all monitors, though the point is in reach
    [InlineData(true, 0.0, 3000.0, 540.0, false)]    // live, but out of this bubble's reach
    [InlineData(true, 2.01, 3000.0, 540.0, false)]   // neither
    public void Can_steer_is_both_rules(bool onScreen, double idleSec, double cx, double cy, bool expected)
        => Assert.Equal(expected, MagnetBubble.CanSteer(onScreen, idleSec, cx, cy, L, R, T, B));

    // ---- the ring pulse ----------------------------------------------------------------

    [Fact]
    public void Ring_pulse_stays_inside_its_band_and_actually_moves()
    {
        double min = double.MaxValue, max = double.MinValue;
        for (int i = 0; i < 600; i++)
        {
            double v = MagnetBubble.RingPulseAt(i * 0.02);
            min = Math.Min(min, v); max = Math.Max(max, v);
        }
        Assert.True(min >= 1.0 - MagnetBubble.RingPulseDepth - 1e-9);
        Assert.True(max <= 1.0 + 1e-9);
        Assert.True(max - min > MagnetBubble.RingPulseDepth * 0.9);
    }

    [Fact]
    public void Its_pulse_is_not_the_brain_drain_breathe()
    {
        Assert.NotEqual(BrainDrainBubble.PulseDepth, MagnetBubble.RingPulseDepth);
        Assert.NotEqual(BrainDrainBubble.PulseRateRadPerSec, MagnetBubble.RingPulseRateRadPerSec);
    }

    // ---- the persisted switch ----------------------------------------------------------

    [Fact]
    public void FreshInstall_MagnetBubbleOn()
        => Assert.True(new AppSettings().BubbleMagnetEnabled);

    [Fact]
    public void An_older_settings_file_without_the_key_reads_as_on()
    {
        var s = JsonConvert.DeserializeObject<AppSettings>("{}")!;
        Assert.True(s.BubbleMagnetEnabled);
    }

    [Fact]
    public void Switching_it_off_round_trips()
    {
        var s = JsonConvert.DeserializeObject<AppSettings>("{\"BubbleMagnetEnabled\": false}")!;
        Assert.False(s.BubbleMagnetEnabled);
        Assert.DoesNotContain(MagnetBubble.VariantId,
                              MagnetBubble.RollPool(DefaultIds, true, s.BubbleMagnetEnabled));
    }
}
