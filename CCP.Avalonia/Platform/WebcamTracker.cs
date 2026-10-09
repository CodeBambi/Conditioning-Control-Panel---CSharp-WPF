using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Webcam;
using OpenCvSharp;
using Serilog;
using ScreenPoint = Avalonia.Point;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>Where frames come from. The only seam in the webcam engine: tests hand in a fake,
    /// the app an OpenCV capture. Frames are written into the caller's Mat and never leave memory.</summary>
    internal interface IFrameSource : IDisposable
    {
        bool Open();
        bool Read(Mat bgr);
    }

    /// <summary>The Linux twin of WPF WebcamDeviceEnumerator: one entry per V4L2 capture node read from sysfs,
    /// never opening a camera. Nodes whose <c>index</c> is not 0 are the metadata twins a UVC camera also
    /// exposes; listing them would offer a "camera" that yields no frames.</summary>
    internal static class V4l2Cameras
    {
        /// <summary>sysfs root; tests point it at a fake tree.</summary>
        internal static string Root = "/sys/class/video4linux";

        public static IReadOnlyList<(int Index, string Name)> Enumerate()
        {
            var list = new List<(int, string)>();
            try
            {
                if (!Directory.Exists(Root)) return list;
                foreach (var dir in Directory.GetDirectories(Root, "video*"))
                {
                    if (!int.TryParse(Path.GetFileName(dir)["video".Length..], out int n)) continue;
                    var idx = Path.Combine(dir, "index");
                    if (File.Exists(idx) && File.ReadAllText(idx).Trim() != "0") continue;
                    var nameFile = Path.Combine(dir, "name");
                    var name = File.Exists(nameFile) ? File.ReadAllText(nameFile).Trim() : "";
                    list.Add((n, name.Length > 0 ? name : $"video{n}"));
                }
            }
            catch (Exception ex) { Log.Warning(ex, "Webcam: camera enumeration failed"); }
            list.Sort((a, b) => a.Item1.CompareTo(b.Item1));
            return list;
        }
    }

    /// <summary>V4L2 capture through OpenCV. <c>CCP_WEBCAM_DEVICE</c> (an index) overrides the saved camera
    /// index; live checks point it at one with no /dev/videoN so no real camera is opened.</summary>
    internal sealed class OpenCvFrameSource : IFrameSource
    {
        private VideoCapture? _cap;

        public bool Open()
        {
            int index = int.TryParse(Environment.GetEnvironmentVariable("CCP_WEBCAM_DEVICE"), out var i)
                ? Math.Max(0, i) : ResolveSavedIndex();
            _cap = new VideoCapture(index, OperatingSystem.IsLinux() ? VideoCaptureAPIs.V4L2 : VideoCaptureAPIs.ANY);
            if (!_cap.IsOpened()) return false;
            // WPF's default mode (WebcamTrackingService CaptureWidth/Height/TargetFps).
            _cap.Set(VideoCaptureProperties.FrameWidth, 640);
            _cap.Set(VideoCaptureProperties.FrameHeight, 480);
            _cap.Set(VideoCaptureProperties.Fps, 30);
            return true;
        }

        /// <summary>The saved /dev/videoN when it is listed, else the first listed camera - the one the
        /// Settings picker shows in that case (V4L2 numbers have gaps, so "0" may not exist).</summary>
        internal static int ResolveSavedIndex()
        {
            int saved = CoreSettings.Current.WebcamDeviceIndex;
            var cams = V4l2Cameras.Enumerate();
            foreach (var c in cams) if (c.Index == saved) return saved;
            return cams.Count > 0 ? cams[0].Index : Math.Max(0, saved);
        }

        public bool Read(Mat bgr) => _cap != null && _cap.Read(bgr) && !bgr.Empty();

        public void Dispose() { _cap?.Release(); _cap?.Dispose(); _cap = null; }
    }

    /// <summary>
    /// The Linux webcam engine: frame source -> Core BlazeFace/FaceMesh/Iris detectors -> Core
    /// BlinkDetector + GazeEngine, the same pipeline WebcamTrackingService.ProcessFrame runs on WPF:
    /// blinks, face lost/found, head pose, raw iris, gaze side and the projected gaze point. Camera
    /// open only between Start and Stop, only with current consent.
    /// ponytail: no mouth/tongue, long stare or gaze lock-on yet (the MAR/HSV detectors are still
    /// WPF-only), so calibration's mouth prompts time out to "moving on", as WPF does on a miss.
    /// </summary>
    internal sealed class WebcamTracker
    {
        internal static WebcamTracker Instance { get; } = new();

        /// <summary>Tests swap in a fake; the app captures through OpenCV.</summary>
        internal static Func<IFrameSource> SourceFactory = () => new OpenCvFrameSource();

        internal static string ModelDir = Path.Combine(AppContext.BaseDirectory, "Resources", "Models");

        public event Action? OnBlink;
        /// <summary>Eyes held shut past BlinkDetector.EyesClosedLongMs (WPF OnEyesClosedLong).</summary>
        public event Action? OnEyesClosedLong;
        public event Action? StateChanged;
        public event Action? OnFaceLost;
        public event Action? OnFaceFound;
        /// <summary>Median-filtered iris vector, what calibration samples (WPF OnRawIris).</summary>
        public event Action<double, double>? OnRawIris;
        public event Action<double, double>? OnHeadPose;
        public event Action<GazeSide>? OnGazeSide;
        /// <summary>Tests: what a classified frame publishes, without a face in front of a camera.</summary>
        internal void RaiseGazeSideForTest(GazeSide side) => OnGazeSide?.Invoke(side);
        /// <summary>Gaze in DIPs of the calibrated monitor; fires only with a calibration.</summary>
        public event Action<ScreenPoint>? OnGazeMove;
        /// <summary>Start's stage and status text, 1.0 when tracking is up (WPF OnStartupProgress, same
        /// texts); drives the loading splash. A failed start ends with StateChanged and LastError.</summary>
        public event Action<double, string>? OnStartupProgress;
        /// <summary>The last start failed only because a Stop (panic, revoke) overtook it: WPF's Stopped
        /// state, which closes the splash instead of showing an error.</summary>
        internal bool StartWasStopped { get; private set; }
        private void Progress(double p, string status) => Dispatcher.UIThread.Post(() => OnStartupProgress?.Invoke(p, status));

        /// <summary>The saved calibration (WPF's file in the profile folder), read once on first use
        /// as WPF reads it in the service constructor; revoke clears it with the file.</summary>
        public WebcamCalibrationData? Calibration
        {
            get { if (!_calibrationLoaded) { _calibration = WebcamCalibrationData.Load(); _calibrationLoaded = true; } return _calibration; }
            internal set { _calibration = value; _calibrationLoaded = true; }   // internal: tests
        }
        private volatile WebcamCalibrationData? _calibration;
        private volatile bool _calibrationLoaded;

        public bool IsRunning => _run != null;
        /// <summary>Why the last start failed, for the user; null after a good start.</summary>
        public string? LastError { get; private set; }
        /// <summary>Frames whose processing threw (the loop logs and carries on); tests assert zero.</summary>
        internal int FrameErrors;

        /// <summary>One camera session. The loop owns it and releases it when it exits, so a loop
        /// abandoned by a timed-out Stop still closes its own camera when the driver lets go.</summary>
        private sealed class Run
        {
            public volatile bool Stop;
            public IFrameSource? Source;
            public BlazeFaceDetector? Face;
            public FaceMeshDetector? Mesh;
            public IrisDetector? Iris;
            public Thread? Thread;
            public void Release()
            {
                try { Source?.Dispose(); } catch (Exception ex) { Diag.Swallowed(ex); }
                Face?.Dispose(); Mesh?.Dispose(); Iris?.Dispose();
                Source = null; Face = null; Mesh = null; Iris = null;
            }
        }

        private Run? _run;
        private Thread? _wedged;
        private readonly BlinkDetector _blink = new();
        private readonly GazeEngine _gaze = new();
        private int _busy;
        /// <summary>Bumped by every Stop (and so by panic). A start that began before it never publishes
        /// its camera: it releases it instead. Guarded by <see cref="_gate"/> with the publish.</summary>
        private int _gen;
        private readonly object _gate = new();

        /// <summary>Off the UI thread, as WPF StartWebcamOffUiThreadAsync: model load and camera
        /// negotiation can take seconds.</summary>
        public Task<bool> StartAsync()
        {
            int gen = Volatile.Read(ref _gen);
            Interlocked.Increment(ref _queued);   // counts as starting before the pool picks it up
            return Task.Run(() => { try { return Start(gen); } finally { Interlocked.Decrement(ref _queued); } });
        }
        private int _queued;
        /// <summary>The generation is bumped before the hop, so a start in flight cannot publish its
        /// camera even if it finishes before the queued Stop runs.</summary>
        public Task StopAsync() { lock (_gate) _gen++; return Task.Run(Stop); }

        /// <summary>A Start is between its consent check and publishing (camera open / model load).</summary>
        internal bool IsStarting => Volatile.Read(ref _busy) != 0 || Volatile.Read(ref _queued) != 0;

        public bool Start() => Start(Volatile.Read(ref _gen));

        private bool Start(int gen)
        {
            if (IsRunning) return true;
            if (Interlocked.Exchange(ref _busy, 1) != 0) return false;
            var run = new Run();
            try
            {
                LastError = null;
                StartWasStopped = false;
                if (!WebcamConsent.IsCurrent(CoreSettings.Current)) { LastError = "Webcam consent is not current."; return false; }
                // WPF #743: a loop a timed-out Stop gave up on may still hold the camera.
                if (_wedged is { IsAlive: true })
                {
                    LastError = "The previous camera session is still closing. Try again in a moment, or restart the app.";
                    return false;
                }
                Progress(0.08, "Preparing eye-tracking engine…");
                try { Cv2.GetVersionString(); }
                catch (Exception ex)
                {
                    Log.Warning(ex, "[Webcam] OpenCV native library failed to load");
                    LastError = "Webcam tracking is unavailable: the OpenCV library could not be loaded on this system.";
                    return false;
                }
                // Models before the camera (WPF opens the camera first), so the stages swap places.
                Progress(0.25, "Loading AI models…");
                try
                {
                    run.Face = new BlazeFaceDetector(Path.Combine(ModelDir, "face_detection_short_range.onnx"), Path.Combine(ModelDir, "blazeface_anchors.json"));
                    run.Mesh = new FaceMeshDetector(Path.Combine(ModelDir, "face_landmark.onnx"));
                    run.Iris = new IrisDetector(Path.Combine(ModelDir, "iris_landmark.onnx"));
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "[Webcam] eye-tracking models failed to load");
                    LastError = "Webcam tracking is unavailable: the eye-tracking models could not be loaded.";
                    run.Release();
                    return false;
                }
                Progress(0.55, "Opening camera…");
                run.Source = SourceFactory();
                bool opened;
                try { opened = run.Source.Open(); } catch (Exception ex) { Log.Warning(ex, "[Webcam] open threw"); opened = false; }
                if (!opened)
                {
                    LastError = "No camera could be opened. Check that a webcam is connected and not in use by another app.";
                    run.Release();
                    return false;
                }
                _blink.Reset();
                _gaze.Reset();
                run.Thread = new Thread(() => Loop(run)) { IsBackground = true, Name = "WebcamCapture", Priority = ThreadPriority.BelowNormal };
                bool stale;
                lock (_gate)
                {
                    stale = gen != _gen;
                    if (!stale) _run = run;
                }
                if (stale)
                {
                    LastError = "Webcam tracking was stopped before the camera finished opening.";
                    StartWasStopped = true;
                    run.Release();   // outside _gate: a slow driver close must not block Stop/StopAsync
                    return false;
                }
                Progress(0.92, "Starting capture…");
                run.Thread.Start();
                Progress(1.0, "Ready");
                Log.Information("[Webcam] tracking started");
                return true;
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
                Dispatcher.UIThread.Post(() => StateChanged?.Invoke());
            }
        }

        /// <summary>WPF WebcamTrackingService.RevokeConsent, all four promises: stop everything using the
        /// camera, drop the calibration, clear the consent record, turn the webcam features off. Seeded as
        /// CoreWebcam.RevokeConsentAction; UI thread (it closes the Blink Trainer overlays).</summary>
        internal static void RevokeConsent()
        {
            Views.Overlays.BlinkTrainerSession.Stop();
            Instance.Stop();
            WebcamCalibrationData.DeleteIfExists();   // WPF ClearCalibration (WebcamTrackingService.cs:1112)
            Instance.Calibration = null;
            var s = CoreSettings.Current;
            s.WebcamConsentGiven = false;
            s.WebcamConsentVersion = "";
            s.WebcamConsentDate = null;
            s.WebcamCalibrated = false;
            s.WebcamCalibrationMode = "";
            s.WebcamTriggersEnabled = false;
            s.FocusGameEnabled = false;
            CoreSettings.Save();
        }

        public void Stop()
        {
            Run? run;
            lock (_gate) { _gen++; run = Interlocked.Exchange(ref _run, null); }
            if (run == null) return;
            run.Stop = true;
            // WPF Stop: bounded join; a wedged driver must not hang the caller forever. The loop
            // releases its own camera whenever it does exit; until then Start refuses.
            if (!run.Thread!.Join(TimeSpan.FromSeconds(5)))
            {
                _wedged = run.Thread;
                Log.Warning("[Webcam] capture thread did not exit in 5s; abandoned, it closes the camera when it exits");
            }
            Log.Information("[Webcam] tracking stopped");
            Dispatcher.UIThread.Post(() => StateChanged?.Invoke());
        }

        private void Loop(Run run)
        {
            try
            {
                using var frame = new Mat();
                int fails = 0;
                while (!run.Stop)
                {
                    // Consent revoked mid-session (or during Start): close the camera now.
                    if (!WebcamConsent.IsCurrent(CoreSettings.Current)) { LastError = "Webcam consent was revoked."; break; }
                    if (!run.Source!.Read(frame))
                    {
                        // WPF MaxConsecutiveReadFails: a camera that stopped delivering ends tracking.
                        if (++fails >= 30) { LastError = "The camera stopped delivering frames."; break; }
                        Thread.Sleep(20);
                        continue;
                    }
                    fails = 0;
                    if (run.Stop) break;
                    try { ProcessFrame(run, frame); }
                    catch (Exception ex) { Interlocked.Increment(ref FrameErrors); Log.Warning(ex, "[Webcam] frame processing threw"); Thread.Sleep(50); }
                }
            }
            finally
            {
                run.Release();
                // Ended on its own (read failures, revoke): no longer running.
                if (Interlocked.CompareExchange(ref _run, null, run) == run)
                    Dispatcher.UIThread.Post(() => StateChanged?.Invoke());
            }
        }

        /// <summary>WPF ApplyCalibration: save to the profile file, then use it. False (nothing
        /// changed) when the save failed.</summary>
        public bool ApplyCalibration(WebcamCalibrationData data)
        {
            if (!data.Save()) return false;
            SetCalibrationLive(data);
            Log.Information("[Webcam] calibration applied (mode={Mode})", data.Mode);
            return true;
        }

        /// <summary>WPF SetCalibrationLive: in memory only (calibration's verify phase); null clears.</summary>
        public void SetCalibrationLive(WebcamCalibrationData? data)
        {
            Calibration = data;
            _gaze.ResetSideHysteresis();
        }

        /// <summary>WPF Quick Recal's SetRuntimeOffset: swap the whole calibration (the capture thread
        /// reads it every frame), optionally saving it. Null clears the offset.</summary>
        public void SetRuntimeOffset(RuntimeOffsetData? offset, bool persist)
        {
            lock (_gate)
            {
                if (Calibration is not { } current) return;
                var updated = current.WithRuntimeOffset(offset);
                if (persist) updated.Save();
                Calibration = updated;
            }
            Log.Information("[Webcam] runtime offset {State} (persist={Persist})", offset == null ? "cleared" : "set", persist);
        }

        /// <summary>WPF ProcessFrame without mouth/tongue: face, mesh, face lost/found, head pose,
        /// iris, EAR blink, then the gaze chain (EmitGazeEvents minus lock-on and long stare).</summary>
        private void ProcessFrame(Run run, Mat bgr)
        {
            var found = run.Face!.Detect(bgr);
            if (found is not { } f0) { NoFace(); return; }
            int x = Math.Clamp(f0.X, 0, bgr.Width - 1), y = Math.Clamp(f0.Y, 0, bgr.Height - 1);
            var r = new Rect(x, y, Math.Clamp(f0.Width, 0, bgr.Width - x), Math.Clamp(f0.Height, 0, bgr.Height - y));
            if (r.Width < 16 || r.Height < 16) { NoFace(); return; }
            var lm = run.Mesh!.Detect(bgr, r);
            if (lm == null) { NoFace(); return; }
            if (_gaze.FaceSeen()) Dispatcher.UIThread.Post(() => OnFaceFound?.Invoke());
            if (HeadPose(_gaze, lm, bgr.Width, bgr.Height) is { } pose)
                Dispatcher.UIThread.Post(() => OnHeadPose?.Invoke(pose.Yaw, pose.Pitch));
            var left = run.Iris!.Detect(bgr, lm[FaceMeshDetector.LeftEyeOuterIdx], lm[FaceMeshDetector.LeftEyeInnerIdx], isRightEye: false);
            var right = run.Iris.Detect(bgr, lm[FaceMeshDetector.RightEyeOuterIdx], lm[FaceMeshDetector.RightEyeInnerIdx], isRightEye: true);
            if (left == null && right == null) return;
            if (left != null && right != null)
            {
                var ev = _blink.Update(
                    BlinkDetector.ComputeEar(left.Contour, BlinkDetector.IrisContourEarIndices),
                    BlinkDetector.ComputeEar(right.Contour, BlinkDetector.IrisContourEarIndices), DateTime.UtcNow);
                if (ev == BlinkEvent.Blink) Dispatcher.UIThread.Post(() => OnBlink?.Invoke());
                else if (ev == BlinkEvent.EyesClosedLong) Dispatcher.UIThread.Post(() => OnEyesClosedLong?.Invoke());
            }
            (double Dx, double Dy)? vl = left == null ? null
                : _gaze.NormalizeIris(left.IrisCenter, lm[FaceMeshDetector.LeftEyeOuterIdx], lm[FaceMeshDetector.LeftEyeInnerIdx], rightEye: false);
            (double Dx, double Dy)? vr = right == null ? null
                : _gaze.NormalizeIris(right.IrisCenter, lm[FaceMeshDetector.RightEyeOuterIdx], lm[FaceMeshDetector.RightEyeInnerIdx], rightEye: true);
            if (_gaze.CombineEyes(vl, vr) is not { } v || _blink.EyesClosed) return;   // eyes closed: hold, as WPF

            var (dx, dy) = _gaze.PreFilter(v.Dx, v.Dy);
            Dispatcher.UIThread.Post(() => OnRawIris?.Invoke(dx, dy));
            var mapped = _gaze.Step(Calibration, dx, dy, GazeEngine.Now(), out var side);
            Dispatcher.UIThread.Post(() => OnGazeSide?.Invoke(side));
            if (mapped is not { } m) return;
            var p = _gaze.Follow(m.X, m.Y);
            Dispatcher.UIThread.Post(() => OnGazeMove?.Invoke(new ScreenPoint(p.X, p.Y)));
        }

        /// <summary>WPF UpdateHeadPose: solvePnP here (OpenCvSharp 4.13 takes SolvePnPMethod, see
        /// GazeEngine), model, Euler extraction and smoothing in Core.</summary>
        internal static (double Yaw, double Pitch)? HeadPose(GazeEngine gaze, float[][] lm, int w, int h)
        {
            try
            {
                var pts = gaze.HeadPoseImagePoints(lm);
                if (pts == null) return null;
                using var cam = new Mat(3, 3, MatType.CV_64FC1);
                var k = GazeEngine.HeadPoseCameraMatrix(w, h);
                for (int i = 0; i < 9; i++) cam.Set(i / 3, i % 3, k[i]);
                using var dist = new Mat(4, 1, MatType.CV_64FC1, Scalar.All(0));
                using var obj = InputArray.Create(GazeEngine.HeadPoseModelPoints);
                using var img = InputArray.Create(pts);
                using var rvec = new Mat();
                using var tvec = new Mat();
                Cv2.SolvePnP(obj, img, cam, dist, rvec, tvec, useExtrinsicGuess: false, flags: SolvePnPMethod.Iterative);
                if (rvec.Empty() || rvec.Total() < 3) { gaze.HeadPoseFailed(); return null; }
                using var rot = new Mat();
                Cv2.Rodrigues(rvec, rot);
                return gaze.UpdateHeadPose(rot.At<double>(2, 0), rot.At<double>(2, 1), rot.At<double>(2, 2));
            }
            catch (Exception ex) { gaze.HeadPoseFailed(); Log.Debug("[Webcam] head pose failed: {Error}", ex.Message); return null; }
        }

        private void NoFace()
        {
            if (_gaze.FaceMissing()) Dispatcher.UIThread.Post(() => OnFaceLost?.Invoke());
            _blink.CancelClosure();
        }
    }
}
