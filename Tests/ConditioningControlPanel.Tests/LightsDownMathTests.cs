using System;
using ConditioningControlPanel.Services.Super;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Super Lights Down (rides the mandatory video): the pure depth, aperture and stutter maths.
/// Constants come from mkWatch in handoff-1001/super-effects-mockup.html. The overlay window
/// only copies a <see cref="LightsDownFrame"/> onto its visuals, so everything about how the
/// room falls away and comes back is pinned here.
/// </summary>
public class LightsDownMathTests
{
    private const double Dt = 1.0 / 60;

    private static LightsDownFrame Run(LightsDownState s, double seconds, bool attended,
        LightsDownMotion motion = LightsDownMotion.Full, bool photosafe = false)
    {
        var f = default(LightsDownFrame);
        int n = (int)Math.Round(seconds / Dt);
        for (int i = 0; i < n; i++) f = s.Step(Dt, attended, motion, photosafe);
        return f;
    }

    [Fact]
    public void Depth_rises_toward_092_and_never_passes_it()
    {
        var s = new LightsDownState();
        Run(s, 1, true);
        // 1 - e^-0.55 of the way there after one second.
        Assert.InRange(s.Depth, 0.92 * (1 - Math.Exp(-0.55)) - 0.01, 0.92 * (1 - Math.Exp(-0.55)) + 0.01);
        Run(s, 60, true);
        Assert.InRange(s.Depth, 0.91, LightsDownState.DepthMax);
    }

    [Fact]
    public void Depth_falls_at_13_per_second_when_not_watched()
    {
        var s = new LightsDownState();
        Run(s, 30, true);
        double from = s.Depth;
        Run(s, 0.5, false);
        Assert.InRange(s.Depth, from - 0.65 - 0.02, from - 0.65 + 0.02);
        Run(s, 1, false);
        Assert.Equal(0, s.Depth);
    }

    [Fact]
    public void Nothing_draws_at_rest()
    {
        var f = new LightsDownState().Step(Dt, false, LightsDownMotion.Full, false);
        Assert.Equal(0, f.DimAlpha);
        Assert.Equal(0, f.ApertureAlpha);
        Assert.Equal(0, f.GlowAlpha);
        Assert.Equal(0, f.RayAlpha);
        Assert.Equal(1, f.Swell);
        Assert.Equal(-1, f.LockRun);
        Assert.False(f.Lost);
    }

    [Fact]
    public void Full_depth_dims_half_and_swells_seven_percent()
    {
        var s = new LightsDownState();
        var f = Run(s, 60, true);
        Assert.InRange(f.DimAlpha - f.Beat, 0.5 * 0.91, 0.5 * 0.92 + 1e-9);
        Assert.InRange(f.Swell, 1.063, 1.0645);
        Assert.InRange(f.ApertureAlpha, 0.36, 0.369);
        Assert.InRange(f.Hue, 260, 340);
    }

    [Fact]
    public void Lock_in_runs_once_when_depth_first_passes_08()
    {
        var s = new LightsDownState();
        int fired = 0;
        double firedAt = -1;
        for (int i = 0; i < 60 * 30; i++)
        {
            var f = s.Step(Dt, true, LightsDownMotion.Full, false);
            if (f.LockInFired) { fired++; firedAt = s.Depth; }
        }
        Assert.Equal(1, fired);
        Assert.InRange(firedAt, 0.8, 0.82);
    }

    [Fact]
    public void Lock_in_rearms_after_attention_is_lost()
    {
        var s = new LightsDownState();
        Run(s, 30, true);
        Run(s, 2, false);
        int fired = 0;
        for (int i = 0; i < 60 * 30; i++)
            if (s.Step(Dt, true, LightsDownMotion.Full, false).LockInFired) fired++;
        Assert.Equal(1, fired);
    }

    [Fact]
    public void Lock_run_lasts_11_seconds()
    {
        var s = new LightsDownState();
        LightsDownFrame f;
        do f = s.Step(Dt, true, LightsDownMotion.Full, false); while (!f.LockInFired);
        Assert.InRange(f.LockRun, 0, 0.001);
        f = Run(s, 0.55, true);
        Assert.InRange(f.LockRun, 0.45, 0.55);
        f = Run(s, 0.6, true);
        Assert.Equal(-1, f.LockRun);
    }

    [Fact]
    public void Looking_away_stutters_three_times_in_065s_then_stops()
    {
        var s = new LightsDownState();
        Run(s, 30, true);
        int drops = 0;
        bool wasLow = false;
        bool sawSplit = false, sawStreak = false, sawLost = false;
        for (int i = 0; i < 39; i++) // 39 frames = 0.65 s
        {
            var f = s.Step(Dt, false, LightsDownMotion.Full, false);
            bool low = f.Depth < s.Depth * 0.5;
            if (low && !wasLow) drops++;
            wasLow = low;
            sawSplit |= f.SplitAlpha > 0;
            sawStreak |= f.StreakAlpha > 0;
            sawLost |= f.Lost;
        }
        Assert.Equal(3, drops);
        Assert.True(sawSplit && sawStreak && sawLost);
        s.Step(Dt, false, LightsDownMotion.Full, false);
        var after = s.Step(Dt, false, LightsDownMotion.Full, false);
        Assert.Equal(s.Depth, after.Depth, 9);
        Assert.Equal(0, after.SplitAlpha);
        Assert.Equal(0, after.StreakAlpha);
    }

    [Theory]
    [InlineData(LightsDownMotion.Full, true)]
    [InlineData(LightsDownMotion.Reduced, false)]
    [InlineData(LightsDownMotion.Off, false)]
    public void Photosafe_or_calm_motion_never_flickers_it_fades_in_04s(LightsDownMotion motion, bool photosafe)
    {
        var s = new LightsDownState();
        Run(s, 30, true, motion, photosafe);
        double prev = s.Depth;
        for (double t = 0; t < 0.4 + 2 * Dt; t += Dt)
        {
            var f = s.Step(Dt, false, motion, photosafe);
            Assert.True(f.Depth <= prev + 1e-12, "depth must only ever go down");
            Assert.Equal(0, f.SplitAlpha);
            Assert.Equal(0, f.StreakAlpha);
            prev = f.Depth;
        }
        Assert.Equal(0, s.Depth);
    }

    [Fact]
    public void Reduced_halves_the_swell_and_the_spin()
    {
        var full = new LightsDownState();
        var reduced = new LightsDownState();
        var ff = Run(full, 30, true, LightsDownMotion.Full);
        var fr = Run(reduced, 30, true, LightsDownMotion.Reduced);
        Assert.Equal((ff.Swell - 1) / 2, fr.Swell - 1, 6);
        Assert.InRange((fr.ApertureRotation - 0.2) / (ff.ApertureRotation - 0.2), 0.49, 0.51);
    }

    [Fact]
    public void Off_is_still_no_spin_no_swell_no_beat_no_run()
    {
        var s = new LightsDownState();
        double rot0 = s.Step(Dt, true, LightsDownMotion.Off, false).ApertureRotation;
        for (int i = 0; i < 60 * 30; i++)
        {
            var f = s.Step(Dt, true, LightsDownMotion.Off, false);
            Assert.Equal(rot0, f.ApertureRotation);
            Assert.Equal(1, f.Swell);
            Assert.Equal(0, f.Beat);
            Assert.Equal(-1, f.LockRun);
            Assert.Equal(1, f.ApertureClose); // the aperture sits shut and only fades in
        }
        Assert.InRange(s.Depth, 0.9, 0.92);
    }

    [Fact]
    public void Heartbeat_only_past_half_depth_and_capped()
    {
        var s = new LightsDownState();
        double maxBeat = 0;
        for (int i = 0; i < 60 * 40; i++)
        {
            var f = s.Step(Dt, true, LightsDownMotion.Full, false);
            if (f.Depth <= 0.5) Assert.Equal(0, f.Beat);
            maxBeat = Math.Max(maxBeat, f.Beat);
        }
        Assert.InRange(maxBeat, 0.04, 0.05 * 0.92 + 1e-9);
    }

    [Fact]
    public void A_hitch_cannot_jump_the_room_to_black()
    {
        var s = new LightsDownState();
        s.Step(5, true, LightsDownMotion.Full, false);
        Assert.True(s.Depth < 0.15);
    }

    [Fact]
    public void Aperture_closes_from_the_long_side_to_064_of_the_picture()
    {
        Assert.Equal(1920 * 0.95, LightsDownMath.ApertureRadius(1920, 1080, 1000, 0, 0), 6);
        Assert.Equal(640, LightsDownMath.ApertureRadius(1920, 1080, 1000, 1, 0), 6);
        Assert.Equal(640 * 1.18, LightsDownMath.ApertureRadius(1920, 1080, 1000, 1, 3), 6);
    }

    [Fact]
    public void Fully_open_aperture_clears_the_whole_screen()
    {
        // The open hexagon's inner radius must reach every screen corner, or a pale room would
        // already wear dark corners at depth 0+.
        foreach (var (w, h) in new[] { (1920.0, 1080.0), (1080.0, 1920.0), (3440.0, 1440.0), (1024.0, 1024.0) })
        {
            double inner = LightsDownMath.ApertureRadius(w, h, w, 0, 0) * Math.Cos(Math.PI / 6);
            Assert.True(inner >= Math.Sqrt(w * w + h * h) / 2, $"{w}x{h}");
        }
    }

    [Fact]
    public void Picture_is_contain_fit_and_centred()
    {
        Assert.Equal((0.0, 0.0, 1920.0, 1080.0), LightsDownMath.FitPicture(1920, 1080, 16.0 / 9));
        var (x, y, w, h) = LightsDownMath.FitPicture(1920, 1080, 9.0 / 16);
        Assert.Equal(1080, h, 6);
        Assert.Equal(607.5, w, 6);
        Assert.Equal((1920 - 607.5) / 2, x, 6);
        Assert.Equal(0, y, 6);
        Assert.Equal((0.0, 0.0, 1920.0, 1080.0), LightsDownMath.FitPicture(1920, 1080, 0));
    }

    [Fact]
    public void Hexagon_has_six_unit_vertices()
    {
        for (int i = 0; i < 6; i++)
        {
            var (x, y) = LightsDownMath.HexVertex(i);
            Assert.Equal(1, Math.Sqrt(x * x + y * y), 9);
        }
    }

    [Fact]
    public void Motes_spawn_at_14_per_second_at_full_depth()
    {
        int total = 0;
        for (int i = 0; i < 600; i++) total += LightsDownMath.MotesToSpawn(Dt, 1, i * 0.6180339887 % 1);
        Assert.InRange(total, 138, 142); // 10 s x 14, the rolls spread evenly
        Assert.Equal(0, LightsDownMath.MotesToSpawn(Dt, 0, 0));
        Assert.Equal(0, LightsDownMath.MoteAlpha(3, 1));
        Assert.Equal(0.55, LightsDownMath.MoteAlpha(1.5, 1), 6);
    }

    [Fact]
    public void Hsl_matches_known_colours()
    {
        Assert.Equal(((byte)255, (byte)0, (byte)0), LightsDownMath.Hsl(0, 1, 0.5));
        Assert.Equal(((byte)255, (byte)0, (byte)255), LightsDownMath.Hsl(300, 1, 0.5));
        Assert.Equal(((byte)255, (byte)0, (byte)255), LightsDownMath.Hsl(-60, 1, 0.5));
        Assert.Equal(((byte)255, (byte)255, (byte)255), LightsDownMath.Hsl(123, 0.9, 1));
    }

    [Theory]
    [InlineData(true, true, false, true, true)]
    [InlineData(false, true, false, true, false)]   // switched off or tier lost
    [InlineData(true, false, false, true, false)]   // panic / emergency exit ended the video
    [InlineData(true, true, true, true, false)]     // teardown in progress
    [InlineData(true, true, false, false, false)]   // no video window to ride
    public void Runs_only_while_the_video_it_rides_is_live(bool on, bool playing, bool cleaning, bool windows, bool expected)
        => Assert.Equal(expected, LightsDownMath.ShouldRun(on, playing, cleaning, windows));

    [Theory]
    [InlineData(false, false, false, true, true)]   // cursor on the picture
    [InlineData(false, false, false, false, false)]
    [InlineData(false, true, true, false, true)]    // gaze wins over the cursor
    [InlineData(false, true, false, true, false)]   // gaze away, cursor on: gaze wins
    [InlineData(true, false, false, true, false)]   // held video is never watched
    public void Attention_gaze_wins_when_live(bool held, bool gazeLive, bool gazeOn, bool cursorOn, bool expected)
        => Assert.Equal(expected, LightsDownMath.Attended(held, gazeLive, gazeOn, cursorOn));
}
