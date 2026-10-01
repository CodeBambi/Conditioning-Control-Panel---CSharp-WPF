using System;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Super;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Super Inner Bloom: the pure maths. The layer only draws what this decides - the nesting a
/// bloom carries, where the kids orbit, where they press the film out, how the pop runs, how a
/// released kid travels and escapes, and when a sweep counts as clean.
/// </summary>
public class InnerBloomTests
{
    private static readonly InnerBloom.Motion Full = InnerBloom.Motion.For(MotionLevel.Full);

    [Fact]
    public void Plan_carries_two_or_three_mediums_and_at_most_one_carrier_of_two_smalls()
    {
        var r = new Random(7);
        bool sawCarrier = false, sawPlain = false;
        for (int i = 0; i < 400; i++)
        {
            var p = InnerBloom.Plan(r);
            Assert.InRange(p.Kids.Count, 2, 3);
            int carriers = p.Kids.Count(k => k.IsCarrier);
            Assert.InRange(carriers, 0, 1);
            foreach (var k in p.Kids.Where(k => k.IsCarrier))
            {
                Assert.Equal(2, k.Kids.Count);
                Assert.All(k.Kids, s => Assert.False(s.IsCarrier));   // three levels, never four
            }
            sawCarrier |= carriers == 1;
            sawPlain |= carriers == 0;
        }
        Assert.True(sawCarrier && sawPlain);
    }

    [Fact]
    public void Released_count_covers_every_nested_bubble()
    {
        var p = new InnerBloom.Node(0, new[]
        {
            new InnerBloom.Node(10),
            new InnerBloom.Node(20, new[] { new InnerBloom.Node(30), new InnerBloom.Node(40) }),
            new InnerBloom.Node(50),
        });
        Assert.Equal(5, p.ReleasedCount());
        Assert.Equal(4, p.Leaves().Count());
    }

    [Fact]
    public void Released_kids_are_sized_to_the_film_they_sat_in_and_stay_catchable()
    {
        Assert.Equal(100, InnerBloom.ReleasedSizeDip(45));
        Assert.Equal(InnerBloom.MinKidSizeDip, InnerBloom.ReleasedSizeDip(3));
        var plan = InnerBloom.Plan(new Random(1));
        Assert.True(plan.IsRoot);
        Assert.All(plan.Kids, k => Assert.False(k.IsRoot));
    }

    [Fact]
    public void Spawn_roll_is_about_one_in_six()
    {
        var r = new Random(3);
        int hits = Enumerable.Range(0, 60000).Count(_ => InnerBloom.RollSpawn(r));
        Assert.InRange(hits / 60000.0, 1 / 6.0 - .01, 1 / 6.0 + .01);
    }

    [Fact]
    public void Kid_orbit_is_0_42R_modulated_by_sin_t_1_3_plus_2j()
    {
        const double R = 100;
        for (int j = 0; j < 3; j++)
            for (double t = 0; t < 6; t += .37)
            {
                var k = InnerBloom.KidAt(j, 3, t, R, 0, Full);
                Assert.Equal(R * .42 * (1 + .26 * Math.Sin(t * 1.3 + j * 2)), k.Orbit, 9);
                Assert.Equal(Math.Cos(k.Angle) * k.Orbit, k.X, 9);
                Assert.Equal(Math.Sin(k.Angle) * k.Orbit * .85, k.Y, 9);
            }
    }

    [Fact]
    public void Motion_off_holds_the_kids_still()
    {
        var off = InnerBloom.Motion.For(MotionLevel.Off);
        var a = InnerBloom.KidAt(1, 3, 0, 100, 0, off);
        var b = InnerBloom.KidAt(1, 3, 9.5, 100, 0, off);
        Assert.Equal(a, b);
        Assert.Equal((0d, 0d), InnerBloom.ReleaseOffset(1, .8, off));
    }

    [Fact]
    public void Reduced_halves_speed_and_amplitude()
    {
        var red = InnerBloom.Motion.For(MotionLevel.Reduced);
        Assert.Equal(.5, red.Speed);
        Assert.Equal(.5, red.Amp);
        var f = InnerBloom.ReleaseOffset(.3, .5, Full);
        var h = InnerBloom.ReleaseOffset(.3, .5, red);
        Assert.Equal(f.Dx / 2, h.Dx, 9);
    }

    [Fact]
    public void Film_bulges_only_where_a_kid_presses_past_0_8R()
    {
        // Leaves (0.22R) never reach 0.8R at the widest orbit; a carrier (0.30R) does.
        var parent = new InnerBloom.Node(0, new[] { new InnerBloom.Node(1), new InnerBloom.Node(2, new[] { new InnerBloom.Node(4), new InnerBloom.Node(5) }), new InnerBloom.Node(3) }) { IsRoot = true };
        Span<InnerBloom.Bulge> buf = stackalloc InnerBloom.Bulge[4];
        bool sawNone = false, sawSome = false;
        for (double t = 0; t < 10; t += .05)
        {
            int n = InnerBloom.Bulges(parent, t, 100, 0, Full, buf);
            for (int i = 0; i < n; i++)
            {
                var b = buf[i];
                Assert.True(b.Mag > 0);
                // On the bulge the film reaches further than its opposite side.
                double here = InnerBloom.RadiusFactor(b.Angle, buf[..n]);
                Assert.True(here > 1);
            }
            sawNone |= n == 0;
            sawSome |= n > 0;
        }
        Assert.True(sawNone && sawSome);
        Assert.Equal(1.0, InnerBloom.RadiusFactor(1.2, ReadOnlySpan<InnerBloom.Bulge>.Empty));
    }

    [Fact]
    public void Bulge_is_gaussian_and_wraps_the_circle()
    {
        var b = new[] { new InnerBloom.Bulge(0, .3, .4) };
        Assert.Equal(1.3, InnerBloom.RadiusFactor(0, b), 9);
        Assert.Equal(InnerBloom.RadiusFactor(.2, b), InnerBloom.RadiusFactor(Math.PI * 2 - .2, b), 9);
        Assert.True(InnerBloom.RadiusFactor(Math.PI, b) < 1.001);
    }

    [Fact]
    public void Pop_squeezes_for_a_quarter_second_then_tears_round_in_0_35s()
    {
        Assert.Equal(0, InnerBloom.Squeeze(0));
        Assert.Equal(1, InnerBloom.Squeeze(.25));
        Assert.Equal(0, InnerBloom.TearGap(0));
        Assert.Equal(Math.PI, InnerBloom.TearGap(.35), 9);
        Assert.Equal(Math.PI, InnerBloom.TearGap(2), 9);
    }

    [Fact]
    public void Release_moves_outward_and_lifts()
    {
        var right = InnerBloom.ReleaseOffset(0, .6, Full);
        Assert.True(right.Dx > 30);
        Assert.True(right.Dy < 0);    // minus 30 up with no downward component
        var e = (1 - Math.Exp(-2.4 * .6)) / 2.4;
        Assert.Equal(100 * e, right.Dx, 9);
        Assert.Equal(-30 * e - .36 * 3, right.Dy, 9);
        // It eases out: the second half second covers less ground than the first.
        var a = InnerBloom.ReleaseOffset(0, .5, Full).Dx;
        var b = InnerBloom.ReleaseOffset(0, 1.0, Full).Dx;
        Assert.True(b - a < a);
    }

    [Fact]
    public void A_small_escapes_after_about_a_second()
    {
        Assert.Equal(1, InnerBloom.Escape(.9, Full).Alpha);
        Assert.InRange(InnerBloom.Escape(1.5, Full).Alpha, .4, .6);
        Assert.Equal(0, InnerBloom.Escape(2.01, Full).Alpha);
        Assert.True(InnerBloom.Escape(1.5, Full).Dy < InnerBloom.Escape(1.0, Full).Dy);
    }

    [Fact]
    public void Sweep_is_clean_once_when_every_released_bubble_is_popped()
    {
        var s = new InnerBloom.Sweep(3);
        Assert.False(s.Popped());
        Assert.False(s.Popped());
        Assert.True(s.Popped());
        Assert.False(s.Popped());   // never twice
    }

    [Fact]
    public void Sweep_is_lost_when_any_one_gets_away()
    {
        var s = new InnerBloom.Sweep(3);
        s.Popped();
        s.Lose();
        s.Popped();
        Assert.False(s.Popped());
        Assert.True(s.Lost);
    }

    [Fact]
    public void Kids_on_the_near_side_of_the_orbit_draw_bigger_and_the_far_side_dims()
    {
        // The orbit is a tilted ring seen from above: sin(angle) = +1 is nearest the viewer.
        Assert.Equal(1, InnerBloom.Depth(Math.PI / 2, Full), 9);
        Assert.Equal(-1, InnerBloom.Depth(-Math.PI / 2, Full), 9);
        Assert.Equal(1.14, InnerBloom.DepthSize(1), 9);
        Assert.Equal(.86, InnerBloom.DepthSize(-1), 9);
        Assert.Equal(1, InnerBloom.DepthAlpha(1), 9);     // only the far half dims
        Assert.Equal(1, InnerBloom.DepthAlpha(0), 9);
        Assert.Equal(.65, InnerBloom.DepthAlpha(-1), 9);

        var parent = new InnerBloom.Node(0, new[] { new InnerBloom.Node(1), new InnerBloom.Node(2) }) { IsRoot = true };
        double near = double.MinValue, far = double.MaxValue;
        for (double t = 0; t < 10; t += .05)
        {
            var p = InnerBloom.Place(parent, 0, t, 100, Full);
            Assert.Equal(InnerBloom.KidRadius(parent.Kids[0], 100) * InnerBloom.DepthSize(InnerBloom.Depth(p.Angle, Full)), p.Radius, 9);
            near = Math.Max(near, p.Radius);
            far = Math.Min(far, p.Radius);
        }
        Assert.True(near > 22 * 1.13 && far < 22 * .87);
    }

    [Fact]
    public void Depth_follows_motion_amplitude_and_stops_under_off()
    {
        var red = InnerBloom.Motion.For(MotionLevel.Reduced);
        var off = InnerBloom.Motion.For(MotionLevel.Off);
        Assert.Equal(.5, InnerBloom.Depth(Math.PI / 2, red), 9);
        Assert.Equal(0, InnerBloom.Depth(Math.PI / 2, off), 9);
        // Under Off a still kid keeps its plain size, so a release spawns it at the size it showed.
        var parent = new InnerBloom.Node(0, new[] { new InnerBloom.Node(1), new InnerBloom.Node(2) }) { IsRoot = true, Seed = 1.3 };
        Assert.Equal(22, InnerBloom.Place(parent, 1, 4, 100, off).Radius, 9);
    }

    [Fact]
    public void A_near_carrier_presses_the_film_harder_than_a_far_one()
    {
        // One carrier alone, orbit wobble at its widest: compare the bulge with the kid near vs far.
        var parent = new InnerBloom.Node(0, new[] { new InnerBloom.Node(2, new[] { new InnerBloom.Node(4), new InnerBloom.Node(5) }) }) { IsRoot = true };
        Span<InnerBloom.Bulge> buf = stackalloc InnerBloom.Bulge[4];
        double nearMag = 0, farMag = 0;
        for (double t = 0; t < 40; t += .01)
        {
            if (InnerBloom.Bulges(parent, t, 100, 0, Full, buf) == 0) continue;
            double z = InnerBloom.Depth(buf[0].Angle, Full);
            if (z > .9) nearMag = Math.Max(nearMag, buf[0].Mag);
            if (z < -.9) farMag = Math.Max(farMag, buf[0].Mag);
        }
        // Far away the carrier sinks inside the film (0.86 size never reaches 0.8R); near, it pushes out.
        Assert.True(nearMag > farMag && nearMag > 0, $"near {nearMag} far {farMag}");
    }
}
