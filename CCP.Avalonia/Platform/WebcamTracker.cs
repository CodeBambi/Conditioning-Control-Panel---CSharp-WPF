using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Webcam;
using OpenCvSharp;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>Where frames come from. The only seam in the webcam engine: tests hand in a fake,
    /// the app an OpenCV capture. Frames are written into the caller's Mat and never leave memory.</summary>
    internal interface IFrameSource : IDisposable
    {
        bool Open();
        bool Read(Mat bgr);
    }

    /// <summary>V4L2 capture through OpenCV. <c>CCP_WEBCAM_DEVICE</c> (an index) overrides the saved camera
    /// index; live checks point it at one with no /dev/videoN so no real camera is opened.</summary>
    internal sealed class OpenCvFrameSource : IFrameSource
    {
        private VideoCapture? _cap;

        public bool Open()
        {
            int index = int.TryParse(Environment.GetEnvironmentVariable("CCP_WEBCAM_DEVICE"), out var i)
                ? i : Math.Max(0, CoreSettings.Current.WebcamDeviceIndex);
            _cap = new VideoCapture(index, OperatingSystem.IsLinux() ? VideoCaptureAPIs.V4L2 : VideoCaptureAPIs.ANY);
            if (!_cap.IsOpened()) return false;
            // WPF's default mode (WebcamTrackingService CaptureWidth/Height/TargetFps).
            _cap.Set(VideoCaptureProperties.FrameWidth, 640);
            _cap.Set(VideoCaptureProperties.FrameHeight, 480);
            _cap.Set(VideoCaptureProperties.Fps, 30);
            return true;
        }

        public bool Read(Mat bgr) => _cap != null && _cap.Read(bgr) && !bgr.Empty();

        public void Dispose() { _cap?.Release(); _cap?.Dispose(); _cap = null; }
    }

    /// <summary>
    /// The Linux webcam engine: frame source -> Core BlazeFace/FaceMesh/Iris detectors -> Core
    /// BlinkDetector, the same pipeline WebcamTrackingService.ProcessFrame runs on WPF, blink half
    /// only. Camera open only between Start and Stop, only with current consent.
    /// ponytail: blink only - gaze projection, head pose, mouth/tongue stay WPF until a consumer
    /// (calibration, gaze minigame) is ported.
    /// </summary>
    internal sealed class WebcamTracker
    {
        internal static WebcamTracker Instance { get; } = new();

        /// <summary>Tests swap in a fake; the app captures through OpenCV.</summary>
        internal static Func<IFrameSource> SourceFactory = () => new OpenCvFrameSource();

        internal static string ModelDir = Path.Combine(AppContext.BaseDirectory, "Resources", "Models");

        public event Action? OnBlink;
        public event Action? StateChanged;

        public bool IsRunning => _thread != null;
        /// <summary>Why the last start failed, for the user; null after a good start.</summary>
        public string? LastError { get; private set; }

        private Thread? _thread;
        private volatile bool _stop;
        private IFrameSource? _source;
        private BlazeFaceDetector? _face;
        private FaceMeshDetector? _mesh;
        private IrisDetector? _iris;
        private readonly BlinkDetector _blink = new();
        private int _busy;

        /// <summary>Off the UI thread, as WPF StartWebcamOffUiThreadAsync: model load and camera
        /// negotiation can take seconds.</summary>
        public Task<bool> StartAsync() => Task.Run(Start);
        public Task StopAsync() => Task.Run(Stop);

        public bool Start()
        {
            if (IsRunning) return true;
            if (Interlocked.Exchange(ref _busy, 1) != 0) return false;
            try
            {
                LastError = null;
                if (!WebcamConsent.IsCurrent(CoreSettings.Current)) { LastError = "Webcam consent is not current."; return false; }
                try { Cv2.GetVersionString(); }
                catch (Exception ex)
                {
                    Log.Warning(ex, "[Webcam] OpenCV native library failed to load");
                    LastError = "Webcam tracking is unavailable: the OpenCV library could not be loaded on this system.";
                    return false;
                }
                try
                {
                    _face = new BlazeFaceDetector(Path.Combine(ModelDir, "face_detection_short_range.onnx"), Path.Combine(ModelDir, "blazeface_anchors.json"));
                    _mesh = new FaceMeshDetector(Path.Combine(ModelDir, "face_landmark.onnx"));
                    _iris = new IrisDetector(Path.Combine(ModelDir, "iris_landmark.onnx"));
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "[Webcam] eye-tracking models failed to load");
                    LastError = "Webcam tracking is unavailable: the eye-tracking models could not be loaded.";
                    Release();
                    return false;
                }
                _source = SourceFactory();
                bool opened;
                try { opened = _source.Open(); } catch (Exception ex) { Log.Warning(ex, "[Webcam] open threw"); opened = false; }
                if (!opened)
                {
                    LastError = "No camera could be opened. Check that a webcam is connected and not in use by another app.";
                    Release();
                    return false;
                }
                _blink.Reset();
                _stop = false;
                _thread = new Thread(Loop) { IsBackground = true, Name = "WebcamCapture", Priority = ThreadPriority.BelowNormal };
                _thread.Start();
                Log.Information("[Webcam] tracking started");
                return true;
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
                Dispatcher.UIThread.Post(() => StateChanged?.Invoke());
            }
        }

        public void Stop()
        {
            var t = _thread;
            if (t == null) return;
            _stop = true;
            // WPF Stop: bounded join; a wedged driver must not hang the caller forever.
            if (!t.Join(TimeSpan.FromSeconds(5))) Log.Warning("[Webcam] capture thread did not exit in 5s");
            else Release();
            _thread = null;
            Log.Information("[Webcam] tracking stopped");
            Dispatcher.UIThread.Post(() => StateChanged?.Invoke());
        }

        private void Release()
        {
            try { _source?.Dispose(); } catch (Exception ex) { Diag.Swallowed(ex); }
            _face?.Dispose(); _mesh?.Dispose(); _iris?.Dispose();
            _source = null; _face = null; _mesh = null; _iris = null;
        }

        private void Loop()
        {
            using var frame = new Mat();
            int fails = 0;
            while (!_stop)
            {
                if (!_source!.Read(frame))
                {
                    // WPF MaxConsecutiveReadFails: a camera that stopped delivering ends tracking.
                    if (++fails >= 30) { LastError = "The camera stopped delivering frames."; break; }
                    Thread.Sleep(20);
                    continue;
                }
                fails = 0;
                try { ProcessFrame(frame); }
                catch (Exception ex) { Log.Warning(ex, "[Webcam] frame processing threw"); Thread.Sleep(50); }
            }
            if (!_stop) { Release(); _thread = null; Dispatcher.UIThread.Post(() => StateChanged?.Invoke()); }
        }

        /// <summary>WPF ProcessFrame steps 1, 2, 4 and 5: face, mesh, iris, EAR blink.</summary>
        private void ProcessFrame(Mat bgr)
        {
            var rect = _face!.Detect(bgr);
            if (rect is not { Width: >= 16, Height: >= 16 } r) { _blink.CancelClosure(); return; }
            var lm = _mesh!.Detect(bgr, r);
            if (lm == null) { _blink.CancelClosure(); return; }
            var left = _iris!.Detect(bgr, lm[FaceMeshDetector.LeftEyeOuterIdx], lm[FaceMeshDetector.LeftEyeInnerIdx], isRightEye: false);
            var right = _iris.Detect(bgr, lm[FaceMeshDetector.RightEyeOuterIdx], lm[FaceMeshDetector.RightEyeInnerIdx], isRightEye: true);
            if (left == null || right == null) return;
            var ev = _blink.Update(
                BlinkDetector.ComputeEar(left.Contour, BlinkDetector.IrisContourEarIndices),
                BlinkDetector.ComputeEar(right.Contour, BlinkDetector.IrisContourEarIndices), DateTime.UtcNow);
            if (ev == BlinkEvent.Blink) Dispatcher.UIThread.Post(() => OnBlink?.Invoke());
        }
    }
}
