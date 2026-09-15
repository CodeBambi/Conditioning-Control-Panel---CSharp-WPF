using System;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Flash;
using Newtonsoft.Json;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Flashes v2 wave 2, Shatter: the pure maths behind a dismissed flash breaking into falling
/// pieces. The compositor only hands this delta time and reads the shard transforms back, so
/// everything deciding how the break LOOKS is in here - how many pieces each motion level makes,
/// that the cut still covers the whole picture, that the pieces fall rather than float, when they
/// fade, and when the layer is allowed to drop the item.
///
/// Geometry is a 400x300 flash sitting in the middle of a 1920x1080 monitor unless a test says so.
/// </summary>
public class FlashShatterTests
{
    private const double BW = 1920, BH = 1080;
    private const double RX = 760, RY = 390, RW = 400, RH = 300;

    private static FlashShatterState Break(MotionLevel level = MotionLevel.Full, int seed = 7)
        => FlashShatter.Create(RX, RY, RW, RH, 0, 0, BW, BH, level, new Random(seed));

    /// <summary>Run the break to its end at 60 fps, with a cap so a bug cannot hang the suite.</summary>
    private static int RunToDone(FlashShatterState s, double dt = 1.0 / 60.0, int maxTicks = 6000)
    {
        var ticks = 0;
        while (!s.Done && ticks < maxTicks) { FlashShatter.Step(s, dt); ticks++; }
        return ticks;
    }

    // ---------------------------------------------------------------- how many pieces

    [Fact]
    public void FullMotionBreaksIntoSixToNinePieces()
    {
        for (var seed = 0; seed < 50; seed++)
        {
            var s = Break(MotionLevel.Full, seed);
            Assert.InRange(s.Shards.Length, 6, 9);
            // 3 columns against 2 or 3 rows: nothing in between is reachable.
            Assert.True(s.Shards.Length == 6 || s.Shards.Length == 9);
        }
    }

    [Fact]
    public void BothGridsActuallyGetRolled()
    {
        var counts = Enumerable.Range(0, 60).Select(seed => Break(MotionLevel.Full, seed).Shards.Length)
            .Distinct().OrderBy(n => n).ToArray();
        Assert.Equal(new[] { 6, 9 }, counts);
    }

    [Fact]
    public void ReducedMotionBreaksIntoFour()
    {
        for (var seed = 0; seed < 20; seed++)
            Assert.Equal(4, Break(MotionLevel.Reduced, seed).Shards.Length);
    }

    [Fact]
    public void MotionOffReturnsNoShardsAndIsDoneOnArrival()
    {
        var s = Break(MotionLevel.Off);
        Assert.Empty(s.Shards);
        Assert.True(s.Done);
        // And it never starts moving, so the renderer can only ever take the plain cut.
        Assert.False(FlashShatter.Step(s, 0.1));
        Assert.Empty(s.Shards);
    }

    [Fact]
    public void ShardCountAgreesWithWhatCreateProduces()
    {
        Assert.Equal(9, FlashShatter.ShardCount(MotionLevel.Full, 3));
        Assert.Equal(6, FlashShatter.ShardCount(MotionLevel.Full, 2));
        Assert.Equal(4, FlashShatter.ShardCount(MotionLevel.Reduced, 3));
        Assert.Equal(0, FlashShatter.ShardCount(MotionLevel.Off, 3));
    }

    [Fact]
    public void ADegenerateRectNeverBreaks()
    {
        var s = FlashShatter.Create(0, 0, 0, 0, 0, 0, BW, BH, MotionLevel.Full, new Random(1));
        Assert.Empty(s.Shards);
        Assert.True(s.Done);
    }

    // ---------------------------------------------------------------- the cut

    [Fact]
    public void TheShardsTileTheWholePictureWithNoGapAndNoOverlap()
    {
        for (var seed = 0; seed < 30; seed++)
        {
            var s = Break(MotionLevel.Full, seed);
            // Every piece is a real rect inside the unit box...
            foreach (var shard in s.Shards)
            {
                Assert.True(shard.U1 > shard.U0);
                Assert.True(shard.V1 > shard.V0);
                Assert.InRange(shard.U0, 0.0, 1.0);
                Assert.InRange(shard.V1, 0.0, 1.0);
            }
            // ...and together they cover it exactly once.
            var area = s.Shards.Sum(sh => (sh.U1 - sh.U0) * (sh.V1 - sh.V0));
            Assert.Equal(1.0, area, 9);
        }
    }

    [Fact]
    public void TheCutLinesAreJitteredSoTwoBreaksDiffer()
    {
        var a = Break(MotionLevel.Full, 3);
        var b = Break(MotionLevel.Full, 4);
        var same = a.Shards.Length == b.Shards.Length
                   && a.Shards.Zip(b.Shards).All(p => Math.Abs(p.First.U1 - p.Second.U1) < 1e-9);
        Assert.False(same);
    }

    [Fact]
    public void TheOutsideEdgesStayPut()
    {
        var s = Break(MotionLevel.Full, 11);
        Assert.Equal(0.0, s.Shards.Min(sh => sh.U0), 9);
        Assert.Equal(0.0, s.Shards.Min(sh => sh.V0), 9);
        Assert.Equal(1.0, s.Shards.Max(sh => sh.U1), 9);
        Assert.Equal(1.0, s.Shards.Max(sh => sh.V1), 9);
    }

    // ---------------------------------------------------------------- gravity

    [Fact]
    public void GravityPullsEveryShardDownAndKeepsAccelerating()
    {
        var s = Break();
        var startVy = s.Shards.Select(sh => sh.Vy).ToArray();

        FlashShatter.Step(s, 0.1);
        for (var i = 0; i < s.Shards.Length; i++)
        {
            Assert.Equal(startVy[i] + FlashShatter.GravityPxPerSec2 * 0.1, s.Shards[i].Vy, 6);
            Assert.True(s.Shards[i].Dy > 0, "a tenth of a second in, every piece has already dropped");
        }

        // A second tick is faster than the first: that is the fall, not a slide.
        var beforeDy = s.Shards.Select(sh => sh.Dy).ToArray();
        FlashShatter.Step(s, 0.1);
        for (var i = 0; i < s.Shards.Length; i++)
            Assert.True(s.Shards[i].Dy - beforeDy[i] > beforeDy[i]);
    }

    [Fact]
    public void ThePiecesOpenOutwardsBeforeTheyFall()
    {
        // The left column goes left and the right column goes right on the very first tick,
        // before gravity has had time to drown the sideways kick.
        var s = Break(MotionLevel.Full, 21);
        FlashShatter.Step(s, 1.0 / 60.0);
        foreach (var shard in s.Shards)
        {
            var mid = (shard.U0 + shard.U1) / 2.0;
            if (mid < 0.45) Assert.True(shard.Dx < 0);
            if (mid > 0.55) Assert.True(shard.Dx > 0);
        }
    }

    [Fact]
    public void EachPieceTumblesALittleAndTheyDoNotAllTurnTheSameWay()
    {
        var s = Break(MotionLevel.Full, 5);
        FlashShatter.Step(s, 0.2);
        Assert.All(s.Shards, sh => Assert.InRange(Math.Abs(sh.AngularVelRad), 0.0, FlashShatter.SpinMaxRadPerSec));
        Assert.Contains(s.Shards, sh => sh.AngularVelRad > 0);
        Assert.Contains(s.Shards, sh => sh.AngularVelRad < 0);
        Assert.All(s.Shards, sh => Assert.Equal(sh.AngularVelRad * 0.2, sh.AngleRad, 6));
    }

    [Fact]
    public void AZeroDeltaNeverMovesAnything()
    {
        var s = Break();
        Assert.False(FlashShatter.Step(s, 0));
        Assert.All(s.Shards, sh => Assert.Equal(0.0, sh.Dy, 9));
    }

    // ---------------------------------------------------------------- the fade

    [Fact]
    public void TheShardsHoldFullOpacityThenFadeStraightToNothing()
    {
        var d = FlashShatter.DurationSec;
        Assert.Equal(1.0, FlashShatter.AlphaAt(0, d), 9);
        Assert.Equal(1.0, FlashShatter.AlphaAt(d * FlashShatter.HoldFraction, d), 9);
        Assert.Equal(0.5, FlashShatter.AlphaAt(d * (FlashShatter.HoldFraction + (1 - FlashShatter.HoldFraction) / 2), d), 9);
        Assert.Equal(0.0, FlashShatter.AlphaAt(d, d), 9);
        Assert.Equal(0.0, FlashShatter.AlphaAt(d * 2, d), 9);
    }

    [Fact]
    public void TheFadeIsMonotonicAndReachesTheShards()
    {
        var s = Break();
        var last = 1.0;
        for (var i = 0; i < 60; i++)
        {
            FlashShatter.Step(s, FlashShatter.DurationSec / 60.0);
            var alpha = s.Shards[0].Alpha;
            Assert.True(alpha <= last + 1e-9, "the fade never brightens");
            Assert.All(s.Shards, sh => Assert.Equal(alpha, sh.Alpha, 9));
            last = alpha;
        }
        Assert.Equal(0.0, last, 9);
    }

    // ---------------------------------------------------------------- done

    [Fact]
    public void AFullBreakLastsAboutSevenHundredMilliseconds()
    {
        Assert.Equal(0.70, Break(MotionLevel.Full).DurationSec, 9);
        var ticks = RunToDone(Break());
        Assert.InRange(ticks / 60.0, 0.5, 0.75);
    }

    [Fact]
    public void ReducedMotionHalvesTheDuration()
    {
        Assert.Equal(FlashShatter.DurationSec / 2, Break(MotionLevel.Reduced).DurationSec, 9);
        var full = RunToDone(Break(MotionLevel.Full, 2));
        var reduced = RunToDone(Break(MotionLevel.Reduced, 2));
        Assert.True(reduced < full);
    }

    [Fact]
    public void ABreakIsDoneOnceTheFadeRunsOut()
    {
        var s = Break();
        Assert.False(s.Done);
        RunToDone(s);
        Assert.True(s.Done);
        Assert.All(s.Shards, sh => Assert.Equal(0.0, sh.Alpha, 9));
    }

    [Fact]
    public void ADoneBreakNeverMovesAgain()
    {
        var s = Break();
        RunToDone(s);
        var dy = s.Shards[0].Dy;
        Assert.False(FlashShatter.Step(s, 1.0));
        Assert.Equal(dy, s.Shards[0].Dy, 9);
    }

    [Fact]
    public void ABreakAtTheBottomEdgeIsDoneOnceEveryPieceHasLeftTheScreen()
    {
        // Sitting on the last 20 px of the monitor: the shards clear it long before the fade ends.
        var s = FlashShatter.Create(760, BH - 20, RW, 20, 0, 0, BW, BH, MotionLevel.Full, new Random(9));
        var ticks = RunToDone(s);
        Assert.True(s.Done);
        Assert.True(ticks / 60.0 < FlashShatter.DurationSec,
            "off the bottom of the monitor ends the break early");
        Assert.All(s.Shards, sh => Assert.True(FlashShatter.OffScreen(s, sh)));
    }

    [Fact]
    public void OffScreenMeansTheWholePieceHasLeftTheMonitor()
    {
        var s = Break();
        var shard = s.Shards[0];
        Assert.False(FlashShatter.OffScreen(s, shard));
        shard.Dy = BH;                 // straight down, past the bottom
        Assert.True(FlashShatter.OffScreen(s, shard));
        shard.Dy = 0;
        shard.Dx = BW;                 // and off the right-hand side
        Assert.True(FlashShatter.OffScreen(s, shard));
    }

    [Fact]
    public void NoBoundsMeansNothingCanBeOffScreen()
    {
        var s = FlashShatter.Create(RX, RY, RW, RH, 0, 0, 0, 0, MotionLevel.Full, new Random(1));
        s.Shards[0].Dy = 1e6;
        Assert.False(FlashShatter.OffScreen(s, s.Shards[0]));
        // With no monitor to leave, only the fade can end it, so it still ends.
        RunToDone(s);
        Assert.True(s.Done);
    }

    // ---------------------------------------------------------------- the drawn rect

    [Fact]
    public void APlainFlashBreaksOverItsOwnRectWithNoTilt()
    {
        var s = Break();
        FlashShatter.TakeOverDrawnRect(s, 100, 200, 300, 400);
        Assert.Equal(100, s.RectX, 9);
        Assert.Equal(200, s.RectY, 9);
        Assert.Equal(300, s.RectW, 9);
        Assert.Equal(400, s.RectH, 9);
        Assert.Equal(0.0, s.FrozenAngleRad, 9);
    }

    [Fact]
    public void APendulumBreaksOverTheMediaItDrawsNotTheBoxItOccupies()
    {
        // The box a swinging 400x300 picture occupies is the bounds of the ROTATED media, which
        // is wider and taller than the picture itself. Cutting from that would make the flash
        // jump bigger the instant it broke, so the break takes the pivot-space media rect.
        const double pivotX = 960, pivotY = 0, rope = 500, angle = 0.28;
        var aabb = FlashMotion.HangingBounds(pivotX, pivotY, rope, angle, RW, RH);
        Assert.True(aabb.W > RW && aabb.H > RH, "the swinging box really is bigger than the picture");

        var s = Break();
        FlashShatter.TakeOverHangingRect(s, pivotX, pivotY, rope, angle, RW, RH);
        Assert.Equal(RW, s.RectW, 9);
        Assert.Equal(RH, s.RectH, 9);
        Assert.Equal(pivotX - RW / 2, s.RectX, 9);
        Assert.Equal(pivotY + rope - RH / 2, s.RectY, 9);
    }

    [Fact]
    public void ThePendulumTiltIsFrozenAtTheBreakAndTheSwingIsOver()
    {
        var motion = FlashMotion.Create(FlashMotionStyle.Pendulum, RX, RY, RW, RH,
            0, 0, BW, BH, MotionLevel.Full, new Random(4));
        FlashMotion.Step(motion, 0.4);           // let it swing somewhere off straight down
        var angleAtTheBreak = motion.AngleRad;
        Assert.NotEqual(0.0, angleAtTheBreak, 3);

        var s = Break();
        FlashShatter.TakeOverHangingRect(s, motion.PivotX, motion.PivotY, motion.Rope,
            motion.AngleRad, motion.MediaW, motion.MediaH);
        Assert.Equal(angleAtTheBreak, s.FrozenAngleRad, 9);
        Assert.Equal(motion.PivotX, s.PivotX, 9);
        Assert.Equal(motion.PivotY, s.PivotY, 9);

        // The break runs its whole course and the tilt never moves: the shards fall away along
        // the axis the picture had, and nothing snaps level under the cursor.
        RunToDone(s);
        Assert.Equal(angleAtTheBreak, s.FrozenAngleRad, 9);
    }

    [Fact]
    public void TakingOverADrawnRectClearsAPreviousTilt()
    {
        var s = Break();
        FlashShatter.TakeOverHangingRect(s, 960, 0, 500, 0.3, RW, RH);
        FlashShatter.TakeOverDrawnRect(s, RX, RY, RW, RH);
        Assert.Equal(0.0, s.FrozenAngleRad, 9);
        Assert.Equal(0.0, s.PivotX, 9);
        Assert.Equal(0.0, s.PivotY, 9);
    }

    // ---------------------------------------------------------------- the setting

    [Fact]
    public void ShatterDefaultsOn()
    {
        Assert.True(new AppSettings().FlashShatterEnabled);
        var loaded = JsonConvert.DeserializeObject<AppSettings>("{}", new JsonSerializerSettings
        {
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            Error = (_, args) => { args.ErrorContext.Handled = true; }
        })!;
        Assert.True(loaded.FlashShatterEnabled);
        Assert.False(JsonConvert.DeserializeObject<AppSettings>(
            JsonConvert.SerializeObject(new AppSettings { FlashShatterEnabled = false }))!.FlashShatterEnabled);
    }
}
