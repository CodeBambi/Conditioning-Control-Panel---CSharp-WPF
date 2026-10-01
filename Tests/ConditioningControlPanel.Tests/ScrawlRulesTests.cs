using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Super;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Super Scrawl: the rules behind wall stamps, corner hits and the click slam. The field is a
/// 1920x1080 monitor and the bouncing text font is 72 unless a test says so.
/// </summary>
public class ScrawlRulesTests
{
    private static readonly ScrawlBounds Screen = new(0, 0, 1920, 1080);
    private const double Fs = 72;

    [Fact]
    public void A_side_hit_wins_over_top_and_bottom()
    {
        Assert.Equal(ScrawlWall.Left, ScrawlRules.WallOf(true, false, true, false));
        Assert.Equal(ScrawlWall.Right, ScrawlRules.WallOf(false, true, false, true));
        Assert.Equal(ScrawlWall.Top, ScrawlRules.WallOf(false, false, true, false));
        Assert.Equal(ScrawlWall.Bottom, ScrawlRules.WallOf(false, false, false, true));
        Assert.Equal(ScrawlWall.None, ScrawlRules.WallOf(false, false, false, false));
    }

    [Fact]
    public void Top_and_bottom_stamps_read_left_to_right_flush_on_their_wall()
    {
        var top = ScrawlRules.PlaceStamp(ScrawlWall.Top, 900, 30, Screen, Fs, 0);
        Assert.Equal(900, top.X);
        Assert.Equal(0.85 * Fs, top.Y, 6);
        Assert.Equal(0, top.Rotation);

        var bottom = ScrawlRules.PlaceStamp(ScrawlWall.Bottom, 900, 1050, Screen, Fs, 1);
        Assert.Equal(1080 - 0.85 * Fs, bottom.Y, 6);
        Assert.Equal(0.07, bottom.Rotation, 6);
    }

    [Fact]
    public void Side_stamps_turn_ninety_degrees_into_the_wall_with_jitter()
    {
        var left = ScrawlRules.PlaceStamp(ScrawlWall.Left, 40, 500, Screen, Fs, -1);
        Assert.Equal(0.85 * Fs, left.X, 6);
        Assert.Equal(-Math.PI / 2 - 0.07, left.Rotation, 6);

        var right = ScrawlRules.PlaceStamp(ScrawlWall.Right, 1880, 500, Screen, Fs, 0.5);
        Assert.Equal(1920 - 0.85 * Fs, right.X, 6);
        Assert.Equal(Math.PI / 2 + 0.035, right.Rotation, 6);
    }

    [Fact]
    public void Jitter_never_passes_seven_hundredths_of_a_radian()
    {
        var s = ScrawlRules.PlaceStamp(ScrawlWall.Top, 900, 0, Screen, Fs, 40);
        Assert.Equal(0.07, s.Rotation, 6);
    }

    [Fact]
    public void Stamps_keep_clear_of_the_corners()
    {
        var s = ScrawlRules.PlaceStamp(ScrawlWall.Top, 5, 0, Screen, Fs, 0);
        Assert.Equal(1.6 * Fs, s.X, 6);
        var r = ScrawlRules.PlaceStamp(ScrawlWall.Right, 1920, 1075, Screen, Fs, 0);
        Assert.Equal(1080 - 1.6 * Fs, r.Y, 6);
    }

    [Fact]
    public void A_corner_needs_both_axes_within_one_point_four_font_sizes()
    {
        // Word box flush on the left wall, top edge 90 px down: 90 < 1.4 * 72 = 100.8
        Assert.True(ScrawlRules.IsCorner(0, 90, 300, 170, Screen, Fs));
        // 110 px down is not a corner
        Assert.False(ScrawlRules.IsCorner(0, 110, 300, 190, Screen, Fs));
        // Bottom right
        Assert.True(ScrawlRules.IsCorner(1600, 1000, 1920, 1080, Screen, Fs));
        // Middle of the top wall is not a corner
        Assert.False(ScrawlRules.IsCorner(800, 0, 1100, 80, Screen, Fs));
    }

    [Fact]
    public void Corner_stamps_tuck_into_the_nearest_corner_along_the_diagonal()
    {
        var tl = ScrawlRules.PlaceCornerStamp(100, 60, Screen, Fs);
        Assert.Equal(1.7 * Fs, tl.X, 6);
        Assert.Equal(1.4 * Fs, tl.Y, 6);
        Assert.Equal(-0.35, tl.Rotation, 6);

        var tr = ScrawlRules.PlaceCornerStamp(1800, 60, Screen, Fs);
        Assert.Equal(1920 - 1.7 * Fs, tr.X, 6);
        Assert.Equal(0.35, tr.Rotation, 6);

        var br = ScrawlRules.PlaceCornerStamp(1800, 1000, Screen, Fs);
        Assert.Equal(-0.35, br.Rotation, 6);
    }

    [Fact]
    public void The_wall_glow_sits_on_the_wall_line_at_the_hit()
    {
        Assert.Equal((0.0, 400.0), ScrawlRules.GlowPoint(ScrawlWall.Left, 50, 400, Screen));
        Assert.Equal((700.0, 1080.0), ScrawlRules.GlowPoint(ScrawlWall.Bottom, 700, 1040, Screen));
    }

    [Fact]
    public void Ink_is_stable_per_word_and_one_of_three()
    {
        Assert.Equal(ScrawlRules.InkFor("SINK"), ScrawlRules.InkFor("sink"));
        foreach (var w in new[] { "LET", "GO", "SINK", "DEEP", "", null })
            Assert.InRange(ScrawlRules.InkFor(w), 0, 2);
    }

    [Fact]
    public void Stamps_fade_over_fourteen_seconds_and_flare_after_a_slam()
    {
        Assert.Equal(1, ScrawlRules.StampAlpha(0, 14, -1));
        Assert.Equal(0.5, ScrawlRules.StampAlpha(7, 14, -1), 6);
        Assert.Equal(0, ScrawlRules.StampAlpha(15, 14, -1));
        // Half faded, flared just now: 0.5 * 1.7
        Assert.Equal(0.85, ScrawlRules.StampAlpha(7, 14, 0), 6);
        // The flare is gone after 0.5 s
        Assert.Equal(0.5, ScrawlRules.StampAlpha(7, 14, 0.6), 6);
        // Never above one
        Assert.Equal(1, ScrawlRules.StampAlpha(1, 14, 0));
    }

    [Fact]
    public void Press_in_runs_from_one_and_a_half_to_one_and_obeys_motion()
    {
        Assert.Equal(1.5, ScrawlRules.PressScale(0, false, MotionLevel.Full), 6);
        Assert.Equal(1, ScrawlRules.PressScale(0.13, false, MotionLevel.Full), 6);
        Assert.Equal(1.25, ScrawlRules.PressScale(0, false, MotionLevel.Reduced), 6);
        Assert.Equal(1, ScrawlRules.PressScale(0, false, MotionLevel.Off));
        Assert.Equal(1.55, ScrawlRules.PressScale(0, true, MotionLevel.Full), 6);
    }

    [Fact]
    public void The_slammed_word_swells_to_three_point_two_then_lets_go()
    {
        Assert.Equal(1, ScrawlRules.SlamWordScale(0, MotionLevel.Full), 6);
        Assert.Equal(2.1, ScrawlRules.SlamWordScale(0.1, MotionLevel.Full), 6);
        Assert.Equal(3.2, ScrawlRules.SlamWordScale(0.3, MotionLevel.Full), 6);
        Assert.Equal(1, ScrawlRules.SlamWordScale(0.45, MotionLevel.Full));
        Assert.Equal(2.1, ScrawlRules.SlamWordScale(0.3, MotionLevel.Reduced), 6);
        Assert.Equal(1, ScrawlRules.SlamWordScale(0.3, MotionLevel.Off));
    }

    [Fact]
    public void A_slam_knocks_other_words_straight_away_at_the_scaled_impulse()
    {
        var (vx, vy) = ScrawlRules.Knock(1060, 540, 960, 540, 115);
        Assert.Equal(170, vx, 6);
        Assert.Equal(0, vy, 6);

        // A word cruising at 230 is knocked twice as hard, keeping the mockup's ratio
        var (ux, uy) = ScrawlRules.Knock(960, 240, 960, 540, 230);
        Assert.Equal(0, ux, 6);
        Assert.Equal(-340, uy, 6);

        // A word sitting on the slam point still gets a push
        var (zx, zy) = ScrawlRules.Knock(960, 540, 960, 540, 115);
        Assert.True(Math.Abs(zx) + Math.Abs(zy) > 100);
    }

    [Fact]
    public void Knocked_words_relax_back_to_their_cruise_speed()
    {
        Assert.Equal(1, ScrawlRules.RelaxFactor(115, 115, 0.016));
        double speed = 285;
        for (int i = 0; i < 600; i++) speed *= ScrawlRules.RelaxFactor(speed, 115, 1 / 60.0);
        Assert.InRange(speed, 115, 115 * 1.03);
        // One frame never overshoots below cruise
        Assert.True(285 * ScrawlRules.RelaxFactor(285, 115, 5) >= 115 - 1e-9);
    }

    [Fact]
    public void Shake_halves_under_reduced_motion_and_is_gone_under_off()
    {
        Assert.Equal(1, ScrawlRules.ShakeGain(MotionLevel.Full));
        Assert.Equal(0.5, ScrawlRules.ShakeGain(MotionLevel.Reduced));
        Assert.Equal(0, ScrawlRules.ShakeGain(MotionLevel.Off));
        // The mockup's decay, 3.5 per second
        Assert.Equal(0, ScrawlRules.DecayShake(1, 1 / 3.5), 6);
        Assert.Equal(0.5, ScrawlRules.DecayShake(1, 0.5 / 3.5), 6);
        // Photosafe never shakes, whatever the motion level
        Assert.Equal(0, ScrawlRules.ShakeGain(MotionLevel.Full, photosafe: true));
        Assert.Equal(0, ScrawlRules.ShakeGain(MotionLevel.Reduced, photosafe: true));
    }

    [Fact]
    public void The_knock_halves_under_reduced_motion_and_is_gone_under_off()
    {
        Assert.Equal(85, ScrawlRules.Knock(1060, 540, 960, 540, 115, MotionLevel.Reduced).Dvx, 6);
        var (ox, oy) = ScrawlRules.Knock(1060, 540, 960, 540, 115, MotionLevel.Off);
        Assert.Equal(0, ox);
        Assert.Equal(0, oy);
    }

    [Fact]
    public void A_slam_never_swallows_a_click_meant_for_a_ccp_window()
    {
        // Another app or the desktop under the word: the click is a slam
        Assert.True(ScrawlRules.MaySwallow(onWord: true, targetIsOurs: false, targetClickThrough: false));
        // A click-through overlay of ours (the words, a compositor layer): still a slam
        Assert.True(ScrawlRules.MaySwallow(onWord: true, targetIsOurs: true, targetClickThrough: true));
        // CCP's own input window (panel, Lockdown, a lock card, a leash window, an attention check): never
        Assert.False(ScrawlRules.MaySwallow(onWord: true, targetIsOurs: true, targetClickThrough: false));
        // Off the word: never
        Assert.False(ScrawlRules.MaySwallow(onWord: false, targetIsOurs: false, targetClickThrough: false));
    }

    [Fact]
    public void Ink_dots_land_in_their_ring()
    {
        Span<(double X, double Y, double R)> dots = stackalloc (double, double, double)[7];
        ScrawlRules.Dots(new Random(3), 7, Fs, 0.9, 2.0, 0.55, 0.8, 2.4, dots);
        foreach (var d in dots)
        {
            double unflat = Math.Sqrt(d.X * d.X + (d.Y / 0.55) * (d.Y / 0.55));
            Assert.InRange(unflat, 0.9 * Fs - 1e-6, 2.0 * Fs + 1e-6);
            Assert.InRange(d.R, 0.8, 2.4);
        }
    }
}
