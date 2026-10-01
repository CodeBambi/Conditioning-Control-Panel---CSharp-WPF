using System;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Webcam;
using OpenCvSharp;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>Webcam slice 4: head pose on real OpenCV natives, and Quick Recal over the tracker with a
/// fake frame source (never a camera).</summary>
public sealed class WebcamGazeTests
{
    /// <summary>Six model points seen by a pinhole camera 1.5 m away, head turned by <paramref name="yaw"/>.</summary>
    private static float[][] Landmarks(double yaw)
    {
        var model = new (int Idx, double X, double Y, double Z)[]
        {
            (1, 0, 0, 0), (152, 0, -330, -65), (33, 225, 170, -135), (263, -225, 170, -135), (61, 150, -150, -125), (291, -150, -150, -125),
        };
        var lm = new float[468][];
        for (int i = 0; i < lm.Length; i++) lm[i] = new[] { 320f, 240f };
        foreach (var (idx, x, y, z) in model)
        {
            double xr = x * Math.Cos(yaw) + z * Math.Sin(yaw), zr = -x * Math.Sin(yaw) + z * Math.Cos(yaw);
            double zc = -zr + 1500;
            lm[idx] = new[] { (float)(640 * xr / zc + 320), (float)(640 * -y / zc + 240) };
        }
        return lm;
    }

    [Fact]
    public void HeadPose_TracksAYawTurn()
    {
        var straight = WebcamTracker.HeadPose(new GazeEngine(), Landmarks(0), 640, 480);
        var turned = WebcamTracker.HeadPose(new GazeEngine(), Landmarks(0.3), 640, 480);
        Assert.NotNull(straight);
        Assert.NotNull(turned);
        Assert.InRange(Math.Abs(turned!.Value.Yaw - straight!.Value.Yaw), 0.25, 0.35);
    }

    private sealed class BlankSource : IFrameSource
    {
        public bool Open() => true;
        public bool Read(Mat bgr) { bgr.Create(480, 640, MatType.CV_8UC3); bgr.SetTo(Scalar.All(0)); Thread.Sleep(20); return true; }
        public void Dispose() { }
    }

    [Fact]
    public void QuickRecal_WithNoGaze_RefusesAsWpf_AndRestoresTheOldOffset() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var s = CoreSettings.Current;
        var old = (s.WebcamConsentGiven, s.WebcamConsentVersion, WebcamTracker.SourceFactory);
        var tracker = WebcamTracker.Instance;
        var oldCal = tracker.Calibration;
        try
        {
            s.WebcamConsentGiven = true;
            s.WebcamConsentVersion = WebcamConsent.ConsentVersion;
            WebcamTracker.SourceFactory = () => new BlankSource();
            tracker.Calibration = new WebcamCalibrationData { RuntimeOffset = new RuntimeOffsetData { Dx = 5, Dy = 7 } };
            Assert.True(tracker.Start(), tracker.LastError);

            var win = new WebcamQuickRecalWindow();
            win.Show();
            Assert.True(WaitUntil(() => tracker.Calibration!.RuntimeOffset == null));   // sampling the raw projection
            var error = win.FindControl<TextBlock>("TxtErrorDetail")!;
            Assert.True(WaitUntil(() => error.IsEffectivelyVisible && error.Text != null));
            Assert.Equal("Didn't capture enough gaze samples (0). Make sure your face is visible and try again.", error.Text);
            win.Close();
            Assert.Equal(5, tracker.Calibration!.RuntimeOffset!.Dx);                   // cancelled: old nudge back
        }
        finally
        {
            tracker.Stop();
            tracker.Calibration = oldCal;
            (s.WebcamConsentGiven, s.WebcamConsentVersion, WebcamTracker.SourceFactory) = old;
        }
    });

    [Fact]
    public void Revoke_DropsTheInMemoryCalibration() => AvaloniaTestDispatcher.Run(() =>
    {
        var s = CoreSettings.Current;
        var old = (s.WebcamConsentGiven, s.WebcamConsentVersion, s.WebcamConsentDate, s.WebcamCalibrated, s.WebcamCalibrationMode, s.WebcamTriggersEnabled, s.FocusGameEnabled);
        try
        {
            WebcamTracker.Instance.Calibration = new WebcamCalibrationData();
            WebcamTracker.RevokeConsent();
            Assert.Null(WebcamTracker.Instance.Calibration);
        }
        finally
        {
            (s.WebcamConsentGiven, s.WebcamConsentVersion, s.WebcamConsentDate, s.WebcamCalibrated, s.WebcamCalibrationMode, s.WebcamTriggersEnabled, s.FocusGameEnabled) = old;
        }
    });

    private static bool WaitUntil(Func<bool> done, int ms = 8000)
    {
        for (var sw = System.Diagnostics.Stopwatch.StartNew(); sw.ElapsedMilliseconds < ms; Thread.Sleep(20))
        {
            Dispatcher.UIThread.RunJobs();
            if (done()) return true;
        }
        return done();
    }
}
