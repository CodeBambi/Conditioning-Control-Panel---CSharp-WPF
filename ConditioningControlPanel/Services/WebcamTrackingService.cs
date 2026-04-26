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
    //  Detection pipeline (current — phase-2 partial pivot, mid-rollout):
    //    Resources/Models/face_detection_short_range.onnx  — BlazeFace (MediaPipe)
    //    Resources/Models/blazeface_anchors.json           — precomputed 896 SSD anchors
    //    Resources/Models/haarcascade_eye.xml              — eye detection (still Haar this phase)
    //    All shipped in the installer. No internet at runtime.
    //
    //    Face   → BlazeFace ONNX, top-1 above 0.5 sigmoid score, mapped back
    //             to source-frame pixel coords through letterbox padding.
    //    Eyes   → Haar eye cascade within face ROI (TEMPORARY — replaced by
    //             FaceMesh landmarks in phase 3).
    //    Blink  → eye-absence heuristic (TEMPORARY — replaced by EAR on
    //             FaceMesh eyelid landmarks in phase 3).
    //    Gaze   → iris-as-darkest-pixel within eye ROI (TEMPORARY — replaced
    //             by Iris model exact iris-center landmark in phase 4).
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

        // Eye cascade tuning (Haar — temporary, removed in phase 3)
        private const double EyeScaleFactor = 1.1;
        private const int EyeMinNeighbors = 5;

        // Blink detection (eye-absence based — see ProcessFrame for algorithm)
        private const int EyeRoiPaddingPx = 4;
        private const int MinBlinkAbsentFrames = 2;          // ~67ms — filters spurious 1-frame detection misses
        private const int MaxBlinkAbsentFrames = 12;         // ~400ms — anything longer isn't a blink
        private const int BlinkCooldownMs = 700;             // gap required between consecutive blink fires

        // Gaze parameters
        private const int GazeBufferSize = 30;
        private const int LongStareDurationMs = 3000;
        private const double LongStareMaxDeviationPx = 60.0;
        private const int LongStareCooldownMs = 5000;
        private const int IrisSmoothFrames = 5;              // rolling-mean window over raw iris vector

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

        // Capture-thread state
        private VideoCapture? _capture;
        private BlazeFaceDetector? _faceDetector;
        private CascadeClassifier? _eyeCascade;
        private Thread? _captureThread;
        private volatile bool _stopRequested;

        // Heuristic state (capture-thread only)
        private DateTime _lastBlinkAt = DateTime.MinValue;
        private int _eyesAbsentStreak;                           // frames in a row Haar found <2 eyes
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
                if (paths.FaceModel == null || paths.FaceAnchors == null || paths.EyeCascade == null)
                {
                    App.Logger?.Warning("WebcamTrackingService: model files missing in Resources/Models/ (face_detection_short_range.onnx, blazeface_anchors.json, haarcascade_eye.xml)");
                    SetState(WebcamTrackingState.Error);
                    return false;
                }

                if (!TryOpenCamera())
                {
                    return false; // state already set
                }

                if (!TryLoadModels(paths.FaceModel, paths.FaceAnchors, paths.EyeCascade))
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

        private static (string? FaceModel, string? FaceAnchors, string? EyeCascade) ResolveModelPaths()
        {
            try
            {
                var baseDir = AppContext.BaseDirectory;
                var faceModel = Path.Combine(baseDir, "Resources", "Models", "face_detection_short_range.onnx");
                var faceAnchors = Path.Combine(baseDir, "Resources", "Models", "blazeface_anchors.json");
                var eye = Path.Combine(baseDir, "Resources", "Models", "haarcascade_eye.xml");
                return (
                    File.Exists(faceModel) ? faceModel : null,
                    File.Exists(faceAnchors) ? faceAnchors : null,
                    File.Exists(eye) ? eye : null);
            }
            catch
            {
                return (null, null, null);
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

        private bool TryLoadModels(string faceModelPath, string faceAnchorsPath, string eyeCascadePath)
        {
            try
            {
                _faceDetector = new BlazeFaceDetector(faceModelPath, faceAnchorsPath);
                _eyeCascade = new CascadeClassifier(eyeCascadePath);
                if (_eyeCascade.Empty())
                {
                    App.Logger?.Warning("WebcamTrackingService: eye cascade loaded empty");
                    return false;
                }
                App.Logger?.Information("WebcamTrackingService: BlazeFace + Haar eye loaded");
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
            try { _eyeCascade?.Dispose(); } catch { }
            _faceDetector = null;
            _eyeCascade = null;
        }

        private void ResetHeuristicState()
        {
            _gazeBuffer.Clear();
            _faceWasFound = false;
            _consecutiveNoFaceFrames = 0;
            _lastFaceRect = null;
            _lastLeftEyeRect = null;
            _lastRightEyeRect = null;
            _eyesAbsentStreak = 0;
            _irisDxSmoothBuffer.Clear();
            _irisDySmoothBuffer.Clear();
            _lastGazeSide = GazeSide.Center;
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
                    if (_capture == null || _faceDetector == null || _eyeCascade == null) break;

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

            using var faceRoi = new Mat(gray, faceRect);
            var eyes = _eyeCascade!.DetectMultiScale(
                faceRoi,
                scaleFactor: EyeScaleFactor,
                minNeighbors: EyeMinNeighbors);
            bool bothEyesDetected = eyes != null && eyes.Length >= 2;

            if (bothEyesDetected)
            {
                var sorted = eyes!.OrderBy(e => e.X).Take(2).ToArray();
                _lastLeftEyeRect = OffsetRect(sorted[0], faceRect.X, faceRect.Y);
                _lastRightEyeRect = OffsetRect(sorted[1], faceRect.X, faceRect.Y);

                // Eye-absence blink detection: a brief streak of frames where
                // Haar's eye cascade fails to find both eyes (because the
                // cascade is trained on open eyes, the eyelid breaks it),
                // followed by re-detection, signals a blink. Far more robust
                // than pixel-variance across glasses, lighting, head angles.
                if (_eyesAbsentStreak >= MinBlinkAbsentFrames && _eyesAbsentStreak <= MaxBlinkAbsentFrames)
                {
                    var now = DateTime.UtcNow;
                    if ((now - _lastBlinkAt).TotalMilliseconds >= BlinkCooldownMs)
                    {
                        _lastBlinkAt = now;
                        Dispatch(() => OnBlink?.Invoke());
                    }
                }
                _eyesAbsentStreak = 0;
            }
            else
            {
                _eyesAbsentStreak++;
                // Don't clear eye rects yet — keep last good ones so gaze
                // stays smooth across the brief blink itself.
            }

            HandleFaceFound();

            if (_lastLeftEyeRect == null || _lastRightEyeRect == null) return;

            // Iris-as-darkest-pixel for gaze direction.
            var leftIris = ComputeIrisVector(gray, _lastLeftEyeRect.Value);
            var rightIris = ComputeIrisVector(gray, _lastRightEyeRect.Value);
            var avgX = (leftIris.X + rightIris.X) / 2.0;
            var avgY = (leftIris.Y + rightIris.Y) / 2.0;
            EmitGazeEvents(avgX, avgY);
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
            // blink was caused by stale rects — keep this clear.)
            _lastLeftEyeRect = null;
            _lastRightEyeRect = null;
            _eyesAbsentStreak = 0;
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
        //  Heuristics (pure functions of the current frame + state)
        // ─────────────────────────────────────────────────────────────────────────

        private static (double X, double Y) ComputeIrisVector(Mat gray, CvRect eyeRect)
        {
            var rect = ClipRect(
                eyeRect.X + EyeRoiPaddingPx,
                eyeRect.Y + EyeRoiPaddingPx,
                eyeRect.Width - 2 * EyeRoiPaddingPx,
                eyeRect.Height - 2 * EyeRoiPaddingPx,
                gray.Width,
                gray.Height);
            if (rect.Width < 4 || rect.Height < 4) return (0, 0);

            using var roi = new Mat(gray, rect);
            using var blurred = new Mat();
            Cv2.GaussianBlur(roi, blurred, new CvSize(5, 5), 0);
            Cv2.MinMaxLoc(blurred, out _, out _, out var minLoc, out _);

            var dx = (minLoc.X - rect.Width / 2.0) / rect.Width;
            var dy = (minLoc.Y - rect.Height / 2.0) / rect.Height;
            return (dx, dy);
        }

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

            var side = ClassifyGazeSide(smoothDx);
            Dispatch(() => OnGazeSide?.Invoke(side));

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
    }
}
