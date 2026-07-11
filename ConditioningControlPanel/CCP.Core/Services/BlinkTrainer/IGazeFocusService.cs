namespace ConditioningControlPanel.Core.Services.BlinkTrainer;

/// <summary>
/// Cross-platform seam for gaze dwell / blink-pop interaction with bubbles and flashes.
/// </summary>
public interface IGazeFocusService
{
    /// <summary>True while gaze processing is active.</summary>
    bool IsActive { get; }

    /// <summary>How long gaze must linger before a dwell pop fires, in milliseconds.</summary>
    int DwellMs { get; set; }

    /// <summary>
    /// The explicit master "Focus Gaze" arm toggle. The engine also runs whenever any
    /// per-feature gaze consumer (flash gaze-pop / linger, video gaze-click) is enabled
    /// AND the shared webcam is already running — see the implementation's
    /// EvaluateDesiredState (WPF parity: GazeFocusService.cs:90, :170). Defaults to false;
    /// setting it re-evaluates whether the engine should be active. This flag and the
    /// per-feature consumers NEVER power the camera on by themselves — only the explicit
    /// <see cref="Start"/> path starts the webcam.
    /// </summary>
    bool MasterEnabled { get; set; }

    /// <summary>Starts gaze processing. Returns false if the webcam cannot be started.</summary>
    bool Start();

    /// <summary>Stops gaze processing without stopping the shared webcam.</summary>
    void Stop();

    /// <summary>Fires when <see cref="IsActive"/> flips.</summary>
    event Action<bool>? OnActiveChanged;

    /// <summary>Fires when a bubble is popped by gaze (dwell or blink).</summary>
    event Action? GazePopped;
}
