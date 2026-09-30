using System;
using System.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
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
        public bool Disposed;
        public bool Open() { Opens++; return OpenResult; }
        public bool Read(Mat bgr)
        {
            Interlocked.Increment(ref Reads);
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
            Assert.True(WebcamTracker.Instance.Start(), WebcamTracker.Instance.LastError);
            Assert.True(WebcamTracker.Instance.IsRunning);
            var until = DateTime.UtcNow.AddSeconds(20);
            while (Volatile.Read(ref src.Reads) < 5 && DateTime.UtcNow < until) Thread.Sleep(20);
            Assert.True(src.Reads >= 5);
            WebcamTracker.Instance.Stop();
            Assert.False(WebcamTracker.Instance.IsRunning);
            Assert.True(src.Disposed);
        });
    }
}
