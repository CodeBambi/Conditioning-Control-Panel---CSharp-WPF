using CcpClient.Desktop.Capabilities;

namespace CcpClient.Desktop.Camera;

/// <summary>
/// One video-capture device, as the operating system NAMED it. Metadata only.
///
/// <para><b>There is no handle here, and that is the point.</b> Nothing in this record can be
/// opened, read from, or turned into pixels: enumerating a camera and using one are different
/// operations, and this slice does only the first. Nothing in this whole namespace carries a frame,
/// a buffer, a crop, a tensor, a landmark or a gaze sample, which is how
/// <c>client/docs/capability-inventory.md</c>'s "Webcam, face, and gaze tracking" memory-only rule
/// is kept STRUCTURALLY rather than by discipline — there is no type here that could be written to
/// disk, put in a log line, or attached to a crash report even by mistake.</para>
/// </summary>
/// <param name="StableId">
/// The most durable identity the enumeration route offered. On Windows this is the DirectShow
/// moniker display name, which carries the device interface path (vendor, product and instance);
/// on Linux it is the <c>/dev/videoN</c> node path, which is NOT durable — see
/// <paramref name="IdentityIsStable"/>.
/// </param>
/// <param name="DisplayName">
/// What the OS calls it, for a human choosing between two cameras. Upstream reads DirectShow's
/// <c>FriendlyName</c> for exactly this reason — <i>"useful for letting users disambiguate physical
/// webcams from virtual cameras (OBS, Snap, etc.)"</i>
/// (<c>Services/Webcam/WebcamTrackingService.cs:1111-1117</c>).
/// </param>
/// <param name="IdentityIsStable">
/// Whether <paramref name="StableId"/> may be PERSISTED as this camera's identity.
///
/// <para><b>It exists because the capability contract bans exactly one thing by name</b>
/// (<c>client/docs/capability-inventory.md</c>, "Webcam, face, and gaze tracking": <i>"Never use
/// only a transient camera index"</i>), and because upstream carries the same warning in its own
/// words twice — DirectShow index order versus the MSMF index OpenCV actually opens
/// (<c>Services/Webcam/WebcamDeviceEnumerator.cs:15-21</c>,
/// <c>Services/Webcam/WebcamWinRtEnumerator.cs:19-22</c>: <i>"usually identical on a typical system,
/// but is not guaranteed"</i>). A flag that says "this identity is a bus path the kernel may
/// reassign" is what stops the capture slice writing one into a settings file. This slice persists
/// no camera identity at all.</para>
/// </param>
public readonly record struct CameraDevice(string StableId, string DisplayName, bool IdentityIsStable);

/// <summary>
/// What ONE enumeration ask learned. A snapshot of that ask and nothing cached — the rule
/// <c>Haptics/IHapticSink.cs</c>'s observation follows, for the same reason: a member answered from
/// a field this process remembered writing is not an answer about the machine.
/// </summary>
/// <param name="Route">
/// The enumeration route that produced this, named so the System page's line can say HOW the
/// answer was obtained. Never a claim that anything was opened.
/// </param>
/// <param name="Devices">The devices the route named, in the route's own order.</param>
/// <param name="Refusal">
/// The typed refusal that stopped this ask, or null when the route really ran. It is a full
/// <see cref="CapabilityState"/> rather than a bare <see cref="CapabilityReason"/> because only the
/// route knows which KIND of refusal it hit — a denied OS permission is
/// <see cref="CapabilityState.PermissionRequired"/>, an absent enumeration route is
/// <see cref="CapabilityState.DependencyMissing"/>, and a route that threw is
/// <see cref="CapabilityState.Faulted"/>. Deciding that at the classification site would mean
/// re-deriving it from a string code, which is the shape that eventually mistypes one.
/// </param>
public sealed record CameraInventory(
    string Route, IReadOnlyList<CameraDevice> Devices, CapabilityState? Refusal)
{
    /// <summary>A route that really ran and named these devices (possibly none).</summary>
    public static CameraInventory Named(string route, IReadOnlyList<CameraDevice> devices) =>
        new(route, devices, Refusal: null);

    /// <summary>A route that refused before it could name anything. Never carries devices.</summary>
    public static CameraInventory Refusing(string route, CapabilityState refusal) =>
        new(route, [], refusal);
}

/// <summary>
/// This process's ability to ask the operating system WHICH cameras exist — and nothing else.
///
/// <para><b>THIS seam has one verb and it still cannot open anything.</b> There is no <c>Open</c>,
/// no <c>Start</c>, no <c>ReadFrame</c> and no stream on it, and the capture slice did NOT add one
/// here — it added a separate <see cref="ICameraCaptureSource"/>, deliberately, so that the type
/// every launch-time probe touches stays one through which no camera can be opened at all. That is
/// what keeps the consent contract enforceable —
/// <c>client/docs/capability-inventory.md</c> requires that opening the dashboard, restoring
/// settings, or finding a calibration NEVER start the camera, and the strongest form of that
/// guarantee is that the code those paths reach has no way to.</para>
///
/// <para><b>AUDIO IS NEVER OPENED, and here that is a property of the type rather than a promise.</b>
/// Upstream states the rule as a comment over a video-only API — <i>"Open audio capture
/// (VideoCapture is video-only by API contract)"</i>
/// (<c>Services/Webcam/WebcamTrackingService.cs:30</c>, the file-header privacy contract at <c>:22-39</c>) — and enumerates the VIDEO device
/// category only (<c>Services/Webcam/WebcamDeviceEnumerator.cs:28</c>,
/// <c>CLSID_VideoInputDeviceCategory</c>; <c>Services/Webcam/WebcamWinRtEnumerator.cs:36</c>,
/// <c>DeviceClass.VideoCapture</c>). This interface has no audio member to call, no audio device
/// class to pass, and no implementation that names one.</para>
///
/// <para><b>Enumeration does not touch a device.</b> Both implementations read metadata the OS
/// already holds — a COM device-moniker list on Windows, the kernel's device-class directory on
/// Linux. Neither opens a device node, neither lights a camera indicator, and neither is affected
/// by whether the user has consented, which is why the consent gate lives ABOVE this seam
/// (<see cref="CameraParticipant.ProbeAsync"/>) and refuses before it is ever called.</para>
/// </summary>
public interface ICameraDeviceSource
{
    /// <summary>The route this source speaks for, for the detail strings a user reads.</summary>
    string Route { get; }

    /// <summary>
    /// Ask the operating system what it has, right now. Synchronous because both real routes are
    /// (upstream's DirectShow walk is synchronous, and upstream's WinRT fallback BLOCKS on the async
    /// one with a five-second timeout — <c>Services/Webcam/WebcamWinRtEnumerator.cs:35-42</c>);
    /// the caller runs it off the UI thread, which is where
    /// <c>client/docs/capability-inventory.md</c> requires camera acquisition to happen.
    ///
    /// <para>Never throws: an enumeration route that failed is a typed
    /// <see cref="CapabilityState.Faulted"/> in the returned inventory. Upstream traps the same way
    /// and returns an empty list (<c>Services/Webcam/WebcamDeviceEnumerator.cs:119-122</c>) — the
    /// divergence is deliberate, because "the route threw" and "there is no camera" are the two
    /// facts this capability exists to keep apart.</para>
    /// </summary>
    CameraInventory Enumerate();
}

/// <summary>
/// The source for a platform this build cannot enumerate on. It looks at nothing and says so,
/// naming the platform rather than reporting an absence it never checked for — the
/// <c>Pointer/UnsupportedPointerSurface.cs</c> and <c>Audio/UnsupportedAudioPresence.cs</c> shape.
/// </summary>
public sealed class UnsupportedCameraDeviceSource(string route, string detail) : ICameraDeviceSource
{
    /// <inheritdoc/>
    public string Route { get; } = route;

    /// <inheritdoc/>
    public CameraInventory Enumerate() => CameraInventory.Refusing(
        Route,
        new CapabilityState.DependencyMissing(
            "a camera-enumeration route for this platform",
            new CapabilityReason(CameraReasonCodes.CameraEnumerationUnsupported, detail)));
}
