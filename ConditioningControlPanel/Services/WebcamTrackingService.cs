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
    //    Blink  → EAR (Eye Aspect Ratio) averaged across both eyes computed from
    //             the IRIS MODEL's 71-point eye contour (the model is dedicated
    //             to the eye region — its eyelid landmarks compress aggressively
    //             on closure, unlike FaceMesh's whole-face landmarks which barely
    //             move). Baseline = 90th percentile of last 90 frames (rejects
    //             transient EAR spikes from eyebrow raises etc.). Hysteresis
    //             closed<0.80×base / open>0.88×base, closed→open transition with
    //             60 ms–1.5 s window. Diagnostic log every ~3s.
    //    Iris   → Iris model on 64×64 eye crops (right eye flipped before
    //             inference, output un-flipped). Iris-center landmark gives
    //             a precise gaze vector — replaces darkest-pixel heuristic.
    //    Mouth-open → MAR (Mouth Aspect Ratio) on FaceMesh's inner-lip landmarks
    //             (pattern matches EAR — same percentile baseline + hysteresis).
    //             No new model.
    //    Tongue-out → HSV color heuristic inside the inner-lip polygon, gated
    //             on mouth-open. Counts pink/red pixels excluding teeth-bright
    //             and shadow-dark; fires above a ratio threshold. Known false-
    //             positive: red lipstick on the inner lip. Documented as v2
    //             follow-up that would need a dedicated model.
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

        /// <summary>
        /// True when the user has granted consent AND the recorded consent
        /// version matches the current contract version. A version mismatch
        /// is treated as "not granted" so callers re-prompt — that is the
        /// whole point of the version field, and the only mechanism that
        /// makes bumping <see cref="ConsentVersion"/> actually re-consent
        /// existing users when the privacy contract changes.
        /// </summary>
        public static bool IsConsentCurrent()
        {
            var s = App.Settings?.Current;
            if (s == null) return false;
            if (!s.WebcamConsentGiven) return false;
            return s.WebcamConsentVersion == ConsentVersion;
        }

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

        // EAR is computed against the IRIS MODEL's 71-point eye contour, NOT
        // FaceMesh's eyelid landmarks. FaceMesh's landmarks barely move during
        // mid-closed eyelids (the model is trained on whole-face geometry, not
        // eyelid edges), so EAR drops only ~10% during real blinks — too little
        // to reliably trigger threshold. The iris model is dedicated to the eye
        // region and tracks closure aggressively.
        //
        // Iris-model contour layout (per IntelliProve LEFT_EYE_TO_FACE_LANDMARK_INDEX):
        //   indices 0..8 = lower contour (outer→inner along bottom)
        //   indices 9..15 = upper contour (outer→inner along top)
        // 6-point EAR mapping (P1=outer, P2/P3=upper, P4=inner, P5/P6=lower):
        //   P1=0 (outer corner), P4=8 (inner corner)
        //   P2=11 (upper, FaceMesh-equiv 160), P6=3 (lower, FaceMesh-equiv 144)
        //   P3=13 (upper, FaceMesh-equiv 158), P5=5 (lower, FaceMesh-equiv 153)
        // Same indices apply to both eyes — right-eye contour was un-flipped by
        // IrisDetector so geometry matches left-eye orientation.
        private static readonly int[] IrisContourEarIndices = { 0, 11, 13, 8, 5, 3 };

        // FaceMesh eye-box bounding-box landmarks (kept for diagnostic eye rects)
        private static readonly int[] LeftEyeBoxIndices  = { 33, 133, 159, 145, 158, 153 };  // 33 outer, 133 inner, 159 top, 145 bottom
        private static readonly int[] RightEyeBoxIndices = { 263, 362, 386, 374, 385, 380 }; // 263 outer, 362 inner, 386 top, 374 bottom

        // Mouth-open detection — MAR (Mouth Aspect Ratio), same shape as EAR.
        // Uses FaceMesh's inner-lip landmarks (already in the 468-point set, no
        // new model needed). Three vertical chord pairs averaged to suppress
        // single-landmark noise, divided by mouth corner-to-corner width.
        //   Corners: 78 (left), 308 (right)
        //   Vertical pairs (top, bottom): (81,178), (13,14), (311,402)
        // Baseline pattern matches EAR: 90-frame rolling buffer, 90th-percentile
        // baseline (rejects yawn / surprised-face spikes from polluting it).
        // Hysteresis: enter "open" at >1.8×baseline, leave at <1.4×baseline.
        // Min open window 80 ms (filters single-frame jitter). Cooldown 800 ms.
        private const int MarBaselineFrames = 90;
        private const int MarMinSamplesForBaseline = 15;
        private const double MarOpenRatio = 1.80;
        private const double MarCloseRatio = 1.40;
        private const int MinMouthOpenMs = 80;
        private const int MouthCooldownMs = 800;
        private const int MouthDiagLogIntervalMs = 3000;
        private static readonly int[] MarVerticalPairs = { 81, 178, 13, 14, 311, 402 };
        private const int MouthCornerLeftIdx = 78;
        private const int MouthCornerRightIdx = 308;

        // Tongue-out detection — HSV color heuristic on pixels inside the inner-
        // lip polygon. Only runs when MAR says the mouth is currently open, so
        // closed-mouth lipstick can't false-fire. The 16 indices below trace the
        // inner-lip ring clockwise from upper-center (canonical MediaPipe
        // FaceMesh layout). Per-pixel HSV bands and the open/close ratio are
        // tuned for typical webcam lighting; documented limitation: red lipstick
        // on the inner lip can false-fire (would need a dedicated model to fix).
        private static readonly int[] InnerLipPolygonIndices =
        {
            13, 312, 311, 310, 415, 308, 324, 318, 402, 317,
            14, 87, 178, 88, 95, 78, 191, 80, 81, 82
        };
        private const double TongueEnterRatio = 0.22;   // tongue_px / valid_px
        private const double TongueLeaveRatio = 0.14;
        private const int MinTongueOutMs = 150;
        private const int TongueCooldownMs = 700;
        private const int TongueDiagLogIntervalMs = 3000;
        // HSV bands (OpenCV: H ∈ [0,179], S/V ∈ [0,255]). Tuned loose because
        // consumer webcams often desaturate skin/tongue tones — a strict S>80
        // gate misclassifies real tongue pixels as "other". Sat/val floor was
        // 50/50 — bumped down because users with dim or color-corrected
        // cameras had the detector miss real protrusions entirely.
        private const int TongueHueLow1 = 0;
        private const int TongueHueHigh1 = 20;
        private const int TongueHueLow2 = 160;
        private const int TongueHueHigh2 = 179;
        private const int TongueMinSat = 30;
        private const int TongueMinVal = 40;
        private const int TeethMinVal = 200;            // bright + low sat = teeth (excluded)
        private const int TeethMaxSat = 50;
        private const int ShadowMaxVal = 40;            // very dark = shadow / mouth interior void (excluded)

        // Gaze parameters
        private const int GazeBufferSize = 30;
        private const int LongStareDurationMs = 3000;
        private const double LongStareMaxDeviationPx = 60.0;
        private const int LongStareCooldownMs = 5000;
        private const int IrisSmoothFrames = 12;             // rolling-mean window over raw iris vector (~400ms at ~30fps)
        private const int SideStabilityFrames = 3;           // consecutive frames of same classification before emitting (filters Center pass-through)

        // One-Euro filter tunables for the cursor-projection path. The rolling-mean
        // buffer above still feeds the gaze-side classifier (which has its own
        // hysteresis and benefits from a smoother lagging signal). One-Euro is
        // velocity-adaptive: tight cutoff at fixation kills jitter, loose cutoff
        // during saccades avoids lag. Defaults from Casiez 2012 retuned for ~30 Hz
        // gaze sampling — bump Beta if the cursor still feels laggy on fast eye
        // movement; raise MinCutoff if it still wobbles when the user is fixating.
        // MinCutoff at 1.4 Hz is on the smooth side of the Casiez sweet-spot
        // because webcam iris detection has more frame-to-frame jitter than the
        // touch-input scenarios the original paper targeted.
        private const double OneEuroMinCutoff = 1.4;
        private const double OneEuroBeta = 0.007;
        private const double OneEuroDCutoff = 1.0;

        private readonly object _stateLock = new();
        private volatile bool _disposed;

        // Backed by a volatile field so the capture thread's IsRunning checks
        // and other readers across threads see writes from Start/Stop without
        // having to take _stateLock on every poll. The state-machine writes
        // themselves still go through SetState while _stateLock is held by
        // Start/Stop, so the value never tears between visible snapshots.
        private volatile WebcamTrackingState _state = WebcamTrackingState.Stopped;
        public WebcamTrackingState State => _state;
        // Set when ONNX runtime init fails for a reason that will keep failing this session
        // (ETW blocked, missing redist, OS-level restriction). Prevents Start() from
        // hammering OrtEnv.CreateInstance over and over.
        private volatile bool _modelInitPermanentlyFailed;
        public bool IsRunning => _state == WebcamTrackingState.Tracking || _state == WebcamTrackingState.FaceLost;
        public WebcamCalibrationData? Calibration { get; private set; }

        public event Action? OnBlink;
        public event Action<System.Windows.Point>? OnLongStare;
        public event Action? OnMouthOpen;
        public event Action? OnTongueOut;
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

        // Mouth state (mirrors blink state machine)
        private DateTime _lastMouthOpenAt = DateTime.MinValue;
        private readonly Queue<double> _marBuffer = new();
        private double _marBaseline;
        private bool _mouthOpen;
        private DateTime? _mouthOpenedAt;
        private double _maxMarThisOpening;
        private double _windowMinMar = double.MaxValue;
        private double _windowMaxMar = double.MinValue;
        private DateTime _lastMouthDiagAt = DateTime.MinValue;
        private int _mouthOpenCount;

        // Tongue state (gated on _mouthOpen)
        private DateTime _lastTongueOutAt = DateTime.MinValue;
        private bool _tongueOut;
        private DateTime? _tongueOutSince;
        private double _maxTongueRatioThisFire;
        private double _windowMaxTongueRatio;
        private DateTime _lastTongueDiagAt = DateTime.MinValue;
        private int _tongueOutCount;
        // Per-window class accumulators for the tongue diag log — totals across
        // all sampled frames since the last log emission. Lets us see whether
        // tongue is being misclassified as "other" (loosen sat) or as "shadow"
        // (loosen ShadowMaxVal), instead of guessing.
        private long _diagTongueSum, _diagTeethSum, _diagShadowSum, _diagOtherSum;
        private int _diagTongueFrames;
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
        // Left↔Right around the classifier midpoint. Feeds the side classifier;
        // the cursor-projection path uses the One-Euro filters below.
        private readonly Queue<double> _irisDxSmoothBuffer = new();
        private readonly Queue<double> _irisDySmoothBuffer = new();

        // Velocity-adaptive smoothing for the cursor-projection path. See
        // OneEuroMinCutoff/Beta/DCutoff above for tuning notes.
        private readonly OneEuroFilter _irisDxFilter = new(OneEuroMinCutoff, OneEuroBeta, OneEuroDCutoff);
        private readonly OneEuroFilter _irisDyFilter = new(OneEuroMinCutoff, OneEuroBeta, OneEuroDCutoff);

        // Head-pose state — solvePnP-derived yaw/pitch (radians), smoothed to
        // match iris smoothing. Used to apply a geometric correction on the
        // iris vector before projecting through the polynomial: when the head
        // turns off the calibration baseline, the eyes have to counter-rotate
        // to keep looking at the same screen point, so the iris vector shifts
        // even though gaze didn't move. Subtracting a sin(deltaPose)-scaled
        // offset puts the cursor back roughly where gaze actually points.
        private readonly Queue<double> _yawSmoothBuffer = new();
        private readonly Queue<double> _pitchSmoothBuffer = new();
        private bool _headPoseValid;

        /// <summary>
        /// Smoothed head yaw in radians. Sign and magnitude follow whatever
        /// solvePnP returns for the canonical 3D model below — empirical, used
        /// as a relative measure (delta from calibration baseline).
        /// </summary>
        public double LastYaw { get; private set; }
        public double LastPitch { get; private set; }
        public bool HasHeadPose => _headPoseValid;

        /// <summary>Fires every processed frame with the latest smoothed (yaw, pitch). Used by the calibration window to capture the baseline.</summary>
        public event Action<double, double>? OnHeadPose;

        // Geometric correction applied to the iris vector when a baseline pose
        // is recorded in the calibration:
        //   ix' = ix + AxYaw * sin(Δyaw) + AxPitch * sin(Δpitch)
        //   iy' = iy + AyYaw * sin(Δyaw) + AyPitch * sin(Δpitch)
        // Coefficients are fit empirically per calibration (in
        // WebcamCalibrationWindow.FitHeadPoseComp) from the natural head-pose
        // variance during sampling, so sign and magnitude are correct by
        // construction for this camera/face. Comp is skipped when the
        // calibration didn't include a fit (older calibrations, or the user
        // held perfectly still during sampling so R² was below threshold).

        // Canonical 3D face model (mm-ish, dlib/OpenCV head-pose tutorial
        // values). Anchored at the nose tip; +X to subject's left (image
        // right on an unmirrored frame), +Y up, +Z toward camera. solvePnP
        // figures out the rotation that maps these to the per-frame 2D
        // landmarks, and we extract Euler yaw/pitch from that.
        private static readonly Point3f[] HeadPoseModelPoints = new[]
        {
            new Point3f(0f,     0f,     0f),       // 0: nose tip      (FaceMesh idx 1)
            new Point3f(0f,     -330f,  -65f),     // 1: chin          (FaceMesh idx 152)
            new Point3f(225f,   170f,   -135f),    // 2: subject's left eye outer  (FaceMesh idx 33)
            new Point3f(-225f,  170f,   -135f),    // 3: subject's right eye outer (FaceMesh idx 263)
            new Point3f(150f,   -150f,  -125f),    // 4: subject's left mouth corner  (FaceMesh idx 61)
            new Point3f(-150f,  -150f,  -125f),    // 5: subject's right mouth corner (FaceMesh idx 291)
        };
        private static readonly int[] HeadPoseLandmarkIndices = new[] { 1, 152, 33, 263, 61, 291 };

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
                if (!IsConsentCurrent())
                {
                    App.Logger?.Information("WebcamTrackingService: Start() refused — consent not current (granted={Granted}, storedVersion={Stored}, currentVersion={Current})",
                        App.Settings?.Current?.WebcamConsentGiven, App.Settings?.Current?.WebcamConsentVersion, ConsentVersion);
                    return false;
                }
                if (_modelInitPermanentlyFailed)
                {
                    // ONNX runtime can't initialize on this machine (ETW blocked, missing
                    // VC++ redist, etc). No point retrying every time the user clicks Start.
                    App.Logger?.Information("WebcamTrackingService: Start() refused — ONNX model load previously failed in a way that won't recover this session");
                    SetState(WebcamTrackingState.Error);
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

            // Wait for the capture thread to exit before releasing any native
            // handle it might still be using. Disposing _capture or the ONNX
            // InferenceSessions while the thread is inside VideoCapture.Read
            // or InferenceSession.Run is a guaranteed native AV. If the thread
            // is wedged (driver hang, USB stall, slow inference), we'd rather
            // leak the handles than crash — the process will free them on
            // exit, and the user gets an Error state instead of a dialog.
            if (thread != null)
            {
                if (!thread.Join(TimeSpan.FromSeconds(2)))
                {
                    App.Logger?.Warning("WebcamTrackingService: capture thread did not exit within 2s — extending wait");
                    if (!thread.Join(TimeSpan.FromSeconds(3)))
                    {
                        App.Logger?.Error("WebcamTrackingService: capture thread did not exit within 5s total — leaving native handles alive to avoid disposal race");
                        lock (_stateLock)
                        {
                            SetState(WebcamTrackingState.Error);
                        }
                        return;
                    }
                }
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

        /// <summary>
        /// Atomically replace the live calibration's <see cref="WebcamCalibrationData.RuntimeOffset"/>
        /// (the post-projection nudge captured by Quick Recal) with a new value.
        /// Mutating the live instance in place would race the capture thread,
        /// which reads the offset every frame; this clones the calibration so
        /// readers always see a consistent snapshot. Pass null to clear.
        /// </summary>
        public void SetRuntimeOffset(RuntimeOffsetData? offset, bool persist)
        {
            lock (_stateLock)
            {
                var current = Calibration;
                if (current == null) return;
                var updated = current.WithRuntimeOffset(offset);
                if (persist) updated.Save();
                Calibration = updated;
            }
            App.Logger?.Information("WebcamTrackingService: runtime offset {State} (persist={Persist})",
                offset == null ? "cleared" : "set", persist);
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

        /// <summary>
        /// Returns the connected video-capture devices in DirectShow's
        /// enumeration order. The Index field is what gets passed to
        /// VideoCapture; the Name field is the OS-reported FriendlyName,
        /// useful for letting users disambiguate physical webcams from
        /// virtual cameras (OBS, Snap, etc.).
        /// </summary>
        public IReadOnlyList<WebcamDeviceEnumerator.WebcamDevice> EnumerateDevices()
        {
            var devices = WebcamDeviceEnumerator.Enumerate();
            if (devices.Count == 0)
            {
                App.Logger?.Information("WebcamTrackingService: no video-capture devices found via DirectShow enumeration");
            }
            else
            {
                App.Logger?.Information("WebcamTrackingService: {Count} video-capture device(s) detected: {Names}",
                    devices.Count, string.Join(" | ", devices.Select(d => $"[{d.Index}] {d.Name}")));
            }
            return devices;
        }

        private bool TryOpenCamera()
        {
            try
            {
                int configured = App.Settings?.Current?.WebcamDeviceIndex ?? -1;
                int deviceIndex = configured >= 0 ? configured : 0;

                // Snapshot the current device list so we can log which physical
                // device the configured index points at — and warn if the
                // configured name no longer matches (USB reorder, virtual cam
                // installed, etc.).
                var devices = WebcamDeviceEnumerator.Enumerate();
                string detectedName = "(unknown)";
                if (devices.Count > 0)
                {
                    if (deviceIndex >= devices.Count)
                    {
                        App.Logger?.Warning(
                            "WebcamTrackingService: configured device index {Configured} is out of range ({Count} devices present); falling back to 0",
                            deviceIndex, devices.Count);
                        deviceIndex = 0;
                    }
                    detectedName = devices[deviceIndex].Name;

                    string savedName = App.Settings?.Current?.WebcamDeviceName ?? "";
                    if (!string.IsNullOrEmpty(savedName) && !string.Equals(savedName, detectedName, StringComparison.Ordinal))
                    {
                        App.Logger?.Warning(
                            "WebcamTrackingService: device at index {Index} is '{Detected}', but settings remembered '{Saved}' — enumeration order may have shifted",
                            deviceIndex, detectedName, savedName);
                    }
                }
                else if (configured >= 0)
                {
                    App.Logger?.Warning("WebcamTrackingService: no devices enumerated but settings remember index {Index} — opening anyway", configured);
                }

                App.Logger?.Information("WebcamTrackingService: opening device index {Index} ('{Name}') with MSMF", deviceIndex, detectedName);
                var cap = new VideoCapture(deviceIndex, VideoCaptureAPIs.MSMF);
                if (!cap.IsOpened())
                {
                    cap.Dispose();
                    App.Logger?.Information("WebcamTrackingService: MSMF open failed for index {Index}, retrying with DSHOW", deviceIndex);
                    cap = new VideoCapture(deviceIndex, VideoCaptureAPIs.DSHOW);
                    if (!cap.IsOpened())
                    {
                        cap.Dispose();
                        App.Logger?.Warning(
                            "WebcamTrackingService: VideoCapture.Open returned false on both MSMF and DSHOW for device index {Index} ('{Name}')",
                            deviceIndex, detectedName);
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
                    App.Logger?.Warning(
                        "WebcamTrackingService: probe frame failed for device index {Index} ('{Name}') — camera may be in use by another app",
                        deviceIndex, detectedName);
                    SetState(WebcamTrackingState.CameraInUse);
                    return false;
                }

                _capture = cap;
                App.Logger?.Information("WebcamTrackingService: device {Index} ('{Name}') opened successfully", deviceIndex, detectedName);
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
            // Build into locals first and only publish to fields once all three
            // detectors construct successfully. If the second or third ctor
            // throws, the partially-constructed earlier ones get disposed in
            // the catch — otherwise their ONNX InferenceSessions and Mat
            // buffers leak until process exit (Start's caller calls
            // ReleaseCapture but not ReleaseModels on this failure path).
            BlazeFaceDetector? face = null;
            FaceMeshDetector? mesh = null;
            IrisDetector? iris = null;
            try
            {
                face = new BlazeFaceDetector(faceModelPath, faceAnchorsPath);
                mesh = new FaceMeshDetector(meshModelPath);
                iris = new IrisDetector(irisModelPath);

                _faceDetector = face;
                _faceMesh = mesh;
                _irisDetector = iris;
                App.Logger?.Information("WebcamTrackingService: BlazeFace + FaceMesh + Iris loaded");
                return true;
            }
            catch (Exception ex)
            {
                // Some failure modes are environmental and won't recover until the user
                // changes something on their machine — don't keep retrying them on every
                // click of Start. The most common one we see in bug reports is ETW
                // registration failing (HRESULT 0x8007046E / -2147024786 or 0x80070776),
                // which means the Event Tracing for Windows service is restricted by
                // Group Policy or AV software. ONNX Runtime requires it.
                var msg = ex.Message ?? string.Empty;
                var isEtwFailure = msg.IndexOf("ETW", StringComparison.OrdinalIgnoreCase) >= 0
                    || msg.IndexOf("etw_sink", StringComparison.OrdinalIgnoreCase) >= 0;
                if (isEtwFailure)
                {
                    _modelInitPermanentlyFailed = true;
                    App.Logger?.Warning(
                        "WebcamTrackingService: ONNX Runtime cannot initialize because Windows Event Tracing (ETW) is restricted on this machine. This is usually caused by Group Policy, antivirus, or a disabled Event Log service. Webcam tracking will remain unavailable until that's resolved.");
                }
                else
                {
                    App.Logger?.Warning(ex, "WebcamTrackingService: failed to load detection models");
                }
                try { face?.Dispose(); } catch { }
                try { mesh?.Dispose(); } catch { }
                try { iris?.Dispose(); } catch { }
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
            _marBuffer.Clear();
            _marBaseline = 0;
            _mouthOpen = false;
            _mouthOpenedAt = null;
            _maxMarThisOpening = 0;
            _windowMinMar = double.MaxValue;
            _windowMaxMar = double.MinValue;
            _lastMouthDiagAt = DateTime.MinValue;
            _mouthOpenCount = 0;
            _tongueOut = false;
            _tongueOutSince = null;
            _maxTongueRatioThisFire = 0;
            _windowMaxTongueRatio = 0;
            _lastTongueDiagAt = DateTime.MinValue;
            _tongueOutCount = 0;
            _diagTongueSum = _diagTeethSum = _diagShadowSum = _diagOtherSum = 0;
            _diagTongueFrames = 0;
            _irisDxSmoothBuffer.Clear();
            _irisDySmoothBuffer.Clear();
            _irisDxFilter.Reset();
            _irisDyFilter.Reset();
            _yawSmoothBuffer.Clear();
            _pitchSmoothBuffer.Clear();
            _headPoseValid = false;
            LastYaw = 0;
            LastPitch = 0;
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
                    // Snapshot field references locally so a stray Stop/Dispose
                    // ordering bug or future code change can't null them out
                    // between the check and the dereference. Stop() now waits
                    // for this thread to exit before calling ReleaseCapture /
                    // ReleaseModels, so in normal flow these stay non-null for
                    // the entire iteration; the snapshot is defense in depth.
                    var capture = _capture;
                    var faceDet = _faceDetector;
                    var faceMesh = _faceMesh;
                    var irisDet = _irisDetector;
                    if (capture == null || faceDet == null || faceMesh == null || irisDet == null) break;

                    if (!capture.Read(frame) || frame.Empty())
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
                        // Debug, not Information — fires every ~10s during capture so it
                        // would otherwise dominate app-.log and ride along on bug reports
                        // (LogScrubber doesn't strip webcam telemetry). Lifecycle entries
                        // (started/stopped/exited) stay at Information.
                        App.Logger?.Debug("WebcamTrackingService: {Frames} frames processed, ~{Fps:F1} fps", processedFrames, fps);
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

            HandleFaceFound();

            // 3a) Head pose — solvePnP against the canonical 3D face model
            //     using 6 stable FaceMesh landmarks. Updates LastYaw/LastPitch
            //     (smoothed) and _headPoseValid. Used downstream by the gaze
            //     projection to apply a geometric correction relative to the
            //     calibration baseline.
            UpdateHeadPose(landmarks, gray.Width, gray.Height);

            // 4) Iris model — exact iris-center landmark per eye AND 71-point
            //    eye contour for EAR. Right eye is fed flipped by IrisDetector,
            //    output un-flipped, so contour indices are consistent across eyes.
            var leftEye  = _irisDetector!.Detect(bgr, landmarks[LeftEyeOuterIdx],  landmarks[LeftEyeInnerIdx],  isRightEye: false);
            var rightEye = _irisDetector!.Detect(bgr, landmarks[RightEyeOuterIdx], landmarks[RightEyeInnerIdx], isRightEye: true);
            if (leftEye == null && rightEye == null) return;

            // 5) EAR-based blink detection on iris-model contour (more responsive
            //    to closure than FaceMesh's eyelid landmarks).
            if (leftEye != null && rightEye != null)
            {
                double earL = ComputeEAR(leftEye.Contour, IrisContourEarIndices);
                double earR = ComputeEAR(rightEye.Contour, IrisContourEarIndices);
                UpdateBlinkState(earL, earR);
            }

            // 5a) MAR-based mouth-open detection on FaceMesh inner-lip landmarks.
            //     Tongue heuristic runs only if mouth is currently open (gated
            //     inside UpdateTongueState). Both share the lip polygon math.
            double mar = ComputeMAR(landmarks);
            UpdateMouthState(mar);
            UpdateTongueState(bgr, landmarks);

            // 6) Iris-vector for gaze (head-pose stable, normalized against
            //    eye-corner midpoint and scaled by corner-to-corner distance).
            double sumDx = 0, sumDy = 0;
            int count = 0;
            if (leftEye != null)
            {
                var v = NormalizeIrisVector(leftEye.IrisCenter, landmarks[LeftEyeOuterIdx], landmarks[LeftEyeInnerIdx]);
                sumDx += v.Dx; sumDy += v.Dy; count++;
            }
            if (rightEye != null)
            {
                var v = NormalizeIrisVector(rightEye.IrisCenter, landmarks[RightEyeOuterIdx], landmarks[RightEyeInnerIdx]);
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

        /// <summary>
        /// Run solvePnP on 6 stable FaceMesh landmarks (nose tip, chin, both
        /// outer eye corners, both mouth corners) against the canonical 3D
        /// model, extract Euler yaw/pitch from the rotation matrix, and feed
        /// the smoothing buffers. Failures (degenerate landmarks, solvePnP
        /// returning false) leave the previous smoothed value in place — the
        /// downstream comp falls back to "no compensation" via _headPoseValid.
        /// </summary>
        private void UpdateHeadPose(float[][] landmarks, int frameW, int frameH)
        {
            if (landmarks == null || landmarks.Length < 468) { _headPoseValid = false; return; }

            try
            {
                // 2D image points matching HeadPoseModelPoints, in source-frame
                // pixel coords. Drop in early if any landmark is missing/NaN.
                var imagePoints = new Point2f[HeadPoseLandmarkIndices.Length];
                for (int i = 0; i < HeadPoseLandmarkIndices.Length; i++)
                {
                    var lm = landmarks[HeadPoseLandmarkIndices[i]];
                    if (lm == null || lm.Length < 2) { _headPoseValid = false; return; }
                    if (float.IsNaN(lm[0]) || float.IsNaN(lm[1])) { _headPoseValid = false; return; }
                    imagePoints[i] = new Point2f(lm[0], lm[1]);
                }

                // Pinhole approximation: focal length ≈ frame width, principal
                // point at frame center, no distortion. Good enough for
                // consumer webcams; the absolute angles aren't important —
                // only deltas relative to calibration are.
                double fx = frameW;
                double fy = frameW;
                double pcx = frameW / 2.0;
                double pcy = frameH / 2.0;
                using var cameraMatrix = new Mat(3, 3, MatType.CV_64FC1);
                cameraMatrix.Set(0, 0, fx); cameraMatrix.Set(0, 1, 0.0); cameraMatrix.Set(0, 2, pcx);
                cameraMatrix.Set(1, 0, 0.0); cameraMatrix.Set(1, 1, fy); cameraMatrix.Set(1, 2, pcy);
                cameraMatrix.Set(2, 0, 0.0); cameraMatrix.Set(2, 1, 0.0); cameraMatrix.Set(2, 2, 1.0);
                using var distCoeffs = new Mat(4, 1, MatType.CV_64FC1, new double[] { 0, 0, 0, 0 });
                using var objPoints = InputArray.Create(HeadPoseModelPoints);
                using var imgPoints = InputArray.Create(imagePoints);
                using var rvec = new Mat();
                using var tvec = new Mat();

                // SolvePnP overload returns void in this OpenCvSharp version
                // — failures throw or leave rvec degenerate, and we catch both
                // via the try/catch + downstream NaN guard.
                Cv2.SolvePnP(objPoints, imgPoints, cameraMatrix, distCoeffs, rvec, tvec,
                    useExtrinsicGuess: false, flags: SolvePnPFlags.Iterative);
                if (rvec.Empty() || rvec.Total() < 3) { _headPoseValid = false; return; }

                using var rotMat = new Mat();
                Cv2.Rodrigues(rvec, rotMat);

                // Euler-angle extraction from R (Y-X-Z order, picking yaw=Y,
                // pitch=X). Values in radians. Sign convention is whatever
                // solvePnP gave us — irrelevant for the relative-delta path
                // we use downstream.
                double r20 = rotMat.At<double>(2, 0);
                double r21 = rotMat.At<double>(2, 1);
                double r22 = rotMat.At<double>(2, 2);
                double pitch = Math.Atan2(r21, r22);
                double yaw   = Math.Atan2(-r20, Math.Sqrt(r21 * r21 + r22 * r22));

                if (double.IsNaN(yaw) || double.IsNaN(pitch)) { _headPoseValid = false; return; }

                EnqueueWithCap(_yawSmoothBuffer,   yaw,   IrisSmoothFrames);
                EnqueueWithCap(_pitchSmoothBuffer, pitch, IrisSmoothFrames);
                double sumY = 0, sumP = 0;
                foreach (var v in _yawSmoothBuffer)   sumY += v;
                foreach (var v in _pitchSmoothBuffer) sumP += v;
                LastYaw   = sumY / _yawSmoothBuffer.Count;
                LastPitch = sumP / _pitchSmoothBuffer.Count;
                _headPoseValid = true;

                var emitYaw = LastYaw;
                var emitPitch = LastPitch;
                Dispatch(() => OnHeadPose?.Invoke(emitYaw, emitPitch));
            }
            catch (Exception ex)
            {
                _headPoseValid = false;
                App.Logger?.Debug("UpdateHeadPose failed: {Error}", ex.Message);
            }
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
            _headPoseValid = false;
            _yawSmoothBuffer.Clear();
            _pitchSmoothBuffer.Clear();
            _eyesClosed = false;
            _eyesClosedAt = null;
            _mouthOpen = false;
            _mouthOpenedAt = null;
            _tongueOut = false;
            _tongueOutSince = null;
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
            // Suppress all gaze emission while the eyes are closed. The iris
            // model still returns an iris-center landmark on a closing eye, but
            // the eye-corner reference frame collapses vertically as the lid
            // descends, so the normalized iris vector goes haywire — the
            // visualization dot bobs up/down on every blink, and calibration
            // sampling absorbs garbage frames if the user blinks while looking
            // at a dot. Holding the last emitted state through the blink fixes
            // both. The gaze-side stability buffer and screen-projection state
            // resume cleanly when the eyes reopen.
            if (_eyesClosed) return;

            // Raw iris vector — used by the calibration window to sample reference
            // points. Always fires (when eyes are open); calibration consumers
            // expect a per-frame event during sampling.
            Dispatch(() => OnRawIris?.Invoke(irisDx, irisDy));

            // Two-output smoothing split:
            //   sideSmooth*  — rolling mean → ClassifyGazeSide. The side classifier
            //                  has its own hysteresis (asymmetric enter/leave bands)
            //                  and stability filter; lagging is fine here, what
            //                  matters is suppressing the per-frame ~5% iris wobble
            //                  so the midpoint flicker doesn't re-trigger.
            //   cursorSmooth* — One-Euro filter → polynomial projection. Tight
            //                   cutoff at fixation (kills jitter when the user is
            //                   trying to hold a point), wider cutoff during fast
            //                   eye movement (no lag on saccades). Without this
            //                   the cursor wobbled visibly even when the user
            //                   reported sitting perfectly still.
            EnqueueWithCap(_irisDxSmoothBuffer, irisDx, IrisSmoothFrames);
            EnqueueWithCap(_irisDySmoothBuffer, irisDy, IrisSmoothFrames);
            double sumX = 0, sumY = 0;
            foreach (var v in _irisDxSmoothBuffer) sumX += v;
            foreach (var v in _irisDySmoothBuffer) sumY += v;
            var sideSmoothDx = sumX / _irisDxSmoothBuffer.Count;

            var nowTicks = Stopwatch.GetTimestamp();
            var smoothDx = _irisDxFilter.Filter(irisDx, nowTicks);
            var smoothDy = _irisDyFilter.Filter(irisDy, nowTicks);

            var classifiedSide = ClassifyGazeSide(sideSmoothDx);

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

            // Head-pose compensation was retired here. The PnP head-pose
            // estimator from face landmarks is noisier than the natural head
            // movement during normal use, so applying empirically-fit comp
            // coefficients was injecting variance instead of removing it
            // (consistent with Funes Mora & Odobez 2013, who observed the
            // same below ~5° rotation noise). The polynomial fit is fed the
            // raw smoothed iris vector. BaselineHeadPose / HeadPoseComp on
            // legacy calibrations are simply ignored.
            var screenPoint = ProjectGazeToScreen(smoothDx, smoothDy);
            if (screenPoint.HasValue)
            {
                var p = screenPoint.Value;
                // Quick-recal translational nudge — corrects whole-map drift
                // captured after the user clicked "Quick Recal" on a center
                // dot. Null on calibrations that haven't run quick-recal yet.
                if (Calibration?.RuntimeOffset is { } off)
                {
                    p = new System.Windows.Point(p.X + off.Dx, p.Y + off.Dy);
                }

                // Clamp to monitor bounds so the cursor sticks at the edge
                // rather than disappearing off-screen when the user glances
                // past the bezel. Without this, looking at the wall above
                // the monitor projects to negative Y and the cursor drops
                // out of sight; the user can't tell whether tracking is
                // still working or has lost the face. EdgePad keeps the
                // cursor visualization (typically 14–36 DIP) wholly visible.
                if (Calibration?.MonitorBounds is { } bounds && bounds.Width > 0 && bounds.Height > 0)
                {
                    const double EdgePad = 4.0;
                    var maxX = bounds.Width - EdgePad;
                    var maxY = bounds.Height - EdgePad;
                    p = new System.Windows.Point(
                        Math.Max(EdgePad, Math.Min(p.X, maxX)),
                        Math.Max(EdgePad, Math.Min(p.Y, maxY)));
                }
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
            // Prefer 2nd-order polynomial when present — captures the
            // nonlinear iris→screen response. Falls back to homography for
            // calibrations saved before the polynomial fit was added.
            //
            // Two polynomial forms are accepted for forward/backward compat:
            //   7 coeffs (current): Cerrolaza asymmetric form — adds ix²·iy
            //                       to X and iy²·ix to Y, ~0.15-0.25° better
            //                       than the symmetric form on webcam grids.
            //   6 coeffs (legacy): symmetric 2nd-order — projection still
            //                       works, just lacks the asymmetric term.
            var poly = Calibration?.Polynomial;
            if (poly != null && poly.X != null && poly.Y != null
                && poly.X.Length == poly.Y.Length
                && (poly.X.Length == 6 || poly.X.Length == 7))
            {
                var ix2 = irisDx * irisDx;
                var iy2 = irisDy * irisDy;
                var ixy = irisDx * irisDy;
                double x, y;
                if (poly.X.Length == 7)
                {
                    // [1, ix, iy, ix·iy, ix², iy², ix²·iy] for X
                    // [1, ix, iy, ix·iy, ix², iy², iy²·ix] for Y
                    x = poly.X[0] + poly.X[1] * irisDx + poly.X[2] * irisDy
                      + poly.X[3] * ixy + poly.X[4] * ix2 + poly.X[5] * iy2
                      + poly.X[6] * ix2 * irisDy;
                    y = poly.Y[0] + poly.Y[1] * irisDx + poly.Y[2] * irisDy
                      + poly.Y[3] * ixy + poly.Y[4] * ix2 + poly.Y[5] * iy2
                      + poly.Y[6] * iy2 * irisDx;
                }
                else
                {
                    // Legacy symmetric form: [1, ix, iy, ix², iy², ix·iy]
                    x = poly.X[0] + poly.X[1] * irisDx + poly.X[2] * irisDy
                      + poly.X[3] * ix2 + poly.X[4] * iy2 + poly.X[5] * ixy;
                    y = poly.Y[0] + poly.Y[1] * irisDx + poly.Y[2] * irisDy
                      + poly.Y[3] * ix2 + poly.Y[4] * iy2 + poly.Y[5] * ixy;
                }
                return new System.Windows.Point(x, y);
            }

            var h = Calibration?.Homography;
            if (h == null || h.Length != 3 || h[0].Length != 3) return null;

            var hx = h[0][0] * irisDx + h[0][1] * irisDy + h[0][2];
            var hy = h[1][0] * irisDx + h[1][1] * irisDy + h[1][2];
            var hw = h[2][0] * irisDx + h[2][1] * irisDy + h[2][2];
            if (Math.Abs(hw) < 1e-9) return null;

            return new System.Windows.Point(hx / hw, hy / hw);
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
            if (_state == state) return;
            _state = state;
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

            // Update rolling baseline. We use the 90th percentile of the buffer
            // (NOT the max) because raising eyebrows / surprised expressions
            // briefly spike EAR way above the user's normal open-eye value.
            // A single spike pollutes a max-baseline for 3 seconds, making the
            // closed threshold unreachable. The 90th percentile rejects those
            // outliers while still tracking the user's normal open-eye EAR.
            EnqueueWithCap(_earBuffer, avgEar, EarBaselineFrames);
            _earBaseline = PercentileOf(_earBuffer, 0.90);

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
                // Debug, not Information — per-event blink data is behavioral
                // biometric correlate (eye-aspect-ratio, closure timing). Keeping
                // it out of persisted logs / bug reports honors the privacy
                // contract at the top of this file.
                App.Logger?.Debug(
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
            // Debug, not Information — periodic EAR baseline diagnostics are
            // tuning aids, not lifecycle events. Same privacy reasoning as the
            // per-blink log above.
            App.Logger?.Debug(
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

        private static double PercentileOf(Queue<double> q, double pct)
        {
            if (q.Count == 0) return 0;
            var arr = q.ToArray();
            Array.Sort(arr);
            int idx = (int)(arr.Length * pct);
            if (idx >= arr.Length) idx = arr.Length - 1;
            if (idx < 0) idx = 0;
            return arr[idx];
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  MAR (Mouth Aspect Ratio) mouth-open detection.
        //  Pattern matches EAR: rolling 90-frame buffer, 90th-percentile baseline
        //  (rejects yawn / surprise spikes), hysteresis enter at 1.8× baseline,
        //  leave at 1.4×, min open window 80 ms, cooldown 800 ms. Diag log
        //  every ~3 s (counts only — privacy contract).
        // ─────────────────────────────────────────────────────────────────────────
        private static double ComputeMAR(float[][] landmarks)
        {
            // Three vertical chord pairs averaged, normalized by corner-to-corner
            // width. Direct port of the standard MAR formula.
            double v1 = Distance(landmarks[MarVerticalPairs[0]], landmarks[MarVerticalPairs[1]]);
            double v2 = Distance(landmarks[MarVerticalPairs[2]], landmarks[MarVerticalPairs[3]]);
            double v3 = Distance(landmarks[MarVerticalPairs[4]], landmarks[MarVerticalPairs[5]]);
            double w  = Distance(landmarks[MouthCornerLeftIdx], landmarks[MouthCornerRightIdx]);
            return w > 1e-6 ? (v1 + v2 + v3) / (3.0 * w) : 0.0;
        }

        private void UpdateMouthState(double mar)
        {
            EnqueueWithCap(_marBuffer, mar, MarBaselineFrames);
            // Closed-mouth values dominate the buffer; the 10th percentile
            // approximates the resting closed MAR. We compare current MAR to
            // that closed baseline (e.g. >1.8× resting = open).
            _marBaseline = PercentileOf(_marBuffer, 0.10);

            if (mar < _windowMinMar) _windowMinMar = mar;
            if (mar > _windowMaxMar) _windowMaxMar = mar;

            var now = DateTime.UtcNow;
            MaybeLogMouthDiag(now);

            if (_marBuffer.Count < MarMinSamplesForBaseline) return;
            if (_marBaseline <= 0) return;

            bool nowOpen = _mouthOpen
                ? mar > MarCloseRatio * _marBaseline
                : mar > MarOpenRatio  * _marBaseline;

            if (nowOpen && !_mouthOpen)
            {
                _mouthOpenedAt = now;
                _maxMarThisOpening = mar;
            }
            else if (nowOpen)
            {
                if (mar > _maxMarThisOpening) _maxMarThisOpening = mar;
                // Fire OnMouthOpen once per open window, after MinMouthOpenMs has
                // elapsed and cooldown allows it. Edge-trigger so a long-held
                // open mouth doesn't spam the event.
                if (_mouthOpenedAt.HasValue && _lastMouthOpenAt < _mouthOpenedAt.Value)
                {
                    var openMs = (now - _mouthOpenedAt.Value).TotalMilliseconds;
                    if (openMs >= MinMouthOpenMs
                        && (now - _lastMouthOpenAt).TotalMilliseconds >= MouthCooldownMs)
                    {
                        _lastMouthOpenAt = now;
                        _mouthOpenCount++;
                        Dispatch(() => OnMouthOpen?.Invoke());
                        // Debug, not Information — see blink fire log above for reasoning.
                        App.Logger?.Debug(
                            "WebcamTrackingService: mouth-open #{N} FIRED (open for {Ms:F0}ms, baseline MAR={Base:F3}, max MAR during open={Max:F3}, ratio={Ratio:F2}× baseline)",
                            _mouthOpenCount, openMs, _marBaseline, _maxMarThisOpening,
                            _maxMarThisOpening / _marBaseline);
                    }
                }
            }
            else if (!nowOpen && _mouthOpen)
            {
                _mouthOpenedAt = null;
                _maxMarThisOpening = 0;
            }

            _mouthOpen = nowOpen;
        }

        private void MaybeLogMouthDiag(DateTime now)
        {
            if (_lastMouthDiagAt == DateTime.MinValue) { _lastMouthDiagAt = now; return; }
            if ((now - _lastMouthDiagAt).TotalMilliseconds < MouthDiagLogIntervalMs) return;
            _lastMouthDiagAt = now;

            double openThreshold = _marBaseline * MarOpenRatio;
            double closeThreshold = _marBaseline * MarCloseRatio;
            double winMaxRatio = _marBaseline > 0 ? _windowMaxMar / _marBaseline : 0;
            // Debug, not Information — see blink-diag for reasoning.
            App.Logger?.Debug(
                "WebcamTrackingService: mouth-diag baseline={Base:F3} openThr={OT:F3} closeThr={CT:F3} winMin={WMin:F3} winMax={WMax:F3}({WMaxR:P0}) state={State} mouthOpens={N} samples={S}",
                _marBaseline, openThreshold, closeThreshold,
                _windowMinMar, _windowMaxMar, winMaxRatio,
                _mouthOpen ? "OPEN" : "closed", _mouthOpenCount, _marBuffer.Count);

            _windowMinMar = double.MaxValue;
            _windowMaxMar = double.MinValue;
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  Tongue-out detection (HSV color heuristic, gated on _mouthOpen).
        //  Builds the inner-lip polygon, masks it onto the source frame, converts
        //  the masked region to HSV, classifies each pixel as tongue / teeth /
        //  shadow / other. Fires when tongue / valid pixel ratio crosses the
        //  enter threshold and the mouth has been open long enough to expose
        //  the tongue. Hysteresis on leave to avoid flicker.
        // ─────────────────────────────────────────────────────────────────────────
        private void UpdateTongueState(Mat bgr, float[][] landmarks)
        {
            // Hard reset when the mouth is closed. Tongue is by definition
            // invisible at that point, and the polygon would be a sliver where
            // teeth/shadow ratios go haywire.
            if (!_mouthOpen)
            {
                if (_tongueOut)
                {
                    _tongueOut = false;
                    _tongueOutSince = null;
                    _maxTongueRatioThisFire = 0;
                }
                return;
            }

            double tongueRatio = ComputeTongueRatio(bgr, landmarks,
                out int tonguePx, out int teethPx, out int shadowPx, out int otherPx);

            if (tongueRatio > _windowMaxTongueRatio) _windowMaxTongueRatio = tongueRatio;
            _diagTongueSum += tonguePx;
            _diagTeethSum  += teethPx;
            _diagShadowSum += shadowPx;
            _diagOtherSum  += otherPx;
            _diagTongueFrames++;

            var now = DateTime.UtcNow;
            MaybeLogTongueDiag(now);

            bool nowOut = _tongueOut
                ? tongueRatio > TongueLeaveRatio
                : tongueRatio > TongueEnterRatio;

            if (nowOut && !_tongueOut)
            {
                _tongueOutSince = now;
                _maxTongueRatioThisFire = tongueRatio;
            }
            else if (nowOut)
            {
                if (tongueRatio > _maxTongueRatioThisFire) _maxTongueRatioThisFire = tongueRatio;
                if (_tongueOutSince.HasValue && _lastTongueOutAt < _tongueOutSince.Value)
                {
                    var outMs = (now - _tongueOutSince.Value).TotalMilliseconds;
                    if (outMs >= MinTongueOutMs
                        && (now - _lastTongueOutAt).TotalMilliseconds >= TongueCooldownMs)
                    {
                        _lastTongueOutAt = now;
                        _tongueOutCount++;
                        Dispatch(() => OnTongueOut?.Invoke());
                        // Debug, not Information — see blink fire log for reasoning.
                        App.Logger?.Debug(
                            "WebcamTrackingService: tongue-out #{N} FIRED (visible for {Ms:F0}ms, max ratio={Max:P0})",
                            _tongueOutCount, outMs, _maxTongueRatioThisFire);
                    }
                }
            }
            else if (!nowOut && _tongueOut)
            {
                _tongueOutSince = null;
                _maxTongueRatioThisFire = 0;
            }

            _tongueOut = nowOut;
        }

        private static double ComputeTongueRatio(Mat bgr, float[][] landmarks,
            out int tongue, out int teeth, out int shadow, out int other)
        {
            tongue = teeth = shadow = other = 0;

            // Build polygon in source-frame pixel coords + bounding box.
            int n = InnerLipPolygonIndices.Length;
            var pts = new CvPoint[n];
            int xMin = int.MaxValue, yMin = int.MaxValue, xMax = int.MinValue, yMax = int.MinValue;
            for (int i = 0; i < n; i++)
            {
                int px = (int)Math.Round(landmarks[InnerLipPolygonIndices[i]][0]);
                int py = (int)Math.Round(landmarks[InnerLipPolygonIndices[i]][1]);
                pts[i] = new CvPoint(px, py);
                if (px < xMin) xMin = px;
                if (py < yMin) yMin = py;
                if (px > xMax) xMax = px;
                if (py > yMax) yMax = py;
            }

            // Clip bbox to source frame; bail if degenerate.
            xMin = Math.Max(0, xMin);
            yMin = Math.Max(0, yMin);
            xMax = Math.Min(bgr.Width - 1, xMax);
            yMax = Math.Min(bgr.Height - 1, yMax);
            int bbW = xMax - xMin + 1;
            int bbH = yMax - yMin + 1;
            if (bbW < 4 || bbH < 4) return 0.0;

            // Translate polygon into bbox-local coords for the mask.
            var localPts = new CvPoint[n];
            for (int i = 0; i < n; i++)
                localPts[i] = new CvPoint(pts[i].X - xMin, pts[i].Y - yMin);

            using var mask = new Mat(bbH, bbW, MatType.CV_8UC1, Scalar.All(0));
            Cv2.FillPoly(mask, new[] { localPts }, Scalar.All(255));

            using var crop = new Mat(bgr, new CvRect(xMin, yMin, bbW, bbH));
            using var hsv = new Mat();
            Cv2.CvtColor(crop, hsv, ColorConversionCodes.BGR2HSV);

            // Walk pixels by raw byte access for speed (avoids per-pixel C# Mat
            // indexer calls). HSV is CV_8UC3, so 3 bytes per pixel, row-major.
            int total = bbW * bbH;
            var hsvBytes = new byte[total * 3];
            System.Runtime.InteropServices.Marshal.Copy(hsv.Data, hsvBytes, 0, hsvBytes.Length);
            var maskBytes = new byte[total];
            System.Runtime.InteropServices.Marshal.Copy(mask.Data, maskBytes, 0, maskBytes.Length);

            for (int i = 0; i < total; i++)
            {
                if (maskBytes[i] == 0) continue;
                byte h = hsvBytes[3 * i + 0];
                byte s = hsvBytes[3 * i + 1];
                byte v = hsvBytes[3 * i + 2];

                // Order matters: shadow first (V is dominant), then teeth (V high
                // + S low), then tongue (red-pink hue + saturated + bright enough),
                // else "other" (lip surface, in-between).
                if (v < ShadowMaxVal) { shadow++; continue; }
                if (v >= TeethMinVal && s <= TeethMaxSat) { teeth++; continue; }
                bool inHue = (h >= TongueHueLow1 && h <= TongueHueHigh1)
                          || (h >= TongueHueLow2 && h <= TongueHueHigh2);
                if (inHue && s >= TongueMinSat && v >= TongueMinVal) { tongue++; continue; }
                other++;
            }

            int valid = tongue + other;     // exclude teeth and shadow from the denominator
            if (valid < 8) return 0.0;      // too few useful pixels to trust the ratio
            return (double)tongue / valid;
        }

        private void MaybeLogTongueDiag(DateTime now)
        {
            if (_lastTongueDiagAt == DateTime.MinValue) { _lastTongueDiagAt = now; return; }
            if ((now - _lastTongueDiagAt).TotalMilliseconds < TongueDiagLogIntervalMs) return;
            _lastTongueDiagAt = now;

            // Per-class share of pixels across the window. If "tongue" stays
            // tiny but "other" is huge while the user reports sticking out
            // their tongue, the saturation gate is too tight (loosen
            // TongueMinSat). If "shadow" dominates, ShadowMaxVal is too high.
            long classified = _diagTongueSum + _diagTeethSum + _diagShadowSum + _diagOtherSum;
            double tonguePct = classified > 0 ? (double)_diagTongueSum / classified : 0;
            double teethPct  = classified > 0 ? (double)_diagTeethSum  / classified : 0;
            double shadowPct = classified > 0 ? (double)_diagShadowSum / classified : 0;
            double otherPct  = classified > 0 ? (double)_diagOtherSum  / classified : 0;

            // Debug, not Information — see blink-diag for reasoning.
            App.Logger?.Debug(
                "WebcamTrackingService: tongue-diag winMaxRatio={Max:P0} state={State} fires={N} frames={F} | per-class share: tongue={T:P0} teeth={E:P0} shadow={S:P0} other={O:P0}",
                _windowMaxTongueRatio, _tongueOut ? "OUT" : "in", _tongueOutCount,
                _diagTongueFrames, tonguePct, teethPct, shadowPct, otherPct);

            _windowMaxTongueRatio = 0;
            _diagTongueSum = _diagTeethSum = _diagShadowSum = _diagOtherSum = 0;
            _diagTongueFrames = 0;
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  One-Euro filter (Casiez 2012)
        //  ────────────────────────────────────────────────────────────────────────
        //  Velocity-adaptive low-pass: at low speed it tightens the cutoff (kills
        //  jitter when fixating), at high speed it widens (no lag during saccades).
        //  Two scalar tunables — MinCutoff sets the floor cutoff at zero speed,
        //  Beta sets how aggressively cutoff scales with |dx/dt|. DCutoff smooths
        //  the velocity estimate itself.
        // ─────────────────────────────────────────────────────────────────────────
        private sealed class OneEuroFilter
        {
            private readonly double _minCutoff;
            private readonly double _beta;
            private readonly double _dCutoff;
            private double _xPrev;
            private double _dxPrev;
            private long _tPrevTicks;
            private bool _initialized;

            public OneEuroFilter(double minCutoff, double beta, double dCutoff)
            {
                _minCutoff = minCutoff;
                _beta = beta;
                _dCutoff = dCutoff;
            }

            public void Reset()
            {
                _initialized = false;
                _xPrev = 0;
                _dxPrev = 0;
                _tPrevTicks = 0;
            }

            public double Filter(double x, long tTicks)
            {
                if (!_initialized)
                {
                    _initialized = true;
                    _xPrev = x;
                    _dxPrev = 0;
                    _tPrevTicks = tTicks;
                    return x;
                }

                double dt = (tTicks - _tPrevTicks) / (double)Stopwatch.Frequency;
                // Sane fallback for clock anomalies / capture-thread stalls — don't
                // let dt go to zero (alpha→1, signal collapses to raw input) or
                // grow huge (alpha→0, output freezes).
                if (dt <= 0 || dt > 1.0) dt = 1.0 / 30.0;

                double dx = (x - _xPrev) / dt;
                double aD = Alpha(dt, _dCutoff);
                double dxHat = aD * dx + (1 - aD) * _dxPrev;

                double cutoff = _minCutoff + _beta * Math.Abs(dxHat);
                double a = Alpha(dt, cutoff);
                double xHat = a * x + (1 - a) * _xPrev;

                _xPrev = xHat;
                _dxPrev = dxHat;
                _tPrevTicks = tTicks;
                return xHat;
            }

            private static double Alpha(double dt, double cutoff)
            {
                double tau = 1.0 / (2.0 * Math.PI * cutoff);
                return 1.0 / (1.0 + tau / dt);
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
            // Cached byte staging buffer for the BGR→RGB float conversion in
            // FillInputBufferFromBgr — at ~49KB per BlazeFace frame this would
            // otherwise allocate fresh each frame. Caching it drops Gen0
            // pressure noticeably across the three detectors.
            private readonly byte[] _byteBuffer = new byte[InputSize * InputSize * 3];
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
                FillInputBufferFromBgr(_paddedBuffer, _inputBuffer, _byteBuffer);

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

            private static void FillInputBufferFromBgr(Mat bgr128, float[] dst, byte[] bytes)
            {
                // bgr128 is contiguous CV_8UC3, 128×128. Copy raw bytes once, then
                // rearrange in place: BGR → RGB and uint8 → float in [-1,1].
                if (bgr128.Width != InputSize || bgr128.Height != InputSize || bgr128.Type() != MatType.CV_8UC3)
                    throw new InvalidOperationException("FillInputBufferFromBgr: expected 128×128 CV_8UC3");

                int total = InputSize * InputSize;
                System.Runtime.InteropServices.Marshal.Copy(bgr128.Data, bytes, 0, total * 3);
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
            // Cached byte staging buffer — see BlazeFaceDetector for reasoning.
            // 192×192×3 = ~110KB, the largest of the three detectors.
            private readonly byte[] _byteBuffer = new byte[InputSize * InputSize * 3];
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
                FillInputBufferFromBgr(_resizedBuffer, _inputBuffer, _byteBuffer);

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

            private static void FillInputBufferFromBgr(Mat bgr192, float[] dst, byte[] bytes)
            {
                if (bgr192.Width != InputSize || bgr192.Height != InputSize || bgr192.Type() != MatType.CV_8UC3)
                    throw new InvalidOperationException("FaceMesh.FillInputBufferFromBgr: expected 192×192 CV_8UC3");

                int total = InputSize * InputSize;
                System.Runtime.InteropServices.Marshal.Copy(bgr192.Data, bytes, 0, total * 3);
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
        //  in 64-px tensor coords. We use iris[0] (CENTER) for gaze and the eye
        //  contour for blink EAR (more responsive to closure than FaceMesh).
        //
        //  Returns: EyeLandmarks containing iris center + 71-point eye contour
        //  in source-frame pixel coords, or null if the eye corners aren't far
        //  enough apart to give a meaningful crop.
        // ─────────────────────────────────────────────────────────────────────────
        public sealed class EyeLandmarks
        {
            public (double X, double Y) IrisCenter;
            public float[][] Contour = System.Array.Empty<float[]>();   // 71 [x, y]
        }

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
            // Cached byte staging buffer — see BlazeFaceDetector for reasoning.
            // 64×64×3 = 12KB; called twice per frame (once per eye) so the
            // saving here doubles up.
            private readonly byte[] _byteBuffer = new byte[InputSize * InputSize * 3];
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

            public EyeLandmarks? Detect(Mat bgr, float[] outerCorner, float[] innerCorner, bool isRightEye)
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

                FillInputBufferFromBgr(fed, _inputBuffer, _byteBuffer);

                var inputTensor = new DenseTensor<float>(_inputBuffer, new[] { 1, InputSize, InputSize, 3 });
                using var results = _session.Run(new[] { NamedOnnxValue.CreateFromTensor(_inputName, inputTensor) });

                // Iris output: 5×3 = 15 floats. Eye contour: 71×3 = 213 floats.
                Tensor<float>? irisT = null;
                Tensor<float>? eyeT = null;
                foreach (var r in results)
                {
                    var t = r.AsTensor<float>();
                    long len = 1;
                    foreach (var d in t.Dimensions) len *= d;
                    if (len == NumIrisPoints * LandmarkDims) irisT = t;
                    else if (len == NumEyeContour * LandmarkDims) eyeT = t;
                }
                if (irisT == null || eyeT == null) return null;

                float invInput = 1f / InputSize;

                // Iris center is point 0 (IrisIndex.CENTER in IntelliProve constants).
                var iris = irisT.ToArray();
                float ix64 = iris[0];
                float iy64 = iris[1];
                if (isRightEye) ix64 = InputSize - ix64;
                double irisX = rx + (ix64 * invInput) * rs;
                double irisY = ry + (iy64 * invInput) * rs;

                // Eye contour 71 points → source-frame pixel coords.
                var eye = eyeT.ToArray();
                var contour = new float[NumEyeContour][];
                for (int i = 0; i < NumEyeContour; i++)
                {
                    float ex64 = eye[i * LandmarkDims + 0];
                    float ey64 = eye[i * LandmarkDims + 1];
                    if (isRightEye) ex64 = InputSize - ex64;
                    contour[i] = new[] { rx + (ex64 * invInput) * rs, ry + (ey64 * invInput) * rs };
                }

                return new EyeLandmarks { IrisCenter = (irisX, irisY), Contour = contour };
            }

            private static void FillInputBufferFromBgr(Mat bgr64, float[] dst, byte[] bytes)
            {
                if (bgr64.Width != InputSize || bgr64.Height != InputSize || bgr64.Type() != MatType.CV_8UC3)
                    throw new InvalidOperationException("IrisDetector.FillInputBufferFromBgr: expected 64×64 CV_8UC3");

                int total = InputSize * InputSize;
                System.Runtime.InteropServices.Marshal.Copy(bgr64.Data, bytes, 0, total * 3);
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
