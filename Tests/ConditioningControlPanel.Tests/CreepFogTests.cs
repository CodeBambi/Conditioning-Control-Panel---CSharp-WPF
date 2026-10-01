using System;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Super;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Super Creep: the pure maths behind the pink fog. The layer only hands this delta time and
/// clicks and draws what comes back, so the ramp, the cap, the click drop and the push all live
/// here. A 1920x1080 monitor at the desktop origin unless a test says so.
/// </summary>
public class CreepFogTests
{
    private const double Dt = 1.0 / 30.0;

    private static void Run(CreepState s, double seconds, CreepSpeed speed = CreepSpeed.Normal,
        double cap = CreepFog.MaxCap, MotionLevel level = MotionLevel.Full, CreepField? f = null)
    {
        var n = (int)Math.Round(seconds / Dt);
        for (var i = 0; i < n; i++)
        {
            CreepFog.Step(s, Dt, speed, cap, level);
            if (f != null) CreepFog.StepField(s, f, Dt, level);
        }
    }

    [Fact]
    public void StartsThin()
    {
        var s = new CreepState();
        Assert.Equal(CreepFog.StartCoverage, s.Coverage);
        Assert.Equal(CreepFog.StartCoverage, s.Target);
    }

    [Theory]
    [InlineData(CreepSpeed.Slow, 15)]
    [InlineData(CreepSpeed.Normal, 10)]
    [InlineData(CreepSpeed.Fast, 5)]
    public void ReachesTheCapAroundTheRampTime(CreepSpeed speed, double minutes)
    {
        var s = new CreepState();
        Run(s, minutes * 60 * 0.5, speed);
        Assert.InRange(s.Coverage, 0.4, 0.55);          // about half way at half time
        Run(s, minutes * 60 * 0.5 + 10, speed);
        Assert.InRange(s.Coverage, 0.88, CreepFog.MaxCap);
    }

    [Fact]
    public void NeverPassesTheCap()
    {
        var s = new CreepState();
        Run(s, 20 * 60, CreepSpeed.Fast);
        Assert.True(s.Target <= CreepFog.MaxCap + 1e-9);
        Assert.True(s.Coverage <= CreepFog.MaxCap + 1e-9);
    }

    [Fact]
    public void ThePlayersCapHoldsAndClamps()
    {
        var s = new CreepState();
        Run(s, 6 * 60, CreepSpeed.Fast, cap: 0.5);
        Assert.InRange(s.Coverage, 0.49, 0.5 + 1e-9);
        Assert.Equal(CreepFog.MaxCap, CreepFog.ClampCap(2));
        Assert.Equal(CreepFog.MinCap, CreepFog.ClampCap(0));
        Assert.Equal(CreepFog.MaxCap, CreepFog.ClampCap(double.NaN));
    }

    [Fact]
    public void LoweringTheCapPullsAThickFogBack()
    {
        var s = new CreepState { Coverage = 0.9, Target = 0.9 };
        Run(s, 2, cap: 0.3);
        Assert.InRange(s.Coverage, 0.29, 0.31);
    }

    [Fact]
    public void AClickDropsTheTargetByPointFourWithAFloor()
    {
        var s = new CreepState { Coverage = 0.9, Target = 0.9 };
        CreepFog.Click(s, Array.Empty<CreepField>(), 0, 0, MotionLevel.Full);
        Assert.Equal(0.5, s.Target, 6);
        CreepFog.Click(s, Array.Empty<CreepField>(), 0, 0, MotionLevel.Full);
        CreepFog.Click(s, Array.Empty<CreepField>(), 0, 0, MotionLevel.Full);
        Assert.Equal(CreepFog.Floor, s.Target, 6);
    }

    [Fact]
    public void FallsFastAndRisesSlow()
    {
        var s = new CreepState { Coverage = 0.9, Target = 0.9 };
        CreepFog.Click(s, Array.Empty<CreepField>(), 0, 0, MotionLevel.Full);
        Run(s, 0.5);
        var fell = 0.9 - s.Coverage;
        Assert.True(fell > 0.35, $"fell {fell}");     // rate 6: 95% of the drop in half a second

        var r = new CreepState { Coverage = 0.1, Target = 0.5 };
        CreepFog.Step(r, 0.5, CreepSpeed.Normal, 0.9, MotionLevel.Full);
        var rose = r.Coverage - 0.1;
        Assert.True(rose < fell, $"rose {rose} fell {fell}");
    }

    [Fact]
    public void MotionOffRecedesIn120Ms()
    {
        var s = new CreepState { Coverage = 0.9, Target = 0.9 };
        CreepFog.Click(s, Array.Empty<CreepField>(), 0, 0, MotionLevel.Off);
        CreepFog.Step(s, 0.06, CreepSpeed.Normal, 0.9, MotionLevel.Off);
        Assert.True(s.Coverage > 0.6 && s.Coverage < 0.8);   // half way at 60 ms
        CreepFog.Step(s, 0.07, CreepSpeed.Normal, 0.9, MotionLevel.Off);
        Assert.Equal(s.Target, s.Coverage, 6);               // landed by 130 ms
        Assert.Empty(s.Rings);                               // and no ring
    }

    [Fact]
    public void AClickPushesBlobsAwayAndTheyComeBack()
    {
        var s = new CreepState { Coverage = 0.5, Target = 0.5 };
        var f = new CreepField(0, 0, 1920, 1080);
        CreepFog.StepField(s, f, 0, MotionLevel.Full);
        var before = f.Blobs.Select(b => (b.Px, b.Py)).ToArray();
        const double cx = 960, cy = 540;

        CreepFog.Click(s, new[] { f }, cx, cy, MotionLevel.Full);
        Run(s, 0.3, f: f);
        for (var i = 0; i < f.Blobs.Length; i++)
        {
            var b = f.Blobs[i];
            var d0 = Math.Sqrt(Math.Pow(before[i].Px - cx, 2) + Math.Pow(before[i].Py - cy, 2));
            var dOff = Math.Sqrt(b.Ox * b.Ox + b.Oy * b.Oy);
            Assert.True(dOff > 5, $"blob {i} barely moved");
            // the push points away from the click
            var dot = (before[i].Px - cx) * b.Ox + (before[i].Py - cy) * b.Oy;
            Assert.True(dot > 0, $"blob {i} moved toward the click (d0 {d0})");
        }

        Run(s, 12, f: f);
        Assert.All(f.Blobs, b => Assert.True(Math.Abs(b.Ox) + Math.Abs(b.Oy) < 2));
    }

    [Fact]
    public void ThePushFallsOffWithDistance()
    {
        var s = new CreepState();
        var f = new CreepField(0, 0, 1920, 1080);
        CreepFog.StepField(s, f, 0, MotionLevel.Full);
        var near = f.Blobs.OrderBy(b => Dist(b, 0, 0)).First();
        var far = f.Blobs.OrderBy(b => Dist(b, 0, 0)).Last();
        CreepFog.Click(s, new[] { f }, 0, 0, MotionLevel.Full);
        Assert.True(Speed(near) > Speed(far));
        Assert.True(Speed(near) <= CreepFog.PushStrength + 1e-6);
    }

    [Fact]
    public void ReducedHalvesThePushAndOffSkipsIt()
    {
        static double PushAt(MotionLevel level)
        {
            var s = new CreepState();
            var f = new CreepField(0, 0, 1920, 1080);
            CreepFog.StepField(s, f, 0, MotionLevel.Full);
            CreepFog.Click(s, new[] { f }, 960, 540, level);
            return f.Blobs.Sum(Speed);
        }
        Assert.Equal(PushAt(MotionLevel.Full) / 2, PushAt(MotionLevel.Reduced), 6);
        Assert.Equal(0, PushAt(MotionLevel.Off), 6);
    }

    [Fact]
    public void BlobsSitOnTheirEdgesAndCreepInWithCoverage()
    {
        var thin = new CreepState { Coverage = 0.04 };
        var thick = new CreepState { Coverage = 0.9 };
        var a = new CreepField(0, 0, 1920, 1080);
        var b = new CreepField(0, 0, 1920, 1080);
        CreepFog.StepField(thin, a, 0, MotionLevel.Off);
        CreepFog.StepField(thick, b, 0, MotionLevel.Off);
        Assert.Equal(CreepFog.BlobCount, a.Blobs.Length);
        for (var i = 0; i < a.Blobs.Length; i++)
            Assert.True(EdgeDist(b.Blobs[i], b) > EdgeDist(a.Blobs[i], a));
        Assert.All(a.Blobs, x => Assert.True(EdgeDist(x, a) < 1080 * 0.05));
    }

    [Fact]
    public void FieldsUseWorldCoordinates()
    {
        var s = new CreepState();
        var left = new CreepField(0, 0, 1920, 1080);
        var right = new CreepField(1920, 0, 1920, 1080);
        CreepFog.StepField(s, left, 0, MotionLevel.Off);
        CreepFog.StepField(s, right, 0, MotionLevel.Off);
        for (var i = 0; i < left.Blobs.Length; i++)
            Assert.Equal(left.Blobs[i].Px + 1920, right.Blobs[i].Px, 6);
    }

    [Fact]
    public void MotionOffIsStill()
    {
        var s = new CreepState { Coverage = 0.5, Target = 0.5 };
        var f = new CreepField(0, 0, 1920, 1080);
        CreepFog.StepField(s, f, 0, MotionLevel.Off);
        var p0 = f.Blobs.Select(b => (b.Px, b.Py, b.DrawSize)).ToArray();
        Run(s, 3, cap: 0.5, level: MotionLevel.Off, f: f);
        for (var i = 0; i < f.Blobs.Length; i++)
        {
            Assert.Equal(p0[i].Px, f.Blobs[i].Px, 3);
            Assert.Equal(p0[i].DrawSize, f.Blobs[i].DrawSize, 3);
        }
    }

    [Fact]
    public void ReducedDriftsHalfAsFar()
    {
        static double MaxSwing(MotionLevel level)
        {
            var s = new CreepState { Coverage = 0.5, Target = 0.5 };
            var f = new CreepField(0, 0, 1920, 1080);
            CreepFog.StepField(s, f, 0, MotionLevel.Off);
            var home = f.Blobs.Select(b => b.Px).ToArray();
            double max = 0;
            for (var i = 0; i < 2000; i++)
            {
                CreepFog.Step(s, Dt, CreepSpeed.Normal, 0.5, level);
                CreepFog.StepField(s, f, Dt, level);
                for (var j = 0; j < f.Blobs.Length; j++) max = Math.Max(max, Math.Abs(f.Blobs[j].Px - home[j]));
            }
            return max;
        }
        var full = MaxSwing(MotionLevel.Full);
        var reduced = MaxSwing(MotionLevel.Reduced);
        Assert.InRange(reduced / full, 0.45, 0.55);
        Assert.True(full <= 1080 * CreepFog.DriftAmp * 2 + 1);
    }

    [Fact]
    public void RingsLastPointSevenSeconds()
    {
        var s = new CreepState();
        CreepFog.Click(s, Array.Empty<CreepField>(), 10, 10, MotionLevel.Full);
        Assert.Single(s.Rings);
        var (r0, a0) = CreepFog.RingAt(s.Rings[0], 1080);
        Assert.Equal(0, r0);
        Assert.Equal(0.7, a0, 6);
        Run(s, 0.6);
        Assert.Single(s.Rings);
        Run(s, 0.2);
        Assert.Empty(s.Rings);
    }

    [Fact]
    public void ResetStartsThinAndStill()
    {
        var s = new CreepState { Coverage = 0.8, Target = 0.8 };
        var f = new CreepField(0, 0, 1920, 1080);
        CreepFog.Click(s, new[] { f }, 5, 5, MotionLevel.Full);
        CreepFog.Reset(s, new[] { f });
        Assert.Equal(CreepFog.StartCoverage, s.Coverage);
        Assert.Empty(s.Rings);
        Assert.All(f.Blobs, b => Assert.Equal(0, b.Vx));
    }

    [Fact]
    public void AStalledTickDoesNotJumpTheFog()
    {
        var s = new CreepState();
        CreepFog.Step(s, 60, CreepSpeed.Fast, 0.9, MotionLevel.Full);
        Assert.True(s.Coverage < 0.06);
    }

    [Fact]
    public void AlphaFollowsTheMockup()
    {
        Assert.Equal(0.3, CreepFog.Alpha(0), 6);
        Assert.Equal(0.975, CreepFog.Alpha(0.9), 6);
        Assert.Equal(1, CreepFog.Alpha(5), 6);
    }

    /// <summary>Per-pixel fog opacity the way the layer draws it: blobs stacked source-over in the
    /// fog surface at <paramref name="blobAlpha"/>, the surface laid down at <paramref name="surfaceAlpha"/>.</summary>
    private static (double max, double mid) Composite(CreepField f, double blobAlpha, double surfaceAlpha)
    {
        double max = 0, mid = 0;
        const int n = 48;
        for (var gx = 0; gx < n; gx++)
        for (var gy = 0; gy < n; gy++)
        {
            var px = f.X + (gx + 0.5) / n * f.W;
            var py = f.Y + (gy + 0.5) / n * f.H;
            var keep = 1.0;
            foreach (var b in f.Blobs)
                keep *= 1 - CreepFog.SpriteAlpha(Dist(b, px, py) / (b.DrawSize / 2)) * blobAlpha;
            var a = (1 - keep) * surfaceAlpha;
            max = Math.Max(max, a);
            if (gx == n / 2 && gy == n / 2) mid = a;
        }
        return (max, mid);
    }

    private static CreepField FogAt(double coverage)
    {
        var s = new CreepState { Coverage = coverage, Target = coverage };
        var f = new CreepField(0, 0, 1920, 1080);
        CreepFog.StepField(s, f, 0, MotionLevel.Off);
        return f;
    }

    [Fact]
    public void NoPixelOfFogPassesTheCeiling()
    {
        var f = FogAt(CreepFog.MaxCap);
        // Without the ceiling the stacked blobs went near opaque: that is the bug this guards.
        var (rawMax, _) = Composite(f, CreepFog.Alpha(CreepFog.MaxCap), 1);
        Assert.True(rawMax > 0.95, $"raw stack {rawMax:F3}");
        var (max, mid) = Composite(f, CreepFog.LayerAlpha(CreepFog.MaxCap), CreepFog.OpacityCeiling);
        Assert.True(max <= CreepFog.OpacityCeiling + 1e-9, $"fog {max:F3}");
        Assert.True(mid <= CreepFog.OpacityCeiling + 1e-9);
        Assert.InRange(CreepFog.OpacityCeiling, 0.5, 0.85);
    }

    [Theory]
    [InlineData(CreepFog.StartCoverage)]
    [InlineData(0.1)]
    public void AThinFogLooksAsItDidInTheMockup(double coverage)
    {
        // The lift by 1 / ceiling cancels the ceiling for one blob: a lone thin blob is unchanged.
        Assert.Equal(CreepFog.Alpha(coverage), CreepFog.LayerAlpha(coverage) * CreepFog.OpacityCeiling, 6);
    }

    [Fact]
    public void TheFogSurfaceIsAQuarterSize()
    {
        Assert.Equal(480, CreepFog.SurfaceSize(1920));
        Assert.Equal(271, CreepFog.SurfaceSize(1081));
        Assert.Equal(1, CreepFog.SurfaceSize(0));
    }

    [Fact]
    public void TheSpriteFollowsTheMockupStops()
    {
        Assert.Equal(0.55, CreepFog.SpriteAlpha(0), 6);
        Assert.Equal(0.22, CreepFog.SpriteAlpha(0.5), 6);
        Assert.Equal(0, CreepFog.SpriteAlpha(1), 6);
        Assert.Equal(0, CreepFog.SpriteAlpha(double.NaN), 6);
    }

    private static double Dist(CreepBlob b, double x, double y) => Math.Sqrt((b.Px - x) * (b.Px - x) + (b.Py - y) * (b.Py - y));
    private static double Speed(CreepBlob b) => Math.Sqrt(b.Vx * b.Vx + b.Vy * b.Vy);

    private static double EdgeDist(CreepBlob b, CreepField f) => b.Side switch
    {
        0 => b.Py - f.Y,
        1 => f.X + f.W - b.Px,
        2 => f.Y + f.H - b.Py,
        _ => b.Px - f.X,
    };
}
