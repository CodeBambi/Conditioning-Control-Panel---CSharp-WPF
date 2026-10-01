using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Super;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>Afterglow's Duration, Opacity and Tilt sliders: maths, clamping, and that defaults change nothing.</summary>
public class AfterglowLookOptionsTests
{
    [Fact]
    public void Defaults_reproduce_the_shipped_look()
    {
        Assert.Equal(1.0, AfterglowOptions.DurationFactor(AfterglowOptions.DurationDefault), 9);
        Assert.Equal(1.0, AfterglowOptions.OpacityFactor(AfterglowOptions.OpacityDefault), 9);
        Assert.Equal(1.0, AfterglowOptions.TiltFactor(AfterglowOptions.TiltSliderDefault), 9);
        var s = new AppSettings();
        Assert.Equal(5, s.AfterglowDuration);
        Assert.Equal(100, s.AfterglowOpacity);
        Assert.Equal(5, s.AfterglowTilt);
    }

    [Fact]
    public void Duration_runs_from_a_third_to_four_times_and_only_rises()
    {
        Assert.InRange(AfterglowOptions.DurationFactor(1), 0.3, 0.36);
        Assert.InRange(AfterglowOptions.DurationFactor(10), 3.9, 4.1);
        for (int i = 2; i <= 10; i++)
            Assert.True(AfterglowOptions.DurationFactor(i) > AfterglowOptions.DurationFactor(i - 1));
        Assert.Equal(AfterglowOptions.DurationFactor(1), AfterglowOptions.DurationFactor(-5), 9);
        Assert.Equal(AfterglowOptions.DurationFactor(10), AfterglowOptions.DurationFactor(99), 9);
    }

    [Fact]
    public void Life_and_alpha_scale_with_the_duration_but_off_stays_one_short_fade()
    {
        double f = AfterglowOptions.DurationFactor(10);
        Assert.Equal(AfterglowField.Life(MotionLevel.Full, false) * f, AfterglowField.Life(MotionLevel.Full, false, f), 9);
        Assert.Equal(AfterglowField.Life(MotionLevel.Full, true) * f, AfterglowField.Life(MotionLevel.Full, true, f), 9);
        Assert.Equal(AfterglowField.OffFadeS, AfterglowField.Life(MotionLevel.Off, false, f), 9);
        // The same point in the envelope, scaled: half way through the fade-in.
        Assert.Equal(0.5, AfterglowField.Alpha(AfterglowField.FadeInS * f / 2, MotionLevel.Full, false, f), 6);
        Assert.Equal(1.0, AfterglowField.Alpha((AfterglowField.FadeInS + AfterglowField.HoldS / 2) * f, MotionLevel.Full, false, f), 6);
        Assert.Equal(0.0, AfterglowField.Alpha(AfterglowField.Life(MotionLevel.Full, false, f), MotionLevel.Full, false, f), 6);
        // A bad factor falls back to 1.
        Assert.Equal(AfterglowField.Life(MotionLevel.Full, false), AfterglowField.Life(MotionLevel.Full, false, 0), 9);
    }

    [Fact]
    public void A_field_removes_a_pop_after_its_scaled_life()
    {
        var field = new AfterglowField { DurationFactor = 3.0 };
        var r = new System.Random(1);
        field.Spawn("x", 100, 100, 1, MotionLevel.Full, false, r.NextDouble);
        field.Step(AfterglowField.Life(MotionLevel.Full, false) * 2, MotionLevel.Full, false, r.NextDouble);   // past the shipped life, inside the scaled one
        Assert.Single(field.Pops);
        field.Step(AfterglowField.Life(MotionLevel.Full, false) * 2, MotionLevel.Full, false, r.NextDouble);
        Assert.Empty(field.Pops);
    }

    [Fact]
    public void Opacity_is_a_tenth_to_full_and_tilt_is_none_to_double()
    {
        Assert.Equal(0.1, AfterglowOptions.OpacityFactor(0), 9);
        Assert.Equal(0.5, AfterglowOptions.OpacityFactor(50), 9);
        Assert.Equal(1.0, AfterglowOptions.OpacityFactor(500), 9);
        Assert.Equal(0.0, AfterglowOptions.TiltFactor(0), 9);
        Assert.Equal(2.0, AfterglowOptions.TiltFactor(10), 9);
        Assert.Equal(2.0, AfterglowOptions.TiltFactor(99), 9);
    }

    [Fact]
    public void The_settings_clamp_and_round_trip()
    {
        var s = new AppSettings { AfterglowDuration = 40, AfterglowOpacity = -3, AfterglowTilt = 99 };
        Assert.Equal(10, s.AfterglowDuration);
        Assert.Equal(10, s.AfterglowOpacity);
        Assert.Equal(10, s.AfterglowTilt);
        s.AfterglowDuration = 3; s.AfterglowOpacity = 40; s.AfterglowTilt = 0;
        var back = Newtonsoft.Json.JsonConvert.DeserializeObject<AppSettings>(Newtonsoft.Json.JsonConvert.SerializeObject(s))!;
        Assert.Equal(3, back.AfterglowDuration);
        Assert.Equal(40, back.AfterglowOpacity);
        Assert.Equal(0, back.AfterglowTilt);
    }
}
