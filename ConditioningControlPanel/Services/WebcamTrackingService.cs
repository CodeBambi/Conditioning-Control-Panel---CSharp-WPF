using System;
using System.Threading;
using System.Windows;
using Serilog;

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
    //    Resources/Models/face_detection_yunet.onnx — OpenCV's YuNet detector
    //    Outputs: face bbox + 5 keypoints (left eye, right eye, nose tip,
    //             left mouth corner, right mouth corner)
    //
    //    Blink detection: eye-region pixel-intensity variance heuristic
    //                     (cruder than EAR but works without lid landmarks)
    //    Gaze direction:  iris-as-darkest-pixel relative to eye center
    //    Mouth-open:      DEFERRED to v2 — YuNet keypoints lack lip vertical
    //                     extents needed for MAR-style detection
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
    /// Local, offline webcam-based eye and mouth tracking. Powers Lab Box 1
    /// (Webcam Triggers) and Lab Box 2 (Focus Training). Owns the only
    /// VideoCapture handle in the application.
    ///
    /// SKELETON: capture loop, ONNX inference, and event firing land in
    /// subsequent commits. This commit wires the public surface and
    /// disposal contract so settings and UI can compile against it.
    /// </summary>
    public class WebcamTrackingService : IDisposable
    {
        /// <summary>
        /// Bumped any time we add a new sensor type, broaden what the camera
        /// observes, or change what numbers are stored. On bump, the consent
        /// dialog re-runs from screen 1 for every existing user.
        /// </summary>
        public const string ConsentVersion = "1.0";

        private readonly object _stateLock = new();
        private bool _disposed;

        public WebcamTrackingState State { get; private set; } = WebcamTrackingState.Stopped;

        public bool IsRunning => State == WebcamTrackingState.Tracking || State == WebcamTrackingState.FaceLost;

        public WebcamCalibrationData? Calibration { get; private set; }

        public event Action? OnBlink;
        public event Action<Point>? OnLongStare;
        public event Action? OnMouthOpen;
        public event Action<Point>? OnGazeMove;
        public event Action<GazeSide>? OnGazeSide;
        public event Action? OnFaceLost;
        public event Action? OnFaceFound;
        public event Action<WebcamTrackingState>? OnTrackingStateChanged;

        public WebcamTrackingService()
        {
            App.Logger?.Information("WebcamTrackingService: constructed (skeleton — no capture wired yet)");
            Calibration = WebcamCalibrationData.Load();
        }

        /// <summary>
        /// Open the webcam handle and begin the capture/inference loop.
        /// Returns false if consent has not been given, the camera is in
        /// use by another app, or the OS has denied camera access.
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

                // Capture loop, model load, and frame processing land in subsequent commits.
                SetState(WebcamTrackingState.Stopped);
                App.Logger?.Information("WebcamTrackingService: Start() called (skeleton — capture loop not yet implemented)");
                return false;
            }
        }

        public void Stop()
        {
            lock (_stateLock)
            {
                if (!IsRunning) return;
                SetState(WebcamTrackingState.Stopped);
                App.Logger?.Information("WebcamTrackingService: Stop()");
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

        /// <summary>
        /// Wipe all webcam state — for the "Revoke consent" button.
        /// Stops tracking, clears calibration, resets settings flags.
        /// </summary>
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

        private void SetState(WebcamTrackingState state)
        {
            if (State == state) return;
            State = state;
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.HasShutdownStarted)
            {
                dispatcher.BeginInvoke(new Action(() => OnTrackingStateChanged?.Invoke(state)));
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { Stop(); } catch { /* best-effort during shutdown */ }
            App.Logger?.Information("WebcamTrackingService: disposed");
        }

        // Suppress unused-event warnings until the capture loop wires them up.
        // These will be invoked from the inference pipeline in a later commit.
        internal void RaiseBlinkForTesting() => OnBlink?.Invoke();
        internal void RaiseLongStareForTesting(Point p) => OnLongStare?.Invoke(p);
        internal void RaiseMouthOpenForTesting() => OnMouthOpen?.Invoke();
        internal void RaiseGazeMoveForTesting(Point p) => OnGazeMove?.Invoke(p);
        internal void RaiseGazeSideForTesting(GazeSide s) => OnGazeSide?.Invoke(s);
        internal void RaiseFaceLostForTesting() => OnFaceLost?.Invoke();
        internal void RaiseFaceFoundForTesting() => OnFaceFound?.Invoke();
    }
}
