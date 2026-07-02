using System;
using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Core.Services.Speech;

/// <summary>
/// Offline keyword wake service (e.g. "hey bambi") backed by a streaming KWS model (sherpa-onnx on
/// Windows). Preferred over the grammar-based <see cref="ISpeechRecognitionService.WaitForWakeWordAsync"/>
/// wake path when <see cref="IsAvailable"/>. The Windows head implements it; other heads resolve it as
/// null and the autonomy voice falls back to grammar wake.
/// </summary>
public interface ISpeechWakeService
{
    /// <summary>True when the wake spotter can actually run: model present, a mic exists, engine initialised.</summary>
    bool IsAvailable { get; }

    /// <summary>The full KWS model drop-in is present (cheap check, no engine init).</summary>
    bool IsConfigured { get; }

    /// <summary>True while the mic is physically open for a wake wait. Light/drop a privacy pill on change.</summary>
    bool IsListening { get; }

    /// <summary>Raised on the UI thread when <see cref="IsListening"/> flips.</summary>
    event EventHandler<bool>? ListeningChanged;

    /// <summary>
    /// Open the mic and block until the wake keyword fires or <paramref name="ct"/> cancels.
    /// Returns false if unavailable or cancelled. Re-entrant calls are rejected.
    /// </summary>
    Task<bool> WaitForWakeAsync(CancellationToken ct);

    /// <summary>
    /// Tune the wake threshold to THIS user's voice + mic. Records <paramref name="target"/> spoken
    /// wake utterances (endpointed on silence) plus the room tone between them, then sweeps the trigger
    /// threshold to find the strictest value that still catches the user reliably without the ambient
    /// firing, and stores it. Uses the wake loop's own capture device. The caller MUST stop the wake
    /// loop first (the recognizer is single-session); re-arm after. Audio stays in memory, never
    /// written to disk. Returns a not-available result when no model/mic is present.
    /// </summary>
    Task<WakeCalibrationResult> CalibrateAsync(int target = 5, IProgress<WakeCalibrationProgress>? progress = null, CancellationToken ct = default);
}

/// <summary>Live progress of a <see cref="ISpeechWakeService.CalibrateAsync"/> run.</summary>
public sealed class WakeCalibrationProgress
{
    /// <summary>"listen" while collecting says, "analyze" during the threshold sweep.</summary>
    public string Phase = "";
    /// <summary>Utterances captured so far.</summary>
    public int Captured;
    /// <summary>How many we want.</summary>
    public int Target;
    /// <summary>Current input RMS (0..1) for a meter.</summary>
    public double Level;
}

/// <summary>Outcome of a <see cref="ISpeechWakeService.CalibrateAsync"/> run.</summary>
public sealed class WakeCalibrationResult
{
    /// <summary>True when a threshold was chosen + persisted.</summary>
    public bool Success;
    /// <summary>User-facing outcome message.</summary>
    public string Message = "";
    /// <summary>The chosen <c>SpeechWakeThreshold</c>.</summary>
    public double ChosenThreshold;
    /// <summary>How many utterances were captured.</summary>
    public int Utterances;
    /// <summary>How many of those the chosen threshold caught.</summary>
    public int CaughtAtChosen;
}
