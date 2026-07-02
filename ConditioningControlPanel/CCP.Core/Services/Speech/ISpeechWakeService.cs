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
}
