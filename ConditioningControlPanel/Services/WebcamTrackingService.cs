using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
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
    //  Detection pipeline (v1):
    //    Resources/Models/haarcascade_frontalface_default.xml — face detection
    //    Resources/Models/haarcascade_eye.xml                 — eye detection
    //    Both are OpenCV's official Haar cascades, MIT-licensed, bundled in the
    //    installer. No internet connection is used at runtime.
    //
    //    Face   → largest detection per frame
    //    Eyes   → up to 2 detections within face ROI; leftmost / rightmost
    //             classified as left / right eye relative to face.
    //    Blink  → eye-region pixel-intensity variance heuristic (no eyelid
    //             landmarks needed; cruder than EAR but works).
    //    Gaze   → iris-as-darkest-pixel relative to eye-region center.
    //    Mouth-open: DEFERRED to v2 — Haar smile cascade is unreliable; a
    //                proper lip landmark model is the right v2 path.
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
        private const int FaceProcessEveryNFrames = 2;             // detect every other frame for perf

        // Face / eye cascade tuning
        private const double FaceScaleFactor = 1.2;
        private const int FaceMinNeighbors = 5;
        private const int FaceMinSizePx = 80;
        private const double EyeScaleFactor = 1.1;
        private const int EyeMinNeighbors = 5;

        // Blink heuristic parameters
        private const int EyeRoiPaddingPx = 4;
        private const int BlinkBaselineFrames = 30;
        private const double BlinkClosedRatio = 0.55;
        private const double BlinkOpenRatio = 0.80;
        private const int BlinkMaxDurationMs = 500;
        private const int BlinkCooldownMs = 1500;

        // Gaze parameters
        private const int GazeBufferSize = 30;
        private const int LongStareDurationMs = 3000;
        private const double LongStareMaxDeviationPx = 60.0;
        private const int LongStareCooldownMs = 5000;

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

        // Capture-thread state
        private VideoCapture? _capture;
        private CascadeClassifier? _faceCascade;
        private CascadeClassifier? _eyeCascade;
        private Thread? _captureThread;
        private volatile bool _stopRequested;

        // Heuristic state (capture-thread only)
        private readonly Queue<double> _leftEyeStddevHistory = new();
        private readonly Queue<double> _rightEyeStddevHistory = new();
        private bool _bothEyesClosed;
        private DateTime _eyesClosedAt;
        private DateTime _lastBlinkAt = DateTime.MinValue;
        private bool _faceWasFound;
        private int _consecutiveNoFaceFrames;
        private DateTime _lastLongStareAt = DateTime.MinValue;
        private readonly Queue<(DateTime Time, System.Windows.Point ScreenPoint)> _gazeBuffer = new();
        private CvRect? _lastFaceRect;
        private CvRect? _lastLeftEyeRect;
        private CvRect? _lastRightEyeRect;
        private int _frameCounter;

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

                var (facePath, eyePath) = ResolveCascadePaths();
                if (facePath == null || eyePath == null)
                {
                    App.Logger?.Warning("WebcamTrackingService: cascade XML missing in Resources/Models/");
                    SetState(WebcamTrackingState.Error);
                    return false;
                }

                if (!TryOpenCamera())
                {
                    return false; // state already set
                }

                if (!TryLoadCascades(facePath, eyePath))
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
                ReleaseCascades();
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
            App.Logger?.Information("WebcamTrackingService: calibration applied (mode={Mode})", data.Mode);
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

        private static (string? Face, string? Eye) ResolveCascadePaths()
        {
            try
            {
                var baseDir = AppContext.BaseDirectory;
                var face = Path.Combine(baseDir, "Resources", "Models", "haarcascade_frontalface_default.xml");
                var eye = Path.Combine(baseDir, "Resources", "Models", "haarcascade_eye.xml");
                return (File.Exists(face) ? face : null, File.Exists(eye) ? eye : null);
            }
            catch
            {
                return (null, null);
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

        private bool TryLoadCascades(string facePath, string eyePath)
        {
            try
            {
                _faceCascade = new CascadeClassifier(facePath);
                _eyeCascade = new CascadeClassifier(eyePath);
                if (_faceCascade.Empty() || _eyeCascade.Empty())
                {
                    App.Logger?.Warning("WebcamTrackingService: cascade classifier loaded empty");
                    return false;
                }
                App.Logger?.Information("WebcamTrackingService: cascades loaded");
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "WebcamTrackingService: failed to load cascade");
                return false;
            }
        }

        private void ReleaseCapture()
        {
            try { _capture?.Release(); } catch { }
            try { _capture?.Dispose(); } catch { }
            _capture = null;
        }

        private void ReleaseCascades()
        {
            try { _faceCascade?.Dispose(); } catch { }
            try { _eyeCascade?.Dispose(); } catch { }
            _faceCascade = null;
            _eyeCascade = null;
        }

        private void ResetHeuristicState()
        {
            _leftEyeStddevHistory.Clear();
            _rightEyeStddevHistory.Clear();
            _bothEyesClosed = false;
            _gazeBuffer.Clear();
            _faceWasFound = false;
            _consecutiveNoFaceFrames = 0;
            _lastFaceRect = null;
            _lastLeftEyeRect = null;
            _lastRightEyeRect = null;
            _frameCounter = 0;
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
                    if (_capture == null || _faceCascade == null || _eyeCascade == null) break;

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
                        ProcessFrame(gray);
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

        private void ProcessFrame(Mat gray)
        {
            _frameCounter++;
            var runFullDetect = (_frameCounter % FaceProcessEveryNFrames) == 0 || _lastFaceRect == null;

            CvRect faceRect;
            if (runFullDetect)
            {
                var faces = _faceCascade!.DetectMultiScale(
                    gray,
                    scaleFactor: FaceScaleFactor,
                    minNeighbors: FaceMinNeighbors,
                    flags: HaarDetectionTypes.ScaleImage,
                    minSize: new CvSize(FaceMinSizePx, FaceMinSizePx));
                if (faces == null || faces.Length == 0)
                {
                    HandleNoFace();
                    return;
                }
                faceRect = faces.OrderByDescending(r => r.Width * r.Height).First();
                _lastFaceRect = faceRect;

                // Re-detect eyes within new face region.
                using var faceRoi = new Mat(gray, faceRect);
                var eyes = _eyeCascade!.DetectMultiScale(
                    faceRoi,
                    scaleFactor: EyeScaleFactor,
                    minNeighbors: EyeMinNeighbors);
                if (eyes != null && eyes.Length >= 2)
                {
                    var sorted = eyes.OrderBy(e => e.X).Take(2).ToArray();
                    _lastLeftEyeRect = OffsetRect(sorted[0], faceRect.X, faceRect.Y);
                    _lastRightEyeRect = OffsetRect(sorted[1], faceRect.X, faceRect.Y);
                }
                else
                {
                    // Don't clear stale rects — we'd rather use slightly outdated
                    // eye boxes than drop tracking on every other frame.
                }
            }
            else
            {
                faceRect = _lastFaceRect!.Value;
            }

            HandleFaceFound();

            if (_lastLeftEyeRect == null || _lastRightEyeRect == null) return;

            // Per-eye blink heuristic via stddev of grayscale eye ROI.
            var leftStddev = SampleEyeRegionStddev(gray, _lastLeftEyeRect.Value);
            var rightStddev = SampleEyeRegionStddev(gray, _lastRightEyeRect.Value);
            UpdateBlinkHeuristic(leftStddev, rightStddev);

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
            if (_consecutiveNoFaceFrames > FaceLostFramesThreshold * 2)
            {
                _leftEyeStddevHistory.Clear();
                _rightEyeStddevHistory.Clear();
                _gazeBuffer.Clear();
                _bothEyesClosed = false;
                _lastFaceRect = null;
                _lastLeftEyeRect = null;
                _lastRightEyeRect = null;
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

        private static double SampleEyeRegionStddev(Mat gray, CvRect eyeRect)
        {
            var rect = ClipRect(
                eyeRect.X + EyeRoiPaddingPx,
                eyeRect.Y + EyeRoiPaddingPx,
                eyeRect.Width - 2 * EyeRoiPaddingPx,
                eyeRect.Height - 2 * EyeRoiPaddingPx,
                gray.Width,
                gray.Height);
            if (rect.Width < 4 || rect.Height < 4) return 0;

            using var roi = new Mat(gray, rect);
            Cv2.MeanStdDev(roi, out _, out var stddev);
            return stddev.Val0;
        }

        private void UpdateBlinkHeuristic(double leftStddev, double rightStddev)
        {
            EnqueueWithCap(_leftEyeStddevHistory, leftStddev, BlinkBaselineFrames);
            EnqueueWithCap(_rightEyeStddevHistory, rightStddev, BlinkBaselineFrames);

            if (_leftEyeStddevHistory.Count < BlinkBaselineFrames / 2 ||
                _rightEyeStddevHistory.Count < BlinkBaselineFrames / 2)
            {
                return;
            }

            var leftBase = HighQuartile(_leftEyeStddevHistory);
            var rightBase = HighQuartile(_rightEyeStddevHistory);
            if (leftBase < 1.0 || rightBase < 1.0) return;

            var leftClosed = leftStddev < leftBase * BlinkClosedRatio;
            var rightClosed = rightStddev < rightBase * BlinkClosedRatio;
            var leftOpen = leftStddev > leftBase * BlinkOpenRatio;
            var rightOpen = rightStddev > rightBase * BlinkOpenRatio;

            var now = DateTime.UtcNow;

            if (!_bothEyesClosed && leftClosed && rightClosed)
            {
                _bothEyesClosed = true;
                _eyesClosedAt = now;
            }
            else if (_bothEyesClosed && leftOpen && rightOpen)
            {
                _bothEyesClosed = false;
                var blinkDuration = now - _eyesClosedAt;
                if (blinkDuration.TotalMilliseconds <= BlinkMaxDurationMs &&
                    (now - _lastBlinkAt).TotalMilliseconds >= BlinkCooldownMs)
                {
                    _lastBlinkAt = now;
                    Dispatch(() => OnBlink?.Invoke());
                }
            }
        }

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
            var side = ClassifyGazeSide(irisDx);
            Dispatch(() => OnGazeSide?.Invoke(side));

            var screenPoint = ProjectGazeToScreen(irisDx, irisDy);
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
                var threshold = Math.Abs(leftRef - rightRef) * 0.25;
                if (irisDx < midpoint - threshold) return leftRef < rightRef ? GazeSide.Left : GazeSide.Right;
                if (irisDx > midpoint + threshold) return leftRef < rightRef ? GazeSide.Right : GazeSide.Left;
                return GazeSide.Center;
            }

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

        private static double HighQuartile(Queue<double> q)
        {
            if (q.Count == 0) return 0;
            var arr = q.ToArray();
            Array.Sort(arr);
            return arr[Math.Min(arr.Length - 1, (int)(arr.Length * 0.75))];
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
    }
}
