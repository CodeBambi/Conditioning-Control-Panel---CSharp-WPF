using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using CcpClient.Desktop;
using CcpClient.Desktop.Camera;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Lifecycle;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The CAMERA seam and its refusals.
///
/// <para>Every fact here is about a capability with no gaze engine behind it, which is exactly why
/// the classification is tested as a TRUTH TABLE over directly-constructed inputs rather than only
/// through the product path: the arms a real engine would reach must be executed too, or the one
/// refusal this build produces would be the only thing anybody ever ran. That is the shape
/// <see cref="HapticCapabilityTests"/> established for the same reason.</para>
///
/// <para><b>What no fact here proves.</b> No camera is opened, no frame is decoded, no model is
/// loaded and no gaze sample exists anywhere in this build. The Linux route's parsing is proved
/// against injected fixture trees, which is evidence about the parsing and NO evidence about the
/// layout of a running kernel's sysfs — nothing in this repository has executed
/// <see cref="V4L2CameraDeviceSource"/> on Linux.</para>
/// </summary>
public sealed class CameraCapabilityTests(ITestOutputHelper output)
{
    // =====================================================================================
    //  The refusal this build actually produces, and the gap it names
    // =====================================================================================

    /// <summary>
    /// THIS build has NO gaze engine, and the refusal names the ENGINE and nothing else.
    ///
    /// <para><b>The ordering half is the sharp half.</b> The last block hands the classification a
    /// current consent AND a real device roster — every lower rung satisfied — and the answer must
    /// still be the engine. Ask the device question first and this fact fails, which is the whole
    /// reason the order is written down: a user told to plug a camera in for a feature that has no
    /// engine has been sent to fix something that is not wrong.</para>
    /// </summary>
    [Fact]
    public void ThisBuildHasNOGazeEngine_AndTheRefusalNamesTHATAndNeverACameraOrAConsent()
    {
        Assert.Empty(CameraCapability.AdmittedEngines);

        var state = Assert.IsType<CapabilityState.Unavailable>(
            CameraCapability.Classify(engineAdmitted: false, consentRefusal: null, inventory: null));
        Assert.Equal(CameraReasonCodes.CameraNoEngine, state.Reason.Code);

        // It must never READ as a missing camera or a withheld consent: both have a repair the user
        // could go and perform, and performing it would change nothing at all. The detail says so in
        // as many words rather than leaving a reader to infer it.
        Assert.Contains("NOTHING WAS ASKED OF ANY CAMERA", state.Reason.Detail, StringComparison.Ordinal);
        Assert.Contains("THIS IS NOT \"no camera found\"", state.Reason.Detail, StringComparison.Ordinal);
        Assert.Contains("NOT \"consent needed\"", state.Reason.Detail, StringComparison.Ordinal);
        Assert.NotEqual(CameraReasonCodes.CameraNoDevice, state.Reason.Code);
        Assert.NotEqual(CameraReasonCodes.CameraConsentAbsent, state.Reason.Code);

        // EVERY lower rung satisfied, and the engine still wins.
        var satisfied = CameraCapability.Classify(
            engineAdmitted: false,
            consentRefusal: null,
            inventory: CameraInventory.Named("a fake route", [new CameraDevice("id", "A Camera", true)]));
        Assert.Equal(
            CameraReasonCodes.CameraNoEngine,
            Assert.IsType<CapabilityState.Unavailable>(satisfied).Reason.Code);
    }

    /// <summary>
    /// <b>NO input at all makes this capability Available.</b>
    ///
    /// <para>It is not a restatement of "the engine list is empty": the loop admits the engine,
    /// grants the consent and hands over a roster of real-looking devices — the best case any later
    /// build could reach through this classification — and asserts the answer is still
    /// <see cref="CapabilityState.Degraded"/> with the ceiling named. A device roster and a stored
    /// consent are not a frame, and a probe that said Available for them would be claiming a
    /// capability nothing in this build has exercised. Add an Available arm and this fact fails.</para>
    /// </summary>
    [Fact]
    public void NOTHINGMakesTheCameraCapabilityAvailable_BecauseNothingHereHasOpenedACamera()
    {
        CapabilityReason?[] consents =
        [
            null,
            new CapabilityReason(CameraReasonCodes.CameraConsentAbsent, "not given"),
            new CapabilityReason(CameraReasonCodes.CameraConsentStale, "older contract"),
        ];

        CameraInventory?[] inventories =
        [
            null,
            CameraInventory.Named("route", []),
            CameraInventory.Named("route", [new CameraDevice("a", "A", true)]),
            CameraInventory.Named("route", [new CameraDevice("a", "A", true), new CameraDevice("b", "B", false)]),
            CameraInventory.Refusing("route", new CapabilityState.PermissionRequired(
                new CapabilityReason(CameraReasonCodes.CameraPermissionDenied, "denied"))),
            CameraInventory.Refusing("route", new CapabilityState.Faulted(
                new CapabilityReason(CameraReasonCodes.CameraEnumerationFailed, "threw"))),
        ];

        var seen = 0;
        foreach (var engine in new[] { true, false })
        {
            foreach (var consent in consents)
            {
                foreach (var inventory in inventories)
                {
                    var state = CameraCapability.Classify(engine, consent, inventory);
                    Assert.IsNotType<CapabilityState.Available>(state);
                    seen++;
                }
            }
        }

        Assert.Equal(2 * consents.Length * inventories.Length, seen); // the loop is not vacuous

        // And the TOP of the ladder — everything satisfied — says the ceiling out loud rather than
        // rounding it up.
        var best = Assert.IsType<CapabilityState.Degraded>(CameraCapability.Classify(
            engineAdmitted: true,
            consentRefusal: null,
            inventory: CameraInventory.Named("a fake route", [new CameraDevice("id", "A Camera", true)])));
        Assert.Equal(CameraReasonCodes.CameraNotOpened, best.Reason.Code);
        Assert.Contains("NO CAMERA WAS OPENED", best.Reason.Detail, StringComparison.Ordinal);
        Assert.Contains("1 video-capture device(s)", best.SurvivingSemantics, StringComparison.Ordinal);
    }

    // =====================================================================================
    //  Consent, and the order it is asked in
    // =====================================================================================

    /// <summary>
    /// A consent given against an OLDER privacy contract refuses AGAIN, and says which contract.
    ///
    /// <para>The inversion is the last block: strip the version comparison and a user who agreed to
    /// yesterday's promise is silently held to today's, which is the exact thing upstream's version
    /// field exists to stop (<c>Services/Webcam/WebcamTrackingService.cs:100-114</c>).</para>
    /// </summary>
    [Fact]
    public void ConsentGivenAgainstAnOLDERContract_RefusesAgain_AndIsNotTheSameRefusalAsNeverAsked()
    {
        var never = new CameraConsentDocument();
        var neverReason = Assert.IsType<CapabilityReason>(CameraConsent.Evaluate(never));
        Assert.Equal(CameraReasonCodes.CameraConsentAbsent, neverReason.Code);

        var stale = new CameraConsentDocument
        {
            Granted = true,
            Version = "0.9",
            GrantedUtc = DateTimeOffset.UnixEpoch,
        };
        var staleReason = Assert.IsType<CapabilityReason>(CameraConsent.Evaluate(stale));
        Assert.Equal(CameraReasonCodes.CameraConsentStale, staleReason.Code);
        Assert.Contains("'0.9'", staleReason.Detail, StringComparison.Ordinal);
        Assert.Contains($"'{CameraConsent.CurrentVersion}'", staleReason.Detail, StringComparison.Ordinal);

        // The two are different codes because they are different things to say to a person, and
        // BOTH still refuse — the outcome upstream folds into one bool.
        Assert.NotEqual(neverReason.Code, staleReason.Code);

        stale.Version = CameraConsent.CurrentVersion;
        Assert.Null(CameraConsent.Evaluate(stale));
    }

    /// <summary>
    /// Upstream's consent dialog gates its Enable button on THREE acknowledgements AND a typed
    /// confirmation (<c>Dialogs/WebcamConsentDialog.xaml.cs:113-119</c>). Every one of the four is
    /// load-bearing here, checked by withholding exactly one at a time — and a request that has not
    /// passed them all writes NOTHING, which is stronger than a disabled button: there is no caller
    /// that can commit an unfinished consent.
    /// </summary>
    [Fact]
    public void ConsentNeedsEVERYGate_AndAnIncompleteRequestWritesNOTHING()
    {
        var complete = new CameraConsentRequest(true, true, true, CameraConsent.ConfirmationWord);
        Assert.True(complete.IsComplete);

        CameraConsentRequest[] incomplete =
        [
            complete with { AcknowledgedFramesNeverTransmitted = false },
            complete with { AcknowledgedFramesNeverSaved = false },
            complete with { AcknowledgedOthersPresentConsent = false },
            complete with { TypedConfirmation = null },
            complete with { TypedConfirmation = string.Empty },
            complete with { TypedConfirmation = "enable" },   // upstream's comparison is case-sensitive
            complete with { TypedConfirmation = "ENABLED" },
        ];

        foreach (var request in incomplete)
        {
            Assert.False(request.IsComplete, $"this request must NOT be complete: {request}");

            var document = new CameraConsentDocument();
            Assert.False(CameraConsent.TryGrant(document, request, DateTimeOffset.UnixEpoch));
            Assert.False(document.Granted);
            Assert.Equal(string.Empty, document.Version);
            Assert.Null(document.GrantedUtc);
        }

        // Whitespace around the typed word is trimmed, which is upstream's `.Trim()` at :118.
        Assert.True((complete with { TypedConfirmation = "  ENABLE\t" }).IsComplete);

        var granted = new CameraConsentDocument();
        Assert.True(CameraConsent.TryGrant(granted, complete, DateTimeOffset.UnixEpoch));
        Assert.True(granted.Granted);
        Assert.Equal(CameraConsent.CurrentVersion, granted.Version);
        Assert.Equal(DateTimeOffset.UnixEpoch, granted.GrantedUtc);
        Assert.Null(CameraConsent.Evaluate(granted));
    }

    /// <summary>
    /// Revoking clears the CONTRACT VERSION as well as the flag, so a later write that only flips
    /// the flag back cannot resurrect the old agreement.
    ///
    /// <para>Upstream clears all three fields for exactly this reason
    /// (<c>Services/Webcam/WebcamTrackingService.cs:1065-1067</c>). Leave the version behind and the
    /// second block below passes — a revoked consent silently re-granted by a bool.</para>
    /// </summary>
    [Fact]
    public void RevokeClearsTheCONTRACTVERSIONToo_SoAFlagAloneCannotResurrectTheAgreement()
    {
        var document = new CameraConsentDocument();
        Assert.True(CameraConsent.TryGrant(
            document,
            new CameraConsentRequest(true, true, true, CameraConsent.ConfirmationWord),
            DateTimeOffset.UnixEpoch));
        Assert.Null(CameraConsent.Evaluate(document));

        CameraConsent.Revoke(document);
        Assert.False(document.Granted);
        Assert.Equal(string.Empty, document.Version);
        Assert.Null(document.GrantedUtc);

        // The flag alone is NOT consent: the stored contract version no longer matches, so this
        // refuses as STALE rather than passing as current.
        document.Granted = true;
        var reason = Assert.IsType<CapabilityReason>(CameraConsent.Evaluate(document));
        Assert.Equal(CameraReasonCodes.CameraConsentStale, reason.Code);
    }

    // =====================================================================================
    //  Which refusal is produced, and by whom
    // =====================================================================================

    /// <summary>
    /// "No camera" is said ONLY after a real route really looked, and the route's OWN refusal
    /// survives verbatim with its own TYPE.
    ///
    /// <para>The first block is the important one: a null inventory means the question was never
    /// put, and it must answer <c>not-probed</c> rather than a device verdict. Collapse the two —
    /// treat "did not ask" as "asked and found nothing" — and this fact fails, which is the failure
    /// this capability exists to prevent, because a silent "no device" is indistinguishable from a
    /// real absence.</para>
    /// </summary>
    [Fact]
    public void NoDeviceIsSaidONLYAfterARouteReallyLooked_AndTheRoutesOwnRefusalSurvivesTyped()
    {
        // Nothing was asked.
        var unasked = Assert.IsType<CapabilityState.Unavailable>(
            CameraCapability.Classify(engineAdmitted: true, consentRefusal: null, inventory: null));
        Assert.Equal(CapabilityReasonCodes.NotProbed, unasked.Reason.Code);
        Assert.NotEqual(CameraReasonCodes.CameraNoDevice, unasked.Reason.Code);

        // A route really looked and named nothing: the ONE place a device may be called missing.
        var empty = Assert.IsType<CapabilityState.DependencyMissing>(CameraCapability.Classify(
            engineAdmitted: true, consentRefusal: null, inventory: CameraInventory.Named("test route", [])));
        Assert.Equal(CameraReasonCodes.CameraNoDevice, empty.Reason.Code);
        Assert.Contains("test route", empty.Reason.Detail, StringComparison.Ordinal);

        // The route's own refusals pass through with their TYPE intact, because only the route knows
        // which kind it hit. Re-deriving the type from the code here is the shape that mistypes one.
        var denied = new CapabilityState.PermissionRequired(
            new CapabilityReason(CameraReasonCodes.CameraPermissionDenied, "the OS says no"));
        Assert.Same(denied, CameraCapability.Classify(true, null, CameraInventory.Refusing("r", denied)));

        var faulted = new CapabilityState.Faulted(
            new CapabilityReason(CameraReasonCodes.CameraEnumerationFailed, "it threw"));
        Assert.Same(faulted, CameraCapability.Classify(true, null, CameraInventory.Refusing("r", faulted)));

        var missing = new CapabilityState.DependencyMissing(
            "a route", new CapabilityReason(CameraReasonCodes.CameraEnumerationUnsupported, "none here"));
        Assert.Same(missing, CameraCapability.Classify(true, null, CameraInventory.Refusing("r", missing)));
    }

    // =====================================================================================
    //  Both platforms answer, and neither answers by accident
    // =====================================================================================

    /// <summary>
    /// The LINUX route reads the kernel's real V4L2 device class, in node order, and refuses to
    /// invent devices out of the nodes that are not cameras.
    ///
    /// <para>The fixture holds one camera, a second camera at a higher node number, a sensor
    /// sub-device, a teletext node and a radio tuner. Only the two <c>videoN</c> nodes are cameras;
    /// drop the node-name filter and this fact fails with three phantom devices, which a user would
    /// see as a camera picker full of hardware they do not own.</para>
    ///
    /// <para>The identity assertion is the other half. A <c>/dev/videoN</c> path is a kernel-assigned
    /// node number, so every device from this route reports
    /// <c>IdentityIsStable = false</c> — the flag that stops a later slice persisting one as a camera
    /// identity, which <c>client/docs/capability-inventory.md</c> forbids by name.</para>
    /// </summary>
    [Fact]
    public void Linux_TheV4L2RouteReadsRealNodesInOrder_AndNeverInventsADeviceOutOfANonCameraNode()
    {
        var root = FixtureRoot();
        try
        {
            WriteNode(root, "video0", "Integrated Camera: Integrated C");
            WriteNode(root, "video2", "HD Pro Webcam C920");
            WriteNode(root, "video10", "Later Node Camera");
            WriteNode(root, "v4l-subdev0", "ov5693 sensor");
            WriteNode(root, "vbi0", "teletext");
            WriteNode(root, "radio0", "tuner");
            Directory.CreateDirectory(Path.Combine(root, "video3")); // registered node, unreadable name

            var inventory = new V4L2CameraDeviceSource(root, sandboxed: false).Enumerate();

            Assert.Null(inventory.Refusal);
            Assert.Equal(
                ["/dev/video0", "/dev/video2", "/dev/video3", "/dev/video10"],
                inventory.Devices.Select(device => device.StableId));
            Assert.Equal(
                ["Integrated Camera: Integrated C", "HD Pro Webcam C920", "(unnamed device)", "Later Node Camera"],
                inventory.Devices.Select(device => device.DisplayName));

            // A node the kernel registered must not vanish because its name would not read, which is
            // upstream's placeholder behaviour (Services/Webcam/WebcamDeviceEnumerator.cs:87).
            Assert.Contains(inventory.Devices, device => device.DisplayName == "(unnamed device)");

            // Nothing from this route may be persisted as a camera identity.
            Assert.All(inventory.Devices, device => Assert.False(device.IdentityIsStable));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// A Linux machine with no V4L2 device class, and a SANDBOXED one, get DIFFERENT typed refusals,
    /// and neither of them is a silent "no device".
    ///
    /// <para>This is the fact the packet's platform rule is about. Under Flatpak or Snap the host's
    /// <c>/dev/video*</c> is not visible to a confined process at all and the supported route is the
    /// XDG Camera portal — which this build has no client for. Answering "no camera found" there
    /// would send a user with a webcam plugged in to go and buy one. Delete the sandbox branch and
    /// this fact fails on both of its last two blocks.</para>
    /// </summary>
    [Fact]
    public void Linux_AnAbsentDeviceClassAndASANDBOXNameWhatIsMissing_NeverASilentNoDevice()
    {
        // A fresh GUID under the temp root: never created here, so it cannot exist.
        var absent = Path.Combine(Path.GetTempPath(), "ccp-camera-absent-" + Guid.NewGuid().ToString("N"));

        // Not sandboxed: the kernel really has no V4L2 node registered.
        var kernel = new V4L2CameraDeviceSource(absent, sandboxed: false).Enumerate();
        var kernelRefusal = Assert.IsType<CapabilityState.DependencyMissing>(kernel.Refusal);
        Assert.Equal(CameraReasonCodes.CameraEnumerationUnsupported, kernelRefusal.Reason.Code);
        Assert.NotEqual(CameraReasonCodes.CameraNoDevice, kernelRefusal.Reason.Code);
        Assert.Contains(absent, kernelRefusal.Dependency, StringComparison.Ordinal);
        Assert.Contains("videodev", kernelRefusal.Reason.Detail, StringComparison.Ordinal);
        Assert.Empty(kernel.Devices);

        // Sandboxed, device class invisible: the PORTAL is named, not a missing camera.
        var sandboxed = new V4L2CameraDeviceSource(absent, sandboxed: true).Enumerate();
        var portal = Assert.IsType<CapabilityState.DependencyMissing>(sandboxed.Refusal);
        Assert.Equal(CameraReasonCodes.CameraEnumerationUnsupported, portal.Reason.Code);
        Assert.Contains("org.freedesktop.portal.Camera", portal.Dependency, StringComparison.Ordinal);
        Assert.Contains("PipeWire", portal.Dependency, StringComparison.Ordinal);
        Assert.NotEqual(kernelRefusal.Reason.Detail, portal.Reason.Detail);

        // Sandboxed with the class PRESENT AND EMPTY is the same story: a sandbox does not hand a
        // confined process the host's nodes, so an empty list there is not evidence of an empty desk.
        var empty = FixtureRoot();
        try
        {
            var confined = new V4L2CameraDeviceSource(empty, sandboxed: true).Enumerate();
            Assert.Contains(
                "org.freedesktop.portal.Camera",
                Assert.IsType<CapabilityState.DependencyMissing>(confined.Refusal).Dependency,
                StringComparison.Ordinal);

            // And UNSANDBOXED, the very same empty directory IS a real "no camera" — the roster is
            // returned and the classification, not the route, calls it missing.
            var unconfined = new V4L2CameraDeviceSource(empty, sandboxed: false).Enumerate();
            Assert.Null(unconfined.Refusal);
            Assert.Empty(unconfined.Devices);
            Assert.Equal(
                CameraReasonCodes.CameraNoDevice,
                Assert.IsType<CapabilityState.DependencyMissing>(
                    CameraCapability.Classify(true, null, unconfined)).Reason.Code);
        }
        finally
        {
            Directory.Delete(empty, recursive: true);
        }
    }

    /// <summary>
    /// A platform outside the supported pair, and the Windows route selected off Windows, both
    /// refuse by NAMING what is missing rather than reporting an absence they never checked for.
    /// </summary>
    [Fact]
    public void AnUnsupportedPlatformNamesITSELF_AndNeverReportsAnAbsentCamera()
    {
        foreach (var platform in new[] { CameraHostPlatform.MacOs, CameraHostPlatform.Unknown })
        {
            var inventory = CameraDeviceSourceFactory.For(platform).Enumerate();
            var refusal = Assert.IsType<CapabilityState.DependencyMissing>(inventory.Refusal);
            Assert.Equal(CameraReasonCodes.CameraEnumerationUnsupported, refusal.Reason.Code);
            Assert.Contains("nothing was attempted", refusal.Reason.Detail, StringComparison.Ordinal);
            Assert.Empty(inventory.Devices);
        }

        // The Linux arm is selectable from ANY machine — the platform is a parameter — which is what
        // makes the V4L2 facts above executable on the Windows box this suite usually runs on.
        Assert.IsType<V4L2CameraDeviceSource>(CameraDeviceSourceFactory.For(CameraHostPlatform.Linux));

        // The Windows arm off Windows must NOT construct the [SupportedOSPlatform("windows")] COM
        // route; it refuses instead, and the refusal is the same UnsupportedCameraDeviceSource the
        // loop above just exercised. Written as two branch-free equalities against the factory's OWN
        // platform answer — which is also the stronger claim, because it is the factory's selection
        // input rather than the runtime's that has to decide.
        var onWindows = CameraDeviceSourceFactory.CurrentPlatform() == CameraHostPlatform.Windows;
        var windows = CameraDeviceSourceFactory.For(CameraHostPlatform.Windows);
        Assert.Equal(onWindows, windows is DirectShowCameraDeviceSource);
        Assert.Equal(!onWindows, windows is UnsupportedCameraDeviceSource);
    }

    /// <summary>
    /// THE ROUTE FOR THE MACHINE THIS SUITE IS RUNNING ON REALLY RUNS, and answers typed.
    ///
    /// <para>It is one fact rather than two OS-gated ones on purpose: an allowed skip would make the
    /// other platform's leg invisible on every run, and this leg is the only place the REAL
    /// enumeration route for this machine is executed at all. On Windows it drives the DirectShow
    /// COM walk against whatever is really plugged in; on Linux it reads the real
    /// <c>/sys/class/video4linux</c>. Either way the assertion is the same and is about honesty
    /// rather than hardware: a typed answer, no exception, and — whatever it found — nothing that
    /// claims a camera was opened.</para>
    /// </summary>
    [Fact]
    public void TheRouteForTHISMachineReallyRuns_AndAnswersTypedWithoutOpeningACamera()
    {
        var source = CameraDeviceSourceFactory.ForCurrentPlatform();
        var inventory = source.Enumerate();

        Assert.Equal(source.Route, inventory.Route);
        Assert.NotEmpty(inventory.Route);

        if (inventory.Refusal is { } refusal)
        {
            // A refusal is always typed and always carries one of THIS capability's codes.
            var code = refusal switch
            {
                CapabilityState.PermissionRequired permission => permission.Reason.Code,
                CapabilityState.DependencyMissing dependency => dependency.Reason.Code,
                CapabilityState.Faulted faulted => faulted.Reason.Code,
                _ => "unexpected-state:" + refusal.GetType().Name,
            };
            Assert.Contains(code, new[]
            {
                CameraReasonCodes.CameraPermissionDenied,
                CameraReasonCodes.CameraEnumerationUnsupported,
                CameraReasonCodes.CameraEnumerationFailed,
            });
            Assert.Empty(inventory.Devices);
        }
        else
        {
            // Whatever it found, every device carries a non-empty identity and a non-empty name.
            Assert.All(inventory.Devices, device =>
            {
                Assert.False(string.IsNullOrWhiteSpace(device.StableId));
                Assert.False(string.IsNullOrWhiteSpace(device.DisplayName));
            });
        }

        // Nothing here may reach Available, on any machine, with any hardware attached.
        Assert.IsNotType<CapabilityState.Available>(CameraCapability.Classify(true, null, inventory));
    }

    // =====================================================================================
    //  Memory-only, structurally
    // =====================================================================================

    /// <summary>
    /// <b>NO FRAME CAN BE RETAINED ANYWHERE IN THE CAMERA SEAM, AND NO FRAME CAN ESCAPE IT</b> — so
    /// no frame, crop, tensor, landmark or gaze sample can be written to disk, put in a log line,
    /// attached to a crash report or handed to an AI prompt, because there is nothing holding one
    /// when any of that happens and nothing to hand over.
    ///
    /// <para><b>This fact was WEAKENED ON ITS FACE by the capture slice, and strengthened underneath,
    /// so read the change before trusting it.</b> Until a camera could be opened, the rule was
    /// simply "no member anywhere under <c>Camera/</c> may carry pixels", and that was exactly right
    /// for a build that decoded nothing. A build that really opens a camera has to touch pixels
    /// somewhere, so a rule forbidding it everywhere would either be deleted or evaded — the usual
    /// evasion being a sub-namespace the scan does not reach. Rather than that, the rule now has
    /// four parts, and THREE OF THEM ARE NEW OBLIGATIONS THAT DID NOT EXIST BEFORE:</para>
    ///
    /// <para><b>And this fact did not keep its own promise until 2026-08-25, which is worth stating
    /// plainly because the repair is what made the sentence above true.</b> The scan matched the
    /// namespace EXACTLY, so the sub-namespace evasion it names as its reason for existing was not
    /// prevented at all; and the recogniser knew five fixed shapes, none of which is an imaging or
    /// tensor type, so <c>private Mat _resizeBuffer;</c> — the field upstream's pipeline holds five
    /// times over — passed it in silence. The scope is a PREFIX now and the recogniser walks
    /// STRUCTURE (<see cref="CarriesPixels"/>) and ORIGIN (<see cref="ForeignOrigin"/>). No offending
    /// member ever existed in this tree: the guard was blind, the product was not leaking, and
    /// <see cref="TheRetentionRuleCatchesABufferTypeItHasNEVERHeardOf_AndTheSweepReachesEverySubNamespace"/>
    /// is what keeps the sight.</para>
    ///
    /// <list type="number">
    /// <item><b>RETENTION IS BANNED OUTRIGHT.</b> No type in the namespace OR ANY NAMESPACE BELOW IT
    /// — including the pixel boundary, including the interop declarations — may declare a FIELD or a
    /// PROPERTY that can carry pixels, and "can carry pixels" is decided TRANSITIVELY rather than
    /// from a list of type names. Nothing in this product can hold the last frame a camera saw, so
    /// there is no object for a serializer, a logger or a crash dumper to find one in.</item>
    /// <item><b>STATE TYPED FROM A PACKAGE IS BANNED</b> wherever it is not a buffer by shape either
    /// — <c>NamedOnnxValue</c> is a <c>string</c> and an <c>object</c>, and no structural rule can
    /// ever see what such a thing holds. A field or property typed from an imaging library, an
    /// inference runtime or a media framework has to be argued for by editing this fact.</item>
    /// <item><b>ESCAPE IS BANNED except at ONE NAMED TYPE.</b> Pixel-carrying parameters and returns
    /// are allowed only on <c>CameraFrameProbe</c> and only on PRIVATE members elsewhere. The
    /// boundary type is checked to be <c>static</c> and <c>abstract sealed</c> with NO fields at all,
    /// and its pixel parameters are <c>ReadOnlySpan&lt;byte&gt;</c>, which the C# compiler itself
    /// forbids storing in a field, capturing in a closure or boxing. A frame handed to it cannot
    /// outlive the call by construction rather than by care.</item>
    /// <item><b>NATIVE HANDLES stay confined to the operating system's own signatures</b> —
    /// <c>[ComImport]</c> declarations and <c>[DllImport]</c> methods. Those are the OS's shapes, not
    /// this port's design. An <c>IntPtr</c> anywhere else still fails immediately.</item>
    /// </list>
    ///
    /// <para>So: add a <c>byte[] Frame</c> field, a <c>float[] Landmarks</c> property, a
    /// <c>Stream Preview</c>, a <c>Mat</c>, a <c>DenseTensor&lt;float&gt;</c>, an <c>OrtValue</c>, a
    /// public method returning pixels, or an <c>IntPtr</c> on a hand-written type anywhere under
    /// <c>Camera/</c> OR ANY FOLDER BELOW IT, and this fails. The seam still has to keep arguing for
    /// itself; it now has to argue for two more things than it used to.</para>
    /// </summary>
    [Fact]
    public void NoFrameCanBeRETAINEDAnywhereInTheCameraSeam_AndNoneCanESCAPEItExceptAtTheOnePixelBoundary()
    {
        var product = typeof(CameraCapability).Assembly;
        var seam = typeof(CameraCapability).Namespace!;
        var types = product.GetTypes()
            .Where(type => InTheCameraSeam(type.Namespace, seam))
            .ToList();
        Assert.NotEmpty(types); // the scan below is not vacuous

        // The stop rule inside CarriesPixels is only meaningful if "the runtime's own assemblies"
        // and "everything else" are actually different places. Asserted, not assumed: a build that
        // put them in one folder would silently stop walking OpenCvSharp and OnnxRuntime too.
        Assert.NotEqual(string.Empty, RuntimeDirectory);
        Assert.True(IsRuntimeOwned(typeof(object)), "the runtime must own its own object type");
        Assert.False(
            IsRuntimeOwned(typeof(CameraCapability)),
            "the product loaded from the runtime's own directory, so the field walk would stop at "
            + "every package type instead of walking it: " + RuntimeDirectory);

        // The ONE type allowed to take pixels, named here so that adding a second is an edit to this
        // fact rather than a quiet addition under Camera/.
        var boundary = typeof(CameraFrameProbe);
        Assert.Contains(boundary, types);

        // It cannot RETAIN a frame, and that is checked rather than asserted in prose: a static class
        // with no fields has nowhere to put one, and every pixel parameter below is a ref struct the
        // compiler will not let anybody store.
        Assert.True(boundary.IsAbstract && boundary.IsSealed, "the pixel boundary must be a static class");
        var boundaryStorage = boundary
            .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                | BindingFlags.Static | BindingFlags.DeclaredOnly)
            // `const` fields are compile-time literals baked into every call site: they hold the
            // ported acceptance thresholds and cannot store anything at run time, let alone a frame.
            // Every OTHER field is storage, and the boundary may have none.
            .Where(field => !field.IsLiteral)
            .Select(field => field.Name)
            .ToList();
        Assert.True(
            boundaryStorage.Count == 0,
            "the pixel boundary gained storage, so a frame handed to it could now outlive the call: "
            + string.Join(", ", boundaryStorage));

        var offenders = new List<string>();
        var scanned = 0;
        foreach (var type in types)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

            // A [ComImport] declaration is the OPERATING SYSTEM's signature rather than this port's
            // design: it holds no state, and its IntPtr parameters are COM out-parameters and error
            // logs (IPropertyBag.Read's pErrorLog and IMFMediaBuffer::Lock's ppbBuffer are two).
            // Native handles are allowed there and on P/Invoke declarations, which are the same
            // thing — a signature Windows wrote — and banned everywhere else.
            var comImport = type.IsDefined(typeof(System.Runtime.InteropServices.ComImportAttribute), false);

            // RETENTION: fields and properties may NEVER carry pixels, on any type at all, native
            // handles included nowhere except a COM declaration's own members.
            foreach (var property in type.GetProperties(flags))
            {
                scanned++;
                if (CarriesPixels(property.PropertyType, comImport))
                {
                    offenders.Add($"RETAINS {type.Name}.{property.Name} : {property.PropertyType.Name}");
                }
                else if (ForeignOrigin(property.PropertyType, product) is { } foreign)
                {
                    offenders.Add($"FOREIGN {type.Name}.{property.Name} : {foreign}");
                }
            }

            foreach (var field in type.GetFields(flags))
            {
                scanned++;
                if (CarriesPixels(field.FieldType, comImport))
                {
                    offenders.Add($"RETAINS {type.Name}.{field.Name} : {field.FieldType.Name}");
                }
                else if (ForeignOrigin(field.FieldType, product) is { } foreign)
                {
                    offenders.Add($"FOREIGN {type.Name}.{field.Name} : {foreign}");
                }
            }

            // ESCAPE: a member that can hand pixels across a boundary. Private members of the
            // implementation may (the capture path has to copy a locked native buffer somewhere);
            // anything callable from outside the type may not, except on the named boundary.
            foreach (var method in type.GetMethods(flags))
            {
                scanned++;
                var nativeAllowed = comImport || method.Attributes.HasFlag(MethodAttributes.PinvokeImpl);
                if (!CarriesPixels(method.ReturnType, nativeAllowed)
                    && !method.GetParameters().Any(parameter => CarriesPixels(parameter.ParameterType, nativeAllowed)))
                {
                    continue;
                }

                if (type == boundary || method.IsPrivate)
                {
                    continue;
                }

                offenders.Add($"ESCAPES {type.Name}.{method.Name}(...) : {method.ReturnType.Name}");
            }
        }

        // WHAT WAS ACTUALLY SWEPT, written into the run's own output, because a guard that reports
        // success over an empty set is the failure mode this port has hit twice. A reader of a green
        // run can see the subject was there without re-deriving it.
        output.WriteLine(
            $"camera seam scan: {types.Count} type(s) under '{seam}' and below, {scanned} member(s) examined");
        Assert.True(scanned > 50, $"only {scanned} members scanned — the seam scan has lost its subject");
        Assert.True(
            offenders.Count == 0,
            "the camera seam gained a member that can RETAIN image or per-frame biometric data, that holds a type "
            + "from a FOREIGN package, or that can let it ESCAPE, which client/docs/capability-inventory.md "
            + "requires to be memory-only:\n  "
            + string.Join("\n  ", offenders));

        // And there is no audio anywhere in it either: upstream's rule is that audio capture is never
        // opened (Services/Webcam/WebcamTrackingService.cs:30), and here there is no member to
        // open one with.
        Assert.DoesNotContain(types, type => type.Name.Contains("Audio", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// <b>THE RETENTION RULE RECOGNISES A BUFFER TYPE THIS PORT HAS NEVER HEARD OF, AND THE SCAN
    /// REACHES EVERY SUB-NAMESPACE.</b> The fact above sweeps the real tree; this one proves the
    /// sweep has teeth, over inputs constructed here, because a rule that catches nothing on a clean
    /// tree and a rule that catches everything look identical from a green run.
    ///
    /// <para><b>Both halves were holes until 2026-08-25, and both are the same hole: an engine slice
    /// that lands anyway.</b> The recogniser knew five shapes, none of which is <c>Mat</c>,
    /// <c>DenseTensor&lt;float&gt;</c> or <c>OrtValue</c>; and the sweep matched the namespace
    /// EXACTLY, so a type in <c>Camera.Inference</c> was never looked at — which the retention rule's
    /// own docstring names as the evasion it exists to prevent. No offending field existed in the
    /// tree at any point: this repaired the GUARD, not a leak.</para>
    ///
    /// <para>The decoys below are the shapes of the real thing rather than the real thing: a handle
    /// on a base class is <c>OpenCvSharp.Mat</c> (<c>DisposableCvObject.ptr</c>), a
    /// <c>Memory&lt;float&gt;</c> is <c>DenseTensor&lt;T&gt;</c>, a <see cref="SafeHandle"/> field is
    /// <c>OrtValue</c>. Nothing here takes a dependency to test one, and every one of them is caught
    /// by structure, so the fourth such type — the one nobody has named yet — is caught too.</para>
    ///
    /// <para>The second list is the load-bearing half of a strictness claim: the real shapes the seam
    /// already holds must NOT be flagged. A <see cref="Dictionary{TKey, TValue}"/> keeps an
    /// <c>int[]</c> of hash buckets and a <see cref="CancellationToken"/> reaches a kernel wait
    /// handle, and a walk that called those "pixels" would be turned off within the week.</para>
    /// </summary>
    [Fact]
    public void TheRetentionRuleCatchesABufferTypeItHasNEVERHeardOf_AndTheSweepReachesEverySubNamespace()
    {
        // ---- the sweep's SCOPE: a prefix, so no sub-namespace can hide under it ----
        const string seam = "CcpClient.Desktop.Camera";
        Assert.True(InTheCameraSeam(seam, seam));
        Assert.True(InTheCameraSeam(seam + ".Inference", seam));
        Assert.True(InTheCameraSeam(seam + ".Gaze.Onnx", seam));
        Assert.False(InTheCameraSeam("CcpClient.Desktop", seam));
        Assert.False(InTheCameraSeam("CcpClient.Desktop.CameraMocks", seam)); // a sibling, not the seam
        Assert.False(InTheCameraSeam(null, seam));

        // And the REAL seam types really are selected by it — the scope change is not theoretical.
        Assert.Equal(seam, typeof(CameraCapability).Namespace);
        Assert.True(InTheCameraSeam(typeof(CameraFrameProbe).Namespace, seam));

        // ---- what CARRIES a buffer ----
        Type[] carriers =
        [
            typeof(DecoyNativeImage),                    // IntPtr on a BASE class: the Mat shape
            typeof(DecoyResizeBuffer),                   // ... reached through a derived type
            typeof(DecoyTensor),                         // Memory<float>: the DenseTensor shape
            typeof(DecoyHandle),                         // a SafeHandle itself
            typeof(DecoySession),                        // a SafeHandle FIELD: the OrtValue shape
            typeof(DecoyResizeBuffer[]),                 // an array of them
            typeof(List<DecoyResizeBuffer>),             // ... and a collection of them
            typeof(Dictionary<string, DecoyTensor>),     // ... behind a runtime type's generic argument
            typeof(Task<DecoyNativeImage>),              // ... and behind an await
            typeof(byte[]), typeof(float[]), typeof(IntPtr), typeof(UIntPtr), typeof(HandleRef),
            typeof(GCHandle), typeof(System.Buffers.MemoryHandle),   // a PINNED frame is still a frame
            typeof(MemoryStream), typeof(Memory<float>), typeof(ReadOnlyMemory<byte>), typeof(Span<byte>),
        ];

        foreach (var carrier in carriers)
        {
            Assert.True(
                CarriesPixels(carrier, allowNativeHandles: false),
                $"{carrier.Name} can hold a frame and the retention rule did not say so");
        }

        // ---- what does NOT, including every shape the seam really holds today ----
        Type[] innocent =
        [
            typeof(string), typeof(int), typeof(bool), typeof(Guid), typeof(DateTimeOffset),
            typeof(bool[]), typeof(char[]), typeof(string[]),
            typeof(List<string>), typeof(IReadOnlyList<string>),
            typeof(Dictionary<string, JsonElement>),     // CameraConsentDocument.ExtensionData
            typeof(CancellationToken),                   // reaches a kernel handle; is not an image
            typeof(CameraDevice), typeof(CameraInventory), typeof(CameraConsentDocument),
            typeof(DecoyRing),                           // self-referencing: must TERMINATE, and be false
        ];

        foreach (var type in innocent)
        {
            Assert.False(
                CarriesPixels(type, allowNativeHandles: false),
                $"{type.Name} cannot hold a frame, and a rule that says it can will be deleted");
        }

        // The native-handle allowance is for the OPERATING SYSTEM's own signatures and covers handles
        // ONLY — a COM declaration is still not allowed to carry an array of pixels or a stream.
        Assert.False(CarriesPixels(typeof(IntPtr), allowNativeHandles: true));
        Assert.False(CarriesPixels(typeof(DecoyNativeImage), allowNativeHandles: true));
        Assert.True(CarriesPixels(typeof(byte[]), allowNativeHandles: true));
        Assert.True(CarriesPixels(typeof(MemoryStream), allowNativeHandles: true));

        // ---- and the blind spot, covered from the other side ----
        // NamedOnnxValue is a string and an object: no structural rule can see what it holds, so the
        // seam refuses state typed from a package instead, whatever its shape.
        var product = typeof(CameraCapability).Assembly;
        Assert.NotNull(ForeignOrigin(typeof(FactAttribute), product));
        Assert.NotNull(ForeignOrigin(typeof(FactAttribute[]), product));
        Assert.NotNull(ForeignOrigin(typeof(List<FactAttribute>), product));
        Assert.Null(ForeignOrigin(typeof(CameraInventory), product));
        Assert.Null(ForeignOrigin(typeof(Dictionary<string, JsonElement>), product));
        Assert.Null(ForeignOrigin(typeof(int), product));
    }

    /// <summary>
    /// The consent file is the ONLY thing this capability writes, and it holds consent metadata and
    /// nothing else.
    ///
    /// <para>The document is granted, saved through the real store, and the bytes on disk are read
    /// back key by key. Anything beyond the three consent fields plus the store's own schema members
    /// fails it — so a later edit that persists a device identity, a calibration, a preview path or a
    /// frame count has to delete this fact to land.</para>
    /// </summary>
    [Fact]
    public async Task TheConsentFileIsTheONLYThingWritten_AndHoldsCONSENTMETADATAOnly()
    {
        var (participant, directory, _) = NewParticipant();
        try
        {
            await participant.StartAsync(TestContext.Current.CancellationToken);
            Assert.True(await participant.GrantConsentAsync(
                new CameraConsentRequest(true, true, true, CameraConsent.ConfirmationWord),
                DateTimeOffset.UnixEpoch));
            await participant.FlushAsync(TimeSpan.FromSeconds(5));

            var files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetFileName(path)!)
                .Order()
                .ToList();
            Assert.Equal([CameraConsentDocument.FileName], files);

            using var document = JsonDocument.Parse(
                await File.ReadAllTextAsync(
                    Path.Combine(directory, CameraConsentDocument.FileName),
                    TestContext.Current.CancellationToken));
            var keys = document.RootElement.EnumerateObject().Select(property => property.Name).Order().ToList();
            Assert.Equal(["granted", "grantedUtc", "migrationJournal", "schemaVersion", "version"], keys);
            Assert.True(document.RootElement.GetProperty("granted").GetBoolean());
            Assert.Equal(CameraConsent.CurrentVersion, document.RootElement.GetProperty("version").GetString());

            // The withdrawal reaches disk too — a lost revoke leaves a stored agreement the user
            // believes they cancelled.
            await participant.RevokeConsentAsync();
            await participant.FlushAsync(TimeSpan.FromSeconds(5));
            using var revoked = JsonDocument.Parse(
                await File.ReadAllTextAsync(
                    Path.Combine(directory, CameraConsentDocument.FileName),
                    TestContext.Current.CancellationToken));
            Assert.False(revoked.RootElement.GetProperty("granted").GetBoolean());
            Assert.Equal(string.Empty, revoked.RootElement.GetProperty("version").GetString());
        }
        finally
        {
            await participant.StopAsync();
            Directory.Delete(directory, recursive: true);
        }
    }

    // =====================================================================================
    //  The product path: a whole launch that asks nothing of any camera
    // =====================================================================================

    /// <summary>
    /// RESTORING SETTINGS, PROBING, GRANTING CONSENT AND REVOKING IT ALL ASK NOTHING OF ANY CAMERA —
    /// and the reason the last two still ask nothing is that the ENGINE gate is asked FIRST.
    ///
    /// <para>This is the fact <c>client/docs/capability-inventory.md</c>'s "opening the dashboard,
    /// restoring settings, or finding calibration never starts it" turns into something that can
    /// fail. The recording route counts every ask; consent is then GRANTED, which removes the one
    /// gate a reader would expect to be doing the work, and the count is still zero because no
    /// engine exists to spend a camera on. Move the consent rung above the engine rung and the third
    /// block fails.</para>
    /// </summary>
    [Fact]
    public async Task RestoringSettingsAndGrantingConsentBothAskNOTHINGOfAnyCamera()
    {
        var (participant, directory, route) = NewParticipant();
        try
        {
            await participant.StartAsync(TestContext.Current.CancellationToken);
            Assert.Equal(0, route.Asks);
            Assert.Equal(0, participant.Enumerations);
            Assert.Null(participant.LastInventory);

            // Probing with no consent refuses on the ENGINE and asks nothing.
            var unconsented = Assert.IsType<CapabilityState.Unavailable>(
                await participant.ProbeAsync(TestContext.Current.CancellationToken));
            Assert.Equal(CameraReasonCodes.CameraNoEngine, unconsented.Reason.Code);
            Assert.Equal(0, route.Asks);

            // CONSENT GRANTED — and it STILL asks nothing, because the engine is asked first.
            Assert.True(await participant.GrantConsentAsync(
                new CameraConsentRequest(true, true, true, CameraConsent.ConfirmationWord),
                DateTimeOffset.UnixEpoch));
            Assert.Null(CameraConsent.Evaluate(participant.Consent.Current));

            var consented = Assert.IsType<CapabilityState.Unavailable>(
                await participant.ProbeAsync(TestContext.Current.CancellationToken));
            Assert.Equal(CameraReasonCodes.CameraNoEngine, consented.Reason.Code);
            Assert.Equal(0, route.Asks);
            Assert.Equal(0, participant.Enumerations);

            await participant.RevokeConsentAsync();
            Assert.Equal(0, route.Asks);
        }
        finally
        {
            await participant.StopAsync();
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// With an engine admitted, THE CONSENT GATE alone still stops the camera question being put —
    /// and once consent is current the route is asked exactly once per probe.
    ///
    /// <para>The engine is a parameter here for the reason the participant documents: with the
    /// build's real answer always false, the consent rung, the permission rung and the no-device rung
    /// would never be executed by anything at all. Delete the consent gate from
    /// <c>CameraParticipant.ProbeAsync</c> and the second block fails with an ask nobody consented
    /// to.</para>
    /// </summary>
    [Fact]
    public async Task WithAnEngineAdmitted_CONSENTAloneStillStopsTheCameraQuestionBeingPut()
    {
        var (participant, directory, route) = NewParticipant(
            engineAdmitted: true,
            inventory: CameraInventory.Named("recording route", [new CameraDevice("id-1", "A Camera", true)]));
        try
        {
            await participant.StartAsync(TestContext.Current.CancellationToken);

            var refused = Assert.IsType<CapabilityState.Unavailable>(
                await participant.ProbeAsync(TestContext.Current.CancellationToken));
            Assert.Equal(CameraReasonCodes.CameraConsentAbsent, refused.Reason.Code);
            Assert.Equal(0, route.Asks);
            Assert.Null(participant.LastInventory);

            Assert.True(await participant.GrantConsentAsync(
                new CameraConsentRequest(true, true, true, CameraConsent.ConfirmationWord),
                DateTimeOffset.UnixEpoch));

            var probed = Assert.IsType<CapabilityState.Degraded>(
                await participant.ProbeAsync(TestContext.Current.CancellationToken));
            Assert.Equal(CameraReasonCodes.CameraNotOpened, probed.Reason.Code);
            Assert.Equal(1, route.Asks);
            Assert.Equal(1, participant.Enumerations);
            Assert.NotNull(participant.LastInventory);

            // Withdrawing consent drops the roster: it was learned under a consent that is gone.
            await participant.RevokeConsentAsync();
            Assert.Null(participant.LastInventory);
            Assert.Equal(
                CameraReasonCodes.CameraConsentAbsent,
                Assert.IsType<CapabilityState.Unavailable>(participant.State).Reason.Code);
        }
        finally
        {
            await participant.StopAsync();
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// The refusal path LOGS NO CAMERA DATA: not a device name, not a device identity, not the
    /// contents of anything a camera saw.
    ///
    /// <para>The recording route names its device <c>SECRET-CAM-DEADBEEF</c> with the identity
    /// <c>/dev/secret-deadbeef</c>, and after a full lifecycle — restore, refuse, consent, probe,
    /// revoke — neither string appears in a single logged line, while the COUNT does. Log the device
    /// list the way upstream does (<c>Services/Webcam/WebcamTrackingService.cs:1136-1141</c>) and
    /// this fails. There is no frame to check for because the seam has no frame type at all, which
    /// <see cref="NoFrameCanBeRETAINEDAnywhereInTheCameraSeam_AndNoneCanESCAPEItExceptAtTheOnePixelBoundary"/>
    /// proves separately.</para>
    /// </summary>
    [Fact]
    public async Task TheRefusalPathLOGSNoDeviceIdentity_OnlyStateAndACount()
    {
        const string secretName = "SECRET-CAM-DEADBEEF";
        const string secretId = "/dev/secret-deadbeef";
        var (participant, directory, _) = NewParticipant(
            engineAdmitted: true,
            inventory: CameraInventory.Named("recording route", [new CameraDevice(secretId, secretName, false)]),
            out var lines);
        try
        {
            await participant.StartAsync(TestContext.Current.CancellationToken);
            await participant.ProbeAsync(TestContext.Current.CancellationToken);
            Assert.True(await participant.GrantConsentAsync(
                new CameraConsentRequest(true, true, true, CameraConsent.ConfirmationWord),
                DateTimeOffset.UnixEpoch));
            await participant.ProbeAsync(TestContext.Current.CancellationToken);
            await participant.RevokeConsentAsync();

            Assert.NotEmpty(lines); // the scan below is not vacuous
            var transcript = string.Join("\n", lines);
            Assert.DoesNotContain(secretName, transcript, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(secretId, transcript, StringComparison.OrdinalIgnoreCase);

            // The count IS logged — the only question a log is asked here.
            Assert.Contains(lines, line => line.Contains("named 1 device(s)", StringComparison.Ordinal));
            Assert.Contains(lines, line => line.Contains("no camera was opened", StringComparison.Ordinal));
        }
        finally
        {
            await participant.StopAsync();
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// THE REAL COMPOSITION ROOT registers the camera capability, and a whole real launch — every
    /// phase, the real enumeration route for this machine — asks NOTHING of any camera.
    ///
    /// <para>No doubles: this boots the product's own participants on a fresh data root and reads the
    /// participant's own counter afterwards. A capability that refuses every user while staying
    /// invisible in the one place the port reports what it cannot do is the shape the
    /// truthful-capability contract exists to prevent, so the registration is asserted too — and the
    /// state it reports must be the ENGINE gap, never a claim about this machine's hardware.</para>
    /// </summary>
    [Fact]
    public async Task TheREALCompositionRootRegistersTheCamera_AndAWholeLaunchAsksNothingOfAnyCamera()
    {
        var trace = new StartupTrace();
        var settingsDirectory = Path.Combine(Path.GetTempPath(), "ccp-camera-root-" + Guid.NewGuid().ToString("N"));
        var root = new CompositionRoot
        {
            SettingsPathFactory = () => Path.Combine(settingsDirectory, "settings.json"),
        };
        ApplicationHost? host = null;

        var outcome = await StartupPhaseRunner.RunAsync(
            Program.CreateStartupPhases(root, trace, built => host = built), trace, CancellationToken.None);

        try
        {
            Assert.IsType<StartupOutcome.Success>(outcome);
            Assert.Contains(CameraCapability.CapabilityName, host!.Capabilities!.Names);

            var state = Assert.IsType<CapabilityState.Unavailable>(
                host.Capabilities.GetState(CameraCapability.CapabilityName));
            Assert.Equal(CameraReasonCodes.CameraNoEngine, state.Reason.Code);

            // Never a claim about this machine's hardware or this user's privacy decision.
            Assert.NotEqual(CameraReasonCodes.CameraNoDevice, state.Reason.Code);
            Assert.NotEqual(CameraReasonCodes.CameraConsentAbsent, state.Reason.Code);
            Assert.NotEqual(CameraReasonCodes.CameraPermissionDenied, state.Reason.Code);

            // The REAL participant, with the REAL routes for this platform, asked it nothing.
            var participant = Assert.Single(host.Participants.OfType<CcpClient.Desktop.Camera.CameraParticipant>());
            Assert.Equal(0, participant.Enumerations);
            Assert.Null(participant.LastInventory);
            Assert.False(participant.EngineAdmitted);
            Assert.False(participant.Consent.Current.Granted);

            // AND NO CAMERA WAS OPENED, which is the claim the capture slice added. Enumerations
            // alone stopped being enough the moment an Open verb existed: a launch that opened a
            // device without enumerating one would have passed the line above and lit this user's
            // camera indicator.
            Assert.Equal(0, participant.CameraOpenAttempts);
            Assert.False(participant.CaptureRunning);
            Assert.Equal(0, participant.FramesRead);
            Assert.Empty(participant.CaptureAttempts);
        }
        finally
        {
            if (host is not null)
            {
                await host.ShutdownAsync();
            }

            try
            {
                Directory.Delete(settingsDirectory, recursive: true);
            }
            catch (DirectoryNotFoundException)
            {
                // Nothing was written; there is nothing to clean up.
            }
        }
    }

    // =====================================================================================
    //  Helpers
    // =====================================================================================

    /// <summary>Whether a namespace IS the camera seam or is nested INSIDE it.
    ///
    /// <para>A prefix, never an equality. The seam scan asked <c>Namespace == "…Camera"</c> until
    /// 2026-08-25, which meant a type in <c>…Camera.Inference</c> or <c>…Camera.Gaze</c> was not
    /// scanned at all — the exact evasion the retention rule's own docstring says it exists to stop.
    /// The boundary character matters as much as the prefix: a sibling namespace such as
    /// <c>CcpClient.Desktop.CameraMocks</c> is NOT the seam and must not be swept into it.</para>
    /// </summary>
    private static bool InTheCameraSeam(string? candidate, string seam) =>
        candidate is not null
        && candidate.StartsWith(seam, StringComparison.Ordinal)
        && (candidate.Length == seam.Length || candidate[seam.Length] == '.');

    /// <summary>The directory the .NET runtime's OWN assemblies are loaded from, which is wherever
    /// <see cref="object"/> lives. Everything else — this product, and every NuGet package it or a
    /// later slice pulls in — is loaded from somewhere else, and that is the whole discriminator
    /// <see cref="CarriesPixels"/> uses to decide whose private state it is entitled to walk. The
    /// retention fact asserts this is a real distinction rather than assuming it.</summary>
    private static readonly string RuntimeDirectory =
        Path.GetDirectoryName(typeof(object).Assembly.Location) ?? string.Empty;

    /// <summary>Whether a type belongs to the runtime itself.</summary>
    private static bool IsRuntimeOwned(Type type)
    {
        var location = type.Assembly.Location;
        return RuntimeDirectory.Length > 0
            && location.Length > 0
            && string.Equals(Path.GetDirectoryName(location), RuntimeDirectory, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether a type can carry pixels, tensors, landmarks or any other raw buffer — <b>decided by
    /// SHAPE, TRANSITIVELY</b>, so a type this port has never heard of is caught the first time
    /// somebody declares a field of it.
    ///
    /// <para><b>Why it is not a list of names.</b> Until 2026-08-25 this recognised exactly five
    /// shapes — pointers, <see cref="IntPtr"/>, <see cref="Stream"/>, a span/memory generic and a
    /// primitive array — and <c>OpenCvSharp.Mat</c>, <c>DenseTensor&lt;float&gt;</c> and
    /// <c>OrtValue</c> are NONE of them, so <c>private Mat _resizeBuffer;</c> passed in silence.
    /// That is precisely the field upstream's gaze pipeline holds
    /// (<c>Services/Webcam/WebcamTrackingService.cs:3179,3183,3349,3356,3536</c>), so the guard was
    /// blind to the only shape it was ever going to have to catch. Adding those three names would
    /// have rotted the day a fourth appeared; the rule below asks what a type IS MADE OF instead.</para>
    ///
    /// <list type="bullet">
    /// <item><b>Leaves</b> — the shapes that ARE a buffer or a handle to one: a pointer, an
    /// <see cref="IntPtr"/>/<see cref="UIntPtr"/>, a <see cref="SafeHandle"/>, a
    /// <see cref="CriticalHandle"/>, a <see cref="HandleRef"/>, a <see cref="GCHandle"/>, a
    /// <see cref="System.Buffers.MemoryHandle"/>, a <see cref="Stream"/>, any span/memory generic,
    /// and an array of a numeric primitive.</item>
    /// <item><b>The walk</b> — by-ref element, array element, EVERY generic argument, and every
    /// field on the whole base chain, public and private, instance and static.
    /// Cycle-safe by a visited set and depth-bounded, so a self-referencing type terminates.</item>
    /// <item><b>The one stop</b> — the runtime's own private plumbing is not opened up. A
    /// <see cref="Dictionary{TKey, TValue}"/> holds an <c>int[]</c> of hash buckets and a
    /// <see cref="CancellationTokenSource"/> holds a kernel wait handle; neither is an image, and a
    /// walk that descended into them would flag every collection in the seam and be deleted within a
    /// week. Their GENERIC ARGUMENTS are still walked, so <c>Dictionary&lt;string, byte[]&gt;</c>,
    /// <c>Task&lt;Mat&gt;</c> and <c>Lazy&lt;DenseTensor&lt;float&gt;&gt;</c> are all caught.</item>
    /// </list>
    ///
    /// <para><b>What it does NOT catch, stated rather than implied.</b> A buffer behind an
    /// <see cref="object"/>-typed or non-generic-interface-typed field is invisible to it —
    /// <c>NamedOnnxValue</c> is exactly that shape, a <c>string</c> and an <c>object</c> — because a
    /// declared type is all reflection can see without an instance. That hole is covered from the
    /// other side by <see cref="ForeignOrigin"/>, which asks where a type came FROM instead of what
    /// it is made of. A buffer smuggled inside a runtime type's private state is not caught either,
    /// and neither is one that only exists inside a method body — the latter deliberately, since a
    /// local cannot outlive its call and the ESCAPE half already checks every signature.</para>
    /// </summary>
    private static bool CarriesPixels(Type type, bool allowNativeHandles) =>
        CarriesPixels(type, allowNativeHandles, [], 0);

    private static bool CarriesPixels(Type type, bool allowNativeHandles, HashSet<Type> visited, int depth)
    {
        if (type.IsPointer)
        {
            return true;
        }

        if (type.IsByRef)
        {
            return CarriesPixels(type.GetElementType()!, allowNativeHandles, visited, depth);
        }

        if (typeof(Stream).IsAssignableFrom(type))
        {
            return true;
        }

        // Native handles: banned everywhere except on the operating system's own signatures. The
        // runtime's OWN handle wrappers are enumerated here for one reason — they live in the
        // runtime, whose private state the walk below deliberately does not open, so SafeHandle's
        // IntPtr and GCHandle's pinned buffer would otherwise be invisible. This is the one place a
        // name list is unavoidable, and it is a list of the runtime's handle CATEGORY rather than of
        // anybody's imaging types.
        if (!allowNativeHandles
            && (type == typeof(IntPtr) || type == typeof(UIntPtr) || type == typeof(HandleRef)
                || type == typeof(GCHandle) || type == typeof(System.Buffers.MemoryHandle)
                || typeof(SafeHandle).IsAssignableFrom(type)
                || typeof(CriticalHandle).IsAssignableFrom(type)))
        {
            return true;
        }

        var name = type.Name;
        if (name.StartsWith("Span`", StringComparison.Ordinal)
            || name.StartsWith("ReadOnlySpan`", StringComparison.Ordinal)
            || name.StartsWith("Memory`", StringComparison.Ordinal)
            || name.StartsWith("ReadOnlyMemory`", StringComparison.Ordinal))
        {
            return true;
        }

        if (type.IsArray)
        {
            var element = type.GetElementType()!;
            return element.IsPrimitive
                ? element != typeof(bool) && element != typeof(char)
                : CarriesPixels(element, allowNativeHandles, visited, depth + 1);
        }

        // 12 is far past anything a real object graph in this seam reaches; the visited set is what
        // actually terminates a cycle, and this is the belt for the braces.
        if (depth >= 12 || type.IsPrimitive || type.IsEnum || type == typeof(string) || !visited.Add(type))
        {
            return false;
        }

        foreach (var argument in type.GetGenericArguments())
        {
            if (CarriesPixels(argument, allowNativeHandles, visited, depth + 1))
            {
                return true;
            }
        }

        const BindingFlags storage = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        // The WHOLE base chain, because a private field on a base class is not returned for a
        // derived type — and a base class is exactly where an imaging library keeps its handle
        // (OpenCvSharp puts Mat's behind DisposableCvObject).
        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
        {
            if (IsRuntimeOwned(current))
            {
                continue;
            }

            foreach (var field in current.GetFields(storage))
            {
                // `const` is a compile-time literal with no storage at run time.
                if (!field.IsLiteral && CarriesPixels(field.FieldType, allowNativeHandles, visited, depth + 1))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// The name of the first type in a declared type's closure that came from NEITHER the runtime
    /// NOR this product, or null when there is none.
    ///
    /// <para><b>This is the shape rule's blind spot, covered from the other side.</b>
    /// <c>NamedOnnxValue</c> is a <c>string</c> and an <c>object</c>: nothing about its structure
    /// says "inference", and no structural rule can ever see what its <c>object</c> holds. What CAN
    /// be seen is that it arrived from an inference package. So the camera seam may hold state typed
    /// from the runtime and from itself, and a field or property typed from any package — an imaging
    /// library, an inference runtime, a media framework — has to be argued for by editing this fact.
    /// It is the same discipline the ONE pixel boundary is already named under, and it is why an
    /// engine slice cannot land a retained tensor by picking a type whose fields happen to be
    /// opaque.</para>
    ///
    /// <para>Generic arguments and element types are followed; FIELDS are not, because a product
    /// type's internals are the product's business and the structural rule already walks them.</para>
    /// </summary>
    private static string? ForeignOrigin(Type type, Assembly product)
    {
        if (type.IsByRef || type.IsPointer || type.IsArray)
        {
            return ForeignOrigin(type.GetElementType()!, product);
        }

        if (!type.IsGenericParameter && type.Assembly != product && !IsRuntimeOwned(type))
        {
            return type.FullName ?? type.Name;
        }

        foreach (var argument in type.GetGenericArguments())
        {
            if (ForeignOrigin(argument, product) is { } foreign)
            {
                return foreign;
            }
        }

        return null;
    }

    private static string FixtureRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "ccp-camera-v4l2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void WriteNode(string root, string node, string name)
    {
        var directory = Path.Combine(root, node);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "name"), name + "\n");
    }

    private static (CameraParticipant Participant, string Directory, RecordingCameraDeviceSource Route) NewParticipant(
        bool engineAdmitted = false, CameraInventory? inventory = null) =>
        NewParticipant(engineAdmitted, inventory, out _);

    private static (CameraParticipant Participant, string Directory, RecordingCameraDeviceSource Route) NewParticipant(
        bool engineAdmitted, CameraInventory? inventory, out List<string> lines)
    {
        var directory = Path.Combine(Path.GetTempPath(), "ccp-camera-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var captured = new List<string>();
        lines = captured;
        var route = new RecordingCameraDeviceSource(
            inventory ?? CameraInventory.Named("recording route", []));
        var infra = new ParticipantInfrastructure(
            new OperationRegistry(), new UiDispatchBoundary(), new ListLogSink(captured));
        return (new CameraParticipant(infra, directory, route, engineAdmitted), directory, route);
    }

    /// <summary>A route that counts every ask. It is what turns "nothing was asked of any camera"
    /// into an assertion about a number instead of a claim about unexecuted code.</summary>
    private sealed class RecordingCameraDeviceSource(CameraInventory answer) : ICameraDeviceSource
    {
        public int Asks { get; private set; }

        public string Route => answer.Route;

        public CameraInventory Enumerate()
        {
            Asks++;
            return answer;
        }
    }

    private sealed class ListLogSink(List<string> lines) : ILogSink
    {
        public void Log(string message)
        {
            lock (lines)
            {
                lines.Add(message);
            }
        }
    }

    // =====================================================================================
    //  Decoys: the SHAPES an engine slice would bring, without taking the dependency to say so
    // =====================================================================================

    /// <summary>The <c>OpenCvSharp.Mat</c> shape. The buffer is a native pointer, and the pointer is
    /// a PRIVATE field on a BASE class, so a derived type's own field list never mentions it and
    /// neither does its public surface.
    ///
    /// <para>Read off the real package rather than remembered: OpenCvSharp4 4.9.0.20240103 — the
    /// version upstream references (<c>ConditioningControlPanel/ConditioningControlPanel.csproj:136</c>) —
    /// declares <c>OpenCvSharp.Mat</c> with NO fields of its own, <c>DisposableCvObject</c> with
    /// <c>IntPtr ptr</c>, and <c>DisposableObject</c> with a <see cref="GCHandle"/> and a second
    /// <see cref="IntPtr"/>. A rule that only looked at the declaring type would see nothing at
    /// all.</para></summary>
    private class DecoyNativeImage : IDisposable
    {
        private IntPtr _pixels;

        public bool IsAllocated => _pixels != IntPtr.Zero;

        public void Dispose() => _pixels = IntPtr.Zero;
    }

    /// <summary>What <c>private Mat _resizeBuffer;</c> looks like to reflection — the field upstream's
    /// pipeline really holds (<c>Services/Webcam/WebcamTrackingService.cs:3179,3183,3349,3356,3536</c>),
    /// and the one this guard passed in silence until 2026-08-25.</summary>
    private sealed class DecoyResizeBuffer : DecoyNativeImage
    {
    }

    /// <summary>The <c>DenseTensor&lt;float&gt;</c> shape: managed storage behind a memory handle,
    /// with no pointer and no array in sight.</summary>
    private sealed class DecoyTensor
    {
        private Memory<float> _values = Memory<float>.Empty;

        public int Length => _values.Length;
    }

    /// <summary>The shape ONNX Runtime's <c>OrtValue</c> keeps a native tensor behind. Its
    /// <see cref="IntPtr"/> lives in the runtime's own private state, where the field walk
    /// deliberately does not go — which is why a handle is a LEAF of the rule and not a walk.</summary>
    private sealed class DecoyHandle() : SafeHandle(IntPtr.Zero, ownsHandle: true)
    {
        public override bool IsInvalid => true;

        protected override bool ReleaseHandle() => true;
    }

    /// <summary>A type whose only state is a handle to somebody else's buffer.</summary>
    private sealed class DecoySession
    {
        private readonly DecoyHandle _value = new();

        public bool IsClosed => _value.IsClosed;
    }

    /// <summary>Holds no buffer and points at itself. The walk must TERMINATE on it and answer no —
    /// a recursive rule without a visited set dies here rather than reporting anything.</summary>
    private sealed class DecoyRing
    {
        public DecoyRing? Next { get; init; }
    }
}
