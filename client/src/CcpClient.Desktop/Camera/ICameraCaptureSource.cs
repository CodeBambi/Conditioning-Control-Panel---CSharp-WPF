using CcpClient.Desktop.Capabilities;

namespace CcpClient.Desktop.Camera;

/// <summary>
/// <b>The verb that opens a camera.</b> Slice 1 deliberately had none —
/// <see cref="ICameraDeviceSource"/> can only ask the operating system which cameras EXIST — and
/// this is the seam that adds one, with the gate that decides whether it may be called kept where it
/// already was (<see cref="CameraParticipant.StartCaptureAsync"/>).
///
/// <para><b>One object, one handle, exactly as upstream.</b> Upstream's service owns <i>"the only
/// VideoCapture handle in the application"</i>
/// (<c>Services/Webcam/WebcamTrackingService.cs:86-90</c>); here the source IS the handle's owner
/// rather than a factory for session objects, which is why <see cref="Open"/>,
/// <see cref="ReadFrame"/> and <see cref="IDisposable.Dispose"/> live on one type. There is no
/// second lifetime to get wrong and no way to hold a device after the owner is disposed.</para>
///
/// <para><b>NOTHING ON THIS INTERFACE HANDS BACK A PIXEL.</b> <see cref="ReadFrame"/> returns a
/// <see cref="bool"/>. That is not an oversight and it is not temporary: this build has no gaze
/// engine, so there is no consumer for a frame, and a seam that returned one would be an escape
/// route for exactly the data <c>client/docs/capability-inventory.md</c> requires to stay
/// memory-only. Frames are decoded, measured by <see cref="CameraFrameProbe"/> and dropped inside
/// the implementation, where the buffers are method locals that cannot outlive the call. The engine
/// slice will have to add its consumer deliberately, in the open, against this paragraph.</para>
///
/// <para><b>AUDIO IS STILL NEVER OPENED</b> and it is still a property of the type: there is no
/// audio member here, no audio device class named anywhere in this namespace, and the Windows
/// implementation asks Media Foundation for the VIDEO capture device category alone. Upstream states
/// the same rule over a video-only API — <i>"Open audio capture (VideoCapture is video-only by API
/// contract)"</i> (<c>Services/Webcam/WebcamTrackingService.cs:30</c>, the file-header privacy contract at <c>:22-39</c>).</para>
/// </summary>
public interface ICameraCaptureSource : IDisposable
{
    /// <summary>The capture backend this source speaks for, for the detail strings a user reads.
    /// Never a claim that anything opened.</summary>
    string Backend { get; }

    /// <summary>Whether a device is open RIGHT NOW. Answered from the handle this object holds, so
    /// it cannot be stale in the direction that matters: it is false the instant
    /// <see cref="Close"/> or <see cref="IDisposable.Dispose"/> has run.</summary>
    bool IsOpen { get; }

    /// <summary>The negotiated frame width in pixels, or 0 when nothing is open.</summary>
    int Width { get; }

    /// <summary>The negotiated frame height in pixels, or 0 when nothing is open.</summary>
    int Height { get; }

    /// <summary>
    /// The ladder rung that was ADOPTED, or null when nothing is open. Named — never an index —
    /// because a user told "camera opened" learns nothing, and a support log that says which rung
    /// won is the difference between diagnosing a format-negotiation bug and guessing at one.
    /// </summary>
    string? AdoptedRung { get; }

    /// <summary>
    /// What the last <see cref="Open"/> did, in order: the rungs it climbed and the outcome of each,
    /// or the reason it never reached a rung at all. Strings only — no device identity, no frame
    /// data, no pixel statistics.
    ///
    /// <para><b>EMPTY MEANS OPEN HAS NEVER BEEN CALLED ON THIS SOURCE, and that is load-bearing.</b>
    /// Every implementation must write at least one line for every call, including calls that fail
    /// before any format is tried, so that "this launch never asked a camera to open" is checkable
    /// from the SEAM rather than from a counter the caller maintains. A counter on the caller is
    /// exactly what a mutation slipped past: code that reaches this interface directly leaves a
    /// participant's own tally at zero, and the operating system has still been asked for the user's
    /// device list by then.</para>
    /// </summary>
    IReadOnlyList<string> AttemptedRungs { get; }

    /// <summary>How many frames have been read since the last successful <see cref="Open"/>. A
    /// COUNT, which is the only thing about a frame this product may write down.</summary>
    int FramesRead { get; }

    /// <summary>
    /// Open the named device and warm it up until it delivers a frame that clears
    /// <see cref="CameraFrameProbe.Accepts"/>. Returns null when a camera is open, and a typed
    /// refusal otherwise.
    ///
    /// <para><b>A frame arriving is not success</b>, and that is upstream's hardest-won lesson:
    /// an Elgato Facecam Neo delivered 1240 non-empty frames containing zero faces
    /// (<c>Services/Webcam/WebcamTrackingService.cs:123-130</c>). An implementation that returns
    /// null here without having accepted a probe frame is lying to every caller.</para>
    ///
    /// <para>Synchronous, and the caller runs it off the UI thread —
    /// <c>client/docs/capability-inventory.md</c> requires camera acquisition to be cancellable and
    /// off the UI thread, and the token is observed between rungs and between probe reads so a user
    /// who changes their mind during a multi-second warm-up is obeyed.</para>
    ///
    /// <para>Never throws for a device or driver failure: that is a typed
    /// <see cref="CapabilityState.Faulted"/>. <see cref="OperationCanceledException"/> is the one
    /// exception that propagates, because a cancelled open is not a fact about the camera.</para>
    /// </summary>
    CapabilityState? Open(CameraDevice device, CancellationToken cancellationToken);

    /// <summary>
    /// Read one frame from the open device and DROP IT. Returns whether pixels arrived.
    ///
    /// <para>False for a read that timed out or returned nothing, which upstream tolerates in a run
    /// of up to <c>MaxConsecutiveReadFails</c> before treating the device as lost
    /// (<c>Services/Webcam/WebcamTrackingService.cs:120</c>, ~1s at 30fps). Deciding when a run of
    /// failures means the camera is gone belongs to the caller that knows what the frames were for.</para>
    /// </summary>
    bool ReadFrame(CancellationToken cancellationToken);

    /// <summary>
    /// Release the device. <b>Idempotent, and it must really let go</b> — a camera the user believes
    /// they turned off, whose indicator is still lit and whose handle another application cannot
    /// take, is the worst failure this capability has available to it.
    /// </summary>
    void Close();
}

/// <summary>
/// <b>The order a camera's formats are tried in, and the reason there is more than one.</b>
///
/// <para>Upstream's ladder is four rungs — <c>(DSHOW, null)</c>, <c>(DSHOW, "MJPG")</c>,
/// <c>(MSMF, null)</c>, <c>(MSMF, "MJPG")</c> — and it is two independent ideas crossed together
/// (<c>Services/Webcam/WebcamTrackingService.cs:167-172</c>): which BACKEND, and which PIXEL FORMAT.
/// The port has one backend per platform, so only the format axis survives, and these are its two
/// values.</para>
///
/// <para><b>The rung names are platform-neutral on purpose.</b> The Windows route means them as
/// "whatever Media Foundation negotiates" and "an MJPG native type"; a future V4L2 capture path
/// would mean the same two things with <c>VIDIOC_ENUM_FMT</c>. The escalation is a property of
/// CAMERAS — one UVC webcam's default-format feed contains no detectable face
/// (<c>:163-166</c>, BUG-F2XJE2E7X9) — not a property of Windows, so a Linux route inherits the
/// problem and should inherit the ladder rather than invent a second vocabulary for it.</para>
/// </summary>
public static class CameraCaptureLadder
{
    /// <summary>Rung one: leave the driver's own format negotiation alone. It runs FIRST for
    /// upstream's stated reason — <i>"so cameras that already work are untouched"</i>
    /// (<c>Services/Webcam/WebcamTrackingService.cs:162-163</c>).</summary>
    public const string DefaultFormat = "default-format";

    /// <summary>Rung two: ask the camera for MJPG. Escalated to ONLY after the default feed has been
    /// rejected by <see cref="CameraFrameProbe"/>, <i>"without risking YUY2-only cameras that don't
    /// support MJPG"</i> (<c>Services/Webcam/WebcamTrackingService.cs:163-166</c>).</summary>
    public const string MotionJpeg = "motion-jpeg";

    /// <summary>The ladder, in the order it is climbed. Order is behaviour, not preference: reverse
    /// it and every working camera is renegotiated into MJPG for no reason.</summary>
    public static IReadOnlyList<string> Order { get; } = [DefaultFormat, MotionJpeg];
}

/// <summary>
/// The capture source for a platform or a build that cannot open a camera. It opens nothing and says
/// what is missing, rather than reporting a failure it never attempted — the
/// <see cref="UnsupportedCameraDeviceSource"/> shape, for the same reason.
/// </summary>
public sealed class UnsupportedCameraCaptureSource(string backend, string detail, string dependency)
    : ICameraCaptureSource
{
    private readonly List<string> _attempted = [];

    /// <inheritdoc/>
    public string Backend { get; } = backend;

    /// <inheritdoc/>
    public bool IsOpen => false;

    /// <inheritdoc/>
    public int Width => 0;

    /// <inheritdoc/>
    public int Height => 0;

    /// <inheritdoc/>
    public string? AdoptedRung => null;

    /// <inheritdoc/>
    public IReadOnlyList<string> AttemptedRungs => _attempted;

    /// <inheritdoc/>
    public int FramesRead => 0;

    /// <inheritdoc/>
    public CapabilityState? Open(CameraDevice device, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Recorded even though no rung was climbed, because an EMPTY AttemptedRungs has to mean
        // "nobody asked" on every implementation or it means nothing on any of them.
        _attempted.Clear();
        _attempted.Add("no rung: this build has no capture route here, so nothing was attempted");
        return new CapabilityState.DependencyMissing(
            dependency, new CapabilityReason(CameraReasonCodes.CameraCaptureUnsupported, detail));
    }

    /// <inheritdoc/>
    public bool ReadFrame(CancellationToken cancellationToken) => false;

    /// <inheritdoc/>
    public void Close()
    {
        // Nothing was ever opened, so there is nothing to release. Stated rather than left blank:
        // a Close() that quietly does nothing is only correct because Open() cannot succeed.
    }

    /// <inheritdoc/>
    public void Dispose() => Close();
}

/// <summary>
/// The one place that decides whether an enumerated camera and a capture backend's device are the
/// SAME PHYSICAL CAMERA, and it does it without an index.
///
/// <para><b>Why this exists.</b> Slice 1's roster comes from DirectShow, whose identity is a moniker
/// display name; the Windows capture path is Media Foundation, whose identity is a symbolic link.
/// Both are the same device-interface path wearing a different interface GUID — DirectShow's
/// <c>@device:pnp:\\?\usb#vid_…#{65e8773d-…}\global</c> and Media Foundation's
/// <c>\\?\usb#vid_…#{e5323777-…}\global</c> — so the durable part is the hardware instance BETWEEN
/// the prefix and the trailing interface GUID.</para>
///
/// <para><b>The alternative is the bug upstream warns about twice, in its own words.</b> Matching by
/// position means trusting that DirectShow's enumeration order equals the order the capture backend
/// opens: <i>"usually identical on a typical system, but is not guaranteed"</i>
/// (<c>Services/Webcam/WebcamDeviceEnumerator.cs:15-21</c>,
/// <c>Services/Webcam/WebcamWinRtEnumerator.cs:19-22</c>), and upstream carries a runtime warning
/// for when the remembered index resolves to a different camera than the remembered name
/// (<c>Services/Webcam/WebcamTrackingService.cs:1243-1249</c>). This port has no index anywhere:
/// <c>client/docs/capability-inventory.md</c> says <i>"Never use only a transient camera index"</i>,
/// and the way to obey that is not to have one.</para>
/// </summary>
public static class CameraHardwareKey
{
    private const string PnpMonikerPrefix = "@device:pnp:";
    private const string DevicePathPrefix = @"\\?\";

    /// <summary>
    /// The hardware instance shared by every interface a device exposes, lower-cased, or null when
    /// the string carries none.
    ///
    /// <para>Null is a REFUSAL TO GUESS. A software moniker with no device path, an empty string or
    /// a name that is only an interface GUID produce null, and <see cref="Matches"/> then never
    /// matches — because a wrong match here opens somebody's second camera, and there is no
    /// user-visible difference between that and this product choosing a camera at random.</para>
    /// </summary>
    public static string? Of(string? identity)
    {
        if (string.IsNullOrWhiteSpace(identity))
        {
            return null;
        }

        var value = identity.Trim();
        if (value.StartsWith(PnpMonikerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            value = value[PnpMonikerPrefix.Length..];
        }

        if (value.StartsWith(DevicePathPrefix, StringComparison.Ordinal))
        {
            value = value[DevicePathPrefix.Length..];
        }

        // Cut the trailing interface GUID — the ONLY part that differs between the DirectShow and
        // Media Foundation views of one camera. `>= 0` and not `> 0`: a string whose device path IS
        // nothing but an interface GUID has no hardware in it at all, and cutting at index 0 leaves
        // the empty string that becomes the null below. Written `> 0` first, which kept the raw GUID
        // as a "key" and would have matched two unrelated devices that expose the same interface.
        var guid = value.LastIndexOf("#{", StringComparison.Ordinal);
        if (guid >= 0)
        {
            value = value[..guid];
        }

        value = value.Trim();
        return value.Length == 0 ? null : value.ToLowerInvariant();
    }

    /// <summary>Whether two identities name the same physical device. False whenever either side has
    /// no key at all, which is the safe direction: no camera is opened rather than the wrong one.</summary>
    public static bool Matches(string? left, string? right)
    {
        var key = Of(left);
        return key is not null && key == Of(right);
    }
}
