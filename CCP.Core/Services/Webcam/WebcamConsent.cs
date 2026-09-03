using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Webcam
{
    /// <summary>
    /// The webcam privacy CONTRACT, as opposed to the tracker that honours it. The tracker owns a
    /// camera and stays in a head; the version string is what every head's consent dialog stamps
    /// and what every head's "is consent still current?" check compares against, so it lives here
    /// and there is exactly one of it.
    /// </summary>
    public static class WebcamConsent
    {
        /// <summary>
        /// Bumped any time we add a new sensor type, broaden what the camera observes, or change
        /// what numbers are stored. On bump, the consent dialog re-runs from screen 1 for every
        /// existing user, because a stored version that no longer matches reads as "not granted".
        /// </summary>
        public const string ConsentVersion = "1.0";

        /// <summary>
        /// True when consent was granted AND the recorded version matches the contract above.
        /// A version mismatch is treated as "not granted" so callers re-prompt — that is the whole
        /// point of the version field, and the only mechanism that makes bumping
        /// <see cref="ConsentVersion"/> actually re-consent existing users. Null settings read as
        /// "not granted".
        /// </summary>
        public static bool IsCurrent(AppSettings? settings) =>
            settings != null && settings.WebcamConsentGiven && settings.WebcamConsentVersion == ConsentVersion;
    }
}
