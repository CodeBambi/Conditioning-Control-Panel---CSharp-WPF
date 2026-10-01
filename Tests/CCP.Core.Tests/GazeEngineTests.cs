using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The gaze half of the webcam pipeline on synthetic iris vectors: no camera, no frames.</summary>
public class GazeEngineTests
{
    // Linear map: x = 1000 + 2000*ix, y = 500 + 1000*iy (7-coeff form, higher terms zero).
    private static WebcamCalibrationData Linear() => new()
    {
        Polynomial = new PolynomialFitData
        {
            X = new double[] { 1000, 2000, 0, 0, 0, 0, 0 },
            Y = new double[] { 500, 0, 1000, 0, 0, 0, 0 },
        },
    };

    [Fact]
    public void Project_UsesPolynomial_ThenHomography_ElseNothing()
    {
        Assert.Equal((1200.0, 600.0), GazeEngine.Project(Linear(), 0.1, 0.1));
        var h = new WebcamCalibrationData { Polynomial = null, Homography = new[] { new[] { 10.0, 0, 5 }, new[] { 0, 10.0, 7 }, new[] { 0, 0, 1.0 } } };
        Assert.Equal((6.0, 8.0), GazeEngine.Project(h, 0.1, 0.1));
        Assert.Null(GazeEngine.Project(null, 0.1, 0.1));
    }

    [Fact]
    public void AxisCurve_InterpolatesAndExtrapolates()
    {
        double[] src = { 0, 100, 200 }, dst = { 0, 50, 200 };
        Assert.Equal(25, GazeEngine.ApplyAxisCurve(src, dst, 50));
        Assert.Equal(350, GazeEngine.ApplyAxisCurve(src, dst, 300));
    }

    [Fact]
    public void Step_AppliesTheQuickRecalOffset_AndSoftEdges()
    {
        var cal = Linear();
        cal.RuntimeOffset = new RuntimeOffsetData { Dx = 40, Dy = -20 };
        var e = new GazeEngine();
        (double X, double Y)? p = null;
        for (int i = 0; i < 60; i++) p = e.Step(cal, 0.1, 0.1, i * 333_333L, out _);   // steady gaze, filters settle
        Assert.Equal(1240, p!.Value.X, 0);
        Assert.Equal(580, p.Value.Y, 0);

        cal.MonitorBounds = new MonitorBoundsRecord { Width = 1920, Height = 1080 };
        var edge = new GazeEngine();
        for (int i = 0; i < 60; i++) p = edge.Step(cal, 5, 0.1, i * 333_333L, out _);   // way off-screen right
        Assert.InRange(p!.Value.X, 1800, 1920);                                          // settles short of the edge
    }

    [Fact]
    public void FaceLost_FiresOnceAfterFifteenMissingFrames_OnlyAfterAFace()
    {
        var e = new GazeEngine();
        Assert.Equal(0, Enumerable.Range(0, 30).Count(_ => e.FaceMissing()));   // never seen: never "lost"
        Assert.True(e.FaceSeen());
        Assert.False(e.FaceSeen());
        var fired = Enumerable.Range(1, 40).Where(_ => e.FaceMissing()).ToList();
        Assert.Single(fired);
        Assert.True(e.FaceSeen());
    }

    [Fact]
    public void GazeSide_NeedsThreeStableFrames()
    {
        var cal = Linear();
        cal.LeftRefVec = new[] { -0.2, 0 };
        cal.RightRefVec = new[] { 0.2, 0 };
        var e = new GazeEngine();
        GazeSide side = GazeSide.Center;
        for (int i = 0; i < 30; i++) e.Step(cal, 0, 0, i, out side);
        Assert.Equal(GazeSide.Center, side);
        var seen = new List<GazeSide>();
        for (int i = 30; i < 60; i++) { e.Step(cal, 0.3, 0, i, out side); seen.Add(side); }
        Assert.Equal(4, seen.IndexOf(GazeSide.Right));   // classifier flips on frame 2; three stable frames emit it
        Assert.Equal(GazeSide.Right, seen[^1]);
    }

    [Fact]
    public void TwoEyeGate_HoldsASpike_ButFailsOpen()
    {
        var e = new GazeEngine();
        for (int i = 0; i < 20; i++) Assert.NotNull(e.CombineEyes((0.10, 0), (0.11, 0)));
        Assert.Null(e.CombineEyes((0.10, 0), (0.60, 0)));                       // one eye lies: hold
        for (int i = 0; i < 7; i++) e.CombineEyes((0.10, 0), (0.60, 0));
        Assert.NotNull(e.CombineEyes((0.10, 0), (0.60, 0)));                    // budget spent: accept
    }

    [Fact]
    public void QuickRecalMedian_DropsTheSaccade()
    {
        var samples = Enumerable.Repeat((900.0, 900.0), 10).Concat(Enumerable.Range(0, 15).Select(i => (500.0 + i, 300.0 + i))).ToList();
        Assert.Equal((507.0, 307.0), GazeEngine.MedianAfterSaccadeSettle(samples, 10));   // 512 with the saccade kept
    }
}
