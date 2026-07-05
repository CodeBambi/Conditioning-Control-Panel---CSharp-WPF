using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Core.Services.Webcam;

/// <summary>
/// Horizontal gaze direction relative to the calibrated screen center.
/// </summary>
public enum GazeSide { Left, Right, Center }

/// <summary>
/// Which gaze-feature pipeline the tracker feeds into the (feature-agnostic)
/// calibrate→fit→project stack. <see cref="Current"/> = the classical
/// MediaPipe-iris vector (the shipped baseline, kept 100% intact for A/B).
/// <see cref="DeepModel"/> = an appearance-based deep gaze estimator
/// (MobileGaze / L2CS-Net ONNX) that emits a head-pose-invariant (yaw,pitch)
/// feature. The selected mode is chosen at calibration time and recorded in the
/// calibration so runtime feeds the matching feature.
/// </summary>
// Current   = the shipped MediaPipe iris-vector pipeline (default, never removed - A/B baseline).
// Tier1     = improved-classical: the SAME iris vector but roll-normalized (de-rotated by the
//             head-tilt angle between the two outer eye corners) and fit with a 3rd-order cubic
//             ridge instead of the 2nd-order Cerrolaza polynomial. No new model / license.
// DeepModel = appearance-based deep ONNX gaze (head-pose-invariant yaw/pitch).
public enum GazeFeatureMode { Current, Tier1, DeepModel }

/// <summary>
/// Deep-gaze ONNX backbone (all share an identical I/O contract: input
/// [1,3,448,448], named outputs yaw/pitch [1,90]). Selectable via a dropdown
/// when <see cref="GazeFeatureMode.DeepModel"/> is active. Ordered fastest →
/// most accurate; <see cref="MobileOneS0"/> is the default.
/// </summary>
public enum DeepGazeBackbone { MobileOneS0, MobileNetV2, ResNet18, ResNet34, ResNet50 }

/// <summary>
/// Coarse state of the webcam tracking engine, surfaced via
/// <see cref="IWebcamService.OnTrackingStateChanged"/>. Consumers show honest
/// status text (starting / tracking / camera-busy / denied / error) instead of
/// optimistically assuming tracking succeeded.
/// </summary>
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
/// Snapshot of a captured quick-recal translational offset (screen DIPs) plus
/// the moment it was taken. Portable, Core-friendly view of the concrete
/// tracker's <c>RuntimeOffsetData</c>; round-tripped through
/// <see cref="IWebcamService.GetRuntimeOffset"/> /
/// <see cref="IWebcamService.SetRuntimeOffset"/>.
/// </summary>
public sealed record RuntimeGazeOffset(double Dx, double Dy, DateTime CapturedAt);

/// <summary>
/// One enumerable capture device: the OpenCV <c>VideoCapture</c> index and the
/// OS-reported friendly name. Portable, Core-friendly view of the concrete
/// tracker's device record so the Lab / Blink Trainer device combos can be
/// populated through the seam.
/// </summary>
public sealed record WebcamDeviceInfo(int Index, string Name);

/// <summary>
/// One raw-iris sample captured while the user fixated a calibration dot, paired with the
/// head pose at that frame. <c>Dx</c>/<c>Dy</c> are the averaged iris vector (~[-0.5,+0.5]);
/// <c>Yaw</c>/<c>Pitch</c> are head-pose degrees, valid only when <c>HasPose</c> is true.
/// Portable, Core-friendly view the shared calibration window hands to the platform tracker.
/// </summary>
public readonly record struct CalibrationIrisSample(double Dx, double Dy, double Yaw, double Pitch, bool HasPose);

/// <summary>
/// All raw-iris samples collected while the user fixated one calibration dot, paired with that
/// dot's on-screen target in calibrated-monitor-local screen pixels/DIPs.
/// </summary>
public sealed record CalibrationDotSamples(double TargetX, double TargetY, IReadOnlyList<CalibrationIrisSample> Samples);

/// <summary>
/// Outcome of <see cref="IWebcamService.BuildCalibrationPreview"/>: whether the fit succeeded,
/// the residual RMS per axis (screen px; lower is better), and a message when it failed.
/// </summary>
public sealed record CalibrationPreviewResult(bool Success, double RmsX, double RmsY, string? Error = null);

/// <summary>
/// Cross-platform seam for webcam / gaze tracking.
/// The legacy engine lives in the WPF head under <c>Lab/GazeMinigame</c> and related services.
/// </summary>
public interface IWebcamService
{
    /// <summary>Whether gaze/webcam tracking is currently active.</summary>
    bool IsRunning { get; }

    /// <summary>
    /// True when a calibration is loaded (a homography / polynomial fit has
    /// been persisted and is available for gaze projection). Quick Recal and
    /// the tracker test require this; full Calibrate produces it.
    /// </summary>
    bool HasCalibration { get; }

    /// <summary>Start webcam/gaze tracking.</summary>
    void StartTracking();

    /// <summary>Stop webcam/gaze tracking.</summary>
    void StopTracking();

    /// <summary>Run a one-shot calibration routine.</summary>
    void Calibrate();

    /// <summary>Run a tracker self-test.</summary>
    void TestTracker();

    /// <summary>Refresh the list of available capture devices.</summary>
    void RefreshDevices();

    /// <summary>
    /// Enumerate the connected video-capture devices. Implementations that
    /// cannot enumerate (e.g. the in-memory stub on a headless platform)
    /// return an empty list — never <c>null</c>.
    /// </summary>
    IReadOnlyList<WebcamDeviceInfo> EnumerateDevices();

    /// <summary>Revoke user consent and stop tracking.</summary>
    void RevokeConsent();

    /// <summary>
    /// Returns the monitor the current calibration was performed on, or null when
    /// no calibration is loaded or the calibrated monitor is no longer connected.
    /// Implementations that do not support calibration should return null.
    /// </summary>
    ScreenInfo? GetCalibratedScreen() => null;

    /// <summary>
    /// Snapshot the current quick-recal runtime offset, or null when no
    /// calibration is loaded / quick-recal has never been run. Used by the
    /// Quick Recal window to save + restore the prior offset around a re-sample.
    /// </summary>
    RuntimeGazeOffset? GetRuntimeOffset();

    /// <summary>
    /// Persist a freshly measured quick-recal offset (screen DIPs) as the live
    /// calibration's translational nudge. No-op (returns) when no calibration
    /// is loaded. When <paramref name="persist"/> is true the calibration file
    /// is rewritten; otherwise the change is in-memory only.
    /// </summary>
    void SetRuntimeOffset(double dx, double dy, bool persist);

    /// <summary>
    /// Drop the quick-recal offset entirely (passing null through the same
    /// atomic swap path as <see cref="SetRuntimeOffset"/>). Used by Quick Recal
    /// to sample raw projection output, then restore on cancel.
    /// </summary>
    void ClearRuntimeOffset(bool persist);

    /// <summary>
    /// Solve a gaze calibration from per-dot iris samples and apply it IN-MEMORY only (no disk
    /// write) so <see cref="OnGazeMove"/> immediately reflects the candidate fit for a verify pass.
    /// The shared calibration window owns the sampling UI + dot targets; the platform tracker owns
    /// the solver and the (platform-specific) calibration data model. Default no-op returns a failed
    /// result — platforms without a real tracker cannot calibrate, and the window shows an honest
    /// "not available" panel.
    /// </summary>
    CalibrationPreviewResult BuildCalibrationPreview(IReadOnlyList<CalibrationDotSamples> dots, ScreenInfo screen, string mode)
        => new(false, 0, 0, "Calibration is not supported on this platform.");

    /// <summary>
    /// Persist the calibration most recently previewed by <see cref="BuildCalibrationPreview"/>
    /// (write the calibration file and keep it live). No-op when there is no pending preview.
    /// </summary>
    void CommitCalibration() { }

    /// <summary>
    /// Discard a pending calibration preview and revert to the previously loaded calibration (or
    /// none). Called when the user cancels or chooses to recalibrate. No-op by default.
    /// </summary>
    void CancelCalibrationPreview() { }

    // ---- Gaze feature pipeline (A/B/C) ----
    // The tracker runs one feature pipeline at a time; the choice is made at
    // calibration time and persisted into the calibration, so each fit is
    // paired with the feature it was trained on. Default no-ops / Current keep
    // platforms without a real tracker honest.

    /// <summary>The gaze-feature pipeline the tracker is currently running.</summary>
    GazeFeatureMode GazePipelineMode => GazeFeatureMode.Current;

    /// <summary>
    /// Select the gaze-feature pipeline. Call BEFORE a calibrate run so the
    /// resulting fit is stamped with the matching feature. No-op on platforms
    /// without a real tracker.
    /// </summary>
    void SetGazePipelineMode(GazeFeatureMode mode) { }

    /// <summary>The deep-gaze backbone currently selected.</summary>
    DeepGazeBackbone DeepGazeModel => DeepGazeBackbone.MobileOneS0;

    /// <summary>
    /// Select the deep-gaze ONNX backbone. Only meaningful when
    /// <see cref="GazePipelineMode"/> is <see cref="GazeFeatureMode.DeepModel"/>.
    /// Switching backbone should prompt a recalibrate (each carries a slightly
    /// different systematic bias the per-user fit absorbs). No-op by default.
    /// </summary>
    void SetDeepGazeModel(DeepGazeBackbone backbone) { }

    /// <summary>
    /// True when the deep-gaze model files are present and the platform can run
    /// the <see cref="GazeFeatureMode.DeepModel"/> pipeline. The calibration
    /// window greys out the Deep option when this is false.
    /// </summary>
    bool DeepGazeModelAvailable => false;

    // ---- Events consumed by the Deeper enhancement engine + UI ----
    // All events are marshalled to the UI thread by the provider, so handlers
    // may touch UI directly.

    /// <summary>Fires when a blink is detected.</summary>
    event Action? OnBlink;

    /// <summary>Fires when the user holds a long stare; argument is the gaze point in screen pixels.</summary>
    event Action<Point>? OnLongStare;

    /// <summary>Fires when the mouth-open gesture is detected.</summary>
    event Action? OnMouthOpen;

    /// <summary>Fires when the tongue-out gesture is detected.</summary>
    event Action? OnTongueOut;

    /// <summary>Fires when gaze moves; argument is in calibrated-monitor-local screen pixels.</summary>
    event Action<Point>? OnGazeMove;

    /// <summary>Fires with the discrete horizontal gaze direction each classified frame.</summary>
    event Action<GazeSide>? OnGazeSide;

    /// <summary>Fires when a tracked face is lost.</summary>
    event Action? OnFaceLost;

    /// <summary>Fires when a tracked face is found again.</summary>
    event Action? OnFaceFound;

    /// <summary>
    /// Fires on every engine state transition. Lets the UI surface honest
    /// status (starting / tracking / camera-busy / denied / error) and tear
    /// down splash + subscriptions on terminal states.
    /// </summary>
    event Action<WebcamTrackingState>? OnTrackingStateChanged;

    /// <summary>
    /// Fired during <see cref="StartTracking"/> to report engine-load progress
    /// (0.0–1.0) and a human-readable phase label.
    /// </summary>
    event Action<double, string>? OnStartupProgress;

    /// <summary>
    /// Fired with the estimated head pose (yaw, pitch) when a face is tracked.
    /// Diagnostic / advanced-UI only.
    /// </summary>
    event Action<double, double>? OnHeadPose;

    /// <summary>
    /// Raw iris vector (averaged across both eyes), roughly in [-0.5, +0.5],
    /// fired every processed frame when a face is found. Calibration sampling
    /// consumes this; normal feature code should use <see cref="OnGazeMove"/> /
    /// <see cref="OnGazeSide"/> instead.
    /// </summary>
    event Action<double, double>? OnRawIris;
}
