using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Flash;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Where a burst of pendulum flashes hangs from.
///
/// <para><b>Why this suite exists.</b> Every pendulum used to hang from <c>bx + bw / 2</c>, the
/// exact top centre of the monitor. Amplitude, period, phase and rope were already rolled per
/// flash and none of that separates two pictures on the same nail, so a burst read as one picture
/// smeared over itself ("multiple pendulums often playing on top of each other", Tock, tier2
/// 2026-09-19). The rig hangs each new one clear of the ones already up; these tests measure the
/// rest positions, which is what a still frame of a burst actually looks like.</para>
///
/// <para>Bounds are a 1080p monitor at the origin unless a test says otherwise. Pure maths - the
/// same two calls the compositor makes.</para>
/// </summary>
public class FlashPendulumRigTests
{
    private const double BW = 1920, BH = 1080;

    /// <summary>A quarter-width flash: the size several of them can genuinely share a screen at.</summary>
    private const double MW = 384, MH = 216;

    /// <summary>
    /// A burst: <paramref name="count"/> pendulums spawned one after another, each told about the
    /// ones already up - exactly what FlashService.LivePendulumsOn hands FlashMotion.Create.
    /// </summary>
    private static List<FlashMotionState> Burst(int count, int seed = 7,
        double mw = MW, double mh = MH, double bw = BW, double bh = BH)
    {
        var rng = new Random(seed);
        var up = new List<FlashMotionState>();
        var taken = new List<PendulumNeighbour>();
        for (int i = 0; i < count; i++)
        {
            // Spawn Ys spread down the screen, the way the placer scatters a burst.
            var y = 120 + i * 170;
            var s = FlashMotion.Create(FlashMotionStyle.Pendulum, 300, y, mw, mh,
                0, 0, bw, bh, MotionLevel.Full, rng, taken);
            up.Add(s);
            taken.Add(new PendulumNeighbour(s.PivotX, s.Phase));
        }
        return up;
    }

    /// <summary>The picture's box hanging straight down, which is where the swing spends most of its time.</summary>
    private static (double X, double Y, double W, double H) AtRest(FlashMotionState s)
        => FlashMotion.HangingBounds(s.PivotX, s.PivotY, s.Rope, 0, s.MediaW, s.MediaH);

    private static double OverlapArea(FlashMotionState a, FlashMotionState b)
    {
        var (ax, ay, aw, ah) = AtRest(a);
        var (bx, by, bw, bh) = AtRest(b);
        var w = Math.Min(ax + aw, bx + bw) - Math.Max(ax, bx);
        var h = Math.Min(ay + ah, by + bh) - Math.Max(ay, by);
        return w <= 0 || h <= 0 ? 0 : w * h;
    }

    // =====================================================================================
    //  the burst
    // =====================================================================================

    [Theory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void PendulumsInOneBurstDoNotHangOnTopOfEachOther(int count)
    {
        for (int seed = 0; seed < 40; seed++)
        {
            var up = Burst(count, seed);
            for (int i = 0; i < up.Count; i++)
                for (int j = i + 1; j < up.Count; j++)
                {
                    // A tenth, not nothing: each pendulum's band is decided by its OWN rope, so
                    // four of them cannot always cut the screen into four exact slices. The
                    // number that matters is the one this replaced - before the rig they hung
                    // from one nail and covered each other completely.
                    var overlap = OverlapArea(up[i], up[j]);
                    var area = MW * MH;
                    Assert.True(overlap <= area * 0.10,
                        $"seed {seed}, {count} up: pendulums {i} and {j} cover "
                        + $"{overlap / area:P0} of each other at rest");
                }
        }
    }

    [Theory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(6)]
    public void NoTwoPendulumsInOneBurstShareAPivot(int count)
    {
        for (int seed = 0; seed < 40; seed++)
        {
            var pivots = Burst(count, seed).Select(s => s.PivotX).ToList();
            for (int i = 0; i < pivots.Count; i++)
                for (int j = i + 1; j < pivots.Count; j++)
                    Assert.True(Math.Abs(pivots[i] - pivots[j]) > 1e-6,
                        $"seed {seed}: pendulums {i} and {j} hang from the same nail ({pivots[i]})");
        }
    }

    [Fact]
    public void ABurstFansOutAcrossTheMonitorRatherThanClusteringInTheMiddle()
    {
        // Two flashes should be at opposite ends of whatever band the rope leaves, not merely a
        // few pixels apart - "distinct" is not the same thing as "readably apart".
        for (int seed = 0; seed < 40; seed++)
        {
            var up = Burst(2, seed);
            Assert.True(Math.Abs(up[0].PivotX - up[1].PivotX) >= MW,
                $"seed {seed}: two pendulums hang {Math.Abs(up[0].PivotX - up[1].PivotX):0}px apart, "
                + $"narrower than the {MW}px picture");
        }
    }

    [Theory]
    [InlineData(2)] [InlineData(3)] [InlineData(5)]
    public void EveryPendulumInABurstStillSwingsInsideItsMonitor(int count)
    {
        for (int seed = 0; seed < 25; seed++)
        {
            foreach (var s in Burst(count, seed))
            {
                for (int step = 0; step < 6 * 100; step++)
                {
                    FlashMotion.Step(s, 0.01);
                    Assert.True(s.X >= -0.5, $"seed {seed}: left {s.X}");
                    Assert.True(s.Y >= -0.5, $"seed {seed}: top {s.Y}");
                    Assert.True(s.X + s.W <= BW + 0.5, $"seed {seed}: right {s.X + s.W}");
                    Assert.True(s.Y + s.H <= BH + 0.5, $"seed {seed}: bottom {s.Y + s.H}");
                }
            }
        }
    }

    // =====================================================================================
    //  the lone flash, and the picture too wide to move
    // =====================================================================================

    [Fact]
    public void AloneOnAMonitorAPendulumStillTakesTheTopCentre()
    {
        // The ordinary case is one flash at a time, and it must look exactly as it did before the
        // rig existed: dead centre of the top edge.
        var s = FlashMotion.Create(FlashMotionStyle.Pendulum, 2100, 300, 400, 300, 1920, 0, 2560, 1440,
            MotionLevel.Full, new Random(1));
        Assert.Equal(1920 + 1280, s.PivotX);
        Assert.Equal(0, s.PivotY);
    }

    [Fact]
    public void APictureTooWideToMoveKeepsTheCentreAndTakesTheOppositePhaseInstead()
    {
        // A flash at the default ImageScale is 40% of the monitor's width; at nearly the whole
        // width there is no band left to move along. The pivot stays put - anywhere else would
        // swing off screen - and the swing is separated in TIME instead.
        var rng = new Random(3);
        var first = FlashMotion.Create(FlashMotionStyle.Pendulum, 60, 400, 1800, 500,
            0, 0, BW, BH, MotionLevel.Full, rng);
        var taken = new[] { new PendulumNeighbour(first.PivotX, first.Phase) };
        var second = FlashMotion.Create(FlashMotionStyle.Pendulum, 60, 600, 1800, 500,
            0, 0, BW, BH, MotionLevel.Full, rng, taken);

        Assert.Equal(first.PivotX, second.PivotX, 6);
        var apart = Math.Abs(FlashPendulumRig.Wrap(second.Phase - first.Phase) - Math.PI);
        Assert.True(apart < 1e-6, $"the two share a nail and start {apart} off anti-phase");
    }

    [Fact]
    public void TwoPendulumsFarEnoughApartSwingInStepSoTheGapHolds()
    {
        // The opposite rule, and the reason it is not one rule: with a real gap between the nails,
        // opposite phases walk the two pictures into each other every half period. In step, the
        // gap they started with is the gap they keep.
        var rng = new Random(11);
        var first = FlashMotion.Create(FlashMotionStyle.Pendulum, 200, 300, MW, MH,
            0, 0, BW, BH, MotionLevel.Full, rng);
        var taken = new[] { new PendulumNeighbour(first.PivotX, first.Phase) };
        var second = FlashMotion.Create(FlashMotionStyle.Pendulum, 200, 500, MW, MH,
            0, 0, BW, BH, MotionLevel.Full, rng, taken);

        Assert.True(Math.Abs(second.PivotX - first.PivotX) >= MW);
        Assert.Equal(FlashPendulumRig.Wrap(first.Phase), second.Phase, 6);
    }

    // =====================================================================================
    //  the rig's own rules
    // =====================================================================================

    [Fact]
    public void TheBandIsWhateverTheRopeAndTheSwingLeaveOver()
    {
        // A short rope on a narrow picture can hang almost anywhere; a long rope on a wide one
        // cannot move at all. Both are the same subtraction.
        var wide = FlashPendulumRig.PivotSpan(BW, 384, 216, rope: 300, ampRad: 15 * Math.PI / 180);
        Assert.True(wide > 500, $"a small picture on a short rope should roam, got {wide}");

        var none = FlashPendulumRig.PivotSpan(BW, 1600, 500, rope: 700, ampRad: 15 * Math.PI / 180);
        Assert.True(none < 0, $"a picture wider than the screen cannot move, got {none}");
    }

    [Fact]
    public void ThePivotIsAlwaysInsideTheBandItWasGiven()
    {
        var rng = new Random(5);
        var taken = new List<PendulumNeighbour> { new(900, 0), new(1100, 1) };
        for (int i = 0; i < 200; i++)
        {
            var pivot = FlashPendulumRig.ChoosePivot(960, 400, taken, rng);
            Assert.InRange(pivot, 560 - 1e-9, 1360 + 1e-9);
        }
    }

    [Fact]
    public void WithNoRoomOrNoNeighboursThePivotIsTheCentre()
    {
        var rng = new Random(5);
        Assert.Equal(960, FlashPendulumRig.ChoosePivot(960, 400, null, rng));
        Assert.Equal(960, FlashPendulumRig.ChoosePivot(960, 400, Array.Empty<PendulumNeighbour>(), rng));
        Assert.Equal(960, FlashPendulumRig.ChoosePivot(960, -20, new[] { new PendulumNeighbour(960, 0) }, rng));
    }

    // =====================================================================================
    //  motion level
    // =====================================================================================

    [Fact]
    public void MotionOffStillMeansStillEvenInACrowd()
    {
        var taken = new[] { new PendulumNeighbour(600, 0), new PendulumNeighbour(1300, 2) };
        var s = FlashMotion.Create(FlashMotionStyle.Pendulum, 123, 456, MW, MH,
            0, 0, BW, BH, MotionLevel.Off, new Random(1), taken);

        Assert.Equal(FlashMotionStyle.Still, s.Style);
        Assert.Equal(123, s.X);
        Assert.Equal(456, s.Y);
        Assert.False(FlashMotion.Step(s, 0.5));
    }

    [Fact]
    public void ReducedMotionHalvesTheSwingAndTheBurstStillFansOut()
    {
        // Half the amplitude means a shorter reach, which means a WIDER band - reduced motion must
        // not quietly put everything back on one nail.
        var full = Burst(3, seed: 9);
        var rng = new Random(9);
        var taken = new List<PendulumNeighbour>();
        var reduced = new List<FlashMotionState>();
        for (int i = 0; i < 3; i++)
        {
            var s = FlashMotion.Create(FlashMotionStyle.Pendulum, 300, 120 + i * 170, MW, MH,
                0, 0, BW, BH, MotionLevel.Reduced, rng, taken);
            reduced.Add(s);
            taken.Add(new PendulumNeighbour(s.PivotX, s.Phase));
        }

        Assert.True(reduced[0].AmpRad < full[0].AmpRad);
        Assert.Equal(3, reduced.Select(s => Math.Round(s.PivotX, 3)).Distinct().Count());
    }
}
