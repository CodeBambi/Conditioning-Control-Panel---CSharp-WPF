namespace CcpClient.Desktop.Camera;

/// <summary>
/// Stable machine-readable reason codes for the camera capability
/// (<c>runtime-capability-contract.md</c> §1: codes are additive and land with their consumer).
/// They live beside their consumer for the same reason <c>Haptics/HapticReasonCodes.cs</c>,
/// <c>Audio/AudioReasonCodes.cs</c> and <c>Pointer/PointerReasonCodes.cs</c> do.
///
/// <para><b>Why a camera needs its own vocabulary, and why the rungs must never collapse.</b> Four
/// completely different things stop this product looking through a webcam, and each has a different
/// repair: this build has no gaze ENGINE (wait for a release), the user has not CONSENTED (open the
/// consent flow), the operating system DENIES camera access to desktop apps (a Windows privacy
/// setting, or a Linux permission), and there is NO CAMERA (plug one in). Collapsing any pair sends
/// somebody to fix a thing that is not wrong.</para>
///
/// <para><b>The one that must never be improvised is <see cref="CameraNoDevice"/>.</b> "No device
/// found" is indistinguishable from a real absence, so it may be said ONLY when a real enumeration
/// route ran and named nothing. A platform this build cannot enumerate on says
/// <see cref="CameraEnumerationUnsupported"/> and names what is missing —
/// <c>runtime-capability-contract.md</c> §2 rule 2, and the exact failure the port's capability
/// contract exists to prevent.</para>
/// </summary>
public static class CameraReasonCodes
{
    /// <summary>
    /// <b>No gaze engine stands behind this capability in this build, so nothing was asked of any
    /// camera and no consent was consulted.</b>
    ///
    /// <para>It is a property of the BUILD — not of the machine, not of the platform, not of the
    /// user's hardware and not of their consent — and it is the rung this build always stops on
    /// (<see cref="CameraCapability.AdmittedEngines"/> is empty). It is never
    /// <see cref="CameraNoDevice"/> and never <see cref="CameraConsentAbsent"/>: telling a user to
    /// plug a webcam in, or to work through a consent flow, for a feature that has no engine behind
    /// it wastes their time and their privacy decision on nothing.</para>
    /// </summary>
    public const string CameraNoEngine = "camera-no-engine";

    /// <summary>
    /// <b>No consent has been given</b>, so nothing was asked of any camera. Upstream refuses in
    /// exactly this place and logs which field failed
    /// (<c>Services/Webcam/WebcamTrackingService.cs:812-815</c>), and its consent flow persists the
    /// grant WITHOUT starting the camera (<c>Dialogs/WebcamConsentDialog.xaml.cs:140-151</c>).
    /// </summary>
    public const string CameraConsentAbsent = "camera-consent-absent";

    /// <summary>
    /// <b>Consent was given against an OLDER privacy contract</b> and has not been renewed.
    ///
    /// <para>Upstream folds this into "not granted" — one predicate over both fields
    /// (<c>Services/Webcam/WebcamTrackingService.cs:108-114</c>) — but it LOGS the two apart
    /// (<c>:814-815</c>) because they are not the same event. Here they are separate codes, because
    /// the repairs differ: a user who has never consented is being asked for the first time, and a
    /// user whose stored version no longer matches is being asked again BECAUSE THE PROMISE
    /// CHANGED. The outcome is identical — both refuse — which is the whole point of the version
    /// field (<c>:100-107</c>).</para>
    /// </summary>
    public const string CameraConsentStale = "camera-consent-stale";

    /// <summary>
    /// <b>The operating system denies camera access to this process</b>, decided WITHOUT touching a
    /// camera. On Windows that is the Camera privacy setting, read out of the CapabilityAccessManager
    /// consent store; on Linux it is the device class being unreadable to this user. Upstream has the
    /// same state (<c>Services/Webcam/WebcamTrackingService.cs:82</c>,
    /// <c>WebcamTrackingState.CameraDenied</c>) but can only reach it by trying to OPEN the device.
    /// </summary>
    public const string CameraPermissionDenied = "camera-permission-denied";

    /// <summary>
    /// <b>A real enumeration route ran and named no video-capture device.</b> The only code that may
    /// say "no camera", and it may be said only after a route really looked. A
    /// <see cref="Capabilities.CapabilityState.DependencyMissing"/> cause rather than an
    /// <see cref="Capabilities.CapabilityState.Unavailable"/> one, because the missing thing is a
    /// named external dependency the user can go and plug in.
    /// </summary>
    public const string CameraNoDevice = "camera-no-device";

    /// <summary>
    /// <b>This build has no enumeration route on this platform, so nothing looked and nothing may be
    /// said about a device.</b> The detail NAMES what is missing — the sandbox portal, the kernel
    /// device class, or the platform itself — because a silent "no device" here is indistinguishable
    /// from a real absence.
    /// </summary>
    public const string CameraEnumerationUnsupported = "camera-enumeration-unsupported";

    /// <summary>The enumeration route itself failed. Carries the exception class only — never a
    /// message, which on this path can carry a device path or a user profile directory.</summary>
    public const string CameraEnumerationFailed = "camera-enumeration-failed";

    /// <summary>
    /// <b>Every rung below the capture itself passed, and NO CAMERA WAS OPENED.</b> The ceiling of
    /// the CAPABILITY PROBE, said out loud rather than rounded up: a device roster and a current
    /// consent are not a frame, an engine, or a gaze sample, so the capability is
    /// <see cref="Capabilities.CapabilityState.Degraded"/> and never
    /// <see cref="Capabilities.CapabilityState.Available"/>
    /// (<see cref="CameraCapability.Classify"/> cannot return Available for any input at all).
    ///
    /// <para>Still true after the capture slice, and it has to be: the probe that produces this code
    /// runs at launch and never opens anything (<see cref="CameraParticipant.ProbeAsync"/>). Opening
    /// a camera is a separate, explicitly-started operation
    /// (<see cref="CameraParticipant.StartCaptureAsync"/>) and reports
    /// <see cref="CameraOpen"/> instead.</para>
    /// </summary>
    public const string CameraNotOpened = "camera-not-opened";

    /// <summary>
    /// <b>A camera IS open and delivering frames.</b> Reported by an explicit user start and by
    /// nothing else.
    ///
    /// <para>It is carried on a <see cref="Capabilities.CapabilityState.Degraded"/> rather than an
    /// <see cref="Capabilities.CapabilityState.Available"/>, and that is not modesty. The feature a
    /// user asked for is GAZE, and this build has no gaze engine
    /// (<see cref="CameraCapability.AdmittedEngines"/> is empty): an open camera with nothing looking
    /// through it delivers none of it. Saying Available here would be the fake-available shape the
    /// truthful-capability contract exists to prevent, one rung higher up than
    /// <see cref="CameraNotOpened"/> sits.</para>
    /// </summary>
    public const string CameraOpen = "camera-open";

    /// <summary>
    /// <b>This build has no capture route on this platform</b>, so nothing was opened and nothing may
    /// be said about whether a camera would have worked. Names what is missing — the V4L2 streaming
    /// path, the sandbox portal, or the platform itself — for the reason
    /// <see cref="CameraEnumerationUnsupported"/> does: a silent failure here is indistinguishable
    /// from a broken camera.
    /// </summary>
    public const string CameraCaptureUnsupported = "camera-capture-unsupported";

    /// <summary>
    /// <b>The device would not open on any format.</b> Upstream reaches the same conclusion when
    /// <c>VideoCapture.Open</c> returns false on every backend and reports
    /// <c>WebcamTrackingState.CameraDenied</c>
    /// (<c>Services/Webcam/WebcamTrackingService.cs:1315-1321</c>). It is never
    /// <see cref="CameraNoDevice"/>: a device that was enumerated and then refused to open is a
    /// different problem from no device at all, with a different repair.
    /// </summary>
    public const string CameraOpenFailed = "camera-open-failed";

    /// <summary>
    /// <b>The device opened and never delivered a frame anything could be seen in.</b> Upstream's
    /// <c>CameraInUse</c> (<c>Services/Webcam/WebcamTrackingService.cs:1305-1314</c>): most often
    /// another application or antivirus webcam shielding holds the feed, and sometimes it is the
    /// genuinely degenerate feed <see cref="CameraFrameProbe"/> exists to reject.
    ///
    /// <para>Kept apart from <see cref="CameraOpenFailed"/> because upstream keeps them apart, using
    /// an <c>anyOpened</c> flag for the purpose (<c>:1273-1321</c>) — the repairs are "close the
    /// other app" and "check the device", and neither helps with the other.</para>
    /// </summary>
    public const string CameraNoUsableFrame = "camera-no-usable-frame";

    /// <summary>
    /// <b>The enumerated camera has no counterpart in the capture backend's device list.</b> Almost
    /// always a camera unplugged between the roster and the start. It is NOT
    /// <see cref="CameraNoDevice"/> — a device WAS named — and this product refuses rather than
    /// opening whatever else it found, because "the wrong camera" and "a camera chosen at random"
    /// look identical to the person in front of it (<see cref="CameraHardwareKey"/>).
    /// </summary>
    public const string CameraDeviceNotMatched = "camera-device-not-matched";

    /// <summary>The capture route itself failed. Carries the exception class only — never a message,
    /// which on this path can carry a device symbolic link.</summary>
    public const string CameraCaptureFailed = "camera-capture-failed";
}
