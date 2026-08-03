using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

        // A solid-colour / black feed reads as a perfectly valid non-empty Mat but
        // contains no detectable face — the failure mode behind BUG-F2XJE2E7X9
        // (Elgato Facecam Neo over MSMF: 1240 frames read, zero faces). We reject a
        // probe frame whose richest channel still has near-zero spatial variation
        // so the backend loop falls through to the next API. The threshold is
        // deliberately tiny: even a dim, grainy room scatters well above it, so this
        // only catches genuinely degenerate feeds, never a legitimately dark one.
        private const double MinProbeStdDev = 3.0;

        // Second, independent way to accept a probe frame: temporal change. A
        // genuinely dark room can read as low spatial contrast (below MinProbeStdDev)
        // yet is still a LIVE feed — it changes frame-to-frame as the user moves. A
        // dead / solid / frozen "garbage" feed (the Elgato BUG-F2XJE2E7X9 case the
        // spatial floor exists to reject) does NOT change. So we ALSO adopt a feed
        // whose mean absolute inter-frame difference clears this bar, which rescues a
        // dim-but-real camera from a false CameraInUse WITHOUT lowering MinProbeStdDev
        // (lowering it would re-admit the very solid feed the floor was added to reject).
        // Set above the uniform sensor-noise flicker of a static feed (~<1 on 0-255)
        // but below real scene motion. Spatial-clean frames still win first, so cameras
        // that already work — and the Elgato flat+static feed — are completely unaffected.
        private const double MinProbeTemporalDelta = 2.0;

        // Per-attempt warm-up budget for the probe loop. Some UVC webcams (the Lovense
        // Webcam 2 in BUG-6W469QGMHS) stream black for over a second after the handle
        // opens before real frames arrive. A fixed 5-read window (~1s) rejected those as
        // "degenerate" and fell through every backend, surfacing a false CameraInUse. We
        // poll for up to this long instead so a slow-warming camera is adopted the moment
        // it delivers a usable frame; a backend that's truly dead just costs this once
        // before the loop tries the next one (still far under the 90s open watchdog).
        private const int ProbeWarmupMs = 3000;
        private const int ProbeReadIntervalMs = 200;

        // Ordered open attempts: each tries one backend with one pixel-format
        // request, accepting the first that delivers a usable frame.
        //  • DSHOW shares WebcamDeviceEnumerator's DirectShow index space (so the
        //    configured index resolves to the same physical device the dropdown
        //    showed) and is the more reliable path for Elgato/UVC webcams; MSMF
        //    stays as a fallback for the MF-only / 32-bit-only devices the WinRT
        //    enumerator catches.
        //  • The default (Fourcc == null) attempt runs FIRST per backend so cameras
        //    that already work are untouched. MJPG is escalated to only when the
        //    default feed is unusable — that's the Elgato Facecam Neo fix
        //    (BUG-F2XJE2E7X9: default format reads non-empty but contains no face)
        //    without risking YUY2-only cameras that don't support MJPG.
        private static readonly (VideoCaptureAPIs Api, string Name, string? Fourcc)[] CaptureAttempts =
        {
            (VideoCaptureAPIs.DSHOW, "DSHOW", null),
            (VideoCaptureAPIs.DSHOW, "DSHOW", "MJPG"),
            (VideoCaptureAPIs.MSMF,  "MSMF",  null),
            (VideoCaptureAPIs.MSMF,  "MSMF",  "MJPG"),
        };

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
        // Deliberate "hold your eyes shut" gesture. Sits ABOVE MaxBlinkClosedMs on purpose:
        // a closure long enough to fire OnEyesClosedLong can never also be reported as a
        // blink, so the two gestures are mutually exclusive by construction.
        private const int EyesClosedLongMs = 2000;

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
        // Hysteresis: enter "open" at >1.65×baseline, leave at <1.30×baseline.
        // Min open window 80 ms (filters single-frame jitter). Cooldown 800 ms.
        // In dim light the inner-lip landmark spread compresses and the resting
        // baseline reads high, so a purely relative threshold can become
        // unreachable — an open mouth never hits 1.8× its own (inflated) resting
        // value. The ratio is therefore loosened AND backed by an absolute
        // "clearly wide open" floor (MAR is normalized by mouth width, so it's
        // roughly scale-invariant): mouth counts as open if EITHER the relative
        // ratio OR the absolute floor is crossed. Floors sit well above normal
        // speech (~0.15-0.30) so talking won't false-fire.
        private const int MarBaselineFrames = 90;
        private const int MarMinSamplesForBaseline = 15;
        private const double MarOpenRatio = 1.65;
        private const double MarCloseRatio = 1.30;
        private const double MarAbsoluteOpen = 0.38;   // enter open if MAR exceeds this regardless of baseline
        private const double MarAbsoluteClose = 0.28;  // stay open until MAR drops below this (absolute hysteresis)
        // Speech guard on the RELATIVE open path. A purely relative threshold
        // is a false-positive machine for users with a low resting MAR: 1.65×
        // a 0.10 baseline is ~0.17, and ordinary talking sweeps 0.15-0.30 all
        // day. Real deliberate opens sit ~0.5+. The relative path therefore
        // ALSO requires this absolute floor (scaled by the sensitivity
        // slider); the separate MarAbsoluteOpen path is unaffected. Kept
        // below speech peaks' ceiling so shouting doesn't fire, above dim-
        // light baseline inflation so the relative path still has a job.
        private const double MarSpeechFloor = 0.32;
        // Was 80 ms — shorter than one spoken syllable, so any speech frame
        // run above threshold fired. A deliberate "open wide" gesture is held;
        // requiring a sustained window filters talking without hurting it.
        private const int MinMouthOpenMs = 250;
        private const int MouthCooldownMs = 800;
        // Median-of-N smoothing on raw MAR. FaceMesh inner-lip landmarks are
        // noisy on webcams; a single-frame spike dipping below the close
        // threshold mid-open resets the min-open timer and drops a real open.
        // A short median rejects those spikes with negligible lag.
        private const int MarSmoothFrames = 3;
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
        // Enter/leave ratios and the minimum hold were tightened together
        // (0.22/0.14/150ms → 0.28/0.18/350ms): with the looser values, pink
        // inner-lip pixels during ordinary talking cleared the ratio for a
        // couple of frames and fired constantly. A real deliberate tongue-out
        // shows a big pink area and is held — it clears all three easily.
        private const double TongueEnterRatio = 0.28;   // tongue_px / valid_px
        private const double TongueLeaveRatio = 0.18;
        private const int MinTongueOutMs = 350;
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

        /// <summary>
        /// Returns the System.Windows.Forms.Screen that matches the monitor the
        /// current calibration ran on, or null when (a) no calibration is loaded,
        /// (b) the calibration predates the monitor-identity capture and has no
        /// DeviceName, or (c) a monitor with that DeviceName is no longer
        /// connected. Callers should treat null as "fall back to primary screen
        /// with no calibration-based clamping."
        /// </summary>
        public System.Windows.Forms.Screen? GetCalibratedScreen()
        {
            var name = Calibration?.MonitorBounds?.DeviceName;
            if (string.IsNullOrEmpty(name)) return null;
            try
            {
                foreach (var screen in System.Windows.Forms.Screen.AllScreens)
                {
                    if (string.Equals(screen.DeviceName, name, StringComparison.OrdinalIgnoreCase))
                        return screen;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "WebcamTrackingService.GetCalibratedScreen: enumeration failed");
            }
            return null;
        }

        /// <summary>
        /// Populates <paramref name="bounds"/> with the calibrated screen's
        /// full bounds rect. Returns false in the same cases as
        /// <see cref="GetCalibratedScreen"/> returns null.
        /// </summary>
        public bool TryGetCalibratedBounds(out System.Windows.Rect bounds)
        {
            var screen = GetCalibratedScreen();
            if (screen == null)
            {
                bounds = default;
                return false;
            }
            bounds = new System.Windows.Rect(
                screen.Bounds.X, screen.Bounds.Y,
                screen.Bounds.Width, screen.Bounds.Height);
            return true;
        }

        /// <summary>
        /// Returns true when (x, y, width, height) describes the same monitor
        /// the current calibration ran on. Used by call sites that operate on
        /// raw rect components (e.g. FlashService.MonitorInfo) and can't share
        /// a Screen reference. False when no calibration is loaded or the
        /// calibrated monitor is no longer connected.
        /// </summary>
        public bool IsCalibratedMonitor(int x, int y, int width, int height)
        {
            var screen = GetCalibratedScreen();
            if (screen == null) return false;
            return screen.Bounds.X == x
                && screen.Bounds.Y == y
                && screen.Bounds.Width == width
                && screen.Bounds.Height == height;
        }

        public event Action? OnBlink;

        /// <summary>
        /// Fired once per closure when the eyes have been continuously closed for
        /// <see cref="EyesClosedLongMs"/> (2s) — a deliberate "hold it shut" gesture
        /// rather than a blink. Fires WHILE still closed (not on reopen), so the
        /// gesture feels immediate. <see cref="OnBlink"/> can never fire for the same
        /// closure: that path requires closedMs &lt;= MaxBlinkClosedMs (1500ms).
        /// Marshalled to the UI dispatcher like every other event here.
        /// </summary>
        public event Action? OnEyesClosedLong;

        public event Action<System.Windows.Point>? OnLongStare;
        public event Action? OnMouthOpen;
        public event Action? OnTongueOut;
        public event Action<System.Windows.Point>? OnGazeMove;
        public event Action<GazeSide>? OnGazeSide;
        public event Action? OnFaceLost;
        public event Action? OnFaceFound;
        public event Action<WebcamTrackingState>? OnTrackingStateChanged;

        /// <summary>
        /// Fired during Start() to report engine-load progress (0.0–1.0) and a
        /// human-readable phase label. Marshalled to the UI dispatcher, so
        /// handlers may touch UI directly. Start() runs on a worker thread and
        /// can block several seconds opening the camera and constructing the
        /// three ONNX sessions; without this the UI has no insight into that
        /// sequence. Consumed by the movable loading splash.
        /// </summary>
        public event Action<double, string>? OnStartupProgress;

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

        // Infinite-hang guard, NOT a UX timeout. The open (VideoCapture ctor +
        // probe frames) runs on a worker; TryOpenCamera's Wait returns the
        // instant that worker finishes, so this ceiling only matters for a
        // driver that wedges the ctor *forever* — which used to hang Start()
        // under the state lock and freeze the loading splash at "Opening
        // camera…" (#311). A slow-but-successful open is adopted as soon as it
        // completes, well before this elapses.
        //
        // History: 12s, then 30s, were both too tight. MSMF source activation
        // is pathologically slow when several virtual-camera filters are
        // registered (Snap, FilterOnMe, OBS, NVIDIA Broadcast, VTubeStudio) —
        // it walks every one. Real opens were completing at ~30-40s, but the
        // watchdog had already fired, set _openAbandoned, declared CameraInUse,
        // and disposed the handle the worker opened a beat later: "camera in
        // use" + the camera light flicking on ~10s after the failure
        // (#321 C920, #323 GENERAL WEBCAM). 90s clears realistic slow opens
        // with margin while still bounding a genuinely wedged driver.
        private const int CameraOpenTimeoutSeconds = 90;

        // Stamps each camera-open attempt so a worker still blocked in the driver disposes
        // whatever it eventually opens instead of leaking a live capture handle behind our back.
        //
        // #743: this was a resettable bool, and TryOpenCamera's first statement cleared it. A retry
        // therefore re-armed the PREVIOUS attempt's worker: that worker returned from the driver,
        // found the flag clear, and published its handle into a closure whose call had already
        // returned - so a live VideoCapture stayed open on the same device the retry was reading
        // from. OpenCvSharp's VideoCapture is finalizable, which makes that worse than a leak: the
        // GC finalizer thread later called release() on a device mid-Read (native AV, invisible to
        // every managed handler), or blocked inside it and stalled process-wide finalization -
        // the global slowdown users reported just before the crash to desktop.
        //
        // A monotonic generation can't be re-armed: only the attempt that owns the current stamp
        // may publish, and any bump invalidates every worker still in flight.
        private int _openGeneration;

        // Guards the publish/abandon handoff in TryOpenCamera. Deliberately NOT _stateLock, which
        // Start() holds for its whole body - taking that here would deadlock the open worker.
        private readonly object _openPublishLock = new();

        // #743: 1 while a start is in progress. Checked outside _stateLock, because a caller that
        // queues on the lock only acquires it after the state has left Starting.
        private int _startInFlight;

        // Set when a capture-open attempt fails because the OpenCV native runtime
        // (OpenCvSharpExtern.dll) can't load — almost always a missing MSVC runtime
        // (DllNotFoundException 0x8007007E). Lets OpenCaptureCore report Error with an
        // explicit "install the VC++ redist" log line instead of a misleading
        // CameraDenied that sends users hunting through privacy settings (#347).
        private volatile bool _nativeRuntimeMissing;

        // Capture-thread state
        private VideoCapture? _capture;
        private BlazeFaceDetector? _faceDetector;
        private FaceMeshDetector? _faceMesh;
        private IrisDetector? _irisDetector;

        /// <summary>#743: true while any ONNX session is still held, so Stop() can tell a service
        /// with orphaned native resources from a genuinely idle one. Read under _stateLock.</summary>
        private bool HasLoadedModels => _faceDetector != null || _faceMesh != null || _irisDetector != null;
        private Thread? _captureThread;
        private volatile bool _stopRequested;

        // Heuristic state (capture-thread only)
        private DateTime _lastBlinkAt = DateTime.MinValue;
        private readonly Queue<double> _earBuffer = new();       // rolling avg-EAR samples for baseline
        private double _earBaseline;                             // rolling 90-frame max of avg EAR
        private bool _eyesClosed;                                // hysteresis state for both eyes (single)
        private DateTime? _eyesClosedAt;                         // start of current closed window
        private bool _eyesClosedLongFired;                       // OnEyesClosedLong already fired for THIS closure
        private double _minEarThisClosure;                       // tracks deepest closure during current closed window
        private double _windowMinEar = double.MaxValue;          // min EAR seen since last diag log (for tuning)
        private double _windowMaxEar = double.MinValue;          // max EAR seen since last diag log
        private int _windowNearMissCount;                        // count of frames where EAR < 0.90×baseline but we didn't trigger closed
        private DateTime _lastBlinkDiagAt = DateTime.MinValue;
        private int _blinkCount;                                 // total blinks fired since service start

        // Mouth state (mirrors blink state machine)
        private DateTime _lastMouthOpenAt = DateTime.MinValue;
        private readonly Queue<double> _marBuffer = new();
        private readonly Queue<double> _marSmoothBuffer = new();  // last few raw MAR for median de-jitter
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

        // Eye-corner reference-frame smoothing. NormalizeIrisVector expresses
        // the iris position relative to the eye-corner midpoint and scales it
        // by the corner-to-corner width — but those corners come from raw
        // per-frame FaceMesh landmarks that jitter ~1-2px, which shifts BOTH
        // the origin and the scale of the normalized vector every frame. That
        // is correlated (multiplicative) noise the downstream One-Euro can't
        // cleanly remove. The corners are head-anchored — they barely move when
        // only the EYES move — so a short rolling mean of the reference frame
        // (midpoint cx/cy + width w) strips that jitter with negligible gaze
        // lag. The fast-moving iris center itself is NOT smoothed here.
        private const int EyeRefSmoothFrames = 6;
        private readonly Queue<double> _leftRefCxBuffer = new();
        private readonly Queue<double> _leftRefCyBuffer = new();
        private readonly Queue<double> _leftRefWBuffer = new();
        private readonly Queue<double> _rightRefCxBuffer = new();
        private readonly Queue<double> _rightRefCyBuffer = new();
        private readonly Queue<double> _rightRefWBuffer = new();

        // Median pre-filter on the raw iris vector, applied before any other
        // smoothing (and before OnRawIris fires, so calibration samples the
        // same signal the runtime projects). A 3-sample median rejects the
        // single-frame landmark spikes that One-Euro only partially absorbs,
        // at the cost of ≤1 frame of lag. Mirrors the MAR median de-jitter.
        private const int IrisMedianFrames = 3;
        private readonly Queue<double> _irisDxMedianBuffer = new();
        private readonly Queue<double> _irisDyMedianBuffer = new();

        // Screen-space One-Euro, applied AFTER the polynomial projection. The
        // iris-space One-Euro (_irisD*Filter) can't smooth uniformly: the
        // polynomial slope is steep near the screen edges, so the same
        // iris-space cutoff produces far more screen-DIP jitter at the edges
        // than at the center. A second One-Euro in screen-DIP space evens that
        // out — it is the stage the user actually perceives as "the cursor
        // holding still." Beta is in DIP/s velocity units here (independent of
        // the iris filter's units). Kept gentle so it doesn't stack noticeable
        // lag on top of the iris filter; raise ScreenOneEuroMinCutoff if the
        // cursor feels laggy, lower it if it still wobbles at fixation.
        private const double ScreenOneEuroMinCutoff = 1.5;
        private const double ScreenOneEuroBeta = 0.02;
        private readonly OneEuroFilter _screenXFilter = new(ScreenOneEuroMinCutoff, ScreenOneEuroBeta, OneEuroDCutoff);
        private readonly OneEuroFilter _screenYFilter = new(ScreenOneEuroMinCutoff, ScreenOneEuroBeta, OneEuroDCutoff);

        // Two-eye consistency gate. The projected gaze is the AVERAGE of the
        // two per-eye iris vectors, so a single-eye failure (glasses glare,
        // hair, a lash clipping the iris model) yanks the average even though
        // the user's gaze never moved — one of the two big "cursor flies
        // across the screen" sources. Two defenses, both adaptive so they
        // scale across setups instead of hardcoding one camera's noise:
        //   • Disagreement gate: the left/right vectors normally differ by a
        //     stable per-user amount (vergence + face asymmetry). We track a
        //     rolling median of |vL − vR| and drop frames whose disagreement
        //     spikes far above it — one eye is lying, and we can't tell which.
        //   • Transient one-eye drop: if we've been running on two eyes and
        //     suddenly only one detects, that's occlusion/noise, not a new
        //     steady state — skip emission briefly rather than jump to the
        //     one-eye average. If the one-eye state PERSISTS we accept it as
        //     the new normal (some users genuinely track with one eye).
        // Both gates fail open after MaxGateSkipFrames consecutive skips so a
        // genuine regime change (user turned, new glasses) can't mute gaze
        // output forever.
        private const int EyeDisagreementBaselineFrames = 90;
        private const int EyeDisagreementMinSamples = 15;
        private const double EyeDisagreementRatio = 3.0;   // spike = baseline×ratio + floor
        private const double EyeDisagreementFloor = 0.04;  // iris units; below this never gate
        private const int TwoEyeStreakRequired = 15;       // frames of 2-eye before 1-eye counts as transient
        // Short fail-open budget (~270ms at 30fps). Head movement legitimately
        // shifts how the two eyes disagree (foreshortening hits the far eye
        // harder), so a long budget turned every head turn into freeze-then-
        // jump: output held for the full budget, then failed open with a
        // discrete jolt to the new position. A short budget still rejects the
        // single-frame glint/lash spikes it exists for, while a sustained
        // regime change passes through quickly enough for the smoothing chain
        // to absorb it as a glide instead of a snap.
        private const int MaxGateSkipFrames = 8;
        private readonly Queue<double> _eyeDisagreementBuffer = new();
        private int _twoEyeStreak;
        private int _gateSkipStreak;

        // Gaze lock-on — an optional "the user is trying to hit THIS" hint
        // set by UI flows that know the current target (the calibration
        // bubble test, GazeFocusService for live gaze-pop bubbles). Unlike
        // the old static attractor, the lock is STATEFUL: an engagement
        // level builds while the gaze lingers near the target and drains
        // when it leaves — or when the gaze sway grows too large relative to
        // the target radius, so a user who is clearly not holding the target
        // loses the lock gradually instead of it snapping off. While
        // engaged, the deviation from the target is contracted (wobble stays
        // visible, scaled down in proportion to the lock), tapering to
        // nothing past 2.5× the radius so there's no seam at the boundary.
        //
        // Consumers that need the uncontracted point (residual/accuracy
        // math) read LastPreLockGaze instead of inverting the transform.
        // Tuned QUADRANT-strong (round-11 user direction): "if we are in
        // that quadrant, we are looking at that target". Gaze anywhere in a
        // target's broad region is treated as intent — the cursor tends hard
        // to the target. Sway still drains the lock so a user who clearly
        // moves on is released gradually.
        private const double GazeLockMaxStrength = 0.93; // deviation contraction at full engagement
        private const double GazeLockCaptureFrac = 3.5;  // capture zone, × radius — quadrant-scale for a typical bubble
        private const double GazeLockBuildRate = 0.22;   // per-frame engagement growth toward 1 (~0.2s to lock at 30fps)
        private const double GazeLockLeaveDrain = 0.05;  // per-frame drain while outside the capture zone (~0.6s release)
        private const double GazeLockSwayFrac = 1.4;     // sway (EMA dips) above this ×radius starts draining the lock
        private const double GazeLockSwayDrain = 0.12;   // drain rate per unit of excess swayNorm
        private volatile GazeAttractorTarget? _attractor;

        // Lock state — touched only on the capture thread inside EmitGazeEvents.
        private double _lockEngage;
        private double _lockCenterX = double.NaN, _lockCenterY = double.NaN;
        private double _gazeEmaX = double.NaN, _gazeEmaY = double.NaN;
        private double _swayEma;

        private sealed record GazeAttractorTarget(double X, double Y, double Radius);
        private sealed record GazeSnapshot(double X, double Y);
        private volatile GazeSnapshot? _lastPreLockGaze;

        /// <summary>
        /// The most recent gaze point BEFORE the lock-on contraction (but
        /// after all mapping/smoothing/edge stages). Accuracy/residual math
        /// must use this instead of the OnGazeMove point so the lock doesn't
        /// flatter the measurement. Null until the first emitted frame.
        /// </summary>
        public System.Windows.Point? LastPreLockGaze
            => _lastPreLockGaze is { } s ? new System.Windows.Point(s.X, s.Y) : null;

        /// <summary>Declare the current gaze target (coordinates in the OnGazeMove DIP space). Safe to call every tick with a drifting center — lock engagement carries over unless the center jumps by more than the radius. UI-thread safe; the capture thread picks it up next frame.</summary>
        public void SetGazeAttractor(double centerX, double centerY, double radius)
            => _attractor = new GazeAttractorTarget(centerX, centerY, radius);

        public void ClearGazeAttractor() => _attractor = null;

        // NOTE: a dispersion-lock fixation stabilizer (I-DT-style centroid
        // hold as the final output stage) was tried here and REVERTED after
        // live testing: it locked onto spurious mid-saccade clusters and
        // parked the cursor at random screen positions ("lingering"), and at
        // saturated edge projections it read as the cursor being glued to the
        // bezel. The One-Euro chain + gaze attractor cover stability without
        // the failure mode. Don't reintroduce a hard lock on the general
        // cursor path — target-scoped stickiness (SetGazeAttractor) is the
        // acceptable form.

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
            // #743: refuse a concurrent start rather than queueing behind it. A second click used to
            // block here on _stateLock for the remainder of the 90s camera timeout and then re-run
            // the entire open + model load, producing a second capture handle on the same device.
            // The check has to sit OUTSIDE the lock - by the time a queued caller acquired it, the
            // state had already left Starting, so an in-lock check would never see it.
            if (Interlocked.CompareExchange(ref _startInFlight, 1, 0) != 0)
            {
                App.Logger?.Information("WebcamTrackingService: Start() refused — a start is already in flight");
                return false;
            }

            try
            {
                return StartCore();
            }
            finally
            {
                Interlocked.Exchange(ref _startInFlight, 0);
            }
        }

        private bool StartCore()
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

                // #743: a previous Stop() gives up joining a wedged capture thread after 5s and
                // deliberately leaves it running. Starting again would set _stopRequested = false
                // below, resurrecting that thread, and then start a second one - two 30fps
                // pipelines sharing one detector set, whose Mat buffers are documented
                // capture-thread-only. That races raw-pointer reads against buffer disposal.
                if (_captureThread is { IsAlive: true })
                {
                    App.Logger?.Error("WebcamTrackingService: Start() refused — the previous capture thread is still alive (wedged driver). Restart the app to recover the camera.");
                    SetState(WebcamTrackingState.Error);
                    return false;
                }

                // #743: a failed start leaves a live capture graph and up to three ONNX sessions
                // orphaned, because Stop() early-returns for the Error/CameraInUse states it lands
                // in and CaptureLoop's self-termination releases nothing. Every retry then added
                // another capture plus ~6 ORT threads and their arenas - the compounding slowdown.
                // Safe here: nothing is running, and no capture thread is alive (checked above).
                ReleaseModels();
                ReleaseCapture();

                SetState(WebcamTrackingState.Starting);
                ReportStartupProgress(0.08, "Preparing eye-tracking engine…");

                var paths = ResolveModelPaths();
                if (paths.FaceModel == null || paths.FaceAnchors == null || paths.MeshModel == null || paths.IrisModel == null)
                {
                    App.Logger?.Warning("WebcamTrackingService: model files missing in Resources/Models/ (face_detection_short_range.onnx, blazeface_anchors.json, face_landmark.onnx, iris_landmark.onnx)");
                    SetState(WebcamTrackingState.Error);
                    return false;
                }

                ReportStartupProgress(0.25, "Opening camera…");
                if (!TryOpenCamera())
                {
                    return false; // state already set
                }

                ReportStartupProgress(0.55, "Loading AI models…");
                if (!TryLoadModels(paths.FaceModel, paths.FaceAnchors, paths.MeshModel, paths.IrisModel))
                {
                    ReleaseCapture();
                    SetState(WebcamTrackingState.Error);
                    return false;
                }

                ReportStartupProgress(0.92, "Starting capture…");
                _stopRequested = false;
                _captureThread = new Thread(CaptureLoop)
                {
                    IsBackground = true,
                    Name = "WebcamCapture",
                    Priority = ThreadPriority.BelowNormal
                };
                _captureThread.Start();

                SetState(WebcamTrackingState.Tracking);
                ReportStartupProgress(1.0, "Ready");
                App.Logger?.Information("WebcamTrackingService: capture started ({W}x{H}, target {Fps} fps)",
                    CaptureWidth, CaptureHeight, TargetFps);
                return true;
            }
        }

        /// <summary>
        /// Runs <see cref="Start"/> on a worker thread. Start() opens the camera
        /// and constructs three ONNX sessions synchronously, which can block
        /// 10-30s on slow USB negotiation / first-run model init; calling it
        /// directly on the UI thread freezes the window (Windows' "not responding"
        /// reaper can then kill the app) and also prevents the loading splash —
        /// driven by <see cref="OnStartupProgress"/>, which it dispatches to the
        /// UI thread — from ever painting. UI callers should await this rather
        /// than calling Start() directly.
        /// </summary>
        public Task<bool> StartAsync() => Task.Run(() => Start());

        public void Stop()
        {
            Thread? thread;
            lock (_stateLock)
            {
                // #743: Error / CameraInUse / CameraDenied are exactly the states a failed start
                // lands in, and they are also the states holding orphaned native handles. Returning
                // early for them meant nothing ever released a failed attempt's capture graph or
                // ONNX sessions. Only a genuinely idle service has nothing to do here.
                if (!IsRunning
                    && State != WebcamTrackingState.Starting
                    && _captureThread == null
                    && _capture == null
                    && !HasLoadedModels)
                {
                    return;
                }

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

        /// <summary>
        /// Fold a newly-measured per-axis linear correction (from the bubble
        /// test) into the calibration's GazeTrim. The new fit was measured
        /// with the existing trim ACTIVE, so the two compose (apply old,
        /// then new): for x' = x + a0 + a1·u then x'' = x' + b0 + b1·u',
        /// the composition is c0 = a0 + b0 + b1·a0, c1 = a1 + b1 + a1·b1.
        /// Composed coefficients are clamped so repeated tests can't wind
        /// the map up into something wild.
        /// </summary>
        public void ApplyGazeTrim(double x0, double x1, double y0, double y1,
            double centerX, double centerY, bool persist)
        {
            const double MaxOffset = 300;   // DIPs
            const double MaxScale = 0.35;   // ±35% stretch, total

            lock (_stateLock)
            {
                var current = Calibration;
                if (current == null) return;

                double cx0 = x0, cx1 = x1, cy0 = y0, cy1 = y1;
                if (current.GazeTrim is { } old)
                {
                    cx0 = old.X0 + x0 + x1 * old.X0;
                    cx1 = old.X1 + x1 + old.X1 * x1;
                    cy0 = old.Y0 + y0 + y1 * old.Y0;
                    cy1 = old.Y1 + y1 + old.Y1 * y1;
                }
                var composed = new GazeTrimData
                {
                    X0 = Math.Clamp(cx0, -MaxOffset, MaxOffset),
                    X1 = Math.Clamp(cx1, -MaxScale, MaxScale),
                    Y0 = Math.Clamp(cy0, -MaxOffset, MaxOffset),
                    Y1 = Math.Clamp(cy1, -MaxScale, MaxScale),
                    CenterX = centerX,
                    CenterY = centerY,
                    CapturedAt = DateTime.UtcNow,
                };
                var updated = current.WithGazeTrim(composed);
                if (persist) updated.Save();
                Calibration = updated;

                App.Logger?.Information(
                    "WebcamTrackingService: gaze trim applied — total x0={X0:F0} x1={X1:F3} y0={Y0:F0} y1={Y1:F3} (persist={Persist})",
                    composed.X0, composed.X1, composed.Y0, composed.Y1, persist);
            }
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
                // 64-bit DirectShow SystemDeviceEnum misses cameras that register
                // only 32-bit DirectShow filters or are Media-Foundation-only — yet
                // OpenCV-MSMF (our open path) and Discord/Windows Camera can still
                // use them. Fall back to the WinRT/MF list so the selector isn't
                // empty for those users (#282/#279/#291).
                var winrt = WebcamWinRtEnumerator.Enumerate();
                if (winrt.Count > 0)
                {
                    App.Logger?.Information("WebcamTrackingService: DirectShow found 0 devices; using WinRT/MF fallback — {Count} device(s): {Names}",
                        winrt.Count, string.Join(" | ", winrt.Select(d => $"[{d.Index}] {d.Name}")));
                    return winrt;
                }
                App.Logger?.Information("WebcamTrackingService: no video-capture devices found via DirectShow or WinRT enumeration");
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
            // #743: claim a fresh generation. Never clear a shared flag here - that re-armed the
            // previous attempt's worker and leaked a live handle onto the device (see _openGeneration).
            var generation = Interlocked.Increment(ref _openGeneration);

            // The VideoCapture ctor and the probe reads below can block for an
            // unbounded time on a wedged driver or a virtual camera that never
            // produces a frame. Run them on a worker and bound the wait so Start()
            // (which holds _stateLock) can't hang the whole engine + splash (#311).
            VideoCapture? opened = null;
            var openTask = Task.Run(() =>
            {
                var cap = OpenCaptureCore(generation);
                if (cap == null) return false;
                // Check and publish atomically against the timeout path below, so a handle can
                // never be published into the gap between "still current" and the assignment.
                lock (_openPublishLock)
                {
                    if (Volatile.Read(ref _openGeneration) != generation)
                    {
                        // The caller has moved on — don't hand over something nobody will dispose.
                        try { cap.Dispose(); } catch { }
                        return false;
                    }
                    opened = cap;
                    return true;
                }
            });

            if (!openTask.Wait(TimeSpan.FromSeconds(CameraOpenTimeoutSeconds)))
            {
                // Invalidate this attempt, and adopt anything the worker managed to publish in the
                // instant before we did — otherwise that handle has no owner at all.
                VideoCapture? stray;
                lock (_openPublishLock)
                {
                    Interlocked.Increment(ref _openGeneration);
                    stray = opened;
                    opened = null;
                }
                if (stray != null) { try { stray.Dispose(); } catch { } }

                App.Logger?.Warning(
                    "WebcamTrackingService: camera open timed out after {Seconds}s — driver may be hung or the device is held exclusively by another app",
                    CameraOpenTimeoutSeconds);
                SetState(WebcamTrackingState.CameraInUse);
                return false;
            }

            if (!openTask.Result || opened == null)
            {
                // Core already set the appropriate failure state (CameraDenied /
                // CameraInUse / Error), or the result was abandoned post-timeout.
                return false;
            }

            _capture = opened;
            return true;
        }

        /// <summary>
        /// Blocking camera-open core. Returns an opened <see cref="VideoCapture"/>
        /// on success, or null after setting the appropriate failure state. Does
        /// NOT publish to <see cref="_capture"/> — the watchdog in TryOpenCamera
        /// owns that so a post-timeout success can be safely disposed.
        /// </summary>
        /// <param name="generation">
        /// #743: the <see cref="_openGeneration"/> stamp this attempt owns. Any bump means a
        /// timeout or a newer Start() has taken over, so this attempt must stop and own nothing.
        /// </param>
        private VideoCapture? OpenCaptureCore(int generation)
        {
            bool Abandoned() => Volatile.Read(ref _openGeneration) != generation;

            _nativeRuntimeMissing = false;
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

                // Try each attempt in turn, accepting the first that both opens AND
                // delivers a usable (non-empty, non-degenerate) frame. An attempt that
                // opens but only produces black/garbage — as the default format does for
                // the Elgato Facecam Neo (BUG-F2XJE2E7X9) — is disposed so the loop falls
                // through to the next one instead of locking in a feed that reads fine
                // but contains no detectable face.
                // Note on timing: each attempt gets its own ProbeWarmupMs budget, so a
                // device that opens-but-never-streams on every backend can take up to
                // (attempts × warm-up) before we conclude CameraInUse. We accept that
                // worst case deliberately — the budget exists for slow-to-START cameras
                // (the Lovense streams black >1s, #335), and that symptom is
                // indistinguishable from a dead feed until the budget elapses, so it
                // can't be safely shortened. A wedged native Read() can also exceed the
                // budget (it's only checked between reads); the worker thread + 90s open
                // watchdog bound that, and we do NOT dispose mid-Read (guaranteed native
                // AV — see Stop()). What we CAN do is keep the user informed so the
                // multi-second open doesn't read as a hang.
                bool anyOpened = false;
                for (int attemptIdx = 0; attemptIdx < CaptureAttempts.Length; attemptIdx++)
                {
                    var attempt = CaptureAttempts[attemptIdx];
                    ReportStartupProgress(
                        0.25 + 0.20 * (attemptIdx / (double)CaptureAttempts.Length),
                        attemptIdx == 0
                            ? "Opening camera…"
                            : $"Camera slow to respond — trying another method ({attemptIdx + 1}/{CaptureAttempts.Length})…");

                    var cap = TryOpenWithBackend(deviceIndex, detectedName, attempt.Api, attempt.Name, attempt.Fourcc, ref anyOpened, generation);
                    if (cap != null)
                    {
                        App.Logger?.Information(
                            "WebcamTrackingService: device {Index} ('{Name}') opened successfully via {Api}{Fourcc}",
                            deviceIndex, detectedName, attempt.Name, attempt.Fourcc != null ? "/" + attempt.Fourcc : "");
                        return cap;
                    }
                    // Abandoned mid-open (post-timeout) — stop trying, the handle's owner has moved on.
                    if (Abandoned()) return null;
                }

                if (_nativeRuntimeMissing)
                {
                    // The camera was never the problem — the native capture library couldn't
                    // load. Report Error (not CameraDenied) so we don't send the user to
                    // Windows camera privacy settings for a runtime issue (#347).
                    App.Logger?.Error(
                        "WebcamTrackingService: OpenCV native runtime (OpenCvSharpExtern.dll) failed to load for device index {Index} ('{Name}') — the Microsoft Visual C++ Redistributable (x64) is almost certainly missing. Install it from https://aka.ms/vs/17/release/vc_redist.x64.exe",
                        deviceIndex, detectedName);
                    SetState(WebcamTrackingState.Error);
                }
                else if (anyOpened)
                {
                    // A backend opened the device but no usable frame ever arrived — most
                    // likely held by antivirus webcam shielding / Windows camera privacy,
                    // or delivering a degenerate feed we deliberately rejected.
                    App.Logger?.Warning(
                        "WebcamTrackingService: device index {Index} ('{Name}') opened but produced no usable frame on any backend",
                        deviceIndex, detectedName);
                    SetState(WebcamTrackingState.CameraInUse);
                }
                else
                {
                    App.Logger?.Warning(
                        "WebcamTrackingService: VideoCapture.Open returned false on all backends for device index {Index} ('{Name}')",
                        deviceIndex, detectedName);
                    SetState(WebcamTrackingState.CameraDenied);
                }
                return null;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "WebcamTrackingService: OpenCaptureCore threw");
                SetState(WebcamTrackingState.Error);
                return null;
            }
        }

        /// <summary>
        /// Opens <paramref name="deviceIndex"/> with a single backend / pixel-format
        /// request and returns the capture only if it produces a usable probe frame;
        /// otherwise disposes it and returns null so the caller can try the next
        /// attempt. <paramref name="fourcc"/> is a 4-char FOURCC (e.g. "MJPG") to
        /// request, or null to leave the driver's default format negotiation alone.
        /// Sets <paramref name="anyOpened"/> to true if the device opened at all (even
        /// if no usable frame arrived) so the caller can distinguish CameraDenied from
        /// CameraInUse.
        /// </summary>
        /// <param name="generation">#743: the <see cref="_openGeneration"/> stamp of the owning
        /// attempt. Any bump means this probe must abandon the handle rather than return it.</param>
        private VideoCapture? TryOpenWithBackend(int deviceIndex, string detectedName, VideoCaptureAPIs api, string apiName, string? fourcc, ref bool anyOpened, int generation)
        {
            string label = fourcc != null ? apiName + "/" + fourcc : apiName;
            App.Logger?.Information("WebcamTrackingService: opening device index {Index} ('{Name}') with {Api}", deviceIndex, detectedName, label);
            VideoCapture? cap = null;
            try
            {
                cap = new VideoCapture(deviceIndex, api);
                if (!cap.IsOpened())
                {
                    App.Logger?.Information("WebcamTrackingService: {Api} open failed for index {Index}", label, deviceIndex);
                    cap.Dispose();
                    return null;
                }
                anyOpened = true;

                // Request the explicit pixel format before resolution when one is
                // given. Elgato Facecam Neo (and many UVC cams) hand OpenCV a
                // non-empty-but-garbage feed under default format negotiation, which
                // reads fine yet yields no detectable face; MJPG is broadly supported
                // and decodes cleanly. Only escalated to after the default attempt
                // fails, so cameras that already work keep their native format.
                if (fourcc != null && fourcc.Length == 4)
                {
                    cap.Set(VideoCaptureProperties.FourCC, VideoWriter.FourCC(fourcc[0], fourcc[1], fourcc[2], fourcc[3]));
                }
                cap.Set(VideoCaptureProperties.FrameWidth, CaptureWidth);
                cap.Set(VideoCaptureProperties.FrameHeight, CaptureHeight);
                cap.Set(VideoCaptureProperties.Fps, TargetFps);
                cap.Set(VideoCaptureProperties.BufferSize, 1);

                // Slow drivers (USB UVC over a hub, virtual cameras that lazy-init
                // their pipeline, the Lovense Webcam 2 that streams black for >1s) can
                // return empty or degenerate frames right after the handle opens even
                // though the device isn't held by another app — poll up to ProbeWarmupMs
                // so that case warms up and is adopted instead of being misdiagnosed as
                // CameraInUse (BUG-6W469QGMHS).
                using var probe = new Mat();
                using var prevProbe = new Mat();
                using var diff = new Mat();
                bool havePrev = false;
                var probeDeadline = DateTime.UtcNow.AddMilliseconds(ProbeWarmupMs);
                int probeReads = 0;
                while (DateTime.UtcNow < probeDeadline)
                {
                    if (Volatile.Read(ref _openGeneration) != generation) { cap.Dispose(); return null; }
                    probeReads++;
                    if (cap.Read(probe) && !probe.Empty())
                    {
                        Cv2.MeanStdDev(probe, out var mean, out var std);
                        double maxStd = Math.Max(std.Val0, Math.Max(std.Val1, std.Val2));

                        // Temporal change vs the previous (flat) frame. A dim-but-real
                        // feed moves frame-to-frame even when its spatial contrast is
                        // below MinProbeStdDev; a solid / frozen garbage feed does not.
                        // This is a SECOND acceptance path so a dark room isn't rejected
                        // into a false CameraInUse — spatial-clean still wins first, so
                        // the Elgato flat+static feed is unaffected (BUG-F2XJE2E7X9).
                        double temporalDelta = 0;
                        if (havePrev && prevProbe.Size() == probe.Size() && prevProbe.Type() == probe.Type())
                        {
                            Cv2.Absdiff(probe, prevProbe, diff);
                            var dmean = Cv2.Mean(diff);
                            temporalDelta = Math.Max(dmean.Val0, Math.Max(dmean.Val1, dmean.Val2));
                        }

                        if (maxStd >= MinProbeStdDev || temporalDelta >= MinProbeTemporalDelta)
                        {
                            App.Logger?.Information(
                                "WebcamTrackingService: device {Index} ('{Name}') via {Api} probe ok ({W}x{H}, mean={Mean:F1}, maxStdDev={Std:F1}, temporalDelta={Td:F1}, reads={Reads})",
                                deviceIndex, detectedName, label, probe.Width, probe.Height, mean.Val0, maxStd, temporalDelta, probeReads);
                            var result = cap;
                            cap = null; // hand ownership to caller; keep the catch from disposing it
                            return result;
                        }

                        // Non-empty but degenerate (solid colour / black, and not yet
                        // changing) — remember it and keep probing within the warm-up
                        // budget in case it's a slow-warming or dim-but-live feed.
                        App.Logger?.Debug(
                            "WebcamTrackingService: device {Index} via {Api} degenerate probe frame (maxStdDev={Std:F1} < {Min}, temporalDelta={Td:F1} < {MinTd})",
                            deviceIndex, label, maxStd, MinProbeStdDev, temporalDelta, MinProbeTemporalDelta);
                        probe.CopyTo(prevProbe);
                        havePrev = true;
                    }
                    System.Threading.Thread.Sleep(ProbeReadIntervalMs);
                }

                App.Logger?.Warning(
                    "WebcamTrackingService: device index {Index} ('{Name}') opened via {Api} but produced no usable frame in {Budget}ms ({Reads} reads) — trying next attempt",
                    deviceIndex, detectedName, label, ProbeWarmupMs, probeReads);
                cap.Dispose();
                return null;
            }
            catch (Exception ex)
            {
                // A DllNotFoundException (often wrapped in TypeInitializationException from
                // OpenCvSharp's NativeMethods cctor) means OpenCvSharpExtern.dll couldn't load
                // its dependencies — the MSVC runtime. Flag it so the caller reports the real
                // cause instead of CameraDenied. Newer installers bundle the VC++ redist (#347).
                if (ex is DllNotFoundException
                    || ex is TypeInitializationException
                    || ex.InnerException is DllNotFoundException)
                {
                    _nativeRuntimeMissing = true;
                }
                App.Logger?.Warning(ex, "WebcamTrackingService: TryOpenWithBackend({Api}) threw for index {Index}", label, deviceIndex);
                try { cap?.Dispose(); } catch { }
                return null;
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
            _eyesClosedLongFired = false;
            _minEarThisClosure = double.MaxValue;
            _windowMinEar = double.MaxValue;
            _windowMaxEar = double.MinValue;
            _windowNearMissCount = 0;
            _lastBlinkDiagAt = DateTime.MinValue;
            _blinkCount = 0;
            _marBuffer.Clear();
            _marSmoothBuffer.Clear();
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
            _leftRefCxBuffer.Clear(); _leftRefCyBuffer.Clear(); _leftRefWBuffer.Clear();
            _rightRefCxBuffer.Clear(); _rightRefCyBuffer.Clear(); _rightRefWBuffer.Clear();
            _irisDxMedianBuffer.Clear(); _irisDyMedianBuffer.Clear();
            _screenXFilter.Reset(); _screenYFilter.Reset();
            _eyeDisagreementBuffer.Clear();
            _twoEyeStreak = 0;
            _gateSkipStreak = 0;
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
            //    Gated for two-eye consistency before it feeds the smoothing
            //    chain — see the gate constants block for the model.
            (double Dx, double Dy)? vLeft = null, vRight = null;
            if (leftEye != null)
            {
                vLeft = NormalizeIrisVectorSmoothed(leftEye.IrisCenter, landmarks[LeftEyeOuterIdx], landmarks[LeftEyeInnerIdx],
                    _leftRefCxBuffer, _leftRefCyBuffer, _leftRefWBuffer);
            }
            if (rightEye != null)
            {
                vRight = NormalizeIrisVectorSmoothed(rightEye.IrisCenter, landmarks[RightEyeOuterIdx], landmarks[RightEyeInnerIdx],
                    _rightRefCxBuffer, _rightRefCyBuffer, _rightRefWBuffer);
            }

            if (vLeft.HasValue && vRight.HasValue)
            {
                double ddx = vLeft.Value.Dx - vRight.Value.Dx;
                double ddy = vLeft.Value.Dy - vRight.Value.Dy;
                double disagreement = Math.Sqrt(ddx * ddx + ddy * ddy);

                bool spike = false;
                if (_eyeDisagreementBuffer.Count >= EyeDisagreementMinSamples)
                {
                    double baseline = PercentileOf(_eyeDisagreementBuffer, 0.50);
                    spike = disagreement > baseline * EyeDisagreementRatio + EyeDisagreementFloor;
                }

                if (spike && _gateSkipStreak < MaxGateSkipFrames)
                {
                    // One eye is off (glint, lash, partial lid) — the average
                    // would jump. Hold the last output instead; don't feed the
                    // outlier into the baseline either.
                    _gateSkipStreak++;
                    return;
                }

                _gateSkipStreak = 0;
                _twoEyeStreak++;
                EnqueueWithCap(_eyeDisagreementBuffer, disagreement, EyeDisagreementBaselineFrames);
                EmitGazeEvents(
                    (vLeft.Value.Dx + vRight.Value.Dx) / 2.0,
                    (vLeft.Value.Dy + vRight.Value.Dy) / 2.0);
            }
            else
            {
                var v = vLeft ?? vRight;
                if (!v.HasValue) return;

                if (_twoEyeStreak >= TwoEyeStreakRequired && _gateSkipStreak < MaxGateSkipFrames)
                {
                    // Sudden one-eye frame after a healthy two-eye run —
                    // transient occlusion. Skipping (holding last output)
                    // beats emitting the one-eye average, which sits at a
                    // different point than the two-eye average and reads as
                    // a cursor jump. Persistent one-eye falls through once
                    // the skip budget is spent.
                    _gateSkipStreak++;
                    return;
                }

                _gateSkipStreak = 0;
                _twoEyeStreak = 0;
                EmitGazeEvents(v.Value.Dx, v.Value.Dy);
            }
        }

        /// <summary>
        /// Convert an iris-center pixel position into a head-pose-stable iris
        /// vector relative to the eye-corner midpoint, scaled by corner-to-corner
        /// distance. Output is roughly in [-0.5, +0.5] for normal gaze ranges.
        ///
        /// The eye-corner reference frame (midpoint + width) is smoothed over a
        /// short rolling window before it's applied, using the per-eye buffers
        /// passed in. This strips the per-frame FaceMesh corner jitter that
        /// would otherwise shift the vector's origin and scale every frame (see
        /// EyeRefSmoothFrames). The iris center itself is left un-smoothed so
        /// gaze stays responsive.
        /// </summary>
        private (double Dx, double Dy) NormalizeIrisVectorSmoothed(
            (double X, double Y) iris, float[] outerCorner, float[] innerCorner,
            Queue<double> cxBuf, Queue<double> cyBuf, Queue<double> wBuf)
        {
            double cx = (outerCorner[0] + innerCorner[0]) / 2.0;
            double cy = (outerCorner[1] + innerCorner[1]) / 2.0;
            double w = Math.Sqrt(
                (outerCorner[0] - innerCorner[0]) * (outerCorner[0] - innerCorner[0]) +
                (outerCorner[1] - innerCorner[1]) * (outerCorner[1] - innerCorner[1]));
            if (w < 1.0) return (0, 0);

            EnqueueWithCap(cxBuf, cx, EyeRefSmoothFrames);
            EnqueueWithCap(cyBuf, cy, EyeRefSmoothFrames);
            EnqueueWithCap(wBuf, w, EyeRefSmoothFrames);
            double scx = Average(cxBuf);
            double scy = Average(cyBuf);
            double sw = Average(wBuf);
            if (sw < 1.0) return (0, 0);

            return ((iris.X - scx) / sw, (iris.Y - scy) / sw);
        }

        private static double Average(Queue<double> q)
        {
            if (q.Count == 0) return 0;
            double s = 0;
            foreach (var v in q) s += v;
            return s / q.Count;
        }

        private static double MedianFilter(Queue<double> buffer, double value, int window)
        {
            buffer.Enqueue(value);
            while (buffer.Count > window) buffer.Dequeue();
            var arr = buffer.ToArray();
            Array.Sort(arr);
            return arr[arr.Length / 2];
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
            _eyesClosedLongFired = false;
            _mouthOpen = false;
            _mouthOpenedAt = null;
            _marSmoothBuffer.Clear();   // drop stale MAR so the median doesn't blip on face re-acquire
            _tongueOut = false;
            _tongueOutSince = null;
            // Drop stale gaze-smoothing state so the cursor doesn't average
            // across the face-loss gap (which would yank it on re-acquire).
            _leftRefCxBuffer.Clear(); _leftRefCyBuffer.Clear(); _leftRefWBuffer.Clear();
            _rightRefCxBuffer.Clear(); _rightRefCyBuffer.Clear(); _rightRefWBuffer.Clear();
            _irisDxMedianBuffer.Clear(); _irisDyMedianBuffer.Clear();
            _screenXFilter.Reset(); _screenYFilter.Reset();
            _twoEyeStreak = 0;
            _gateSkipStreak = 0;
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

            // Median pre-filter (≤1 frame lag) to reject single-frame landmark
            // spikes before any other stage. Applied up here so OnRawIris (the
            // calibration sampler) sees exactly the signal the runtime path
            // projects — calibration and runtime must agree on the input
            // transform or the trained mapping is fed a different signal than
            // it was fit against.
            irisDx = MedianFilter(_irisDxMedianBuffer, irisDx, IrisMedianFrames);
            irisDy = MedianFilter(_irisDyMedianBuffer, irisDy, IrisMedianFrames);

            // Raw iris vector — used by the calibration window to sample reference
            // points. Always fires (when eyes are open); calibration consumers
            // expect a per-frame event during sampling. Fired BEFORE head-pose
            // comp so the calibration fits (grid polynomial AND the guided
            // motion comp itself) train on the uncompensated signal.
            Dispatch(() => OnRawIris?.Invoke(irisDx, irisDy));

            // NOTE: head-pose compensation is fully RETIRED (2026-07-02). Two
            // generations died here: (1) natural-variance fits — PnP noise
            // dominated when the user held still; (2) guided-motion fits from
            // a dedicated calibration phase — the pitch term actively fought
            // vertical gaze (cursor stuck at the bottom of the screen), and
            // even the yaw-only variant wasn't worth its calibration step.
            // Saved calibrations may still carry HeadPoseComp/BaselineHeadPose
            // fields; they are deliberately ignored. Robustness to head
            // movement comes from the smoothing chain + iris-range clamp +
            // axis residual correction instead.

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

            // (Head-pose compensation, when a guided-motion fit exists, was
            // already applied to irisDx/irisDy upstream — before the
            // smoothing split — so both the side classifier and this cursor
            // path see the corrected signal. An earlier natural-variance comp
            // that lived at this spot was retired as noise; see the comp
            // block after the OnRawIris dispatch for the current pipeline.)

            // Clamp the smoothed iris vector to the calibrated range plus a
            // margin before projecting. The polynomial is only valid inside
            // the hull it was trained on; its quadratic terms extrapolate
            // violently outside it, so an iris sample past the calibrated
            // extremes (glance at the keyboard, residual glint noise) used to
            // sling the cursor way past the screen and the bounds clamp
            // pinned it to an edge until the filters recovered. The margin
            // leaves room to reach the bezel-adjacent strip the EdgeMargin
            // dots couldn't cover, without letting extrapolation run away.
            if (Calibration?.IrisRange is { } range)
            {
                // Margin is deliberately generous: its job is only to bound the
                // polynomial's quadratic blow-up, not to fence normal use. A
                // tight margin (first tried 0.18) saturated as soon as head
                // drift shifted the iris baseline — every eye movement then
                // projected to the same clamped extreme and the cursor sat
                // pinned at a screen edge ("locked to the sides").
                const double IrisClampMarginFrac = 0.35;
                double spanX = Math.Max(1e-6, range.MaxX - range.MinX);
                double spanY = Math.Max(1e-6, range.MaxY - range.MinY);
                smoothDx = Math.Max(range.MinX - spanX * IrisClampMarginFrac,
                           Math.Min(range.MaxX + spanX * IrisClampMarginFrac, smoothDx));
                smoothDy = Math.Max(range.MinY - spanY * IrisClampMarginFrac,
                           Math.Min(range.MaxY + spanY * IrisClampMarginFrac, smoothDy));
            }

            var screenPoint = ProjectGazeToScreen(smoothDx, smoothDy);
            if (screenPoint.HasValue)
            {
                var p = screenPoint.Value;

                // Screen-space velocity-adaptive smoothing. The iris-space
                // One-Euro above is uneven across the screen because the
                // polynomial slope steepens toward the edges; this second pass
                // in screen-DIP space evens the jitter out where the user sees
                // it. Applied before the quick-recal offset and the bounds
                // clamp so the smoothing operates on raw projected motion, not
                // on the post-clamp edge plateau (which would otherwise feed
                // the velocity estimator a string of identical clamped points).
                p = new System.Windows.Point(
                    _screenXFilter.Filter(p.X, nowTicks),
                    _screenYFilter.Filter(p.Y, nowTicks));

                // Quick-recal translational nudge — corrects whole-map drift
                // captured after the user clicked "Quick Recal" on a center
                // dot. Null on calibrations that haven't run quick-recal yet.
                if (Calibration?.RuntimeOffset is { } off)
                {
                    p = new System.Windows.Point(p.X + off.Dx, p.Y + off.Dy);
                }

                // Bubble-test trim — per-axis offset + scale measured against
                // the test bubbles ("cursor went up but the bubble was
                // down" → the map is stretched; the trim un-stretches it).
                // Applied before the pre-lock snapshot so a rerun of the
                // bubble test measures residuals WITH the trim active and
                // fits only the remainder (corrections converge instead of
                // double-applying).
                if (Calibration?.GazeTrim is { } trim)
                {
                    p = new System.Windows.Point(
                        p.X + trim.X0 + trim.X1 * (p.X - trim.CenterX),
                        p.Y + trim.Y0 + trim.Y1 * (p.Y - trim.CenterY));
                }

                // Soft screen edges instead of a hard clamp. Nobody actually
                // looks at the last few pixels of the bezel — a cursor parked
                // flat against an edge only ever means the mapping saturated,
                // and it reads as "stuck" (repeated user report). The outer
                // band compresses smoothly: motion into it slows down, an
                // off-screen projection settles a bit short of the edge
                // instead of pinning flat against it, and normal mid-screen
                // gaze is untouched.
                if (Calibration?.MonitorBounds is { } bounds && bounds.Width > 0 && bounds.Height > 0)
                {
                    double bandX = Math.Clamp(bounds.Width * 0.055, 28, 90);
                    double bandY = Math.Clamp(bounds.Height * 0.055, 28, 90);
                    p = new System.Windows.Point(
                        SoftEdge(p.X, 0, bounds.Width, bandX),
                        SoftEdge(p.Y, 0, bounds.Height, bandY));
                }

                // Snapshot BEFORE the lock-on so accuracy/residual consumers
                // (calibration bubble test) can read the unflattered point.
                _lastPreLockGaze = new GazeSnapshot(p.X, p.Y);

                p = ApplyGazeLock(p);

                // Final motion shaping: the emitted cursor is a slowed
                // follower of the mapped point, not the mapped point itself.
                // Small deviations are heavily damped (the cursor glides, its
                // direction stays consistent, residual jitter is fine), while
                // a genuinely large error — the user snapped their gaze
                // across the screen — ramps the follow speed up smoothly so
                // saccades are caught in a beat without ever teleporting.
                // This also makes the target lock read as natural drift
                // instead of an obvious magnet snap.
                p = ShapeCursorMotion(p);

                Dispatch(() => OnGazeMove?.Invoke(p));
                UpdateLongStareHeuristic(p);
            }
        }

        // Output follower state — capture thread only.
        private double _followX = double.NaN, _followY = double.NaN;

        /// <summary>
        /// Distance-adaptive exponential follower (see the comment at the
        /// call site). Follow fraction per frame eases quadratically from
        /// FollowMin (smooth glide) to FollowMax (saccade catch-up) as the
        /// distance to the mapped point approaches RampDist — quadratic so
        /// mid-size corrections stay calm and only real gaze jumps get the
        /// speed. Never snaps: even at max the approach is exponential.
        /// </summary>
        private System.Windows.Point ShapeCursorMotion(System.Windows.Point target)
        {
            const double FollowMin = 0.09;  // ~370ms time constant at 30fps — a slow, deliberate glide
            const double FollowMax = 0.48;  // large jumps land in ~4-5 frames, still eased
            const double RampDist = 480.0;  // DIPs to reach full catch-up speed

            if (double.IsNaN(_followX)) { _followX = target.X; _followY = target.Y; return target; }
            double ex = target.X - _followX, ey = target.Y - _followY;
            double dist = Math.Sqrt(ex * ex + ey * ey);
            double t = Math.Min(1.0, dist / RampDist);
            double alpha = FollowMin + (FollowMax - FollowMin) * t * t;
            _followX += ex * alpha;
            _followY += ey * alpha;
            return new System.Windows.Point(_followX, _followY);
        }

        /// <summary>
        /// Soft edge compression for one axis. Inside the inner region the
        /// value passes through untouched; within the outer band it is
        /// compressed with a tanh so approach slows smoothly; a wildly
        /// off-screen projection asymptotes ~0.2×band short of the true
        /// edge — visible, clearly "over there", but never glued flat to
        /// the bezel.
        /// </summary>
        private static double SoftEdge(double v, double lo, double hi, double band)
        {
            double innerLo = lo + band, innerHi = hi - band;
            if (innerHi <= innerLo) return Math.Max(lo, Math.Min(hi, v));
            if (v < innerLo) return innerLo - band * 0.8 * Math.Tanh((innerLo - v) / band);
            if (v > innerHi) return innerHi + band * 0.8 * Math.Tanh((v - innerHi) / band);
            return v;
        }

        /// <summary>
        /// Stateful target lock-on (see the constant block by SetGazeAttractor).
        /// Engagement builds while the PRE-lock gaze lingers inside the capture
        /// zone and drains when it leaves or when the measured sway outgrows
        /// the target radius — the release is gradual, never a snap. Sway and
        /// distance are always measured on the raw point, so the lock can't
        /// feed its own output back and hold forever. Capture thread only.
        /// </summary>
        private System.Windows.Point ApplyGazeLock(System.Windows.Point p)
        {
            var att = _attractor;
            if (att == null)
            {
                _lockEngage = 0;
                _lockCenterX = _lockCenterY = double.NaN;
                _gazeEmaX = _gazeEmaY = double.NaN;
                _swayEma = 0;
                return p;
            }

            // Target switch (a different bubble, not the same one drifting):
            // engagement doesn't carry over to a new target.
            if (!double.IsNaN(_lockCenterX))
            {
                double mdx = att.X - _lockCenterX, mdy = att.Y - _lockCenterY;
                if (Math.Sqrt(mdx * mdx + mdy * mdy) > att.Radius) _lockEngage = 0;
            }
            _lockCenterX = att.X;
            _lockCenterY = att.Y;

            // Sway estimate: EMA of the deviation from a short-horizon EMA of
            // the raw gaze — roughly the wobble amplitude in DIPs.
            if (double.IsNaN(_gazeEmaX)) { _gazeEmaX = p.X; _gazeEmaY = p.Y; }
            _gazeEmaX += (p.X - _gazeEmaX) * 0.25;
            _gazeEmaY += (p.Y - _gazeEmaY) * 0.25;
            double wx = p.X - _gazeEmaX, wy = p.Y - _gazeEmaY;
            _swayEma += (Math.Sqrt(wx * wx + wy * wy) - _swayEma) * 0.15;

            double dx = p.X - att.X, dy = p.Y - att.Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);

            if (dist <= att.Radius * GazeLockCaptureFrac)
                _lockEngage += (1.0 - _lockEngage) * GazeLockBuildRate;
            else
                _lockEngage -= GazeLockLeaveDrain;

            double swayNorm = _swayEma / Math.Max(1e-6, att.Radius);
            if (swayNorm > GazeLockSwayFrac)
                _lockEngage -= (swayNorm - GazeLockSwayFrac) * GazeLockSwayDrain;

            _lockEngage = Math.Max(0, Math.Min(1, _lockEngage));
            if (_lockEngage <= 0) return p;

            // Distance taper: full pull inside the radius, fading to nothing
            // by 4× — quadrant reach, so an engaged lock draws the cursor in
            // from anywhere in the target's region of the screen.
            double taper = dist <= att.Radius
                ? 1.0
                : dist <= att.Radius * 4.0
                    ? (att.Radius * 4.0 - dist) / (att.Radius * 3.0)
                    : 0.0;
            double k = GazeLockMaxStrength * _lockEngage * taper;
            if (k <= 0) return p;
            return new System.Windows.Point(p.X - dx * k, p.Y - dy * k);
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
                // Axis residual correction — cancels the polynomial's
                // systematic per-row/per-column bias (measured at calibration
                // finalize from the grid's own dots). This is what stops the
                // "everything is skewed toward the top, can't reach the
                // bottom" failure: without it a bottom row that projects
                // 150 px high stays 150 px high forever.
                if (Calibration?.AxisCorrection is { } ac)
                {
                    x = ApplyAxisCurve(ac.SrcX, ac.DstX, x);
                    y = ApplyAxisCurve(ac.SrcY, ac.DstY, y);
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

        /// <summary>
        /// Piecewise-linear map through calibration anchors (see
        /// AxisCorrectionData): linear interpolation between anchors,
        /// end-segment slope beyond them. Anchors are validated monotonic
        /// and gain-sane at build time, so no guards are needed per frame.
        /// </summary>
        private static double ApplyAxisCurve(double[] src, double[] dst, double v)
        {
            int n = src.Length;
            if (n < 2 || dst.Length != n) return v;
            int i = 1;
            while (i < n - 1 && v > src[i]) i++;
            double t = (v - src[i - 1]) / (src[i] - src[i - 1]);
            return dst[i - 1] + t * (dst[i] - dst[i - 1]);
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

        private void ReportStartupProgress(double progress, string status)
            => Dispatch(() => OnStartupProgress?.Invoke(progress, status));

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
                _eyesClosedLongFired = false;
            }
            else if (nowClosed)
            {
                if (avgEar < _minEarThisClosure) _minEarThisClosure = avgEar;
                // Still closed: the 2s "hold" gesture fires here, once per closure,
                // without waiting for the eyes to reopen.
                if (!_eyesClosedLongFired && _eyesClosedAt.HasValue
                    && (now - _eyesClosedAt.Value).TotalMilliseconds >= EyesClosedLongMs)
                {
                    _eyesClosedLongFired = true;
                    Dispatch(() => OnEyesClosedLong?.Invoke());
                }
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
                _eyesClosedLongFired = false;
            }
            else if (!nowClosed)
            {
                _eyesClosedAt = null;
                _minEarThisClosure = double.MaxValue;
                _eyesClosedLongFired = false;
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

        private void UpdateMouthState(double rawMar)
        {
            // De-jitter the noisy raw MAR with a short median before any
            // thresholding. Without this, single-frame landmark spikes flip the
            // open/close hysteresis and reset the min-open timer, dropping real
            // opens. PercentileOf(.,0.50) on the small buffer is the median.
            EnqueueWithCap(_marSmoothBuffer, rawMar, MarSmoothFrames);
            double mar = PercentileOf(_marSmoothBuffer, 0.50);

            // Resting-closed baseline = 10th percentile of recent CLOSED frames.
            // Closed-mouth values dominate the buffer; the 10th percentile
            // approximates the resting closed MAR, and we compare current MAR to
            // it (e.g. >1.8× resting = open). We deliberately STOP feeding the
            // baseline while the mouth is open (except to seed it initially) so
            // repeated or held opens can't drag the baseline — and thus the open
            // threshold — upward, which previously made the 2nd/3rd open in a
            // row harder to detect than the first.
            if (!_mouthOpen || _marBuffer.Count < MarMinSamplesForBaseline)
                EnqueueWithCap(_marBuffer, mar, MarBaselineFrames);
            _marBaseline = PercentileOf(_marBuffer, 0.10);

            if (mar < _windowMinMar) _windowMinMar = mar;
            if (mar > _windowMaxMar) _windowMaxMar = mar;

            var now = DateTime.UtcNow;
            MaybeLogMouthDiag(now);

            if (_marBuffer.Count < MarMinSamplesForBaseline) return;
            if (_marBaseline <= 0) return;

            // Open if EITHER the relative ratio (vs resting baseline) OR the
            // absolute floor is crossed. The absolute path is the low-light
            // rescue: when a dim frame inflates the baseline so the ratio can't
            // be reached, a genuinely wide-open mouth still trips the floor.
            //
            // BUT an absolute floor is only meaningful when it sits ABOVE the
            // resting baseline. For users whose resting (closed) mouth MAR is
            // naturally high (>= MarAbsoluteClose), the fixed close floor sits
            // *below* their shut mouth, so `mar > MarAbsoluteClose` stays true
            // even when the mouth is closed — latching MouthOpen on forever and
            // spamming phantom mouth/tongue gestures (#367/#371). Gate each
            // absolute path on the floor genuinely exceeding the baseline; when
            // it doesn't, fall back to the relative ratio alone.
            // Per-user sensitivity (Webcam settings slider, 0..1, default 0.5) scales the ENTER-open
            // thresholds: below 0.5 raises them so facial hair / naturally-parted lips stop producing
            // phantom mouth-open fires (#432); above 0.5 lowers them for the under-detected. 0.5 is
            // neutral. The close/hysteresis floors are left fixed (they stay below the scaled open).
            double sensitivity = Math.Clamp(App.Settings?.Current?.WebcamSensitivity ?? 0.5, 0.0, 1.0);
            double openScale = 1.0 + (0.5 - sensitivity) * 0.8;   // 1.4 @0.0 → 1.0 @0.5 → 0.6 @1.0
            double openRatio = MarOpenRatio * openScale;
            double absoluteOpen = MarAbsoluteOpen * openScale;

            bool absoluteOpenUsable  = absoluteOpen  > _marBaseline;
            bool absoluteCloseUsable = MarAbsoluteClose > _marBaseline;
            // Relative path requires the speech floor too — see MarSpeechFloor.
            double speechFloor = MarSpeechFloor * openScale;
            bool nowOpen = _mouthOpen
                ? (mar > MarCloseRatio * _marBaseline || (absoluteCloseUsable && mar > MarAbsoluteClose))
                : ((mar > openRatio * _marBaseline && mar > speechFloor)
                    || (absoluteOpenUsable && mar > absoluteOpen));

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
