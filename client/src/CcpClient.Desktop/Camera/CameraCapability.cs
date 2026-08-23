using CcpClient.Desktop.Capabilities;

namespace CcpClient.Desktop.Camera;

/// <summary>
/// The camera capability's NAME, its engine admission, and the one ladder that turns everything
/// known at a moment into exactly one typed state.
///
/// <para><b>This slice is the refusal, deliberately, and the row says why it comes first:</b> a
/// capability that refuses honestly with no engine, no consent, denied permission or no device is
/// what makes every later slice's refusal truthful. No frame is decoded here, no model is loaded,
/// no engine is chosen and no package is added — and the seam is built so that none of those can be
/// smuggled in later without deleting something that argues against it.</para>
/// </summary>
public static class CameraCapability
{
    /// <summary>The registered capability name. A constant so the System page's row and the
    /// registration cannot be spelled two different ways — <c>Lifecycle/CompositionRoot</c>'s
    /// <c>HapticCapabilityName</c> rule.</summary>
    public const string CapabilityName = "webcam-gaze";

    /// <summary>
    /// The gaze engines this build can run. <b>EMPTY, and that is the whole state of this build.</b>
    ///
    /// <para>It is the analogue of <c>Haptics/HapticSinkFactory.AdmittedRoutes</c>, and like that
    /// list it is not a choice a user makes: it says which engines have an implementation compiled
    /// in. <c>client/docs/capability-inventory.md</c> ADMITS one — the local BlazeFace / FaceMesh /
    /// Iris pipeline, named as the default — and the third-party deep-learning engine is closed
    /// UNAVAILABLE on training-data rights (<c>client/docs/gaze-engine-admission-spike.md</c>).
    /// Admitted is not implemented: the local engine's port is a later slice, and until its code
    /// exists this list stays empty rather than naming an engine nothing can run. A name in this
    /// list with no implementation behind it would be the fake-available shape the truthful-capability
    /// contract bans.</para>
    /// </summary>
    public static IReadOnlyList<string> AdmittedEngines { get; } = [];

    /// <summary>
    /// The refusal for a build with no gaze engine, worded so that nobody can read it as a missing
    /// camera or a withheld consent.
    /// </summary>
    public const string EngineGap =
        "no gaze engine is implemented in this build, so NOTHING WAS ASKED OF ANY CAMERA and no consent was "
        + "consulted. THIS IS NOT \"no camera found\" and NOT \"consent needed\": there is nothing here that could "
        + "look through a camera, so a device roster and a privacy decision would both be spent on nothing. The "
        + "admitted engine is the local BlazeFace/FaceMesh/Iris pipeline the shipping Windows product runs "
        + "(Services/Webcam/WebcamTrackingService.cs), it runs entirely on this machine, and porting it is a later "
        + "slice. The third-party deep-learning alternative is closed UNAVAILABLE on commercial training-data "
        + "rights and is not waiting on this row";

    /// <summary>
    /// Everything known at one moment, resolved to exactly one typed state.
    ///
    /// <para><b>ARM ORDER IS THE DESIGN.</b> Each rung is asked before anything it would make
    /// meaningless, so no combination of the lower facts can produce an answer about something this
    /// build never reached:</para>
    ///
    /// <list type="number">
    /// <item><b>The ENGINE first</b>, for the reason <c>Haptics/HapticServerObservation.Classify</c>
    /// asks its admission first: a user told to plug a camera in, or to work through a consent flow,
    /// for a feature that has no engine has been sent to fix something that is not wrong. In this
    /// build this arm always wins.</item>
    /// <item><b>CONSENT second, and BEFORE any device fact</b>, because that ordering is the consent
    /// contract itself. <c>client/docs/capability-inventory.md</c> requires the camera to start only
    /// after current explicit consent, and the strongest form of that is refusing before the
    /// question is even put to the operating system — which is why
    /// <see cref="CameraParticipant.ProbeAsync"/> passes a null inventory here rather than an empty
    /// one: it did not look.</item>
    /// <item><b>"Nobody has asked yet" third</b>, which is <c>not-probed</c> — an existing arm
    /// rather than a state invented for this branch. Never a device answer: nothing was asked, so
    /// nothing may be said about a camera.</item>
    /// <item><b>The ROUTE'S OWN refusal fourth</b>, verbatim. Only the route knows whether it hit a
    /// denied OS permission, an absent enumeration route or a fault, and re-deriving that here from
    /// a string code is the shape that eventually mistypes one.</item>
    /// <item><b>"The route ran and named nothing" fifth</b> — the ONLY place
    /// <see cref="CameraReasonCodes.CameraNoDevice"/> may be produced, and only after a real route
    /// really looked.</item>
    /// <item><b>Everything passed, and NO CAMERA WAS OPENED.</b></item>
    /// </list>
    ///
    /// <para><b>THIS METHOD CANNOT RETURN <see cref="CapabilityState.Available"/> FOR ANY INPUT.</b>
    /// That is not an accident of the current build and must not be repaired by adding an arm: a
    /// device roster and a stored consent are not a frame, a model or a gaze sample, so Available
    /// would be claiming a capability nothing here has exercised
    /// (<c>runtime-capability-contract.md</c> §2 — a probe's Available rests on its declared
    /// evidence, and this slice's evidence stops at metadata). The top arm is
    /// <see cref="CapabilityState.Degraded"/>, which names what really does hold and what does not.
    /// The slice that opens a camera earns the right to say more.</para>
    /// </summary>
    /// <param name="engineAdmitted">Whether a gaze engine is implemented in this build.</param>
    /// <param name="consentRefusal">The consent verdict from <see cref="CameraConsent.Evaluate"/>; null means current.</param>
    /// <param name="inventory">What an enumeration ask learned, or null when no ask was made.</param>
    public static CapabilityState Classify(
        bool engineAdmitted, CapabilityReason? consentRefusal, CameraInventory? inventory)
    {
        if (!engineAdmitted)
        {
            return new CapabilityState.Unavailable(
                new CapabilityReason(CameraReasonCodes.CameraNoEngine, EngineGap));
        }

        if (consentRefusal is { } refusal)
        {
            return new CapabilityState.Unavailable(refusal);
        }

        if (inventory is null)
        {
            return new CapabilityState.Unavailable(new CapabilityReason(
                CapabilityReasonCodes.NotProbed,
                "camera consent is current and nothing has been asked of this machine's cameras yet, so nothing "
                + "is known about them"));
        }

        if (inventory.Refusal is { } routeRefusal)
        {
            return routeRefusal;
        }

        if (inventory.Devices.Count == 0)
        {
            return new CapabilityState.DependencyMissing(
                "a video-capture device attached to this machine",
                new CapabilityReason(
                    CameraReasonCodes.CameraNoDevice,
                    $"the {inventory.Route} enumeration ran and named no video-capture device. Plug a webcam in, "
                    + "or check that another application has not taken exclusive control of it"));
        }

        return new CapabilityState.Degraded(
            $"{inventory.Devices.Count} video-capture device(s) named by {inventory.Route}, and current explicit "
            + "consent to use one",
            new CapabilityReason(
                CameraReasonCodes.CameraNotOpened,
                "NO CAMERA WAS OPENED, no frame was decoded and no gaze sample exists: this build enumerates and "
                + "consents, and the capture and engine slices are unported. A device roster is not a working "
                + "feature"));
    }
}
