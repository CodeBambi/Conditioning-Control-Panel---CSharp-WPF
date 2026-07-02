namespace ConditioningControlPanel.Core.Services.Webcam;

/// <summary>
/// Marker for click-driven gaze drift correction (continuous implicit recalibration). Implemented by
/// platform heads that own a real webcam tracker + a global mouse hook. The shared shell resolves this
/// from DI at startup so the Windows head's implementation is constructed (and self-starts) without the
/// shared code referencing head-specific types. Null on platforms without a webcam tracker.
/// </summary>
public interface IGazeDriftCorrectionService { }
