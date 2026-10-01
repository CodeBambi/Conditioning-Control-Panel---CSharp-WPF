using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
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

/// <summary>Webcam slice 5: the 16-point calibration over Core WebcamCalibrationFit, fed synthetic iris
/// vectors on a tracker whose frame source is blank (never a camera).</summary>
public sealed class WebcamCalibrationTests
{
    /// <summary>A plausible eye: iris vector roughly linear in screen position with a mild bend.</summary>
    private static (double X, double Y) Iris(double x, double y, int k = 0)
    {
        double ix = (x - 640) / 3000.0, iy = (y - 360) / 3000.0;
        double jitter = ((k * 7919) % 11 - 5) * 1e-5;
        return (ix + 0.3 * ix * iy + jitter, iy + 0.2 * ix * ix - jitter);
    }

    [Fact]
    public void Fit_MapsEveryDotBackOntoItself()
    {
        var grid = WebcamCalibrationFit.BuildGrid(1280, 720);
        var samples = grid.Select(g => Enumerable.Range(0, 25)
            .Select(k => { var (ix, iy) = Iris(g.Screen.X, g.Screen.Y, k); return (ix, iy, 0.0, 0.0, false); }).ToList()).ToList();
        var fit = WebcamCalibrationFit.Fit(samples, Array.Empty<(double, double)>(), grid.Select(g => g.Screen).ToArray(), 1280, 720);

        var data = Assert.IsType<WebcamCalibrationData>(fit.Data);
        Assert.False(fit.TooInaccurate);
        Assert.Equal("SixteenPoint", data.Mode);
        Assert.Equal(1280, data.MonitorBounds!.Width);
        Assert.Equal(7, data.Polynomial!.X.Length);
        foreach (var g in grid)
        {
            var (ix, iy) = Iris(g.Screen.X, g.Screen.Y);
            var p = GazeEngine.Project(data, ix, iy)!.Value;
            Assert.InRange(Math.Abs(p.X - g.Screen.X), 0, 5);
            Assert.InRange(Math.Abs(p.Y - g.Screen.Y), 0, 5);
        }
    }

    private sealed class BlankSource : IFrameSource
    {
        public bool Open() => true;
        public bool Read(Mat bgr) { bgr.Create(480, 640, MatType.CV_8UC3); bgr.SetTo(Scalar.All(0)); Thread.Sleep(20); return true; }
        public void Dispose() { }
    }

    /// <summary>Runs <paramref name="body"/> with a running blank tracker, a sentinel live calibration and
    /// no calibration file, restoring all of it afterwards.</summary>
    private static void WithTracker(bool start, Action<WebcamTracker, WebcamCalibrationData> body) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var s = CoreSettings.Current;
        var old = (s.WebcamConsentGiven, s.WebcamConsentVersion, s.WebcamCalibrated, s.WebcamCalibrationMode, WebcamTracker.SourceFactory);
        var tracker = WebcamTracker.Instance;
        var oldCal = tracker.Calibration;
        var sentinel = new WebcamCalibrationData { Mode = "Sentinel" };
        WebcamCalibrationWindow.Time = Clock = new ManualClock();
        try
        {
            s.WebcamConsentGiven = true;
            s.WebcamConsentVersion = WebcamConsent.ConsentVersion;
            WebcamTracker.SourceFactory = () => new BlankSource();
            WebcamCalibrationData.DeleteIfExists();
            tracker.Calibration = sentinel;
            if (start) Assert.True(tracker.Start(), tracker.LastError);
            body(tracker, sentinel);
        }
        finally
        {
            WebcamCalibrationWindow.Time = TimeProvider.System;
            tracker.Stop();
            WebcamCalibrationData.DeleteIfExists();
            tracker.Calibration = oldCal;
            (s.WebcamConsentGiven, s.WebcamConsentVersion, s.WebcamCalibrated, s.WebcamCalibrationMode, WebcamTracker.SourceFactory) = old;
        }
    });

    private static WebcamCalibrationWindow OpenAndContinue()
    {
        var win = new WebcamCalibrationWindow { WindowState = WindowState.Normal, Width = 1280, Height = 720 };
        win.Show();
        Dispatcher.UIThread.RunJobs();
        win.FindControl<Button>("BtnIntroContinue")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        return win;
    }

    private static ManualClock Clock = new();

    /// <summary>The window's clock, stepped by the test: a wait ends exactly when the test advances past it,
    /// whatever the machine load, so the flow never depends on wall-clock time.</summary>
    private sealed class ManualClock : TimeProvider
    {
        private long _now;
        private readonly List<(long Due, TimerCallback Cb, object? State)> _timers = new();
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => _now;
        public override ITimer CreateTimer(TimerCallback cb, object? state, TimeSpan due, TimeSpan period)
        {
            var t = (_now + (long)due.TotalMilliseconds, cb, state);
            _timers.Add(t);
            return new Handle(() => _timers.Remove(t));
        }
        public void Advance(int ms)
        {
            _now += ms;
            foreach (var t in _timers.Where(t => t.Due <= _now).ToList()) { _timers.Remove(t); t.Cb(t.State); }
        }
        private sealed class Handle(Action remove) : ITimer
        {
            public bool Change(TimeSpan due, TimeSpan period) => false;
            public void Dispose() => remove();
            public ValueTask DisposeAsync() { remove(); return default; }
        }
    }

    /// <summary>Looks at whichever dot is up, feeding one iris sample per 10 ms of window time, until
    /// <paramref name="done"/> or <paramref name="ms"/> of window time have passed.</summary>
    private static bool Gaze(WebcamCalibrationWindow win, Func<bool> done, int ms = 60000)
    {
        int k = 0;
        for (int t = 0; t < ms; t += 10, Clock.Advance(10))
        {
            if (win.ActiveDotIndex is var i and >= 0 && i < win.Positions.Length)
            {
                var (ix, iy) = Iris(win.Positions[i].Screen.X, win.Positions[i].Screen.Y, k++);
                win.OnHeadPose(0, 0);
                win.OnRawIris(ix, iy);
            }
            Dispatcher.UIThread.RunJobs();
            if (done()) return true;
        }
        return done();
    }

    [Fact]
    public void FullRun_SavesTheWpfProfileFile_AndGoesLive() => WithTracker(start: true, (tracker, sentinel) =>
    {
        var win = OpenAndContinue();
        var verify = win.FindControl<Border>("VerifyPanel")!;
        Assert.True(Gaze(win, () => verify.IsVisible), win.FindControl<TextBlock>("TxtErrorDetail")!.Text);

        var saved = WebcamCalibrationData.Load();
        Assert.NotNull(saved);
        Assert.Equal("SixteenPoint", saved!.Mode);
        Assert.Equal(7, saved.Polynomial!.Y.Length);
        Assert.Equal(3, saved.Homography!.Length);
        Assert.Equal("SixteenPoint", tracker.Calibration!.Mode);
        Assert.True(CoreSettings.Current.WebcamCalibrated);
        win.FindControl<Button>("BtnVerifyDone")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(win.IsVisible);
    });

    [Theory]
    [InlineData(true)]    // Escape during the gesture checks, with the candidate already live
    [InlineData(false)]   // panic: tracking stops during the gesture checks
    public void Cancel_SavesNothing_AndPutsTheOldCalibrationBack(bool escape) => WithTracker(start: true, (tracker, sentinel) =>
    {
        var win = OpenAndContinue();
        var validation = win.FindControl<Grid>("ValidationPanel")!;
        // Both stop with the unsaved candidate already live, so they prove the restore.
        Assert.True(Gaze(win, () => validation.IsVisible && tracker.Calibration?.Mode == "SixteenPoint"));
        if (escape) win.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        else tracker.Stop();
        Assert.True(Gaze(win, () => !win.IsVisible, 3000));
        Assert.False(File.Exists(WebcamCalibrationData.FilePath));
        Assert.Same(sentinel, tracker.Calibration);
        Assert.False(WebcamCalibrationWindow.IsShowing);
    });

    [Fact]
    public void RevokeDuringCalibration_IsNotUndoneByTheClose() => WithTracker(start: true, (tracker, _) =>
    {
        var win = OpenAndContinue();
        var validation = win.FindControl<Grid>("ValidationPanel")!;
        Assert.True(Gaze(win, () => validation.IsVisible && tracker.Calibration?.Mode == "SixteenPoint"));
        var s = CoreSettings.Current;
        var old = (s.WebcamConsentDate, s.WebcamTriggersEnabled, s.FocusGameEnabled);
        WebcamTracker.RevokeConsent();   // stops tracking, drops the calibration; the window closes
        (s.WebcamConsentDate, s.WebcamTriggersEnabled, s.FocusGameEnabled) = old;
        Assert.True(Gaze(win, () => !win.IsVisible, 3000));
        Assert.Null(tracker.Calibration);
        Assert.False(File.Exists(WebcamCalibrationData.FilePath));
    });

    [Fact]
    public void Save_IsAtomic_AFailedSaveKeepsTheOldFile() => WithTracker(start: false, (_, _) =>
    {
        Assert.True(new WebcamCalibrationData { Mode = "Old" }.Save());
        var tmp = WebcamCalibrationData.FilePath + ".tmp";
        Directory.CreateDirectory(tmp);   // the temp write cannot happen
        try
        {
            Assert.False(new WebcamCalibrationData { Mode = "New" }.Save());
            Assert.False(WebcamTracker.Instance.ApplyCalibration(new WebcamCalibrationData { Mode = "New" }));
            Assert.Equal("Old", WebcamCalibrationData.Load()!.Mode);
            Assert.NotEqual("New", WebcamTracker.Instance.Calibration?.Mode);
        }
        finally { Directory.Delete(tmp); }
        Assert.True(new WebcamCalibrationData { Mode = "New" }.Save());
        Assert.Equal("New", WebcamCalibrationData.Load()!.Mode);
        Assert.False(File.Exists(tmp));
    });

    [Fact]
    public void NoCamera_ShowsWpfMessage() => WithTracker(start: false, (tracker, _) =>
    {
        var win = new WebcamCalibrationWindow { WindowState = WindowState.Normal, Width = 1280, Height = 720 };
        win.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.True(win.FindControl<Border>("ErrorPanel")!.IsVisible);
        Assert.Equal("Webcam tracking is not running. Start tracking before calibrating.", win.FindControl<TextBlock>("TxtErrorDetail")!.Text);
        win.Close();
    });
}
