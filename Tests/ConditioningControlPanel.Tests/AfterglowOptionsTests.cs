using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Super;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>Super Afterglow's option box: the maths, the settings and the control.</summary>
public class AfterglowOptionsTests
{
    private static Func<double> Seq(params double[] v)
    {
        int i = 0;
        return () => v[i++ % v.Length];
    }

    // ---- frequency ----

    [Fact]
    public void Default_frequency_is_the_shipped_one_and_a_half_to_four_and_a_half_seconds()
    {
        AfterglowOptions.IntervalRange(5, out var lo, out var hi);
        Assert.Equal(AfterglowField.IntervalMinS, lo, 6);
        Assert.Equal(AfterglowField.IntervalMaxS, hi, 6);
        Assert.Equal(AfterglowField.NextInterval(0.37), AfterglowOptions.NextInterval(5, 0.37), 6);
    }

    [Fact]
    public void Higher_frequency_pops_more_often_and_lower_rarer()
    {
        double prev = double.MaxValue;
        for (int f = 1; f <= 10; f++)
        {
            AfterglowOptions.IntervalRange(f, out var lo, out var hi);
            Assert.True(lo < hi);
            Assert.True(hi < prev, $"frequency {f} is not quicker than {f - 1}");
            prev = hi;
        }
        AfterglowOptions.IntervalRange(10, out var lo10, out _);
        AfterglowOptions.IntervalRange(1, out _, out var hi1);
        Assert.Equal(0.375, lo10, 3);
        Assert.InRange(hi1, 13, 14);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(11, 10)]
    [InlineData(7, 7)]
    public void Frequency_clamps(int v, int expected) => Assert.Equal(expected, AfterglowOptions.ClampFrequency(v));

    // ---- size ----

    [Theory]
    [InlineData(1, 20)]
    [InlineData(5, 40)]
    [InlineData(10, 90)]
    [InlineData(0, 20)]
    [InlineData(99, 90)]
    public void Size_maps_twenty_to_ninety_dip(int size, double dip) => Assert.Equal(dip, AfterglowOptions.SizeDip(size), 6);

    [Fact]
    public void Size_factor_is_one_at_the_default_and_rises_monotonically()
    {
        Assert.Equal(1.0, AfterglowOptions.SizeFactor(5), 6);
        for (int s = 2; s <= 10; s++)
            Assert.True(AfterglowOptions.SizeFactor(s) > AfterglowOptions.SizeFactor(s - 1));
    }

    // ---- burst ----

    [Fact]
    public void One_word_is_the_old_single_pop()
    {
        var plan = AfterglowOptions.BurstPlan(1, Seq(0.9));
        var slot = Assert.Single(plan);
        Assert.Equal(0, slot.Line);
        Assert.Equal(0, slot.Nudge);
        Assert.Equal(0, slot.DelayS);
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    [InlineData(4, 2)]
    public void A_burst_spreads_wraps_to_two_lines_and_staggers_fifty_to_ninety_ms(int count, int rows)
    {
        var rng = new Random(count);
        for (int run = 0; run < 50; run++)
        {
            var plan = AfterglowOptions.BurstPlan(count, rng.NextDouble);
            Assert.Equal(count, plan.Length);
            Assert.Equal(rows, AfterglowOptions.RowsFor(count));
            Assert.Equal(0, plan[0].DelayS);
            for (int k = 1; k < count; k++)
                Assert.InRange(plan[k].DelayS - plan[k - 1].DelayS, 0.05 - 1e-9, 0.09 + 1e-9);

            // Every (row, col) place is used exactly once, at most two words per row.
            var places = plan.Select(p => (p.Row, p.Col)).ToArray();
            Assert.Equal(count, places.Distinct().Count());
            Assert.All(plan, p => Assert.InRange(p.RowSize, 1, AfterglowOptions.WordsPerRow));
            Assert.Equal(rows, plan.Select(p => p.Row).Distinct().Count());

            // Rows sit well apart: even with jitter, two rows never come closer than one font size.
            if (rows == 2)
            {
                double top = plan.Where(p => p.Row == 0).Max(p => p.Line);
                double bottom = plan.Where(p => p.Row == 1).Min(p => p.Line);
                Assert.True(bottom - top >= AfterglowOptions.LineSpacing - 2 * AfterglowOptions.LineJitter - 1e-9);
                Assert.True(bottom - top > 1.2, "rows must not touch");
            }
            Assert.All(plan, p => Assert.InRange(p.Nudge, -AfterglowOptions.NudgeSpan, AfterglowOptions.NudgeSpan));
            Assert.All(plan, p => Assert.InRange(p.Tilt, -AfterglowOptions.TiltMax, AfterglowOptions.TiltMax));
            Assert.All(plan, p => Assert.True(Math.Abs(p.Line) <= AfterglowOptions.BurstHalfLines(count) + 1e-9));
        }
    }

    [Fact]
    public void Burst_words_carry_a_slight_tilt_but_one_word_is_straight()
    {
        Assert.Equal(0, AfterglowOptions.BurstPlan(1, Seq(0.9))[0].Tilt);
        var plan = AfterglowOptions.BurstPlan(4, new Random(7).NextDouble);
        Assert.Contains(plan, p => Math.Abs(p.Tilt) > 0.01);
        Assert.True(AfterglowOptions.TiltMax is > 0.05 and < 0.25, "slight, not a spin");
    }

    [Fact]
    public void Count_clamps_to_four_and_raises_max_alive()
    {
        Assert.Equal(4, AfterglowOptions.BurstPlan(9, Seq(0.5)).Length);
        Assert.Single(AfterglowOptions.BurstPlan(0, Seq(0.5)));
        Assert.Equal(AfterglowField.MaxAlive, AfterglowOptions.MaxAlive(1));
        Assert.Equal(AfterglowField.MaxAlive * 4, AfterglowOptions.MaxAlive(4));
    }

    [Fact]
    public void Field_capacity_lets_a_whole_burst_live()
    {
        var f = new AfterglowField { Capacity = AfterglowOptions.MaxAlive(4) };
        for (int i = 0; i < 4; i++) f.Spawn("w" + i, i * 10, 0, 1, MotionLevel.Full, false, Seq(0.5));
        Assert.Equal(4, f.Pops.Count);
        var g = new AfterglowField();   // shipped capacity: the oldest goes
        for (int i = 0; i < 4; i++) g.Spawn("w" + i, i * 10, 0, 1, MotionLevel.Full, false, Seq(0.5));
        Assert.Equal(AfterglowField.MaxAlive, g.Pops.Count);
    }

    [Fact]
    public void Pops_take_one_of_the_two_colours()
    {
        var f = new AfterglowField();
        Assert.Equal(0, f.Spawn("a", 0, 0, 1, MotionLevel.Full, false, Seq(0.1)).Hue);
        Assert.Equal(1, f.Spawn("b", 0, 0, 1, MotionLevel.Full, false, Seq(0.9)).Hue);
        var g = new AfterglowField();
        g.Spawn("c", 0, 0, 1, MotionLevel.Full, false, new Random(3).NextDouble);
        Assert.All(Enumerable.Range(0, g.ParticleCount), i => Assert.InRange(g.Particles[i].Hue, 0, 1));
    }

    [Fact]
    public void Photosafe_burst_still_sheds_no_sparks()
    {
        var f = new AfterglowField { Capacity = 8 };
        for (int i = 0; i < 4; i++) f.Spawn("w", 0, 0, 2, MotionLevel.Full, true, Seq(0.5));
        Assert.Equal(0, f.ParticleCount);
    }

    [Theory]
    [InlineData(5, 4)]
    [InlineData(3, 3)]
    [InlineData(1, 4)]
    public void Word_picks_are_distinct_while_the_pool_lasts(int pool, int count)
    {
        var rng = new Random(pool * 7 + count);
        for (int run = 0; run < 50; run++)
        {
            var picks = AfterglowOptions.PickIndices(pool, count, rng.NextDouble);
            Assert.Equal(count, picks.Length);
            Assert.All(picks, p => Assert.InRange(p, 0, pool - 1));
            Assert.Equal(Math.Min(pool, count), picks.Take(Math.Min(pool, count)).Distinct().Count());
        }
        Assert.Empty(AfterglowOptions.PickIndices(0, 3, rng.NextDouble));
    }

    // ---- colours ----

    [Theory]
    [InlineData("#FF5FB0", 255, 95, 176)]
    [InlineData("5fffd0", 95, 255, 208)]
    [InlineData("#80102030", 16, 32, 48)]
    [InlineData(" #010203 ", 1, 2, 3)]
    public void Colours_parse(string hex, int r, int g, int b)
        => Assert.Equal(((byte)r, (byte)g, (byte)b), AfterglowOptions.ParseColor(hex, AfterglowOptions.ColorADefault));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("pink")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    public void A_bad_colour_falls_back_to_the_default(string? hex)
    {
        Assert.Equal(((byte)255, (byte)95, (byte)176), AfterglowOptions.ParseColor(hex, AfterglowOptions.ColorADefault));
        Assert.Equal("#5FFFD0", AfterglowOptions.Normalise(hex, AfterglowOptions.ColorBDefault));
    }

    [Fact]
    public void Default_colours_are_the_shipped_pink_and_mint_and_sparks_are_lighter()
    {
        Assert.Equal(((byte)255, (byte)95, (byte)176), AfterglowOptions.ParseColor(AfterglowOptions.ColorADefault, "#000000"));
        Assert.Equal(((byte)95, (byte)255, (byte)208), AfterglowOptions.ParseColor(AfterglowOptions.ColorBDefault, "#000000"));
        var sp = AfterglowOptions.SparkOf((95, 255, 208));
        Assert.True(sp.R > 95 && sp.G == 255 && sp.B > 208);
    }

    // ---- settings ----

    [Fact]
    public void Settings_default_to_the_shipped_effect_and_clamp()
    {
        var s = new AppSettings();
        Assert.Equal(5, s.AfterglowFrequency);
        Assert.Equal(1, s.AfterglowCount);
        Assert.Equal(5, s.AfterglowSize);
        Assert.Equal("#FF5FB0", s.AfterglowColorA);
        Assert.Equal("#5FFFD0", s.AfterglowColorB);

        s.AfterglowFrequency = 40; s.AfterglowCount = 9; s.AfterglowSize = -2;
        s.AfterglowColorA = "nope"; s.AfterglowColorB = "abcdef";
        Assert.Equal(10, s.AfterglowFrequency);
        Assert.Equal(4, s.AfterglowCount);
        Assert.Equal(1, s.AfterglowSize);
        Assert.Equal("#FF5FB0", s.AfterglowColorA);
        Assert.Equal("#ABCDEF", s.AfterglowColorB);
    }

    [Fact]
    public void Settings_round_trip_through_json()
    {
        var s = new AppSettings { AfterglowFrequency = 8, AfterglowCount = 3, AfterglowSize = 9, AfterglowColorA = "#112233", AfterglowColorB = "#445566" };
        var back = Newtonsoft.Json.JsonConvert.DeserializeObject<AppSettings>(Newtonsoft.Json.JsonConvert.SerializeObject(s))!;
        Assert.Equal(8, back.AfterglowFrequency);
        Assert.Equal(3, back.AfterglowCount);
        Assert.Equal(9, back.AfterglowSize);
        Assert.Equal("#112233", back.AfterglowColorA);
        Assert.Equal("#445566", back.AfterglowColorB);
    }
}

/// <summary>The option box XAML loads offscreen and opens.</summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class AfterglowOptionsBoxRenderTests
{
    [Fact]
    public void Box_realizes_and_opens()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var box = new AfterglowOptionsBox();
            var host = new Grid { Width = 600 };
            host.Children.Add(box);
            box.Visibility = Visibility.Visible;
            box.SetOpen(true);
            host.Measure(new Size(600, double.PositiveInfinity));
            host.Arrange(new Rect(new Point(0, 0), new Size(600, Math.Max(1, host.DesiredSize.Height))));
            host.UpdateLayout();
            Assert.True(box.IsOpen);
            Assert.True(box.DesiredSize.Height > 80, $"open box measured {box.DesiredSize.Height}");
            box.SetOpen(false);
            Assert.False(box.IsOpen);
        });
    }

    [Fact]
    public void Subliminal_panel_hosts_the_box_with_one_line()
    {
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "ConditioningControlPanel", "Features")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var xaml = System.IO.File.ReadAllText(System.IO.Path.Combine(dir!.FullName, "ConditioningControlPanel", "Features", "SubliminalFeatureControl.xaml"));
        Assert.Contains("<ctl:AfterglowOptionsBox/>", xaml);
    }
}
