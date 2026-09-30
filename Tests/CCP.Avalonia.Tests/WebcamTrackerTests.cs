using System;
using System.IO;
using System.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Webcam;
using OpenCvSharp;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// The Linux webcam engine over a fake frame source: never a real camera. Frames run through the real
/// OpenCV + ONNX Runtime natives and the shipped models, so this also proves both natives load.
/// </summary>
public sealed class WebcamTrackerTests
{
    private sealed class FakeSource : IFrameSource
    {
        public bool OpenResult = true;
        public int Opens, Reads;
        public volatile bool Disposed;
        /// <summary>Unset = Read blocks, as a wedged driver does.</summary>
        public readonly ManualResetEventSlim Gate = new(true);
        public bool Open() { Opens++; return OpenResult; }
        public bool Read(Mat bgr)
        {
            Interlocked.Increment(ref Reads);
            Gate.Wait();
            bgr.Create(480, 640, MatType.CV_8UC3);
            bgr.SetTo(Scalar.All(0)); // no face in it: the detector says so and nothing fires
            Thread.Sleep(5);
            return true;
        }
        public void Dispose() => Disposed = true;
    }

    // On the shared test dispatcher: the tracker posts state changes to the UI thread, and the first
    // touch of Dispatcher.UIThread must not come from a bare test thread.
    private static void WithConsent(bool consent, Action body) => AvaloniaTestDispatcher.Run(() =>
    {
        var s = ConditioningControlPanel.CoreSettings.Current;
        var old = (s.WebcamConsentGiven, s.WebcamConsentVersion, WebcamTracker.SourceFactory);
        try
        {
            s.WebcamConsentGiven = consent;
            s.WebcamConsentVersion = consent ? WebcamConsent.ConsentVersion : "";
            body();
        }
        finally
        {
            WebcamTracker.Instance.Stop();
            (s.WebcamConsentGiven, s.WebcamConsentVersion, WebcamTracker.SourceFactory) = old;
        }
    });

    [Fact]
    public void NoConsent_NeverOpensTheSource()
    {
        var src = new FakeSource();
        WithConsent(false, () =>
        {
            WebcamTracker.SourceFactory = () => src;
            Assert.False(WebcamTracker.Instance.Start());
            Assert.Equal(0, src.Opens);
            Assert.NotNull(WebcamTracker.Instance.LastError);
        });
    }

    [Fact]
    public void MissingCamera_FailsCleanlyWithAMessage()
    {
        var src = new FakeSource { OpenResult = false };
        WithConsent(true, () =>
        {
            WebcamTracker.SourceFactory = () => src;
            Assert.False(WebcamTracker.Instance.Start());
            Assert.False(WebcamTracker.Instance.IsRunning);
            Assert.StartsWith("No camera could be opened", WebcamTracker.Instance.LastError);
            Assert.True(src.Disposed);
        });
    }

    [Fact]
    public void Consented_FramesFlowThroughTheModels_AndStopReleasesTheSource()
    {
        var src = new FakeSource();
        WithConsent(true, () =>
        {
            WebcamTracker.SourceFactory = () => src;
            WebcamTracker.Instance.FrameErrors = 0;
            Assert.True(WebcamTracker.Instance.Start(), WebcamTracker.Instance.LastError);
            Assert.True(WebcamTracker.Instance.IsRunning);
            WaitFor(() => Volatile.Read(ref src.Reads) >= 5);
            Assert.Equal(0, WebcamTracker.Instance.FrameErrors); // the loop swallows; nothing may have thrown
            WebcamTracker.Instance.Stop();
            Assert.False(WebcamTracker.Instance.IsRunning);
            Assert.True(src.Disposed);
        });
    }

    private static void WaitFor(Func<bool> cond)
    {
        var until = DateTime.UtcNow.AddSeconds(20);
        while (!cond() && DateTime.UtcNow < until) Thread.Sleep(20);
        Assert.True(cond());
    }

    [Fact]
    public void Detectors_RunOnASyntheticFrame_WithoutThrowing()
    {
        var dir = WebcamTracker.ModelDir;
        using var face = new BlazeFaceDetector(Path.Combine(dir, "face_detection_short_range.onnx"), Path.Combine(dir, "blazeface_anchors.json"));
        using var mesh = new FaceMeshDetector(Path.Combine(dir, "face_landmark.onnx"));
        using var iris = new IrisDetector(Path.Combine(dir, "iris_landmark.onnx"));
        using var bgr = new Mat(480, 640, MatType.CV_8UC3, new Scalar(90, 120, 160));
        Cv2.Circle(bgr, new Point(320, 240), 120, new Scalar(150, 180, 220), -1);

        Assert.Null(face.Detect(bgr)); // flat blob, no face
        var lm = mesh.Detect(bgr, new Rect(200, 120, 240, 240));
        Assert.True(lm == null || lm.Length == 468);
        var eye = iris.Detect(bgr, new[] { 260f, 220f }, new[] { 300f, 220f }, isRightEye: true);
        Assert.NotNull(eye);
        Assert.Equal(71, eye!.Contour.Length);
    }

    [Fact]
    public void RevokingConsent_WhileRunning_ClosesTheCamera()
    {
        var src = new FakeSource();
        WithConsent(true, () =>
        {
            WebcamTracker.SourceFactory = () => src;
            Assert.True(WebcamTracker.Instance.Start(), WebcamTracker.Instance.LastError);
            ConditioningControlPanel.CoreSettings.Current.WebcamConsentGiven = false;
            WaitFor(() => src.Disposed);
            WaitFor(() => !WebcamTracker.Instance.IsRunning);
        });
    }

    [Fact]
    public void WedgedStop_AbandonsTheLoop_RefusesStart_UntilItReleasesTheCamera()
    {
        var wedged = new FakeSource();
        var next = new FakeSource();
        WithConsent(true, () =>
        {
            WebcamTracker.SourceFactory = () => wedged;
            Assert.True(WebcamTracker.Instance.Start(), WebcamTracker.Instance.LastError);
            WaitFor(() => Volatile.Read(ref wedged.Reads) >= 1);
            wedged.Gate.Reset();
            int r = Volatile.Read(ref wedged.Reads);
            WaitFor(() => Volatile.Read(ref wedged.Reads) > r); // this Read is now blocked on the gate
            WebcamTracker.Instance.Stop();                          // 5 s join times out
            Assert.False(WebcamTracker.Instance.IsRunning);
            Assert.False(wedged.Disposed);

            WebcamTracker.SourceFactory = () => next;
            Assert.False(WebcamTracker.Instance.Start());
            Assert.StartsWith("The previous camera session is still closing", WebcamTracker.Instance.LastError);
            Assert.Equal(0, next.Opens);

            wedged.Gate.Set();                  // driver lets go: the abandoned loop closes its camera
            WaitFor(() => wedged.Disposed);
            Thread.Sleep(100);
            Assert.True(WebcamTracker.Instance.Start(), WebcamTracker.Instance.LastError);
        });
    }
}
