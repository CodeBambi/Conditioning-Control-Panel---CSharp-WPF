namespace ConditioningControlPanel.Core.Services.Webcam;

/// <summary>
/// Single source of truth for the webcam tracking privacy/consent contract
/// version. Bumped any time a new sensor type is added, the camera's scope is
/// broadened, or the persisted numbers change shape. On bump, existing users
/// are treated as "not consented" and re-prompted through the multi-gate
/// consent flow — that is the only mechanism that makes a version bump actually
/// re-consent people when the privacy contract changes.
/// </summary>
/// <remarks>
/// This constant is referenced by every consent-aware surface (the
/// <c>AvaloniaWebcamTrackingService</c> header contract, the
/// <c>WebcamConsentDialog</c>, and the Blink Trainer / Lab view-models) so the
/// whole app agrees on what "current" means. The legacy WPF head keeps its own
/// copy and is intentionally not coupled here.
/// </remarks>
public static class WebcamConsent
{
    /// <summary>Current consent contract version.</summary>
    public const string ConsentVersion = "1.0";
}
