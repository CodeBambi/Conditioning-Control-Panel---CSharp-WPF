using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// The frame-free webcam pipeline fed from a synthetic EAR/iris stream at 30 fps. No camera is
/// ever opened here: the "frame source" is a list of numbers.
/// </summary>
public class WebcamPipelineTests
{
    private const double Open = 0.30, Shut = 0.15;
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Feeds (ear, frames) segments at 33ms per frame; returns every event with its frame time.</summary>
    private static List<(BlinkEvent Event, double Ms)> Run(BlinkDetector d, params (double Ear, int Frames)[] segments)
    {
        var events = new List<(BlinkEvent, double)>();
        int frame = 0;
        foreach (var (ear, n) in segments)
            for (int i = 0; i < n; i++, frame++)
            {
                var e = d.Update(ear, ear, T0.AddMilliseconds(frame * 33));
                if (e != BlinkEvent.None) events.Add((e, frame * 33));
            }
        return events;
    }

    [Fact]
    public void ShortClosureIsOneBlink()
    {
        var d = new BlinkDetector();
        var ev = Run(d, (Open, 60), (Shut, 5), (Open, 10));
        Assert.Equal(new[] { BlinkEvent.Blink }, ev.Select(e => e.Event));
        Assert.Equal(1, d.BlinkCount);
    }

    [Fact]
    public void NoBlinkBeforeBaselineIsSeeded()
    {
        var d = new BlinkDetector();
        Assert.Empty(Run(d, (Open, BlinkDetector.EarMinSamplesForBaseline - 5), (Shut, 3), (Open, 5)));
    }

    [Fact]
    public void SingleFrameDipIsNoise()
    {
        // 1 frame closed = 33ms < MinBlinkClosedMs.
        Assert.Empty(Run(new BlinkDetector(), (Open, 60), (Shut, 1), (Open, 10)));
    }

    [Fact]
    public void LongHoldFiresEyesClosedLongOnceAndNeverABlink()
    {
        var ev = Run(new BlinkDetector(), (Open, 60), (Shut, 90), (Open, 10));
        Assert.Equal(new[] { BlinkEvent.EyesClosedLong }, ev.Select(e => e.Event));
        // Fires while still closed, at the 2 s mark, not on reopening.
        Assert.InRange(ev[0].Ms - 60 * 33, BlinkDetector.EyesClosedLongMs, BlinkDetector.EyesClosedLongMs + 34);
    }

    [Fact]
    public void CooldownSwallowsASecondBlinkTooSoon()
    {
        // Reopen for 5 frames (165ms) < 500ms cooldown.
        var ev = Run(new BlinkDetector(), (Open, 60), (Shut, 4), (Open, 5), (Shut, 4), (Open, 30), (Shut, 4), (Open, 5));
        Assert.Equal(2, ev.Count);
    }

    [Fact]
    public void FaceLossMidClosureIsNotABlink()
    {
        var d = new BlinkDetector();
        int frame = 0;
        BlinkEvent Feed(double ear) => d.Update(ear, ear, T0.AddMilliseconds(33 * frame++));
        for (int i = 0; i < 60; i++) Feed(Open);
        for (int i = 0; i < 3; i++) Feed(Shut);
        d.CancelClosure();
        for (int i = 0; i < 10; i++) Assert.Equal(BlinkEvent.None, Feed(Open));
    }

    [Fact]
    public void GazeSideHysteresisHoldsInsideTheLeaveBand()
    {
        double[] left = { -0.2 }, right = { 0.2 };        // spread 0.4: enter 0.07, leave 0.03
        var last = GazeSide.Center;
        Assert.Equal(GazeSide.Center, GazeSideClassifier.Classify(-0.05, left, right, ref last));
        Assert.Equal(GazeSide.Left, GazeSideClassifier.Classify(-0.08, left, right, ref last));
        Assert.Equal(GazeSide.Left, GazeSideClassifier.Classify(-0.05, left, right, ref last)); // inside hysteresis
        Assert.Equal(GazeSide.Center, GazeSideClassifier.Classify(-0.02, left, right, ref last));
        Assert.Equal(GazeSide.Right, GazeSideClassifier.Classify(0.08, left, right, ref last));
        // Uncalibrated: raw +-0.10.
        var none = GazeSide.Center;
        Assert.Equal(GazeSide.Left, GazeSideClassifier.Classify(-0.11, null, null, ref none));
        Assert.Equal(GazeSide.Center, GazeSideClassifier.Classify(-0.05, null, right, ref none));
    }

    [Fact]
    public void OneEuroConvergesAndSurvivesClockStall()
    {
        var f = new OneEuroFilter(1.0, 0.0, 1.0);
        long tick = System.Diagnostics.Stopwatch.Frequency / 30;
        Assert.Equal(0.0, f.Filter(0.0, 0));
        double y = 0;
        for (int i = 1; i <= 60; i++) y = f.Filter(1.0, i * tick);
        Assert.InRange(y, 0.95, 1.0);
        Assert.False(double.IsNaN(f.Filter(1.0, 60 * tick))); // dt = 0 falls back to 1/30 s
    }

    [Fact]
    public void CalibrationRoundTripsInTheSandboxProfileAndDeletes()
    {
        // RoadmapTestProfile points CCP_USERDATA_DIR at a temp dir for this whole assembly.
        Assert.StartsWith(RoadmapTestProfile.DirectoryPath, WebcamCalibrationData.FilePath);
        WebcamCalibrationData.DeleteIfExists();
        Assert.Null(WebcamCalibrationData.Load());
        new WebcamCalibrationData { LeftRefVec = new[] { -0.2, 0 }, RightRefVec = new[] { 0.2, 0 } }.Save();
        Assert.Equal(0.2, WebcamCalibrationData.Load()!.RightRefVec[0]);
        WebcamCalibrationData.DeleteIfExists();
        Assert.False(File.Exists(WebcamCalibrationData.FilePath));
    }
}
