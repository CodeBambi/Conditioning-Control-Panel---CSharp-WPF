using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Fyp;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// For You eye control (WPF 7.1.5 FypHostService :1188-1437): consent first, then the camera, then
/// blink / eyesClosed / gaze frames, with an eyeStatus frame for every step. The tracker is a fake:
/// no test opens a camera. Swaps process-wide seams and opens a game window, so it runs alone.
/// </summary>
[Collection(RunsAloneCollection.Name)]
public sealed class FypEyeControlTests
{
    private sealed class FakeEye : IFypEye
    {
        public bool IsRunning { get; set; }
        public bool Calibrated { get; set; }
        public bool Faulted { get; set; }
        public bool StartAnswer = true;
        public int Starts, Stops;
        public (double OriginX, double OriginY, double Scale) CalSpace => (0, 0, 1);
        public event Action? OnBlink;
        public event Action? OnEyesClosedLong;
        public event Action<Point>? OnGazeMove;
        public Task<bool> StartAsync() { Starts++; IsRunning = StartAnswer; return Task.FromResult(StartAnswer); }
        public void Stop() { Stops++; IsRunning = false; }
        public void Blink() => OnBlink?.Invoke();
        public void CloseEyes() => OnEyesClosedLong?.Invoke();
        public void Gaze(Point p) => OnGazeMove?.Invoke(p);
        public bool HasGazeListener => OnGazeMove != null;
        public bool HasBlinkListener => OnBlink != null;
    }

    private static void Run(FakeEye eye, Func<Window, Task<bool>> consent, Action<GameWindow, List<JObject>> body, Func<Window, Task>? calibrate = null)
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var service = new SettingsService();
            var oldService = CoreSettings.ServiceProvider;
            CoreSettings.ServiceProvider = () => service;
            var oldGate = (CoreAccount.IsLoggedInProvider, CoreEntitlement.HasPremiumProvider, CoreEntitlement.HasLabProvider, CoreProgression.AddXPProvider);
            CoreAccount.IsLoggedInProvider = () => true;
            CoreEntitlement.HasPremiumProvider = () => true;
            CoreEntitlement.HasLabProvider = () => false;
            CoreProgression.AddXPProvider = (_, _) => { };
            var oldSource = GameWindow.FypEyeSource;
            var oldConsent = GameWindow.FypEyeConsent;
            var oldCalibrate = GameWindow.FypEyeCalibrate;
            var oldCalibrating = GameWindow.FypEyeCalibrating;
            var stats = Path.Combine(Path.GetTempPath(), "k19-fyp-eye-" + Guid.NewGuid().ToString("N") + ".json");
            var prevStats = FypHostService.StatsFilePathOverride;
            var s = CoreSettings.Current;
            GameWindow? w = null;
            try
            {
                FypHostService.StatsFilePathOverride = stats;
                GameWindow.FypEyeSource = () => eye;
                GameWindow.FypEyeConsent = consent;
                GameWindow.FypEyeCalibrate = calibrate ?? (_ => Task.CompletedTask);
                GameWindow.FypEyeCalibrating = () => false;
                s.FypEyeControl = false;
                s.FypEyeGaze = false;

                w = GameWindow.Launch(GameWindow.FypId)!;
                Dispatcher.UIThread.RunJobs();
                var posted = new List<JObject>();
                w.Posted += json => posted.Add(JObject.Parse(json));
                w.HandleMessage("{\"type\":\"ready\"}");
                posted.Clear();
                body(w, posted);
            }
            finally
            {
                try { w?.Close(); Dispatcher.UIThread.RunJobs(); } catch { }
                GameWindow.FypEyeSource = oldSource;
                GameWindow.FypEyeConsent = oldConsent;
                GameWindow.FypEyeCalibrate = oldCalibrate;
                GameWindow.FypEyeCalibrating = oldCalibrating;
                FypHostService.StatsFilePathOverride = prevStats;
                GameWindow.CloseAllForPanic();
                Dispatcher.UIThread.RunJobs();
                (CoreAccount.IsLoggedInProvider, CoreEntitlement.HasPremiumProvider, CoreEntitlement.HasLabProvider, CoreProgression.AddXPProvider) = oldGate;
                CoreSettings.ServiceProvider = oldService;
                service.SealForReset();
                try { File.Delete(stats); } catch { }
            }
        });
    }

    private static void Settle(GameWindow w)
    {
        for (var i = 0; i < 20 && !w.FypEyeTask.IsCompleted; i++) Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        Assert.True(w.FypEyeTask.IsCompleted);
    }

    private static void Toggle(GameWindow w, string key, bool on) =>
        w.HandleMessage("{\"type\":\"settings-changed\",\"key\":\"" + key + "\",\"value\":" + (on ? "true" : "false") + "}");

    [Fact]
    public void ConsentRefused_NeverTouchesTheCamera_AndTheToggleGoesBackOff()
    {
        var eye = new FakeEye();
        Run(eye, _ => Task.FromResult(false), (w, posted) =>
        {
            Toggle(w, "eyeControl", true);
            Settle(w);
            Assert.Equal(0, eye.Starts);
            var status = posted.Single();
            Assert.Equal("eyeStatus", (string?)status["type"]);
            Assert.Equal("consent", (string?)status["reason"]);
            Assert.False((bool)status["enabled"]!);
            Assert.False(CoreSettings.Current.FypEyeControl);
            Assert.False(eye.HasBlinkListener);
        });
    }

    [Fact]
    public void ConsentGiven_StartsTheCamera_ThenBlinksAndClosedEyesReachThePage()
    {
        var eye = new FakeEye();
        var asked = 0;
        Run(eye, _ => { asked++; return Task.FromResult(true); }, (w, posted) =>
        {
            Toggle(w, "eyeControl", true);
            Settle(w);
            Assert.Equal(1, asked);
            Assert.Equal(1, eye.Starts);
            Assert.Equal(new[] { "starting", null }, posted.Select(p => (string?)p["reason"]));
            Assert.True((bool)posted[1]["enabled"]!);
            Assert.True((bool)posted[1]["running"]!);
            Assert.False((bool)posted[1]["calibrated"]!);

            posted.Clear();
            eye.Blink();
            eye.CloseEyes();
            Assert.Equal(new[] { "blink", "eyesClosed" }, posted.Select(p => (string?)p["type"]));

            // Off: the listeners go, the camera this window started is stopped, the page hears it.
            posted.Clear();
            Toggle(w, "eyeControl", false);
            Assert.False(eye.HasBlinkListener);
            Assert.Equal(1, eye.Stops);
            Assert.False((bool)posted.Single()["enabled"]!);
            eye.Blink();
            Assert.Single(posted);
        });
    }

    [Fact]
    public void ACameraThatWillNotStart_SaysWhy()
    {
        var none = new FakeEye { StartAnswer = false };
        Run(none, _ => Task.FromResult(true), (w, posted) =>
        {
            Toggle(w, "eyeControl", true);
            Settle(w);
            Assert.Equal("no-camera", (string?)posted.Last()["reason"]);
            Assert.False(CoreSettings.Current.FypEyeControl);
        });
        var broken = new FakeEye { StartAnswer = false, Faulted = true };
        Run(broken, _ => Task.FromResult(true), (w, posted) =>
        {
            Toggle(w, "eyeControl", true);
            Settle(w);
            Assert.Equal("error", (string?)posted.Last()["reason"]);
        });
    }

    [Fact]
    public void ACameraSomeoneElseStarted_IsNotStoppedByTheFeed()
    {
        var eye = new FakeEye { IsRunning = true };
        Run(eye, _ => Task.FromResult(true), (w, posted) =>
        {
            Toggle(w, "eyeControl", true);
            Settle(w);
            Assert.Equal(0, eye.Starts);
            Toggle(w, "eyeControl", false);
            Assert.Equal(0, eye.Stops);
            Assert.True(eye.IsRunning);
        });
    }

    [Fact]
    public void Gaze_IsWiredOnlyWithGazeModeAndACalibration_AndIsThrottled()
    {
        var eye = new FakeEye();
        Run(eye, _ => Task.FromResult(true), (w, posted) =>
        {
            Toggle(w, "eyeControl", true);
            Settle(w);
            Toggle(w, "eyeGaze", true);
            Assert.False(eye.HasGazeListener);   // no calibration: the page falls back to a random tile

            // The page's Calibrate button: the dialog runs, then gaze is wired and the page hears it.
            posted.Clear();
            var calibrated = 0;
            GameWindow.FypEyeCalibrate = _ => { calibrated++; eye.Calibrated = true; return Task.CompletedTask; };
            w.HandleMessage("{\"type\":\"calibrate\"}");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, calibrated);
            Assert.True(eye.HasGazeListener);
            Assert.True((bool)posted.Last()["calibrated"]!);

            posted.Clear();
            eye.Gaze(new Point(10, 10));
            eye.Gaze(new Point(20, 20));   // inside the 100 ms window: dropped
            Assert.True(posted.Count(p => (string?)p["type"] == "gaze") <= 1);

            Toggle(w, "eyeGaze", false);
            Assert.False(eye.HasGazeListener);
        });
    }

    [Fact]
    public void ClosingTheFeed_HandsTheCameraBack()
    {
        var eye = new FakeEye();
        Run(eye, _ => Task.FromResult(true), (w, _) =>
        {
            Toggle(w, "eyeControl", true);
            Settle(w);
            w.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, eye.Stops);
            Assert.False(eye.HasBlinkListener);
        });
    }

    [Fact]
    public void GazeMath_GoesThroughBothDpiScales()
    {
        // Calibrated on a 150% monitor at physical (1920, 0); the web view sits at physical (2020, 100)
        // on a 200% window, 400 x 300 DIPs. Gaze DIP (200, 200) -> physical (2220, 300).
        var g = FypGaze.Normalize(new Point(200, 200), (1920, 0, 1.5), new PixelPoint(2020, 100), 2.0, new Size(400, 300))!.Value;
        Assert.Equal(0.25, g.X, 6);
        Assert.Equal(1.0 / 3, g.Y, 6);
        Assert.True(g.Inside);

        var off = FypGaze.Normalize(new Point(0, 0), (0, 0, 1), new PixelPoint(500, 500), 1.0, new Size(400, 300))!.Value;
        Assert.Equal((0.0, 0.0, false), off);   // clamped, and flagged as off the window
        Assert.Null(FypGaze.Normalize(new Point(1, 1), (0, 0, 1), new PixelPoint(0, 0), 1.0, new Size(0, 0)));
    }
}
