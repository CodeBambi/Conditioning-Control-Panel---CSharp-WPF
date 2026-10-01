using System;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Super;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Super Creep juice: the arrival, the edge pulse, the front's surges and breath, the wisps, the
/// motes and the eased retreat. Feel only: none of this may move the ramp, the cap, the click or
/// the opacity ceiling, and the panic stop stays instant.
/// </summary>
public class CreepJuiceTests
{
    private const double Dt = 1.0 / 60.0;

    private static void Run(CreepState s, double seconds, MotionLevel level = MotionLevel.Full, CreepField? f = null)
    {
        var n = (int)Math.Round(seconds / Dt);
        for (var i = 0; i < n; i++)
        {
            CreepFog.Step(s, Dt, CreepSpeed.Normal, CreepFog.MaxCap, level);
            if (f != null) CreepFog.StepField(s, f, Dt, level);
        }
    }

    [Fact]
    public void ArrivalStartsFromNothingAndOvershootsALittle()
    {
        var (a0, r0) = CreepFog.ArriveEnvelope(0, MotionLevel.Full);
        Assert.Equal(0, a0, 6);
        Assert.Equal(0, r0, 6);
        double peak = 0, prevAlpha = 0;
        for (var t = 0.0; t <= CreepFog.ArriveSeconds; t += 0.01)
        {
            var (a, r) = CreepFog.ArriveEnvelope(t, MotionLevel.Full);
            Assert.True(a >= prevAlpha - 1e-9, "the fade-in never dips");
            prevAlpha = a;
            peak = Math.Max(peak, r);
        }
        Assert.InRange(peak, 1.03, 1.08);   // house overshoot, 3-8%
        Assert.Equal((1.0, 1.0), CreepFog.ArriveEnvelope(CreepFog.ArriveSeconds, MotionLevel.Full));
    }

    [Fact]
    public void ReducedArrivesHalfAsFarAndOffJustFades()
    {
        var (_, rr0) = CreepFog.ArriveEnvelope(0, MotionLevel.Reduced);
        Assert.Equal(0.5, rr0, 6);   // half the travel
        double peak = 0;
        for (var t = 0.0; t <= CreepFog.ArriveSeconds * 2; t += 0.01)
            peak = Math.Max(peak, CreepFog.ArriveEnvelope(t, MotionLevel.Reduced).reach);
        Assert.InRange(peak - 1, 0.015, 0.04);   // half the overshoot

        Assert.Equal(1, CreepFog.ArriveEnvelope(0, MotionLevel.Off).reach, 6);
        Assert.Equal(0.5, CreepFog.ArriveEnvelope(0.06, MotionLevel.Off).alpha, 6);
        Assert.Equal(1, CreepFog.ArriveEnvelope(0.12, MotionLevel.Off).alpha, 6);
    }

    [Fact]
    public void TheEdgePulseIsOneSoftBeat()
    {
        Assert.Equal(0, CreepFog.VignetteAt(0, MotionLevel.Full), 6);
        Assert.Equal(CreepFog.VignettePeak, CreepFog.VignetteAt(CreepFog.VignettePeakAt, MotionLevel.Full), 6);
        Assert.Equal(0, CreepFog.VignetteAt(CreepFog.VignetteSeconds, MotionLevel.Full), 6);
        // one rise and one fall: never flickers (photosafe)
        int turns = 0; double prev = 0, dir = 1;
        for (var t = 0.0; t < 2; t += 0.005)
        {
            var v = CreepFog.VignetteAt(t, MotionLevel.Full);
            var d = Math.Sign(v - prev);
            if (d != 0 && d != dir) { turns++; dir = d; }
            prev = v;
        }
        Assert.Equal(1, turns);
        Assert.Equal(CreepFog.VignettePeak / 2, CreepFog.VignetteAt(CreepFog.VignettePeakAt, MotionLevel.Reduced), 6);
        Assert.Equal(0, CreepFog.VignetteAt(CreepFog.VignettePeakAt, MotionLevel.Off), 6);
        Assert.True(CreepFog.VignettePeak < CreepFog.OpacityCeiling);
    }

    [Fact]
    public void AShowSlidesTheFrontInFromTheEdges()
    {
        var s = new CreepState { Coverage = 0.6, Target = 0.6 };
        var f = new CreepField(0, 0, 1920, 1080);
        CreepFog.Arrive(s);
        CreepFog.Step(s, Dt, CreepSpeed.Normal, 0.6, MotionLevel.Full);
        CreepFog.StepField(s, f, Dt, MotionLevel.Full);
        var early = f.Blobs.Average(b => EdgeDist(b, f));
        Assert.True(s.AlphaMul < 0.2 && s.ReachMul < 0.1);
        Run(s, 2.5, f: f);
        var late = f.Blobs.Average(b => EdgeDist(b, f));
        Assert.True(late > early + 100, $"early {early:F0} late {late:F0}");
        Assert.Equal(1, s.AlphaMul, 6);
        Assert.Equal(1, s.ReachMul, 6);
        Assert.False(CreepFog.InTransition(s));
    }

    [Fact]
    public void TheRetreatGathersThenRecedesAndEnds()
    {
        var (a0, r0) = CreepFog.RetreatEnvelope(0, MotionLevel.Full);
        Assert.Equal((1.0, 1.0), (a0, r0));
        Assert.True(CreepFog.RetreatEnvelope(CreepFog.RetreatSeconds * 0.1, MotionLevel.Full).reach > 1.02);   // gather
        var (aEnd, rEnd) = CreepFog.RetreatEnvelope(CreepFog.RetreatSeconds, MotionLevel.Full);
        Assert.Equal(0, aEnd, 6);
        Assert.InRange(rEnd, -1e-9, 0.05);   // back on the edges

        var s = new CreepState { Coverage = 0.5, Target = 0.5 };
        CreepFog.BeginRetreat(s);
        CreepFog.BeginRetreat(s);   // idempotent
        Run(s, CreepFog.RetreatSeconds * 0.5);
        Assert.False(CreepFog.RetreatDone(s, MotionLevel.Full));
        Run(s, CreepFog.RetreatSeconds * 0.6);
        Assert.True(CreepFog.RetreatDone(s, MotionLevel.Full));
        Assert.True(s.AlphaMul < 0.01);
    }

    [Fact]
    public void RetreatFollowsMotion()
    {
        Assert.Equal(CreepFog.RetreatSeconds * 2, CreepFog.RetreatDuration(MotionLevel.Reduced), 6);
        Assert.Equal(0.5, CreepFog.RetreatEnvelope(CreepFog.RetreatSeconds * 2, MotionLevel.Reduced).reach, 6);
        Assert.Equal(CreepFog.OffFadeSeconds, CreepFog.RetreatDuration(MotionLevel.Off), 6);
        var off = CreepFog.RetreatEnvelope(0.06, MotionLevel.Off);
        Assert.Equal(1, off.reach, 6);         // Off never travels, it only fades
        Assert.Equal(0.5, off.alpha, 6);
    }

    [Fact]
    public void ComingBackMidRetreatDoesNotPop()
    {
        var s = new CreepState { Coverage = 0.5, Target = 0.5 };
        CreepFog.BeginRetreat(s);
        Run(s, CreepFog.RetreatSeconds * 0.6);
        var before = s.AlphaMul;
        Assert.InRange(before, 0.1, 0.9);
        CreepFog.CancelRetreat(s, MotionLevel.Full);
        CreepFog.Step(s, Dt, CreepSpeed.Normal, 0.9, MotionLevel.Full);
        Assert.False(s.Retreating);
        Assert.InRange(s.AlphaMul, before - 0.05, before + 0.1);
        Assert.Equal(0, s.Vignette, 6);   // the pulse is for a real arrival only
        Run(s, 2);
        Assert.Equal(1, s.AlphaMul, 6);
    }

    [Fact]
    public void TheFrontSurgesAndBreathesWithinItsBounds()
    {
        double min = 1, max = -1;
        for (var t = 0.0; t < CreepFog.SurgePeriod * 3; t += 0.05)
        {
            var v = CreepFog.FrontSwell(t, 1, 0.4, 1);
            min = Math.Min(min, v); max = Math.Max(max, v);
        }
        Assert.True(max > CreepFog.SurgeAmp * 0.8, $"max {max}");
        Assert.True(max <= CreepFog.SurgeAmp + CreepFog.FrontBreathAmp + 1e-9);
        Assert.True(min >= -CreepFog.FrontBreathAmp - 1e-9);
        // sides surge on their own beat
        Assert.NotEqual(CreepFog.FrontSwell(2, 0, 0.5, 1), CreepFog.FrontSwell(2, 2, 0.5, 1), 3);
        Assert.Equal(0, CreepFog.FrontSwell(5, 0, 0.5, 0), 9);
        Assert.Equal(CreepFog.FrontSwell(5, 0, 0.5, 1) / 2, CreepFog.FrontSwell(5, 0, 0.5, 0.5), 9);
    }

    [Fact]
    public void WispsReachInFromTheirEdgesAndSway()
    {
        var s = new CreepState { Coverage = 0.6, Target = 0.6 };
        var f = new CreepField(0, 0, 1920, 1080);
        Assert.Equal(CreepFog.WispCount, f.Wisps.Length);
        Assert.True(f.Wisps.Select(w => w.Side).Distinct().Count() == 4);
        CreepFog.StepField(s, f, 0, MotionLevel.Full);
        var angles = f.Wisps.Select(w => w.Angle).ToArray();
        foreach (var w in f.Wisps)
        {
            Assert.True(w.Length > 0 && w.Width > 0);
            Assert.True(w.Length > w.Width * 2, "a wisp is long and thin");
        }
        Run(s, 3, f: f);
        Assert.Contains(f.Wisps, w => Math.Abs(w.Angle - angles[Array.IndexOf(f.Wisps, w)]) > 0.02);

        var still = new CreepState { Coverage = 0.6, Target = 0.6 };
        var g = new CreepField(0, 0, 1920, 1080);
        CreepFog.StepField(still, g, 0, MotionLevel.Off);
        var p0 = g.Wisps.Select(w => (w.Width, w.Angle)).ToArray();   // the ramp still moves the reach
        Run(still, 3, MotionLevel.Off, g);
        for (var i = 0; i < g.Wisps.Length; i++)
        {
            Assert.Equal(p0[i].Angle, g.Wisps[i].Angle, 6);
            Assert.Equal(p0[i].Width, g.Wisps[i].Width, 6);
        }
    }

    [Fact]
    public void MotesFadeInAndOutAndNeverFlicker()
    {
        Assert.Equal(0, CreepFog.MoteAlpha(0, CreepFog.MoteLife, 0.3, false), 6);
        Assert.Equal(0, CreepFog.MoteAlpha(CreepFog.MoteLife, CreepFog.MoteLife, 0.3, false), 6);
        Assert.True(CreepFog.MoteAlpha(CreepFog.MoteLife / 2, CreepFog.MoteLife, 0.3, false) > 0.3);
        Assert.True(CreepFog.MoteTwinkle / (2 * Math.PI) < 3);   // photosafe
        Assert.Equal(0, CreepFog.MoteAlpha(0, CreepFog.BurstLife, 0, true), 6);   // 60 ms in, not a pop
        Assert.True(CreepFog.MoteAlpha(0.06, CreepFog.BurstLife, 0, true) > 0.6);
        Assert.True(CreepFog.MoteRadius(CreepFog.BurstLife, CreepFog.BurstLife, 0, true)
                    < CreepFog.MoteRadius(0, CreepFog.BurstLife, 0, true));
        Assert.Equal(10, CreepFog.BurstCount(MotionLevel.Full));
        Assert.Equal(5, CreepFog.BurstCount(MotionLevel.Reduced));
        Assert.Equal(0, CreepFog.BurstCount(MotionLevel.Off));
    }

    [Fact]
    public void TheRingShovesThenCoasts()
    {
        var half = CreepFog.RingAt(new CreepRing { Age = CreepFog.RingSeconds / 2 }, 1000);
        Assert.True(half.radius > 1000 * CreepFog.RingReach * 0.8);   // ease out: most of the way at half time
        Assert.True(half.alpha < 0.7 / 2);
    }

    [Fact]
    public void JuiceNeverMovesTheRampOrTheCeiling()
    {
        var plain = new CreepState();
        var shown = new CreepState();
        CreepFog.Arrive(shown);
        Run(plain, 30); Run(shown, 30);
        Assert.Equal(plain.Coverage, shown.Coverage, 9);
        Assert.Equal(plain.Target, shown.Target, 9);
        Assert.InRange(CreepFog.OpacityCeiling, 0.5, 0.85);
    }

    /// <summary>Panic and the engine stop stay instant: Stop hides the layer, it never retreats.</summary>
    [Fact]
    public void StopIsInstantAndOnlyTheSwitchRetreats()
    {
        var path = System.IO.Path.Combine(SourceRoot(), "Services/Super/CreepController.cs");
        var text = System.IO.File.ReadAllText(path);
        var stop = text.Substring(text.IndexOf("public static void Stop()", StringComparison.Ordinal));
        stop = stop.Substring(0, stop.IndexOf("\n    }", StringComparison.Ordinal));
        Assert.Contains("_layer.Hide()", stop);
        Assert.DoesNotContain("Retreat", stop);
    }

    private static string SourceRoot()
    {
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = System.IO.Path.Combine(dir.FullName, "ConditioningControlPanel", "ConditioningControlPanel.csproj");
            if (System.IO.File.Exists(candidate)) return System.IO.Path.GetDirectoryName(candidate)!;
            dir = dir.Parent;
        }
        throw new System.IO.DirectoryNotFoundException("ConditioningControlPanel project not found");
    }

    private static double EdgeDist(CreepBlob b, CreepField f) => b.Side switch
    {
        0 => b.Py - f.Y,
        1 => f.X + f.W - b.Px,
        2 => f.Y + f.H - b.Py,
        _ => b.Px - f.X,
    };
}
