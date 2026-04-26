using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Newtonsoft.Json;
using OpenCvSharp;
using Serilog;

// Disambiguate WPF System.Windows types from OpenCvSharp types.
using CvPoint = OpenCvSharp.Point;
using CvRect = OpenCvSharp.Rect;
using CvSize = OpenCvSharp.Size;

namespace ConditioningControlPanel.Services
{
    // ─────────────────────────────────────────────────────────────────────────────
    //  PRIVACY CONTRACT — read before editing this file
    // ─────────────────────────────────────────────────────────────────────────────
    //  This service must NEVER:
    //    • Write a frame, image, or any per-frame derived array to disk.
    //    • Send a frame, image, or any per-frame derived array over the network.
    //    • Log per-frame numbers (gaze X/Y, eye-state, etc.) — only state
    //      strings and counts.
    //    • Open audio capture (VideoCapture is video-only by API contract).
    //    • Persist anything beyond the calibration JSON (numbers only, see
    //      WebcamCalibrationData).
    //
    //  Any change that broadens what the camera observes (new sensor type, new
    //  stored value, new outbound data) MUST bump WebcamTrackingService.ConsentVersion
    //  so users re-consent on next launch.
    //
    //  Frames live in RAM, get processed, get disposed. That is the whole story.
    // ─────────────────────────────────────────────────────────────────────────────
    //
    //  Detection pipeline (current — full MediaPipe ONNX, post phase-4 pivot):
    //    Resources/Models/face_detection_short_range.onnx  — BlazeFace (MediaPipe)
    //    Resources/Models/blazeface_anchors.json           — precomputed 896 SSD anchors
    //    Resources/Models/face_landmark.onnx               — FaceMesh 468 landmarks (MediaPipe)
    //    Resources/Models/iris_landmark.onnx               — Iris 71 eye contour + 5 iris pts (MediaPipe)
    //    All shipped in the installer. No internet at runtime.
    //
    //    Face   → BlazeFace ONNX, top-1 above 0.5 sigmoid score, mapped back
    //             to source-frame pixel coords through letterbox padding.
    //    Mesh   → FaceMesh ONNX on a 1.5×-expanded SquareLong face crop, returning
    //             468 landmarks in source-frame pixel coords.
    //    Blink  → EAR (Eye Aspect Ratio) averaged across both eyes from FaceMesh
    //             6-point eyelid indices. Rolling 90-frame max baseline, hysteresis
    //             (closed<0.75×base, open>0.85×base), closed→open transition with
    //             30 ms–1.5 s window absorbs both fast natural blinks and slow
    //             deliberate ones. Periodic diagnostic log every ~3s.
    //    Iris   → Iris model on 64×64 eye crops (right eye flipped before
    //             inference, output un-flipped). Iris-center landmark gives
    //             a precise gaze vector — replaces darkest-pixel heuristic.
    //    Mouth-open: DEFERRED to v2.
    // ─────────────────────────────────────────────────────────────────────────────

    public enum GazeSide { Left, Right, Center }

    public enum WebcamTrackingState
    {
        Stopped,
        Starting,
        Tracking,
        FaceLost,
        CameraInUse,
        CameraDenied,
        Error
    }

    /// <summary>
    /// Local, offline webcam-based eye and gaze tracking. Powers Lab Box 1
    /// (Webcam Triggers) and Lab Box 2 (Focus Training). Owns the only
    /// VideoCapture handle in the application.
    /// </summary>
    public class WebcamTrackingService : IDisposable
    {
        /// <summary>
        /// Bumped any time we add a new sensor type, broaden what the camera
        /// observes, or change what numbers are stored. On bump, the consent
        /// dialog re-runs from screen 1 for every existing user.
        /// </summary>
        public const string ConsentVersion = "1.0";

        // Capture parameters
        private const int CaptureWidth = 640;
        private const int CaptureHeight = 480;
        private const int TargetFps = 30;
        private const int MaxConsecutiveReadFails = 30;            // ~1s at 30fps
        private const int FaceLostFramesThreshold = 15;            // ~0.5s

        // EAR-based blink detection. Standard 6-point EAR (Soukupová & Čech 2016)
        // averaged across both eyes (avoids per-eye asymmetry shrinking the
        // both-closed detection window to nothing). Rolling 90-frame max baseline.
        // Window is generous: real blinks are ~100ms but the calibration prompt
        // explicitly tells users to "blink slowly and deliberately" so we accept
        // up to 1.5s as a single blink — anything longer is treated as a stare.
        private const int EarBaselineFrames = 90;            // ~3s at 30fps — rolling max window
        private const int EarMinSamplesForBaseline = 15;     // need this many before any blink fires
        private const double EarClosedRatio = 0.80;          // EAR < 0.80 × baseline → enter closed
        private const double EarOpenRatio = 0.88;            // EAR > 0.88 × baseline → leave closed (hysteresis gap)
        private const double EarNearMissRatio = 0.90;        // dips below 0.90×base log as near-miss for tuning
        private const int MinBlinkClosedMs = 60;             // shorter than this is noise (filters 1-frame jitter)
        private const int MaxBlinkClosedMs = 1500;           // longer than this is a stare/squint, not a blink
        private const int BlinkCooldownMs = 500;             // gap required between consecutive blink fires
        private const int BlinkDiagLogIntervalMs = 3000;     // log EAR baseline + blink count every ~3s

        // Standard 6-point EAR landmark indices for MediaPipe FaceMesh
        // (P1=outer, P2/P3=upper eyelid, P4=inner, P5/P6=lower eyelid)
        private static readonly int[] LeftEarIndices  = { 33, 160, 158, 133, 153, 144 };
        private static readonly int[] RightEarIndices = { 263, 387, 385, 362, 380, 373 };
        // Eye-box bounding-box landmarks (outer/inner corners + upper/lower eyelid extremes)
        private static readonly int[] LeftEyeBoxIndices  = { 33, 133, 159, 145, 158, 153 };  // 33 outer, 133 inner, 159 top, 145 bottom
        private static readonly int[] RightEyeBoxIndices = { 263, 362, 386, 374, 385, 380 }; // 263 outer, 362 inner, 386 top, 374 bottom

        // Gaze parameters
        private const int GazeBufferSize = 30;
        private const int LongStareDurationMs = 3000;
        private const double LongStareMaxDeviationPx = 60.0;
        private const int LongStareCooldownMs = 5000;
        private const int IrisSmoothFrames = 5;              // rolling-mean window over raw iris vector
        private const int SideStabilityFrames = 3;           // consecutive frames of same classification before emitting (filters Center pass-through)

        private readonly object _stateLock = new();
        private bool _disposed;

        public WebcamTrackingState State { get; private set; } = WebcamTrackingState.Stopped;
        public bool IsRunning => State == WebcamTrackingState.Tracking || State == WebcamTrackingState.FaceLost;
        public WebcamCalibrationData? Calibration { get; private set; }

        public event Action? OnBlink;
        public event Action<System.Windows.Point>? OnLongStare;
        public event Action? OnMouthOpen;          // reserved for v2
        public event Action<System.Windows.Point>? OnGazeMove;
        public event Action<GazeSide>? OnGazeSide;
        public event Action? OnFaceLost;
        public event Action? OnFaceFound;
        public event Action<WebcamTrackingState>? OnTrackingStateChanged;

        /// <summary>
        /// Raw iris vector (averaged across both eyes) in eye-region-relative
        /// coordinates, roughly in [-0.5, +0.5]. Fired every processed frame
        /// when a face is found. Used by the calibration window to sample
        /// reference points; normal feature code should use OnGazeSide /
        /// OnGazeMove (which apply calibration) instead.
        /// </summary>
        public event Action<double, double>? OnRawIris;

        // FaceMesh corner indices used for iris ROI + iris-vector reference frame
        // (left = subject's left eye, on the right side of an unmirrored frame)
        private const int LeftEyeOuterIdx = 33,  LeftEyeInnerIdx = 133;
        private const int RightEyeOuterIdx = 263, RightEyeInnerIdx = 362;

        // Capture-thread state
        private VideoCapture? _capture;
        private BlazeFaceDetector? _faceDetector;
        private FaceMeshDetector? _faceMesh;
        private IrisDetector? _irisDetector;
        private Thread? _captureThread;
        private volatile bool _stopRequested;

        // Heuristic state (capture-thread only)
        private DateTime _lastBlinkAt = DateTime.MinValue;
        private readonly Queue<double> _earBuffer = new();       // rolling avg-EAR samples for baseline
        private double _earBaseline;                             // rolling 90-frame max of avg EAR
        private bool _eyesClosed;                                // hysteresis state for both eyes (single)
        private DateTime? _eyesClosedAt;                         // start of current closed window
        private double _minEarThisClosure;                       // tracks deepest closure during current closed window
        private double _windowMinEar = double.MaxValue;          // min EAR seen since last diag log (for tuning)
        private double _windowMaxEar = double.MinValue;          // max EAR seen since last diag log
        private int _windowNearMissCount;                        // count of frames where EAR < 0.90×baseline but we didn't trigger closed
        private DateTime _lastBlinkDiagAt = DateTime.MinValue;
        private int _blinkCount;                                 // total blinks fired since service start
        // Gaze-side stability filter — require N consecutive frames of the same
        // classification before emitting, so that transient passes through Center
        // during a Left↔Right movement don't fire spurious Center events.
        private GazeSide _lastEmittedSide = GazeSide.Center;
        private GazeSide _pendingSide = GazeSide.Center;
        private int _pendingSideStreak;
        private bool _faceWasFound;
        private int _consecutiveNoFaceFrames;
        private DateTime _lastLongStareAt = DateTime.MinValue;
        private readonly Queue<(DateTime Time, System.Windows.Point ScreenPoint)> _gazeBuffer = new();
        private CvRect? _lastFaceRect;
        private CvRect? _lastLeftEyeRect;
        private CvRect? _lastRightEyeRect;

        // Iris smoothing — rolling mean over raw irisDx/dy. Blunts the per-frame
        // jitter from Haar eye-box wobble that was causing Gaze side to flicker
        // Left↔Right around the classifier midpoint.
        private readonly Queue<double> _irisDxSmoothBuffer = new();
        private readonly Queue<double> _irisDySmoothBuffer = new();

        // Hysteresis state for gaze-side classification — keeps the side from
        // toggling on tiny crossings of the midpoint band.
        private GazeSide _lastGazeSide = GazeSide.Center;

        public WebcamTrackingService()
        {
            App.Logger?.Information("WebcamTrackingService: constructed");
            Calibration = WebcamCalibrationData.Load();
        }

        /// <summary>
        /// Open the webcam handle and begin the capture/inference loop.
        /// Returns false if consent has not been given, the cascade XMLs are
        /// missing, the camera is in use by another app, or the OS has denied
        /// access.
        /// </summary>
        public bool Start()
        {
            lock (_stateLock)
            {
                if (_disposed) return false;
                if (App.Settings?.Current?.WebcamConsentGiven != true)
                {
                    App.Logger?.Information("WebcamTrackingService: Start() refused — consent not given");
                    return false;
                }
                if (IsRunning) return true;

                SetState(WebcamTrackingState.Starting);

                var paths = ResolveModelPaths();
                if (paths.FaceModel == null || paths.FaceAnchors == null || paths.MeshModel == null || paths.IrisModel == null)
                {
                    App.Logger?.Warning("WebcamTrackingService: model files missing in Resources/Models/ (face_detection_short_range.onnx, blazeface_anchors.json, face_landmark.onnx, iris_landmark.onnx)");
                    SetState(WebcamTrackingState.Error);
                    return false;
                }

                if (!TryOpenCamera())
                {
                    return false; // state already set
                }

                if (!TryLoadModels(paths.FaceModel, paths.FaceAnchors, paths.MeshModel, paths.IrisModel))
                {
                    ReleaseCapture();
                    SetState(WebcamTrackingState.Error);
                    return false;
                }

                _stopRequested = false;
                _captureThread = new Thread(CaptureLoop)
                {
                    IsBackground = true,
                    Name = "WebcamCapture",
                    Priority = ThreadPriority.BelowNormal
                };
                _captureThread.Start();

                SetState(WebcamTrackingState.Tracking);
                App.Logger?.Information("WebcamTrackingService: capture started ({W}x{H}, target {Fps} fps)",
                    CaptureWidth, CaptureHeight, TargetFps);
                return true;
            }
        }

        public void Stop()
        {
            Thread? thread;
            lock (_stateLock)
            {
                if (!IsRunning && State != WebcamTrackingState.Starting) return;
                _stopRequested = true;
                thread = _captureThread;
            }

            if (thread != null && !thread.Join(TimeSpan.FromSeconds(2)))
            {
                App.Logger?.Warning("WebcamTrackingService: capture thread did not exit within 2s");
            }

            lock (_stateLock)
            {
                _captureThread = null;
                ReleaseModels();
                ReleaseCapture();
                ResetHeuristicState();
                SetState(WebcamTrackingState.Stopped);
                App.Logger?.Information("WebcamTrackingService: stopped");
            }
        }

        public void ApplyCalibration(WebcamCalibrationData data)
        {
            data.Save();
            Calibration = data;
            _lastGazeSide = GazeSide.Center;
            App.Logger?.Information("WebcamTrackingService: calibration applied (mode={Mode})", data.Mode);
        }

        /// <summary>
        /// Apply a candidate calibration in-memory only — no disk write. Used by
        /// the calibration window during the validation phase, where we need
        /// live classification to reflect the new fit but don't want to persist
        /// until the user has demonstrated the calibration actually works.
        /// Pass null to revert to no calibration in memory.
        /// </summary>
        public void SetCalibrationLive(WebcamCalibrationData? data)
        {
            Calibration = data;
            _lastGazeSide = GazeSide.Center;
        }

        public void ClearCalibration()
        {
            WebcamCalibrationData.DeleteIfExists();
            Calibration = null;
            App.Logger?.Information("WebcamTrackingService: calibration cleared");
        }

        public void RevokeConsent()
        {
            Stop();
            ClearCalibration();

            var s = App.Settings?.Current;
            if (s != null)
            {
                s.WebcamConsentGiven = false;
                s.WebcamConsentVersion = "";
                s.WebcamConsentDate = null;
                s.WebcamCalibrated = false;
                s.WebcamCalibrationMode = "";
                s.WebcamTriggersEnabled = false;
                s.FocusGameEnabled = false;
                App.Settings?.Save();
            }

            App.Logger?.Information("WebcamTrackingService: consent revoked at {Time}", DateTime.UtcNow);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { Stop(); } catch (Exception ex) { App.Logger?.Warning(ex, "WebcamTrackingService.Dispose: Stop() threw"); }
            App.Logger?.Information("WebcamTrackingService: disposed");
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  Setup helpers
        // ─────────────────────────────────────────────────────────────────────────

        private static (string? FaceModel, string? FaceAnchors, string? MeshModel, string? IrisModel) ResolveModelPaths()
        {
            try
            {
                var baseDir = AppContext.BaseDirectory;
                var faceModel = Path.Combine(baseDir, "Resources", "Models", "face_detection_short_range.onnx");
                var faceAnchors = Path.Combine(baseDir, "Resources", "Models", "blazeface_anchors.json");
                var meshModel = Path.Combine(baseDir, "Resources", "Models", "face_landmark.onnx");
                var irisModel = Path.Combine(baseDir, "Resources", "Models", "iris_landmark.onnx");
                return (
                    File.Exists(faceModel) ? faceModel : null,
                    File.Exists(faceAnchors) ? faceAnchors : null,
                    File.Exists(meshModel) ? meshModel : null,
                    File.Exists(irisModel) ? irisModel : null);
            }
            catch
            {
                return (null, null, null, null);
            }
        }

        private bool TryOpenCamera()
        {
            try
            {
                var cap = new VideoCapture(0, VideoCaptureAPIs.MSMF);
                if (!cap.IsOpened())
                {
                    cap.Dispose();
                    cap = new VideoCapture(0, VideoCaptureAPIs.DSHOW);
                    if (!cap.IsOpened())
                    {
                        cap.Dispose();
                        App.Logger?.Warning("WebcamTrackingService: VideoCapture.Open returned false on both MSMF and DSHOW backends");
                        SetState(WebcamTrackingState.CameraDenied);
                        return false;
                    }
                }

                cap.Set(VideoCaptureProperties.FrameWidth, CaptureWidth);
                cap.Set(VideoCaptureProperties.FrameHeight, CaptureHeight);
                cap.Set(VideoCaptureProperties.Fps, TargetFps);
                cap.Set(VideoCaptureProperties.BufferSize, 1);

                using var probe = new Mat();
                if (!cap.Read(probe) || probe.Empty())
                {
                    cap.Dispose();
                    App.Logger?.Warning("WebcamTrackingService: probe frame failed -- camera may be in use by another app");
                    SetState(WebcamTrackingState.CameraInUse);
                    return false;
                }

                _capture = cap;
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "WebcamTrackingService: TryOpenCamera threw");
                SetState(WebcamTrackingState.Error);
                return false;
            }
        }

        private bool TryLoadModels(string faceModelPath, string faceAnchorsPath, string meshModelPath, string irisModelPath)
        {
            try
            {
                _faceDetector = new BlazeFaceDetector(faceModelPath, faceAnchorsPath);
                _faceMesh = new FaceMeshDetector(meshModelPath);
                _irisDetector = new IrisDetector(irisModelPath);
                App.Logger?.Information("WebcamTrackingService: BlazeFace + FaceMesh + Iris loaded");
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "WebcamTrackingService: failed to load detection models");
                return false;
            }
        }

        private void ReleaseCapture()
        {
            try { _capture?.Release(); } catch { }
            try { _capture?.Dispose(); } catch { }
            _capture = null;
        }

        private void ReleaseModels()
        {
            try { _faceDetector?.Dispose(); } catch { }
            try { _faceMesh?.Dispose(); } catch { }
            try { _irisDetector?.Dispose(); } catch { }
            _faceDetector = null;
            _faceMesh = null;
            _irisDetector = null;
        }

        private void ResetHeuristicState()
        {
            _gazeBuffer.Clear();
            _faceWasFound = false;
            _consecutiveNoFaceFrames = 0;
            _lastFaceRect = null;
            _lastLeftEyeRect = null;
            _lastRightEyeRect = null;
            _earBuffer.Clear();
            _earBaseline = 0;
            _eyesClosed = false;
            _eyesClosedAt = null;
            _minEarThisClosure = double.MaxValue;
            _windowMinEar = double.MaxValue;
            _windowMaxEar = double.MinValue;
            _windowNearMissCount = 0;
            _lastBlinkDiagAt = DateTime.MinValue;
            _blinkCount = 0;
            _irisDxSmoothBuffer.Clear();
            _irisDySmoothBuffer.Clear();
            _lastGazeSide = GazeSide.Center;
            _lastEmittedSide = GazeSide.Center;
            _pendingSide = GazeSide.Center;
            _pendingSideStreak = 0;
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  Capture loop
        // ─────────────────────────────────────────────────────────────────────────

        private void CaptureLoop()
        {
            App.Logger?.Information("WebcamTrackingService: capture loop entered");
            int consecutiveReadFails = 0;
            using var frame = new Mat();
            using var gray = new Mat();
            var sw = new Stopwatch();
            int processedFrames = 0;
            sw.Start();

            try
            {
                while (!_stopRequested)
                {
                    if (_capture == null || _faceDetector == null || _faceMesh == null || _irisDetector == null) break;

                    if (!_capture.Read(frame) || frame.Empty())
                    {
                        consecutiveReadFails++;
                        if (consecutiveReadFails >= MaxConsecutiveReadFails)
                        {
                            App.Logger?.Warning("WebcamTrackingService: {N} consecutive read failures -- stopping", consecutiveReadFails);
                            SetState(WebcamTrackingState.Error);
                            break;
                        }
                        Thread.Sleep(20);
                        continue;
                    }
                    consecutiveReadFails = 0;

                    try
                    {
                        Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);
                        Cv2.EqualizeHist(gray, gray);
                        ProcessFrame(frame, gray);
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Warning(ex, "WebcamTrackingService: ProcessFrame threw");
                        Thread.Sleep(50);
                    }

                    processedFrames++;
                    if (processedFrames % 300 == 0)
                    {
                        var fps = processedFrames / sw.Elapsed.TotalSeconds;
                        App.Logger?.Information("WebcamTrackingService: {Frames} frames processed, ~{Fps:F1} fps", processedFrames, fps);
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "WebcamTrackingService: capture loop terminated by exception");
                SetState(WebcamTrackingState.Error);
            }
            finally
            {
                App.Logger?.Information("WebcamTrackingService: capture loop exited after {Frames} frames", processedFrames);
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  Per-frame processing (capture thread)
        // ─────────────────────────────────────────────────────────────────────────

        private void ProcessFrame(Mat bgr, Mat gray)
        {
            // 1) BlazeFace face detection
            var faceRectOrNull = _faceDetector!.Detect(bgr);
            if (faceRectOrNull == null)
            {
                HandleNoFace();
                return;
            }
            var faceRect = ClipRect(faceRectOrNull.Value.X, faceRectOrNull.Value.Y,
                                    faceRectOrNull.Value.Width, faceRectOrNull.Value.Height,
                                    gray.Width, gray.Height);
            if (faceRect.Width < 16 || faceRect.Height < 16)
            {
                HandleNoFace();
                return;
            }
            _lastFaceRect = faceRect;

            // 2) FaceMesh — 468 landmarks in source-frame pixel coords
            var landmarks = _faceMesh!.Detect(bgr, faceRect);
            if (landmarks == null)
            {
                HandleNoFace();
                return;
            }

            // 3) Eye boxes for diagnostics/visualization (not used for iris now)
            _lastLeftEyeRect = EyeBoxFromLandmarks(landmarks, LeftEyeBoxIndices, gray.Width, gray.Height);
            _lastRightEyeRect = EyeBoxFromLandmarks(landmarks, RightEyeBoxIndices, gray.Width, gray.Height);

            // 4) EAR-based blink detection
            double earL = ComputeEAR(landmarks, LeftEarIndices);
            double earR = ComputeEAR(landmarks, RightEarIndices);
            UpdateBlinkState(earL, earR);

            HandleFaceFound();

            // 5) Iris model — exact iris-center landmark per eye, normalized
            //    against eye-corner midpoint to give a head-pose-stable gaze
            //    vector. Right eye is fed flipped, output un-flipped, by the
            //    detector. Eye-corner-to-corner distance defines the unit so
            //    the vector is roughly invariant to face-to-camera distance.
            var leftIrisCenter  = _irisDetector!.DetectIrisCenter(bgr, landmarks[LeftEyeOuterIdx],  landmarks[LeftEyeInnerIdx],  isRightEye: false);
            var rightIrisCenter = _irisDetector!.DetectIrisCenter(bgr, landmarks[RightEyeOuterIdx], landmarks[RightEyeInnerIdx], isRightEye: true);
            if (leftIrisCenter == null && rightIrisCenter == null) return;

            double sumDx = 0, sumDy = 0;
            int count = 0;
            if (leftIrisCenter != null)
            {
                var v = NormalizeIrisVector(leftIrisCenter.Value, landmarks[LeftEyeOuterIdx], landmarks[LeftEyeInnerIdx]);
                sumDx += v.Dx; sumDy += v.Dy; count++;
            }
            if (rightIrisCenter != null)
            {
                var v = NormalizeIrisVector(rightIrisCenter.Value, landmarks[RightEyeOuterIdx], landmarks[RightEyeInnerIdx]);
                sumDx += v.Dx; sumDy += v.Dy; count++;
            }
            EmitGazeEvents(sumDx / count, sumDy / count);
        }

        /// <summary>
        /// Convert an iris-center pixel position into a head-pose-stable iris
        /// vector relative to the eye-corner midpoint, scaled by corner-to-corner
        /// distance. Output is roughly in [-0.5, +0.5] for normal gaze ranges.
        /// </summary>
        private static (double Dx, double Dy) NormalizeIrisVector((double X, double Y) iris, float[] outerCorner, float[] innerCorner)
        {
            double cx = (outerCorner[0] + innerCorner[0]) / 2.0;
            double cy = (outerCorner[1] + innerCorner[1]) / 2.0;
            double w = Math.Sqrt(
                (outerCorner[0] - innerCorner[0]) * (outerCorner[0] - innerCorner[0]) +
                (outerCorner[1] - innerCorner[1]) * (outerCorner[1] - innerCorner[1]));
            if (w < 1.0) return (0, 0);
            return ((iris.X - cx) / w, (iris.Y - cy) / w);
        }

        private void HandleNoFace()
        {
            _consecutiveNoFaceFrames++;
            if (_faceWasFound && _consecutiveNoFaceFrames >= FaceLostFramesThreshold)
            {
                _faceWasFound = false;
                SetState(WebcamTrackingState.FaceLost);
                Dispatch(() => OnFaceLost?.Invoke());
            }
            // Drop cached eye rects so gaze code can't fire on stale data while
            // the face is missing. (Hand-over-camera test that fired a ghost
            // blink was caused by stale rects — keep this clear.) Also drop
            // mid-blink state so a face-loss can't be misread as a giant blink.
            _lastLeftEyeRect = null;
            _lastRightEyeRect = null;
            _eyesClosed = false;
            _eyesClosedAt = null;
            if (_consecutiveNoFaceFrames > FaceLostFramesThreshold * 2)
            {
                _gazeBuffer.Clear();
                _lastFaceRect = null;
            }
        }

        private void HandleFaceFound()
        {
            _consecutiveNoFaceFrames = 0;
            if (!_faceWasFound)
            {
                _faceWasFound = true;
                if (State == WebcamTrackingState.FaceLost)
                {
                    SetState(WebcamTrackingState.Tracking);
                }
                Dispatch(() => OnFaceFound?.Invoke());
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  Gaze-event emission (consumes iris vectors from the iris model)
        // ─────────────────────────────────────────────────────────────────────────

        private void EmitGazeEvents(double irisDx, double irisDy)
        {
            // Raw iris vector — used by the calibration window to sample reference
            // points. Always fires; calibration consumers expect every frame.
            Dispatch(() => OnRawIris?.Invoke(irisDx, irisDy));

            // Smoothed iris vector — used by live classification. Per-frame iris
            // estimates wobble by ~5% because Haar's eye box jitters; without
            // smoothing, gaze-side flips Left↔Right every other frame whenever
            // the user's actual gaze sits anywhere near the midpoint.
            EnqueueWithCap(_irisDxSmoothBuffer, irisDx, IrisSmoothFrames);
            EnqueueWithCap(_irisDySmoothBuffer, irisDy, IrisSmoothFrames);
            double sumX = 0, sumY = 0;
            foreach (var v in _irisDxSmoothBuffer) sumX += v;
            foreach (var v in _irisDySmoothBuffer) sumY += v;
            var smoothDx = sumX / _irisDxSmoothBuffer.Count;
            var smoothDy = sumY / _irisDySmoothBuffer.Count;

            var classifiedSide = ClassifyGazeSide(smoothDx);

            // Stability filter: only switch the *confirmed* side after the new
            // classification has held for SideStabilityFrames consecutive frames.
            // We still emit the confirmed side EVERY frame so consumers (like the
            // calibration validation gate's hold timer) get continuous polling —
            // they need a per-frame event to advance their elapsed-time check.
            // Without this stability gate, a fast Left→Right movement passes
            // through the Center band for a frame or two and the gate's elapsed
            // timer resets mid-hold.
            if (classifiedSide == _lastEmittedSide)
            {
                _pendingSide = classifiedSide;
                _pendingSideStreak = 0;
            }
            else if (classifiedSide == _pendingSide)
            {
                _pendingSideStreak++;
                if (_pendingSideStreak >= SideStabilityFrames)
                {
                    _lastEmittedSide = _pendingSide;
                    _pendingSideStreak = 0;
                }
            }
            else
            {
                _pendingSide = classifiedSide;
                _pendingSideStreak = 1;
            }
            var emit = _lastEmittedSide;
            Dispatch(() => OnGazeSide?.Invoke(emit));

            var screenPoint = ProjectGazeToScreen(smoothDx, smoothDy);
            if (screenPoint.HasValue)
            {
                var p = screenPoint.Value;
                Dispatch(() => OnGazeMove?.Invoke(p));
                UpdateLongStareHeuristic(p);
            }
        }

        private GazeSide ClassifyGazeSide(double irisDx)
        {
            if (Calibration?.LeftRefVec is double[] left && Calibration?.RightRefVec is double[] right
                && left.Length >= 1 && right.Length >= 1)
            {
                var leftRef = left[0];
                var rightRef = right[0];
                var midpoint = (leftRef + rightRef) / 2.0;
                var spread = Math.Abs(leftRef - rightRef);
                if (spread < 1e-6) return GazeSide.Center;

                // Direction sign: which way along irisDx is "looking-left."
                // If leftRef < rightRef, then smaller irisDx = looking left,
                // so we negate to make `towardLeft` consistent (positive = left).
                var towardLeft = leftRef < rightRef ? (midpoint - irisDx) : (irisDx - midpoint);

                // Asymmetric thresholds for hysteresis:
                //   enterBand: how far past midpoint to FIRST enter a side (~17.5% of spread)
                //   leaveBand: how close to midpoint before LEAVING a side (~7.5% of spread)
                // The gap between the two suppresses flicker on small jitter.
                var enterBand = spread * 0.175;
                var leaveBand = spread * 0.075;

                switch (_lastGazeSide)
                {
                    case GazeSide.Left:
                        if (towardLeft < -enterBand) _lastGazeSide = GazeSide.Right;
                        else if (towardLeft < leaveBand) _lastGazeSide = GazeSide.Center;
                        break;
                    case GazeSide.Right:
                        if (towardLeft > enterBand) _lastGazeSide = GazeSide.Left;
                        else if (towardLeft > -leaveBand) _lastGazeSide = GazeSide.Center;
                        break;
                    case GazeSide.Center:
                    default:
                        if (towardLeft > enterBand) _lastGazeSide = GazeSide.Left;
                        else if (towardLeft < -enterBand) _lastGazeSide = GazeSide.Right;
                        break;
                }
                return _lastGazeSide;
            }

            // Uncalibrated fallback (raw thresholds; hysteresis ignored — calibrate
            // for a real experience).
            if (irisDx < -0.10) return GazeSide.Left;
            if (irisDx > 0.10) return GazeSide.Right;
            return GazeSide.Center;
        }

        private System.Windows.Point? ProjectGazeToScreen(double irisDx, double irisDy)
        {
            var h = Calibration?.Homography;
            if (h == null || h.Length != 3 || h[0].Length != 3) return null;

            var x = h[0][0] * irisDx + h[0][1] * irisDy + h[0][2];
            var y = h[1][0] * irisDx + h[1][1] * irisDy + h[1][2];
            var w = h[2][0] * irisDx + h[2][1] * irisDy + h[2][2];
            if (Math.Abs(w) < 1e-9) return null;

            return new System.Windows.Point(x / w, y / w);
        }

        private void UpdateLongStareHeuristic(System.Windows.Point point)
        {
            var now = DateTime.UtcNow;
            _gazeBuffer.Enqueue((now, point));
            while (_gazeBuffer.Count > GazeBufferSize) _gazeBuffer.Dequeue();
            if (_gazeBuffer.Count < 5) return;

            var oldest = _gazeBuffer.Peek();
            if ((now - oldest.Time).TotalMilliseconds < LongStareDurationMs) return;
            if ((now - _lastLongStareAt).TotalMilliseconds < LongStareCooldownMs) return;

            double cx = 0, cy = 0;
            foreach (var s in _gazeBuffer) { cx += s.ScreenPoint.X; cy += s.ScreenPoint.Y; }
            cx /= _gazeBuffer.Count;
            cy /= _gazeBuffer.Count;

            foreach (var s in _gazeBuffer)
            {
                var ddx = s.ScreenPoint.X - cx;
                var ddy = s.ScreenPoint.Y - cy;
                if ((ddx * ddx + ddy * ddy) > LongStareMaxDeviationPx * LongStareMaxDeviationPx) return;
            }

            _lastLongStareAt = now;
            var stareCenter = new System.Windows.Point(cx, cy);
            Dispatch(() => OnLongStare?.Invoke(stareCenter));
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  Helpers
        // ─────────────────────────────────────────────────────────────────────────

        private static CvRect OffsetRect(CvRect r, int dx, int dy)
        {
            return new CvRect(r.X + dx, r.Y + dy, r.Width, r.Height);
        }

        private static CvRect ClipRect(int x, int y, int w, int h, int maxW, int maxH)
        {
            x = Math.Max(0, Math.Min(x, maxW - 1));
            y = Math.Max(0, Math.Min(y, maxH - 1));
            w = Math.Max(0, Math.Min(w, maxW - x));
            h = Math.Max(0, Math.Min(h, maxH - y));
            return new CvRect(x, y, w, h);
        }

        private static void EnqueueWithCap(Queue<double> q, double value, int cap)
        {
            q.Enqueue(value);
            while (q.Count > cap) q.Dequeue();
        }

        private void SetState(WebcamTrackingState state)
        {
            if (State == state) return;
            State = state;
            Dispatch(() => OnTrackingStateChanged?.Invoke(state));
        }

        private static void Dispatch(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.HasShutdownStarted)
            {
                dispatcher.BeginInvoke(action);
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  EAR (Eye Aspect Ratio) blink detection — see Soukupová & Čech 2016.
        //  Per-eye rolling 90-frame max baseline (closed frames can't bias it
        //  down because max naturally rejects low values). Per-eye hysteresis:
        //  enter "closed" at <0.7×baseline, leave at >0.85×baseline. Blink fires
        //  on the both-eyes closed→open transition when the closed window was
        //  50–400 ms long, with a 700 ms cooldown.
        // ─────────────────────────────────────────────────────────────────────────
        private static double ComputeEAR(float[][] landmarks, int[] idx)
        {
            // Standard 6-point EAR: (||p2-p6|| + ||p3-p5||) / (2 * ||p1-p4||)
            var p1 = landmarks[idx[0]];
            var p2 = landmarks[idx[1]];
            var p3 = landmarks[idx[2]];
            var p4 = landmarks[idx[3]];
            var p5 = landmarks[idx[4]];
            var p6 = landmarks[idx[5]];
            double a = Distance(p2, p6);
            double b = Distance(p3, p5);
            double c = Distance(p1, p4);
            return c > 1e-6 ? (a + b) / (2.0 * c) : 0.0;
        }

        private static double Distance(float[] a, float[] b)
        {
            double dx = a[0] - b[0];
            double dy = a[1] - b[1];
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static CvRect? EyeBoxFromLandmarks(float[][] landmarks, int[] indices, int frameW, int frameH)
        {
            // First two indices are outer/inner corner; remaining are upper/lower
            // eyelid points whose y-extent gives the eye height. We pad ~25% each
            // side to give the iris-darkest-pixel estimator some margin.
            float xMin = float.MaxValue, xMax = float.MinValue;
            float yMin = float.MaxValue, yMax = float.MinValue;
            foreach (var i in indices)
            {
                if (landmarks[i][0] < xMin) xMin = landmarks[i][0];
                if (landmarks[i][0] > xMax) xMax = landmarks[i][0];
                if (landmarks[i][1] < yMin) yMin = landmarks[i][1];
                if (landmarks[i][1] > yMax) yMax = landmarks[i][1];
            }
            float w = xMax - xMin;
            float h = yMax - yMin;
            if (w < 4 || h < 2) return null;
            float padX = w * 0.15f;
            float padY = h * 0.50f;          // eyelid landmarks are tight; need vertical room
            int rx = (int)Math.Round(xMin - padX);
            int ry = (int)Math.Round(yMin - padY);
            int rw = (int)Math.Round(w + 2 * padX);
            int rh = (int)Math.Round(h + 2 * padY);
            return ClipRect(rx, ry, rw, rh, frameW, frameH);
        }

        private void UpdateBlinkState(double earL, double earR)
        {
            // Combine the two eyes into a single signal — averaging absorbs
            // small per-eye asymmetry that would otherwise prevent the eyes
            // from being measured as simultaneously closed at the same frame.
            double avgEar = (earL + earR) / 2.0;

            // Update rolling baseline. Max naturally rejects low (closed) values.
            EnqueueWithCap(_earBuffer, avgEar, EarBaselineFrames);
            _earBaseline = MaxOf(_earBuffer);

            // Track per-window min/max for diagnostic log
            if (avgEar < _windowMinEar) _windowMinEar = avgEar;
            if (avgEar > _windowMaxEar) _windowMaxEar = avgEar;

            var now = DateTime.UtcNow;
            MaybeLogBlinkDiag(now, avgEar);

            // Need enough samples before any blink can fire (avoid a startup
            // spurious blink while the baseline is still seeded by the first
            // few low-EAR frames).
            if (_earBuffer.Count < EarMinSamplesForBaseline) return;
            if (_earBaseline <= 0) return;

            // Near-miss: dropped under 0.90×baseline but we're not in closed
            // state. Counting these per window tells us how many "almost-blinks"
            // we missed without committing to an aggressive threshold up front.
            if (!_eyesClosed && avgEar < EarNearMissRatio * _earBaseline)
            {
                _windowNearMissCount++;
            }

            // Hysteresis: once closed, stay closed until EAR > openRatio×baseline.
            bool nowClosed = _eyesClosed
                ? avgEar < EarOpenRatio  * _earBaseline
                : avgEar < EarClosedRatio * _earBaseline;

            if (nowClosed && !_eyesClosed)
            {
                _eyesClosedAt = now;
                _minEarThisClosure = avgEar;       // start tracking minimum
            }
            else if (nowClosed)
            {
                if (avgEar < _minEarThisClosure) _minEarThisClosure = avgEar;
            }
            else if (!nowClosed && _eyesClosed && _eyesClosedAt.HasValue)
            {
                var closedMs = (now - _eyesClosedAt.Value).TotalMilliseconds;
                bool fired = false;
                if (closedMs >= MinBlinkClosedMs && closedMs <= MaxBlinkClosedMs
                    && (now - _lastBlinkAt).TotalMilliseconds >= BlinkCooldownMs)
                {
                    _lastBlinkAt = now;
                    _blinkCount++;
                    fired = true;
                    Dispatch(() => OnBlink?.Invoke());
                }
                App.Logger?.Information(
                    "WebcamTrackingService: blink {Outcome} (closed for {Ms:F0}ms, baseline EAR={Base:F3}, min EAR during closure={Min:F3}, ratio={Ratio:F2}× baseline)",
                    fired ? $"#{_blinkCount} FIRED" : "rejected",
                    closedMs, _earBaseline, _minEarThisClosure, _minEarThisClosure / _earBaseline);
                _eyesClosedAt = null;
                _minEarThisClosure = double.MaxValue;
            }
            else if (!nowClosed)
            {
                _eyesClosedAt = null;
                _minEarThisClosure = double.MaxValue;
            }

            _eyesClosed = nowClosed;
        }

        private void MaybeLogBlinkDiag(DateTime now, double avgEar)
        {
            // Periodic diagnostic log — counts and aggregate state only, no
            // per-frame data. Window min/max help tune thresholds: if you blink
            // hard during a window and windowMin only reaches 0.85×baseline,
            // that tells us EAR isn't dropping enough for the current 0.80
            // threshold — switch to iris-model contour or loosen further.
            if (_lastBlinkDiagAt == DateTime.MinValue) { _lastBlinkDiagAt = now; return; }
            if ((now - _lastBlinkDiagAt).TotalMilliseconds < BlinkDiagLogIntervalMs) return;
            _lastBlinkDiagAt = now;

            double closedThreshold = _earBaseline * EarClosedRatio;
            double openThreshold = _earBaseline * EarOpenRatio;
            double winMinRatio = _earBaseline > 0 ? _windowMinEar / _earBaseline : 0;
            double winMaxRatio = _earBaseline > 0 ? _windowMaxEar / _earBaseline : 0;
            App.Logger?.Information(
                "WebcamTrackingService: blink-diag baseline={Base:F3} closedThr={CT:F3} openThr={OT:F3} winMin={WMin:F3}({WMinR:P0}) winMax={WMax:F3}({WMaxR:P0}) nearMiss={NM} state={State} blinks={N} samples={S}",
                _earBaseline, closedThreshold, openThreshold,
                _windowMinEar, winMinRatio, _windowMaxEar, winMaxRatio,
                _windowNearMissCount,
                _eyesClosed ? "CLOSED" : "open", _blinkCount, _earBuffer.Count);

            _windowMinEar = double.MaxValue;
            _windowMaxEar = double.MinValue;
            _windowNearMissCount = 0;
        }

        private static double MaxOf(Queue<double> q)
        {
            double m = 0;
            foreach (var v in q) if (v > m) m = v;
            return m;
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  BlazeFace short-range detector (MediaPipe)
        //  ────────────────────────────────────────────────────────────────────────
        //  Loads face_detection_short_range.onnx + the precomputed 896 SSD anchors
        //  (blazeface_anchors.json, generated by IntelliProve's _ssd_generate_anchors
        //  with SSD_OPTIONS_SHORT, ported in the phase-1 spike — bit-exact match
        //  against Python reference, see /tmp/blazeface-spike/).
        //
        //  The detector accepts a BGR Mat of arbitrary size, letterbox-resizes to
        //  128×128, normalizes to [-1,1], runs inference, picks the top-1 anchor
        //  above 0.5 sigmoid score, decodes the box, and unpads back to source-frame
        //  pixel coordinates.
        //
        //  This is a CPU-only inference path. Anchors and ONNX session are loaded
        //  once at Start() and reused across frames; no per-frame allocations beyond
        //  the input tensor buffer.
        // ─────────────────────────────────────────────────────────────────────────
        private sealed class BlazeFaceDetector : IDisposable
        {
            private const int InputSize = 128;
            private const float InputScale = 128f;
            private const float MinSigmoidScore = 0.5f;
            private const float RawScoreLimit = 80f;
            private const int NumAnchors = 896;
            private const int RegressorsPerAnchor = 16;   // [cx, cy, w, h, kp0x, kp0y ... kp5x, kp5y]
            // Below this raw logit we know sigmoid ≤ MinSigmoidScore=0.5; skip the exp.
            // logit(0.5) = 0; we filter with raw>0 then sigmoid only the survivors.
            private const float RawScoreCheap = 0f;

            private readonly InferenceSession _session;
            private readonly float[] _anchorsFlat;        // length 2*NumAnchors (x,y interleaved)
            private readonly string _inputName;

            // Reusable buffers (capture-thread-only access; no locking needed).
            private readonly float[] _inputBuffer = new float[InputSize * InputSize * 3];
            private Mat? _resizeBuffer;       // 128×96 (or whatever the aspect-preserving size is)
            private Mat? _paddedBuffer;       // 128×128

            public BlazeFaceDetector(string modelPath, string anchorsJsonPath)
            {
                var so = new SessionOptions
                {
                    GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                    InterOpNumThreads = 1,
                    IntraOpNumThreads = 2,
                };
                _session = new InferenceSession(modelPath, so);
                _inputName = _session.InputMetadata.Keys.First();

                var json = File.ReadAllText(anchorsJsonPath);
                var nested = JsonConvert.DeserializeObject<float[][]>(json)
                    ?? throw new InvalidOperationException("blazeface_anchors.json: deserialized null");
                if (nested.Length != NumAnchors)
                    throw new InvalidOperationException($"blazeface_anchors.json: expected {NumAnchors} anchors, got {nested.Length}");
                _anchorsFlat = new float[NumAnchors * 2];
                for (int i = 0; i < NumAnchors; i++)
                {
                    if (nested[i].Length != 2)
                        throw new InvalidOperationException($"blazeface_anchors.json: anchor[{i}] not 2 values");
                    _anchorsFlat[2 * i + 0] = nested[i][0];
                    _anchorsFlat[2 * i + 1] = nested[i][1];
                }
            }

            /// <summary>
            /// Detects the most-confident face in <paramref name="bgr"/>. Returns the
            /// face rect in source-image pixel coords, clipped to the source bounds,
            /// or null if no face above MinSigmoidScore.
            /// </summary>
            public CvRect? Detect(Mat bgr)
            {
                int srcW = bgr.Width, srcH = bgr.Height;
                if (srcW <= 0 || srcH <= 0) return null;

                // Letterbox-resize: scale so the longer side fills 128 px, pad the
                // shorter side with black to a square 128×128. This preserves face
                // aspect ratio (the model was trained on undistorted faces).
                float scale = Math.Min((float)InputSize / srcW, (float)InputSize / srcH);
                int newW = Math.Max(1, (int)Math.Round(srcW * scale));
                int newH = Math.Max(1, (int)Math.Round(srcH * scale));
                int padX = (InputSize - newW) / 2;
                int padY = (InputSize - newH) / 2;

                if (_resizeBuffer == null || _resizeBuffer.Width != newW || _resizeBuffer.Height != newH)
                {
                    _resizeBuffer?.Dispose();
                    _resizeBuffer = new Mat();
                }
                if (_paddedBuffer == null)
                {
                    _paddedBuffer = new Mat(InputSize, InputSize, MatType.CV_8UC3, Scalar.All(0));
                }
                else
                {
                    _paddedBuffer.SetTo(Scalar.All(0));
                }

                Cv2.Resize(bgr, _resizeBuffer, new CvSize(newW, newH), 0, 0, InterpolationFlags.Linear);
                using (var roi = new Mat(_paddedBuffer, new CvRect(padX, padY, newW, newH)))
                {
                    _resizeBuffer.CopyTo(roi);
                }

                // BGR → RGB and normalize to [-1, 1] in channel-last order.
                // We avoid an extra Mat allocation by walking the padded buffer directly.
                FillInputBufferFromBgr(_paddedBuffer, _inputBuffer);

                var inputTensor = new DenseTensor<float>(_inputBuffer, new[] { 1, InputSize, InputSize, 3 });
                using var results = _session.Run(new[] { NamedOnnxValue.CreateFromTensor(_inputName, inputTensor) });

                var rawBoxes = results.First(r => r.Name == "regressors").AsTensor<float>();
                var rawScores = results.First(r => r.Name == "classificators").AsTensor<float>();

                // Find best anchor above threshold (raw>0 ⇒ sigmoid>0.5).
                int bestIdx = -1;
                float bestRaw = float.NegativeInfinity;
                for (int i = 0; i < NumAnchors; i++)
                {
                    float raw = rawScores[0, i, 0];
                    if (raw > bestRaw) { bestRaw = raw; bestIdx = i; }
                }
                if (bestIdx < 0 || bestRaw <= RawScoreCheap) return null;

                // Sigmoid only the winner — cheaper than computing all 896.
                float clipped = bestRaw > RawScoreLimit ? RawScoreLimit : (bestRaw < -RawScoreLimit ? -RawScoreLimit : bestRaw);
                float sig = 1f / (1f + (float)Math.Exp(-clipped));
                if (sig < MinSigmoidScore) return null;

                // Decode box: cx,cy,w,h are anchor offsets (in 128-px units), normalize.
                float ax = _anchorsFlat[2 * bestIdx + 0];
                float ay = _anchorsFlat[2 * bestIdx + 1];
                float cx = rawBoxes[0, bestIdx, 0] / InputScale + ax;
                float cy = rawBoxes[0, bestIdx, 1] / InputScale + ay;
                float w  = rawBoxes[0, bestIdx, 2] / InputScale;
                float h  = rawBoxes[0, bestIdx, 3] / InputScale;
                float xmin01 = cx - w / 2f;
                float ymin01 = cy - h / 2f;

                // Convert from 128-px-padded normalized [0,1] → source pixel coords.
                // padded_pixel = norm * 128. Then subtract pad and divide by scale.
                float srcXmin = (xmin01 * InputSize - padX) / scale;
                float srcYmin = (ymin01 * InputSize - padY) / scale;
                float srcW01  = (w * InputSize) / scale;
                float srcH01  = (h * InputSize) / scale;

                int rx = (int)Math.Round(srcXmin);
                int ry = (int)Math.Round(srcYmin);
                int rw = (int)Math.Round(srcW01);
                int rh = (int)Math.Round(srcH01);

                // Final clip to source bounds.
                if (rx < 0) { rw += rx; rx = 0; }
                if (ry < 0) { rh += ry; ry = 0; }
                if (rx + rw > srcW) rw = srcW - rx;
                if (ry + rh > srcH) rh = srcH - ry;
                if (rw <= 0 || rh <= 0) return null;

                return new CvRect(rx, ry, rw, rh);
            }

            private static void FillInputBufferFromBgr(Mat bgr128, float[] dst)
            {
                // bgr128 is contiguous CV_8UC3, 128×128. Copy raw bytes once, then
                // rearrange in place: BGR → RGB and uint8 → float in [-1,1].
                if (bgr128.Width != InputSize || bgr128.Height != InputSize || bgr128.Type() != MatType.CV_8UC3)
                    throw new InvalidOperationException("FillInputBufferFromBgr: expected 128×128 CV_8UC3");

                int total = InputSize * InputSize;
                var bytes = new byte[total * 3];
                System.Runtime.InteropServices.Marshal.Copy(bgr128.Data, bytes, 0, bytes.Length);
                const float kScale = 2f / 255f;
                for (int i = 0; i < total; i++)
                {
                    // OpenCV channel order is BGR; we want RGB at the dst.
                    byte b = bytes[3 * i + 0];
                    byte g = bytes[3 * i + 1];
                    byte r = bytes[3 * i + 2];
                    dst[3 * i + 0] = r * kScale - 1f;
                    dst[3 * i + 1] = g * kScale - 1f;
                    dst[3 * i + 2] = b * kScale - 1f;
                }
            }

            public void Dispose()
            {
                try { _session.Dispose(); } catch { }
                try { _resizeBuffer?.Dispose(); } catch { }
                try { _paddedBuffer?.Dispose(); } catch { }
                _resizeBuffer = null;
                _paddedBuffer = null;
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  FaceMesh detector (MediaPipe — face_landmark.onnx)
        //  ────────────────────────────────────────────────────────────────────────
        //  Input: 1×192×192×3 RGB normalized to [0, 1] of a 1.5×-expanded square
        //  crop around the BlazeFace face rect.
        //  Output: 1404 floats (468 landmarks × x,y,z) in 192-px tensor coords +
        //  a face-presence sigmoid.
        //
        //  Returns: 468 (x, y) landmarks in source-frame pixel coordinates, or
        //  null if face presence sigmoid < 0.5.
        //
        //  Z is intentionally dropped (not used by gaze/blink). All output coords
        //  stay in float pixel space — caller rounds when needed.
        // ─────────────────────────────────────────────────────────────────────────
        private sealed class FaceMeshDetector : IDisposable
        {
            private const int InputSize = 192;
            private const int NumLandmarks = 468;
            private const int LandmarkDims = 3;     // x, y, z (z dropped on output)
            private const float MinPresenceScore = 0.5f;
            private const float RoiScale = 1.5f;    // SquareLong scale around face bbox

            private readonly InferenceSession _session;
            private readonly string _inputName;
            private readonly float[] _inputBuffer = new float[InputSize * InputSize * 3];
            private Mat? _croppedBuffer;            // square crop with black padding
            private Mat? _resizedBuffer;            // 192×192 resized
            private int _lastSide = -1;

            public FaceMeshDetector(string modelPath)
            {
                var so = new SessionOptions
                {
                    GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                    InterOpNumThreads = 1,
                    IntraOpNumThreads = 2,
                };
                _session = new InferenceSession(modelPath, so);
                _inputName = _session.InputMetadata.Keys.First();
            }

            /// <summary>
            /// Detect 468 face landmarks. Returns array of [x, y] in source-frame
            /// pixel coords, or null if no face.
            /// </summary>
            public float[][]? Detect(Mat bgr, CvRect faceRect)
            {
                int srcW = bgr.Width, srcH = bgr.Height;

                // Square ROI around face center, 1.5× the longer side.
                float cx = faceRect.X + faceRect.Width / 2f;
                float cy = faceRect.Y + faceRect.Height / 2f;
                float side = Math.Max(faceRect.Width, faceRect.Height) * RoiScale;
                int rx = (int)Math.Round(cx - side / 2f);
                int ry = (int)Math.Round(cy - side / 2f);
                int rs = (int)Math.Round(side);
                if (rs < 16) return null;

                if (_croppedBuffer == null || _lastSide != rs)
                {
                    _croppedBuffer?.Dispose();
                    _croppedBuffer = new Mat(rs, rs, MatType.CV_8UC3, Scalar.All(0));
                    _lastSide = rs;
                }
                else
                {
                    _croppedBuffer.SetTo(Scalar.All(0));
                }
                _resizedBuffer ??= new Mat();

                // Black-padded crop: copy in only the in-bounds portion.
                int sx0 = Math.Max(0, rx);
                int sy0 = Math.Max(0, ry);
                int sx1 = Math.Min(srcW, rx + rs);
                int sy1 = Math.Min(srcH, ry + rs);
                if (sx1 > sx0 && sy1 > sy0)
                {
                    int dx0 = sx0 - rx;
                    int dy0 = sy0 - ry;
                    using var src = new Mat(bgr, new CvRect(sx0, sy0, sx1 - sx0, sy1 - sy0));
                    using var dst = new Mat(_croppedBuffer, new CvRect(dx0, dy0, sx1 - sx0, sy1 - sy0));
                    src.CopyTo(dst);
                }

                Cv2.Resize(_croppedBuffer, _resizedBuffer, new CvSize(InputSize, InputSize), 0, 0, InterpolationFlags.Linear);
                FillInputBufferFromBgr(_resizedBuffer, _inputBuffer);

                var inputTensor = new DenseTensor<float>(_inputBuffer, new[] { 1, InputSize, InputSize, 3 });
                using var results = _session.Run(new[] { NamedOnnxValue.CreateFromTensor(_inputName, inputTensor) });

                // Identify outputs by tensor length rather than by name (the ONNX
                // exporter named them generic conv2d_X).
                Tensor<float>? landmarksRaw = null;
                Tensor<float>? presenceRaw = null;
                foreach (var r in results)
                {
                    var t = r.AsTensor<float>();
                    long len = 1;
                    foreach (var d in t.Dimensions) len *= d;
                    if (len >= NumLandmarks * LandmarkDims) landmarksRaw = t;
                    else if (len == 1) presenceRaw = t;
                }
                if (landmarksRaw == null || presenceRaw == null) return null;

                float presenceVal = presenceRaw.ToArray()[0];
                float presence = 1f / (1f + (float)Math.Exp(-presenceVal));
                if (presence < MinPresenceScore) return null;

                var raw = landmarksRaw.ToArray();
                var output = new float[NumLandmarks][];
                for (int i = 0; i < NumLandmarks; i++)
                {
                    float x192 = raw[i * LandmarkDims + 0];
                    float y192 = raw[i * LandmarkDims + 1];
                    float nx = x192 / InputSize;
                    float ny = y192 / InputSize;
                    output[i] = new[] { rx + nx * rs, ry + ny * rs };
                }
                return output;
            }

            private static void FillInputBufferFromBgr(Mat bgr192, float[] dst)
            {
                if (bgr192.Width != InputSize || bgr192.Height != InputSize || bgr192.Type() != MatType.CV_8UC3)
                    throw new InvalidOperationException("FaceMesh.FillInputBufferFromBgr: expected 192×192 CV_8UC3");

                int total = InputSize * InputSize;
                var bytes = new byte[total * 3];
                System.Runtime.InteropServices.Marshal.Copy(bgr192.Data, bytes, 0, bytes.Length);
                const float kScale = 1f / 255f;
                for (int i = 0; i < total; i++)
                {
                    dst[3 * i + 0] = bytes[3 * i + 2] * kScale; // R
                    dst[3 * i + 1] = bytes[3 * i + 1] * kScale; // G
                    dst[3 * i + 2] = bytes[3 * i + 0] * kScale; // B
                }
            }

            public void Dispose()
            {
                try { _session.Dispose(); } catch { }
                try { _croppedBuffer?.Dispose(); } catch { }
                try { _resizedBuffer?.Dispose(); } catch { }
                _croppedBuffer = null;
                _resizedBuffer = null;
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  Iris detector (MediaPipe — iris_landmark.onnx)
        //  ────────────────────────────────────────────────────────────────────────
        //  Input: 1×64×64×3 RGB normalized to [0, 1]. ROI is a 2.3×-expanded
        //  SquareLong bbox of the eye-corner pair. Right eye is flipped horizontally
        //  before inference and the output landmarks are un-flipped (x' = 64 - x)
        //  before mapping back. (The model was trained on left eyes only.)
        //
        //  Output: 71 eyelid contour points + 5 iris points per eye, each (x, y, z)
        //  in 64-px tensor coords. We only need iris point 0 (CENTER).
        //
        //  Returns: (X, Y) iris center in source-frame pixel coords, or null
        //  if the eye corners aren't far enough apart to give a meaningful crop.
        // ─────────────────────────────────────────────────────────────────────────
        private sealed class IrisDetector : IDisposable
        {
            private const int InputSize = 64;
            private const float RoiScale = 2.3f;
            private const int NumEyeContour = 71;
            private const int NumIrisPoints = 5;
            private const int LandmarkDims = 3;

            private readonly InferenceSession _session;
            private readonly string _inputName;
            private readonly float[] _inputBuffer = new float[InputSize * InputSize * 3];
            private Mat? _croppedBuffer;
            private Mat? _resizedBuffer;
            private Mat? _flippedBuffer;
            private int _lastSide = -1;

            public IrisDetector(string modelPath)
            {
                var so = new SessionOptions
                {
                    GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                    InterOpNumThreads = 1,
                    IntraOpNumThreads = 2,
                };
                _session = new InferenceSession(modelPath, so);
                _inputName = _session.InputMetadata.Keys.First();
            }

            public (double X, double Y)? DetectIrisCenter(Mat bgr, float[] outerCorner, float[] innerCorner, bool isRightEye)
            {
                int srcW = bgr.Width, srcH = bgr.Height;

                // Square ROI of the corner-pair bbox, expanded 2.3x.
                float xMin = Math.Min(outerCorner[0], innerCorner[0]);
                float xMax = Math.Max(outerCorner[0], innerCorner[0]);
                float yMin = Math.Min(outerCorner[1], innerCorner[1]);
                float yMax = Math.Max(outerCorner[1], innerCorner[1]);
                float cx = (xMin + xMax) / 2f;
                float cy = (yMin + yMax) / 2f;
                float side = Math.Max(xMax - xMin, yMax - yMin) * RoiScale;
                if (side < 8) return null;
                int rx = (int)Math.Round(cx - side / 2f);
                int ry = (int)Math.Round(cy - side / 2f);
                int rs = (int)Math.Round(side);
                if (rs < InputSize / 4) return null;

                if (_croppedBuffer == null || _lastSide != rs)
                {
                    _croppedBuffer?.Dispose();
                    _croppedBuffer = new Mat(rs, rs, MatType.CV_8UC3, Scalar.All(0));
                    _lastSide = rs;
                }
                else
                {
                    _croppedBuffer.SetTo(Scalar.All(0));
                }
                _resizedBuffer ??= new Mat();
                _flippedBuffer ??= new Mat();

                int sx0 = Math.Max(0, rx);
                int sy0 = Math.Max(0, ry);
                int sx1 = Math.Min(srcW, rx + rs);
                int sy1 = Math.Min(srcH, ry + rs);
                if (sx1 > sx0 && sy1 > sy0)
                {
                    using var src = new Mat(bgr, new CvRect(sx0, sy0, sx1 - sx0, sy1 - sy0));
                    using var dst = new Mat(_croppedBuffer, new CvRect(sx0 - rx, sy0 - ry, sx1 - sx0, sy1 - sy0));
                    src.CopyTo(dst);
                }

                Cv2.Resize(_croppedBuffer, _resizedBuffer, new CvSize(InputSize, InputSize), 0, 0, InterpolationFlags.Linear);

                Mat fed = _resizedBuffer;
                if (isRightEye)
                {
                    Cv2.Flip(_resizedBuffer, _flippedBuffer, FlipMode.Y);
                    fed = _flippedBuffer;
                }

                FillInputBufferFromBgr(fed, _inputBuffer);

                var inputTensor = new DenseTensor<float>(_inputBuffer, new[] { 1, InputSize, InputSize, 3 });
                using var results = _session.Run(new[] { NamedOnnxValue.CreateFromTensor(_inputName, inputTensor) });

                // Identify outputs by length. Iris output has 5×3 = 15 floats.
                Tensor<float>? irisT = null;
                foreach (var r in results)
                {
                    var t = r.AsTensor<float>();
                    long len = 1;
                    foreach (var d in t.Dimensions) len *= d;
                    if (len == NumIrisPoints * LandmarkDims) { irisT = t; break; }
                }
                if (irisT == null) return null;

                var iris = irisT.ToArray();
                // Iris center is point 0 (IrisIndex.CENTER in IntelliProve constants).
                float ix64 = iris[0];
                float iy64 = iris[1];
                if (isRightEye) ix64 = InputSize - ix64;
                float nx = ix64 / InputSize;
                float ny = iy64 / InputSize;
                return (rx + nx * rs, ry + ny * rs);
            }

            private static void FillInputBufferFromBgr(Mat bgr64, float[] dst)
            {
                if (bgr64.Width != InputSize || bgr64.Height != InputSize || bgr64.Type() != MatType.CV_8UC3)
                    throw new InvalidOperationException("IrisDetector.FillInputBufferFromBgr: expected 64×64 CV_8UC3");

                int total = InputSize * InputSize;
                var bytes = new byte[total * 3];
                System.Runtime.InteropServices.Marshal.Copy(bgr64.Data, bytes, 0, bytes.Length);
                const float kScale = 1f / 255f;
                for (int i = 0; i < total; i++)
                {
                    dst[3 * i + 0] = bytes[3 * i + 2] * kScale; // R
                    dst[3 * i + 1] = bytes[3 * i + 1] * kScale; // G
                    dst[3 * i + 2] = bytes[3 * i + 0] * kScale; // B
                }
            }

            public void Dispose()
            {
                try { _session.Dispose(); } catch { }
                try { _croppedBuffer?.Dispose(); } catch { }
                try { _resizedBuffer?.Dispose(); } catch { }
                try { _flippedBuffer?.Dispose(); } catch { }
                _croppedBuffer = null;
                _resizedBuffer = null;
                _flippedBuffer = null;
            }
        }
    }
}
