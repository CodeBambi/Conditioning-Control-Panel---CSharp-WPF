using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Core.Services.Webcam;

/// <summary>
/// Horizontal gaze direction relative to the calibrated screen center.
/// </summary>
public enum GazeSide { Left, Right, Center }

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
