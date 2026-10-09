using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Lab.GazeMinigame;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Lab.GazeMinigame;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Webcam;
using OpenCvSharp;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>Lab gaze minigame: the persisted Focus/Ignore packs reach Start, Start runs on the webcam
/// tracker (a fake frame source, never a camera), the tracker's gaze side scores the round, and panic
/// closes the game. Stepped clock and instant countdown (P08).</summary>
public sealed class GazeMinigameTests
{
    private sealed class BlankSource : IFrameSource
    {
        public bool Open() => true;
        public bool Read(Mat bgr) { bgr.Create(480, 640, MatType.CV_8UC3); bgr.SetTo(Scalar.All(0)); return true; }
        public void Dispose() { }
    }

    private sealed class SteppedClock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    // 1x1 PNG
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    [Fact]
    public void Start_RunsOnTheTracker_GazeScoresTheRound_PanicClosesTheGame() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var s = CoreSettings.Current;
        var old = (s.WebcamConsentGiven, s.WebcamConsentVersion, WebcamTracker.SourceFactory);
        var tracker = WebcamTracker.Instance;
        var oldCal = tracker.Calibration;
        var (oldClock, oldDelay) = (GazeMinigameWindow.Clock, GazeMinigameWindow.Delay);
        var dir = Directory.CreateTempSubdirectory("gaze-test-");
        GazeMinigameWindow? win = null;
        try
        {
            s.WebcamConsentGiven = true;
            s.WebcamConsentVersion = WebcamConsent.ConsentVersion;
            WebcamTracker.SourceFactory = () => new BlankSource();
            tracker.Calibration = new WebcamCalibrationData();
            var clock = new SteppedClock();
            GazeMinigameWindow.Clock = clock;
            GazeMinigameWindow.Delay = _ => Task.CompletedTask;

            string Pack(string name)
            {
                var p = Directory.CreateDirectory(Path.Combine(dir.FullName, name)).FullName;
                File.WriteAllBytes(Path.Combine(p, "1.png"), Png);
                return p;
            }
            var saved = new GazeMinigameSettings { ImageCount = 1, VideoCount = 0, PassTimeSec = 3, WrongHoldMs = 2000 };
            saved.Packs.Add(new GazePackRef { Path = Pack("focus"), Role = GazePackRole.Focus });
            saved.Packs.Add(new GazePackRef { Path = Pack("noise"), Role = GazePackRole.Ignore });
            saved.Save();

            win = new GazeMinigameWindow();
            win.Show();
            var start = win.FindControl<Button>("BtnStartGame")!;
            Assert.True(start.IsEnabled);                          // the saved roles came back

            start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(SpinUntil(() => win.FindControl<Grid>("GameplayScreen")!.IsVisible), "game did not start");
            Assert.True(tracker.IsRunning);                        // Start opened the (fake) camera

            var correct = win.CorrectIsLeftForTest ? GazeSide.Left : GazeSide.Right;
            clock.Now += TimeSpan.FromMilliseconds(1100);          // past the round-start grace
            win.RoundTicker_Tick(null, EventArgs.Empty);
            tracker.RaiseGazeSideForTest(correct);
            for (int i = 0; i < 60; i++) { clock.Now += TimeSpan.FromMilliseconds(50); win.RoundTicker_Tick(null, EventArgs.Empty); }
            Assert.Equal("GOOD GIRL", win.FindControl<TextBlock>("TxtFeedback")!.Text);

            Assert.True(GazeMinigameWindow.IsAnyRunning());        // fullscreen: panic must not arm the exit ladder
            PanicSurfaces.All.Single(x => x.Id == "gaze-minigame").Stop(null);
            Assert.False(win.IsVisible);
            Assert.False(GazeMinigameWindow.IsAnyRunning());
        }
        finally
        {
            win?.Close();
            tracker.Stop();
            tracker.Calibration = oldCal;
            (s.WebcamConsentGiven, s.WebcamConsentVersion, WebcamTracker.SourceFactory) = old;
            (GazeMinigameWindow.Clock, GazeMinigameWindow.Delay) = (oldClock, oldDelay);
            try { File.Delete(Path.Combine(CorePaths.UserData, GazeMinigameSettings.FileName)); } catch { }
            try { dir.Delete(true); } catch { }
        }
    });

    /// <summary>Start hops the camera open to the pool (StartAsync); pump the dispatcher until it lands.</summary>
    private static bool SpinUntil(Func<bool> done)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && sw.ElapsedMilliseconds < 15000)
        {
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Thread.Yield();
        }
        return done();
    }
}
